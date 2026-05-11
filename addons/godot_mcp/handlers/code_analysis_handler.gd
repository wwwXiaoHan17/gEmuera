@tool
extends McpHandler

# Code Analysis handler — pure static project introspection. Walks res:// once
# per call, reads .tscn / .gd / .tres / .gdshader files as text, then runs
# regex / parse passes to surface unused resources, signal connection graphs,
# scene complexity stats, script reference maps, and circular scene
# dependencies (Tarjan SCC). Big-O is dominated by file I/O, so we accept
# `max_files` and `include_globs` filters to keep responses bounded.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"find_unused_resources",
		"analyze_signal_flow",
		"analyze_scene_complexity",
		"find_script_references",
		"detect_circular_dependencies",
		"get_project_statistics",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"find_unused_resources":
			return _handle_find_unused(params)
		"analyze_signal_flow":
			return _handle_signal_flow(params)
		"analyze_scene_complexity":
			return _handle_scene_complexity(params)
		"find_script_references":
			return _handle_script_refs(params)
		"detect_circular_dependencies":
			return _handle_circular_deps(params)
		"get_project_statistics":
			return _handle_statistics(params)
		_:
			return {"error": "Unknown code_analysis method: " + method}

# ----------------------------------------------------------------------------
# Filesystem walk
# ----------------------------------------------------------------------------

const _DEFAULT_MAX_FILES := 5000
const _RESOURCE_EXTS := ["tscn", "tres", "gd", "gdshader", "shader",
	"png", "jpg", "jpeg", "webp", "svg", "tga", "bmp",
	"ogg", "wav", "mp3",
	"glb", "gltf", "obj", "fbx",
	"ttf", "otf",
	"json", "txt", "cfg"]

func _gather_files(root: String, exts: Array, max_files: int, out: Array) -> void:
	var dir := DirAccess.open(root)
	if not dir:
		return
	dir.list_dir_begin()
	var name := dir.get_next()
	while name != "":
		if not name.begins_with("."):
			var full = root.path_join(name)
			if dir.current_is_dir():
				if out.size() < max_files:
					_gather_files(full, exts, max_files, out)
			else:
				if exts.is_empty() or exts.has(name.get_extension().to_lower()):
					if out.size() < max_files:
						out.append(full)
		name = dir.get_next()
	dir.list_dir_end()

func _read_text(path: String) -> String:
	var f := FileAccess.open(path, FileAccess.READ)
	if not f:
		return ""
	var t := f.get_as_text()
	f.close()
	return t

# ----------------------------------------------------------------------------
# find_unused_resources
# ----------------------------------------------------------------------------

func _handle_find_unused(params: Dictionary) -> Dictionary:
	var max_files := int(params.get("max_files", _DEFAULT_MAX_FILES))
	var ignore: Array = params.get("ignore", [])
	# Always-ignore: project metadata & engine generated files.
	var always_ignore := [
		"res://project.godot",
		"res://export_presets.cfg",
		"res://default_bus_layout.tres",
		"res://default_env.tres",
		"res://icon.svg",
		"res://icon.svg.import",
	]

	var files: Array = []
	_gather_files("res://", _RESOURCE_EXTS, max_files, files)

	var all_resources := {}
	for f in files:
		# Skip .import sidecars; they're tracked indirectly via the underlying file.
		if f.ends_with(".import"):
			continue
		all_resources[f] = true

	var referenced := {}
	# Regex once, reuse for all files.
	var rx_res := RegEx.new()
	rx_res.compile(r'res://[^\s"\)\]]+')
	var rx_uid := RegEx.new()
	rx_uid.compile(r'uid://[A-Za-z0-9]+')

	# Parsing scan files (the ones that reference resources).
	var parse_exts := ["tscn", "tres", "gd", "gdshader", "shader", "cfg", "godot", "json"]
	var parse_files: Array = []
	_gather_files("res://", parse_exts, max_files, parse_files)
	# project.godot lives outside res:// scan in some Godot versions; manually add.
	if FileAccess.file_exists("res://project.godot"):
		parse_files.append("res://project.godot")

	for f in parse_files:
		var text := _read_text(f)
		if text.is_empty():
			continue
		for m in rx_res.search_all(text):
			var ref: String = m.get_string()
			# Strip trailing punctuation like ")" or ","
			ref = ref.rstrip(",) \"'")
			referenced[ref] = true
		for m in rx_uid.search_all(text):
			var uid_text: String = m.get_string()
			var uid_id := ResourceUID.text_to_id(uid_text)
			if ResourceUID.has_id(uid_id):
				var path := ResourceUID.get_id_path(uid_id)
				if path:
					referenced[path] = true

	var unused := []
	for path in all_resources.keys():
		if always_ignore.has(path):
			continue
		var skip := false
		for pattern in ignore:
			if path.match(String(pattern)):
				skip = true
				break
		if skip:
			continue
		if not referenced.has(path):
			unused.append(path)
	unused.sort()

	return {
		"success": true,
		"total_resources": all_resources.size(),
		"referenced_count": referenced.size(),
		"unused_count": unused.size(),
		"unused": unused,
		"truncated": files.size() >= max_files,
	}

# ----------------------------------------------------------------------------
# analyze_signal_flow
# ----------------------------------------------------------------------------

# Parse [connection ...] sections in .tscn. Each line looks like:
#   [connection signal="pressed" from="Button" to="." method="_on_button_pressed"]
func _handle_signal_flow(params: Dictionary) -> Dictionary:
	var max_files := int(params.get("max_files", _DEFAULT_MAX_FILES))
	var files: Array = []
	_gather_files("res://", ["tscn"], max_files, files)

	var connections := []
	var rx := RegEx.new()
	rx.compile(r'\[connection\s+signal="(?<signal>[^"]+)"\s+from="(?<from>[^"]+)"\s+to="(?<to>[^"]+)"\s+method="(?<method>[^"]+)"')

	for f in files:
		var text := _read_text(f)
		for m in rx.search_all(text):
			connections.append({
				"scene": f,
				"signal": m.get_string("signal"),
				"from": m.get_string("from"),
				"to": m.get_string("to"),
				"method": m.get_string("method"),
			})

	# Build a lightweight adjacency graph keyed by (scene, from-node).
	var graph := {}
	for c in connections:
		var key: String = c["scene"] + "::" + c["from"]
		if not graph.has(key):
			graph[key] = []
		graph[key].append({
			"signal": c["signal"],
			"to": c["to"],
			"method": c["method"],
		})

	return {
		"success": true,
		"connection_count": connections.size(),
		"connections": connections,
		"graph": graph,
	}

# ----------------------------------------------------------------------------
# analyze_scene_complexity
# ----------------------------------------------------------------------------

# Counts nodes / depth / instance count per .tscn. Depth comes from "parent="
# attribute paths (parent="." is depth 1, parent="Foo" is depth 2, etc.).
func _handle_scene_complexity(params: Dictionary) -> Dictionary:
	var max_files := int(params.get("max_files", _DEFAULT_MAX_FILES))
	var files: Array = []
	_gather_files("res://", ["tscn"], max_files, files)

	var rx_node := RegEx.new()
	rx_node.compile(r'\[node\s+([^\]]+)\]')
	var rx_parent := RegEx.new()
	rx_parent.compile(r'parent="([^"]*)"')
	var rx_instance := RegEx.new()
	rx_instance.compile(r'\[ext_resource\s+[^\]]*type="PackedScene"')

	var per_scene := []
	var totals := {"nodes": 0, "scenes": files.size(), "instances": 0}
	for f in files:
		var text := _read_text(f)
		var node_matches := rx_node.search_all(text)
		var node_count := node_matches.size()
		var max_depth := 1
		for m in node_matches:
			var attrs := m.get_string(1)
			var pmatch := rx_parent.search(attrs)
			if pmatch:
				var parent_path := pmatch.get_string(1)
				if not parent_path.is_empty() and parent_path != ".":
					var depth := parent_path.split("/").size() + 1
					if depth > max_depth:
						max_depth = depth
				else:
					if max_depth < 1:
						max_depth = 1
		var instance_count := rx_instance.search_all(text).size()
		totals["nodes"] += node_count
		totals["instances"] += instance_count
		per_scene.append({
			"path": f,
			"node_count": node_count,
			"max_depth": max_depth,
			"instance_count": instance_count,
			"size_bytes": text.length(),
		})

	return {
		"success": true,
		"totals": totals,
		"per_scene": per_scene,
	}

# ----------------------------------------------------------------------------
# find_script_references
# ----------------------------------------------------------------------------

func _handle_script_refs(params: Dictionary) -> Dictionary:
	var max_files := int(params.get("max_files", _DEFAULT_MAX_FILES))
	var script_path: String = String(params.get("script_path", ""))
	var class_name_str: String = String(params.get("class_name", ""))
	if script_path.is_empty() and class_name_str.is_empty():
		return {"error": "Either script_path or class_name is required"}

	var files: Array = []
	_gather_files("res://", ["tscn", "tres", "gd"], max_files, files)

	var refs := []
	var search_terms := []
	if not script_path.is_empty():
		search_terms.append(script_path)
		var uid := ResourceLoader.get_resource_uid(script_path) if ResourceLoader.exists(script_path) else -1
		if uid != -1:
			search_terms.append(ResourceUID.id_to_text(uid))
	if not class_name_str.is_empty():
		search_terms.append(class_name_str)

	for f in files:
		var text := _read_text(f)
		if text.is_empty():
			continue
		var hits := []
		var line_num := 1
		for line in text.split("\n"):
			for term in search_terms:
				if line.find(String(term)) != -1:
					hits.append({"line": line_num, "snippet": line.strip_edges().substr(0, 200)})
					break
			line_num += 1
		if not hits.is_empty():
			refs.append({"file": f, "hits": hits})

	return {
		"success": true,
		"reference_count": refs.size(),
		"search_terms": search_terms,
		"references": refs,
	}

# ----------------------------------------------------------------------------
# detect_circular_dependencies (Tarjan SCC)
# ----------------------------------------------------------------------------

func _handle_circular_deps(params: Dictionary) -> Dictionary:
	var max_files := int(params.get("max_files", _DEFAULT_MAX_FILES))
	var files: Array = []
	_gather_files("res://", ["tscn"], max_files, files)

	# Build edges: scene → scene (PackedScene ext_resource references).
	var edges := {}
	var rx := RegEx.new()
	rx.compile(r'\[ext_resource\s+[^\]]*type="PackedScene"\s+[^\]]*path="(?<path>res://[^"]+)"')

	for f in files:
		edges[f] = []
		var text := _read_text(f)
		for m in rx.search_all(text):
			var dep: String = m.get_string("path")
			if dep != f:
				edges[f].append(dep)

	var sccs := _tarjan_scc(edges)
	var cycles := []
	for scc in sccs:
		if scc.size() > 1:
			cycles.append(scc)

	return {
		"success": true,
		"scene_count": files.size(),
		"cycle_count": cycles.size(),
		"cycles": cycles,
	}

# Iterative Tarjan SCC. Recursive form blew the stack on large graphs.
func _tarjan_scc(graph: Dictionary) -> Array:
	var index := 0
	var stack := []
	var on_stack := {}
	var indices := {}
	var lowlinks := {}
	var result := []
	var nodes := graph.keys()

	# Each call frame: {node, child_index, neighbors}
	var call_stack := []

	for v0 in nodes:
		if indices.has(v0):
			continue
		call_stack.push_back({"node": v0, "child_index": 0, "neighbors": graph.get(v0, [])})
		indices[v0] = index
		lowlinks[v0] = index
		index += 1
		stack.push_back(v0)
		on_stack[v0] = true

		while not call_stack.is_empty():
			var frame: Dictionary = call_stack.back()
			var v: String = frame["node"]
			var ci: int = frame["child_index"]
			var neighbors: Array = frame["neighbors"]
			if ci < neighbors.size():
				var w: String = neighbors[ci]
				frame["child_index"] = ci + 1
				if not indices.has(w):
					indices[w] = index
					lowlinks[w] = index
					index += 1
					stack.push_back(w)
					on_stack[w] = true
					call_stack.push_back({"node": w, "child_index": 0, "neighbors": graph.get(w, [])})
				elif on_stack.get(w, false):
					lowlinks[v] = min(int(lowlinks[v]), int(indices[w]))
			else:
				if int(lowlinks[v]) == int(indices[v]):
					var component := []
					while not stack.is_empty():
						var w2: String = stack.pop_back()
						on_stack.erase(w2)
						component.append(w2)
						if w2 == v:
							break
					result.append(component)
				call_stack.pop_back()
				if not call_stack.is_empty():
					var parent_frame: Dictionary = call_stack.back()
					var p: String = parent_frame["node"]
					lowlinks[p] = min(int(lowlinks[p]), int(lowlinks[v]))
	return result

# ----------------------------------------------------------------------------
# get_project_statistics
# ----------------------------------------------------------------------------

func _handle_statistics(params: Dictionary) -> Dictionary:
	var max_files := int(params.get("max_files", _DEFAULT_MAX_FILES))
	var all_files: Array = []
	_gather_files("res://", [], max_files, all_files)

	var by_ext := {}
	var total_bytes := 0
	var total_lines := 0
	var script_files := 0
	var scene_files := 0
	var resource_files := 0

	for f in all_files:
		var ext: String = f.get_extension().to_lower()
		by_ext[ext] = int(by_ext.get(ext, 0)) + 1
		var size := FileAccess.get_file_as_bytes(f).size()
		total_bytes += size
		match ext:
			"gd":
				script_files += 1
				var text := _read_text(f)
				total_lines += text.split("\n").size()
			"tscn":
				scene_files += 1
			"tres":
				resource_files += 1

	return {
		"success": true,
		"total_files": all_files.size(),
		"total_bytes": total_bytes,
		"by_extension": by_ext,
		"script_files": script_files,
		"scene_files": scene_files,
		"resource_files": resource_files,
		"total_script_lines": total_lines,
		"truncated": all_files.size() >= max_files,
	}
