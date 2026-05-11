using Godot;
using System.Collections.Generic;

/// <summary>
/// Debug panel that shows every image loaded during gameplay.
/// Displays a scrollable grid of thumbnails with names.
/// Toggle with F3 key. Instance via EmueraMain._Ready().
/// </summary>
public partial class SpriteDebugViewer : Control
{
    private Panel _panel;
    private ScrollContainer _scroll;
    private FlowContainer _grid;
    private Label _titleLabel;
    private Label _countLabel;
    private Button _clearBtn;
    private Button _toggleBtn;
    private CheckBox _autoScrollCb;

    private List<Node> _entries = new List<Node>();
    private const int MaxEntries = 100;
    private const int ThumbSize = 160;
    private bool _visible = false;

    // Thread-safe pending queue for cross-thread notifications
    private List<(string name, Image image, string info)> _pendingItems =
        new List<(string name, Image image, string info)>();
    private readonly object _pendingLock = new object();

    public override void _Ready()
    {
        AnchorLeft = 0;
        AnchorTop = 0;
        AnchorRight = 1;
        AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 100;

        SetupUI();
        SpriteDebugNotifier.OnImageLoaded += OnImageLoaded;
        HidePanel();
    }

    void SetupUI()
    {
        // Panel at bottom 45% of screen
        _panel = new Panel();
        _panel.AnchorLeft = 0;
        _panel.AnchorRight = 1;
        _panel.AnchorTop = 0.55f;
        _panel.AnchorBottom = 1;
        _panel.MouseFilter = MouseFilterEnum.Stop;
        _panel.ZIndex = 101;
        var panelStyle = new StyleBoxFlat();
        panelStyle.BgColor = new Color(0.05f, 0.05f, 0.08f, 0.92f);
        panelStyle.SetBorderWidthAll(2);
        panelStyle.BorderColor = new Color(0.3f, 0.5f, 0.8f, 1f);
        panelStyle.SetCornerRadiusAll(6);
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_panel);

        // Margin container for inner padding
        var margin = new MarginContainer();
        margin.AnchorLeft = 0;
        margin.AnchorRight = 1;
        margin.AnchorTop = 0;
        margin.AnchorBottom = 1;
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        margin.AddChild(vbox);

        // Header bar
        var header = new HBoxContainer();
        vbox.AddChild(header);

        _titleLabel = new Label();
        _titleLabel.Text = "Sprite Debug Viewer";
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.85f, 1f));
        _titleLabel.AddThemeFontSizeOverride("font_size", 15);
        header.AddChild(_titleLabel);

        _countLabel = new Label();
        _countLabel.Text = "(0 images)";
        _countLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        _countLabel.AddThemeFontSizeOverride("font_size", 13);
        header.AddChild(_countLabel);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(spacer);

        _autoScrollCb = new CheckBox();
        _autoScrollCb.Text = "Auto-scroll";
        _autoScrollCb.ButtonPressed = true;
        header.AddChild(_autoScrollCb);

        _clearBtn = new Button();
        _clearBtn.Text = "Clear";
        _clearBtn.Pressed += ClearAll;
        header.AddChild(_clearBtn);

        _toggleBtn = new Button();
        _toggleBtn.Text = "Hide (F3)";
        _toggleBtn.Pressed += TogglePanel;
        header.AddChild(_toggleBtn);

        // Scrollable grid
        _scroll = new ScrollContainer();
        _scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        vbox.AddChild(_scroll);

        _grid = new FlowContainer();
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        _scroll.AddChild(_grid);
    }

    public override void _Process(double delta)
    {
        // Process pending items from background threads
        List<(string name, Image image, string info)> batch = null;
        lock (_pendingLock)
        {
            if (_pendingItems.Count > 0)
            {
                batch = _pendingItems;
                _pendingItems = new List<(string name, Image image, string info)>();
            }
        }
        if (batch != null)
        {
            foreach (var item in batch)
                AddEntry(item.name, item.image, item.info);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F3)
            TogglePanel();
    }

    void TogglePanel()
    {
        _visible = !_visible;
        _panel.Visible = _visible;
        _toggleBtn.Text = _visible ? "Hide (F3)" : "Show (F3)";
    }

    void HidePanel()
    {
        _panel.Visible = false;
        _toggleBtn.Text = "Show (F3)";
    }

    void ClearAll()
    {
        foreach (var entry in _entries)
            entry.QueueFree();
        _entries.Clear();
        _countLabel.Text = "(0 images)";
    }

    void OnImageLoaded(string name, Image image, string info)
    {
        lock (_pendingLock)
        {
            _pendingItems.Add((name, image, info));
        }
    }

    void AddEntry(string name, Image image, string info)
    {
        if (_entries.Count == 0)

        if (_entries.Count >= MaxEntries)
        {
            _entries[0].QueueFree();
            _entries.RemoveAt(0);
        }

        var entry = CreateEntry(name, image, info);
        _grid.AddChild(entry);
        _entries.Add(entry);
        _countLabel.Text = $"({_entries.Count} images)";

        if (_autoScrollCb.ButtonPressed && _scroll.GetVScrollBar() != null)
            _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }

    Node CreateEntry(string name, Image image, string info)
    {
        var container = new VBoxContainer();

        var texRect = new TextureRect();
        texRect.CustomMinimumSize = new Vector2(ThumbSize, ThumbSize);
        texRect.ExpandMode = TextureRect.ExpandModeEnum.FitHeightProportional;
        texRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;

        var thumbTex = CreateThumbnailTexture(image);
        texRect.Texture = thumbTex;

        container.AddChild(texRect);

        var nameLabel = new Label();
        string shortName = name.Length > 22 ? name.Substring(0, 20) + ".." : name;
        nameLabel.Text = shortName;
        nameLabel.AddThemeColorOverride("font_color", new Color(0.82f, 0.82f, 0.82f));
        nameLabel.AddThemeFontSizeOverride("font_size", 11);
        nameLabel.TooltipText = $"{name}\n{info}";
        container.AddChild(nameLabel);

        var sizeLabel = new Label();
        sizeLabel.Text = $"{image.GetWidth()}x{image.GetHeight()}";
        sizeLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
        sizeLabel.AddThemeFontSizeOverride("font_size", 10);
        container.AddChild(sizeLabel);

        return container;
    }

    ImageTexture CreateThumbnailTexture(Image src)
    {
        int w = src.GetWidth();
        int h = src.GetHeight();
        if (w <= 0 || h <= 0) return null;

        if (w > ThumbSize || h > ThumbSize)
        {
            float scale = Mathf.Min((float)ThumbSize / w, (float)ThumbSize / h);
            int newW = Mathf.Max(1, (int)(w * scale));
            int newH = Mathf.Max(1, (int)(h * scale));
            var resized = src.Duplicate() as Image;
            if (resized != null)
            {
                resized.Resize(newW, newH, Image.Interpolation.Lanczos);
                var tex = ImageTexture.CreateFromImage(resized);
                resized.Dispose();
                return tex;
            }
        }

        return ImageTexture.CreateFromImage(src);
    }

    public override void _ExitTree()
    {
        SpriteDebugNotifier.OnImageLoaded -= OnImageLoaded;
    }
}
