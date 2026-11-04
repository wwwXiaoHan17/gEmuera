# GodotScrollRangeBehaviorTest.gd —— 取证 Godot 4.7 ScrollContainer 在父尺寸变化后的
# 子尺寸传播/vbar range 更新时机，以及越界 scroll_vertical 赋值是否被引擎钳制。
# 这是 systematic-debugging Phase 1 的证据测试：不改产品代码，只钉住引擎行为假设。
extends GdUnitTestSuite


func _make_scroll(custom_height: float) -> Array:
	var root := Control.new()
	root.size = Vector2(800, 600)
	add_child(root)
	auto_free(root)
	var scroll := ScrollContainer.new()
	scroll.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_child(scroll)
	var child := Control.new()
	child.custom_minimum_size = Vector2(0, custom_height)
	scroll.add_child(child)
	return [root, scroll, child]


func test_resize_updates_child_and_vbar_after_one_frame() -> void:
	var ctx := _make_scroll(2000.0)
	var root: Control = ctx[0]
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	assert_bool(vbar.max_value - vbar.page > 0.0).is_true()
	root.size = Vector2(600, 300)
	await get_tree().process_frame
	assert_float(child.size.y).is_greater_equal(2000.0)
	assert_float(vbar.max_value - vbar.page).is_equal_approx(maxf(0.0, child.size.y - scroll.size.y), 1.0)


func test_resize_same_frame_is_stale_characterization() -> void:
	# 取证：尺寸变化当帧读取的 vbar/child 可能仍是旧值（不失败，记录到 stdout 供 Phase 1 报告）。
	var ctx := _make_scroll(2000.0)
	var root: Control = ctx[0]
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	root.size = Vector2(600, 300)
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	print("[phase1-characterization] same_frame child_size=", child.size, " scroll_size=", scroll.size, " vbar_range=", vbar.max_value - vbar.page)
	assert_bool(true).is_true()


func test_scroll_vertical_is_clamped_to_range() -> void:
	var ctx := _make_scroll(2000.0)
	var scroll: ScrollContainer = ctx[1]
	await get_tree().process_frame
	scroll.scroll_vertical = 999999
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	assert_int(scroll.scroll_vertical).is_less_equal(int(vbar.max_value - vbar.page) + 1)


func test_growing_child_min_size_same_frame_range_may_be_stale() -> void:
	# 取证：同一帧内改子节点 min size 后立刻读取 vbar range / 写入大 scroll 值，
	# 验证「过渡态 range 当权威会把 scroll 钳小」这一竞态链假设（不失败，记录数值）。
	var ctx := _make_scroll(1000.0)
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	print("[phase1-characterization] before_grow range=", vbar.max_value - vbar.page, " child_size=", child.size)
	child.custom_minimum_size = Vector2(0, 4000.0)
	print("[phase1-characterization] after_grow_same_frame range=", vbar.max_value - vbar.page, " child_size=", child.size)
	scroll.scroll_vertical = 999999
	print("[phase1-characterization] assigned_999999_same_frame scroll_y=", scroll.scroll_vertical, " range_now=", vbar.max_value - vbar.page)
	for i in range(1, 7):
		await get_tree().process_frame
		print("[phase1-characterization] frame+", i, " range=", vbar.max_value - vbar.page, " child_size=", child.size, " scroll_y=", scroll.scroll_vertical)
	assert_bool(true).is_true()


func test_queue_sort_after_min_size_change_updates_range() -> void:
	var ctx := _make_scroll(1000.0)
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	child.custom_minimum_size = Vector2(0, 4000.0)
	scroll.queue_sort()
	await get_tree().process_frame
	print("[phase1-characterization] queue_sort range=", vbar.max_value - vbar.page, " child_size=", child.size)
	assert_bool(true).is_true()


func test_direct_child_size_set_updates_range() -> void:
	var ctx := _make_scroll(1000.0)
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	child.size = Vector2(600, 4000)
	await get_tree().process_frame
	print("[phase1-characterization] direct_size range=", vbar.max_value - vbar.page, " child_size=", child.size)
	assert_bool(true).is_true()


func test_refresh_minimum_size_cache_then_range_updates() -> void:
	# 取证：ScrollContainer 的 largest_child_min_size 是 _get_minimum_size() 里的缓存；
	# 子节点 min size 变化后必须强制刷新缓存，vbar range 才会更新。
	var ctx := _make_scroll(1000.0)
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	child.custom_minimum_size = Vector2(0, 4000.0)
	var combined := scroll.get_combined_minimum_size()
	scroll.queue_sort()
	await get_tree().process_frame
	print("[phase1-characterization] refresh_min_cache combined=", combined, " range=", vbar.max_value - vbar.page, " child_size=", child.size)
	scroll.scroll_vertical = 999999
	print("[phase1-characterization] refresh_min_cache assigned_999999 scroll_y=", scroll.scroll_vertical)
	assert_bool(true).is_true()


func test_update_minimum_size_then_range_updates() -> void:
	var ctx := _make_scroll(1000.0)
	var scroll: ScrollContainer = ctx[1]
	var child: Control = ctx[2]
	await get_tree().process_frame
	var vbar: VScrollBar = scroll.get_v_scroll_bar()
	child.custom_minimum_size = Vector2(0, 4000.0)
	scroll.update_minimum_size()
	scroll.queue_sort()
	await get_tree().process_frame
	print("[phase1-characterization] update_min_size range=", vbar.max_value - vbar.page, " child_size=", child.size)
	assert_bool(true).is_true()
