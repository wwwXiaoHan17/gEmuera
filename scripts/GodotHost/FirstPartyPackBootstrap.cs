using System;
using GEmuera.Core.Compatibility.Packs;
using Godot;

/// <summary>
/// 第一方兼容包启动释放（执行设计 §6.1/§6.2）：
/// 把 res://compat_packs/&lt;packId&gt;/&lt;packId&gt;.dll（导出/APK PCK 内 staging）释放到安装根，
/// 供 launcher 按生产映射的 relativePath 预检与注入 GEMUERA_COMPAT_PACKS。
/// <para>
/// 时机（builder 裁定）：必须在权限解析后的首次包扫描之前调用；Android 未拿到
/// "所有文件访问"时只记 Info 并跳过（下次 resume/扫描重试），绝不写失败日志或阻断启动。
/// 编辑器模式跳过释放：编辑器直接扫 res://compat_packs（映射根也按项目根解析）。
/// </para>
/// <para>
/// 过期第一方包自愈（2026-11-24）：用户曾手动放进安装根的旧包没有状态记录，原先永远不被内置
/// 包更新，引擎表面一变就被拒载。现在释放时注入 <see cref="CompatPackStalePackPolicy"/> 判定，
/// 把「同名且对当前引擎必然被拒」的用户文件换成内置包（旧文件备份在包目录内）；其余用户文件照旧保留。
/// 本类同时向启动器提供自愈所需的两样宿主事实：引擎头部契约值与内置包路径。
/// </para>
/// </summary>
public static class FirstPartyPackBootstrap
{
    const string SourceRoot = "res://compat_packs";
    const string DesktopInstallDirectoryName = "compat_packs";
    const string AndroidInstallRoot = "/storage/emulated/0/emuera/packs";
    /// <summary>引擎唯一存活的基线档案；第一方包只允许叠加在它上面，引擎头部值也按它投影。</summary>
    const string BaselineProfileId = "v24pure";

    static CompatPackEngineHeader engineHeader;
    static bool engineHeaderResolved;

    /// <summary>
    /// 当前引擎的头部契约值，与引擎真实加载校验取自同一个构造点
    /// （CompatPackHost.BuildValidationContext），因此预检与加载同源。取不到时返回 null——
    /// 调用方跳过自愈，绝不阻断启动。结果进程内缓存。
    /// </summary>
    internal static CompatPackEngineHeader TryGetEngineHeader()
    {
        if (engineHeaderResolved)
            return engineHeader;
        engineHeaderResolved = true;
        try
        {
            engineHeader = CompatPackEngineHeader.FromContext(
                MinorShift.Emuera.Compatibility.CompatPackHost.BuildValidationContext(BaselineProfileId));
        }
        catch (Exception exception)
        {
            engineHeader = null;
            global::GenericUtils.Warn(
                "[LOAD] 引擎头部契约值取不到，跳过过期兼容包自愈：" + exception.Message);
        }
        return engineHeader;
    }

    /// <summary>
    /// App 内置包在本平台的文件路径（一包一目录）。编辑器模式 = 项目 res://compat_packs；
    /// 导出端 = 释放目标根（Android 主存储 emuera/packs，桌面 exe 同级 compat_packs）。
    /// 文件当前不可见（Android 未授权、或 staging 缺包）时返回 false。
    /// </summary>
    internal static bool TryGetBundledPackPath(string packId, out string path)
    {
        path = "";
        if (!CompatPackBundleInstaller.IsValidPackId(packId))
            return false;
        string root;
        if (OS.HasFeature("editor"))
        {
            root = ProjectSettings.GlobalizePath(SourceRoot);
        }
        else if (OS.GetName() == "Android")
        {
            root = AndroidInstallRoot;
        }
        else
        {
            string executableDirectory = OS.GetExecutablePath().GetBaseDir();
            if (string.IsNullOrEmpty(executableDirectory))
                return false;
            root = executableDirectory.TrimEnd('/', '\\') + "/" + DesktopInstallDirectoryName;
        }
        if (string.IsNullOrEmpty(root))
            return false;
        path = root.TrimEnd('/', '\\') + "/" + packId + "/" + packId + ".dll";
        return FileAccess.FileExists(path);
    }

    /// <summary>读一个包文件的内容供预检使用；不存在、为空或读失败一律返回 null（不抛异常）。</summary>
    internal static byte[] TryReadPackBytes(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !FileAccess.FileExists(path))
                return null;
            byte[] bytes = FileAccess.GetFileAsBytes(path);
            return bytes is { Length: > 0 } ? bytes : null;
        }
        // 不 using System.IO：它的 FileAccess 枚举会与 Godot.FileAccess 撞名。
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>在 launcher 首次/每次包扫描前调用；内部按字节幂等，重复调用安全。</summary>
    public static void EnsureInstalled()
    {
        if (OS.HasFeature("editor"))
            return; // 编辑器/legacy-runner 直接读 res://compat_packs，不做释放。

        if (OS.GetName() == "Android")
        {
            using var storage = DirAccess.Open("/storage/emulated/0");
            if (storage == null)
            {
                global::GenericUtils.Info(
                    "[LOAD] 等待存储权限后释放内置兼容包（Android 未授予所有文件访问）。");
                return;
            }
        }

        string targetRoot = OS.GetName() == "Android"
            ? AndroidInstallRoot
            : OS.GetExecutablePath().GetBaseDir().TrimEnd('/', '\\') + "/" + DesktopInstallDirectoryName;

        var payloads = new System.Collections.Generic.List<GEmuera.Core.Compatibility.Packs.CompatPackBundlePayload>();
        using var source = DirAccess.Open(SourceRoot);
        if (source == null)
        {
            global::GenericUtils.Info("[LOAD] 内置兼容包 staging 缺省（" + SourceRoot + "）：跳过释放。");
            return;
        }
        source.IncludeHidden = false;
        foreach (string packId in source.GetDirectories())
        {
            if (!GEmuera.Core.Compatibility.Packs.CompatPackBundleInstaller.IsValidPackId(packId))
            {
                global::GenericUtils.Warn("[LOAD] 内置兼容包 staging 子目录非法，已跳过：" + packId);
                continue;
            }
            string dllResPath = SourceRoot + "/" + packId + "/" + packId + ".dll";
            if (!FileAccess.FileExists(dllResPath))
            {
                global::GenericUtils.Warn("[LOAD] 内置兼容包 staging 缺少主 DLL：" + dllResPath);
                continue;
            }
            byte[] bytes = FileAccess.GetFileAsBytes(dllResPath);
            if (bytes is { Length: > 0 })
                payloads.Add(new GEmuera.Core.Compatibility.Packs.CompatPackBundlePayload(packId, bytes));
        }

        if (payloads.Count == 0)
        {
            global::GenericUtils.Info("[LOAD] 内置兼容包 staging 为空：跳过释放。");
            return;
        }

        // 过期第一方包替换判定：内置载荷可用、目标是同名且必然被拒的用户文件时才放行。
        CompatPackEngineHeader engine = TryGetEngineHeader();
        var result = CompatPackBundleInstaller.InstallFromPayloads(
            payloads, targetRoot, null,
            engine is null ? null : CompatPackStalePackPolicy.CreateStaleFirstPartyReplacer(engine));
        string summary = "[LOAD] FirstPartyPacks installed=" + result.Installed
            + " skipped=" + result.Skipped
            + " kept-user=" + result.KeptUser
            + " replaced-stale=" + result.ReplacedStale
            + " failed=" + result.Failed
            + " target=" + targetRoot;
        if (result.ReplacedStale > 0)
        {
            // 自愈是用户该知道的事：按 Warn 级落日志（默认日志级别下也留痕）。
            global::GenericUtils.Warn("[LOAD] 已用内置包替换用户手装的过期第一方包："
                + string.Join(", ", result.ReplacedStalePackIds)
                + "（旧文件备份为各自包目录内的隐藏文件 .<包名>.dll"
                + CompatPackStalePackPolicy.StaleBackupSuffix + "）");
        }
        if (result.Succeeded)
        {
            global::GenericUtils.Info(summary);
            return;
        }

        global::GenericUtils.Warn(summary + "；失败原因：" + string.Join("; ", result.Errors)
            + "（相关 profile 将按映射预检失败回退 v24 基线）。");
    }
}
