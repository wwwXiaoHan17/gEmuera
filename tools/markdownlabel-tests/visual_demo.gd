extends SceneTree
## Visual verification demo for the MarkdownLabel rewrite.
##
## Rendering requires a real (non-headless) display; the headless renderer has no
## viewport texture. Run from a desktop session:
##
##   & "<Godot mono 4.7 console exe>" --path "<project root>" \
##       --script "res://tools/markdownlabel-tests/visual_demo.gd" --resolution 900x1200
##
## The script renders a sample document (headings, block quotes with nesting, lists,
## task list, tables with alignment/header styling/zebra rows, an image, a fenced code
## block, a horizontal rule and inline formatting), then saves the whole window to
## D:\gemuera-reports\markdownlabel-visual.png and exits 0.
##
## Exit codes: 0 = PNG written, 1 = rendering/saving failed (details on stderr).

const LABEL_SCRIPT_PATH := "res://addons/markdownlabel/markdownlabel.gd"
const SCREENSHOT_PATH := "res://addons/markdownlabel/assets/screenshot.png"
const OUTPUT_DIR := "D:/gemuera-reports"
const OUTPUT_PATH := "D:/gemuera-reports/markdownlabel-visual.png"
const WINDOW_SIZE := Vector2i(900, 1200)

var _label = null


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	print("visual_demo: Godot %s | requested window %s" % [Engine.get_version_info().string, str(WINDOW_SIZE)])
	if root == null:
		push_error("visual_demo: no root Window (is this running with --headless?)")
		quit(1)
		return
	var label_script: Script = load(LABEL_SCRIPT_PATH)
	if label_script == null:
		push_error("visual_demo: could not load %s (GDScript parse error in the addon?)" % LABEL_SCRIPT_PATH)
		quit(1)
		return

	# Fill the window with a light background so dark text and table borders are visible.
	var background := ColorRect.new()
	background.color = Color(0.98, 0.98, 0.96)
	root.add_child(background)
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 28)
	margin.add_theme_constant_override("margin_right", 28)
	margin.add_theme_constant_override("margin_top", 20)
	margin.add_theme_constant_override("margin_bottom", 20)
	root.add_child(margin)
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	_label = label_script.new()
	if _label == null:
		push_error("visual_demo: script.new() returned null")
		quit(1)
		return
	_label.bbcode_enabled = true
	_label.fit_content = false
	_label.scroll_active = false
	_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_label.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_label.add_theme_color_override("default_color", Color(0.10, 0.10, 0.12))
	margin.add_child(_label)

	# Let the layout settle so image width fitting happens against the final label width.
	await process_frame
	await process_frame
	_apply_demo_styles()
	_label.markdown_text = _demo_markdown()

	# Wait for the deferred update + rendering to produce a complete frame.
	for _frame in range(15):
		await process_frame

	var image: Image = root.get_texture().get_image()
	if image == null:
		push_error("visual_demo: root window texture has no image (rendering unavailable)")
		quit(1)
		return
	DirAccess.make_dir_recursive_absolute(OUTPUT_DIR)
	var save_error: int = image.save_png(OUTPUT_PATH)
	if save_error != OK:
		push_error("visual_demo: failed to save %s (error %d)" % [OUTPUT_PATH, save_error])
		quit(1)
		return
	print("visual_demo: saved %s (%dx%d)" % [OUTPUT_PATH, image.get_width(), image.get_height()])
	quit(0)


func _apply_demo_styles() -> void:
	if _label == null:
		return
	# Table styling: visible header background, zebra data rows and default border/padding.
	_label.table_header_background = Color(0.84, 0.90, 0.98, 1.0)
	_label.table_row_background = Color(0.97, 0.97, 0.97, 1.0)
	_label.table_alternate_row_background = Color(0.91, 0.91, 0.91, 1.0)
	# Standalone images are centered; wide images are fitted to the label width.
	_label.image_alignment = "center"
	# A subtle block quote background makes the indent visible next to the bar.
	_label.blockquote_background_color = Color(0.93, 0.95, 0.99, 1.0)
	_label.blockquote_bar_color = Color(0.35, 0.38, 0.45)
	# Horizontal rules default to white; use a dark grey so they show on the light demo background.
	_label.hr_color = Color(0.45, 0.45, 0.45)


func _demo_markdown() -> String:
	return "\n".join([
		"# MarkdownLabel visual check",
		"",
		"Inline: **bold**, *italic*, ***bold italic***, ~~strikethrough~~, `inline code` and a [link](https://example.com).",
		"",
		"## Block quotes",
		"",
		"> First-level quote with a left bar.",
		">",
		"> > Nested quote (second level).",
		">",
		"> - quoted list item A",
		"> - quoted list item B",
		"",
		"## Table",
		"",
		"| Left | Center | Right |",
		"| :--- | :---: | ---: |",
		"| alpha | beta | 123 |",
		"| gamma | delta | 456 |",
		"",
		"## Image",
		"",
		"![MarkdownLabel screenshot](" + SCREENSHOT_PATH + ")",
		"",
		"## Code and lists",
		"",
		"```gdscript",
		"func hello(name: String) -> void:",
		"    print(\"Hello, %s\" % name)",
		"```",
		"",
		"- unordered item one",
		"- unordered item two",
		"  - nested item",
		"- [ ] unfinished task",
		"- [x] finished task",
		"",
		"1. ordered item one",
		"2. ordered item two",
		"",
		"---",
		"",
		"Setext level-two heading",
		"---",
		"",
	])
