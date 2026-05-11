@tool
extends McpHandler

# AnimationTree handler — covers create, structural editing of state machine
# nodes/transitions, blend tree node insertion, parameter set, runtime control.
# Tree structure introspection walks AnimationNodeStateMachine /
# AnimationNodeBlendTree to surface a JSON-friendly graph.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"create_animation_tree",
		"get_animation_tree_structure",
		"add_state_machine_state",
		"remove_state_machine_state",
		"add_state_machine_transition",
		"remove_state_machine_transition",
		"set_blend_tree_node",
		"set_tree_parameter",
		"travel_to_state",
		"set_active",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"create_animation_tree":
			return _handle_create_animation_tree(params)
		"get_animation_tree_structure":
			return _handle_get_structure(params)
		"add_state_machine_state":
			return _handle_add_state(params)
		"remove_state_machine_state":
			return _handle_remove_state(params)
		"add_state_machine_transition":
			return _handle_add_transition(params)
		"remove_state_machine_transition":
			return _handle_remove_transition(params)
		"set_blend_tree_node":
			return _handle_set_blend_tree_node(params)
		"set_tree_parameter":
			return _handle_set_tree_parameter(params)
		"travel_to_state":
			return _handle_travel(params)
		"set_active":
			return _handle_set_active(params)
		_:
			return {"error": "Unknown animation_tree method: " + method}

func _resolve_tree(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node = get_target_node(node_path)
	if not node:
		return {"error": "AnimationTree node not found: " + node_path}
	if not node is AnimationTree:
		return {"error": "Node is not AnimationTree: " + node.get_class()}
	return {"tree": node}

func _handle_create_animation_tree(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	var animation_player_path = params.get("animation_player_path", "")
	var tree_name = params.get("tree_name", "AnimationTree")
	var with_state_machine = bool(params.get("with_state_machine", true))
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}

	var tree := AnimationTree.new()
	tree.name = tree_name
	if not animation_player_path.is_empty():
		# Stored as a NodePath relative to the tree itself, after parent.add_child
		# we'll set this so the path resolves properly.
		tree.anim_player = NodePath(animation_player_path)

	if with_state_machine:
		var sm := AnimationNodeStateMachine.new()
		tree.tree_root = sm

	var tracked = UndoHelper.add_node(parent, tree, "Add AnimationTree")
	return {
		"success": true,
		"node_path": str(tree.get_path()) if tree.get_parent() else "",
		"with_state_machine": with_state_machine,
		"undoable": tracked,
	}

func _handle_get_structure(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var info = {
		"node_path": str(tree.get_path()),
		"active": tree.active,
		"anim_player": str(tree.anim_player),
	}
	if tree.tree_root:
		info["root"] = _serialize_anim_node(tree.tree_root)
	else:
		info["root"] = null
	return info

func _serialize_anim_node(an: AnimationNode) -> Dictionary:
	var entry := {"class": an.get_class()}
	if an is AnimationNodeStateMachine:
		var sm: AnimationNodeStateMachine = an
		var states := []
		for sname in sm.get_node_list():
			var sub = sm.get_node(sname)
			states.append({
				"name": sname,
				"class": sub.get_class(),
				"position": TypeHelper.variant_to_json(sm.get_node_position(sname)),
			})
		var transitions := []
		for i in range(sm.get_transition_count()):
			transitions.append({
				"from": sm.get_transition_from(i),
				"to": sm.get_transition_to(i),
				"transition": _serialize_transition(sm.get_transition(i)),
			})
		entry["states"] = states
		entry["transitions"] = transitions
	elif an is AnimationNodeBlendTree:
		var bt: AnimationNodeBlendTree = an
		var nodes := []
		for nname in bt.get_node_list():
			var sub = bt.get_node(nname)
			nodes.append({
				"name": nname,
				"class": sub.get_class(),
				"position": TypeHelper.variant_to_json(bt.get_node_position(nname)),
			})
		entry["nodes"] = nodes
	elif an is AnimationNodeTransition:
		entry["xfade_time"] = an.xfade_time
	elif an is AnimationNodeAnimation:
		entry["animation"] = String(an.animation)
	return entry

func _serialize_transition(tr) -> Dictionary:
	# Godot 4.7+: AnimationNodeStateMachineTransition no longer extends AnimationNode
	var entry := {"class": tr.get_class()}
	if "xfade_time" in tr:
		entry["xfade_time"] = tr.xfade_time
	if "advance_condition" in tr:
		entry["advance_condition"] = String(tr.advance_condition) if tr.advance_condition else ""
	if "auto_advance" in tr:
		entry["auto_advance"] = tr.auto_advance
	if "switch_mode" in tr:
		entry["switch_mode"] = tr.switch_mode
	return entry

func _handle_add_state(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var sm = tree.tree_root
	if not sm is AnimationNodeStateMachine:
		return {"error": "tree_root is not a StateMachine"}
	var state_name = params.get("state_name", "")
	var animation_name = params.get("animation_name", "")
	var position = params.get("position", Vector2(0, 0))
	if state_name.is_empty():
		return {"error": "state_name is required"}
	var anim_node := AnimationNodeAnimation.new()
	if not animation_name.is_empty():
		anim_node.animation = animation_name

	var pos: Vector2 = TypeHelper.parse_godot_value(position, TYPE_VECTOR2)
	var do_call = func(): sm.add_node(state_name, anim_node, pos)
	var undo_call = func():
		if sm.has_node(state_name):
			sm.remove_node(state_name)
	var tracked = UndoHelper.do_simple("Add SM state %s" % state_name, do_call, undo_call)
	return {"success": true, "state_name": state_name, "undoable": tracked}

func _handle_remove_state(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var sm = tree.tree_root
	if not sm is AnimationNodeStateMachine:
		return {"error": "tree_root is not a StateMachine"}
	var state_name = params.get("state_name", "")
	if state_name.is_empty():
		return {"error": "state_name is required"}
	if not sm.has_node(state_name):
		return {"error": "State not found: " + state_name}
	# Best-effort: capture node + position for undo
	var anode = sm.get_node(state_name)
	var apos = sm.get_node_position(state_name)
	var do_call = func(): sm.remove_node(state_name)
	var undo_call = func(): sm.add_node(state_name, anode, apos)
	var tracked = UndoHelper.do_simple("Remove SM state %s" % state_name, do_call, undo_call)
	return {"success": true, "state_name": state_name, "undoable": tracked}

func _handle_add_transition(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var sm = tree.tree_root
	if not sm is AnimationNodeStateMachine:
		return {"error": "tree_root is not a StateMachine"}
	var from_state = params.get("from", "")
	var to_state = params.get("to", "")
	if from_state.is_empty() or to_state.is_empty():
		return {"error": "from and to are required"}
	var transition := AnimationNodeStateMachineTransition.new()
	transition.xfade_time = float(params.get("xfade_time", 0.0))
	if params.has("advance_condition"):
		transition.advance_condition = String(params["advance_condition"])
	if params.has("auto_advance"):
		transition.auto_advance = bool(params["auto_advance"])
	if params.has("switch_mode"):
		transition.switch_mode = int(params["switch_mode"])

	var do_call = func(): sm.add_transition(from_state, to_state, transition)
	var undo_call = func():
		var idx = _find_transition_index(sm, from_state, to_state)
		if idx >= 0:
			sm.remove_transition_by_index(idx)
	var tracked = UndoHelper.do_simple("Add SM transition", do_call, undo_call)
	return {"success": true, "from": from_state, "to": to_state, "undoable": tracked}

func _find_transition_index(sm: AnimationNodeStateMachine, from_s: String, to_s: String) -> int:
	for i in range(sm.get_transition_count()):
		if sm.get_transition_from(i) == from_s and sm.get_transition_to(i) == to_s:
			return i
	return -1

func _handle_remove_transition(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var sm = tree.tree_root
	if not sm is AnimationNodeStateMachine:
		return {"error": "tree_root is not a StateMachine"}
	var from_s = params.get("from", "")
	var to_s = params.get("to", "")
	if from_s.is_empty() or to_s.is_empty():
		return {"error": "from and to are required"}
	var idx = _find_transition_index(sm, from_s, to_s)
	if idx < 0:
		return {"error": "Transition not found"}
	var saved_transition = sm.get_transition(idx)
	var do_call = func(): sm.remove_transition_by_index(idx)
	var undo_call = func(): sm.add_transition(from_s, to_s, saved_transition)
	var tracked = UndoHelper.do_simple("Remove SM transition", do_call, undo_call)
	return {"success": true, "undoable": tracked}

func _handle_set_blend_tree_node(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var bt = tree.tree_root
	if not bt is AnimationNodeBlendTree:
		return {"error": "tree_root is not a BlendTree"}
	var node_name = params.get("name", "")
	var node_class = params.get("node_class", "AnimationNodeAdd2")
	var position = params.get("position", Vector2(0, 0))
	if node_name.is_empty():
		return {"error": "name is required"}
	if not ClassDB.class_exists(node_class):
		return {"error": "Unknown node_class: " + node_class}
	var instance = ClassDB.instantiate(node_class)
	if not instance is AnimationNode:
		return {"error": "Class is not an AnimationNode: " + node_class}

	# Apply optional properties
	var properties = params.get("properties", {})
	if properties is Dictionary:
		for key in properties.keys():
			if key in instance:
				var current = instance.get(key)
				instance.set(key, TypeHelper.parse_godot_value(properties[key], typeof(current)))

	var pos: Vector2 = TypeHelper.parse_godot_value(position, TYPE_VECTOR2)
	var do_call = func(): bt.add_node(node_name, instance, pos)
	var undo_call = func():
		if bt.has_node(node_name):
			bt.remove_node(node_name)
	var tracked = UndoHelper.do_simple("Add BlendTree node %s" % node_name, do_call, undo_call)
	return {"success": true, "name": node_name, "node_class": node_class, "undoable": tracked}

func _handle_set_tree_parameter(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var param_path = params.get("param_path", "")
	var value = params.get("value", null)
	if param_path.is_empty():
		return {"error": "param_path is required"}
	# Parameters live under "parameters/..." path
	var full_path = "parameters/" + param_path if not param_path.begins_with("parameters/") else param_path
	var old_value = tree.get(full_path)
	var converted = TypeHelper.parse_godot_value(value, typeof(old_value))
	var do_call = func(): tree.set(full_path, converted)
	var undo_call = func(): tree.set(full_path, old_value)
	var tracked = UndoHelper.do_simple("Set tree param %s" % param_path, do_call, undo_call)
	return {"success": true, "param_path": full_path, "undoable": tracked}

func _handle_travel(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var to_state = params.get("to", "")
	if to_state.is_empty():
		return {"error": "to is required"}
	# Travel goes through the playback object — runtime control, no undo
	var playback = tree.get("parameters/playback")
	if playback is AnimationNodeStateMachinePlayback:
		playback.travel(to_state)
		return {"success": true, "to": to_state, "undoable": false}
	return {"error": "No state machine playback parameter found"}

func _handle_set_active(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tree(params)
	if resolved.has("error"):
		return resolved
	var tree: AnimationTree = resolved["tree"]
	var active = bool(params.get("active", true))
	var tracked = UndoHelper.do_property(tree, "active", active, "Set AnimationTree.active")
	return {"success": true, "active": active, "undoable": tracked}
