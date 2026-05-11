@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"read_resource",
		"edit_resource",
		"create_resource",
		"list_resources_in_dir",
		"duplicate_resource",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"read_resource":
			return _handle_read_resource(params)
		"edit_resource":
			return _handle_edit_resource(params)
		"create_resource":
			return _handle_create_resource(params)
		"list_resources_in_dir":
			return _handle_list_resources_in_dir(params)
		"duplicate_resource":
			return _handle_duplicate_resource(params)
		_:
			return {"error": "Unknown resource method: " + method}

func _normalize_path(path: String) -> String:
	if not path.begins_with("res://") and not path.begins_with("uid://"):
		return "res://" + path
	return path

func _handle_read_resource(params: Dictionary) -> Dictionary:
	var path = params.get("resource_path", "")
	if path.is_empty():
		return {"error": "resource_path is required"}
	path = _normalize_path(path)

	if not ResourceLoader.exists(path):
		return {"error": "Resource not found: " + path}

	var res = ResourceLoader.load(path)
	if not res:
		return {"error": "Failed to load resource: " + path}

	var props = {}
	for prop in res.get_property_list():
		if prop.usage & PROPERTY_USAGE_STORAGE:
			var pname = prop.name
			props[pname] = {
				"value": TypeHelper.variant_to_json(res.get(pname)),
				"type": prop.type,
				"class_name": prop.get("class_name", ""),
			}

	return {
		"path": path,
		"resource_class": res.get_class(),
		"resource_name": res.resource_name,
		"properties": props,
	}

func _handle_edit_resource(params: Dictionary) -> Dictionary:
	var path = params.get("resource_path", "")
	var properties = params.get("properties", {})
	if path.is_empty():
		return {"error": "resource_path is required"}
	if properties.is_empty():
		return {"error": "properties dict is required"}

	path = _normalize_path(path)
	if not ResourceLoader.exists(path):
		return {"error": "Resource not found: " + path}

	var res = ResourceLoader.load(path)
	if not res:
		return {"error": "Failed to load resource: " + path}

	var changed = []
	for key in properties.keys():
		if not key in res:
			continue
		var existing = res.get(key)
		var target_type = typeof(existing)
		var class_hint = ""
		if existing is Object and existing != null:
			class_hint = existing.get_class()
		var converted = TypeHelper.parse_godot_value(properties[key], target_type, class_hint)
		res.set(key, converted)
		changed.append(key)

	var err = ResourceSaver.save(res, path)
	if err != OK:
		return {"error": "Failed to save resource: " + str(err), "code": err}

	return {
		"success": true,
		"path": path,
		"changed": changed,
	}

func _handle_create_resource(params: Dictionary) -> Dictionary:
	var path = params.get("resource_path", "")
	var resource_class = params.get("resource_class", "")
	var properties = params.get("properties", {})
	if path.is_empty():
		return {"error": "resource_path is required"}
	if resource_class.is_empty():
		return {"error": "resource_class is required (e.g. \"StandardMaterial3D\")"}

	path = _normalize_path(path)

	if not ClassDB.class_exists(resource_class):
		return {"error": "Unknown class: " + resource_class}
	if not ClassDB.is_parent_class(resource_class, "Resource"):
		return {"error": "Class is not a Resource: " + resource_class}

	var instance = ClassDB.instantiate(resource_class)
	if not instance:
		return {"error": "Failed to instantiate: " + resource_class}
	if not instance is Resource:
		return {"error": "Instance is not a Resource: " + resource_class}

	for key in properties.keys():
		if key in instance:
			var existing = instance.get(key)
			var target_type = typeof(existing)
			var class_hint = ""
			if existing is Object and existing != null:
				class_hint = existing.get_class()
			instance.set(key, TypeHelper.parse_godot_value(properties[key], target_type, class_hint))

	var err = ResourceSaver.save(instance, path)
	if err != OK:
		return {"error": "Failed to save resource: " + str(err), "code": err}

	return {
		"success": true,
		"path": path,
		"resource_class": resource_class,
	}

func _handle_list_resources_in_dir(params: Dictionary) -> Dictionary:
	var directory = params.get("directory", "res://")
	var recursive = params.get("recursive", true)
	var extensions = params.get("extensions", ["tres", "res"])

	if not directory.begins_with("res://"):
		directory = "res://" + directory

	var results = []
	_scan_resources(directory, recursive, extensions, results)
	return {"resources": results, "count": results.size()}

func _scan_resources(dir_path: String, recursive: bool, extensions: Array, results: Array) -> void:
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
				_scan_resources(full_path, recursive, extensions, results)
		else:
			var ext = file_name.get_extension().to_lower()
			if ext in extensions:
				results.append(full_path)
		file_name = dir.get_next()
	dir.list_dir_end()

func _handle_duplicate_resource(params: Dictionary) -> Dictionary:
	var source_path = params.get("source_path", "")
	var target_path = params.get("target_path", "")
	if source_path.is_empty() or target_path.is_empty():
		return {"error": "source_path and target_path are required"}

	source_path = _normalize_path(source_path)
	target_path = _normalize_path(target_path)

	if not ResourceLoader.exists(source_path):
		return {"error": "Source resource not found: " + source_path}

	var src = ResourceLoader.load(source_path)
	if not src:
		return {"error": "Failed to load source resource"}

	var deep = bool(params.get("deep", true))
	var dup = src.duplicate(deep) if src is Resource else null
	if not dup:
		return {"error": "Failed to duplicate resource"}

	var err = ResourceSaver.save(dup, target_path)
	if err != OK:
		return {"error": "Failed to save duplicated resource: " + str(err)}

	return {
		"success": true,
		"source": source_path,
		"target": target_path,
	}
