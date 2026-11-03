extends RefCounted
## Inline Markdown -> BBCode converter, internal to MarkdownLabel.
##
## Contract (see D:/gemuera-reports/markdownlabel-rewrite-spec.md section 7):
##   setup(host: Object, references: Dictionary) -> void
##   convert(text: String, context: Dictionary = {}) -> String
##
## context keys (optional):
##   "line_prefix": String - BBCode emitted before the converted line (blockquote bar)
##   "in_table": bool - disables image auto-fit and exclusive-line image alignment
##
## convert() is stateless: every call builds a fresh token table, so no markup leaks
## between lines. Script-level RegEx instances are compiled once and reused.
##
## Version gates (Engine.get_version_info().hex, spec section 8):
##   >= 0x040300: [img width=...%] / [img width=...em] values
##   >= 0x040600: [img alt="..."]
## Pixel width values are safe on every supported version.

## Version thresholds compared against Engine.get_version_info().hex.
const VERSION_4_3 := 0x040300
const VERSION_4_6 := 0x040600

## Token placeholders. Delimited by Unicode noncharacters so they can never be
## mistaken for Markdown syntax by a later processing step.
const TOKEN_START := "\uFDD0"
const TOKEN_END := "\uFDD1"

## Characters a backslash can escape (spec 7.1). Escaped text is protected by a token
## so later steps cannot reinterpret it; at the end it is restored as the literal
## character (escaped "[" / "]" become [lb] / [rb]).
const ESCAPABLE_CHARACTERS := "\\`*_~#+-.!|[](){}<>\"'"

## BBCode tags that are not container tags.
const VOID_TAGS := ["lb", "rb", "br", "hr"]

const SIZE_VALUE_PATTERN := "^\\d+(\\.\\d+)?(%|em)?$"
const EMAIL_PATTERN := "^[^<>()\\[\\]\\\\@\\s]+@[^<>()\\[\\]\\\\@\\s]+\\.[^<>()\\[\\]\\s]+$"
const BARE_URL_PATTERN := "(?<![\\w/])(https?://[^\\s<>\"']+)"
const BARE_WWW_PATTERN := "(?<![\\w/])(www\\.[^\\s<>\"']+)"
const URL_TRAILING_PUNCTUATION := ".,;:!?)]}"

# Script-level compiled RegEx cache (compiled once, never inside a loop).
static var _regex_size_value: RegEx = null
static var _regex_email: RegEx = null
static var _regex_bare_url: RegEx = null
static var _regex_bare_www: RegEx = null
static var _engine_hex_cache := -1

var _host: Object
var _references: Dictionary = {}

## True when the last convert() emitted an image whose width depends on host.size.x.
## MarkdownParser can OR this into its own has_width_dependent_images field.
var has_width_dependent_images := false

var _tokens: Dictionary = {}
var _next_token := 0
var _image_tokens: Dictionary = {}
var _image_sizes: Dictionary = {}
var _warned_image_paths: Dictionary = {}
var _version_hex := 0


#region Public API
func setup(host: Object, references: Dictionary) -> void:
	_host = host
	_references = references if references != null else {}


func convert(text: String, context: Dictionary = {}) -> String:
	if context == null:
		context = {}
	var line_prefix: String = str(context.get("line_prefix", ""))
	var in_table: bool = bool(context.get("in_table", false))
	_version_hex = _engine_version_hex()
	_reset_state()

	if _is_plain_text(text):
		return line_prefix + text

	var processed := text
	# Steps 1 + 2 share one scanner: backslash-escaped backticks never open a code
	# span and code span content is never escape-processed.
	processed = _process_escapes_and_code(processed)
	# Step 3: images (before links, so "![...]" is not mistaken for a link).
	processed = _process_images(processed, in_table)
	# Step 4: inline / reference links.
	processed = _process_links(processed)
	# Step 5: autolinks ("<...>" forms first, then bare URLs).
	processed = _process_autolinks(processed)
	# Step 6: strikethrough.
	processed = _process_strikethrough(processed)
	# Step 7: emphasis.
	processed = _process_emphasis(processed)
	# Exclusive-line image alignment needs the token table, so it runs before restoring.
	processed = _apply_exclusive_image_alignment(processed, in_table, line_prefix)
	# Step 8: restore every token placeholder.
	processed = _restore_tokens(processed)
	if line_prefix != "":
		processed = line_prefix + processed
	return processed
#endregion


#region State
func _reset_state() -> void:
	_tokens = {}
	_next_token = 0
	_image_tokens = {}
	_warned_image_paths = {}
	has_width_dependent_images = false


func _add_token(value: String) -> String:
	var id := _next_token
	_next_token += 1
	_tokens[id] = value
	return "%s%d%s" % [TOKEN_START, id, TOKEN_END]


func _restore_tokens(text: String) -> String:
	var result := text
	# Tokens inserted by earlier restores can contain nested tokens (for instance an
	# escaped character inside an image alt attribute), so repeat until stable.
	for _iteration in range(8):
		if not result.contains(TOKEN_START):
			break
		var parts := PackedStringArray()
		var cursor := 0
		var n := result.length()
		var replaced_any := false
		while cursor < n:
			var start := result.find(TOKEN_START, cursor)
			if start == -1:
				parts.append(result.substr(cursor))
				break
			var end := result.find(TOKEN_END, start + 1)
			if end == -1:
				parts.append(result.substr(cursor))
				break
			var id_text := result.substr(start + 1, end - start - 1)
			if not id_text.is_valid_int() or not _tokens.has(int(id_text)):
				parts.append(result.substr(cursor, start + 1 - cursor))
				cursor = start + 1
				continue
			parts.append(result.substr(cursor, start - cursor))
			parts.append(str(_tokens[int(id_text)]))
			cursor = end + 1
			replaced_any = true
		result = "".join(parts)
		if not replaced_any:
			break
	return result
#endregion


#region Step 1+2: backslash escapes and inline code
func _is_plain_text(text: String) -> bool:
	for character in text:
		if character in "\\*_~`[]!<":
			return false
	if text.contains("http://") or text.contains("https://") or text.contains("www."):
		return false
	return true


func _process_escapes_and_code(text: String) -> String:
	if not text.contains("\\") and not text.contains("`"):
		return text
	var parts := PackedStringArray()
	var i := 0
	var n := text.length()
	while i < n:
		var character := text[i]
		if character == "\\" and i + 1 < n and ESCAPABLE_CHARACTERS.contains(text[i + 1]):
			var escaped := text[i + 1]
			var literal := escaped
			if escaped == "[":
				literal = "[lb]"
			elif escaped == "]":
				literal = "[rb]"
			parts.append(_add_token(literal))
			i += 2
		elif character == "`":
			var run_length := _count_run(text, i, "`")
			var close_index := _find_code_span_close(text, i + run_length, run_length)
			if close_index == -1:
				parts.append("`".repeat(run_length))
			else:
				var content := text.substr(i + run_length, close_index - i - run_length)
				content = _strip_code_span_spaces(content)
				parts.append(_add_token("[code]%s[/code]" % _escape_bbcode(content)))
				i = close_index + run_length
				continue
			i += run_length
		else:
			parts.append(character)
			i += 1
	return "".join(parts)


func _count_run(text: String, start: int, character: String) -> int:
	var count := 0
	var n := text.length()
	while start + count < n and text[start + count] == character:
		count += 1
	return count


func _find_code_span_close(text: String, from: int, run_length: int) -> int:
	var i := from
	var n := text.length()
	while i < n:
		if text[i] == "`":
			var run := _count_run(text, i, "`")
			if run == run_length:
				return i
			i += run
		else:
			i += 1
	return -1


func _strip_code_span_spaces(content: String) -> String:
	# CommonMark: if the content begins and ends with a space but is not all spaces,
	# strip one space from each side.
	if content.length() >= 2 and content.begins_with(" ") and content.ends_with(" ") and content.strip_edges() != "":
		return content.substr(1, content.length() - 2)
	return content


func _escape_bbcode(text: String) -> String:
	# Two-phase replacement is required: inserting "[lb]" first would let the "]" pass
	# corrupt it ("a[1]" must become "a[lb]1[rb]", never "a[lb[rb]1[rb]").
	return text.replace("[", "\uFDD2").replace("]", "[rb]").replace("\uFDD2", "[lb]")
#endregion


#region Step 3: images
func _process_images(text: String, in_table: bool) -> String:
	if not text.contains("!["):
		return text
	var parts := PackedStringArray()
	var i := 0
	var n := text.length()
	while i < n:
		var start := text.find("![", i)
		if start == -1:
			parts.append(text.substr(i))
			break
		parts.append(text.substr(i, start - i))
		var parsed := _parse_image_at(text, start, in_table)
		if parsed.is_empty():
			parts.append("![")
			i = start + 2
			continue
		parts.append(str(parsed["token"]))
		i = int(parsed["end"])
	return "".join(parts)


func _parse_image_at(text: String, start: int, in_table: bool) -> Dictionary:
	# Image candidates that cannot be resolved stay literal "![", exactly like
	# unescaped raw BBCode is preserved.
	var alt_end := _find_matching_bracket(text, start + 1)
	if alt_end == -1:
		return {}
	var alt := text.substr(start + 2, alt_end - start - 2)
	var cursor := alt_end + 1
	var n := text.length()
	var url := ""
	var title := ""
	if cursor < n and text[cursor] == "(":
		var destination := _parse_inline_destination(text, cursor)
		if destination.is_empty():
			return {}
		url = str(destination["url"])
		title = str(destination["title"])
		cursor = int(destination["end"])
	elif cursor < n and text[cursor] == "[":
		var ref_end := _find_matching_bracket(text, cursor)
		if ref_end == -1:
			return {}
		var ref_label := text.substr(cursor + 1, ref_end - cursor - 1)
		if ref_label.strip_edges() == "":
			ref_label = alt
		var reference := _lookup_reference(ref_label)
		if reference.is_empty():
			return {}
		url = _safe_string(reference.get("url", ""))
		title = _safe_string(reference.get("title", ""))
		cursor = ref_end + 1
	else:
		var shortcut := _lookup_reference(alt)
		if shortcut.is_empty():
			return {}
		url = _safe_string(shortcut.get("url", ""))
		title = _safe_string(shortcut.get("title", ""))
	# Optional size/alignment extension, immediately after the image.
	var attributes: Dictionary = {}
	if bool(_host_get("image_allow_attributes", true)) and cursor < n and text[cursor] == "{":
		var attributes_end := text.find("}", cursor)
		if attributes_end != -1:
			var parsed_attributes := _parse_image_attributes(text.substr(cursor + 1, attributes_end - cursor - 1))
			if not parsed_attributes.is_empty():
				attributes = parsed_attributes
				cursor = attributes_end + 1
	var rendered := _render_image(alt, url, title, attributes, in_table)
	if str(rendered["markup"]) == "":
		return {"token": "", "end": cursor}
	var token := _add_token(str(rendered["markup"]))
	_image_tokens[token] = rendered
	return {"token": token, "end": cursor}


func _parse_image_attributes(block: String) -> Dictionary:
	var attributes: Dictionary = {}
	for part in block.split(" ", false):
		var separator := part.find("=")
		if separator <= 0:
			continue
		var key := part.substr(0, separator).strip_edges()
		var value := part.substr(separator + 1).strip_edges()
		if key == "width" or key == "height":
			if _get_size_regex().search(value) != null:
				attributes[key] = value
		elif key == "align":
			if value == "left" or value == "center" or value == "right" or value == "inline":
				attributes[key] = value
	return attributes


func _render_image(alt: String, url: String, title: String, attributes: Dictionary, in_table: bool) -> Dictionary:
	var alignment := _safe_string(attributes.get("align", ""))
	var path := url.strip_edges()
	if path.contains("[") or path.contains("]"):
		# A raw bracket inside the [img] body would inject BBCode; treat the path as invalid.
		_warn_missing_image(path)
		var invalid_markup := ""
		if bool(_host_get("image_show_alt_when_missing", true)) and alt.strip_edges() != "":
			invalid_markup = "[i]%s[/i]" % _escape_bbcode(alt)
		return {"markup": invalid_markup, "align": alignment}
	var width_value := _gate_size_value(_safe_string(attributes.get("width", "")))
	var height_value := _gate_size_value(_safe_string(attributes.get("height", "")))
	var has_explicit_size: bool = attributes.has("width") or attributes.has("height")
	if not _image_exists(path):
		_warn_missing_image(path)
		var markup := ""
		if bool(_host_get("image_show_alt_when_missing", true)) and alt.strip_edges() != "":
			markup = "[i]%s[/i]" % _escape_bbcode(alt)
		return {"markup": markup, "align": alignment}
	if not has_explicit_size and bool(_host_get("image_fit_width", true)) and not in_table:
		var label_width := _host_size_x()
		if label_width > 0.0:
			var natural_width := _image_natural_width(path)
			if natural_width > int(label_width):
				has_width_dependent_images = true
				if _version_hex >= VERSION_4_3:
					width_value = "100%"
				else:
					width_value = str(int(label_width))
	var attribute_parts := PackedStringArray()
	var alt_text := _clean_attribute_text(alt)
	if alt_text != "" and _version_hex >= VERSION_4_6:
		attribute_parts.append("alt=\"%s\"" % alt_text)
	var title_text := _clean_attribute_text(title)
	if title_text != "":
		attribute_parts.append("tooltip=\"%s\"" % title_text)
	if width_value != "":
		attribute_parts.append("width=%s" % width_value)
	if height_value != "":
		attribute_parts.append("height=%s" % height_value)
	var attribute_string := ""
	if not attribute_parts.is_empty():
		attribute_string = " " + " ".join(attribute_parts)
	return {"markup": "[img%s]%s[/img]" % [attribute_string, path], "align": alignment}


func _gate_size_value(value: String) -> String:
	if value == "":
		return ""
	if _version_hex >= VERSION_4_3:
		return value
	# Godot 4.2 only understands plain pixel values; % / em units are dropped.
	if value.is_valid_int():
		return value
	return ""


func _image_exists(path: String) -> bool:
	if path == "":
		return false
	if path.begins_with("res://") or path.begins_with("user://"):
		return ResourceLoader.exists(path, "Texture2D")
	return FileAccess.file_exists(path)


func _image_natural_width(path: String) -> int:
	if _image_sizes.has(path):
		return int(_image_sizes[path])
	var width := -1
	var resource = load(path)
	if resource is Texture2D:
		width = (resource as Texture2D).get_width()
	_image_sizes[path] = width
	return width


func _warn_missing_image(path: String) -> void:
	if _warned_image_paths.has(path):
		return
	_warned_image_paths[path] = true
	push_warning("MarkdownLabel: image not found: %s" % path)


func _apply_exclusive_image_alignment(text: String, in_table: bool, line_prefix: String) -> String:
	if in_table or line_prefix != "":
		return text
	var trimmed := text.strip_edges()
	if trimmed == "" or not _image_tokens.has(trimmed):
		return text
	var alignment := _safe_string(_image_tokens[trimmed].get("align", ""))
	if alignment == "":
		alignment = _safe_string(_host_get("image_alignment", "inline"))
	if alignment == "left" or alignment == "center" or alignment == "right":
		return "[%s]%s[/%s]" % [alignment, trimmed, alignment]
	return text
#endregion


#region Step 4: links
func _process_links(text: String) -> String:
	if not text.contains("["):
		return text
	var parts := PackedStringArray()
	var i := 0
	var n := text.length()
	while i < n:
		var open := text.find("[", i)
		if open == -1:
			parts.append(text.substr(i))
			break
		parts.append(text.substr(i, open - i))
		var parsed := _parse_link_at(text, open)
		if parsed.is_empty():
			parts.append("[")
			i = open + 1
			continue
		parts.append(str(parsed["token"]))
		i = int(parsed["end"])
	return "".join(parts)


func _parse_link_at(text: String, open: int) -> Dictionary:
	var close := _find_matching_bracket(text, open)
	if close == -1:
		return {}
	var label := text.substr(open + 1, close - open - 1)
	if not _tags_balanced(label):
		return {}
	var cursor := close + 1
	var n := text.length()
	if cursor < n and text[cursor] == "(":
		var destination := _parse_inline_destination(text, cursor)
		if destination.is_empty():
			return {}
		var url := _safe_string(destination.get("url", ""))
		if url == "":
			return {"token": label, "end": int(destination["end"])}
		return {
			"token": _render_link(label, url, _safe_string(destination.get("title", ""))),
			"end": int(destination["end"]),
		}
	if cursor < n and text[cursor] == "[":
		var ref_end := _find_matching_bracket(text, cursor)
		if ref_end == -1:
			return {}
		var ref_label := text.substr(cursor + 1, ref_end - cursor - 1).strip_edges()
		if ref_label == "":
			ref_label = label
		var reference := _lookup_reference(ref_label)
		if reference.is_empty():
			return {}
		return {
			"token": _render_link(label, _safe_string(reference.get("url", "")), _safe_string(reference.get("title", ""))),
			"end": ref_end + 1,
		}
	var shortcut := _lookup_reference(label)
	if shortcut.is_empty():
		return {}
	return {
		"token": _render_link(label, _safe_string(shortcut.get("url", "")), _safe_string(shortcut.get("title", ""))),
		"end": cursor,
	}


func _render_link(label: String, url: String, title: String) -> String:
	# A double quote inside a [url=...] attribute would break the tag (spec 7.4).
	var clean_url := url.replace("\"", "").strip_edges()
	# A raw "]" would terminate the [url=...] attribute early; percent-encode BBCode delimiters.
	clean_url = clean_url.replace("]", "%5D").replace("[", "%5B")
	var link := "[url=%s]%s[/url]" % [_add_token(clean_url), label]
	if title.strip_edges() == "":
		return link
	return "[hint=\"%s\"]%s[/hint]" % [_add_token(_clean_attribute_text(title)), link]
#endregion


#region Step 5: autolinks
func _process_autolinks(text: String) -> String:
	if not text.contains("<") and not text.contains("http://") and not text.contains("https://") and not text.contains("www."):
		return text
	var processed := _process_angle_autolinks(text)
	if bool(_host_get("autolink_bare_urls", true)):
		processed = _process_bare_autolinks(processed)
	return processed


func _process_angle_autolinks(text: String) -> String:
	var spans := _collect_bbcode_spans(text)
	var tag_spans: Array = spans["tags"]
	var url_text_spans: Array = spans["url_text"]
	var parts := PackedStringArray()
	var i := 0
	var n := text.length()
	while i < n:
		var character := text[i]
		if character == "<":
			var close := text.find(">", i + 1)
			if close != -1 and not _is_span_position(tag_spans, i) and not _is_span_position(url_text_spans, i):
				var inner := text.substr(i + 1, close - i - 1)
				var email := _match_email(inner)
				if email != "":
					parts.append("[url=mailto:%s]%s[/url]" % [_add_token(email), _add_token(email)])
					i = close + 1
					continue
				if _is_angle_url(inner):
					parts.append("[url]%s[/url]" % _add_token(inner))
					i = close + 1
					continue
		parts.append(character)
		i += 1
	return "".join(parts)


func _match_email(value: String) -> String:
	var result := _get_email_regex().search(value)
	if result == null or result.get_start() != 0 or result.get_end() != value.length():
		return ""
	return value


func _is_angle_url(value: String) -> bool:
	if value != value.strip_edges():
		return false
	if value.contains("<") or value.contains(" "):
		return false
	return value.begins_with("http://") or value.begins_with("https://") or value.begins_with("ftp://")


func _process_bare_autolinks(text: String) -> String:
	var processed := _bare_url_pass(text, _get_bare_url_regex(), false)
	processed = _bare_url_pass(processed, _get_bare_www_regex(), true)
	return processed


func _bare_url_pass(text: String, pattern: RegEx, is_www: bool) -> String:
	var spans := _collect_bbcode_spans(text)
	var tag_spans: Array = spans["tags"]
	var url_text_spans: Array = spans["url_text"]
	var parts := PackedStringArray()
	var cursor := 0
	var search_from := 0
	var n := text.length()
	while search_from < n:
		var result := pattern.search(text, search_from)
		if result == null:
			break
		var match_start := result.get_start()
		var match_end := result.get_end()
		if _is_span_position(tag_spans, match_start) or _is_span_position(url_text_spans, match_start):
			search_from = match_start + 1
			continue
		var url := result.get_string(1)
		var trailing := ""
		while url.length() > 0 and URL_TRAILING_PUNCTUATION.contains(url[url.length() - 1]):
			trailing = url[url.length() - 1] + trailing
			url = url.substr(0, url.length() - 1)
		if url == "" or url.ends_with("://") or url == "www.":
			search_from = match_start + 1
			continue
		parts.append(text.substr(cursor, match_start - cursor))
		if is_www:
			parts.append("[url=%s]%s[/url]" % [_add_token("https://" + url), _add_token(url)])
		else:
			parts.append("[url]%s[/url]" % _add_token(url))
		parts.append(trailing)
		cursor = match_end
		search_from = match_end
	parts.append(text.substr(cursor))
	return "".join(parts)
#endregion


#region Step 6+7: strikethrough and emphasis
func _process_strikethrough(text: String) -> String:
	if not text.contains("~~"):
		return text
	return _apply_pair(text, "~~", "[s]", "[/s]", false)


func _process_emphasis(text: String) -> String:
	if not text.contains("*") and not text.contains("_"):
		return text
	var processed := text
	# Triple markers first so that "***x***" is not eaten by the "**" pass.
	processed = _apply_pair(processed, "***", "[b][i]", "[/i][/b]", false)
	processed = _apply_pair(processed, "___", "[b][i]", "[/i][/b]", true)
	processed = _apply_pair(processed, "**", "[b]", "[/b]", false)
	processed = _apply_pair(processed, "__", "[b]", "[/b]", true)
	processed = _apply_pair(processed, "*", "[i]", "[/i]", false)
	processed = _apply_pair(processed, "_", "[i]", "[/i]", true)
	return processed


func _apply_pair(text: String, marker: String, open_tag: String, close_tag: String, underscore_rule: bool) -> String:
	var parts := PackedStringArray()
	var i := 0
	var n := text.length()
	while i < n:
		var open := text.find(marker, i)
		if open == -1:
			parts.append(text.substr(i))
			break
		var after_open := open + marker.length()
		if not _can_open_marker(text, open, marker, underscore_rule):
			parts.append(text.substr(i, after_open - i))
			i = after_open
			continue
		var close := -1
		var search_from := after_open
		while true:
			var candidate := text.find(marker, search_from)
			if candidate == -1:
				break
			var content := text.substr(after_open, candidate - after_open)
			if content.strip_edges() != "" \
					and _can_close_marker(text, candidate, marker, underscore_rule) \
					and _tags_balanced(content):
				close = candidate
				break
			search_from = candidate + marker.length()
		if close == -1:
			parts.append(text.substr(i, after_open - i))
			i = after_open
			continue
		parts.append(text.substr(i, open - i))
		parts.append(open_tag)
		parts.append(text.substr(after_open, close - after_open))
		parts.append(close_tag)
		i = close + marker.length()
	return "".join(parts)


func _can_open_marker(text: String, index: int, marker: String, underscore_rule: bool) -> bool:
	if not underscore_rule:
		return true
	var previous := ""
	if index > 0:
		previous = text[index - 1]
	var following := ""
	if index + marker.length() < text.length():
		following = text[index + marker.length()]
	return not (_is_alphanumeric(previous) and _is_alphanumeric(following))


func _can_close_marker(text: String, index: int, marker: String, underscore_rule: bool) -> bool:
	if not underscore_rule:
		return true
	var previous := ""
	if index > 0:
		previous = text[index - 1]
	var following := ""
	if index + marker.length() < text.length():
		following = text[index + marker.length()]
	return not (_is_alphanumeric(previous) and _is_alphanumeric(following))


func _is_alphanumeric(character: String) -> bool:
	if character.length() != 1:
		return false
	var code := character.unicode_at(0)
	if code >= 48 and code <= 57:
		return true
	if code >= 65 and code <= 90:
		return true
	if code >= 97 and code <= 122:
		return true
	# Treat CJK / fullwidth alphanumerics as word characters too, so intraword
	# underscores stay literal in mixed-language text.
	if code >= 0x4E00 and code <= 0x9FFF:
		return true
	if code >= 0x3040 and code <= 0x30FF:
		return true
	if code >= 0xFF10 and code <= 0xFF19:
		return true
	if code >= 0xFF21 and code <= 0xFF3A:
		return true
	if code >= 0xFF41 and code <= 0xFF5A:
		return true
	return false


func _tags_balanced(text: String) -> bool:
	# A candidate emphasis range must not cross an existing BBCode tag boundary;
	# otherwise "[b]a [i]b[/b] c[/i]" would be produced.
	var stack: Array[String] = []
	var i := 0
	var n := text.length()
	while i < n:
		if text[i] == "[":
			var tag := _tag_name_at(text, i)
			if not tag.is_empty():
				var name: String = tag["name"]
				if not VOID_TAGS.has(name):
					if bool(tag["closing"]):
						if stack.is_empty() or stack[stack.size() - 1] != name:
							return false
						stack.pop_back()
					else:
						stack.push_back(name)
				i = int(tag["end"])
				continue
		i += 1
	return stack.is_empty()
#endregion


#region Shared helpers
func _find_matching_bracket(text: String, open_index: int) -> int:
	var depth := 1
	var i := open_index + 1
	var n := text.length()
	while i < n:
		var character := text[i]
		if character == "[":
			depth += 1
		elif character == "]":
			depth -= 1
			if depth == 0:
				return i
		i += 1
	return -1


func _parse_inline_destination(text: String, open_parenthesis: int) -> Dictionary:
	var n := text.length()
	var i := open_parenthesis + 1
	while i < n and (text[i] == " " or text[i] == "\t"):
		i += 1
	var url := ""
	if i < n and text[i] == "<":
		var close := text.find(">", i + 1)
		if close == -1:
			return {}
		url = text.substr(i + 1, close - i - 1)
		i = close + 1
	else:
		var destination_start := i
		var depth := 0
		while i < n:
			var character := text[i]
			if character == " " or character == "\t":
				break
			elif character == "(":
				depth += 1
			elif character == ")":
				if depth == 0:
					break
				depth -= 1
			i += 1
		url = text.substr(destination_start, i - destination_start)
	while i < n and (text[i] == " " or text[i] == "\t"):
		i += 1
	var title := ""
	if i < n and (text[i] == "\"" or text[i] == "'"):
		var quote := text[i]
		var title_end := text.find(quote, i + 1)
		if title_end == -1:
			return {}
		title = text.substr(i + 1, title_end - i - 1)
		i = title_end + 1
		while i < n and (text[i] == " " or text[i] == "\t"):
			i += 1
	if i >= n or text[i] != ")":
		return {}
	return {"url": url.strip_edges(), "title": title, "end": i + 1}


func _lookup_reference(label: String) -> Dictionary:
	var key := _normalize_reference_label(label)
	if key == "" or not _references.has(key):
		return {}
	var reference = _references[key]
	if reference is Dictionary:
		return reference
	return {}


func _normalize_reference_label(label: String) -> String:
	var lowered := label.strip_edges().to_lower()
	var parts := PackedStringArray()
	var previous_was_space := false
	for character in lowered:
		if character == " " or character == "\t" or character == "\n" or character == "\r":
			if not previous_was_space and parts.size() > 0:
				parts.append(" ")
			previous_was_space = true
		else:
			parts.append(character)
			previous_was_space = false
	var normalized := "".join(parts)
	return normalized.strip_edges()


func _clean_attribute_text(value: String) -> String:
	return value.replace("\"", "'").strip_edges()


func _safe_string(value) -> String:
	if value == null:
		return ""
	return str(value)


func _tag_name_at(text: String, open_index: int) -> Dictionary:
	# Returns {"name": String, "closing": bool, "end": int} for a BBCode tag starting
	# at open_index, or {} when "[" does not start a tag (e.g. a literal bracket).
	var n := text.length()
	var i := open_index + 1
	var closing := false
	if i < n and text[i] == "/":
		closing = true
		i += 1
	var name_start := i
	while i < n:
		var code := text.unicode_at(i)
		if (code >= 65 and code <= 90) or (code >= 97 and code <= 122) or code == 95:
			i += 1
		else:
			break
	if i == name_start:
		return {}
	var name := text.substr(name_start, i - name_start).to_lower()
	while i < n and text[i] != "]":
		if text[i] == "[":
			return {}
		i += 1
	if i >= n:
		return {}
	return {"name": name, "closing": closing, "end": i + 1}


func _collect_bbcode_spans(text: String) -> Dictionary:
	var tag_spans: Array = []
	var url_text_spans: Array = []
	var depth := 0
	var inner_start := -1
	var i := 0
	var n := text.length()
	while i < n:
		var open := text.find("[", i)
		if open == -1:
			break
		var tag := _tag_name_at(text, open)
		if tag.is_empty():
			i = open + 1
			continue
		var tag_end: int = int(tag["end"])
		tag_spans.append(Vector2i(open, tag_end))
		var name: String = tag["name"]
		var closing: bool = tag["closing"]
		if name == "url":
			if not closing:
				if depth == 0:
					inner_start = tag_end
				depth += 1
			else:
				if depth == 1 and inner_start >= 0:
					url_text_spans.append(Vector2i(inner_start, open))
				depth = maxi(depth - 1, 0)
		i = tag_end
	return {"tags": tag_spans, "url_text": url_text_spans}


func _is_span_position(spans: Array, position: int) -> bool:
	for span in spans:
		if position >= int(span.x) and position < int(span.y):
			return true
	return false


func _host_get(property_name: String, default_value):
	if _host == null:
		return default_value
	var value = _host.get(property_name)
	if value == null:
		return default_value
	return value


func _host_size_x() -> float:
	var host_size = _host_get("size", Vector2.ZERO)
	if host_size is Vector2:
		return (host_size as Vector2).x
	if host_size is Vector2i:
		return float((host_size as Vector2i).x)
	return 0.0


static func _engine_version_hex() -> int:
	if _engine_hex_cache < 0:
		_engine_hex_cache = int(Engine.get_version_info().get("hex", 0))
	return _engine_hex_cache


static func _get_size_regex() -> RegEx:
	if _regex_size_value == null:
		_regex_size_value = RegEx.create_from_string(SIZE_VALUE_PATTERN)
	return _regex_size_value


static func _get_email_regex() -> RegEx:
	if _regex_email == null:
		_regex_email = RegEx.create_from_string(EMAIL_PATTERN)
	return _regex_email


static func _get_bare_url_regex() -> RegEx:
	if _regex_bare_url == null:
		_regex_bare_url = RegEx.create_from_string(BARE_URL_PATTERN)
	return _regex_bare_url


static func _get_bare_www_regex() -> RegEx:
	if _regex_bare_www == null:
		_regex_bare_www = RegEx.create_from_string(BARE_WWW_PATTERN)
	return _regex_bare_www
#endregion
