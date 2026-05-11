@tool
extends McpHandler

# Editor-side proxy for the runtime agent (mcp_runtime_agent.gd) running inside
# the game process. Each tool call here sends a JSON-RPC request over a
# secondary WebSocket (port 9081-9085) and blocks until the agent responds or
# the timeout fires. If the game isn't running, the connect attempt fails fast
# and we return a clear "Game not running. Start it with run_project or
# play_scene first." error so the LLM knows what to do.

const _DEFAULT_PORT := 9081
const _MAX_PORT := 9085
const _DEFAULT_TIMEOUT_MS := 5000
const _CONNECT_TIMEOUT_MS := 2000
const _LARGE_TIMEOUT_MS := 30000  # for capture_frames / wait_for_node etc.

var _client: WebSocketPeer
var _connected_port: int = -1
var _request_seq: int = 0

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"get_game_scene_tree",
		"get_game_node_properties",
		"set_game_node_properties",
		"get_autoload",
		"find_nodes_by_script",
		"find_ui_elements",
		"wait_for_node",
		"batch_get_properties",
		"call_game_method",
		"execute_game_script",
		"capture_frames",
		"monitor_properties",
		"start_recording",
		"stop_recording",
		"replay_recording",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	# Methods that may legitimately take longer than 5 seconds.
	var timeout_ms = _DEFAULT_TIMEOUT_MS
	match method:
		"wait_for_node":
			# Caller sets timeout_ms — give the agent room plus a margin.
			timeout_ms = int(params.get("timeout_ms", 5000)) + 2000
		"capture_frames":
			timeout_ms = _LARGE_TIMEOUT_MS
		"replay_recording":
			# Replay sleeps inline at the agent; size the budget conservatively.
			timeout_ms = _LARGE_TIMEOUT_MS
		"monitor_properties":
			# fetch/snapshot/start/stop are quick; bump only if start with sample_ms.
			timeout_ms = _DEFAULT_TIMEOUT_MS

	if not get_methods().has(method):
		return {"error": "Unknown runtime_analysis method: " + method}

	var ensure := _ensure_connected()
	if ensure.has("error"):
		return ensure

	return _rpc_call(method, params, timeout_ms)

# --- Connection management -------------------------------------------------

func _ensure_connected() -> Dictionary:
	if _client and _client.get_ready_state() == WebSocketPeer.STATE_OPEN:
		return {}
	# Stale or never opened — try ports 9081..9085 in order.
	for p in range(_DEFAULT_PORT, _MAX_PORT + 1):
		if _try_connect_to_port(p):
			_connected_port = p
			return {}
	_client = null
	_connected_port = -1
	return {
		"error": "Game not running. Start it with run_project or play_scene first, then retry. (No runtime agent listening on ports %d-%d)" % [_DEFAULT_PORT, _MAX_PORT],
		"error_code": "agent_unreachable",
	}

func _try_connect_to_port(port: int) -> bool:
	var ws := WebSocketPeer.new()
	var url := "ws://127.0.0.1:%d" % port
	if ws.connect_to_url(url) != OK:
		return false
	var deadline := Time.get_ticks_msec() + _CONNECT_TIMEOUT_MS
	while Time.get_ticks_msec() < deadline:
		ws.poll()
		var state := ws.get_ready_state()
		if state == WebSocketPeer.STATE_OPEN:
			_client = ws
			return true
		if state == WebSocketPeer.STATE_CLOSED or state == WebSocketPeer.STATE_CLOSING:
			return false
		OS.delay_msec(20)
	# Timed out — give up on this port.
	ws.close()
	return false

# --- JSON-RPC plumbing -----------------------------------------------------

func _next_id() -> String:
	_request_seq += 1
	return "rt-%d" % _request_seq

func _rpc_call(method: String, params: Dictionary, timeout_ms: int) -> Dictionary:
	var id := _next_id()
	var payload := {
		"jsonrpc": "2.0",
		"id": id,
		"method": method,
		"params": params,
	}
	var text := JSON.stringify(payload)
	var send_err := _client.send_text(text)
	if send_err != OK:
		# Connection probably dropped between connect and send — tear down so
		# the next call retries from scratch.
		_client = null
		return {"error": "Failed to send request (err %d). Agent connection lost." % send_err}

	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		_client.poll()
		var state := _client.get_ready_state()
		if state == WebSocketPeer.STATE_CLOSED:
			_client = null
			return {"error": "Agent closed connection mid-request"}
		while _client.get_available_packet_count() > 0:
			var raw := _client.get_packet()
			var resp_text := raw.get_string_from_utf8()
			if resp_text.is_empty():
				continue
			var json := JSON.new()
			if json.parse(resp_text) != OK:
				continue
			var resp: Dictionary = json.get_data()
			if String(resp.get("id", "")) != id:
				# Stale response from earlier or unrelated server-push — skip.
				continue
			if resp.has("error"):
				var e: Dictionary = resp["error"]
				return {
					"error": String(e.get("message", "Unknown agent error")),
					"error_code": "agent_error",
					"agent_code": int(e.get("code", -32000)),
				}
			if resp.has("result"):
				return resp["result"] if resp["result"] is Dictionary else {"success": true, "result": resp["result"]}
			# Malformed — treat as success with raw payload for diagnostics.
			return {"success": true, "raw": resp}
		OS.delay_msec(15)
	return {
		"error": "Agent request timed out after %dms" % timeout_ms,
		"error_code": "timeout",
		"method": method,
	}
