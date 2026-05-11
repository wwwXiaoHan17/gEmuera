extends SceneTree

func _initialize() -> void:
	var paths = [
		"res://addons/godot_mcp/runtime/mcp_runtime_agent.gd"
	]
	var failed = []
	for path in paths:
		var script = load(path)
		if not script:
			failed.append(path + " -> load failed")
			continue
		var instance = script.new()
		if not instance:
			failed.append(path + " -> new() failed")
	if failed.is_empty():
		print("ALL SCRIPTS COMPILE OK")
	else:
		for f in failed:
			print("COMPILE FAIL: " + f)
	quit(1 if not failed.is_empty() else 0)
