using Godot;

public partial class Scalepad : Control
{
    PanelContainer panel;
    HBoxContainer hbox;
    HSlider slider;
    Label valueLabel;
    const int PanelHeight = 58;
    const int SideMargin = 10;
    const int BottomMargin = 12;

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

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.03f, 0.03f, 0.035f, 0.88f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 6;
        style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        style.ContentMarginTop = style.ContentMarginBottom = 7;
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

        var fitBtn = new Button();
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

    void OnSliderChanged(double value)
    {
        SetScale((float)value);
    }

    void OnAutoFit()
    {
        var windowSize = DisplayServer.WindowGetSize();
        int drawableWidth = MinorShift.Emuera.Config.DrawableWidth + 3;
        float scale;
        if (drawableWidth > 0)
            scale = windowSize.X / (float)drawableWidth;
        else
            scale = 1.0f;
        if (scale < 0.5f) scale = 0.5f;
        if (scale > 3.0f) scale = 3.0f;
        SetScale(scale);
    }

    public void SetScale(float scale)
    {
        if (slider != null)
        {
            slider.SetBlockSignals(true);
            slider.Value = scale;
            slider.SetBlockSignals(false);
        }
        if (valueLabel != null)
            valueLabel.Text = string.Format("{0:F1}x", scale);
        EmueraContent.instance?.SetContentScale(scale);
    }

    public void ShowPad()
    {
        ApplyPanelLayout();
        Visible = true;
        GetParent()?.MoveChild(this, GetParent().GetChildCount() - 1);
    }

    public void HidePad()
    {
        Visible = false;
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
}
