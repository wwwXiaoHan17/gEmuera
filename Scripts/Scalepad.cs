using Godot;

public partial class Scalepad : Control
{
    HSlider slider;
    Label valueLabel;

    public override void _Ready()
    {
        Visible = false;
        CustomMinimumSize = new Vector2(0, 48);

        var hbox = new HBoxContainer();
        hbox.AnchorRight = 1;
        hbox.AnchorBottom = 1;
        hbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        hbox.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(hbox);

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
        hbox.AddChild(valueLabel);
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
        Visible = true;
    }

    public void HidePad()
    {
        Visible = false;
    }

    public void ApplyFont(FontFile font, int fontSize)
    {
        // Apply font to existing buttons if needed
        // Since buttons are created in _Ready, we iterate children
        foreach (var child in GetChildren())
        {
            if (child is HBoxContainer hbox)
            {
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
        }
    }

    public bool IsShow => Visible;
}
