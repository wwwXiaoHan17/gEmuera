namespace GEmuera.Core.Compatibility.Packs;

/// <summary>一次改绑：原本选中的包文件 → 改用的内置包文件，以及给用户看的一句话说明。</summary>
public sealed record CompatPackRebindNotice(string OldPath, string NewPath, string Message);

/// <summary>
/// 启用清单经自愈处理后的结果：<see cref="Paths"/> 是真正应注入引擎的包路径清单（保序、已去重）；
/// <see cref="Rebinds"/> 记录每处改绑；<see cref="Warnings"/> 是换不了、只能提示的不可用包。
/// </summary>
public sealed record CompatPackSelectionResolution(
    IReadOnlyList<string> Paths,
    IReadOnlyList<CompatPackRebindNotice> Rebinds,
    IReadOnlyList<string> Warnings)
{
    public bool Changed => Rebinds.Count > 0 || Warnings.Count > 0;
}

/// <summary>
/// 启用清单里的「过期第一方包」改绑判定（启动器注入引擎之前执行，纯逻辑）。
/// <para>
/// 逐个预检选中的包文件：对当前引擎可用的原样保留；不可用的，若 App 内置了<b>清单 packId 同名</b>且
/// 可用的包、并且不是同一个文件（路径按平台大小写语义比较），就改用内置包；否则保留原路径并给出警告——
/// 引擎对包集合是整体拒载，所以保留原路径等于「本次回退 v24 基线」，与警告文案一致。
/// </para>
/// <para>
/// 两个刻意的不为：社区包（不在内置表里）永远只提示不替换；内置表按清单里的 packId 查，不按文件名猜
/// （文件读不出来时才退而按文件名查）。同一个文件既是选中的又是内置路径（安卓上被用户覆盖成旧版的那种）
/// 换路径无意义，交给第一方包释放器处理，这里只提示。
/// </para>
/// </summary>
public static class CompatPackStaleSelectionRebinder
{
    /// <summary>预检一个包文件（读字节 + <see cref="CompatPackManifestPeek.Assess(byte[], CompatPackEngineHeader)"/>）。</summary>
    public delegate CompatPackPeekResult PeekFile(string packPath);

    /// <summary>按 packId 查 App 内置包的文件路径；不内置则返回 null。</summary>
    public delegate string? LookupBundledPack(string packId);

    public static CompatPackSelectionResolution Resolve(
        IReadOnlyList<string> selectedPaths,
        PeekFile peek,
        LookupBundledPack bundled,
        StringComparer pathComparer)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ArgumentNullException.ThrowIfNull(peek);
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(pathComparer);

        var paths = new List<string>();
        var rebinds = new List<CompatPackRebindNotice>();
        var warnings = new List<string>();
        foreach (string path in selectedPaths)
        {
            CompatPackPeekResult verdict = SafePeek(peek, path);
            if (verdict.IsCompatible)
            {
                paths.Add(path);
                continue;
            }

            string displayName = SafeFileName(path);
            string? packId = verdict.Manifest?.PackId;
            if (string.IsNullOrEmpty(packId))
            {
                // 读不出清单（文件缺失/损坏）：只能按文件名查内置表。
                string fileName = SafeFileNameWithoutExtension(path);
                packId = fileName is not null && CompatPackBundleInstaller.IsValidPackId(fileName) ? fileName : null;
            }

            string? bundledPath = packId is null ? null : SafeLookup(bundled, packId);
            if (!string.IsNullOrEmpty(bundledPath)
                && !pathComparer.Equals(
                    CompatPackLauncherConfig.NormalizePackPath(path),
                    CompatPackLauncherConfig.NormalizePackPath(bundledPath))
                && SafePeek(peek, bundledPath).IsCompatible)
            {
                string normalizedBundled = CompatPackLauncherConfig.NormalizePackPath(bundledPath);
                bool alreadySelected = paths.Any(existing => pathComparer.Equals(
                    CompatPackLauncherConfig.NormalizePackPath(existing), normalizedBundled));
                if (!alreadySelected)
                    paths.Add(bundledPath);
                rebinds.Add(new CompatPackRebindNotice(
                    path,
                    bundledPath,
                    "兼容包 " + displayName + " 与当前引擎不匹配（" + verdict.ShortReason
                    + "），已改用内置包 " + SafeFileName(bundledPath) + "。"));
                continue;
            }

            paths.Add(path);
            warnings.Add("兼容包 " + displayName + " 与当前引擎不匹配（" + verdict.ShortReason
                + "），本次将以 v24 基线启动，请更新该包。");
        }

        return new CompatPackSelectionResolution(paths, rebinds, warnings);
    }

    static CompatPackPeekResult SafePeek(PeekFile peek, string path)
    {
        try
        {
            CompatPackPeekResult? result = peek(path);
            return result ?? CompatPackManifestPeek.CreateUnreadable("预检没有返回结果。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException or InvalidOperationException)
        {
            return CompatPackManifestPeek.CreateUnreadable("包文件无法读取：" + exception.Message);
        }
    }

    static string? SafeLookup(LookupBundledPack bundled, string packId)
    {
        try
        {
            return bundled(packId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException or InvalidOperationException)
        {
            return null;
        }
    }

    static string SafeFileName(string path)
    {
        try
        {
            string? name = Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException)
        {
            return path;
        }
    }

    static string? SafeFileNameWithoutExtension(string path)
    {
        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception exception) when (exception is ArgumentException or PathTooLongException)
        {
            return null;
        }
    }
}
