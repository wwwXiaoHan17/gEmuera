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

	WindowLayoutKind previous = WindowLayoutKind.Unknown;
	Timer debounceTimer;
	int failureWarnings;
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
		GetWindow().SizeChanged += OnWindowSizeChanged;
		GetViewport().SizeChanged += OnWindowSizeChanged;
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
			return;
		}
		failureWarnings = 0;
		WindowLayoutKind current = isMultiWindow ? WindowLayoutKind.MultiWindow : WindowLayoutKind.Fullscreen;
		OrientationRequest request = WindowOrientationPolicy.Decide(previous, current);
		previous = current;
		MobileLayoutDiagnostics.Trace("[ORIENTATION] state -> multiWindow=" + isMultiWindow);
		if (request == OrientationRequest.None)
			return;
		RequestOrientation(request, isMultiWindow ? "enter_multi_window" : "exit_multi_window");
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
