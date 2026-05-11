@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"get_scene_tree",
		"get_scene_file_content",
		"open_scene",
		"delete_scene",
		"add_scene_instance",
		"play_scene",
		"stop_scene",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"get_scene_tree":
			return _handle_get_scene_tree(params)
		"get_scene_file_content":
			return _handle_get_scene_file_content(params)
		"open_scene":
			return _handle_open_scene(params)
		"delete_scene":
			return _handle_delete_scene(params)
		"add_scene_instance":
			return _handle_add_scene_instance(params)
		"play_scene":
			return _handle_play_scene(params)
		"stop_scene":
			return _handle_stop_scene(params)
		_:
			return {"error": "Unknown scene method: " + method}

func _handle_get_scene_tree(params: Dictionary) -> Dictionary:
	var root = get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently open in the editor"}

	var max_depth = params.get("max_depth", 10)
	var include_properties = params.get("include_properties", false)
	var tree = _serialize_node(root, "/root", 0, max_depth, include_properties)
	return {"tree": tree}

func _serialize_node(node: Node, path: String, depth: int, max_depth: int, include_properties: bool) -> Dictionary:
	var result = {
		"name": node.name,
		"type": node.get_class(),
		"path": path,
		"visible": node.visible if node.has_method("set_visible") else true
	}

	if include_properties:
		result["properties"] = _get_node_exported_properties(node)

	if depth < max_depth:
		result["children"] = []
		for i in range(node.get_child_count()):
			var child = node.get_child(i)
			var child_path = path + "/" + child.name
			result.children.append(_serialize_node(child, child_path, depth + 1, max_depth, include_properties))
	return result

func _get_node_exported_properties(node: Node) -> Dictionary:
	var props = {}
	var prop_list = node.get_property_list()
	for prop in prop_list:
		if prop.usage & PROPERTY_USAGE_EDITOR:
			var pname = prop.name
			var value = node.get(pname)
			props[pname] = TypeHelper.variant_to_json(value)
	return props

# Forwarder kept so existing internal callers (and any reflection users) keep working.
func _variant_to_json(value: Variant) -> Variant:
	return TypeHelper.variant_to_json(value)

func _handle_get_scene_file_content(params: Dictionary) -> Dictionary:
	var scene_path: String = params.get("scene_path", "")
	if scene_path.is_empty():
		return {"error": "scene_path is required"}

	if not scene_path.begins_with("res://"):
		scene_path = "res://" + scene_path

	if not FileAccess.file_exists(scene_path):
		return {"error": "Scene file not found: " + scene_path}

	var ext: String = scene_path.get_extension().to_lower()
	if ext != "tscn" and ext != "scn":
		return {"error": "Not a scene file (expected .tscn or .scn): " + scene_path}

	var content = FileAccess.get_file_as_string(scene_path)
	return {"content": content, "path": scene_path}

func _handle_open_scene(params: Dictionary) -> Dictionary:
	var scene_path = params.get("scene_path", "")
	if scene_path.is_empty():
		return {"error": "scene_path is required"}

	if not scene_path.begins_with("res://"):
		scene_path = "res://" + scene_path

	if not FileAccess.file_exists(scene_path):
		return {"error": "Scene file not found: " + scene_path}

	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}

	ei.open_scene_from_path(scene_path)
	return {"success": true, "opened": scene_path}

func _handle_delete_scene(params: Dictionary) -> Dictionary:
	var scene_path = params.get("scene_path", "")
	if scene_path.is_empty():
		return {"error": "scene_path is required"}

	if not scene_path.begins_with("res://"):
		scene_path = "res://" + scene_path

	if not FileAccess.file_exists(scene_path):
		return {"error": "Scene file not found: " + scene_path}

	var absolute_path = ProjectSettings.globalize_path(scene_path)
	var err = DirAccess.remove_absolute(absolute_path)
	if err != OK:
		return {"error": "Failed to delete scene: " + str(err)}
	return {"success": true, "deleted": scene_path}

func _handle_add_scene_instance(params: Dictionary) -> Dictionary:
	var scene_path = params.get("scene_path", "")
	var parent_path = params.get("parent_node_path", "")
	var instance_name = params.get("instance_name", "")

	if scene_path.is_empty():
		return {"error": "scene_path is required"}
	if parent_path.is_empty():
		return {"error": "parent_node_path is required"}

	if not scene_path.begins_with("res://"):
		scene_path = "res://" + scene_path

	if not FileAccess.file_exists(scene_path):
		return {"error": "Scene file not found: " + scene_path}

	var packed = load(scene_path) as PackedScene
	if not packed:
		return {"error": "Failed to load PackedScene: " + scene_path}

	var root = get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently open"}

	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Parent node not found: " + parent_path}

	var instance = packed.instantiate()
	if not instance:
		return {"error": "Failed to instantiate scene"}

	if not instance_name.is_empty():
		instance.name = instance_name

	var tracked = UndoHelper.add_node(parent, instance, "Add scene instance")
	return {
		"success": true,
		"instance_path": str(parent.get_path_to(instance)),
		"undoable": tracked,
	}

# In-editor scene playback. Distinct from the legacy CLI `run_project`, which
# spawns a separate Godot process. play_scene drives the editor's own play
# button so the user keeps debugger visibility, breakpoints, etc.
func _handle_play_scene(params: Dictionary) -> Dictionary:
	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}
	var scene_path = String(params.get("scene_path", ""))
	if scene_path.is_empty():
		ei.play_main_scene()
		return {"success": true, "mode": "main_scene"}
	if not scene_path.begins_with("res://"):
		scene_path = "res://" + scene_path
	if not FileAccess.file_exists(scene_path):
		return {"error": "Scene file not found: " + scene_path}
	ei.play_custom_scene(scene_path)
	return {"success": true, "mode": "custom_scene", "scene_path": scene_path}

func _handle_stop_scene(params: Dictionary) -> Dictionary:
	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}
	ei.stop_playing_scene()
	return {"success": true}
