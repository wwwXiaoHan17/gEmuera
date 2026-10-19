using Godot;
using MinorShift.Emuera.GameProc;

// Mobile-first input overlay for emuera prompts. It follows virtual keyboard
// geometry instead of relying on desktop-style fixed placement.
public partial class Inputpad : Control
{
	PanelContainer panel;
	HBoxContainer hbox;
	LineEdit inputField;
	Button confirmBtn;
	Button repeatBtn;
	string lastInput;
	int lastKeyboardHeight = -1;
	// IME 高度查询节流：可见期间不必每帧调用 DisplayServer 原生查询，
	// 累积到 ~100ms 再查一次即可；布局只在数值实际变化时应用。
	const float ImePollIntervalSeconds = 0.1f;
	float imePollAccumulator;
	// Component interface: panels advertise their own show/hide state changes so
	// host scenes can react without polling. Emitted purely additively; callers
	// that never connect are unaffected.
	[Signal]
	public delegate void PadShownEventHandler();

	[Signal]
	public delegate void PadHiddenEventHandler();

	// Controls are sized for touch operation in exported APKs. Keep these values
	// coordinated with EmueraContent system-button minimums when changing UI scale.
	// Exported so the layout metrics are reusable/tunable per scene instance.
	[Export]
	public int PanelHeight = 64;
	[Export]
	public int SideMargin = 10;
	[Export]
	public int BottomMargin = 12;

	public override void _Ready()
	{
		Visible = false;
		ZIndex = 96;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;
		SetProcess(false);

		panel = new PanelContainer();
		panel.SetAnchorsPreset(LayoutPreset.TopLeft);
		panel.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(panel);

		var style = GEmueraTheme.SurfaceStyle(
			GEmueraTheme.WithAlpha(GEmueraTheme.SurfaceRaised, 0.94f),
			GEmueraTheme.Border,
			GEmueraTheme.CardRadius,
			1,
			8,
			new Vector2(0, 4),
			8, 8, 7, 7);
		panel.AddThemeStyleboxOverride("panel", style);

		hbox = new HBoxContainer();
		hbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		hbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		hbox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(hbox);

		inputField = new LineEdit();
		inputField.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		inputField.CustomMinimumSize = new Vector2(0, 48);
		inputField.TextSubmitted += OnTextSubmitted;
		hbox.AddChild(inputField);

		confirmBtn = new Button();
		confirmBtn.Text = MultiLanguage.Get("Inputpad.Confirm", "OK");
		confirmBtn.CustomMinimumSize = new Vector2(64, 48);
		EmueraContent.StyleButton(confirmBtn);
		confirmBtn.Pressed += OnConfirm;
		hbox.AddChild(confirmBtn);

		repeatBtn = new Button();
		repeatBtn.Text = MultiLanguage.Get("Inputpad.Repeat", "Repeat");
		repeatBtn.CustomMinimumSize = new Vector2(82, 48);
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

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		// Android reports virtual keyboard height asynchronously after focus. Poll
		// only while visible so the panel tracks IME animation without adding idle
		// work during normal console rendering. ~100ms 节流：数值变化才应用布局，
		// 布局更新时机与未节流时一致（仅查询频率降低）。
		imePollAccumulator += (float)delta;
		if (imePollAccumulator < ImePollIntervalSeconds)
			return;
		imePollAccumulator = 0f;
		int keyboardHeight = GetVirtualKeyboardHeight();
		if (keyboardHeight != lastKeyboardHeight)
			ApplyPanelLayout();
	}

	bool isApplyingLayout;
	void ApplyPanelLayout()
	{
		// 输入栏是交互控件，必须落在系统安全区内；正文坐标仍由 EmueraContent
		// 统一映射，避免前摄/挖孔遮住确认按钮或输入框。
		if (panel == null)
			return;
		// 防重入：下面 set_Size 会触发 NotificationResized → _Notification → 本方法，
		// 不加守卫会在 _Ready 首帧直接无限递归（godot-master: NEVER set size in a
		// resize notification without a re-entrancy guard）。
		if (isApplyingLayout)
			return;
		isApplyingLayout = true;
		try
		{
			var safeRect = EmueraContent.GetSafeViewportRect(GetViewport());
			var viewportSize = safeRect.Size;
			int keyboardHeight = GetVirtualKeyboardHeight();
			lastKeyboardHeight = keyboardHeight;
			Position = safeRect.Position;
			Size = viewportSize;
			float left = SideMargin;
			float right = SideMargin;
			float bottomInset = BottomMargin + keyboardHeight;
			var width = Mathf.Max(1, viewportSize.X - left - right);
			panel.Position = new Vector2(left, Mathf.Max(0, viewportSize.Y - bottomInset - PanelHeight));
			panel.Size = new Vector2(width, PanelHeight);
			panel.CustomMinimumSize = panel.Size;
		}
		finally
		{
			isApplyingLayout = false;
		}
	}

	static int GetVirtualKeyboardHeight()
	{
		// Desktop/editor returns zero to preserve the existing testing workflow.
		// Android values can briefly be negative or stale, so clamp at the boundary.
		if (!OS.HasFeature("mobile"))
			return 0;
		return System.Math.Max(0, DisplayServer.VirtualKeyboardGetHeight());
	}

	// Re-read cached MultiLanguage texts after a language change. Idempotent.
	public void RefreshUiTexts()
	{
		if (confirmBtn != null)
			confirmBtn.Text = MultiLanguage.Get("Inputpad.Confirm", "OK");
		if (repeatBtn != null)
			repeatBtn.Text = MultiLanguage.Get("Inputpad.Repeat", "Repeat");
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
		SetProcess(true);
		GetParent()?.MoveChild(this, GetParent().GetChildCount() - 1);
		inputField.Text = "";
		inputField.GrabFocus();
		EmitSignal(SignalName.PadShown);
	}

	public void HidePad()
	{
		Visible = false;
		SetProcess(false);
		lastKeyboardHeight = -1;
		inputField.Text = "";
		inputField.ReleaseFocus();
		EmitSignal(SignalName.PadHidden);
	}

	public bool IsShow => Visible;

	public void RefreshSafeAreaLayout()
	{
		ApplyPanelLayout();
	}

	public bool HasInputFocus()
	{
		return inputField != null && inputField.HasFocus();
	}

	public void ApplyFont(Font font, int fontSize)
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
