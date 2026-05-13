using System.Collections.Generic;
using Godot;

internal static class MultiLanguage
{
    const string SettingsPath = "user://settings.cfg";
    const string Section = "Display";
    const string LanguageKey = "Language";

    static Dictionary<string, string> texts = new Dictionary<string, string>();
    static bool loaded = false;
    static string currentLanguage = null;

    public static string CurrentLanguage => currentLanguage ?? LoadSavedLanguage();

    public static void Load(string lang = null)
    {
        bool explicitLanguage = !string.IsNullOrEmpty(lang);
        if (!explicitLanguage)
            lang = LoadSavedLanguage();
        if (string.IsNullOrEmpty(lang))
            lang = "default";

        loaded = true;
        currentLanguage = lang;
        texts.Clear();
        var path = $"res://Lang/{lang}.txt";
        if (!FileAccess.FileExists(path))
            path = "res://Lang/default.txt";
        if (!FileAccess.FileExists(path))
            return;

        var content = FileAccess.GetFileAsString(path);
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
        cfg.Load(SettingsPath);
        return (string)cfg.GetValue(Section, LanguageKey, "default");
    }

    static void SaveLanguage(string lang)
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue(Section, LanguageKey, lang);
        cfg.Save(SettingsPath);
    }
}
