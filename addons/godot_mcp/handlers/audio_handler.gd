@tool
extends McpHandler

# Audio handler — exposes AudioServer (bus layout, volume, effects) plus scene-
# level AudioStreamPlayer / 2D / 3D placement. AudioServer is process-global
# state; its mutations don't enter EditorUndoRedoManager. Bus layout edits are
# persisted by re-saving res://default_bus_layout.tres on demand.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"get_audio_bus_layout",
		"add_audio_bus",
		"set_audio_bus",
		"add_audio_bus_effect",
		"add_audio_player",
		"get_audio_info",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"get_audio_bus_layout":
			return _handle_get_layout(params)
		"add_audio_bus":
			return _handle_add_bus(params)
		"set_audio_bus":
			return _handle_set_bus(params)
		"add_audio_bus_effect":
			return _handle_add_effect(params)
		"add_audio_player":
			return _handle_add_player(params)
		"get_audio_info":
			return _handle_get_info(params)
		_:
			return {"error": "Unknown audio method: " + method}

func _resolve_bus_index(target: Variant) -> int:
	if target is int:
		return int(target)
	if target is String:
		return AudioServer.get_bus_index(target)
	return -1

func _serialize_bus(idx: int) -> Dictionary:
	var info := {
		"index": idx,
		"name": AudioServer.get_bus_name(idx),
		"volume_db": AudioServer.get_bus_volume_db(idx),
		"mute": AudioServer.is_bus_mute(idx),
		"solo": AudioServer.is_bus_solo(idx),
		"bypass_effects": AudioServer.is_bus_bypassing_effects(idx),
		"send": AudioServer.get_bus_send(idx) if idx > 0 else "",
		"effect_count": AudioServer.get_bus_effect_count(idx),
	}
	var effects := []
	for i in range(AudioServer.get_bus_effect_count(idx)):
		var eff := AudioServer.get_bus_effect(idx, i)
		effects.append({
			"index": i,
			"class": eff.get_class() if eff else "",
			"enabled": AudioServer.is_bus_effect_enabled(idx, i),
		})
	info["effects"] = effects
	return info

func _handle_get_layout(params: Dictionary) -> Dictionary:
	var buses := []
	for i in range(AudioServer.bus_count):
		buses.append(_serialize_bus(i))
	return {
		"success": true,
		"bus_count": AudioServer.bus_count,
		"buses": buses,
		"output_latency": AudioServer.get_output_latency(),
		"time_to_next_mix": AudioServer.get_time_to_next_mix(),
	}

func _save_bus_layout_if_requested(params: Dictionary) -> Dictionary:
	if not bool(params.get("save_layout", false)):
		return {"saved": false}
	var path := String(params.get("layout_path", "res://default_bus_layout.tres"))
	var layout := AudioServer.generate_bus_layout()
	var err := ResourceSaver.save(layout, path)
	if err != OK:
		return {"saved": false, "save_error": "ResourceSaver error: " + str(err)}
	return {"saved": true, "saved_path": path}

func _handle_add_bus(params: Dictionary) -> Dictionary:
	var name = String(params.get("name", ""))
	var at_position := int(params.get("at_position", -1))
	var send_to := String(params.get("send_to", "Master"))
	var volume_db := float(params.get("volume_db", 0.0))

	var insert_at := at_position
	if insert_at < 0 or insert_at > AudioServer.bus_count:
		insert_at = AudioServer.bus_count
	AudioServer.add_bus(insert_at)
	if not name.is_empty():
		AudioServer.set_bus_name(insert_at, name)
	if AudioServer.get_bus_index(send_to) != -1 and insert_at > 0:
		AudioServer.set_bus_send(insert_at, send_to)
	AudioServer.set_bus_volume_db(insert_at, volume_db)

	var saved := _save_bus_layout_if_requested(params)
	return {
		"success": true,
		"index": insert_at,
		"name": AudioServer.get_bus_name(insert_at),
		"undoable": false,
		"layout_saved": saved.get("saved", false),
		"saved_path": saved.get("saved_path", ""),
	}

func _handle_set_bus(params: Dictionary) -> Dictionary:
	var idx := _resolve_bus_index(params.get("bus", -1))
	if idx < 0 or idx >= AudioServer.bus_count:
		return {"error": "Bus not found"}
	if params.has("name"):
		AudioServer.set_bus_name(idx, String(params["name"]))
	if params.has("volume_db"):
		AudioServer.set_bus_volume_db(idx, float(params["volume_db"]))
	if params.has("mute"):
		AudioServer.set_bus_mute(idx, bool(params["mute"]))
	if params.has("solo"):
		AudioServer.set_bus_solo(idx, bool(params["solo"]))
	if params.has("bypass_effects"):
		AudioServer.set_bus_bypass_effects(idx, bool(params["bypass_effects"]))
	if params.has("send_to") and idx > 0:
		var target := String(params["send_to"])
		if AudioServer.get_bus_index(target) != -1:
			AudioServer.set_bus_send(idx, target)
	var saved := _save_bus_layout_if_requested(params)
	return {
		"success": true,
		"bus": _serialize_bus(idx),
		"undoable": false,
		"layout_saved": saved.get("saved", false),
	}

const _EFFECT_CLASS_MAP := {
	"reverb": "AudioEffectReverb",
	"delay": "AudioEffectDelay",
	"compressor": "AudioEffectCompressor",
	"eq6": "AudioEffectEQ6",
	"eq10": "AudioEffectEQ10",
	"eq21": "AudioEffectEQ21",
	"distortion": "AudioEffectDistortion",
	"limiter": "AudioEffectLimiter",
	"chorus": "AudioEffectChorus",
	"phaser": "AudioEffectPhaser",
	"highpass": "AudioEffectHighPassFilter",
	"lowpass": "AudioEffectLowPassFilter",
	"bandpass": "AudioEffectBandPassFilter",
	"pitchshift": "AudioEffectPitchShift",
	"stereoenhance": "AudioEffectStereoEnhance",
	"panner": "AudioEffectPanner",
	"capture": "AudioEffectCapture",
	"spectrumanalyzer": "AudioEffectSpectrumAnalyzer",
}

func _handle_add_effect(params: Dictionary) -> Dictionary:
	var idx := _resolve_bus_index(params.get("bus", -1))
	if idx < 0 or idx >= AudioServer.bus_count:
		return {"error": "Bus not found"}
	var effect_type = String(params.get("effect_type", "")).to_lower()
	var class_name_str: String
	if _EFFECT_CLASS_MAP.has(effect_type):
		class_name_str = _EFFECT_CLASS_MAP[effect_type]
	elif effect_type.begins_with("audioeffect"):
		# Allow caller to pass the raw class name when the alias map doesn't have it.
		class_name_str = String(params["effect_type"])
	else:
		return {
			"error": "Unknown effect_type",
			"available_aliases": _EFFECT_CLASS_MAP.keys(),
		}
	if not ClassDB.class_exists(class_name_str):
		return {"error": "Class does not exist: " + class_name_str}
	var instance = ClassDB.instantiate(class_name_str)
	if not (instance is AudioEffect):
		return {"error": "Class is not an AudioEffect: " + class_name_str}

	# Apply optional properties on the freshly constructed effect.
	var properties: Dictionary = params.get("properties", {})
	if properties is Dictionary:
		for key in properties.keys():
			if key in instance:
				var current = instance.get(key)
				instance.set(key, TypeHelper.parse_godot_value(properties[key], typeof(current)))

	var at_position := int(params.get("at_position", -1))
	if at_position < 0:
		AudioServer.add_bus_effect(idx, instance)
		at_position = AudioServer.get_bus_effect_count(idx) - 1
	else:
		AudioServer.add_bus_effect(idx, instance, at_position)

	var saved := _save_bus_layout_if_requested(params)
	return {
		"success": true,
		"bus_index": idx,
		"effect_class": class_name_str,
		"effect_position": at_position,
		"undoable": false,
		"layout_saved": saved.get("saved", false),
	}

func _handle_add_player(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", "")
	if parent_path.is_empty():
		return {"error": "parent_path is required"}
	var parent := get_target_node(parent_path)
	if not parent:
		return {"error": "Parent node not found: " + parent_path}

	var player_type = String(params.get("player_type", "stream")).to_lower()
	var node: Node
	match player_type:
		"2d":
			node = AudioStreamPlayer2D.new()
		"3d":
			node = AudioStreamPlayer3D.new()
		"stream", "":
			node = AudioStreamPlayer.new()
		_:
			return {"error": "player_type must be 'stream', '2d', or '3d'"}

	node.name = String(params.get("node_name", "AudioPlayer"))
	var bus_name = String(params.get("bus", "Master"))
	if AudioServer.get_bus_index(bus_name) != -1:
		node.bus = bus_name
	if params.has("volume_db") and "volume_db" in node:
		node.volume_db = float(params["volume_db"])
	if params.has("autoplay") and "autoplay" in node:
		node.autoplay = bool(params["autoplay"])
	if params.has("stream_path"):
		var stream_path = String(params["stream_path"])
		if not stream_path.is_empty():
			var loaded = load(stream_path)
			if loaded is AudioStream:
				node.stream = loaded
	# Handle 3D-specific defaults so the node is audible without further setup.
	if node is AudioStreamPlayer3D:
		if params.has("max_distance"):
			node.max_distance = float(params["max_distance"])
		if params.has("unit_size"):
			node.unit_size = float(params["unit_size"])

	var tracked := UndoHelper.add_node(parent, node, "Add %s" % node.get_class())
	return {
		"success": true,
		"node_path": str(node.get_path()) if node.get_parent() else "",
		"node_class": node.get_class(),
		"bus": bus_name,
		"undoable": tracked,
	}

# Walk the edited scene to collect any AudioStreamPlayer / 2D / 3D nodes plus
# the global bus layout. Useful for the LLM to see "what audio is set up".
func _collect_audio_nodes(node: Node, out: Array) -> void:
	if node is AudioStreamPlayer or node is AudioStreamPlayer2D or node is AudioStreamPlayer3D:
		var entry := {
			"path": str(node.get_path()),
			"class": node.get_class(),
			"bus": node.bus,
			"volume_db": node.volume_db,
			"autoplay": node.autoplay,
		}
		if node.stream:
			entry["stream"] = node.stream.resource_path if node.stream.resource_path else "<inline>"
		out.append(entry)
	for c in node.get_children():
		_collect_audio_nodes(c, out)

func _handle_get_info(params: Dictionary) -> Dictionary:
	var layout := _handle_get_layout({})
	var info := {
		"buses": layout.get("buses", []),
		"output_latency": layout.get("output_latency", 0.0),
	}
	var root := get_edited_scene_root()
	if root:
		var nodes := []
		_collect_audio_nodes(root, nodes)
		info["audio_nodes"] = nodes
	else:
		info["audio_nodes"] = []
	return {"success": true, "info": info}
