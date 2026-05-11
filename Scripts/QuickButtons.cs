using Godot;
using System.Collections.Generic;

public partial class QuickButtons : Control
{
	VBoxContainer rowsContainer;
	HBoxContainer currentRow;
	List<Button> buttons = new List<Button>();
	FontFile fontFile;
	int fontSize;

	public override void _Ready()
	{
		Visible = false;

		var scroll = new ScrollContainer();
		scroll.AnchorLeft = 0;
		scroll.AnchorTop = 0;
		scroll.AnchorRight = 1;
		scroll.AnchorBottom = 1;
		scroll.CustomMinimumSize = new Vector2(0, 120);
		AddChild(scroll);

		rowsContainer = new VBoxContainer();
		rowsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.AddChild(rowsContainer);

		currentRow = new HBoxContainer();
		rowsContainer.AddChild(currentRow);
	}

	public void Clear()
	{
		foreach (var btn in buttons)
			btn.QueueFree();
		buttons.Clear();
		foreach (var child in rowsContainer.GetChildren())
			child.QueueFree();
		currentRow = new HBoxContainer();
		rowsContainer.AddChild(currentRow);
	}

	public void AddButton(string text, Godot.Color color, string code)
	{
		var btn = new Button();
		btn.Text = text;
		EmueraContent.StyleButton(btn);
		btn.AddThemeColorOverride("font_color", color);
		if (fontFile != null)
			btn.AddThemeFontOverride("font", fontFile);
		if (fontSize > 0)
			btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.CustomMinimumSize = new Vector2(40, fontSize > 0 ? fontSize + 8 : 32);
		string inputCode = code;
		btn.Pressed += () => EmueraThread.instance.Input(inputCode, true);
		currentRow.AddChild(btn);
		buttons.Add(btn);
	}

	public void ShiftLine()
	{
		currentRow = new HBoxContainer();
		rowsContainer.AddChild(currentRow);
	}

	public void ShowPad()
	{
		Visible = true;
	}

	public void HidePad()
	{
		Visible = false;
	}

	public bool IsShow => Visible;

	public void ApplyFont(FontFile font, int fontSize)
	{
		fontFile = font;
		this.fontSize = fontSize;
		if (font != null)
		{
			foreach (var btn in buttons)
				btn.AddThemeFontOverride("font", font);
		}
		foreach (var btn in buttons)
			btn.AddThemeFontSizeOverride("font_size", fontSize);
	}
}
