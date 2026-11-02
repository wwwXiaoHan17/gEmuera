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
/// 所有真实包加载都经 <see cref="IsolatedPackDirectory"/>：加载器把包目录顶层全部 *.dll
/// 读成依赖闭包，共享 bin 目录会让"包"语义失真。
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
    public void TryLoad_ChangingDependencyBytes_ChangesPackSha256()
    {
        // 依赖闭包进 PackSha256：同一主 DLL / 内嵌清单，只改同目录依赖字节 → 包哈希必须变，
        // 程序集哈希（只看主 DLL 字节）必须不变。依赖只按文件基名/长度/字节入哈希。
        using var directory = new IsolatedPackDirectory(HelloPackPath);
        const string dependencyName = "DummyDependency.dll";
        directory.WriteAssembly(dependencyName, new byte[] { 1, 2, 3, 4 });

        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var first, out var firstErrors),
            string.Join("; ", firstErrors));
        string firstPackSha256 = first!.PackSha256;
        string assemblySha256 = first.AssemblySha256;
        CompatPackDependencyInfo recorded = Assert.Single(
            first.Dependencies, dependency => dependency.FileName == dependencyName);
        // 诊断只记 basename + SHA256（不泄漏完整路径）。
        Assert.DoesNotContain(Path.DirectorySeparatorChar, recorded.FileName);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3, 4 }))
                .ToLowerInvariant(),
            recorded.Sha256);
        Assert.Matches("^[0-9a-f]{64}$", firstPackSha256);
        first.Unload();

        directory.WriteAssembly(dependencyName, new byte[] { 1, 2, 3, 5 });
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, RealContext(), out var second, out var secondErrors),
            string.Join("; ", secondErrors));
        Assert.Equal(assemblySha256, second!.AssemblySha256);
        Assert.NotEqual(firstPackSha256, second.PackSha256);
        // 独立推演：主 DLL + 内嵌清单 + DummyDependency 字节的规范哈希。
        byte[] mainBytes = File.ReadAllBytes(directory.MainAssemblyPath);
        Assert.Equal(
            ExpectedPackSha256(mainBytes, (dependencyName, new byte[] { 1, 2, 3, 5 })),
            second.PackSha256);
        second.Unload();
    }

    [Fact]
    public void TryLoad_SymlinkOutsidePackDirectory_IsRejected()
    {
        // 依赖闭包读的是"包目录内的 dll"：指向目录外的 symlink 必须拒载，否则部署目录
        // 里的一个链接就能把任意宿主/系统文件卷进包哈希与装载路径。Windows 创建符号链接
        // 需要开发者模式/特权；创建失败时按环境能力跳过本用例（不是通过）。
        using var directory = new IsolatedPackDirectory(HelloPackPath);
        string outsideDirectory = Path.Combine(
            Path.GetTempPath(), "compat-pack-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDirectory);
        string outsideDll = Path.Combine(outsideDirectory, "LinkedDependency.dll");
        File.WriteAllBytes(outsideDll, new byte[] { 0x4d, 0x5a });
        try
        {
            string linkPath = Path.Combine(directory.DirectoryPath, "LinkedDependency.dll");
            try
            {
                File.CreateSymbolicLink(linkPath, outsideDll);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                return;
            }

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
        Assert.Contains(errors, error => error.Contains("ReflectionTypeLoadException")
            || error.Contains("CompatPackTypeLoadDependency"));
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
