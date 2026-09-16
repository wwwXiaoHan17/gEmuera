using Godot;
using MinorShift._Library;

/// <summary>
/// main.tscn 的「鼠标」虚拟鼠标组件（组合式：一个 Node2D 根 + Sprite2D 视觉 + 四个子 Area2D 命中区域
/// + 代码构建的 L/R/M 按钮标注 + 触控板手势）。它是唯一的虚拟指针输入设备：既是可点击的
/// 「遥控器」机身，也是全屏「触控板」——滑动机身外任意处移动光标、单击提交左键。
///
/// 区域布局（由美术在编辑器摆放，本组件只读取，绝不移动/缩放它们的局部变换）：
///   左键区域 —— 单击 = PC 左键点击；长按 = PC 左键长按（WinInput 键保持按下，供 GETKEY）。
///   右键区域 —— 单击 = PC 右键点击（机身标注 R；去掉旧版"隐藏区域+长按"）。
///   滚轮区域 —— 单击 = PC 中键点击；按下后上下滑动 = 即时累计滚轮上/下滑（机身标注 M）。
///   移动区域 —— 拖动超过 4px 即 1:1 重定位机身（仅移动机身，不动光标）。
///   机身以外 —— 触控板：滑动移动光标，单击提交左键（恢复上一版 VirtualCursor 的打开光标行为）。
///
/// 交互模型（2026-08 用户确认：遥控器 + 触控板）。
/// Why：拖动机身是为了重定位"遥控器"（避免挡住内容），而不是拖动游戏指针；指针移动交给
///      触控板，指针语义（MOUSEX/MOUSEY + hover/tooltip）由 EmueraContent 持有。
/// What/How：
///   - 机身按钮的点击落在「共享光标」位置（EmueraContent.VirtualCursorCommitClick），
///     而非机身箭头热点；因此按钮点击永远命中"光标正在指向"的内容。
///   - 移动区域拖动只重定位机身（MoveComponentBy 不再 SyncCursor）。
///   - 光标由机身外滑动移动（MoveCursorBy），与旧 VirtualCursor 触控板一致。
///
/// Godot 组合优于继承：把旧 VirtualCursor（触摸板设备）合并进本组件并删除其文件/场景，
/// 消除"两个设备抢输入"的冗余；视觉标注用子节点组合，命中仍走既有碰撞区域
/// （约束：碰撞区域不能动——只读引用 + 手算命中，绝不写 Transform）。
///
/// 2026-08 重定焦：
///   - 关闭时隐藏机身（修复旧实现关闭后机身 Sprite 仍显示的视觉残留）。
///   - 移动/滚轮改为按下即时手势分流（HandleDrag 内直接判定，不再等 _Process 长按）；
///     长按语义仅保留左键按住（供 GETKEY/GETKEYTRIGGERED 读取）。
/// </summary>
public partial class VirtualMouse : Node2D
{
	public const int VK_LEFT = 0x01;
	public const int VK_RIGHT = 0x02;
	public const int VK_MIDDLE = 0x04;

	const float LongPressDuration = 0.45f;
	const float TapThresholdSquared = 10f * 10f;
	// 移动区即时拖动阈值：拖动超过该距离即开始重定位机身（无需长按，解决 0.45s 延迟痛点）。
	const float MoveThresholdSquared = 4f * 4f;
	// 触控板拖动阈值：超过该距离视为移动光标（松手不提交左键），未超过视为单击。
	const float DragThresholdSquared = 10f * 10f;
	// 滚轮一格需要的累计滑动像素（需求语义：上/下滑 = 滚轮上/下）。
	// 2026-08：从 48 降到 24——滚轮胶囊屏上仅 ~72px 高，48px/格几乎要滑满整个滚轮，
	// 造成"滚轮区不好滚"（问题4）；24px/格一次全滑可出 2~3 格，滚动更跟手。
	const float WheelStepThresholdPx = 24f;
	const float WheelAccumMaxPx = 200f;
	// 滚轮命中半径放宽系数（仅手算命中测试用，不改碰撞几何——约束：碰撞区域不能动）。
	// 胶囊屏上仅 ~32px 宽，命中带太窄；放宽到 ~1.4× 让滚动条更容易被点到。
	const float WheelHitRadiusScale = 1.4f;

	/// <summary>滚轮一步滚动的内容像素（滚动量再 × ConfiguredSensitivity）。
	/// Why：与 WheelStepThresholdPx（手指滑动阈值）共同决定滚轮手感；集中在此一处，
	/// 供 EmueraContent.VirtualCursorScrollWheel 使用，避免两处常量漂移。</summary>
	public const int WheelStepPixels = 48;

	// 机身按钮标注（纯视觉）的屏幕尺寸与字号。命中仍走下方碰撞区域，标注只负责"看得见"。
	// Why：滚轮区是窄胶囊（宽约 32px@0.167 缩放），M 标注必须比 L/R 更窄，否则标注边缘
	// 超出滚轮命中带、按下会落到移动区/触控板（命中带错位，修复移动端触控目标）。
	static readonly Vector2 LabelSizePx = new Vector2(44, 40);
	const int LabelFontSizePx = 18;
	static readonly Vector2 WheelLabelSizePx = new Vector2(28, 36);
	const int WheelLabelFontSizePx = 14;

	// 触控板移动光标灵敏度（0.5x~3.0x 可调），持久化到 user://settings.cfg [Display]。
	// Why：键名沿用旧 VirtualCursorSensitivity 以兼容既有用户设置；该设置在合并后归本组件所有，
	//      同时作用于触控板光标移动量（×灵敏度）与滚轮每格滚动量（见 EmueraContent.VirtualCursorScrollWheel）。
	const string SettingsPath = "user://settings.cfg";
	const string SettingsSection = "Display";
	const string SensitivityKey = "VirtualCursorSensitivity";
	public const float MinCursorSensitivity = 0.5f;
	public const float MaxCursorSensitivity = 3.0f;
	const float DefaultSensitivity = 0.70f;
	static float configuredSensitivity = -1f;

	public static float ConfiguredSensitivity
	{
		get
		{
			if (configuredSensitivity < 0f)
			{
				var cfg = new ConfigFile();
				cfg.Load(SettingsPath);
				configuredSensitivity = (float)(double)cfg.GetValue(SettingsSection, SensitivityKey, DefaultSensitivity);
				configuredSensitivity = Mathf.Clamp(configuredSensitivity, MinCursorSensitivity, MaxCursorSensitivity);
			}
			return configuredSensitivity;
		}
		set
		{
			configuredSensitivity = Mathf.Clamp(value, MinCursorSensitivity, MaxCursorSensitivity);
			var cfg = new ConfigFile();
			cfg.Load(SettingsPath);
			cfg.SetValue(SettingsSection, SensitivityKey, configuredSensitivity);
			cfg.Save(SettingsPath);
		}
	}

	// 虚拟鼠标缩放（需求：默认 0.167，范围 0.1~0.2），持久化到 user://settings.cfg [Display]。
	// 与灵敏度相同的懒加载 + Clamp 模式。
	const string ScaleKey = "VirtualMouseScale";
	public const float MinScale = 0.1f;
	public const float MaxScale = 0.2f;
	const float DefaultScale = 0.167f;
	static float configuredScale = -1f;

	public static float ConfiguredScale
	{
		get
		{
			if (configuredScale < 0f)
			{
				var cfg = new ConfigFile();
				cfg.Load(SettingsPath);
				configuredScale = (float)(double)cfg.GetValue(SettingsSection, ScaleKey, DefaultScale);
				configuredScale = Mathf.Clamp(configuredScale, MinScale, MaxScale);
			}
			return configuredScale;
		}
		set
		{
			configuredScale = Mathf.Clamp(value, MinScale, MaxScale);
			var cfg = new ConfigFile();
			cfg.Load(SettingsPath);
			cfg.SetValue(SettingsSection, ScaleKey, configuredScale);
			cfg.Save(SettingsPath);
		}
	}

	// 机身透明度（0.4~1.0，默认 1.0），持久化到 user://settings.cfg [Display]。
	// 只改 Sprite 的 Modulate.A，不影响四个命中区域（碰撞几何不变）。
	const string OpacityKey = "VirtualMouseOpacity";
	public const float MinOpacity = 0.4f;
	public const float MaxOpacity = 1.0f;
	const float DefaultOpacity = 1.0f;
	static float configuredOpacity = -1f;

	public static float ConfiguredOpacity
	{
		get
		{
			if (configuredOpacity < 0f)
			{
				var cfg = new ConfigFile();
				cfg.Load(SettingsPath);
				configuredOpacity = (float)(double)cfg.GetValue(SettingsSection, OpacityKey, DefaultOpacity);
				configuredOpacity = Mathf.Clamp(configuredOpacity, MinOpacity, MaxOpacity);
			}
			return configuredOpacity;
		}
		set
		{
			configuredOpacity = Mathf.Clamp(value, MinOpacity, MaxOpacity);
			var cfg = new ConfigFile();
			cfg.Load(SettingsPath);
			cfg.SetValue(SettingsSection, OpacityKey, configuredOpacity);
			cfg.Save(SettingsPath);
		}
	}

	/// <summary>机身前端热点：仅用于首次启用时把共享光标锚定到机身前端。启用后光标由触控板
	/// 移动、机身重定位不再带动光标（遥控器 + 触控板模型），因此热点不再是点击落点。</summary>
	[Export]
	public Vector2 CursorHotspotLocal = new Vector2(0, -880f);

	/// <summary>当前激活的虚拟鼠标实例（场景中唯一）。Disable 时置空，表示"当前没有鼠标设备"。</summary>
	public static VirtualMouse Active { get; private set; }

	/// <summary>场景中存在的虚拟鼠标实例（与启用状态无关；用于设备切换时判断鼠标组件是否在场）。</summary>
	public static VirtualMouse Instance { get; private set; }

	enum GestureKind
	{
		None,
		Body, // 机身上的区域手势（左/右/滚轮/移动）
		Trackpad, // 机身外的触控板手势（移动光标/单击）
	}

	enum RegionKind
	{
		None,
		Left,
		Right,
		Wheel,
		Move,
	}

	// 区域只读引用（%UniqueName 解析，树重构后仍稳定；约束：碰撞区域不能动，只读不做变换）。
	Area2D leftRegion;
	Area2D rightRegion;
	Area2D wheelRegion;
	Area2D moveRegion;
	CollisionPolygon2D leftPoly;
	CollisionPolygon2D rightPoly;
	CollisionPolygon2D movePoly;
	CollisionShape2D wheelShape;
	// 机身视觉 Sprite：仅用于透明度（Modulate.A）与机身位置钳制（半尺寸）；命中走四个碰撞区域。
	Sprite2D spriteVisual;
	// 机身按钮标注（纯视觉，MouseFilter=Ignore，不拦截输入；缩放取倒数以在机身缩放下保持可读）。
	readonly System.Collections.Generic.List<Control> buttonLabels = new System.Collections.Generic.List<Control>();
	readonly System.Collections.Generic.List<Vector2> buttonLabelCenters = new System.Collections.Generic.List<Vector2>();
	readonly System.Collections.Generic.List<Vector2> buttonLabelSizes = new System.Collections.Generic.List<Vector2>();
	// 机身命中区域并集（鼠标局部坐标，只读计算一次）：用于把机身整体钳制在可视区内。
	Rect2 bodyBoundsLocal;

	bool enabled;
	GestureKind gestureKind;
	RegionKind activeRegion;
	int trackedPointer = -1;
	Vector2 pressGlobal;
	Vector2 lastGlobal;
	float gestureElapsed;
	bool longPressTriggered;
	bool moved;
	int heldVk; // 长按按住中的虚拟键（0=未按住）
	float wheelAccumY;
	// 触控板光标（屏幕全局坐标）。与机身位置解耦：机身重定位不改变光标。
	Vector2 cursorGlobalPosition;

	public bool IsEnabled => enabled;

	public override void _Ready()
	{
		// 只解析只读引用；绝不修改区域节点的 Transform / Collision*。
		leftRegion = GetNodeOrNull<Area2D>("%左键区域");
		rightRegion = GetNodeOrNull<Area2D>("%右键区域");
		wheelRegion = GetNodeOrNull<Area2D>("%滚轮区域");
		moveRegion = GetNodeOrNull<Area2D>("%移动区域");
		leftPoly = leftRegion?.GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		rightPoly = rightRegion?.GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		movePoly = moveRegion?.GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		wheelShape = wheelRegion?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		spriteVisual = GetNodeOrNull<Sprite2D>("Sprite");
		// 只读计算一次机身命中区域并集（供机身位置钳制；碰撞区域本身不动）。
		ComputeBodyBoundsLocal();

		ApplyConfiguredScale();
		RefreshOpacity();
		BuildButtonLabels();

		Instance = this;
		SetProcess(true);

		// 层级钉在内容之上（问题3根因）：内容 HTML div 用 z=1024-depth 抬层（HtmlDivZIndexBase=1024），
		// 有效 z 可达 ~1044，高于鼠标在 main.tscn 里的 z_index=999，于是游戏内容盖住鼠标机身，
		// 移动区/滚轮区无法触及。仅改 z_index 会与新加入的内容元素赛跑；
		// 正确做法是把本组件移入专用高 CanvasLayer（内容默认 canvas=0 之上、VirtualPointer=92 之下、
		// 菜单=100 / tooltip=150 之下，弹窗 msgBox/OptionWindow 是 Window 恒在最上）。
		// 用 CallDeferred 确保在 EmueraContent（EmueraMain._Ready 运行期 AddChild）之后执行。
		ZAsRelative = false;
		ZIndex = 999;
		CallDeferred(MethodName.MoveIntoOwnLayer);

		// 虚拟鼠标默认关闭（所有平台一致）：只有用户用系统菜单的鼠标按钮手动开启时才启用。
		// Why：默认开启会改变游戏内容的输入行为（触控板取代直接滚动/点击），桌面端还会劫持真实鼠标；
		// 需要虚拟鼠标（机身按钮/触控板）的玩家可主动开启（Enable 负责置 Active，禁用态 Active 为 null）。
		// EmueraContent 此时可能尚未创建，首次交互时补同步（其 _Ready 用 PointerGlobalPosition 初始化共享指针）。
		// 禁用态隐藏机身：场景里 Sprite 默认可见，不 Enable 时要显式收起，否则会出现"看得见却点不动"的死鼠标。
		RefreshBodyVisibility();
	}

	public override void _EnterTree()
	{
		// 移入专用 CanvasLayer 会触发一次 Exit→Enter 循环，这里恢复静态引用。
		// 注意：只恢复 Instance；Active 仅在 enabled 时才恢复（禁用态下 Active 应为 null，
		// 表示"当前无鼠标设备"，与 Disable() 的语义一致）。
		Instance = this;
		if (enabled && Active == null)
			Active = this;
	}

	public override void _ExitTree()
	{
		// 退出场景前释放按住键：防止长按中的左键 hold（WinInput.virtualHeldKeys）在组件销毁后残留
		// （input-latch 泄漏）。不调用 ResetGestureState——它会访问 EmueraContent，而销毁顺序上
		// EmueraContent（运行时 AddChild）先于本场景节点被释放，调用会打在已释放对象上；
		// 按下反馈环随整树销毁，无需单独隐藏。
		if (heldVk != 0)
		{
			WinInput.SetVirtualKeyReleased(heldVk);
			heldVk = 0;
		}
		if (Active == this)
			Active = null;
		if (Instance == this)
			Instance = null;
	}

	/// <summary>把本组件移入专用高 CanvasLayer（Layer=90），使其恒渲染在游戏内容之上。
	/// Why：内容 HTML div 的 z 基准是 1024，鼠标 z=999 会被盖住；CanvasLayer 与树序/z 完全解耦，
	/// 是"鼠标始终最上层"最稳的实现（问题3）。</summary>
	void MoveIntoOwnLayer()
	{
		Node oldParent = GetParent();
		if (oldParent == null || !GodotObject.IsInstanceValid(oldParent))
			return;
		var layer = new CanvasLayer();
		layer.Name = "VirtualMouseLayer";
		layer.Layer = VirtualPointer.PointerCanvasLayer - 2; // 90：内容(0)之上、光标(92)/菜单(100)/tooltip(150)之下
		oldParent.RemoveChild(this);
		oldParent.AddChild(layer);
		layer.AddChild(this);
	}

	public void Enable()
	{
		enabled = true;
		Active = this;
		ResetGestureState();
		// 重新应用缩放/标注布局：禁用期间 OptionWindow 改缩放时 Active 为空、RefreshScale 被跳过，
		// 重开时若不补应用，机身会带着旧 Scale 显示（修复）。
		ApplyConfiguredScale();
		RefreshButtonLabelLayout();
		InitializeCursor();
		// 打开时把机身收回可视区域：编辑器摆放的初始坐标 (961,370) 在部分机型上会偏出/压到屏幕边缘，
		// 导致移动区无法被触及（问题3）；进入游戏后先钳制一次，保证移动区可用。
		ClampBodyToViewport();
		// 调试：打印机身当前屏幕位置，方便在 Godot 输出面板里定位四个区域该往哪点。
		GD.Print($"[VirtualMouse] 启用: 机身位置 @({Mathf.RoundToInt(GlobalPosition.X)},{Mathf.RoundToInt(GlobalPosition.Y)}) 缩放={Scale.X:F3}");
		// 打开时显示机身（含按钮标注）。
		RefreshBodyVisibility();
		EmueraContent.instance?.RefreshVirtualPointerVisibility();
	}

	public void Disable()
	{
		ResetGestureState();
		enabled = false;
		if (Active == this)
			Active = null;
		EmueraContent.instance?.VirtualCursorClearHover();
		// 关闭时隐藏机身（修复旧实现关闭后机身 Sprite 仍显示的视觉残留）。
		RefreshBodyVisibility();
		EmueraContent.instance?.RefreshVirtualPointerVisibility();
	}

	/// <summary>外力接管输入（双指缩放等）时取消当前手势：释放按住键并重置状态，不提交点击。
	/// Why：虚拟鼠标长按期间第二指触碰内容会让 HandleContentTouchGesture 进入缩放模式并吞掉
	/// 后续所有 drag/release，本组件 ReleaseGesture 不再被调用 → 按住键泄漏、组件卡死。</summary>
	public void CancelActiveGesture()
	{
		if (trackedPointer < 0)
			return;
		ResetGestureState();
	}

	public override void _Input(InputEvent @event)
	{
		// 内容完全绘制后，其 Control（rootContent 默认 MouseFilter=Stop、行/按钮/div）可能在 GUI
		// 分发阶段吞掉机身上的触摸，导致机身区域点不到（问题：进入正常游戏流程后无法使用）。
		// godot-master「Input Eaten」诊断：用 _Input（先于 GUI 分发）直接接管机身触摸并消费。
		// 机身外仍交给内容转发（触控板 + 双指缩放），不破坏缩放。
		if (!enabled)
			return;
		bool isPointer = IsPressEvent(@event, out int idx, out Vector2 pos)
			|| IsDragEvent(@event, out idx, out pos, out _)
			|| IsReleaseEvent(@event, out idx, out pos);
		if (!isPointer)
			return;
		// 正在机身手势中：完整接管按下/拖动/释放（首次按下在 _Input 消费后，后续事件只有 _Input 能看到）。
		if (gestureKind == GestureKind.Body && trackedPointer >= 0)
		{
			HandleGesture(@event, true);
			return;
		}
		// 无手势时，命中机身的按下：直接接管，防止被内容吞掉。
		if (gestureKind == GestureKind.None && HitTestRegion(pos) != RegionKind.None)
			HandleGesture(@event, true);
	}

	public override void _Process(double delta)
	{
		// 长按按住语义仅保留左键（GETKEY 读取）；右键/中键为点击、移动/滚轮为即时手势。
		if (!enabled || trackedPointer < 0 || gestureKind != GestureKind.Body
			|| activeRegion != RegionKind.Left || longPressTriggered || moved)
			return;

		gestureElapsed += (float)delta;
		if (gestureElapsed < LongPressDuration)
			return;
		longPressTriggered = true;
		BeginHeldKey(VK_LEFT);
	}

	#region 手势

	/// <summary>由 EmueraContent.HandleContentPointerInput 委托调用；消费触摸则返回 true。
	/// 虚拟鼠标启用时消费全部单指触摸：机身区域 → 区域手势，机身外 → 触控板。</summary>
	public bool HandleGesture(InputEvent @event, bool acceptEvent)
	{
		if (!enabled)
			return false;

		if (IsPressEvent(@event, out int pressIndex, out Vector2 pressPos))
		{
			if (trackedPointer < 0)
			{
				RegionKind region = HitTestRegion(pressPos);
				if (region != RegionKind.None)
					BeginBodyGesture(pressIndex, region, pressPos);
				else
					BeginTrackpadGesture(pressIndex, pressPos);
				if (acceptEvent)
					AcceptEvent();
				return true;
			}
			// 跟踪中的额外手指：吞掉，不让它落入内容滚动/点击路径
			// （Why：虚拟鼠标启用时单指手势归本组件，第二指只应触发双指缩放——
			// 双指缩放已在 HandleContentTouchGesture 先行判定，走到这里即非缩放，直接消费）。
			if (acceptEvent)
				AcceptEvent();
			return true;
		}

		if (IsDragEvent(@event, out int dragIndex, out Vector2 dragPos, out Vector2 dragRel))
		{
			if (trackedPointer != dragIndex)
			{
				// 非跟踪手指的拖动：消费，避免内容侧误滚动。
				if (acceptEvent)
					AcceptEvent();
				return true;
			}
			bool handled = HandleDrag(dragPos, dragRel);
			if (acceptEvent && handled)
				AcceptEvent();
			return handled;
		}

		if (IsReleaseEvent(@event, out int relIndex, out Vector2 relPos))
		{
			if (trackedPointer != relIndex)
			{
				if (acceptEvent)
					AcceptEvent();
				return true;
			}
			ReleaseGesture(relPos);
			if (acceptEvent)
				AcceptEvent();
			return true;
		}

		return false;
	}

	void BeginBodyGesture(int pointerIndex, RegionKind region, Vector2 globalPos)
	{
		gestureKind = GestureKind.Body;
		activeRegion = region;
		trackedPointer = pointerIndex;
		pressGlobal = globalPos;
		lastGlobal = globalPos;
		gestureElapsed = 0f;
		longPressTriggered = false;
		moved = false;
		heldVk = 0;
		wheelAccumY = 0f;
		GD.Print($"[VirtualMouse] 按下区域: {RegionName(region)} @({Mathf.RoundToInt(globalPos.X)},{Mathf.RoundToInt(globalPos.Y)})");
		// 可点击区域（左/右/滚轮）在「共享光标」处显示按下反馈环，确认点击落点
		// （手指在机身上、落点在光标处，视觉分离需要反馈确认）。
		// 移动区域不产生点击，不显示反馈环（避免拖动机身时无意义的闪光）。
		if (region == RegionKind.Left || region == RegionKind.Right || region == RegionKind.Wheel)
			EmueraContent.instance?.VirtualPointerShowPressFeedback(CurrentCursorGlobal());
	}

	void BeginTrackpadGesture(int pointerIndex, Vector2 globalPos)
	{
		gestureKind = GestureKind.Trackpad;
		activeRegion = RegionKind.None;
		trackedPointer = pointerIndex;
		pressGlobal = globalPos;
		lastGlobal = globalPos;
		gestureElapsed = 0f;
		longPressTriggered = false;
		moved = false;
		GD.Print($"[VirtualMouse] 按下区域: 触控板 @({Mathf.RoundToInt(globalPos.X)},{Mathf.RoundToInt(globalPos.Y)})");
		// 触控板不显示反馈环：光标即点击落点，用户已能看到目标。
	}

	bool HandleDrag(Vector2 globalPos, Vector2 relative)
	{
		Vector2 delta = ResolveDragDelta(globalPos, relative);
		switch (gestureKind)
		{
			case GestureKind.Body:
				HandleBodyDrag(globalPos, delta);
				break;
			case GestureKind.Trackpad:
				HandleTrackpadDrag(globalPos, delta);
				break;
		}
		lastGlobal = globalPos;
		return true;
	}

	void HandleBodyDrag(Vector2 globalPos, Vector2 delta)
	{
		switch (activeRegion)
		{
			case RegionKind.Left:
			case RegionKind.Right:
				// 长按按住期间忽略移动（左键按住=不动）；未进入长按时移动超阈值则取消 tap。
				if (longPressTriggered)
					break;
				if ((globalPos - pressGlobal).LengthSquared() >= TapThresholdSquared)
					moved = true;
				break;

			case RegionKind.Wheel:
				// 即时滚轮模式（无需长按）：上下滑动即累计滚轮步进，消除 0.45s 进入模式的等待。
				// 总位移超过 tap 阈值则视为滑动（松手不提交中键）。
				if ((globalPos - pressGlobal).LengthSquared() >= TapThresholdSquared)
					moved = true;
				AccumulateWheel(delta.Y);
				break;

			case RegionKind.Move:
				// 即时重定位机身（无需长按）：拖动超过 4px 即 1:1 跟随手指，仅移动机身不动光标。
				if (!moved && (globalPos - pressGlobal).LengthSquared() >= MoveThresholdSquared)
				{
					moved = true;
					GD.Print($"[VirtualMouse] 开始拖动机身 @({Mathf.RoundToInt(globalPos.X)},{Mathf.RoundToInt(globalPos.Y)})");
				}
				if (moved)
					MoveComponentBy(delta);
				break;
		}
	}

	void HandleTrackpadDrag(Vector2 globalPos, Vector2 delta)
	{
		// 触控板：滑动移动光标（相对位移 × 灵敏度）。
		MoveCursorBy(delta * ConfiguredSensitivity);
		if ((globalPos - pressGlobal).LengthSquared() >= DragThresholdSquared)
			moved = true;
	}

	void ReleaseGesture(Vector2 globalPos)
	{
		switch (gestureKind)
		{
			case GestureKind.Body:
				ReleaseBodyGesture();
				break;
			case GestureKind.Trackpad:
				// 触控板单击（未拖动）= 左键；拖动已随光标移动完成。
				if (!moved)
					CommitClick(VK_LEFT);
				break;
		}
		ResetGestureState();
	}

	void ReleaseBodyGesture()
	{
		switch (activeRegion)
		{
			case RegionKind.Left:
				if (longPressTriggered)
					ReleaseHeldKey(VK_LEFT);
				else if (!moved)
					CommitClick(VK_LEFT);
				break;

			case RegionKind.Right:
				// 可见 R 按钮：单击 = 右键（不再长按）。
				if (!moved)
					CommitClick(VK_RIGHT);
				break;

			case RegionKind.Wheel:
				// 单击（未滑动）才提交中键；滑动滚轮已即时步进，不再有长按进入/退出模式。
				if (!moved)
					CommitClick(VK_MIDDLE);
				break;

			case RegionKind.Move:
				// 移动区单点无动作（防误触）；拖动已随机身重定位完成。
				if (moved)
					GD.Print($"[VirtualMouse] 拖动机身结束 -> 机身位置 ({Mathf.RoundToInt(GlobalPosition.X)},{Mathf.RoundToInt(GlobalPosition.Y)})");
				break;
		}
	}

	void ResetGestureState()
	{
		if (heldVk != 0)
		{
			WinInput.SetVirtualKeyReleased(heldVk);
			heldVk = 0;
		}
		gestureKind = GestureKind.None;
		activeRegion = RegionKind.None;
		trackedPointer = -1;
		gestureElapsed = 0f;
		longPressTriggered = false;
		moved = false;
		wheelAccumY = 0f;
		// 松手/取消：隐藏按下高亮环。
		EmueraContent.instance?.VirtualPointerHidePressFeedback();
	}

	Vector2 ResolveDragDelta(Vector2 eventPos, Vector2 eventRelative)
	{
		// Godot 拖动事件已提供 relative，优先使用，避免 Android 首帧 position 基准不一致导致跳变。
		if (eventRelative.LengthSquared() > 0.0001f)
			return eventRelative;
		Vector2 fallback = eventPos - lastGlobal;
		lastGlobal = eventPos;
		return fallback;
	}

	#endregion

	#region 动作（点击 / 按住 / 滚轮 / 光标 / 移动）

	Vector2 GetHotspotGlobal()
	{
		return ToGlobal(CursorHotspotLocal);
	}

	/// <summary>共享指针当前位置（屏幕全局）；未同步过时退回机身箭头热点。
	/// Why：机身按钮的点击落在「光标」处（遥控器模型），而非手指按下的机身位置。</summary>
	Vector2 CurrentCursorGlobal()
	{
		var p = EmueraContent.instance?.CurrentPointerGlobalPosition;
		if (p.HasValue && !float.IsNaN(p.Value.X) && !float.IsNaN(p.Value.Y))
			return p.Value;
		return GetHotspotGlobal();
	}

	/// <summary>机身箭头热点（屏幕全局）。EmueraContent 用它做首次共享指针同步。</summary>
	public Vector2 PointerGlobalPosition => GetHotspotGlobal();

	/// <summary>启用时初始化触控板光标：继承共享指针当前位置，否则锚定到机身箭头热点。
	/// Why：从其他状态切回时指针不跳回中心（与旧 VirtualCursor.Enable 一致）。</summary>
	void InitializeCursor()
	{
		var current = EmueraContent.instance?.CurrentPointerGlobalPosition;
		if (current.HasValue && !float.IsNaN(current.Value.X) && !float.IsNaN(current.Value.Y))
			cursorGlobalPosition = current.Value;
		else
			cursorGlobalPosition = GetHotspotGlobal();
		ClampCursorToMovementBounds();
		EmueraContent.instance?.VirtualCursorSynchronizePosition(cursorGlobalPosition);
	}

	void MoveCursorBy(Vector2 delta)
	{
		cursorGlobalPosition += delta;
		ClampCursorToMovementBounds();
		// 位置写入共享指针 → MOUSEX/MOUSEY + 可见光标/hover/tooltip 跟随。
		EmueraContent.instance?.VirtualCursorSynchronizePosition(cursorGlobalPosition);
	}

	Rect2 GetCursorMovementBounds()
	{
		var content = EmueraContent.instance;
		if (content != null)
		{
			var contentRect = content.VirtualCursorGetContentViewportRect();
			if (contentRect.Size.X > 1.0f && contentRect.Size.Y > 1.0f)
				return contentRect;
		}
		var viewport = GetViewport();
		if (viewport != null)
			return viewport.GetVisibleRect();
		return new Rect2(Vector2.Zero, new Vector2(1, 1));
	}

	void ClampCursorToMovementBounds()
	{
		var bounds = GetCursorMovementBounds();
		cursorGlobalPosition.X = Mathf.Clamp(cursorGlobalPosition.X, bounds.Position.X, bounds.Position.X + bounds.Size.X);
		cursorGlobalPosition.Y = Mathf.Clamp(cursorGlobalPosition.Y, bounds.Position.Y, bounds.Position.Y + bounds.Size.Y);
	}

	/// <summary>旋转、分屏或系统栏变化时把光标与机身收回新的可视内容区域。</summary>
	public void RefreshViewportBounds()
	{
		if (!enabled)
			return;
		ClampCursorToMovementBounds();
		EmueraContent.instance?.VirtualCursorSynchronizePosition(cursorGlobalPosition);
		ClampBodyToViewport();
	}

	// 调试输出：区域/虚拟键 中文名（供用户在 Godot 输出面板核对四个区域是否正常响应）。
	static string RegionName(RegionKind r)
	{
		switch (r)
		{
			case RegionKind.Left: return "左键";
			case RegionKind.Right: return "右键";
			case RegionKind.Wheel: return "滚轮";
			case RegionKind.Move: return "移动";
			default: return "?";
		}
	}

	static string VkName(int vk)
	{
		switch (vk)
		{
			case VK_LEFT: return "左键";
			case VK_RIGHT: return "右键";
			case VK_MIDDLE: return "中键";
			default: return $"VK 0x{vk:X2}";
		}
	}

	void CommitClick(int mouseVk)
	{
		// 点击同时给 WinInput 打一次键脉冲，让 GETKEY/GETKEYTRIGGERED 也能读到鼠标键（对照源码
		// MainWindow.MouseDown 的 WinInput.SetKeyPressed）。RESULT:1 由 VirtualCursorCommitClick
		// 内部的 EmueraThread 提交路径负责。
		Vector2 target = CurrentCursorGlobal();
		GD.Print($"[VirtualMouse] {VkName(mouseVk)} 点击 @({Mathf.RoundToInt(target.X)},{Mathf.RoundToInt(target.Y)})");
		WinInput.PulseVirtualKey(mouseVk);
		EmueraContent.instance?.VirtualCursorCommitClick(target, mouseVk);
	}

	void BeginHeldKey(int vk)
	{
		heldVk = vk;
		WinInput.SetVirtualKeyPressed(vk);
		Vector2 target = CurrentCursorGlobal();
		GD.Print($"[VirtualMouse] {VkName(vk)} 按住开始 @({Mathf.RoundToInt(target.X)},{Mathf.RoundToInt(target.Y)})");
		// 长按发生的时刻提交一次按下（按钮/空白点击），与 PC 按下鼠标键一致。
		EmueraContent.instance?.VirtualCursorCommitClick(target, vk);
	}

	void ReleaseHeldKey(int vk)
	{
		if (heldVk == vk)
			heldVk = 0;
		WinInput.SetVirtualKeyReleased(vk);
		GD.Print($"[VirtualMouse] {VkName(vk)} 按住结束");
	}

	void AccumulateWheel(float deltaY)
	{
		wheelAccumY = Mathf.Clamp(wheelAccumY + deltaY, -WheelAccumMaxPx, WheelAccumMaxPx);
		while (Mathf.Abs(wheelAccumY) >= WheelStepThresholdPx)
		{
			// 上滑(deltaY<0) = 滚轮上滑(+1)；下滑(deltaY>0) = 滚轮下滑(-1)。
			int step = wheelAccumY < 0f ? 1 : -1;
			wheelAccumY -= Mathf.Sign(wheelAccumY) * WheelStepThresholdPx;
			GD.Print($"[VirtualMouse] 滚轮{(step > 0 ? "上滑" : "下滑")}");
			EmueraContent.instance?.VirtualCursorScrollWheel(step);
		}
	}

	void MoveComponentBy(Vector2 delta)
	{
		// Why：移动区域 = 重定位「遥控器」机身，只改 GlobalPosition；不同步光标
		// （遥控器 + 触控板模型：光标由触控板移动，机身重定位不带动指针）。
		// 1:1 跟随手指（不乘灵敏度），抓取点才不会滑出机身。
		GlobalPosition += delta;
		ClampBodyToViewport();
	}

	void ClampBodyToViewport()
	{
		var content = EmueraContent.instance;
		Rect2 bounds;
		if (content != null)
		{
			bounds = content.VirtualCursorGetContentViewportRect();
			if (bounds.Size.X <= 1.0f || bounds.Size.Y <= 1.0f)
				bounds = GetViewport().GetVisibleRect();
		}
		else
		{
			bounds = GetViewport().GetVisibleRect();
		}
		// 用机身命中区域并集的半尺寸约束机身原点，保证全部可点击区域留在可视区内。
		// Why：不用整张 Sprite 半尺寸（2048×2048 画布含大量透明边距，会过度钳制，
		// 窄屏上几乎无法重定位机身）；用区域并集（≈鼠标绘制主体）让拖动有合理自由空间。
		// 半尺寸只读一次计算并 × Scale，拖动时零分配。
		Vector2 half = bodyBoundsLocal.Size * 0.5f * Scale;
		float hx = Mathf.Max(1f, half.X);
		float hy = Mathf.Max(1f, half.Y);
		GlobalPosition = new Vector2(
			Mathf.Clamp(GlobalPosition.X, bounds.Position.X + hx, Mathf.Max(bounds.Position.X + hx, bounds.End.X - hx)),
			Mathf.Clamp(GlobalPosition.Y, bounds.Position.Y + hy, Mathf.Max(bounds.Position.Y + hy, bounds.End.Y - hy)));
	}

	/// <summary>只读计算四个命中区域在鼠标局部坐标下的并集包围盒（约束：只读，绝不写 Transform）。</summary>
	void ComputeBodyBoundsLocal()
	{
		Rect2 acc = new Rect2(Vector2.Zero, Vector2.Zero);
		bool valid = false;
		void Grow(Rect2 r)
		{
			if (r.Size.X <= 0f || r.Size.Y <= 0f)
				return;
			acc = valid ? acc.Merge(r) : r;
			valid = true;
		}
		if (leftPoly != null) Grow(GetPolygonBoundsLocal(leftPoly));
		if (rightPoly != null) Grow(GetPolygonBoundsLocal(rightPoly));
		if (movePoly != null) Grow(GetPolygonBoundsLocal(movePoly));
		if (wheelShape != null && wheelShape.Shape is CapsuleShape2D cap)
		{
			Vector2 c = wheelShape.Position;
			float h = cap.Height * 0.5f;
			Grow(new Rect2(new Vector2(c.X - cap.Radius, c.Y - h), new Vector2(cap.Radius * 2f, cap.Height)));
		}
		bodyBoundsLocal = valid ? acc : new Rect2(Vector2.Zero, new Vector2(1, 1));
	}

	static Rect2 GetPolygonBoundsLocal(CollisionPolygon2D poly)
	{
		if (poly.Polygon == null || poly.Polygon.Length == 0)
			return new Rect2(Vector2.Zero, Vector2.Zero);
		// 多边形点在 CollisionPolygon2D 自身局部空间；加上节点的 position 得到鼠标局部坐标。
		Vector2 offset = poly.Position;
		Vector2 min = offset + poly.Polygon[0];
		Vector2 max = min;
		for (int i = 1; i < poly.Polygon.Length; i++)
		{
			Vector2 p = offset + poly.Polygon[i];
			min = new Vector2(Mathf.Min(min.X, p.X), Mathf.Min(min.Y, p.Y));
			max = new Vector2(Mathf.Max(max.X, p.X), Mathf.Max(max.Y, p.Y));
		}
		return new Rect2(min, max - min);
	}

	#endregion

	#region 机身按钮标注（纯视觉，不参与命中）

	/// <summary>构建 L/R/M 可见按钮标注。Why：中/右键从"隐藏区域+长按"改为机身可见标注，
	/// 降低用户使用与理解成本；标注不拦截输入，命中仍走既有碰撞区域（约束：碰撞区域不能动）。</summary>
	void BuildButtonLabels()
	{
		AddButtonLabel("L", GetRegionCenter(leftPoly, leftRegion), LabelSizePx, LabelFontSizePx);
		AddButtonLabel("R", GetRegionCenter(rightPoly, rightRegion), LabelSizePx, LabelFontSizePx);
		AddButtonLabel("M", GetWheelCenter(), WheelLabelSizePx, WheelLabelFontSizePx);
		RefreshButtonLabelLayout();
	}

	void AddButtonLabel(string text, Vector2 regionCenterLocal, Vector2 sizePx, int fontSizePx)
	{
		var panel = new PanelContainer();
		panel.Name = $"Label{text}";
		panel.MouseFilter = Control.MouseFilterEnum.Ignore; // 纯视觉，不拦截输入
		panel.CustomMinimumSize = sizePx;
		var style = GEmueraTheme.SurfaceStyle(
			GEmueraTheme.WithAlpha(GEmueraTheme.SurfaceRaised, 0.90f),
			GEmueraTheme.Accent,
			8,
			2,
			0,
			null,
			6, 6, 6, 6);
		panel.AddThemeStyleboxOverride("panel", style);

		var label = new Label();
		label.Text = text;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeFontSizeOverride("font_size", fontSizePx);
		label.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
		panel.AddChild(label);

		AddChild(panel);
		buttonLabels.Add(panel);
		buttonLabelCenters.Add(regionCenterLocal);
		buttonLabelSizes.Add(sizePx);
	}

	Vector2 GetRegionCenter(CollisionPolygon2D poly, Area2D fallbackNode)
	{
		if (poly != null && poly.Polygon != null && poly.Polygon.Length > 0)
		{
			// 多边形点在 CollisionPolygon2D 自身局部空间，必须加上节点 position 才是鼠标局部坐标
			// （左键区域的 CollisionPolygon2D 带 position=(-11.98,0)，不加会偏约 12 局部px）。
			Vector2 offset = poly.Position;
			Vector2 min = offset + poly.Polygon[0];
			Vector2 max = min;
			for (int i = 1; i < poly.Polygon.Length; i++)
			{
				Vector2 p = offset + poly.Polygon[i];
				min = new Vector2(Mathf.Min(min.X, p.X), Mathf.Min(min.Y, p.Y));
				max = new Vector2(Mathf.Max(max.X, p.X), Mathf.Max(max.Y, p.Y));
			}
			return (min + max) * 0.5f;
		}
		return fallbackNode != null ? fallbackNode.Position : Vector2.Zero;
	}

	Vector2 GetWheelCenter()
	{
		if (wheelShape != null)
			return wheelShape.Position;
		if (wheelRegion != null)
			return wheelRegion.Position;
		return Vector2.Zero;
	}

	void RefreshButtonLabelLayout()
	{
		// 标注逆缩放：机身(scale≈0.167)缩放下标注仍保持恒定屏幕尺寸。
		// 居中公式：Position = center - size/(2×机身scale)（见 AddButtonLabel 注释推导）。
		float inv = ConfiguredScale > 0.001f ? 1f / ConfiguredScale : 1f;
		for (int i = 0; i < buttonLabels.Count; i++)
		{
			Control panel = buttonLabels[i];
			panel.Scale = new Vector2(inv, inv);
			panel.Position = buttonLabelCenters[i] - buttonLabelSizes[i] * (0.5f * inv);
		}
	}

	#endregion

	#region 显隐 / 缩放 / 透明度

	void RefreshBodyVisibility()
	{
		if (spriteVisual != null)
			spriteVisual.Visible = enabled;
		for (int i = 0; i < buttonLabels.Count; i++)
			buttonLabels[i].Visible = enabled;
	}

	void ApplyConfiguredScale()
	{
		Scale = Vector2.One * ConfiguredScale;
	}

	/// <summary>设置页改动缩放后重新应用（Node2D 缩放会连带缩放子 Sprite、Area2D 碰撞与按钮标注，
	/// 区域局部变换不变）。Why：光标与机身解耦，机身缩放变化不带动光标；只重排标注并钳制机身。</summary>
	public void RefreshScale()
	{
		ApplyConfiguredScale();
		RefreshButtonLabelLayout();
		ClampBodyToViewport();
	}

	/// <summary>设置页改动透明度后重新应用（Sprite 与按钮标注同步调制，命中区域不受影响）。</summary>
	public void RefreshOpacity()
	{
		float alpha = ConfiguredOpacity;
		if (spriteVisual != null)
		{
			var color = spriteVisual.Modulate;
			color.A = alpha;
			spriteVisual.Modulate = color;
		}
		// 标注也随透明度淡出（旧实现只改 Sprite，透明度<1 时标注仍全亮，视觉不一致）。
		for (int i = 0; i < buttonLabels.Count; i++)
		{
			var c = buttonLabels[i].Modulate;
			c.A = alpha;
			buttonLabels[i].Modulate = c;
		}
	}

	#endregion

	#region 区域命中测试（只读，不修改任何 Transform）

	RegionKind HitTestRegion(Vector2 globalPos)
	{
		// 优先级：滚轮（最窄的中央元素）→ 左键 → 右键 → 移动区域（最大的机身）。
		// Why：区域之间有轻微重叠（滚轮胶囊与左右键内沿、按钮底部与机身顶部），
		// 更小的区域优先，符合"滚轮在左右键之间、按钮在机身上方"的物理直觉。
		if (wheelRegion != null && wheelShape != null && PointInWheel(globalPos))
			return RegionKind.Wheel;
		if (leftRegion != null && leftPoly != null && PointInPolygon(globalPos, leftPoly))
			return RegionKind.Left;
		if (rightRegion != null && rightPoly != null && PointInPolygon(globalPos, rightPoly))
			return RegionKind.Right;
		if (moveRegion != null && movePoly != null && PointInPolygon(globalPos, movePoly))
			return RegionKind.Move;
		return RegionKind.None;
	}

	static bool PointInPolygon(Vector2 globalPos, CollisionPolygon2D poly)
	{
		if (poly == null || poly.Polygon == null || poly.Polygon.Length < 3)
			return false;
		Vector2 local = poly.ToLocal(globalPos);
		return Geometry2D.IsPointInPolygon(local, poly.Polygon);
	}

	bool PointInWheel(Vector2 globalPos)
	{
		// 手动判定胶囊：CapsuleShape2D 高度含两端半圆，直线段长 = height - 2*radius。
		// Why：Area2D 无内置 HasPoint，用物理 IntersectPoint 有开销且有竞态；4 个区域手算最快最稳。
		// 命中半径用 WheelHitRadiusScale 放宽（仅手算，不动碰撞几何）——滚轮命中带太窄会点不中。
		if (!(wheelShape.Shape is CapsuleShape2D capsule))
			return false;
		Vector2 local = wheelShape.ToLocal(globalPos);
		float segmentLen = Mathf.Max(0f, capsule.Height - 2f * capsule.Radius);
		float halfSeg = segmentLen * 0.5f;
		float dy = Mathf.Clamp(local.Y, -halfSeg, halfSeg);
		float hitRadius = capsule.Radius * WheelHitRadiusScale;
		return (local - new Vector2(0f, dy)).LengthSquared() <= hitRadius * hitRadius;
	}

	#endregion

	#region 事件判定

	static bool IsPressEvent(InputEvent @event, out int pointerIndex, out Vector2 position)
	{
		pointerIndex = -1;
		position = default;
		if (@event is InputEventScreenTouch st && st.Pressed)
		{
			pointerIndex = st.Index;
			position = st.Position;
			return true;
		}
		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			// 桌面调试：用真实左键驱动虚拟鼠标区域。
			pointerIndex = 0;
			position = mb.Position;
			return true;
		}
		return false;
	}

	static bool IsReleaseEvent(InputEvent @event, out int pointerIndex, out Vector2 position)
	{
		pointerIndex = -1;
		position = default;
		if (@event is InputEventScreenTouch st && !st.Pressed)
		{
			pointerIndex = st.Index;
			position = st.Position;
			return true;
		}
		if (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			pointerIndex = 0;
			position = mb.Position;
			return true;
		}
		return false;
	}

	static bool IsDragEvent(InputEvent @event, out int pointerIndex, out Vector2 position, out Vector2 relative)
	{
		pointerIndex = -1;
		position = default;
		relative = default;
		if (@event is InputEventScreenDrag drag)
		{
			pointerIndex = drag.Index;
			position = drag.Position;
			relative = drag.Relative;
			return true;
		}
		if (@event is InputEventMouseMotion motion)
		{
			pointerIndex = 0;
			position = motion.Position;
			relative = motion.Relative;
			return true;
		}
		return false;
	}

	void AcceptEvent()
	{
		var vp = GetViewport();
		if (vp != null && GodotObject.IsInstanceValid(vp))
			vp.SetInputAsHandled();
	}

	#endregion
}
