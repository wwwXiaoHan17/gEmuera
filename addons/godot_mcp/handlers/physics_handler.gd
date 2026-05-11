@tool
extends McpHandler

# Detects 2D vs 3D from the target node and creates the matching
# CollisionShape*/CollisionPolygon* / shape resource. All shape creation
# routes through UndoHelper.add_node so the editor can undo cleanly.

const _2D_BODIES = ["StaticBody2D", "RigidBody2D", "CharacterBody2D", "Area2D", "AnimatableBody2D"]
const _3D_BODIES = ["StaticBody3D", "RigidBody3D", "CharacterBody3D", "Area3D", "AnimatableBody3D"]

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"setup_collision",
		"set_physics_layers",
		"get_physics_layers",
		"add_raycast",
		"setup_physics_body",
		"get_collision_info",
		"query_collision_at_point",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"setup_collision":
			return _handle_setup_collision(params)
		"set_physics_layers":
			return _handle_set_physics_layers(params)
		"get_physics_layers":
			return _handle_get_physics_layers(params)
		"add_raycast":
			return _handle_add_raycast(params)
		"setup_physics_body":
			return _handle_setup_physics_body(params)
		"get_collision_info":
			return _handle_get_collision_info(params)
		"query_collision_at_point":
			return _handle_query_collision_at_point(params)
		_:
			return {"error": "Unknown physics method: " + method}

func _is_2d_body(node: Node) -> bool:
	for cls in _2D_BODIES:
		if node.is_class(cls):
			return true
	return false

func _is_3d_body(node: Node) -> bool:
	for cls in _3D_BODIES:
		if node.is_class(cls):
			return true
	return false

func _handle_setup_collision(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var shape_type = String(params.get("shape_type", "box")).to_lower()
	var size = params.get("size", null)
	var collision_name = params.get("collision_name", "CollisionShape")
	if node_path.is_empty():
		return {"error": "node_path is required"}

	var body = get_target_node(node_path)
	if not body:
		return {"error": "Node not found: " + node_path}

	var is_2d = _is_2d_body(body)
	var is_3d = _is_3d_body(body)
	if not is_2d and not is_3d:
		return {"error": "Target node is not a 2D or 3D physics body: " + body.get_class()}

	var shape: Resource = null
	var dim = "2d" if is_2d else "3d"
	match shape_type:
		"box", "rectangle":
			if is_2d:
				var s := RectangleShape2D.new()
				if size != null:
					s.size = TypeHelper.parse_godot_value(size, TYPE_VECTOR2)
				else:
					s.size = Vector2(32, 32)
				shape = s
			else:
				var s := BoxShape3D.new()
				if size != null:
					s.size = TypeHelper.parse_godot_value(size, TYPE_VECTOR3)
				else:
					s.size = Vector3(1, 1, 1)
				shape = s
		"circle", "sphere":
			if is_2d:
				var s := CircleShape2D.new()
				s.radius = float(size) if size is float or size is int else 16.0
				shape = s
			else:
				var s := SphereShape3D.new()
				s.radius = float(size) if size is float or size is int else 0.5
				shape = s
		"capsule":
			if is_2d:
				var s := CapsuleShape2D.new()
				if size is Dictionary:
					s.radius = float(size.get("radius", 16))
					s.height = float(size.get("height", 64))
				shape = s
			else:
				var s := CapsuleShape3D.new()
				if size is Dictionary:
					s.radius = float(size.get("radius", 0.5))
					s.height = float(size.get("height", 2.0))
				shape = s
		"cylinder":
			if is_3d:
				var s := CylinderShape3D.new()
				if size is Dictionary:
					s.radius = float(size.get("radius", 0.5))
					s.height = float(size.get("height", 2.0))
				shape = s
			else:
				return {"error": "cylinder shape only valid for 3D"}
		_:
			return {"error": "Unknown shape_type: " + shape_type + " (supported: box, circle/sphere, capsule, cylinder)"}

	var collision_node: Node = null
	if is_2d:
		collision_node = CollisionShape2D.new()
	else:
		collision_node = CollisionShape3D.new()
	collision_node.shape = shape
	collision_node.name = collision_name

	var tracked = UndoHelper.add_node(body, collision_node, "Add CollisionShape")
	return {
		"success": true,
		"dim": dim,
		"shape_class": shape.get_class(),
		"node_path": str(collision_node.get_path()) if collision_node.get_parent() else "",
		"undoable": tracked,
	}

func _handle_set_physics_layers(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var layer = params.get("collision_layer", null)
	var mask = params.get("collision_mask", null)
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	if not ("collision_layer" in node and "collision_mask" in node):
		return {"error": "Node has no collision_layer/collision_mask: " + node.get_class()}

	var changes := []
	if layer != null:
		var t = UndoHelper.do_property(node, "collision_layer", int(layer), "Set collision_layer")
		changes.append({"property": "collision_layer", "value": int(layer), "undoable": t})
	if mask != null:
		var t = UndoHelper.do_property(node, "collision_mask", int(mask), "Set collision_mask")
		changes.append({"property": "collision_mask", "value": int(mask), "undoable": t})
	return {"success": true, "node_path": node_path, "changes": changes}

func _handle_get_physics_layers(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	var info = {}
	if "collision_layer" in node:
		info["collision_layer"] = node.collision_layer
	if "collision_mask" in node:
		info["collision_mask"] = node.collision_mask

	# Pull the human-readable layer names from project settings
	var layer_names_2d = []
	var layer_names_3d = []
	for i in range(1, 33):
		var key2d = "layer_names/2d_physics/layer_%d" % i
		var key3d = "layer_names/3d_physics/layer_%d" % i
		if ProjectSettings.has_setting(key2d):
			layer_names_2d.append({"index": i, "name": ProjectSettings.get_setting(key2d)})
		if ProjectSettings.has_setting(key3d):
			layer_names_3d.append({"index": i, "name": ProjectSettings.get_setting(key3d)})
	return {
		"node": info,
		"layer_names_2d": layer_names_2d,
		"layer_names_3d": layer_names_3d,
	}

func _handle_add_raycast(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	var dim = String(params.get("dim", "2d")).to_lower()
	var target = params.get("target", null)
	var raycast_name = params.get("raycast_name", "RayCast")
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}

	var ray: Node = null
	if dim == "2d":
		ray = RayCast2D.new()
		if target != null:
			ray.target_position = TypeHelper.parse_godot_value(target, TYPE_VECTOR2)
		else:
			ray.target_position = Vector2(0, 50)
	elif dim == "3d":
		ray = RayCast3D.new()
		if target != null:
			ray.target_position = TypeHelper.parse_godot_value(target, TYPE_VECTOR3)
		else:
			ray.target_position = Vector3(0, -1, 0)
	else:
		return {"error": "dim must be 2d or 3d"}
	ray.name = raycast_name
	ray.enabled = bool(params.get("enabled", true))

	var tracked = UndoHelper.add_node(parent, ray, "Add RayCast")
	return {
		"success": true,
		"raycast_class": ray.get_class(),
		"node_path": str(ray.get_path()) if ray.get_parent() else "",
		"undoable": tracked,
	}

func _handle_setup_physics_body(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	var body_type = String(params.get("body_type", "CharacterBody2D"))
	var body_name = params.get("body_name", body_type)
	var add_collision = bool(params.get("add_collision", true))
	var shape_type = String(params.get("shape_type", "box"))
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}
	if not ClassDB.class_exists(body_type):
		return {"error": "Unknown body_type: " + body_type}

	var body: Node = ClassDB.instantiate(body_type)
	if not body:
		return {"error": "Failed to instantiate: " + body_type}
	body.name = body_name

	var ur = UndoHelper.begin_batch("Setup physics body")
	var tracked := false
	if ur:
		ur.add_do_method(parent, "add_child", body, true)
		ur.add_do_property(body, "owner", _scene_owner(parent))
		ur.add_undo_method(parent, "remove_child", body)
		ur.add_do_reference(body)
		var col_node: Node = null
		if add_collision:
			col_node = _make_default_collision_for(body, shape_type)
			if col_node:
				ur.add_do_method(body, "add_child", col_node, true)
				ur.add_do_property(col_node, "owner", _scene_owner(parent))
				ur.add_undo_method(body, "remove_child", col_node)
				ur.add_do_reference(col_node)
		UndoHelper.commit_batch(ur)
		tracked = true
	else:
		parent.add_child(body)
		body.owner = _scene_owner(parent)
		if add_collision:
			var col_node = _make_default_collision_for(body, shape_type)
			if col_node:
				body.add_child(col_node)
				col_node.owner = _scene_owner(parent)

	return {
		"success": true,
		"body_path": str(body.get_path()),
		"undoable": tracked,
	}

func _scene_owner(parent: Node) -> Node:
	# Pick a sensible owner so nodes appear in the scene dock
	var ei = get_editor_interface()
	if ei and ei.get_edited_scene_root():
		return ei.get_edited_scene_root()
	return parent.owner if parent.owner else parent

func _make_default_collision_for(body: Node, shape_type: String) -> Node:
	var is_2d = _is_2d_body(body)
	var col: Node = CollisionShape2D.new() if is_2d else CollisionShape3D.new()
	var shape: Resource = null
	match shape_type.to_lower():
		"box", "rectangle":
			if is_2d:
				var s := RectangleShape2D.new(); s.size = Vector2(32, 32); shape = s
			else:
				var s := BoxShape3D.new(); s.size = Vector3(1, 1, 1); shape = s
		"sphere", "circle":
			if is_2d:
				var s := CircleShape2D.new(); s.radius = 16.0; shape = s
			else:
				var s := SphereShape3D.new(); s.radius = 0.5; shape = s
		"capsule":
			if is_2d:
				var s := CapsuleShape2D.new(); s.radius = 16.0; s.height = 48.0; shape = s
			else:
				var s := CapsuleShape3D.new(); s.radius = 0.5; s.height = 2.0; shape = s
		_:
			if is_2d:
				var s := RectangleShape2D.new(); s.size = Vector2(32, 32); shape = s
			else:
				var s := BoxShape3D.new(); s.size = Vector3(1, 1, 1); shape = s
	col.shape = shape
	col.name = "CollisionShape"
	return col

func _handle_get_collision_info(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var root = get_target_node(node_path) if not node_path.is_empty() else null
	if not root:
		var ei = get_editor_interface()
		root = ei.get_edited_scene_root() if ei else null
	if not root:
		return {"error": "No root or scene available"}

	var collisions = []
	_collect_collisions(root, collisions)
	return {"root": str(root.get_path()), "collisions": collisions, "count": collisions.size()}

func _collect_collisions(node: Node, into: Array) -> void:
	if _is_2d_body(node) or _is_3d_body(node):
		var entry = {
			"path": str(node.get_path()),
			"class": node.get_class(),
			"collision_layer": node.collision_layer if "collision_layer" in node else 0,
			"collision_mask": node.collision_mask if "collision_mask" in node else 0,
			"shapes": [],
		}
		for c in node.get_children():
			if c is CollisionShape2D and c.shape:
				entry["shapes"].append({"name": c.name, "class": c.shape.get_class()})
			elif c is CollisionShape3D and c.shape:
				entry["shapes"].append({"name": c.name, "class": c.shape.get_class()})
		into.append(entry)
	for child in node.get_children():
		_collect_collisions(child, into)

func _handle_query_collision_at_point(params: Dictionary) -> Dictionary:
	var dim = String(params.get("dim", "2d")).to_lower()
	var point = params.get("point", null)
	if point == null:
		return {"error": "point is required"}
	var ei = get_editor_interface()
	var scene_root = ei.get_edited_scene_root() if ei else null
	if not scene_root:
		return {"error": "No edited scene"}

	if dim == "2d":
		var pos: Vector2 = TypeHelper.parse_godot_value(point, TYPE_VECTOR2)
		var world = scene_root.get_world_2d() if scene_root.has_method("get_world_2d") else null
		if not world:
			# CanvasItem-based root: try to find one
			for c in scene_root.find_children("*", "CanvasItem", true, false):
				if c.has_method("get_world_2d"):
					world = c.get_world_2d()
					break
		if not world:
			return {"error": "No World2D available — point query requires running scene tree"}
		var space_state = world.direct_space_state
		var qp := PhysicsPointQueryParameters2D.new()
		qp.position = pos
		qp.collide_with_areas = bool(params.get("collide_with_areas", true))
		qp.collide_with_bodies = bool(params.get("collide_with_bodies", true))
		var hits = space_state.intersect_point(qp, int(params.get("max_results", 32)))
		return {"hits": hits, "count": hits.size()}
	elif dim == "3d":
		var pos3: Vector3 = TypeHelper.parse_godot_value(point, TYPE_VECTOR3)
		var world3 = scene_root.get_world_3d() if scene_root.has_method("get_world_3d") else null
		if not world3:
			for c in scene_root.find_children("*", "Node3D", true, false):
				if c.has_method("get_world_3d"):
					world3 = c.get_world_3d()
					break
		if not world3:
			return {"error": "No World3D available — point query requires running scene tree"}
		var space_state = world3.direct_space_state
		var qp := PhysicsPointQueryParameters3D.new()
		qp.position = pos3
		qp.collide_with_areas = bool(params.get("collide_with_areas", true))
		qp.collide_with_bodies = bool(params.get("collide_with_bodies", true))
		var hits = space_state.intersect_point(qp, int(params.get("max_results", 32)))
		return {"hits": hits, "count": hits.size()}
	return {"error": "dim must be 2d or 3d"}
