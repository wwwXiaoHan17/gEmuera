extends SceneTree

func _initialize() -> void:
	var paths = [
		"res://addons/godot_mcp/handlers/scene_handler.gd",
		"res://addons/godot_mcp/handlers/tilemap_handler.gd",
		"res://addons/godot_mcp/handlers/theme_handler.gd",
		"res://addons/godot_mcp/handlers/export_handler.gd",
	]
	for p in paths:
		var s = load(p)
		if not s:
			print("LOAD FAIL: " + p)
			continue
		var inst = s.new()
		if not inst:
			print("NEW FAIL: " + p)
		else:
			print("OK: " + p + " -> " + inst.get_class())
	quit(0)
