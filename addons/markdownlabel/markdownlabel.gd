@tool
class_name MarkdownLabel
extends RichTextLabel
## A control for displaying Markdown-style text.
##
## A custom node that extends [RichTextLabel] to use Markdown instead of BBCode.
## [br][br]
## [b][u]Usage:[/u][/b]
## Simply add a MarkdownLabel to the scene and write its [member markdown_text] field in Markdown format. Alternatively, you can use the  [method display_file] method to automatically import the contents of a Markdown file.
## [br][br]
## On its [RichTextLabel] properties: [member RichTextLabel.bbcode_enabled] property must be enabled. Do not touch the [member RichTextLabel.text] property, since it's used by MarkdownLabel to properly format its text. You can use the rest of its properties as normal.
## [br][br]
## You can still use BBCode tags that don't have a Markdown equivalent, such as [code][u]underlined text[/u][/code], allowing you to have the full functionality of RichTextLabel with the simplicity and readibility of Markdown.
## [br][br]
## Supported Markdown: inline formatting (bold/italic/strikethrough/inline code), ATX and Setext headers with anchors,
## fenced and indented code blocks, block quotes (nested), unordered/ordered/task lists, GFM tables with column alignment
## and cell styling, images (inline and reference style, with optional size attributes and width fitting),
## inline and reference links, autolinks, horizontal rules and backslash escaping.
## [br][br]
## Check out the full guide in the addon readme file (linked below). If encountering any unreported bug or unexpected
## behaviour, please ensure that your Markdown is written as clean as possible, following best practices.
##
## @tutorial(Github repository): https://github.com/daenvil/MarkdownLabel

const _CHECKBOX_KEY := "markdownlabel-checkbox"
const _FRONTMATTER_REGEX := r"^(?:(?:---|\+\+\+)\r?\n([\s\S]*?)\r?\n(?:---|\+\+\+)\r?\n)?(?:\r?\n)?([\s\S]*)$"
const MarkdownParser := preload("markdown_parser.gd")

#region Public:
## Emitted when the node does not handle a click on a link. Can be used to execute custom functions when a link is clicked. [param meta] is the link metadata (in a regular link, it would be the URL).
signal unhandled_link_clicked(meta: Variant)
## Emitted when a task list checkbox is clicked. Arguments are:
## the id of the checkbox (used internally),
## the line number it is on (within the original Markdown text),
## a boolean representing whether the checkbox is now checked (true) or unchecked (false),
## and a string containing the text after the checkbox (within the same line).
signal task_checkbox_clicked(id: int, line: int, checked: bool, task_string: String)

## The text to be displayed in Markdown format.[br][br]
## [i]Note: the [member RichTextLabel.text] property is overridden so modifying it through code has the same effect as modifying [member markdown_text].[/i]
@export_multiline var markdown_text: String : set = _set_markdown_text

## If enabled, links will be automatically handled by this node, without needing to manually connect them. Valid header anchors will make the label scroll to that header's position. Valid URLs and e-mails will be opened according to the user's default settings.
@export var automatic_links := true
## If enabled, unrecognized links will be opened as HTTPS URLs (e.g. "example.com" will be opened as "https://example.com"). If disabled, unrecognized links will be left unhandled (emitting the [signal unhandled_link_clicked] signal). Ignored if [member automatic_links] is disabled.
@export var assume_https_links := true
@export_group("Links")
## If enabled, bare URLs written in the text (e.g. `https://example.com` or `www.example.com`) are converted into clickable links. This is an extension over standard Markdown.
@export var autolink_bare_urls := true :
	set(new_value):
		autolink_bare_urls = new_value
		queue_update()

@export_group("Header formats")
## Formatting options for level-1 headers
@export var h1 := H1Format.new() : set = _set_h1_format
## Formatting options for level-2 headers
@export var h2 := H2Format.new() : set = _set_h2_format
## Formatting options for level-3 headers
@export var h3 := H3Format.new() : set = _set_h3_format
## Formatting options for level-4 headers
@export var h4 := H4Format.new() : set = _set_h4_format
## Formatting options for level-5 headers
@export var h5 := H5Format.new() : set = _set_h5_format
## Formatting options for level-6 headers
@export var h6 := H6Format.new() : set = _set_h6_format

@export_group("Task lists")
## Whether task list checkboxes are clickable or not.
@export var enable_checkbox_clicks := true :
	set(new_value):
		enable_checkbox_clicks = new_value
		queue_update()
## String that will be displayed for unchecked task list items. Accepts BBCode and Markdown.
@export var unchecked_item_character := "☐" :
	set(new_value):
		unchecked_item_character = new_value
		queue_update()
## String that will be displayed for checked task list items. Accepts BBCode and Markdown.
@export var checked_item_character := "☑" :
	set(new_value):
		checked_item_character = new_value
		queue_update()

@export_group("Horizontal rules", "hr_")
## Height of horizontal rules. Only for Godot 4.5+.
@export_range(0, 99, 1, "suffix:px") var hr_height: int = 2 :
	set(new_value):
		hr_height = new_value
		queue_update()
## Width of horizontal rules, as a percentage of the label's width. Only for Godot 4.5+.
@export_range(0, 100, 1, "suffix:%") var hr_width: float = 90 :
	set(new_value):
		hr_width = new_value
		queue_update()
## Alignment of horizontal rules. Only for Godot 4.5+.
@export_enum("left", "center", "right") var hr_alignment: String = "center" :
	set(new_value):
		hr_alignment = new_value
		queue_update()
## Color of horizontal rules. Only for Godot 4.5+.
@export var hr_color: Color = Color.WHITE :
	set(new_value):
		hr_color = new_value
		queue_update()

@export_group("Blockquotes", "blockquote_")
## Character used to draw the left bar of block quotes. Set to an empty string to disable the bar.
@export var blockquote_bar_character: String = "│" :
	set(new_value):
		blockquote_bar_character = new_value
		queue_update()
## Color of the block quote left bar.
@export var blockquote_bar_color: Color = Color(0.55, 0.55, 0.55) :
	set(new_value):
		blockquote_bar_color = new_value
		queue_update()
## If enabled, text inside block quotes uses [member blockquote_text_color] instead of inheriting the label's default color.
@export var blockquote_override_text_color := false :
	set(new_value):
		blockquote_override_text_color = new_value
		queue_update()
## Custom text color for block quotes. Ignored if [member blockquote_override_text_color] is disabled.
@export var blockquote_text_color: Color = Color(0.75, 0.75, 0.75) :
	set(new_value):
		blockquote_text_color = new_value
		queue_update()
## If enabled, text inside block quotes is displayed in italics.
@export var blockquote_italic := false :
	set(new_value):
		blockquote_italic = new_value
		queue_update()
## Optional background color for block quotes. Disabled when its alpha is zero.
@export var blockquote_background_color: Color = Color(0, 0, 0, 0) :
	set(new_value):
		blockquote_background_color = new_value
		queue_update()

@export_group("Tables", "table_")
## Whether the first row of a table with a delimiter row is displayed as a header row.
@export var table_header_row := true :
	set(new_value):
		table_header_row = new_value
		queue_update()
## Whether header cells are displayed in bold.
@export var table_header_bold := true :
	set(new_value):
		table_header_bold = new_value
		queue_update()
## Background color of header cells. Disabled when its alpha is zero.
@export var table_header_background: Color = Color(0, 0, 0, 0) :
	set(new_value):
		table_header_background = new_value
		queue_update()
## Border color of table cells. Disabled when its alpha is zero.
@export var table_border_color: Color = Color(0.55, 0.55, 0.55, 0.4) :
	set(new_value):
		table_border_color = new_value
		queue_update()
## Padding of table cells, in the order left, top, right, bottom. Only for Godot 4.3+.
@export var table_cell_padding: Vector4i = Vector4i(3, 2, 3, 2) :
	set(new_value):
		table_cell_padding = new_value
		queue_update()
## Background color of odd table data rows (1st, 3rd...). Disabled when its alpha is zero.
@export var table_row_background: Color = Color(0, 0, 0, 0) :
	set(new_value):
		table_row_background = new_value
		queue_update()
## Background color of even table data rows (2nd, 4th...). Disabled when its alpha is zero.
@export var table_alternate_row_background: Color = Color(0, 0, 0, 0) :
	set(new_value):
		table_alternate_row_background = new_value
		queue_update()
## Default column alignment for tables without a delimiter row.
@export_enum("left", "center", "right") var table_default_alignment: String = "left" :
	set(new_value):
		table_default_alignment = new_value
		queue_update()

@export_group("Images", "image_")
## Whether images wider than the label are scaled down to fit it.
@export var image_fit_width := true :
	set(new_value):
		image_fit_width = new_value
		queue_update()
## Alignment used by images that occupy a whole line on their own.
@export_enum("inline", "left", "center", "right") var image_alignment: String = "inline" :
	set(new_value):
		image_alignment = new_value
		queue_update()
## If enabled, images whose file is not found are replaced by their alt text.
@export var image_show_alt_when_missing := true :
	set(new_value):
		image_show_alt_when_missing = new_value
		queue_update()
## If enabled, images support the optional size extension, e.g. `![alt](res://image.png){width=50% height=2em align=center}`.
@export var image_allow_attributes := true :
	set(new_value):
		image_allow_attributes = new_value
		queue_update()

#endregion

#region Private:
var _dirty: bool = false
var _header_anchor_paragraph := {}
var _checkbox_record := {}
var _frontmatter := ""
var _has_width_dependent_images := false
var _last_conversion_width: float = -1.0
var _url_regex: RegEx
var _mail_regex: RegEx
#endregion

#region Built-in methods:
@warning_ignore("shadowed_variable")
func _init(markdown_text: String = "") -> void:
	bbcode_enabled = true
	self.markdown_text = markdown_text
	_url_regex = RegEx.create_from_string("^(ftp|http|https):\\/\\/[^\\s\\\"]+$")
	_mail_regex = RegEx.create_from_string("^mailto:[^\\s]+@[^\\s]+\\.[^\\s]+$")
	meta_clicked.connect(_on_meta_clicked)
	resized.connect(_on_resized)

func _ready() -> void:
	for header_format: Resource in [h1, h2, h3, h4, h5, h6]:
		_connect_header_format(header_format)
	if Engine.is_editor_hint():
		bbcode_enabled = true
	queue_update()

func _process(_delta: float) -> void:
	if _dirty:
		_update()

func _on_meta_clicked(meta: Variant) -> void:
	if typeof(meta) != TYPE_STRING:
		unhandled_link_clicked.emit(meta)
		return
	var meta_text: String = meta
	if meta_text.begins_with("{") and _CHECKBOX_KEY in meta_text:
		var parsed: Variant = JSON.parse_string(meta_text)
		if parsed is Dictionary and parsed.get(_CHECKBOX_KEY, false) and _checkbox_record.has(int(parsed.get("id", -1))):
			_on_checkbox_clicked(int(parsed.get("id", -1)), bool(parsed.get("checked", false)))
			return
	if not automatic_links:
		unhandled_link_clicked.emit(meta)
		return
	if meta_text.begins_with("#") and _header_anchor_paragraph.has(meta_text):
		self.scroll_to_paragraph(int(_header_anchor_paragraph[meta_text]))
		return
	if _url_regex.search(meta_text) or _mail_regex.search(meta_text):
		OS.shell_open(meta_text)
		return
	if assume_https_links:
		OS.shell_open("https://" + meta_text)
	else:
		unhandled_link_clicked.emit(meta)

func _validate_property(property: Dictionary) -> void:
	if property.name == "bbcode_enabled":
		property.usage = PROPERTY_USAGE_NO_EDITOR
	elif property.name == "text":
		property.usage = PROPERTY_USAGE_NONE # Don't store in scene file (markdown_text is already stored) nor show in editor

func _set(property: StringName, value: Variant) -> bool:
	if property == "text":
		_set_markdown_text(value)
		return true
	return false

func _get(property: StringName) -> Variant:
	if property == "text":
		return markdown_text
	return null

func _notification(what: int) -> void:
	if what == NOTIFICATION_TRANSLATION_CHANGED:
		queue_update()

#endregion

#region Public methods:
## Reads the specified file and displays it as markdown.[br][br]
## If [param handle_frontmatter] is [code]true[/code] (default), any valid YAML or TOML front-matter won't be displayed
## and will be saved to optionally be later retrieved using [method get_frontmatter].
## Otherwise, the document will be displayed as-is.
## Returns a [enum @GlobalScope.Error].
func display_file(file_path: String, handle_frontmatter: bool = true) -> Error:
	var result: Error = OK
	var content: String = FileAccess.get_file_as_string(file_path)
	if not content:
		result = FileAccess.get_open_error()
	if handle_frontmatter and result == OK:
		var regex := RegEx.create_from_string(_FRONTMATTER_REGEX)
		var regex_match := regex.search(content)
		if regex_match:
			_frontmatter = regex_match.get_string(1).strip_edges()
			markdown_text = regex_match.get_string(2)
			return OK
		else:
			result = ERR_BUG
	_frontmatter = ""
	markdown_text = content
	return result

## Returns the stored front-matter of the last file that was displayed using [method display_file], or an empty string otherwise.
func get_frontmatter() -> String:
	return _frontmatter

## Requests a redraw of the label, reprocessing the Markdown text.
func queue_update() -> void:
	_dirty = true
	set_process(true)
	queue_redraw()

## Converts Markdown [param source_text] to BBCode without changing the label's contents.
## Mostly useful for tests and for previewing the generated BBCode. When [param source_text] is empty,
## [member markdown_text] is converted instead.
func _convert_markdown(source_text: String = "") -> String:
	var text := source_text
	if text.is_empty():
		text = markdown_text
	return _convert_source(text)

func _convert_source(source_text: String) -> String:
	if not bbcode_enabled:
		push_warning("WARNING: MarkdownLabel node will not format Markdown syntax if it doesn't have 'bbcode_enabled=true'")
		return source_text
	var parser = MarkdownParser.new()
	var bbcode: String = parser.convert(source_text, self)
	_header_anchor_paragraph = parser.anchors
	_checkbox_record = parser.checkbox_records
	_has_width_dependent_images = parser.has_width_dependent_images
	return bbcode

#endregion

#region Private methods:
func _update() -> void:
	_dirty = false
	set_process(false)
	var source_text: String = TranslationServer.translate(markdown_text) as String if _can_auto_translate() else markdown_text
	if not bbcode_enabled:
		push_warning("WARNING: MarkdownLabel node will not format Markdown syntax if it doesn't have 'bbcode_enabled=true'")
		super.clear()
		super.add_text(source_text)
		return
	var bbcode: String = _convert_source(source_text)
	_last_conversion_width = size.x
	super.clear()
	super.parse_bbcode(bbcode)

func _on_resized() -> void:
	# Images scaled as a percentage of the label width must be recomputed when that width changes.
	if _has_width_dependent_images and not is_equal_approx(size.x, _last_conversion_width):
		queue_update()

func _can_auto_translate() -> bool:
	var version := Engine.get_version_info()
	if version.hex >= 0x040300 and has_method("can_auto_translate"):
		return call("can_auto_translate")
	return auto_translate

func _connect_header_format(header_format: Resource) -> void:
	if header_format and not header_format.changed.is_connected(queue_update):
		header_format.changed.connect(queue_update)

func _set_markdown_text(new_text: String) -> void:
	markdown_text = new_text
	queue_update()

func _set_h1_format(new_format: H1Format) -> void:
	h1 = new_format
	_connect_header_format(new_format)
	queue_update()

func _set_h2_format(new_format: H2Format) -> void:
	h2 = new_format
	_connect_header_format(new_format)
	queue_update()

func _set_h3_format(new_format: H3Format) -> void:
	h3 = new_format
	_connect_header_format(new_format)
	queue_update()

func _set_h4_format(new_format: H4Format) -> void:
	h4 = new_format
	_connect_header_format(new_format)
	queue_update()

func _set_h5_format(new_format: H5Format) -> void:
	h5 = new_format
	_connect_header_format(new_format)
	queue_update()

func _set_h6_format(new_format: H6Format) -> void:
	h6 = new_format
	_connect_header_format(new_format)
	queue_update()

## This method is called before any block syntax is processed by the node and does nothing by default. It can be overridden to customize how the node handles each source line.
func _preprocess_line(line: String) -> String:
	return line

## This method is called for every rendered output line before it is stored and does nothing by default. It can be overridden to handle custom additional syntax.
func _process_custom_syntax(line: String) -> String:
	return line

## Returns the font size used as the reference for header scaling.
func _get_normal_font_size() -> int:
	return get_theme_font_size("normal_font_size")

func _get_header_format(level: int) -> Resource:
	match level:
		1:
			return h1
		2:
			return h2
		3:
			return h3
		4:
			return h4
		5:
			return h5
		6:
			return h6
	push_warning("Invalid header level: "+str(level))
	return null

func _on_checkbox_clicked(id: int, was_checked: bool) -> void:
	if not _checkbox_record.has(id):
		push_error("Couldn't find the clicked task list checkbox (id=%d)" % id)
		return
	var record: Dictionary = _checkbox_record[id]
	var iline: int = int(record.get("line", -1))
	var lines := markdown_text.split("\n")
	if iline < 0 or iline >= lines.size():
		push_error("Couldn't find the clicked task list checkbox (id=%d, line=%d)" % [id, iline])
		return
	var old_string: String = String(record.get("marker", "[x]" if was_checked else "[ ]"))
	var new_string := "[ ]" if was_checked else "[x]"
	var start: int = int(record.get("column", 0))
	var i := lines[iline].find(old_string, start)
	if i == -1:
		i = lines[iline].find(old_string)
	if i == -1:
		push_error("Couldn't find the clicked task list checkbox (id=%d, line=%d)" % [id, iline])
		return
	lines[iline] = lines[iline].erase(i, old_string.length()).insert(i, new_string)
	record["checked"] = not was_checked
	record["marker"] = new_string
	_checkbox_record[id] = record
	markdown_text = "\n".join(lines)
	var task_string: String = lines[iline].substr(i + new_string.length()).strip_edges()
	task_checkbox_clicked.emit(id, iline, not was_checked, task_string)
#endregion
