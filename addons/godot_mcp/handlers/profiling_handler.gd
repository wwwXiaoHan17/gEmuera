@tool
extends McpHandler

# Profiling handler — exposes Godot's Performance singleton and Engine-level
# counters so an LLM can sanity-check FPS, memory, draw calls, physics activity,
# etc. without needing to attach a profiler. Read-only; nothing here mutates
# state, so no UndoHelper involvement.

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"get_performance_monitors",
		"get_editor_performance",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"get_performance_monitors":
			return _handle_get_performance_monitors(params)
		"get_editor_performance":
			return _handle_get_editor_performance(params)
		_:
			return {"error": "Unknown profiling method: " + method}

# Map Performance.MONITOR_* enum values to a stable string key. Iterating the
# enum dynamically would couple us to internal naming; this list is tied to the
# Godot 4.x Performance API surface.
const _MONITOR_KEYS := {
	Performance.TIME_FPS: "fps",
	Performance.TIME_PROCESS: "time_process",
	Performance.TIME_PHYSICS_PROCESS: "time_physics_process",
	Performance.TIME_NAVIGATION_PROCESS: "time_navigation_process",
	Performance.MEMORY_STATIC: "memory_static",
	Performance.MEMORY_STATIC_MAX: "memory_static_max",
	Performance.MEMORY_MESSAGE_BUFFER_MAX: "memory_message_buffer_max",
	Performance.OBJECT_COUNT: "object_count",
	Performance.OBJECT_RESOURCE_COUNT: "object_resource_count",
	Performance.OBJECT_NODE_COUNT: "object_node_count",
	Performance.OBJECT_ORPHAN_NODE_COUNT: "object_orphan_node_count",
	Performance.RENDER_TOTAL_OBJECTS_IN_FRAME: "render_total_objects_in_frame",
	Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME: "render_total_primitives_in_frame",
	Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME: "render_total_draw_calls_in_frame",
	Performance.RENDER_VIDEO_MEM_USED: "render_video_mem_used",
	Performance.RENDER_TEXTURE_MEM_USED: "render_texture_mem_used",
	Performance.RENDER_BUFFER_MEM_USED: "render_buffer_mem_used",
	Performance.PHYSICS_2D_ACTIVE_OBJECTS: "physics_2d_active_objects",
	Performance.PHYSICS_2D_COLLISION_PAIRS: "physics_2d_collision_pairs",
	Performance.PHYSICS_2D_ISLAND_COUNT: "physics_2d_island_count",
	Performance.PHYSICS_3D_ACTIVE_OBJECTS: "physics_3d_active_objects",
	Performance.PHYSICS_3D_COLLISION_PAIRS: "physics_3d_collision_pairs",
	Performance.PHYSICS_3D_ISLAND_COUNT: "physics_3d_island_count",
	Performance.AUDIO_OUTPUT_LATENCY: "audio_output_latency",
	Performance.NAVIGATION_ACTIVE_MAPS: "navigation_active_maps",
	Performance.NAVIGATION_REGION_COUNT: "navigation_region_count",
	Performance.NAVIGATION_AGENT_COUNT: "navigation_agent_count",
	Performance.NAVIGATION_LINK_COUNT: "navigation_link_count",
	Performance.NAVIGATION_POLYGON_COUNT: "navigation_polygon_count",
	Performance.NAVIGATION_EDGE_COUNT: "navigation_edge_count",
	Performance.NAVIGATION_EDGE_MERGE_COUNT: "navigation_edge_merge_count",
	Performance.NAVIGATION_EDGE_CONNECTION_COUNT: "navigation_edge_connection_count",
	Performance.NAVIGATION_EDGE_FREE_COUNT: "navigation_edge_free_count",
}

func _handle_get_performance_monitors(params: Dictionary) -> Dictionary:
	var include_filter: Array = params.get("include", [])
	var monitors := {}
	for monitor_id in _MONITOR_KEYS.keys():
		var key: String = _MONITOR_KEYS[monitor_id]
		if not include_filter.is_empty() and not include_filter.has(key):
			continue
		var value = Performance.get_monitor(monitor_id)
		monitors[key] = value

	# Try to surface any custom monitors the project registered via
	# Performance.add_custom_monitor(); these don't have stable enum IDs so
	# we look them up by name. Best-effort.
	var custom_names: Array = params.get("custom_monitors", [])
	if not custom_names.is_empty():
		var custom := {}
		for name in custom_names:
			# get_custom_monitor returns 0 for unknown names without erroring.
			if Performance.has_custom_monitor(name):
				custom[name] = Performance.get_custom_monitor(name)
			else:
				custom[name] = null
		monitors["_custom"] = custom

	return {
		"success": true,
		"monitors": monitors,
		"timestamp_ms": Time.get_ticks_msec(),
	}

func _handle_get_editor_performance(params: Dictionary) -> Dictionary:
	var info := {
		"fps": Engine.get_frames_per_second(),
		"frames_drawn": Engine.get_frames_drawn(),
		"physics_frames": Engine.get_physics_frames(),
		"process_frames": Engine.get_process_frames(),
		"time_scale": Engine.time_scale,
		"max_fps": Engine.max_fps,
		"physics_ticks_per_second": Engine.physics_ticks_per_second,
		"memory_static": Performance.get_monitor(Performance.MEMORY_STATIC),
		"memory_static_max": Performance.get_monitor(Performance.MEMORY_STATIC_MAX),
		"object_count": Performance.get_monitor(Performance.OBJECT_COUNT),
		"node_count": Performance.get_monitor(Performance.OBJECT_NODE_COUNT),
		"resource_count": Performance.get_monitor(Performance.OBJECT_RESOURCE_COUNT),
		"orphan_nodes": Performance.get_monitor(Performance.OBJECT_ORPHAN_NODE_COUNT),
		"video_mem_used": Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED),
		"texture_mem_used": Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED),
		"draw_calls_last_frame": Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),
		"primitives_last_frame": Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME),
	}

	# OS-level memory stats are useful when the user wants to know how heavy
	# the editor process itself has become.
	var mem_info := OS.get_memory_info()
	if mem_info is Dictionary:
		info["os_memory"] = {
			"physical": mem_info.get("physical", 0),
			"free": mem_info.get("free", 0),
			"available": mem_info.get("available", 0),
			"stack": mem_info.get("stack", 0),
		}

	# Editor-only hints
	var ei = get_editor_interface()
	if ei:
		info["editor_scale"] = ei.get_editor_scale()
		var current_scene := ei.get_edited_scene_root()
		info["edited_scene"] = current_scene.scene_file_path if current_scene else ""

	return {
		"success": true,
		"info": info,
		"timestamp_ms": Time.get_ticks_msec(),
	}
