namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// launcher.cfg 兼容包选择的纯文本逻辑（设计 §5.1：显式启用，禁扫描）：游戏根目录 →
/// 启用包路径清单的序列化/解析/匹配。存储层（LauncherSettingsStore）只管 raw 读写，
/// 本类持有规范化语义：
///   游戏键 = 游戏根目录去尾斜杠，按 <see cref="GameKeyComparer"/> 的平台大小写语义折叠
///   （Windows 路径大小写不敏感 → 小写化；Android/Linux 文件系统大小写敏感 → 保留大小写，
///   仅差大小写的两个游戏根是不同目录，键不得折叠，E2-R4）；
///   路径清单 = 分号分隔，逐项 trim、去空、保序去重（存在性校验留给加载器，fail-closed）；
///   启用清单注入 = <see cref="MergeSelection"/>：launcher 按游戏选择优先，外部环境变量
///   仅在无选择时作为诊断输入（E2-R3）。
/// </summary>
public static class CompatPackLauncherConfig
{
    /// <summary>
    /// 游戏键比较器（平台语义）：Windows 路径大小写不敏感 →
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>；其余平台（Android/Linux）文件系统
    /// 大小写敏感 → <see cref="StringComparer.Ordinal"/>。游戏键与包路径键的匹配统一走本
    /// 比较器，禁止在调用侧自行小写化（那会在大小写敏感文件系统上折叠两个不同的游戏）。
    /// </summary>
    public static StringComparer GameKeyComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>按 caseSensitiveFilesystem 显式选择游戏键比较器（平台默认走 <see cref="GameKeyComparer"/>）。</summary>
    static StringComparer GameKeyComparerFor(bool caseSensitiveFilesystem) =>
        caseSensitiveFilesystem ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// 游戏根目录 → 规范化配置键（平台默认包装：Windows 折叠大小写，其余平台保留，
    /// 见 <see cref="NormalizeGameKey(string?, bool)"/>）。
    /// </summary>
    public static string NormalizeGameKey(string? gameRoot)
    {
        return NormalizeGameKey(gameRoot, caseSensitiveFilesystem: !OperatingSystem.IsWindows());
    }

    /// <summary>
    /// 游戏根目录 → 规范化配置键：去尾部斜杠（两种分隔符都认）；caseSensitiveFilesystem=
    /// false（Windows 等大小写不敏感平台）折叠大小写——既有 launcher.cfg 存储键语义，
    /// 逐字节不变；=true（Android/Linux 等大小写敏感平台）保留大小写，仅差大小写的两个
    /// 游戏根是不同目录，不得共用配置键（E2-R4）。
    /// 分隔符形式不做跨平台改写（"/" 与 "\" 各自保留）：键的存取两端同源于同一 GameRoot
    /// 字符串，改写只会撕裂既有存储键（历史键 = GameRoot 原形折叠大小写，"/" 形态来自
    /// Godot 路径）；包路径的完整分隔符统一由 <see cref="NormalizePackPath"/> 承担。
    /// </summary>
    public static string NormalizeGameKey(string? gameRoot, bool caseSensitiveFilesystem)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return string.Empty;
        string trimmed = gameRoot.TrimEnd('/', '\\');
        return caseSensitiveFilesystem ? trimmed : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// 兼容包路径比对键规范化：Path.GetFullPath（相对→绝对、消除 ./..）+ 分隔符统一为
    /// 当前平台目录分隔符 + 去尾斜杠；大小写语义由 <see cref="GameKeyComparer"/> 承担，
    /// 键本身保留大小写（大小写敏感文件系统上 A.dll 与 a.dll 是不同文件）。空白输入 →
    /// 空串；GetFullPath 对非法路径字符抛出的异常按 trim 后原样返回——键生成路径绝不抛
    /// 异常，存在性校验留给加载器（fail-closed）。
    /// </summary>
    public static string NormalizePackPath(string? path)
    {
        string trimmed = (path ?? "").Trim();
        if (trimmed.Length == 0)
            return string.Empty;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmed);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or IOException or System.Security.SecurityException)
        {
            fullPath = trimmed;
        }
        string unified = fullPath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.DirectorySeparatorChar != '\\')
            unified = unified.Replace('\\', Path.DirectorySeparatorChar);
        return unified.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

    /// <summary>
    /// 合并两路启用清单输入（均为分号分隔路径串；launcher 按游戏存储的选择 vs 外部
    /// GEMUERA_COMPAT_PACKS 环境变量）：stored 非空（解析后至少一条路径）时优先——外部
    /// 变量不得永久遮蔽 launcher 按游戏选择（E2-R3）；stored 为空时外部变量仅作为诊断
    /// 输入透传（无选择游戏的外部显式设置契约保持）；两路皆空 → 空串（清除注入，回纯
    /// v24）。返回值已经 parse→serialize 归一（trim/去空/去重保序）。
    /// </summary>
    public static string MergeSelection(string? stored, string? external)
    {
        return MergeSelection(stored, external, mapped: null);
    }

    /// <summary>
    /// 三路合并（执行设计 §4.5 / P-D spec §1.2）：P0 launcher.cfg 按游戏显式选择 &gt;
    /// P1 外部 GEMUERA_COMPAT_PACKS（诊断）&gt; P2 profile→第一方包默认映射 &gt; P3 纯基线。
    /// 低优先级仅在更高优先级全部为空时生效；任一非空层经 parse→serialize 归一。
    /// </summary>
    public static string MergeSelection(string? stored, string? external, string? mapped)
    {
        IReadOnlyList<string> storedPaths = ParseSelection(stored);
        if (storedPaths.Count > 0)
            return SerializeSelection(storedPaths);
        IReadOnlyList<string> externalPaths = ParseSelection(external);
        if (externalPaths.Count > 0)
            return SerializeSelection(externalPaths);
        IReadOnlyList<string> mappedPaths = ParseSelection(mapped);
        if (mappedPaths.Count > 0)
            return SerializeSelection(mappedPaths);
        return string.Empty;
    }

    /// <summary>从 raw 映射（配置键 → 分号串）查指定游戏的启用清单；键不存在返回 false。
    /// 按 <see cref="GameKeyComparer"/> 平台语义匹配。</summary>
    public static bool TryGetSelectionForGame(
        IReadOnlyDictionary<string, string> selections,
        string? gameRoot,
        out IReadOnlyList<string> paths)
    {
        return TryGetSelectionForGame(
            selections, gameRoot, caseSensitiveFilesystem: !OperatingSystem.IsWindows(), out paths);
    }

    /// <summary>
    /// <see cref="TryGetSelectionForGame(IReadOnlyDictionary{string, string}, string?, out IReadOnlyList{string})"/>
    /// 的显式大小写语义版本（测试与显式判定路径用）：caseSensitiveFilesystem=true 时
    /// 键保留大小写并按 Ordinal 匹配，仅差大小写的两个游戏根互不命中（E2-R4）。
    /// </summary>
    public static bool TryGetSelectionForGame(
        IReadOnlyDictionary<string, string> selections,
        string? gameRoot,
        bool caseSensitiveFilesystem,
        out IReadOnlyList<string> paths)
    {
        paths = Array.Empty<string>();
        if (selections is null || string.IsNullOrWhiteSpace(gameRoot))
            return false;
        string key = NormalizeGameKey(gameRoot, caseSensitiveFilesystem);
        if (key.Length == 0)
            return false;
        // 字典比较器不受本类控制（存储层返回默认 Ordinal 字典），按平台游戏键比较器线性
        // 匹配：游戏数为个位~十位量级，开销可忽略，换来与 NormalizeGameKey 一致的大小写语义。
        StringComparer comparer = GameKeyComparerFor(caseSensitiveFilesystem);
        foreach (KeyValuePair<string, string> pair in selections)
        {
            if (!comparer.Equals(pair.Key, key))
                continue;
            paths = ParseSelection(pair.Value);
            return true;
        }
        return false;
    }
}
