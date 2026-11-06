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
/// </summary>
public static class FirstPartyPackBootstrap
{
    const string SourceRoot = "res://compat_packs";
    const string DesktopInstallDirectoryName = "compat_packs";
    const string AndroidInstallRoot = "/storage/emulated/0/emuera/packs";

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

        var result = GEmuera.Core.Compatibility.Packs.CompatPackBundleInstaller.InstallFromPayloads(
            payloads, targetRoot);
        string summary = "[LOAD] FirstPartyPacks installed=" + result.Installed
            + " skipped=" + result.Skipped
            + " kept-user=" + result.KeptUser
            + " failed=" + result.Failed
            + " target=" + targetRoot;
        if (result.Succeeded)
        {
            global::GenericUtils.Info(summary);
            return;
        }

        global::GenericUtils.Warn(summary + "；失败原因：" + string.Join("; ", result.Errors)
            + "（相关 profile 将按映射预检失败回退 v24 基线）。");
    }
}
