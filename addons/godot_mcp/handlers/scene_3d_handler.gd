@tool
extends McpHandler

# 3D scene helpers: meshes, lights, materials, environment, camera, gridmap, transforms.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"add_mesh_instance",
		"setup_lighting",
		"set_material_3d",
		"setup_environment",
		"setup_camera_3d",
		"add_gridmap",
		"set_transform_3d",
		"import_glb",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"add_mesh_instance":
			return _handle_add_mesh_instance(params)
		"setup_lighting":
			return _handle_setup_lighting(params)
		"set_material_3d":
			return _handle_set_material_3d(params)
		"setup_environment":
			return _handle_setup_environment(params)
		"setup_camera_3d":
			return _handle_setup_camera_3d(params)
		"add_gridmap":
			return _handle_add_gridmap(params)
		"set_transform_3d":
			return _handle_set_transform_3d(params)
		"import_glb":
			return _handle_import_glb(params)
		_:
			return {"error": "Unknown 3D scene method: " + method}

func _scene_owner(parent: Node) -> Node:
	var ei = get_editor_interface()
	if ei and ei.get_edited_scene_root():
		return ei.get_edited_scene_root()
	return parent.owner if parent.owner else parent

func _handle_add_mesh_instance(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	var mesh_type = String(params.get("mesh_type", "box")).to_lower()
	var instance_name = params.get("instance_name", "MeshInstance3D")
	var mesh_resource_path = params.get("mesh_resource", "")
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}

	var mesh: Mesh = null
	if not String(mesh_resource_path).is_empty():
		var p = String(mesh_resource_path)
		if not p.begins_with("res://"):
			p = "res://" + p
		var loaded = ResourceLoader.load(p)
		if loaded is Mesh:
			mesh = loaded
		else:
			return {"error": "mesh_resource is not a Mesh: " + p}
	else:
		match mesh_type:
			"box", "cube":
				mesh = BoxMesh.new()
			"sphere":
				mesh = SphereMesh.new()
			"cylinder":
				mesh = CylinderMesh.new()
			"capsule":
				mesh = CapsuleMesh.new()
			"plane":
				mesh = PlaneMesh.new()
			"prism":
				mesh = PrismMesh.new()
			"quad":
				mesh = QuadMesh.new()
			"torus":
				mesh = TorusMesh.new()
			_:
				return {"error": "Unknown mesh_type: " + mesh_type}

	var node := MeshInstance3D.new()
	node.name = instance_name
	node.mesh = mesh

	# Optional initial transform
	var transform = params.get("transform", null)
	if transform != null:
		node.transform = TypeHelper.parse_godot_value(transform, TYPE_TRANSFORM3D)

	var tracked = UndoHelper.add_node(parent, node, "Add MeshInstance3D")
	return {
		"success": true,
		"node_path": str(node.get_path()) if node.get_parent() else "",
		"mesh_class": mesh.get_class(),
		"undoable": tracked,
	}

func _handle_setup_lighting(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	var preset = String(params.get("preset", "sun")).to_lower()
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}

	var ur = UndoHelper.begin_batch("Setup lighting (%s)" % preset)
	var added = []

	match preset:
		"sun":
			var sun := DirectionalLight3D.new()
			sun.name = "Sun"
			sun.light_energy = 1.2
			sun.shadow_enabled = true
			sun.rotation = Vector3(deg_to_rad(-50), deg_to_rad(-30), 0)
			added.append(sun)
		"indoor":
			var key := OmniLight3D.new()
			key.name = "KeyLight"
			key.light_energy = 1.0
			key.position = Vector3(2, 3, 2)
			key.shadow_enabled = true
			added.append(key)
			var fill := OmniLight3D.new()
			fill.name = "FillLight"
			fill.light_energy = 0.4
			fill.position = Vector3(-2, 2, -1)
			added.append(fill)
		"dramatic":
			var spot := SpotLight3D.new()
			spot.name = "Spot"
			spot.light_energy = 2.5
			spot.spot_angle = 30.0
			spot.position = Vector3(0, 4, 2)
			spot.rotation = Vector3(deg_to_rad(-60), 0, 0)
			spot.shadow_enabled = true
			added.append(spot)
			var rim := DirectionalLight3D.new()
			rim.name = "RimLight"
			rim.light_energy = 0.6
			rim.rotation = Vector3(deg_to_rad(-15), deg_to_rad(150), 0)
			added.append(rim)
		_:
			return {"error": "Unknown lighting preset: " + preset + " (sun, indoor, dramatic)"}

	if ur:
		for n in added:
			ur.add_do_method(parent, "add_child", n, true)
			ur.add_do_property(n, "owner", _scene_owner(parent))
			ur.add_undo_method(parent, "remove_child", n)
			ur.add_do_reference(n)
		UndoHelper.commit_batch(ur)
		return {
			"success": true,
			"preset": preset,
			"lights": added.map(func(n): return n.name),
			"undoable": true,
		}
	else:
		for n in added:
			parent.add_child(n)
			n.owner = _scene_owner(parent)
		return {
			"success": true,
			"preset": preset,
			"lights": added.map(func(n): return n.name),
			"undoable": false,
		}

func _handle_set_material_3d(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var properties = params.get("properties", {})
	var material_property = String(params.get("material_property", "material_override"))
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}

	var existing = node.get(material_property) if material_property in node else null
	var mat: StandardMaterial3D = null
	if existing is StandardMaterial3D:
		mat = existing.duplicate()
	else:
		mat = StandardMaterial3D.new()

	for key in properties.keys():
		if key in mat:
			var current = mat.get(key)
			var target = typeof(current)
			var hint = ""
			if current is Object and current != null:
				hint = current.get_class()
			mat.set(key, TypeHelper.parse_godot_value(properties[key], target, hint))

	var tracked = UndoHelper.do_property(node, material_property, mat, "Set 3D material")
	return {
		"success": true,
		"node_path": node_path,
		"material_property": material_property,
		"undoable": tracked,
	}

func _handle_setup_environment(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	var preset = String(params.get("preset", "default"))
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}

	var env := Environment.new()
	match preset.to_lower():
		"sky":
			env.background_mode = Environment.BG_SKY
			var sky := Sky.new()
			var pks := ProceduralSkyMaterial.new()
			sky.sky_material = pks
			env.sky = sky
		"clear":
			env.background_mode = Environment.BG_COLOR
			env.background_color = Color(0.1, 0.1, 0.15)
		"foggy":
			env.background_mode = Environment.BG_COLOR
			env.background_color = Color(0.6, 0.6, 0.7)
			env.fog_enabled = true
			env.fog_density = 0.04
		"dramatic":
			env.background_mode = Environment.BG_COLOR
			env.background_color = Color(0.0, 0.0, 0.05)
			env.glow_enabled = true
			env.glow_intensity = 1.2
			env.ssao_enabled = true
			env.ssr_enabled = true
		"default", _:
			env.background_mode = Environment.BG_SKY
			var sky := Sky.new()
			sky.sky_material = ProceduralSkyMaterial.new()
			env.sky = sky

	# Override fields from caller
	var overrides = params.get("environment", {})
	if overrides is Dictionary:
		for key in overrides.keys():
			if key in env:
				var current = env.get(key)
				var target = typeof(current)
				env.set(key, TypeHelper.parse_godot_value(overrides[key], target))

	var node := WorldEnvironment.new()
	node.name = params.get("name", "WorldEnvironment")
	node.environment = env

	var tracked = UndoHelper.add_node(parent, node, "Setup environment")
	return {
		"success": true,
		"preset": preset,
		"node_path": str(node.get_path()) if node.get_parent() else "",
		"undoable": tracked,
	}

func _handle_setup_camera_3d(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}

	var cam := Camera3D.new()
	cam.name = params.get("camera_name", "Camera3D")
	cam.fov = float(params.get("fov", 75.0))
	cam.near = float(params.get("near", 0.05))
	cam.far = float(params.get("far", 4000.0))
	cam.current = bool(params.get("current", true))

	if params.has("transform"):
		cam.transform = TypeHelper.parse_godot_value(params["transform"], TYPE_TRANSFORM3D)
	else:
		var pos = params.get("position", null)
		var look_at = params.get("look_at", null)
		if pos != null:
			cam.position = TypeHelper.parse_godot_value(pos, TYPE_VECTOR3)
		else:
			cam.position = Vector3(0, 2, 5)
		if look_at != null:
			cam.look_at(TypeHelper.parse_godot_value(look_at, TYPE_VECTOR3))

	var tracked = UndoHelper.add_node(parent, cam, "Setup camera 3D")
	return {
		"success": true,
		"node_path": str(cam.get_path()) if cam.get_parent() else "",
		"undoable": tracked,
	}

func _handle_add_gridmap(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}
	if not ClassDB.class_exists("GridMap"):
		return {"error": "GridMap class not available"}

	var gm = ClassDB.instantiate("GridMap")
	gm.name = params.get("gridmap_name", "GridMap")

	var library_path = String(params.get("mesh_library", ""))
	if not library_path.is_empty():
		if not library_path.begins_with("res://"):
			library_path = "res://" + library_path
		var lib = ResourceLoader.load(library_path)
		if lib and "mesh_library" in gm:
			gm.mesh_library = lib

	if params.has("cell_size"):
		gm.cell_size = TypeHelper.parse_godot_value(params["cell_size"], TYPE_VECTOR3)

	var tracked = UndoHelper.add_node(parent, gm, "Add GridMap")

	# Optional initial cells: [{x,y,z, item}]
	var cells = params.get("cells", [])
	if cells is Array:
		for c in cells:
			if c is Dictionary and c.has("item"):
				gm.set_cell_item(Vector3i(int(c.get("x", 0)), int(c.get("y", 0)), int(c.get("z", 0))), int(c["item"]))

	return {
		"success": true,
		"node_path": str(gm.get_path()) if gm.get_parent() else "",
		"undoable": tracked,
	}

func _handle_set_transform_3d(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	if not (node is Node3D):
		return {"error": "Node is not Node3D: " + node.get_class()}

	if params.has("transform"):
		var t = TypeHelper.parse_godot_value(params["transform"], TYPE_TRANSFORM3D)
		var tracked = UndoHelper.do_property(node, "transform", t, "Set transform")
		return {"success": true, "undoable": tracked}

	# Per-component update
	var changes = []
	for prop in ["position", "rotation", "rotation_degrees", "scale"]:
		if params.has(prop):
			var current = node.get(prop)
			var target_type = typeof(current)
			var v = TypeHelper.parse_godot_value(params[prop], target_type)
			var t = UndoHelper.do_property(node, prop, v, "Set " + prop)
			changes.append({"property": prop, "undoable": t})
	if changes.is_empty():
		return {"error": "Provide transform or any of position/rotation/rotation_degrees/scale"}
	return {"success": true, "changes": changes}

func _handle_import_glb(params: Dictionary) -> Dictionary:
	var glb_path = String(params.get("glb_path", ""))
	var parent_path = params.get("parent_path", "")
	if glb_path.is_empty() or parent_path.is_empty():
		return {"error": "glb_path and parent_path are required"}
	var parent = get_target_node(parent_path)
	if not parent:
		return {"error": "Node not found: " + parent_path}
	if not glb_path.begins_with("res://") and not glb_path.begins_with("user://") and not glb_path.is_absolute_path():
		glb_path = "res://" + glb_path

	# Importing .glb at runtime via GLTFDocument
	if FileAccess.file_exists(glb_path):
		var doc = GLTFDocument.new()
		var state = GLTFState.new()
		var err = doc.append_from_file(glb_path, state)
		if err != OK:
			return {"error": "GLTF import failed: " + str(err)}
		var scene = doc.generate_scene(state)
		if not scene:
			return {"error": "GLTF generated null scene"}
		scene.name = params.get("instance_name", scene.name)
		var tracked = UndoHelper.add_node(parent, scene, "Import GLB")
		return {
			"success": true,
			"node_path": str(scene.get_path()) if scene.get_parent() else "",
			"undoable": tracked,
		}
	# Otherwise treat as a Godot resource (.tscn / .glb already imported)
	if ResourceLoader.exists(glb_path):
		var loaded = ResourceLoader.load(glb_path)
		if loaded is PackedScene:
			var instance = loaded.instantiate()
			if instance:
				var tracked = UndoHelper.add_node(parent, instance, "Instance imported scene")
				return {
					"success": true,
					"node_path": str(instance.get_path()) if instance.get_parent() else "",
					"undoable": tracked,
				}
	return {"error": "GLB/scene not found at: " + glb_path}
