@tool
class_name McpDispatcher

var _handlers: Dictionary = {}
# Reverse lookup populated at registration time from each handler's get_methods().
# Single-source-of-truth for routing once all handlers declare get_methods().
var _method_to_handler: Dictionary = {}

func register_handler(category: String, handler: McpHandler) -> void:
	_handlers[category] = handler
	# Build the reverse method->handler index from the handler's declared methods.
	var declared = handler.get_methods()
	for method in declared:
		if _method_to_handler.has(method):
			push_warning("[MCP] Method '" + method + "' already registered, overwriting with handler '" + category + "'")
		_method_to_handler[method] = handler
	print("[MCP] Registered handler: ", category, " (", declared.size(), " methods)")

func dispatch(request: Dictionary) -> Dictionary:
	var id = request.get("id", "")
	var method = request.get("method", "")
	var params = request.get("params", {})

	if method.is_empty():
		return {"id": id, "error": {"code": -32600, "message": "Invalid request: missing method"}}

	# Internal heartbeat ping support (paired with TS-side keepalive)
	if method == "_ping":
		return {"id": id, "result": {"pong": Time.get_ticks_msec()}}

	print("[MCP] Dispatching: ", method)

	var handler = _find_handler_for_method(method)
	if not handler:
		return {"id": id, "error": {"code": -32601, "message": "Method not found: " + method}}

	var result = handler.handle(method, params)

	# Lift handler-level errors to JSON-RPC error frames so TS clients
	# see real failures instead of a {result: {error: ...}} envelope.
	# Carve-out: handlers can return {success: true, error: ...} for
	# partial-success batch operations — those stay in the result channel.
	if result is Dictionary and result.has("error") and not (result.get("success", false) == true):
		return {
			"id": id,
			"error": {
				"code": -32000,
				"message": str(result.get("error", "Unknown error")),
				"data": result,
			}
		}
	return {"id": id, "result": result}

# Primary lookup goes through the registry; falls back to legacy hardcoded
# routing during incremental migration so handlers without get_methods() still work.
func _find_handler_for_method(method: String) -> McpHandler:
	var handler = _method_to_handler.get(method)
	if handler:
		return handler
	return _legacy_find_handler_for_method(method)

func _legacy_find_handler_for_method(method: String) -> McpHandler:
	# Project handlers
	if method in ["get_filesystem_tree", "search_files", "get_project_settings", "set_project_settings"]:
		return _handlers.get("project")

	# Scene handlers
	if method in ["get_scene_tree", "get_scene_file_content", "open_scene", "delete_scene", "add_scene_instance"]:
		return _handlers.get("scene")

	# Node handlers
	if method in ["delete_node", "rename_node", "duplicate_node", "move_node", "get_node_properties", "update_property", "connect_signal", "disconnect_signal", "get_signals"]:
		return _handlers.get("node")

	# Script handlers
	if method in ["list_scripts", "read_script", "create_script", "edit_script", "attach_script", "get_open_scripts"]:
		return _handlers.get("script")

	# Editor handlers
	if method in ["get_editor_errors", "get_editor_screenshot", "get_game_screenshot", "execute_editor_script", "reload_plugin", "reload_project", "clear_output"]:
		return _handlers.get("editor")

	# Input handlers
	if method in ["simulate_key", "simulate_mouse_click", "simulate_mouse_move", "simulate_action"]:
		return _handlers.get("input")

	# Animation handlers
	if method in ["list_animations", "create_animation", "add_animation_track", "set_animation_keyframe", "get_animation_info", "remove_animation"]:
		return _handlers.get("animation")

	return null
