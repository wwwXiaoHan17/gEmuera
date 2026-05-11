using Godot;
using MinorShift.Emuera.GameProc;

public partial class Inputpad : Control
{
	LineEdit inputField;
	Button confirmBtn;
	Button repeatBtn;
	string lastInput;

	public override void _Ready()
	{
		Visible = false;
		CustomMinimumSize = new Vector2(0, 40);

		var hbox = new HBoxContainer();
		hbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		AddChild(hbox);

		inputField = new LineEdit();
		inputField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		inputField.TextSubmitted += OnTextSubmitted;
		hbox.AddChild(inputField);

		confirmBtn = new Button();
		confirmBtn.Text = MultiLanguage.Get("Inputpad.Confirm", "OK");
		EmueraContent.StyleButton(confirmBtn);
		confirmBtn.Pressed += OnConfirm;
		hbox.AddChild(confirmBtn);

		repeatBtn = new Button();
		repeatBtn.Text = MultiLanguage.Get("Inputpad.Repeat", "Repeat");
		EmueraContent.StyleButton(repeatBtn);
		repeatBtn.Pressed += OnRepeat;
		hbox.AddChild(repeatBtn);
	}

	internal void UpdateInputType(InputType type)
	{
		// Godot 4 LineEdit does not expose VirtualKeyboardType in this version.
		// Mobile keyboard type switching is skipped.
		var console = MinorShift.Emuera.GlobalStatic.Console;
		if (console != null && console.IsWaitingOnePhrase)
		{
			inputField.MaxLength = 1;
		}
		else
		{
			inputField.MaxLength = 0;
		}
	}

	void OnTextSubmitted(string text)
	{
		lastInput = text;
		EmueraThread.instance.Input(text, true);
		inputField.Text = "";
	}

	void OnConfirm()
	{
		if (inputField.Visible)
		{
			lastInput = inputField.Text;
			EmueraThread.instance.Input(lastInput, true);
		}
		else
		{
			EmueraThread.instance.Input("", true);
		}
		inputField.Text = "";
	}

	void OnRepeat()
	{
		if (inputField.Visible)
		{
			inputField.Text = lastInput ?? "";
		}
		else if (lastInput != null)
		{
			EmueraThread.instance.Input(lastInput, true);
		}
	}

	public void ShowPad()
	{
		Visible = true;
		inputField.Text = "";
		lastInput = null;
		inputField.GrabFocus();
	}

	public void HidePad()
	{
		Visible = false;
		inputField.Text = "";
		lastInput = null;
	}

	public bool IsShow => Visible;

	public bool HasInputFocus()
	{
		return inputField != null && inputField.HasFocus();
	}

	public void ApplyFont(FontFile font, int fontSize)
	{
		if (font != null)
		{
			inputField.AddThemeFontOverride("font", font);
			confirmBtn.AddThemeFontOverride("font", font);
			repeatBtn.AddThemeFontOverride("font", font);
		}
		inputField.AddThemeFontSizeOverride("font_size", fontSize);
		confirmBtn.AddThemeFontSizeOverride("font_size", fontSize);
		repeatBtn.AddThemeFontSizeOverride("font_size", fontSize);
	}
}
