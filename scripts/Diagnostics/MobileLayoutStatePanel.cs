using Godot;

/// <summary>
/// 移动端布局状态面板（排障用，默认不创建）：左上角半透明浮层，0.5s 刷新一次，
/// 显示 window/viewport/safe/scale/滚动范围/小窗与方向状态，便于真机截图回报。
/// </summary>
public partial class MobileLayoutStatePanel : Control
{
	Label label;
	double elapsed;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ZIndex = 4096;
		SetAnchorsPreset(LayoutPreset.TopLeft);
		Position = new Vector2(8, 8);

		var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0f, 0f, 0f, 0.55f),
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			ContentMarginLeft = 6,
			ContentMarginTop = 4,
			ContentMarginRight = 6,
			ContentMarginBottom = 4,
		};
		panel.AddThemeStyleboxOverride("panel", style);
		AddChild(panel);

		label = new Label { MouseFilter = MouseFilterEnum.Ignore };
		label.AddThemeFontSizeOverride("font_size", 11);
		label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.9f));
		panel.AddChild(label);
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		elapsed += delta;
		if (elapsed < 0.5)
			return;
		elapsed = 0;
		if (label == null)
			return;
		label.Text = MobileLayoutDiagnostics.LatestLayout + "\n"
			+ MobileLayoutDiagnostics.LatestScroll + "\n"
			+ MobileLayoutDiagnostics.LatestOrientation;
	}
}
