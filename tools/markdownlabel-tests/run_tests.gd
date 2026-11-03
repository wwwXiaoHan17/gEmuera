extends SceneTree
## MarkdownLabel rewrite acceptance suite (contract tests).
##
## Spec (collaboration contract, project-external):
##   D:/gemuera-reports/markdownlabel-rewrite-spec.md
## Delivery scope for the harness owner: tools/markdownlabel-tests/**
##
## Run headless:
##   & "<Godot mono 4.7 console exe>" --headless --path "<project root>" \
##       --script "res://tools/markdownlabel-tests/run_tests.gd"
## Exit code: 0 = all cases passed, 1 = at least one failed (or the addon failed to load).
##
## Design notes:
## - Every case builds a fresh MarkdownLabel via script.new(); no scene tree is required.
## - Output is asserted three ways: exact/structural substrings, RichTextLabel semantics
##   (parse_bbcode + get_parsed_text, with "\t" indent characters normalized away), and a
##   tag-stack validator that rejects unknown tags, unbalanced/crossed tags, non-cell
##   children of [table] and block-level tags inside [cell].
## - Assertions follow the spec, NOT the current implementation. A failing case may simply
##   mean the parallel rewrite is not finished yet.

const LABEL_SCRIPT_PATH := "res://addons/markdownlabel/markdownlabel.gd"
const PARSER_SCRIPT_PATH := "res://addons/markdownlabel/markdown_parser.gd"
const SCREENSHOT_PATH := "res://addons/markdownlabel/assets/screenshot.png"

const KNOWN_TAGS := {
	"b": true, "i": true, "u": true, "s": true, "code": true,
	"color": true, "bgcolor": true, "fgcolor": true, "font": true, "font_size": true,
	"outline_size": true, "outline_color": true,
	"url": true, "hint": true, "img": true,
	"center": true, "right": true, "left": true, "indent": true,
	"ul": true, "ol": true, "table": true, "cell": true, "hr": true,
	"lang": true, "p": true, "fade": true, "shake": true, "wave": true,
	"tornado": true, "rainbow": true, "pulse": true, "dropcap": true,
	"opentype_features": true,
	"lb": true, "rb": true, "bs": true, "lrm": true, "shy": true,
	"zwj": true, "zwnj": true, "em": true, "en": true, "nbsp": true,
}
const VOID_TAGS := {
	"hr": true, "lb": true, "rb": true, "bs": true, "lrm": true, "shy": true,
	"zwj": true, "zwnj": true, "em": true, "en": true, "nbsp": true,
}
const BLOCK_TAGS := {
	"table": true, "ul": true, "ol": true, "indent": true, "hr": true, "p": true,
}

var _label_script: Script = null
var _parser_script: Script = null

var _last_label = null
var _last_text: String = ""

var _suite: String = ""
var _case: String = ""
var _case_errors: Array = []
var _assertion_count: int = 0
var _click_events: Array = []
var _passed: int = 0
var _failed: int = 0
var _failed_names: Array = []
var _start_msec: int = 0


# ============================================================
# Runner
# ============================================================

func _initialize() -> void:
	_start_msec = Time.get_ticks_msec()
	print("MarkdownLabel rewrite test suite (spec section 10 contract tests)")
	print("Godot: %s | addon: %s" % [Engine.get_version_info().string, LABEL_SCRIPT_PATH])
	print("spec:  D:/gemuera-reports/markdownlabel-rewrite-spec.md")
	print("")
	_label_script = load(LABEL_SCRIPT_PATH)
	_parser_script = load(PARSER_SCRIPT_PATH)
	if _label_script == null:
		print("FAIL  bootstrap :: load addon script")
		print("      | load('%s') returned null - GDScript parse error in the addon? (see engine output above)" % LABEL_SCRIPT_PATH)
		_passed = 0
		_failed = 1
		_failed_names.append("bootstrap :: load addon script")
		_finish()
		return

	_suite_self_check()
	_suite_bootstrap()
	_suite_style_properties()
	_suite_paragraphs()
	_suite_inline()
	_suite_code_blocks()
	_suite_headings()
	_suite_horizontal_rules()
	_suite_blockquotes()
	_suite_lists()
	_suite_tables()
	_suite_images()
	_suite_links()
	_suite_regressions()
	_finish()


func _finish() -> void:
	_free_last_label()
	var total: int = _passed + _failed
	var elapsed: float = float(Time.get_ticks_msec() - _start_msec) / 1000.0
	print("")
	print("============================================================")
	print("PASS %d / %d   (%.2fs)" % [_passed, total, elapsed])
	if _failed > 0:
		print("FAILED CASES (%d):" % _failed)
		for failed_name in _failed_names:
			print("  - %s" % failed_name)
		print("RESULT: FAIL")
		quit(1)
	else:
		print("RESULT: PASS")
		quit(0)


func _start_suite(name: String) -> void:
	_suite = name
	print("")
	print("-- suite: %s --" % name)


func _it(case_name: String, body: Callable) -> void:
	_free_last_label()
	_case = case_name
	_case_errors = []
	_assertion_count = 0
	body.call()
	if _case_errors.is_empty() and _assertion_count == 0:
		_case_errors.append("no assertions executed inside this case (runtime script error?)")
	if _case_errors.is_empty():
		_passed += 1
		print("PASS  %s :: %s" % [_suite, case_name])
	else:
		_failed += 1
		_failed_names.append("%s :: %s" % [_suite, case_name])
		print("FAIL  %s :: %s" % [_suite, case_name])
		for error_text in _case_errors:
			print("      | %s" % str(error_text))


# ============================================================
# Assertions
# ============================================================

func _fail(message: String) -> void:
	_assertion_count += 1
	_case_errors.append(message)


func _check(condition: bool, message: String) -> void:
	_assertion_count += 1
	if not condition:
		_case_errors.append(message)


func _eq(actual, expected, label: String) -> void:
	_assertion_count += 1
	if typeof(actual) != typeof(expected) or actual != expected:
		_case_errors.append("%s: expected %s, got %s" % [label, _repr(expected), _repr(actual)])


func _eq_approx(actual: float, expected: float, label: String) -> void:
	_assertion_count += 1
	if not is_equal_approx(actual, expected):
		_case_errors.append("%s: expected ~%s, got %s" % [label, str(expected), str(actual)])


func _has(haystack: String, needle: String, label: String) -> void:
	_assertion_count += 1
	if not haystack.contains(needle):
		_case_errors.append("%s: expected output to contain %s\n      actual output: %s" % [label, _repr(needle), _trunc(haystack)])


func _not_has(haystack: String, needle: String, label: String) -> void:
	_assertion_count += 1
	if haystack.contains(needle):
		_case_errors.append("%s: expected output NOT to contain %s\n      actual output: %s" % [label, _repr(needle), _trunc(haystack)])


func _has_text(out: String, needle: String, label: String) -> void:
	_has(_semantic(out), needle, label + " (parsed text)")


func _not_has_text(out: String, needle: String, label: String) -> void:
	_not_has(_semantic(out), needle, label + " (parsed text)")


func _eq_strip(actual: String, expected: String, label: String) -> void:
	_eq(actual.strip_edges(), expected, label + " (strip_edges)")


func _assert_valid(out: String, label: String) -> void:
	_assertion_count += 1
	var errors := _tag_errors(out)
	if not errors.is_empty():
		for error_text in errors:
			_case_errors.append("%s: invalid BBCode: %s\n      actual output: %s" % [label, str(error_text), _trunc(out)])


func _repr(value) -> String:
	if typeof(value) == TYPE_STRING:
		return JSON.stringify(value)
	return str(value)


func _trunc(text: String, max_length: int = 700) -> String:
	var flat: String = text.replace("\n", "\\n").replace("\t", "\\t")
	if flat.length() <= max_length:
		return flat
	return flat.substr(0, max_length) + "...<truncated>"


func _at_least(hex_version: int) -> bool:
	return Engine.get_version_info().hex >= hex_version


# ============================================================
# General helpers
# ============================================================

func _new_label(text: String = ""):
	_free_last_label()
	if _label_script == null:
		_fail("addon script is not loaded")
		return null
	var ml = _label_script.new()
	if ml == null:
		_fail("script.new() returned null")
		return null
	_last_label = ml
	_last_text = text
	ml.set("markdown_text", text)
	return ml


func _free_last_label() -> void:
	if _last_label != null and is_instance_valid(_last_label):
		_last_label.free()
	_last_label = null


func _convert(ml, text: String) -> String:
	if ml == null:
		return ""
	if not ml.has_method("_convert_markdown"):
		_fail("MarkdownLabel has no _convert_markdown() method")
		return ""
	var out = ml._convert_markdown(text)
	if typeof(out) != TYPE_STRING:
		# Tolerate a no-argument version of the entry point while the rewrite is in flight.
		out = ml._convert_markdown()
	if typeof(out) != TYPE_STRING:
		_fail("_convert_markdown() did not return a String (got %s)" % typeof(out))
		return ""
	return out


func _render(text: String, style: Dictionary = {}) -> String:
	var ml = _new_label(text)
	if ml == null:
		return ""
	for property_name in style:
		ml.set(property_name, style[property_name])
	return _convert(ml, text)


func _render_on(ml, text: String, style: Dictionary = {}) -> String:
	if ml == null:
		return ""
	_last_label = ml
	_last_text = text
	for property_name in style:
		ml.set(property_name, style[property_name])
	return _convert(ml, text)


## Renders with engine warnings suppressed (the missing-image fallback cases intentionally trigger
## the spec-mandated "image not found" push_warning, which would otherwise spam the test log).
func _render_quiet(text: String, style: Dictionary = {}) -> String:
	var previous := Engine.print_error_messages
	Engine.print_error_messages = false
	var out := _render(text, style)
	Engine.print_error_messages = previous
	return out


func _semantic(bbcode: String) -> String:
	var rtl := RichTextLabel.new()
	rtl.bbcode_enabled = true
	rtl.parse_bbcode(bbcode)
	var parsed: String = rtl.get_parsed_text()
	rtl.free()
	return parsed.replace("\t", "").replace("\r", "")


func _count(haystack: String, needle: String) -> int:
	return haystack.count(needle)


func _line_index(out: String, needle: String) -> int:
	var out_lines: PackedStringArray = out.split("\n")
	for index in range(out_lines.size()):
		if out_lines[index].contains(needle):
			return index
	return -1


func _code_content(out: String) -> String:
	var start: int = out.find("[code]")
	if start == -1:
		return ""
	var stop: int = out.find("[/code]", start)
	if stop == -1:
		return ""
	return out.substr(start + 6, stop - start - 6)


func _table_infos(out: String) -> Array:
	var infos: Array = []
	var table_regex := RegEx.create_from_string(r"\[table=(\d+)\]([\s\S]*?)\[/table\]")
	var cell_regex := RegEx.create_from_string(r"\[cell([^\]]*)\]([\s\S]*?)\[/cell\]")
	for table_match in table_regex.search_all(out):
		var info := {"cols": int(table_match.get_string(1)), "rows": []}
		var body: String = table_match.get_string(2)
		for row_line in body.split("\n"):
			if row_line.strip_edges() == "":
				continue
			var cells: Array = []
			for cell_match in cell_regex.search_all(row_line):
				cells.append({"attrs": cell_match.get_string(1), "content": cell_match.get_string(2)})
			if not cells.is_empty():
				info["rows"].append(cells)
		infos.append(info)
	return infos


func _first_table(out: String) -> Dictionary:
	var infos := _table_infos(out)
	if infos.is_empty():
		return {}
	return infos[0]


func _attr_value(attrs: String, attr_name: String) -> String:
	var attr_regex := RegEx.create_from_string("(?:^| )" + attr_name + "=([^ ]+)")
	var attr_match := attr_regex.search(attrs)
	if attr_match == null:
		return ""
	return attr_match.get_string(1)


func _row_attrs(table_info: Dictionary, row_index: int) -> Array:
	var result: Array = []
	if table_info.is_empty():
		return result
	var rows: Array = table_info["rows"]
	if row_index < 0 or row_index >= rows.size():
		return result
	for cell in rows[row_index]:
		result.append(cell["attrs"])
	return result


func _row_contents(table_info: Dictionary, row_index: int) -> Array:
	var result: Array = []
	if table_info.is_empty():
		return result
	var rows: Array = table_info["rows"]
	if row_index < 0 or row_index >= rows.size():
		return result
	for cell in rows[row_index]:
		result.append(cell["content"])
	return result


func _anchors() -> Dictionary:
	if _last_label == null or not is_instance_valid(_last_label):
		return {}
	for field_name in ["_header_anchor_paragraph", "anchors"]:
		if field_name in _last_label:
			var value = _last_label.get(field_name)
			if typeof(value) == TYPE_DICTIONARY:
				return value
	return _parser_anchors()


func _checkboxes() -> Dictionary:
	if _last_label == null or not is_instance_valid(_last_label):
		return {}
	for field_name in ["_checkbox_record", "checkbox_records"]:
		if field_name in _last_label:
			var value = _last_label.get(field_name)
			if typeof(value) == TYPE_DICTIONARY:
				return value
	return _parser_checkboxes()


func _make_parser():
	if _parser_script == null:
		_parser_script = load(PARSER_SCRIPT_PATH)
	if _parser_script == null:
		return null
	return _parser_script.new()


func _parser_anchors() -> Dictionary:
	var parser = _make_parser()
	if parser == null:
		return {}
	parser.convert(_last_text, _last_label)
	var value = parser.get("anchors")
	return value if typeof(value) == TYPE_DICTIONARY else {}


func _parser_checkboxes() -> Dictionary:
	var parser = _make_parser()
	if parser == null:
		return {}
	parser.convert(_last_text, _last_label)
	var value = parser.get("checkbox_records")
	return value if typeof(value) == TYPE_DICTIONARY else {}


func _style_defaults() -> Dictionary:
	return {
		"blockquote_bar_character": "│",
		"blockquote_bar_color": Color(0.55, 0.55, 0.55),
		"blockquote_override_text_color": false,
		"blockquote_text_color": Color(0.75, 0.75, 0.75),
		"blockquote_italic": false,
		"blockquote_background_color": Color(0, 0, 0, 0),
		"table_header_row": true,
		"table_header_bold": true,
		"table_header_background": Color(0, 0, 0, 0),
		"table_border_color": Color(0.55, 0.55, 0.55, 0.4),
		"table_cell_padding": Vector4i(3, 2, 3, 2),
		"table_row_background": Color(0, 0, 0, 0),
		"table_alternate_row_background": Color(0, 0, 0, 0),
		"table_default_alignment": "left",
		"image_fit_width": true,
		"image_alignment": "inline",
		"image_show_alt_when_missing": true,
		"image_allow_attributes": true,
		"autolink_bare_urls": true,
	}


func _style_alternates() -> Dictionary:
	return {
		"blockquote_bar_character": "▏",
		"blockquote_bar_color": Color.RED,
		"blockquote_override_text_color": true,
		"blockquote_text_color": Color.BLUE,
		"blockquote_italic": true,
		"blockquote_background_color": Color(0.1, 0.1, 0.1, 0.5),
		"table_header_row": false,
		"table_header_bold": false,
		"table_header_background": Color.RED,
		"table_border_color": Color.RED,
		"table_cell_padding": Vector4i(1, 1, 1, 1),
		"table_row_background": Color.RED,
		"table_alternate_row_background": Color.BLUE,
		"table_default_alignment": "center",
		"image_fit_width": false,
		"image_alignment": "center",
		"image_show_alt_when_missing": false,
		"image_allow_attributes": false,
		"autolink_bare_urls": false,
	}


# ============================================================
# BBCode tag-stack validator
# ============================================================

func _tag_errors(bbcode: String) -> Array:
	var errors: Array = []
	var tag_regex := RegEx.create_from_string(r"\[(/?)([A-Za-z_][A-Za-z0-9_]*)([^\]]*)\]")
	var stack: Array = []
	var position: int = 0
	for tag_match in tag_regex.search_all(bbcode):
		var preceding: String = bbcode.substr(position, tag_match.get_start() - position)
		_validate_text_chunk(preceding, stack, errors)
		var closing: bool = tag_match.get_string(1) == "/"
		var tag: String = tag_match.get_string(2).to_lower()
		if not KNOWN_TAGS.has(tag):
			errors.append("unknown BBCode tag [%s] at offset %d" % [tag, tag_match.get_start()])
		var is_void: bool = VOID_TAGS.has(tag)
		if closing:
			if is_void:
				errors.append("closing tag [/%s] used for a void tag" % tag)
			elif stack.is_empty():
				errors.append("unexpected closing tag [/%s] (no matching open tag)" % tag)
			elif stack[-1] != tag:
				errors.append("crossed closing tag [/%s], expected [/%s]" % [tag, stack[-1]])
			else:
				stack.pop_back()
		else:
			if not stack.is_empty() and stack[-1] == "table" and tag != "cell" and not is_void:
				errors.append("tag [%s] directly inside [table]; only [cell] is allowed" % tag)
			if is_void and not stack.is_empty() and stack[-1] == "table":
				errors.append("void tag [%s] directly inside [table]" % tag)
			if BLOCK_TAGS.has(tag) and stack.has("cell"):
				errors.append("block-level tag [%s] inside [cell] (tables allow inline formatting only)" % tag)
			if not is_void:
				stack.append(tag)
		position = tag_match.get_end()
	var tail: String = bbcode.substr(position)
	_validate_text_chunk(tail, stack, errors)
	for unclosed_tag in stack:
		errors.append("unclosed tag [%s]" % unclosed_tag)
	return errors


func _validate_text_chunk(text: String, stack: Array, errors: Array) -> void:
	if text.contains("["):
		errors.append("stray '[' in text chunk %s" % JSON.stringify(_trunc(text, 80)))
	if not stack.is_empty() and stack[-1] == "table" and text.strip_edges() != "":
		errors.append("text %s directly inside [table]; only cells are allowed" % JSON.stringify(_trunc(text.strip_edges(), 80)))


# ============================================================
# Suite: self-check (validates the harness itself)
# ============================================================

func _suite_self_check() -> void:
	_start_suite("self-check (harness internals)")

	_it("tag validator accepts spec-shaped valid BBCode", func() -> void:
		var valid_samples: Array = [
			"[b]x[/b]",
			"[font_size=36]T[/font_size]",
			"[url=https://example.com]t[/url]",
			"[hint=\"T\"][url=https://example.com]t[/url][/hint]",
			"[table=2]\n[cell border=#8c8c8c66]a[/cell][cell]b[/cell]\n[/table]",
			"[indent][ul]\n[color=#8c8c8cff]│ [/color]a[/ul][/indent]",
			"[hr height=2 width=90% align=center color=#ffffffff]",
			"[img alt=\"a\" tooltip=\"t\" width=100%]res://x.png[/img]",
			"[url={\"markdownlabel-checkbox\":true,\"id\":0,\"checked\":false}]X[/url]",
			"[lb]b[rb]",
			"[code]a[lb]b[rb][/code]",
		]
		for sample in valid_samples:
			var errors := _tag_errors(sample)
			_check(errors.is_empty(), "validator wrongly rejected %s -> %s" % [_repr(sample), str(errors)])
	)

	_it("tag validator rejects malformed BBCode", func() -> void:
		var invalid_samples: Array = [
			"[b]x[/i]",
			"[b]x",
			"[/b]",
			"[table=1][b]x[/b][/table]",
			"[table=1][cell]a[/cell]stray[/table]",
			"[foo]x[/foo]",
			"[table=1][cell][ul]x[/ul][/cell][/table]",
		]
		for sample in invalid_samples:
			var errors := _tag_errors(sample)
			_check(not errors.is_empty(), "validator wrongly accepted %s" % _repr(sample))
	)

	_it("semantic helper strips indent tabs", func() -> void:
		_eq(_semantic("[b]x[/b]"), "x", "plain semantic text")
		_eq(_semantic("[indent]a[/indent]"), "a", "indent tab removed")
		_eq(_semantic("[table=1]\n[cell]a[/cell]\n[/table]"), "a", "table cell semantic text")
	)

	_it("table parser helper reads rows, cells and attrs", func() -> void:
		var sample := "[table=2]\n[cell border=#8c8c8c66]a[/cell][cell]b[/cell]\n[cell]1[/cell][cell]2[/cell]\n[/table]"
		var infos := _table_infos(sample)
		_eq(infos.size(), 1, "table count")
		if infos.is_empty():
			return
		_eq(infos[0]["cols"], 2, "column count")
		_eq(infos[0]["rows"].size(), 2, "row count")
		_eq(_row_contents(infos[0], 0), ["a", "b"], "row 0 contents")
		_eq(_attr_value(_row_attrs(infos[0], 0)[0], "border"), "#8c8c8c66", "border attr")
	)

# ============================================================
# Suite: bootstrap
# ============================================================

func _suite_bootstrap() -> void:
	_start_suite("bootstrap")

	_it("addon script loads and instantiates a RichTextLabel", func() -> void:
		var ml = _new_label("")
		_check(ml != null, "script.new() failed")
		if ml == null:
			return
		_check(ml is RichTextLabel, "MarkdownLabel must extend RichTextLabel, got %s" % ml.get_class())
		_eq(ml.get("bbcode_enabled"), true, "bbcode_enabled must be true after instantiation")
		_check(ml.has_method("_convert_markdown"), "MarkdownLabel must keep _convert_markdown() (spec section 4)")
		_check(ml.has_method("_update"), "MarkdownLabel must keep _update()")
		_check(ml.has_method("queue_update"), "MarkdownLabel must keep queue_update()")
		_check(ml.has_method("display_file"), "MarkdownLabel must keep display_file()")
	)

	_it("empty markdown text produces empty output", func() -> void:
		var out := _render("")
		_check(out.strip_edges() == "", "empty input should render empty output, got %s" % _repr(out))
		_assert_valid(out, "empty input")
	)

	_it("plain text passes through without formatting", func() -> void:
		var out := _render("hello world")
		_eq_strip(out, "hello world", "plain text output")
		_assert_valid(out, "plain text")
	)

	_it("two source lines stay on separate output lines (no soft-wrap merge)", func() -> void:
		var out := _render("line one\nline two")
		_has_text(out, "line one", "first line")
		_has_text(out, "line two", "second line")
		_check(out.split("\n").size() >= 2, "expected at least two output lines, got %s" % _repr(out))
		_assert_valid(out, "two paragraphs")
	)

	_it("up to three leading spaces are ignored in paragraphs", func() -> void:
		var out := _render("   indented paragraph")
		_eq_strip(out, "indented paragraph", "leading spaces")
	)

	_it("whitespace-only input renders nothing", func() -> void:
		var out := _render("   \n\t\n ")
		_check(out.strip_edges() == "", "whitespace-only input should render empty output, got %s" % _repr(out))
		_assert_valid(out, "whitespace only")
	)

	_it("CRLF input behaves like LF input", func() -> void:
		var out_crlf := _render("alpha\r\nbeta")
		var out_lf := _render("alpha\nbeta")
		_not_has(out_crlf, "\r", "CRLF output must not keep carriage returns")
		_eq(out_crlf, out_lf, "CRLF vs LF output")
	)

	_it("raw BBCode passes through", func() -> void:
		var out := _render("[u]underlined[/u]")
		_has(out, "[u]", "raw underline open tag")
		_has(out, "[/u]", "raw underline close tag")
		_eq_strip(_semantic(out), "underlined", "raw BBCode semantic text")
		_assert_valid(out, "raw BBCode")
	)

	_it("raw BBCode mixed with markdown inline", func() -> void:
		var out := _render("[color=green]*x*[/color]")
		_has(out, "[color=green]", "raw color tag preserved")
		_has(out, "[i]x[/i]", "markdown italic inside raw BBCode")
		_eq_strip(_semantic(out), "x", "semantic text")
		_assert_valid(out, "raw BBCode + markdown")
	)


	_it("display_file strips YAML frontmatter and keeps the body", func() -> void:
		var temp_path := "user://markdownlabel_frontmatter_test.md"
		var temp_file := FileAccess.open(temp_path, FileAccess.WRITE)
		if temp_file == null:
			_fail("could not write %s (error %d)" % [temp_path, FileAccess.get_open_error()])
			return
		temp_file.store_string("---\ntitle: T\n---\n\n# Body")
		temp_file.close()
		var ml = _new_label("")
		if ml == null:
			DirAccess.remove_absolute(ProjectSettings.globalize_path(temp_path))
			return
		var result: int = ml.display_file(temp_path)
		_eq(result, OK, "display_file() must return OK")
		_eq(ml.get_frontmatter(), "title: T", "frontmatter is stored and trimmed")
		_has(ml.markdown_text, "# Body", "markdown_text keeps the document body")
		_not_has(ml.markdown_text, "---", "frontmatter fences are removed from markdown_text")
		var out := _convert(ml, ml.markdown_text)
		_not_has(out, "---", "frontmatter must not be rendered")
		_has(out, "[font_size=", "body heading is rendered")
		_has_text(out, "Body", "body text is rendered")
		_assert_valid(out, "display_file frontmatter")
		DirAccess.remove_absolute(ProjectSettings.globalize_path(temp_path))
	)


# ============================================================
# Suite: style properties (spec section 5)
# ============================================================

func _suite_style_properties() -> void:
	_start_suite("style properties (spec section 5)")

	_it("new style properties exist with the spec defaults and types", func() -> void:
		var ml = _new_label("")
		if ml == null:
			return
		var defaults := _style_defaults()
		for property_name in defaults:
			if not (property_name in ml):
				_fail("missing property '%s'" % property_name)
				continue
			var actual = ml.get(property_name)
			var expected = defaults[property_name]
			_eq(typeof(actual), typeof(expected), "type of '%s'" % property_name)
			if typeof(expected) == TYPE_COLOR:
				_check((actual as Color).is_equal_approx(expected), "'%s' default %s != %s" % [property_name, str(actual), str(expected)])
			elif typeof(expected) == TYPE_FLOAT:
				_eq_approx(float(actual), float(expected), "default of '%s'" % property_name)
			else:
				_eq(actual, expected, "default of '%s'" % property_name)
	)

	_it("existing markdownlabel properties keep their documented defaults", func() -> void:
		var ml = _new_label("")
		if ml == null:
			return
		_eq(ml.get("unchecked_item_character"), "☐", "unchecked_item_character")
		_eq(ml.get("checked_item_character"), "☑", "checked_item_character")
		_eq(ml.get("enable_checkbox_clicks"), true, "enable_checkbox_clicks")
		_eq(ml.get("automatic_links"), true, "automatic_links")
		_eq(ml.get("assume_https_links"), true, "assume_https_links")
		_eq(ml.get("hr_height"), 2, "hr_height")
		_eq_approx(float(ml.get("hr_width")), 90.0, "hr_width")
		_eq(ml.get("hr_alignment"), "center", "hr_alignment")
		var hr_color = ml.get("hr_color")
		_check(typeof(hr_color) == TYPE_COLOR and (hr_color as Color).is_equal_approx(Color.WHITE), "hr_color default must be white")
		for level in range(1, 7):
			var header_format = ml.get("h%d" % level)
			_check(header_format != null, "h%d header format resource missing" % level)
	)

	_it("every new style setter requests an update via queue_update", func() -> void:
		var alternates := _style_alternates()
		for property_name in alternates:
			var ml = _new_label("")
			if ml == null:
				return
			if not (property_name in ml):
				_fail("missing property '%s'" % property_name)
				continue
			ml.set_process(false)
			ml.set(property_name, alternates[property_name])
			_check(ml.is_processing(), "setter of '%s' did not enable processing (queue_update must set_process(true))" % property_name)
	)

	_it("queue_update enables processing and _update disables it", func() -> void:
		var ml = _new_label("# A")
		if ml == null:
			return
		ml.set_process(false)
		ml.queue_update()
		_check(ml.is_processing(), "queue_update() must set_process(true)")
		ml._update()
		_check(not ml.is_processing(), "_update() must set_process(false) (no per-frame polling)")
		_has_text(ml.get_parsed_text(), "A", "output rendered by _update")
	)


# ============================================================
# Suite: paragraphs and line structure (spec section 6.7)
# ============================================================

func _suite_paragraphs() -> void:
	_start_suite("paragraphs (spec section 6.7)")

	_it("blank line separates paragraphs and survives as an output line", func() -> void:
		var out := _render("first\n\nsecond")
		_has_text(out, "first", "first paragraph")
		_has_text(out, "second", "second paragraph")
		_check(out.split("\n").size() >= 3, "blank line should add an output line, got %s" % _repr(out))
		_assert_valid(out, "blank line")
	)

	_it("consecutive plain lines are not soft-wrapped together", func() -> void:
		var out := _render("one\ntwo\nthree")
		var out_lines := out.split("\n")
		_check(out_lines.size() >= 3, "expected a separate output line per source line, got %s" % _repr(out))
		_check(out.contains("one") and out.contains("two") and out.contains("three"), "all lines must be preserved")
	)

	_it("mixed Chinese and ASCII paragraph text is preserved", func() -> void:
		var out := _render("段落 with 中文 and ASCII.")
		_eq_strip(_semantic(out), "段落 with 中文 and ASCII.", "mixed-script paragraph")
	)


# ============================================================
# Suite: inline formatting (spec section 7)
# ============================================================

func _suite_inline() -> void:
	_start_suite("inline formatting (spec section 7)")

	_it("bold with **double asterisks**", func() -> void:
		var out := _render("**bold**")
		_has(out, "[b]bold[/b]", "bold output")
		_eq_strip(_semantic(out), "bold", "semantic text")
		_assert_valid(out, "bold")
	)

	_it("italic with *single asterisks*", func() -> void:
		var out := _render("*italic*")
		_has(out, "[i]italic[/i]", "italic output")
		_assert_valid(out, "italic")
	)

	_it("bold+italic with ***triple asterisks***", func() -> void:
		var out := _render("***both***")
		_has(out, "[b][i]both[/i][/b]", "bold+italic output")
		_eq_strip(_semantic(out), "both", "semantic text")
		_assert_valid(out, "bold+italic")
	)

	_it("bold with __double underscores__", func() -> void:
		var out := _render("__bold__")
		_has(out, "[b]bold[/b]", "underscore bold")
		_assert_valid(out, "underscore bold")
	)

	_it("italic with _single underscores_", func() -> void:
		var out := _render("_italic_")
		_has(out, "[i]italic[/i]", "underscore italic")
		_assert_valid(out, "underscore italic")
	)

	_it("intraword underscores stay literal", func() -> void:
		var out := _render("foo_bar_baz")
		_not_has(out, "[i]", "intraword underscore must not become italic")
		_has_text(out, "foo_bar_baz", "literal intraword underscores")
		_assert_valid(out, "intraword underscores")
	)

	_it("strikethrough with ~~double tildes~~", func() -> void:
		var out := _render("~~gone~~")
		_has(out, "[s]gone[/s]", "strikethrough output")
		_assert_valid(out, "strikethrough")
	)

	_it("backslash escapes emphasis markers", func() -> void:
		var out := _render("\\*not italic\\*")
		_not_has(out, "[i]", "escaped asterisks must not become italic")
		_has_text(out, "*not italic*", "escaped asterisks literal text")
		_assert_valid(out, "escaped asterisks")
	)

	_it("backslash escapes brackets into [lb]/[rb]", func() -> void:
		var out := _render("\\[not a tag\\]")
		_has(out, "[lb]", "escaped '[' should become [lb]")
		_has(out, "[rb]", "escaped ']' should become [rb]")
		_has_text(out, "[not a tag]", "escaped brackets are visible literally")
		_assert_valid(out, "escaped brackets")
	)

	_it("backslash escapes pipe, backslash and other specials", func() -> void:
		var out := _render("a \\| b \\\\ c \\# d")
		_has_text(out, "a | b", "escaped pipe")
		_has_text(out, "\\ c", "escaped backslash")
		_has_text(out, "#", "escaped hash")
		_assert_valid(out, "escaped specials")
	)

	_it("inline code with single backticks", func() -> void:
		var out := _render("`code`")
		_has(out, "[code]code[/code]", "inline code output")
		_eq_strip(_semantic(out), "code", "semantic text")
		_assert_valid(out, "inline code")
	)

	_it("inline code escapes BBCode brackets inside", func() -> void:
		var out := _render("`a[b]c`")
		_has(out, "[code]a[lb]b[rb]c[/code]", "inline code bracket escaping")
		_has_text(out, "a[b]c", "inline code literal brackets")
		_assert_valid(out, "inline code with brackets")
	)

	_it("inline code does not process emphasis", func() -> void:
		var out := _render("`**b**`")
		_not_has(out, "[b]", "emphasis inside inline code must stay literal")
		_has_text(out, "**b**", "literal emphasis markers")
		_assert_valid(out, "inline code emphasis")
	)

	_it("inline code with double backtick delimiters", func() -> void:
		var out := _render("`` `x` ``")
		_has(out, "[code]", "double backtick code output")
		_has_text(out, "`x`", "content keeps single backticks")
		_assert_valid(out, "double backtick code")
	)

	_it("inline code strips one bounding space pair", func() -> void:
		var out := _render("a ` x ` b")
		_has(out, "[code]x[/code]", "one space stripped from each side")
		_eq_strip(_semantic(out), "a x b", "semantic text")
	)

	_it("inline link with title becomes hint + url", func() -> void:
		var out := _render("[text](https://example.com \"Title\")")
		_has(out, "[url=https://example.com]text[/url]", "link url tag")
		_has(out, "[hint=", "hint wrapper for link title")
		_has_text(out, "text", "link text")
		_assert_valid(out, "link with title")
	)

	_it("link text keeps inline formatting", func() -> void:
		var out := _render("[**bold**](https://example.com)")
		_has(out, "[url=https://example.com]", "link url")
		_has(out, "[b]bold[/b]", "bold inside link text")
		_assert_valid(out, "link text formatting")
	)

	_it("undefined [b] reference stays raw BBCode", func() -> void:
		var out := _render("[b]x[/b]")
		_eq_strip(out, "[b]x[/b]", "raw BBCode must be preserved")
		_eq_strip(_semantic(out), "x", "semantic text")
		_assert_valid(out, "raw BBCode reference")
	)

	_it("several inline formats combine on one line", func() -> void:
		var out := _render("a **b** *i* `c` ~~s~~")
		_has(out, "[b]b[/b]", "bold")
		_has(out, "[i]i[/i]", "italic")
		_has(out, "[code]c[/code]", "inline code")
		_has(out, "[s]s[/s]", "strikethrough")
		_assert_valid(out, "combined inline formats")
	)

# ============================================================
# Suite: code blocks (spec section 6.1)
# ============================================================

func _suite_code_blocks() -> void:
	_start_suite("code blocks (spec section 6.1)")

	_it("backtick fence with info string", func() -> void:
		var out := _render("```gdscript\nprint(1)\n```")
		_has(out, "[code]", "fenced code opens")
		_has(out, "[/code]", "fenced code closes")
		_code_body_equals(out, "print(1)", "fenced code body")
		_not_has(out, "```", "fence markers must not leak into output")
		_assert_valid(out, "fenced code")
	)

	_it("code block content is not surrounded by blank lines", func() -> void:
		var out := _render("```\nprint(1)\n```")
		_has(out, "[code]print(1)[/code]", "spec 6.1: [code] + content lines + [/code] without extra newlines")
	)

	_it("tilde fence", func() -> void:
		var out := _render("~~~\nx = 1\n~~~")
		_code_body_equals(out, "x = 1", "tilde fenced code body")
		_not_has(out, "~~~", "tilde fence markers must not leak")
		_assert_valid(out, "tilde fenced code")
	)

	_it("fenced code escapes BBCode brackets", func() -> void:
		var out := _render("```\n[b]x[/b]\n```")
		_has(_code_content(out), "[lb]b[rb]", "code content bracket escaping")
		_has_text(out, "[b]x[/b]", "code content visible literally")
		_assert_valid(out, "fenced code brackets")
	)

	_it("fenced code preserves leading spaces", func() -> void:
		var out := _render("```\n  spaced line\n```")
		_has(_code_content(out), "  spaced line", "leading spaces preserved inside code")
		_assert_valid(out, "fenced code spacing")
	)

	_it("unclosed fence is closed at end of document", func() -> void:
		var out := _render("```\nunclosed code")
		_code_body_equals(out, "unclosed code", "unclosed fence body")
		_has(out, "[/code]", "unclosed fence gets closing tag")
		_assert_valid(out, "unclosed fence")
	)

	_it("closing fence longer than opening fence closes the block", func() -> void:
		var out := _render("```\ncode\n`````")
		_code_body_equals(out, "code", "longer closing fence body")
		_not_has(out, "`````", "closing fence must not leak")
		_assert_valid(out, "long closing fence")
	)

	_it("indented code block with four spaces", func() -> void:
		var out := _render("    indented [x]")
		_code_body_equals(out, "indented [lb]x[rb]", "indented code body")
		_has_text(out, "indented [x]", "indented code literal text")
		_assert_valid(out, "indented code")
	)

	_it("indented code block keeps inline markdown literal", func() -> void:
		var out := _render("    **not bold**")
		_not_has(out, "[b]", "markdown inside indented code must stay literal")
		_code_body_equals(out, "**not bold**", "literal markdown body")
		_assert_valid(out, "indented code markdown")
	)

	_it("code block inside a list item keeps its content", func() -> void:
		var out := _render("- item\n      indented code")
		_has(out, "[ul]", "list still rendered")
		_has(out, "indented code", "indented code after a list item")
		_assert_valid(out, "list with indented code")
	)


# ============================================================
# Suite: headings and anchors (spec section 6.2)
# ============================================================

func _heading_font_size(level: int) -> int:
	if _last_label == null:
		return -1
	var header_format = _last_label.get("h%d" % level)
	if header_format == null:
		return -1
	var scale: float = float(header_format.get("font_size"))
	var normal: int = _last_label.get_theme_font_size("normal_font_size")
	return int(scale * normal)


func _suite_headings() -> void:
	_start_suite("headings and anchors (spec section 6.2)")

	_it("ATX levels 1-6 use their header format font size", func() -> void:
		for level in range(1, 7):
			var text := "%s Level" % "#".repeat(level)
			var ml = _new_label(text)
			if ml == null:
				return
			var header_format = ml.get("h%d" % level)
			if header_format == null:
				_fail("h%d header format missing" % level)
				continue
			var expected_size := int(float(header_format.get("font_size")) * ml.get_theme_font_size("normal_font_size"))
			var out := _convert(ml, text)
			_has(out, "[font_size=%d]" % expected_size, "h%d font size tag" % level)
			_eq_strip(_semantic(out), "Level", "h%d semantic text" % level)
			_not_has(out, "#", "h%d source hashes must be removed" % level)
			_assert_valid(out, "h%d" % level)
	)

	_it("heading format tags are emitted in spec order", func() -> void:
		var ml = _new_label("# T")
		if ml == null:
			return
		var header_format = ml.get("h1")
		header_format.set("is_bold", true)
		header_format.set("is_italic", true)
		header_format.set("is_underlined", true)
		header_format.set("override_font_color", true)
		header_format.set("font_color", Color.RED)
		var expected_size := int(float(header_format.get("font_size")) * ml.get_theme_font_size("normal_font_size"))
		var out := _convert(ml, "# T")
		var expected := "[color=#%s][font_size=%d][b][i][u]T[/u][/i][/b][/font_size][/color]" % [
			Color.RED.to_html(), expected_size,
		]
		_eq_strip(out, expected, "heading tag order (color > font_size > b > i > u)")
		_assert_valid(out, "heading format order")
	)

	_it("ATX closing hashes are stripped", func() -> void:
		var out := _render("## Title ##")
		_eq_strip(_semantic(out), "Title", "closing hashes stripped")
		_not_has(out, "##", "closing hashes must not leak")
		_has(out, "[font_size=", "heading font size tag")
		_assert_valid(out, "closing hashes")
	)

	_it("seven hashes are not a heading", func() -> void:
		var out := _render("####### nope")
		_not_has(out, "[font_size", "seven hashes must not be a heading")
		_has_text(out, "nope", "text still rendered as paragraph")
		_assert_valid(out, "seven hashes")
	)

	_it("up to three leading spaces are allowed before an ATX heading", func() -> void:
		var out := _render("   # H")
		_has(out, "[font_size=", "heading font size tag")
		_eq_strip(_semantic(out), "H", "semantic text")
		_assert_valid(out, "indented ATX heading")
	)

	_it("four leading spaces become indented code, not a heading", func() -> void:
		var out := _render("    # H")
		_has(out, "[code]", "four spaces must be an indented code block")
		_not_has(out, "[font_size", "four spaces must not be an ATX heading")
		_assert_valid(out, "four space heading")
	)

	_it("inline markdown works inside a heading", func() -> void:
		var out := _render("# A **b**")
		_has(out, "[b]b[/b]", "bold inside heading")
		_eq_strip(_semantic(out), "A b", "semantic heading text")
		_assert_valid(out, "heading inline markdown")
	)

	_it("Setext heading with === is an h1", func() -> void:
		var out := _render("Title\n===")
		var expected_size := _heading_font_size(1)
		_check(expected_size > 0, "h1 format font size not available")
		_has(out, "[font_size=%d]" % expected_size, "Setext h1 uses h1 format")
		_eq_strip(_semantic(out), "Title", "Setext h1 text")
		_not_has(out, "===", "Setext underline must not leak")
		_assert_valid(out, "Setext h1")
	)

	_it("Setext heading with --- is an h2, not a horizontal rule", func() -> void:
		var out := _render("Title\n---")
		var expected_size := _heading_font_size(2)
		_check(expected_size > 0, "h2 format font size not available")
		_has(out, "[font_size=%d]" % expected_size, "Setext h2 uses h2 format")
		_not_has(out, "[hr", "Setext underline must not become a horizontal rule")
		_eq_strip(_semantic(out), "Title", "Setext h2 text")
		_assert_valid(out, "Setext h2")
	)

	_it("standalone --- without a preceding paragraph is a horizontal rule", func() -> void:
		var out := _render("---")
		_has(out, "[hr", "standalone --- must be a horizontal rule")
		_not_has(out, "[font_size", "standalone --- must not be a heading")
		_assert_valid(out, "standalone hr")
	)

	_it("header draw_horizontal_rule adds a separate hr output line", func() -> void:
		if not _at_least(0x040500):
			return
		var ml = _new_label("# A")
		if ml == null:
			return
		var header_format = ml.get("h1")
		header_format.set("draw_horizontal_rule", true)
		var out := _convert(ml, "# A")
		_has(out, "[hr", "header horizontal rule")
		_eq_strip(_semantic(out), "A", "heading text")
		_assert_valid(out, "header hr")
	)

	_it("leading blank lines do not shift heading anchors", func() -> void:
		var out := _render("\n\n# Title\n\ntext\n\n## Sub")
		var anchors := _anchors()
		_check(anchors.has("#title"), "expected anchor '#title', got keys %s" % str(anchors.keys()))
		if anchors.has("#title"):
			_eq(anchors["#title"], 0, "leading blank lines are trimmed before anchor counting")
		_check(anchors.has("#sub"), "expected anchor '#sub', got keys %s" % str(anchors.keys()))
		if anchors.has("#sub"):
			_eq(anchors["#sub"], 4, "#sub paragraph index after trimming leading blanks")
		_assert_valid(out, "leading blanks anchor offset")
	)

	_it("heading anchors use GitHub slugs", func() -> void:
		var out := _render("# Hello, World!")
		var anchors := _anchors()
		_has(out, "[font_size=", "heading output")
		_check(anchors.has("#hello-world"), "expected anchor '#hello-world', got keys %s" % str(anchors.keys()))
		if anchors.has("#hello-world"):
			_eq(anchors["#hello-world"], 0, "first heading anchor line index")
	)

	_it("anchor index points at the heading output line", func() -> void:
		var out := _render("text\n\n# Head")
		var anchors := _anchors()
		var expected_index := _line_index(out, "[font_size")
		_check(expected_index >= 0, "heading output line not found in %s" % _repr(out))
		_check(anchors.has("#head"), "expected anchor '#head', got keys %s" % str(anchors.keys()))
		if anchors.has("#head"):
			_eq(anchors["#head"], expected_index, "anchor index vs heading output line")
	)

	_it("table before a heading counts as one paragraph for anchors", func() -> void:
		var out := _render("| a | b |\n|---|---|\n| 1 | 2 |\n\n# H")
		_has(out, "[table=2]", "table rendered before the heading")
		var anchors := _anchors()
		_check(anchors.has("#h"), "expected anchor '#h', got keys %s" % str(anchors.keys()))
		if anchors.has("#h"):
			_eq(anchors["#h"], 2, "whole table counts as 1 RichTextLabel paragraph + blank line = paragraph index 2")
		_assert_valid(out, "table anchor paragraph count")
	)

	_it("duplicate headings get -1 and -2 slug suffixes", func() -> void:
		var out := _render("# A\n# A\n# A")
		var anchors := _anchors()
		for suffix in ["#a", "#a-1", "#a-2"]:
			_check(anchors.has(suffix), "expected anchor '%s', got keys %s" % [suffix, str(anchors.keys())])
		if anchors.has("#a") and anchors.has("#a-1") and anchors.has("#a-2"):
			_eq(anchors["#a"], 0, "first duplicate index")
			_eq(anchors["#a-1"], 1, "second duplicate index")
			_eq(anchors["#a-2"], 2, "third duplicate index")
		_assert_valid(out, "duplicate headings")
	)

	_it("duplicate slug suffixes stay unique when suffixes collide", func() -> void:
		var out := _render("# Foo\n\n## Foo\n\n## Foo-1")
		var anchors := _anchors()
		_eq(anchors.size(), 3, "three unique anchors")
		for key in ["#foo", "#foo-1", "#foo-1-1"]:
			_check(anchors.has(key), "expected anchor '%s', got keys %s" % [key, str(anchors.keys())])
		if anchors.has("#foo") and anchors.has("#foo-1") and anchors.has("#foo-1-1"):
			_eq(anchors["#foo"], 0, "#foo paragraph index")
			_eq(anchors["#foo-1"], 2, "#foo-1 paragraph index")
			_eq(anchors["#foo-1-1"], 4, "#foo-1-1 paragraph index")
		_assert_valid(out, "colliding slug suffixes")
	)

	_it("anchors reset between conversions (no accumulation)", func() -> void:
		var ml = _new_label("# A\n# B")
		if ml == null:
			return
		var out_first := _convert(ml, "# A\n# B")
		var anchors_first := _anchors()
		var out_second := _convert(ml, "# A\n# B")
		var anchors_second := _anchors()
		_eq(out_second, out_first, "second conversion output")
		_eq(anchors_first.size(), 2, "first conversion anchor count")
		_eq(anchors_second.size(), 2, "second conversion anchor count")
		for key in ["#a", "#b"]:
			_check(anchors_second.has(key), "anchor '%s' missing after second conversion" % key)
		_check(not anchors_second.has("#a-1"), "duplicate suffix must not accumulate (#a-1 present)")
		_check(not anchors_second.has("#b-1"), "duplicate suffix must not accumulate (#b-1 present)")
		_assert_valid(out_second, "anchors reset")
	)

	_it("unicode heading slugs keep unicode letters", func() -> void:
		var out := _render("# 标题 One")
		var anchors := _anchors()
		_check(anchors.has("#标题-one"), "expected anchor '#标题-one', got keys %s" % str(anchors.keys()))
		_assert_valid(out, "unicode heading")
	)

	_it("Setext heading registers an anchor", func() -> void:
		var out := _render("Title\n===")
		var anchors := _anchors()
		_check(anchors.has("#title"), "expected anchor '#title', got keys %s" % str(anchors.keys()))
		if anchors.has("#title"):
			_eq(anchors["#title"], 0, "Setext heading anchor index")
		_assert_valid(out, "Setext anchor")
	)


# ============================================================
# Suite: horizontal rules (spec section 6.3)
# ============================================================

func _suite_horizontal_rules() -> void:
	_start_suite("horizontal rules (spec section 6.3)")

	_it("dash rule renders a [hr] tag", func() -> void:
		var out := _render("---")
		_has(out, "[hr", "hr tag")
		_has(out, "height=", "hr height attribute")
		_has(out, "width=", "hr width attribute")
		_has(out, "align=", "hr alignment attribute")
		_has(out, "color=", "hr color attribute")
		_assert_valid(out, "dash rule")
	)

	_it("asterisk and underscore rules render [hr]", func() -> void:
		for source in ["***", "___"]:
			var out := _render(source)
			_has(out, "[hr", "rule %s" % _repr(source))
			_assert_valid(out, "rule %s" % _repr(source))
	)

	_it("spaced rule characters are accepted", func() -> void:
		var out := _render("- - -")
		_has(out, "[hr", "spaced dash rule")
		_assert_valid(out, "spaced rule")
	)

	_it("hr_* properties are applied to the tag", func() -> void:
		var out := _render("---", {
			"hr_height": 4,
			"hr_width": 75.0,
			"hr_alignment": "right",
			"hr_color": Color.RED,
		})
		_has(out, "height=4", "hr height")
		_has(out, "width=75%", "hr width")
		_has(out, "align=right", "hr alignment")
		_has(out, "color=#%s" % Color.RED.to_html(), "hr color")
		_assert_valid(out, "hr props")
	)

	_it("source rule characters are not rendered as text", func() -> void:
		var out := _render("paragraph\n\n---")
		_has_text(out, "paragraph", "preceding paragraph")
		_not_has_text(out, "---", "rule characters must not leak into text")
		_assert_valid(out, "rule text")
	)

# ============================================================
# Suite helpers for later sections
# ============================================================

func _semantic_line_with(out: String, needle: String) -> String:
	for line in _semantic(out).split("\n"):
		if line.contains(needle):
			return line
	return ""


func _code_body_equals(out: String, expected: String, label: String) -> void:
	_eq(_code_content(out).strip_edges(), expected, label)


func _checkbox_metas(out: String) -> Array:
	var result: Array = []
	var meta_regex := RegEx.create_from_string(r"\[url=(\{[^\]]*\})\]")
	for meta_match in meta_regex.search_all(out):
		var parsed = JSON.parse_string(meta_match.get_string(1))
		if typeof(parsed) == TYPE_DICTIONARY:
			result.append(parsed)
	return result


# ============================================================
# Suite: block quotes (spec section 6.4)
# ============================================================

func _bar_tag() -> String:
	return "[color=#%s]│ [/color]" % Color(0.55, 0.55, 0.55).to_html()


func _suite_blockquotes() -> void:
	_start_suite("block quotes (spec section 6.4)")

	_it("single-level quote wraps content in [indent] and prefixes bars", func() -> void:
		var out := _render("> quote")
		_has(out, "[indent]", "quote opens indent")
		_has(out, "[/indent]", "quote closes indent")
		_has(out, _bar_tag(), "quote bar prefix with colored bar character")
		_has_text(out, "│ quote", "bar text prefix")
		_not_has_text(out, "> quote", "quote marker must not leak into text")
		_assert_valid(out, "single blockquote")
	)

	_it("bar character and color properties are applied", func() -> void:
		var out := _render("> quote", {
			"blockquote_bar_character": "▎",
			"blockquote_bar_color": Color.RED,
		})
		_has(out, "[color=#%s]▎ [/color]" % Color.RED.to_html(), "custom bar color/character")
		_has_text(out, "▎ quote", "custom bar text prefix")
		_assert_valid(out, "blockquote bar props")
	)

	_it("nested quotes add one indent level and one bar per depth", func() -> void:
		var out := _render("> outer\n>> inner")
		_eq(_count(out, "[indent]"), 2, "two nesting levels")
		_eq(_count(out, "[/indent]"), 2, "two closing indent tags")
		var outer_line := _semantic_line_with(out, "outer")
		var inner_line := _semantic_line_with(out, "inner")
		_eq(_count(outer_line, "│"), 1, "outer line bar count")
		_eq(_count(inner_line, "│"), 2, "inner line bar count")
		_assert_valid(out, "nested blockquote")
	)

	_it("lazy continuation lines stay inside the quote", func() -> void:
		var out := _render("> a\nb")
		_eq(_count(out, "[indent]"), 1, "one quote level")
		_has_text(out, "│ a", "first quoted line")
		_has_text(out, "│ b", "lazy continuation line")
		_assert_valid(out, "lazy continuation")
	)

	_it("lazy continuation does not absorb a following block start", func() -> void:
		var out := _render("> a\n- item")
		_has(out, "[indent]", "quote level")
		_has(out, "[ul]", "list after the quote")
		_check(out.find("[/indent]") < out.find("[ul]"), "list must start after the quote closes")
		_assert_valid(out, "lazy continuation block start")
	)

	_it("quoted blank lines are kept as paragraph separators", func() -> void:
		var out := _render("> a\n>\n> b")
		_eq(_count(out, "[indent]"), 1, "one quote level")
		_has_text(out, "│ a", "first paragraph")
		_has_text(out, "│ b", "second paragraph")
		_assert_valid(out, "quoted blank line")
	)

	_it("blockquote supports headings", func() -> void:
		var out := _render("> # H")
		_has(out, "[indent]", "quote level")
		_has(out, "[font_size=", "heading inside quote")
		_has_text(out, "│ H", "heading text with bar")
		_assert_valid(out, "quote heading")
	)

	_it("blockquote supports Setext headings", func() -> void:
		var out := _render("> Title\n> ---")
		_has(out, "[indent]", "quote level")
		_has(out, "[font_size=", "Setext heading inside quote")
		_not_has(out, "[hr", "Setext underline must not become a rule inside a quote")
		_has_text(out, "│ Title", "heading text with bar")
		_assert_valid(out, "quote Setext heading")
	)

	_it("blockquote supports lists (item text keeps the bar prefix)", func() -> void:
		var out := _render("> - a\n> - b")
		_has(out, "[indent]", "quote level")
		_has(out, "[ul]", "list inside quote")
		_has_text(out, "│ a", "quoted list item text prefix")
		_assert_valid(out, "quote list")
	)

	_it("quoted task lists map checkbox records to original source lines", func() -> void:
		_render("> - [ ] quoted task")
		var records_single := _checkboxes()
		_eq(records_single.size(), 1, "one checkbox record for a single quoted task")
		if records_single.size() == 1:
			_eq(records_single[0].get("line"), 0, "quoted task line 0 maps to source line 0")
			_eq(records_single[0].get("marker"), "[ ]", "quoted task marker")
			_eq(records_single[0].get("checked"), false, "quoted task checked state")
			_eq(records_single[0].get("text"), "quoted task", "quoted task text")

		var source := "> quote\n> - [ ] quoted task"
		var ml = _new_label(source)
		if ml == null:
			return
		var out := _convert(ml, source)
		_has_text(out, "quoted task", "quoted task rendered inside the quote")
		_has_text(out, "│", "quote bar prefix present on the quoted task line")
		var records := _checkboxes()
		_eq(records.size(), 1, "one checkbox record for the nested quoted task")
		if records.size() == 1:
			_eq(records[0].get("line"), 1, "quote recursion must keep the original source line number")
			_eq(records[0].get("marker"), "[ ]", "nested quoted task marker")
			_eq(records[0].get("checked"), false, "nested quoted task checked state")
		ml.set("automatic_links", false)
		_click_events = []
		ml.task_checkbox_clicked.connect(func(id: int, line: int, checked: bool, task_text: String) -> void:
			_click_events.append([id, line, checked, task_text])
		)
		if ml.has_method("_on_meta_clicked"):
			ml._on_meta_clicked(JSON.stringify({
				"markdownlabel-checkbox": true,
				"id": 0,
				"checked": false,
			}))
		_eq(ml.markdown_text, "> quote\n> - [x] quoted task", "click write-back flips the quoted source line")
		_eq(_click_events.size(), 1, "quoted task click signal emitted")
		if _click_events.size() == 1:
			_eq(_click_events[0][1], 1, "click signal line is the original source line")
			_eq(String(_click_events[0][3]).strip_edges(), "quoted task", "click signal task text")
		_assert_valid(out, "quoted task record mapping")
	)

	_it("blockquote supports fenced code with plain-text bar prefix", func() -> void:
		var out := _render("> ```\n> code\n> ```")
		_has(out, "[indent]", "quote level")
		_has(out, "[code]", "code block inside quote")
		var code_body := _code_content(out)
		_has(code_body, "│ code", "bar prefix inside code (plain text)")
		_not_has(code_body, "[color", "code body must not contain colored bar tags")
		_assert_valid(out, "quote code")
	)

	_it("blockquote supports tables without bar prefixes", func() -> void:
		var out := _render("> | a | b |\n> | --- | --- |\n> | 1 | 2 |")
		_has(out, "[indent]", "quote level")
		_has(out, "[table=2]", "table inside quote")
		_not_has_text(out, "│", "table rows must not get bar prefixes")
		_assert_valid(out, "quote table")
	)

	_it("blockquote supports horizontal rules without bar prefix", func() -> void:
		var out := _render("> ---")
		_has(out, "[indent]", "quote level")
		_has(out, "[hr", "rule inside quote")
		_not_has_text(out, "│", "rule must not get a bar prefix")
		_assert_valid(out, "quote hr")
	)

	_it("blockquote text color override", func() -> void:
		var out := _render("> x", {"blockquote_override_text_color": true})
		_has(out, "[color=#%s]" % Color(0.75, 0.75, 0.75).to_html(), "default quote text color")
		_assert_valid(out, "quote text color")
	)

	_it("blockquote italic style", func() -> void:
		var out := _render("> x", {"blockquote_italic": true})
		_has(out, "[i]", "quote italic open tag")
		_has(out, "[/i]", "quote italic close tag")
		_assert_valid(out, "quote italic")
	)

	_it("blockquote background color", func() -> void:
		var out := _render("> x", {"blockquote_background_color": Color(0.1, 0.1, 0.1, 0.5)})
		_has(out, "[bgcolor=#%s]" % Color(0.1, 0.1, 0.1, 0.5).to_html(), "quote background color")
		_assert_valid(out, "quote background")
	)

	_it("blockquote wrapper order is indent > bgcolor > color > italic", func() -> void:
		var background := Color(0.1, 0.1, 0.1, 0.5)
		var text_color := Color.RED
		var out := _render("> x", {
			"blockquote_background_color": background,
			"blockquote_override_text_color": true,
			"blockquote_text_color": text_color,
			"blockquote_italic": true,
		})
		var index_indent := out.find("[indent]")
		var index_background := out.find("[bgcolor=#%s]" % background.to_html())
		var index_color := out.find("[color=#%s]" % text_color.to_html())
		var index_italic := out.find("[i]")
		_check(index_indent >= 0 and index_background >= 0 and index_color >= 0 and index_italic >= 0, "expected all wrapper tags, got %s" % _repr(out))
		_check(index_indent < index_background, "[indent] must wrap [bgcolor]")
		_check(index_background < index_color, "[bgcolor] must wrap [color]")
		_check(index_color < index_italic, "[color] must wrap [i]")
		_assert_valid(out, "quote wrapper order")
	)

	_it("paragraph after a quote is outside the indent", func() -> void:
		var out := _render("> quoted\n\nafter")
		_has_text(out, "│ quoted", "quoted line")
		_has_text(out, "after", "paragraph after quote")
		_check(out.find("[/indent]") < out.find("after"), "paragraph must come after [/indent]")
		_assert_valid(out, "quote followed by paragraph")
	)


# ============================================================
# Suite: lists (spec section 6.5)
# ============================================================

func _suite_lists() -> void:
	_start_suite("lists (spec section 6.5)")

	_it("unordered list uses [ul] with one output line per item", func() -> void:
		var out := _render("- a\n- b")
		_has(out, "[ul]", "list open tag")
		_has(out, "[/ul]", "list close tag")
		_has_text(out, "a", "first item")
		_has_text(out, "b", "second item")
		_not_has_text(out, "- a", "list markers must not leak into text")
		_assert_valid(out, "unordered list")
	)

	_it("ordered list uses [ol]", func() -> void:
		var out := _render("1. a\n2. b")
		_has(out, "[ol]", "ordered list open tag")
		_has(out, "[/ol]", "ordered list close tag")
		_has_text(out, "a", "first item")
		_not_has_text(out, "1. a", "ordered marker must not leak into text")
		_assert_valid(out, "ordered list")
	)

	_it("ordered list may start at any number without a start attribute", func() -> void:
		var out := _render("3. a\n4. b")
		_has(out, "[ol]", "ordered list open tag")
		_not_has(out, "start=", "Godot [ol] has no start attribute")
		_has_text(out, "a", "first item")
		_has_text(out, "b", "second item")
		_assert_valid(out, "ordered list start")
	)

	_it("nested unordered lists nest [ul] tags", func() -> void:
		var out := _render("- a\n  - b\n- c")
		_eq(_count(out, "[ul]"), 2, "two unordered list levels")
		_eq(_count(out, "[/ul]"), 2, "matching close tags")
		_has_text(out, "a", "outer item")
		_has_text(out, "b", "nested item")
		_has_text(out, "c", "item after nested list")
		_assert_valid(out, "nested unordered list")
	)

	_it("three-level nested list stays balanced", func() -> void:
		var out := _render("- a\n  - b\n    - c\n- d")
		_eq(_count(out, "[ul]"), 3, "three unordered list levels")
		_eq(_count(out, "[/ul]"), 3, "matching close tags")
		for item_text in ["a", "b", "c", "d"]:
			_has_text(out, item_text, "item %s" % item_text)
		_assert_valid(out, "deep nested list")
	)

	_it("mixed ordered/unordered nesting", func() -> void:
		var out := _render("1. a\n   - b\n2. c")
		_has(out, "[ol]", "ordered outer list")
		_has(out, "[ul]", "unordered nested list")
		_assert_valid(out, "mixed nesting")
	)

	_it("item continuation lines are merged into the item", func() -> void:
		var out := _render("- a\n  continued")
		_eq(_count(out, "[/ul]"), 1, "single list")
		_has_text(out, "a continued", "continuation merged with a space")
		_assert_valid(out, "list continuation")
	)

	_it("empty line ends the list", func() -> void:
		var out := _render("- a\n\ntext")
		_has(out, "[/ul]", "list closed")
		_has_text(out, "text", "paragraph after the list")
		_check(out.find("[/ul]") < out.find("text"), "paragraph must come after the list closes")
		_assert_valid(out, "list followed by paragraph")
	)

	_it("loose list with a blank line between items stays one list", func() -> void:
		var out := _render("- a\n\n- b")
		_eq(_count(out, "[ul]"), 1, "loose list should stay a single list")
		_has_text(out, "a", "first item")
		_has_text(out, "b", "second item")
		_assert_valid(out, "loose list")
	)

	_it("two lists separated by a paragraph produce two lists", func() -> void:
		var out := _render("- a\n\nplain\n\n- b")
		_eq(_count(out, "[ul]"), 2, "two separate lists")
		_has_text(out, "plain", "separating paragraph")
		_assert_valid(out, "two lists")
	)

	_it("list items support inline formatting", func() -> void:
		var out := _render("- **b** and `c`")
		_has(out, "[b]b[/b]", "bold inside list item")
		_has(out, "[code]c[/code]", "inline code inside list item")
		_assert_valid(out, "list item formatting")
	)

	_it("task list renders checkbox characters", func() -> void:
		var out := _render("- [ ] todo\n- [x] done")
		_has(out, "☐", "unchecked checkbox character")
		_has(out, "☑", "checked checkbox character")
		_has_text(out, "todo", "unchecked task text")
		_has_text(out, "done", "checked task text")
		_assert_valid(out, "task list")
	)

	_it("task list checkbox URLs carry the click metadata", func() -> void:
		var out := _render("- [ ] todo\n- [x] done")
		var metas := _checkbox_metas(out)
		_eq(metas.size(), 2, "two checkbox meta URLs")
		if metas.size() == 2:
			_eq(metas[0].get("markdownlabel-checkbox"), true, "first meta marker key")
			_eq(int(metas[0].get("id")), 0, "first checkbox id")
			_eq(metas[0].get("checked"), false, "first checkbox state")
			_eq(int(metas[1].get("id")), 1, "second checkbox id")
			_eq(metas[1].get("checked"), true, "second checkbox state")
	)

	_it("task list records line, marker, checked state and text", func() -> void:
		_render("# Title\n\n- [ ] todo\n- [x] done")
		var records := _checkboxes()
		_eq(records.size(), 2, "two checkbox records")
		if records.size() == 2:
			var first: Dictionary = records[0]
			_eq(first.get("line"), 2, "first checkbox source line (0-based)")
			_eq(first.get("marker"), "[ ]", "first checkbox marker")
			_eq(first.get("checked"), false, "first checkbox checked state")
			_eq(first.get("text"), "todo", "first checkbox text")
			var second: Dictionary = records[1]
			_eq(second.get("line"), 3, "second checkbox source line (0-based)")
			_eq(second.get("marker"), "[x]", "second checkbox marker")
			_eq(second.get("checked"), true, "second checkbox checked state")
			_eq(second.get("text"), "done", "second checkbox text")
	)

	_it("checkbox ids reset between conversions", func() -> void:
		var ml = _new_label("- [ ] a\n- [x] b")
		if ml == null:
			return
		var out_first := _convert(ml, "- [ ] a\n- [x] b")
		var records_first := _checkboxes().duplicate(true)
		var out_second := _convert(ml, "- [ ] a\n- [x] b")
		var records_second := _checkboxes().duplicate(true)
		_eq(out_second, out_first, "second conversion output")
		_eq(records_first.size(), 2, "first records count")
		_eq(records_second.size(), 2, "second records count")
		_check(records_second.has(0) and records_second.has(1), "ids must restart at 0")
	)

	_it("task list without checkbox clicks renders plain characters", func() -> void:
		var out := _render("- [ ] todo", {"enable_checkbox_clicks": false})
		_has(out, "☐", "plain checkbox character")
		_not_has(out, "markdownlabel-checkbox", "no checkbox meta URL when clicks are disabled")
		_assert_valid(out, "task list without clicks")
	)

	_it("uppercase [X] is a checked task item", func() -> void:
		var out := _render("- [X] done")
		_has(out, "☑", "uppercase X checkbox character")
		var records := _checkboxes()
		_eq(records.size(), 1, "one checkbox record")
		if records.size() == 1:
			_eq(records[0].get("checked"), true, "uppercase X checked state")
	)

	_it("custom checkbox characters are used", func() -> void:
		var out := _render("- [ ] todo\n- [x] done", {
			"unchecked_item_character": "U",
			"checked_item_character": "C",
		})
		_has(out, "U", "custom unchecked character")
		_has(out, "C", "custom checked character")
		_assert_valid(out, "custom checkbox characters")
	)


# ============================================================
# Suite: tables (spec section 6.6)
# ============================================================

func _suite_tables() -> void:
	_start_suite("tables (spec section 6.6)")

	_it("GFM table renders [table=N] with header row and cells", func() -> void:
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |")
		_has(out, "[table=2]", "table open tag with column count")
		_has(out, "[/table]", "table close tag")
		_eq(_count(out, "[cell"), 4, "two header + two data cells")
		_eq(_count(out, "[/cell]"), 4, "matching closing cell tags")
		_has(out, "[b]a[/b]", "header cell bold")
		_assert_valid(out, "GFM table")
	)

	_it("cell attributes: border then padding, in spec order", func() -> void:
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |")
		var table_info := _first_table(out)
		_eq(table_info.get("cols"), 2, "column count")
		var first_attrs: String = _row_attrs(table_info, 0)[0]
		_eq(_attr_value(first_attrs, "border"), "#" + Color(0.55, 0.55, 0.55, 0.4).to_html(), "border color attribute")
		_eq(_attr_value(first_attrs, "padding"), "3,2,3,2", "padding attribute")
		_check(first_attrs.find("border=") < first_attrs.find("padding="), "border must come before padding")
		_not_has(first_attrs, "bg=", "no row background by default")
	)

	_it("border and padding can be disabled", func() -> void:
		var out := _render("| a |\n| --- |\n| 1 |", {
			"table_border_color": Color(0, 0, 0, 0),
			"table_cell_padding": Vector4i.ZERO,
		})
		_not_has(out, "border=", "border attribute disabled")
		_not_has(out, "padding=", "padding attribute disabled")
		_has(out, "[cell]", "plain cell tag")
		_assert_valid(out, "table without border/padding")
	)

	_it("column alignment comes from the delimiter colons", func() -> void:
		var out := _render("| L | C | R |\n| :--- | :---: | ---: |\n| 1 | 2 | 3 |")
		_has(out, "[center][b]C[/b][/center]", "center-aligned header cell")
		_has(out, "[right][b]R[/b][/right]", "right-aligned header cell")
		_has(out, "[b]L[/b]", "left-aligned header cell")
		_not_has(out, "[center][b]L", "left column must not be centered")
		_has(out, "[center]2[/center]", "center-aligned data cell")
		_has(out, "[right]3[/right]", "right-aligned data cell")
		_assert_valid(out, "table column alignment")
	)

	_it("GFM delimiter row accepts a single dash and colon alignment", func() -> void:
		var out := _render("a | b\n:- | -:\n1 | 2")
		_has(out, "[table=2]", "short GFM delimiter row is detected (>=1 dash per cell)")
		_has(out, "[right][b]b[/b][/right]", "right-aligned header from '-:' delimiter")
		_has(out, "[right]2[/right]", "right-aligned data cell")
		_assert_valid(out, "short GFM delimiter")
	)

	_it("delimiter row with fewer cells keeps the header column count", func() -> void:
		var out := _render("| a | b | c |\n|---|---|\n| 1 | 2 | 3 |")
		_has(out, "[table=3]", "column count comes from the header row")
		_has(out, "[b]a[/b]", "header cell is bold")
		_not_has_text(out, "---", "delimiter row must not be rendered")
		var table_info := _first_table(out)
		_eq(table_info.get("cols"), 3, "three columns")
		_eq(_row_contents(table_info, 0).size(), 3, "three header cells")
		_eq(_row_contents(table_info, 1).size(), 3, "three data cells")
		_assert_valid(out, "short delimiter row")
	)

	_it("trailing escaped pipe is kept inside the last cell", func() -> void:
		var out := _render("| a | b \\|\n| c | d |")
		var table_info := _first_table(out)
		_eq(table_info.get("cols"), 2, "escaped trailing pipe must not add a column")
		var first_row := _row_contents(table_info, 0)
		if first_row.size() == 2:
			_has_text(String(first_row[1]), "b |", "trailing escaped pipe is rendered as a literal pipe")
		_not_has_text(out, "\\", "no dangling backslash in the output")
		_assert_valid(out, "trailing escaped pipe")
	)

	_it("table_header_row=false renders the header as a normal row", func() -> void:
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |", {"table_header_row": false})
		_not_has(out, "[b]a[/b]", "header row must not be bold when table_header_row is false")
		_eq(_count(out, "[cell"), 4, "all four cells are still rendered")
		_assert_valid(out, "table without header row")
	)

	_it("table_header_bold=false disables header bold", func() -> void:
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |", {"table_header_bold": false})
		_not_has(out, "[b]a[/b]", "header must not be bold when table_header_bold is false")
		_assert_valid(out, "table header not bold")
	)

	_it("header background is applied to header cells only", func() -> void:
		var header_color := Color(0.2, 0.4, 0.8, 0.5)
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |", {"table_header_background": header_color})
		var table_info := _first_table(out)
		for attrs in _row_attrs(table_info, 0):
			_eq(_attr_value(attrs, "bg"), "#" + header_color.to_html(), "header cell background")
		for attrs in _row_attrs(table_info, 1):
			_eq(_attr_value(attrs, "bg"), "", "data cell must not use the header background")
		_assert_valid(out, "table header background")
	)

	_it("odd data rows use table_row_background", func() -> void:
		var odd_color := Color(1, 0, 0, 0.5)
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |", {"table_row_background": odd_color})
		var table_info := _first_table(out)
		for attrs in _row_attrs(table_info, 1):
			_eq(_attr_value(attrs, "bg"), "#" + odd_color.to_html(), "first data row (odd) background")
		for attrs in _row_attrs(table_info, 2):
			_eq(_attr_value(attrs, "bg"), "", "second data row (even) has no background")
		_assert_valid(out, "table odd row background")
	)

	_it("even data rows use table_alternate_row_background", func() -> void:
		var even_color := Color(0, 0, 1, 0.5)
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |", {"table_alternate_row_background": even_color})
		var table_info := _first_table(out)
		for attrs in _row_attrs(table_info, 1):
			_eq(_attr_value(attrs, "bg"), "", "first data row (odd) has no alternate background")
		for attrs in _row_attrs(table_info, 2):
			_eq(_attr_value(attrs, "bg"), "#" + even_color.to_html(), "second data row (even) background")
		_assert_valid(out, "table even row background")
	)

	_it("both row backgrounds alternate by data-row parity", func() -> void:
		var odd_color := Color(1, 0, 0, 0.5)
		var even_color := Color(0, 0, 1, 0.5)
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |", {
			"table_row_background": odd_color,
			"table_alternate_row_background": even_color,
		})
		var table_info := _first_table(out)
		for attrs in _row_attrs(table_info, 1):
			_eq(_attr_value(attrs, "bg"), "#" + odd_color.to_html(), "odd data row background")
		for attrs in _row_attrs(table_info, 2):
			_eq(_attr_value(attrs, "bg"), "#" + even_color.to_html(), "even data row background")
		_assert_valid(out, "table zebra rows")
	)

	_it("escaped pipe stays inside a cell", func() -> void:
		var out := _render("| a \\| b | c |\n| --- | --- |\n| 1 | 2 |")
		var table_info := _first_table(out)
		_eq(table_info.get("cols"), 2, "escaped pipe must not split cells")
		var header_contents := _row_contents(table_info, 0)
		if header_contents.size() == 2:
			_has_text(header_contents[0], "a | b", "escaped pipe rendered literally")
		_assert_valid(out, "table escaped pipe")
	)

	_it("pipe inside inline code does not split cells", func() -> void:
		var out := _render("| `a|b` | c |\n| --- | --- |\n| 1 | 2 |")
		var table_info := _first_table(out)
		_eq(table_info.get("cols"), 2, "code span pipe must not split cells")
		var header_contents := _row_contents(table_info, 0)
		if header_contents.size() == 2:
			_has(header_contents[0], "[code]", "inline code rendered in the cell")
			_has_text(header_contents[0], "a|b", "literal pipe inside inline code")
		_assert_valid(out, "table code pipe")
	)

	_it("missing cells are padded with empty cells", func() -> void:
		var out := _render("| a | b |\n| --- | --- |\n| 1 |")
		var table_info := _first_table(out)
		var data_contents := _row_contents(table_info, 1)
		_eq(data_contents.size(), 2, "data row padded to the column count")
		if data_contents.size() == 2:
			_eq_strip(String(data_contents[1]), "", "padded cell is empty")
		_assert_valid(out, "table missing cells")
	)

	_it("extra cells are dropped", func() -> void:
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 | 3 |")
		var table_info := _first_table(out)
		var data_contents := _row_contents(table_info, 1)
		_eq(data_contents.size(), 2, "extra cells dropped")
		_not_has_text(out, "3", "extra cell content must not be rendered")
		_assert_valid(out, "table extra cells")
	)

	_it("empty header cells are allowed", func() -> void:
		var out := _render("| | b |\n| --- | --- |\n| 1 | 2 |")
		var table_info := _first_table(out)
		_eq(table_info.get("cols"), 2, "empty header cell keeps the column")
		_eq(_row_contents(table_info, 0).size(), 2, "header row has two cells")
		_assert_valid(out, "table empty header cell")
	)

	_it("table without a delimiter row uses table_default_alignment", func() -> void:
		var out_left := _render("| a | b |\n| 1 | 2 |")
		_has(out_left, "[table=2]", "delimiter-less table detected")
		_not_has(out_left, "[b]a[/b]", "delimiter-less table has no header styling")
		_not_has(out_left, "[center]", "left is the default alignment")
		_assert_valid(out_left, "delimiter-less table")
		var out_center := _render("| a | b |\n| 1 | 2 |", {"table_default_alignment": "center"})
		_has(out_center, "[center]a[/center]", "cells centered with table_default_alignment=center")
		_assert_valid(out_center, "delimiter-less table centered")
	)

	_it("table followed by a paragraph does not swallow the line", func() -> void:
		var out := _render("| a | b |\n| --- | --- |\n| 1 | 2 |\nplain text")
		_has(out, "[/table]", "table closed")
		_has_text(out, "plain text", "paragraph after the table")
		_check(out.find("[/table]") < out.find("plain text"), "paragraph must come after [/table]")
		_eq(_first_table(out).get("rows", []).size(), 2, "table must not absorb the paragraph row")
		_assert_valid(out, "table followed by text")
	)

	_it("paragraph followed by a table keeps both blocks", func() -> void:
		var out := _render("before\n\n| a | b |\n| --- | --- |\n| 1 | 2 |")
		_has_text(out, "before", "paragraph before table")
		_has(out, "[table=2]", "table after paragraph")
		_assert_valid(out, "paragraph before table")
	)

	_it("table cells allow inline formatting", func() -> void:
		var out := _render("| **b** | *i* |\n| --- | --- |\n| `c` | [l](https://example.com) |")
		_has(out, "[b]b[/b]", "bold inside table cell")
		_has(out, "[i]i[/i]", "italic inside header cell")
		_has(out, "[code]c[/code]", "inline code inside data cell")
		_has(out, "[url=https://example.com]l[/url]", "link inside table cell")
		_assert_valid(out, "table inline formatting")
	)

	_it("table column count follows the header row", func() -> void:
		var out := _render("| a | b | c |\n| --- | --- | --- |\n| 1 | 2 | 3 |")
		_has(out, "[table=3]", "three-column table")
		var table_info := _first_table(out)
		_eq(_row_contents(table_info, 0).size(), 3, "three header cells")
		_eq(_row_contents(table_info, 1).size(), 3, "three data cells")
		_assert_valid(out, "table column count")
	)


# ============================================================
# Suite: images (spec section 7 step 3)
# ============================================================

func _suite_images() -> void:
	_start_suite("images (spec section 7 step 3)")

	_it("local image renders [img] with alt attribute", func() -> void:
		var out := _render("![alt](%s)" % SCREENSHOT_PATH)
		_has(out, "[img", "image tag")
		_has(out, "[/img]", "image close tag")
		_has(out, "alt=\"alt\"", "alt attribute (Godot 4.6+)")
		_has(out, SCREENSHOT_PATH, "image path preserved")
		_assert_valid(out, "local image")
	)

	_it("image title becomes a tooltip attribute", func() -> void:
		var out := _render("![alt](%s \"hello\")" % SCREENSHOT_PATH)
		_has(out, "tooltip=\"hello\"", "image tooltip")
		_assert_valid(out, "image title")
	)

	_it("missing image falls back to alt text in italics", func() -> void:
		var out := _render_quiet("![Alt text](res://__missing__/nope.png)")
		_has(out, "[i]Alt text[/i]", "alt fallback in italics")
		_has_text(out, "Alt text", "alt text visible")
		_assert_valid(out, "missing image alt fallback")
	)

	_it("missing image without alt renders nothing", func() -> void:
		var out := _render_quiet("![](res://__missing__/nope.png)")
		_not_has(out, "[img", "no image tag for a missing image")
		_check(out.strip_edges() == "", "missing image without alt should render nothing, got %s" % _repr(out))
	)

	_it("missing image alt fallback can be disabled", func() -> void:
		var out := _render_quiet("![Alt text](res://__missing__/nope.png)", {"image_show_alt_when_missing": false})
		_not_has(out, "[img", "no image tag")
		_not_has(out, "[i]", "no alt fallback when disabled")
	)

	_it("wide images are fitted to the label width", func() -> void:
		var out := _render("![alt](%s)" % SCREENSHOT_PATH, {"size": Vector2(100, 600)})
		_has(out, "width=100%", "image fitted to 100% width")
		_assert_valid(out, "image fit width")
	)

	_it("images narrower than the label keep their natural size", func() -> void:
		var out := _render("![alt](%s)" % SCREENSHOT_PATH, {"size": Vector2(4000, 600)})
		_not_has(out, "width=", "no width attribute when the image fits")
		_assert_valid(out, "image natural size")
	)

	_it("explicit size attributes are emitted", func() -> void:
		var out := _render("![alt](%s){width=50%% height=2em}" % SCREENSHOT_PATH)
		_has(out, "width=50%", "explicit width attribute")
		_has(out, "height=2em", "explicit height attribute")
		_assert_valid(out, "image explicit size")
	)

	_it("image attributes can be disabled", func() -> void:
		var out := _render("![alt](%s){width=50%%}" % SCREENSHOT_PATH, {"image_allow_attributes": false})
		_not_has(out, "[img width=", "size extension must be ignored when image_allow_attributes is false")
		_assert_valid(out, "image attributes disabled")
	)

	_it("standalone image alignment wraps the image", func() -> void:
		for alignment in ["center", "left", "right"]:
			var out := _render("![alt](%s)" % SCREENSHOT_PATH, {"image_alignment": alignment})
			_has(out, "[%s]" % alignment, "alignment wrapper [%s]" % alignment)
			_has(out, "[/%s]" % alignment, "alignment wrapper close tag [/%s]" % alignment)
			_check(out.find("[%s]" % alignment) < out.find("[img"), "alignment wrapper must wrap the image")
			_assert_valid(out, "image alignment %s" % alignment)
	)

	_it("per-image align attribute overrides image_alignment", func() -> void:
		var out := _render("![alt](%s){align=center}" % SCREENSHOT_PATH, {"image_alignment": "inline"})
		_has(out, "[center]", "per-image align override")
		_check(out.find("[center]") < out.find("[img"), "per-image alignment must wrap the image")
		_assert_valid(out, "image per-image align")
	)

	_it("inline image is not wrapped in an alignment tag", func() -> void:
		var out := _render("see ![alt](%s) now" % SCREENSHOT_PATH, {"image_alignment": "center"})
		_not_has(out, "[center]", "inline image must not be alignment-wrapped")
		_has(out, "[img", "image tag present")
		_assert_valid(out, "inline image not aligned")
	)

	_it("image inside a blockquote is not alignment-wrapped", func() -> void:
		var out := _render("> ![alt](%s)" % SCREENSHOT_PATH, {"image_alignment": "center"})
		_has(out, "[indent]", "quote level")
		_not_has(out, "[center]", "quote line prefix disables image alignment")
		_has(out, "[img", "image tag present")
		_assert_valid(out, "quote image alignment")
	)

	_it("reference-style image resolves from a definition", func() -> void:
		var out := _render("![shot][pic]\n\n[pic]: %s" % SCREENSHOT_PATH)
		_has(out, "[img", "reference image tag")
		_has(out, SCREENSHOT_PATH, "reference image path")
		_not_has_text(out, "[pic]", "reference definition must not be rendered")
		_assert_valid(out, "reference image")
	)

	_it("collapsed reference image resolves from a definition", func() -> void:
		var out := _render("![shot][]\n\n[shot]: %s" % SCREENSHOT_PATH)
		_has(out, "[img", "collapsed reference image tag")
		_assert_valid(out, "collapsed reference image")
	)

	_it("shortcut reference image resolves when the reference exists", func() -> void:
		var out := _render("![pic]\n\n[pic]: %s" % SCREENSHOT_PATH)
		_has(out, "[img", "shortcut reference image tag")
		_assert_valid(out, "shortcut reference image")
	)

	_it("shortcut image without a reference is left alone", func() -> void:
		var out := _render("![alt]")
		_not_has(out, "[img", "undefined shortcut image must not become an image")
	)

	_it("image attribute order is alt, tooltip, width, height", func() -> void:
		var out := _render("![alt](%s \"tt\"){width=50%% height=2em}" % SCREENSHOT_PATH)
		var index_alt := out.find("alt=\"alt\"")
		var index_tooltip := out.find("tooltip=\"tt\"")
		var index_width := out.find("width=50%")
		var index_height := out.find("height=2em")
		_check(index_alt >= 0 and index_tooltip >= 0 and index_width >= 0 and index_height >= 0, "expected all image attributes, got %s" % _repr(out))
		_check(index_alt < index_tooltip, "alt must come before tooltip")
		_check(index_tooltip < index_width, "tooltip must come before width")
		_check(index_width < index_height, "width must come before height")
		_assert_valid(out, "image attribute order")
	)


# ============================================================
# Suite: links, references and autolinks (spec sections 6.8 + 7)
# ============================================================

func _suite_links() -> void:
	_start_suite("links and autolinks (spec sections 6.8 + 7)")

	_it("inline link renders [url]", func() -> void:
		var out := _render("[text](https://example.com)")
		_has(out, "[url=https://example.com]text[/url]", "inline link")
		_eq_strip(_semantic(out), "text", "link text")
		_assert_valid(out, "inline link")
	)

	_it("reference definition line is not rendered", func() -> void:
		var out := _render("[ref]: https://example.com")
		_eq_strip(out, "", "reference definition must not be rendered")
	)

	_it("reference-style link resolves from a definition", func() -> void:
		var out := _render("[text][ref]\n\n[ref]: https://example.com")
		_has(out, "[url=https://example.com]text[/url]", "reference link")
		_has_text(out, "text", "reference link text")
		_assert_valid(out, "reference link")
	)

	_it("reference-style link keeps its title as a hint", func() -> void:
		var out := _render("[text][ref]\n\n[ref]: https://example.com \"Title\"")
		_has(out, "[hint=\"Title\"]", "reference link title hint")
		_assert_valid(out, "reference link title")
	)

	_it("collapsed reference link uses the link text as label", func() -> void:
		var out := _render("[text][]\n\n[text]: https://example.com")
		_has(out, "[url=https://example.com]text[/url]", "collapsed reference link")
		_assert_valid(out, "collapsed reference link")
	)

	_it("shortcut reference link resolves from a definition", func() -> void:
		var out := _render("[ref]\n\n[ref]: https://example.com")
		_has(out, "[url=https://example.com]ref[/url]", "shortcut reference link")
		_assert_valid(out, "shortcut reference link")
	)

	_it("unquoted reference-definition text is not consumed as a title", func() -> void:
		var out := _render("[note]: this is a reminder\n\ntext")
		_has_text(out, "[note]: this is a reminder", "unquoted definition line must stay visible as text")
		_has_text(out, "text", "following paragraph still rendered")
		# The line contains a literal "[note]" which is not valid BBCode, so no tag-stack assertion here.
		var paren_out := _render("[t][id]\n\n[id]: https://example.com (Paren Title)")
		_has(paren_out, "[hint=\"Paren Title\"]", "parenthesised reference title becomes a hint")
		_has(paren_out, "[url=https://example.com]t[/url]", "parenthesised reference link")
		_assert_valid(paren_out, "parenthesised reference title")
	)

	_it("reference labels are case-insensitive", func() -> void:
		var out := _render("[x][ref]\n\n[REF]: https://example.com")
		_has(out, "[url=https://example.com]x[/url]", "case-insensitive reference label")
		_assert_valid(out, "case-insensitive reference")
	)

	_it("duplicate reference definitions keep the first one", func() -> void:
		var out := _render("[t][r]\n\n[r]: https://first.example\n[r]: https://second.example")
		_has(out, "https://first.example", "first definition wins")
		_not_has(out, "second.example", "duplicate definition must be ignored")
		_assert_valid(out, "duplicate reference definitions")
	)

	_it("angle autolink for URLs", func() -> void:
		var out := _render("<https://example.com>")
		_has(out, "[url]https://example.com[/url]", "angle URL autolink")
		_eq_strip(_semantic(out), "https://example.com", "autolink text")
		_assert_valid(out, "angle autolink")
	)

	_it("angle autolink for email addresses", func() -> void:
		var out := _render("<mail@example.com>")
		_has(out, "[url=mailto:mail@example.com]mail@example.com[/url]", "mailto autolink")
		_assert_valid(out, "mail autolink")
	)

	_it("bare URLs become autolinks", func() -> void:
		var out := _render("see https://example.com/a for details")
		_has(out, "[url]https://example.com/a[/url]", "bare URL autolink")
		_assert_valid(out, "bare URL")
	)

	_it("bare URL autolinks drop trailing punctuation", func() -> void:
		var out := _render("go https://example.com/a.")
		_has(out, "a[/url].", "trailing period must stay outside the link")
		_has_text(out, "https://example.com/a.", "visible URL keeps the period")
		_assert_valid(out, "bare URL trailing punctuation")
	)

	_it("bare www URLs get an https:// target", func() -> void:
		var out := _render("www.example.com")
		_has(out, "[url=https://www.example.com]www.example.com[/url]", "www autolink")
		_assert_valid(out, "www URL")
	)

	_it("bare URL autolinks can be disabled", func() -> void:
		var out := _render("https://example.com", {"autolink_bare_urls": false})
		_not_has(out, "[url", "bare URLs must stay plain when autolink_bare_urls is false")
		_assert_valid(out, "bare URL autolinks disabled")
	)

	_it("bare URLs inside inline code are not linked", func() -> void:
		var out := _render("`https://example.com`")
		_not_has(out, "[url", "URL inside inline code must not become a link")
		_has(out, "[code]", "inline code preserved")
		_assert_valid(out, "URL in inline code")
	)

	_it("URLs inside an existing link text are not linked twice", func() -> void:
		var out := _render("[https://a.example](https://b.example)")
		_eq(_count(out, "[url"), 1, "exactly one url tag")
		_eq(_count(out, "[/url]"), 1, "exactly one closing url tag")
		_assert_valid(out, "nested link autolink")
	)

	_it("invalid reference definition is rendered as a paragraph", func() -> void:
		var out := _render("[foo] : https://example.com")
		_has(out, "foo", "invalid definition line is kept as text")
	)


# ============================================================
# Suite: regressions and cross-cutting behaviour
# ============================================================

func _suite_regressions() -> void:
	_start_suite("regressions and cross-cutting behaviour")

	_it("converting twice on the same node yields identical output", func() -> void:
		var doc := "\n".join([
			"# Title",
			"",
			"para with **bold**, *italic*, `code`, [link](https://example.com), ![alt](" + SCREENSHOT_PATH + ")",
			"",
			"> quote",
			"> - item",
			"",
			"| a | b |",
			"| :--- | ---: |",
			"| 1 | 2 |",
			"",
			"```",
			"code [x]",
			"```",
			"",
			"- one",
			"- [ ] task",
		])
		var ml = _new_label(doc)
		if ml == null:
			return
		var out_first := _convert(ml, doc)
		var anchors_first := _anchors().duplicate()
		var checkboxes_first := _checkboxes().duplicate(true)
		var out_second := _convert(ml, doc)
		var anchors_second := _anchors().duplicate()
		var checkboxes_second := _checkboxes().duplicate(true)
		_eq(out_second, out_first, "same-input conversion output must be identical")
		_eq(anchors_second, anchors_first, "anchors must be identical after a second conversion")
		_eq(checkboxes_second, checkboxes_first, "checkbox records must be identical after a second conversion")
		_assert_valid(out_second, "idempotent conversion")
	)

	_it("task checkbox click writes the new state back to markdown_text", func() -> void:
		var ml = _new_label("- [ ] first\n- [x] second")
		if ml == null:
			return
		_convert(ml, "- [ ] first\n- [x] second")
		ml.set("automatic_links", false)
		_click_events = []
		ml.task_checkbox_clicked.connect(func(id: int, line: int, checked: bool, task_text: String) -> void:
			_click_events.append([id, line, checked, task_text])
		)
		var meta := JSON.stringify({
			"markdownlabel-checkbox": true,
			"id": 0,
			"checked": false,
		})
		_check(ml.has_method("_on_meta_clicked"), "_on_meta_clicked() is the checkbox click entry point")
		if ml.has_method("_on_meta_clicked"):
			ml._on_meta_clicked(meta)
		_eq(ml.markdown_text, "- [x] first\n- [x] second", "markdown_text after clicking the first checkbox")
		_eq(_click_events.size(), 1, "task_checkbox_clicked emission count")
		if _click_events.size() == 1:
			_eq(_click_events[0][0], 0, "signal id")
			_eq(_click_events[0][1], 0, "signal line")
			_eq(_click_events[0][2], true, "signal checked state")
			_eq(String(_click_events[0][3]).strip_edges(), "first", "signal task text")
		var out_after := _convert(ml, ml.markdown_text)
		_has(out_after, "☑", "first item is now checked")
		var records_after := _checkboxes()
		_eq(records_after.size(), 2, "records still present after write-back")
		if records_after.size() == 2:
			_eq(records_after[0].get("checked"), true, "first record now checked")
			_eq(records_after[1].get("checked"), true, "second record stays checked")
		_assert_valid(out_after, "checkbox write-back")
	)

	_it("style property changes affect the next conversion", func() -> void:
		var ml = _new_label("| a | b |\n| --- | --- |\n| 1 | 2 |")
		if ml == null:
			return
		var out_with_border := _convert(ml, ml.markdown_text)
		_has(out_with_border, "border=", "default table border")
		ml.set("table_border_color", Color(0, 0, 0, 0))
		var out_without_border := _convert(ml, ml.markdown_text)
		_not_has(out_without_border, "border=", "border removed after property change")
		_assert_valid(out_without_border, "style change")
	)

	_it("_update() render path matches _convert_markdown()", func() -> void:
		var doc := "# Title\n\npara **bold**\n\n> quote\n\n- item\n"
		var ml = _new_label(doc)
		if ml == null:
			return
		ml._update()
		var parsed_path := _semantic(ml.get_parsed_text())
		var direct := _convert(ml, doc)
		_eq(parsed_path, _semantic(direct), "_update() and _convert_markdown() must produce the same rendering")
		_check(not ml.is_processing(), "processing must be disabled after _update()")
	)

	_it("custom line hooks are called and can be overridden", func() -> void:
		var sub := GDScript.new()
		sub.source_code = "extends \"res://addons/markdownlabel/markdownlabel.gd\"\nvar pre_calls := 0\nvar custom_calls := 0\nfunc _preprocess_line(line: String) -> String:\n\tpre_calls += 1\n\treturn line\nfunc _process_custom_syntax(line: String) -> String:\n\tcustom_calls += 1\n\treturn line\n"
		var reload_result: int = sub.reload()
		_eq(reload_result, OK, "dynamic subclass of markdownlabel.gd compiles")
		if reload_result != OK:
			return
		var inst = sub.new()
		if inst == null:
			_fail("dynamic subclass instantiation failed")
			return
		var out: String = inst._convert_markdown("a\n# H\n- b")
		_check(int(inst.get("pre_calls")) >= 3, "_preprocess_line must be called for every source line (got %s)" % str(inst.get("pre_calls")))
		_check(int(inst.get("custom_calls")) >= 3, "_process_custom_syntax must be called for every rendered line (got %s)" % str(inst.get("custom_calls")))
		_has(out, "[font_size=", "subclass still renders headings")
		_has(out, "[ul]", "subclass still renders lists")
		_assert_valid(out, "custom hooks")
		inst.free()
	)

	_it("full document renders valid BBCode with all block types", func() -> void:
		var doc := "\n".join([
			"# Title",
			"",
			"Paragraph with **bold**, *italic*, `code`, ~~strike~~ and [link](https://example.com).",
			"",
			"> quoted paragraph",
			">",
			"> > nested quote",
			">",
			"> - quoted item",
			"",
			"| col a | col b |",
			"| :--- | ---: |",
			"| 1 | 2 |",
			"",
			"![shot](" + SCREENSHOT_PATH + ")",
			"",
			"```gdscript",
			"var x = [1, 2]",
			"```",
			"",
			"- plain item",
			"- [ ] task item",
			"",
			"---",
			"",
			"## Section",
			"",
			"Setext heading",
			"===",
			"",
			"[ref link][r]",
			"",
			"[r]: https://example.com/ref",
		])
		var ml = _new_label(doc)
		if ml == null:
			return
		var out := _convert(ml, doc)
		_assert_valid(out, "full document")
		for expected_text in ["Title", "Paragraph", "bold", "quoted paragraph", "nested quote", "col a", "var x", "plain item", "Section", "Setext heading"]:
			_has_text(out, expected_text, "full document text %s" % expected_text)
		_not_has(out, "```", "no fence markers in the output")
		_has(out, "[table=2]", "table rendered")
		_has(out, "[code]", "code block rendered")
		_has(out, "[hr", "horizontal rule rendered")
		_has(out, "[img", "image rendered")
		_has(out, "[url=https://example.com/ref]", "reference link rendered")
		var anchors := _anchors()
		_check(anchors.has("#title"), "full document anchor #title")
		_check(anchors.has("#section"), "full document anchor #section")
		var out_second := _convert(ml, doc)
		_eq(out_second, out, "full document conversion is idempotent")
	)
