@tool
extends McpHandler

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"simulate_key",
		"simulate_mouse_click",
		"simulate_mouse_move",
		"simulate_action",
		"simulate_sequence",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"simulate_key":
			return _handle_simulate_key(params)
		"simulate_mouse_click":
			return _handle_simulate_mouse_click(params)
		"simulate_mouse_move":
			return _handle_simulate_mouse_move(params)
		"simulate_action":
			return _handle_simulate_action(params)
		"simulate_sequence":
			return _handle_simulate_sequence(params)
		_:
			return {"error": "Unknown input method: " + method}

func _handle_simulate_key(params: Dictionary) -> Dictionary:
	var key = params.get("key", "")
	var pressed = params.get("pressed", true)
	var shift = params.get("shift", false)
	var ctrl = params.get("ctrl", false)
	var alt = params.get("alt", false)
	var meta = params.get("meta", false)

	if key.is_empty():
		return {"error": "key is required"}

	var event = InputEventKey.new()
	var keycode = OS.find_keycode_from_string(key)
	if keycode == KEY_NONE:
		# Try to map common named keys
		match key.to_lower():
			"space": keycode = KEY_SPACE
			"enter", "return": keycode = KEY_ENTER
			"escape", "esc": keycode = KEY_ESCAPE
			"tab": keycode = KEY_TAB
			"backspace": keycode = KEY_BACKSPACE
			"delete", "del": keycode = KEY_DELETE
			"up": keycode = KEY_UP
			"down": keycode = KEY_DOWN
			"left": keycode = KEY_LEFT
			"right": keycode = KEY_RIGHT
			"home": keycode = KEY_HOME
			"end": keycode = KEY_END
			"pageup": keycode = KEY_PAGEUP
			"pagedown": keycode = KEY_PAGEDOWN
			"shift": keycode = KEY_SHIFT
			"ctrl", "control": keycode = KEY_CTRL
			"alt": keycode = KEY_ALT
			_:
				return {"error": "Unknown key: " + key}

	event.keycode = keycode
	event.pressed = pressed
	event.shift_pressed = shift
	event.ctrl_pressed = ctrl
	event.alt_pressed = alt
	event.meta_pressed = meta

	Input.parse_input_event(event)
	return {"success": true, "key": key, "pressed": pressed}

func _handle_simulate_mouse_click(params: Dictionary) -> Dictionary:
	var x = params.get("x", -1)
	var y = params.get("y", -1)
	var button = params.get("button", MOUSE_BUTTON_LEFT)
	var pressed = params.get("pressed", true)
	var double_click = params.get("double_click", false)
	var node_path = params.get("node_path", "")

	var position = Vector2(x, y)

	# If node_path is provided, compute center position
	if not node_path.is_empty():
		var root = get_edited_scene_root()
		if root:
			var node = root.get_node_or_null(node_path)
			if node:
				if node is Control:
					position = node.get_global_transform_with_canvas().origin + node.size / 2
				else:
					position = node.get_global_transform_with_canvas().origin
			else:
				return {"error": "Node not found: " + node_path}
		else:
			return {"error": "No scene is currently open"}

	if x < 0 or y < 0:
		return {"error": "x and y coordinates are required, or provide node_path"}

	var event = InputEventMouseButton.new()
	event.button_index = button
	event.pressed = pressed
	event.double_click = double_click
	event.position = position
	event.global_position = position

	Input.parse_input_event(event)
	return {"success": true, "position": {"x": position.x, "y": position.y}, "button": button, "pressed": pressed}

func _handle_simulate_mouse_move(params: Dictionary) -> Dictionary:
	var x = params.get("x", -1)
	var y = params.get("y", -1)
	var relative_x = params.get("relative_x", 0)
	var relative_y = params.get("relative_y", 0)
	var node_path = params.get("node_path", "")

	var position = Vector2(x, y)
	var relative = Vector2(relative_x, relative_y)

	if not node_path.is_empty():
		var root = get_edited_scene_root()
		if root:
			var node = root.get_node_or_null(node_path)
			if node:
				position = node.get_global_transform_with_canvas().origin
			else:
				return {"error": "Node not found: " + node_path}
		else:
			return {"error": "No scene is currently open"}
	elif x < 0 or y < 0:
		return {"error": "x and y coordinates are required, or provide node_path"}

	var event = InputEventMouseMotion.new()
	event.position = position
	event.global_position = position
	event.relative = relative

	Input.parse_input_event(event)
	return {"success": true, "position": {"x": position.x, "y": position.y}, "relative": {"x": relative.x, "y": relative.y}}

func _handle_simulate_action(params: Dictionary) -> Dictionary:
	var action = params.get("action", "")
	var pressed = params.get("pressed", true)
	var strength = params.get("strength", 1.0)

	if action.is_empty():
		return {"error": "action is required"}

	if not InputMap.has_action(action):
		return {"error": "Action not found in InputMap: " + action}

	var event = InputEventAction.new()
	event.action = action
	event.pressed = pressed
	event.strength = strength

	Input.parse_input_event(event)
	return {"success": true, "action": action, "pressed": pressed, "strength": strength}

# Run a series of input events with optional inter-event delay. Each event is
# a dict with `type` ∈ {"key","mouse_click","mouse_move","action","wait"} plus
# whatever payload that event needs. delay_ms on a step is the wait *before*
# that step. Total duration is capped so a misbehaving caller can't hang the
# editor; the cap defaults to 30 seconds and can be raised via max_total_ms.
func _handle_simulate_sequence(params: Dictionary) -> Dictionary:
	var events: Array = params.get("events", [])
	if events.is_empty():
		return {"error": "events array is required and must not be empty"}
	var max_total_ms := int(params.get("max_total_ms", 30000))

	var results: Array = []
	var total_wait := 0
	for i in range(events.size()):
		var step = events[i]
		if not (step is Dictionary):
			results.append({"index": i, "error": "Step is not a Dictionary"})
			continue
		var delay := int(step.get("delay_ms", 0))
		if delay > 0:
			total_wait += delay
			if total_wait > max_total_ms:
				results.append({
					"index": i,
					"error": "Sequence exceeded max_total_ms (%d), stopping" % max_total_ms,
				})
				break
			OS.delay_msec(delay)
		var step_type := String(step.get("type", "")).to_lower()
		var step_result: Dictionary
		match step_type:
			"key":
				step_result = _handle_simulate_key(step)
			"mouse_click":
				step_result = _handle_simulate_mouse_click(step)
			"mouse_move":
				step_result = _handle_simulate_mouse_move(step)
			"action":
				step_result = _handle_simulate_action(step)
			"wait":
				# delay_ms already consumed above; nothing else to do.
				step_result = {"success": true, "type": "wait", "waited_ms": delay}
			_:
				step_result = {"error": "Unknown step type: " + step_type}
		step_result["index"] = i
		results.append(step_result)

	var failures := 0
	for r in results:
		if r.has("error"):
			failures += 1
	return {
		"success": failures == 0,
		"failure_count": failures,
		"step_count": results.size(),
		"results": results,
		"total_delay_ms": total_wait,
	}
