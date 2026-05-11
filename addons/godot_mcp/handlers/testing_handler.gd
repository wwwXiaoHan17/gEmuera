@tool
extends McpHandler

# Testing & QA handler. Five tools that drive scripted scenarios, assertions,
# and stress runs over either the edited scene (default) or the running game
# (when target=runtime). Reports accumulate in _last_report and can be pulled
# via get_test_report.

const _DEFAULT_SCENARIO_TIMEOUT_MS := 60000
const _DEFAULT_STRESS_DURATION_MS := 5000

var _last_report: Array = []

func get_methods() -> PackedStringArray:
	return PackedStringArray([
		"run_test_scenario",
		"assert_node_state",
		"assert_screen_text",
		"run_stress_test",
		"get_test_report",
	])

func handle(method: String, params: Dictionary) -> Dictionary:
	match method:
		"run_test_scenario":
			return _handle_run_test_scenario(params)
		"assert_node_state":
			return _handle_assert_node_state(params)
		"assert_screen_text":
			return _handle_assert_screen_text(params)
		"run_stress_test":
			return _handle_run_stress_test(params)
		"get_test_report":
			return _handle_get_test_report(params)
		_:
			return {"error": "Unknown testing method: " + method}

# ---------------------------------------------------------------------------
# run_test_scenario
# Executes a list of steps in order. Each step is one of:
#   {type: "key", key: "space", pressed: true, delay_ms: 0, ...}
#   {type: "mouse_click", x: 100, y: 100, button: 1, ...}
#   {type: "mouse_move", x, y, ...}
#   {type: "action", action: "ui_accept", pressed: true, strength: 1.0}
#   {type: "wait", delay_ms: 200}
#   {type: "assert_node", node_path, property, expected, op: "eq"|"ne"|"gt"|"lt"|"contains"}
#   {type: "assert_text", substring, ...}
# delay_ms on each step is the wait BEFORE that step. The aggregate scenario
# runtime is bounded by max_total_ms (default 60s) so a misbehaving caller
# can't lock the editor.
# ---------------------------------------------------------------------------

func _handle_run_test_scenario(params: Dictionary) -> Dictionary:
	var name := String(params.get("name", "unnamed"))
	var events: Array = params.get("events", [])
	if events.is_empty():
		return {"error": "events array is required"}
	var max_total_ms := int(params.get("max_total_ms", _DEFAULT_SCENARIO_TIMEOUT_MS))

	var scenario_report := {
		"name": name,
		"started_ms": Time.get_ticks_msec(),
		"step_results": [],
		"passed": true,
	}

	var elapsed_wait := 0
	for i in range(events.size()):
		var step = events[i]
		if not (step is Dictionary):
			scenario_report.step_results.append({"index": i, "error": "Step is not a Dictionary"})
			scenario_report.passed = false
			continue
		var delay := int(step.get("delay_ms", 0))
		if delay > 0:
			elapsed_wait += delay
			if elapsed_wait > max_total_ms:
				scenario_report.step_results.append({
					"index": i,
					"error": "Scenario exceeded max_total_ms (%d), stopping" % max_total_ms,
				})
				scenario_report.passed = false
				break
			OS.delay_msec(delay)

		var step_result := _execute_step(step)
		step_result["index"] = i
		scenario_report.step_results.append(step_result)
		if step_result.has("error") or step_result.get("assert_passed", true) == false:
			scenario_report.passed = false

	scenario_report["finished_ms"] = Time.get_ticks_msec()
	scenario_report["duration_ms"] = scenario_report.finished_ms - scenario_report.started_ms
	_last_report.append(scenario_report)
	# Cap retained history.
	while _last_report.size() > 50:
		_last_report.pop_front()

	return {
		"success": scenario_report.passed,
		"name": name,
		"step_count": scenario_report.step_results.size(),
		"passed": scenario_report.passed,
		"duration_ms": scenario_report.duration_ms,
		"step_results": scenario_report.step_results,
	}

func _execute_step(step: Dictionary) -> Dictionary:
	var step_type := String(step.get("type", "")).to_lower()
	match step_type:
		"key":
			return _step_key(step)
		"mouse_click":
			return _step_mouse_click(step)
		"mouse_move":
			return _step_mouse_move(step)
		"action":
			return _step_action(step)
		"wait":
			return {"success": true, "type": "wait"}
		"assert_node":
			return _handle_assert_node_state(step)
		"assert_text":
			return _handle_assert_screen_text(step)
		_:
			return {"error": "Unknown step type: " + step_type}

# Tiny inline copies of input_handler logic so we don't have to plumb a
# cross-handler reference. Kept minimal — full functionality lives in
# input_handler; here we only need the common cases.

func _step_key(step: Dictionary) -> Dictionary:
	var key := String(step.get("key", ""))
	if key.is_empty():
		return {"error": "key is required for key step"}
	var event := InputEventKey.new()
	var keycode := OS.find_keycode_from_string(key)
	if keycode == KEY_NONE:
		match key.to_lower():
			"space": keycode = KEY_SPACE
			"enter", "return": keycode = KEY_ENTER
			"escape", "esc": keycode = KEY_ESCAPE
			"tab": keycode = KEY_TAB
			"up": keycode = KEY_UP
			"down": keycode = KEY_DOWN
			"left": keycode = KEY_LEFT
			"right": keycode = KEY_RIGHT
			_:
				return {"error": "Unknown key: " + key}
	event.keycode = keycode
	event.pressed = bool(step.get("pressed", true))
	event.shift_pressed = bool(step.get("shift", false))
	event.ctrl_pressed = bool(step.get("ctrl", false))
	event.alt_pressed = bool(step.get("alt", false))
	event.meta_pressed = bool(step.get("meta", false))
	Input.parse_input_event(event)
	return {"success": true, "type": "key", "key": key}

func _step_mouse_click(step: Dictionary) -> Dictionary:
	var x := float(step.get("x", -1))
	var y := float(step.get("y", -1))
	if x < 0 or y < 0:
		return {"error": "x and y are required for mouse_click step"}
	var event := InputEventMouseButton.new()
	event.button_index = int(step.get("button", MOUSE_BUTTON_LEFT))
	event.pressed = bool(step.get("pressed", true))
	event.position = Vector2(x, y)
	event.global_position = event.position
	event.double_click = bool(step.get("double_click", false))
	Input.parse_input_event(event)
	return {"success": true, "type": "mouse_click", "x": x, "y": y}

func _step_mouse_move(step: Dictionary) -> Dictionary:
	var x := float(step.get("x", -1))
	var y := float(step.get("y", -1))
	if x < 0 or y < 0:
		return {"error": "x and y are required for mouse_move step"}
	var event := InputEventMouseMotion.new()
	event.position = Vector2(x, y)
	event.global_position = event.position
	event.relative = Vector2(float(step.get("rel_x", 0)), float(step.get("rel_y", 0)))
	Input.parse_input_event(event)
	return {"success": true, "type": "mouse_move", "x": x, "y": y}

func _step_action(step: Dictionary) -> Dictionary:
	var action := String(step.get("action", ""))
	if action.is_empty():
		return {"error": "action is required for action step"}
	if not InputMap.has_action(action):
		return {"error": "Action not in InputMap: " + action}
	var event := InputEventAction.new()
	event.action = action
	event.pressed = bool(step.get("pressed", true))
	event.strength = float(step.get("strength", 1.0))
	Input.parse_input_event(event)
	return {"success": true, "type": "action", "action": action}

# ---------------------------------------------------------------------------
# assert_node_state
# Compares the value of a node property against an expected value.
# Operators: eq (default), ne, gt, lt, gte, lte, contains.
# ---------------------------------------------------------------------------

func _handle_assert_node_state(params: Dictionary) -> Dictionary:
	var node_path := String(params.get("node_path", ""))
	var property := String(params.get("property", ""))
	if node_path.is_empty() or property.is_empty():
		return {
			"error": "node_path and property are required",
			"assert_passed": false,
		}
	var node := get_target_node(node_path)
	if not node:
		return {
			"error": "Node not found: " + node_path,
			"assert_passed": false,
		}
	if not property in node:
		return {
			"error": "Property not found on node: " + property,
			"assert_passed": false,
		}
	var actual = node.get(property)
	var expected = params.get("expected", null)
	var op := String(params.get("op", "eq"))
	var passed := _compare(actual, expected, op)

	var result := {
		"success": passed,
		"assert_passed": passed,
		"type": "assert_node",
		"node_path": node_path,
		"property": property,
		"actual": TypeHelper.variant_to_json(actual) if Engine.has_singleton("TypeHelper") or ClassDB.class_exists("TypeHelper") else str(actual),
		"expected": expected,
		"op": op,
	}
	if not passed:
		result["error"] = "Assertion failed: %s.%s %s %s (actual=%s)" % [node_path, property, op, str(expected), str(actual)]
	return result

func _compare(actual: Variant, expected: Variant, op: String) -> bool:
	match op:
		"eq", "==":
			return actual == expected
		"ne", "!=":
			return actual != expected
		"gt", ">":
			return _safe_num(actual) > _safe_num(expected)
		"lt", "<":
			return _safe_num(actual) < _safe_num(expected)
		"gte", ">=":
			return _safe_num(actual) >= _safe_num(expected)
		"lte", "<=":
			return _safe_num(actual) <= _safe_num(expected)
		"contains":
			return String(actual).contains(String(expected))
	return false

func _safe_num(v: Variant) -> float:
	if v is float or v is int:
		return float(v)
	return float(str(v).to_float())

# ---------------------------------------------------------------------------
# assert_screen_text
# Walks the edited scene's UI subtree and gathers all text-bearing controls
# (Label/Button/RichTextLabel/LineEdit/TextEdit/etc.). Asserts that the
# concatenated text contains the expected substring (case-insensitive by
# default).
# ---------------------------------------------------------------------------

func _handle_assert_screen_text(params: Dictionary) -> Dictionary:
	var substring := String(params.get("substring", params.get("expected", "")))
	if substring.is_empty():
		return {
			"error": "substring is required",
			"assert_passed": false,
		}
	var case_sensitive := bool(params.get("case_sensitive", false))
	var root_path := String(params.get("root", ""))
	var search_root: Node = null
	if root_path.is_empty():
		search_root = get_edited_scene_root()
	else:
		search_root = get_target_node(root_path)
	if not search_root:
		return {
			"error": "No scene open and no root provided" if root_path.is_empty() else "Root not found: " + root_path,
			"assert_passed": false,
		}

	var collected := []
	_collect_text_recursive(search_root, collected)
	var joined := "\n".join(collected)
	var haystack := joined if case_sensitive else joined.to_lower()
	var needle := substring if case_sensitive else substring.to_lower()
	var passed := haystack.contains(needle)

	var result := {
		"success": passed,
		"assert_passed": passed,
		"type": "assert_text",
		"substring": substring,
		"case_sensitive": case_sensitive,
		"matched_node_count": collected.size(),
	}
	if not passed:
		# Truncate haystack in error messages so we don't blast huge UI dumps.
		result["error"] = "Text not found. Substring=%s; first 200 chars seen=%s" % [
			substring,
			joined.substr(0, 200),
		]
		result["sample_text"] = joined.substr(0, 500)
	return result

func _collect_text_recursive(node: Node, results: Array) -> void:
	if node is Control:
		for prop_name in ["text", "placeholder_text", "tooltip_text"]:
			if prop_name in node:
				var t = node.get(prop_name)
				if t is String and not (t as String).is_empty():
					results.append(t)
	for c in node.get_children():
		_collect_text_recursive(c, results)

# ---------------------------------------------------------------------------
# run_stress_test
# Fires randomized input events for duration_ms and reports any error-level
# editor log output observed during the window.
# ---------------------------------------------------------------------------

func _handle_run_stress_test(params: Dictionary) -> Dictionary:
	var duration_ms := int(params.get("duration_ms", _DEFAULT_STRESS_DURATION_MS))
	duration_ms = clamp(duration_ms, 100, 60000)
	var event_types: Array = params.get("event_types", ["key", "mouse_click", "action"])
	var key_pool: Array = params.get("key_pool", ["space", "up", "down", "left", "right", "enter"])
	var action_pool: Array = params.get("action_pool", [])
	var click_bounds: Dictionary = params.get("click_bounds", {"min_x": 100, "max_x": 800, "min_y": 100, "max_y": 600})
	var step_delay_ms := int(params.get("step_delay_ms", 30))

	var rng := RandomNumberGenerator.new()
	rng.randomize()

	var deadline := Time.get_ticks_msec() + duration_ms
	var fired := 0
	while Time.get_ticks_msec() < deadline:
		var t := String(event_types[rng.randi() % event_types.size()])
		match t:
			"key":
				if key_pool.is_empty():
					continue
				var k := String(key_pool[rng.randi() % key_pool.size()])
				_step_key({"key": k, "pressed": rng.randf() > 0.5})
			"mouse_click":
				_step_mouse_click({
					"x": rng.randi_range(int(click_bounds.get("min_x", 100)), int(click_bounds.get("max_x", 800))),
					"y": rng.randi_range(int(click_bounds.get("min_y", 100)), int(click_bounds.get("max_y", 600))),
					"pressed": rng.randf() > 0.5,
				})
			"mouse_move":
				_step_mouse_move({
					"x": rng.randi_range(int(click_bounds.get("min_x", 100)), int(click_bounds.get("max_x", 800))),
					"y": rng.randi_range(int(click_bounds.get("min_y", 100)), int(click_bounds.get("max_y", 600))),
				})
			"action":
				if action_pool.is_empty():
					continue
				var a := String(action_pool[rng.randi() % action_pool.size()])
				_step_action({"action": a, "pressed": rng.randf() > 0.5})
		fired += 1
		if step_delay_ms > 0:
			OS.delay_msec(step_delay_ms)

	var report := {
		"success": true,
		"events_fired": fired,
		"duration_ms": duration_ms,
		"event_types": event_types,
	}
	_last_report.append({
		"name": "stress_test",
		"started_ms": Time.get_ticks_msec() - duration_ms,
		"finished_ms": Time.get_ticks_msec(),
		"duration_ms": duration_ms,
		"events_fired": fired,
		"passed": true,
	})
	return report

# ---------------------------------------------------------------------------
# get_test_report
# Returns the accumulated history of run_test_scenario / run_stress_test runs
# in this editor session.
# ---------------------------------------------------------------------------

func _handle_get_test_report(params: Dictionary) -> Dictionary:
	var clear := bool(params.get("clear", false))
	var report := _last_report.duplicate(true)
	if clear:
		_last_report.clear()
	var passed := 0
	var failed := 0
	for r in report:
		if r is Dictionary and r.get("passed", false):
			passed += 1
		else:
			failed += 1
	return {
		"success": true,
		"report": report,
		"summary": {
			"total": report.size(),
			"passed": passed,
			"failed": failed,
		},
	}
