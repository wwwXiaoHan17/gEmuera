using System;
using System.Collections.Generic;
using Godot;

internal static class MultiLanguage
{
	// Fired on the calling thread right after a new language is loaded. Language
	// switching only happens from the UI thread (OptionWindow.OnLanguageSelected),
	// so subscribers refresh their node texts directly; refresh handlers must be
	// idempotent because Load() may also run for the same language.
	public static event Action LanguageChanged;

	const string SettingsPath = "user://settings.cfg";
	const string Section = "Display";
	const string LanguageKey = "Language";

	static Dictionary<string, string> texts = new Dictionary<string, string>();
	static bool loaded = false;
	static string currentLanguage = null;

	/// <summary>
	/// 当前生效的语言 id。未加载时按"已保存偏好 → 系统区域 → default"推导，
	/// 与 <see cref="Load"/> 的解析顺序保持一致，避免设置窗下拉框与界面文字不一致。
	/// </summary>
	public static string CurrentLanguage
	{
		get
		{
			if (currentLanguage != null)
				return currentLanguage;
			string saved = LoadSavedLanguage();
			return string.IsNullOrEmpty(saved) ? DetectSystemLanguage() : saved;
		}
	}

	public static void Load(string lang = null)
	{
		bool explicitLanguage = !string.IsNullOrEmpty(lang);
		if (!explicitLanguage)
			lang = LoadSavedLanguage();
		if (string.IsNullOrEmpty(lang))
			// 未保存任何语言偏好时跟随系统区域：中文系统不该默认显示英文。
			// 用户显式选过语言（含显式选 "default"）会由 LoadSavedLanguage 返回，不受影响。
			lang = DetectSystemLanguage();

		string previous = currentLanguage;
		loaded = true;
		currentLanguage = lang;
		texts.Clear();
		var path = $"res://assets/lang/{lang}.txt";
		if (!Godot.FileAccess.FileExists(path))
			path = "res://assets/lang/default.txt";
		if (!Godot.FileAccess.FileExists(path))
			return;

		var content = Godot.FileAccess.GetFileAsString(path);
		var lines = content.Split('\n');
		foreach (var line in lines)
		{
			var trimmed = line.Trim();
			if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";"))
				continue;
			var idx = trimmed.IndexOf('=');
			if (idx < 0)
				continue;
			var key = trimmed.Substring(0, idx).Trim();
			var value = trimmed.Substring(idx + 1).Trim();
			texts[key] = value;
		}

		if (explicitLanguage)
			SaveLanguage(lang);

		// Notify UI that cached one-time texts must be re-read. Fire only on an
		// actual language change so repeated Load() calls stay cheap and refresh
		// handlers stay idempotent.
		if (previous != currentLanguage)
			LanguageChanged?.Invoke();
	}

	public static string Get(string key, string fallback = null)
	{
		if (!loaded)
			Load();
		if (texts.TryGetValue(key, out var value))
			return string.IsNullOrEmpty(value) ? fallback ?? key : value;
		return fallback ?? key;
	}

	static string LoadSavedLanguage()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(SettingsPath) != Error.Ok)
			return null; // 无配置文件：交给 DetectSystemLanguage 决定
		var value = (string)cfg.GetValue(Section, LanguageKey, "");
		return string.IsNullOrEmpty(value) ? null : value;
	}

	/// <summary>
	/// 按系统区域挑选语言文件；无匹配时回退 default（英文）。
	/// 只覆盖仓库实际提供的语言：zh_cn / en_us / jp。
	/// </summary>
	static string DetectSystemLanguage()
	{
		try
		{
			string locale = OS.GetLocale(); // 例如 "zh_CN"、"ja_JP"
			if (string.IsNullOrEmpty(locale))
				return "default";
			string lower = locale.ToLowerInvariant();
			if (lower.StartsWith("zh"))
				return "zh_cn";
			if (lower.StartsWith("ja"))
				return "jp";
			if (lower.StartsWith("en"))
				return "en_us";
			return "default";
		}
		catch
		{
			return "default";
		}
	}

	static void SaveLanguage(string lang)
	{
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		cfg.SetValue(Section, LanguageKey, lang);
		cfg.Save(SettingsPath);
	}
}
