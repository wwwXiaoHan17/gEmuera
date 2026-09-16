using Godot;
using System;
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
	Stack<Panel> buttonPool = new Stack<Panel>();
	Stack<HBoxContainer> rowPool = new Stack<HBoxContainer>();
	Dictionary<uint, StyleBoxFlat> quickButtonStyleCache = new Dictionary<uint, StyleBoxFlat>();
	Dictionary<uint, StyleBoxFlat> quickButtonHoverStyleCache = new Dictionary<uint, StyleBoxFlat>();
	Font fontFile;
	int fontSize;
	bool layoutUpdateQueued;
	int layoutBatchDepth;
	bool layoutBatchPending;
	bool layoutBatchKeepBottom;
	bool resizingWidth;
	bool scrollingByDrag;
	bool dragMoved;
	bool floatingHosted;
	bool windowDragEnabled;
	bool draggingWindow;
	bool quickInputEnabled = true;
	bool scrollToBottomAfterLayout = true;
	bool quickInertiaActive;
	int quickScrollInteractionSerial;
	bool quickContentSizeDirty = true;
	Vector2 cachedQuickContentSize = Vector2.Zero;
	float resizeStartMouseX;
	float resizeStartWidth;
	float quickInertiaDeceleration = 900.0f;
	Vector2 dragStartPosition;
	Vector2 dragLastPosition;
	Vector2 quickScrollVelocity = Vector2.Zero;
	Vector2 quickInertiaRemainder = Vector2.Zero;
	Control dragButton;
	bool dragPointerIsTouch;
	int dragPointerIndex = -1;
	ulong quickLastDragTick;
	float userWidth = -1;
	const string SettingsPath = "user://settings.cfg";
	const string SettingsSection = "Display";
	const string QuickButtonWidthKey = "QuickButtonWidth";
	const string QuickButtonFontSizeKey = "QuickButtonFontSize";
	const string QuickFlipKey = "QuickFlip";
	const string QuickFloatingKey = "QuickFloating";
	const int DefaultQuickButtonFontSize = 12;
	const int DefaultQuickButtonWidth = 90;
	public const int MinQuickButtonFontSize = 8;
	public const int MaxQuickButtonFontSize = 32;
	public const int MinQuickButtonWidth = 48;
	public const int MaxQuickButtonWidth = 220;
	const int QuickButtonPadding = 2;
	const int QuickButtonSpacing = 3;
	const int ResizeHandleWidth = 28;
	const int ScrollBottomTolerance = 4;
	const float DragThreshold = 10.0f;
	const float QuickInertiaMinVelocity = 80.0f;
	const float QuickInertiaFastVelocity = 4500.0f;
	const float QuickInertiaMaxVelocity = 14000.0f;
	const float QuickInertiaMinReleaseBoost = 1.2f;
	const float QuickInertiaMaxReleaseBoost = 3.0f;
	const float QuickInertiaSlowDeceleration = 1800.0f;
	const float QuickInertiaFastDeceleration = 520.0f;
	const float QuickInertiaStopVelocity = 6.0f;
	const int MaxPooledButtons = 256;
	const int MaxPooledRows = 64;
	static int configuredButtonWidth = -1;
	static int configuredFontSize = -1;
	static bool configuredFlip;
	static bool configuredFloating;

	// Component interface: the panel advertises its own show/hide state changes
	// so host scenes can react without polling. Emitted purely additively;
	// callers that never connect are unaffected. Button sizing stays driven by
	// the persisted settings (ConfiguredButtonWidth/ConfiguredFontSize), so no
	// extra export parameters are added here.
	[Signal]
	public delegate void PadShownEventHandler();

	[Signal]
	public delegate void PadHiddenEventHandler();

	public static int ConfiguredButtonWidth
	{
		get
		{
			EnsureSettingsLoaded();
			return configuredButtonWidth;
		}
		set
		{
			configuredButtonWidth = Mathf.Clamp(value, MinQuickButtonWidth, MaxQuickButtonWidth);
			SaveSetting(QuickButtonWidthKey, configuredButtonWidth);
		}
	}

	public static int ConfiguredFontSize
	{
		get
		{
			EnsureSettingsLoaded();
			return configuredFontSize;
		}
		set
		{
			configuredFontSize = Mathf.Clamp(value, MinQuickButtonFontSize, MaxQuickButtonFontSize);
			SaveSetting(QuickButtonFontSizeKey, configuredFontSize);
		}
	}

	// 面板水平翻转：默认靠右，翻转后靠左（宽度调节条同步翻到内侧）。默认关闭。
	public static bool FlipEnabled
	{
		get
		{
			EnsureSettingsLoaded();
			return configuredFlip;
		}
		set
		{
			configuredFlip = value;
			SaveSetting(QuickFlipKey, value);
		}
	}

	// 面板悬浮：由 QuickFloatingWindow（Godot Window 组件）承载，实现悬浮窗效果。
	// 默认关闭；Android 上嵌入 Window 不渲染，由 EmueraContent 门控忽略该设置。
	public static bool FloatingEnabled
	{
		get
		{
			EnsureSettingsLoaded();
			return configuredFloating;
		}
		set
		{
			configuredFloating = value;
			SaveSetting(QuickFloatingKey, value);
		}
	}

	// 面板是否真的被悬浮宿主（QuickFloatingWindow）承载。悬浮设置只在真正承载时
	// 影响布局锚点；移动端/内嵌模式忽略悬浮设置，面板保持常规位置（右下角），
	// 避免桌面同步来的悬浮设置让 Android 面板意外贴左贴顶甚至“消失”。
	public bool FloatingHosted
	{
		get => floatingHosted;
		set => floatingHosted = value;
	}

	// 宿主允许把面板拖动解释为窗口移动（仅桌面悬浮模式开启；移动端为 false）。
	public bool WindowDragEnabled
	{
		get => windowDragEnabled;
		set => windowDragEnabled = value;
	}

	/// <summary>面板拖动位移（逻辑像素；1:1 内容下即物理像素），由悬浮宿主订阅以移动窗口。</summary>
	public event Action<Vector2> WindowDragRequested;

	// 布局锚点派生：翻转 → 面板靠左；悬浮且真正被 Window 承载 → 贴左贴顶。
	bool AnchoredLeft => configuredFlip || (configuredFloating && floatingHosted);
	bool AnchoredTop => configuredFloating && floatingHosted;

	/// <summary>悬浮宿主（QuickFloatingWindow）读取面板尺寸以同步窗口大小。</summary>
	public Vector2 GetPanelSize()
	{
		return IsControlAlive(panel) ? panel.Size : Vector2.Zero;
	}

	public override void _Ready()
	{
		EnsureSettingsLoaded();
		Visible = false;
		Layer = 90;
		// _Process 只服务惯性滚动；空闲时关闭，避免 Android 上每帧一次
		// native→managed 调用（面板隐藏是常态，StartQuickInertia 会按需重开）。
		SetProcess(false);

		layerRoot = new Control();
		layerRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layerRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(layerRoot);

		panel = new PanelContainer();
		panel.AnchorLeft = AnchoredLeft ? 0 : 1;
		panel.AnchorTop = AnchoredTop ? 0 : 1;
		panel.AnchorRight = AnchoredLeft ? 0 : 1;
		panel.AnchorBottom = AnchoredTop ? 0 : 1;
		panel.GrowHorizontal = Control.GrowDirection.Begin;
		panel.GrowVertical = Control.GrowDirection.Begin;
		panel.OffsetRight = AnchoredLeft ? 20 : -20;
		panel.OffsetBottom = AnchoredTop ? 20 : -20;
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
		resizeHandle.AnchorLeft = AnchoredLeft ? 0 : 1;
		resizeHandle.AnchorTop = AnchoredTop ? 0 : 1;
		resizeHandle.AnchorRight = AnchoredLeft ? 0 : 1;
		resizeHandle.AnchorBottom = AnchoredTop ? 0 : 1;
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
		ConfigureQuickScrollContainer();
		scroll.MouseFilter = Control.MouseFilterEnum.Pass;
		scroll.GuiInput += inputEvent => OnQuickGuiInput(inputEvent, scroll);
		panel.AddChild(scroll);

		rowsContainer = new VBoxContainer();
		rowsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rowsContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		rowsContainer.MouseFilter = Control.MouseFilterEnum.Pass;
		rowsContainer.GuiInput += inputEvent => OnQuickGuiInput(inputEvent, rowsContainer);
		rowsContainer.AddThemeConstantOverride("separation", QuickButtonSpacing);
		scroll.AddChild(rowsContainer);

		currentRow = AcquireRow();
		rowsContainer.AddChild(currentRow);
	}

	// Enterprise UI policy for the exported APK quick-command panel:
	// the panel must remain drag-scrollable, but the ScrollContainer bars are
	// intentionally hidden. The quick panel sits above gameplay text on phone
	// screens, so visible bars waste command space and look like tappable controls.
	// Do not enable Auto/ShowAlways unless a concrete accessibility or debugging
	// build requires visible scrollbars and that change is tested on Android.
	void ConfigureQuickScrollContainer()
	{
		if (scroll == null)
			return;

		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
		HideQuickScrollBar(scroll.GetHScrollBar());
		HideQuickScrollBar(scroll.GetVScrollBar());
	}

	// Defense in depth for theme/platform updates: ShowNever is the policy, and
	// this keeps any internal ScrollBar child non-interactive and size-free if
	// Godot still creates it for range management.
	static void HideQuickScrollBar(Godot.ScrollBar scrollBar)
	{
		if (scrollBar == null)
			return;
		scrollBar.Visible = false;
		scrollBar.MouseFilter = Control.MouseFilterEnum.Ignore;
		scrollBar.CustomMinimumSize = Vector2.Zero;
	}

	Rect2 GetSafeRect()
	{
		return EmueraContent.GetSafeViewportRect(GetViewport());
	}

	float GetSafeRightInset(Rect2 safeRect)
	{
		var viewportSize = GetViewport().GetVisibleRect().Size;
		return Mathf.Max(0, viewportSize.X - (safeRect.Position.X + safeRect.Size.X));
	}

	float GetSafeBottomInset(Rect2 safeRect)
	{
		var viewportSize = GetViewport().GetVisibleRect().Size;
		return Mathf.Max(0, viewportSize.Y - (safeRect.Position.Y + safeRect.Size.Y));
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
			var safeWidth = GetSafeRect().Size.X;
			var minWidth = EffectiveButtonWidth + ResizeHandleWidth;
			var maxWidth = Mathf.Max(minWidth, safeWidth - 40);
			// 拉条在面板右侧（贴左布局）→ 向右拖变宽；拉条在左侧（贴右布局）→ 向左拖变宽。
			if (AnchoredLeft)
				userWidth = Mathf.Clamp(resizeStartWidth + (mouseMotion.GlobalPosition.X - resizeStartMouseX), minWidth, maxWidth);
			else
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

	void OnQuickGuiInput(InputEvent @event, Control eventSource)
	{
		if (!IsControlAlive(eventSource))
			return;
		HandleQuickPointerInput(@event, true, null, null, eventSource);
	}

	void OnResizeHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			resizingWidth = mouseButton.Pressed;
			resizeStartMouseX = mouseButton.GlobalPosition.X;
			resizeStartWidth = panel != null ? panel.Size.X : EffectiveButtonWidth;
			GetViewport().SetInputAsHandled();
		}
	}

	public void Clear()
	{
		StopQuickInertia();
		scrollingByDrag = false;
		dragMoved = false;
		draggingWindow = false;
		dragButton = null;
		dragPointerIsTouch = false;
		dragPointerIndex = -1;
		buttons.Clear();
		quickButtonStyleCache.Clear();
		// 倒序 + GetChild(i)：ReleaseRow 会即时 RemoveChild，倒序遍历不受索引移动
		// 影响，同时避免 GetChildren() 每次重建都分配一个数组包装。
		for (int i = rowsContainer.GetChildCount() - 1; i >= 0; i--)
		{
			if (rowsContainer.GetChild(i) is HBoxContainer row)
				ReleaseRow(row);
			else
				rowsContainer.GetChild(i).QueueFree();
		}
		currentRow = AcquireRow();
		rowsContainer.AddChild(currentRow);
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(true);
	}

	public void AddButton(string text, Godot.Color color, string code, long generation)
	{
		var btn = AcquireButton();
		ConfigureButton(btn, text, color, code, generation);
		currentRow.AddChild(btn);
		buttons.Add(btn);
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void UpdateButtonGeneration(long generation)
	{
		for (int i = 0; i < buttons.Count; i++)
		{
			var btn = buttons[i];
			if (IsControlAlive(btn))
				btn.SetMeta("input_generation", generation);
		}
	}

	public void BeginBatch()
	{
		layoutBatchDepth++;
	}

	public void EndBatch()
	{
		if (layoutBatchDepth <= 0)
			return;

		layoutBatchDepth--;
		if (layoutBatchDepth > 0)
			return;

		bool pending = layoutBatchPending;
		bool keepBottom = layoutBatchKeepBottom;
		layoutBatchPending = false;
		layoutBatchKeepBottom = false;
		if (pending)
			UpdatePanelSize(keepBottom);
	}

	Panel AcquireButton()
	{
		while (buttonPool.Count > 0)
		{
			var pooled = buttonPool.Pop();
			if (IsControlAlive(pooled))
				return pooled;
		}

		var btn = new Panel();
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.MouseForcePassScrollEvents = false;
		btn.GuiInput += inputEvent => OnQuickButtonGuiInput(inputEvent, btn);
		// hover/press 只影响视觉（stylebox 微提亮 + 下压缩放），不改变命中、value 或信号。
		btn.MouseEntered += () => OnQuickButtonHoverChanged(btn, true);
		btn.MouseExited += () => OnQuickButtonHoverChanged(btn, false);

		var label = new Label();
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.OffsetLeft = QuickButtonPadding;
		label.OffsetTop = QuickButtonPadding;
		label.OffsetRight = -QuickButtonPadding;
		label.OffsetBottom = -QuickButtonPadding;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		label.VerticalAlignment = VerticalAlignment.Center;
		btn.AddChild(label);
		return btn;
	}

	void ConfigureButton(Panel btn, string text, Godot.Color color, string code, long generation)
	{
		btn.MouseFilter = quickInputEnabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		StyleQuickButton(btn, color);
		btn.SetMeta("quick_color", color);
		btn.Scale = Vector2.One;
		btn.Modulate = quickInputEnabled ? Colors.White : new Color(1, 1, 1, 0.55f);
		btn.CustomMinimumSize = new Vector2(EffectiveButtonWidth, QuickButtonHeight);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		btn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

		var label = GetButtonLabel(btn);
		if (label != null)
		{
			label.Text = (text ?? "").Trim();
			label.AddThemeColorOverride("font_color", color);
			if (fontFile != null)
				label.AddThemeFontOverride("font", fontFile);
			label.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
		}

		string inputCode = code;
		btn.SetMeta("input_code", inputCode);
		btn.SetMeta("input_generation", generation);
	}

	void OnQuickButtonGuiInput(InputEvent @event, Control btn)
	{
		if (!IsControlAlive(btn))
			return;
		HandleQuickPointerInput(@event, true, btn, null, btn);
	}

	HBoxContainer AcquireRow()
	{
		while (rowPool.Count > 0)
		{
			var row = rowPool.Pop();
			if (IsControlAlive(row))
				return row;
		}
		var fresh = new HBoxContainer();
		fresh.AddThemeConstantOverride("separation", QuickButtonSpacing);
		return fresh;
	}

	void ReleaseRow(HBoxContainer row)
	{
		if (!IsControlAlive(row))
			return;

		// 倒序遍历：ReleaseButton 会即时 RemoveChild，倒序不受索引移动影响。
		for (int i = row.GetChildCount() - 1; i >= 0; i--)
		{
			if (row.GetChild(i) is Panel button)
				ReleaseButton(button);
			else
				row.GetChild(i).QueueFree();
		}

		if (row.GetParent() != null)
			row.GetParent().RemoveChild(row);
		if (rowPool.Count < MaxPooledRows)
			rowPool.Push(row);
		else
			row.QueueFree();
	}

	void ReleaseButton(Panel btn)
	{
		if (!IsControlAlive(btn))
			return;

		if (btn.GetParent() != null)
			btn.GetParent().RemoveChild(btn);
		ResetButtonForPool(btn);
		if (buttonPool.Count < MaxPooledButtons)
			buttonPool.Push(btn);
		else
			btn.QueueFree();
	}

	void ResetButtonForPool(Panel btn)
	{
		// The quick panel is rebuilt often when the emulator prints a new command
		// generation. Pooling keeps Godot nodes and signal connections stable while
		// clearing all script-visible state before the next reuse.
		btn.Visible = true;
		btn.Modulate = Colors.White;
		// 按压缩放 tween（AnimateQuickPress）是 node-bound 且不随池重置停止；复用前
		// 必须 Kill，否则运行中的 tween 会继续把 Scale 写回 0.97，复用按钮卡在缩小态。
		var pressTween = btn.HasMeta("_press_tween")
			? btn.GetMeta("_press_tween", default(Variant)).As<Tween>()
			: null;
		if (pressTween != null && GodotObject.IsInstanceValid(pressTween))
			pressTween.Kill();
		btn.Scale = Vector2.One;
		btn.MouseFilter = Control.MouseFilterEnum.Ignore;
		btn.RemoveThemeStyleboxOverride("panel");
		btn.SetMeta("input_code", "");
		btn.SetMeta("input_generation", -1L);
		var label = GetButtonLabel(btn);
		if (label != null)
			label.Text = "";
	}

	bool HandleQuickPointerInput(InputEvent @event, bool acceptEvent, Control button = null, string inputCode = null, Control eventSource = null)
	{
		if (TryGetPointer(@event, out var position, out var pressed, out var released, out var motion, out var isTouch, out var pointerIndex))
		{
			if (!IsControlAlive(scroll) || !IsControlAlive(panel) || (!scrollingByDrag && !panel.GetGlobalRect().HasPoint(position)))
				return false;

			if (scrollingByDrag && motion && !IsActivePointer(isTouch, pointerIndex))
			{
				GetViewport().SetInputAsHandled();
				return true;
			}

			if (pressed && scrollingByDrag)
			{
				if (!IsActivePointer(isTouch, pointerIndex))
				{
					GetViewport().SetInputAsHandled();
					return true;
				}
				AcceptQuickInput(acceptEvent, eventSource);
				return true;
			}

			if (pressed)
			{
				StopQuickInertia();
				scrollingByDrag = true;
				dragMoved = false;
				quickScrollInteractionSerial++;
				scrollToBottomAfterLayout = false;
				dragButton = button;
				dragPointerIsTouch = isTouch;
				dragPointerIndex = pointerIndex;
				dragStartPosition = position;
				dragLastPosition = position;
				quickLastDragTick = Time.GetTicksMsec();
				// 悬浮宿主：内容无滚动余量时拖动 = 移动窗口；有滚动余量时拖动 = 滚动面板。
				draggingWindow = windowDragEnabled && !CanScrollQuickPanel();
				if (button != null)
				{
					AcceptQuickInput(acceptEvent, eventSource);
					AnimateQuickPress(button, GEmueraTheme.PressScale);
					return true;
				}
				return false;
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
				if (dragMoved && draggingWindow)
				{
					WindowDragRequested?.Invoke(position - dragLastPosition);
					dragLastPosition = position;
					AcceptQuickInput(acceptEvent, eventSource);
					return true;
				}
				if (dragMoved && scroll != null)
				{
					var rawScrollDelta = dragLastPosition - position;
					var appliedDelta = ScrollQuickBy(rawScrollDelta);
					UpdateQuickScrollVelocity(rawScrollDelta, appliedDelta);
					dragLastPosition = position;
					AcceptQuickInput(acceptEvent, eventSource);
					return true;
				}
				dragLastPosition = position;
				return false;
			}

			if (released)
			{
				bool handled = FinishQuickButtonPointer();
				if (handled)
					AcceptQuickInput(acceptEvent, eventSource);
				return handled;
			}
		}
		return false;
	}

	void AcceptQuickInput(bool acceptEvent, Control eventSource)
	{
		GetViewport().SetInputAsHandled();
	}

	bool FinishQuickButtonPointer()
	{
		if (!scrollingByDrag)
			return false;

		bool handled = false;
		string inputCode = null;
		Control activeButton = IsControlAlive(dragButton) ? dragButton : null;
		if (TryGetStringMeta(activeButton, "input_code", out var storedInputCode))
			inputCode = storedInputCode;
		if (!dragMoved && !string.IsNullOrEmpty(inputCode))
		{
			handled = true;
			if (quickInputEnabled)
			{
				long generation = 0;
				TryGetInt64Meta(activeButton, "input_generation", out generation);
				EmueraContent.instance?.SubmitQuickButtonInput(inputCode, generation);
			}
		}
		else if (dragMoved)
		{
			// 窗口拖动手势不产生滚动惯性（内容本就无滚动余量）。
			if (!draggingWindow)
				StartQuickInertia();
			handled = true;
		}
		if (activeButton != null)
			AnimateQuickPress(activeButton, 1.0f);
		scrollingByDrag = false;
		dragMoved = false;
		draggingWindow = false;
		dragButton = null;
		dragPointerIsTouch = false;
		dragPointerIndex = -1;
		return handled;
	}

	// 面板内容是否还有可滚动余量（决定拖动手势用于滚动还是移动窗口）。
	bool CanScrollQuickPanel()
	{
		return GetMaxVerticalScroll() > 0 || GetMaxHorizontalScroll() > 0;
	}

	bool IsActivePointer(bool isTouch, int pointerIndex)
	{
		if (dragPointerIsTouch != isTouch)
			return false;
		return !isTouch || dragPointerIndex == pointerIndex;
	}

	static bool TryGetPointer(InputEvent @event, out Vector2 position, out bool pressed, out bool released, out bool motion, out bool isTouch, out int pointerIndex)
	{
		position = Vector2.Zero;
		pressed = false;
		released = false;
		motion = false;
		isTouch = false;
		pointerIndex = -1;

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
			isTouch = true;
			pointerIndex = touch.Index;
			return true;
		}
		if (@event is InputEventScreenDrag drag)
		{
			position = drag.Position;
			motion = true;
			isTouch = true;
			pointerIndex = drag.Index;
			return true;
		}
		return false;
	}

	public void ShiftLine()
	{
			if (currentRow.GetChildCount() == 0)
			return;
		currentRow = AcquireRow();
		rowsContainer.AddChild(currentRow);
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void ShowPad()
	{
		Visible = true;
		EmitSignal(SignalName.PadShown);
	}

	public void HidePad()
	{
		Visible = false;
		EmitSignal(SignalName.PadHidden);
	}

	public bool IsShow => Visible;

	public void ApplyFont(Font font, int fontSize)
	{
		fontFile = font;
		this.fontSize = ConfiguredFontSize;
		for (int i = buttons.Count - 1; i >= 0; i--)
		{
			var btn = buttons[i];
			if (!IsControlAlive(btn))
			{
				buttons.RemoveAt(i);
				continue;
			}
			ApplyButtonMetrics(btn);
		}
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void RefreshSizing()
	{
		this.fontSize = ConfiguredFontSize;
		for (int i = buttons.Count - 1; i >= 0; i--)
		{
			var btn = buttons[i];
			if (!IsControlAlive(btn))
			{
				buttons.RemoveAt(i);
				continue;
			}
			ApplyButtonMetrics(btn);
		}
		if (userWidth > 0)
		{
			float minWidth = EffectiveButtonWidth + ResizeHandleWidth;
			userWidth = Mathf.Clamp(userWidth, minWidth, Mathf.Max(minWidth, GetSafeRect().Size.X - 40));
		}
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void RefreshSafeAreaLayout()
	{
		RefreshSizing();
	}

	public void SetInputEnabled(bool enabled)
	{
		if (quickInputEnabled == enabled)
			return;
		quickInputEnabled = enabled;
		if (panel != null)
			panel.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		if (resizeHandle != null)
			resizeHandle.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		if (scroll != null)
			scroll.MouseFilter = enabled ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
		if (rowsContainer != null)
			rowsContainer.MouseFilter = enabled ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
		for (int i = buttons.Count - 1; i >= 0; i--)
		{
			var btn = buttons[i];
			if (!IsControlAlive(btn))
			{
				buttons.RemoveAt(i);
				continue;
			}
			btn.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
			btn.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.55f);
		}
	}

	int EffectiveFontSize => fontSize > 0 ? fontSize : ConfiguredFontSize;

	int EffectiveButtonWidth => ConfiguredButtonWidth;

	int QuickButtonHeight => EffectiveFontSize * 3 + QuickButtonPadding * 2;

	/// <summary>
	/// Quick button bg 由文字色的反色推导（era 快键语义：保证文字可读）。
	/// 该推导逻辑与点击/拖拽/输入完全解耦，只改变背景呈现。
	/// </summary>
	Color ComputeQuickButtonBg(Color textColor)
	{
		var bgSource = IsMidGray(textColor)
			? new Color(MinorShift.Emuera.Config.BackColor.r, MinorShift.Emuera.Config.BackColor.g, MinorShift.Emuera.Config.BackColor.b, 1)
			: textColor;
		return new Color(1 - bgSource.R, 1 - bgSource.G, 1 - bgSource.B, 0.75f);
	}

	StyleBoxFlat CreateQuickButtonStyle(Color bgColor)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		// 细边框增强深色现代的按钮轮廓，透明部分不遮挡游戏文字。
		style.BorderColor = new Color(0, 0, 0, 0.22f);
		style.SetBorderWidthAll(1);
		style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 4;
		style.ContentMarginLeft = QuickButtonPadding;
		style.ContentMarginRight = QuickButtonPadding;
		style.ContentMarginTop = QuickButtonPadding;
		style.ContentMarginBottom = QuickButtonPadding;
		return style;
	}

	void StyleQuickButton(Panel btn, Color textColor)
	{
		uint key = ColorCacheKey(ComputeQuickButtonBg(textColor));
		if (!quickButtonStyleCache.TryGetValue(key, out var normal))
		{
			normal = CreateQuickButtonStyle(ComputeQuickButtonBg(textColor));
			quickButtonStyleCache[key] = normal;
		}

		btn.AddThemeStyleboxOverride("panel", normal);
	}

	void OnQuickButtonHoverChanged(Panel btn, bool hovering)
	{
		if (btn == null || !IsControlAlive(btn))
			return;

		if (hovering)
		{
			// hover 只把背景微提亮（+8% 明度），命中/value/信号不变。
			if (TryGetColorMeta(btn, "quick_color", out var color))
			{
				uint key = ColorCacheKey(ComputeQuickButtonBg(color));
				if (!quickButtonHoverStyleCache.TryGetValue(key, out var hover))
				{
					hover = CreateQuickButtonStyle(GEmueraTheme.Lighten(ComputeQuickButtonBg(color), 0.08f));
					quickButtonHoverStyleCache[key] = hover;
				}
				btn.AddThemeStyleboxOverride("panel", hover);
			}
		}
		else if (TryGetColorMeta(btn, "quick_color", out var restoreColor))
		{
			StyleQuickButton(btn, restoreColor);
		}
	}

	bool TryGetColorMeta(Control control, string name, out Color color)
	{
		color = Colors.White;
		if (control == null || !IsControlAlive(control))
			return false;
		if (!control.HasMeta(name))
			return false;
		color = control.GetMeta(name).As<Godot.Color>();
		return true;
	}

	/// <summary>按下/抬起的下压缩放动效（0.10s ease-out），仅视觉层。</summary>
	void AnimateQuickPress(Control btn, float targetScale)
	{
		if (btn == null || !IsControlAlive(btn))
			return;
		btn.PivotOffset = btn.Size * 0.5f;
		// 记录 tween 引用：ResetButtonForPool 复用节点前 Kill，避免旧 press 动画
		// 把复用的按钮写回缩放 0.97。
		var prevTween = btn.HasMeta("_press_tween")
			? btn.GetMeta("_press_tween", default(Variant)).As<Tween>()
			: null;
		if (prevTween != null && GodotObject.IsInstanceValid(prevTween))
			prevTween.Kill();
		var tween = btn.CreateTween();
		btn.SetMeta("_press_tween", tween);
		tween.BindNode(btn);
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(btn, "scale", new Vector2(targetScale, targetScale), GEmueraTheme.PressSeconds);
	}

	static uint ColorCacheKey(Color color)
	{
		uint r = (uint)Mathf.Clamp(Mathf.RoundToInt(color.R * 255.0f), 0, 255);
		uint g = (uint)Mathf.Clamp(Mathf.RoundToInt(color.G * 255.0f), 0, 255);
		uint b = (uint)Mathf.Clamp(Mathf.RoundToInt(color.B * 255.0f), 0, 255);
		uint a = (uint)Mathf.Clamp(Mathf.RoundToInt(color.A * 255.0f), 0, 255);
		return r | (g << 8) | (b << 16) | (a << 24);
	}

	void ApplyFontToButtonLabel(Control btn)
	{
		var label = GetButtonLabel(btn);
		if (label == null)
			return;
		if (fontFile != null)
			label.AddThemeFontOverride("font", fontFile);
		label.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
	}

	Label GetButtonLabel(Control btn)
	{
		if (btn == null || btn.GetChildCount() == 0)
			return null;
		// 按钮结构固定为唯一 Label 子节点，直接取第 0 个避免 GetChildren() 分配。
		return btn.GetChild(0) as Label;
	}

	void ApplyButtonMetrics(Control btn)
	{
		if (btn == null)
			return;
		btn.CustomMinimumSize = new Vector2(EffectiveButtonWidth, QuickButtonHeight);
		btn.Size = new Vector2(EffectiveButtonWidth, QuickButtonHeight);
		ApplyFontToButtonLabel(btn);
	}

	bool IsMidGray(Color color)
	{
		return Mathf.Abs(color.R - 0.5f) <= 0.063f
			&& Mathf.Abs(color.G - 0.5f) <= 0.063f
			&& Mathf.Abs(color.B - 0.5f) <= 0.063f;
	}

	void RequestPanelSizeUpdate(bool keepBottom)
	{
		if (layoutBatchDepth > 0)
		{
			layoutBatchPending = true;
			layoutBatchKeepBottom = layoutBatchKeepBottom || keepBottom;
			return;
		}

		UpdatePanelSize(keepBottom);
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
		if (!IsControlAlive(panel) || !IsControlAlive(scroll) || !IsControlAlive(rowsContainer))
		{
			layoutUpdateQueued = false;
			return;
		}
		layoutUpdateQueued = false;
		var safeSize = GetSafeRect().Size;
		var contentSize = GetQuickContentSize();
		float minWidth = EffectiveButtonWidth;
		float maxWidth = Mathf.Max(minWidth, safeSize.X * 0.6f);
		float maxHeight = Mathf.Max(QuickButtonHeight, safeSize.Y - 66);
		float autoWidth = Mathf.Min(Mathf.Max(contentSize.X, minWidth), maxWidth);
		float width = userWidth > 0
			? Mathf.Clamp(userWidth, minWidth, Mathf.Max(minWidth, safeSize.X - 40))
			: autoWidth;
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

		var safeSize = GetSafeRect().Size;
		var contentSize = GetQuickContentSize();
		float maxHeight = Mathf.Max(QuickButtonHeight, safeSize.Y - 66);
		float minWidth = EffectiveButtonWidth;
		float maxWidth = Mathf.Max(minWidth, safeSize.X * 0.6f);
		float width = userWidth > 0
			? Mathf.Clamp(userWidth, minWidth, Mathf.Max(minWidth, safeSize.X - 40))
			: Mathf.Min(Mathf.Max(contentSize.X, minWidth), maxWidth);
		float height = Mathf.Min(contentSize.Y, maxHeight);
		ApplyPanelSize(width, height);
	}

	void ApplyPanelSize(float width, float height)
	{
		if (!IsControlAlive(panel) || !IsControlAlive(resizeHandle))
			return;
		var safeRect = GetSafeRect();
		float rightMargin = 20 + GetSafeRightInset(safeRect);
		float bottomMargin = 20 + GetSafeBottomInset(safeRect);
		float leftMargin = 20;
		float topMargin = 20;

		// 水平锚点：默认贴右（面板在右下角），翻转/悬浮 → 贴左。
		// 宽度调节条始终在面板内侧（贴右 → 拉条在左；贴左 → 拉条在右），
		// 翻转后用户仍能从屏幕内侧拖宽面板。
		if (AnchoredLeft)
		{
			panel.AnchorLeft = 0;
			panel.AnchorRight = 0;
			panel.OffsetLeft = leftMargin;
			panel.OffsetRight = leftMargin + width;
			resizeHandle.AnchorLeft = 0;
			resizeHandle.AnchorRight = 0;
			resizeHandle.OffsetLeft = leftMargin + width;
			resizeHandle.OffsetRight = leftMargin + width + ResizeHandleWidth;
		}
		else
		{
			panel.AnchorLeft = 1;
			panel.AnchorRight = 1;
			panel.OffsetLeft = -rightMargin - width;
			panel.OffsetRight = -rightMargin;
			resizeHandle.AnchorLeft = 1;
			resizeHandle.AnchorRight = 1;
			resizeHandle.OffsetLeft = -rightMargin - width - ResizeHandleWidth;
			resizeHandle.OffsetRight = -rightMargin - width;
		}

		if (AnchoredTop)
		{
			panel.AnchorTop = 0;
			panel.AnchorBottom = 0;
			panel.OffsetTop = topMargin;
			panel.OffsetBottom = topMargin + height;
			resizeHandle.AnchorTop = 0;
			resizeHandle.AnchorBottom = 0;
			resizeHandle.OffsetTop = topMargin;
			resizeHandle.OffsetBottom = topMargin + height;
		}
		else
		{
			panel.AnchorTop = 1;
			panel.AnchorBottom = 1;
			panel.OffsetTop = -bottomMargin - height;
			panel.OffsetBottom = -bottomMargin;
			resizeHandle.AnchorTop = 1;
			resizeHandle.AnchorBottom = 1;
			resizeHandle.OffsetTop = -bottomMargin - height;
			resizeHandle.OffsetBottom = -bottomMargin;
		}
	}

	Vector2 ScrollQuickBy(Vector2 delta)
	{
		if (scroll == null)
			return Vector2.Zero;
		delta *= EmueraContent.ButtonDragSensitivity;
		return ApplyQuickScrollDelta(delta);
	}

	Vector2 ApplyQuickScrollDelta(Vector2 delta)
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

		var instantVelocity = rawScrollDelta * EmueraContent.ButtonDragSensitivity / elapsed;
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
		{
			quickInertiaActive = true;
			SetProcess(true);
		}
		else
			StopQuickInertia();
	}

	void StopQuickInertia()
	{
		quickInertiaActive = false;
		quickScrollVelocity = Vector2.Zero;
		quickInertiaRemainder = Vector2.Zero;
		quickLastDragTick = 0;
		if (IsInsideTree())
			SetProcess(false);
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
			var appliedDelta = ApplyQuickScrollDelta(roundedDelta);
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
		float contentWidth = Mathf.Max(rowsContainer.Size.X, GetQuickContentSize().X);
		return Mathf.RoundToInt(Mathf.Max(0, contentWidth - scroll.Size.X));
	}

	int GetMaxVerticalScroll()
	{
		if (scroll == null || rowsContainer == null)
			return 0;
		float contentHeight = Mathf.Max(rowsContainer.Size.Y, GetQuickContentSize().Y);
		return Mathf.RoundToInt(Mathf.Max(0, contentHeight - scroll.Size.Y));
	}

	void MarkQuickContentSizeDirty()
	{
		quickContentSizeDirty = true;
	}

	Vector2 GetQuickContentSize()
	{
		if (rowsContainer == null)
			return Vector2.Zero;
		if (quickContentSizeDirty)
		{
			cachedQuickContentSize = rowsContainer.GetCombinedMinimumSize();
			quickContentSizeDirty = false;
		}
		return cachedQuickContentSize;
	}

	static void EnsureSettingsLoaded()
	{
		if (configuredButtonWidth > 0 && configuredFontSize > 0)
			return;
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		configuredButtonWidth = Mathf.Clamp(
			Mathf.RoundToInt((float)(double)cfg.GetValue(SettingsSection, QuickButtonWidthKey, DefaultQuickButtonWidth)),
			MinQuickButtonWidth,
			MaxQuickButtonWidth);
		configuredFontSize = Mathf.Clamp(
			Mathf.RoundToInt((float)(double)cfg.GetValue(SettingsSection, QuickButtonFontSizeKey, DefaultQuickButtonFontSize)),
			MinQuickButtonFontSize,
			MaxQuickButtonFontSize);
		configuredFlip = cfg.GetValue(SettingsSection, QuickFlipKey, false).AsBool();
		configuredFloating = cfg.GetValue(SettingsSection, QuickFloatingKey, false).AsBool();
	}

	static void SaveSetting(string key, int value)
	{
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		cfg.SetValue(SettingsSection, key, value);
		cfg.Save(SettingsPath);
	}

	static void SaveSetting(string key, bool value)
	{
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		cfg.SetValue(SettingsSection, key, value);
		cfg.Save(SettingsPath);
	}

	static bool IsControlAlive(Control control)
	{
		if (control == null)
			return false;
		try
		{
			return GodotObject.IsInstanceValid(control) && !control.IsQueuedForDeletion();
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	static bool TryGetStringMeta(Control control, string key, out string value)
	{
		value = null;
		if (!IsControlAlive(control))
			return false;
		try
		{
			if (!control.HasMeta(key))
				return false;
			value = control.GetMeta(key).As<string>();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	static bool TryGetInt64Meta(Control control, string key, out long value)
	{
		value = 0;
		if (!IsControlAlive(control))
			return false;
		try
		{
			if (!control.HasMeta(key))
				return false;
			value = control.GetMeta(key).AsInt64();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	static bool TryGetBoolMeta(Control control, string key, out bool value)
	{
		value = false;
		if (!IsControlAlive(control))
			return false;
		try
		{
			if (!control.HasMeta(key))
				return false;
			value = control.GetMeta(key).AsBool();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}
}
