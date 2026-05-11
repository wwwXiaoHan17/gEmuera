@tool
extends McpHandler

# Supports both classic TileMap nodes and the newer (4.3+) per-layer
# TileMapLayer nodes. The handler dispatches at the node level: classic
# TileMap exposes a `layer` parameter; TileMapLayer ignores it.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"tilemap_set_cell",
		"tilemap_fill_rect",
		"tilemap_get_cell",
		"tilemap_clear",
		"tilemap_get_info",
		"tilemap_get_used_cells",
		"tilemap_set_layer",
		"tileset_get_info",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"tilemap_set_cell":
			return _handle_set_cell(params)
		"tilemap_fill_rect":
			return _handle_fill_rect(params)
		"tilemap_get_cell":
			return _handle_get_cell(params)
		"tilemap_clear":
			return _handle_clear(params)
		"tilemap_get_info":
			return _handle_get_info(params)
		"tilemap_get_used_cells":
			return _handle_get_used_cells(params)
		"tilemap_set_layer":
			return _handle_set_layer(params)
		"tileset_get_info":
			return _handle_tileset_get_info(params)
		_:
			return {"error": "Unknown tilemap method: " + method}

func _resolve_tilemap(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node = get_target_node(node_path)
	if not node:
		return {"error": "Node not found: " + node_path}
	var cls = node.get_class()
	if cls != "TileMap" and cls != "TileMapLayer":
		return {"error": "Node is not a TileMap or TileMapLayer: " + cls}
	return {"node": node, "is_layer_node": cls == "TileMapLayer"}

func _handle_set_cell(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tilemap(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var is_layer = resolved["is_layer_node"]
	var layer = int(params.get("layer", 0))
	var x = int(params.get("x", 0))
	var y = int(params.get("y", 0))
	var coords = Vector2i(x, y)
	var source_id = int(params.get("source_id", -1))
	var atlas_coords = Vector2i(int(params.get("atlas_x", 0)), int(params.get("atlas_y", 0)))
	var alternative = int(params.get("alternative", 0))

	var prev_source := -1
	var prev_atlas = Vector2i(-1, -1)
	var prev_alt := -1
	if is_layer:
		prev_source = node.get_cell_source_id(coords)
		prev_atlas = node.get_cell_atlas_coords(coords)
		prev_alt = node.get_cell_alternative_tile(coords)
	else:
		prev_source = node.get_cell_source_id(layer, coords)
		prev_atlas = node.get_cell_atlas_coords(layer, coords)
		prev_alt = node.get_cell_alternative_tile(layer, coords)

	var do_call = func():
		if is_layer:
			node.set_cell(coords, source_id, atlas_coords, alternative)
		else:
			node.set_cell(layer, coords, source_id, atlas_coords, alternative)
	var undo_call = func():
		if is_layer:
			node.set_cell(coords, prev_source, prev_atlas, prev_alt)
		else:
			node.set_cell(layer, coords, prev_source, prev_atlas, prev_alt)

	var tracked = UndoHelper.do_simple("Set tile (%d, %d)" % [x, y], do_call, undo_call)
	return {
		"success": true,
		"coords": {"x": x, "y": y},
		"source_id": source_id,
		"undoable": tracked,
	}

func _handle_fill_rect(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tilemap(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var is_layer = resolved["is_layer_node"]
	var layer = int(params.get("layer", 0))
	var x = int(params.get("x", 0))
	var y = int(params.get("y", 0))
	var w = int(params.get("w", 1))
	var h = int(params.get("h", 1))
	var source_id = int(params.get("source_id", -1))
	var atlas_coords = Vector2i(int(params.get("atlas_x", 0)), int(params.get("atlas_y", 0)))
	var alternative = int(params.get("alternative", 0))

	# Capture previous state for undo
	var previous = []
	for ix in range(x, x + w):
		for iy in range(y, y + h):
			var c = Vector2i(ix, iy)
			var ps: int
			var pa: Vector2i
			var palt: int
			if is_layer:
				ps = node.get_cell_source_id(c)
				pa = node.get_cell_atlas_coords(c)
				palt = node.get_cell_alternative_tile(c)
			else:
				ps = node.get_cell_source_id(layer, c)
				pa = node.get_cell_atlas_coords(layer, c)
				palt = node.get_cell_alternative_tile(layer, c)
			previous.append({"coords": c, "source": ps, "atlas": pa, "alt": palt})

	var do_call = func():
		for ix in range(x, x + w):
			for iy in range(y, y + h):
				var c = Vector2i(ix, iy)
				if is_layer:
					node.set_cell(c, source_id, atlas_coords, alternative)
				else:
					node.set_cell(layer, c, source_id, atlas_coords, alternative)
	var undo_call = func():
		for entry in previous:
			if is_layer:
				node.set_cell(entry["coords"], entry["source"], entry["atlas"], entry["alt"])
			else:
				node.set_cell(layer, entry["coords"], entry["source"], entry["atlas"], entry["alt"])

	var tracked = UndoHelper.do_simple("Fill tilemap rect", do_call, undo_call)
	return {
		"success": true,
		"cells_filled": w * h,
		"undoable": tracked,
	}

func _handle_get_cell(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tilemap(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var is_layer = resolved["is_layer_node"]
	var layer = int(params.get("layer", 0))
	var coords = Vector2i(int(params.get("x", 0)), int(params.get("y", 0)))
	var source: int
	var atlas: Vector2i
	var alt: int
	if is_layer:
		source = node.get_cell_source_id(coords)
		atlas = node.get_cell_atlas_coords(coords)
		alt = node.get_cell_alternative_tile(coords)
	else:
		source = node.get_cell_source_id(layer, coords)
		atlas = node.get_cell_atlas_coords(layer, coords)
		alt = node.get_cell_alternative_tile(layer, coords)
	return {
		"coords": {"x": coords.x, "y": coords.y},
		"source_id": source,
		"atlas_coords": {"x": atlas.x, "y": atlas.y},
		"alternative": alt,
		"empty": source == -1,
	}

func _handle_clear(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tilemap(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var is_layer = resolved["is_layer_node"]
	var layer = int(params.get("layer", -1))

	# Capture full snapshot for undo
	var snapshot = []
	var used_cells = []
	if is_layer:
		used_cells = node.get_used_cells()
	else:
		used_cells = node.get_used_cells(layer if layer >= 0 else 0)
	for c in used_cells:
		var ps: int
		var pa: Vector2i
		var palt: int
		if is_layer:
			ps = node.get_cell_source_id(c)
			pa = node.get_cell_atlas_coords(c)
			palt = node.get_cell_alternative_tile(c)
		else:
			var lyr = layer if layer >= 0 else 0
			ps = node.get_cell_source_id(lyr, c)
			pa = node.get_cell_atlas_coords(lyr, c)
			palt = node.get_cell_alternative_tile(lyr, c)
		snapshot.append({"coords": c, "source": ps, "atlas": pa, "alt": palt})

	var do_call = func():
		if is_layer:
			node.clear()
		else:
			if layer < 0:
				node.clear()
			else:
				node.clear_layer(layer)
	var undo_call = func():
		for entry in snapshot:
			if is_layer:
				node.set_cell(entry["coords"], entry["source"], entry["atlas"], entry["alt"])
			else:
				var lyr = layer if layer >= 0 else 0
				node.set_cell(lyr, entry["coords"], entry["source"], entry["atlas"], entry["alt"])

	var tracked = UndoHelper.do_simple("Clear tilemap", do_call, undo_call)
	return {"success": true, "cleared": snapshot.size(), "undoable": tracked}

func _handle_get_info(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tilemap(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var is_layer = resolved["is_layer_node"]
	var info = {
		"class": node.get_class(),
		"name": node.name,
		"path": str(node.get_path()),
	}
	if is_layer:
		info["tile_set"] = node.tile_set.resource_path if node.tile_set else ""
		info["used_rect"] = TypeHelper.variant_to_json(node.get_used_rect())
		info["used_cells"] = node.get_used_cells().size()
		info["enabled"] = node.enabled
	else:
		info["tile_set"] = node.tile_set.resource_path if node.tile_set else ""
		info["layer_count"] = node.get_layers_count()
		info["used_rect"] = TypeHelper.variant_to_json(node.get_used_rect())
		var layers_info = []
		for i in range(node.get_layers_count()):
			layers_info.append({
				"index": i,
				"name": node.get_layer_name(i),
				"enabled": node.is_layer_enabled(i),
				"modulate": TypeHelper.variant_to_json(node.get_layer_modulate(i)),
				"y_sort_enabled": node.is_layer_y_sort_enabled(i),
				"used_cells": node.get_used_cells(i).size(),
			})
		info["layers"] = layers_info
	return info

func _handle_get_used_cells(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tilemap(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var is_layer = resolved["is_layer_node"]
	var layer = int(params.get("layer", 0))
	var cells = []
	var raw = []
	if is_layer:
		raw = node.get_used_cells()
	else:
		raw = node.get_used_cells(layer)
	for c in raw:
		cells.append({"x": c.x, "y": c.y})
	return {"cells": cells, "count": cells.size()}

func _handle_set_layer(params: Dictionary) -> Dictionary:
	var resolved = _resolve_tilemap(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var is_layer = resolved["is_layer_node"]
	if is_layer:
		# TileMapLayer's properties are top-level node properties — use update_property instead
		return {"error": "For TileMapLayer nodes, use update_property to set 'enabled', 'modulate', etc."}
	var layer = int(params.get("layer", 0))
	var properties = params.get("properties", {})
	if not properties is Dictionary or properties.is_empty():
		return {"error": "properties dict is required"}

	if "name" in properties:
		node.set_layer_name(layer, str(properties["name"]))
	if "enabled" in properties:
		node.set_layer_enabled(layer, bool(properties["enabled"]))
	if "modulate" in properties:
		var col = TypeHelper.parse_godot_value(properties["modulate"], TYPE_COLOR)
		if col is Color:
			node.set_layer_modulate(layer, col)
	if "y_sort" in properties:
		node.set_layer_y_sort_enabled(layer, bool(properties["y_sort"]))
	if "z_index" in properties:
		node.set_layer_z_index(layer, int(properties["z_index"]))
	return {"success": true, "layer": layer, "applied": properties.keys()}

func _handle_tileset_get_info(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var resource_path = params.get("resource_path", "")
	var ts: TileSet = null
	if not node_path.is_empty():
		var node = get_target_node(node_path)
		if node and ("tile_set" in node):
			ts = node.tile_set
	if not ts and not resource_path.is_empty():
		if not resource_path.begins_with("res://"):
			resource_path = "res://" + resource_path
		ts = ResourceLoader.load(resource_path) as TileSet
	if not ts:
		return {"error": "TileSet not found"}

	var sources = []
	for i in range(ts.get_source_count()):
		var sid = ts.get_source_id(i)
		var src = ts.get_source(sid)
		var entry = {"id": sid, "class": src.get_class()}
		if src is TileSetAtlasSource:
			entry["texture"] = src.texture.resource_path if src.texture else ""
			entry["tiles_count"] = src.get_tiles_count()
			entry["texture_region_size"] = TypeHelper.variant_to_json(src.texture_region_size)
		sources.append(entry)

	return {
		"tile_size": TypeHelper.variant_to_json(ts.tile_size),
		"tile_shape": ts.tile_shape,
		"physics_layers": ts.get_physics_layers_count(),
		"navigation_layers": ts.get_navigation_layers_count(),
		"custom_data_layers": ts.get_custom_data_layers_count(),
		"sources": sources,
	}
