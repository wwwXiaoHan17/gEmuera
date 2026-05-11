@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"create_theme",
		"set_theme_color",
		"set_theme_constant",
		"set_theme_font_size",
		"set_theme_stylebox",
		"get_theme_info",
		"apply_theme_to_node",
		"list_theme_types",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"create_theme":
			return _handle_create_theme(params)
		"set_theme_color":
			return _handle_set_theme_color(params)
		"set_theme_constant":
			return _handle_set_theme_constant(params)
		"set_theme_font_size":
			return _handle_set_theme_font_size(params)
		"set_theme_stylebox":
			return _handle_set_theme_stylebox(params)
		"get_theme_info":
			return _handle_get_theme_info(params)
		"apply_theme_to_node":
			return _handle_apply_theme_to_node(params)
		"list_theme_types":
			return _handle_list_theme_types(params)
		_:
			return {"error": "Unknown theme method: " + method}

func _normalize_path(path: String) -> String:
	if not path.begins_with("res://"):
		return "res://" + path
	return path

func _load_or_create_theme(path: String) -> Theme:
	if ResourceLoader.exists(path):
		var loaded = ResourceLoader.load(path)
		if loaded is Theme:
			return loaded
	return null

func _handle_create_theme(params: Dictionary) -> Dictionary:
	var path = params.get("theme_path", "")
	if path.is_empty():
		return {"error": "theme_path is required"}
	path = _normalize_path(path)

	var theme = Theme.new()
	# Optionally seed common defaults
	var default_font_size = int(params.get("default_font_size", 0))
	if default_font_size > 0:
		theme.default_font_size = default_font_size

	var err = ResourceSaver.save(theme, path)
	if err != OK:
		return {"error": "Failed to save theme: " + str(err)}
	return {"success": true, "path": path}

func _handle_set_theme_color(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("theme_path", ""))
	var type_name = params.get("type", "")
	var color_name = params.get("color_name", "")
	var color_value = params.get("color", null)
	if path.is_empty() or type_name.is_empty() or color_name.is_empty():
		return {"error": "theme_path, type, color_name are required"}

	var theme = _load_or_create_theme(path)
	if not theme:
		return {"error": "Theme not found at: " + path}

	var col = TypeHelper.parse_godot_value(color_value, TYPE_COLOR)
	if not col is Color:
		return {"error": "color must be a Color (got " + str(typeof(col)) + ")"}

	theme.set_color(color_name, type_name, col)
	var err = ResourceSaver.save(theme, path)
	if err != OK:
		return {"error": "Failed to save theme: " + str(err)}
	return {"success": true, "type": type_name, "color_name": color_name}

func _handle_set_theme_constant(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("theme_path", ""))
	var type_name = params.get("type", "")
	var constant_name = params.get("constant_name", "")
	var value = int(params.get("value", 0))
	if path.is_empty() or type_name.is_empty() or constant_name.is_empty():
		return {"error": "theme_path, type, constant_name are required"}

	var theme = _load_or_create_theme(path)
	if not theme:
		return {"error": "Theme not found at: " + path}

	theme.set_constant(constant_name, type_name, value)
	var err = ResourceSaver.save(theme, path)
	if err != OK:
		return {"error": "Failed to save theme: " + str(err)}
	return {"success": true, "type": type_name, "constant_name": constant_name, "value": value}

func _handle_set_theme_font_size(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("theme_path", ""))
	var type_name = params.get("type", "")
	var size_name = params.get("size_name", "font_size")
	var value = int(params.get("value", 16))
	if path.is_empty() or type_name.is_empty():
		return {"error": "theme_path and type are required"}

	var theme = _load_or_create_theme(path)
	if not theme:
		return {"error": "Theme not found at: " + path}

	theme.set_font_size(size_name, type_name, value)
	var err = ResourceSaver.save(theme, path)
	if err != OK:
		return {"error": "Failed to save theme: " + str(err)}
	return {"success": true, "type": type_name, "size_name": size_name, "value": value}

func _handle_set_theme_stylebox(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("theme_path", ""))
	var type_name = params.get("type", "")
	var stylebox_name = params.get("stylebox_name", "")
	var stylebox_class = params.get("stylebox_class", "StyleBoxFlat")
	var properties = params.get("properties", {})
	if path.is_empty() or type_name.is_empty() or stylebox_name.is_empty():
		return {"error": "theme_path, type, stylebox_name are required"}

	var theme = _load_or_create_theme(path)
	if not theme:
		return {"error": "Theme not found at: " + path}

	if not ClassDB.class_exists(stylebox_class):
		return {"error": "Unknown StyleBox class: " + stylebox_class}
	if not ClassDB.is_parent_class(stylebox_class, "StyleBox"):
		return {"error": "Class is not a StyleBox: " + stylebox_class}

	var sb = ClassDB.instantiate(stylebox_class)
	if not sb:
		return {"error": "Failed to instantiate: " + stylebox_class}

	for key in properties.keys():
		if key in sb:
			var existing = sb.get(key)
			var target_type = typeof(existing)
			var class_hint = ""
			if existing is Object and existing != null:
				class_hint = existing.get_class()
			sb.set(key, TypeHelper.parse_godot_value(properties[key], target_type, class_hint))

	theme.set_stylebox(stylebox_name, type_name, sb)
	var err = ResourceSaver.save(theme, path)
	if err != OK:
		return {"error": "Failed to save theme: " + str(err)}
	return {
		"success": true,
		"type": type_name,
		"stylebox_name": stylebox_name,
		"stylebox_class": stylebox_class,
	}

func _handle_get_theme_info(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("theme_path", ""))
	if path.is_empty():
		return {"error": "theme_path is required"}
	var theme = _load_or_create_theme(path)
	if not theme:
		return {"error": "Theme not found at: " + path}

	var types = theme.get_type_list()
	var entries = {}
	for t in types:
		var entry = {
			"colors": Array(theme.get_color_list(t)),
			"constants": Array(theme.get_constant_list(t)),
			"font_sizes": Array(theme.get_font_size_list(t)),
			"fonts": Array(theme.get_font_list(t)),
			"icons": Array(theme.get_icon_list(t)),
			"styleboxes": Array(theme.get_stylebox_list(t)),
		}
		entries[t] = entry

	return {
		"path": path,
		"default_font_size": theme.default_font_size,
		"types": types,
		"entries": entries,
	}

func _handle_apply_theme_to_node(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var theme_path = _normalize_path(params.get("theme_path", ""))
	if node_path.is_empty() or theme_path.is_empty():
		return {"error": "node_path and theme_path are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	if not (node is Control or node is Window):
		return {"error": "Node must be Control or Window: " + node.get_class()}

	var theme = ResourceLoader.load(theme_path)
	if not theme is Theme:
		return {"error": "Theme not found or invalid: " + theme_path}

	var tracked = UndoHelper.do_property(node, "theme", theme, "Apply theme")
	return {"success": true, "node_path": node_path, "theme_path": theme_path, "undoable": tracked}

func _handle_list_theme_types(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("theme_path", ""))
	var theme = _load_or_create_theme(path)
	if not theme:
		return {"error": "Theme not found at: " + path}
	return {"types": theme.get_type_list(), "default_font_size": theme.default_font_size}
