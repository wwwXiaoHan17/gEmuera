/// <summary>
/// 移动端布局诊断汇聚点：日志（受设置门控）+ 面板可读的最新快照。
/// 日志走 GenericUtils 路由（UI 分类），遵守 docs/logging-convention.md 单行 k=v 句式。
/// </summary>
public static class MobileLayoutDiagnostics
{
	public static bool Enabled => MobileLayoutDiagnosticsSettings.LoggingEnabled;

	public static string LatestLayout { get; private set; } = "";
	public static string LatestScroll { get; private set; } = "";
	public static string LatestOrientation { get; private set; } = "";

	public static void PublishLayout(string message)
	{
		LatestLayout = message;
		if (Enabled)
			GenericUtils.Info(EmueraLogCategory.UI, () => message);
	}

	public static void PublishScroll(string message)
	{
		LatestScroll = message;
		if (Enabled)
			GenericUtils.Info(EmueraLogCategory.UI, () => message);
	}

	public static void PublishOrientation(string message)
	{
		LatestOrientation = message;
		if (Enabled)
			GenericUtils.Info(EmueraLogCategory.UI, () => message);
	}

	public static void Trace(string message)
	{
		if (Enabled)
			GenericUtils.Info(EmueraLogCategory.UI, () => message);
	}

	public static void Warn(string message)
	{
		GenericUtils.Warn(EmueraLogCategory.UI, () => message);
	}
}
