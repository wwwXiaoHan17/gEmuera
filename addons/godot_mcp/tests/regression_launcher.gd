extends SceneTree

func _initialize() -> void:
	print("========== MCP Regression Tests ==========")
	var script = load("res://addons/godot_mcp/tests/regression.gd")
	if not script:
		push_error("Failed to load regression.gd")
		quit(1)
		return
	var instance = script.new()
	instance._run()
	await create_timer(0.5).timeout
	quit(0 if instance._fail == 0 else 1)
