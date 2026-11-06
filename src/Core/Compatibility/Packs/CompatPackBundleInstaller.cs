using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 单个内置包载荷：packId + 已读入内存的 DLL 字节（宿主经 Godot FileAccess 从
/// res://compat_packs/&lt;packId&gt;/&lt;packId&gt;.dll 读入；Core 不依赖 Godot）。
/// </summary>
public sealed record CompatPackBundlePayload(string PackId, byte[] DllBytes);

/// <summary>
/// 第一方包释放结果（幂等）：Installed=本次写入；Skipped=目标已是同一字节；
/// KeptUser=目标存在且不是内置上次写入的版本（用户手装/改过，不覆盖）；
/// Failed=逐包失败聚合（不阻断其它包，也不阻断启动）。
/// </summary>
public sealed record CompatPackBundleInstallResult(
    int Installed,
    int Skipped,
    int KeptUser,
    int Failed,
    IReadOnlyList<string> InstalledPackIds,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Failed == 0;
}

/// <summary>
/// 第一方兼容包分发 installer（执行设计 §6）：纯逻辑、无 Godot 依赖。
/// <para>
/// 安装根维护 &lt;targetRoot&gt;/.first-party-packs.json（packId → 上次由内置释放写入的 SHA256）：
/// 目标缺失 → 写；目标 SHA 与记录值不同 → 视为用户手装/改过，不覆盖并计入 KeptUser；
/// 目标 SHA 与记录值相同 → 允许覆盖（内置升级）；目标与源字节相同 → 跳过。
/// 写入使用"临时文件 + rename"保证原子性；单包失败聚合到 Errors，绝不抛给启动链。
/// </para>
/// </summary>
public static partial class CompatPackBundleInstaller
{
    /// <summary>安装根状态文件名：记录每个包上次由内置释放写入的 SHA256。</summary>
    public const string StateFileName = ".first-party-packs.json";

    /// <summary>第一方包主 DLL 文件名（一包一目录：&lt;packId&gt;/&lt;packId&gt;.dll）。</summary>
    public const string MainAssemblyFileName = "pack.gemuera.NAME.dll";

    const int StateVersion = 1;
    const int MaxPackIdLength = 96;

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackIdPattern();

    /// <summary>从物理目录枚举 &lt;packId&gt;/&lt;packId&gt;.dll 并释放（桌面/编辑器/runner 用）。</summary>
    public static CompatPackBundleInstallResult InstallFromDirectory(
        string sourceRoot,
        string targetRoot,
        string? statePath = null)
    {
        var payloads = new List<CompatPackBundlePayload>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            return new CompatPackBundleInstallResult(0, 0, 0, 1, Array.Empty<string>(),
                new[] { "内置包 staging 根不存在：" + (sourceRoot ?? "<null>") });

        foreach (string directory in Directory.EnumerateDirectories(sourceRoot).Order(StringComparer.Ordinal))
        {
            string packId = Path.GetFileName(directory);
            if (!IsValidPackId(packId))
            {
                errors.Add("staging 子目录不是合法 packId：'" + packId + "'。");
                continue;
            }
            string dllPath = Path.Combine(directory, packId + ".dll");
            if (!File.Exists(dllPath))
            {
                errors.Add("staging 缺少主 DLL：" + dllPath + "（期望 <packId>/<packId>.dll）。");
                continue;
            }
            try
            {
                payloads.Add(new CompatPackBundlePayload(packId, File.ReadAllBytes(dllPath)));
            }
            catch (Exception exception)
            {
                errors.Add("读取 staging 包失败 " + packId + "：" + exception.GetType().Name + ": " + exception.Message);
            }
        }

        if (payloads.Count == 0)
        {
            errors.Add("staging 未发现任何可释放的第一方包：" + sourceRoot);
            return new CompatPackBundleInstallResult(0, 0, 0, Math.Max(1, errors.Count),
                Array.Empty<string>(), errors);
        }

        CompatPackBundleInstallResult result = InstallFromPayloads(payloads, targetRoot, statePath);
        return new CompatPackBundleInstallResult(
            result.Installed, result.Skipped, result.KeptUser, result.Failed + errors.Count,
            result.InstalledPackIds, errors.Concat(result.Errors).ToArray());
    }

    /// <summary>按内存载荷释放（宿主从 res:// / APK PCK 读入后调用；单测直接覆盖此入口）。</summary>
    public static CompatPackBundleInstallResult InstallFromPayloads(
        IReadOnlyList<CompatPackBundlePayload> payloads,
        string targetRoot,
        string? statePath = null)
    {
        var errors = new List<string>();
        int installed = 0;
        int skipped = 0;
        int keptUser = 0;
        var installedPackIds = new List<string>();
        if (payloads is null || payloads.Count == 0)
            return new CompatPackBundleInstallResult(0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(targetRoot))
            return new CompatPackBundleInstallResult(0, 0, 0, payloads.Count, Array.Empty<string>(),
                new[] { "安装根为空。" });

        string effectiveStatePath = string.IsNullOrWhiteSpace(statePath)
            ? Path.Combine(targetRoot, StateFileName)
            : statePath;
        Dictionary<string, string> installedHashes = LoadState(effectiveStatePath, errors);
        bool stateChanged = false;

        foreach (CompatPackBundlePayload payload in payloads)
        {
            if (payload is null || !IsValidPackId(payload.PackId))
            {
                errors.Add("非法 packId：'" + (payload?.PackId ?? "<null>") + "'（拒绝路径穿越/非法字符）。");
                continue;
            }
            if (!LooksLikePortableExecutable(payload.DllBytes))
            {
                errors.Add("包 " + payload.PackId + " 的 DLL 载荷损坏（缺少 PE/MZ 头或为空）。");
                continue;
            }

            try
            {
                string targetDirectory = Path.Combine(targetRoot, payload.PackId);
                string targetPath = Path.Combine(targetDirectory, payload.PackId + ".dll");
                // 防御纵深：即使 packId 正则被绕过，也拒绝逃逸安装根。
                string fullTargetRoot = Path.GetFullPath(targetRoot);
                string fullTargetPath = Path.GetFullPath(targetPath);
                if (!fullTargetPath.StartsWith(
                        fullTargetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("包 " + payload.PackId + " 的目标路径逃逸安装根。");
                    continue;
                }

                string sourceHash = Convert.ToHexString(SHA256.HashData(payload.DllBytes)).ToLowerInvariant();
                Directory.CreateDirectory(targetDirectory);
                if (!File.Exists(targetPath))
                {
                    WriteAtomically(targetPath, payload.DllBytes);
                    installedHashes[payload.PackId] = sourceHash;
                    stateChanged = true;
                    installed++;
                    installedPackIds.Add(payload.PackId);
                    continue;
                }

                string targetHash = HashFile(targetPath);
                if (string.Equals(targetHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                {
                    // 同字节：跳过写入；仅在记录确实变化时才标记 stateChanged，避免每次启动都重写状态文件。
                    if (!installedHashes.TryGetValue(payload.PackId, out string? recordedSame)
                        || !string.Equals(recordedSame, sourceHash, StringComparison.OrdinalIgnoreCase))
                    {
                        installedHashes[payload.PackId] = sourceHash;
                        stateChanged = true;
                    }
                    skipped++;
                    continue;
                }

                if (installedHashes.TryGetValue(payload.PackId, out string? recordedHash)
                    && string.Equals(recordedHash, targetHash, StringComparison.OrdinalIgnoreCase))
                {
                    // 目标仍是上次内置释放的字节 → 接受内置升级。
                    WriteAtomically(targetPath, payload.DllBytes);
                    installedHashes[payload.PackId] = sourceHash;
                    stateChanged = true;
                    installed++;
                    installedPackIds.Add(payload.PackId);
                    continue;
                }

                // 记录缺失（旧版本升级路径）或用户手装/改过：保留用户版本。
                keptUser++;
            }
            catch (Exception exception)
            {
                errors.Add("释放包 " + payload.PackId + " 失败：" + exception.GetType().Name + ": " + exception.Message);
            }
        }

        if (stateChanged && !TrySaveState(effectiveStatePath, installedHashes, errors))
        {
            // 状态写失败不撤销已写入的 DLL，但必须让调用方看到失败（下次仍能继续）。
        }

        return new CompatPackBundleInstallResult(
            installed, skipped, keptUser, errors.Count, installedPackIds, errors);
    }

    public static bool IsValidPackId(string? packId)
    {
        return !string.IsNullOrWhiteSpace(packId)
            && packId.Length <= MaxPackIdLength
            && PackIdPattern().IsMatch(packId)
            && packId != "."
            && packId != "..";
    }

    static bool LooksLikePortableExecutable(byte[]? bytes)
    {
        return bytes is { Length: >= 64 } && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z';
    }

    static string HashFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    static void WriteAtomically(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("目标路径缺少目录：" + path);
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(
            directory,
            "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* 清理失败不影响已提交结果；下次释放会再写。 */ }
            }
        }
    }

    static Dictionary<string, string> LoadState(string statePath, List<string> errors)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(statePath))
            return result;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(statePath, Encoding.UTF8));
            if (!document.RootElement.TryGetProperty("packs", out JsonElement packs)
                || packs.ValueKind != JsonValueKind.Object)
            {
                return result;
            }
            foreach (JsonProperty property in packs.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    string? hash = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(hash))
                        result[property.Name] = hash.Trim().ToLowerInvariant();
                }
            }
        }
        catch (Exception exception)
        {
            // 记录损坏 = 视为无记录：现有目标一律按"用户版本"保留，绝不清空目录。
            errors.Add("安装状态记录损坏（按无记录处理）：" + exception.GetType().Name + ": " + exception.Message);
        }
        return result;
    }

    static bool TrySaveState(string statePath, IReadOnlyDictionary<string, string> hashes, List<string> errors)
    {
        try
        {
            string? directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("version", StateVersion);
                writer.WriteStartObject("packs");
                foreach (KeyValuePair<string, string> pair in hashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    writer.WriteString(pair.Key, pair.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            WriteAtomically(statePath, stream.ToArray());
            return true;
        }
        catch (Exception exception)
        {
            errors.Add("写入安装状态记录失败：" + exception.GetType().Name + ": " + exception.Message);
            return false;
        }
    }
}
