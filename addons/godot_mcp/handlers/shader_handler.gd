@tool
extends McpHandler

const SHADER_TEMPLATES = {
	"canvas_item": "shader_type canvas_item;\n\nvoid fragment() {\n\tCOLOR = texture(TEXTURE, UV);\n}\n",
	"spatial": "shader_type spatial;\n\nvoid fragment() {\n\tALBEDO = vec3(1.0);\n}\n",
	"particles": "shader_type particles;\n\nvoid start() {\n\tVELOCITY = vec3(0.0, 1.0, 0.0);\n}\n\nvoid process() {\n\tVELOCITY.y += 9.8 * DELTA;\n}\n",
	"sky": "shader_type sky;\n\nvoid sky() {\n\tCOLOR = vec3(0.5, 0.7, 1.0);\n}\n",
	"fog": "shader_type fog;\n\nvoid fog() {\n\tDENSITY = 0.05;\n}\n",
}

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"create_shader",
		"read_shader",
		"edit_shader",
		"assign_shader_material",
		"set_shader_param",
		"get_shader_params",
		"compile_check",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"create_shader":
			return _handle_create_shader(params)
		"read_shader":
			return _handle_read_shader(params)
		"edit_shader":
			return _handle_edit_shader(params)
		"assign_shader_material":
			return _handle_assign_shader_material(params)
		"set_shader_param":
			return _handle_set_shader_param(params)
		"get_shader_params":
			return _handle_get_shader_params(params)
		"compile_check":
			return _handle_compile_check(params)
		_:
			return {"error": "Unknown shader method: " + method}

func _normalize_path(path: String) -> String:
	if not path.begins_with("res://"):
		return "res://" + path
	return path

func _handle_create_shader(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("shader_path", ""))
	var shader_type = params.get("shader_type", "canvas_item")
	var custom_code = params.get("code", "")
	if path.is_empty():
		return {"error": "shader_path is required"}
	if not path.ends_with(".gdshader"):
		return {"error": "shader_path must end with .gdshader"}

	var code = custom_code if not custom_code.is_empty() else SHADER_TEMPLATES.get(shader_type, "")
	if code.is_empty():
		return {"error": "Unknown shader_type: " + shader_type + " (supported: canvas_item, spatial, particles, sky, fog)"}

	# Write source file
	var file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to create shader file at: " + path}
	file.store_string(code)
	file.close()

	# Re-import / refresh
	var ei = get_editor_interface()
	if ei:
		var fs = ei.get_resource_filesystem()
		if fs:
			fs.update_file(path)

	return {"success": true, "path": path, "shader_type": shader_type, "lines": code.split("\n").size()}

func _handle_read_shader(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("shader_path", ""))
	if path.is_empty():
		return {"error": "shader_path is required"}
	if not FileAccess.file_exists(path):
		return {"error": "Shader file not found: " + path}
	return {"path": path, "code": FileAccess.get_file_as_string(path)}

func _handle_edit_shader(params: Dictionary) -> Dictionary:
	var path = _normalize_path(params.get("shader_path", ""))
	if path.is_empty():
		return {"error": "shader_path is required"}
	if not FileAccess.file_exists(path):
		return {"error": "Shader file not found: " + path}

	var content = FileAccess.get_file_as_string(path)
	var modified = false

	# Wholesale replace
	var new_code = params.get("code", "")
	if not new_code.is_empty():
		content = new_code
		modified = true
	else:
		# Search/replace
		var search = params.get("search", "")
		var replace = params.get("replace", "")
		if not search.is_empty():
			content = content.replace(search, replace)
			modified = true

	if modified:
		var file = FileAccess.open(path, FileAccess.WRITE)
		if not file:
			return {"error": "Failed to write shader file"}
		file.store_string(content)
		file.close()
		var ei = get_editor_interface()
		if ei:
			var fs = ei.get_resource_filesystem()
			if fs:
				fs.update_file(path)

	return {"success": true, "modified": modified, "path": path}

func _handle_assign_shader_material(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var shader_path = _normalize_path(params.get("shader_path", ""))
	var material_property = params.get("material_property", "material")
	if node_path.is_empty() or shader_path.is_empty():
		return {"error": "node_path and shader_path are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	if not material_property in node:
		return {"error": "Property not found on node: " + material_property}

	var shader = ResourceLoader.load(shader_path)
	if not shader is Shader:
		return {"error": "Not a valid shader: " + shader_path}

	var material = ShaderMaterial.new()
	material.shader = shader

	# Apply optional initial params
	var init_params = params.get("params", {})
	if init_params is Dictionary:
		for key in init_params.keys():
			material.set_shader_parameter(key, TypeHelper.parse_godot_value(init_params[key]))

	var tracked = UndoHelper.do_property(node, material_property, material, "Assign shader material")
	return {
		"success": true,
		"node_path": node_path,
		"shader_path": shader_path,
		"undoable": tracked,
	}

func _handle_set_shader_param(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var param_name = params.get("param_name", "")
	var value = params.get("value", null)
	var material_property = params.get("material_property", "material")
	if node_path.is_empty() or param_name.is_empty():
		return {"error": "node_path and param_name are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	var mat = node.get(material_property)
	if not mat is ShaderMaterial:
		return {"error": "Node's material is not a ShaderMaterial"}

	var converted = TypeHelper.parse_godot_value(value)
	var old_value = mat.get_shader_parameter(param_name)

	var do_call = func(): mat.set_shader_parameter(param_name, converted)
	var undo_call = func(): mat.set_shader_parameter(param_name, old_value)
	var tracked = UndoHelper.do_simple("Set shader param: " + param_name, do_call, undo_call)

	return {
		"success": true,
		"param_name": param_name,
		"undoable": tracked,
	}

func _handle_get_shader_params(params: Dictionary) -> Dictionary:
	var shader_path = _normalize_path(params.get("shader_path", ""))
	if shader_path.is_empty():
		return {"error": "shader_path is required"}
	var shader = ResourceLoader.load(shader_path)
	if not shader is Shader:
		return {"error": "Not a valid shader: " + shader_path}

	var uniforms = []
	for u in shader.get_shader_uniform_list():
		uniforms.append({
			"name": u.name,
			"type": u.type,
			"hint": u.hint,
			"hint_string": u.hint_string,
		})
	return {"path": shader_path, "uniforms": uniforms, "count": uniforms.size()}

func _handle_compile_check(params: Dictionary) -> Dictionary:
	var shader_path = _normalize_path(params.get("shader_path", ""))
	if shader_path.is_empty():
		return {"error": "shader_path is required"}
	if not FileAccess.file_exists(shader_path):
		return {"error": "Shader file not found: " + shader_path}

	var shader = Shader.new()
	shader.code = FileAccess.get_file_as_string(shader_path)

	# If load succeeds via ResourceLoader, compilation is OK
	var loaded = ResourceLoader.load(shader_path, "Shader", ResourceLoader.CACHE_MODE_REPLACE)
	var ok = loaded is Shader
	return {
		"success": ok,
		"path": shader_path,
		"compiled": ok,
		"shader_class": shader.get_class(),
	}
