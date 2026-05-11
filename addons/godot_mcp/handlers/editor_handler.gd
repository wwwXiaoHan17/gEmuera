@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"get_editor_errors",
		"get_editor_screenshot",
		"get_game_screenshot",
		"execute_editor_script",
		"reload_plugin",
		"reload_project",
		"clear_output",
		"compare_screenshots",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"get_editor_errors":
			return _handle_get_editor_errors(params)
		"get_editor_screenshot":
			return _handle_get_editor_screenshot(params)
		"get_game_screenshot":
			return _handle_get_game_screenshot(params)
		"execute_editor_script":
			return _handle_execute_editor_script(params)
		"reload_plugin":
			return _handle_reload_plugin(params)
		"reload_project":
			return _handle_reload_project(params)
		"clear_output":
			return _handle_clear_output(params)
		"compare_screenshots":
			return _handle_compare_screenshots(params)
		_:
			return {"error": "Unknown editor method: " + method}

func _handle_get_editor_errors(params: Dictionary) -> Dictionary:
	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}

	var errors = []
	var script_editor = ei.get_script_editor()
	if script_editor:
		var current = script_editor.get_current_editor()
		if current and current.has_method("get_errors"):
			var errs = current.get_errors()
			for e in errs:
				errors.append({
					"line": e.get("line", 0),
					"column": e.get("column", 0),
					"message": e.get("message", ""),
				})

	# Also try to get errors from EditorLog
	var base = ei.get_base_control()
	if base:
		var log_node = base.find_child("*Log*", true, false)
		if log_node and log_node.has_method("get_messages"):
			var messages = log_node.get_messages()
			for msg in messages:
				if msg is Dictionary and msg.get("type", "") == "error":
					errors.append({
						"line": msg.get("line", 0),
						"message": msg.get("text", ""),
					})

	return {"errors": errors}

func _handle_get_editor_screenshot(params: Dictionary) -> Dictionary:
	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}

	var main_screen = ei.get_editor_main_screen()
	if not main_screen:
		return {"error": "Editor main screen not available"}

	var viewport = main_screen.get_viewport()
	if not viewport:
		return {"error": "Viewport not available"}

	var image = viewport.get_texture().get_image()
	if not image:
		return {"error": "Failed to capture screenshot"}

	var buffer = image.save_png_to_buffer()
	var base64 = Marshalls.raw_to_base64(buffer)
	return {
		"image_base64": base64,
		"width": image.get_width(),
		"height": image.get_height(),
		"format": "png"
	}

func _handle_get_game_screenshot(params: Dictionary) -> Dictionary:
	var tree = get_editor_interface().get_tree()
	if not tree:
		return {"error": "Scene tree not available"}

	var root = tree.root
	if not root:
		return {"error": "Root viewport not available"}

	var viewport = root.get_viewport()
	var image = viewport.get_texture().get_image()
	if not image:
		return {"error": "Failed to capture game screenshot"}

	var buffer = image.save_png_to_buffer()
	var base64 = Marshalls.raw_to_base64(buffer)
	return {
		"image_base64": base64,
		"width": image.get_width(),
		"height": image.get_height(),
		"format": "png"
	}

func _handle_execute_editor_script(params: Dictionary) -> Dictionary:
	var code = params.get("code", "")
	if code.is_empty():
		return {"error": "code is required"}

	# Advisory guardrails (NOT a sandbox). When false (default), reject
	# scripts that contain the named API surfaces. Caller can opt in
	# explicitly by passing the corresponding allow_* flag.
	var allow_filesystem_writes = bool(params.get("allow_filesystem_writes", false))
	var allow_os_execute = bool(params.get("allow_os_execute", false))
	var timeout_ms = int(params.get("timeout_ms", 5000))

	if not allow_filesystem_writes:
		var fs_patterns = [
			"DirAccess.remove",
			"DirAccess.remove_absolute",
			"DirAccess.rename",
			"FileAccess.open",  # we'll check WRITE flag below
			"ProjectSettings.save",
		]
		for pat in fs_patterns:
			if code.find(pat) != -1:
				# Refine: only reject FileAccess.open if it's likely a write
				if pat == "FileAccess.open":
					if code.find("FileAccess.WRITE") == -1 and code.find("FileAccess.WRITE_READ") == -1:
						continue
				return {
					"error": "Filesystem-mutating API rejected by guardrail: " + pat,
					"error_code": "guarded",
					"hint": "Pass allow_filesystem_writes=true to override (advisory only).",
				}

	if not allow_os_execute:
		var os_patterns = ["OS.execute", "OS.shell_open", "OS.create_process", "OS.kill"]
		for pat in os_patterns:
			if code.find(pat) != -1:
				return {
					"error": "OS-spawning API rejected by guardrail: " + pat,
					"error_code": "guarded",
					"hint": "Pass allow_os_execute=true to override (advisory only).",
				}

	# Note: timeout_ms is informational only — Godot's expression/script
	# evaluation runs synchronously in the editor and cannot be interrupted
	# from another thread without crashing. This field exists so future
	# implementations can enforce it via a separate process.
	var _ignored_timeout = timeout_ms

	# Try as Expression first (faster, safer)
	var expression = Expression.new()
	var err = expression.parse(code)
	if err == OK:
		var root = get_editor_interface().get_tree().root
		var result = expression.execute([], root)
		if expression.has_execute_failed():
			return {"error": "Expression execution failed: " + expression.get_error_text()}
		return {"result": str(result), "type": "expression"}

	# Fall back to a full GDScript class
	var script = GDScript.new()
	script.source_code = code
	err = script.reload()
	if err != OK:
		return {"error": "Script compilation failed: " + str(err)}

	var instance = script.new()
	if not instance:
		return {"error": "Failed to instantiate script"}

	var result = null
	if instance.has_method("run"):
		result = instance.run()
		return {"result": str(result), "type": "script"}

	if "result" in instance:
		return {"result": str(instance.result), "type": "script"}

	return {"success": true, "type": "script", "message": "Script executed"}

func _handle_reload_plugin(params: Dictionary) -> Dictionary:
	var plugin_name = params.get("plugin_name", "")
	if plugin_name.is_empty():
		return {"error": "plugin_name is required"}

	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}

	# Get current status and toggle
	var is_enabled = ei.is_plugin_enabled(plugin_name)
	ei.set_plugin_enabled(plugin_name, false)
	ei.set_plugin_enabled(plugin_name, true)
	return {"success": true, "plugin_name": plugin_name, "was_enabled": is_enabled}

func _handle_reload_project(params: Dictionary) -> Dictionary:
	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}

	# restart_editor(save: bool) will prompt to save and restart
	ei.restart_editor(true)
	return {"success": true, "message": "Project reload initiated"}

func _handle_clear_output(params: Dictionary) -> Dictionary:
	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}

	var base = ei.get_base_control()
	if base:
		var log_node = base.find_child("*Log*", true, false)
		if log_node and log_node.has_method("clear"):
			log_node.clear()
			return {"success": true}

	return {"success": false, "message": "Could not find output log node"}

# Compare two PNG screenshots pixel-by-pixel. Accepts either base64-encoded
# PNGs (image_a_base64 / image_b_base64) or filesystem paths
# (image_a_path / image_b_path). Returns aggregate diff stats — diff_pixels,
# total_pixels, max_diff_per_channel — plus a small sample of differing
# coordinates so the LLM can see where the change happened without needing
# the full diff buffer.
func _handle_compare_screenshots(params: Dictionary) -> Dictionary:
	var image_a := _load_image_arg(params, "a")
	if image_a is Dictionary and image_a.has("error"):
		return image_a
	var image_b := _load_image_arg(params, "b")
	if image_b is Dictionary and image_b.has("error"):
		return image_b

	var img_a: Image = image_a
	var img_b: Image = image_b
	if img_a.get_width() != img_b.get_width() or img_a.get_height() != img_b.get_height():
		return {
			"error": "Image dimensions differ",
			"size_a": {"w": img_a.get_width(), "h": img_a.get_height()},
			"size_b": {"w": img_b.get_width(), "h": img_b.get_height()},
		}

	var tolerance := int(params.get("tolerance", 0))
	var max_samples := int(params.get("max_samples", 10))

	var w := img_a.get_width()
	var h := img_a.get_height()
	var total := w * h
	var diff_pixels := 0
	var max_channel_diff := 0
	var samples: Array = []
	# Convert to RGBA8 once to keep per-pixel reads cheap and uniform.
	if img_a.get_format() != Image.FORMAT_RGBA8:
		img_a.convert(Image.FORMAT_RGBA8)
	if img_b.get_format() != Image.FORMAT_RGBA8:
		img_b.convert(Image.FORMAT_RGBA8)

	for y in range(h):
		for x in range(w):
			var ca := img_a.get_pixel(x, y)
			var cb := img_b.get_pixel(x, y)
			var dr := abs(int(ca.r * 255) - int(cb.r * 255))
			var dg := abs(int(ca.g * 255) - int(cb.g * 255))
			var db := abs(int(ca.b * 255) - int(cb.b * 255))
			var da := abs(int(ca.a * 255) - int(cb.a * 255))
			var max_d := max(max(dr, dg), max(db, da))
			if max_d > tolerance:
				diff_pixels += 1
				if max_d > max_channel_diff:
					max_channel_diff = max_d
				if samples.size() < max_samples:
					samples.append({"x": x, "y": y, "max_diff": max_d})

	var ratio := float(diff_pixels) / float(total) if total > 0 else 0.0
	return {
		"success": true,
		"identical": diff_pixels == 0,
		"diff_pixels": diff_pixels,
		"total_pixels": total,
		"diff_ratio": ratio,
		"max_diff_per_channel": max_channel_diff,
		"tolerance": tolerance,
		"size": {"w": w, "h": h},
		"sample_diffs": samples,
	}

# Load image from one of: image_<suffix>_base64, image_<suffix>_path. Returns
# either an Image instance or {error: "..."} dictionary.
func _load_image_arg(params: Dictionary, suffix: String) -> Variant:
	var b64_key := "image_%s_base64" % suffix
	var path_key := "image_%s_path" % suffix
	if params.has(b64_key):
		var b64: String = String(params[b64_key])
		if b64.begins_with("data:"):
			var comma := b64.find(",")
			if comma != -1:
				b64 = b64.substr(comma + 1)
		var bytes := Marshalls.base64_to_raw(b64)
		var img := Image.new()
		var err := img.load_png_from_buffer(bytes)
		if err != OK:
			err = img.load_jpg_from_buffer(bytes)
		if err != OK:
			return {"error": "Failed to decode image %s (not PNG/JPG)" % suffix}
		return img
	if params.has(path_key):
		var path: String = String(params[path_key])
		if not path.begins_with("res://") and not path.is_absolute_path():
			return {"error": "image_%s_path must be a res:// path or absolute path" % suffix}
		var img := Image.new()
		var err := img.load(path)
		if err != OK:
			return {"error": "Failed to load image at %s: %s" % [path, str(err)]}
		return img
	return {"error": "Provide either image_%s_base64 or image_%s_path" % [suffix, suffix]}
