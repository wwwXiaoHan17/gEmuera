using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 兼容包加载器（显式发现 → 依赖闭包 → ALC 隔离 → 清单解析 → 入口发现 → 语义校验 →
/// 冻结差量）。设计文档 docs/designs/compat-pack-interface.md §5：
///   发现仅接受调用方显式给出的路径清单，本类不做任何目录扫描/注册中心/远程下载；
///   任一环节失败即该包拒载（fail-closed），错误全量收集并带包路径前缀。
/// T5 加载完整性：
///   1. 主 DLL 字节恰好读一次（<see cref="BytesReaderOverrideForTest"/> 为测试 seam）；
///   2. 枚举包目录顶层全部 *.dll（Ordinal 排序），拒绝指向包目录外的 symlink/junction；
///   3. 依赖字节图（file name → bytes，含主 DLL）一次读入，哈希与 ALC 装载共享同一份字节；
///   4. ALC 只从该字节图 <c>LoadFromStream</c> 装依赖，禁止在 Load() 里按路径二次读盘；
///   5. PackSha256 覆盖主程序集 + 内嵌清单原始字节 + 依赖闭包（文件名/长度/字节）；
///   6. 入口类 Manifest 必须与内嵌清单全字段相等；类型加载失败一律 fail-closed。
/// </summary>
public static class CompatPackLoader
{
    const char NewLine = (char)10;
    const char Colon = (char)58;

    /// <summary>
    /// 仅测试 seam：注入包程序集字节读取器（按路径 → 字节）。默认 null，生产路径恒走
    /// <see cref="File.ReadAllBytes(string)"/>；测试用它钉住"哈希与加载同源字节"的 TOCTOU
    /// 不变量（E2-R5）。生产代码不得赋值。
    /// </summary>
    internal static Func<string, byte[]>? BytesReaderOverrideForTest { get; set; }

    /// <summary>
    /// 路径包含判定的大小写语义：Windows 文件系统大小写不敏感，其它平台敏感。
    /// 只用于"symlink 目标是否仍在包目录内"与"枚举文件是否为主 DLL"的判定。
    /// </summary>
    static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// 包程序集字节读取的唯一入口（<see cref="BytesReaderOverrideForTest"/> 命中时走 seam，
    /// 否则走磁盘读取）。调用方拿到的是同一份 byte[]，哈希与 stream 装载必须共用它。
    /// </summary>
    static byte[] ReadBytes(string path) =>
        BytesReaderOverrideForTest is { } reader ? reader(path) : File.ReadAllBytes(path);

    /// <summary>
    /// 加载单个包。任何失败返回 false 且 handle 为 null，绝不返回半加载结果。
    /// 同源字节不变量（E2-R5 TOCTOU）：主程序集字节与依赖字节恰好读一次，双哈希与经 stream
    /// 的装载共用这些 byte[]；<see cref="CompatPackHandle.PackAssemblyPath"/> 仅作诊断来源，
    /// 不再作为装载输入。
    /// </summary>
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

        string fullPath = Path.GetFullPath(packAssemblyPath);
        string packDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;

        byte[] assemblyBytes;
        try
        {
            assemblyBytes = ReadBytes(packAssemblyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            collected.Add(prefix + "包程序集读取失败：" + exception.Message);
            errors = collected;
            return false;
        }

        // 依赖闭包（T5）：包目录顶层的全部 *.dll 都是包的一部分——既是 ALC 的唯一依赖来源，
        // 也是 PackSha256 的输入。主 DLL 也在图中（避免哈希把主 DLL 漏出闭包语义），
        // 但计算哈希时排除（主程序集字节已单独入账）。
        if (!TryBuildDependencyBytes(
                fullPath, packDirectory, assemblyBytes, prefix, collected,
                out IReadOnlyDictionary<string, byte[]> dependencyBytes, out string mainFileName))
        {
            errors = collected;
            return false;
        }

        // 同源字节不变量（E2-R5 TOCTOU）：assemblyBytes 恰好读一次，既是双哈希的输入，
        // 也是经 MemoryStream 装载的程序集本体——两次独立读盘（先哈希后按路径装载）会被
        // 并发的文件替换撕开，固化哈希描述旧内容而运行的是新内容。
        Assembly assembly;
        var loadContext = new CompatPackLoadContext(fullPath, dependencyBytes);
        try
        {
            assembly = loadContext.LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
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
        // PackSha256 必须在任何语义校验之前冻结（校验失败也不改变"这个包内容是什么"的
        // 事实）；规范格式见 ComputePackHash。
        string packSha256 = ComputePackHash(
            assemblyBytes, ReadEmbeddedManifestBytes(assembly), dependencyBytes, mainFileName);

        ICompatPack? pack;
        try
        {
            pack = ResolvePackEntry(assembly, manifest, collected);
        }
        catch (Exception exception)
        {
            collected.Add(prefix + "包入口实例化失败：" + DescribeException(exception));
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

        IReadOnlyList<ICompatPackContribution> manifestContributions = CreateManifestContributions(manifest, context);
        if (!ValidatePackEntry(
                pack, manifest, context, out _, out var entryErrors, out _, out ValidatedPackDelta? validatedDelta,
                manifestContributions, packSha256))
        {
            collected.AddRange(entryErrors.Select(error => prefix + error));
            loadContext.Unload();
            errors = collected;
            return false;
        }
        if (validatedDelta is null)
        {
            collected.Add(prefix + "校验通过但未冻结 ValidatedPackDelta（内部不变量破坏）。");
            loadContext.Unload();
            errors = collected;
            return false;
        }

        var dependencies = dependencyBytes
            .Where(pair => !string.Equals(pair.Key, mainFileName, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new CompatPackDependencyInfo(pair.Key, Hash(pair.Value)))
            .ToArray();

        handle = new CompatPackHandle(
            manifest, pack, fullPath, assemblySha256, validatedDelta, dependencies, loadContext);
        errors = collected;
        return true;
    }

    /// <summary>
    /// 读入包目录顶层的依赖字节图（file name → bytes，含主 DLL）。枚举顺序 Ordinal 排序；
    /// 任一文件是指向包目录外的 symlink/junction 即整体拒载（路径逃逸不能进入哈希/装载）；
    /// 主 DLL 复用调用方已读取的字节（TOCTOU 同源不变量），其余文件各读一次。
    /// </summary>
    static bool TryBuildDependencyBytes(
        string mainFullPath,
        string packDirectory,
        byte[] assemblyBytes,
        string prefix,
        List<string> errors,
        out IReadOnlyDictionary<string, byte[]> dependencyBytes,
        out string mainFileName)
    {
        var graph = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        dependencyBytes = new ReadOnlyDictionary<string, byte[]>(graph);
        mainFileName = Path.GetFileName(mainFullPath);
        if (string.IsNullOrEmpty(packDirectory))
        {
            errors.Add(prefix + "包程序集路径没有目录部分。");
            return false;
        }

        string[] files;
        try
        {
            files = Directory.EnumerateFiles(packDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add(prefix + "包目录枚举失败：" + exception.Message);
            return false;
        }
        Array.Sort(files, StringComparer.Ordinal);

        bool mainSeen = false;
        foreach (string file in files)
        {
            string fullFile;
            try
            {
                fullFile = Path.GetFullPath(file);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add(prefix + "包目录内文件路径非法：" + Path.GetFileName(file) + "（" + exception.Message + "）。");
                return false;
            }

            if (!TryEnsureLinkWithinDirectory(fullFile, packDirectory, prefix, errors))
                return false;

            string fileName = Path.GetFileName(fullFile);
            if (string.Equals(fullFile, mainFullPath, PathComparison))
            {
                graph[mainFileName] = assemblyBytes;
                mainSeen = true;
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = ReadBytes(fullFile);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add(prefix + "依赖程序集读取失败（" + fileName + "）：" + exception.Message);
                return false;
            }
            if (!graph.TryAdd(fileName, bytes))
            {
                errors.Add(prefix + "包目录内存在重名依赖程序集：" + fileName + "。");
                return false;
            }
        }

        if (!mainSeen)
        {
            // 正常路径不会发生（主 DLL 必然被顶层枚举命中）；仍显式入图，保证"主 DLL 也在
            // 依赖字节图中"的结构性不变量不被平台差异打破。
            graph[mainFileName] = assemblyBytes;
        }
        return true;
    }

    /// <summary>
    /// 拒绝指向包目录外的文件符号链接/目录联接。解析失败同样 fail-closed（无法证明目标
    /// 在包目录内）；普通文件（非链接）返回 true。
    /// </summary>
    static bool TryEnsureLinkWithinDirectory(
        string fullPath,
        string packDirectory,
        string prefix,
        List<string> errors)
    {
        FileSystemInfo? target;
        try
        {
            target = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add(prefix + "包目录内文件的符号链接解析失败（" + Path.GetFileName(fullPath) + "）："
                + exception.Message);
            return false;
        }
        if (target is null)
            return true;

        string resolved = Path.GetFullPath(target.FullName);
        string directoryWithSeparator =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(packDirectory)) + Path.DirectorySeparatorChar;
        if (resolved.StartsWith(directoryWithSeparator, PathComparison))
            return true;

        errors.Add(prefix + "包目录内的文件是指向包目录外的符号链接/联接，拒载（路径逃逸）："
            + Path.GetFileName(fullPath) + "。");
        return false;
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
    /// T5 起校验成功时同时冻结 <see cref="ValidatedPackDelta"/>：CapabilityIds 为 capability
    /// closure（含 Requires/Implies 展开）结果，SurfaceStates 为双形态闭包后的规范态。
    /// <paramref name="packSha256"/> 由加载器计算后传入（含依赖闭包）；规则层直测时缺省为空串。
    /// </summary>
    public static bool ValidatePackEntry(
        ICompatPack pack,
        CompatPackManifest manifest,
        CompatPackValidationContext context,
        out IReadOnlyList<ICompatPackContribution> contributions,
        out IReadOnlyList<string> errors,
        out IReadOnlyList<CompatSymbolState> surfaceStates,
        out ValidatedPackDelta? validatedDelta,
        IReadOnlyList<ICompatPackContribution>? manifestContributions = null,
        string? packSha256 = null)
    {
        var collected = new List<string>();
        contributions = Array.Empty<ICompatPackContribution>();
        surfaceStates = Array.Empty<CompatSymbolState>();
        validatedDelta = null;
        try
        {
            // 入口类自报的清单必须与内嵌清单全字段相等：只比 id+版本会放过 capabilities/
            // 表面/变体选择漂移，让"清单声明的包"与"代码宣称的包"静默分裂。入口清单
            // 不得覆盖内嵌清单（内嵌资源是唯一事实源）。
            if (!TryValidateEntryManifestEquals(pack.Manifest, manifest, collected))
            {
                errors = collected;
                return false;
            }

            IReadOnlyList<ICompatPackContribution> entryContributions =
                pack.Contributions ?? Array.Empty<ICompatPackContribution>();
            var combined = new List<ICompatPackContribution>(
                (manifestContributions?.Count ?? 0) + entryContributions.Count);
            if (manifestContributions is { Count: > 0 })
                combined.AddRange(manifestContributions);
            combined.AddRange(entryContributions);
            contributions = combined;
            if (!CompatPackRules.Validate(
                    manifest, contributions, context, out var ruleErrors, out surfaceStates, out validatedDelta,
                    packSha256))
            {
                collected.AddRange(ruleErrors);
                validatedDelta = null;
                errors = collected;
                return false;
            }

            errors = collected;
            return true;
        }
        catch (Exception exception)
        {
            contributions = Array.Empty<ICompatPackContribution>();
            surfaceStates = Array.Empty<CompatSymbolState>();
            validatedDelta = null;
            collected.Add("包入口访问失败（Manifest/Contributions 抛出异常）：" + exception.Message);
            errors = collected;
            return false;
        }
    }

    /// <summary>
    /// 兼容重载（不需要冻结差量的调用点/直测）：等价于 7 参版本丢弃
    /// <c>validatedDelta</c>。新代码应使用带 <c>out ValidatedPackDelta?</c> 的重载。
    /// </summary>
    public static bool ValidatePackEntry(
        ICompatPack pack,
        CompatPackManifest manifest,
        CompatPackValidationContext context,
        out IReadOnlyList<ICompatPackContribution> contributions,
        out IReadOnlyList<string> errors,
        out IReadOnlyList<CompatSymbolState> surfaceStates,
        IReadOnlyList<ICompatPackContribution>? manifestContributions = null)
        => ValidatePackEntry(
            pack, manifest, context, out contributions, out errors, out surfaceStates, out _, manifestContributions);

    /// <summary>
    /// 入口类 Manifest 与内嵌清单的逐字段相等判定；不相等时把差异字段名并入错误
    ///（fail-closed，入口清单不得覆盖内嵌清单）。
    /// </summary>
    static bool TryValidateEntryManifestEquals(
        CompatPackManifest? entryManifest,
        CompatPackManifest embeddedManifest,
        List<string> errors)
    {
        if (entryManifest is null)
        {
            errors.Add("入口类 Manifest 为 null（与内嵌清单不一致）。");
            return false;
        }

        var differing = new List<string>();
        if (!string.Equals(entryManifest.PackId, embeddedManifest.PackId, StringComparison.Ordinal))
            differing.Add("packId");
        if (!string.Equals(entryManifest.PackVersion, embeddedManifest.PackVersion, StringComparison.Ordinal))
            differing.Add("packVersion");
        if (entryManifest.ContractAbi != embeddedManifest.ContractAbi)
            differing.Add("contractAbi");
        if (entryManifest.EngineApi != embeddedManifest.EngineApi)
            differing.Add("engineApi");
        if (entryManifest.UsedLegacyTargetEngineApi != embeddedManifest.UsedLegacyTargetEngineApi)
            differing.Add("targetEngineApi");
        if (!string.Equals(entryManifest.BaseProfileId, embeddedManifest.BaseProfileId, StringComparison.Ordinal))
            differing.Add("baseProfileId");
        if (!SurfaceEquals(entryManifest.Surface, embeddedManifest.Surface))
            differing.Add("surface");
        if (!string.Equals(entryManifest.BaseSurfaceHash, embeddedManifest.BaseSurfaceHash, StringComparison.Ordinal))
            differing.Add("baseSurfaceHash");
        if (!SequenceEqual(entryManifest.Capabilities, embeddedManifest.Capabilities))
            differing.Add("capabilities");
        if (!string.Equals(entryManifest.SaveProfileId, embeddedManifest.SaveProfileId, StringComparison.Ordinal))
            differing.Add("saveProfileId");
        if (!VariantSelectionsEqual(entryManifest.VariantSelections, embeddedManifest.VariantSelections))
            differing.Add("variantSelections");
        if (!GameIdentityEquals(entryManifest.GameIdentity, embeddedManifest.GameIdentity))
            differing.Add("gameIdentity");

        if (differing.Count == 0)
            return true;
        errors.Add("入口类 Manifest 与内嵌清单不一致（字段：" + string.Join(", ", differing)
            + "）；入口清单不得覆盖内嵌清单。");
        return false;
    }

    static bool SurfaceEquals(CompatPackSurface left, CompatPackSurface right)
        => SequenceEqual(left.AddInstructions, right.AddInstructions)
            && SequenceEqual(left.HideInstructions, right.HideInstructions)
            && SequenceEqual(left.AddFunctions, right.AddFunctions)
            && SequenceEqual(left.HideFunctions, right.HideFunctions);

    static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    static bool VariantSelectionsEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        foreach (KeyValuePair<string, string> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out string? value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    static bool GameIdentityEquals(CompatPackGameIdentity? left, CompatPackGameIdentity? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return string.Equals(left.GameCode, right.GameCode, StringComparison.Ordinal)
            && string.Equals(left.Version, right.Version, StringComparison.Ordinal)
            && string.Equals(left.VersionAccept, right.VersionAccept, StringComparison.Ordinal);
    }

    /// <summary>
    /// 解析包入口：恰好一个 ICompatPack 实现类；完整枚举且零个入口类时按 manifest-only
    /// 数据包处理（表面/capability/variant 全部来自内嵌清单，不要求包作者编写 C# 入口）。
    /// T5 fail-closed：<c>Assembly.GetTypes()</c> 抛 ReflectionTypeLoadException（依赖缺失/
    /// 类型元数据损坏）时收集 LoaderExceptions 并返回 null——不再从部分枚举结果里挑入口，
    /// 那会把半坏包静默降级成 manifest-only。多个入口类仍拒载。
    /// </summary>
    internal static ICompatPack? ResolvePackEntry(
        Assembly assembly,
        CompatPackManifest manifest,
        List<string> errors)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            errors.Add("程序集类型加载失败（ReflectionTypeLoadException）：包依赖缺失或类型元数据"
                + "无法解析，按 fail-closed 拒载（不允许部分枚举降级为 manifest-only）。");
            foreach (string detail in DescribeLoaderExceptions(exception))
                errors.Add("  " + detail);
            return null;
        }
        catch (Exception exception)
        {
            errors.Add("程序集类型枚举失败（fail-closed 拒载）：" + DescribeException(exception));
            return null;
        }

        List<Type> entries = types
            .Where(type => type is not null
                && type.IsClass
                && !type.IsAbstract
                && typeof(ICompatPack).IsAssignableFrom(type))
            .ToList();
        if (entries.Count > 1)
        {
            errors.Add("程序集内存在多个 ICompatPack 实现类（" + entries.Count + " 个），必须恰好一个。");
            return null;
        }
        if (entries.Count == 0)
            return new ManifestOnlyCompatPack(manifest);

        try
        {
            return (ICompatPack?)Activator.CreateInstance(entries[0]);
        }
        catch (Exception exception)
        {
            errors.Add("包入口实例化失败：" + DescribeException(exception));
            return null;
        }
    }

    static IEnumerable<string> DescribeLoaderExceptions(ReflectionTypeLoadException exception)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Exception? loaderException in exception.LoaderExceptions)
        {
            if (loaderException is null)
                continue;
            string description = DescribeException(loaderException);
            if (seen.Add(description))
                yield return description;
        }
    }

    internal static IReadOnlyList<ICompatPackContribution> CreateManifestContributions(
        CompatPackManifest manifest,
        CompatPackValidationContext context)
    {
        CompatPackSurface surface = manifest.Surface;
        if (surface.AddInstructions.Count == 0
            && surface.HideInstructions.Count == 0
            && surface.AddFunctions.Count == 0
            && surface.HideFunctions.Count == 0)
        {
            return Array.Empty<ICompatPackContribution>();
        }
        return new ICompatPackContribution[]
        {
            new ManifestSurfaceContribution(surface, context.KnownFunctionReturnTypes),
        };
    }

    /// <summary>
    /// 拒载错误文案：Activator.CreateInstance 把构造器异常包进 TargetInvocationException
    /// （通用文案，不携带原因），诊断必须保留最内层异常类型与消息——例如 ALC 允许清单
    /// 硬化后的 FileLoadException（指名被拒绝的绑定程序集）。
    /// </summary>
    internal static string DescribeException(Exception exception)
    {
        Exception innermost = exception;
        while (innermost.InnerException is { } inner)
            innermost = inner;
        if (ReferenceEquals(innermost, exception))
            return exception.GetType().Name + ": " + exception.Message;
        return exception.GetType().Name + ": " + exception.Message
            + "（最内层 " + innermost.GetType().Name + ": " + innermost.Message + "）";
    }

    /// <summary>
    /// PackSha256 的规范格式（v1，UTF-8 字节流，SHA256 小写十六进制）：
    /// <code>
    /// gemuera.compatpack.pack.v1
    /// assembly:&lt;十进制字节数&gt;
    /// &lt;主程序集原始字节&gt;
    /// manifest:&lt;十进制字节数&gt;
    /// &lt;内嵌清单资源原始字节&gt;
    /// 每个依赖（排除主 DLL；按 file name Ordinal 排序）：
    /// dependency:&lt;文件名字节数&gt;:&lt;文件名&gt;:&lt;数据字节数&gt;
    /// &lt;依赖原始字节&gt;
    /// </code>
    /// 所有段都有显式长度前缀，因此 name/bytes 中出现 ':'、换行等字符也不会产生歧义；
    /// 依赖闭包（而非仅主 DLL）入哈希，使"换掉包目录里的依赖"必然改变 PackSha256。
    /// </summary>
    static string ComputePackHash(
        byte[] assemblyBytes,
        byte[] manifestBytes,
        IReadOnlyDictionary<string, byte[]> dependencyBytes,
        string mainFileName)
    {
        using var buffer = new MemoryStream();
        WriteUtf8(buffer, "gemuera.compatpack.pack.v1" + NewLine);
        WriteBlock(buffer, "assembly", assemblyBytes);
        WriteBlock(buffer, "manifest", manifestBytes);
        foreach (KeyValuePair<string, byte[]> dependency in dependencyBytes
            .Where(pair => !string.Equals(pair.Key, mainFileName, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(dependency.Key);
            WriteUtf8(buffer, "dependency:");
            WriteUtf8(buffer, nameBytes.Length.ToString(CultureInfo.InvariantCulture));
            buffer.WriteByte((byte)Colon);
            buffer.Write(nameBytes);
            buffer.WriteByte((byte)Colon);
            WriteUtf8(buffer, dependency.Value.Length.ToString(CultureInfo.InvariantCulture));
            buffer.WriteByte((byte)NewLine);
            buffer.Write(dependency.Value);
        }
        return Hash(buffer.ToArray());
    }

    static void WriteBlock(Stream stream, string label, byte[] data)
    {
        WriteUtf8(stream, label + ":" + data.Length.ToString(CultureInfo.InvariantCulture) + NewLine);
        stream.Write(data);
    }

    static void WriteUtf8(Stream stream, string text)
        => stream.Write(Encoding.UTF8.GetBytes(text));

    static byte[] ReadEmbeddedManifestBytes(Assembly assembly)
    {
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!string.Equals(name, CompatPackManifest.ManifestResourceName, StringComparison.Ordinal))
                continue;
            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
                return Array.Empty<byte>();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        return Array.Empty<byte>();
    }

    static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
