namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// launcher.cfg 兼容包选择的纯文本逻辑（设计 §5.1：显式启用，禁扫描）：游戏根目录 →
/// 启用包路径清单的序列化/解析/匹配。存储层（LauncherSettingsStore）只管 raw 读写，
/// 本类持有规范化语义：
///   游戏键 = 游戏根目录去尾斜杠后小写化（Windows 路径大小写不敏感）；
///   路径清单 = 分号分隔，逐项 trim、去空、保序去重（存在性校验留给加载器，fail-closed）。
/// </summary>
public static class CompatPackLauncherConfig
{
    /// <summary>游戏根目录 → 规范化配置键（去尾斜杠 + 小写）。</summary>
    public static string NormalizeGameKey(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return string.Empty;
        return gameRoot.TrimEnd('/', '\\').ToLowerInvariant();
    }

    /// <summary>分号串 → 路径清单（trim/去空/保序去重）。null/空串 → 空清单。</summary>
    public static IReadOnlyList<string> ParseSelection(string? packed)
    {
        if (string.IsNullOrWhiteSpace(packed))
            return Array.Empty<string>();
        var result = new List<string>();
        foreach (string entry in packed.Split(';'))
        {
            string trimmed = entry.Trim();
            if (trimmed.Length == 0 || result.Contains(trimmed, StringComparer.Ordinal))
                continue;
            result.Add(trimmed);
        }
        return result;
    }

    /// <summary>路径清单 → 分号串（空清单 → 空串，表示清除该游戏的选择）。</summary>
    public static string SerializeSelection(IEnumerable<string>? paths)
    {
        if (paths is null)
            return string.Empty;
        return string.Join(";", paths
            .Select(path => (path ?? "").Trim())
            .Where(path => path.Length > 0));
    }

    /// <summary>从 raw 映射（配置键 → 分号串）查指定游戏的启用清单；键不存在返回 false。</summary>
    public static bool TryGetSelectionForGame(
        IReadOnlyDictionary<string, string> selections,
        string? gameRoot,
        out IReadOnlyList<string> paths)
    {
        paths = Array.Empty<string>();
        if (selections is null || string.IsNullOrWhiteSpace(gameRoot))
            return false;
        string key = NormalizeGameKey(gameRoot);
        if (key.Length == 0 || !selections.TryGetValue(key, out string? packed))
            return false;
        paths = ParseSelection(packed);
        return true;
    }
}
