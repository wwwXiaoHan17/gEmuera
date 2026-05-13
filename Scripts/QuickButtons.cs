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
	List<Control> buttons = new List<Control>();
	FontFile fontFile;
	int fontSize;
	bool layoutUpdateQueued;
	bool resizingWidth;
	bool scrollingByDrag;
	bool dragMoved;
	bool scrollToBottomAfterLayout = true;
	bool quickInertiaActive;
	int quickScrollInteractionSerial;
	float resizeStartMouseX;
	float resizeStartWidth;
	float quickInertiaDeceleration = 900.0f;
	Vector2 dragStartPosition;
	Vector2 dragLastPosition;
	Vector2 quickScrollVelocity = Vector2.Zero;
	Vector2 quickInertiaRemainder = Vector2.Zero;
	Control dragButton;
	ulong quickLastDragTick;
	float userWidth = -1;
	const int QuickButtonFontSize = 12;
	const int QuickButtonWidth = 90;
	const int QuickButtonPadding = 2;
	const int QuickButtonSpacing = 3;
	const int ResizeHandleWidth = 28;
	const int ScrollBottomTolerance = 4;
	const float DragThreshold = 3.0f;
	const float QuickInertiaMinVelocity = 80.0f;
	const float QuickInertiaFastVelocity = 4500.0f;
	const float QuickInertiaMaxVelocity = 14000.0f;
	const float QuickInertiaMinReleaseBoost = 1.2f;
	const float QuickInertiaMaxReleaseBoost = 3.0f;
	const float QuickInertiaSlowDeceleration = 1800.0f;
	const float QuickInertiaFastDeceleration = 520.0f;
	const float QuickInertiaStopVelocity = 6.0f;

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
		scroll.MouseFilter = Control.MouseFilterEnum.Pass;
		scroll.GuiInput += OnQuickGuiInput;
		panel.AddChild(scroll);

		rowsContainer = new VBoxContainer();
		rowsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rowsContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		rowsContainer.MouseFilter = Control.MouseFilterEnum.Pass;
		rowsContainer.GuiInput += OnQuickGuiInput;
		rowsContainer.AddThemeConstantOverride("separation", QuickButtonSpacing);
		scroll.AddChild(rowsContainer);

		currentRow = new HBoxContainer();
		currentRow.AddThemeConstantOverride("separation", QuickButtonSpacing);
		rowsContainer.AddChild(currentRow);
	}

	public override void _Process(double delta)
	{
		ProcessQuickInertia((float)delta);
	}

	public override void _Input(InputEvent @event)
	{
		if (scrollingByDrag)
		{
			HandleQuickPointerInput(@event, false);
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

	void OnQuickGuiInput(InputEvent @event)
	{
		HandleQuickPointerInput(@event, true);
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
		UpdatePanelSize(true);
	}

	public void AddButton(string text, Godot.Color color, string code)
	{
		var btn = new Panel();
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.MouseForcePassScrollEvents = false;
		btn.MouseFilter = Control.MouseFilterEnum.Stop;
		StyleQuickButton(btn, color);
		btn.CustomMinimumSize = new Vector2(QuickButtonWidth, QuickButtonHeight);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		btn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

		var label = new Label();
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.OffsetLeft = QuickButtonPadding;
		label.OffsetTop = QuickButtonPadding;
		label.OffsetRight = -QuickButtonPadding;
		label.OffsetBottom = -QuickButtonPadding;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.Text = text.Trim();
		label.AddThemeColorOverride("font_color", color);
		if (fontFile != null)
			label.AddThemeFontOverride("font", fontFile);
		label.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		label.VerticalAlignment = VerticalAlignment.Center;
		btn.AddChild(label);

		string inputCode = code;
		btn.SetMeta("input_code", inputCode);
		btn.GuiInput += inputEvent => OnQuickButtonGuiInput(inputEvent, btn, inputCode);
		currentRow.AddChild(btn);
		buttons.Add(btn);
		UpdatePanelSize(ShouldStickToBottom());
	}

	void OnQuickButtonGuiInput(InputEvent @event, Control btn, string inputCode)
	{
		HandleQuickPointerInput(@event, true, btn, inputCode);
	}

	bool HandleQuickPointerInput(InputEvent @event, bool acceptEvent, Control button = null, string inputCode = null)
	{
		if (TryGetPointer(@event, out var position, out var pressed, out var released, out var motion))
		{
			if (scroll == null || panel == null || (!scrollingByDrag && !panel.GetGlobalRect().HasPoint(position)))
				return false;

			if (pressed && scrollingByDrag && button == null)
			{
				if (acceptEvent)
					GetViewport().SetInputAsHandled();
				else
					GetViewport().SetInputAsHandled();
				return true;
			}

			if (pressed)
			{
				StopQuickInertia();
				scrollingByDrag = true;
				dragMoved = false;
				scrollToBottomAfterLayout = false;
				dragButton = button;
				dragStartPosition = position;
				dragLastPosition = position;
				quickLastDragTick = Time.GetTicksMsec();
				if (button != null || acceptEvent)
					GetViewport().SetInputAsHandled();
				return button != null || acceptEvent;
			}

			if (!scrollingByDrag)
				return false;

			if (motion)
			{
				var totalDelta = position - dragStartPosition;
				if (!dragMoved && totalDelta.Length() >= DragThreshold)
				{
					dragMoved = true;
					quickScrollInteractionSerial++;
				}
				if (dragMoved && scroll != null)
				{
					var delta = dragLastPosition - position;
					var appliedDelta = ScrollQuickBy(delta);
					UpdateQuickScrollVelocity(delta, appliedDelta);
					if (acceptEvent)
						GetViewport().SetInputAsHandled();
					else
						GetViewport().SetInputAsHandled();
					dragLastPosition = position;
					return true;
				}
				dragLastPosition = position;
				return false;
			}

			if (released)
			{
				FinishQuickButtonPointer(inputCode);
				if (acceptEvent)
					GetViewport().SetInputAsHandled();
				else
					GetViewport().SetInputAsHandled();
				return true;
			}
		}
		return false;
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
		else if (dragMoved)
			StartQuickInertia();
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
		UpdatePanelSize(ShouldStickToBottom());
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
				ApplyFontToButtonLabel(btn);
		}
		foreach (var btn in buttons)
			ApplyFontToButtonLabel(btn);
		UpdatePanelSize(ShouldStickToBottom());
	}

	int EffectiveFontSize => fontSize > 0 ? fontSize : QuickButtonFontSize;

	int QuickButtonHeight => EffectiveFontSize * 3 + QuickButtonPadding * 2;

	void StyleQuickButton(Panel btn, Color textColor)
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

		btn.AddThemeStyleboxOverride("panel", normal);
	}

	void ApplyFontToButtonLabel(Control btn)
	{
		if (btn == null)
			return;
		foreach (var child in btn.GetChildren())
		{
			if (child is Label label)
			{
				if (fontFile != null)
					label.AddThemeFontOverride("font", fontFile);
				label.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
				return;
			}
		}
	}

	bool IsMidGray(Color color)
	{
		return Mathf.Abs(color.R - 0.5f) <= 0.063f
			&& Mathf.Abs(color.G - 0.5f) <= 0.063f
			&& Mathf.Abs(color.B - 0.5f) <= 0.063f;
	}

	async void UpdatePanelSize(bool keepBottom)
	{
		if (panel == null || scroll == null || rowsContainer == null)
			return;
		scrollToBottomAfterLayout = !scrollingByDrag && !quickInertiaActive && (scrollToBottomAfterLayout || keepBottom);
		if (layoutUpdateQueued)
			return;

		layoutUpdateQueued = true;
		int autoScrollInteractionSerial = quickScrollInteractionSerial;
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
		if (scrollToBottomAfterLayout && !scrollingByDrag && !quickInertiaActive && autoScrollInteractionSerial == quickScrollInteractionSerial)
			ScrollQuickToBottom();
		scrollToBottomAfterLayout = false;
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

	Vector2 ScrollQuickBy(Vector2 delta)
	{
		if (scroll == null)
			return Vector2.Zero;
		int oldHorizontal = scroll.ScrollHorizontal;
		int oldVertical = scroll.ScrollVertical;
		int nextHorizontal = scroll.ScrollHorizontal + Mathf.RoundToInt(delta.X);
		int nextVertical = scroll.ScrollVertical + Mathf.RoundToInt(delta.Y);
		scroll.ScrollHorizontal = Mathf.Clamp(nextHorizontal, 0, GetMaxHorizontalScroll());
		scroll.ScrollVertical = Mathf.Clamp(nextVertical, 0, GetMaxVerticalScroll());
		return new Vector2(scroll.ScrollHorizontal - oldHorizontal, scroll.ScrollVertical - oldVertical);
	}

	void ScrollQuickToBottom()
	{
		if (scroll == null)
			return;
		scroll.ScrollVertical = GetMaxVerticalScroll();
	}

	bool ShouldStickToBottom()
	{
		if (scroll == null || scrollingByDrag || quickInertiaActive)
			return false;
		return scroll.ScrollVertical >= GetMaxVerticalScroll() - ScrollBottomTolerance;
	}

	void UpdateQuickScrollVelocity(Vector2 rawScrollDelta, Vector2 appliedDelta)
	{
		ulong now = Time.GetTicksMsec();
		if (quickLastDragTick == 0)
		{
			quickLastDragTick = now;
			return;
		}

		if (rawScrollDelta.LengthSquared() <= 0.01f)
			return;

		float elapsed = Mathf.Max((now - quickLastDragTick) / 1000.0f, 1.0f / 120.0f);
		if (appliedDelta.LengthSquared() <= 0.01f)
		{
			quickScrollVelocity = Vector2.Zero;
			quickLastDragTick = now;
			return;
		}

		var instantVelocity = rawScrollDelta / elapsed;
		if (instantVelocity.Length() > QuickInertiaMaxVelocity)
			instantVelocity = instantVelocity.Normalized() * QuickInertiaMaxVelocity;

		quickScrollVelocity = quickScrollVelocity.Lerp(instantVelocity, 0.78f);
		quickLastDragTick = now;
	}

	void StartQuickInertia()
	{
		float releaseSpeed = quickScrollVelocity.Length();
		float fastRatio = Mathf.Clamp(
			(releaseSpeed - QuickInertiaMinVelocity) / (QuickInertiaFastVelocity - QuickInertiaMinVelocity),
			0.0f,
			1.0f);
		float releaseBoost = Mathf.Lerp(QuickInertiaMinReleaseBoost, QuickInertiaMaxReleaseBoost, fastRatio);
		quickInertiaDeceleration = Mathf.Lerp(QuickInertiaSlowDeceleration, QuickInertiaFastDeceleration, fastRatio);
		quickScrollVelocity *= releaseBoost;
		if (quickScrollVelocity.Length() > QuickInertiaMaxVelocity)
			quickScrollVelocity = quickScrollVelocity.Normalized() * QuickInertiaMaxVelocity;
		if (quickScrollVelocity.Length() >= QuickInertiaMinVelocity)
			quickInertiaActive = true;
		else
			StopQuickInertia();
	}

	void StopQuickInertia()
	{
		quickInertiaActive = false;
		quickScrollVelocity = Vector2.Zero;
		quickInertiaRemainder = Vector2.Zero;
		quickLastDragTick = 0;
	}

	void ProcessQuickInertia(float delta)
	{
		if (!quickInertiaActive || scrollingByDrag || scroll == null)
			return;

		var desiredDelta = quickScrollVelocity * delta + quickInertiaRemainder;
		var roundedDelta = new Vector2(Mathf.Round(desiredDelta.X), Mathf.Round(desiredDelta.Y));
		quickInertiaRemainder = desiredDelta - roundedDelta;
		if (roundedDelta.LengthSquared() > 0.01f)
		{
			var appliedDelta = ScrollQuickBy(roundedDelta);
			if (appliedDelta.LengthSquared() <= 0.01f)
			{
				StopQuickInertia();
				return;
			}
		}

		float speed = quickScrollVelocity.Length();
		speed = Mathf.MoveToward(speed, 0, quickInertiaDeceleration * delta);
		if (speed <= QuickInertiaStopVelocity)
		{
			StopQuickInertia();
			return;
		}
		quickScrollVelocity = quickScrollVelocity.Normalized() * speed;
	}

	int GetMaxHorizontalScroll()
	{
		if (scroll == null || rowsContainer == null)
			return 0;
		float contentWidth = Mathf.Max(rowsContainer.Size.X, rowsContainer.GetCombinedMinimumSize().X);
		return Mathf.RoundToInt(Mathf.Max(0, contentWidth - scroll.Size.X));
	}

	int GetMaxVerticalScroll()
	{
		if (scroll == null || rowsContainer == null)
			return 0;
		float contentHeight = Mathf.Max(rowsContainer.Size.Y, rowsContainer.GetCombinedMinimumSize().Y);
		return Mathf.RoundToInt(Mathf.Max(0, contentHeight - scroll.Size.Y));
	}
}
