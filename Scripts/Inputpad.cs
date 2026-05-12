using Godot;
using MinorShift.Emuera.GameProc;

public partial class Inputpad : Control
{
	PanelContainer panel;
	HBoxContainer hbox;
	LineEdit inputField;
	Button confirmBtn;
	Button repeatBtn;
	string lastInput;
	const int PanelHeight = 58;
	const int SideMargin = 10;
	const int BottomMargin = 12;

	public override void _Ready()
	{
		Visible = false;
		ZIndex = 96;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;

		panel = new PanelContainer();
		panel.SetAnchorsPreset(LayoutPreset.TopLeft);
		panel.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(panel);

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.03f, 0.03f, 0.035f, 0.88f);
		style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 6;
		style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
		style.ContentMarginLeft = style.ContentMarginRight = 8;
		style.ContentMarginTop = style.ContentMarginBottom = 7;
		panel.AddThemeStyleboxOverride("panel", style);

		hbox = new HBoxContainer();
		hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		hbox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(hbox);

		inputField = new LineEdit();
		inputField.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		inputField.CustomMinimumSize = new Vector2(0, 44);
		inputField.TextSubmitted += OnTextSubmitted;
		hbox.AddChild(inputField);

		confirmBtn = new Button();
		confirmBtn.Text = MultiLanguage.Get("Inputpad.Confirm", "OK");
		confirmBtn.CustomMinimumSize = new Vector2(64, 44);
		EmueraContent.StyleButton(confirmBtn);
		confirmBtn.Pressed += OnConfirm;
		hbox.AddChild(confirmBtn);

		repeatBtn = new Button();
		repeatBtn.Text = MultiLanguage.Get("Inputpad.Repeat", "Repeat");
		repeatBtn.CustomMinimumSize = new Vector2(82, 44);
		EmueraContent.StyleButton(repeatBtn);
		repeatBtn.Pressed += OnRepeat;
		hbox.AddChild(repeatBtn);

		ApplyPanelLayout();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized)
			ApplyPanelLayout();
	}

	void ApplyPanelLayout()
	{
		if (panel == null)
			return;

		var viewportSize = GetViewport().GetVisibleRect().Size;
		Position = Vector2.Zero;
		Size = viewportSize;
		var width = Mathf.Max(1, viewportSize.X - SideMargin * 2);
		panel.Position = new Vector2(SideMargin, Mathf.Max(0, viewportSize.Y - BottomMargin - PanelHeight));
		panel.Size = new Vector2(width, PanelHeight);
		panel.CustomMinimumSize = panel.Size;
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
		if (string.IsNullOrEmpty(lastInput))
			return;

		if (inputField.Visible)
		{
			inputField.Text = lastInput;
			inputField.CaretColumn = inputField.Text.Length;
		}
		else
		{
			EmueraThread.instance.Input(lastInput, true);
		}
	}

	public void ShowPad()
	{
		ApplyPanelLayout();
		Visible = true;
		GetParent()?.MoveChild(this, GetParent().GetChildCount() - 1);
		inputField.Text = "";
	}

	public void HidePad()
	{
		Visible = false;
		inputField.Text = "";
	}

	public bool IsShow => Visible;

	public bool HasInputFocus()
	{
		return inputField != null && inputField.HasFocus();
	}

	public void ApplyFont(FontFile font, int fontSize)
	{
		if (inputField == null || confirmBtn == null || repeatBtn == null)
			return;
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
