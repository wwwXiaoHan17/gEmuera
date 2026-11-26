namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 「用户手装的过期第一方包」替换策略（2026-11-24 自愈）。
/// <para>
/// 第一方包随引擎发布：引擎表面一变（例如新增 capability 后重钉 baseSurfaceHash），旧包就必然
/// 被拒载。用户曾经手动放进安装根的旧副本（解压 zip 覆盖过一次也算）没有状态记录，释放器按
/// 「用户版本」永远不覆盖——于是内置的新包到不了，游戏每次启动都静默回退基线。
/// 本策略只放行一种替换：内置载荷对当前引擎可用，且目标是<b>同名且必然被拒</b>的包
/// （判据见 <see cref="CompatPackManifestPeek"/>）。目标读不出、不是包、是别的包、或仍然可用
/// （哪怕字节与内置不同，例如用户自己修过的版本）一律保留。
/// </para>
/// </summary>
public static class CompatPackStalePackPolicy
{
    /// <summary>被替换的用户旧文件在包目录内的备份文件名后缀（完整名为 .&lt;packId&gt;.dll&lt;后缀&gt;，隐藏文件）。</summary>
    public const string StaleBackupSuffix = ".stale-bak";

    /// <summary>
    /// 判断能否用 <paramref name="payloadBytes"/>（内置载荷）替换 <paramref name="targetBytes"/>（安装根里的现有文件）。
    /// 只读两份字节的内嵌清单，不装载、不执行；任何读不出的输入都按「不替换」处理。
    /// </summary>
    public static bool ShouldReplaceStalePack(
        byte[] payloadBytes,
        byte[] targetBytes,
        CompatPackEngineHeader engine,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(engine);
        CompatPackPeekResult payload = CompatPackManifestPeek.Assess(payloadBytes, engine);
        if (!payload.IsCompatible || payload.Manifest is null)
        {
            reason = "内置包自身对当前引擎不可用（" + payload.ShortReason + "），不替换。";
            return false;
        }

        CompatPackPeekResult target = CompatPackManifestPeek.Assess(targetBytes, engine);
        if (target.IsCompatible)
        {
            reason = "目标包仍可用，保留。";
            return false;
        }
        if (target.Manifest is null)
        {
            reason = "目标清单读不出，无法确认是过期同名包，保留。";
            return false;
        }
        if (!string.Equals(target.Manifest.PackId, payload.Manifest.PackId, StringComparison.Ordinal))
        {
            reason = "目标是另一个包（" + target.Manifest.PackId + "），保留。";
            return false;
        }

        reason = "目标包对当前引擎已不可用（" + target.ShortReason + "），按内置包 "
            + payload.Manifest.PackVersion + " 替换。";
        return true;
    }

    /// <summary>
    /// 生成给 <see cref="CompatPackBundleInstaller.InstallFromPayloads"/> 用的判定委托：从磁盘读目标文件后
    /// 按 <see cref="ShouldReplaceStalePack"/> 裁决。目标缺失或读失败一律返回 false（不替换），
    /// 异常不逃逸到释放链。
    /// </summary>
    public static Func<CompatPackBundlePayload, string, bool> CreateStaleFirstPartyReplacer(CompatPackEngineHeader engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return (payload, targetPath) =>
        {
            try
            {
                if (!File.Exists(targetPath))
                    return false;
                return ShouldReplaceStalePack(payload.DllBytes, File.ReadAllBytes(targetPath), engine, out _);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        };
    }
}
