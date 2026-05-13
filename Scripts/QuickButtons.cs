using Godot;
using System.Collections.Generic;

public partial class QuickButtons : CanvasLayer
{
	Control layerRoot;
	PanelContainer panel;
	ScrollContainer scroll;
	Control resizeHandle;
	VBoxContainer rowsContainer;
	HBoxContainer currentRow;
	List<Button> buttons = new List<Button>();
	FontFile fontFile;
	int fontSize;
	bool layoutUpdateQueued;
	bool resizingWidth;
	bool scrollingByDrag;
	bool dragMoved;
	float resizeStartMouseX;
	float resizeStartWidth;
	Vector2 dragStartPosition;
	Vector2 dragLastPosition;
	Button dragButton;
	float userWidth = -1;
	const int QuickButtonFontSize = 12;
	const int QuickButtonWidth = 90;
	const int QuickButtonPadding = 2;
	const int QuickButtonSpacing = 3;
	const int ResizeHandleWidth = 28;
	const float DragThreshold = 10.0f;

	public override void _Ready()
	{
		Visible = false;
		Layer = 90;

		layerRoot = new Control();
		layerRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layerRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(layerRoot);

		panel = new PanelContainer();
		panel.AnchorLeft = 1;
		panel.AnchorTop = 1;
		panel.AnchorRight = 1;
		panel.AnchorBottom = 1;
		panel.GrowHorizontal = Control.GrowDirection.Begin;
		panel.GrowVertical = Control.GrowDirection.Begin;
		panel.OffsetRight = -20;
		panel.OffsetBottom = -20;
		panel.MouseFilter = Control.MouseFilterEnum.Stop;
		panel.ClipContents = true;
		layerRoot.AddChild(panel);

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0, 0, 0, 0);
		panelStyle.BorderColor = new Color(0, 0, 0, 0);
		panelStyle.ContentMarginLeft = 0;
		panelStyle.ContentMarginRight = 0;
		panelStyle.ContentMarginTop = 0;
		panelStyle.ContentMarginBottom = 0;
		panel.AddThemeStyleboxOverride("panel", panelStyle);

		resizeHandle = new Control();
		resizeHandle.AnchorLeft = 1;
		resizeHandle.AnchorTop = 1;
		resizeHandle.AnchorRight = 1;
		resizeHandle.AnchorBottom = 1;
		resizeHandle.GrowHorizontal = Control.GrowDirection.Begin;
		resizeHandle.GrowVertical = Control.GrowDirection.Begin;
		resizeHandle.MouseDefaultCursorShape = Control.CursorShape.Hsize;
		resizeHandle.MouseFilter = Control.MouseFilterEnum.Stop;
		resizeHandle.GuiInput += OnResizeHandleGuiInput;
		layerRoot.AddChild(resizeHandle);

		var resizeStripe = new ColorRect();
		resizeStripe.AnchorLeft = 0.35f;
		resizeStripe.AnchorTop = 0;
		resizeStripe.AnchorRight = 0.65f;
		resizeStripe.AnchorBottom = 1;
		resizeStripe.Color = new Color(1, 1, 1, 0.28f);
		resizeStripe.MouseFilter = Control.MouseFilterEnum.Ignore;
		resizeHandle.AddChild(resizeStripe);

		scroll = new ScrollContainer();
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		scroll.MouseFilter = Control.MouseFilterEnum.Stop;
		panel.AddChild(scroll);

		rowsContainer = new VBoxContainer();
		rowsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rowsContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		rowsContainer.AddThemeConstantOverride("separation", QuickButtonSpacing);
		scroll.AddChild(rowsContainer);

		currentRow = new HBoxContainer();
		currentRow.AddThemeConstantOverride("separation", QuickButtonSpacing);
		rowsContainer.AddChild(currentRow);
	}

	public override void _Input(InputEvent @event)
	{
		if (scrollingByDrag)
		{
			HandleQuickDragInput(@event);
			return;
		}

		if (!resizingWidth)
			return;

		if (@event is InputEventMouseMotion mouseMotion)
		{
			var viewportWidth = GetViewport().GetVisibleRect().Size.X;
			var minWidth = QuickButtonWidth + ResizeHandleWidth;
			var maxWidth = Mathf.Max(minWidth, viewportWidth - 40);
			userWidth = Mathf.Clamp(resizeStartWidth + resizeStartMouseX - mouseMotion.GlobalPosition.X, minWidth, maxWidth);
			ApplyPanelSize();
			GetViewport().SetInputAsHandled();
		}
		else if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
		{
			resizingWidth = false;
			GetViewport().SetInputAsHandled();
		}
	}

	void HandleQuickDragInput(InputEvent @event)
	{
		if (TryGetPointer(@event, out var position, out var pressed, out var released, out var motion))
		{
			if (pressed)
				return;
			if (motion)
			{
				var totalDelta = position - dragStartPosition;
				if (!dragMoved && totalDelta.Length() >= DragThreshold)
					dragMoved = true;
				if (dragMoved && scroll != null)
				{
					var delta = dragLastPosition - position;
					scroll.ScrollHorizontal += Mathf.RoundToInt(delta.X);
					scroll.ScrollVertical += Mathf.RoundToInt(delta.Y);
					GetViewport().SetInputAsHandled();
				}
				dragLastPosition = position;
				return;
			}
			if (released)
			{
				FinishQuickButtonPointer();
				GetViewport().SetInputAsHandled();
			}
		}
	}

	void OnResizeHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			resizingWidth = mouseButton.Pressed;
			resizeStartMouseX = mouseButton.GlobalPosition.X;
			resizeStartWidth = panel != null ? panel.Size.X : QuickButtonWidth;
			GetViewport().SetInputAsHandled();
		}
	}

	public void Clear()
	{
		buttons.Clear();
		foreach (var child in rowsContainer.GetChildren())
		{
			rowsContainer.RemoveChild(child);
			child.QueueFree();
		}
		currentRow = new HBoxContainer();
		currentRow.AddThemeConstantOverride("separation", QuickButtonSpacing);
		rowsContainer.AddChild(currentRow);
		UpdatePanelSize();
	}

	public void AddButton(string text, Godot.Color color, string code)
	{
		var btn = new Button();
		btn.Text = text.Trim();
		StyleQuickButton(btn, color);
		btn.AddThemeColorOverride("font_color", color);
		if (fontFile != null)
			btn.AddThemeFontOverride("font", fontFile);
		btn.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
		btn.CustomMinimumSize = new Vector2(QuickButtonWidth, QuickButtonHeight);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		btn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		btn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		btn.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		string inputCode = code;
		btn.SetMeta("input_code", inputCode);
		btn.GuiInput += inputEvent => OnQuickButtonGuiInput(inputEvent, btn, inputCode);
		currentRow.AddChild(btn);
		buttons.Add(btn);
		UpdatePanelSize();
	}

	void OnQuickButtonGuiInput(InputEvent @event, Button btn, string inputCode)
	{
		if (TryGetPointer(@event, out var position, out var pressed, out var released, out var motion))
		{
			if (pressed)
			{
				scrollingByDrag = true;
				dragMoved = false;
				dragButton = btn;
				dragStartPosition = position;
				dragLastPosition = position;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (!scrollingByDrag || dragButton != btn)
				return;

			if (motion)
			{
				var totalDelta = position - dragStartPosition;
				if (!dragMoved && totalDelta.Length() >= DragThreshold)
					dragMoved = true;
				if (dragMoved && scroll != null)
				{
					var delta = dragLastPosition - position;
					scroll.ScrollHorizontal += Mathf.RoundToInt(delta.X);
					scroll.ScrollVertical += Mathf.RoundToInt(delta.Y);
					GetViewport().SetInputAsHandled();
				}
				dragLastPosition = position;
				return;
			}

			if (released)
			{
				FinishQuickButtonPointer(inputCode);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	void FinishQuickButtonPointer(string fallbackInputCode = null)
	{
		if (!scrollingByDrag)
			return;
		string inputCode = fallbackInputCode;
		if (inputCode == null && dragButton != null && dragButton.HasMeta("input_code"))
			inputCode = dragButton.GetMeta("input_code").As<string>();
		if (!dragMoved && !string.IsNullOrEmpty(inputCode))
			EmueraThread.instance.Input(inputCode, true);
		scrollingByDrag = false;
		dragMoved = false;
		dragButton = null;
	}

	static bool TryGetPointer(InputEvent @event, out Vector2 position, out bool pressed, out bool released, out bool motion)
	{
		position = Vector2.Zero;
		pressed = false;
		released = false;
		motion = false;

		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			position = mouseButton.GlobalPosition;
			pressed = mouseButton.Pressed;
			released = !mouseButton.Pressed;
			return true;
		}
		if (@event is InputEventMouseMotion mouseMotion && (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
		{
			position = mouseMotion.GlobalPosition;
			motion = true;
			return true;
		}
		if (@event is InputEventScreenTouch touch)
		{
			position = touch.Position;
			pressed = touch.Pressed;
			released = !touch.Pressed;
			return true;
		}
		if (@event is InputEventScreenDrag drag)
		{
			position = drag.Position;
			motion = true;
			return true;
		}
		return false;
	}

	public void ShiftLine()
	{
		if (currentRow.GetChildCount() == 0)
			return;
		currentRow = new HBoxContainer();
		currentRow.AddThemeConstantOverride("separation", QuickButtonSpacing);
		rowsContainer.AddChild(currentRow);
		UpdatePanelSize();
	}

	public void ShowPad()
	{
		Visible = true;
	}

	public void HidePad()
	{
		Visible = false;
	}

	public bool IsShow => Visible;

	public void ApplyFont(FontFile font, int fontSize)
	{
		fontFile = font;
		this.fontSize = fontSize > 0 ? QuickButtonFontSize : 0;
		if (font != null)
		{
			foreach (var btn in buttons)
				btn.AddThemeFontOverride("font", font);
		}
		foreach (var btn in buttons)
			btn.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
		UpdatePanelSize();
	}

	int EffectiveFontSize => fontSize > 0 ? fontSize : QuickButtonFontSize;

	int QuickButtonHeight => EffectiveFontSize * 3 + QuickButtonPadding * 2;

	void StyleQuickButton(Button btn, Color textColor)
	{
		var bgSource = IsMidGray(textColor)
			? new Color(MinorShift.Emuera.Config.BackColor.r, MinorShift.Emuera.Config.BackColor.g, MinorShift.Emuera.Config.BackColor.b, 1)
			: textColor;
		var bgColor = new Color(1 - bgSource.R, 1 - bgSource.G, 1 - bgSource.B, 0.75f);

		var normal = new StyleBoxFlat();
		normal.BgColor = bgColor;
		normal.CornerRadiusTopLeft = normal.CornerRadiusTopRight = 3;
		normal.CornerRadiusBottomLeft = normal.CornerRadiusBottomRight = 3;
		normal.ContentMarginLeft = QuickButtonPadding;
		normal.ContentMarginRight = QuickButtonPadding;
		normal.ContentMarginTop = QuickButtonPadding;
		normal.ContentMarginBottom = QuickButtonPadding;

		var pressed = normal.Duplicate() as StyleBoxFlat;
		pressed.BgColor = new Color(bgColor.R, bgColor.G, bgColor.B, 0.95f);

		btn.AddThemeStyleboxOverride("normal", normal);
		btn.AddThemeStyleboxOverride("hover", pressed);
		btn.AddThemeStyleboxOverride("pressed", pressed);
		btn.AddThemeStyleboxOverride("focus", pressed);
		btn.AddThemeColorOverride("font_hover_color", textColor);
		btn.AddThemeColorOverride("font_pressed_color", textColor);
		btn.AddThemeColorOverride("font_focus_color", textColor);
	}

	bool IsMidGray(Color color)
	{
		return Mathf.Abs(color.R - 0.5f) <= 0.063f
			&& Mathf.Abs(color.G - 0.5f) <= 0.063f
			&& Mathf.Abs(color.B - 0.5f) <= 0.063f;
	}

	async void UpdatePanelSize()
	{
		if (panel == null || scroll == null || rowsContainer == null)
			return;
		if (layoutUpdateQueued)
			return;

		layoutUpdateQueued = true;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		layoutUpdateQueued = false;
		var viewportSize = GetViewport().GetVisibleRect().Size;
		var contentSize = rowsContainer.GetCombinedMinimumSize();
		float maxWidth = viewportSize.X * 0.6f;
		float maxHeight = Mathf.Max(QuickButtonHeight, viewportSize.Y - 66);
		float autoWidth = Mathf.Min(contentSize.X, maxWidth);
		float width = userWidth > 0 ? Mathf.Min(userWidth, viewportSize.X - 40) : autoWidth;
		float height = Mathf.Min(contentSize.Y, maxHeight);
		ApplyPanelSize(width, height);
		scroll.ScrollVertical = (int)rowsContainer.Size.Y;
	}

	void ApplyPanelSize()
	{
		if (panel == null || rowsContainer == null)
			return;

		var viewportSize = GetViewport().GetVisibleRect().Size;
		var contentSize = rowsContainer.GetCombinedMinimumSize();
		float maxHeight = Mathf.Max(QuickButtonHeight, viewportSize.Y - 66);
		float width = userWidth > 0 ? userWidth : Mathf.Min(contentSize.X, viewportSize.X * 0.6f);
		float height = Mathf.Min(contentSize.Y, maxHeight);
		ApplyPanelSize(width, height);
	}

	void ApplyPanelSize(float width, float height)
	{
		panel.OffsetLeft = -20 - width;
		panel.OffsetRight = -20;
		panel.OffsetTop = -20 - height;
		panel.OffsetBottom = -20;
		resizeHandle.OffsetLeft = -20 - width - ResizeHandleWidth;
		resizeHandle.OffsetRight = -20 - width;
		resizeHandle.OffsetTop = -20 - height;
		resizeHandle.OffsetBottom = -20;
	}
}
