@tool
extends EditorScript

# Launcher wrapper that runs regression.gd and then quits the editor.
func _run() -> void:
	var script = load("res://addons/godot_mcp/tests/regression.gd")
	if not script:
		push_error("Failed to load regression.gd")
		get_editor_interface().get_tree().quit(1)
		return
	var instance = script.new()
	instance._run()
	# Give Output panel time to flush, then quit.
	await get_editor_interface().get_tree().create_timer(0.5).timeout
	get_editor_interface().get_tree().quit(0 if instance._fail == 0 else 1)
