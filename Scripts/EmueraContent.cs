using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;
using EmuFont = uEmuera.Drawing.Font;
using EmuColor = uEmuera.Drawing.Color;

public partial class EmueraContent : Control
{
    public static EmueraContent instance { get; private set; }

    ScrollContainer scrollContainer;
    Control scaledContentRoot;
    VBoxContainer lineContainer;
    VBoxContainer htmlIslandContainer;
    HBoxContainer menuBar;
    Inputpad inputpad;
    QuickButtons quickButtons;
    Scalepad scalepad;
    ColorRect bgRect;
    Control cbgContainer;
    OptionWindow optionWindow;
    AudioStreamPlayer bgmPlayer;
    List<AudioStreamPlayer> soundPlayers = new List<AudioStreamPlayer>();
    List<int> soundRepeatRemaining = new List<int>();
    float soundVolume = 1.0f;
    float bgmVolume = 1.0f;

    Dictionary<int, ConsoleDisplayLine> lineObjects = new Dictionary<int, ConsoleDisplayLine>();
    HashSet<string> failedTextureSearches = new HashSet<string>();
    List<EmueraImage> cbgNodes = new List<EmueraImage>();
    List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> renderedCbgLayers = new List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage>();
    bool batchingDisplayLines = false;

    public const int DefaultMaxVisibleLines = 360;
    public const int MinMaxVisibleLines = 120;
    public const int MaxMaxVisibleLines = 3000;
    static int MaxVisibleLines => ConfiguredMaxVisibleLines;
    static int LineTrimBatch => System.Math.Max(40, System.Math.Min(200, MaxVisibleLines / 6));

    Font mainFont;
    int lastButtonGeneration = -1;
    int displayRevision = 0;
    int quickRenderedGeneration = int.MinValue;
    int quickRenderedRevision = -1;
    bool quickInputGateActive = false;
    bool quickAutoHiddenUntilNextButtons = false;
    int quickAutoHiddenGeneration = int.MinValue;
    long quickInputGateGeneration = -1;
    int quickInputGateRevision = -1;
    ulong quickInputGateTick = 0;
    int lastCbgScrollVertical = int.MinValue;
    uint lastClickTick = 0;
    bool contentDragActive = false;
    bool contentDragMoved = false;
    bool contentDragStartedOnButton = false;
    Vector2 contentDragStartPosition;
    Vector2 contentDragLastPosition;
    Vector2 contentScrollVelocity = Vector2.Zero;
    Vector2 contentInertiaRemainder = Vector2.Zero;
    Control contentDragButton;
    string contentDragButtonInput;
    long contentDragButtonGeneration;
    ulong contentLastDragTick = 0;
    bool contentInertiaActive = false;
    float contentInertiaDeceleration = 900.0f;
    int contentScrollInteractionSerial = 0;
    bool pendingScroll = false;
    bool pendingScaleBoundsUpdate = false;
    float contentScale = 1.0f;
    const float ScrollDragThreshold = 10.0f;
    const float ContentInertiaMinVelocity = 80.0f;
    const float ContentInertiaFastVelocity = 4500.0f;
    const float ContentInertiaMaxVelocity = 14000.0f;
    const float ContentInertiaMinReleaseBoost = 1.2f;
    const float ContentInertiaMaxReleaseBoost = 3.0f;
    const float ContentInertiaSlowDeceleration = 1800.0f;
    const float ContentInertiaFastDeceleration = 520.0f;
    const float ContentInertiaStopVelocity = 6.0f;
    const string SettingsPath = "user://settings.cfg";
    const string SettingsSection = "Display";
    const string ContentDragSensitivityKey = "ContentDragSensitivity";
    const string ButtonDragSensitivityKey = "ButtonDragSensitivity";
    const string MaxVisibleLinesKey = "MaxVisibleLines";
    const ulong QuickInputGateFallbackMs = 500;
    static float contentDragSensitivity = -1.0f;
    static int configuredMaxVisibleLines = -1;

    Label inProcessLabel;

    // Message box
    PopupPanel msgBox;
    Label msgBoxTitle;
    Label msgBoxMessage;
    Button msgBoxConfirmBtn;
    Button msgBoxCancelBtn;
    System.Action msgBoxConfirmCallback;
    System.Action msgBoxCancelCallback;

    CanvasLayer menuLayer;
    HBoxContainer menuExpandedBar;
    bool menuExpanded = false;
    TextureButton inputMenuButton;
    TextureButton quickMenuButton;
    TextureButton autoSkipMenuButton;
    TextureButton scaleMenuButton;
    bool autoClickSkipEnabled = false;
    ulong lastAutoClickSkipTick = 0;
    static readonly Color ActiveSystemButtonColor = new Color(1.0f, 0.86f, 0.15f, 1.0f);
    static readonly Color NormalSystemButtonColor = new Color(1, 1, 1, 1);

    public static int ContentWidth { get; private set; }
    public static int ConfiguredMaxVisibleLines
    {
        get
        {
            if (configuredMaxVisibleLines < 0)
            {
                var cfg = new ConfigFile();
                cfg.Load(SettingsPath);
                configuredMaxVisibleLines = ClampMaxVisibleLines(
                    (int)cfg.GetValue(SettingsSection, MaxVisibleLinesKey, DefaultMaxVisibleLines));
            }
            return configuredMaxVisibleLines;
        }
        set
        {
            configuredMaxVisibleLines = ClampMaxVisibleLines(value);
            var cfg = new ConfigFile();
            cfg.Load(SettingsPath);
            cfg.SetValue(SettingsSection, MaxVisibleLinesKey, configuredMaxVisibleLines);
            cfg.Save(SettingsPath);
            instance?.TrimVisibleLinesToLimit();
        }
    }

    static int ClampMaxVisibleLines(int value)
    {
        return System.Math.Max(MinMaxVisibleLines, System.Math.Min(MaxMaxVisibleLines, value));
    }

    public static float ContentDragSensitivity
    {
        get
        {
            if (contentDragSensitivity < 0)
            {
                var cfg = new ConfigFile();
                cfg.Load(SettingsPath);
                var fallback = cfg.GetValue(SettingsSection, ButtonDragSensitivityKey, 2.0);
                contentDragSensitivity = Mathf.Clamp(
                    (float)(double)cfg.GetValue(SettingsSection, ContentDragSensitivityKey, fallback),
                    0.5f,
                    2.0f);
            }
            return contentDragSensitivity;
        }
        set
        {
            contentDragSensitivity = Mathf.Clamp(value, 0.5f, 2.0f);
            var cfg = new ConfigFile();
            cfg.Load(SettingsPath);
            cfg.SetValue(SettingsSection, ContentDragSensitivityKey, contentDragSensitivity);
            cfg.Save(SettingsPath);
        }
    }

    public static float ButtonDragSensitivity
    {
        get => ContentDragSensitivity;
        set => ContentDragSensitivity = value;
    }

    public override void _Ready()
    {
        instance = this;
        Size = GetViewportRect().Size;
        ContentWidth = (int)Size.X;
        if (ContentWidth > Config.WindowX)
            Config.UpdateWindowWidth(ContentWidth);
        GetViewport().SizeChanged += OnViewportSizeChanged;

        mainFont = LoadConfiguredFont();

        bgRect = new ColorRect();
        bgRect.AnchorLeft = 0;
        bgRect.AnchorTop = 0;
        bgRect.AnchorRight = 1;
        bgRect.AnchorBottom = 1;
        AddChild(bgRect);

        var rootVBox = new Control();
        rootVBox.AnchorLeft = 0;
        rootVBox.AnchorTop = 0;
        rootVBox.AnchorRight = 1;
        rootVBox.AnchorBottom = 1;
        AddChild(rootVBox);

        // Menu bar on a separate CanvasLayer so it doesn't block click-to-advance.
        // Toggle sits in the top-right corner; tapping it expands icons to the left.
        menuLayer = new CanvasLayer();
        menuLayer.Layer = 100;
        AddChild(menuLayer);

        var menuRoot = new HBoxContainer();
        menuRoot.AnchorLeft = 1;
        menuRoot.AnchorRight = 1;
        menuRoot.AnchorTop = 0;
        menuRoot.AnchorBottom = 0;
        menuRoot.GrowHorizontal = Control.GrowDirection.Begin;
        menuRoot.OffsetRight = -4;
        menuRoot.OffsetTop = 4;
        menuRoot.MouseFilter = MouseFilterEnum.Pass;
        menuRoot.AddThemeConstantOverride("separation", 2);
        menuLayer.AddChild(menuRoot);

        // Panel holding the 9 action icons (hidden until toggled).
        // Placed BEFORE the toggle in the HBox so it sits on the toggle's left.
        var menuPanel = new PanelContainer();
        menuPanel.MouseFilter = MouseFilterEnum.Stop;
        var menuPanelStyle = new StyleBoxFlat();
        menuPanelStyle.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        menuPanelStyle.CornerRadiusTopLeft = menuPanelStyle.CornerRadiusBottomLeft = 4;
        menuPanelStyle.CornerRadiusTopRight = menuPanelStyle.CornerRadiusBottomRight = 4;
        menuPanelStyle.ContentMarginLeft = menuPanelStyle.ContentMarginRight = 4;
        menuPanelStyle.ContentMarginTop = menuPanelStyle.ContentMarginBottom = 2;
        menuPanel.AddThemeStyleboxOverride("panel", menuPanelStyle);
        menuPanel.Visible = false;
        menuRoot.AddChild(menuPanel);

        menuExpandedBar = new HBoxContainer();
        menuExpandedBar.AddThemeConstantOverride("separation", 2);
        menuPanel.AddChild(menuExpandedBar);

        menuBar = menuExpandedBar;
        AddIconButton("res://Icons/fenxiang.svg", OnBackPressed);
        AddIconButton("res://Icons/restart.svg", OnRestartPressed);
        AddIconButton("res://Icons/options.svg", OnOptionsPressed);
        inputMenuButton = AddIconButton("res://Icons/io-input.svg", OnInputTogglePressed);
        quickMenuButton = AddIconButton("res://Icons/quick.svg", OnQuickTogglePressed);
        autoSkipMenuButton = AddIconButton("res://Icons/autoskip.svg", OnAutoSkipTogglePressed);
        AddIconButton("res://Icons/menu_save_log.svg", OnSaveLogPressed);
        AddIconButton("res://Icons/Title.svg", OnGotoTitlePressed);
        AddIconButton("res://Icons/exit.svg", OnExitPressed);
        scaleMenuButton = AddIconButton("res://Icons/Scale.svg", OnScaleTogglePressed);

        // Toggle at the right edge (last child = rightmost in HBox).
        var menuToggleBtn = new TextureButton();
        menuToggleBtn.CustomMinimumSize = new Vector2(36, 36);
        menuToggleBtn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
        menuToggleBtn.MouseFilter = MouseFilterEnum.Stop;
        if (ResourceLoader.Exists("res://Icons/menu.svg"))
            menuToggleBtn.TextureNormal = ResourceLoader.Load<Texture2D>("res://Icons/menu.svg");
        WireSystemButton(menuToggleBtn, OnMenuTogglePressed);
        menuRoot.AddChild(menuToggleBtn);

        quickButtons = new QuickButtons();
        AddChild(quickButtons);

        inputpad = new Inputpad();
        AddChild(inputpad);

        scalepad = new Scalepad();
        AddChild(scalepad);

        optionWindow = new OptionWindow();
        AddChild(optionWindow);

        scrollContainer = new ScrollContainer();
        scrollContainer.AnchorLeft = 0;
        scrollContainer.AnchorTop = 0;
        scrollContainer.AnchorRight = 1;
        scrollContainer.AnchorBottom = 1;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        scrollContainer.FollowFocus = false;
        scrollContainer.ClipContents = true;
        scrollContainer.MouseFilter = MouseFilterEnum.Pass;
        scrollContainer.GuiInput += OnContentGuiInput;
        rootVBox.AddChild(scrollContainer);

        inProcessLabel = new Label();
        inProcessLabel.AnchorLeft = 0;
        inProcessLabel.AnchorTop = 0;
        inProcessLabel.AnchorRight = 1;
        inProcessLabel.AnchorBottom = 0;
        inProcessLabel.OffsetTop = 4;
        inProcessLabel.OffsetBottom = 32;
        inProcessLabel.Text = MultiLanguage.Get("EmueraContent.InProcess", "Processing...");
        inProcessLabel.MouseFilter = MouseFilterEnum.Ignore;
        inProcessLabel.ZIndex = 20;
        ApplyFont(inProcessLabel);
        inProcessLabel.HorizontalAlignment = HorizontalAlignment.Center;
        inProcessLabel.Visible = false;
        rootVBox.AddChild(inProcessLabel);

        scaledContentRoot = new Control();
        scaledContentRoot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scaledContentRoot.SizeFlagsVertical = SizeFlags.ExpandFill;
        scaledContentRoot.MouseFilter = MouseFilterEnum.Pass;
        scaledContentRoot.GuiInput += OnContentGuiInput;
        scrollContainer.AddChild(scaledContentRoot);

        lineContainer = new VBoxContainer();
        lineContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lineContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        lineContainer.ClipContents = false;
        lineContainer.AddThemeConstantOverride("separation", 0);
        scaledContentRoot.AddChild(lineContainer);

        htmlIslandContainer = new VBoxContainer();
        htmlIslandContainer.MouseFilter = MouseFilterEnum.Ignore;
        htmlIslandContainer.ClipContents = false;
        htmlIslandContainer.AddThemeConstantOverride("separation", 0);
        scaledContentRoot.AddChild(htmlIslandContainer);

        // Message box popup
        msgBox = new PopupPanel();
        msgBox.Size = new Vector2I(400, 220);
        AddChild(msgBox);

        var msgVBox = new VBoxContainer();
        msgVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        msgVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        msgVBox.Alignment = BoxContainer.AlignmentMode.Center;
        msgBox.AddChild(msgVBox);

        msgBoxTitle = new Label();
        msgBoxTitle.HorizontalAlignment = HorizontalAlignment.Center;
        msgVBox.AddChild(msgBoxTitle);

        msgBoxMessage = new Label();
        msgBoxMessage.HorizontalAlignment = HorizontalAlignment.Center;
        msgBoxMessage.AutowrapMode = TextServer.AutowrapMode.Word;
        msgBoxMessage.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        msgVBox.AddChild(msgBoxMessage);

        var msgBtnHBox = new HBoxContainer();
        msgBtnHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        msgBtnHBox.Alignment = BoxContainer.AlignmentMode.Center;
        msgVBox.AddChild(msgBtnHBox);

        msgBoxConfirmBtn = new Button();
        msgBoxConfirmBtn.Text = MultiLanguage.Get("MsgBox.Confirm", "OK");
        StyleButton(msgBoxConfirmBtn);
        msgBoxConfirmBtn.Pressed += OnMsgConfirm;
        msgBtnHBox.AddChild(msgBoxConfirmBtn);

        msgBoxCancelBtn = new Button();
        msgBoxCancelBtn.Text = MultiLanguage.Get("MsgBox.Cancel", "Cancel");
        StyleButton(msgBoxCancelBtn);
        msgBoxCancelBtn.Pressed += OnMsgCancel;
        msgBtnHBox.AddChild(msgBoxCancelBtn);

        // Apply fonts to auxiliary UI
        inputpad.ApplyFont(mainFont, FontSize);
        quickButtons.ApplyFont(mainFont, FontSize);
        scalepad.ApplyFont(mainFont, FontSize);
        ApplyFont(msgBoxTitle);
        ApplyFont(msgBoxMessage);
        ApplyFont(msgBoxConfirmBtn);
        ApplyFont(msgBoxCancelBtn);

        cbgContainer = new Control();
        cbgContainer.AnchorLeft = 0;
        cbgContainer.AnchorTop = 0;
        cbgContainer.AnchorRight = 1;
        cbgContainer.AnchorBottom = 1;
        cbgContainer.MouseFilter = MouseFilterEnum.Ignore;
        cbgContainer.ZIndex = 10;
        AddChild(cbgContainer);
    }

    public void Clear()
    {
        GenericUtils.ClearPointingButton();
        foreach(var child in lineContainer.GetChildren())
            SafeQueueFree(child);
        lineObjects.Clear();
        failedTextureSearches.Clear();
        displayRevision++;
        RefreshQuickInputGate();
        quickRenderedRevision = -1;
        if (quickButtons != null && quickButtons.IsShow)
            quickButtons.Clear();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= OnViewportSizeChanged;
        if (instance == this)
            instance = null;
    }

    int FontSize => Config.FontSize > 0 ? Config.FontSize : 18;

    int _effectiveLineHeight = -1;
    int EffectiveLineHeight
    {
        get
        {
            if (_effectiveLineHeight < 0)
            {
                if (mainFont != null)
                {
                    int fontH = (int)System.Math.Ceiling(mainFont.GetHeight(FontSize));
                    int lineSpacing = 3;
                    _effectiveLineHeight = System.Math.Max(Config.LineHeight, fontH + lineSpacing);
                }
                else
                {
                    _effectiveLineHeight = System.Math.Max(Config.LineHeight, FontSize + 9);
                }
            }
            return _effectiveLineHeight;
        }
    }

    void ApplyFont(Control control)
    {
        if (mainFont != null)
            control.AddThemeFontOverride("font", mainFont);
        control.AddThemeFontSizeOverride("font_size", FontSize);
    }

    static void SetFixedControlSize(Control control, Vector2 size)
    {
        control.CustomMinimumSize = size;
        control.Size = size;
    }

    Label CreateTextPart(string text, EmuColor color, EmuFont font, float width)
    {
        var label = new Label();
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.Text = text ?? "";
        ApplyFont(label);
        label.AddThemeColorOverride("font_color", new Godot.Color(color.r, color.g, color.b, color.a));
        if (font?.Bold == true)
        {
            label.AddThemeConstantOverride("outline_size", 1);
            label.AddThemeColorOverride("font_outline_color", new Godot.Color(color.r, color.g, color.b, color.a));
        }
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ClipText = true;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        SetFixedControlSize(label, new Vector2(width, EffectiveLineHeight));
        return label;
    }

    Font LoadConfiguredFont()
    {
        string requested = Config.FontName;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return new SystemFont
            {
                FontNames = new[]
                {
                    requested,
                    "SimHei",
                    "Microsoft YaHei",
                    "MS Gothic",
                    "Noto Sans CJK SC",
                    "Noto Sans CJK JP",
                    "Droid Sans Fallback",
                    "sans-serif"
                }
            };
        }
        return ResourceLoader.Load<Font>("res://Fonts/MS Gothic.ttf");
    }

    int GetPartTop(AConsoleDisplayPart part)
    {
        if (part == null)
            return 0;
        if (part is ConsoleImagePart image)
        {
            if (image.Display == DisplayMode.Relative)
                return 0;
            return System.Math.Min(0, image.PositionY);
        }
        if (part is ConsoleDivPart div && div.IsRelative)
            return System.Math.Min(0, div.Y);
        return System.Math.Min(0, part.Top);
    }

    int GetPartBottom(AConsoleDisplayPart part)
    {
        if (part == null)
            return EffectiveLineHeight;
        if (part is ConsoleImagePart image)
        {
            if (image.Display == DisplayMode.Relative)
                return EffectiveLineHeight;
            return EffectiveLineHeight;
        }
        if (part is ConsoleDivPart div && div.IsRelative)
            return System.Math.Max(EffectiveLineHeight, div.Y + div.DivHeight);
        return System.Math.Max(EffectiveLineHeight, part.Bottom);
    }

    int GetButtonTop(ConsoleButtonString button)
    {
        int top = 0;
        if (button?.StrArray == null)
            return top;
        foreach (var part in button.StrArray)
            top = System.Math.Min(top, GetPartTop(part));
        return top;
    }

    int GetButtonBottom(ConsoleButtonString button)
    {
        int bottom = EffectiveLineHeight;
        if (button?.StrArray == null)
            return bottom;
        foreach (var part in button.StrArray)
            bottom = System.Math.Max(bottom, GetPartBottom(part));
        return bottom;
    }

    int GetLineBottom(ConsoleDisplayLine line)
    {
        int bottom = EffectiveLineHeight;
        if (line?.Buttons == null)
            return bottom;
        foreach (var button in line.Buttons)
            bottom = System.Math.Max(bottom, GetButtonBottom(button));
        return bottom;
    }

    static StyleBoxFlat _btnNormalStyle;
    static StyleBoxFlat _btnHoverStyle;

    static void EnsureButtonStyles()
    {
        if (_btnNormalStyle != null)
            return;
        _btnNormalStyle = new StyleBoxFlat();
        _btnNormalStyle.BgColor = new Color(0, 0, 0, 0);
        _btnNormalStyle.BorderColor = new Color(0, 0, 0, 0);
        _btnNormalStyle.BorderWidthLeft = _btnNormalStyle.BorderWidthRight = 0;
        _btnNormalStyle.BorderWidthTop = _btnNormalStyle.BorderWidthBottom = 0;
        _btnNormalStyle.ContentMarginLeft = _btnNormalStyle.ContentMarginRight = 0;
        _btnNormalStyle.ContentMarginTop = _btnNormalStyle.ContentMarginBottom = 0;

        _btnHoverStyle = new StyleBoxFlat();
        _btnHoverStyle.BgColor = new Color(0, 0, 0, 0);
        _btnHoverStyle.BorderColor = new Color(0, 0, 0, 0);
        _btnHoverStyle.BorderWidthLeft = _btnHoverStyle.BorderWidthRight = 0;
        _btnHoverStyle.BorderWidthTop = _btnHoverStyle.BorderWidthBottom = 0;
        _btnHoverStyle.ContentMarginLeft = _btnHoverStyle.ContentMarginRight = 0;
        _btnHoverStyle.ContentMarginTop = _btnHoverStyle.ContentMarginBottom = 0;
    }

    internal static void StyleButton(Button btn)
    {
        EnsureButtonStyles();
        btn.AddThemeStyleboxOverride("normal", _btnNormalStyle);
        btn.AddThemeStyleboxOverride("hover", _btnHoverStyle);
        btn.AddThemeStyleboxOverride("pressed", _btnHoverStyle);
        btn.AddThemeStyleboxOverride("focus", _btnHoverStyle);
        btn.AddThemeColorOverride("font_color", new Color(1, 1, 1, 1));
        var focusColor = new Godot.Color(Config.FocusColor.r, Config.FocusColor.g, Config.FocusColor.b, Config.FocusColor.a);
        btn.AddThemeColorOverride("font_hover_color", focusColor);
        btn.AddThemeColorOverride("font_pressed_color", focusColor);
        btn.AddThemeColorOverride("font_focus_color", focusColor);
    }

    TextureButton AddIconButton(string iconPath, System.Action callback)
    {
        var btn = new TextureButton();
        btn.CustomMinimumSize = new Vector2(36, 36);
        btn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
        btn.MouseFilter = MouseFilterEnum.Stop;
        if (!string.IsNullOrWhiteSpace(iconPath)
            && !string.Equals(iconPath, "res://", System.StringComparison.OrdinalIgnoreCase)
            && ResourceLoader.Exists(iconPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(iconPath);
            btn.TextureNormal = tex;
        }
        WireSystemButton(btn, callback);
        menuBar.AddChild(btn);
        return btn;
    }

    void WireSystemButton(TextureButton btn, System.Action callback)
    {
        bool tracking = false;
        bool moved = false;
        Vector2 start = Vector2.Zero;
        btn.GuiInput += inputEvent =>
        {
            if (!TryGetPointer(inputEvent, out var position, out var pressed, out var released, out var motion))
                return;

            if (pressed)
            {
                tracking = true;
                moved = false;
                start = position;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (!tracking)
                return;

            if (motion)
            {
                if ((position - start).Length() >= ScrollDragThreshold)
                    moved = true;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (released)
            {
                tracking = false;
                GetViewport().SetInputAsHandled();
                if (!moved)
                    callback?.Invoke();
            }
        };
    }

    internal void AddLine(ConsoleDisplayLine line, bool isUpdate)
    {
        if (line == null)
            return;
        int lineHeight = GetLineBottom(line);
        var lineControl = new Control();
        lineControl.MouseFilter = MouseFilterEnum.Pass;
        lineControl.ClipContents = false;
        AddLineBackground(line, lineControl, lineHeight);
        int maxLineRight = 0;

        foreach(var button in line.Buttons)
        {
            if(button.IsButton)
            {
                int buttonTop = GetButtonTop(button);
                int buttonHeight = GetButtonBottom(button) - buttonTop;
                if (buttonHeight <= 0)
                    buttonHeight = EffectiveLineHeight;
                var btn = new Panel();
                btn.FocusMode = FocusModeEnum.None;
                btn.MouseForcePassScrollEvents = false;
                btn.MouseFilter = MouseFilterEnum.Stop;
                btn.ClipContents = false;
                EnsureButtonStyles();
                btn.AddThemeStyleboxOverride("panel", _btnNormalStyle);
                string inputs = button.Inputs;
                long generation = button.Generation;
                btn.GuiInput += inputEvent => OnContentButtonGuiInput(inputEvent, btn, inputs, generation);
                btn.MouseEntered += () => GenericUtils.SetPointingButton(inputs, generation);
                btn.MouseExited += () => GenericUtils.ClearPointingButton(generation);
                btn.SetMeta("generation", generation);

                var contentBox = new Control();
                contentBox.MouseFilter = MouseFilterEnum.Ignore;
                contentBox.ClipContents = false;
                contentBox.Position = new Vector2(0, -buttonTop);
                btn.AddChild(contentBox);

                foreach(var part in button.StrArray)
                {
                    AddPartToContainer(part, contentBox, button.PointX);
                }

                // Let clicks pass through to the Button
                foreach (var child in contentBox.GetChildren())
                {
                    if (child is Control c)
                        c.MouseFilter = MouseFilterEnum.Ignore;
                }

                SetFixedControlSize(contentBox, new Vector2(button.Width, buttonHeight));
                btn.CustomMinimumSize = new Vector2(button.Width, buttonHeight);
                btn.Position = new Vector2(button.PointX, buttonTop);
                btn.Size = new Vector2(button.Width, buttonHeight);
                lineControl.AddChild(btn);

                int btnRight = button.PointX + button.Width;
                if (btnRight > maxLineRight) maxLineRight = btnRight;
            }
            else
            {
                foreach(var part in button.StrArray)
                {
                    AddPartToContainer(part, lineControl, 0);
                }
                int right = button.PointX + button.Width;
                if (right > maxLineRight) maxLineRight = right;
            }
        }

        int fixedLineHeight = lineControl.GetChildCount() == 0 ? 0 : lineHeight;
        SetFixedControlSize(lineControl, new Vector2(maxLineRight, fixedLineHeight));

        // Handle line updates: replace existing Control if LineNo already exists
        int insertIndex = -1;
        if (isUpdate && lineObjects.ContainsKey(line.LineNo))
        {
            int childCount = lineContainer.GetChildCount();
            for (int i = 0; i < childCount; i++)
            {
                var child = lineContainer.GetChild(i);
                if (child.HasMeta("line_no") && (int)child.GetMeta("line_no") == line.LineNo)
                {
                    SafeQueueFree(child);
                    insertIndex = i;
                    break;
                }
            }
        }

        lineContainer.AddChild(lineControl);
        if (insertIndex >= 0)
            lineContainer.MoveChild(lineControl, insertIndex);

        lineControl.SetMeta("line_no", line.LineNo);
        lineObjects[line.LineNo] = line;
        displayRevision++;

        // Enforce node cap to prevent unbounded memory growth
        if (!batchingDisplayLines && lineContainer.GetChildCount() > MaxVisibleLines)
            RemoveTopLines(LineTrimBatch);

        if (!batchingDisplayLines)
        {
            RefreshQuickInputGate();
            if (isUpdate)
                QueueScaleBoundsUpdate();
            else
                QueueDisplayFollowUp();
        }
    }

    internal void AddLines(IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines)
    {
        if (lines == null || lines.Count == 0)
            return;

        batchingDisplayLines = true;
        try
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var item = lines[i];
                if (item.Line != null)
                    AddLine(item.Line, item.Update);
            }
        }
        finally
        {
            batchingDisplayLines = false;
        }

        int overflow = lineContainer.GetChildCount() - MaxVisibleLines;
        if (overflow > 0)
            RemoveTopLines(System.Math.Max(LineTrimBatch, overflow));

        RefreshQuickInputGate();
        QueueDisplayFollowUp();
    }

    void QueueDisplayFollowUp()
    {
        QueueScaleBoundsUpdate();
        if (!pendingScroll)
        {
            pendingScroll = true;
            CallDeferred(nameof(DeferredScrollToBottom), contentScrollInteractionSerial);
        }
    }

    void AddLineBackground(ConsoleDisplayLine line, Control lineControl, int lineHeight)
    {
        if (line.TextBackgroundColor == null)
            return;
        var c = line.TextBackgroundColor.Value;
        var bg = new ColorRect();
        bg.MouseFilter = MouseFilterEnum.Ignore;
        bg.Color = new Godot.Color(c.r, c.g, c.b, c.a);
        bg.Position = Vector2.Zero;
        bg.Size = new Vector2(Config.DrawableWidth, lineHeight);
        bg.CustomMinimumSize = new Vector2(Config.DrawableWidth, lineHeight);
        bg.ZIndex = -1;
        lineControl.AddChild(bg);
    }

    internal void SetHtmlIsland(ConsoleDisplayLine[] lines)
    {
        ClearHtmlIsland();
        if (htmlIslandContainer == null || lines == null)
            return;
        foreach (var line in lines)
        {
            if (line == null)
                continue;
            int lineHeight = GetLineBottom(line);
            var lineControl = new Control();
            lineControl.MouseFilter = MouseFilterEnum.Ignore;
            lineControl.ClipContents = false;
            AddLineBackground(line, lineControl, lineHeight);
            foreach (var button in line.Buttons)
            {
                foreach (var part in button.StrArray)
                    AddPartToContainer(part, lineControl, 0);
            }
            SetFixedControlSize(lineControl, new Vector2(0, lineHeight));
            htmlIslandContainer.AddChild(lineControl);
        }
        displayRevision++;
        RefreshQuickInputGate();
        QueueScaleBoundsUpdate();
    }

    internal void ClearHtmlIsland()
    {
        if (htmlIslandContainer == null)
            return;
        foreach (var child in htmlIslandContainer.GetChildren())
            SafeQueueFree(child);
        displayRevision++;
        RefreshQuickInputGate();
    }

    async void DeferredScrollToBottom(int interactionSerial)
    {
        if (scrollContainer != null)
        {
            for (int i = 0; i < 5; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                UpdateScaleBounds();
                if (!contentDragActive && !contentInertiaActive && interactionSerial == contentScrollInteractionSerial)
                    scrollContainer.ScrollVertical = GetMaxContentVerticalScroll();
                else
                    break;
            }
        }
        pendingScroll = false;
    }

    void QueueScaleBoundsUpdate()
    {
        if (pendingScaleBoundsUpdate)
            return;
        pendingScaleBoundsUpdate = true;
        CallDeferred(nameof(DeferredUpdateScaleBounds));
    }

    async void DeferredUpdateScaleBounds()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        pendingScaleBoundsUpdate = false;
        UpdateScaleBounds();
    }

    void UpdateScaleBounds()
    {
        if (scaledContentRoot == null || lineContainer == null)
            return;

        var scrollSize = scrollContainer != null ? scrollContainer.Size : Vector2.Zero;
        var contentSize = CalculateLineContentSize();
        float unscaledWidth = Mathf.Max(Mathf.Max(Config.DrawableWidth, scrollSize.X / contentScale), contentSize.X);
        lineContainer.CustomMinimumSize = new Vector2(unscaledWidth, contentSize.Y);
        lineContainer.Position = Vector2.Zero;
        lineContainer.Size = new Vector2(unscaledWidth, contentSize.Y);
        var scaledSize = new Vector2(
            Mathf.Max(unscaledWidth * contentScale, scrollSize.X),
            Mathf.Max(contentSize.Y * contentScale, scrollSize.Y));
        scaledContentRoot.Position = Vector2.Zero;
        scaledContentRoot.CustomMinimumSize = scaledSize;
        scaledContentRoot.Size = scaledSize;
    }

    Vector2 CalculateLineContentSize()
    {
        float width = Config.DrawableWidth;
        float height = 0;
        int visibleRows = 0;
        int childCount = lineContainer.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            if (lineContainer.GetChild(i) is Control row)
            {
                var rowSize = row.CustomMinimumSize;
                if (rowSize.Y <= 0)
                    rowSize = row.GetCombinedMinimumSize();
                width = Mathf.Max(width, rowSize.X);
                height += rowSize.Y;
                visibleRows++;
            }
        }
        if (visibleRows > 1)
            height += (visibleRows - 1) * lineContainer.GetThemeConstant("separation");
        return new Vector2(width, height);
    }

    int AddPartToContainer(AConsoleDisplayPart part, Control container, int relX)
    {
        if(part is ConsoleStyledString css)
        {
            if (string.IsNullOrEmpty(css.Str))
                return EffectiveLineHeight;
            float posX = css.PointX - relX;
            float w;
            if (relX == 0)
            {
                float maxW = Config.DrawableWidth - css.PointX;
                w = css.Width > 0 ? System.Math.Min(css.Width, maxW) : maxW;
                if (w <= 0) w = 1;
            }
            else
            {
                w = css.Width > 0 ? css.Width : 9999;
            }
            var text = CreateTextPart(css.Str, css.pColor, css.Font, w);
            text.Position = new Vector2(posX, 0);
            container.AddChild(text);

            return EffectiveLineHeight;
        }
        else if(part is ConsoleDivPart div)
        {
            return AddDivPartToContainer(div, container, relX);
        }
        else if(part is ConsoleImagePart cip)
        {
            // Lazy retry: if cip.Image was null at construction time, try again now.
            // Dynamic sprites (e.g. CSPRITE/GDRAWCIMG-created 颜绘) may not have been
            // registered in imageDictionary when ConsoleImagePart was constructed.
            ASprite sprite = cip.Image;
            if (sprite == null && !string.IsNullOrEmpty(cip.ResourceName))
            {
                sprite = AppContents.GetSprite(cip.ResourceName);
            }

            var texture = GetSpriteTexture(sprite);
            if (texture == null && !string.IsNullOrEmpty(cip.ResourceName) && !IsDynamicCutinName(cip.ResourceName) && !failedTextureSearches.Contains(cip.ResourceName))
            {
                string resName = cip.ResourceName;
                var tryPaths = new List<string>
                {
                    resName,
                    System.IO.Path.Combine(Program.ContentDir, resName),
                    System.IO.Path.Combine(Program.ExeDir, resName),
                    System.IO.Path.Combine(Program.ExeDir, "resources", resName),
                };
                bool hasExt = resName.Contains(".");
                if (!hasExt)
                {
                    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" })
                    {
                        tryPaths.Add(resName + ext);
                        tryPaths.Add(System.IO.Path.Combine(Program.ContentDir, resName + ext));
                        tryPaths.Add(System.IO.Path.Combine(Program.ExeDir, "resources", resName + ext));
                    }
                }
                foreach (var tryPath in tryPaths)
                {
                    bool exists = uEmuera.Utils.FileExists(tryPath);
                    if (exists)
                    {
                        var ti = SpriteManager.GetTextureInfo(resName, tryPath);
                        if (ti != null)
                        {
                            texture = ti.texture;
                            break;
                        }
                    }
                }
                // Subdirectory search: scan ContentDir recursively for a matching filename
                if (texture == null && !string.IsNullOrEmpty(Program.ContentDir))
                {
                    string searchName = hasExt ? resName : null;
                    string[] exts = hasExt ? new[] { "" } : new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" };
                    foreach (var ext in exts)
                    {
                        string target = (searchName ?? resName) + ext;
                        var found = uEmuera.Utils.FindFileRecursive(Program.ContentDir, target);
                        if (!string.IsNullOrEmpty(found))
                        {
                            var ti = SpriteManager.GetTextureInfo(resName, found);
                            if (ti != null)
                            {
                                GenericUtils.Info($"[IMG] Found \"{resName}\" via subdirectory search: {found}");
                                texture = ti.texture;
                                break;
                            }
                        }
                    }
                }
                if (texture == null)
                {
                    failedTextureSearches.Add(resName);
                    GenericUtils.Info($"[IMG] All fallback paths failed for \"{resName}\"");
                }
            }
            if (texture != null)
            {
                int w, imgH;
                if (cip.dest_rect.Width > 0 && cip.dest_rect.Height > 0)
                {
                    // Both dimensions specified explicitly
                    w = cip.dest_rect.Width;
                    imgH = cip.dest_rect.Height;
                }
                else if (cip.dest_rect.Width > 0)
                {
                    // Width specified, compute height from aspect ratio
                    w = cip.dest_rect.Width;
                    imgH = texture.GetHeight() > 0 ? texture.GetHeight() * w / texture.GetWidth() : w;
                }
                else if (cip.dest_rect.Height > 0 && texture.GetHeight() > 0)
                {
                    // Height specified, compute width from aspect ratio (matches constructor logic)
                    imgH = cip.dest_rect.Height;
                    w = texture.GetWidth() * imgH / texture.GetHeight();
                }
                else
                {
                    // No dimensions specified, use natural size
                    w = texture.GetWidth() > 0 ? texture.GetWidth() : 32;
                    imgH = texture.GetHeight() > 0 ? texture.GetHeight() : 32;
                }
                // Ensure rendered width matches layout-allocated width to prevent gaps/overlaps
                if (cip.Width > 0 && cip.Width != w)
                {
                    int layoutW = cip.Width;
                    if (w > 0)
                        imgH = imgH * layoutW / w;
                    w = layoutW;
                }

                var emuImg = new EmueraImage();
                emuImg.MouseFilter = MouseFilterEnum.Ignore;
                if (texture is AtlasTexture atlas)
                {
                    emuImg.SourceTexture = atlas.Atlas;
                    emuImg.SourceRegion = atlas.Region;
                }
                else
                {
                    emuImg.SourceTexture = texture;
                }
                emuImg.DrawOffset = new Vector2(0, 0);
                emuImg.Position = GetHtmlImagePosition(cip, relX);
                emuImg.Size = new Vector2(w, imgH);
                emuImg.FlipX = cip.FlipX;
                emuImg.FlipY = cip.FlipY;
                emuImg.SetColorMatrix(cip.ColorMatrix);
                // Inline images are absolutely positioned inside a fixed-height Emuera line.
                // Giving them a minimum size lets Godot containers add blank vertical space.
                emuImg.CustomMinimumSize = Vector2.Zero;
                container.AddChild(emuImg);
                return EffectiveLineHeight;
            }
            else
            {
                var spacer = new Control();
                spacer.MouseFilter = MouseFilterEnum.Ignore;
                float placeholderWidth = System.Math.Max(cip.Width, EffectiveLineHeight);
                SetFixedControlSize(spacer, new Vector2(placeholderWidth, EffectiveLineHeight));
                spacer.Position = new Vector2(cip.PointX - relX, 0);
                container.AddChild(spacer);
                return EffectiveLineHeight;
            }
        }
        else if(part is ConsoleShapePart csp)
        {
            if (csp is ConsoleRectangleShapePart rectShape)
            {
                var colorRect = new ColorRect();
                colorRect.MouseFilter = MouseFilterEnum.Ignore;
                colorRect.CustomMinimumSize = new Vector2(rectShape.Width, rectShape.Bottom - rectShape.Top);
                var sc = rectShape.pColor;
                colorRect.Color = new Godot.Color(sc.r, sc.g, sc.b, sc.a);
                colorRect.Position = new Vector2(rectShape.PointX - relX, rectShape.Top);
                container.AddChild(colorRect);
                return rectShape.Bottom;
            }
            else if (csp is ConsoleSpacePart)
            {
                var spacer = new Control();
                spacer.MouseFilter = MouseFilterEnum.Ignore;
                spacer.CustomMinimumSize = new Vector2(csp.Width, FontSize);
                spacer.Position = new Vector2(csp.PointX - relX, 0);
                container.AddChild(spacer);
                return EffectiveLineHeight;
            }
            else if (csp is ConsoleErrorShapePart errShape)
            {
                float labelWidth = System.Math.Max(csp.Width, EffectiveLineHeight);
                var text = CreateTextPart(errShape.AltText ?? errShape.Str ?? "", Config.ForeColor, Config.Font, labelWidth);
                text.Position = new Vector2(csp.PointX - relX, 0);
                container.AddChild(text);
                return EffectiveLineHeight;
            }
            return EffectiveLineHeight;
        }
        return EffectiveLineHeight;
    }

    int AddDivPartToContainer(ConsoleDivPart div, Control container, int relX)
    {
        var wrapper = BuildDivControl(div, relX);
        container.AddChild(wrapper);
        if (!div.IsRelative)
            return EffectiveLineHeight;
        return System.Math.Max(EffectiveLineHeight, div.Y + div.DivHeight);
    }

    Control BuildDivControl(ConsoleDivPart div, int relX)
    {
        var wrapper = new Control();
        wrapper.MouseFilter = MouseFilterEnum.Pass;
        wrapper.ClipContents = true;
        wrapper.Position = GetHtmlDivPosition(div, relX);
        wrapper.Size = new Vector2(div.DivWidth, div.DivHeight);
        wrapper.CustomMinimumSize = new Vector2(div.DivWidth, div.DivHeight);
        wrapper.ZIndex = div.Depth;

        int[] margin = div.StyledBox?.Margin;
        int[] padding = div.StyledBox?.Padding;
        int[] border = div.StyledBox?.Border;
        int[] borderColor = div.StyledBox?.BorderColor;

        int marginLeft = BoxValue(margin, BoxDirection.Left);
        int marginTop = BoxValue(margin, BoxDirection.Top);
        int marginRight = BoxValue(margin, BoxDirection.Right);
        int marginBottom = BoxValue(margin, BoxDirection.Bottom);
        int borderLeft = BoxValue(border, BoxDirection.Left);
        int borderTop = BoxValue(border, BoxDirection.Top);
        int borderRight = BoxValue(border, BoxDirection.Right);
        int borderBottom = BoxValue(border, BoxDirection.Bottom);
        int paddingLeft = BoxValue(padding, BoxDirection.Left);
        int paddingTop = BoxValue(padding, BoxDirection.Top);
        int paddingRight = BoxValue(padding, BoxDirection.Right);
        int paddingBottom = BoxValue(padding, BoxDirection.Bottom);

        float boxX = marginLeft;
        float boxY = marginTop;
        float boxW = Mathf.Max(0, div.DivWidth - marginLeft - marginRight);
        float boxH = Mathf.Max(0, div.DivHeight - marginTop - marginBottom);

        if (div.BackgroundColor.HasValue && boxW > 0 && boxH > 0)
        {
            var bg = new ColorRect();
            bg.MouseFilter = MouseFilterEnum.Ignore;
            var c = div.BackgroundColor.Value;
            bg.Color = new Godot.Color(c.r, c.g, c.b, c.a);
            bg.Position = new Vector2(boxX, boxY);
            bg.Size = new Vector2(boxW, boxH);
            wrapper.AddChild(bg);
        }

        AddDivBorder(wrapper, border, borderColor, boxX, boxY, boxW, boxH);

        var content = new Control();
        content.MouseFilter = MouseFilterEnum.Pass;
        content.ClipContents = true;
        content.Position = new Vector2(boxX + borderLeft + paddingLeft, boxY + borderTop + paddingTop);
        content.Size = new Vector2(
            Mathf.Max(0, boxW - borderLeft - borderRight - paddingLeft - paddingRight),
            Mathf.Max(0, boxH - borderTop - borderBottom - paddingTop - paddingBottom));
        content.CustomMinimumSize = content.Size;
        wrapper.AddChild(content);

        int y = 0;
        foreach (var childLine in div.Children)
        {
            AddDisplayLineToContainer(childLine, content, y);
            y += EffectiveLineHeight;
        }

        return wrapper;
    }

    int AddDisplayLineToContainer(ConsoleDisplayLine line, Control container, int yOffset)
    {
        if (line == null)
            return 0;
        var row = new Control();
        row.MouseFilter = MouseFilterEnum.Pass;
        row.ClipContents = false;
        row.Position = new Vector2(0, yOffset);

        foreach (var button in line.Buttons)
        {
            if (button.IsButton)
            {
                int buttonTop = GetButtonTop(button);
                int buttonHeight = GetButtonBottom(button) - buttonTop;
                if (buttonHeight <= 0)
                    buttonHeight = EffectiveLineHeight;
                var btn = new Panel();
                btn.FocusMode = FocusModeEnum.None;
                btn.MouseForcePassScrollEvents = false;
                btn.MouseFilter = MouseFilterEnum.Stop;
                btn.ClipContents = false;
                EnsureButtonStyles();
                btn.AddThemeStyleboxOverride("panel", _btnNormalStyle);
                string inputs = button.Inputs;
                long generation = button.Generation;
                btn.GuiInput += inputEvent => OnContentButtonGuiInput(inputEvent, btn, inputs, generation);
                btn.MouseEntered += () => GenericUtils.SetPointingButton(inputs, generation);
                btn.MouseExited += () => GenericUtils.ClearPointingButton(generation);
                btn.SetMeta("generation", generation);

                var contentBox = new Control();
                contentBox.MouseFilter = MouseFilterEnum.Ignore;
                contentBox.ClipContents = false;
                contentBox.Position = new Vector2(0, -buttonTop);
                btn.AddChild(contentBox);

                foreach (var part in button.StrArray)
                    AddPartToContainer(part, contentBox, button.PointX);

                foreach (var child in contentBox.GetChildren())
                {
                    if (child is Control c)
                        c.MouseFilter = MouseFilterEnum.Ignore;
                }

                SetFixedControlSize(contentBox, new Vector2(button.Width, buttonHeight));
                btn.CustomMinimumSize = new Vector2(button.Width, buttonHeight);
                btn.Position = new Vector2(button.PointX, buttonTop);
                btn.Size = new Vector2(button.Width, buttonHeight);
                row.AddChild(btn);
            }
            else
            {
                foreach (var part in button.StrArray)
                    AddPartToContainer(part, row, 0);
            }
        }

        int maxHeight = GetLineBottom(line);
        SetFixedControlSize(row, new Vector2(GetLineRight(line), maxHeight));
        container.AddChild(row);
        return maxHeight;
    }

    static int GetLineRight(ConsoleDisplayLine line)
    {
        int right = 0;
        if (line?.Buttons == null)
            return right;
        foreach (var button in line.Buttons)
        {
            if (button == null)
                continue;
            right = System.Math.Max(right, button.PointX + button.Width);
        }
        return right;
    }

    void AddDivBorder(Control wrapper, int[] border, int[] borderColor, float boxX, float boxY, float boxW, float boxH)
    {
        if (border == null || borderColor == null || boxW <= 0 || boxH <= 0)
            return;
        AddBorderRect(wrapper, boxX, boxY, boxW, BoxValue(border, BoxDirection.Top), ColorValue(borderColor, BoxDirection.Top));
        AddBorderRect(wrapper, boxX + boxW - BoxValue(border, BoxDirection.Right), boxY, BoxValue(border, BoxDirection.Right), boxH, ColorValue(borderColor, BoxDirection.Right));
        AddBorderRect(wrapper, boxX, boxY + boxH - BoxValue(border, BoxDirection.Bottom), boxW, BoxValue(border, BoxDirection.Bottom), ColorValue(borderColor, BoxDirection.Bottom));
        AddBorderRect(wrapper, boxX, boxY, BoxValue(border, BoxDirection.Left), boxH, ColorValue(borderColor, BoxDirection.Left));
    }

    void AddBorderRect(Control wrapper, float x, float y, float w, float h, int color)
    {
        if (w <= 0 || h <= 0 || color < 0)
            return;
        var rect = new ColorRect();
        rect.MouseFilter = MouseFilterEnum.Ignore;
        rect.Color = new Godot.Color(((color >> 16) & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, (color & 0xFF) / 255f, 1f);
        rect.Position = new Vector2(x, y);
        rect.Size = new Vector2(w, h);
        wrapper.AddChild(rect);
    }

    static int BoxValue(int[] values, int index)
    {
        if (values == null || index < 0 || index >= values.Length)
            return 0;
        return values[index];
    }

    static int ColorValue(int[] values, int index)
    {
        if (values == null || index < 0 || index >= values.Length)
            return -1;
        return values[index];
    }

    Vector2 GetHtmlDivPosition(ConsoleDivPart div, int relX)
    {
        switch (div.Display)
        {
            case DisplayMode.Absolute:
            case DisplayMode.AbsoluteLeftBottom:
                return new Vector2(div.X, Config.WindowY - div.Y - div.DivHeight);
            case DisplayMode.AbsoluteLeftTop:
                return new Vector2(div.X, div.Y);
            default:
                return new Vector2(div.PointX - relX + div.X, div.Y);
        }
    }

    Vector2 GetHtmlImagePosition(ConsoleImagePart imagePart, int relX)
    {
        switch (imagePart.Display)
        {
            case DisplayMode.Absolute:
            case DisplayMode.AbsoluteLeftBottom:
                return new Vector2(
                    imagePart.PositionX + imagePart.dest_rect.X,
                    Config.WindowY + imagePart.PositionY);
            case DisplayMode.AbsoluteLeftTop:
                return new Vector2(
                    imagePart.PositionX + imagePart.dest_rect.X,
                    imagePart.PositionY);
            default:
                return new Vector2(
                    imagePart.PointX - relX + imagePart.dest_rect.X,
                    imagePart.dest_rect.Y);
        }
    }

    static bool IsDynamicCutinName(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.StartsWith("CUTIN", StringComparison.OrdinalIgnoreCase) || name.Length == 5)
            return false;
        for (int i = 5; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
                return false;
        }
        return true;
    }

    Texture2D GetSpriteTexture(ASprite sprite)
    {
        if (sprite == null)
            return null;

        if (sprite.Bitmap is uEmuera.Drawing.BitmapTexture bt && bt.texture != null)
        {
            if (sprite is ASpriteSingle single)
            {
                var srcRect = single.SrcRectangle;
                if (srcRect.X == 0 && srcRect.Y == 0 &&
                    srcRect.Width == bt.texture.GetWidth() &&
                    srcRect.Height == bt.texture.GetHeight())
                {
                    return bt.texture;
                }
                var atlas = new AtlasTexture();
                atlas.Atlas = bt.texture;
                atlas.Region = new Rect2(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
                return atlas;
            }
            return bt.texture;
        }

        if (sprite is ASpriteSingle singleSprite)
        {
            if (singleSprite.BaseImage is GraphicsImage gImg && gImg.godotImage != null)
            {
                var srcRect = singleSprite.SrcRectangle;
                if (srcRect.X == 0 && srcRect.Y == 0 &&
                    srcRect.Width == gImg.godotImage.GetWidth() &&
                    srcRect.Height == gImg.godotImage.GetHeight())
                {
                    return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
                }
                var region = gImg.godotImage.GetRegion(new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));
                if (region != null)
                    return Godot.ImageTexture.CreateFromImage(region);
                return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
            }
            if (singleSprite.BaseImage?.Bitmap != null)
            {
                var bmp = singleSprite.BaseImage.Bitmap;
                var ti = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
                if (ti == null && !string.IsNullOrEmpty(bmp.filename))
                    ti = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
                if (ti != null)
                {
                    var atlas = new AtlasTexture();
                    atlas.Atlas = ti.texture;
                    atlas.Region = new Rect2(singleSprite.SrcRectangle.X, singleSprite.SrcRectangle.Y,
                        singleSprite.SrcRectangle.Width, singleSprite.SrcRectangle.Height);
                    return atlas;
                }
            }
        }

        if (sprite is SpriteAnime anime)
        {
            AbstractImage baseImage;
            uEmuera.Drawing.Rectangle srcRect;
            uEmuera.Drawing.Point offset;
            if (anime.GetCurrentFrameInfo(out baseImage, out srcRect, out offset))
            {
                if (baseImage is GraphicsImage gImg && gImg.godotImage != null)
                {
                    if (srcRect.X == 0 && srcRect.Y == 0 &&
                        srcRect.Width == gImg.godotImage.GetWidth() &&
                        srcRect.Height == gImg.godotImage.GetHeight())
                    {
                        return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
                    }
                    var region = gImg.godotImage.GetRegion(new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));
                    if (region != null)
                        return Godot.ImageTexture.CreateFromImage(region);
                    return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
                }
                if (baseImage?.Bitmap != null)
                {
                    var bmp = baseImage.Bitmap;
                    var ti = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
                    if (ti == null && !string.IsNullOrEmpty(bmp.filename))
                        ti = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
                    if (ti != null)
                    {
                        var atlas = new AtlasTexture();
                        atlas.Atlas = ti.texture;
                        atlas.Region = new Rect2(srcRect.X, srcRect.Y,
                            srcRect.Width, srcRect.Height);
                        return atlas;
                    }
                }
            }
        }

        return null;
    }

    internal ConsoleDisplayLine GetLine(int lineno)
    {
        lineObjects.TryGetValue(lineno, out var line);
        return line;
    }

    public int GetMaxLineNo()
    {
        int max = -1;
        foreach(var k in lineObjects.Keys)
            if(k > max) max = k;
        return max;
    }

    public int GetMinLineNo()
    {
        if (lineObjects.Count == 0)
            return -1;
        int min = int.MaxValue;
        foreach(var k in lineObjects.Keys)
            if(k < min) min = k;
        return min;
    }

    public void RemoveTopLines(int count)
    {
        bool removedAny = false;
        if (count > 0)
            GenericUtils.ClearPointingButton();
        for(int i = 0; i < count && lineContainer.GetChildCount() > 0; i++)
        {
            var child = lineContainer.GetChild(0);
            removedAny = true;
            if (child.HasMeta("line_no"))
            {
                int lineNo = (int)child.GetMeta("line_no");
                lineObjects.Remove(lineNo);
            }
            SafeQueueFree(child);
        }
        if (removedAny)
        {
            displayRevision++;
            RefreshQuickInputGate();
        }
    }

    public void TrimVisibleLinesToLimit()
    {
        if (lineContainer == null)
            return;
        int overflow = lineContainer.GetChildCount() - MaxVisibleLines;
        if (overflow > 0)
            RemoveTopLines(overflow);
    }

    public void RemoveBottomLines(int count)
    {
        bool removedAny = false;
        if (count > 0)
            GenericUtils.ClearPointingButton();
        for(int i = 0; i < count && lineContainer.GetChildCount() > 0; i++)
        {
            var child = lineContainer.GetChild(lineContainer.GetChildCount() - 1);
            removedAny = true;
            if (child.HasMeta("line_no"))
            {
                int lineNo = (int)child.GetMeta("line_no");
                lineObjects.Remove(lineNo);
            }
            SafeQueueFree(child);
        }
        if (removedAny)
        {
            displayRevision++;
            RefreshQuickInputGate();
        }
    }

    static void SafeQueueFree(Node node)
    {
        node.SetProcess(false);
        node.SetPhysicsProcess(false);
        node.SetProcessInput(false);
        node.SetProcessUnhandledInput(false);
        node.SetProcessUnhandledKeyInput(false);
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    public void UpdateDisplay()
    {
        // Layout is handled automatically by Godot containers
    }

    internal void RefreshCBG(List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> list)
    {
        if (cbgContainer == null)
            return;

        if (list == null || list.Count == 0)
        {
            TrimCbgNodes(0);
            return;
        }

        int nodeIndex = 0;
        renderedCbgLayers.Clear();
        int currentScrollY = GetCurrentContentScrollY();
        foreach (var cbg in list)
        {
            if (cbg.zdepth == 0)
                continue;
            if (cbg.Img == null || !cbg.Img.IsCreated)
                continue;

            var texture = GetSpriteTexture(cbg.Img);
            if (texture == null)
                continue;

            var emuImg = GetOrCreateCbgNode(nodeIndex);
            if (texture is AtlasTexture atlas)
            {
                emuImg.SourceTexture = atlas.Atlas;
                emuImg.SourceRegion = atlas.Region;
            }
            else
            {
                emuImg.SourceTexture = texture;
                emuImg.SourceRegion = default;
            }
            emuImg.DrawOffset = new Vector2(0, 0);
            emuImg.Position = GetCbgLayerPosition(cbg, currentScrollY);
            bool flipX = cbg.width < 0;
            bool flipY = cbg.height < 0;
            int w = cbg.width != 0 ? System.Math.Abs(cbg.width) : (cbg.Img.DestBaseSize.Width > 0 ? cbg.Img.DestBaseSize.Width : texture.GetWidth());
            int h = cbg.height != 0 ? System.Math.Abs(cbg.height) : (cbg.Img.DestBaseSize.Height > 0 ? cbg.Img.DestBaseSize.Height : texture.GetHeight());
            emuImg.Size = new Vector2(w, h);
            emuImg.FlipX = flipX;
            emuImg.FlipY = flipY;
            emuImg.Modulate = new Godot.Color(1, 1, 1, cbg.opacity);
            emuImg.SetColorMatrix(cbg.colorMatrix);
            emuImg.Visible = true;
            renderedCbgLayers.Add(cbg);
            nodeIndex++;
        }
        lastCbgScrollVertical = currentScrollY;
        TrimCbgNodes(nodeIndex);
    }

    Vector2 GetCbgLayerPosition(MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage cbg, int currentScrollY)
    {
        int y = cbg.y;
        if (cbg.followScroll)
        {
            if (cbg.initialScrollY == int.MinValue)
                cbg.initialScrollY = currentScrollY;
            y -= currentScrollY - cbg.initialScrollY;
        }
        return new Vector2(cbg.x, y);
    }

    int GetCurrentContentScrollY()
    {
        return scrollContainer != null ? scrollContainer.ScrollVertical : 0;
    }

    void RefreshCbgFollowScrollPositions()
    {
        if (renderedCbgLayers.Count == 0 || cbgNodes.Count == 0)
            return;
        int currentScrollY = GetCurrentContentScrollY();
        if (currentScrollY == lastCbgScrollVertical)
            return;
        int count = System.Math.Min(renderedCbgLayers.Count, cbgNodes.Count);
        for (int i = 0; i < count; i++)
        {
            var cbg = renderedCbgLayers[i];
            if (cbg != null && cbg.followScroll && cbgNodes[i] != null)
                cbgNodes[i].Position = GetCbgLayerPosition(cbg, currentScrollY);
        }
        lastCbgScrollVertical = currentScrollY;
    }

    EmueraImage GetOrCreateCbgNode(int index)
    {
        while (cbgNodes.Count <= index)
        {
            var node = new EmueraImage();
            node.MouseFilter = MouseFilterEnum.Ignore;
            cbgNodes.Add(node);
            cbgContainer.AddChild(node);
        }
        return cbgNodes[index];
    }

    public void PlaySoundFile(string path, bool loop, int channel)
    {
        PlaySoundFile(path, loop ? -1 : 1, channel);
    }

    public void PlaySoundFile(string path, int repeat, int channel)
    {
        bool loop = repeat < 0;
        var stream = LoadAudioStream(path, loop);
        if (stream == null)
        {
            GenericUtils.NotifySoundPlaybackFailed(channel, path);
            return;
        }
        while (soundPlayers.Count <= channel)
        {
            int newChannel = soundPlayers.Count;
            var newPlayer = new AudioStreamPlayer();
            newPlayer.Finished += () => OnSoundPlayerFinished(newChannel);
            soundPlayers.Add(newPlayer);
            soundRepeatRemaining.Add(0);
            AddChild(newPlayer);
        }
        AudioStreamPlayer player = soundPlayers[channel];
        player.Stop();
        player.Stream = stream;
        player.VolumeDb = LinearToDb(soundVolume);
        soundRepeatRemaining[channel] = loop ? -1 : Math.Max(repeat, 1);
        player.Play();
        GenericUtils.NotifySoundPlaybackStarted(channel, path, GetAudioStreamLengthMs(stream));
    }

    void OnSoundPlayerFinished(int channel)
    {
        if (channel < 0 || channel >= soundPlayers.Count || channel >= soundRepeatRemaining.Count)
            return;
        int remaining = soundRepeatRemaining[channel];
        if (remaining < 0)
            return;
        remaining--;
        soundRepeatRemaining[channel] = remaining;
        if (remaining > 0)
            soundPlayers[channel].Play();
    }

    public void StopSounds()
    {
        for (int i = 0; i < soundPlayers.Count; i++)
        {
            soundRepeatRemaining[i] = 0;
            soundPlayers[i].Stop();
        }
    }

    public void StopSoundChannel(int channel)
    {
        if (channel < 0 || channel >= soundPlayers.Count)
            return;
        if (channel < soundRepeatRemaining.Count)
            soundRepeatRemaining[channel] = 0;
        soundPlayers[channel].Stop();
    }

    public void PauseSoundChannel(int channel, bool paused)
    {
        if (channel < 0 || channel >= soundPlayers.Count)
            return;
        soundPlayers[channel].StreamPaused = paused;
    }

    public void SetSoundChannelSpeed(int channel, float speed)
    {
        if (channel < 0 || channel >= soundPlayers.Count)
            return;
        soundPlayers[channel].PitchScale = Mathf.Max(0.01f, speed);
    }

    public void PlayBgmFile(string path)
    {
        var stream = LoadAudioStream(path, true);
        if (stream == null)
        {
            GenericUtils.NotifyBgmPlaybackFailed(path);
            return;
        }
        if (bgmPlayer == null)
        {
            bgmPlayer = new AudioStreamPlayer();
            AddChild(bgmPlayer);
        }
        bgmPlayer.Stop();
        bgmPlayer.Stream = stream;
        bgmPlayer.VolumeDb = LinearToDb(bgmVolume);
        bgmPlayer.Play();
        GenericUtils.NotifyBgmPlaybackStarted(path, GetAudioStreamLengthMs(stream));
    }

    public void StopBgm()
    {
        bgmPlayer?.Stop();
    }

    public void PauseBgm(bool paused)
    {
        if (bgmPlayer == null)
            return;
        bgmPlayer.StreamPaused = paused;
    }

    public void SetBgmSpeed(float speed)
    {
        if (bgmPlayer == null)
            return;
        bgmPlayer.PitchScale = Mathf.Max(0.01f, speed);
    }

    public void SetSoundVolume(int volume)
    {
        soundVolume = NormalizeEraVolume(volume);
        foreach (var player in soundPlayers)
            player.VolumeDb = LinearToDb(soundVolume);
    }

    public void SetBgmVolume(int volume)
    {
        bgmVolume = NormalizeEraVolume(volume);
        if (bgmPlayer != null)
            bgmPlayer.VolumeDb = LinearToDb(bgmVolume);
    }

    AudioStream LoadAudioStream(string path, bool loop)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        path = uEmuera.Utils.ResolveExistingFilePath(path);
        if (!uEmuera.Utils.FileExists(path))
        {
            GenericUtils.Info($"[AUDIO] File not found: {path}");
            return null;
        }
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".wav":
                var wav = AudioStreamWav.LoadFromFile(path);
                if (wav != null)
                    wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                return wav;
            case ".ogg":
                var ogg = AudioStreamOggVorbis.LoadFromFile(path);
                if (ogg != null)
                    ogg.Loop = loop;
                return ogg;
            case ".mp3":
                var mp3 = AudioStreamMP3.LoadFromFile(path);
                if (mp3 != null)
                    mp3.Loop = loop;
                else
                    GenericUtils.Info($"[AUDIO] Failed to load MP3: {path}");
                return mp3;
            default:
                GenericUtils.Info($"[AUDIO] Unsupported audio extension \"{ext}\": {path}");
                return null;
        }
    }

    static long GetAudioStreamLengthMs(AudioStream stream)
    {
        if (stream == null)
            return 0;
        double length = stream.GetLength();
        if (length <= 0 || double.IsNaN(length) || double.IsInfinity(length))
            return 0;
        return (long)(length * 1000.0);
    }

    static float NormalizeEraVolume(int volume)
    {
        if (volume <= 0)
            return 0.0f;
        if (volume >= 100)
            return 1.0f;
        return volume / 100.0f;
    }

    static float LinearToDb(float linear)
    {
        if (linear <= 0.0001f)
            return -80.0f;
        return Mathf.LinearToDb(linear);
    }

    void TrimCbgNodes(int keepCount)
    {
        for (int i = cbgNodes.Count - 1; i >= keepCount; i--)
        {
            var node = cbgNodes[i];
            cbgNodes.RemoveAt(i);
            SafeQueueFree(node);
        }
    }

    public void SetLastButtonGeneration(int generation)
    {
        bool shouldAutoShowQuick = quickAutoHiddenUntilNextButtons
            && generation >= 0
            && generation != quickAutoHiddenGeneration;
        lastButtonGeneration = generation;
        RefreshQuickInputGate();

        if (quickButtons != null && (quickButtons.IsShow || shouldAutoShowQuick))
        {
            if (quickRenderedGeneration == lastButtonGeneration && quickRenderedRevision == displayRevision)
            {
                if (shouldAutoShowQuick)
                {
                    quickAutoHiddenUntilNextButtons = false;
                    quickAutoHiddenGeneration = int.MinValue;
                    quickButtons.ShowPad();
                    UpdateSystemButtonVisuals();
                }
                return;
            }

            quickButtons.Clear();
            quickRenderedGeneration = lastButtonGeneration;
            quickRenderedRevision = displayRevision;

            if (lastButtonGeneration < 0)
                return;

            var lineGroups = new List<(int lineNo, List<(string text, Godot.Color color, string code)> buttons)>();
            foreach (var kvp in lineObjects)
            {
                var line = kvp.Value;
                var lineButtons = new List<(string text, Godot.Color color, string code)>();
                CollectQuickButtons(line, lineButtons);
                if (lineButtons.Count > 0)
                    lineGroups.Add((kvp.Key, lineButtons));
            }

            lineGroups.Sort((a, b) => a.lineNo.CompareTo(b.lineNo));

            if (lineGroups.Count == 0)
                return;

            if (shouldAutoShowQuick)
            {
                quickAutoHiddenUntilNextButtons = false;
                quickAutoHiddenGeneration = int.MinValue;
                quickButtons.ShowPad();
                quickButtons.SetInputEnabled(true);
                UpdateSystemButtonVisuals();
            }

            for (int i = 0; i < lineGroups.Count; i++)
            {
                foreach (var btn in lineGroups[i].buttons)
                {
                    quickButtons.AddButton(btn.text, btn.color, btn.code, lastButtonGeneration);
                }
                if (i < lineGroups.Count - 1)
                    quickButtons.ShiftLine();
            }
        }
    }

    void CollectQuickButtons(ConsoleDisplayLine line, List<(string text, Godot.Color color, string code)> output)
    {
        if (line?.Buttons == null || output == null)
            return;
        foreach (var btn in line.Buttons)
        {
            if (btn.IsButton && btn.Generation == lastButtonGeneration)
            {
                string text = btn.ToString().Trim();
                if (string.IsNullOrEmpty(text))
                    text = btn.Title ?? "";
                output.Add((text, GetQuickButtonColor(btn), btn.Inputs));
            }
            foreach (var part in btn.StrArray)
            {
                if (part is ConsoleDivPart div && div.Children != null)
                {
                    foreach (var childLine in div.Children)
                        CollectQuickButtons(childLine, output);
                }
            }
        }
    }

    public void SubmitQuickButtonInput(string input, long generation)
    {
        if (quickInputGateActive && quickInputGateGeneration == generation && quickInputGateRevision == displayRevision)
            return;

        if (generation >= lastButtonGeneration)
        {
            quickInputGateActive = true;
            quickInputGateGeneration = generation;
            quickInputGateRevision = displayRevision;
            quickInputGateTick = Time.GetTicksMsec();
            quickAutoHiddenUntilNextButtons = true;
            quickAutoHiddenGeneration = (int)generation;
            quickButtons?.SetInputEnabled(true);
            quickButtons?.HidePad();
            UpdateSystemButtonVisuals();
        }

        OnButtonPressed(input, generation);
    }

    public void RefreshQuickButtonSettings()
    {
        quickButtons?.RefreshSizing();
    }

    void RefreshQuickInputGate()
    {
        if (!quickInputGateActive)
        {
            quickButtons?.SetInputEnabled(true);
            return;
        }

        if (quickInputGateGeneration != lastButtonGeneration || quickInputGateRevision != displayRevision)
        {
            quickInputGateActive = false;
            quickInputGateGeneration = -1;
            quickInputGateRevision = -1;
            quickInputGateTick = 0;
            quickButtons?.SetInputEnabled(true);
        }
    }

    void RestoreQuickInputGate()
    {
        quickInputGateActive = false;
        quickInputGateGeneration = -1;
        quickInputGateRevision = -1;
        quickInputGateTick = 0;
        quickButtons?.SetInputEnabled(true);
    }

    Godot.Color GetQuickButtonColor(ConsoleButtonString button)
    {
        if (button.StrArray != null && button.StrArray.Length > 0)
        {
            if (button.StrArray[button.StrArray.Length - 1] is AConsoleColoredPart coloredPart)
            {
                var c = coloredPart.pColor;
                return new Godot.Color(c.r, c.g, c.b, c.a);
            }
        }
        return new Godot.Color(Config.ForeColor.r, Config.ForeColor.g, Config.ForeColor.b, Config.ForeColor.a);
    }

    public void SetBackgroundColor(uEmuera.Drawing.Color color)
    {
        if (bgRect != null)
            bgRect.Color = new Godot.Color(color.r, color.g, color.b, color.a);
    }

    public void ShowIsInProcess(bool show)
    {
        if (inProcessLabel != null)
            inProcessLabel.Visible = show;
    }

    public void ShowInput(bool show)
    {
        if (inputpad == null)
            return;
        if (show)
        {
            inputpad.ShowPad();
            var console = GlobalStatic.Console;
            if (console != null)
                inputpad.UpdateInputType(console.InputType);
        }
        else
        {
            inputpad.HidePad();
        }
        UpdateSystemButtonVisuals();
    }

    public bool IsInputVisible()
    {
        return inputpad != null && inputpad.IsShow;
    }

    void OnButtonPressed(string input, long generation, bool skip = false)
    {
        if (generation < lastButtonGeneration)
        {
            // Old button clicked - send empty input (acts as skip/advance)
            EmueraThread.instance.Input("", false, skip);
            return;
        }
        EmueraThread.instance.Input(input, true, skip);
    }

    void OnBackPressed()
    {
        if (EmueraThread.instance.Running())
        {
            ShowMessageBox(
                MultiLanguage.Get("[Wait]", "Wait"),
                MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
            return;
        }
        ShowConfirmDialog(
            MultiLanguage.Get("[BackMenu]", "Back to Menu"),
            MultiLanguage.Get("[BackMenuContent]", "Return to menu?"),
            () =>
            {
                EmueraThread.instance.End();
                GetTree().ChangeSceneToFile("res://first_window.tscn");
            });
    }

    void OnRestartPressed()
    {
        if (EmueraThread.instance.Running())
        {
            ShowMessageBox(
                MultiLanguage.Get("[Wait]", "Wait"),
                MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
            return;
        }
        ShowConfirmDialog(
            MultiLanguage.Get("[ReloadGame]", "Reload Game"),
            MultiLanguage.Get("[ReloadGameContent]", "Reload the game?"),
            () =>
            {
                EmueraThread.instance.End();
                CallDeferred(nameof(RestartScene));
            });
    }

    void RestartScene()
    {
        GetTree().ReloadCurrentScene();
    }

    public void RequestRestartFromErb()
    {
        EmueraThread.instance.End();
        CallDeferred(nameof(RestartScene));
    }

    void OnOptionsPressed()
    {
        optionWindow?.ShowPopup();
    }

    void OnInputTogglePressed()
    {
        if (inputpad.IsShow)
        {
            inputpad.HidePad();
        }
        else
        {
            quickButtons?.HidePad();
            scalepad?.HidePad();
            inputpad.ShowPad();
        }
        UpdateSystemButtonVisuals();
    }

    void OnQuickTogglePressed()
    {
        if (quickButtons.IsShow)
        {
            quickAutoHiddenUntilNextButtons = false;
            quickAutoHiddenGeneration = int.MinValue;
            quickButtons.HidePad();
        }
        else
        {
            quickAutoHiddenUntilNextButtons = false;
            quickAutoHiddenGeneration = int.MinValue;
            inputpad?.HidePad();
            scalepad?.HidePad();
            quickButtons.ShowPad();
            SetLastButtonGeneration(lastButtonGeneration);
        }
        UpdateSystemButtonVisuals();
    }

    void OnAutoSkipTogglePressed()
    {
        autoClickSkipEnabled = !autoClickSkipEnabled;
        lastAutoClickSkipTick = 0;
        UpdateSystemButtonVisuals();
    }

    public void ShowMessageBox(string title, string message)
    {
        msgBoxTitle.Text = title;
        msgBoxMessage.Text = message;
        msgBoxConfirmCallback = null;
        msgBoxCancelCallback = null;
        msgBoxCancelBtn.Visible = false;
        msgBox.PopupCentered();
    }

    public void ShowConfirmDialog(string title, string message, System.Action onConfirm, System.Action onCancel = null)
    {
        msgBoxTitle.Text = title;
        msgBoxMessage.Text = message;
        msgBoxConfirmCallback = onConfirm;
        msgBoxCancelCallback = onCancel;
        msgBoxCancelBtn.Visible = true;
        msgBox.PopupCentered();
    }

    void OnMsgConfirm()
    {
        msgBox.Hide();
        msgBoxConfirmCallback?.Invoke();
        msgBoxConfirmCallback = null;
        msgBoxCancelCallback = null;
    }

    void OnMsgCancel()
    {
        msgBox.Hide();
        msgBoxCancelCallback?.Invoke();
        msgBoxConfirmCallback = null;
        msgBoxCancelCallback = null;
    }

    void OnSaveLogPressed()
    {
        var path = MinorShift.Emuera.Program.ExeDir;
        var time = System.DateTime.Now;
        string fname = time.ToString("yyyyMMdd-HHmmss");
        path = System.IO.Path.Combine(path, fname + ".log");
        bool result = false;
        var console = GlobalStatic.Console;
        if (console != null)
            result = console.OutputLog(path);

        ShowMessageBox(
            MultiLanguage.Get("[SaveLog]", "Save Log"),
            result ? $"{MultiLanguage.Get("[SavePath]", "Path")}:\n{path}" : MultiLanguage.Get("[Failure]", "Failure"));
    }

    void OnGotoTitlePressed()
    {
        if (EmueraThread.instance.Running())
        {
            ShowMessageBox(
                MultiLanguage.Get("[Wait]", "Wait"),
                MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
            return;
        }
        ShowConfirmDialog(
            MultiLanguage.Get("[BackTitle]", "Back to Title"),
            MultiLanguage.Get("[BackTitleContent]", "Return to title screen?"),
            () =>
            {
                GlobalStatic.Console?.GotoTitle();
            });
    }

    void OnExitPressed()
    {
        if (EmueraThread.instance.Running())
        {
            ShowMessageBox(
                MultiLanguage.Get("[Wait]", "Wait"),
                MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
            return;
        }
        ShowConfirmDialog(
            MultiLanguage.Get("[Exit]", "Exit"),
            MultiLanguage.Get("[ExitContent]", "Exit the game?"),
            () =>
            {
                GetTree().Quit();
            });
    }

    void OnScaleTogglePressed()
    {
        if (scalepad.IsShow)
        {
            scalepad.HidePad();
        }
        else
        {
            inputpad?.HidePad();
            quickButtons?.HidePad();
            scalepad.ShowPad();
        }
        UpdateSystemButtonVisuals();
    }

    void UpdateSystemButtonVisuals()
    {
        SetSystemButtonActive(inputMenuButton, inputpad != null && inputpad.IsShow);
        SetSystemButtonActive(quickMenuButton, quickButtons != null && quickButtons.IsShow);
        SetSystemButtonActive(autoSkipMenuButton, autoClickSkipEnabled);
        SetSystemButtonActive(scaleMenuButton, scalepad != null && scalepad.IsShow);
    }

    static void SetSystemButtonActive(TextureButton button, bool active)
    {
        if (button == null)
            return;
        button.SelfModulate = active ? ActiveSystemButtonColor : NormalSystemButtonColor;
    }

    void OnMenuTogglePressed()
    {
        menuExpanded = !menuExpanded;
        if (menuExpandedBar != null && menuExpandedBar.GetParent() is Control panel)
            panel.Visible = menuExpanded;
    }

    void OnViewportSizeChanged()
    {
        Size = GetViewportRect().Size;
        ContentWidth = (int)Size.X;
        if (ContentWidth > Config.WindowX)
            Config.UpdateWindowWidth(ContentWidth);
        QueueScaleBoundsUpdate();
    }

    public void SetContentScale(float scale)
    {
        contentScale = Mathf.Clamp(scale, 0.5f, 3.0f);
        var scaleVector = new Vector2(contentScale, contentScale);
        if (scrollContainer != null)
        {
            scrollContainer.HorizontalScrollMode = contentScale > 1.01f
                ? ScrollContainer.ScrollMode.Auto
                : ScrollContainer.ScrollMode.Disabled;
            if (contentScale <= 1.01f)
                scrollContainer.ScrollHorizontal = 0;
        }
        if (lineContainer != null)
            lineContainer.Scale = scaleVector;
        if (cbgContainer != null)
            cbgContainer.Scale = scaleVector;
        QueueScaleBoundsUpdate();
        // Force scroll container to recalculate its scroll area on next frame after layout
        if (!pendingScroll)
        {
            pendingScroll = true;
            CallDeferred(nameof(DeferredScrollToBottom), contentScrollInteractionSerial);
        }
    }

    public void RefreshFontSize()
    {
        int size = FontSize;
// Update all existing lines
        foreach (var node in lineContainer.GetChildren())
        {
            if (node is Control lineCtrl)
            {
                foreach (var child in lineCtrl.GetChildren())
                {
                    if (child is Control ctrl)
                    {
                        ctrl.AddThemeFontSizeOverride("font_size", size);
                    }
                }
            }
        }
        // Update auxiliary UI
        inputpad?.ApplyFont(mainFont, size);
        quickButtons?.ApplyFont(mainFont, size);
        scalepad?.ApplyFont(mainFont, size);
        inProcessLabel?.AddThemeFontSizeOverride("font_size", size);
        ApplyFont(msgBoxTitle);
        ApplyFont(msgBoxMessage);
        ApplyFont(msgBoxConfirmBtn);
        ApplyFont(msgBoxCancelBtn);
    }

    public override void _Process(double delta)
    {
        ProcessContentInertia((float)delta);
        PublishAudioPlaybackPositions();
        RefreshCbgFollowScrollPositions();
        RefreshCbgAnimationPauseState();
        RefreshQuickInputGate();
        if (quickInputGateActive && Time.GetTicksMsec() - quickInputGateTick >= QuickInputGateFallbackMs && !EmueraThread.instance.Running())
        {
            RestoreQuickInputGate();
        }

        if (!autoClickSkipEnabled)
            return;
        var console = GlobalStatic.Console;
        if (console == null || (!console.IsWaitingEnterKey && !console.IsWaitAnyKey))
            return;
        ulong now = Time.GetTicksMsec();
        if (now - lastAutoClickSkipTick < 80)
            return;
        lastAutoClickSkipTick = now;
        EmueraThread.instance.Input("", false, true);
    }

    void RefreshCbgAnimationPauseState()
    {
        if (renderedCbgLayers.Count == 0 || cbgNodes.Count == 0 || cbgContainer == null)
            return;
        var viewRect = cbgContainer.GetGlobalRect();
        int count = System.Math.Min(renderedCbgLayers.Count, cbgNodes.Count);
        for (int i = 0; i < count; i++)
        {
            var sprite = renderedCbgLayers[i].Img;
            if (sprite is MinorShift.Emuera.Content.SpriteAnime anime)
            {
                var node = cbgNodes[i];
                if (node == null)
                    continue;
                var nodeRect = node.GetGlobalRect();
                bool visible = nodeRect.Intersects(viewRect);
                if (visible)
                    anime.ResumeAnimation();
                else
                    anime.PauseAnimation();
            }
        }
    }

    void PublishAudioPlaybackPositions()
    {
        if (bgmPlayer != null)
        {
            double total = bgmPlayer.Stream?.GetLength() ?? 0.0;
            GenericUtils.NotifyBgmPlaybackPosition(bgmPlayer.GetPlaybackPosition(), total, bgmPlayer.Playing && !bgmPlayer.StreamPaused);
        }
        for (int i = 0; i < soundPlayers.Count; i++)
        {
            var player = soundPlayers[i];
            if (player == null)
                continue;
            double total = player.Stream?.GetLength() ?? 0.0;
            GenericUtils.NotifySoundPlaybackPosition(i, player.GetPlaybackPosition(), total, player.Playing && !player.StreamPaused);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (contentDragActive)
            HandleContentPointerInput(@event, false);
    }

    public override void _GuiInput(InputEvent @event)
    {
        HandleContentPointerInput(@event, true);
    }

    void OnContentGuiInput(InputEvent @event)
    {
        HandleContentPointerInput(@event, true);
    }

    void OnContentButtonGuiInput(InputEvent @event, Control btn, string input, long generation)
    {
        HandleContentPointerInput(@event, true, btn, input, generation);
    }

    bool HandleContentPointerInput(InputEvent @event, bool acceptEvent, Control button = null, string input = null, long generation = 0)
    {
        if (!TryGetPointer(@event, out var pointerPosition, out var pressed, out var released, out var motion))
            return false;

        UpdatePointerPosition(pointerPosition);

        if (scrollContainer == null || (!contentDragActive && !scrollContainer.GetGlobalRect().HasPoint(pointerPosition)))
            return false;

        if (pressed && contentDragActive && button == null)
        {
            if (acceptEvent)
                AcceptEvent();
            else
                GetViewport().SetInputAsHandled();
            return true;
        }

        if (pressed)
        {
            MinorShift._Library.WinInput.PulseVirtualKey(0x01);
            StopContentInertia();
            contentDragActive = true;
            contentDragMoved = false;
            contentDragStartedOnButton = button != null;
            contentDragButton = button;
            contentDragButtonInput = input;
            contentDragButtonGeneration = generation;
            contentDragStartPosition = pointerPosition;
            contentDragLastPosition = pointerPosition;
            contentLastDragTick = Time.GetTicksMsec();
            if (contentDragStartedOnButton)
            {
                if (acceptEvent)
                    AcceptEvent();
                else
                    GetViewport().SetInputAsHandled();
                return true;
            }
            return false;
        }

        if (!contentDragActive)
            return false;

        if (motion)
        {
            var totalDelta = pointerPosition - contentDragStartPosition;
            if (!contentDragMoved && totalDelta.Length() >= ScrollDragThreshold)
            {
                contentDragMoved = true;
                contentScrollInteractionSerial++;
            }
            if (contentDragMoved)
            {
                var rawScrollDelta = contentDragLastPosition - pointerPosition;
                var appliedDelta = ScrollContentBy(rawScrollDelta);
                UpdateContentScrollVelocity(rawScrollDelta, appliedDelta);
                contentDragLastPosition = pointerPosition;
                if (acceptEvent)
                    AcceptEvent();
                else
                    GetViewport().SetInputAsHandled();
                return true;
            }
            contentDragLastPosition = pointerPosition;
            return false;
        }

        if (!released)
            return false;

        bool handled = false;
        bool restoreQuickInputGate = false;
        string pressedButtonInput = null;
        long pressedButtonGeneration = 0;
        bool advanceTap = false;
        if (contentDragMoved)
        {
            StartContentInertia();
            handled = true;
        }
        else if (contentDragStartedOnButton)
        {
            pressedButtonInput = contentDragButtonInput;
            pressedButtonGeneration = contentDragButtonGeneration;
            restoreQuickInputGate = true;
            handled = true;
        }
        else if (!contentDragStartedOnButton)
        {
            var console = GlobalStatic.Console;
            advanceTap = console != null && (console.IsWaitingEnterKey || console.IsWaitAnyKey);
            handled = advanceTap;
            restoreQuickInputGate = advanceTap;
        }

        ResetContentDragState();
        if (pressedButtonInput != null)
            OnButtonPressed(pressedButtonInput, pressedButtonGeneration);
        else if (advanceTap)
            TryAdvanceTap(acceptEvent);
        if (restoreQuickInputGate)
            RestoreQuickInputGate();
        if (handled)
        {
            if (acceptEvent)
                AcceptEvent();
            else
                GetViewport().SetInputAsHandled();
        }
        return handled;
    }

    void UpdatePointerPosition(Vector2 globalPosition)
    {
        if (scrollContainer == null)
        {
            GenericUtils.SetPointerPosition(globalPosition.X, globalPosition.Y);
            return;
        }

        var rect = scrollContainer.GetGlobalRect();
        var contentPosition = globalPosition - rect.Position;
        contentPosition.X += scrollContainer.ScrollHorizontal;
        contentPosition.Y += scrollContainer.ScrollVertical;
        if (contentScale > 0.001f)
            contentPosition /= contentScale;
        GenericUtils.SetPointerPosition(contentPosition.X, contentPosition.Y);
    }

    Vector2 ScrollContentBy(Vector2 delta)
    {
        if (scrollContainer == null)
            return Vector2.Zero;
        delta *= ContentDragSensitivity;
        return ApplyContentScrollDelta(delta);
    }

    Vector2 ApplyContentScrollDelta(Vector2 delta)
    {
        if (scrollContainer == null)
            return Vector2.Zero;

        int oldHorizontal = scrollContainer.ScrollHorizontal;
        int oldVertical = scrollContainer.ScrollVertical;
        int nextHorizontal = Mathf.Clamp(oldHorizontal + Mathf.RoundToInt(delta.X), 0, GetMaxContentHorizontalScroll());
        int nextVertical = Mathf.Clamp(oldVertical + Mathf.RoundToInt(delta.Y), 0, GetMaxContentVerticalScroll());
        scrollContainer.ScrollHorizontal = nextHorizontal;
        scrollContainer.ScrollVertical = nextVertical;
        return new Vector2(nextHorizontal - oldHorizontal, nextVertical - oldVertical);
    }

    void UpdateContentScrollVelocity(Vector2 rawScrollDelta, Vector2 appliedDelta)
    {
        ulong now = Time.GetTicksMsec();
        if (contentLastDragTick == 0)
        {
            contentLastDragTick = now;
            return;
        }

        if (rawScrollDelta.LengthSquared() <= 0.01f)
            return;

        float elapsed = Mathf.Max((now - contentLastDragTick) / 1000.0f, 1.0f / 120.0f);
        if (appliedDelta.LengthSquared() <= 0.01f)
        {
            contentScrollVelocity = Vector2.Zero;
            contentLastDragTick = now;
            return;
        }

        var instantVelocity = rawScrollDelta * ContentDragSensitivity / elapsed;
        if (instantVelocity.Length() > ContentInertiaMaxVelocity)
            instantVelocity = instantVelocity.Normalized() * ContentInertiaMaxVelocity;

        contentScrollVelocity = contentScrollVelocity.Lerp(instantVelocity, 0.78f);
        contentLastDragTick = now;
    }

    void StartContentInertia()
    {
        float releaseSpeed = contentScrollVelocity.Length();
        float fastRatio = Mathf.Clamp(
            (releaseSpeed - ContentInertiaMinVelocity) / (ContentInertiaFastVelocity - ContentInertiaMinVelocity),
            0.0f,
            1.0f);
        float releaseBoost = Mathf.Lerp(ContentInertiaMinReleaseBoost, ContentInertiaMaxReleaseBoost, fastRatio);
        contentInertiaDeceleration = Mathf.Lerp(ContentInertiaSlowDeceleration, ContentInertiaFastDeceleration, fastRatio);
        contentScrollVelocity *= releaseBoost;
        if (contentScrollVelocity.Length() > ContentInertiaMaxVelocity)
            contentScrollVelocity = contentScrollVelocity.Normalized() * ContentInertiaMaxVelocity;
        if (contentScrollVelocity.Length() >= ContentInertiaMinVelocity)
            contentInertiaActive = true;
        else
            StopContentInertia();
    }

    void StopContentInertia()
    {
        contentInertiaActive = false;
        contentScrollVelocity = Vector2.Zero;
        contentInertiaRemainder = Vector2.Zero;
        contentLastDragTick = 0;
    }

    void ProcessContentInertia(float delta)
    {
        if (!contentInertiaActive || contentDragActive || scrollContainer == null)
            return;

        var desiredDelta = contentScrollVelocity * delta + contentInertiaRemainder;
        var roundedDelta = new Vector2(Mathf.Round(desiredDelta.X), Mathf.Round(desiredDelta.Y));
        contentInertiaRemainder = desiredDelta - roundedDelta;
        if (roundedDelta.LengthSquared() > 0.01f)
        {
            var appliedDelta = ApplyContentScrollDelta(roundedDelta);
            if (appliedDelta.LengthSquared() <= 0.01f)
            {
                StopContentInertia();
                return;
            }
        }

        float speed = contentScrollVelocity.Length();
        speed = Mathf.MoveToward(speed, 0, contentInertiaDeceleration * delta);
        if (speed <= ContentInertiaStopVelocity)
        {
            StopContentInertia();
            return;
        }
        contentScrollVelocity = contentScrollVelocity.Normalized() * speed;
    }

    int GetMaxContentHorizontalScroll()
    {
        if (scrollContainer == null)
            return 0;
        var hScroll = scrollContainer.GetHScrollBar();
        return Mathf.RoundToInt(Mathf.Max(0, hScroll.MaxValue));
    }

    int GetMaxContentVerticalScroll()
    {
        if (scrollContainer == null)
            return 0;
        var vScroll = scrollContainer.GetVScrollBar();
        return Mathf.RoundToInt(Mathf.Max(0, vScroll.MaxValue));
    }

    bool TryAdvanceTap(bool acceptEvent)
    {
        var console = GlobalStatic.Console;
        if (console == null || (!console.IsWaitingEnterKey && !console.IsWaitAnyKey))
            return false;

        uint nowTick = MinorShift._Library.WinmmTimer.TickCount;
        bool skipFlag = (nowTick - lastClickTick < 200);
        EmueraThread.instance.Input("", false, skipFlag);
        lastClickTick = nowTick;
        return true;
    }

    void ResetContentDragState()
    {
        contentDragActive = false;
        contentDragMoved = false;
        contentDragStartedOnButton = false;
        contentDragButton = null;
        contentDragButtonInput = null;
        contentDragButtonGeneration = 0;
    }

    static bool TryGetPointer(InputEvent @event, out Vector2 position, out bool pressed, out bool released, out bool motion)
    {
        position = Vector2.Zero;
        pressed = false;
        released = false;
        motion = false;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            position = mb.GlobalPosition;
            pressed = mb.Pressed;
            released = !mb.Pressed;
            return true;
        }
        if (@event is InputEventMouseMotion mm)
        {
            position = mm.GlobalPosition;
            motion = true;
            return true;
        }
        if (@event is InputEventScreenTouch touch)
        {
            position = touch.Position;
            pressed = touch.Pressed;
            released = !touch.Pressed;
            return true;
        }
        if (@event is InputEventScreenDrag drag)
        {
            position = drag.Position;
            motion = true;
            return true;
        }
        return false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (HandleContentPointerInput(@event, false))
            return;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter)
                MinorShift._Library.WinInput.PulseVirtualKey(0x0D);
            else if (keyEvent.Keycode == Key.Escape)
                MinorShift._Library.WinInput.PulseVirtualKey(0x1B);
            if (inputpad != null && inputpad.IsShow)
                return;
            var console = GlobalStatic.Console;
            if (console != null && EmueraThread.instance != null)
            {
                if (console.IsWaitAnyKey)
                {
                    EmueraThread.instance.Input("", false);
                    GetViewport().SetInputAsHandled();
                }
                else if (console.IsWaitingEnterKey && (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter))
                {
                    EmueraThread.instance.Input("", false);
                    GetViewport().SetInputAsHandled();
                }
            }
        }
    }
}
