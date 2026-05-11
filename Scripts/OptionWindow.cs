using Godot;
using System.Collections.Generic;

public partial class OptionWindow : Control
{
    PopupPanel popup;
    Slider fontSizeSlider;
    Label fontSizeLabel;
    OptionButton resolutionOption;
    OptionButton languageOption;

    static readonly string[] languages = new string[] { "default", "zh_cn", "en_us", "jp" };
    static readonly string[] languageNames = new string[] { "Default", "简体中文", "English", "日本語" };

    public override void _Ready()
    {
        popup = new PopupPanel();
        popup.Size = new Vector2I(400, 300);
        AddChild(popup);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
        popup.AddChild(vbox);

        // Font size
        var fontHBox = new HBoxContainer();
        vbox.AddChild(fontHBox);
        var fontLabel = new Label();
        fontLabel.Text = MultiLanguage.Get("OptionWindow.FontSize", "Font Size");
        fontHBox.AddChild(fontLabel);
        fontSizeSlider = new HSlider();
        fontSizeSlider.MinValue = 8;
        fontSizeSlider.MaxValue = 48;
        fontSizeSlider.Value = MinorShift.Emuera.Config.FontSize > 0 ? MinorShift.Emuera.Config.FontSize : 18;
        fontSizeSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fontSizeSlider.CustomMinimumSize = new Vector2(120, 44);
        fontSizeSlider.ValueChanged += OnFontSizeChanged;
        fontHBox.AddChild(fontSizeSlider);
        fontSizeLabel = new Label();
        fontSizeLabel.Text = fontSizeSlider.Value.ToString();
        fontHBox.AddChild(fontSizeLabel);

        // Resolution
        var resHBox = new HBoxContainer();
        vbox.AddChild(resHBox);
        var resLabel = new Label();
        resLabel.Text = MultiLanguage.Get("OptionWindow.Resolution", "Resolution");
        resHBox.AddChild(resLabel);
        resolutionOption = new OptionButton();
        for (int i = 0; i < ResolutionHelper.resolutions.Count; i++)
        {
            resolutionOption.AddItem(ResolutionHelper.resolutions[i] + "p", i);
        }
        resolutionOption.ItemSelected += OnResolutionSelected;
        resHBox.AddChild(resolutionOption);

        // Language
        var langHBox = new HBoxContainer();
        vbox.AddChild(langHBox);
        var langLabel = new Label();
        langLabel.Text = MultiLanguage.Get("OptionWindow.Language", "Language");
        langHBox.AddChild(langLabel);
        languageOption = new OptionButton();
        for (int i = 0; i < languages.Length; i++)
        {
            languageOption.AddItem(languageNames[i], i);
        }
        languageOption.ItemSelected += OnLanguageSelected;
        langHBox.AddChild(languageOption);

        // Close button
        var closeBtn = new Button();
        closeBtn.Text = MultiLanguage.Get("OptionWindow.Close", "Close");
        EmueraContent.StyleButton(closeBtn);
        closeBtn.Pressed += () => popup.Hide();
        vbox.AddChild(closeBtn);
    }

    public void ShowPopup()
    {
        popup.PopupCentered();
    }

    void OnFontSizeChanged(double value)
    {
        fontSizeLabel.Text = value.ToString();
        int size = (int)value;
        // Use reflection to set the private static setter on Config.FontSize
        var prop = typeof(MinorShift.Emuera.Config).GetProperty("FontSize",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (prop != null)
        {
            prop.SetValue(null, size);
        }
        EmueraContent.instance?.RefreshFontSize();
    }

    void OnResolutionSelected(long index)
    {
        ResolutionHelper.resolution_index = (int)index;
        ResolutionHelper.Apply();
    }

    void OnLanguageSelected(long index)
    {
        MultiLanguage.Load(languages[index]);
    }
}
