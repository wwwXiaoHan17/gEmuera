using System.Reflection;
using System.Text;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 加载器全链路矩阵（docs/designs/compat-pack-interface.md §5）：正向加载样本为 Task 1
/// 契约夹具 CompatPackContractOnlyFixture（内嵌清单 + ContractOnlyPack 入口，只引用契约
/// 程序集）与 HelloCompatPackFixture（内嵌清单 + 表面/能力贡献），经独立 ALC 验证
/// 显式发现→依赖闭包→隔离→清单解析→入口发现→语义校验→冻结差量→哈希固化。
/// T5 起不再拿测试程序集兼任"包样本"：测试程序集引用 xunit/Core，隔离 ALC 中的无关类型
/// 会让 GetTypes 抛 ReflectionTypeLoadException，而加载器对该异常改为 fail-closed。
/// 所有真实包加载都经 <see cref="IsolatedPackDirectory"/>：包依赖闭包必须在只含本包
/// 程序集（主 DLL + 它真实引用到的依赖）的目录里成立，共享 bin 目录里的无关程序集会
/// 被加载器按"未被引用"拒载（I1 一包一目录）。
/// </summary>
[Collection("CompatPack")]
public class CompatPackLoaderTests
{
    /// <summary>正向加载样本：Task 1 契约夹具（packId community.contract-fixture，无 capability 声明）。</summary>
    static string FixturePath => typeof(CompatPackContractOnlyFixture.ContractOnlyPack).Assembly.Location;

    /// <summary>正向加载样本：Hello 夹具（packId test.hello-pack，表面 + 能力贡献）。</summary>
    static string HelloPackPath => typeof(HelloCompatPackFixture.HelloCompatPack).Assembly.Location;

    static CompatPackValidationContext RealContext() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
        // 宿主口径：METHOD 投影名并集参与双形态闭包（hide 函数会镜像隐藏同名 METHOD 指令）。
        methodProjectedInstructionNames: new HashSet<string>(
            LegacyDialectInventories.MethodProjectedInstructionNames, StringComparer.Ordinal));

    public CompatPackLoaderTests()
    {
        // 前置事实自检：夹具贡献与真实 v24 基线的关系（防生成清单漂移后测试假绿/假红）。
        Assert.Contains("CALLSHARP", LegacyDialectInventories.V24InstructionNames);
        Assert.DoesNotContain("SETANIMETIMER", LegacyDialectInventories.V24InstructionNames);
        Assert.Contains("EXISTVAR", LegacyDialectInventories.V24Functions.Select(entry => entry.Name));
        Assert.DoesNotContain("SQL_CONNECT", LegacyDialectInventories.V24Functions.Select(entry => entry.Name));
        // 清单变体选择的目标指令必须在基线内（交叉对账前置事实）；PRINT 供规则直测的
        // 变体贡献拒载向用例做基线前置，一并钉住。
        Assert.Contains("PRINT", LegacyDialectInventories.V24InstructionNames);
        Assert.Contains("SETBGIMAGE", LegacyDialectInventories.V24InstructionNames);
    }

    [Fact]
    public void TryLoad_ContractOnlyFixture_WithRealV24Baseline_Succeeds()
    {
        using var directory = new IsolatedPackDirectory(FixturePath);
        Assert.True(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            string.Join("; ", errors));

        Assert.Equal("community.contract-fixture", handle!.Manifest.PackId);
        // 契约夹具只声明表面贡献，不携带 capability（正向量与 hello 样本的差异点）。
        Assert.Empty(handle.ValidatedDelta.CapabilityIds);
        Assert.Contains(handle.ValidatedDelta.SurfaceStates, state =>
            state.Key == new CompatSymbolKey(CompatSymbolKind.Instruction, "SETANIMETIMER")
            && state.Kind == CompatSymbolStateKind.Present);
        // v1 死契约（评审 P2-6）：变体/策略贡献加载即拒载，正向夹具不得携带——差量侧恒为空。
        Assert.Empty(handle.ValidatedDelta.VariantSelections);
        Assert.Empty(handle.Dependencies);
        Assert.Matches("^[0-9a-f]{64}$", handle.AssemblySha256);
        Assert.Matches("^[0-9a-f]{64}$", handle.PackSha256);
        Assert.NotEqual(handle.AssemblySha256, handle.PackSha256);
        Assert.Equal(handle.PackSha256, handle.ValidatedDelta.PackSha256);
        Assert.Matches("^[0-9a-f]{64}$", handle.ValidatedDelta.DeltaSemanticHash);
        handle.Unload();
    }

    [Fact]
    public void TryLoad_HashesAndLoadsSameBytes_EvenIfFileChanges()
    {
        // TOCTOU 修复（E2-R5）：哈希与加载必须来自同一份字节。override 字节是真实契约夹具
        // DLL（合法包，packId community.contract-fixture）读一次的缓存；磁盘测试路径写入的
        // 却是另一份不同 manifest/packId 的 DLL（v18 数据包，packId pack.gemuera.v18）。
        // TryLoad 的结果——manifest 与双哈希——必须全部来自 override 字节，与磁盘文件无关。
        byte[] overrideBytes = File.ReadAllBytes(FixturePath);
        string expectedAssemblySha256 =
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(overrideBytes)).ToLowerInvariant();

        using var directory = new IsolatedPackDirectory(FixturePath);
        string diskPath = directory.MainAssemblyPath;
        File.WriteAllBytes(diskPath, File.ReadAllBytes(typeof(GemueraV18Pack.GemueraV18PackMarker).Assembly.Location));
        CompatPackLoader.BytesReaderOverrideForTest = _ => overrideBytes;
        CompatPackHandle? handle = null;
        try
        {
            Assert.True(CompatPackLoader.TryLoad(diskPath, RealContext(), out handle, out var errors),
                string.Join("; ", errors));

            // manifest 来自 override 字节（契约夹具），不是磁盘上的 v18 包。
            Assert.Equal("community.contract-fixture", handle!.Manifest.PackId);
            // 双哈希都按 override 字节推演（assembly ‖ manifest，依赖闭包为空），与磁盘 v18 字节无关。
            Assert.Equal(expectedAssemblySha256, handle.AssemblySha256);
            Assert.Equal(ExpectedPackSha256(overrideBytes), handle.PackSha256);
            // 依赖闭包里只有主 DLL（被排除），无额外依赖。
            Assert.Empty(handle.Dependencies);
        }
        finally
        {
            CompatPackLoader.BytesReaderOverrideForTest = null;
            handle?.Unload();
        }
    }

    /// <summary>
    /// 与 <see cref="CompatPackLoader"/> 规范同式的独立推演（T5 v1 格式）：用一次性探针
    /// ALC 从同一份字节提取内嵌清单原始字节（纯元数据操作，不解析依赖、不执行代码），
    /// 不经过被测加载器，避免同源假设自我循环。
    /// </summary>
    static string ExpectedPackSha256(
        byte[] packBytes,
        params (string Name, byte[] Bytes)[] dependencies)
    {
        const char lf = (char)10;
        var probe = new System.Runtime.Loader.AssemblyLoadContext("pack-hash-probe", isCollectible: true);
        try
        {
            System.Reflection.Assembly probeAssembly = probe.LoadFromStream(new MemoryStream(packBytes));
            byte[] manifestBytes = Array.Empty<byte>();
            foreach (string name in probeAssembly.GetManifestResourceNames())
            {
                if (!string.Equals(name, CompatPackManifest.ManifestResourceName, StringComparison.Ordinal))
                    continue;
                using Stream? stream = probeAssembly.GetManifestResourceStream(name);
                using var memory = new MemoryStream();
                stream!.CopyTo(memory);
                manifestBytes = memory.ToArray();
                break;
            }

            using var buffer = new MemoryStream();
            Write(Encoding.UTF8.GetBytes("gemuera.compatpack.pack.v1" + lf));
            WriteBlock("assembly", packBytes);
            WriteBlock("manifest", manifestBytes);
            foreach ((string name, byte[] bytes) in dependencies.OrderBy(dep => dep.Name, StringComparer.Ordinal))
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(name);
                Write(Encoding.UTF8.GetBytes("dependency:" + nameBytes.Length + ":"));
                Write(nameBytes);
                Write(Encoding.UTF8.GetBytes(":" + bytes.Length + lf));
                Write(bytes);
            }
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffer.ToArray()))
                .ToLowerInvariant();

            void Write(byte[] bytes) => buffer.Write(bytes);

            void WriteBlock(string label, byte[] bytes)
            {
                Write(Encoding.UTF8.GetBytes(label + ":" + bytes.Length + lf));
                Write(bytes);
            }
        }
        finally
        {
            probe.Unload();
        }
    }

    [Fact]
    public void TryLoad_SamePack_Twice_HashesDeterministic()
    {
        using var directory = new IsolatedPackDirectory(FixturePath);
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var second, out _));

        Assert.Equal(first!.PackSha256, second!.PackSha256);
        Assert.Equal(first.AssemblySha256, second.AssemblySha256);
        Assert.Equal(first.ValidatedDelta.DeltaSemanticHash, second.ValidatedDelta.DeltaSemanticHash);
        first.Unload();
        second!.Unload();
    }

    [Fact]
    public void TryLoad_UnknownCapability_Rejects()
    {
        // 只提供清单声明的另一个 id：input.pointer-button.v1 因缺失而成为 unknown。
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal)
            {
                ["startup.continue-after-fault.v1"] = EngineCapabilityCatalog.All["startup.continue-after-fault.v1"],
            },
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        using var directory = new IsolatedPackDirectory(HelloPackPath);
        Assert.False(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("input.pointer-button.v1"));
    }

    [Fact]
    public void TryLoad_EngineApiMismatch_Rejects()
    {
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(2, 0),
            EngineCapabilityCatalog.All,
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        using var directory = new IsolatedPackDirectory(HelloPackPath);
        Assert.False(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out _, out var errors));
        // 双轴模型：legacy 包映射为 1.0/1.0，引擎 2.0 的 major 分裂必须在 engine 轴被点名。
        Assert.Contains(errors, e => e.Contains("engineApi") && e.Contains("2.0"));
    }

    [Fact]
    public void TryLoad_UnknownBuiltinVariant_Rejects()
    {
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        using var directory = new IsolatedPackDirectory(HelloPackPath);
        Assert.False(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("builtin:snake"));
    }

    [Fact]
    public void TryLoad_HideOutsideBaseline_Rejects()
    {
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(StringComparer.Ordinal), // 空指令基线 → HideInstruction("CALLSHARP") 必然失配
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        using var directory = new IsolatedPackDirectory(HelloPackPath);
        Assert.False(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("CALLSHARP"));
    }

    [Fact]
    public void TryLoad_RegisterConflictsBaseline_Rejects()
    {
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames.Append("SETANIMETIMER"), StringComparer.Ordinal),
            new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

        using var directory = new IsolatedPackDirectory(HelloPackPath);
        Assert.False(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("SETANIMETIMER"));
    }

    [Fact]
    public void TryLoad_AssemblyWithoutManifest_Rejects()
    {
        // GEmuera.Core.dll 是真实存在、无内嵌清单的程序集 → 拒载并指名资源。
        string corePath = typeof(CompatPackRules).Assembly.Location;
        using var directory = new IsolatedPackDirectory(corePath);
        Assert.False(CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out _, out var errors));
        Assert.Contains(errors, e => e.Contains(CompatPackManifest.ManifestResourceName));
    }

    [Fact]
    public void TryLoad_MissingFileOrNotDll_Rejects()
    {
        Assert.False(CompatPackLoader.TryLoad("Z:/definitely/not/there.dll", RealContext(), out _, out var errors));
        Assert.Contains(errors, e => e.Contains("不存在"));

        string notDll = Path.Combine(Path.GetTempPath(), "compat-pack-not-dll-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(notDll, "x");
        try
        {
            Assert.False(CompatPackLoader.TryLoad(notDll, RealContext(), out _, out errors));
            Assert.Contains(errors, e => e.Contains(".dll"));
        }
        finally
        {
            File.Delete(notDll);
        }
    }

    [Fact]
    public void TryLoadSet_SinglePack_Succeeds()
    {
        using var directory = new IsolatedPackDirectory(FixturePath);
        Assert.True(CompatPackLoader.TryLoadSet(new[] { directory.MainAssemblyPath }, RealContext(), out var set, out var errors),
            string.Join("; ", errors));
        Assert.Single(set!.Handles);
        set.UnloadAll();
    }

    [Fact]
    public void TryLoadSet_DuplicatePackId_RejectsWholeSet()
    {
        using var directory = new IsolatedPackDirectory(FixturePath);
        string packPath = directory.MainAssemblyPath;
        Assert.False(CompatPackLoader.TryLoadSet(new[] { packPath, packPath }, RealContext(), out var set, out var errors));
        Assert.Null(set);
        Assert.Contains(errors, e => e.Contains("packId 重复"));
    }

    [Fact]
    public void ValidatePackEntry_ThrowingManifestGetter_RejectsWithoutException()
    {
        string json = "{\"packId\":\"test.hello-pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        // DispatchProxy 运行时生成代理类型（不进本程序集元数据，不干扰"恰好一个入口"扫描），
        // 任意成员访问即抛——验证包作者可控 getter 的异常不逃逸。
        var evil = System.Reflection.DispatchProxy.Create<ICompatPack, ThrowingPackProxy>();

        var exception = Record.Exception(() =>
        {
            Assert.False(CompatPackLoader.ValidatePackEntry(evil, manifest, RealContext(), out _, out var errors, out _));
            Assert.Contains(errors!, e => e.Contains("抛出异常"));
        });
        Assert.Null(exception);
    }

    class ThrowingPackProxy : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void ResolvePackEntry_AssemblyWithTwoEntries_Rejects()
    {
        // plan Task 5 Step 6 / spec §6.5：一个包程序集必须恰好一个 ICompatPack 入口。
        // 多入口时按 GetTypes 顺序挑一个会让贡献集合依赖类型枚举顺序（不确定），必须拒载。
        string json = "{\"packId\":\"test.double-entry\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";
        CompatPackManifest manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));

        var errors = new List<string>();
        ICompatPack? pack = CompatPackLoader.ResolvePackEntry(
            typeof(CompatPackDoubleEntryFixture.DoubleEntryFixtureMarker).Assembly, manifest, errors);

        Assert.Null(pack);
        Assert.Contains(errors, error => error.Contains("多个 ICompatPack") && error.Contains("2 个"));
    }

    [Fact]
    public void ResolvePackEntry_AssemblyWithoutEntry_ReturnsManifestOnlyPack()
    {
        string json = "{\"packId\":\"test.data-only\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"surface\":{\"addInstructions\":[\"SETANIMETIMER\"]}}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));

        var errors = new List<string>();
        ICompatPack? pack = CompatPackLoader.ResolvePackEntry(typeof(object).Assembly, manifest, errors);

        Assert.Empty(errors);
        Assert.IsType<ManifestOnlyCompatPack>(pack);
        Assert.Same(manifest, pack!.Manifest);
    }

    [Fact]
    public void ManifestOnlyPack_WithManifestSurface_ValidatesThroughCombinedContributions()
    {
        var context = RealContext();
        string json = "{\"packId\":\"test.data-surface\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"baseProfileId\":\"v24pure\","
            + "\"baseSurfaceHash\":\"" + context.BaselineSurfaceHash + "\","
            + "\"surface\":{\"addInstructions\":[\"SETANIMETIMER\"],\"hideInstructions\":[\"CALLSHARP\"],"
            + "\"addFunctions\":[\"SQL_CONNECT\"],\"hideFunctions\":[\"EXISTVAR\"]}}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        ICompatPack pack = new ManifestOnlyCompatPack(manifest);
        IReadOnlyList<ICompatPackContribution> manifestContributions =
            CompatPackLoader.CreateManifestContributions(manifest, context);

        Assert.Single(manifestContributions);
        Assert.IsType<ManifestSurfaceContribution>(manifestContributions[0]);
        Assert.True(
            CompatPackLoader.ValidatePackEntry(
                pack, manifest, context, out var combined, out var errors, out _, manifestContributions),
            string.Join("; ", errors));
        Assert.Single(combined);
        Assert.IsType<ManifestSurfaceContribution>(combined[0]);
    }

    [Fact]
    public void TryLoad_ManifestOnlyAssembly_LoadsWithoutEntryType()
    {
        string packPath = typeof(DataOnlyFixture.DataOnlyCompatPackFixtureMarker).Assembly.Location;
        using var directory = new IsolatedPackDirectory(packPath);
        Assert.True(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            string.Join("; ", errors));

        Assert.Equal("test.data-only", handle!.Manifest.PackId);
        Assert.IsType<ManifestOnlyCompatPack>(handle.Pack);
        Assert.Contains(handle.ValidatedDelta.SurfaceStates, state =>
            state.Key == new CompatSymbolKey(CompatSymbolKind.Instruction, "SETANIMETIMER")
            && state.Kind == CompatSymbolStateKind.Present);
        Assert.Contains(handle.ValidatedDelta.SurfaceStates, state =>
            state.Key == new CompatSymbolKey(CompatSymbolKind.Instruction, "CALLSHARP")
            && state.Kind == CompatSymbolStateKind.Hidden);
        Assert.Contains("SETANIMETIMER", handle.Manifest.Surface.AddInstructions);
        Assert.Contains("CALLSHARP", handle.Manifest.Surface.HideInstructions);
        handle.Unload();
    }

    // —— T5：加载完整性、依赖哈希与冻结差量 ——

    [Fact]
    public void TryLoad_ChangingReferencedDependencyBytes_ChangesPackSha256()
    {
        // 依赖闭包进 PackSha256：同一主 DLL / 内嵌清单，只改同目录依赖字节 → 包哈希必须变，
        // 程序集哈希（只看主 DLL 字节）必须不变。依赖只按文件基名/长度/字节入哈希。
        string packPath = typeof(CompatPackTypeLoadFixture.TypeLoadFailingPack).Assembly.Location;
        string dependencyPath = typeof(CompatPackTypeLoadDependency.TypeLoadDependencyBase).Assembly.Location;
        byte[] dependencyBytes = File.ReadAllBytes(dependencyPath);
        using var directory = new IsolatedPackDirectory(packPath, dependencyPath);

        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var first, out var firstErrors),
            string.Join("; ", firstErrors));
        string firstPackSha256 = first!.PackSha256;
        string assemblySha256 = first.AssemblySha256;
        CompatPackDependencyInfo recorded = Assert.Single(first.Dependencies);
        Assert.Equal(TypeLoadDependencyName, recorded.FileName);
        // 诊断只记 basename + SHA256（不泄漏完整路径）。
        Assert.DoesNotContain(Path.DirectorySeparatorChar, recorded.FileName);
        Assert.Equal(Sha256Reference(dependencyBytes), recorded.Sha256);
        Assert.Matches("^[0-9a-f]{64}$", firstPackSha256);
        first.Unload();

        // 追加 1 字节：仍是可装载 PE（元数据偏移不变），但字节/哈希必须变——
        // 入哈希的字节就是被 ALC 装载的字节。
        byte[] mutated = dependencyBytes.Append((byte)0).ToArray();
        directory.WriteAssembly(TypeLoadDependencyName, mutated);
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var second, out var secondErrors),
            string.Join("; ", secondErrors));
        Assert.Equal(assemblySha256, second!.AssemblySha256);
        Assert.NotEqual(firstPackSha256, second.PackSha256);
        // 独立推演：主 DLL + 内嵌清单 + 被引用依赖字节的规范哈希。
        byte[] mainBytes = File.ReadAllBytes(directory.MainAssemblyPath);
        Assert.Equal(
            ExpectedPackSha256(mainBytes, (TypeLoadDependencyName, mutated)),
            second.PackSha256);
        second.Unload();
    }

    const string TypeLoadDependencyName = "CompatPackTypeLoadDependency.dll";

    [Fact]
    public void TryLoad_UnreferencedTopLevelDll_RejectsWithActionableError()
    {
        // I1：目录顶层未被主程序集 AssemblyRef 引用的 DLL 一律拒载。旧的"整个目录都是
        // 依赖闭包"语义会让同目录的另一个包进 PackSha256 并可被 ALC 解析（未启用包可见）。
        using var directory = new IsolatedPackDirectory(HelloPackPath);
        string neighbourName = Path.GetFileName(FixturePath);
        directory.WriteAssembly(neighbourName, File.ReadAllBytes(FixturePath));

        Assert.False(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            "目录内未被引用的 DLL 被当作依赖接受。");
        Assert.Null(handle);
        Assert.Contains(errors, error => error.Contains("自包含") && error.Contains(neighbourName));
        Assert.Contains(errors, error => error.Contains("未被主程序集") && error.Contains("独立目录"));
    }

    [Fact]
    public void TryLoad_TwoPacksInOneDirectory_RejectsBothAndNeverMixesPackHashes()
    {
        // I1 主回归（共享扫描根）：两个互不引用的包放在同一目录 → 加载其中任一个都必须
        // 失败（另一个是未被引用的顶层 DLL），而不是把邻居字节算进自己的 PackSha256 /
        // 装进自己的 ALC。移走邻居后，主包 PackSha256 与干净单包目录逐字相同（无污染）。
        string neighbourSource = HelloPackPath;
        string neighbourName = Path.GetFileName(neighbourSource);
        using var shared = new IsolatedPackDirectory(FixturePath);
        shared.WriteAssembly(neighbourName, File.ReadAllBytes(neighbourSource));

        Assert.False(
            CompatPackLoader.TryLoad(shared.MainAssemblyPath, RealContext(), out _, out var mainErrors),
            "同目录另一个包存在时主包仍被接受。");
        Assert.Contains(mainErrors, error => error.Contains(neighbourName) && error.Contains("未被主程序集"));

        Assert.False(
            CompatPackLoader.TryLoad(
                Path.Combine(shared.DirectoryPath, neighbourName), RealContext(), out _, out var neighbourErrors),
            "同目录另一个包存在时邻居包仍被接受。");
        Assert.Contains(neighbourErrors, error => error.Contains("未被主程序集"));

        File.Delete(Path.Combine(shared.DirectoryPath, neighbourName));
        Assert.True(
            CompatPackLoader.TryLoad(shared.MainAssemblyPath, RealContext(), out var sharedHandle, out var sharedErrors),
            string.Join("; ", sharedErrors));
        using var solo = new IsolatedPackDirectory(FixturePath);
        Assert.True(
            CompatPackLoader.TryLoad(solo.MainAssemblyPath, RealContext(), out var soloHandle, out var soloErrors),
            string.Join("; ", soloErrors));
        try
        {
            Assert.Equal(soloHandle!.PackSha256, sharedHandle!.PackSha256);
        }
        finally
        {
            sharedHandle?.Unload();
            soloHandle?.Unload();
        }
    }

    [Fact]
    public void TryLoad_DependencyClosureHash_IsDeterministicAcrossDirectories()
    {
        // M-6：PackSha256 必须与目录落盘/枚举顺序无关（闭包按 AssemblyRef 推导，哈希按
        // 文件基名 Ordinal 排序）。两个内容相同的隔离目录必须得到同一包哈希，且与独立
        // 规范推演（主 DLL + 内嵌清单 + 被引用依赖字节）逐字一致。
        string packPath = typeof(CompatPackTypeLoadFixture.TypeLoadFailingPack).Assembly.Location;
        string dependencyPath = typeof(CompatPackTypeLoadDependency.TypeLoadDependencyBase).Assembly.Location;
        byte[] dependencyBytes = File.ReadAllBytes(dependencyPath);
        using var first = new IsolatedPackDirectory(packPath, dependencyPath);
        using var second = new IsolatedPackDirectory(packPath, dependencyPath);

        Assert.True(
            CompatPackLoader.TryLoad(first.MainAssemblyPath, RealContext(), out var firstHandle, out var firstErrors),
            string.Join("; ", firstErrors));
        Assert.True(
            CompatPackLoader.TryLoad(second.MainAssemblyPath, RealContext(), out var secondHandle, out var secondErrors),
            string.Join("; ", secondErrors));
        try
        {
            Assert.Equal(firstHandle!.PackSha256, secondHandle!.PackSha256);
            byte[] mainBytes = File.ReadAllBytes(first.MainAssemblyPath);
            Assert.Equal(
                ExpectedPackSha256(mainBytes, (TypeLoadDependencyName, dependencyBytes)),
                firstHandle.PackSha256);
        }
        finally
        {
            firstHandle?.Unload();
            secondHandle?.Unload();
        }
    }

    [Fact]
    public void ComputePackHash_TwoDependencies_IsIndependentOfClosureEnumerationOrder()
    {
        // I1 的 AssemblyRef 闭包规则下，旧"两个无引用 dummy 依赖"的端到端夹具不再合法
        // （未被引用的顶层 DLL 一律拒载），两依赖顺序无关属性改为直接钉规范哈希：
        // 同一组依赖以不同插入/枚举顺序传入（模拟闭包发现顺序变化）必须得到同一
        // PackSha256，且与独立规范推演（ExpectedPackSha256）逐字一致。
        Assembly mainAssembly = typeof(CompatPackTypeLoadFixture.TypeLoadFailingPack).Assembly;
        byte[] mainBytes = File.ReadAllBytes(mainAssembly.Location);
        byte[] manifestBytes = EmbeddedManifestBytes(mainAssembly);
        byte[] dependencyA = File.ReadAllBytes(
            typeof(CompatPackTypeLoadDependency.TypeLoadDependencyBase).Assembly.Location);
        byte[] dependencyB = File.ReadAllBytes(typeof(ICompatPack).Assembly.Location);
        const string dependencyAName = "CompatPackTypeLoadDependency.dll";
        const string dependencyBName = "Emuera.CompatPack.dll";
        string mainName = Path.GetFileName(mainAssembly.Location);

        var forward = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [mainName] = mainBytes,
            [dependencyAName] = dependencyA,
            [dependencyBName] = dependencyB,
        };
        var reverse = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [dependencyBName] = dependencyB,
            [dependencyAName] = dependencyA,
            [mainName] = mainBytes,
        };

        string first = CompatPackLoader.ComputePackHash(mainBytes, manifestBytes, forward, mainName);
        string second = CompatPackLoader.ComputePackHash(mainBytes, manifestBytes, reverse, mainName);

        Assert.Equal(first, second);
        Assert.Equal(
            ExpectedPackSha256(mainBytes, (dependencyAName, dependencyA), (dependencyBName, dependencyB)),
            first);
    }

    static byte[] EmbeddedManifestBytes(Assembly assembly)
    {
        using Stream? stream = assembly.GetManifestResourceStream(CompatPackManifest.ManifestResourceName);
        Assert.NotNull(stream);
        using var memory = new MemoryStream();
        stream!.CopyTo(memory);
        return memory.ToArray();
    }

    static string Sha256Reference(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    [SymlinkFact]
    public void TryLoad_SymlinkOutsidePackDirectory_IsRejected()
    {
        // 依赖闭包读的是"包目录内的 dll"：指向目录外的 symlink 必须拒载，否则部署目录
        // 里的一个链接就能把任意宿主/系统文件卷进包哈希与装载路径。Windows 创建符号链接
        // 需要开发者模式/特权：能力探测在 discovery 期完成（SymlinkFactAttribute），不能
        // 创建时 xUnit 报告 skipped——跳过 ≠ 通过。
        using var directory = new IsolatedPackDirectory(HelloPackPath);
        string outsideDirectory = Path.Combine(
            Path.GetTempPath(), "compat-pack-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDirectory);
        string outsideDll = Path.Combine(outsideDirectory, "LinkedDependency.dll");
        File.WriteAllBytes(outsideDll, new byte[] { 0x4d, 0x5a });
        try
        {
            string linkPath = Path.Combine(directory.DirectoryPath, "LinkedDependency.dll");
            // discovery 已确认本机具备能力；若运行期仍失败（环境变化），让异常按测试失败
            // 上报——绝不吞掉后 return 成 passed。
            File.CreateSymbolicLink(linkPath, outsideDll);

            Assert.False(
                CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
                "指向包目录外的 symlink 被当作合法依赖。");
            Assert.Null(handle);
            Assert.Contains(errors, error => error.Contains("符号链接") || error.Contains("路径逃逸"));
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_TypeLoadFailure_IsRejected()
    {
        // 坏依赖字节：入口类型的基类无法解析 → Assembly.GetTypes 抛 ReflectionTypeLoadException。
        // T5 起必须整体拒载，不得把"部分可枚举类型"当成零入口数据包降级为 manifest-only。
        string fixturePath = typeof(CompatPackTypeLoadFixture.TypeLoadFailingPack).Assembly.Location;
        using var directory = new IsolatedPackDirectory(fixturePath);
        directory.WriteAssembly("CompatPackTypeLoadDependency.dll", new byte[] { 0x4d, 0x5a, 0x00, 0x01, 0x02 });

        Assert.False(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            "坏依赖/类型加载失败被降级成 manifest-only（fail-closed 未生效）。");
        Assert.Null(handle);
        // I-2(a)：必须给出"依赖字节确实被找到并送进 LoadFromStream"的证据。坏字节在
        // ALC.Load 中触发 BadImageFormatException，经 ReflectionTypeLoadException 的
        // LoaderExceptions 出现在错误里；依赖缺失/依赖图键错误只会产生裸 FileLoadException，
        // 不能满足 BadImageFormatException 断言（旧的 "ReflectionTypeLoadException 或依赖名"
        // OR 断言会被这种"根本没装载依赖"的失败蒙混过去）。
        Assert.Contains(errors, error => error.Contains("ReflectionTypeLoadException"));
        Assert.Contains(errors, error => error.Contains("BadImageFormatException"));
    }

    [Fact]
    public void TryLoad_ValidDependencyBytes_LoadsDependencyFromGraph()
    {
        // I-2(b) 正向证据：依赖字节图既是 PackSha256 的输入，也是 ALC 的唯一装载来源。
        // 入口类型继承依赖程序集的基类；依赖字节有效且不被覆盖 → GetTypes/入口实例化必须
        // 成功，且依赖程序集只能出现在包的隔离 ALC 内（该 ALC 没有任何路径回退）。
        string fixturePath = typeof(CompatPackTypeLoadFixture.TypeLoadFailingPack).Assembly.Location;
        string dependencyPath = typeof(CompatPackTypeLoadDependency.TypeLoadDependencyBase).Assembly.Location;
        const string dependencyName = "CompatPackTypeLoadDependency.dll";
        byte[] dependencyBytes = File.ReadAllBytes(dependencyPath);

        using var directory = new IsolatedPackDirectory(fixturePath, dependencyPath);
        Assert.True(
            CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var handle, out var errors),
            "有效依赖未被依赖字节图装载：" + string.Join("; ", errors));
        try
        {
            // 入口类型（继承依赖基类）已成功实例化 → 基类解析走通。
            Assert.Equal("CompatPackTypeLoadFixture.TypeLoadFailingPack", handle!.Pack.GetType().FullName);

            // 依赖诊断：basename + 内容 SHA256（不含主 DLL，不含完整路径）。
            CompatPackDependencyInfo recorded = Assert.Single(handle.Dependencies);
            Assert.Equal(dependencyName, recorded.FileName);
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dependencyBytes)).ToLowerInvariant(),
                recorded.Sha256);

            // 依赖程序集确实装进了包的隔离 ALC（来自字节图；该 ALC 无磁盘路径回退）。
            Assert.Contains(
                handle.LoadContext.Assemblies,
                assembly => string.Equals(assembly.GetName().Name, "CompatPackTypeLoadDependency", StringComparison.Ordinal));

            // 依赖字节同时进入 PackSha256：与独立规范推演一致。
            byte[] mainBytes = File.ReadAllBytes(directory.MainAssemblyPath);
            Assert.Equal(
                ExpectedPackSha256(mainBytes, (dependencyName, dependencyBytes)),
                handle.PackSha256);
        }
        finally
        {
            handle?.Unload();
        }
    }

    [Fact]
    public void ValidatePackEntry_ExecutesSurfaceContributionOnce()
    {
        // 校验期回放恰好一次；组装期只读冻结 delta，不再第二次执行包贡献。
        var contribution = new CountingSurfaceContribution();
        string json = "{\"packId\":\"test.once\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\",\"baseProfileId\":\"v24pure\"}";
        CompatPackManifest manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        var pack = new CountingPack(manifest, contribution);
        string packSha256 = new string('a', 64);

        Assert.True(
            CompatPackLoader.ValidatePackEntry(
                pack, manifest, RealContext(), out _, out var errors, out _, out var delta, packSha256: packSha256),
            string.Join("; ", errors));
        Assert.Equal(1, contribution.ApplyCount);
        Assert.NotNull(delta);
        Assert.Equal(packSha256, delta!.PackSha256);
        Assert.Contains(delta.SurfaceStates, state =>
            state.Key == new CompatSymbolKey(CompatSymbolKind.Instruction, "SETANIMETIMER")
            && state.Kind == CompatSymbolStateKind.Present);

        var handle = new CompatPackHandle(
            manifest, pack, "Z:/test.once.dll", new string('b', 64), delta,
            Array.Empty<CompatPackDependencyInfo>(),
            new CompatPackLoadContext("Z:/test.once.dll", new Dictionary<string, byte[]>()));
        try
        {
            Assert.True(
                CompatPackPlanAssembler.TryAssemble(
                    BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure"),
                    new[] { handle },
                    RealContext(),
                    out var assembled,
                    out var assemblyErrors),
                string.Join("; ", assemblyErrors));
            Assert.True(assembled.Dialect.TryGetInstruction("SETANIMETIMER", out _));
            Assert.Equal(1, contribution.ApplyCount);
        }
        finally
        {
            handle.Unload();
        }
    }

    [Fact]
    public void ValidatePackEntry_ManifestFieldMismatch_Rejects()
    {
        // 入口类 Manifest 与内嵌清单必须全字段相等：仅 packId/packVersion 一致不够
        //（capabilities 漂移会让"清单声明的包"与"代码实际宣称的包"分裂）。
        string embeddedJson = "{\"packId\":\"test.manifest-parity\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\",\"baseProfileId\":\"v24pure\"}";
        CompatPackManifest embedded = CompatPackManifest.TryParse(embeddedJson, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        string driftedJson = "{\"packId\":\"test.manifest-parity\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\",\"baseProfileId\":\"v24pure\","
            + "\"capabilities\":[\"input.pointer-button.v1\"]}";
        CompatPackManifest drifted = CompatPackManifest.TryParse(driftedJson, out var parsedDrifted, out var driftErrors)
            ? parsedDrifted! : throw new InvalidOperationException(string.Join("; ", driftErrors));
        var pack = new CountingPack(drifted, new CountingSurfaceContribution());

        Assert.False(
            CompatPackLoader.ValidatePackEntry(pack, embedded, RealContext(), out _, out var errors, out _, out var delta));
        Assert.Null(delta);
        Assert.Contains(errors, error => error.Contains("不一致") && error.Contains("capabilities"));
    }

    [Fact]
    public void ValidatePackEntry_CompositionFieldMismatch_Rejects()
    {
        // T6：入口清单与内嵌清单的 requires/conflicts/overrides 也必须逐字段相等——
        // 组合声明漂移会让"清单声明的包"与"代码宣称的包"在依赖/冲突语义上分裂。
        const string embeddedJson = "{\"packId\":\"test.compose-parity\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\"}";
        CompatPackManifest embedded = CompatPackManifest.TryParse(embeddedJson, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        const string driftedJson = "{\"packId\":\"test.compose-parity\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\","
            + "\"overrides\":[{\"symbol\":\"instruction:SETANIMETIMER\",\"priority\":10}]}";
        CompatPackManifest drifted = CompatPackManifest.TryParse(driftedJson, out var parsedDrifted, out var driftErrors)
            ? parsedDrifted! : throw new InvalidOperationException(string.Join("; ", driftErrors));
        var pack = new CountingPack(drifted, new CountingSurfaceContribution());

        Assert.False(
            CompatPackLoader.ValidatePackEntry(pack, embedded, RealContext(), out _, out var errors, out _, out var delta));
        Assert.Null(delta);
        Assert.Contains(errors, error => error.Contains("不一致") && error.Contains("overrides"));
    }

    [Fact]
    public void EntryManifestComparison_CoversEveryManifestPublicProperty()
    {
        // M-2 反射守卫：入口清单与内嵌清单的"全字段相等"由 loader 的比较表驱动；本用例
        // 遍历 CompatPackManifest 的全部 public 实例属性，断言每个属性都被比较表覆盖。
        // T6 给清单加 requires/conflicts/overrides 时已出现过手工字段清单漂移风险；未来再
        // 加字段而忘记扩展比较表 → 本用例红，而不是让入口清单静默覆盖新字段。
        string[] declared = typeof(CompatPackManifest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(declared);

        IReadOnlyList<string> compared = CompatPackLoader.ComparedEntryManifestProperties;
        string[] missing = declared.Except(compared, StringComparer.Ordinal).ToArray();
        string[] stale = compared.Except(declared, StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0,
            "入口/内嵌清单比较未覆盖 CompatPackManifest 的 public 属性：" + string.Join(", ", missing));
        Assert.True(
            stale.Length == 0,
            "入口/内嵌清单比较表包含不存在/非 public 的 CompatPackManifest 属性：" + string.Join(", ", stale));
        Assert.Equal(declared.Length, compared.Distinct(StringComparer.Ordinal).Count());
    }

    sealed class CountingPack : ICompatPack
    {
        public CountingPack(CompatPackManifest manifest, ICompatPackContribution contribution)
        {
            Manifest = manifest;
            Contributions = new[] { contribution };
        }

        public CompatPackManifest Manifest { get; }

        public IReadOnlyList<ICompatPackContribution> Contributions { get; }
    }

    sealed class CountingSurfaceContribution : ISurfaceContribution
    {
        public int ApplyCount { get; private set; }

        public string ContributionId => "test.once.surface";

        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
        {
            ApplyCount++;
            instructions.RegisterInstruction("SETANIMETIMER");
        }
    }
}
