extends RefCounted
## Block-level Markdown -> BBCode parser, internal helper for MarkdownLabel.
##
## Contract (see the addon README and the collaborative spec):
##   convert(source_text: String, host: Object) -> String
##   anchors: Dictionary          - "#slug" -> paragraph index (0-based output line)
##   checkbox_records: Dictionary - id -> {"line": int, "column": int, "marker": String, "checked": bool, "text": String}
##   has_width_dependent_images: bool
##
## The parser is a line oriented block parser. It classifies lines into blocks
## (paragraph, heading, code, quote, list, table, horizontal rule, blank),
## renders each block into BBCode lines and joins them with newlines. Inline
## Markdown is delegated to markdown_inline.gd.

const MarkdownInline := preload("markdown_inline.gd")

# Regex patterns. Compiled once per script through _re().
# The fence pattern is a normal string so that \u0060 below expands to the
# backtick character (raw strings would keep the escape sequence literally).
const _RE_FENCE := "^ {0,3}(\u0060{3,}|~{3,})(.*)$"
const _RE_ATX := r"^ {0,3}(#{1,6})(.*)$"
const _RE_ATX_OVERFLOW := r"^ {0,3}#{7,}"
const _RE_ATX_CLOSING := r"(?:^|[ \t]+)#+[ \t]*$"
const _RE_SETEXT := r"^ {0,3}(=+|-+)[ \t]*$"
const _RE_HR := r"^ {0,3}(?:(?:\*[ \t]*){3,}|(?:-[ \t]*){3,}|(?:_[ \t]*){3,})$"
const _RE_QUOTE := r"^ {0,3}> ?(.*)$"
const _RE_LIST := r"^([ \t]*)([-*+]|\d{1,9}[.)])([ \t]+)(.*)$"
const _RE_TABLE_DELIM := r"^ {0,3}\|?[ \t]*:?-+:?[ \t]*(?:\|[ \t]*:?-+:?[ \t]*)*\|?[ \t]*$"
const _RE_INDENT_CODE := r"^(?: {4}|\t)(.*)$"
const _RE_REF_DEF := r"^ {0,3}\[([^\]]+)\]:[ \t]*(?:<([^>]*)>|(\S+))(?:[ \t]+(.+?))?[ \t]*$"
const _RE_BBCODE_TAG := r"\[/?[a-zA-Z_]+(?:=[^\]]*)?\]"

const _REF_SENTINEL := "%%markdownlabel-ref%%"
const _TICK := "\u0060"

static var _regex_cache: Dictionary = {}
static var _version_hex_cache: int = -1

var anchors: Dictionary = {}
var checkbox_records: Dictionary = {}
var has_width_dependent_images := false

var _host
var _inline
var _references: Dictionary = {}
var _out_lines: PackedStringArray = PackedStringArray()
var _checkbox_id := 0
var _paragraph_count := 0
var _inside_table_output := false


func convert(source_text: String, host: Object) -> String:
	_host = host
	_references = {}
	_out_lines = PackedStringArray()
	anchors = {}
	checkbox_records = {}
	has_width_dependent_images = false
	_checkbox_id = 0
	_paragraph_count = 0
	_inside_table_output = false
	_inline = MarkdownInline.new()
	var lines := _prepare_lines(source_text)
	_extract_references(lines)
	_inline.setup(_host, _references)
	var source_lines: Array = []
	for line_index in range(lines.size()):
		source_lines.append(line_index)
	var blocks := _parse_blocks(lines, source_lines)
	_render_blocks(blocks, {"depth": 0})
	_trim_blank_lines()
	if _out_lines.is_empty():
		return ""
	return "\n".join(_out_lines)


#region Regex/version helpers

func _re(pattern: String) -> RegEx:
	if not _regex_cache.has(pattern):
		_regex_cache[pattern] = RegEx.create_from_string(pattern)
	return _regex_cache[pattern]

func _version_hex() -> int:
	if _version_hex_cache < 0:
		_version_hex_cache = Engine.get_version_info().hex
	return _version_hex_cache

#endregion


#region Source preparation and reference definitions

func _prepare_lines(source_text: String) -> PackedStringArray:
	var lines := PackedStringArray()
	for raw_line in source_text.split("\n"):
		var line: String = raw_line.trim_suffix("\r")
		if _host.has_method("_preprocess_line"):
			line = _host._preprocess_line(line)
		lines.append(line)
	return lines

func _extract_references(lines: PackedStringArray) -> void:
	var fence_char := ""
	var fence_length := 0
	for i in range(lines.size()):
		var line: String = lines[i]
		if not fence_char.is_empty():
			var stripped := line.strip_edges()
			if stripped.length() >= fence_length and not stripped.is_empty() and stripped.count(fence_char) == stripped.length():
				fence_char = ""
			continue
		var fence_match := _re(_RE_FENCE).search(line)
		if fence_match:
			fence_char = fence_match.get_string(1).substr(0, 1)
			fence_length = fence_match.get_string(1).length()
			continue
		var match := _re(_RE_REF_DEF).search(line)
		if match == null:
			continue
		var label := _normalize_reference_label(match.get_string(1))
		if label.is_empty():
			continue
		var raw_title := match.get_string(4).strip_edges()
		var title := ""
		if not raw_title.is_empty():
			title = _clean_reference_title(raw_title)
			if title == raw_title:
				# Only "title", 'title' and (title) are valid definition titles (spec 6.8);
				# an unquoted remainder means this line is ordinary paragraph text.
				continue
		lines[i] = _REF_SENTINEL
		if _references.has(label):
			continue
		var url: String = match.get_string(2)
		if url.is_empty():
			url = match.get_string(3)
		_references[label] = {"url": url, "title": title}

func _normalize_reference_label(label: String) -> String:
	var normalized := label.strip_edges().to_lower()
	while normalized.contains("  "):
		normalized = normalized.replace("  ", " ")
	return normalized

func _clean_reference_title(title: String) -> String:
	var cleaned := title.strip_edges()
	if cleaned.length() >= 2:
		var first := cleaned.substr(0, 1)
		var last := cleaned.substr(cleaned.length() - 1, 1)
		if (first == "\"" and last == "\"") or (first == "'" and last == "'") or (first == "(" and last == ")"):
			cleaned = cleaned.substr(1, cleaned.length() - 2).strip_edges()
	return cleaned

#endregion


#region Block parsing

func _parse_blocks(lines: PackedStringArray, source_lines: Array) -> Array:
	var blocks: Array = []
	var i := 0
	var count := lines.size()
	while i < count:
		var line: String = lines[i]
		if line == _REF_SENTINEL:
			i += 1
			continue
		if _is_blank(line):
			blocks.append({"type": "blank"})
			i += 1
			continue
		var fence_match := _re(_RE_FENCE).search(line)
		if fence_match:
			var code_result := _parse_fenced_code(lines, i, fence_match)
			blocks.append(code_result.block)
			i = code_result.next
			continue
		if _re(_RE_QUOTE).search(line):
			var quote_result := _parse_quote(lines, source_lines, i)
			blocks.append(quote_result.block)
			i = quote_result.next
			continue
		var atx_match := _re(_RE_ATX).search(line)
		if atx_match and _re(_RE_ATX_OVERFLOW).search(line):
			atx_match = null
		if atx_match:
			var atx_text := _strip_atx_closing(atx_match.get_string(2))
			if not atx_text.is_empty():
				blocks.append({
					"type": "heading",
					"level": atx_match.get_string(1).length(),
					"text": atx_text,
				})
				i += 1
				continue
		if i + 1 < count and _setext_level(lines[i + 1]) > 0 and _is_paragraph_candidate(line):
			blocks.append({
				"type": "heading",
				"level": _setext_level(lines[i + 1]),
				"text": line.strip_edges(),
			})
			i += 2
			continue
		if _version_hex() >= 0x040500 and _re(_RE_HR).search(line):
			blocks.append({"type": "hr"})
			i += 1
			continue
		if not _list_marker(line).is_empty():
			var list_result := _parse_list(lines, source_lines, i)
			blocks.append(list_result.block)
			i = list_result.next
			continue
		var table_start_data := _table_start(lines, i)
		if not table_start_data.is_empty():
			var table_result := _parse_table(lines, i, table_start_data)
			blocks.append(table_result.block)
			i = table_result.next
			continue
		if _re(_RE_INDENT_CODE).search(line) and (i == 0 or _is_blank(lines[i - 1])):
			var indented_result := _parse_indented_code(lines, i)
			blocks.append(indented_result.block)
			i = indented_result.next
			continue
		var paragraph_result := _parse_paragraph(lines, i)
		blocks.append(paragraph_result.block)
		i = paragraph_result.next
	return blocks

func _parse_fenced_code(lines: PackedStringArray, start: int, fence_match: RegExMatch) -> Dictionary:
	var fence_sequence := fence_match.get_string(1)
	var fence_char := fence_sequence.substr(0, 1)
	var fence_length := fence_sequence.length()
	var content: Array = []
	var i := start + 1
	while i < lines.size():
		var line: String = lines[i]
		var stripped := line.strip_edges()
		if stripped.length() >= fence_length and not stripped.is_empty() and stripped.count(fence_char) == stripped.length():
			i += 1
			break
		content.append(line)
		i += 1
	return {"block": {"type": "code", "lines": content}, "next": i}

func _parse_indented_code(lines: PackedStringArray, start: int) -> Dictionary:
	var content: Array = []
	var i := start
	while i < lines.size():
		var line: String = lines[i]
		var match := _re(_RE_INDENT_CODE).search(line)
		if match:
			content.append(match.get_string(1))
			i += 1
			continue
		if _is_blank(line):
			# Keep blank lines only if the code block continues after them.
			var j := i
			while j < lines.size() and _is_blank(lines[j]):
				j += 1
			if j < lines.size() and _re(_RE_INDENT_CODE).search(lines[j]):
				for _k in range(i, j):
					content.append("")
				i = j
				continue
			break
		break
	return {"block": {"type": "code", "lines": content}, "next": i}

func _parse_quote(lines: PackedStringArray, source_lines: Array, start: int) -> Dictionary:
	var inner: Array = []
	var inner_source_lines: Array = []
	var i := start
	var started := false
	while i < lines.size():
		var line: String = lines[i]
		var match := _re(_RE_QUOTE).search(line)
		if match:
			inner.append(match.get_string(1))
			inner_source_lines.append(source_lines[i])
			started = true
			i += 1
			continue
		if not started or _is_blank(line):
			break
		if _starts_block(lines, i):
			break
		# Lazy continuation line of a block quote paragraph.
		inner.append(line)
		inner_source_lines.append(source_lines[i])
		i += 1
	return {"block": {"type": "quote", "lines": inner, "source_lines": inner_source_lines}, "next": i}

func _parse_list(lines: PackedStringArray, source_lines: Array, start: int) -> Dictionary:
	var items: Array = []
	var i := start
	var last_item: Dictionary = {}
	while i < lines.size():
		var line: String = lines[i]
		if line == _REF_SENTINEL:
			break
		if _is_blank(line):
			var j := i + 1
			while j < lines.size() and _is_blank(lines[j]):
				j += 1
			if j < lines.size():
				var next_marker := _list_marker(lines[j])
				if not next_marker.is_empty() and not last_item.is_empty() and String(next_marker.type) == String(last_item.type):
					i = j
					continue
			break
		var marker := _list_marker(line)
		if not marker.is_empty():
			var item := {
				"type": marker.type,
				"indent": marker.indent,
				"content": marker.content,
				"source_line": int(source_lines[i]),
				"line_text": line,
			}
			items.append(item)
			last_item = item
			i += 1
			continue
		if last_item.is_empty():
			break
		if _leading_indent_width(line) <= int(last_item.indent):
			break
		if _starts_block(lines, i):
			break
		last_item.content = String(last_item.content) + " " + line.strip_edges()
		i += 1
	return {"block": {"type": "list", "items": items}, "next": i}

func _parse_paragraph(lines: PackedStringArray, start: int) -> Dictionary:
	var collected: Array = []
	var i := start
	while i < lines.size():
		var line: String = lines[i]
		if line == _REF_SENTINEL or _is_blank(line):
			break
		if i > start:
			var setext := _setext_level(line)
			if setext > 0:
				return {
					"block": {"type": "heading", "level": setext, "text": " ".join(collected)},
					"next": i + 1,
				}
			if _starts_block(lines, i):
				break
		collected.append(line.strip_edges())
		i += 1
	return {"block": {"type": "paragraph", "lines": collected}, "next": i}

func _starts_block(lines: PackedStringArray, index: int) -> bool:
	var line: String = lines[index]
	if line == _REF_SENTINEL or _is_blank(line):
		return true
	if _re(_RE_FENCE).search(line):
		return true
	if _re(_RE_QUOTE).search(line):
		return true
	if _re(_RE_ATX).search(line):
		return true
	if _version_hex() >= 0x040500 and _re(_RE_HR).search(line):
		return true
	if not _list_marker(line).is_empty():
		return true
	if not _table_start(lines, index).is_empty():
		return true
	if _re(_RE_INDENT_CODE).search(line):
		return true
	return false

func _is_paragraph_candidate(line: String) -> bool:
	if _is_blank(line) or line == _REF_SENTINEL:
		return false
	if _re(_RE_FENCE).search(line):
		return false
	if _re(_RE_QUOTE).search(line):
		return false
	if _re(_RE_ATX).search(line):
		return false
	if _version_hex() >= 0x040500 and _re(_RE_HR).search(line):
		return false
	if not _list_marker(line).is_empty():
		return false
	if _re(_RE_INDENT_CODE).search(line):
		return false
	return true

#endregion


#region Block classification helpers

func _is_blank(line: String) -> bool:
	return line.strip_edges().is_empty()

func _setext_level(line: String) -> int:
	var match := _re(_RE_SETEXT).search(line)
	if match == null:
		return 0
	return 1 if match.get_string(1).begins_with("=") else 2

func _strip_atx_closing(text: String) -> String:
	var stripped := text.strip_edges()
	var match := _re(_RE_ATX_CLOSING).search(stripped)
	if match and match.get_start() > 0:
		stripped = stripped.substr(0, match.get_start()).strip_edges()
	return stripped

func _list_marker(line: String) -> Dictionary:
	var match := _re(_RE_LIST).search(line)
	if match == null:
		return {}
	var marker_text := match.get_string(2)
	return {
		"indent": _indent_width(match.get_string(1)),
		"type": "ul" if marker_text.substr(0, 1) in "-*+" else "ol",
		"content": match.get_string(4),
	}

func _indent_width(prefix: String) -> int:
	var width := 0
	for character in prefix:
		width += 4 if character == "\t" else 1
	return width

func _leading_indent_width(line: String) -> int:
	var width := 0
	for character in line:
		if character == " ":
			width += 1
		elif character == "\t":
			width += 4
		else:
			break
	return width

func _count_unescaped_pipes(line: String) -> int:
	var count := 0
	var in_code := false
	var tick_count := 0
	var i := 0
	while i < line.length():
		var character := line.substr(i, 1)
		if character == "\\" and i + 1 < line.length():
			i += 2
			continue
		if character == _TICK:
			var run := 1
			while i + run < line.length() and line.substr(i + run, 1) == _TICK:
				run += 1
			if not in_code:
				in_code = true
				tick_count = run
			elif run == tick_count:
				in_code = false
			i += run
			continue
		if character == "|" and not in_code:
			count += 1
		i += 1
	return count

func _split_table_row(line: String) -> Array:
	var source := line.strip_edges()
	if source.begins_with("|"):
		source = source.substr(1)
	if source.ends_with("|"):
		var backslashes := 0
		var check := source.length() - 2
		while check >= 0 and source.substr(check, 1) == "\\":
			backslashes += 1
			check -= 1
		if backslashes % 2 == 0:
			source = source.substr(0, source.length() - 1)
	var cells: Array = []
	var current := ""
	var in_code := false
	var tick_count := 0
	var i := 0
	while i < source.length():
		var character := source.substr(i, 1)
		if character == "\\" and i + 1 < source.length():
			current += source.substr(i, 2)
			i += 2
			continue
		if character == _TICK:
			var run := 1
			while i + run < source.length() and source.substr(i + run, 1) == _TICK:
				run += 1
			if not in_code:
				in_code = true
				tick_count = run
			elif run == tick_count:
				in_code = false
			current += source.substr(i, run)
			i += run
			continue
		if character == "|" and not in_code:
			cells.append(current.strip_edges())
			current = ""
			i += 1
			continue
		current += character
		i += 1
	cells.append(current.strip_edges())
	return cells

func _table_start(lines: PackedStringArray, index: int) -> Dictionary:
	var line: String = lines[index]
	var pipes := _count_unescaped_pipes(line)
	if pipes < 1:
		return {}
	if index + 1 < lines.size() and _re(_RE_TABLE_DELIM).search(lines[index + 1]):
		var header_cells := _split_table_row(line)
		var delimiter_cells := _split_table_row(lines[index + 1])
		if header_cells.size() >= 1 and delimiter_cells.size() >= 1:
			var aligns := _parse_table_alignments(delimiter_cells)
			while aligns.size() < header_cells.size():
				aligns.append(String(_host.table_default_alignment))
			if aligns.size() > header_cells.size():
				aligns.resize(header_cells.size())
			return {
				"columns": header_cells.size(),
				"header": header_cells,
				"aligns": aligns,
				"has_header": true,
				"legacy": false,
			}
	if pipes >= 2:
		var cells := _split_table_row(line)
		if cells.size() >= 2:
			return {
				"columns": cells.size(),
				"header": [],
				"aligns": [],
				"has_header": false,
				"legacy": true,
			}
	return {}

func _parse_table_alignments(delimiter_cells: Array) -> Array:
	var aligns: Array = []
	for cell in delimiter_cells:
		var text: String = cell.strip_edges()
		var left := text.begins_with(":")
		var right := text.ends_with(":")
		if left and right:
			aligns.append("center")
		elif right:
			aligns.append("right")
		else:
			aligns.append("left")
	return aligns

func _parse_table(lines: PackedStringArray, start: int, table_start_data: Dictionary) -> Dictionary:
	var has_header := bool(table_start_data.has_header)
	var columns := int(table_start_data.columns)
	var rows: Array = []
	var i := start
	if has_header:
		i += 2
	else:
		i += 0
	while i < lines.size():
		var line: String = lines[i]
		if line == _REF_SENTINEL or _is_blank(line):
			break
		if _is_table_terminator(line):
			break
		if _count_unescaped_pipes(line) < 1:
			break
		rows.append(_split_table_row(line))
		i += 1
	return {
		"block": {
			"type": "table",
			"columns": columns,
			"header": table_start_data.header,
			"aligns": table_start_data.aligns,
			"rows": rows,
			"has_header": has_header,
		},
		"next": i,
	}

func _is_table_terminator(line: String) -> bool:
	if _re(_RE_FENCE).search(line):
		return true
	if _re(_RE_QUOTE).search(line):
		return true
	if _re(_RE_ATX).search(line):
		return true
	if _version_hex() >= 0x040500 and _re(_RE_HR).search(line):
		return true
	if not _list_marker(line).is_empty():
		return true
	return false

#endregion


#region Rendering

func _render_blocks(blocks: Array, context: Dictionary) -> void:
	for block in blocks:
		var block_type := String(block.type)
		match block_type:
			"paragraph":
				_render_paragraph(block, context)
			"heading":
				_render_heading(block, context)
			"code":
				_render_code(block, context)
			"quote":
				_render_quote(block, context)
			"list":
				_render_list(block, context)
			"table":
				_render_table(block)
			"hr":
				_render_hr()
			"blank":
				_render_blank(context)
			_:
				pass

func _render_paragraph(block: Dictionary, context: Dictionary) -> void:
	var bar_prefix := _quote_bar_prefix(context)
	for line in block.lines:
		var text: String = line
		if _host.image_fit_width and text.contains("!["):
			has_width_dependent_images = true
		var converted: String = _inline.convert(text, {"line_prefix": bar_prefix, "in_table": false})
		_append_line(converted)

func _render_heading(block: Dictionary, context: Dictionary) -> void:
	var level := int(block.level)
	var format = _host._get_header_format(level)
	var text: String = block.text
	var open_tags := _header_open_tags(format)
	var close_tags := _header_close_tags(format)
	var converted: String = _inline.convert(text)
	_register_anchor(_strip_bbcode(converted).strip_edges())
	_append_line(_quote_bar_prefix(context) + open_tags + converted + close_tags)
	if format != null and bool(format.draw_horizontal_rule) and _version_hex() >= 0x040500:
		_append_line(_hr_tag())

func _render_code(block: Dictionary, context: Dictionary) -> void:
	var depth := int(context.get("depth", 0))
	var bar := ""
	if depth > 0:
		bar = _plain_quote_bar_prefix(context)
	var content := PackedStringArray()
	for line in block.lines:
		content.append(bar + _escape_bbcode(String(line)))
	var code := "[code]" + "\n".join(content) + "[/code]"
	for code_line in code.split("\n"):
		_append_line(code_line)

func _render_quote(block: Dictionary, context: Dictionary) -> void:
	var inner_blocks := _parse_blocks(block.lines, block.get("source_lines", []))
	var inner_context := {"depth": int(context.get("depth", 0)) + 1}
	var start := _out_lines.size()
	_render_blocks(inner_blocks, inner_context)
	if _out_lines.size() == start:
		return
	var open_tags := "[indent]"
	var background: Color = _host.blockquote_background_color
	if background.a > 0.0:
		open_tags += "[bgcolor=#%s]" % background.to_html()
	if bool(_host.blockquote_override_text_color):
		open_tags += "[color=#%s]" % _host.blockquote_text_color.to_html()
	if bool(_host.blockquote_italic):
		open_tags += "[i]"
	var close_tags := ""
	if bool(_host.blockquote_italic):
		close_tags += "[/i]"
	if bool(_host.blockquote_override_text_color):
		close_tags += "[/color]"
	if background.a > 0.0:
		close_tags += "[/bgcolor]"
	close_tags += "[/indent]"
	_out_lines[start] = open_tags + _out_lines[start]
	var last := _out_lines.size() - 1
	_out_lines[last] = _out_lines[last] + close_tags

func _render_list(block: Dictionary, context: Dictionary) -> void:
	var tree := _build_list_tree(block.items)
	var bar_prefix := _quote_bar_prefix(context)
	for list_block in tree:
		var text: String = _render_list_to_string(list_block, bar_prefix)
		for line in text.split("\n"):
			_append_line(line)

func _render_table(block: Dictionary) -> void:
	var columns := int(block.columns)
	var aligns: Array = block.aligns
	if aligns.size() != columns:
		aligns = []
		for _column in range(columns):
			aligns.append(String(_host.table_default_alignment))
	var has_header := bool(block.has_header)
	var render_header := has_header and bool(_host.table_header_row)
	_append_line("[table=%d]" % columns)
	var data_row_index := 0
	if has_header and not render_header:
		_append_line(_render_table_row(block.header, aligns, false, data_row_index))
		data_row_index += 1
	elif render_header:
		_append_line(_render_table_row(block.header, aligns, true, 0))
	for row in block.rows:
		_append_line(_render_table_row(row, aligns, false, data_row_index))
		data_row_index += 1
	_append_line("[/table]")

func _render_table_row(cells: Array, aligns: Array, is_header: bool, data_row_index: int) -> String:
	var row := ""
	for column in range(aligns.size()):
		var text := ""
		if column < cells.size():
			text = String(cells[column])
		var converted: String = _inline.convert(text, {"in_table": true})
		if is_header and bool(_host.table_header_bold) and not converted.is_empty():
			converted = "[b]%s[/b]" % converted
		var alignment := String(aligns[column])
		if not converted.is_empty() and alignment == "center":
			converted = "[center]%s[/center]" % converted
		elif not converted.is_empty() and alignment == "right":
			converted = "[right]%s[/right]" % converted
		row += "[cell%s]%s[/cell]" % [_table_cell_attributes(is_header, data_row_index), converted]
	return row

func _table_cell_attributes(is_header: bool, data_row_index: int) -> String:
	var parts := PackedStringArray()
	var border: Color = _host.table_border_color
	if border.a > 0.0:
		parts.append("border=#%s" % border.to_html())
	if _version_hex() >= 0x040300:
		var padding: Vector4i = _host.table_cell_padding
		if padding != Vector4i.ZERO:
			parts.append("padding=%d,%d,%d,%d" % [padding.x, padding.y, padding.z, padding.w])
	var background := Color(0, 0, 0, 0)
	if is_header:
		background = _host.table_header_background
	elif data_row_index % 2 == 0:
		background = _host.table_row_background
	else:
		background = _host.table_alternate_row_background
	if background.a > 0.0:
		parts.append("bg=#%s" % background.to_html())
	if parts.is_empty():
		return ""
	return " " + " ".join(parts)

func _render_hr() -> void:
	_append_line(_hr_tag())

func _render_blank(context: Dictionary) -> void:
	var depth := int(context.get("depth", 0))
	if depth > 0:
		_append_line(_quote_bar_prefix(context))
	else:
		_append_line("")

func _escape_bbcode(source: String) -> String:
	var escaped := ""
	for character in source:
		if character == "[":
			escaped += "[lb]"
		elif character == "]":
			escaped += "[rb]"
		else:
			escaped += character
	return escaped

func _append_line(line: String) -> void:
	var output := line
	if _host.has_method("_process_custom_syntax"):
		output = _host._process_custom_syntax(line)
	_out_lines.append(output)
	# RichTextLabel treats a whole table as a single paragraph: rows separated by
	# newlines inside [table]...[/table] do not increase the paragraph index.
	if _inside_table_output:
		if line.begins_with("[/table]"):
			_inside_table_output = false
		return
	_paragraph_count += 1
	if line.begins_with("[table="):
		_inside_table_output = true

func _trim_blank_lines() -> void:
	var removed_leading := 0
	while _out_lines.size() > 0 and _out_lines[0].strip_edges().is_empty():
		_out_lines.remove_at(0)
		removed_leading += 1
	while _out_lines.size() > 0 and _out_lines[_out_lines.size() - 1].strip_edges().is_empty():
		_out_lines.remove_at(_out_lines.size() - 1)
	if removed_leading > 0:
		for anchor in anchors:
			anchors[anchor] = maxi(0, int(anchors[anchor]) - removed_leading)
		_paragraph_count = maxi(0, _paragraph_count - removed_leading)

#endregion


#region Header helpers, anchors and quote prefixes

func _header_open_tags(format) -> String:
	if format == null:
		return ""
	var tags := ""
	if bool(format.override_font_color) and format.font_color:
		tags += "[color=#%s]" % format.font_color.to_html()
	if float(format.font_size) != 0.0:
		var base_size := 16
		if _host.has_method("_get_normal_font_size"):
			base_size = int(_host._get_normal_font_size())
		tags += "[font_size=%d]" % int(float(format.font_size) * base_size)
	if bool(format.is_bold):
		tags += "[b]"
	if bool(format.is_italic):
		tags += "[i]"
	if bool(format.is_underlined):
		tags += "[u]"
	return tags

func _header_close_tags(format) -> String:
	if format == null:
		return ""
	var tags := ""
	if bool(format.is_underlined):
		tags += "[/u]"
	if bool(format.is_italic):
		tags += "[/i]"
	if bool(format.is_bold):
		tags += "[/b]"
	if float(format.font_size) != 0.0:
		tags += "[/font_size]"
	if bool(format.override_font_color) and format.font_color:
		tags += "[/color]"
	return tags

func _register_anchor(header_text: String) -> void:
	var slug := _slugify(header_text)
	if slug.is_empty():
		slug = "section"
	var base := "#" + slug
	var anchor := base
	var suffix := 0
	# Uniqueness must also consider anchors generated by earlier duplicates:
	# "#foo", "#foo-1" and an explicit "#foo-1" heading must not overwrite each other.
	while anchors.has(anchor):
		suffix += 1
		anchor = base + "-" + str(suffix)
	anchors[anchor] = _paragraph_count

func _slugify(text: String) -> String:
	var slug := ""
	var last_was_dash := false
	for character in text.to_lower():
		if character == " " or character == "\t" or character == "\n":
			if not last_was_dash and not slug.is_empty():
				slug += "-"
				last_was_dash = true
		elif character == "-" or character == "_":
			slug += character
			last_was_dash = character == "-"
		elif _is_slug_character(character):
			slug += character
			last_was_dash = false
	return slug

func _is_slug_character(character: String) -> bool:
	if character.is_empty():
		return false
	var code := character.unicode_at(0)
	if code < 128:
		return character.is_valid_identifier() or character.is_valid_int()
	# Keep Unicode letters/numbers from the scripts commonly used in documents.
	if code >= 0x00C0 and code <= 0x024F:
		return true
	if code >= 0x0370 and code <= 0x052F:
		return true
	if code >= 0x0590 and code <= 0x05FF:
		return true
	if code >= 0x0600 and code <= 0x06FF:
		return true
	if code >= 0x0900 and code <= 0x0DFF:
		return true
	if code >= 0x3040 and code <= 0x30FF:
		return true
	if code >= 0x3400 and code <= 0x4DBF:
		return true
	if code >= 0x4E00 and code <= 0x9FFF:
		return true
	if code >= 0xAC00 and code <= 0xD7AF:
		return true
	if code >= 0xF900 and code <= 0xFAFF:
		return true
	return false

func _strip_bbcode(text: String) -> String:
	var stripped := text.replacen("[lb]", "[").replacen("[rb]", "]")
	stripped = _re(_RE_BBCODE_TAG).sub(stripped, "", true)
	return stripped

func _quote_bar_prefix(context: Dictionary) -> String:
	var depth := int(context.get("depth", 0))
	if depth <= 0:
		return ""
	var bar := String(_host.blockquote_bar_character)
	if bar.is_empty():
		return ""
	var color: String = _host.blockquote_bar_color.to_html()
	var prefix := ""
	for _index in range(depth):
		prefix += "[color=#%s]%s [/color]" % [color, bar]
	return prefix

func _plain_quote_bar_prefix(context: Dictionary) -> String:
	var depth := int(context.get("depth", 0))
	if depth <= 0:
		return ""
	var bar := String(_host.blockquote_bar_character)
	if bar.is_empty():
		return ""
	var prefix := ""
	for _index in range(depth):
		prefix += bar + " "
	return prefix

func _hr_tag() -> String:
	return "[hr height=%d width=%d%% align=%s color=%s]" % [
		int(_host.hr_height),
		int(_host.hr_width),
		String(_host.hr_alignment),
		"#" + _host.hr_color.to_html(),
	]

#endregion


#region List rendering

func _build_list_tree(items: Array) -> Array:
	var roots: Array = []
	var stack: Array = []
	for item in items:
		var indent := int(item.indent)
		while stack.size() > 0 and indent < int(stack[stack.size() - 1].indent):
			stack.pop_back()
		if stack.is_empty():
			var root_block := {"type": String(item.type), "items": []}
			roots.append(root_block)
			stack.append({"indent": indent, "block": root_block, "parent_item": null})
		elif indent == int(stack[stack.size() - 1].indent):
			if String(stack[stack.size() - 1].block.type) != String(item.type):
				var old_entry: Dictionary = stack.pop_back()
				var parent_item = old_entry.parent_item
				var new_block := {"type": String(item.type), "items": []}
				if parent_item == null:
					roots.append(new_block)
				else:
					parent_item.children.append(new_block)
				stack.append({"indent": indent, "block": new_block, "parent_item": parent_item})
		else:
			var parent_block: Dictionary = stack[stack.size() - 1].block
			var parent_item_node: Dictionary = parent_block.items[parent_block.items.size() - 1]
			var child_block := {"type": String(item.type), "items": []}
			parent_item_node.children.append(child_block)
			stack.append({"indent": indent, "block": child_block, "parent_item": parent_item_node})
		var node := {"content": _list_item_bbcode(item), "children": []}
		stack[stack.size() - 1].block.items.append(node)
	return roots

func _list_item_bbcode(item: Dictionary) -> String:
	var content := String(item.content)
	var task := _match_task_marker(content)
	if task.is_empty():
		if _host.image_fit_width and content.contains("!["):
			has_width_dependent_images = true
		return _inline.convert(content)
	var rest := String(task.rest)
	var checked := bool(task.checked)
	var marker := String(task.marker)
	var line_text := String(item.line_text)
	var column := line_text.find(marker)
	if column < 0:
		column = 0
	checkbox_records[_checkbox_id] = {
		"line": int(item.source_line),
		"column": column,
		"marker": marker,
		"checked": checked,
		"text": rest.strip_edges(),
	}
	var checkbox := ""
	if checked:
		checkbox = String(_host.checked_item_character)
	else:
		checkbox = String(_host.unchecked_item_character)
	var converted: String = _inline.convert(rest)
	if bool(_host.enable_checkbox_clicks):
		var meta := {
			"markdownlabel-checkbox": true,
			"id": _checkbox_id,
			"checked": checked,
		}
		converted = "[url=%s]%s[/url]%s" % [JSON.stringify(meta), checkbox, converted]
	else:
		converted = checkbox + converted
	_checkbox_id += 1
	return converted

func _match_task_marker(content: String) -> Dictionary:
	if content.length() < 4:
		return {}
	if content.substr(0, 1) != "[" or content.substr(2, 1) != "]" or content.substr(3, 1) != " ":
		return {}
	var marker_char := content.substr(1, 1)
	if marker_char != " " and marker_char != "x" and marker_char != "X":
		return {}
	return {
		"marker": content.substr(0, 3),
		"checked": marker_char == "x" or marker_char == "X",
		"rest": content.substr(3),
	}

func _render_list_to_string(block: Dictionary, bar_prefix: String = "") -> String:
	var parts := PackedStringArray()
	for item in block.items:
		var item_text := bar_prefix + String(item.content)
		for child in item.children:
			item_text += "\n" + _render_list_to_string(child, bar_prefix)
		parts.append(item_text)
	var body := "\n".join(parts)
	var list_type := String(block.type)
	return "[%s]%s[/%s]" % [list_type, body, list_type]

#endregion
