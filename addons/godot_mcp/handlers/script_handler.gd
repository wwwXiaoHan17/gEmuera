@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"list_scripts",
		"read_script",
		"create_script",
		"edit_script",
		"attach_script",
		"get_open_scripts",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"list_scripts":
			return _handle_list_scripts(params)
		"read_script":
			return _handle_read_script(params)
		"create_script":
			return _handle_create_script(params)
		"edit_script":
			return _handle_edit_script(params)
		"attach_script":
			return _handle_attach_script(params)
		"get_open_scripts":
			return _handle_get_open_scripts(params)
		_:
			return {"error": "Unknown script method: " + method}

func _handle_list_scripts(params: Dictionary) -> Dictionary:
	var directory = params.get("directory", "res://")
	var recursive = params.get("recursive", true)
	var results = []
	_scan_scripts(directory, recursive, results)
	return {"scripts": results}

func _scan_scripts(dir_path: String, recursive: bool, results: Array) -> void:
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
			if recursive:
				_scan_scripts(full_path, recursive, results)
		else:
			var ext = file_name.get_extension().to_lower()
			if ext == "gd" or ext == "cs":
				results.append(full_path)
		file_name = dir.get_next()
	dir.list_dir_end()

func _handle_read_script(params: Dictionary) -> Dictionary:
	var script_path = params.get("script_path", "")
	if script_path.is_empty():
		return {"error": "script_path is required"}
	if not script_path.begins_with("res://"):
		script_path = "res://" + script_path
	if not FileAccess.file_exists(script_path):
		return {"error": "Script file not found: " + script_path}
	var content = FileAccess.get_file_as_string(script_path)
	return {"content": content, "path": script_path}

func _handle_create_script(params: Dictionary) -> Dictionary:
	var script_path = params.get("script_path", "")
	var template = params.get("template", "")
	var extends_class = params.get("extends_class", "Node")
	if script_path.is_empty():
		return {"error": "script_path is required"}
	if not script_path.begins_with("res://"):
		script_path = "res://" + script_path

	var content = template
	if content.is_empty():
		content = "extends " + extends_class + "\n\n"

	var file = FileAccess.open(script_path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to create script file: " + script_path}
	file.store_string(content)
	file.close()
	return {"success": true, "path": script_path}

func _handle_edit_script(params: Dictionary) -> Dictionary:
	var script_path = params.get("script_path", "")
	if script_path.is_empty():
		return {"error": "script_path is required"}
	if not script_path.begins_with("res://"):
		script_path = "res://" + script_path
	if not FileAccess.file_exists(script_path):
		return {"error": "Script file not found: " + script_path}

	var content = FileAccess.get_file_as_string(script_path)
	var modified = false

	# Search and replace mode
	var search = params.get("search", "")
	var replace = params.get("replace", "")
	if not search.is_empty():
		content = content.replace(search, replace)
		modified = true

	# Insert at line mode
	var insert_line = params.get("insert_line", -1)
	var insert_text = params.get("insert_text", "")
	if insert_line >= 0 and not insert_text.is_empty():
		var lines = content.split("\n")
		if insert_line <= lines.size():
			lines.insert(insert_line, insert_text)
			content = "\n".join(lines)
			modified = true

	if modified:
		var file = FileAccess.open(script_path, FileAccess.WRITE)
		if not file:
			return {"error": "Failed to write script file"}
		file.store_string(content)
		file.close()

	return {"success": true, "modified": modified, "path": script_path}

func _handle_attach_script(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var script_path = params.get("script_path", "")
	if node_path.is_empty() or script_path.is_empty():
		return {"error": "node_path and script_path are required"}
	if not script_path.begins_with("res://"):
		script_path = "res://" + script_path
	if not FileAccess.file_exists(script_path):
		return {"error": "Script file not found: " + script_path}

	var root = get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently open"}

	var node = root.get_node_or_null(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var script = load(script_path) as Script
	if not script:
		return {"error": "Failed to load script: " + script_path}

	node.set_script(script)
	return {"success": true, "node_path": node_path, "script_path": script_path}

func _handle_get_open_scripts(params: Dictionary) -> Dictionary:
	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}
	var script_editor = ei.get_script_editor()
	if not script_editor:
		return {"error": "Script editor not available"}

	var open_scripts = []
	if script_editor.has_method("get_open_scripts"):
		var scripts = script_editor.get_open_scripts()
		for s in scripts:
			if s is Script:
				open_scripts.append({"path": s.resource_path, "name": s.resource_path.get_file()})
	return {"open_scripts": open_scripts}
