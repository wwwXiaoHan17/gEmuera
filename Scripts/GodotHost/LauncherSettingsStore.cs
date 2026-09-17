using System;
using System.Collections.Generic;
using Godot;

namespace gEmuera.GodotHost;

/// <summary>
/// user://launcher.cfg [launcher] 节的唯一读写点；兼容包按游戏选择存于同文件
/// [compat_packs] 节（键=游戏根目录规范化，见 CompatPackLauncherConfig）。
/// 路径与键名是存量用户配置及 WS1 引擎侧（emuera_debug_mode / debug_show_window）的契约，不可改名。
/// 读点在文件缺失/损坏时返回调用方默认值；写点 load-modify-save，保留节内其他键。
/// profile 字符串的归一化/校验语义留在启动器（FirstWindow），本类只做原始存取。
/// </summary>
public static class LauncherSettingsStore
{
	const string SettingsPath = "user://launcher.cfg";
	const string Section = "launcher";
	const string LastGamePathKey = "last_game_path";
	const string LastCoreProfileKey = "last_core_profile";
	const string AdvancedCompatibilityKey = "advanced_compatibility";
	const string ManualCoreProfileKey = "manual_core_profile";
	const string EmueraDebugModeKey = "emuera_debug_mode";
	const string DebugShowWindowKey = "debug_show_window";

	static bool TryLoad(out ConfigFile config)
	{
		config = new ConfigFile();
		return config.Load(SettingsPath) == Error.Ok;
	}

	static ConfigFile LoadOrEmpty()
	{
		_ = TryLoad(out var config);
		return config;
	}

	public static string LoadLastGamePath()
	{
		if (!TryLoad(out var config))
			return null;
		return config.GetValue(Section, LastGamePathKey, "").As<string>();
	}

	/// <summary>文件缺失时返回 defaultValue（调用方持有 profile 语义默认）。</summary>
	public static string LoadLastCoreProfileName(string defaultValue)
	{
		if (!TryLoad(out var config))
			return defaultValue;
		return config.GetValue(Section, LastCoreProfileKey, defaultValue).As<string>();
	}

	public static bool LoadAdvancedCompatibility()
	{
		if (!TryLoad(out var config))
			return false;
		return config.GetValue(Section, AdvancedCompatibilityKey, false).AsBool();
	}

	public static string LoadManualCoreProfileName(string defaultValue)
	{
		if (!TryLoad(out var config))
			return defaultValue;
		return config.GetValue(Section, ManualCoreProfileKey, defaultValue).As<string>();
	}

	public static bool LoadEmueraDebugMode()
	{
		if (!TryLoad(out var config))
			return false;
		return config.GetValue(Section, EmueraDebugModeKey, false).AsBool();
	}

	public static bool LoadDebugShowWindow()
	{
		if (!TryLoad(out var config))
			return true;
		return config.GetValue(Section, DebugShowWindowKey, true).AsBool();
	}

	public static void SaveLastGamePath(string path, string coreProfileName)
	{
		var config = LoadOrEmpty();
		config.SetValue(Section, LastGamePathKey, path);
		config.SetValue(Section, LastCoreProfileKey, coreProfileName);
		config.Save(SettingsPath);
	}

	public static void SaveCompatibilitySettings(bool advancedCompatibility, string manualCoreProfileName)
	{
		var config = LoadOrEmpty();
		config.SetValue(Section, AdvancedCompatibilityKey, advancedCompatibility);
		config.SetValue(Section, ManualCoreProfileKey, manualCoreProfileName);
		config.Save(SettingsPath);
	}

	public static void SaveDebugSettings(bool emueraDebugMode, bool debugShowWindow)
	{
		var config = LoadOrEmpty();
		config.SetValue(Section, EmueraDebugModeKey, emueraDebugMode);
		config.SetValue(Section, DebugShowWindowKey, debugShowWindow);
		config.Save(SettingsPath);
	}

	// —— 兼容包按游戏选择（[compat_packs] 节）——
	// 键 = 游戏根目录规范化（CompatPackLauncherConfig.NormalizeGameKey），值 = 分号分隔的包路径串。
	// 本类只做 raw 存取；解析/匹配语义在 Core 的 CompatPackLauncherConfig。

	const string CompatPacksSection = "compat_packs";

	/// <summary>整节读取（游戏键 → 分号串）。文件缺失/节缺失 → 空字典。</summary>
	public static Dictionary<string, string> LoadCompatPackSelections()
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		if (!TryLoad(out var config) || !config.HasSection(CompatPacksSection))
			return result;
		foreach (string key in config.GetSectionKeys(CompatPacksSection))
			result[key] = config.GetValue(CompatPacksSection, key, "").As<string>() ?? "";
		return result;
	}

	/// <summary>保存单个游戏的选择；空串清除该键（不留孤儿条目）。</summary>
	public static void SaveCompatPackSelection(string normalizedGameKey, string packedSelection)
	{
		if (string.IsNullOrEmpty(normalizedGameKey))
			return;
		var config = LoadOrEmpty();
		if (string.IsNullOrEmpty(packedSelection))
		{
			if (config.HasSectionKey(CompatPacksSection, normalizedGameKey))
				config.EraseSectionKey(CompatPacksSection, normalizedGameKey);
		}
		else
		{
			config.SetValue(CompatPacksSection, normalizedGameKey, packedSelection);
		}
		config.Save(SettingsPath);
	}
}
