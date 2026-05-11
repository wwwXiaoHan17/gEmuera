@tool
extends McpHandler

# Particles handler — covers GPUParticles2D / GPUParticles3D creation, process
# material configuration, gradient color ramp setup, and a small library of
# preset effects (fire/smoke/rain/snow/sparks). Auto-detects 2D vs 3D from the
# parent node's class hierarchy.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"create_particles",
		"set_particle_material",
		"set_particle_color_gradient",
		"apply_particle_preset",
		"get_particle_info",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"create_particles":
			return _handle_create_particles(params)
		"set_particle_material":
			return _handle_set_material(params)
		"set_particle_color_gradient":
			return _handle_set_gradient(params)
		"apply_particle_preset":
			return _handle_apply_preset(params)
		"get_particle_info":
			return _handle_get_info(params)
		_:
			return {"error": "Unknown particles method: " + method}

func _resolve_particles(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	if node_path.is_empty():
		return {"error": "node_path is required"}
	var node := get_target_node(node_path)
	if not node:
		return {"error": "Particles node not found: " + node_path}
	if not (node is GPUParticles2D or node is GPUParticles3D):
		return {"error": "Node is not a GPUParticles2D/3D: " + node.get_class()}
	return {"node": node}

func _is_3d_parent(parent: Node) -> bool:
	# Walk up: if any ancestor is Node3D we treat the chain as 3D.
	var n = parent
	while n:
		if n is Node3D:
			return true
		if n is Node2D or n is Control:
			return false
		n = n.get_parent()
	return false

func _handle_create_particles(params: Dictionary) -> Dictionary:
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

	var node_name = String(params.get("node_name", "Particles"))
	var amount := int(params.get("amount", 50))
	var lifetime := float(params.get("lifetime", 1.0))
	var emitting := bool(params.get("emitting", true))
	var one_shot := bool(params.get("one_shot", false))
	var explosiveness := float(params.get("explosiveness", 0.0))

	var particles: Node
	if is_3d:
		particles = GPUParticles3D.new()
	else:
		particles = GPUParticles2D.new()
	particles.name = node_name
	particles.amount = amount
	particles.lifetime = lifetime
	particles.emitting = emitting
	particles.one_shot = one_shot
	particles.explosiveness = explosiveness

	# Always attach a fresh process material so subsequent set_particle_material
	# calls have something to mutate without us re-creating one.
	var process_mat := ParticleProcessMaterial.new()
	particles.process_material = process_mat

	var tracked := UndoHelper.add_node(parent, particles, "Add %s" % particles.get_class())
	return {
		"success": true,
		"node_path": str(particles.get_path()) if particles.get_parent() else "",
		"node_class": particles.get_class(),
		"is_3d": is_3d,
		"undoable": tracked,
	}

# Apply a flat dict of process-material properties. We only set keys that exist
# on ParticleProcessMaterial so we don't crash on typos.
func _apply_process_material_props(mat: ParticleProcessMaterial, props: Dictionary) -> Array:
	var applied := []
	for key in props.keys():
		if key in mat:
			var current = mat.get(key)
			var converted = TypeHelper.parse_godot_value(props[key], typeof(current))
			mat.set(key, converted)
			applied.append(key)
	return applied

func _handle_set_material(params: Dictionary) -> Dictionary:
	var resolved := _resolve_particles(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var mat: ParticleProcessMaterial = node.process_material
	if not mat:
		mat = ParticleProcessMaterial.new()
		node.process_material = mat

	var properties: Dictionary = params.get("properties", {})
	if not (properties is Dictionary):
		return {"error": "properties must be a Dictionary"}

	# We can't easily diff every field for undo, so we snapshot the whole
	# material before/after via duplicate() and use UndoHelper.do_property on
	# the host node's process_material slot.
	var old_mat: ParticleProcessMaterial = mat.duplicate()
	var applied := _apply_process_material_props(mat, properties)
	# Force a property change notification for editor refresh.
	node.notify_property_list_changed()

	# Persist the change as a single property swap on `process_material`.
	# Note: we already wrote to the live material; we now create an undo entry
	# that swaps in the snapshot we captured. This isn't perfect but matches
	# how the editor itself stores material edits in its undo stack.
	var ur = UndoHelper.begin_batch("Set particle material props")
	if ur:
		ur.add_do_property(node, "process_material", mat)
		ur.add_undo_property(node, "process_material", old_mat)
		UndoHelper.commit_batch(ur)

	return {
		"success": true,
		"applied_properties": applied,
		"undoable": ur != null,
	}

# Build a Gradient resource from [{offset: 0.0, color: "#ff8800"}, ...] and
# wire it in via the process material's color_ramp slot. Accepts either a
# GradientTexture1D-shaped color_ramp (default) or a raw Gradient.
func _handle_set_gradient(params: Dictionary) -> Dictionary:
	var resolved := _resolve_particles(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var mat: ParticleProcessMaterial = node.process_material
	if not mat:
		return {"error": "process_material missing — call create_particles first"}

	var stops: Array = params.get("stops", [])
	if stops.size() < 2:
		return {"error": "At least two gradient stops are required"}

	var gradient := Gradient.new()
	# Gradient starts with two default points; clear them by replacing offsets.
	gradient.offsets = []
	gradient.colors = []
	var offsets: PackedFloat32Array = []
	var colors: PackedColorArray = []
	for s in stops:
		if not (s is Dictionary):
			continue
		var off := float(s.get("offset", 0.0))
		var col_value = s.get("color", Color.WHITE)
		var col: Color = TypeHelper.parse_godot_value(col_value, TYPE_COLOR)
		if not (col is Color):
			col = Color.WHITE
		offsets.append(clamp(off, 0.0, 1.0))
		colors.append(col)
	gradient.offsets = offsets
	gradient.colors = colors

	var tex := GradientTexture1D.new()
	tex.gradient = gradient
	tex.width = int(params.get("texture_width", 64))

	var old_ramp = mat.color_ramp
	var ur = UndoHelper.begin_batch("Set particle color gradient")
	var tracked := false
	if ur:
		ur.add_do_property(mat, "color_ramp", tex)
		ur.add_undo_property(mat, "color_ramp", old_ramp)
		UndoHelper.commit_batch(ur)
		tracked = true
	else:
		mat.color_ramp = tex

	return {
		"success": true,
		"stop_count": stops.size(),
		"undoable": tracked,
	}

# Library of preset effects. Each preset is a dict of:
#   - props: ParticleProcessMaterial properties to set
#   - stops: gradient color stops
#   - host: high-level GPUParticles* fields (amount/lifetime/etc.)
const _PRESETS := {
	"fire": {
		"host": {"amount": 60, "lifetime": 1.2, "explosiveness": 0.05},
		"props": {
			"direction": {"x": 0, "y": -1, "z": 0},
			"spread": 25.0,
			"initial_velocity_min": 60.0,
			"initial_velocity_max": 120.0,
			"gravity": {"x": 0, "y": -50, "z": 0},
			"scale_min": 0.6,
			"scale_max": 1.0,
		},
		"stops": [
			{"offset": 0.0, "color": "#ffeb3b"},
			{"offset": 0.4, "color": "#ff9800"},
			{"offset": 0.8, "color": "#f44336"},
			{"offset": 1.0, "color": "#33000000"},
		],
	},
	"smoke": {
		"host": {"amount": 40, "lifetime": 2.5, "explosiveness": 0.0},
		"props": {
			"direction": {"x": 0, "y": -1, "z": 0},
			"spread": 35.0,
			"initial_velocity_min": 20.0,
			"initial_velocity_max": 50.0,
			"gravity": {"x": 0, "y": -10, "z": 0},
			"scale_min": 0.4,
			"scale_max": 1.5,
			"damping_min": 1.0,
			"damping_max": 3.0,
		},
		"stops": [
			{"offset": 0.0, "color": "#aaffffff"},
			{"offset": 0.7, "color": "#66888888"},
			{"offset": 1.0, "color": "#00444444"},
		],
	},
	"rain": {
		"host": {"amount": 200, "lifetime": 1.5, "explosiveness": 0.0},
		"props": {
			"direction": {"x": 0, "y": 1, "z": 0},
			"spread": 5.0,
			"initial_velocity_min": 400.0,
			"initial_velocity_max": 500.0,
			"gravity": {"x": 0, "y": 800, "z": 0},
			"scale_min": 0.2,
			"scale_max": 0.4,
		},
		"stops": [
			{"offset": 0.0, "color": "#cc66ccff"},
			{"offset": 1.0, "color": "#883399ee"},
		],
	},
	"snow": {
		"host": {"amount": 100, "lifetime": 5.0, "explosiveness": 0.0},
		"props": {
			"direction": {"x": 0, "y": 1, "z": 0},
			"spread": 25.0,
			"initial_velocity_min": 30.0,
			"initial_velocity_max": 60.0,
			"gravity": {"x": 5, "y": 50, "z": 0},
			"scale_min": 0.4,
			"scale_max": 0.9,
		},
		"stops": [
			{"offset": 0.0, "color": "#ffffffff"},
			{"offset": 1.0, "color": "#88ffffff"},
		],
	},
	"sparks": {
		"host": {"amount": 25, "lifetime": 0.5, "explosiveness": 0.6, "one_shot": true},
		"props": {
			"direction": {"x": 0, "y": -1, "z": 0},
			"spread": 60.0,
			"initial_velocity_min": 200.0,
			"initial_velocity_max": 400.0,
			"gravity": {"x": 0, "y": 200, "z": 0},
			"scale_min": 0.3,
			"scale_max": 0.6,
		},
		"stops": [
			{"offset": 0.0, "color": "#ffffaa"},
			{"offset": 0.6, "color": "#ff9933"},
			{"offset": 1.0, "color": "#00ff3300"},
		],
	},
}

func _handle_apply_preset(params: Dictionary) -> Dictionary:
	var resolved := _resolve_particles(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var preset_name: String = String(params.get("preset", "")).to_lower()
	if preset_name.is_empty() or not _PRESETS.has(preset_name):
		return {
			"error": "Unknown preset",
			"available": _PRESETS.keys(),
		}
	var preset: Dictionary = _PRESETS[preset_name]

	# Apply host-level properties (amount, lifetime, etc.) using individual
	# UndoHelper.do_property calls so each is reversible. We batch them under
	# a single label by opening a manual undo action.
	var ur = UndoHelper.begin_batch("Apply particle preset: %s" % preset_name)
	var host_props: Dictionary = preset.get("host", {})
	for key in host_props.keys():
		if key in node:
			var current = node.get(key)
			var new_val = TypeHelper.parse_godot_value(host_props[key], typeof(current))
			if ur:
				ur.add_do_property(node, key, new_val)
				ur.add_undo_property(node, key, current)
			else:
				node.set(key, new_val)

	# Apply process-material properties — same approach but on the material.
	var mat: ParticleProcessMaterial = node.process_material
	if not mat:
		mat = ParticleProcessMaterial.new()
		node.process_material = mat
	var mat_props: Dictionary = preset.get("props", {})
	for key in mat_props.keys():
		if key in mat:
			var current = mat.get(key)
			var new_val = TypeHelper.parse_godot_value(mat_props[key], typeof(current))
			if ur:
				ur.add_do_property(mat, key, new_val)
				ur.add_undo_property(mat, key, current)
			else:
				mat.set(key, new_val)

	# Build & wire color ramp.
	var stops: Array = preset.get("stops", [])
	if stops.size() >= 2:
		var gradient := Gradient.new()
		var offsets: PackedFloat32Array = []
		var colors: PackedColorArray = []
		for s in stops:
			offsets.append(float(s.get("offset", 0.0)))
			var col: Color = TypeHelper.parse_godot_value(s.get("color", Color.WHITE), TYPE_COLOR)
			colors.append(col if col is Color else Color.WHITE)
		gradient.offsets = offsets
		gradient.colors = colors
		var tex := GradientTexture1D.new()
		tex.gradient = gradient
		tex.width = 64
		var old_ramp = mat.color_ramp
		if ur:
			ur.add_do_property(mat, "color_ramp", tex)
			ur.add_undo_property(mat, "color_ramp", old_ramp)
		else:
			mat.color_ramp = tex

	UndoHelper.commit_batch(ur)
	return {
		"success": true,
		"preset": preset_name,
		"undoable": ur != null,
	}

func _handle_get_info(params: Dictionary) -> Dictionary:
	var resolved := _resolve_particles(params)
	if resolved.has("error"):
		return resolved
	var node = resolved["node"]
	var info := {
		"node_path": str(node.get_path()),
		"node_class": node.get_class(),
		"amount": node.amount,
		"lifetime": node.lifetime,
		"emitting": node.emitting,
		"one_shot": node.one_shot,
		"explosiveness": node.explosiveness,
		"speed_scale": node.speed_scale,
		"randomness": node.randomness,
		"fixed_fps": node.fixed_fps,
	}
	var mat: ParticleProcessMaterial = node.process_material
	if mat:
		info["process_material"] = {
			"direction": TypeHelper.variant_to_json(mat.direction),
			"spread": mat.spread,
			"initial_velocity_min": mat.initial_velocity_min,
			"initial_velocity_max": mat.initial_velocity_max,
			"gravity": TypeHelper.variant_to_json(mat.gravity),
			"scale_min": mat.scale_min,
			"scale_max": mat.scale_max,
			"emission_shape": mat.emission_shape,
			"has_color_ramp": mat.color_ramp != null,
		}
		if mat.color_ramp is GradientTexture1D and mat.color_ramp.gradient:
			var g: Gradient = mat.color_ramp.gradient
			var ramp_stops := []
			for i in range(g.offsets.size()):
				ramp_stops.append({
					"offset": g.offsets[i],
					"color": TypeHelper.variant_to_json(g.colors[i]) if i < g.colors.size() else null,
				})
			info["process_material"]["color_ramp_stops"] = ramp_stops
	return {"success": true, "info": info}
