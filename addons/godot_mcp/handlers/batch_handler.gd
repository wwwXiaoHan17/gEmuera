@tool
extends McpHandler

# Cross-cutting batch operations & refactoring helpers.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"find_nodes_by_type",
		"find_signal_connections",
		"batch_set_property",
		"find_node_references",
		"get_scene_dependencies",
		"cross_scene_set_property",
		"replace_node_type",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"find_nodes_by_type":
			return _handle_find_nodes_by_type(params)
		"find_signal_connections":
			return _handle_find_signal_connections(params)
		"batch_set_property":
			return _handle_batch_set_property(params)
		"find_node_references":
			return _handle_find_node_references(params)
		"get_scene_dependencies":
			return _handle_get_scene_dependencies(params)
		"cross_scene_set_property":
			return _handle_cross_scene_set_property(params)
		"replace_node_type":
			return _handle_replace_node_type(params)
		_:
			return {"error": "Unknown batch method: " + method}

func _root_for(params: Dictionary) -> Node:
	var rp = params.get("root_path", "")
	if not rp.is_empty():
		return get_target_node(rp)
	var ei = get_editor_interface()
	return ei.get_edited_scene_root() if ei else null

func _handle_find_nodes_by_type(params: Dictionary) -> Dictionary:
	var type_name = params.get("type", "")
	var include_inherited = bool(params.get("include_inherited", true))
	if type_name.is_empty():
		return {"error": "type is required"}
	var root = _root_for(params)
	if not root:
		return {"error": "No root or scene available"}
	var matches := []
	_collect_by_type(root, type_name, include_inherited, matches)
	return {"matches": matches, "count": matches.size()}

func _collect_by_type(node: Node, type_name: String, include_inherited: bool, into: Array) -> void:
	var hit := false
	if include_inherited:
		hit = node.is_class(type_name)
		if not hit:
			# Custom class_name check
			var script = node.get_script()
			if script and script is GDScript:
				var cn = script.get_global_name() if script.has_method("get_global_name") else ""
				if cn == type_name:
					hit = true
	else:
		hit = node.get_class() == type_name
	if hit:
		into.append({"path": str(node.get_path()), "name": node.name, "class": node.get_class()})
	for child in node.get_children():
		_collect_by_type(child, type_name, include_inherited, into)

func _handle_find_signal_connections(params: Dictionary) -> Dictionary:
	var root = _root_for(params)
	if not root:
		return {"error": "No root or scene available"}
	var connections := []
	_collect_connections(root, connections)
	return {"connections": connections, "count": connections.size()}

func _collect_connections(node: Node, into: Array) -> void:
	for sig in node.get_signal_list():
		var sname = sig["name"]
		for c in node.get_signal_connection_list(sname):
			var target = c.get("callable").get_object() if c.has("callable") else null
			into.append({
				"node": str(node.get_path()),
				"signal": sname,
				"target": str(target.get_path()) if target is Node else str(target),
				"method": c.get("callable").get_method() if c.has("callable") else "",
				"flags": c.get("flags", 0),
			})
	for child in node.get_children():
		_collect_connections(child, into)

func _handle_batch_set_property(params: Dictionary) -> Dictionary:
	var node_paths = params.get("node_paths", [])
	var property_name = params.get("property", "")
	var value = params.get("value", null)
	if node_paths.is_empty() or property_name.is_empty():
		return {"error": "node_paths (array) and property are required"}

	var ur = UndoHelper.begin_batch("Batch set " + property_name)
	var applied := []
	var failed := []

	for np in node_paths:
		var node = get_target_node(String(np))
		if not node:
			failed.append({"path": np, "reason": "not found"})
			continue
		if not property_name in node:
			failed.append({"path": np, "reason": "no such property"})
			continue
		var current = node.get(property_name)
		var converted = TypeHelper.parse_godot_value(value, typeof(current))
		if ur:
			ur.add_do_property(node, property_name, converted)
			ur.add_undo_property(node, property_name, current)
		else:
			node.set(property_name, converted)
		applied.append(str(np))

	var tracked := false
	if ur:
		UndoHelper.commit_batch(ur)
		tracked = true

	return {
		"success": true,
		"applied": applied,
		"failed": failed,
		"undoable": tracked,
	}

func _handle_find_node_references(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var search_uid = bool(params.get("search_uid", true))
	if node_path.is_empty():
		return {"error": "node_path is required"}

	var hits := []
	var node = get_target_node(node_path)
	var uid = ""
	if node and node.scene_file_path:
		var p = node.scene_file_path
		var got_uid = ResourceUID.get_id_path(ResourceUID.text_to_id(p)) if ResourceUID.text_to_id(p) != ResourceUID.INVALID_ID else ""
		if not got_uid.is_empty():
			uid = got_uid

	# Look in all .gd / .tscn files
	var queue := ["res://"]
	while not queue.is_empty():
		var d = queue.pop_back()
		var dir = DirAccess.open(d)
		if not dir:
			continue
		dir.list_dir_begin()
		var f = dir.get_next()
		while f != "":
			if f.begins_with("."):
				f = dir.get_next()
				continue
			var fp = d.path_join(f)
			if dir.current_is_dir():
				queue.push_back(fp)
			else:
				var ext = f.get_extension().to_lower()
				if ext == "gd" or ext == "tscn":
					var content = FileAccess.get_file_as_string(fp)
					if not content.is_empty():
						if content.find(node_path) != -1:
							hits.append({"file": fp, "kind": "path"})
						elif search_uid and not uid.is_empty() and content.find(uid) != -1:
							hits.append({"file": fp, "kind": "uid"})
			f = dir.get_next()
		dir.list_dir_end()
	return {"references": hits, "count": hits.size()}

func _handle_get_scene_dependencies(params: Dictionary) -> Dictionary:
	var scene_path = String(params.get("scene_path", ""))
	if scene_path.is_empty():
		return {"error": "scene_path is required"}
	if not scene_path.begins_with("res://"):
		scene_path = "res://" + scene_path
	if not FileAccess.file_exists(scene_path):
		return {"error": "Scene file not found: " + scene_path}

	var content = FileAccess.get_file_as_string(scene_path)
	var ext_resources := []
	var sub_resources := []
	for line in content.split("\n"):
		if line.begins_with("[ext_resource"):
			var entry := {}
			var pairs = _parse_kv_pairs(line)
			entry["path"] = pairs.get("path", "")
			entry["type"] = pairs.get("type", "")
			entry["uid"] = pairs.get("uid", "")
			entry["id"] = pairs.get("id", "")
			ext_resources.append(entry)
		elif line.begins_with("[sub_resource"):
			var pairs = _parse_kv_pairs(line)
			sub_resources.append({"type": pairs.get("type", ""), "id": pairs.get("id", "")})

	return {
		"scene": scene_path,
		"ext_resources": ext_resources,
		"sub_resources": sub_resources,
	}

func _parse_kv_pairs(line: String) -> Dictionary:
	# Tokenize key="value" pairs from a Godot scene-file header line
	var out := {}
	var i := 0
	while i < line.length():
		var eq = line.find("=", i)
		if eq < 0:
			break
		# walk back to start of key
		var key_end = eq
		var key_start = key_end
		while key_start > 0 and line[key_start - 1].strip_edges() != "" and line[key_start - 1] != "[" and line[key_start - 1] != " ":
			key_start -= 1
		var key = line.substr(key_start, key_end - key_start).strip_edges()
		var val_start = eq + 1
		var val := ""
		if val_start < line.length() and line[val_start] == '"':
			val_start += 1
			var val_end = line.find('"', val_start)
			if val_end < 0:
				break
			val = line.substr(val_start, val_end - val_start)
			i = val_end + 1
		else:
			var val_end = val_start
			while val_end < line.length() and line[val_end] != " " and line[val_end] != "]":
				val_end += 1
			val = line.substr(val_start, val_end - val_start)
			i = val_end
		if not key.is_empty():
			out[key] = val
	return out

func _handle_cross_scene_set_property(params: Dictionary) -> Dictionary:
	var scenes = params.get("scenes", [])
	var node_subpath = String(params.get("node_subpath", ""))
	var property_name = String(params.get("property", ""))
	var value = params.get("value", null)
	if scenes.is_empty() or node_subpath.is_empty() or property_name.is_empty():
		return {"error": "scenes (array), node_subpath, property are required"}

	var ei = get_editor_interface()
	if not ei:
		return {"error": "EditorInterface not available"}

	var results = []
	for s in scenes:
		var path = String(s)
		if not path.begins_with("res://"):
			path = "res://" + path
		if not FileAccess.file_exists(path):
			results.append({"scene": path, "ok": false, "reason": "missing"})
			continue
		var packed = ResourceLoader.load(path)
		if not packed is PackedScene:
			results.append({"scene": path, "ok": false, "reason": "not a scene"})
			continue
		var inst = packed.instantiate(PackedScene.GEN_EDIT_STATE_INSTANCE)
		if not inst:
			results.append({"scene": path, "ok": false, "reason": "instantiate failed"})
			continue
		var target = inst.get_node_or_null(NodePath(node_subpath))
		if not target:
			results.append({"scene": path, "ok": false, "reason": "subpath not found"})
			inst.queue_free()
			continue
		if not property_name in target:
			results.append({"scene": path, "ok": false, "reason": "no such property"})
			inst.queue_free()
			continue
		var current = target.get(property_name)
		var converted = TypeHelper.parse_godot_value(value, typeof(current))
		target.set(property_name, converted)
		var ps := PackedScene.new()
		var packed_err = ps.pack(inst)
		if packed_err != OK:
			results.append({"scene": path, "ok": false, "reason": "pack: " + str(packed_err)})
			inst.queue_free()
			continue
		var save_err = ResourceSaver.save(ps, path)
		results.append({
			"scene": path,
			"ok": save_err == OK,
			"reason": "" if save_err == OK else "save: " + str(save_err),
		})
		inst.queue_free()

	# Prompt the editor to refresh
	if ei.get_resource_filesystem():
		ei.get_resource_filesystem().scan()

	return {"success": true, "results": results}

func _handle_replace_node_type(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var new_class = params.get("new_class", "")
	if node_path.is_empty() or new_class.is_empty():
		return {"error": "node_path and new_class are required"}
	if not ClassDB.class_exists(new_class):
		return {"error": "Unknown class: " + new_class}

	var old = get_target_node(node_path)
	if not old:
		return {"error": "Node not found: " + node_path}
	var parent = old.get_parent()
	if not parent:
		return {"error": "Cannot replace root node"}

	var fresh = ClassDB.instantiate(new_class)
	if not fresh is Node:
		return {"error": "Replacement is not a Node"}
	fresh.name = old.name

	# Copy compatible properties
	var copied = []
	for prop in old.get_property_list():
		if not (prop.usage & PROPERTY_USAGE_STORAGE):
			continue
		var pname = prop.name
		if pname in fresh:
			fresh.set(pname, old.get(pname))
			copied.append(pname)

	# Move children
	var children = old.get_children().duplicate()
	var ei = get_editor_interface()
	var owner_node = ei.get_edited_scene_root() if ei else parent

	var ur = UndoHelper.begin_batch("Replace node type")
	var tracked := false
	if ur:
		ur.add_do_method(parent, "remove_child", old)
		ur.add_undo_method(parent, "add_child", old, true)
		ur.add_do_method(parent, "add_child", fresh, true)
		ur.add_undo_method(parent, "remove_child", fresh)
		ur.add_do_property(fresh, "owner", owner_node)
		ur.add_do_reference(fresh)
		ur.add_undo_reference(old)
		for c in children:
			ur.add_do_method(old, "remove_child", c)
			ur.add_undo_method(fresh, "remove_child", c)
			ur.add_do_method(fresh, "add_child", c, true)
			ur.add_undo_method(old, "add_child", c, true)
		UndoHelper.commit_batch(ur)
		tracked = true
	else:
		parent.remove_child(old)
		parent.add_child(fresh)
		fresh.owner = owner_node
		for c in children:
			old.remove_child(c)
			fresh.add_child(c)
			c.owner = owner_node

	return {
		"success": true,
		"old_class": old.get_class(),
		"new_class": new_class,
		"copied_properties": copied,
		"undoable": tracked,
	}
