using Godot;

/// <summary>
/// Read-only status projection for the prototype Core sidecar.
/// </summary>
public partial class PrototypeStatusView : CanvasLayer
{
    private Label statusLabel;
    private PrototypeCommandPanel commandPanel;

    public override void _Ready()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.Position = new Vector2(-360, 12);
        panel.Size = new Vector2(348, 42);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;

        statusLabel = new Label();
        statusLabel.Text = "Core prototype starting...";
        statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statusLabel.VerticalAlignment = VerticalAlignment.Center;
        statusLabel.AddThemeFontSizeOverride("font_size", 12);
        statusLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.86f, 0.78f));
        statusLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(statusLabel);
        AddChild(panel);

        var runtime = GetParent().GetNodeOrNull<PrototypeRuntimeNode>("PrototypeRuntime");
        commandPanel = GetParent().GetNodeOrNull<PrototypeCommandPanel>("PrototypeCommandPanel");
        if (runtime is not null)
            runtime.PrototypeStatusChanged += OnPrototypeStatusChanged;
    }

    public override void _ExitTree()
    {
        var runtime = GetParent().GetNodeOrNull<PrototypeRuntimeNode>("PrototypeRuntime");
        if (runtime is not null)
            runtime.PrototypeStatusChanged -= OnPrototypeStatusChanged;
        commandPanel = null;
    }

    private void OnPrototypeStatusChanged(string status)
    {
        if (statusLabel is not null)
            statusLabel.Text = status;
        commandPanel?.SetStatus(status);
    }
}
