using Godot;
using gEmuera.Diagnostics;

/// <summary>
/// 移动端布局诊断汇聚点：日志 + 面板可读的最新快照。
/// 关键：日志走 DiagnosticLogExporter.WriteInfrastructureRecord（基础设施通道），
/// 不受全局 RuntimeLevel / CategorySwitches 过滤——否则用户打开「布局诊断日志」后，
/// 事件会被 WARN 等级 + UI 分类未启用双重丢弃（2026-11-06 真机日志实证：
/// RuntimeLevel=WARN Categories=General,Config，导出的日志里没有任何布局事件）。
/// 该通道仍受 logging.enabled 总闸门、level=none 全关契约与统一限流约束。
/// </summary>
public static class MobileLayoutDiagnostics
{
	/// <summary>构建标记：写进每条事件，便于从真机日志确认装的是哪一版修复。</summary>
	public const string BuildTag = "mobile-layout-20261106b";

	// 桌面复现/集成冒烟用：置位 GEMUERA_FORCE_MOBILE_VIEWPORT=1 时强制开启诊断（APK 不设置）。
	static readonly bool ForceDiagnostics = OS.GetEnvironment("GEMUERA_FORCE_MOBILE_VIEWPORT") == "1";

	/// <summary>debug 构建默认开启（布局事件低频；release 构建仍由设置页开关控制）。</summary>
	public static bool Enabled => MobileLayoutDiagnosticsSettings.LoggingEnabled || OS.IsDebugBuild() || ForceDiagnostics;

	/// <summary>日志或面板任一需要就构造快照，避免关闭时白拼字符串。</summary>
	public static bool Wanted => Enabled || MobileLayoutDiagnosticsSettings.PanelEnabled;

	public static string LatestLayout { get; private set; } = "";
	public static string LatestScroll { get; private set; } = "";
	public static string LatestOrientation { get; private set; } = "";

	static bool bootLogged;

	/// <summary>每进程一次的构建/环境快照（确认 APK 版本与屏幕/安全区初始状态）。</summary>
	public static void PublishBootOnce()
	{
		if (bootLogged || !Enabled)
			return;
		bootLogged = true;
		Emit("UI.LAYOUT.BOOT", EmueraLogLevel.Info, "[VIEWPORT] 布局诊断启动 → build=" + BuildTag
			+ " android=" + (OS.GetName() == "Android")
			+ " debug_build=" + OS.IsDebugBuild()
			+ " engine=" + OS.GetVersion()
			+ " window=" + DisplayServer.WindowGetSize()
			+ " screen=" + DisplayServer.ScreenGetSize());
	}

	public static void PublishLayout(string message)
	{
		LatestLayout = message;
		Emit("UI.LAYOUT.VIEWPORT", EmueraLogLevel.Info, message);
	}

	public static void PublishScroll(string message)
	{
		LatestScroll = message;
		Emit("UI.LAYOUT.SCROLL", EmueraLogLevel.Info, message);
	}

	public static void PublishOrientation(string message)
	{
		LatestOrientation = message;
		Emit("UI.LAYOUT.ORIENTATION", EmueraLogLevel.Info, message);
	}

	public static void Trace(string message)
	{
		Emit("UI.LAYOUT.TRACE", EmueraLogLevel.Info, message);
	}

	public static void Warn(string message)
	{
		if (Enabled)
			Emit("UI.LAYOUT.WARN", EmueraLogLevel.Warn, message);
		else
			GenericUtils.Warn(EmueraLogCategory.UI, () => message);
	}

	static void Emit(string eventId, EmueraLogLevel level, string message)
	{
		if (!Enabled)
			return;
		DiagnosticLogExporter.WriteInfrastructureRecord(level, EmueraLogCategory.UI, eventId,
			message + " build=" + BuildTag, "");
	}
}
