using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
/// I1 部署契约（2026-11-03 final review）：
///   依赖闭包**只**含主程序集 + 它真实 AssemblyRef 递归引用到的同目录顶层 DLL。
///   目录内未被引用的 DLL 一律拒载——共享扫描根里的另一个包曾经被当成"依赖"计入
///   PackSha256、并可从 ALC 解析（未启用包也可见），同一包在不同安装目录下哈希还会
///   随邻居变化。因此包目录必须自包含且只含本包程序集（一包一目录）。
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

    /// <summary>依赖闭包按完整路径去重时的比较器（与 <see cref="PathComparison"/> 同语义）。</summary>
    static readonly StringComparer PathNameComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// 包程序集字节读取的唯一入口（<see cref="BytesReaderOverrideForTest"/> 命中时走 seam，
    /// 否则走磁盘读取）。调用方拿到的是同一份 byte[]，哈希与 stream 装载必须共用它。
    /// </summary>
    static byte[] ReadBytes(string path) =>
        BytesReaderOverrideForTest is { } reader ? reader(path) : File.ReadAllBytes(path);

    /// <summary>
    /// 拒载错误的路径前缀：只回显文件基名（普通 <c>[LOAD]</c> 日志不得泄漏完整路径）；
    /// 无法取得基名（非法路径字符/超长路径）时退回调用方原始文本，保证前缀构造本身不抛错。
    /// </summary>
    static string DisplayPrefix(string? packAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(packAssemblyPath))
            return "[] ";
        string? name;
        try
        {
            name = Path.GetFileName(packAssemblyPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            name = null;
        }
        return "[" + (string.IsNullOrEmpty(name) ? packAssemblyPath : name) + "] ";
    }

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
        // M-5（spec §7.2）：普通 [LOAD] 诊断只回显文件基名，不泄漏调用方的完整路径；
        // 需要完整路径的排查走 debug 日志（本 loader 不记录路径）。
        string prefix = DisplayPrefix(packAssemblyPath);

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

        // 依赖闭包（T5 同源字节；I1 按真实 AssemblyRef 推导）：主 DLL + 它递归引用到的
        // 同目录顶层 DLL——既是 ALC 的唯一依赖来源，也是 PackSha256 的输入。未被引用的
        // 顶层 DLL 直接拒载（见 TryBuildDependencyBytes）。主 DLL 也在图中（避免哈希把
        // 主 DLL 漏出闭包语义），但计算哈希时排除（主程序集字节已单独入账）。
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
        var loadContext = new CompatPackLoadContext(fullPath, dependencyBytes);
        bool handleRetained = false;
        try
        {
            Assembly assembly;
            try
            {
                assembly = loadContext.LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
            }
            catch (Exception exception)
            {
                collected.Add(prefix + "包程序集加载失败：" + exception.Message);
                errors = collected;
                return false;
            }

            if (!CompatPackManifest.TryLoadFromAssembly(assembly, out var parsedManifest, out var manifestErrors)
                || parsedManifest is null)
            {
                collected.Add(prefix + "内嵌清单缺失或非法：");
                collected.AddRange(manifestErrors.Select(error => prefix + "  " + error));
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
                errors = collected;
                return false;
            }
            if (pack is null || collected.Count > 0)
            {
                errors = collected;
                return false;
            }

            IReadOnlyList<ICompatPackContribution> manifestContributions =
                CreateManifestContributions(manifest, context);
            if (!ValidatePackEntry(
                    pack, manifest, context, out _, out var entryErrors, out _, out ValidatedPackDelta? validatedDelta,
                    manifestContributions, packSha256))
            {
                collected.AddRange(entryErrors.Select(error => prefix + error));
                errors = collected;
                return false;
            }
            if (validatedDelta is null)
            {
                collected.Add(prefix + "校验通过但未冻结 ValidatedPackDelta（内部不变量破坏）。");
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
            handleRetained = true;
            errors = collected;
            return true;
        }
        catch (Exception exception)
        {
            // M-3：哈希/内嵌清单字节读取/清单贡献构造等任何未预期异常都在此按 fail-closed
            // 拒载，异常绝不从 TryLoad 逃逸；ALC 由 finally 统一回收（成功路径才保留）。
            collected.Add(prefix + "包加载失败（未预期异常，fail-closed）：" + DescribeException(exception));
            errors = collected;
            return false;
        }
        finally
        {
            if (!handleRetained)
                loadContext.Unload();
        }
    }

    /// <summary>
    /// 读入包依赖闭包的字节图（file name → bytes，含主 DLL）。闭包推导（I1）：
    ///   1. 枚举包目录顶层 *.dll（Ordinal 排序），任一文件是指向目录外的 symlink/junction
    ///      即整体拒载（路径逃逸不能进入哈希/装载）；
    ///   2. 从主程序集元数据读 AssemblyRef；凡引用名对应目录内 <c>&lt;name&gt;.dll</c>
    ///      的文件进入闭包并递归展开（间接依赖）；
    ///   3. 目录内**未被引用**的 DLL 一律拒载——那是另一个包/无关文件，计入哈希会让
    ///      同一包随邻居变化，进入 ALC 又会让未启用包可解析；
    ///   4. 闭包内每个文件各读一次字节；主 DLL 复用调用方已读取的字节（TOCTOU 同源）。
    /// 元数据不可解析的**已被引用**文件不在此处拒载：它已损坏，交由 ALC 装载期给出
    /// 权威的 BadImageFormatException/ReflectionTypeLoadException（错误更可定位）；
    /// 主程序集自身不可解析时同样跳过闭包推导，只把主 DLL 入图。
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

        // 文件基名 → 完整路径。Windows 文件系统大小写不敏感，与 ALC 的 <name>.dll 解析
        // 语义对齐（CompatPackLoadContext.TryGetDependencyBytes 的 Windows 回退）；同目录
        // 出现仅大小写不同的同名程序集时无法确定绑定目标 → fail-closed。
        var filesByName = new Dictionary<string, string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
            if (!filesByName.TryAdd(fileName, fullFile))
            {
                errors.Add(prefix + "包目录内存在仅大小写不同的重名程序集：" + fileName + "（无法确定绑定目标）。");
                return false;
            }
            if (string.Equals(fullFile, mainFullPath, PathComparison))
                mainSeen = true;
        }

        if (!mainSeen)
        {
            // 正常路径不会发生（主 DLL 必然被顶层枚举命中）；仍显式入图，保证"主 DLL 也在
            // 依赖字节图中"的结构性不变量不被平台差异打破。
            graph[mainFileName] = assemblyBytes;
        }

        if (!TryReadAssemblyReferenceNames(assemblyBytes, out List<string> mainReferences))
        {
            // 主程序集不是可解析的托管程序集：交给 ALC 装载期给出权威拒载文案，
            // 闭包推导阶段不改写错误（也不把同目录其它 DLL 误报成"未被引用"）。
            return true;
        }

        var closure = new HashSet<string>(PathNameComparer) { mainFullPath };
        var bytesByPath = new Dictionary<string, byte[]>(PathNameComparer) { [mainFullPath] = assemblyBytes };
        var pending = new Queue<string>();
        pending.Enqueue(mainFullPath);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            // 已被引用的依赖若元数据损坏则无法继续推导它的引用，但它仍在闭包/哈希内，
            // 装载期必然 fail-closed（不在此处改写错误文案）。
            if (!TryReadAssemblyReferenceNames(bytesByPath[current], out List<string> references))
                continue;
            foreach (string reference in references)
            {
                if (!filesByName.TryGetValue(reference + ".dll", out string? dependencyPath))
                    continue; // 框架/契约/宿主程序集：不在包目录，由 ALC 允许清单处理
                if (!closure.Add(dependencyPath))
                    continue;
                byte[] dependencyBytesForFile;
                try
                {
                    dependencyBytesForFile = ReadBytes(dependencyPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    errors.Add(prefix + "依赖程序集读取失败（" + Path.GetFileName(dependencyPath)
                        + "）：" + exception.Message);
                    return false;
                }
                bytesByPath[dependencyPath] = dependencyBytesForFile;
                pending.Enqueue(dependencyPath);
            }
        }

        // 未被主程序集（递归）引用的顶层 DLL = 另一个包或无关文件：拒绝而不是悄悄计入
        // 哈希/装进 ALC（I1 多包共享扫描根污染）。
        string[] unreferenced = filesByName
            .Where(pair => !closure.Contains(pair.Value))
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unreferenced.Length > 0)
        {
            errors.Add(prefix + "包目录必须自包含且只含本包程序集：顶层 DLL " + string.Join(", ", unreferenced)
                + " 未被主程序集 " + mainFileName + " 的 AssemblyRef 引用（闭包 = 主程序集 + 实际引用的"
                + "递归依赖）。多包混放或无关 DLL 会污染 PackSha256 与 ALC 依赖闭包；"
                + "请把每个包及其私有依赖放入独立目录。");
            return false;
        }

        foreach (KeyValuePair<string, byte[]> entry in bytesByPath)
            graph[Path.GetFileName(entry.Key)] = entry.Value;
        return true;
    }

    /// <summary>
    /// 读取程序集元数据里的 AssemblyRef 名字（不装载、不执行代码）。返回 false = 不是
    /// 可解析的托管程序集（坏 PE/无元数据）；调用方按 fail-closed 处理。
    /// </summary>
    static bool TryReadAssemblyReferenceNames(byte[] assemblyBytes, out List<string> references)
    {
        references = new List<string>();
        try
        {
            using var stream = new MemoryStream(assemblyBytes, writable: false);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return false;
            MetadataReader metadata = peReader.GetMetadataReader();
            foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
            {
                AssemblyReference reference = metadata.GetAssemblyReference(handle);
                string name = metadata.GetString(reference.Name);
                if (!string.IsNullOrEmpty(name))
                    references.Add(name);
            }
            return true;
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 拒绝指向包目录外的文件符号链接/目录联接。解析失败同样 fail-closed（无法证明目标
    /// 在包目录内）；普通文件（非链接）返回 true。
    /// M-1：链接解析 + 目标规范化 + 包含判定整体包在 try/catch 内——超长目标、平台不支持、
    /// 权限等任何解析失败都记错误并返回 false，绝不让异常逃出 loader（TryX 契约）。
    /// </summary>
    static bool TryEnsureLinkWithinDirectory(
        string fullPath,
        string packDirectory,
        string prefix,
        List<string> errors)
    {
        string fileName = Path.GetFileName(fullPath);
        bool withinDirectory;
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
            if (target is null)
                return true;

            string resolved = Path.GetFullPath(target.FullName);
            string directoryWithSeparator =
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(packDirectory)) + Path.DirectorySeparatorChar;
            withinDirectory = resolved.StartsWith(directoryWithSeparator, PathComparison);
        }
        catch (Exception exception)
        {
            errors.Add(prefix + "包目录内文件的符号链接解析失败（" + fileName + "）："
                + exception.Message);
            return false;
        }
        if (withinDirectory)
            return true;

        errors.Add(prefix + "包目录内的文件是指向包目录外的符号链接/联接，拒载（路径逃逸）："
            + fileName + "。");
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
                // M-5：错误前缀同样只回显基名。
                collected.Add(DisplayPrefix(path) + "packId 重复：" + handle.Manifest.PackId);
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
    /// 入口清单与内嵌清单的比较表（M-2）：PropertyName 是 <see cref="CompatPackManifest"/> 的
    /// public 属性名，FieldName 是错误/清单字段名（如 targetEngineApi），Selector 取值。
    /// 比较逻辑只遍历本表，反射守卫测试据 PropertyName 断言"manifest 新增属性未被比较"会红。
    /// </summary>
    static readonly (string PropertyName, string FieldName, Func<CompatPackManifest, object?> Selector)[]
        EntryManifestFields =
        {
            (nameof(CompatPackManifest.PackId), "packId", manifest => manifest.PackId),
            (nameof(CompatPackManifest.PackVersion), "packVersion", manifest => manifest.PackVersion),
            (nameof(CompatPackManifest.ContractAbi), "contractAbi", manifest => manifest.ContractAbi),
            (nameof(CompatPackManifest.EngineApi), "engineApi", manifest => manifest.EngineApi),
            (nameof(CompatPackManifest.UsedLegacyTargetEngineApi), "targetEngineApi",
                manifest => manifest.UsedLegacyTargetEngineApi),
            (nameof(CompatPackManifest.BaseProfileId), "baseProfileId", manifest => manifest.BaseProfileId),
            (nameof(CompatPackManifest.Surface), "surface", manifest => manifest.Surface),
            (nameof(CompatPackManifest.BaseSurfaceHash), "baseSurfaceHash", manifest => manifest.BaseSurfaceHash),
            (nameof(CompatPackManifest.Capabilities), "capabilities", manifest => manifest.Capabilities),
            (nameof(CompatPackManifest.SaveProfileId), "saveProfileId", manifest => manifest.SaveProfileId),
            (nameof(CompatPackManifest.VariantSelections), "variantSelections",
                manifest => manifest.VariantSelections),
            (nameof(CompatPackManifest.RequiredPacks), "requires", manifest => manifest.RequiredPacks),
            (nameof(CompatPackManifest.ConflictingPacks), "conflicts", manifest => manifest.ConflictingPacks),
            (nameof(CompatPackManifest.Overrides), "overrides", manifest => manifest.Overrides),
            (nameof(CompatPackManifest.GameIdentity), "gameIdentity", manifest => manifest.GameIdentity),
        };

    /// <summary>
    /// 比较表已覆盖的 <see cref="CompatPackManifest"/> public 属性名（反射守卫测试的唯一读取面；
    /// 不在别处重复维护）。
    /// </summary>
    internal static IReadOnlyList<string> ComparedEntryManifestProperties { get; } =
        EntryManifestFields.Select(field => field.PropertyName).ToArray();

    /// <summary>
    /// 清单字段的规范相等：字符串 Ordinal、序列按序、字典按内容、值对象/结构按值。
    /// 新增字段类型必须在此显式声明比较语义（否则会落到 Equals，由测试与评审约束）。
    /// </summary>
    static bool ManifestFieldEquals(object? left, object? right) => (left, right) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (string leftText, string rightText) => string.Equals(leftText, rightText, StringComparison.Ordinal),
        (CompatPackSurface leftSurface, CompatPackSurface rightSurface) => SurfaceEquals(leftSurface, rightSurface),
        (IReadOnlyDictionary<string, string> leftVariants, IReadOnlyDictionary<string, string> rightVariants)
            => VariantSelectionsEqual(leftVariants, rightVariants),
        (IReadOnlyList<CompatPackDependency> leftRequires, IReadOnlyList<CompatPackDependency> rightRequires)
            => RequiredPacksEqual(leftRequires, rightRequires),
        (IReadOnlyList<CompatPackOverride> leftOverrides, IReadOnlyList<CompatPackOverride> rightOverrides)
            => OverridesEqual(leftOverrides, rightOverrides),
        (CompatPackGameIdentity leftIdentity, CompatPackGameIdentity rightIdentity)
            => GameIdentityEquals(leftIdentity, rightIdentity),
        (IReadOnlyList<string> leftList, IReadOnlyList<string> rightList) => SequenceEqual(leftList, rightList),
        _ => Equals(left, right),
    };

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
        // 单一比较表：反射守卫测试（CompatPackLoaderTests）遍历 CompatPackManifest 的 public
        // 实例属性并断言全部被本表覆盖——未来给清单加字段而忘记扩展比较时该用例直接红，
        // 防止"入口清单可静默覆盖内嵌清单的新字段"（M-2）。
        foreach ((_, string fieldName, Func<CompatPackManifest, object?> selector) in EntryManifestFields)
        {
            if (!ManifestFieldEquals(selector(entryManifest), selector(embeddedManifest)))
                differing.Add(fieldName);
        }

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

    static bool RequiredPacksEqual(
        IReadOnlyList<CompatPackDependency> left,
        IReadOnlyList<CompatPackDependency> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].PackId, right[index].PackId, StringComparison.Ordinal)
                || !string.Equals(left[index].VersionRange, right[index].VersionRange, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    static bool OverridesEqual(
        IReadOnlyList<CompatPackOverride> left,
        IReadOnlyList<CompatPackOverride> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Symbol, right[index].Symbol, StringComparison.Ordinal)
                || left[index].Priority != right[index].Priority)
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
