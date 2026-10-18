using Godot;
using System.Collections.Generic;

public partial class OptionWindow : Control
{
	PopupPanel popup;
	Slider fontSizeSlider;
	Label fontSizeLabel;
	Slider quickButtonWidthSlider;
	Label quickButtonWidthLabel;
	Slider quickFontSizeSlider;
	Label quickFontSizeLabel;
	Slider buttonDragSensitivitySlider;
	Label buttonDragSensitivityLabel;
	Slider virtualCursorSensitivitySlider;
	Label virtualCursorSensitivityValue;
	Slider virtualMouseScaleSlider;
	Label virtualMouseScaleValue;
	Slider virtualMouseOpacitySlider;
	Label virtualMouseOpacityValue;
	CheckButton pinchZoomToggle;
	CheckButton quickFlipToggle;
	CheckButton quickFloatingToggle;
	CheckButton vsyncToggle;
	Slider maxVisibleLinesSlider;
	Label maxVisibleLinesLabel;
	// 游戏界面左右额外留白（仅移动端生效）：全面屏/曲面屏安全区显示异常时手动修正。
	Slider gameViewLeftPaddingSlider;
	Label gameViewLeftPaddingValue;
	Slider gameViewRightPaddingSlider;
	Label gameViewRightPaddingValue;
	OptionButton resolutionOption;
	OptionButton languageOption;
	OptionButton frameRateOption;

	// MultiLanguage-driven description labels (values are slider-derived numbers
	// and stay untouched); refreshed together when the language changes.
	Label fontLabel;
	Label quickWidthLabel;
	Label quickFontLabel;
	Label sensitivityLabel;
	Label virtualCursorSensitivityLabel;
	Label virtualMouseScaleLabel;
	Label virtualMouseOpacityLabel;
	Label pinchZoomLabel;
	Label quickFlipLabel;
	Label quickFloatingLabel;
	Label maxLinesTextLabel;
	Label gameViewLeftPaddingLabel;
	Label gameViewRightPaddingLabel;
	Label gameViewPaddingHintLabel;
	Label resLabel;
	Label fpsLabel;
	Label vsyncLabel;
	Label vsyncHintLabel;
	Label langLabel;
	Button closeBtn;

	// Group section titles (Display / Quick / Input / System), also
	// MultiLanguage-driven and refreshed by ApplyLanguageTexts.
	Label displayGroupTitle;
	Label quickGroupTitle;
	Label inputGroupTitle;
	Label systemGroupTitle;

	static readonly string[] languages = new string[] { "default", "zh_cn", "en_us", "jp" };
	static readonly string[] languageNames = new string[] { "Default", "简体中文", "English", "日本語" };

	// Component interface: the popup advertises its open/close state so host
	// scenes can react without polling. Emitted purely additively; callers that
	// never connect are unaffected.
	[Signal]
	public delegate void PopupOpenedEventHandler();

	[Signal]
	public delegate void PopupClosedEventHandler();

	// Exported so the dialog size is reusable/tunable per scene instance.
	[Export]
	public Vector2I PopupSize = new Vector2I(520, 640);

	public override void _Ready()
	{
		popup = new PopupPanel();
		popup.Size = PopupSize;
		popup.PopupHide += () => EmitSignal(SignalName.PopupClosed);
		GEmueraTheme.ApplyPopup(popup);
		AddChild(popup);

		// Inner padding. PopupPanel's "panel" stylebox is drawn visually only —
		// child layout starts at the window origin in Godot 4 — so the content
		// inset comes from an explicit MarginContainer (16px, deep-modern spec).
		var margin = new MarginContainer();
		margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		margin.SizeFlagsVertical = SizeFlags.ExpandFill;
		SetMargins(margin, 16);
		popup.AddChild(margin);

		var rootVBox = new VBoxContainer();
		rootVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rootVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		rootVBox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(rootVBox);

		// Scrollable settings body: the grouped rows scroll on small screens /
		// large fonts, while the Close footer stays pinned at the bottom.
		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		rootVBox.AddChild(scroll);

		var content = new VBoxContainer();
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.AddThemeConstantOverride("separation", 16); // 组间距
		scroll.AddChild(content);

		// ---- Display: 字体大小 / 最大行数 / 分辨率 / 帧率 ----
		var displayGroup = CreateGroup(content, out displayGroupTitle);

		var fontRow = CreateRow(displayGroup, out fontLabel);
		fontSizeSlider = new HSlider();
		fontSizeSlider.MinValue = 8;
		fontSizeSlider.MaxValue = 48;
		fontSizeSlider.Value = MinorShift.Emuera.Config.FontSize > 0 ? MinorShift.Emuera.Config.FontSize : 18;
		fontSizeSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		fontSizeSlider.CustomMinimumSize = new Vector2(120, 44);
		fontSizeSlider.Editable = false;
		fontSizeSlider.ValueChanged += OnFontSizeChanged;
		fontRow.AddChild(fontSizeSlider);
		fontSizeLabel = CreateValueLabel();
		fontSizeLabel.Text = fontSizeSlider.Value.ToString();
		fontRow.AddChild(fontSizeLabel);

		var maxLinesRow = CreateRow(displayGroup, out maxLinesTextLabel);
		maxVisibleLinesSlider = new HSlider();
		maxVisibleLinesSlider.MinValue = EmueraContent.MinMaxVisibleLines;
		maxVisibleLinesSlider.MaxValue = EmueraContent.MaxMaxVisibleLines;
		maxVisibleLinesSlider.Step = 20;
		maxVisibleLinesSlider.Value = EmueraContent.ConfiguredMaxVisibleLines;
		maxVisibleLinesSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		maxVisibleLinesSlider.CustomMinimumSize = new Vector2(120, 44);
		maxVisibleLinesSlider.ValueChanged += OnMaxVisibleLinesChanged;
		maxLinesRow.AddChild(maxVisibleLinesSlider);
		maxVisibleLinesLabel = CreateValueLabel();
		maxVisibleLinesLabel.Text = ((int)maxVisibleLinesSlider.Value).ToString();
		maxLinesRow.AddChild(maxVisibleLinesLabel);

		// 全面屏/曲面屏安全区左右留白微调（仅移动端生效；桌面端禁用并显示说明）。
		// 背景：部分设备 DisplayServer 报告的左侧安全区在视觉上不可用/被曲面裁切，
		// 导致内容左侧留白异常；允许用户以像素为单位微调，立即重排游戏内容区。
		bool paddingAdjustAvailable = OS.HasFeature("mobile");
		string paddingHint = MultiLanguage.Get(
			"OptionWindow.GameViewPaddingHint",
			"仅移动端生效：正值增加留白、负值减少留白，用于修正全面屏/曲面屏左右安全区显示异常。");

		var leftPaddingRow = CreateRow(displayGroup, out gameViewLeftPaddingLabel);
		gameViewLeftPaddingSlider = new HSlider();
		gameViewLeftPaddingSlider.MinValue = EmueraContent.MinGameViewHorizontalPadding;
		gameViewLeftPaddingSlider.MaxValue = EmueraContent.MaxGameViewHorizontalPadding;
		gameViewLeftPaddingSlider.Step = 2;
		gameViewLeftPaddingSlider.Value = EmueraContent.GameViewLeftPadding;
		gameViewLeftPaddingSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		gameViewLeftPaddingSlider.CustomMinimumSize = new Vector2(120, 44);
		gameViewLeftPaddingSlider.Editable = paddingAdjustAvailable;
		gameViewLeftPaddingSlider.TooltipText = paddingHint;
		gameViewLeftPaddingSlider.ValueChanged += OnGameViewLeftPaddingChanged;
		leftPaddingRow.AddChild(gameViewLeftPaddingSlider);
		gameViewLeftPaddingValue = CreateValueLabel();
		gameViewLeftPaddingValue.Text = FormatGameViewPadding(EmueraContent.GameViewLeftPadding);
		gameViewLeftPaddingValue.TooltipText = paddingHint;
		leftPaddingRow.AddChild(gameViewLeftPaddingValue);
		gameViewLeftPaddingLabel.TooltipText = paddingHint;

		var rightPaddingRow = CreateRow(displayGroup, out gameViewRightPaddingLabel);
		gameViewRightPaddingSlider = new HSlider();
		gameViewRightPaddingSlider.MinValue = EmueraContent.MinGameViewHorizontalPadding;
		gameViewRightPaddingSlider.MaxValue = EmueraContent.MaxGameViewHorizontalPadding;
		gameViewRightPaddingSlider.Step = 2;
		gameViewRightPaddingSlider.Value = EmueraContent.GameViewRightPadding;
		gameViewRightPaddingSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		gameViewRightPaddingSlider.CustomMinimumSize = new Vector2(120, 44);
		gameViewRightPaddingSlider.Editable = paddingAdjustAvailable;
		gameViewRightPaddingSlider.TooltipText = paddingHint;
		gameViewRightPaddingSlider.ValueChanged += OnGameViewRightPaddingChanged;
		rightPaddingRow.AddChild(gameViewRightPaddingSlider);
		gameViewRightPaddingValue = CreateValueLabel();
		gameViewRightPaddingValue.Text = FormatGameViewPadding(EmueraContent.GameViewRightPadding);
		gameViewRightPaddingValue.TooltipText = paddingHint;
		rightPaddingRow.AddChild(gameViewRightPaddingValue);
		gameViewRightPaddingLabel.TooltipText = paddingHint;

		gameViewPaddingHintLabel = new Label();
		gameViewPaddingHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		gameViewPaddingHintLabel.AddThemeFontSizeOverride("font_size", 12);
		gameViewPaddingHintLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextDim);
		gameViewPaddingHintLabel.Text = paddingHint;
		displayGroup.AddChild(gameViewPaddingHintLabel);

		var resRow = CreateRow(displayGroup, out resLabel);
		resolutionOption = new OptionButton();
		ResolutionHelper.RefreshResolutions();
		for (int i = 0; i < ResolutionHelper.resolutions.Count; i++)
		{
			resolutionOption.AddItem(ResolutionHelper.resolutions[i] + "p", i);
		}
		if (ResolutionHelper.resolution_index >= 0 && ResolutionHelper.resolution_index < ResolutionHelper.resolutions.Count)
			resolutionOption.Select(ResolutionHelper.resolution_index);
		resolutionOption.ItemSelected += OnResolutionSelected;
		resolutionOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		resRow.AddChild(resolutionOption);

		var fpsRow = CreateRow(displayGroup, out fpsLabel);
		frameRateOption = new OptionButton();
		for (int i = 0; i < FrameRateHelper.FrameRates.Count; i++)
		{
			frameRateOption.AddItem(FrameRateHelper.FrameRates[i] + " FPS", i);
		}
		frameRateOption.Select(FrameRateHelper.frame_rate_index);
		frameRateOption.ItemSelected += OnFrameRateSelected;
		frameRateOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		fpsRow.AddChild(frameRateOption);

		// 垂直同步：Godot 仅 Forward+/Mobile 渲染方法支持关闭；Compatibility 下关闭
		// 会被回退为 Enabled。提示行按实际条件显示，不承诺关闭一定生效。
		var vsyncRow = CreateRow(displayGroup, out vsyncLabel);
		vsyncToggle = new CheckButton();
		vsyncToggle.ButtonPressed = VSyncHelper.Enabled;
		vsyncToggle.CustomMinimumSize = new Vector2(120, 44);
		vsyncToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vsyncToggle.Toggled += OnVSyncToggled;
		vsyncRow.AddChild(vsyncToggle);

		vsyncHintLabel = new Label();
		vsyncHintLabel.AddThemeFontSizeOverride("font_size", 12);
		vsyncHintLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		vsyncHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		displayGroup.AddChild(vsyncHintLabel);
		// 初始文案与可见性（ApplyLanguageTexts 在 _Ready 末尾还会再刷新一次，幂等）。
		RefreshVSyncHint();

		// ---- Quick: 快捷按钮宽度 / 快捷字体 ----
		var quickGroup = CreateGroup(content, out quickGroupTitle);

		var quickWidthRow = CreateRow(quickGroup, out quickWidthLabel);
		quickButtonWidthSlider = new HSlider();
		quickButtonWidthSlider.MinValue = QuickButtons.MinQuickButtonWidth;
		quickButtonWidthSlider.MaxValue = QuickButtons.MaxQuickButtonWidth;
		quickButtonWidthSlider.Step = 1;
		quickButtonWidthSlider.Value = QuickButtons.ConfiguredButtonWidth;
		quickButtonWidthSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickButtonWidthSlider.CustomMinimumSize = new Vector2(120, 44);
		quickButtonWidthSlider.ValueChanged += OnQuickButtonWidthChanged;
		quickWidthRow.AddChild(quickButtonWidthSlider);
		quickButtonWidthLabel = CreateValueLabel();
		quickButtonWidthLabel.Text = quickButtonWidthSlider.Value.ToString();
		quickWidthRow.AddChild(quickButtonWidthLabel);

		var quickFontRow = CreateRow(quickGroup, out quickFontLabel);
		quickFontSizeSlider = new HSlider();
		quickFontSizeSlider.MinValue = QuickButtons.MinQuickButtonFontSize;
		quickFontSizeSlider.MaxValue = QuickButtons.MaxQuickButtonFontSize;
		quickFontSizeSlider.Step = 1;
		quickFontSizeSlider.Value = QuickButtons.ConfiguredFontSize;
		quickFontSizeSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickFontSizeSlider.CustomMinimumSize = new Vector2(120, 44);
		quickFontSizeSlider.ValueChanged += OnQuickFontSizeChanged;
		quickFontRow.AddChild(quickFontSizeSlider);
		quickFontSizeLabel = CreateValueLabel();
		quickFontSizeLabel.Text = quickFontSizeSlider.Value.ToString();
		quickFontRow.AddChild(quickFontSizeLabel);

		// 面板翻转：默认靠右，翻转后靠左（宽度调节条同步翻到内侧）。即时生效。
		var quickFlipRow = CreateRow(quickGroup, out quickFlipLabel);
		quickFlipToggle = new CheckButton();
		quickFlipToggle.ButtonPressed = QuickButtons.FlipEnabled;
		quickFlipToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickFlipToggle.CustomMinimumSize = new Vector2(120, 44);
		quickFlipToggle.TooltipText = MultiLanguage.Get("OptionWindow.QuickFlipHint", "Move the quick button panel from the right edge to the left edge; the width resize strip flips too.");
		quickFlipToggle.Toggled += OnQuickFlipToggled;
		quickFlipRow.AddChild(quickFlipToggle);

		// 面板悬浮：桌面端用 Godot Window 组件承载（原生标题栏拖动/关闭）；
		// Android 上嵌入 Window 在 gl_compatibility 下不渲染，故禁用。
		var quickFloatingRow = CreateRow(quickGroup, out quickFloatingLabel);
		quickFloatingToggle = new CheckButton();
		quickFloatingToggle.ButtonPressed = QuickButtons.FloatingEnabled;
		quickFloatingToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickFloatingToggle.CustomMinimumSize = new Vector2(120, 44);
		quickFloatingToggle.TooltipText = MultiLanguage.Get("OptionWindow.QuickFloatingHint", "Show the quick button panel as a floating window (desktop only; Android does not render embedded windows).");
		quickFloatingToggle.Toggled += OnQuickFloatingToggled;
		quickFloatingToggle.Disabled = OS.HasFeature("mobile");
		quickFloatingRow.AddChild(quickFloatingToggle);

		// ---- Input: 滚动灵敏度 / 双指缩放 ----
		var inputGroup = CreateGroup(content, out inputGroupTitle);

		var sensitivityRow = CreateRow(inputGroup, out sensitivityLabel);
		buttonDragSensitivitySlider = new HSlider();
		buttonDragSensitivitySlider.MinValue = 0.5;
		buttonDragSensitivitySlider.MaxValue = 2.0;
		buttonDragSensitivitySlider.Step = 0.05;
		buttonDragSensitivitySlider.Value = EmueraContent.ContentDragSensitivity;
		buttonDragSensitivitySlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		buttonDragSensitivitySlider.CustomMinimumSize = new Vector2(120, 44);
		buttonDragSensitivitySlider.ValueChanged += OnButtonDragSensitivityChanged;
		sensitivityRow.AddChild(buttonDragSensitivitySlider);
		buttonDragSensitivityLabel = CreateValueLabel();
		buttonDragSensitivityLabel.Text = buttonDragSensitivitySlider.Value.ToString("0.00") + "x";
		sensitivityRow.AddChild(buttonDragSensitivityLabel);

		var vcSensitivityRow = CreateRow(inputGroup, out virtualCursorSensitivityLabel);
		virtualCursorSensitivitySlider = new HSlider();
		virtualCursorSensitivitySlider.MinValue = VirtualMouse.MinCursorSensitivity;
		virtualCursorSensitivitySlider.MaxValue = VirtualMouse.MaxCursorSensitivity;
		virtualCursorSensitivitySlider.Step = 0.05;
		virtualCursorSensitivitySlider.Value = VirtualMouse.ConfiguredSensitivity;
		virtualCursorSensitivitySlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		virtualCursorSensitivitySlider.CustomMinimumSize = new Vector2(120, 44);
		virtualCursorSensitivitySlider.ValueChanged += OnVirtualCursorSensitivityChanged;
		vcSensitivityRow.AddChild(virtualCursorSensitivitySlider);
		virtualCursorSensitivityValue = CreateValueLabel();
		virtualCursorSensitivityValue.Text = VirtualMouse.ConfiguredSensitivity.ToString("0.00") + "x";
		vcSensitivityRow.AddChild(virtualCursorSensitivityValue);

		// 虚拟鼠标缩放大小（需求：默认 0.167，范围 0.1~0.2）。应用于 main.tscn 的
		// VirtualMouse（Node2D 根缩放会连带缩放子 Sprite 与子 Area2D 碰撞区域，区域局部变换不变）。
		var vmScaleRow = CreateRow(inputGroup, out virtualMouseScaleLabel);
		virtualMouseScaleSlider = new HSlider();
		virtualMouseScaleSlider.MinValue = VirtualMouse.MinScale;
		virtualMouseScaleSlider.MaxValue = VirtualMouse.MaxScale;
		virtualMouseScaleSlider.Step = 0.001;
		virtualMouseScaleSlider.Value = VirtualMouse.ConfiguredScale;
		virtualMouseScaleSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		virtualMouseScaleSlider.CustomMinimumSize = new Vector2(120, 44);
		virtualMouseScaleSlider.ValueChanged += OnVirtualMouseScaleChanged;
		vmScaleRow.AddChild(virtualMouseScaleSlider);
		virtualMouseScaleValue = CreateValueLabel();
		virtualMouseScaleValue.Text = VirtualMouse.ConfiguredScale.ToString("0.000");
		vmScaleRow.AddChild(virtualMouseScaleValue);

		// 虚拟鼠标机身透明度（需求：0.4~1.0，默认 1.0）。只改 Sprite Modulate.A，命中区域不变。
		var vmOpacityRow = CreateRow(inputGroup, out virtualMouseOpacityLabel);
		virtualMouseOpacitySlider = new HSlider();
		virtualMouseOpacitySlider.MinValue = VirtualMouse.MinOpacity;
		virtualMouseOpacitySlider.MaxValue = VirtualMouse.MaxOpacity;
		virtualMouseOpacitySlider.Step = 0.01;
		virtualMouseOpacitySlider.Value = VirtualMouse.ConfiguredOpacity;
		virtualMouseOpacitySlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		virtualMouseOpacitySlider.CustomMinimumSize = new Vector2(120, 44);
		virtualMouseOpacitySlider.ValueChanged += OnVirtualMouseOpacityChanged;
		vmOpacityRow.AddChild(virtualMouseOpacitySlider);
		virtualMouseOpacityValue = CreateValueLabel();
		virtualMouseOpacityValue.Text = VirtualMouse.ConfiguredOpacity.ToString("0.00");
		vmOpacityRow.AddChild(virtualMouseOpacityValue);

		var pinchZoomRow = CreateRow(inputGroup, out pinchZoomLabel);
		pinchZoomToggle = new CheckButton();
		pinchZoomToggle.ButtonPressed = EmueraContent.ContentPinchZoomEnabled;
		pinchZoomToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		pinchZoomToggle.CustomMinimumSize = new Vector2(120, 44);
		pinchZoomToggle.Toggled += OnPinchZoomToggled;
		pinchZoomRow.AddChild(pinchZoomToggle);

		// ---- System: 语言 ----
		var systemGroup = CreateGroup(content, out systemGroupTitle);

		var langRow = CreateRow(systemGroup, out langLabel);
		languageOption = new OptionButton();
		for (int i = 0; i < languages.Length; i++)
		{
			languageOption.AddItem(languageNames[i], i);
		}
		languageOption.Select(GetLanguageIndex(MultiLanguage.CurrentLanguage));
		languageOption.ItemSelected += OnLanguageSelected;
		languageOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		langRow.AddChild(languageOption);

		// Close button: pinned footer outside the scroll area.
		closeBtn = new Button();
		EmueraContent.StyleButton(closeBtn);
		closeBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		closeBtn.Pressed += () => popup.Hide();
		rootVBox.AddChild(closeBtn);

		ApplyLanguageTexts();
	}

	/// <summary>分组容器：次级文字色标题 + 1px 分隔线，组内行间距 8。</summary>
	VBoxContainer CreateGroup(VBoxContainer parent, out Label titleLabel)
	{
		var group = new VBoxContainer();
		group.AddThemeConstantOverride("separation", 8);
		parent.AddChild(group);

		titleLabel = new Label();
		titleLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		titleLabel.AddThemeFontSizeOverride("font_size", 15);
		group.AddChild(titleLabel);

		var sep = new HSeparator();
		sep.AddThemeStyleboxOverride("separator", SeparatorStyle());
		group.AddChild(sep);

		return group;
	}

	/// <summary>组内行：描述 label 固定宽度对齐（160px），控件由调用方 ExpandFill。</summary>
	HBoxContainer CreateRow(VBoxContainer group, out Label descLabel)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		group.AddChild(row);

		descLabel = new Label();
		descLabel.CustomMinimumSize = new Vector2(160, 0);
		descLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(descLabel);

		return row;
	}

	/// <summary>行尾值 label：右对齐、次级文字色、固定最小宽度。</summary>
	Label CreateValueLabel()
	{
		var label = new Label();
		label.CustomMinimumSize = new Vector2(56, 0);
		label.HorizontalAlignment = HorizontalAlignment.Right;
		label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		label.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		return label;
	}

	static StyleBox SeparatorStyle()
	{
		var line = new StyleBoxLine();
		line.Vertical = false; // 水平分隔线（HSeparator 用）
		line.Color = GEmueraTheme.Border;
		line.Thickness = 1;
		return line;
	}

	static void SetMargins(MarginContainer margin, int padding)
	{
		margin.AddThemeConstantOverride("margin_left", padding);
		margin.AddThemeConstantOverride("margin_top", padding);
		margin.AddThemeConstantOverride("margin_right", padding);
		margin.AddThemeConstantOverride("margin_bottom", padding);
	}

	// Re-read every MultiLanguage-driven description label and group title.
	// Value labels (slider-derived numbers) are intentionally untouched.
	// Idempotent.
	void ApplyLanguageTexts()
	{
		if (fontLabel != null)
			fontLabel.Text = MultiLanguage.Get("OptionWindow.FontSize", "Font Size");
		if (quickWidthLabel != null)
			quickWidthLabel.Text = MultiLanguage.Get("OptionWindow.QuickButtonWidth", "Quick Width");
		if (quickFontLabel != null)
			quickFontLabel.Text = MultiLanguage.Get("OptionWindow.QuickFontSize", "Quick Font");
		if (sensitivityLabel != null)
			sensitivityLabel.Text = MultiLanguage.Get("OptionWindow.ButtonDragSensitivity", "Scroll Sensitivity");
		if (virtualCursorSensitivityLabel != null)
			virtualCursorSensitivityLabel.Text = MultiLanguage.Get("OptionWindow.VirtualCursorSensitivity", "Cursor Sensitivity");
		if (virtualMouseScaleLabel != null)
			virtualMouseScaleLabel.Text = MultiLanguage.Get("OptionWindow.VirtualMouseScale", "Virtual Mouse Scale");
		if (virtualMouseOpacityLabel != null)
			virtualMouseOpacityLabel.Text = MultiLanguage.Get("OptionWindow.VirtualMouseOpacity", "Mouse Opacity");
		if (pinchZoomLabel != null)
			pinchZoomLabel.Text = MultiLanguage.Get("OptionWindow.PinchZoom", "Pinch Zoom");
		if (quickFlipLabel != null)
			quickFlipLabel.Text = MultiLanguage.Get("OptionWindow.QuickFlip", "Quick Panel Flip");
		if (quickFloatingLabel != null)
			quickFloatingLabel.Text = MultiLanguage.Get("OptionWindow.QuickFloating", "Quick Floating Window");
		if (maxLinesTextLabel != null)
			maxLinesTextLabel.Text = MultiLanguage.Get("OptionWindow.MaxVisibleLines", "Max Lines");
		if (gameViewLeftPaddingLabel != null)
			gameViewLeftPaddingLabel.Text = MultiLanguage.Get("OptionWindow.GameViewLeftPadding", "Left Padding");
		if (gameViewRightPaddingLabel != null)
			gameViewRightPaddingLabel.Text = MultiLanguage.Get("OptionWindow.GameViewRightPadding", "Right Padding");
		if (gameViewPaddingHintLabel != null)
			gameViewPaddingHintLabel.Text = MultiLanguage.Get(
				"OptionWindow.GameViewPaddingHint",
				"仅移动端生效：正值增加留白、负值减少留白，用于修正全面屏/曲面屏左右安全区显示异常。");
		if (resLabel != null)
			resLabel.Text = MultiLanguage.Get("OptionWindow.Resolution", "Resolution");
		if (fpsLabel != null)
			fpsLabel.Text = MultiLanguage.Get("OptionWindow.FrameRate", "Frame Rate");
		if (vsyncLabel != null)
			vsyncLabel.Text = MultiLanguage.Get("OptionWindow.VSync", "V-Sync");
		// 提示文案与可见性统一由 RefreshVSyncHint 刷新（语言切换时也走这里）。
		if (vsyncHintLabel != null)
			RefreshVSyncHint();
		if (langLabel != null)
			langLabel.Text = MultiLanguage.Get("OptionWindow.Language", "Language");
		if (closeBtn != null)
			closeBtn.Text = MultiLanguage.Get("OptionWindow.Close", "Close");
		if (displayGroupTitle != null)
			displayGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupDisplay", "Display");
		if (quickGroupTitle != null)
			quickGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupQuick", "Quick");
		if (inputGroupTitle != null)
			inputGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupInput", "Input");
		if (systemGroupTitle != null)
			systemGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupSystem", "System");
	}

	public void ShowPopup()
	{
		popup.PopupCentered();
		CallDeferred(nameof(ClampPopupToSafeArea));
		EmitSignal(SignalName.PopupOpened);
	}

	void ClampPopupToSafeArea()
	{
		if (popup == null)
			return;

		Rect2 safeRect = EmueraContent.GetSafeViewportRect(GetViewport());
		Vector2I size = popup.Size;
		int maxWidth = Mathf.Max(1, Mathf.RoundToInt(safeRect.Size.X - 20));
		int maxHeight = Mathf.Max(1, Mathf.RoundToInt(safeRect.Size.Y - 20));
		if (size.X > maxWidth || size.Y > maxHeight)
		{
			size = new Vector2I(System.Math.Min(size.X, maxWidth), System.Math.Min(size.Y, maxHeight));
			popup.Size = size;
		}

		int left = Mathf.RoundToInt(safeRect.Position.X + 10);
		int top = Mathf.RoundToInt(safeRect.Position.Y + 10);
		int right = Mathf.RoundToInt(safeRect.Position.X + safeRect.Size.X - size.X - 10);
		int bottom = Mathf.RoundToInt(safeRect.Position.Y + safeRect.Size.Y - size.Y - 10);
		if (right < left)
			right = left;
		if (bottom < top)
			bottom = top;
		popup.Position = new Vector2I(
			Mathf.Clamp(popup.Position.X, left, right),
			Mathf.Clamp(popup.Position.Y, top, bottom));
	}

	void OnFontSizeChanged(double value)
	{
		int coreSize = MinorShift.Emuera.Config.FontSize > 0 ? MinorShift.Emuera.Config.FontSize : 18;
		fontSizeLabel.Text = coreSize.ToString();
		if (fontSizeSlider != null && (int)fontSizeSlider.Value != coreSize)
		{
			fontSizeSlider.SetBlockSignals(true);
			fontSizeSlider.Value = coreSize;
			fontSizeSlider.SetBlockSignals(false);
		}
	}

	void OnQuickButtonWidthChanged(double value)
	{
		int width = (int)value;
		quickButtonWidthLabel.Text = width.ToString();
		QuickButtons.ConfiguredButtonWidth = width;
		EmueraContent.instance?.RefreshQuickButtonSettings();
	}

	void OnQuickFontSizeChanged(double value)
	{
		int size = (int)value;
		quickFontSizeLabel.Text = size.ToString();
		QuickButtons.ConfiguredFontSize = size;
		EmueraContent.instance?.RefreshQuickButtonSettings();
	}

	void OnQuickFlipToggled(bool enabled)
	{
		QuickButtons.FlipEnabled = enabled;
		EmueraContent.instance?.RefreshQuickButtonSettings();
	}

	void OnQuickFloatingToggled(bool enabled)
	{
		QuickButtons.FloatingEnabled = enabled;
		EmueraContent.instance?.RefreshQuickHost();
	}

	void OnButtonDragSensitivityChanged(double value)
	{
		float sensitivity = (float)value;
		buttonDragSensitivityLabel.Text = sensitivity.ToString("0.00") + "x";
		EmueraContent.ContentDragSensitivity = sensitivity;
	}

	void OnVirtualCursorSensitivityChanged(double value)
	{
		float sensitivity = (float)value;
		if (virtualCursorSensitivityValue != null)
			virtualCursorSensitivityValue.Text = sensitivity.ToString("0.00") + "x";
		VirtualMouse.ConfiguredSensitivity = sensitivity;
	}

	void OnVirtualMouseScaleChanged(double value)
	{
		float scale = (float)value;
		if (virtualMouseScaleValue != null)
			virtualMouseScaleValue.Text = scale.ToString("0.000");
		VirtualMouse.ConfiguredScale = scale;
		// 用 Instance 而非 Active：鼠标禁用时 Active 为空，改缩放也要能立即应用/记住；
		// 重新 Enable 时会再 ApplyConfiguredScale 补一次（见 VirtualMouse.Enable）。
		VirtualMouse.Instance?.RefreshScale();
	}

	void OnVirtualMouseOpacityChanged(double value)
	{
		float opacity = (float)value;
		if (virtualMouseOpacityValue != null)
			virtualMouseOpacityValue.Text = opacity.ToString("0.00");
		VirtualMouse.ConfiguredOpacity = opacity;
		VirtualMouse.Instance?.RefreshOpacity();
	}

	void OnPinchZoomToggled(bool enabled)
	{
		EmueraContent.ContentPinchZoomEnabled = enabled;
	}

	void OnMaxVisibleLinesChanged(double value)
	{
		int lines = (int)value;
		maxVisibleLinesLabel.Text = lines.ToString();
		EmueraContent.ConfiguredMaxVisibleLines = lines;
	}

	// 左右留白：拖动即写入 user://settings.cfg 并触发内容区重排（仅移动端生效）。
	void OnGameViewLeftPaddingChanged(double value)
	{
		int padding = (int)value;
		gameViewLeftPaddingValue.Text = FormatGameViewPadding(padding);
		EmueraContent.GameViewLeftPadding = padding;
	}

	void OnGameViewRightPaddingChanged(double value)
	{
		int padding = (int)value;
		gameViewRightPaddingValue.Text = FormatGameViewPadding(padding);
		EmueraContent.GameViewRightPadding = padding;
	}

	static string FormatGameViewPadding(int value)
	{
		return (value > 0 ? "+" : "") + value.ToString() + " px";
	}

	void OnResolutionSelected(long index)
	{
		ResolutionHelper.resolution_index = (int)index;
		ResolutionHelper.Apply();
	}

	void OnFrameRateSelected(long index)
	{
		FrameRateHelper.frame_rate_index = (int)index;
		FrameRateHelper.Apply();
	}

	// 垂直同步：先持久化期望值再应用；Apply 会读回实际模式，失败时由提示行说明。
	void OnVSyncToggled(bool enabled)
	{
		VSyncHelper.Enabled = enabled;
		VSyncHelper.Apply();
		RefreshVSyncHint();
	}

	// 提示仅在"请求关闭、但可能被回退"时显示：关闭后（含 Godot Compatibility 渲染器
	// 只支持 Enabled 的情况）才需要解释，默认开启时不占用一行设置说明。
	void RefreshVSyncHint()
	{
		if (vsyncHintLabel == null)
			return;
		vsyncHintLabel.Text = MultiLanguage.Get(
			"OptionWindow.VSyncHint",
			"Godot Compatibility renderer may force V-Sync on; turning it off can fall back to Enabled.");
		vsyncHintLabel.Visible = ShouldShowVSyncHint();
	}

	static bool ShouldShowVSyncHint()
	{
		return !VSyncHelper.Enabled || VSyncHelper.LastRequestUnsupported;
	}

	void OnLanguageSelected(long index)
	{
		MultiLanguage.Load(languages[index]);
		// LanguageChanged already refreshed EmueraContent texts; refresh this
		// window's own description labels as well (idempotent).
		ApplyLanguageTexts();
	}

	int GetLanguageIndex(string lang)
	{
		for (int i = 0; i < languages.Length; i++)
		{
			if (languages[i] == lang)
				return i;
		}
		return 0;
	}
}
