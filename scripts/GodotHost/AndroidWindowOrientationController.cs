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
		if (!TryQueryMultiWindow(out bool isMultiWindow))
		{
			if (failureWarnings++ < MaxFailureWarnings)
				MobileLayoutDiagnostics.Warn("[ORIENTATION] 多窗口检测失败 → AndroidRuntime/getActivity/isInMultiWindowMode 不可用");
			// 检测失败时窗口模式按非小窗兜底：真在分屏/小窗下系统会拒绝全屏请求，无害。
			// 方向逻辑维持原状（previous 不动，避免误判边沿）。
			ApplyImmersiveWindowMode(WindowLayoutKind.Fullscreen, "query_fallback");
			return;
		}
		failureWarnings = 0;
		WindowLayoutKind current = isMultiWindow ? WindowLayoutKind.MultiWindow : WindowLayoutKind.Fullscreen;
		ApplyImmersiveWindowMode(current, isMultiWindow ? "enter_multi_window" : "evaluate");
		OrientationRequest request = WindowOrientationPolicy.Decide(previous, current);
		previous = current;
		MobileLayoutDiagnostics.Trace("[ORIENTATION] 状态 → multiWindow=" + isMultiWindow
			+ " window=" + DisplayServer.WindowGetSize()
			+ " screen=" + DisplayServer.ScreenGetSize()
			+ " requestedRaw=" + (int)DisplayServer.ScreenGetOrientation());
		if (request == OrientationRequest.None)
			return;
		RequestOrientation(request, isMultiWindow ? "enter_multi_window" : "exit_multi_window");
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
			CallDeferred(nameof(Evaluate));
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
