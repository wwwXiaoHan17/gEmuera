using Godot;

/// <summary>
/// Human-facing prototype controls. It emits intent only; orchestration owns
/// the actual session and legacy operations.
/// </summary>
public partial class PrototypeCommandPanel : Control
{
	[Signal]
	public delegate void RequestReloadEventHandler();

	[Signal]
	public delegate void RequestToggleEventHandler();

	[Signal]
	public delegate void RequestDetachEventHandler();

	private Label statusLabel;
	private Label sessionLabel;

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.BottomLeft);
		Position = new Vector2(12, -108);
		Size = new Vector2(360, 96);
		MouseFilter = MouseFilterEnum.Pass;

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 4);
		AddChild(root);

		sessionLabel = new Label { Text = "Prototype session" };
		sessionLabel.AddThemeFontSizeOverride("font_size", 12);
		root.AddChild(sessionLabel);

		statusLabel = new Label { Text = "Ready" };
		statusLabel.AddThemeFontSizeOverride("font_size", 12);
		root.AddChild(statusLabel);

		var commands = new HBoxContainer();
		commands.AddThemeConstantOverride("separation", 4);
		root.AddChild(commands);
		AddButton(commands, "Reload", SignalName.RequestReload);
		AddButton(commands, "Toggle", SignalName.RequestToggle);
		AddButton(commands, "Detach", SignalName.RequestDetach);
	}

	public void SetStatus(string status)
	{
		if (statusLabel is not null)
			statusLabel.Text = status ?? string.Empty;
	}

	public void SetSessionInfo(string profile, long generation, string planHash)
	{
		if (sessionLabel is not null)
			sessionLabel.Text = $"{profile} | gen {generation} | {planHash}";
	}

	private void AddButton(HBoxContainer parent, string text, StringName signal)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 44),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		button.Pressed += () => EmitSignal(signal);
		parent.AddChild(button);
	}
}
