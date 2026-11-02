using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// DOD-3 hello-world 端到端（Core 层）：显式启用（加载 HelloCompatPackFixture）→ 表面变化 →
/// plan 哈希变化 → 禁用（空包集）后与纯 v24 基线逐字节等价（同实例同哈希）。
/// T5 起组装器只读句柄内的 ValidatedPackDelta（不再回放贡献对象）；T6 起组装先经
/// CompatPackCompositionResolver（依赖/冲突/override/双形态闭包）再只读其 OrderedPacks 与
/// SymbolStates——本类同时钉住"resolver 结果落地"与"贡献 Apply 恰好一次"两条不变量。
/// 引擎侧投影接线（handler/变体生效于解析器）不在本层，属宿主接线增量。
/// </summary>
[Collection("CompatPack")]
public class CompatPackAssemblyTests
{
    static string PackPath => typeof(HelloCompatPackFixture.HelloCompatPack).Assembly.Location;

    static CompatPackValidationContext Context(
        IReadOnlyDictionary<string, CapabilityDescriptor>? capabilities = null,
        string? baselineProfileId = null)
        => CompatPackCompositionTestData.Context(capabilities, baselineProfileId: baselineProfileId);

    static CompatibilityPlan Baseline() => BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");

    [Fact]
    public void EndToEnd_EnabledPack_ChangesSurfacePlanAndHash()
    {
        var baseline = Baseline();
        using var directory = new IsolatedPackDirectory(PackPath);
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, Context(), out var handle, out var loadErrors),
            string.Join("; ", loadErrors));
        try
        {
            Assert.True(CompatPackPlanAssembler.TryAssemble(
                    baseline, new[] { handle! }, Context(), out var assembled, out var assemblyErrors),
                string.Join("; ", assemblyErrors));

            // 表面变化：包注册 SETANIMETIMER/SQL_CONNECT、隐藏 CALLSHARP/EXISTVAR。
            Assert.True(assembled.Dialect.TryGetInstruction("SETANIMETIMER", out var addedInstruction));
            Assert.Equal("test.hello-pack", addedInstruction.ModuleId);
            Assert.False(assembled.Dialect.TryGetInstruction("CALLSHARP", out _));
            Assert.True(assembled.Dialect.TryGetFunction("SQL_CONNECT", out var addedFunction));
            Assert.Equal("Int64", addedFunction.ReturnType);
            Assert.False(assembled.Dialect.TryGetFunction("EXISTVAR", out _));
            // 函数名镜像声明进指令面：引擎把可见表达式函数投影为 METHOD 指令进解析器指令
            // 注册表，会话校验要求指令侧声明（App 路径探针曾抛 "Legacy instruction
            // 'SQL_CONNECT' is not declared by the compatibility plan"，此断言钉住该回归）。
            Assert.True(assembled.Dialect.TryGetInstruction("SQL_CONNECT", out var mirroredInstruction));
            Assert.Equal("test.hello-pack", mirroredInstruction.ModuleId);

            // 计数：v24 基线 561/266；指令 +SETANIMETIMER +镜像SQL_CONNECT -CALLSHARP
            // -镜像EXISTVAR（双形态闭包：隐藏函数 EXISTVAR 必须同步隐藏同名 METHOD 指令，
            // 否则计划多声明一个运行时缺席的指令名）→ 净 0；函数 +SQL_CONNECT -EXISTVAR 平衡。
            Assert.Equal(baseline.Dialect.Instructions.Count, assembled.Dialect.Instructions.Count);
            Assert.Equal(baseline.Dialect.Functions.Count, assembled.Dialect.Functions.Count);
            // 双形态闭包的显式断言：ABS 这类 METHOD 投影两侧都必须留在基线里（未被误删），
            // 而隐藏的 EXISTVAR 两侧都必须缺席。
            Assert.True(assembled.Dialect.TryGetInstruction("ABS", out _) && assembled.Dialect.TryGetFunction("ABS", out _));
            Assert.False(assembled.Dialect.TryGetInstruction("EXISTVAR", out _));
            Assert.False(assembled.Dialect.TryGetFunction("EXISTVAR", out _));

            // capability 账本并入（manifest 2 + capability 贡献 1；策略贡献 v1 未接线、
            // 加载即拒载，其绑定 id 不得再进账本——DoesNotContain 钉住该回归）。
            Assert.Contains("input.pointer-button.v1", assembled.CapabilityIds);
            Assert.Contains("startup.continue-after-fault.v1", assembled.CapabilityIds);
            Assert.Contains("math.times-clamp.v1", assembled.CapabilityIds);
            Assert.DoesNotContain("parse.diagnostics.v1", assembled.CapabilityIds);

            // 模块闭包 = 基线 + 包合成快照。
            Assert.Equal(baseline.Dialect.Modules.Count + 1, assembled.Dialect.Modules.Count);
            Assert.Contains(assembled.Dialect.Modules, module => module.ModuleId == "test.hello-pack");

            // 哈希变化：启用包的会话计划不同于纯 v24。
            Assert.NotEqual(baseline.CanonicalHash, assembled.CanonicalHash);
            Assert.NotEqual(baseline.Dialect.CanonicalHash, assembled.Dialect.CanonicalHash);
            Assert.Equal(baseline.ProfileId, assembled.ProfileId);
        }
        finally
        {
            handle!.Unload();
        }
    }

    [Fact]
    public void EndToEnd_SamePackContent_SameAssembledHash()
    {
        // 同包内容同哈希（PackSha256 进哈希链；诊断可复现）。
        using var directory = new IsolatedPackDirectory(PackPath);
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, Context(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, Context(), out var second, out _));
        var baseline = Baseline();
        try
        {
            Assert.True(CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { first! }, Context(), out var planA, out _));
            Assert.True(CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { second! }, Context(), out var planB, out _));
            Assert.Equal(planA!.CanonicalHash, planB!.CanonicalHash);
        }
        finally
        {
            first!.Unload();
            second!.Unload();
        }
    }

    [Fact]
    public void EndToEnd_DisabledPacks_ReturnsBaselineInstanceByteEqual()
    {
        // 禁用任何包：组装器不被需要，空集调用原样返回基线——同实例、同哈希，
        // 「未启用任何包的会话 = 纯 v24」的不变量入口。
        var baseline = Baseline();
        Assert.True(CompatPackPlanAssembler.TryAssemble(
            baseline, Array.Empty<CompatPackHandle>(), Context(), out var assembled, out var errors));
        Assert.Empty(errors);
        Assert.Same(baseline, assembled);
        Assert.Equal(baseline.CanonicalHash, assembled.CanonicalHash);
    }

    [Fact]
    public void EndToEnd_EnableThenDisable_PlanEqualsPureV24()
    {
        // 完整生命周期：启用 → 哈希变化 → 卸载 → 重新组装空集 → 回到基线哈希。
        var baseline = Baseline();
        using var directory = new IsolatedPackDirectory(PackPath);
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, Context(), out var handle, out _));
        Assert.True(CompatPackPlanAssembler.TryAssemble(
            baseline, new[] { handle! }, Context(), out var enabled, out _));
        Assert.NotEqual(baseline.CanonicalHash, enabled!.CanonicalHash);

        handle!.Unload();
        Assert.True(CompatPackPlanAssembler.TryAssemble(
            baseline, Array.Empty<CompatPackHandle>(), Context(), out var disabled, out _));
        Assert.Equal(baseline.CanonicalHash, disabled!.CanonicalHash);
    }

    [Fact]
    public void Assemble_DuplicatePackIdAcrossHandles_Rejects()
    {
        // 同一包被启用两次（同 packId）：组合图在建立 packId → handle 映射时即拒载。
        using var directory = new IsolatedPackDirectory(PackPath);
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, Context(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, Context(), out var second, out _));
        try
        {
            Assert.False(CompatPackPlanAssembler.TryAssemble(
                Baseline(), new[] { first!, second! }, Context(), out var assembled, out var errors));
            Assert.Null(assembled);
            Assert.Contains(errors!, e => e.Contains("packId 重复"));
        }
        finally
        {
            first!.Unload();
            second!.Unload();
        }
    }

    [Fact]
    public void Assemble_CrossPackSameInstruction_RejectsWithoutOverride()
    {
        // 不同包注册同名指令 = 不同最终归属，必须由显式 override 唯一取胜，否则拒载。
        var a = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('a'),
            surfaceStates: new[] { PresentInstruction("SHARED_SYMBOL") });
        var b = ManualHandle("test.pack-b", CompatPackCompositionTestData.ShaOf('b'),
            surfaceStates: new[] { PresentInstruction("SHARED_SYMBOL") });

        Assert.False(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { a, b }, Context(), out var assembled, out var errors));
        Assert.Null(assembled);
        Assert.Contains(errors!, e => e.Contains("被多个包注册"));
    }

    [Fact]
    public void Assemble_ExplicitOverride_SelectsWinnerInPlan()
    {
        // resolver 的 override 仲裁结果必须落到 plan（指令 descriptor 的 ModuleId 归属）。
        var a = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('a'),
            surfaceStates: new[] { PresentInstruction("SHARED_SYMBOL") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED_SYMBOL", 10) });
        var b = ManualHandle("test.pack-b", CompatPackCompositionTestData.ShaOf('b'),
            surfaceStates: new[] { PresentInstruction("SHARED_SYMBOL") },
            overrides: new[] { new CompatPackOverride("instruction:SHARED_SYMBOL", 20) });

        Assert.True(CompatPackPlanAssembler.TryAssemble(
                Baseline(), new[] { a, b }, Context(), out var assembled, out var errors),
            string.Join("; ", errors));
        Assert.True(assembled!.Dialect.TryGetInstruction("SHARED_SYMBOL", out var descriptor));
        Assert.Equal("test.pack-b", descriptor.ModuleId);
    }

    [Fact]
    public void Assemble_DoesNotExecuteSurfaceContributionAgain()
    {
        // 校验期回放一次并冻结 delta；组装期只读 resolver 结果，绝不第二次执行包贡献。
        var contribution = new CountingSurfaceContribution();
        var manifest = CompatPackCompositionTestData.Manifest("test.once");
        var pack = new CountingPack(manifest, contribution);
        string packSha256 = new string('b', 64);

        Assert.True(
            CompatPackLoader.ValidatePackEntry(
                pack, manifest, Context(), out _, out var validationErrors, out _, out var delta, packSha256: packSha256),
            string.Join("; ", validationErrors));
        Assert.Equal(1, contribution.ApplyCount);
        Assert.NotNull(delta);

        var handle = new CompatPackHandle(
            manifest, pack, "Z:/test.once.dll", packSha256, delta!,
            Array.Empty<CompatPackDependencyInfo>(),
            new CompatPackLoadContext("Z:/test.once.dll", new Dictionary<string, byte[]>()));
        try
        {
            Assert.True(CompatPackPlanAssembler.TryAssemble(
                    Baseline(), new[] { handle }, Context(), out var assembled, out var assemblyErrors),
                string.Join("; ", assemblyErrors));
            Assert.True(assembled!.Dialect.TryGetInstruction("SETANIMETIMER", out _));
            Assert.Equal(1, contribution.ApplyCount);
        }
        finally
        {
            handle.Unload();
        }
    }

    [Fact]
    public void Assemble_NullHandle_Rejects()
    {
        Assert.False(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { (CompatPackHandle)null! }, Context(), out var assembled, out var errors));
        Assert.Null(assembled);
        Assert.Contains(errors!, e => e.Contains("null 元素"));
    }

    // —— 以下用手工句柄（internal 构造点，经 InternalsVisibleTo）直接钉哈希组成、
    //    冻结差量账本与跨包对账，不依赖程序集加载路径 ——

    static CompatPackHandle ManualHandle(
        string packId,
        string packSha256,
        IReadOnlyDictionary<string, string>? variantSelections = null,
        IReadOnlyList<CompatSymbolState>? surfaceStates = null,
        IReadOnlyList<string>? capabilityIds = null,
        string? saveProfileId = null,
        IEnumerable<CompatPackDependency>? requires = null,
        IEnumerable<string>? conflicts = null,
        IEnumerable<CompatPackOverride>? overrides = null)
        => CompatPackCompositionTestData.Handle(
            packId, packSha256, surfaceStates, capabilityIds, variantSelections,
            requires: requires, conflicts: conflicts, overrides: overrides, saveProfileId: saveProfileId);

    static CompatSymbolState PresentInstruction(string name) => new(
        new CompatSymbolKey(CompatSymbolKind.Instruction, name),
        CompatSymbolStateKind.Present, "test.pack", null, null);

    static CompatSymbolState HiddenInstruction(string name) => new(
        new CompatSymbolKey(CompatSymbolKind.Instruction, name),
        CompatSymbolStateKind.Hidden, "test.pack", null, null);

    [Fact]
    public void VariantSelection_ConflictingValuesAcrossPacks_Rejects()
    {
        var a = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('a'),
            new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" });
        var b = ManualHandle("test.pack-b", CompatPackCompositionTestData.ShaOf('b'),
            new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:v24" });

        Assert.False(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { a, b }, Context(), out var assembled, out var errors));
        Assert.Null(assembled);
        Assert.Contains(errors!, e => e.Contains("变体选择跨包冲突"));
    }

    [Fact]
    public void VariantSelection_SameValue_AcceptsBothOrders_SameHash()
    {
        // 同键同值幂等；且两个包按不同顺序组装必须得到同一哈希（顺序无关不变量）。
        var a = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('a'),
            new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" });
        var b = ManualHandle("test.pack-b", CompatPackCompositionTestData.ShaOf('b'),
            new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" });

        Assert.True(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { a, b }, Context(), out var ab, out var errorsAb), string.Join("; ", errorsAb));
        Assert.True(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { b, a }, Context(), out var ba, out var errorsBa), string.Join("; ", errorsBa));
        Assert.Equal(ab!.CanonicalHash, ba!.CanonicalHash);
    }

    [Fact]
    public void Hash_PackSha256_IsFirstClassInput()
    {
        // 同 packId 不同包字节哈希 → 组装哈希不同（PackSha256 入哈希链的哨兵）。
        var x = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('a'),
            capabilityIds: new[] { "math.times-clamp.v1" });
        var y = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('b'),
            capabilityIds: new[] { "math.times-clamp.v1" });

        Assert.True(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { x }, Context(), out var planX, out _));
        Assert.True(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { y }, Context(), out var planY, out _));
        Assert.NotEqual(planX!.CanonicalHash, planY!.CanonicalHash);
    }

    [Fact]
    public void Hash_VariantSelectionLine_IsInput()
    {
        // 变体选择行入哈希的哨兵：同包字节/同 capability、有无变体选择 → 哈希不同。
        var plain = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('a'),
            capabilityIds: new[] { "math.times-clamp.v1" });
        var withSelection = ManualHandle("test.pack-a", CompatPackCompositionTestData.ShaOf('a'),
            new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" },
            capabilityIds: new[] { "math.times-clamp.v1" });

        Assert.True(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { plain }, Context(), out var planPlain, out _));
        Assert.True(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { withSelection }, Context(), out var planSelected, out _));
        Assert.NotEqual(planPlain!.CanonicalHash, planSelected!.CanonicalHash);
    }

    [Fact]
    public void Assemble_CompositionLevelCapabilityClosure_EntersPlan()
    {
        // 组装器只并集 resolver 的组合级闭包（pack 差量已冻结 per-pack closure）：合成目录
        // root.v1 requires base.v1 → plan 账本必须同时含两者，证明闭包在组合层再次展开。
        var catalog = EngineCapabilityCatalog.ComposeCatalog(new[]
        {
            CompatPackCompositionTestData.Wired("root.v1", requires: new[] { "base.v1" }),
            CompatPackCompositionTestData.Wired("base.v1"),
        });
        var handle = ManualHandle("test.closure", CompatPackCompositionTestData.ShaOf('c'),
            surfaceStates: new[] { PresentInstruction("TEST_CLOSURE") },
            capabilityIds: new[] { "root.v1" });

        Assert.True(CompatPackPlanAssembler.TryAssemble(
                Baseline(), new[] { handle }, Context(catalog), out var assembled, out var errors),
            string.Join("; ", errors));
        Assert.Contains("root.v1", assembled!.CapabilityIds);
        Assert.Contains("base.v1", assembled.CapabilityIds);
    }

    // —— hide 基线不对称（P2-G）：v24 清单内、当前 profile 基线缺席的隐藏名 = 良性 no-op ——

    [Fact]
    public void Assemble_HideNameAbsentFromSessionBaseline_IsBenignNoOp()
    {
        // v18 会话基线不含 v24 后增指令 SETBGIMAGE（v24 清单内合法隐藏目标）；
        // 组装不得以"规则漂移"整体拒载——归类良性 no-op（隐藏差量仍进哈希，包意图可复现）。
        CompatibilityPlan v18 = BuiltInDialectCatalog.CreateLegacySessionPlan("v18");
        Assert.False(v18.Dialect.TryGetInstruction("SETBGIMAGE", out _), "前置：SETBGIMAGE 不在 v18 基线。");

        var handle = ManualHandle("test.hide-noop", CompatPackCompositionTestData.ShaOf('c'),
            surfaceStates: new[] { HiddenInstruction("SETBGIMAGE") });
        // context 必须与基线同源（profile 身份一致）：v18 基线配 v18 标签，v24 规则事实
        // 仍由测试夹具提供（隐藏目标合法性由 v24 清单对账）。
        Assert.True(CompatPackPlanAssembler.TryAssemble(
                v18, new[] { handle }, Context(baselineProfileId: "v18"), out var assembled, out var errors),
            string.Join("; ", errors));
        Assert.False(assembled!.Dialect.TryGetInstruction("SETBGIMAGE", out _));
        // 表面与 v18 基线一致，但哈希计入隐藏差量（区别于未声明该隐藏的同字节包）。
        Assert.Equal(v18.Dialect.Instructions.Count, assembled.Dialect.Instructions.Count);
        Assert.NotEqual(v18.CanonicalHash, assembled.CanonicalHash);
    }

    [Fact]
    public void Assemble_ContextBaselineProfileMismatch_Rejects()
    {
        // context 必须由同一会话基线构造：v18 基线 + 默认 v24pure 上下文会让 resolver 用
        // 错误的基线事实裁决（profile 身份漂移属接线错误），组装期 fail-closed。
        CompatibilityPlan v18 = BuiltInDialectCatalog.CreateLegacySessionPlan("v18");
        var handle = ManualHandle("test.profile", CompatPackCompositionTestData.ShaOf('p'),
            capabilityIds: new[] { "math.times-clamp.v1" });

        Assert.False(CompatPackPlanAssembler.TryAssemble(
            v18, new[] { handle }, Context(), out var assembled, out var errors));

        Assert.Null(assembled);
        Assert.Contains(errors!, error => error.Contains("profile")
            && error.Contains("v18") && error.Contains("v24pure"));
    }

    [Fact]
    public void Assemble_PackModuleIdsVariantsAndSaveProfile_AreCarriedInPlan()
    {
        var handle = ManualHandle(
            "test.meta",
            CompatPackCompositionTestData.ShaOf('m'),
            new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" },
            saveProfileId: "gemuera.pack.meta");

        Assert.True(CompatPackPlanAssembler.TryAssemble(
                Baseline(), new[] { handle }, Context(), out var assembled, out var errors),
            string.Join("; ", errors));

        Assert.Equal(new[] { "test.meta" }, assembled!.Dialect.PackModuleIds);
        Assert.Equal("builtin:snake", assembled.Dialect.VariantSelections["SETBGIMAGE"]);
        Assert.Equal("gemuera.pack.meta", assembled.SaveProfileId);
    }

    [Fact]
    public void Assemble_ConflictingSaveProfiles_Rejects()
    {
        var a = ManualHandle("test.save-a", CompatPackCompositionTestData.ShaOf('a'),
            capabilityIds: new[] { "math.times-clamp.v1" }, saveProfileId: "gemuera.save.a");
        var b = ManualHandle("test.save-b", CompatPackCompositionTestData.ShaOf('b'),
            capabilityIds: new[] { "math.times-clamp.v1" }, saveProfileId: "gemuera.save.b");

        Assert.False(CompatPackPlanAssembler.TryAssemble(
            Baseline(), new[] { a, b }, Context(), out var assembled, out var errors));
        Assert.Null(assembled);
        Assert.Contains(errors!, e => e.Contains("saveProfileId 冲突"));
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

    sealed class CountingPack : ICompatPack
    {
        public CountingPack(CompatPackManifest manifest, params ICompatPackContribution[] contributions)
        {
            Manifest = manifest;
            Contributions = contributions;
        }

        public CompatPackManifest Manifest { get; }
        public IReadOnlyList<ICompatPackContribution> Contributions { get; }
    }
}
