using Godot;

// Deep-modern dark theme: design tokens + shared StyleBox/动效 helpers.
//
// This is the single source of truth for the m45 UI polish palette, so every
// code-built control (launcher, in-game system UI, floating panels) resolves the
// same tokens instead of hard-coded greenish/blue-ish literals. It mirrors a web
// design-token layer (colors, radii, shadow, motion durations) without changing
// any button value/signal/hit-area/ERB semantics — only the visual layer.
public static class GEmueraTheme
{
	static Theme _loadedTheme;

	/// <summary>
	/// Loads the unified Theme resource (res://assets/theme/gemuera_theme.tres) that supplies
	/// dark-modern baseline styles for controls without explicit code overrides.
	/// </summary>
	public static Theme LoadTheme()
	{
		if (_loadedTheme == null)
			_loadedTheme = GD.Load<Theme>("res://assets/theme/gemuera_theme.tres");
		return _loadedTheme;
	}

	// ---- Colors (dark modern) ----
	public static readonly Color Background = new Color("#14161D");
	public static readonly Color Surface = new Color("#1E222B");
	public static readonly Color SurfaceRaised = new Color("#232833");
	public static readonly Color Border = new Color("#2A2F3A");
	public static readonly Color BorderStrong = new Color("#343A48");
	public static readonly Color TextPrimary = new Color("#E8EAF0");
	public static readonly Color TextSecondary = new Color("#9AA0AE");
	public static readonly Color TextDim = new Color("#6B7280");
	public static readonly Color Accent = new Color("#6C8CFF");
	public static readonly Color AccentAlt = new Color("#9B6CFF");
	public static readonly Color Success = new Color("#4ADE80");
	public static readonly Color Danger = new Color("#F87171");
	public static readonly Color DisabledBg = new Color("#3A3F4A");
	public static readonly Color DisabledText = new Color("#7A8090");

	// ---- Radii ----
	public const int CardRadius = 8;
	public const int ButtonRadius = 6;
	public const int SmallRadius = 4;

	// ---- Motion (hover/press 150-250ms ease-out, entrance 300-400ms) ----
	public const float HoverSeconds = 0.18f;
	public const float PressSeconds = 0.10f;
	public const float EnterSeconds = 0.34f;
	public const float PressScale = 0.97f;

	// ---- Token helpers ----
	public static Color Lighten(Color color, float amount = 0.08f) => color.Lightened(amount);
	public static Color Darken(Color color, float amount = 0.08f) => color.Darkened(amount);
	public static Color WithAlpha(Color color, float alpha) => new Color(color.R, color.G, color.B, alpha);
	/// <summary>蓝紫渐变插值：t=0 时 #6C8CFF，t=1 时 #9B6CFF。</summary>
	public static Color AccentGradient(float t) => Accent.Lerp(AccentAlt, Mathf.Clamp(t, 0f, 1f));

	/// <summary>
	/// Surface/card panel stylebox: 8px radius, 1px border, optional soft shadow.
	/// Content margins are kept caller-controlled so panel inner spacing stays as-is.
	/// </summary>
	public static StyleBoxFlat SurfaceStyle(
		Color? bg = null,
		Color? border = null,
		int radius = CardRadius,
		int borderWidth = 1,
		int shadowSize = 0,
		Vector2? shadowOffset = null,
		int contentMarginLeft = 0,
		int contentMarginRight = 0,
		int contentMarginTop = 0,
		int contentMarginBottom = 0)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bg ?? Surface;
		style.BorderColor = border ?? Border;
		style.SetBorderWidthAll(borderWidth);
		style.SetCornerRadiusAll(radius);
		style.ContentMarginLeft = contentMarginLeft;
		style.ContentMarginRight = contentMarginRight;
		style.ContentMarginTop = contentMarginTop;
		style.ContentMarginBottom = contentMarginBottom;
		if (shadowSize > 0)
		{
			style.ShadowColor = WithAlpha(Color.FromHtml("#000000"), 0.3f);
			style.ShadowSize = shadowSize;
			style.ShadowOffset = shadowOffset ?? new Vector2(0, 3);
		}
		return style;
	}

	/// <summary>
	/// Flat button stylebox. Content margins stay zero: m45 iron rule requires the
	/// button hit area / layout to remain byte-identical to the previous transparent
	/// style, so padding is never injected through the stylebox.
	/// </summary>
	public static StyleBoxFlat ButtonBox(Color bg, Color border, int radius = ButtonRadius, int shadowSize = 0, bool pressed = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bg;
		style.BorderColor = border;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(radius);
		style.ContentMarginLeft = 0;
		style.ContentMarginRight = 0;
		style.ContentMarginTop = 0;
		style.ContentMarginBottom = 0;
		if (shadowSize > 0)
		{
			style.ShadowColor = WithAlpha(Color.FromHtml("#000000"), 0.28f);
			style.ShadowSize = shadowSize;
			style.ShadowOffset = pressed ? new Vector2(0, 1) : new Vector2(0, 3);
		}
		return style;
	}

	/// <summary>
	/// Apply the shared dark button theme (normal/hover/pressed/disabled/focus) plus a
	/// press-down scale tween. Only the visual layer is touched; signals and hit rect
	/// are preserved.
	/// </summary>
	public static void ApplyButton(Button button, Color bg, Color border, bool shadowed = false)
	{
		if (button == null)
			return;

		button.AddThemeStyleboxOverride("normal", ButtonBox(bg, border, ButtonRadius, shadowed ? 4 : 0));
		button.AddThemeStyleboxOverride("hover", ButtonBox(Lighten(bg), Lighten(border), ButtonRadius, shadowed ? 7 : 1));
		button.AddThemeStyleboxOverride("pressed", ButtonBox(Darken(bg, 0.12f), Darken(border, 0.12f), ButtonRadius, 1, pressed: true));
		button.AddThemeStyleboxOverride("disabled", ButtonBox(DisabledBg, Darken(DisabledBg), ButtonRadius, 0));
		button.AddThemeStyleboxOverride("focus", ButtonBox(bg, Accent, ButtonRadius, 2));

		button.AddThemeColorOverride("font_color", TextPrimary);
		button.AddThemeColorOverride("font_hover_color", TextPrimary);
		button.AddThemeColorOverride("font_pressed_color", TextPrimary);
		button.AddThemeColorOverride("font_focus_color", TextPrimary);
		button.AddThemeColorOverride("font_disabled_color", DisabledText);

		WirePressAnimation(button);
	}

	/// <summary>
	/// Accent "primary" button (launcher Start). Flat accent fill with a deeper accent
	/// border; hover brightens ~8%, pressed darkens.
	/// </summary>
	public static void ApplyAccentButton(Button button)
	{
		if (button == null)
			return;

		Color accentDeep = new Color("#5571E8");
		button.AddThemeStyleboxOverride("normal", ButtonBox(Accent, accentDeep, ButtonRadius, 5));
		button.AddThemeStyleboxOverride("hover", ButtonBox(Lighten(Accent), Lighten(accentDeep), ButtonRadius, 8));
		button.AddThemeStyleboxOverride("pressed", ButtonBox(Darken(Accent, 0.12f), Darken(accentDeep, 0.12f), ButtonRadius, 2, pressed: true));
		button.AddThemeStyleboxOverride("disabled", ButtonBox(DisabledBg, Darken(DisabledBg), ButtonRadius, 0));
		button.AddThemeStyleboxOverride("focus", ButtonBox(Accent, TextPrimary, ButtonRadius, 2));

		button.AddThemeColorOverride("font_color", TextPrimary);
		button.AddThemeColorOverride("font_hover_color", TextPrimary);
		button.AddThemeColorOverride("font_pressed_color", TextPrimary);
		button.AddThemeColorOverride("font_focus_color", TextPrimary);
		button.AddThemeColorOverride("font_disabled_color", DisabledText);

		WirePressAnimation(button);
	}

	/// <summary>
	/// Launcher tab rail button. Active state uses an accent-tinted fill and accent
	/// border; inactive uses the neutral surface. Hover always brightens.
	/// </summary>
	public static void ApplyRailButton(Button button, bool active)
	{
		if (button == null)
			return;

		Color bg = active ? WithAlpha(Accent, 0.28f) : Surface;
		Color border = active ? Accent : Border;
		Color text = active ? TextPrimary : TextSecondary;

		button.AddThemeStyleboxOverride("normal", ButtonBox(bg, border, ButtonRadius, active ? 5 : 1));
		button.AddThemeStyleboxOverride("hover", ButtonBox(active ? Lighten(bg, 0.06f) : SurfaceRaised, active ? Lighten(border) : BorderStrong, ButtonRadius, active ? 7 : 2));
		button.AddThemeStyleboxOverride("pressed", ButtonBox(Darken(bg, 0.12f), active ? Darken(border) : Border, ButtonRadius, 1, pressed: true));
		button.AddThemeStyleboxOverride("focus", ButtonBox(bg, Accent, ButtonRadius, 2));

		button.AddThemeColorOverride("font_color", text);
		button.AddThemeColorOverride("font_hover_color", TextPrimary);
		button.AddThemeColorOverride("font_pressed_color", TextPrimary);
		button.AddThemeColorOverride("font_focus_color", TextPrimary);

		WirePressAnimation(button);
	}

	/// <summary>
	/// Preset styles for the in-game system buttons (OK/Repeat/1:1/Fit/Close/M).
	/// These keep the exact hit rect of the previous transparent style.
	/// </summary>
	public static void ApplySystemButton(Button button)
	{
		ApplyButton(button, Surface, Border, shadowed: false);
	}

	public static void WirePressAnimation(Button button)
	{
		if (button == null)
			return;
		// 启动器 Tab 按钮会随切换被多次重新上样式，避免重复挂 ButtonDown/Up 处理器。
		if (button.HasMeta("gemuera_press_wired"))
			return;
		button.SetMeta("gemuera_press_wired", true);
		button.ButtonDown += () => AnimateButtonScale(button, PressScale, PressSeconds);
		button.ButtonUp += () => AnimateButtonScale(button, 1.0f, PressSeconds);
		button.MouseExited += () => AnimateButtonScale(button, 1.0f, HoverSeconds);
	}

	public static void AnimateButtonScale(Button button, float targetScale, float duration)
	{
		if (button == null)
			return;
		button.PivotOffset = button.Size * 0.5f;
		// 与 QuickButtons 的 _press_tween 相同的 Kill-复用模式（M4/5 动效回归）：
		// 连点/快速移出会在 100ms 内连续创建多个 tween，复用前必须 Kill 旧 tween，
		// 否则旧动效会继续把 Scale 写回 0.97，按钮卡在缩小态。tween 已 BindNode，
		// 节点释放时自动终止，meta 引用不泄漏。
		// Godot 4 C# 的 GetMeta(name, default) 绑定实际走无默认的 C++ get_meta(name)，
		// 键不存在时会打印 "The object does not have any 'meta' values..." 错误。
		// 首次点击时 meta 尚不存在，必须先用 HasMeta 检查再取。
		var prevTween = button.HasMeta("_press_tween")
			? button.GetMeta("_press_tween", default(Variant)).As<Tween>()
			: null;
		if (prevTween != null && GodotObject.IsInstanceValid(prevTween))
			prevTween.Kill();
		var tween = button.CreateTween();
		button.SetMeta("_press_tween", tween);
		tween.BindNode(button);
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(button, "scale", new Vector2(targetScale, targetScale), duration);
	}

	/// <summary>Full-rect background ColorRect using the background token.</summary>
	public static ColorRect CreateBackground()
	{
		return new ColorRect
		{
			Color = Background,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
	}

	/// <summary>Shared launcher/panel scrollbar: wide track + rounded grabber.</summary>
	public static void ApplyWideScrollbar(VScrollBar scrollbar, int width = 24)
	{
		if (scrollbar == null)
			return;
		scrollbar.CustomMinimumSize = new Vector2(width, 0);
		scrollbar.AddThemeConstantOverride("scroll_width", width);
		scrollbar.AddThemeStyleboxOverride("scroll", ScrollTrackStyle());
		scrollbar.AddThemeStyleboxOverride("grabber", ScrollGrabberStyle(WithAlpha(BorderStrong, 0.9f)));
		scrollbar.AddThemeStyleboxOverride("grabber_highlight", ScrollGrabberStyle(WithAlpha(TextDim, 0.9f)));
		scrollbar.AddThemeStyleboxOverride("grabber_pressed", ScrollGrabberStyle(WithAlpha(Accent, 0.9f)));
	}

	public static StyleBoxFlat ScrollTrackStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = WithAlpha(Darken(Background, 0.25f), 0.9f);
		style.SetCornerRadiusAll(8);
		style.ContentMarginLeft = 4;
		style.ContentMarginRight = 4;
		return style;
	}

	public static StyleBoxFlat ScrollGrabberStyle(Color color)
	{
		var style = new StyleBoxFlat();
		style.BgColor = color;
		style.SetCornerRadiusAll(8);
		style.ContentMarginLeft = 5;
		style.ContentMarginRight = 5;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}

	/// <summary>Game list (ItemList) baseline: dark panel, readable text, accent selection.</summary>
	public static void ApplyItemList(ItemList list)
	{
		if (list == null)
			return;
		list.AddThemeStyleboxOverride("panel", SurfaceStyle(Surface, Border, CardRadius, 1, 6));
		list.AddThemeStyleboxOverride("focus", ButtonBox(Surface, Accent, CardRadius, 2));
		list.AddThemeColorOverride("font_color", TextPrimary);
		list.AddThemeColorOverride("font_hover_color", TextPrimary);
		list.AddThemeColorOverride("font_selected_color", TextPrimary);
		list.AddThemeColorOverride("selected", WithAlpha(Accent, 0.32f));
	}

	/// <summary>Popup panel baseline (msgbox / option dialog).</summary>
	public static void ApplyPopup(PopupPanel popup)
	{
		if (popup == null)
			return;
		popup.AddThemeStyleboxOverride("panel", SurfaceStyle(SurfaceRaised, Border, CardRadius, 1, 14, new Vector2(0, 6), 12, 12, 12, 12));
	}
}
