using Godot;

public partial class Scalepad : Control
{
    PanelContainer panel;
    HBoxContainer hbox;
    HSlider slider;
    Label valueLabel;
    Button fitBtn;

    // Component interface: panels advertise their own show/hide state changes so
    // host scenes can react without polling. Emitted purely additively; callers
    // that never connect are unaffected.
    [Signal]
    public delegate void PadShownEventHandler();

    [Signal]
    public delegate void PadHiddenEventHandler();

    // Exported so the layout metrics are reusable/tunable per scene instance.
    [Export]
    public int PanelHeight = 58;
    [Export]
    public int SideMargin = 10;
    [Export]
    public int BottomMargin = 12;

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 95;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.TopLeft);
        panel.MouseFilter = MouseFilterEnum.Stop;
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
        hbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hbox.SizeFlagsVertical = SizeFlags.ExpandFill;
        hbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(hbox);

        var oneBtn = new Button();
        oneBtn.Text = "1:1";
        oneBtn.CustomMinimumSize = new Vector2(60, 44);
        EmueraContent.StyleButton(oneBtn);
        oneBtn.Pressed += () => SetScale(1.0f);
        hbox.AddChild(oneBtn);

        fitBtn = new Button();
        fitBtn.Text = MultiLanguage.Get("Scalepad.AutoFit", "Fit");
        fitBtn.CustomMinimumSize = new Vector2(60, 44);
        EmueraContent.StyleButton(fitBtn);
        fitBtn.Pressed += OnAutoFit;
        hbox.AddChild(fitBtn);

        slider = new HSlider();
        slider.MinValue = 0.5;
        slider.MaxValue = 3.0;
        slider.Step = 0.1;
        slider.Value = 1.0;
        slider.CustomMinimumSize = new Vector2(0, 44);
        slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        slider.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        slider.ValueChanged += OnSliderChanged;
        hbox.AddChild(slider);

        valueLabel = new Label();
        valueLabel.Text = "1.0x";
        valueLabel.CustomMinimumSize = new Vector2(54, 44);
        valueLabel.VerticalAlignment = VerticalAlignment.Center;
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        hbox.AddChild(valueLabel);

        ApplyPanelLayout();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            ApplyPanelLayout();
    }

    bool isApplyingLayout;
    void ApplyPanelLayout()
    {
        if (panel == null)
            return;
        // 防重入：set_Size 触发 NotificationResized → _Notification → 本方法，需守卫防无限递归。
        if (isApplyingLayout)
            return;
        isApplyingLayout = true;
        try
        {
            var safeRect = EmueraContent.GetSafeViewportRect(GetViewport());
            var viewportSize = safeRect.Size;
            Position = safeRect.Position;
            Size = viewportSize;
            var width = Mathf.Max(1, viewportSize.X - SideMargin * 2);
            panel.Position = new Vector2(SideMargin, Mathf.Max(0, viewportSize.Y - BottomMargin - PanelHeight));
            panel.Size = new Vector2(width, PanelHeight);
            panel.CustomMinimumSize = panel.Size;
        }
        finally
        {
            isApplyingLayout = false;
        }
    }

    void OnSliderChanged(double value)
    {
        SetScale((float)value);
    }

    void OnAutoFit()
    {
        int safeWidth = EmueraContent.ContentSafeWidth > 0
            ? EmueraContent.ContentSafeWidth
            : DisplayServer.WindowGetSize().X;
        float visualWidth = EmueraContent.instance?.GetCurrentVisualContentWidth() ?? 0.0f;
        float drawableWidth = MinorShift.Emuera.Config.DrawableWidth + 3;
        // eraFL 右侧信息窗等相对 div 可能伸出配置宽度，Fit 应按真实可视宽度缩放。
        float fitWidth = Mathf.Max(drawableWidth, visualWidth);
        float scale;
        if (fitWidth > 0)
            scale = safeWidth / fitWidth;
        else
            scale = 1.0f;
        if (scale < 0.5f) scale = 0.5f;
        if (scale > 3.0f) scale = 3.0f;
        SetScale(scale);
    }

    public void SetScale(float scale)
    {
        SyncScale(scale);
        EmueraContent.instance?.SetContentScale(scale);
    }

    public void SyncScale(float scale)
    {
        if (slider != null)
        {
            slider.SetBlockSignals(true);
            slider.Value = scale;
            slider.SetBlockSignals(false);
        }
        if (valueLabel != null)
            valueLabel.Text = string.Format("{0:F1}x", scale);
    }

    // Re-read cached MultiLanguage texts after a language change. Idempotent.
    public void RefreshUiTexts()
    {
        if (fitBtn != null)
            fitBtn.Text = MultiLanguage.Get("Scalepad.AutoFit", "Fit");
    }

    public void ShowPad()
    {
        ApplyPanelLayout();
        Visible = true;
        GetParent()?.MoveChild(this, GetParent().GetChildCount() - 1);
        EmitSignal(SignalName.PadShown);
    }

    public void HidePad()
    {
        Visible = false;
        EmitSignal(SignalName.PadHidden);
    }

    public void ApplyFont(Font font, int fontSize)
    {
        if (hbox == null)
            return;
        foreach (var c in hbox.GetChildren())
        {
            if (c is Control ctrl)
            {
                if (font != null)
                    ctrl.AddThemeFontOverride("font", font);
                ctrl.AddThemeFontSizeOverride("font_size", fontSize);
            }
        }
    }

    public bool IsShow => Visible;

    public void RefreshSafeAreaLayout()
    {
        ApplyPanelLayout();
    }
}
