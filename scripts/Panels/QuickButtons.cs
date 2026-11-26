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
	bool floatingOverlayMode;
	bool draggingWindow;
	bool floatingDragActive;
	Vector2 floatingDragLastPosition;
	Vector2 floatingPanelPosition = new Vector2(-1, -1);
	// 浮层坐标物化时的安全区（位置+尺寸）：坐标只对物化它时的安全区有意义，
	// 安全区一变（转屏/折叠/分屏/分辨率与水平留白调整）旧坐标即视为陈旧——可能
	// 落在新区域的边缘甚至屏外（2026-11-22 用户反馈「竖屏拖到下面横屏很难看到」），
	// 此时重置为未定位态、下一次布局落到当前方向目标角。
	Rect2 floatingPanelAnchorRect;
	bool floatingPanelAnchorRectValid;
	Control floatingDragBar;
	Label floatingDragGrip;
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
	// 浮动面板位置语义版本；见 EnsureSettingsLoaded 的迁移说明。
	const int FloatingDragBarHeight = 26;
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

	// 面板水平翻转：默认靠右（右下角），翻转后靠左（左下角，内容不镜像）；
	// 宽度调节条同步移到面板内侧。默认关闭。
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

	/// <summary>面板拖动位移（逻辑像素；1:1 内容下即物理像素），由桌面原生窗口宿主订阅以移动窗口。</summary>
	public event Action<Vector2> WindowDragRequested;

	/// <summary>
	/// 移动端/无原生窗口环境的悬浮模式：面板不放进 Window，而是留在主画布内由
	/// 本类自行摆放位置；顶部提供专用拖动条（不依赖 OS 标题栏，也不使用任何
	/// Windows 原生窗口组件）。桌面端仍走 QuickFloatingWindow 原生窗口。
	/// </summary>
	public bool FloatingOverlayMode
	{
		get => floatingOverlayMode;
		set
		{
			if (floatingOverlayMode == value)
				return;
			floatingOverlayMode = value;
			if (floatingDragBar != null)
				floatingDragBar.Visible = value;
			RequestPanelSizeUpdate(false);
		}
	}

	/// <summary>当前悬浮面板左上角（画布坐标）；仅在 <see cref="FloatingOverlayMode"/> 下有效。</summary>
	public Vector2 FloatingPanelPosition => floatingPanelPosition;

	/// <summary>
	/// 拖动条位移：立即同步落位并钳制（不走 RequestPanelSizeUpdate 的异步布局——
	/// 那条路径内部 await 一帧，拖动时面板跟不上手指）。
	/// </summary>
	public void MoveFloatingPanelBy(Vector2 delta)
	{
		if (!floatingOverlayMode || delta == Vector2.Zero)
			return;
		floatingPanelPosition += delta;
		ApplyFloatingOverlayLayoutNow();
	}

	// 拖动/翻转镜像共用的即时落位：不动面板尺寸，按当前尺寸重算钳制位置并同步
	// 摆放面板/调节条/拖动条三件套。悬浮拖动必须同帧生效（2026-11-22 用户反馈
	// 「移动效果差」的修复之一；另一根因见 ToDragBarCanvasPosition 注释）。
	void ApplyFloatingOverlayLayoutNow()
	{
		if (panel == null || !IsControlAlive(panel) || !IsControlAlive(resizeHandle))
			return;
		float width = panel.Size.X;
		float height = panel.Size.Y;
		if (width <= 0 || height <= 0)
			return;
		ApplyFloatingOverlayPanelSize(width, height);
	}

	/// <summary>
	/// 「Quick面板翻转」开关切换后的即时响应（悬浮浮层）：把面板水平镜像到对侧
	/// 并持久化。内嵌锚点模式由 RefreshSizing 的重排覆盖，不需要此调用。
	/// </summary>
	public void NotifyFlipSettingChanged()
	{
		if (!floatingOverlayMode)
			return;
		if (floatingPanelPosition.X < 0 || floatingPanelPosition.Y < 0)
		{
			// 尚未定位过（未显示/未拖动）：保持未定位态，下次布局按新翻转方向取默认位。
			return;
		}
		var safeRect = GetSafeRect();
		float width = IsControlAlive(panel) ? panel.Size.X : EffectiveButtonWidth;
		if (width <= 0)
			return;
		float mirroredX = safeRect.Position.X + safeRect.Size.X
			- (floatingPanelPosition.X - safeRect.Position.X) - width;
		floatingPanelPosition = new Vector2(mirroredX, floatingPanelPosition.Y);
		ApplyFloatingOverlayLayoutNow();
	}

	/// <summary>
	/// 「回到初始位置」（2026-11-22 用户要求的功能）：把浮层送回当前方向目标角
	/// （右下；翻转开=左下）。拖动条右端按钮与隐藏/打开边沿共用此原语。
	/// </summary>
	public void RehomeOverlayPanel()
	{
		if (!floatingOverlayMode)
			return;
		floatingPanelPosition = new Vector2(-1, -1);
		RequestPanelSizeUpdate(false);
	}

	/// <summary>宿主丢失/迁移时复位悬浮层状态，避免旧位置影响常规锚点布局。</summary>
	public void ResetFloatingOverlayState()
	{
		floatingOverlayMode = false;
		floatingDragActive = false;
		if (floatingDragBar != null)
			floatingDragBar.Visible = false;
	}

	// 布局锚点派生（2026-11-22 用户两步裁定）：
	// ① 垂直恒贴底、默认右下角——推翻 2026-11-16 补遗 11 的「一律贴顶」；
	// ② 面板翻转语义收敛为纯位置换边（内容不镜像）：翻转后=左下角。
	// 悬浮且真正被 Window 承载（桌面端）：面板在窗口内容区内贴左贴顶，
	// 屏幕位置由 Window 自身决定。
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
		// floatingPanelPosition 保持字段初始化的未定位态（-1,-1）：位置是纯运行期
		// 状态，不持久化（2026-11-22 用户澄清「返回初始位置」后彻底拔掉坐标存取，
		// 陈旧配置坐标是「重开后钉死在旧位置」的根源）。
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

		// 移动端悬浮拖动条：不依赖原生标题栏；只有 overlay 悬浮模式可见。
		// 拖动条位于面板上方（不遮挡按钮），拖动时移动整个面板并持久化位置。
		floatingDragBar = new Control();
		floatingDragBar.MouseFilter = Control.MouseFilterEnum.Stop;
		floatingDragBar.MouseDefaultCursorShape = Control.CursorShape.Move;
		floatingDragBar.Visible = false;
		floatingDragBar.GuiInput += OnFloatingDragBarGuiInput;
		layerRoot.AddChild(floatingDragBar);
		var dragBarBg = new ColorRect();
		dragBarBg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		dragBarBg.Color = new Color(0.12f, 0.13f, 0.16f, 0.72f);
		dragBarBg.MouseFilter = Control.MouseFilterEnum.Ignore;
		floatingDragBar.AddChild(dragBarBg);
		floatingDragGrip = new Label();
		floatingDragGrip.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		floatingDragGrip.HorizontalAlignment = HorizontalAlignment.Center;
		floatingDragGrip.VerticalAlignment = VerticalAlignment.Center;
		floatingDragGrip.Text = "⋮⋮";
		floatingDragGrip.MouseFilter = Control.MouseFilterEnum.Ignore;
		floatingDragGrip.AddThemeFontSizeOverride("font_size", 12);
		floatingDragGrip.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
		floatingDragBar.AddChild(floatingDragGrip);

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
		// 悬浮浮层在「隐藏→打开」边沿回到初始角（右下；「Quick面板翻转」开=左下）：
		// 位置记忆只在同一次打开的会话内有效（拖拽/翻转镜像仍即时生效）。陈旧绝对
		// 坐标跨关闭/跨方向沿用是「面板丢失」的来源（2026-11-22 用户反馈：竖屏拖过
		// 的位置重开后把面板带去意想不到的地方），隐藏即弃位。只在边沿重置——自动
		// 唤起路径会在面板已可见时重复调 ShowPad，不能把用户刚拖的位置拽回角落。
		if (!Visible && floatingOverlayMode)
		{
			// 无条件重置+请求布局：空面板（游戏未定义 QUICKBUTTON）没有任何其他
			// 布局触发器，若只在「有旧坐标」时才请求，未定位态永远不会物化，
			// 拖动条/面板停在 _Ready 默认锚点上（run_overlay_check 首轮 FAIL 根因）。
			floatingPanelPosition = new Vector2(-1, -1);
			RequestPanelSizeUpdate(false);
		}
		Visible = true;
		EmitSignal(SignalName.PadShown);
	}

	public void HidePad()
	{
		// 关闭即回角（2026-11-22 用户澄清：「返回初始位置」在关闭那一刻立即生效，
		// 不等下次打开——否则重开仍见旧位，观感是「钉死在最后移动的位置」）。
		// 重置为未定位态后由布局路径落到当前方向目标角（右下；翻转开=左下）。
		if (floatingOverlayMode && floatingPanelPosition.X >= 0)
		{
			floatingPanelPosition = new Vector2(-1, -1);
			RequestPanelSizeUpdate(false);
		}
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
		// 安全区与「坐标物化时的安全区」不一致 → 坐标陈旧，重置为未定位态，
		// 下一次布局落到当前方向目标角（右下；翻转开=左下）。
		var safeRect = GetSafeRect();
		if (floatingOverlayMode && floatingPanelAnchorRectValid
			&& (safeRect.Position != floatingPanelAnchorRect.Position
				|| safeRect.Size != floatingPanelAnchorRect.Size))
		{
			floatingPanelPosition = new Vector2(-1, -1);
			RequestPanelSizeUpdate(false);
		}
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
		if (floatingOverlayMode)
		{
			ApplyFloatingOverlayPanelSize(width, height);
			return;
		}
		var safeRect = GetSafeRect();
		float rightMargin = 20 + GetSafeRightInset(safeRect);
		float bottomMargin = 20 + GetSafeBottomInset(safeRect);
		float leftMargin = 20;
		float topMargin = 20;
		// 竖屏时系统菜单栏在右侧竖排（12 个图标向下延伸），面板贴右会直接盖住它，
		// 因此额外让开一个按钮位 + 间距；横屏时菜单栏在顶部横排，只需让开顶部高度。
		bool portraitMenuBar = safeRect.Size.Y > safeRect.Size.X;
		if (portraitMenuBar)
			rightMargin += 48f + 16f;

		// 水平锚点：默认贴右（右下角）；「面板翻转」开启后贴左（左下角，纯位置
		// 换边，内容不镜像）；桌面悬浮承载（Window 内容区内）也贴左。宽度调节条
		// 始终在面板内侧（贴右 → 拉条在左；贴左 → 拉条在右），用户总能从屏幕
		// 内侧拖宽面板。
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

		// 垂直锚点：贴底（2026-11-22 用户裁定默认右下角、翻转只换水平边=左下角，
		// 推翻 2026-11-16「一律贴顶贴着唤出菜单」的决策）。旋转后锚点跟随新视口，
		// RefreshSafeAreaLayout 会按新安全区重新钳制边距，面板始终落在同一侧底部。
		// 悬浮承载（桌面 Window 宿主）仍贴顶 20：窗口内容区内部布局，屏幕位置由
		// Window 自身决定。贴底时保留 maxHeight 顶部的 66px 预留，高面板不会
		// 盖住顶部横排菜单栏。
		float anchorV = AnchoredTop ? 0f : 1f;
		float verticalMargin = AnchoredTop ? topMargin : bottomMargin;
		panel.AnchorTop = anchorV;
		panel.AnchorBottom = anchorV;
		panel.OffsetTop = AnchoredTop ? verticalMargin : -verticalMargin - height;
		panel.OffsetBottom = AnchoredTop ? verticalMargin + height : -verticalMargin;
		resizeHandle.AnchorTop = anchorV;
		resizeHandle.AnchorBottom = anchorV;
		resizeHandle.OffsetTop = panel.OffsetTop;
		resizeHandle.OffsetBottom = panel.OffsetBottom;
	}

	// 移动端悬浮层布局：面板位置完全由 floatingPanelPosition 决定（安全区内钳制），
	// 面板上方保留专用拖动条；不使用 Window/OS 标题栏，因此 Android 同样可用。
	void ApplyFloatingOverlayPanelSize(float width, float height)
	{
		var safeRect = GetSafeRect();
		float barHeight = FloatingDragBarHeight;
		Vector2 pos = floatingPanelPosition;
		if (pos.X < 0 || pos.Y < 0)
		{
			// 默认贴底、贴右；「Quick面板翻转」开启时镜像到贴左（2026-11-22 翻转
			// 语义收敛：纯位置换边，悬浮浮层默认位同样响应）。已手动拖动过的面板
			// 保存有明确坐标，不受默认变化影响；旋转后由下方安全区钳制兜底。
			const float EdgeMargin = 20f;
			float defaultY = safeRect.Position.Y + safeRect.Size.Y - height - barHeight - EdgeMargin;
			pos = FlipEnabled
				? new Vector2(safeRect.Position.X + EdgeMargin, defaultY)
				: new Vector2(safeRect.Position.X + safeRect.Size.X - width - EdgeMargin, defaultY);
		}
		float maxX = Mathf.Max(safeRect.Position.X, safeRect.Position.X + safeRect.Size.X - width);
		float maxY = Mathf.Max(safeRect.Position.Y, safeRect.Position.Y + safeRect.Size.Y - height - barHeight);
		pos.X = Mathf.Clamp(pos.X, safeRect.Position.X, maxX);
		pos.Y = Mathf.Clamp(pos.Y, safeRect.Position.Y, maxY);
		floatingPanelPosition = pos;
		floatingPanelAnchorRect = safeRect;
		floatingPanelAnchorRectValid = true;

		SetTopLeftLayout(panel, pos.X, pos.Y + barHeight, width, height);
		SetTopLeftLayout(resizeHandle, pos.X + width, pos.Y + barHeight, ResizeHandleWidth, height);
		if (floatingDragBar != null)
		{
			floatingDragBar.Visible = true;
			SetTopLeftLayout(floatingDragBar, pos.X, pos.Y, width, barHeight);
		}
	}

	static void SetTopLeftLayout(Control control, float x, float y, float width, float height)
	{
		if (!IsControlAlive(control))
			return;
		control.AnchorLeft = 0;
		control.AnchorTop = 0;
		control.AnchorRight = 0;
		control.AnchorBottom = 0;
		control.OffsetLeft = x;
		control.OffsetTop = y;
		control.OffsetRight = x + width;
		control.OffsetBottom = y + height;
	}

	void OnFloatingDragBarGuiInput(InputEvent @event)
	{
		if (!floatingOverlayMode)
			return;
		if (@event is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				floatingDragActive = true;
				floatingDragLastPosition = ToDragBarCanvasPosition(touch.Position);
				SetFloatingDragBarVisual(active: true);
			}
			else if (floatingDragActive)
			{
				floatingDragActive = false;
				SetFloatingDragBarVisual(active: false);
			}
			GetViewport().SetInputAsHandled();
			return;
		}
		if (@event is InputEventScreenDrag drag && floatingDragActive)
		{
			var canvasPos = ToDragBarCanvasPosition(drag.Position);
			MoveFloatingPanelBy(canvasPos - floatingDragLastPosition);
			floatingDragLastPosition = canvasPos;
			GetViewport().SetInputAsHandled();
			return;
		}
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			if (mouseButton.Pressed)
			{
				floatingDragActive = true;
				floatingDragLastPosition = mouseButton.GlobalPosition;
				SetFloatingDragBarVisual(active: true);
			}
			else if (floatingDragActive)
			{
				floatingDragActive = false;
				SetFloatingDragBarVisual(active: false);
			}
			GetViewport().SetInputAsHandled();
			return;
		}
		if (@event is InputEventMouseMotion mouseMotion && floatingDragActive)
		{
			MoveFloatingPanelBy(mouseMotion.GlobalPosition - floatingDragLastPosition);
			floatingDragLastPosition = mouseMotion.GlobalPosition;
			GetViewport().SetInputAsHandled();
		}
	}

	// GuiInput 里的触摸/拖拽 position 是控件局部坐标（_gui_call_input 会按控件
	// 变换本地化）：拖动条随面板移动，直接用局部差分会被「控件自身位移」抵消——
	// 手指世界系走 d、局部坐标只报回 d-面板位移，收敛后面板仅以约一半手指速度
	// 跟随（2026-11-22 用户反馈「移动效果差/不灵敏」的主根因）。换算到画布坐标
	// 再差分即稳定；鼠标事件的 GlobalPosition 本就是视口画布坐标，无需换算。
	Vector2 ToDragBarCanvasPosition(Vector2 localPosition)
	{
		return IsControlAlive(floatingDragBar)
			? floatingDragBar.GetGlobalTransform() * localPosition
			: localPosition;
	}

	// 拖动中把拖动条提亮，给「已抓住」的视觉反馈；松手复原。
	void SetFloatingDragBarVisual(bool active)
	{
		if (floatingDragBar != null && IsControlAlive(floatingDragBar))
			floatingDragBar.Modulate = active ? new Color(1.3f, 1.3f, 1.35f, 1f) : Colors.White;
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
		// 位置坐标不持久化（QuickFloatingX/Y 于 2026-11-22 起停止读写）：悬浮面板
		// 每次打开/关闭/跨方向旋转都回到当前方向的目标角，旧配置里的遗留键不再被
		// 读取，也不删除用户配置文件中的既有键。
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
