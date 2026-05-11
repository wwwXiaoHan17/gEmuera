extends Node

# Runtime agent — autoload that runs INSIDE the game process (not the editor).
# Opens a secondary WebSocket server on port 9081 (with fallback up to 9085)
# so the editor-side runtime_analysis_handler can introspect, modify, and
# record the running game.
#
# Intentionally NOT marked @tool: this class must not load in the editor, only
# in the game runtime spawned by play_scene / run_project.

const _DEFAULT_PORT := 9081
const _MAX_PORT := 9085

var _tcp: TCPServer
var _clients: Array[WebSocketPeer] = []
var _port: int = -1
var _running: bool = false

# Property monitoring state — populated by start_property_monitor.
var _monitor_specs: Array = []
var _monitor_timer: Timer
var _monitor_samples: Array = []
var _monitor_max_samples: int = 600

# Input recording state.
var _recording: bool = false
var _record_start_ms: int = 0
var _recorded_events: Array = []

func _ready() -> void:
	# Don't run in release builds — the user's published game shouldn't ship a
	# debug WebSocket server.
	if not OS.is_debug_build() or OS.get_name() == "Android":
		queue_free()
		return

	_tcp = TCPServer.new()
	for p in range(_DEFAULT_PORT, _MAX_PORT + 1):
		var err := _tcp.listen(p)
		if err == OK:
			_port = p
			_running = true
			print("[MCP-Runtime] Agent listening on port ", _port)
			break
		else:
			print("[MCP-Runtime] Port ", p, " unavailable, trying next...")
	if not _running:
		push_warning("[MCP-Runtime] Could not bind to any port in range %d-%d" % [_DEFAULT_PORT, _MAX_PORT])
		_tcp = null
		return

	set_process(true)
	# Hook input so start_recording can capture events.
	set_process_input(true)

func _exit_tree() -> void:
	_running = false
	for c in _clients:
		c.close()
	_clients.clear()
	if _tcp:
		_tcp.stop()
		_tcp = null

func _process(_delta: float) -> void:
	if not _running or not _tcp:
		return
	if _tcp.is_connection_available():
		var conn := _tcp.take_connection()
		if conn:
			var ws := WebSocketPeer.new()
			if ws.accept_stream(conn) == OK:
				_clients.append(ws)
				print("[MCP-Runtime] Client connected")
	for i in range(_clients.size() - 1, -1, -1):
		var ws: WebSocketPeer = _clients[i]
		ws.poll()
		var state := ws.get_ready_state()
		if state == WebSocketPeer.STATE_CLOSED:
			_clients.remove_at(i)
			continue
		if state == WebSocketPeer.STATE_OPEN:
			while ws.get_available_packet_count() > 0:
				var packet := ws.get_packet()
				_process_packet(ws, packet)

func _input(event: InputEvent) -> void:
	if _recording:
		_recorded_events.append({
			"time_ms": Time.get_ticks_msec() - _record_start_ms,
			"event": _serialize_input_event(event),
		})

# ---------------------------------------------------------------------------
# JSON-RPC dispatch
# ---------------------------------------------------------------------------

func _process_packet(ws: WebSocketPeer, packet: PackedByteArray) -> void:
	var text := packet.get_string_from_utf8()
	if text.is_empty():
		return
	var json := JSON.new()
	if json.parse(text) != OK:
		_send(ws, _err_response("", -32700, "Parse error"))
		return
	var req: Dictionary = json.get_data()
	var id: String = String(req.get("id", ""))
	var method: String = String(req.get("method", ""))
	var params: Dictionary = req.get("params", {}) if req.get("params", {}) is Dictionary else {}

	if method == "_ping":
		_send(ws, _ok_response(id, {"pong": Time.get_ticks_msec()}))
		return

	var result = await _dispatch(method, params)
	if result.has("error") and not result.get("success", false):
		_send(ws, _err_response(id, -32000, String(result["error"])))
	else:
		_send(ws, _ok_response(id, result))

func _send(ws: WebSocketPeer, text: String) -> void:
	if ws.get_ready_state() == WebSocketPeer.STATE_OPEN:
		ws.send_text(text)

func _ok_response(id: String, result: Dictionary) -> String:
	return JSON.stringify({"jsonrpc": "2.0", "id": id, "result": result})

func _err_response(id: String, code: int, message: String) -> String:
	return JSON.stringify({"jsonrpc": "2.0", "id": id, "error": {"code": code, "message": message}})

func _dispatch(method: String, params: Dictionary) -> Dictionary:
	match method:
		"get_game_scene_tree":
			return _get_scene_tree(params)
		"get_game_node_properties":
			return _get_node_properties(params)
		"set_game_node_properties":
			return _set_node_properties(params)
		"get_autoload":
			return _get_autoload(params)
		"find_nodes_by_script":
			return _find_nodes_by_script(params)
		"find_ui_elements":
			return _find_ui_elements(params)
		"wait_for_node":
			return _wait_for_node(params)
		"batch_get_properties":
			return _batch_get_properties(params)
		"call_game_method":
			return _call_game_method(params)
		"execute_game_script":
			return _execute_game_script(params)
		"capture_frames":
			return await _capture_frames(params)
		"monitor_properties":
			return _monitor_properties(params)
		"start_recording":
			return _start_recording(params)
		"stop_recording":
			return _stop_recording(params)
		"replay_recording":
			return _replay_recording(params)
		_:
			return {"error": "Unknown runtime method: " + method}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

func _resolve(path: String) -> Node:
	if path.is_empty():
		return null
	if path == "/root" or path == "root":
		return get_tree().root
	if path.begins_with("/root/"):
		return get_tree().root.get_node_or_null(path.substr(6))
	return get_tree().root.get_node_or_null(path)

func _serialize_node(node: Node, depth: int, max_depth: int) -> Dictionary:
	var entry := {
		"name": node.name,
		"class": node.get_class(),
		"path": str(node.get_path()),
	}
	if node.get_script():
		entry["script"] = node.get_script().resource_path
	if depth < max_depth:
		var children := []
		for c in node.get_children():
			children.append(_serialize_node(c, depth + 1, max_depth))
		entry["children"] = children
	else:
		entry["child_count"] = node.get_child_count()
	return entry

func _serialize_value(value: Variant) -> Variant:
	if value == null:
		return null
	var t := typeof(value)
	if t == TYPE_OBJECT:
		if value is Resource:
			return {"_class": value.get_class(), "resource_path": value.resource_path}
		if value is Node:
			return {"_class": value.get_class(), "path": str(value.get_path())}
		return {"_class": value.get_class()}
	if t == TYPE_VECTOR2 or t == TYPE_VECTOR2I:
		return {"x": value.x, "y": value.y}
	if t == TYPE_VECTOR3 or t == TYPE_VECTOR3I:
		return {"x": value.x, "y": value.y, "z": value.z}
	if t == TYPE_COLOR:
		return {"r": value.r, "g": value.g, "b": value.b, "a": value.a}
	if t == TYPE_NODE_PATH or t == TYPE_STRING_NAME:
		return str(value)
	if t == TYPE_ARRAY:
		var arr := []
		for v in value:
			arr.append(_serialize_value(v))
		return arr
	if t == TYPE_DICTIONARY:
		var d := {}
		for k in value.keys():
			d[str(k)] = _serialize_value(value[k])
		return d
	return value

# ---------------------------------------------------------------------------
# Tool implementations
# ---------------------------------------------------------------------------

func _get_scene_tree(params: Dictionary) -> Dictionary:
	var max_depth := int(params.get("max_depth", 10))
	var root := get_tree().current_scene if get_tree() else null
	if not root:
		root = get_tree().root if get_tree() else null
	if not root:
		return {"error": "No active scene"}
	return {"success": true, "tree": _serialize_node(root, 0, max_depth)}

func _get_node_properties(params: Dictionary) -> Dictionary:
	var node := _resolve(String(params.get("node_path", "")))
	if not node:
		return {"error": "Node not found"}
	var include_internal := bool(params.get("include_internal", false))
	var props := {}
	for prop in node.get_property_list():
		var usage = prop.usage
		if (usage & PROPERTY_USAGE_EDITOR) or (include_internal and (usage & PROPERTY_USAGE_STORAGE)):
			props[prop.name] = _serialize_value(node.get(prop.name))
	return {
		"success": true,
		"path": str(node.get_path()),
		"class": node.get_class(),
		"properties": props,
	}

func _set_node_properties(params: Dictionary) -> Dictionary:
	var node := _resolve(String(params.get("node_path", "")))
	if not node:
		return {"error": "Node not found"}
	var properties: Dictionary = params.get("properties", {})
	if not (properties is Dictionary):
		return {"error": "properties must be a Dictionary"}
	var applied := []
	for key in properties.keys():
		if key in node:
			# Best-effort type coercion for common cases.
			var current = node.get(key)
			var new_val = properties[key]
			if current is Vector2 and new_val is Dictionary and new_val.has("x"):
				new_val = Vector2(float(new_val.x), float(new_val.y))
			elif current is Vector3 and new_val is Dictionary and new_val.has("x"):
				new_val = Vector3(float(new_val.x), float(new_val.y), float(new_val.get("z", 0)))
			elif current is Color and new_val is Dictionary and new_val.has("r"):
				new_val = Color(float(new_val.r), float(new_val.g), float(new_val.b), float(new_val.get("a", 1.0)))
			node.set(key, new_val)
			applied.append(key)
	return {
		"success": true,
		"applied_properties": applied,
		"undoable": false,
	}

func _get_autoload(params: Dictionary) -> Dictionary:
	# Autoloads are children of /root, registered before the main scene loads.
	# We identify them by walking /root's direct children and excluding the
	# current scene.
	var root := get_tree().root if get_tree() else null
	if not root:
		return {"error": "No tree"}
	var current_scene := get_tree().current_scene
	var autoloads := []
	for c in root.get_children():
		if c == current_scene:
			continue
		if c == self:
			continue
		autoloads.append({
			"name": c.name,
			"class": c.get_class(),
			"path": str(c.get_path()),
			"script": c.get_script().resource_path if c.get_script() else "",
		})
	# Also include self for visibility.
	autoloads.append({
		"name": name,
		"class": get_class(),
		"path": str(get_path()),
		"script": get_script().resource_path if get_script() else "",
	})
	return {"success": true, "autoloads": autoloads}

func _find_nodes_by_script_recursive(node: Node, script_path: String, results: Array) -> void:
	var s = node.get_script()
	if s and s.resource_path == script_path:
		results.append({
			"path": str(node.get_path()),
			"class": node.get_class(),
			"name": node.name,
		})
	for c in node.get_children():
		_find_nodes_by_script_recursive(c, script_path, results)

func _find_nodes_by_script(params: Dictionary) -> Dictionary:
	var script_path := String(params.get("script_path", ""))
	if script_path.is_empty():
		return {"error": "script_path is required"}
	var root := get_tree().root if get_tree() else null
	if not root:
		return {"error": "No tree"}
	var results := []
	_find_nodes_by_script_recursive(root, script_path, results)
	return {"success": true, "matches": results}

func _find_ui_elements_recursive(node: Node, filter_class: String, filter_name: String,
		filter_text: String, results: Array) -> void:
	if node is Control:
		var matches := true
		if not filter_class.is_empty():
			matches = matches and node.is_class(filter_class)
		if not filter_name.is_empty():
			matches = matches and String(node.name).contains(filter_name)
		if not filter_text.is_empty() and "text" in node:
			matches = matches and String(node.get("text")).contains(filter_text)
		if matches:
			var entry := {
				"path": str(node.get_path()),
				"class": node.get_class(),
				"name": node.name,
				"visible": node.visible,
			}
			if "text" in node:
				entry["text"] = node.get("text")
			results.append(entry)
	for c in node.get_children():
		_find_ui_elements_recursive(c, filter_class, filter_name, filter_text, results)

func _find_ui_elements(params: Dictionary) -> Dictionary:
	var root := get_tree().root if get_tree() else null
	if not root:
		return {"error": "No tree"}
	var filter_class := String(params.get("class", ""))
	var filter_name := String(params.get("name", ""))
	var filter_text := String(params.get("text", ""))
	var results := []
	_find_ui_elements_recursive(root, filter_class, filter_name, filter_text, results)
	return {"success": true, "matches": results, "count": results.size()}

func _wait_for_node(params: Dictionary) -> Dictionary:
	var path := String(params.get("node_path", ""))
	if path.is_empty():
		return {"error": "node_path is required"}
	var timeout_ms := int(params.get("timeout_ms", 5000))
	var deadline := Time.get_ticks_msec() + timeout_ms
	while Time.get_ticks_msec() < deadline:
		var n := _resolve(path)
		if n:
			return {
				"success": true,
				"found": true,
				"path": str(n.get_path()),
				"class": n.get_class(),
			}
		OS.delay_msec(50)
	return {"success": true, "found": false, "timeout_ms": timeout_ms}

func _batch_get_properties(params: Dictionary) -> Dictionary:
	var requests: Array = params.get("requests", [])
	var results := []
	for req in requests:
		if not (req is Dictionary):
			results.append({"error": "Request not a Dictionary"})
			continue
		var node := _resolve(String(req.get("node_path", "")))
		if not node:
			results.append({"error": "Node not found", "node_path": req.get("node_path", "")})
			continue
		var prop := String(req.get("property", ""))
		if prop.is_empty():
			results.append({"error": "property required"})
			continue
		results.append({
			"node_path": str(node.get_path()),
			"property": prop,
			"value": _serialize_value(node.get(prop)) if prop in node else null,
			"exists": prop in node,
		})
	return {"success": true, "results": results}

func _call_game_method(params: Dictionary) -> Dictionary:
	var node := _resolve(String(params.get("node_path", "")))
	if not node:
		return {"error": "Node not found"}
	var method := String(params.get("method", ""))
	if method.is_empty():
		return {"error": "method is required"}
	if not node.has_method(method):
		return {"error": "Node has no method: " + method}
	var args: Array = params.get("args", [])
	var result = node.callv(method, args)
	return {"success": true, "result": _serialize_value(result)}

# Mirrors execute_editor_script's advisory guardrails — guard against
# filesystem and OS spawning unless explicitly opted in.
func _execute_game_script(params: Dictionary) -> Dictionary:
	var code := String(params.get("code", ""))
	if code.is_empty():
		return {"error": "code is required"}
	var allow_fs := bool(params.get("allow_filesystem_writes", false))
	var allow_os := bool(params.get("allow_os_execute", false))
	if not allow_fs:
		for pat in ["DirAccess.remove", "DirAccess.rename", "FileAccess.WRITE", "ProjectSettings.save"]:
			if code.find(pat) != -1:
				return {"error": "Guarded: %s" % pat, "error_code": "guarded"}
	if not allow_os:
		for pat in ["OS.execute", "OS.shell_open", "OS.create_process", "OS.kill"]:
			if code.find(pat) != -1:
				return {"error": "Guarded: %s" % pat, "error_code": "guarded"}

	var script := GDScript.new()
	script.source_code = code
	if script.reload() != OK:
		return {"error": "Script compilation failed"}
	var instance = script.new()
	if not instance:
		return {"error": "Script instantiation failed"}
	if instance.has_method("run"):
		var r = instance.run()
		return {"success": true, "result": _serialize_value(r)}
	return {"success": true, "message": "Script executed (no run() method)"}

func _capture_frames(params: Dictionary) -> Dictionary:
	var count := int(params.get("count", 1))
	count = clamp(count, 1, 10)
	var frames := []
	for i in range(count):
		await RenderingServer.frame_post_draw
		var img := get_viewport().get_texture().get_image()
		if not img:
			continue
		var buffer := img.save_png_to_buffer()
		frames.append({
			"index": i,
			"width": img.get_width(),
			"height": img.get_height(),
			"image_base64": Marshalls.raw_to_base64(buffer),
		})
	return {"success": true, "frames": frames, "count": frames.size()}

func _monitor_properties(params: Dictionary) -> Dictionary:
	var action := String(params.get("action", "snapshot"))
	match action:
		"start":
			var specs: Array = params.get("specs", [])
			var sample_ms := int(params.get("sample_ms", 100))
			_monitor_specs = specs
			_monitor_samples.clear()
			_monitor_max_samples = int(params.get("max_samples", 600))
			if not _monitor_timer:
				_monitor_timer = Timer.new()
				add_child(_monitor_timer)
				_monitor_timer.timeout.connect(_on_monitor_tick)
			_monitor_timer.wait_time = sample_ms / 1000.0
			_monitor_timer.start()
			return {"success": true, "monitoring": true, "spec_count": specs.size()}
		"stop":
			if _monitor_timer:
				_monitor_timer.stop()
			return {"success": true, "monitoring": false, "samples_collected": _monitor_samples.size()}
		"fetch":
			return {"success": true, "samples": _monitor_samples}
		"snapshot":
			var specs2: Array = params.get("specs", [])
			var snap := []
			for s in specs2:
				if not (s is Dictionary):
					continue
				var n := _resolve(String(s.get("node_path", "")))
				if not n:
					snap.append({"error": "Node not found", "spec": s})
					continue
				var p := String(s.get("property", ""))
				snap.append({
					"node_path": str(n.get_path()),
					"property": p,
					"value": _serialize_value(n.get(p)) if p in n else null,
				})
			return {"success": true, "snapshot": snap}
	return {"error": "Unknown action: " + action}

func _on_monitor_tick() -> void:
	if _monitor_samples.size() >= _monitor_max_samples:
		_monitor_timer.stop()
		return
	var sample := {"time_ms": Time.get_ticks_msec(), "values": []}
	for s in _monitor_specs:
		if not (s is Dictionary):
			continue
		var n := _resolve(String(s.get("node_path", "")))
		if not n:
			sample["values"].append({"error": "Node not found"})
			continue
		var p := String(s.get("property", ""))
		sample["values"].append({
			"node_path": s.get("node_path", ""),
			"property": p,
			"value": _serialize_value(n.get(p)) if p in n else null,
		})
	_monitor_samples.append(sample)

func _start_recording(_params: Dictionary) -> Dictionary:
	_recording = true
	_record_start_ms = Time.get_ticks_msec()
	_recorded_events.clear()
	return {"success": true, "recording": true, "started_ms": _record_start_ms}

func _stop_recording(_params: Dictionary) -> Dictionary:
	_recording = false
	var duration := Time.get_ticks_msec() - _record_start_ms
	return {
		"success": true,
		"recording": false,
		"event_count": _recorded_events.size(),
		"duration_ms": duration,
		"events": _recorded_events.duplicate(),
	}

func _replay_recording(params: Dictionary) -> Dictionary:
	var events: Array = params.get("events", _recorded_events)
	if events.is_empty():
		return {"error": "No events to replay"}
	var speed := float(params.get("speed", 1.0))
	if speed <= 0:
		speed = 1.0
	var last_time := 0
	for ev in events:
		if not (ev is Dictionary):
			continue
		var t := int(ev.get("time_ms", 0))
		var delay := int(float(t - last_time) / speed)
		if delay > 0:
			OS.delay_msec(delay)
		last_time = t
		var input_event := _deserialize_input_event(ev.get("event", {}))
		if input_event:
			Input.parse_input_event(input_event)
	return {"success": true, "replayed": events.size(), "speed": speed}

# Input event serialization for recording / replay. Covers key, mouse button,
# mouse motion, and InputAction. Other event types (joypad, etc.) are
# represented by their class name and skipped on replay.

func _serialize_input_event(event: InputEvent) -> Dictionary:
	if event is InputEventKey:
		return {
			"type": "key",
			"keycode": event.keycode,
			"pressed": event.pressed,
			"shift": event.shift_pressed,
			"ctrl": event.ctrl_pressed,
			"alt": event.alt_pressed,
			"meta": event.meta_pressed,
		}
	if event is InputEventMouseButton:
		return {
			"type": "mouse_button",
			"button": event.button_index,
			"pressed": event.pressed,
			"x": event.position.x,
			"y": event.position.y,
			"double_click": event.double_click,
		}
	if event is InputEventMouseMotion:
		return {
			"type": "mouse_motion",
			"x": event.position.x,
			"y": event.position.y,
			"rel_x": event.relative.x,
			"rel_y": event.relative.y,
		}
	if event is InputEventAction:
		return {
			"type": "action",
			"action": event.action,
			"pressed": event.pressed,
			"strength": event.strength,
		}
	return {"type": "unsupported", "class": event.get_class()}

func _deserialize_input_event(d: Dictionary) -> InputEvent:
	match String(d.get("type", "")):
		"key":
			var e := InputEventKey.new()
			e.keycode = int(d.get("keycode", 0))
			e.pressed = bool(d.get("pressed", true))
			e.shift_pressed = bool(d.get("shift", false))
			e.ctrl_pressed = bool(d.get("ctrl", false))
			e.alt_pressed = bool(d.get("alt", false))
			e.meta_pressed = bool(d.get("meta", false))
			return e
		"mouse_button":
			var e := InputEventMouseButton.new()
			e.button_index = int(d.get("button", 1))
			e.pressed = bool(d.get("pressed", true))
			e.position = Vector2(float(d.get("x", 0)), float(d.get("y", 0)))
			e.global_position = e.position
			e.double_click = bool(d.get("double_click", false))
			return e
		"mouse_motion":
			var e := InputEventMouseMotion.new()
			e.position = Vector2(float(d.get("x", 0)), float(d.get("y", 0)))
			e.global_position = e.position
			e.relative = Vector2(float(d.get("rel_x", 0)), float(d.get("rel_y", 0)))
			return e
		"action":
			var e := InputEventAction.new()
			e.action = String(d.get("action", ""))
			e.pressed = bool(d.get("pressed", true))
			e.strength = float(d.get("strength", 1.0))
			return e
	return null
