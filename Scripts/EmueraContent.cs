using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;

public partial class EmueraContent : Control
{
    public static EmueraContent instance { get; private set; }

    ScrollContainer scrollContainer;
    VBoxContainer lineContainer;
    HBoxContainer menuBar;
    Inputpad inputpad;
    QuickButtons quickButtons;
    Scalepad scalepad;
    ColorRect bgRect;
    Control cbgContainer;
    OptionWindow optionWindow;

    Dictionary<int, ConsoleDisplayLine> lineObjects = new Dictionary<int, ConsoleDisplayLine>();
    Dictionary<Button, long> buttonGenerations = new Dictionary<Button, long>();
    HashSet<string> failedTextureSearches = new HashSet<string>();

    const int MaxVisibleLines = 1000;
    const int LineTrimBatch = 100;

    FontFile mainFont;
    int lastButtonGeneration = -1;
    uint lastClickTick = 0;
    bool pendingScroll = false;

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

    public static int ContentWidth { get; private set; }

    public override void _Ready()
    {
        instance = this;
        Size = GetViewportRect().Size;
        ContentWidth = (int)Size.X;
        GetViewport().SizeChanged += OnViewportSizeChanged;

        mainFont = ResourceLoader.Load<FontFile>("res://Fonts/MS Gothic.ttf");

        bgRect = new ColorRect();
        bgRect.AnchorLeft = 0;
        bgRect.AnchorTop = 0;
        bgRect.AnchorRight = 1;
        bgRect.AnchorBottom = 1;
        AddChild(bgRect);

        var rootVBox = new VBoxContainer();
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
        AddIconButton("res://Icons/io-input.svg", OnInputTogglePressed);
        AddIconButton("res://Icons/quick.svg", OnQuickTogglePressed);
        AddIconButton("res://Icons/menu_save_log.svg", OnSaveLogPressed);
        AddIconButton("res://Icons/Title.svg", OnGotoTitlePressed);
        AddIconButton("res://Icons/exit.svg", OnExitPressed);
        AddIconButton("res://Icons/Scale.svg", OnScaleTogglePressed);

        // Toggle at the right edge (last child = rightmost in HBox).
        var menuToggleBtn = new TextureButton();
        menuToggleBtn.CustomMinimumSize = new Vector2(36, 36);
        menuToggleBtn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
        menuToggleBtn.MouseFilter = MouseFilterEnum.Stop;
        if (ResourceLoader.Exists("res://Icons/menu.svg"))
            menuToggleBtn.TextureNormal = ResourceLoader.Load<Texture2D>("res://Icons/menu.svg");
        menuToggleBtn.Pressed += OnMenuTogglePressed;
        menuRoot.AddChild(menuToggleBtn);

        // Small top offset so first line of text isn't hidden behind the toggle
        var menuSpacer = new Control();
        menuSpacer.CustomMinimumSize = new Vector2(0, 44);
        rootVBox.AddChild(menuSpacer);

        quickButtons = new QuickButtons();
        quickButtons.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        quickButtons.CustomMinimumSize = new Vector2(0, 120);
        rootVBox.AddChild(quickButtons);

        inputpad = new Inputpad();
        inputpad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rootVBox.AddChild(inputpad);

        scalepad = new Scalepad();
        scalepad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rootVBox.AddChild(scalepad);

        optionWindow = new OptionWindow();
        AddChild(optionWindow);

        inProcessLabel = new Label();
        inProcessLabel.Text = MultiLanguage.Get("EmueraContent.InProcess", "Processing...");
        ApplyFont(inProcessLabel);
        inProcessLabel.HorizontalAlignment = HorizontalAlignment.Center;
        inProcessLabel.Visible = false;
        rootVBox.AddChild(inProcessLabel);

        scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.ClipContents = true;
        rootVBox.AddChild(scrollContainer);

        lineContainer = new VBoxContainer();
        lineContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lineContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
        lineContainer.ClipContents = false;
        lineContainer.AddThemeConstantOverride("separation", 4);
        scrollContainer.AddChild(lineContainer);

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
        foreach(var child in lineContainer.GetChildren())
            SafeQueueFree(child);
        lineObjects.Clear();
        buttonGenerations.Clear();
        failedTextureSearches.Clear();
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

    void AddIconButton(string iconPath, System.Action callback)
    {
        var btn = new TextureButton();
        btn.CustomMinimumSize = new Vector2(36, 36);
        btn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
        btn.MouseFilter = MouseFilterEnum.Stop;
        if (ResourceLoader.Exists(iconPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(iconPath);
            btn.TextureNormal = tex;
        }
        btn.Pressed += callback;
        menuBar.AddChild(btn);
    }

    internal void AddLine(ConsoleDisplayLine line, bool isUpdate)
    {
        var lineControl = new Control();
        lineControl.MouseFilter = MouseFilterEnum.Pass;
        lineControl.ClipContents = false;
        int maxLineRight = 0;

        foreach(var button in line.Buttons)
        {
            if(button.IsButton)
            {
                var btn = new Button();
                btn.Text = "";
                ApplyFont(btn);
                StyleButton(btn);
                string inputs = button.Inputs;
                long generation = button.Generation;
                btn.Pressed += () => OnButtonPressed(inputs, generation);
                btn.SetMeta("generation", generation);
                buttonGenerations[btn] = generation;

                var contentBox = new Control();
                contentBox.MouseFilter = MouseFilterEnum.Ignore;
                contentBox.ClipContents = false;
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

                contentBox.CustomMinimumSize = new Vector2(button.Width, EffectiveLineHeight);
                btn.CustomMinimumSize = new Vector2(button.Width, EffectiveLineHeight);
                btn.Position = new Vector2(button.PointX, 0);
                btn.Size = new Vector2(button.Width, EffectiveLineHeight);
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

        // Fixed line height matching original Emuera behavior:
        // every line is exactly EffectiveLineHeight tall, images overflow via negative Y offset
        int lineHeight = EffectiveLineHeight;
        if (lineControl.GetChildCount() == 0)
            lineHeight = 0;

        lineControl.CustomMinimumSize = new Vector2(0, lineHeight);

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
                    // Remove old buttons from tracking
                    if (child is Control oldCtrl)
                    {
                        foreach (var grandchild in oldCtrl.GetChildren())
                        {
                            if (grandchild is Button oldBtn)
                                buttonGenerations.Remove(oldBtn);
                        }
                    }
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

        // Enforce node cap to prevent unbounded memory growth
        if (lineContainer.GetChildCount() > MaxVisibleLines)
            RemoveTopLines(LineTrimBatch);

        // Auto-scroll to bottom on next frame after layout stabilizes
        if (!pendingScroll)
        {
            pendingScroll = true;
            CallDeferred(nameof(DeferredScrollToBottom));
        }
    }

    async void DeferredScrollToBottom()
    {
        pendingScroll = false;
        if (scrollContainer != null)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scrollContainer.ScrollVertical = (int)scrollContainer.GetVScrollBar().MaxValue;
        }
    }

    int AddPartToContainer(AConsoleDisplayPart part, Control container, int relX)
    {
        if(part is ConsoleStyledString css)
        {
            if (string.IsNullOrEmpty(css.Str))
                return EffectiveLineHeight;
            var label = new Label();
            label.Text = css.Str;
            ApplyFont(label);
            label.AddThemeColorOverride("font_color",
                new Godot.Color(css.pColor.r, css.pColor.g, css.pColor.b, css.pColor.a));
            label.VerticalAlignment = VerticalAlignment.Center;
            label.ClipText = true;
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            container.AddChild(label);
            float posX = css.PointX - relX;
            label.Position = new Vector2(posX, 0);
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
            label.Size = new Vector2(w, EffectiveLineHeight);

            return EffectiveLineHeight;
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
                emuImg.Position = new Vector2(cip.PointX - relX + cip.dest_rect.X, cip.dest_rect.Y);
                emuImg.Size = new Vector2(w, imgH);
                emuImg.CustomMinimumSize = new Vector2(w, imgH);
                container.AddChild(emuImg);
                return System.Math.Max(EffectiveLineHeight, (cip.dest_rect.Y > 0 ? cip.dest_rect.Y : 0) + imgH);
            }
            else
            {
                var label = new Label();
                label.Text = cip.AltText ?? cip.ResourceName ?? "";
                ApplyFont(label);
                label.Position = new Vector2(cip.PointX - relX, 0);
                container.AddChild(label);
                return EffectiveLineHeight;
            }
        }
        else if(part is ConsoleShapePart csp)
        {
            if (csp is ConsoleRectangleShapePart rectShape)
            {
                var colorRect = new ColorRect();
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
                spacer.CustomMinimumSize = new Vector2(csp.Width, FontSize);
                spacer.Position = new Vector2(csp.PointX - relX, 0);
                container.AddChild(spacer);
                return EffectiveLineHeight;
            }
            else if (csp is ConsoleErrorShapePart errShape)
            {
                var label = new Label();
                label.Text = errShape.AltText ?? errShape.Str ?? "";
                ApplyFont(label);
                label.Position = new Vector2(csp.PointX - relX, 0);
                container.AddChild(label);
                return EffectiveLineHeight;
            }
            return EffectiveLineHeight;
        }
        return EffectiveLineHeight;
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
        for(int i = 0; i < count && lineContainer.GetChildCount() > 0; i++)
        {
            var child = lineContainer.GetChild(0);
            if (child.HasMeta("line_no"))
            {
                int lineNo = (int)child.GetMeta("line_no");
                lineObjects.Remove(lineNo);
            }
            if (child is Control ctrl)
            {
                foreach (var grandchild in ctrl.GetChildren())
                {
                    if (grandchild is Button btn)
                        buttonGenerations.Remove(btn);
                }
            }
            SafeQueueFree(child);
        }
    }

    public void RemoveBottomLines(int count)
    {
        for(int i = 0; i < count && lineContainer.GetChildCount() > 0; i++)
        {
            var child = lineContainer.GetChild(lineContainer.GetChildCount() - 1);
            if (child.HasMeta("line_no"))
            {
                int lineNo = (int)child.GetMeta("line_no");
                lineObjects.Remove(lineNo);
            }
            if (child is Control ctrl)
            {
                foreach (var grandchild in ctrl.GetChildren())
                {
                    if (grandchild is Button btn)
                        buttonGenerations.Remove(btn);
                }
            }
            SafeQueueFree(child);
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

        foreach (var child in cbgContainer.GetChildren())
            SafeQueueFree(child);

        if (list == null || list.Count == 0)
            return;

        foreach (var cbg in list)
        {
            if (cbg.zdepth == 0)
                continue;
            if (cbg.Img == null || !cbg.Img.IsCreated)
                continue;

            var texture = GetSpriteTexture(cbg.Img);
            if (texture == null)
                continue;

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
            emuImg.Position = new Vector2(cbg.x, cbg.y);
            int w = cbg.Img.DestBaseSize.Width > 0 ? cbg.Img.DestBaseSize.Width : texture.GetWidth();
            int h = cbg.Img.DestBaseSize.Height > 0 ? cbg.Img.DestBaseSize.Height : texture.GetHeight();
            emuImg.Size = new Vector2(w, h);
            cbgContainer.AddChild(emuImg);
        }
    }

    public void SetLastButtonGeneration(int generation)
    {
        lastButtonGeneration = generation;

        if (quickButtons != null && quickButtons.IsShow)
        {
            quickButtons.Clear();
            if (lastButtonGeneration < 0)
                return;

            // Scan lineObjects from newest to oldest to populate quick buttons
            // Group buttons by line
            var lineGroups = new List<(int lineNo, List<(string text, Godot.Color color, string code)> buttons)>();
            foreach (var kvp in lineObjects)
            {
                var line = kvp.Value;
                var lineButtons = new List<(string text, Godot.Color color, string code)>();
                foreach (var btn in line.Buttons)
                {
                    if (!btn.IsButton)
                        continue;
                    if (btn.Generation != lastButtonGeneration)
                        continue;
                    string text = btn.Title ?? btn.ToString();
                    Godot.Color color = new Godot.Color(Config.ForeColor.r, Config.ForeColor.g, Config.ForeColor.b, Config.ForeColor.a);
                    lineButtons.Add((text, color, btn.Inputs));
                }
                if (lineButtons.Count > 0)
                    lineGroups.Add((kvp.Key, lineButtons));
            }

            // Sort by line number descending (newest first)
            lineGroups.Sort((a, b) => b.lineNo.CompareTo(a.lineNo));

            foreach (var group in lineGroups)
            {
                foreach (var btn in group.buttons)
                {
                    quickButtons.AddButton(btn.text, btn.color, btn.code);
                }
                quickButtons.ShiftLine();
            }
        }
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
    }

    public bool IsInputVisible()
    {
        return inputpad != null && inputpad.IsShow;
    }

    void OnButtonPressed(string input, long generation)
    {
        if (generation < lastButtonGeneration)
        {
            // Old button clicked - send empty input (acts as skip/advance)
            EmueraThread.instance.Input("", false);
            return;
        }
        EmueraThread.instance.Input(input, true);
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
    }

    void OnQuickTogglePressed()
    {
        if (quickButtons.IsShow)
        {
            quickButtons.HidePad();
        }
        else
        {
            inputpad?.HidePad();
            scalepad?.HidePad();
            quickButtons.ShowPad();
            SetLastButtonGeneration(lastButtonGeneration);
        }
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
    }

    public void SetContentScale(float scale)
    {
        if (lineContainer != null)
            lineContainer.Scale = new Vector2(scale, scale);
        // Force scroll container to recalculate its scroll area on next frame after layout
        if (!pendingScroll)
        {
            pendingScroll = true;
            CallDeferred(nameof(DeferredScrollToBottom));
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

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (scrollContainer != null && scrollContainer.GetGlobalRect().HasPoint(GetGlobalMousePosition()))
            {
                var console = GlobalStatic.Console;
                if (console != null && (console.IsWaitingEnterKey || console.IsWaitAnyKey))
                {
                    uint nowTick = MinorShift._Library.WinmmTimer.TickCount;
                    bool skipFlag = (nowTick - lastClickTick < 200);
                    EmueraThread.instance.Input("", false, skipFlag);
                    lastClickTick = nowTick;
                    AcceptEvent();
                }
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
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
