using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 加载器全链路矩阵（docs/designs/compat-pack-interface.md §5）：正向加载样本为 Task 1
/// 契约夹具 CompatPackContractOnlyFixture（内嵌清单 + ContractOnlyPack 入口，只引用契约
/// 程序集），经独立 ALC 验证显式发现→隔离→清单解析→入口发现→语义校验→哈希固化。
/// 本测试程序集（内嵌 HelloCompatPack 清单）保留为负向样本：拒载向用例（fail-closed
/// 三原则正反向）在规则段被拒，不依赖绑定回落。ALC 允许清单的隔离行为另见
/// CompatPackAlcIsolationTests（探针拒载 / 契约夹具正向对照）。
/// </summary>
[Collection("CompatPack")]
public class CompatPackLoaderTests
{
    static string PackPath => typeof(CompatPackLoaderTests).Assembly.Location;

    /// <summary>正向加载样本：Task 1 契约夹具（packId community.contract-fixture，无 capability 声明）。</summary>
    static string FixturePath => typeof(CompatPackContractOnlyFixture.ContractOnlyPack).Assembly.Location;

    static CompatPackValidationContext RealContext() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

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
        Assert.True(CompatPackLoader.TryLoad(FixturePath, RealContext(), out var handle, out var errors),
            string.Join("; ", errors));

        Assert.Equal("community.contract-fixture", handle!.Manifest.PackId);
        Assert.Single(handle.Surface);
        // 契约夹具只声明表面贡献，不携带 capability（正向量与 hello 样本的差异点）。
        Assert.Empty(handle.Capabilities);
        // v1 死契约（评审 P2-6）：变体/策略贡献加载即拒载，正向夹具不得携带——句柄侧恒为空。
        Assert.Empty(handle.Variants);
        Assert.Empty(handle.Policies);
        Assert.Matches("^[0-9a-f]{64}$", handle.AssemblySha256);
        Assert.Matches("^[0-9a-f]{64}$", handle.PackSha256);
        Assert.NotEqual(handle.AssemblySha256, handle.PackSha256);
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

        string diskPath = Path.Combine(
            Path.GetTempPath(), "compat-pack-toctou-" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(diskPath, File.ReadAllBytes(typeof(GemueraV18Pack.GemueraV18PackMarker).Assembly.Location));
        CompatPackLoader.BytesReaderOverrideForTest = _ => overrideBytes;
        CompatPackHandle? handle = null;
        try
        {
            Assert.True(CompatPackLoader.TryLoad(diskPath, RealContext(), out handle, out var errors),
                string.Join("; ", errors));

            // manifest 来自 override 字节（契约夹具），不是磁盘上的 v18 包。
            Assert.Equal("community.contract-fixture", handle!.Manifest.PackId);
            // 双哈希都按 override 字节推演（file ‖ 内嵌清单原始字节），与磁盘 v18 字节无关。
            Assert.Equal(expectedAssemblySha256, handle.AssemblySha256);
            Assert.Equal(ExpectedPackSha256(overrideBytes), handle.PackSha256);
        }
        finally
        {
            CompatPackLoader.BytesReaderOverrideForTest = null;
            handle?.Unload();
            // 按路径加载（修复前行为）会在 Windows 上内存映射磁盘文件，Unload 收尾前删除
            // 可能被拒；stream 加载（修复后）从不打开磁盘文件，此 catch 仅防御清理路径。
            try { File.Delete(diskPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 与 <see cref="CompatPackHandle"/> 文档同式（file‖resource）的独立推演：用一次性探针
    /// ALC 从同一份字节提取内嵌清单原始字节（纯元数据操作，不解析依赖、不执行代码），
    /// 不经过被测加载器，避免同源假设自我循环。
    /// </summary>
    static string ExpectedPackSha256(byte[] packBytes)
    {
        var probe = new System.Runtime.Loader.AssemblyLoadContext("toctou-hash-probe", isCollectible: true);
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
            byte[] concatenated = new byte[packBytes.Length + manifestBytes.Length];
            packBytes.CopyTo(concatenated, 0);
            manifestBytes.CopyTo(concatenated, packBytes.Length);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(concatenated)).ToLowerInvariant();
        }
        finally
        {
            probe.Unload();
        }
    }

    [Fact]
    public void TryLoad_SamePack_Twice_HashesDeterministic()
    {
        Assert.True(CompatPackLoader.TryLoad(FixturePath, RealContext(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(FixturePath, RealContext(), out var second, out _));

        Assert.Equal(first!.PackSha256, second!.PackSha256);
        Assert.Equal(first.AssemblySha256, second.AssemblySha256);
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

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
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

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
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

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
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

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
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

        Assert.False(CompatPackLoader.TryLoad(PackPath, context, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("SETANIMETIMER"));
    }

    [Fact]
    public void TryLoad_AssemblyWithoutManifest_Rejects()
    {
        // GEmuera.Core.dll 是真实存在、无内嵌清单的程序集 → 拒载并指名资源。
        string corePath = typeof(CompatPackRules).Assembly.Location;
        Assert.False(CompatPackLoader.TryLoad(corePath, RealContext(), out _, out var errors));
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
        Assert.True(CompatPackLoader.TryLoadSet(new[] { FixturePath }, RealContext(), out var set, out var errors),
            string.Join("; ", errors));
        Assert.Single(set!.Handles);
        set.UnloadAll();
    }

    [Fact]
    public void TryLoadSet_DuplicatePackId_RejectsWholeSet()
    {
        Assert.False(CompatPackLoader.TryLoadSet(new[] { FixturePath, FixturePath }, RealContext(), out var set, out var errors));
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
            Assert.False(CompatPackLoader.ValidatePackEntry(evil, manifest, RealContext(), out _, out var errors));
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
        string json = "{\"packId\":\"test.data-surface\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"baseProfileId\":\"v24pure\","
            + "\"surface\":{\"addInstructions\":[\"SETANIMETIMER\"],\"hideInstructions\":[\"CALLSHARP\"],"
            + "\"addFunctions\":[\"SQL_CONNECT\"],\"hideFunctions\":[\"EXISTVAR\"]}}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        var context = RealContext();
        ICompatPack pack = new ManifestOnlyCompatPack(manifest);
        IReadOnlyList<ICompatPackContribution> manifestContributions =
            CompatPackLoader.CreateManifestContributions(manifest, context);

        Assert.Single(manifestContributions);
        Assert.IsType<ManifestSurfaceContribution>(manifestContributions[0]);
        Assert.True(
            CompatPackLoader.ValidatePackEntry(
                pack, manifest, context, out var combined, out var errors, manifestContributions),
            string.Join("; ", errors));
        Assert.Single(combined);
        Assert.IsType<ManifestSurfaceContribution>(combined[0]);
    }

    [Fact]
    public void TryLoad_ManifestOnlyAssembly_LoadsWithoutEntryType()
    {
        string packPath = typeof(DataOnlyFixture.DataOnlyCompatPackFixtureMarker).Assembly.Location;
        Assert.True(CompatPackLoader.TryLoad(packPath, RealContext(), out var handle, out var errors),
            string.Join("; ", errors));

        Assert.Equal("test.data-only", handle!.Manifest.PackId);
        Assert.IsType<ManifestOnlyCompatPack>(handle.Pack);
        Assert.Single(handle.Surface);
        Assert.IsType<ManifestSurfaceContribution>(handle.Surface[0]);
        Assert.Contains("SETANIMETIMER", handle.Manifest.Surface.AddInstructions);
        Assert.Contains("CALLSHARP", handle.Manifest.Surface.HideInstructions);
        handle.Unload();
    }

}
