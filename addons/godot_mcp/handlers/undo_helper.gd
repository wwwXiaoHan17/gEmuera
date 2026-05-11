@tool
class_name UndoHelper

# Wraps EditorUndoRedoManager so all destructive MCP operations can be
# rolled back via the editor's standard Undo (Ctrl+Z). Falls through to
# direct execution if the editor manager is unavailable (e.g. CLI mode).
#
# Returns true when the operation was tracked in the undo stack,
# false when it was executed directly without tracking.

static func _undo_redo() -> EditorUndoRedoManager:
	if Engine.has_singleton("EditorInterface"):
		var ei: EditorInterface = Engine.get_singleton("EditorInterface")
		return ei.get_editor_undo_redo()
	return null

static func _scene_context() -> Object:
	if Engine.has_singleton("EditorInterface"):
		var ei: EditorInterface = Engine.get_singleton("EditorInterface")
		return ei.get_edited_scene_root()
	return null

static func do_simple(label: String, do_call: Callable, undo_call: Callable) -> bool:
	var ur = _undo_redo()
	if not ur:
		do_call.call()
		return false
	ur.create_action(label, UndoRedo.MERGE_DISABLE, _scene_context())
	# Godot 4.7+ requires object + method string instead of Callable.
	var do_obj := do_call.get_object()
	var do_meth := do_call.get_method()
	var undo_obj := undo_call.get_object()
	var undo_meth := undo_call.get_method()
	var do_binds := do_call.get_bound_arguments()
	var undo_binds := undo_call.get_bound_arguments()
	if do_binds.is_empty():
		ur.add_do_method(do_obj, do_meth)
	else:
		ur.add_do_method(do_obj, do_meth, do_binds)
	if undo_binds.is_empty():
		ur.add_undo_method(undo_obj, undo_meth)
	else:
		ur.add_undo_method(undo_obj, undo_meth, undo_binds)
	ur.commit_action()
	return true

static func do_property(node: Node, property: String, new_value: Variant, label: String = "") -> bool:
	var ur = _undo_redo()
	if not ur:
		node.set(property, new_value)
		return false
	var old_value = node.get(property)
	if label.is_empty():
		label = "Set %s.%s" % [node.name, property]
	ur.create_action(label, UndoRedo.MERGE_DISABLE, _scene_context())
	ur.add_do_property(node, property, new_value)
	ur.add_undo_property(node, property, old_value)
	ur.commit_action()
	return true

static func add_node(parent: Node, child: Node, label: String = "") -> bool:
	var ur = _undo_redo()
	var owner_root = _scene_context()
	if label.is_empty():
		label = "Add " + child.get_class()
	if not ur:
		parent.add_child(child)
		if owner_root and child != owner_root:
			child.owner = owner_root
		return false
	ur.create_action(label, UndoRedo.MERGE_DISABLE, owner_root)
	ur.add_do_method(parent, "add_child", child)
	if owner_root and child != owner_root:
		ur.add_do_method(child, "set_owner", owner_root)
	ur.add_do_reference(child)
	ur.add_undo_method(parent, "remove_child", child)
	ur.commit_action()
	return true

static func remove_node(node: Node, label: String = "") -> bool:
	var parent = node.get_parent()
	if not parent:
		return false
	var ur = _undo_redo()
	var owner_root = _scene_context()
	if label.is_empty():
		label = "Remove " + node.get_class()
	if not ur:
		parent.remove_child(node)
		node.queue_free()
		return false
	ur.create_action(label, UndoRedo.MERGE_DISABLE, owner_root)
	ur.add_do_method(parent, "remove_child", node)
	ur.add_undo_method(parent, "add_child", node)
	if owner_root and node != owner_root:
		ur.add_undo_method(node, "set_owner", owner_root)
	ur.add_undo_reference(node)
	ur.commit_action()
	return true

static func reparent_node(node: Node, new_parent: Node, label: String = "") -> bool:
	var old_parent = node.get_parent()
	if not old_parent:
		return add_node(new_parent, node, label)
	if old_parent == new_parent:
		return false
	var ur = _undo_redo()
	var owner_root = _scene_context()
	if label.is_empty():
		label = "Reparent " + node.get_class()
	if not ur:
		old_parent.remove_child(node)
		new_parent.add_child(node)
		if owner_root and node != owner_root:
			node.owner = owner_root
		return false
	ur.create_action(label, UndoRedo.MERGE_DISABLE, owner_root)
	ur.add_do_method(old_parent, "remove_child", node)
	ur.add_do_method(new_parent, "add_child", node)
	if owner_root and node != owner_root:
		ur.add_do_method(node, "set_owner", owner_root)
	ur.add_undo_method(new_parent, "remove_child", node)
	ur.add_undo_method(old_parent, "add_child", node)
	if owner_root and node != owner_root:
		ur.add_undo_method(node, "set_owner", owner_root)
	ur.commit_action()
	return true

static func rename_node(node: Node, new_name: String, label: String = "") -> bool:
	var ur = _undo_redo()
	var old_name = node.name
	if label.is_empty():
		label = "Rename %s -> %s" % [old_name, new_name]
	if not ur:
		node.name = new_name
		return false
	ur.create_action(label, UndoRedo.MERGE_DISABLE, _scene_context())
	ur.add_do_property(node, "name", new_name)
	ur.add_undo_property(node, "name", old_name)
	ur.commit_action()
	return true

# Open a new action block for grouping multiple operations under a single Undo step.
# Caller must call commit_batch() (or rollback_batch via has_action()) when done.
static func begin_batch(label: String) -> EditorUndoRedoManager:
	var ur = _undo_redo()
	if ur:
		ur.create_action(label, UndoRedo.MERGE_DISABLE, _scene_context())
	return ur

static func commit_batch(ur: EditorUndoRedoManager) -> void:
	if ur:
		ur.commit_action()
