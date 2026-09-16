extends GdUnitTestSuite

const RUNTIME_SCRIPT := preload("res://Scripts/GodotHost/PrototypeRuntimeNode.cs")
const STATUS_VIEW_SCRIPT := preload("res://Scripts/GodotHost/PrototypeStatusView.cs")
const INPUT_BRIDGE_SCRIPT := preload("res://Scripts/GodotHost/PrototypeInputBridge.cs")
const AUDIO_BRIDGE_SCRIPT := preload("res://Scripts/GodotHost/PrototypeAudioBridge.cs")
const RESOURCE_BRIDGE_SCRIPT := preload("res://Scripts/GodotHost/PrototypeResourceBridge.cs")
const COMMAND_PANEL_SCRIPT := preload("res://Scripts/GodotHost/PrototypeCommandPanel.cs")

const COMMAND_TIMEOUT_FRAMES := 120


func test_reload_button_reloads_the_prototype_session() -> void:
	var host := _create_prototype_host()
	var panel: Control = host.get_node("PrototypeCommandPanel")
	var session_label := _find_session_label(panel)

	assert_bool(await _wait_until(func() -> bool:
		return " | gen 1 | " in session_label.text
	)).is_true()

	_find_button(panel, "Reload").emit_signal("pressed")

	assert_bool(await _wait_until(func() -> bool:
		return " | gen 2 | " in session_label.text
	)).is_true()


func test_toggle_button_switches_the_prototype_profile() -> void:
	var host := _create_prototype_host()
	var panel: Control = host.get_node("PrototypeCommandPanel")
	var session_label := _find_session_label(panel)

	assert_bool(await _wait_until(func() -> bool:
		return " | gen 1 | " in session_label.text
	)).is_true()
	var initial_profile := session_label.text.get_slice(" | ", 0)

	_find_button(panel, "Toggle").emit_signal("pressed")

	assert_bool(await _wait_until(func() -> bool:
		return " | gen 2 | " in session_label.text
	)).is_true()
	assert_str(session_label.text.get_slice(" | ", 0)).is_not_equal(initial_profile)


func test_detach_button_detaches_the_prototype_session() -> void:
	var host := _create_prototype_host()
	var panel: Control = host.get_node("PrototypeCommandPanel")
	var session_label := _find_session_label(panel)
	var status_label := _find_status_label(panel)

	assert_bool(await _wait_until(func() -> bool:
		return " | gen 1 | " in session_label.text
	)).is_true()

	_find_button(panel, "Detach").emit_signal("pressed")

	assert_bool(await _wait_until(func() -> bool:
		return "detached" in status_label.text.to_lower()
	)).is_true()
	assert_str(session_label.text).starts_with("detached | gen 1 |")


func test_observational_input_is_captured_without_consuming_the_event() -> void:
	var viewport := auto_free(SubViewport.new()) as SubViewport
	viewport.size = Vector2i(64, 64)
	var bridge: Node = INPUT_BRIDGE_SCRIPT.new()
	bridge.name = "PrototypeInputBridge"
	viewport.add_child(bridge)
	add_child(viewport)
	await await_idle_frame()
	var captures_unhandled_input: bool = bridge.get("CaptureUnhandledInput")
	assert_bool(captures_unhandled_input).is_true()
	var consumes_unhandled_input: bool = bridge.get("ConsumeUnhandledInput")
	assert_bool(consumes_unhandled_input).is_false()

	var event := InputEventKey.new()
	event.keycode = KEY_A
	event.physical_keycode = KEY_A
	event.pressed = true
	bridge._unhandled_input(event)

	assert_int(bridge.get("ActionQueueCount")).is_equal(1)
	assert_bool(viewport.is_input_handled()).is_false()


func _create_prototype_host() -> Node:
	var host := auto_free(Node.new()) as Node
	host.name = "PrototypeHost"
	_add_scripted_child(host, RUNTIME_SCRIPT, "PrototypeRuntime")
	_add_scripted_child(host, STATUS_VIEW_SCRIPT, "PrototypeStatusView")
	_add_scripted_child(host, INPUT_BRIDGE_SCRIPT, "PrototypeInputBridge")
	_add_scripted_child(host, AUDIO_BRIDGE_SCRIPT, "PrototypeAudioBridge")
	_add_scripted_child(host, RESOURCE_BRIDGE_SCRIPT, "PrototypeResourceBridge")
	_add_scripted_child(host, COMMAND_PANEL_SCRIPT, "PrototypeCommandPanel")
	add_child(host)
	return host


func _add_scripted_child(parent: Node, script: Script, node_name: StringName) -> Node:
	var child: Node = script.new()
	child.name = node_name
	parent.add_child(child)
	return child


func _find_button(panel: Control, text: String) -> Button:
	for child in panel.find_children("*", "Button", true, false):
		var button := child as Button
		if button != null and button.text == text:
			return button
	fail("Missing prototype command button: %s" % text)
	return null


func _find_session_label(panel: Control) -> Label:
	var root := panel.get_child(0) as VBoxContainer
	assert_object(root).is_not_null()
	return root.get_child(0) as Label


func _find_status_label(panel: Control) -> Label:
	var root := panel.get_child(0) as VBoxContainer
	assert_object(root).is_not_null()
	return root.get_child(1) as Label


func _wait_until(predicate: Callable, max_frames := COMMAND_TIMEOUT_FRAMES) -> bool:
	for _frame in range(max_frames):
		if predicate.call():
			return true
		await await_idle_frame()
	return predicate.call()
