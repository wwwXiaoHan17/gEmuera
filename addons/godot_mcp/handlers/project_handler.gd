@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"get_filesystem_tree",
		"search_files",
		"get_project_settings",
		"set_project_settings",
		"uid_to_project_path",
		"project_path_to_uid",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"get_filesystem_tree":
			return _handle_get_filesystem_tree(params)
		"search_files":
			return _handle_search_files(params)
		"get_project_settings":
			return _handle_get_project_settings(params)
		"set_project_settings":
			return _handle_set_project_settings(params)
		"uid_to_project_path":
			return _handle_uid_to_path(params)
		"project_path_to_uid":
			return _handle_path_to_uid(params)
		_:
			return {"error": "Unknown project method: " + method}

func _handle_get_filesystem_tree(params: Dictionary) -> Dictionary:
	var root_path = params.get("path", "res://")
	var max_depth = params.get("max_depth", 10)
	var tree = _scan_directory(root_path, 0, max_depth)
	return {"tree": tree}

func _scan_directory(dir_path: String, depth: int, max_depth: int) -> Dictionary:
	var result = {
		"name": dir_path.get_file(),
		"path": dir_path,
		"type": "directory",
		"children": []
	}
	if depth >= max_depth:
		return result

	var dir = DirAccess.open(dir_path)
	if not dir:
		return result

	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if not file_name.begins_with("."):
			var full_path = dir_path.path_join(file_name)
			if dir.current_is_dir():
				result.children.append(_scan_directory(full_path, depth + 1, max_depth))
			else:
				result.children.append({
					"name": file_name,
					"path": full_path,
					"type": "file",
					"size": FileAccess.get_file_as_bytes(full_path).size()
				})
		file_name = dir.get_next()
	dir.list_dir_end()
	return result

func _handle_search_files(params: Dictionary) -> Dictionary:
	var query = params.get("query", "")
	var glob = params.get("glob", "")
	var directory = params.get("directory", "res://")
	var results = []
	_search_recursive(directory, query, glob, results)
	return {"results": results}

func _search_recursive(dir_path: String, query: String, glob: String, results: Array) -> void:
	var dir = DirAccess.open(dir_path)
	if not dir:
		return
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if file_name.begins_with("."):
			file_name = dir.get_next()
			continue
		var full_path = dir_path.path_join(file_name)
		if dir.current_is_dir():
			_search_recursive(full_path, query, glob, results)
		else:
			var matches = true
			if not query.is_empty():
				matches = matches and file_name.to_lower().contains(query.to_lower())
			if not glob.is_empty():
				matches = matches and file_name.match(glob)
			if matches:
				results.append(full_path)
		file_name = dir.get_next()
	dir.list_dir_end()

func _handle_get_project_settings(params: Dictionary) -> Dictionary:
	var keys = params.get("keys", [])
	if keys.is_empty():
		return {"settings": {}}

	var result = {}
	for key in keys:
		if ProjectSettings.has_setting(key):
			var value = ProjectSettings.get_setting(key)
			result[key] = _variant_to_json(value)
		else:
			result[key] = null
	return {"settings": result}

func _handle_set_project_settings(params: Dictionary) -> Dictionary:
	var settings = params.get("settings", {})
	for key in settings.keys():
		var value = settings[key]
		ProjectSettings.set_setting(key, value)
	ProjectSettings.save()
	return {"success": true, "saved": settings.keys().size()}

func _variant_to_json(value: Variant) -> Variant:
	return TypeHelper.variant_to_json(value)

# ResourceUID conversions (Godot 4.4+). Either direction can fail when the UID
# isn't registered yet (e.g. just-created resources before a save) or when the
# path doesn't actually exist, so we surface the failure with a structured
# response rather than a hard error.

func _handle_uid_to_path(params: Dictionary) -> Dictionary:
	var uid = String(params.get("uid", ""))
	if uid.is_empty():
		return {"error": "uid is required (e.g. 'uid://abc123')"}
	if not uid.begins_with("uid://"):
		return {"error": "uid must start with 'uid://'"}
	var id := ResourceUID.text_to_id(uid)
	if not ResourceUID.has_id(id):
		return {
			"error": "UID not found in registry",
			"uid": uid,
		}
	var path := ResourceUID.get_id_path(id)
	return {
		"success": true,
		"uid": uid,
		"path": path,
		"exists": FileAccess.file_exists(path),
	}

func _handle_path_to_uid(params: Dictionary) -> Dictionary:
	var path = String(params.get("path", ""))
	if path.is_empty():
		return {"error": "path is required (e.g. 'res://scenes/main.tscn')"}
	if not path.begins_with("res://"):
		path = "res://" + path
	if not FileAccess.file_exists(path):
		return {
			"error": "File does not exist",
			"path": path,
		}
	var id := ResourceLoader.get_resource_uid(path)
	if id == ResourceUID.INVALID_ID:
		return {
			"error": "Resource has no UID assigned",
			"path": path,
			"hint": "Run update_project_uids or save the resource to assign a UID.",
		}
	return {
		"success": true,
		"path": path,
		"uid": ResourceUID.id_to_text(id),
	}
