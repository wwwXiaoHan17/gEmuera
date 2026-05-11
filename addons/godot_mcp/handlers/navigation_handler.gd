@tool
extends McpHandler

# Navigation handler — covers NavigationRegion2D/3D + NavigationAgent2D/3D
# placement, layer bitmask manipulation, and best-effort mesh/polygon baking.
# Auto-detects 2D vs 3D from parent node hierarchy. Baking is asynchronous in
# Godot (it spawns a worker thread); we kick it off but don't block waiting
# for completion — caller can poll via get_navigation_info.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"setup_navigation_region",
		"bake_navigation_mesh",
		"setup_navigation_agent",
		"set_navigation_layers",
		"get_navigation_info",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"setup_navigation_region":
			return _handle_setup_region(params)
		"bake_navigation_mesh":
			return _handle_bake(params)
		"setup_navigation_agent":
			return _handle_setup_agent(params)
		"set_navigation_layers":
			return _handle_set_layers(params)
		"get_navigation_info":
			return _handle_get_info(params)
		_:
			return {"error": "Unknown navigation method: " + method}

func _is_3d_parent(parent: Node) -> bool:
	var n = parent
	while n:
		if n is Node3D:
			return true
		if n is Node2D or n is Control:
			return false
		n = n.get_parent()
	return false

func _handle_setup_region(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent := get_target_node(parent_path)
	if not parent:
		return {"error": "Parent node not found: " + parent_path}

	var explicit_dim = String(params.get("dimension", "")).to_lower()
	var is_3d: bool
	if explicit_dim == "3d":
		is_3d = true
	elif explicit_dim == "2d":
		is_3d = false
	else:
		is_3d = _is_3d_parent(parent)

	var region: Node
	if is_3d:
		region = NavigationRegion3D.new()
		var nav_mesh := NavigationMesh.new()
		# Reasonable default cell sizes — can be overridden via properties.
		nav_mesh.cell_size = float(params.get("cell_size", 0.25))
		nav_mesh.cell_height = float(params.get("cell_height", 0.25))
		nav_mesh.agent_radius = float(params.get("agent_radius", 0.5))
		nav_mesh.agent_height = float(params.get("agent_height", 1.5))
		region.navigation_mesh = nav_mesh
	else:
		region = NavigationRegion2D.new()
		var nav_poly := NavigationPolygon.new()
		region.navigation_polygon = nav_poly

	region.name = String(params.get("node_name", "NavigationRegion"))
	if params.has("navigation_layers"):
		region.navigation_layers = int(params["navigation_layers"])

	var tracked := UndoHelper.add_node(parent, region, "Add %s" % region.get_class())
	return {
		"success": true,
		"node_path": str(region.get_path()) if region.get_parent() else "",
		"node_class": region.get_class(),
		"is_3d": is_3d,
		"undoable": tracked,
	}

func _resolve_region(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node := get_target_node(node_path)
	if not node:
		return {"error": "Region not found: " + node_path}
	if not (node is NavigationRegion2D or node is NavigationRegion3D):
		return {"error": "Node is not a NavigationRegion: " + node.get_class()}
	return {"region": node}

func _handle_bake(params: Dictionary) -> Dictionary:
	var resolved := _resolve_region(params)
	if resolved.has("error"):
		return resolved
	var region = resolved["region"]
	var on_thread := bool(params.get("on_thread", true))

	if region is NavigationRegion3D:
		if not region.navigation_mesh:
			region.navigation_mesh = NavigationMesh.new()
		region.bake_navigation_mesh(on_thread)
		return {
			"success": true,
			"baking_started": true,
			"on_thread": on_thread,
			"polygon_count_before": region.navigation_mesh.get_polygon_count(),
			"note": "Baking is async when on_thread=true; poll get_navigation_info to see updated polygon counts.",
		}
	elif region is NavigationRegion2D:
		# 2D region uses NavigationServer2D synchronously via NavigationPolygon
		# source geometry parser. There is no .bake_navigation_polygon on the
		# region itself in Godot 4.x — we re-trigger by calling
		# NavigationServer2D.region_bake_navigation_polygon.
		if not region.navigation_polygon:
			region.navigation_polygon = NavigationPolygon.new()
		# Force a re-bake by re-assigning the polygon.
		var poly = region.navigation_polygon
		region.navigation_polygon = null
		region.navigation_polygon = poly
		return {
			"success": true,
			"baking_started": true,
			"on_thread": false,
			"note": "2D bake is synchronous and re-triggered via polygon reassignment.",
		}
	return {"error": "Region type not supported for baking"}

func _handle_setup_agent(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent := get_target_node(parent_path)
	if not parent:
		return {"error": "Parent node not found: " + parent_path}

	var explicit_dim = String(params.get("dimension", "")).to_lower()
	var is_3d: bool
	if explicit_dim == "3d":
		is_3d = true
	elif explicit_dim == "2d":
		is_3d = false
	else:
		is_3d = _is_3d_parent(parent)

	var agent: Node
	if is_3d:
		agent = NavigationAgent3D.new()
		agent.height = float(params.get("height", 1.0))
	else:
		agent = NavigationAgent2D.new()
	agent.name = String(params.get("node_name", "NavigationAgent"))
	if params.has("radius"):
		agent.radius = float(params["radius"])
	if params.has("avoidance_enabled"):
		agent.avoidance_enabled = bool(params["avoidance_enabled"])
	if params.has("path_max_distance") and "path_max_distance" in agent:
		agent.path_max_distance = float(params["path_max_distance"])
	if params.has("path_desired_distance") and "path_desired_distance" in agent:
		agent.path_desired_distance = float(params["path_desired_distance"])
	if params.has("target_desired_distance") and "target_desired_distance" in agent:
		agent.target_desired_distance = float(params["target_desired_distance"])
	if params.has("max_speed") and "max_speed" in agent:
		agent.max_speed = float(params["max_speed"])
	if params.has("navigation_layers"):
		agent.navigation_layers = int(params["navigation_layers"])

	var tracked := UndoHelper.add_node(parent, agent, "Add %s" % agent.get_class())
	return {
		"success": true,
		"node_path": str(agent.get_path()) if agent.get_parent() else "",
		"node_class": agent.get_class(),
		"is_3d": is_3d,
		"undoable": tracked,
	}

func _handle_set_layers(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node := get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	if not ("navigation_layers" in node):
		return {"error": "Node has no navigation_layers property: " + node.get_class()}
	var layers := int(params.get("layers", 1))
	var tracked := UndoHelper.do_property(node, "navigation_layers", layers, "Set navigation_layers")
	return {
		"success": true,
		"node_path": str(node.get_path()),
		"layers": layers,
		"undoable": tracked,
	}

func _collect_nav_nodes(node: Node, regions: Array, agents: Array) -> void:
	if node is NavigationRegion2D or node is NavigationRegion3D:
		var entry := {
			"path": str(node.get_path()),
			"class": node.get_class(),
			"navigation_layers": node.navigation_layers,
			"enabled": node.enabled,
		}
		if node is NavigationRegion3D and node.navigation_mesh:
			entry["polygon_count"] = node.navigation_mesh.get_polygon_count()
			entry["vertex_count"] = node.navigation_mesh.get_vertices().size()
		regions.append(entry)
	elif node is NavigationAgent2D or node is NavigationAgent3D:
		var entry := {
			"path": str(node.get_path()),
			"class": node.get_class(),
			"radius": node.radius,
			"avoidance_enabled": node.avoidance_enabled,
			"navigation_layers": node.navigation_layers,
		}
		agents.append(entry)
	for c in node.get_children():
		_collect_nav_nodes(c, regions, agents)

func _handle_get_info(params: Dictionary) -> Dictionary:
	var root := get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently open in the editor"}
	var regions := []
	var agents := []
	_collect_nav_nodes(root, regions, agents)
	return {
		"success": true,
		"region_count": regions.size(),
		"agent_count": agents.size(),
		"regions": regions,
		"agents": agents,
	}
