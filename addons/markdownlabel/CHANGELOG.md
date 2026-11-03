# Changelog

All notable changes to this **gEmuera fork** of MarkdownLabel are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). The fork is based on
[Daenvil/MarkdownLabel](https://github.com/daenvil/MarkdownLabel) 1.4.0 and keeps its MIT license.

## [2.0.0] - 2026-11-03

First release of the gEmuera-maintained fork. The public API of 1.4.0 is unchanged; see
[README.md](README.md#upgrading-from-140) for the upgrade notes.

### Added

- Blockquotes (`>`): single and nested levels, lazy continuation lines, white-space-only quote
  lines as paragraph separators, and support for headers, lists, code blocks and tables inside a
  quote. Text lines (including list item text) carry the bar prefix; quoted code lines carry a
  plain-text bar, while table rows and rules are only indented.
  New style properties: `blockquote_bar_character`, `blockquote_bar_color`,
  `blockquote_override_text_color`, `blockquote_text_color`, `blockquote_italic`,
  `blockquote_background_color`.
- GFM tables: delimiter rows (at least one `-` per cell; GFM's usual three or more also work),
  header rows (bold and optionally colored), column alignment (`:---`, `:---:`, `---:`),
  zebra-striped data rows (each row emits a single `bg=#RRGGBBAA`), cell borders and padding,
  escaped pipes (`\|`), inline formatting inside cells, and normalization of rows with too
  few/many cells. Legacy tables without a delimiter row are still supported.
  New style properties: `table_header_row`, `table_header_bold`, `table_header_background`,
  `table_border_color`, `table_cell_padding`, `table_row_background`,
  `table_alternate_row_background`, `table_default_alignment`.
- Image size/alignment extension `{width=... height=... align=...}` behind the new
  `image_allow_attributes` property.
- Automatic fit-to-label-width for oversized images (`image_fit_width`) and whole-line image
  alignment (`image_alignment`, applied to paragraphs containing a single image).
- Alt-text fallback for missing image files (`image_show_alt_when_missing`) and the `alt`
  attribute of `[img]` on Godot 4.6+.
- Reference-style links and images (`[text][id]`, `[text][]`, `[id]`, `![alt][id]`), including
  reference definitions with optional titles.
- Setext headings (a paragraph line underlined with `=` or `-`).
- Language-tagged fenced code blocks: info strings after the opening fence (e.g. a language
  label) are accepted and ignored instead of breaking the fence.
- Indented code blocks (4 spaces or 1 tab, outside lists and quotes).
- Automatic linking of bare URLs behind the new `autolink_bare_urls` property.
- Escaped square brackets now render as literal brackets.
- Internal block/inline parser split (`markdown_parser.gd` / `markdown_inline.gd`); the
  `_preprocess_line()` and `_process_custom_syntax()` extension points are kept.

### Changed

- Parsing is dirty-flag driven: the node re-converts only when it is marked dirty, instead of
  polling in `_process()` every frame. Width-dependent images re-convert on resize.
- Multiline code blocks are emitted as a single `[code]...[/code]` pair with embedded newlines
  (previously the opening and closing tags occupied their own output lines).
- Table delimiter rows are now honored for header styling and alignment instead of being silently
  discarded.
- A paragraph line followed by `===`/`---` now becomes a Setext heading; a `---` line with no
  paragraph above it remains a horizontal rule.
- Header anchors use GitHub-style slugs and are reset on every conversion.
- Horizontal rules and header rules use the label's `hr_*` properties; the rule color is emitted
  as `color=#RRGGBBAA` (leading `#`).
- Version bumped to `2.0.0` in `plugin.cfg`.

### Fixed

- Header anchors accumulated duplicates/misplaced entries after repeated updates.
- Table and paragraph line-break state could corrupt the output after a table ended.
- Image and link title parsing shifted the rendered text (off-by-title-length offsets).
- Task list checkbox clicks could write to the wrong source line.
- List parsing boundary cases: closing of nested lists, lists starting at a number other than 1,
  `)` markers, loose lists and continuation lines.
- Escaped characters could leak internal placeholders into the output.
- Regular expressions were recompiled inside loops; they are now compiled once and cached.
- An unclosed fenced code block now closes at the end of the document instead of swallowing
  subsequent syntax.
- Rows with missing/extra table cells are padded/dropped instead of corrupting the table.
- A delimiter row whose column count differs from the header row no longer leaks its `---` cells
  as a visible data row; the table follows the header row's column count and pads its alignment.
- A trailing escaped pipe (`\|`) at the end of a table row is no longer stripped, so the cell shows
  a literal `|` instead of a dangling backslash.
- Reference definitions with an unquoted trailing title (`[label]: url some words`) are no longer
  consumed as definitions; the line is rendered as ordinary text, matching the documented rules.
- Header anchor collisions: an explicit `#foo-1` heading can no longer overwrite a `#foo-1` anchor
  generated for a duplicated `#foo` heading; suffixes are resolved until unique (GFM-style).
- Anchor paragraph indices now match `RichTextLabel` paragraph counting: leading blank lines that
  are trimmed no longer shift anchors, and a whole table counts as one paragraph.
- Heading text is parsed only once when building the anchor slug (images inside headings no longer
  trigger duplicate conversion or duplicate missing-image warnings).
- Image paths containing square brackets are rejected with the alt-text fallback instead of
  emitting malformed `[img]` markup.

## [1.4.0] - 2023

Upstream baseline release by Daenvil (<https://github.com/daenvil/MarkdownLabel>). See the upstream
repository for its changelog.
