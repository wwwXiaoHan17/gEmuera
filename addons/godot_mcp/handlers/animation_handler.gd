@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"list_animations",
		"create_animation",
		"add_animation_track",
		"set_animation_keyframe",
		"get_animation_info",
		"remove_animation",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"list_animations":
			return _handle_list_animations(params)
		"create_animation":
			return _handle_create_animation(params)
		"add_animation_track":
			return _handle_add_animation_track(params)
		"set_animation_keyframe":
			return _handle_set_animation_keyframe(params)
		"get_animation_info":
			return _handle_get_animation_info(params)
		"remove_animation":
			return _handle_remove_animation(params)
		_:
			return {"error": "Unknown animation method: " + method}

func _find_animation_player(node_path: String) -> AnimationPlayer:
	var root = get_edited_scene_root()
	if not root:
		return null

	if node_path.is_empty():
		var direct = root.find_child("AnimationPlayer", true, false)
		if direct is AnimationPlayer:
			return direct
		var children = root.find_children("*", "AnimationPlayer", true, false)
		if children.size() > 0:
			return children[0] as AnimationPlayer
		return null

	var target = get_target_node(node_path)
	if target is AnimationPlayer:
		return target
	return null

# Returns the default empty-named AnimationLibrary, creating it if necessary.
func _ensure_default_library(player: AnimationPlayer) -> AnimationLibrary:
	var lib_name = StringName("")
	if player.has_animation_library(lib_name):
		return player.get_animation_library(lib_name)
	var lib = AnimationLibrary.new()
	var err = player.add_animation_library(lib_name, lib)
	if err != OK:
		return null
	return lib

# Locate the library currently containing the given animation, or null.
func _find_owning_library(player: AnimationPlayer, anim_name: String) -> Dictionary:
	for lib_name in player.get_animation_library_list():
		var lib = player.get_animation_library(lib_name)
		if lib and lib.has_animation(anim_name):
			return {"library": lib, "name": lib_name}
	return {}

func _handle_list_animations(params: Dictionary) -> Dictionary:
	var player = _find_animation_player(params.get("node_path", ""))
	if not player:
		return {"error": "AnimationPlayer not found"}

	var animations = []
	for anim_name in player.get_animation_list():
		var anim = player.get_animation(anim_name)
		animations.append({
			"name": anim_name,
			"length": anim.length,
			"loop": anim.loop_mode != Animation.LOOP_NONE,
			"loop_mode": anim.loop_mode,
			"tracks": anim.get_track_count()
		})
	return {"animations": animations, "player_path": str(player.get_path())}

func _handle_create_animation(params: Dictionary) -> Dictionary:
	var anim_name = params.get("animation_name", "")
	var length = params.get("length", 1.0)
	var loop = params.get("loop", false)
	if anim_name.is_empty():
		return {"error": "animation_name is required"}

	var player = _find_animation_player(params.get("node_path", ""))
	if not player:
		return {"error": "AnimationPlayer not found"}

	if player.has_animation(anim_name):
		return {"error": "Animation already exists: " + anim_name}

	var lib = _ensure_default_library(player)
	if not lib:
		return {"error": "Failed to obtain default animation library"}

	var anim = Animation.new()
	anim.length = length
	anim.loop_mode = Animation.LOOP_LINEAR if loop else Animation.LOOP_NONE

	var do_call = func(): lib.add_animation(anim_name, anim)
	var undo_call = func():
		if lib.has_animation(anim_name):
			lib.remove_animation(anim_name)
	var tracked = UndoHelper.do_simple("Create animation: " + anim_name, do_call, undo_call)

	return {
		"success": true,
		"animation_name": anim_name,
		"length": length,
		"loop": loop,
		"undoable": tracked,
	}

func _handle_add_animation_track(params: Dictionary) -> Dictionary:
	var anim_name = params.get("animation_name", "")
	var track_type = params.get("track_type", "value")
	var path = params.get("path", "")
	if anim_name.is_empty() or path.is_empty():
		return {"error": "animation_name and path are required"}

	var player = _find_animation_player(params.get("node_path", ""))
	if not player:
		return {"error": "AnimationPlayer not found"}

	if not player.has_animation(anim_name):
		return {"error": "Animation not found: " + anim_name}

	var type_id := -1
	match track_type.to_lower():
		"value":
			type_id = Animation.TYPE_VALUE
		"position_3d", "position3d":
			type_id = Animation.TYPE_POSITION_3D
		"rotation_3d", "rotation3d":
			type_id = Animation.TYPE_ROTATION_3D
		"scale_3d", "scale3d":
			type_id = Animation.TYPE_SCALE_3D
		"blend_shape":
			type_id = Animation.TYPE_BLEND_SHAPE
		"method":
			type_id = Animation.TYPE_METHOD
		"bezier":
			type_id = Animation.TYPE_BEZIER
		_:
			return {"error": "Unknown track type: " + track_type}

	var anim = player.get_animation(anim_name)
	var track_idx = anim.add_track(type_id)
	anim.track_set_path(track_idx, NodePath(path))

	# Track add isn't easily reversible after subsequent ops shift indices,
	# so we register a best-effort undo that removes the trailing track if
	# the count still matches.
	var ur = UndoHelper._undo_redo()
	if ur:
		ur.create_action("Add track: " + path, UndoRedo.MERGE_DISABLE, UndoHelper._scene_context())
		ur.add_undo_method(anim, "remove_track", track_idx)
		# Already executed; redo replays via add_track + track_set_path
		ur.add_do_method(self, "_redo_add_track", anim, type_id, NodePath(path), track_idx)
		ur.commit_action()

	return {
		"success": true,
		"track_index": track_idx,
		"track_type": track_type,
		"path": path,
		"undoable": ur != null,
	}

func _redo_add_track(anim: Animation, type_id: int, path: NodePath, expected_idx: int) -> void:
	var idx = anim.add_track(type_id)
	anim.track_set_path(idx, path)
	# expected_idx is informational; real index is what we got.

func _handle_set_animation_keyframe(params: Dictionary) -> Dictionary:
	var anim_name = params.get("animation_name", "")
	var track_index = params.get("track_index", -1)
	var time = params.get("time", 0.0)
	var value = params.get("value", null)
	if anim_name.is_empty() or track_index < 0:
		return {"error": "animation_name and track_index are required"}

	var player = _find_animation_player(params.get("node_path", ""))
	if not player:
		return {"error": "AnimationPlayer not found"}

	if not player.has_animation(anim_name):
		return {"error": "Animation not found: " + anim_name}

	var anim = player.get_animation(anim_name)
	if track_index >= anim.get_track_count():
		return {"error": "Track index out of range: " + str(track_index)}

	var godot_value = TypeHelper.parse_godot_value(value)
	anim.track_insert_key(track_index, time, godot_value)

	# Record a best-effort undo: find the key by time and remove it.
	var ur = UndoHelper._undo_redo()
	if ur:
		ur.create_action("Insert keyframe", UndoRedo.MERGE_DISABLE, UndoHelper._scene_context())
		ur.add_undo_method(self, "_undo_insert_key", anim, track_index, time)
		ur.add_do_method(anim, "track_insert_key", track_index, time, godot_value)
		ur.commit_action()

	return {
		"success": true,
		"animation_name": anim_name,
		"track_index": track_index,
		"time": time,
		"undoable": ur != null,
	}

func _undo_insert_key(anim: Animation, track_index: int, time: float) -> void:
	var key_idx = anim.track_find_key(track_index, time, Animation.FIND_MODE_EXACT)
	if key_idx >= 0:
		anim.track_remove_key(track_index, key_idx)

func _handle_get_animation_info(params: Dictionary) -> Dictionary:
	var anim_name = params.get("animation_name", "")
	if anim_name.is_empty():
		return {"error": "animation_name is required"}

	var player = _find_animation_player(params.get("node_path", ""))
	if not player:
		return {"error": "AnimationPlayer not found"}

	if not player.has_animation(anim_name):
		return {"error": "Animation not found: " + anim_name}

	var anim = player.get_animation(anim_name)
	var include_keys = params.get("include_keys", true)

	var tracks = []
	for i in range(anim.get_track_count()):
		var track_info = {
			"index": i,
			"path": str(anim.track_get_path(i)),
			"type": _track_type_to_string(anim.track_get_type(i)),
			"type_id": anim.track_get_type(i),
			"key_count": anim.track_get_key_count(i),
			"enabled": anim.track_is_enabled(i),
			"interpolation": anim.track_get_interpolation_type(i),
		}
		if include_keys:
			var keys = []
			for k in range(anim.track_get_key_count(i)):
				keys.append({
					"time": anim.track_get_key_time(i, k),
					"value": TypeHelper.variant_to_json(anim.track_get_key_value(i, k)),
					"transition": anim.track_get_key_transition(i, k),
				})
			track_info["keys"] = keys
		tracks.append(track_info)

	return {
		"name": anim_name,
		"length": anim.length,
		"loop": anim.loop_mode != Animation.LOOP_NONE,
		"loop_mode": anim.loop_mode,
		"step": anim.step,
		"track_count": anim.get_track_count(),
		"tracks": tracks,
		"player_path": str(player.get_path()),
	}

func _handle_remove_animation(params: Dictionary) -> Dictionary:
	var anim_name = params.get("animation_name", "")
	if anim_name.is_empty():
		return {"error": "animation_name is required"}

	var player = _find_animation_player(params.get("node_path", ""))
	if not player:
		return {"error": "AnimationPlayer not found"}

	if not player.has_animation(anim_name):
		return {"error": "Animation not found: " + anim_name}

	var owner = _find_owning_library(player, anim_name)
	if owner.is_empty():
		return {"error": "Animation found but no owning library located: " + anim_name}

	var lib: AnimationLibrary = owner["library"]
	var lib_name = owner["name"]
	var preserved: Animation = lib.get_animation(anim_name)

	var do_call = func():
		if lib.has_animation(anim_name):
			lib.remove_animation(anim_name)
	var undo_call = func():
		if not lib.has_animation(anim_name) and preserved:
			lib.add_animation(anim_name, preserved)
	var tracked = UndoHelper.do_simple("Remove animation: " + anim_name, do_call, undo_call)

	return {
		"success": true,
		"removed": anim_name,
		"library": str(lib_name),
		"undoable": tracked,
	}

func _track_type_to_string(track_type: int) -> String:
	match track_type:
		Animation.TYPE_VALUE: return "value"
		Animation.TYPE_POSITION_3D: return "position_3d"
		Animation.TYPE_ROTATION_3D: return "rotation_3d"
		Animation.TYPE_SCALE_3D: return "scale_3d"
		Animation.TYPE_BLEND_SHAPE: return "blend_shape"
		Animation.TYPE_METHOD: return "method"
		Animation.TYPE_BEZIER: return "bezier"
		Animation.TYPE_AUDIO: return "audio"
		Animation.TYPE_ANIMATION: return "animation"
		_: return "unknown"

# Forwarders preserved for any external reflection callers.
func _variant_to_json(value: Variant) -> Variant:
	return TypeHelper.variant_to_json(value)

func _json_to_variant(value: Variant) -> Variant:
	return TypeHelper.parse_godot_value(value)
