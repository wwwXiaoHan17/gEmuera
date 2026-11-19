// AndroidWindowOrientationController.cs —— 系统浮动小窗方向控制器（问题2 阶段一）。
// systematic-debugging：manifest=fullUser 是「小窗会以竖屏打开」的已知条件；
// 「进入小窗后运行时请求横屏是否被 OEM 接受」是待真机验证的假设，因此：
// - 只在窗口形态边沿变化时请求，避免冷启动跳变与重复请求；
// - 全链路探针日志（singleton/activity/返回值/请求前后），真机判断 changed|ignored；
// - 任何失败限流告警并降级，绝不崩溃、不阻塞；_ExitTree 恢复 Sensor 防止锁死方向。
using System;
using Godot;
using GEmuera.Core.Ui;

public partial class AndroidWindowOrientationController : Node
{
	const double DebounceSeconds = 0.15;
	const int MaxFailureWarnings = 3;
	const int MaxWindowModeFailureWarnings = 3;

	// —— 方向跟随（设置持久化）——
	// sensor：请求 ScreenOrientation.Sensor —— Android 上该值按平台契约忽略用户的
	//         「自动旋转」锁定，手机物理转动即跟随（本功能的核心诉求）。
	// fixed ：请求当前窗口宽高比对应的固定方向，锁定在用户手动选择的方向。
	const string SettingsPath = "user://settings.cfg";
	const string SettingsSection = "Display";
	const string FollowDeviceOrientationKey = "FollowDeviceOrientation";
	// 一次性迁移标记：旧版本手动旋转会无条件把 FollowDeviceOrientation 写成 false
	//（持久化），该值不代表用户真实偏好。为此新增「仅本次/永久」选择并写入本标记；
	// 标记缺失 = 旧配置 = 把该键重置回「跟随」，之后用户选择正常持久化。
	const string FollowMigrationKey = "RotationFollowSemanticsV2";

	static int followDeviceOrientationCache = -1;

	/// <summary>
	/// 旧配置一次性迁移：把旧版本遗留的 FollowDeviceOrientation=false 重置为 true。
	/// 幂等：迁移后写入标记，后续不再执行。
	/// </summary>
	static void MigrateLegacyRotationSettingsIfNeeded()
	{
		try
		{
			var cfg = new ConfigFile();
			if (cfg.Load(SettingsPath) != Error.Ok)
				return; // 无配置文件：无需迁移
			if (cfg.GetValue(SettingsSection, FollowMigrationKey, false).AsBool())
				return; // 已迁移
			cfg.SetValue(SettingsSection, FollowDeviceOrientationKey, true);
			cfg.SetValue(SettingsSection, FollowMigrationKey, true);
			cfg.Save(SettingsPath);
			followDeviceOrientationCache = 1;
			MobileLayoutDiagnostics.PublishOrientation(
				"[ORIENTATION] 迁移旧配置：FollowDeviceOrientation 重置为跟随（旧版本无条件写盘导致）");
		}
		catch (Exception ex)
		{
			// 迁移失败不阻断：把异常降级为告警，行为退回读取原始配置。
			MobileLayoutDiagnostics.Warn("[ORIENTATION] 方向配置迁移失败 -> "
				+ ex.GetType().Name + ": " + ex.Message);
		}
	}

	/// <summary>跟随手机朝向（true）或固定当前方向（false）。写入 settings.cfg。</summary>
	public static bool FollowDeviceOrientation
	{
		get
		{
			if (followDeviceOrientationCache < 0)
			{
				MigrateLegacyRotationSettingsIfNeeded();
				var cfg = new ConfigFile();
				cfg.Load(SettingsPath);
				followDeviceOrientationCache =
					cfg.GetValue(SettingsSection, FollowDeviceOrientationKey, true).AsBool() ? 1 : 0;
			}
			return followDeviceOrientationCache != 0;
		}
		set
		{
			followDeviceOrientationCache = value ? 1 : 0;
			var cfg = new ConfigFile();
			Error loadError = cfg.Load(SettingsPath);
			if (loadError == Error.Ok || loadError == Error.FileNotFound)
			{
				cfg.SetValue(SettingsSection, FollowDeviceOrientationKey, value);
				cfg.Save(SettingsPath);
			}
			ApplyOrientationMode(value ? "follow_sensor" : "fixed");
		}
	}

	/// <summary>手动旋转 90°（竖↔横）。隐含关闭「跟随手机」。</summary>
	/// <param name="persist">
	/// true = 永久（写盘，重启后仍固定）；false = 仅本次运行（不写盘，重启恢复跟随）。
	/// 由 <see cref="RotateChoiceDialog"/> 让用户选择。
	/// </param>
	public static void RotateScreen(bool persist = true)
	{
		if (OS.GetName() != "Android")
			return;
		Vector2I size = DisplayServer.WindowGetSize();
		DisplayServer.ScreenOrientation target = size.X > size.Y
			? DisplayServer.ScreenOrientation.Portrait
			: DisplayServer.ScreenOrientation.Landscape;
		SetFollowCache(false, persist);
		// 必须同时停掉 fullSensor 守护，否则守护会把这张手动方向再次覆盖回 FULL_SENSOR。
		fullSensorApplied = false;
		nextFullSensorGuardTime = 0.0;
		fullSensorConsecutiveFailures = 0;
		try
		{
			DisplayServer.ScreenSetOrientation(target);
			MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] 手动旋转 → target=" + target
				+ " persist=" + persist
				+ " windowBefore=" + size);
		}
		catch (Exception ex)
		{
			MobileLayoutDiagnostics.Warn("[ORIENTATION] 手动旋转失败 -> " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	/// <param name="persist">false = 只改内存态，不写 settings.cfg（重启后恢复配置值）。</param>
	static void SetFollowCache(bool value, bool persist = true)
	{
		followDeviceOrientationCache = value ? 1 : 0;
		if (!persist)
			return;
		var cfg = new ConfigFile();
		Error loadError = cfg.Load(SettingsPath);
		if (loadError == Error.Ok || loadError == Error.FileNotFound)
		{
			cfg.SetValue(SettingsSection, FollowDeviceOrientationKey, value);
			cfg.Save(SettingsPath);
		}
	}

	/// <summary>应用当前模式：优先 fullSensor（跟随手机），失败才退回轮询。</summary>
	static void ApplyOrientationMode(string reason)
	{
		if (OS.GetName() != "Android")
			return;
		if (FollowDeviceOrientation)
		{
			nextGravitySampleTime = 0;
			// 立刻按当前姿态校正一次（清冷却），否则切换开关后要等冷却期结束。
			orientationCooldownUntilMs = 0;
			// 首选 SCREEN_ORIENTATION_FULL_SENSOR：4 方向跟随传感器且忽略用户方向锁定，
			// 旋转完全交给系统。Godot 的 ScreenOrientation 枚举没有该值（导出清单是
			// fullUser = 尊重用户锁定，这才是锁定下不跟随的根因），故直接调 Java。
			fullSensorApplied = TryApplyFullSensor();
			MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] 应用方向模式 → reason=" + reason
				+ " mode=" + (fullSensorApplied ? "full_sensor" : "gravity_poll")
				+ " window=" + DisplayServer.WindowGetSize());
			return;
		}
		fullSensorApplied = false;
		Vector2I size = DisplayServer.WindowGetSize();
		ApplyScreenOrientation(
			size.X > size.Y ? DisplayServer.ScreenOrientation.Landscape : DisplayServer.ScreenOrientation.Portrait,
			reason + ":fixed");
	}

	// ActivityInfo.SCREEN_ORIENTATION_FULL_SENSOR —— 4 方向跟随传感器，忽略用户锁定。
	const int AndroidFullSensorOrientation = 10;

	// fullSensor 守护：真机反馈「仍被强制竖屏」。已知机制是 Activity 重建/resume 时
	// Godot 会按 manifest（fullUser）重新设置方向，把运行时请求覆盖掉。因此定时校验
	// 实际 requestedOrientation，被改写就重设；连续失败则判定系统不接受，退回重力轮询。
	const double FullSensorGuardIntervalSeconds = 1.0;
	const int FullSensorMaxConsecutiveFailures = 8;

	static double nextFullSensorGuardTime;
	static int fullSensorConsecutiveFailures;

	/// <summary>读 Activity 实际持有的 requestedOrientation，确认 FULL_SENSOR 仍在我们手里。</summary>
	static bool IsFullSensorActive()
	{
		try
		{
			GodotObject runtime = Engine.GetSingleton("AndroidRuntime");
			if (runtime == null)
				return false;
			GodotObject activity = runtime.Call("getActivity").AsGodotObject();
			if (activity == null)
				return false;
			Variant result = activity.Call("getRequestedOrientation");
			// Java 方法抛异常时 Call 返回 Nil，且异常存于 JavaClassWrapper。
			if (JavaClassWrapper.GetException() != null)
				return false;
			if (result.VariantType == Variant.Type.Nil)
				return false;
			return result.AsInt32() == AndroidFullSensorOrientation;
		}
		catch (Exception)
		{
			return false;
		}
	}

	static void GuardFullSensor(double delta)
	{
		nextFullSensorGuardTime -= delta;
		if (nextFullSensorGuardTime > 0.0)
			return;
		nextFullSensorGuardTime = FullSensorGuardIntervalSeconds;
		if (IsFullSensorActive())
		{
			fullSensorConsecutiveFailures = 0;
			return;
		}
		if (TryApplyFullSensor())
		{
			fullSensorConsecutiveFailures = 0;
			MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] 守护：检测到方向被系统改写，重新应用 FULL_SENSOR");
			return;
		}
		if (++fullSensorConsecutiveFailures >= FullSensorMaxConsecutiveFailures)
		{
			fullSensorApplied = false;
			MobileLayoutDiagnostics.Warn("[ORIENTATION] fullSensor 连续无效 → 退回重力轮询");
		}
	}

	/// <summary>
	/// 通过 AndroidRuntime 直接调用 Activity.setRequestedOrientation(FULL_SENSOR)。
	/// 返回 true = 调用被接受（此时旋转由系统负责，轮询可停）。
	/// </summary>
	static bool TryApplyFullSensor()
	{
		try
		{
			GodotObject runtime = Engine.GetSingleton("AndroidRuntime");
			if (runtime == null)
				return false;
			GodotObject activity = runtime.Call("getActivity").AsGodotObject();
			if (activity == null)
				return false;
			activity.Call("setRequestedOrientation", AndroidFullSensorOrientation);
			// Java 抛异常时 Call 返回 Nil 且异常存于 JavaClassWrapper，必须按失败计。
			if (JavaClassWrapper.GetException() != null)
				return false;
			return true;
		}
		catch (Exception ex)
		{
			MobileLayoutDiagnostics.Warn("[ORIENTATION] FULL_SENSOR 设置失败 -> "
				+ ex.GetType().Name + ": " + ex.Message);
			return false;
		}
	}

	static void ApplyScreenOrientation(DisplayServer.ScreenOrientation target, string reason)
	{
		try
		{
			DisplayServer.ScreenSetOrientation(target);
			// 旋转后冷却：过渡帧内窗口尺寸与传感器读数都会抖动，期间一律不响应，
			// 否则会形成「横→竖→横」的反向切换循环。
			orientationCooldownUntilMs = Time.GetTicksMsec() + OrientationCooldownMilliseconds;
			MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] 应用方向 → reason=" + reason
				+ " target=" + target
				+ " window=" + DisplayServer.WindowGetSize());
		}
		catch (Exception ex)
		{
			MobileLayoutDiagnostics.Warn("[ORIENTATION] 方向应用失败 -> " + reason + " " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	// —— 传感器轮询（跟随手机）——
	// 无状态判定：当前朝向直接读窗口实际宽高比，而不是维护 lastApplied 之类静态状态。
	// 原因（实测）：窗口 resized / 前后台切换会触发 _Notification 与 Evaluate，静态状态
	// 被重置或与真实窗口不一致时会出现「横→竖→横」反复切换。宽高比是唯一可信事实。
	// 三段防护：
	//   1) 迟滞死区：主分量差值不足 OrientationAxisHysteresis 时不动；
	//   2) 旋转后冷却：过渡帧内窗口尺寸与传感器读数都抖动，冷却期内不响应；
	//   3) 与当前窗口朝向相同则不发请求（天然幂等）。
	const double GravitySampleIntervalSeconds = 0.15;
	const float OrientationAxisHysteresis = 4.0f;
	const double OrientationCooldownMilliseconds = 2000.0;

	static double nextGravitySampleTime;
	static double orientationCooldownUntilMs;
	// fullSensor 生效时旋转交给系统，轮询完全停用（避免与系统互相纠正）。
	static bool fullSensorApplied;

	public override void _Process(double delta)
	{
		if (OS.GetName() != "Android" || !FollowDeviceOrientation)
			return;
		// fullSensor 生效时旋转交给系统，但需守护：Activity 重建/resume 会让系统
		// 按 manifest 的 fullUser 重置方向（真机实测仍被强制竖屏的原因）。
		if (fullSensorApplied)
		{
			GuardFullSensor(delta);
			return;
		}
		// 冷却期内不响应：旋转过渡帧会同时改变窗口尺寸与传感器姿态读数。
		if (Time.GetTicksMsec() < orientationCooldownUntilMs)
			return;
		nextGravitySampleTime -= delta;
		if (nextGravitySampleTime > 0.0)
			return;
		nextGravitySampleTime = GravitySampleIntervalSeconds;

		// GetGravity 是「设备重力方向」语义；部分构建只启用加速度计，回退取之
		//（静止时两者等价，仅符号相反——本判定只用绝对值，符号无关）。
		Vector3 gravity = Input.GetGravity();
		if (gravity.LengthSquared() < 1.0f)
			gravity = Input.GetAccelerometer();
		float ax = Mathf.Abs(gravity.X);
		float ay = Mathf.Abs(gravity.Y);
		if (Mathf.Max(ax, ay) < 3.0f)
			return;                       // 平放桌面：朝向不确定
		if (Mathf.Abs(ax - ay) < OrientationAxisHysteresis)
			return;                       // 死区：接近 45°，保持现状
		bool wantLandscape = ax > ay;

		// 当前朝向 = 窗口宽高比（真实事实，不受任何缓存状态影响）。
		Vector2I size = DisplayServer.WindowGetSize();
		bool isLandscape = size.X > size.Y;
		if (wantLandscape == isLandscape)
			return;                       // 已是目标朝向，不发请求

		ApplyScreenOrientation(
			wantLandscape ? DisplayServer.ScreenOrientation.Landscape : DisplayServer.ScreenOrientation.Portrait,
			"gravity");
	}

	WindowLayoutKind previous = WindowLayoutKind.Unknown;
	Timer debounceTimer;
	int failureWarnings;
	int windowModeFailureWarnings;
	bool lastRequestedLandscape;

	public override void _Ready()
	{
		if (OS.GetName() != "Android")
		{
			SetProcess(false);
			return;
		}
		debounceTimer = new Timer { OneShot = true, WaitTime = DebounceSeconds };
		AddChild(debounceTimer);
		debounceTimer.Timeout += Evaluate;
		// Android 上 GetWindow() 与 GetViewport() 是同一根窗口对象，重复 Connect 同一
		// callable 会在 logcat 报 "Signal 'size_changed' is already connected"（实测）。
		var window = GetWindow();
		if (window == GetViewport())
			window.SizeChanged += OnWindowSizeChanged;
		else
		{
			window.SizeChanged += OnWindowSizeChanged;
			GetViewport().SizeChanged += OnWindowSizeChanged;
		}
		CallDeferred(nameof(Evaluate));
		// 启动即应用方向模式：manifest 的 handheld/orientation 只写清单，运行时显式
		// 请求才能保证「跟随手机」在系统方向锁定下依然生效（Sensor 忽略用户锁定）。
		ApplyOrientationMode("startup");
	}

	public override void _ExitTree()
	{
		if (OS.GetName() != "Android")
			return;
		try
		{
			// 恢复可旋转，避免控制器生命周期结束时把设备方向锁在横屏。
			if (lastRequestedLandscape)
				DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Sensor);
		}
		catch (Exception ex)
		{
			MobileLayoutDiagnostics.Warn("[ORIENTATION] restore failed -> " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	void OnWindowSizeChanged()
	{
		if (debounceTimer != null)
			debounceTimer.Start();
	}

	void Evaluate()
	{
		if (OS.GetName() != "Android")
			return;
		WindowLayoutKind current;
		bool multiWindowKnown = TryQueryMultiWindow(out bool isMultiWindow);
		if (!multiWindowKnown)
		{
			if (failureWarnings++ < MaxFailureWarnings)
				MobileLayoutDiagnostics.Warn("[ORIENTATION] 多窗口检测失败 → AndroidRuntime/getActivity/isInMultiWindowMode 不可用");
			// 检测失败时窗口模式按非小窗兜底：真在分屏/小窗下系统会拒绝全屏请求，无害。
			// 方向逻辑维持原状（previous 不动，避免误判边沿）。
			ApplyImmersiveWindowMode(WindowLayoutKind.Fullscreen, "query_fallback");
			// 多窗口状态未知：跟随机制维持现状（不暂停也不恢复），避免误切换。
			if (FollowDeviceOrientation)
				return;
		}
		else
		{
			failureWarnings = 0;
			current = isMultiWindow ? WindowLayoutKind.MultiWindow : WindowLayoutKind.Fullscreen;
			ApplyImmersiveWindowMode(current, isMultiWindow ? "enter_multi_window" : "evaluate");
			MobileLayoutDiagnostics.Trace("[ORIENTATION] 状态 → multiWindow=" + isMultiWindow
				+ " window=" + DisplayServer.WindowGetSize()
				+ " screen=" + DisplayServer.ScreenGetSize()
				+ " requestedRaw=" + (int)DisplayServer.ScreenGetOrientation());

			// 跟随模式：方向由 fullSensor/重力轮询负责，绝不能走下面的 RequestOrientation——
			// Sensor/SensorLandscape 请求会覆盖 FULL_SENSOR，随后守护又把它改回去，
			// 两个机制互相覆盖形成循环（自查发现的 P0 冲突）。
			// 唯一例外是小窗：小窗内不应随传感器旋转，暂停跟随、交给小窗方向策略。
			if (FollowDeviceOrientation)
			{
				if (current == WindowLayoutKind.MultiWindow)
				{
					if (fullSensorApplied)
					{
						fullSensorApplied = false;
						fullSensorConsecutiveFailures = 0;
						ApplyScreenOrientation(DisplayServer.ScreenOrientation.SensorLandscape, "multiwindow:pause_follow");
					}
				}
				else if (previous == WindowLayoutKind.MultiWindow)
				{
					// 刚退出小窗：恢复跟随（重设 FULL_SENSOR）。
					ApplyOrientationMode("exit_multiwindow");
				}
				previous = current;
				return;
			}
			OrientationRequest request = WindowOrientationPolicy.Decide(previous, current);
			previous = current;
			if (request == OrientationRequest.None)
				return;
			RequestOrientation(request, isMultiWindow ? "enter_multi_window" : "exit_multi_window");
			return;
		}
	}

	// 沉浸全屏（Android 专属语义）：非小窗形态一律 ExclusiveFullscreen——同时隐藏
	// 状态栏与导航栏；进入小窗（分屏/浮动窗）恢复 Windowed，系统在小窗下不接受
	// 全屏请求。以 WindowGetMode 读回去重，回前台（_Notification）后重新评估。
	void ApplyImmersiveWindowMode(WindowLayoutKind kind, string reason)
	{
		DisplayServer.WindowMode target = kind == WindowLayoutKind.MultiWindow
			? DisplayServer.WindowMode.Windowed
			: DisplayServer.WindowMode.ExclusiveFullscreen;
		try
		{
			if (DisplayServer.WindowGetMode() == target)
				return;
			DisplayServer.WindowSetMode(target);
			windowModeFailureWarnings = 0;
			MobileLayoutDiagnostics.PublishOrientation("[WINDOWMODE] 请求窗口模式 → reason=" + reason
				+ " kind=" + kind
				+ " target=" + target
				+ " window=" + DisplayServer.WindowGetSize());
		}
		catch (Exception ex)
		{
			// 失败限流告警并降级：系统栏保留时由安全区管线兜底，绝不崩溃、不阻塞。
			if (windowModeFailureWarnings++ < MaxWindowModeFailureWarnings)
				MobileLayoutDiagnostics.Warn("[WINDOWMODE] request failed -> reason=" + reason + " " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	public override void _Notification(int what)
	{
		if (OS.GetName() != "Android")
			return;
		// 回到前台后系统可能已恢复系统栏（系统策略/切应用覆盖），重新评估一次；
		// Evaluate 幂等（方向按边沿、窗口模式按读回去重），重复触发无害。
		if (what == NotificationApplicationResumed || what == NotificationApplicationFocusIn)
		{
			// 切前后台可能被系统改写方向请求，重新应用用户选择（跟随/固定）。
			ApplyOrientationMode("resume");
			CallDeferred(nameof(Evaluate));
		}
	}

	void RequestOrientation(OrientationRequest request, string reason)
	{
		DisplayServer.ScreenOrientation target = request == OrientationRequest.SensorLandscape
			? DisplayServer.ScreenOrientation.SensorLandscape
			: DisplayServer.ScreenOrientation.Sensor;
		int before = (int)DisplayServer.ScreenGetOrientation();
		Vector2I windowBefore = DisplayServer.WindowGetSize();
		try
		{
			DisplayServer.ScreenSetOrientation(target);
		}
		catch (Exception ex)
		{
			MobileLayoutDiagnostics.Warn("[ORIENTATION] request failed -> reason=" + reason + " " + ex.GetType().Name + ": " + ex.Message);
			return;
		}
		bool landscapeRequest = request == OrientationRequest.SensorLandscape;
		lastRequestedLandscape = landscapeRequest;
		MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] 请求方向 → reason=" + reason
			+ " target=" + target
			+ " requestedBefore=" + before
			+ " windowBefore=" + windowBefore);
		// 结果观测：2 秒后按窗口真实宽高比判定（ScreenGetOrientation 在 Android 只是请求值映射）。
		GetTree().CreateTimer(2.0).Timeout += () => ReportResult(reason, windowBefore, landscapeRequest);
	}

	void ReportResult(string reason, Vector2I windowBefore, bool landscapeRequest)
	{
		if (!GodotObject.IsInstanceValid(this))
			return;
		int requestedAfter = (int)DisplayServer.ScreenGetOrientation();
		Vector2I windowAfter = DisplayServer.WindowGetSize();
		string status;
		if (landscapeRequest)
		{
			OrientationChangeResult result = WindowOrientationPolicy.ClassifyLandscapeRequest(
				windowBefore.X, windowBefore.Y, windowAfter.X, windowAfter.Y);
			status = result == OrientationChangeResult.Changed ? "changed"
				: result == OrientationChangeResult.Already ? "already"
				: "ignored";
		}
		else
		{
			// 退出小窗恢复可旋转：不再判定成败，只记录窗口事实。
			status = windowAfter.X > windowAfter.Y ? "restored_landscape" : "restored_portrait";
		}
		MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] 请求结果 → reason=" + reason
			+ " windowBefore=" + windowBefore
			+ " windowAfter=" + windowAfter
			+ " requestedAfter=" + requestedAfter
			+ " status=" + status);
	}

	static bool TryQueryMultiWindow(out bool isInMultiWindow)
	{
		isInMultiWindow = false;
		try
		{
			GodotObject runtime = Engine.GetSingleton("AndroidRuntime");
			if (runtime == null)
				return false;
			GodotObject activity = runtime.Call("getActivity").AsGodotObject();
			if (activity == null)
				return false;
			Variant result = activity.Call("isInMultiWindowMode");
			// Java 方法抛异常时 Call 返回 Nil，且异常存于 JavaClassWrapper；必须按失败计，
			// 否则会被误判为 Fullscreen 让横屏请求永不发生。
			if (JavaClassWrapper.GetException() != null)
				return false;
			if (result.VariantType == Variant.Type.Nil)
				return false;
			isInMultiWindow = result.AsBool();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
