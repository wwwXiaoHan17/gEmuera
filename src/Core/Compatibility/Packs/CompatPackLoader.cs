using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 兼容包加载器（显式发现 → ALC 隔离 → 清单解析 → 入口发现 → 语义校验 → 哈希固化）。
/// 设计文档 docs/designs/compat-pack-interface.md §5：
///   发现仅接受调用方显式给出的路径清单，本类不做任何目录扫描/注册中心/远程下载；
///   任一环节失败即该包拒载（fail-closed），错误全量收集并带包路径前缀。
/// </summary>
public static class CompatPackLoader
{
    /// <summary>加载单个包。任何失败返回 false 且 handle 为 null，绝不返回半加载结果。</summary>
    public static bool TryLoad(
        string packAssemblyPath,
        CompatPackValidationContext context,
        out CompatPackHandle? handle,
        out IReadOnlyList<string> errors)
    {
        handle = null;
        var collected = new List<string>();
        string prefix = "[" + (packAssemblyPath ?? "") + "] ";

        if (string.IsNullOrWhiteSpace(packAssemblyPath))
        {
            collected.Add(prefix + "包程序集路径为空。");
            errors = collected;
            return false;
        }
        if (!File.Exists(packAssemblyPath))
        {
            collected.Add(prefix + "包程序集文件不存在。");
            errors = collected;
            return false;
        }
        if (!string.Equals(Path.GetExtension(packAssemblyPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            collected.Add(prefix + "包必须是程序集文件（.dll）。");
            errors = collected;
            return false;
        }

        byte[] assemblyBytes;
        try
        {
            assemblyBytes = File.ReadAllBytes(packAssemblyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            collected.Add(prefix + "包程序集读取失败：" + exception.Message);
            errors = collected;
            return false;
        }

        Assembly assembly;
        var loadContext = new CompatPackLoadContext(packAssemblyPath);
        try
        {
            assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(packAssemblyPath));
        }
        catch (Exception exception)
        {
            collected.Add(prefix + "包程序集加载失败：" + exception.Message);
            loadContext.Unload();
            errors = collected;
            return false;
        }

        if (!CompatPackManifest.TryLoadFromAssembly(assembly, out var parsedManifest, out var manifestErrors)
            || parsedManifest is null)
        {
            collected.Add(prefix + "内嵌清单缺失或非法：");
            collected.AddRange(manifestErrors.Select(error => prefix + "  " + error));
            loadContext.Unload();
            errors = collected;
            return false;
        }
        CompatPackManifest manifest = parsedManifest!;
        string assemblySha256 = Hash(assemblyBytes);
        string packSha256 = ComputePackHash(assembly, assemblyBytes);

        ICompatPack? pack;
        try
        {
            pack = FindSinglePackEntry(assembly, collected);
        }
        catch (Exception exception)
        {
            collected.Add(prefix + "包入口实例化失败：" + exception.Message);
            loadContext.Unload();
            errors = collected;
            return false;
        }
        if (pack is null || collected.Count > 0)
        {
            loadContext.Unload();
            errors = collected;
            return false;
        }

        if (!ValidatePackEntry(pack, manifest, context, out IReadOnlyList<ICompatPackContribution> contributions, out var entryErrors))
        {
            collected.AddRange(entryErrors.Select(error => prefix + error));
            loadContext.Unload();
            errors = collected;
            return false;
        }

        var surface = contributions.OfType<ISurfaceContribution>().ToArray();
        var capabilities = contributions.OfType<ICapabilityContribution>().ToArray();
        var variants = contributions.OfType<IInstructionVariantContribution>().ToArray();
        var policies = contributions.OfType<IPolicyContribution>().ToArray();

        handle = new CompatPackHandle(
            manifest, pack, Path.GetFullPath(packAssemblyPath), assemblySha256, packSha256,
            surface, capabilities, variants, policies, loadContext);
        errors = collected;
        return true;
    }

    /// <summary>
    /// 加载会话包集合：全有或全无——任一包失败则整体为 null（调用方按降级不变量回退纯 v24
    /// 并记日志），跨包 packId 重复同样拒载。
    /// </summary>
    public static bool TryLoadSet(
        IEnumerable<string> packAssemblyPaths,
        CompatPackValidationContext context,
        out CompatPackSet? set,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(packAssemblyPaths);
        ArgumentNullException.ThrowIfNull(context);
        set = null;
        var collected = new List<string>();
        var handles = new List<CompatPackHandle>();
        var packIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in packAssemblyPaths)
        {
            if (!TryLoad(path, context, out CompatPackHandle? handle, out var loadErrors))
            {
                collected.AddRange(loadErrors);
                continue;
            }
            if (!packIds.Add(handle!.Manifest.PackId))
            {
                collected.Add("[" + path + "] packId 重复：" + handle.Manifest.PackId);
                // 重复包已成功加载（ALC 存活），确定回收而非等 GC。
                handle.Unload();
                continue;
            }
            handles.Add(handle);
        }

        errors = collected;
        if (collected.Count > 0)
        {
            // 失败即整体回退：已加载的包立即回收，不留半套集合。
            foreach (CompatPackHandle loaded in handles)
                loaded.Unload();
            return false;
        }

        set = new CompatPackSet(handles);
        return true;
    }

    /// <summary>
    /// 包入口校验（公开以便直测）。包作者可控的 Manifest/Contributions getter 抛异常、
    /// 返回 null 或与内嵌清单不一致，一律转为拒载错误——加载器的任何输入都不允许异常逃逸
    /// （设计 §3.3 降级不变量：任何原因失败 → 回退纯 v24，不静默半加载）。
    /// </summary>
    public static bool ValidatePackEntry(
        ICompatPack pack,
        CompatPackManifest manifest,
        CompatPackValidationContext context,
        out IReadOnlyList<ICompatPackContribution> contributions,
        out IReadOnlyList<string> errors)
    {
        var collected = new List<string>();
        contributions = Array.Empty<ICompatPackContribution>();
        try
        {
            // 入口类自报的清单必须与内嵌清单同源（id+版本一致），防止两处漂移。
            if (!string.Equals(pack.Manifest?.PackId, manifest.PackId, StringComparison.Ordinal)
                || !string.Equals(pack.Manifest?.PackVersion, manifest.PackVersion, StringComparison.Ordinal))
            {
                collected.Add("入口类 Manifest 与内嵌清单不一致（packId/packVersion 必须相同）。");
                errors = collected;
                return false;
            }

            contributions = pack.Contributions ?? Array.Empty<ICompatPackContribution>();
            if (!CompatPackRules.Validate(manifest, contributions, context, out var ruleErrors))
            {
                collected.AddRange(ruleErrors);
                errors = collected;
                return false;
            }

            errors = collected;
            return true;
        }
        catch (Exception exception)
        {
            contributions = Array.Empty<ICompatPackContribution>();
            collected.Add("包入口访问失败（Manifest/Contributions 抛出异常）：" + exception.Message);
            errors = collected;
            return false;
        }
    }

    static ICompatPack? FindSinglePackEntry(Assembly assembly, List<string> errors)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // 与 PluginManager.getLoadableTypes 同款容错：测试程序集混载无关类型不阻断包发现。
            types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }

        List<Type> entries = types
            .Where(type => type is not null
                && type.IsClass
                && !type.IsAbstract
                && typeof(ICompatPack).IsAssignableFrom(type))
            .ToList();
        if (entries.Count == 0)
        {
            errors.Add("程序集内没有 ICompatPack 实现类；缺入口的程序集不是兼容包。");
            return null;
        }
        if (entries.Count > 1)
        {
            errors.Add("程序集内存在多个 ICompatPack 实现类（" + entries.Count + " 个），必须恰好一个。");
            return null;
        }
        return (ICompatPack?)Activator.CreateInstance(entries[0]);
    }

    static string ComputePackHash(Assembly assembly, byte[] assemblyBytes)
    {
        byte[] manifestBytes = Array.Empty<byte>();
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!string.Equals(name, CompatPackManifest.ManifestResourceName, StringComparison.Ordinal))
                continue;
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is not null)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                manifestBytes = memory.ToArray();
            }
            break;
        }
        byte[] concatenated = new byte[assemblyBytes.Length + manifestBytes.Length];
        assemblyBytes.CopyTo(concatenated, 0);
        manifestBytes.CopyTo(concatenated, assemblyBytes.Length);
        return Hash(concatenated);
    }

    static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
