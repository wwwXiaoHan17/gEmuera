using System.Collections.Generic;
using Godot;

internal static class MultiLanguage
{
    static Dictionary<string, string> texts = new Dictionary<string, string>();
    static bool loaded = false;

    public static void Load(string lang = "default")
    {
        loaded = true;
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
    }

    public static string Get(string key, string fallback = null)
    {
        if (!loaded)
            Load();
        if (texts.TryGetValue(key, out var value))
            return string.IsNullOrEmpty(value) ? fallback ?? key : value;
        return fallback ?? key;
    }
}
