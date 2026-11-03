# MarkdownLabel 2.0.0 example

This file is rendered by **example.tscn** through `MarkdownLabel.display_file("res://addons/markdownlabel/example.md")`.
It exercises the syntax that this gEmuera fork supports on top of the original MarkdownLabel 1.4.0.

## Headings

### ATX heading (level 3)

#### A heading with **bold** and *italic* text

Setext level 1
==============

Setext level 2
--------------

## Inline formatting

**Bold**, *italic*, ***bold italic***, ~~struck through~~, `inline code`, and a regular [inline link](https://godotengine.org "Godot Engine").

Underscores inside snake_case_words are not italicized.

## Blockquotes

> A single-level quote.
> It spans two source lines thanks to lazy continuation.
>
> > A nested quote with **inline markup** and a [link](https://godotengine.org).
>
> - A list inside the quote
> - [x] A checked task inside the quote

## Lists

- First unordered item
- Second unordered item
  - Nested item
	- Deeper item
- Third unordered item
  Continuation of the third item.

1. First ordered item
2. Second ordered item
  1. Nested ordered item
3. Third ordered item

### Task lists

- [ ] Unchecked task (click it!)
- [x] Checked task
  - [x] Nested checked task
- [ ] Another unchecked task

## Tables

| Element       | Example        | Alignment      |
| :------------ | :------------: | -------------: |
| Inline bold   | **bold text**  | left           |
| Inline code   | `var a := 1`   | center         |
| Escaped pipe  | a \| b         | right          |
| Internal link | [Headings](#headings) | left    |

| Legacy table | without delimiter row |
| second row   | no header styling     |

## Code

Inline code: `var label := MarkdownLabel.new()`.

A fenced code block with a language label:

```gdscript
func _ready() -> void:
	var label: MarkdownLabel = %MarkdownLabelFile
	label.display_file("res://addons/markdownlabel/example.md")
```

Tilde fences work too, and BBCode inside them is shown literally:

```
[b]this is not bold[/b]
```

	An indented code block
	(4 spaces, outside lists and quotes)

## Links

- Inline link: [Godot's BBCode reference](https://docs.godotengine.org/en/stable/tutorials/ui/bbcode_in_richtextlabel.html)
- Tooltip link: [MarkdownLabel upstream](https://github.com/daenvil/MarkdownLabel "Original project by Daenvil")
- Reference-style link: [the Godot docs][godot], and a reference shortcut: [godot]
- Autolink: <https://godotengine.org>
- Bare URL: https://github.com/wwwXiaoHan17/gEmuera
- Anchor link: [jump to the Tables section](#tables)

[godot]: https://docs.godotengine.org "Godot documentation"

## Images

The screenshot below is fitted to the label width; the explicit size extension is demonstrated as well:

![MarkdownLabel screenshot](res://addons/markdownlabel/assets/screenshot.png "MarkdownLabel rendering a Markdown file in Godot"){width=100%}

The next image path is intentionally missing from this example, to demonstrate the alt-text fallback: the label shows the alt text in italics and logs the expected `MarkdownLabel: image not found` warning. ![an image that does not exist](res://addons/markdownlabel/assets/missing.png)

## Horizontal rule

---

## Escaping

Literal characters: \*not italic\*, \`not code\`, \> not a quote, 1\. not a list.
