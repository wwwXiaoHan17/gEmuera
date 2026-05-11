@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"delete_node",
		"rename_node",
		"duplicate_node",
		"move_node",
		"get_node_properties",
		"update_property",
		"connect_signal",
		"disconnect_signal",
		"get_signals",
		"add_resource",
		"set_anchor_preset",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"delete_node":
			return _handle_delete_node(params)
		"rename_node":
			return _handle_rename_node(params)
		"duplicate_node":
			return _handle_duplicate_node(params)
		"move_node":
			return _handle_move_node(params)
		"get_node_properties":
			return _handle_get_node_properties(params)
		"update_property":
			return _handle_update_property(params)
		"connect_signal":
			return _handle_connect_signal(params)
		"disconnect_signal":
			return _handle_disconnect_signal(params)
		"get_signals":
			return _handle_get_signals(params)
		"add_resource":
			return _handle_add_resource(params)
		"set_anchor_preset":
			return _handle_set_anchor_preset(params)
		_:
			return {"error": "Unknown node method: " + method}

# Forwarder kept for backward compatibility within this file.
# Prefer `get_target_node()` from base_handler.gd everywhere.
func _get_target_node(node_path: String) -> Node:
	return get_target_node(node_path)

func _handle_delete_node(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var tracked = UndoHelper.remove_node(node, "Delete " + node.name)
	return {"success": true, "deleted": node_path, "undoable": tracked}

func _handle_rename_node(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var new_name = params.get("new_name", "")
	if node_path.is_empty() or new_name.is_empty():
		return {"error": "node_path and new_name are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var tracked = UndoHelper.rename_node(node, new_name)
	return {"success": true, "renamed_to": new_name, "undoable": tracked}

func _handle_duplicate_node(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var parent = node.get_parent()
	if not parent:
		return {"error": "Cannot duplicate root node"}

	var dup = node.duplicate(Node.DUPLICATE_SIGNALS | Node.DUPLICATE_GROUPS | Node.DUPLICATE_SCRIPTS)
	if not dup:
		return {"error": "Failed to duplicate node"}

	var tracked = UndoHelper.add_node(parent, dup, "Duplicate " + node.name)
	return {
		"success": true,
		"duplicate_path": str(parent.get_path_to(dup)),
		"undoable": tracked,
	}

func _handle_move_node(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var new_parent_path = params.get("new_parent_path", "")
	if node_path.is_empty() or new_parent_path.is_empty():
		return {"error": "node_path and new_parent_path are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var new_parent = get_target_node(new_parent_path)
	if not new_parent:
		return {"error": "New parent not found: " + new_parent_path}

	if node == new_parent or new_parent.is_ancestor_of(node) == false and node.is_ancestor_of(new_parent):
		return {"error": "Cannot reparent into descendant"}

	var tracked = UndoHelper.reparent_node(node, new_parent)
	# Reassign owner recursively for child nodes (UndoHelper handles the
	# top-level node only; children may have lost owner during remove/add).
	var root = get_edited_scene_root()
	if root:
		_reassign_owner_recursive(node, root)
	return {
		"success": true,
		"new_path": str(new_parent.get_path_to(node)),
		"undoable": tracked,
	}

func _reassign_owner_recursive(node: Node, owner_node: Node) -> void:
	for child in node.get_children():
		if child.owner == null or child.owner != owner_node:
			child.owner = owner_node
		_reassign_owner_recursive(child, owner_node)

func _handle_get_node_properties(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var include_internal = params.get("include_internal", false)
	var props = {}
	var prop_list = node.get_property_list()
	for prop in prop_list:
		var usage = prop.usage
		if (usage & PROPERTY_USAGE_EDITOR) or (include_internal and (usage & PROPERTY_USAGE_STORAGE)):
			var pname = prop.name
			var value = node.get(pname)
			props[pname] = {
				"value": TypeHelper.variant_to_json(value),
				"type": prop.type,
				"class_name": prop.get("class_name", "")
			}
	return {"node_path": node_path, "properties": props}

# Forwarders preserved so any external callers (or unit tests) that
# reach into these private helpers via reflection keep working.
func _variant_to_json(value: Variant) -> Variant:
	return TypeHelper.variant_to_json(value)

func _json_to_variant(value: Variant, target_type: int = TYPE_NIL, class_hint: String = "") -> Variant:
	return TypeHelper.parse_godot_value(value, target_type, class_hint)

func _handle_update_property(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var property = params.get("property", "")
	var value = params.get("value", null)

	if node_path.is_empty() or property.is_empty():
		return {"error": "node_path and property are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	if not property in node:
		return {"error": "Property not found on node: " + property}

	var existing = node.get(property)
	var target_type = typeof(existing)
	var class_hint = ""
	if existing is Object and existing != null:
		class_hint = existing.get_class()

	var converted = TypeHelper.parse_godot_value(value, target_type, class_hint)
	var tracked = UndoHelper.do_property(node, property, converted, "Set " + node.name + "." + property)

	return {
		"success": true,
		"property": property,
		"value": TypeHelper.variant_to_json(node.get(property)),
		"undoable": tracked,
	}

func _handle_connect_signal(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var signal_name = params.get("signal_name", "")
	var target_path = params.get("target_node_path", "")
	var method_name = params.get("method_name", "")

	if node_path.is_empty() or signal_name.is_empty() or target_path.is_empty() or method_name.is_empty():
		return {"error": "node_path, signal_name, target_node_path, and method_name are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var target = get_target_node(target_path)
	if not target:
		return {"error": "Target node not found: " + target_path}

	if not node.has_signal(signal_name):
		return {"error": "Signal not found on node: " + signal_name}

	var flags = params.get("flags", 0)
	var callable = Callable(target, method_name)
	if node.is_connected(signal_name, callable):
		return {"error": "Signal already connected: " + signal_name + " -> " + method_name}

	var do_call = func(): node.connect(signal_name, callable, flags)
	var undo_call = func(): node.disconnect(signal_name, callable)
	var tracked = UndoHelper.do_simple("Connect " + signal_name, do_call, undo_call)

	return {
		"success": true,
		"connected": signal_name + " -> " + method_name,
		"undoable": tracked,
	}

func _handle_disconnect_signal(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var signal_name = params.get("signal_name", "")
	var target_path = params.get("target_node_path", "")
	var method_name = params.get("method_name", "")

	if node_path.is_empty() or signal_name.is_empty() or target_path.is_empty() or method_name.is_empty():
		return {"error": "node_path, signal_name, target_node_path, and method_name are required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var target = get_target_node(target_path)
	if not target:
		return {"error": "Target node not found: " + target_path}

	var callable = Callable(target, method_name)
	if not node.is_connected(signal_name, callable):
		return {"error": "Signal not connected: " + signal_name + " -> " + method_name}

	var do_call = func(): node.disconnect(signal_name, callable)
	var undo_call = func(): node.connect(signal_name, callable)
	var tracked = UndoHelper.do_simple("Disconnect " + signal_name, do_call, undo_call)

	return {
		"success": true,
		"disconnected": signal_name + " -> " + method_name,
		"undoable": tracked,
	}

func _handle_get_signals(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}

	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var signals = []
	var signal_list = node.get_signal_list()
	for sig in signal_list:
		var connections = node.get_signal_connection_list(sig.name)
		var conns = []
		for c in connections:
			var target_obj = c.callable.get_object()
			conns.append({
				"target": target_obj.name if target_obj else "",
				"method": c.callable.get_method(),
				"flags": c.flags
			})
		signals.append({
			"name": sig.name,
			"args": sig.args,
			"connections": conns
		})

	return {"node_path": node_path, "signals": signals}

# Construct a fresh Resource and assign it to a node property. Common use cases:
# CollisionShape2D.shape, Sprite2D.texture, AudioStreamPlayer.stream, etc.
func _handle_add_resource(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var property = String(params.get("property", "shape"))
	var resource_class = String(params.get("resource_class", ""))
	if node_path.is_empty() or resource_class.is_empty():
		return {"error": "node_path and resource_class are required"}
	var node := get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	if not property in node:
		return {"error": "Node has no property: " + property}
	if not ClassDB.class_exists(resource_class):
		return {"error": "Class does not exist: " + resource_class}
	var instance = ClassDB.instantiate(resource_class)
	if not (instance is Resource):
		return {"error": "Class is not a Resource: " + resource_class}

	# Apply optional initial properties on the resource.
	var properties: Dictionary = params.get("properties", {})
	if properties is Dictionary:
		for key in properties.keys():
			if key in instance:
				var current = instance.get(key)
				instance.set(key, TypeHelper.parse_godot_value(properties[key], typeof(current)))

	var tracked := UndoHelper.do_property(node, property, instance,
		"Add %s on %s.%s" % [resource_class, node.name, property])
	return {
		"success": true,
		"node_path": node_path,
		"property": property,
		"resource_class": resource_class,
		"undoable": tracked,
	}

# Control.set_anchors_preset wrapper. Accepts either an int (Godot's preset
# enum value) or a friendly string ("center", "full_rect", etc.).
const _ANCHOR_PRESETS := {
	"top_left": Control.PRESET_TOP_LEFT,
	"top_right": Control.PRESET_TOP_RIGHT,
	"bottom_left": Control.PRESET_BOTTOM_LEFT,
	"bottom_right": Control.PRESET_BOTTOM_RIGHT,
	"center_left": Control.PRESET_CENTER_LEFT,
	"center_top": Control.PRESET_CENTER_TOP,
	"center_right": Control.PRESET_CENTER_RIGHT,
	"center_bottom": Control.PRESET_CENTER_BOTTOM,
	"center": Control.PRESET_CENTER,
	"left_wide": Control.PRESET_LEFT_WIDE,
	"top_wide": Control.PRESET_TOP_WIDE,
	"right_wide": Control.PRESET_RIGHT_WIDE,
	"bottom_wide": Control.PRESET_BOTTOM_WIDE,
	"vcenter_wide": Control.PRESET_VCENTER_WIDE,
	"hcenter_wide": Control.PRESET_HCENTER_WIDE,
	"full_rect": Control.PRESET_FULL_RECT,
}

func _handle_set_anchor_preset(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node := get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	if not (node is Control):
		return {"error": "Node is not a Control: " + node.get_class()}

	var preset_value = params.get("preset", 0)
	var preset_int: int
	if preset_value is String:
		var key := String(preset_value).to_lower()
		if not _ANCHOR_PRESETS.has(key):
			return {
				"error": "Unknown preset name: " + key,
				"available": _ANCHOR_PRESETS.keys(),
			}
		preset_int = int(_ANCHOR_PRESETS[key])
	else:
		preset_int = int(preset_value)

	var keep_offsets := bool(params.get("keep_offsets", false))
	# Snapshot the four anchors + offsets so we can build a precise undo entry.
	var ctrl := node as Control
	var old_anchor_left: float = ctrl.anchor_left
	var old_anchor_top: float = ctrl.anchor_top
	var old_anchor_right: float = ctrl.anchor_right
	var old_anchor_bottom: float = ctrl.anchor_bottom
	var old_offset_left: float = ctrl.offset_left
	var old_offset_top: float = ctrl.offset_top
	var old_offset_right: float = ctrl.offset_right
	var old_offset_bottom: float = ctrl.offset_bottom

	var ur := UndoHelper.begin_batch("Set anchor preset on " + node.name)
	if ur:
		ur.add_do_method(node, "set_anchors_preset", preset_int, keep_offsets)
		ur.add_undo_property(node, "anchor_left", old_anchor_left)
		ur.add_undo_property(node, "anchor_top", old_anchor_top)
		ur.add_undo_property(node, "anchor_right", old_anchor_right)
		ur.add_undo_property(node, "anchor_bottom", old_anchor_bottom)
		ur.add_undo_property(node, "offset_left", old_offset_left)
		ur.add_undo_property(node, "offset_top", old_offset_top)
		ur.add_undo_property(node, "offset_right", old_offset_right)
		ur.add_undo_property(node, "offset_bottom", old_offset_bottom)
		UndoHelper.commit_batch(ur)
	else:
		node.set_anchors_preset(preset_int, keep_offsets)

	return {
		"success": true,
		"node_path": node_path,
		"preset": preset_int,
		"undoable": ur != null,
	}
