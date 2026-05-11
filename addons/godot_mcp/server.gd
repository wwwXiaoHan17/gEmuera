@tool
class_name McpServer
extends RefCounted

signal client_connected
signal client_disconnected
signal message_received(data: Dictionary)

var _tcp_server: TCPServer
var _clients: Array[WebSocketPeer] = []
var _port: int = 9080
var _max_port: int = 9090
var _running: bool = false

func start() -> bool:
	_tcp_server = TCPServer.new()
	for p in range(_port, _max_port + 1):
		var err = _tcp_server.listen(p)
		if err == OK:
			_port = p
			_running = true
			print("[MCP] Server listening on port ", _port)
			return true
		else:
			print("[MCP] Port ", p, " unavailable, trying next...")
	push_error("[MCP] Could not bind to any port in range " + str(_port) + "-" + str(_max_port))
	return false

func stop() -> void:
	_running = false
	for client in _clients:
		client.close()
	_clients.clear()
	if _tcp_server:
		_tcp_server.stop()
		_tcp_server = null
	print("[MCP] Server stopped")

func poll() -> void:
	if not _running or not _tcp_server:
		return

	if _tcp_server.is_connection_available():
		var conn = _tcp_server.take_connection()
		if conn:
			var ws = WebSocketPeer.new()
			var err = ws.accept_stream(conn)
			if err == OK:
				_clients.append(ws)
				print("[MCP] Client connected")
				client_connected.emit()
			else:
				print("[MCP] Failed to accept WebSocket connection")

	for i in range(_clients.size() - 1, -1, -1):
		var client = _clients[i]
		client.poll()
		var state = client.get_ready_state()

		if state == WebSocketPeer.STATE_CLOSED:
			print("[MCP] Client disconnected")
			client.close()
			_clients.remove_at(i)
			client_disconnected.emit()
			continue

		if state == WebSocketPeer.STATE_OPEN:
			while client.get_available_packet_count() > 0:
				var packet = client.get_packet()
				if packet.size() > 0:
					var request = McpProtocol.parse_request(packet)
					if request.has("error"):
						var err_id = request.get("id", "")
						send_error(err_id, -32700, request.error.message)
					else:
						message_received.emit(request)

func send_result(id: String, result: Dictionary) -> void:
	var text = McpProtocol.build_result(id, result)
	_send_text(text)

func send_error(id: String, code: int, message: String) -> void:
	var text = McpProtocol.build_error(id, code, message)
	_send_text(text)

func send_notification(method: String, params: Dictionary) -> void:
	var text = McpProtocol.build_notification(method, params)
	_send_text(text)

func _send_text(text: String) -> void:
	for client in _clients:
		if client.get_ready_state() == WebSocketPeer.STATE_OPEN:
			client.send_text(text)

func is_running() -> bool:
	return _running

func get_port() -> int:
	return _port

func has_clients() -> bool:
	return _clients.size() > 0
