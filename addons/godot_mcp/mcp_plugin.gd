@tool
extends EditorPlugin

const _RUNTIME_AGENT_NAME := "McpRuntimeAgent"
const _RUNTIME_AGENT_PATH := "res://addons/godot_mcp/runtime/mcp_runtime_agent.gd"

var _server: McpServer
var _dispatcher: McpDispatcher

func _enter_tree() -> void:
	print("[MCP] Plugin entering tree")

	# Register the runtime agent autoload so it's loaded into the game
	# process when the user runs the project. The autoload removes itself
	# in release builds via OS.is_debug_build() check inside _ready.
	if not ProjectSettings.has_setting("autoload/" + _RUNTIME_AGENT_NAME):
		add_autoload_singleton(_RUNTIME_AGENT_NAME, _RUNTIME_AGENT_PATH)
		# project.godot must be saved for the autoload to persist; warn the
		# user once. They'll need to save the project (Ctrl+S) for runtime
		# bridge to work in subsequent runs.
		push_warning("[MCP] Registered McpRuntimeAgent autoload. Save the project (Ctrl+S) to persist for runtime bridge tools.")

	# Create and configure server
	_server = McpServer.new()
	_server.client_connected.connect(_on_client_connected)
	_server.client_disconnected.connect(_on_client_disconnected)
	_server.message_received.connect(_on_message_received)

	# Create dispatcher and register handlers
	_dispatcher = McpDispatcher.new()
	_register_handlers()

	# Start server
	var started = _server.start()
	if started:
		# Write port file so the TS client can discover the actual port
		# (the server may have fallen back from 9080 to 9081+ if the default
		# port was in use).
		var port_file := FileAccess.open("res://.godot_mcp_port", FileAccess.WRITE)
		if port_file:
			port_file.store_string(str(_server.get_port()))
			port_file.close()
		# Add status indicator to bottom panel
		var label = Label.new()
		label.text = "MCP: " + str(_server.get_port())
		label.name = "McpStatus"
		add_control_to_bottom_panel(label, "MCP")
	else:
		push_error("[MCP] Failed to start server")

func _exit_tree() -> void:
	print("[MCP] Plugin exiting tree")
	if _server:
		_server.stop()
		_server = null
	_dispatcher = null

	# Remove the runtime agent autoload — symmetric with _enter_tree.
	if ProjectSettings.has_setting("autoload/" + _RUNTIME_AGENT_NAME):
		remove_autoload_singleton(_RUNTIME_AGENT_NAME)

	# Remove port file so the TS client knows the editor is gone.
	if FileAccess.file_exists("res://.godot_mcp_port"):
		DirAccess.remove_absolute(ProjectSettings.globalize_path("res://.godot_mcp_port"))

	# Remove status indicator
	var bottom_panel = get_editor_interface().get_base_control().find_child("McpStatus", true, false)
	if bottom_panel:
		remove_control_from_bottom_panel(bottom_panel)
		bottom_panel.queue_free()

func _process(delta: float) -> void:
	if _server:
		_server.poll()

func _register_handlers() -> void:
	# Phase 1 handlers
	_dispatcher.register_handler("project", preload("res://addons/godot_mcp/handlers/project_handler.gd").new())
	_dispatcher.register_handler("scene", preload("res://addons/godot_mcp/handlers/scene_handler.gd").new())
	_dispatcher.register_handler("node", preload("res://addons/godot_mcp/handlers/node_handler.gd").new())

	# Phase 2 handlers
	_dispatcher.register_handler("script", preload("res://addons/godot_mcp/handlers/script_handler.gd").new())
	_dispatcher.register_handler("editor", preload("res://addons/godot_mcp/handlers/editor_handler.gd").new())

	# Phase 3 handlers
	_dispatcher.register_handler("input", preload("res://addons/godot_mcp/handlers/input_handler.gd").new())
	_dispatcher.register_handler("animation", preload("res://addons/godot_mcp/handlers/animation_handler.gd").new())

	# Phase 4 handlers (B-1: data resources & UI)
	_dispatcher.register_handler("resource", preload("res://addons/godot_mcp/handlers/resource_handler.gd").new())
	_dispatcher.register_handler("tilemap", preload("res://addons/godot_mcp/handlers/tilemap_handler.gd").new())
	_dispatcher.register_handler("theme", preload("res://addons/godot_mcp/handlers/theme_handler.gd").new())
	_dispatcher.register_handler("shader", preload("res://addons/godot_mcp/handlers/shader_handler.gd").new())

	# Phase 4 handlers (B-2: physics, 3D, animation tree, batch ops)
	_dispatcher.register_handler("physics", preload("res://addons/godot_mcp/handlers/physics_handler.gd").new())
	_dispatcher.register_handler("scene_3d", preload("res://addons/godot_mcp/handlers/scene_3d_handler.gd").new())
	_dispatcher.register_handler("animation_tree", preload("res://addons/godot_mcp/handlers/animation_tree_handler.gd").new())
	_dispatcher.register_handler("batch", preload("res://addons/godot_mcp/handlers/batch_handler.gd").new())

	# Phase D handlers (closing the gap to ~169 tools / 23 categories)
	_dispatcher.register_handler("particles", preload("res://addons/godot_mcp/handlers/particles_handler.gd").new())
	_dispatcher.register_handler("audio", preload("res://addons/godot_mcp/handlers/audio_handler.gd").new())
	_dispatcher.register_handler("profiling", preload("res://addons/godot_mcp/handlers/profiling_handler.gd").new())
	_dispatcher.register_handler("export", preload("res://addons/godot_mcp/handlers/export_handler.gd").new())
	_dispatcher.register_handler("code_analysis", preload("res://addons/godot_mcp/handlers/code_analysis_handler.gd").new())
	_dispatcher.register_handler("navigation", preload("res://addons/godot_mcp/handlers/navigation_handler.gd").new())
	_dispatcher.register_handler("testing", preload("res://addons/godot_mcp/handlers/testing_handler.gd").new())
	_dispatcher.register_handler("runtime_analysis", preload("res://addons/godot_mcp/handlers/runtime_analysis_handler.gd").new())

func _on_client_connected() -> void:
	print("[MCP] Client connected event")

func _on_client_disconnected() -> void:
	print("[MCP] Client disconnected event")

func _on_message_received(request: Dictionary) -> void:
	print("[MCP] Message received: ", request.get("method", "unknown"))
	var response = _dispatcher.dispatch(request)
	if response.has("id"):
		if response.has("error"):
			_server.send_error(response.id, response.error.code, response.error.message)
		else:
			_server.send_result(response.id, response.result)
