extends SceneTree

## 启动器「兼容」独立页签验收驱动（窗口模式截图 + 结构断言）。
## Run (windowed):
##   Godot --path "<project root>" --script "res://tools/launcher-compat-tab/run_check.gd"
## Exit code: 0 = all checks passed, 1 = at least one failed.
## Outputs: D:/gemuera-reports/launcher-compat-tab/{game_tab,compat_tab}.png
##
## 背景（2026-11-22）：高级兼容设置原是游戏页顶部内联折叠块，展开后其内容被
## 游戏页外层滚动条遮挡；已迁移为独立「兼容」页签。本驱动验证：
## 1) 游戏页不再含旧折叠开关（「兼容设置 ▸」）；
## 2) 侧栏「兼容」按钮存在，按下后出现「高级兼容设置」页（标题 + 高级兼容开关）；
## 3) 两张截图落盘供人工核对滚动条独立。

const OUT_DIR := "D:/gemuera-reports/launcher-compat-tab"
const SCENE_PATH := "res://assets/scenes/first_window.tscn"

var _frames := 0
var _failures: Array[String] = []


func _initialize() -> void:
	var err := change_scene_to_file(SCENE_PATH)
	if err != OK:
		push_error("change_scene failed: %d" % err)
		quit(1)


func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 30:
		_check_game_tab()
		_press_compat_tab()
	elif _frames == 60:
		_check_compat_tab()
		_finish()
		return true
	return false


func _check_game_tab() -> void:
	if _find_node_by_text(root, "兼容设置 ▸") != null:
		_failures.append("游戏页仍存在旧折叠开关「兼容设置 ▸」")
	_shot("game_tab")


func _press_compat_tab() -> void:
	var button := _find_rail_button(root)
	if button == null:
		_failures.append("侧栏未找到「兼容」按钮（text=兼容/Compat）")
		return
	button.emit_signal("pressed")


func _check_compat_tab() -> void:
	if _find_node_by_text(root, "高级兼容设置") == null:
		_failures.append("兼容页签未出现「高级兼容设置」标题")
	if _find_node_by_text(root, "高级兼容模式") == null:
		_failures.append("兼容页签未出现「高级兼容模式」开关")
	_shot("compat_tab")


func _finish() -> void:
	DirAccess.make_dir_recursive_absolute(OUT_DIR)
	if _failures.is_empty():
		print("LAUNCHER_COMPAT_TAB_CHECK: PASS")
	else:
		for failure in _failures:
			push_error("CHECK FAIL: " + failure)
		print("LAUNCHER_COMPAT_TAB_CHECK: FAIL (%d)" % _failures.size())
		quit(1)


func _shot(name: String) -> void:
	DirAccess.make_dir_recursive_absolute(OUT_DIR)
	var image := root.get_texture().get_image()
	var path := OUT_DIR + "/" + name + ".png"
	var err := image.save_png(path)
	if err != OK:
		_failures.append("截图失败 %s -> %d" % [path, err])


func _find_rail_button(node: Node) -> Button:
	for text in ["兼容", "Compat"]:
		var found := _find_node_by_text(node, text)
		if found is Button:
			return found
	return null


func _find_node_by_text(node: Node, text: String) -> Control:
	if node is Control:
		if node is Button and (node as Button).text == text:
			return node
		if node is Label and (node as Label).text == text:
			return node
		if node is CheckButton and (node as CheckButton).text == text:
			return node
	for child in node.get_children():
		var found := _find_node_by_text(child, text)
		if found != null:
			return found
	return null
