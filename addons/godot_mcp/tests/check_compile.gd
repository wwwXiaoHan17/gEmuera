extends SceneTree

func _initialize() -> void:
	var handlers = [
		"res://addons/godot_mcp/handlers/base_handler.gd",
		"res://addons/godot_mcp/dispatcher.gd",
		"res://addons/godot_mcp/handlers/node_handler.gd",
		"res://addons/godot_mcp/handlers/scene_handler.gd",
		"res://addons/godot_mcp/handlers/project_handler.gd",
		"res://addons/godot_mcp/handlers/script_handler.gd",
		"res://addons/godot_mcp/handlers/editor_handler.gd",
		"res://addons/godot_mcp/handlers/input_handler.gd",
		"res://addons/godot_mcp/handlers/animation_handler.gd",
		"res://addons/godot_mcp/handlers/undo_helper.gd",
		"res://addons/godot_mcp/handlers/type_helper.gd",
		"res://addons/godot_mcp/handlers/resource_handler.gd",
		"res://addons/godot_mcp/handlers/tilemap_handler.gd",
		"res://addons/godot_mcp/handlers/theme_handler.gd",
		"res://addons/godot_mcp/handlers/shader_handler.gd",
		"res://addons/godot_mcp/handlers/physics_handler.gd",
		"res://addons/godot_mcp/handlers/scene_3d_handler.gd",
		"res://addons/godot_mcp/handlers/animation_tree_handler.gd",
		"res://addons/godot_mcp/handlers/batch_handler.gd",
		"res://addons/godot_mcp/handlers/particles_handler.gd",
		"res://addons/godot_mcp/handlers/audio_handler.gd",
		"res://addons/godot_mcp/handlers/profiling_handler.gd",
		"res://addons/godot_mcp/handlers/export_handler.gd",
		"res://addons/godot_mcp/handlers/code_analysis_handler.gd",
		"res://addons/godot_mcp/handlers/navigation_handler.gd",
		"res://addons/godot_mcp/handlers/testing_handler.gd",
		"res://addons/godot_mcp/handlers/runtime_analysis_handler.gd",
	]
	var failed = []
	for path in handlers:
		var script = load(path)
		if not script:
			failed.append(path + " -> load failed")
			continue
		var instance = script.new()
		if not instance:
			failed.append(path + " -> new() failed")
	if failed.is_empty():
		print("ALL HANDLERS COMPILE OK")
	else:
		for f in failed:
			print("COMPILE FAIL: " + f)
	quit(1 if not failed.is_empty() else 0)
