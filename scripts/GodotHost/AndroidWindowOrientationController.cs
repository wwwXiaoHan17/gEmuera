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
				MobileLayoutDiagnostics.Warn("[ORIENTATION] query failed -> AndroidRuntime/getActivity/isInMultiWindowMode unavailable");
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
		lastRequestedLandscape = request == OrientationRequest.SensorLandscape;
		MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] request -> reason=" + reason
			+ " target=" + target
			+ " before=" + before
			+ " window=" + windowBefore);
		// 结果观测：2 秒后记录系统是否真的改变了请求方向（changed|ignored）。
		GetTree().CreateTimer(2.0).Timeout += () => ReportResult(reason, target);
	}

	void ReportResult(string reason, DisplayServer.ScreenOrientation target)
	{
		if (!GodotObject.IsInstanceValid(this))
			return;
		int after = (int)DisplayServer.ScreenGetOrientation();
		Vector2I windowAfter = DisplayServer.WindowGetSize();
		string status = after == (int)target ? "changed" : "ignored";
		MobileLayoutDiagnostics.PublishOrientation("[ORIENTATION] result -> reason=" + reason
			+ " target=" + target
			+ " after=" + after
			+ " window=" + windowAfter
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
			isInMultiWindow = result.AsBool();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}
}
