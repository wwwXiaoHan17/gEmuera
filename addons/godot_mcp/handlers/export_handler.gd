@tool
extends McpHandler

# Export handler — read export presets from res://export_presets.cfg, dump
# preset metadata, and trigger a headless build via OS.execute. The editor
# itself doesn't expose a scriptable "export now" API, so export_project
# spawns a separate Godot CLI process. Caller can pass godot_path explicitly;
# otherwise we fall back to OS.get_executable_path() (the running editor's own
# binary, which is the same one used to interpret the project).

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"list_export_presets",
		"export_project",
		"get_export_info",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"list_export_presets":
			return _handle_list_presets(params)
		"export_project":
			return _handle_export_project(params)
		"get_export_info":
			return _handle_get_export_info(params)
		_:
			return {"error": "Unknown export method: " + method}

const _PRESETS_PATH := "res://export_presets.cfg"

# Parse export_presets.cfg into a list of {name, platform, runnable, options...}.
# We use ConfigFile rather than re-implementing INI parsing.
func _load_presets() -> Dictionary:
	if not FileAccess.file_exists(_PRESETS_PATH):
		return {"error": "No export_presets.cfg found. Set up at least one preset in Project > Export."}
	var cfg := ConfigFile.new()
	var err := cfg.load(_PRESETS_PATH)
	if err != OK:
		return {"error": "Failed to parse export_presets.cfg: " + error_string(err)}
	return {"cfg": cfg}

# Walk all sections of the form `preset.N` and `preset.N.options` and group
# them by index. Returns an Array of Dictionaries.
func _gather_presets(cfg: ConfigFile) -> Array:
	var by_index := {}
	for section in cfg.get_sections():
		if not section.begins_with("preset."):
			continue
		var rest := section.substr(7)
		var dot := rest.find(".")
		var idx_str := rest if dot == -1 else rest.substr(0, dot)
		if not idx_str.is_valid_int():
			continue
		var idx := int(idx_str)
		if not by_index.has(idx):
			by_index[idx] = {"index": idx, "options": {}}
		var entry: Dictionary = by_index[idx]
		var is_options: bool = dot != -1 and rest.substr(dot + 1) == "options"
		for key in cfg.get_section_keys(section):
			var value = cfg.get_value(section, key)
			if is_options:
				entry["options"][key] = value
			else:
				entry[key] = value
	var keys = by_index.keys()
	keys.sort()
	var out := []
	for k in keys:
		out.append(by_index[k])
	return out

func _handle_list_presets(params: Dictionary) -> Dictionary:
	var loaded := _load_presets()
	if loaded.has("error"):
		return loaded
	var cfg: ConfigFile = loaded["cfg"]
	var presets := _gather_presets(cfg)
	# Trim down to the most useful fields for a high-level list view.
	var summary := []
	for p in presets:
		summary.append({
			"index": p.get("index", -1),
			"name": p.get("name", ""),
			"platform": p.get("platform", ""),
			"runnable": p.get("runnable", false),
			"export_path": p.get("export_path", ""),
			"export_filter": p.get("export_filter", ""),
			"include_filter": p.get("include_filter", ""),
			"exclude_filter": p.get("exclude_filter", ""),
		})
	return {
		"success": true,
		"presets": summary,
		"count": summary.size(),
	}

func _handle_get_export_info(params: Dictionary) -> Dictionary:
	var loaded := _load_presets()
	if loaded.has("error"):
		return loaded
	var cfg: ConfigFile = loaded["cfg"]
	var presets := _gather_presets(cfg)
	var preset_name = params.get("preset_name", "")
	var preset_index: int = int(params.get("preset_index", -1))

	var matched: Dictionary = {}
	for p in presets:
		if preset_index != -1 and int(p.get("index", -2)) == preset_index:
			matched = p
			break
		if not preset_name.is_empty() and String(p.get("name", "")) == preset_name:
			matched = p
			break
	if matched.is_empty():
		var available := []
		for p in presets:
			available.append(p.get("name", ""))
		return {
			"error": "Preset not found",
			"available": available,
		}
	return {
		"success": true,
		"preset": matched,
	}

func _resolve_godot_path(params: Dictionary) -> String:
	var explicit: String = String(params.get("godot_path", ""))
	if not explicit.is_empty():
		return explicit
	var env: String = OS.get_environment("GODOT_PATH")
	if not env.is_empty():
		return env
	# OS.get_executable_path() returns the Godot binary running this editor.
	return OS.get_executable_path()

func _resolve_project_path(params: Dictionary) -> String:
	var explicit: String = String(params.get("project_path", ""))
	if not explicit.is_empty():
		return explicit
	# ProjectSettings.globalize_path("res://") gives the absolute project dir.
	return ProjectSettings.globalize_path("res://")

func _handle_export_project(params: Dictionary) -> Dictionary:
	var preset_name = String(params.get("preset_name", ""))
	if preset_name.is_empty():
		return {"error": "preset_name is required"}
	var output_path = String(params.get("output_path", ""))
	if output_path.is_empty():
		return {"error": "output_path is required (absolute path to output binary or zip)"}
	var debug := bool(params.get("debug", false))
	var pack_only := bool(params.get("pack_only", false))
	var dry_run := bool(params.get("dry_run", false))

	# Validate the preset exists before spawning a subprocess.
	var info := _handle_get_export_info({"preset_name": preset_name})
	if info.has("error"):
		return info

	var godot_bin := _resolve_godot_path(params)
	var project_dir := _resolve_project_path(params)

	# Build CLI args. Godot 4 supports:
	#   --headless --path <project_dir> --export-debug <preset> <output>
	#   --headless --path <project_dir> --export-release <preset> <output>
	#   --headless --path <project_dir> --export-pack <preset> <output>
	var args: PackedStringArray = []
	args.append("--headless")
	args.append("--path")
	args.append(project_dir)
	if pack_only:
		args.append("--export-pack")
	elif debug:
		args.append("--export-debug")
	else:
		args.append("--export-release")
	args.append(preset_name)
	args.append(output_path)

	if dry_run:
		# Don't actually spawn — return the command we would have run. Useful
		# when the caller wants to invoke the build from a CI script and just
		# needs the canonical command line.
		return {
			"success": true,
			"dry_run": true,
			"godot_path": godot_bin,
			"args": args,
			"command": godot_bin + " " + " ".join(args),
		}

	var output: Array = []
	var exit_code := OS.execute(godot_bin, args, output, true, false)
	var output_text := "\n".join(output)

	if exit_code != 0:
		return {
			"error": "Export failed with exit code " + str(exit_code),
			"exit_code": exit_code,
			"output": output_text,
			"command": godot_bin + " " + " ".join(args),
		}

	return {
		"success": true,
		"preset_name": preset_name,
		"output_path": output_path,
		"debug": debug,
		"pack_only": pack_only,
		"exit_code": exit_code,
		"output": output_text,
	}
