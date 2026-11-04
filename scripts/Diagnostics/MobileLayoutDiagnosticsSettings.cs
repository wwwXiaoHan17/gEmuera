using Godot;

/// <summary>
/// 移动端布局诊断开关（user://settings.cfg [Display] 段，APK 可写）。
/// 用于在真机设置页开启布局诊断日志/状态面板，默认关闭。
/// </summary>
public static class MobileLayoutDiagnosticsSettings
{
	public const string SettingsPath = "user://settings.cfg";
	public const string SettingsSection = "Display";
	public const string LoggingKey = "MobileLayoutLogging";
	public const string PanelKey = "MobileLayoutPanel";

	static bool? loggingEnabled;
	static bool? panelEnabled;

	public static bool LoggingEnabled
	{
		get { if (loggingEnabled == null) loggingEnabled = Load(LoggingKey); return loggingEnabled.Value; }
		set { loggingEnabled = value; Save(LoggingKey, value); }
	}

	public static bool PanelEnabled
	{
		get { if (panelEnabled == null) panelEnabled = Load(PanelKey); return panelEnabled.Value; }
		set { panelEnabled = value; Save(PanelKey, value); EmueraContent.instance?.ApplyMobileLayoutPanelSetting(); }
	}

	static bool Load(string key)
	{
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		return (bool)cfg.GetValue(SettingsSection, key, false);
	}

	static void Save(string key, bool value)
	{
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		cfg.SetValue(SettingsSection, key, value);
		cfg.Save(SettingsPath);
	}
}
