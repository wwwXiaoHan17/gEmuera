using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// DOD-3 hello-world 端到端（Core 层）：显式启用（加载测试程序集兼任的包）→ 表面变化 →
/// plan 哈希变化 → 禁用（空包集）后与纯 v24 基线逐字节等价（同实例同哈希）。
/// 引擎侧投影接线（handler/变体生效于解析器）不在本层，属宿主接线增量。
/// </summary>
public class CompatPackAssemblyTests
{
    static string PackPath => typeof(CompatPackAssemblyTests).Assembly.Location;

    static CompatPackValidationContext Context() => new(
        1,
        new HashSet<string>(StringComparer.Ordinal)
        {
            "input.pointer-button.v1", "startup.continue-after-fault.v1",
            "markup.div-v2.v1", "parse.diagnostics.v1",
        },
        new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        new HashSet<string>(LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
        // 引擎 handler 全集（与宿主 BuildValidationContext 同口径：六 profile 清单并集），
        // 端到端证明真实夹具包（注册 SETANIMETIMER/SQL_CONNECT）穿过 handler 对账。
        knownInstructionHandlers: EngineInstructionHandlers(),
        knownFunctionHandlers: EngineFunctionHandlers());

    static HashSet<string> EngineInstructionHandlers()
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        union.UnionWith(LegacyDialectInventories.V24InstructionNames);
        union.UnionWith(LegacyDialectInventories.SnakeDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.EraFlDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.EraBlueDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.MegatenDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.V18InstructionNames);
        return union;
    }

    static HashSet<string> EngineFunctionHandlers()
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        union.UnionWith(LegacyDialectInventories.V24Functions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.SnakeDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.EraFlDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.EraBlueDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.MegatenDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.V18Functions.Select(entry => entry.Name));
        return union;
    }

    static CompatibilityPlan Baseline() => BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");

    [Fact]
    public void EndToEnd_EnabledPack_ChangesSurfacePlanAndHash()
    {
        var baseline = Baseline();
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var handle, out var loadErrors),
            string.Join("; ", loadErrors));
        try
        {
            Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { handle! }, out var assembled, out var assemblyErrors),
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

            // 计数：v24 基线 561/266；指令 +SETANIMETIMER +镜像SQL_CONNECT -CALLSHARP 净+1，
            // 函数 +SQL_CONNECT -EXISTVAR 平衡。
            Assert.Equal(baseline.Dialect.Instructions.Count + 1, assembled.Dialect.Instructions.Count);
            Assert.Equal(baseline.Dialect.Functions.Count, assembled.Dialect.Functions.Count);

            // capability 账本并入（manifest 2 + capability 贡献 1；策略贡献 v1 未接线、
            // 加载即拒载，其绑定 id 不得再进账本——DoesNotContain 钉住该回归）。
            Assert.Contains("input.pointer-button.v1", assembled.CapabilityIds);
            Assert.Contains("markup.div-v2.v1", assembled.CapabilityIds);
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
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var second, out _));
        var baseline = Baseline();
        try
        {
            Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { first! }, out var planA, out _));
            Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { second! }, out var planB, out _));
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
        Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, Array.Empty<CompatPackHandle>(), out var assembled, out var errors));
        Assert.Empty(errors);
        Assert.Same(baseline, assembled);
        Assert.Equal(baseline.CanonicalHash, assembled.CanonicalHash);
    }

    [Fact]
    public void EndToEnd_EnableThenDisable_PlanEqualsPureV24()
    {
        // 完整生命周期：启用 → 哈希变化 → 卸载 → 重新组装空集 → 回到基线哈希。
        var baseline = Baseline();
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var handle, out _));
        Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, new[] { handle! }, out var enabled, out _));
        Assert.NotEqual(baseline.CanonicalHash, enabled!.CanonicalHash);

        handle!.Unload();
        Assert.True(CompatPackPlanAssembler.TryAssemble(baseline, Array.Empty<CompatPackHandle>(), out var disabled, out _));
        Assert.Equal(baseline.CanonicalHash, disabled!.CanonicalHash);
    }

    [Fact]
    public void Assemble_DuplicateRegistrationAcrossPacks_Rejects()
    {
        // 两个同包句柄（模拟两包注册同名指令）：跨包对账拒载。
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var first, out _));
        Assert.True(CompatPackLoader.TryLoad(PackPath, Context(), out var second, out _));
        try
        {
            Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { first!, second! }, out var assembled, out var errors));
            Assert.Null(assembled);
            Assert.Contains(errors!, e => e.Contains("被多个包注册"));
        }
        finally
        {
            first!.Unload();
            second!.Unload();
        }
    }

    [Fact]
    public void Assemble_NullHandle_Rejects()
    {
        Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { (CompatPackHandle)null! }, out var assembled, out var errors));
        Assert.Null(assembled);
        Assert.Contains(errors!, e => e.Contains("null 元素"));
    }

    // —— 以下用手工句柄（internal 构造点，经 InternalsVisibleTo）直接钉哈希组成与
    //    跨包变体对账，不依赖程序集加载路径 ——

    static CompatPackHandle ManualHandle(
        string packId,
        string packSha256,
        IReadOnlyDictionary<string, string>? variantSelections = null,
        IReadOnlyList<ISurfaceContribution>? surface = null,
        IReadOnlyList<ICapabilityContribution>? capabilities = null,
        IReadOnlyList<IPolicyContribution>? policies = null)
    {
        // CompatPackManifest 的构造器在 Emuera 契约程序集内为 internal（测试程序集无
        // InternalsVisibleTo），经 public TryParse 构造；变体选拼进 JSON。
        string variantsJson = variantSelections is null || variantSelections.Count == 0
            ? ""
            : ",\"variantSelections\":{" + string.Join(",", variantSelections.Select(pair =>
                  "\"" + pair.Key + "\":\"" + pair.Value + "\"")) + "}";
        string json = "{\"packId\":\"" + packId + "\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1" + variantsJson + "}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        return new CompatPackHandle(
            manifest, null!, "Z:/manual/" + packId + ".dll", packSha256, packSha256,
            surface ?? Array.Empty<ISurfaceContribution>(), capabilities ?? Array.Empty<ICapabilityContribution>(),
            Array.Empty<IInstructionVariantContribution>(), policies ?? Array.Empty<IPolicyContribution>(),
            new CompatPackLoadContext("Z:/manual/" + packId + ".dll"));
    }

    static string ShaOf(char fill) => new(fill, 64);

    [Fact]
    public void VariantSelection_ConflictingValuesAcrossPacks_Rejects()
    {
        var a = ManualHandle("test.pack-a", ShaOf('a'), new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" });
        var b = ManualHandle("test.pack-b", ShaOf('b'), new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:v24" });

        Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { a, b }, out var assembled, out var errors));
        Assert.Null(assembled);
        Assert.Contains(errors!, e => e.Contains("变体选择跨包冲突"));
    }

    [Fact]
    public void VariantSelection_SameValue_AcceptsBothOrders_SameHash()
    {
        // 同键同值幂等；且两个包按不同顺序组装必须得到同一哈希（顺序无关不变量）。
        var a = ManualHandle("test.pack-a", ShaOf('a'), new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" });
        var b = ManualHandle("test.pack-b", ShaOf('b'), new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" });

        Assert.True(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { a, b }, out var ab, out var errorsAb), string.Join("; ", errorsAb));
        Assert.True(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { b, a }, out var ba, out var errorsBa), string.Join("; ", errorsBa));
        Assert.Equal(ab!.CanonicalHash, ba!.CanonicalHash);
    }

    [Fact]
    public void Hash_PackSha256_IsFirstClassInput()
    {
        // 同 packId 不同包字节哈希 → 组装哈希不同（PackSha256 入哈希链的哨兵）。
        var x = ManualHandle("test.pack-a", ShaOf('a'));
        var y = ManualHandle("test.pack-a", ShaOf('b'));

        Assert.True(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { x }, out var planX, out _));
        Assert.True(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { y }, out var planY, out _));
        Assert.NotEqual(planX!.CanonicalHash, planY!.CanonicalHash);
    }

    [Fact]
    public void Hash_VariantSelectionLine_IsInput()
    {
        // 变体选择行入哈希的哨兵：同包字节、有无变体选择 → 哈希不同。
        var plain = ManualHandle("test.pack-a", ShaOf('a'));
        var withSelection = ManualHandle("test.pack-a", ShaOf('a'), new Dictionary<string, string> { ["SETBGIMAGE"] = "builtin:snake" });

        Assert.True(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { plain }, out var planPlain, out _));
        Assert.True(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { withSelection }, out var planSelected, out _));
        Assert.NotEqual(planPlain!.CanonicalHash, planSelected!.CanonicalHash);
    }

    // —— hide 基线不对称（P2-G）：v24 清单内、当前 profile 基线缺席的隐藏名 = 良性 no-op ——

    sealed class HideSetBgImageSurface : ISurfaceContribution
    {
        public string ContributionId => "test.hide-noop.surface";
        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
            => instructions.HideInstruction("SETBGIMAGE");
    }

    [Fact]
    public void Assemble_HideNameAbsentFromSessionBaseline_IsBenignNoOp()
    {
        // v18 会话基线不含 v24 后增指令 SETBGIMAGE（v24 清单内合法隐藏目标）；
        // 组装不得以"规则漂移"整体拒载——归类良性 no-op（隐藏差量仍进哈希，包意图可复现）。
        CompatibilityPlan v18 = BuiltInDialectCatalog.CreateLegacySessionPlan("v18");
        Assert.False(v18.Dialect.TryGetInstruction("SETBGIMAGE", out _), "前置：SETBGIMAGE 不在 v18 基线。");

        var handle = ManualHandle("test.hide-noop", ShaOf('c'), surface: new[] { new HideSetBgImageSurface() });
        Assert.True(CompatPackPlanAssembler.TryAssemble(v18, new[] { handle }, out var assembled, out var errors),
            string.Join("; ", errors));
        Assert.False(assembled!.Dialect.TryGetInstruction("SETBGIMAGE", out _));
        // 表面与 v18 基线一致，但哈希计入隐藏差量（区别于未声明该隐藏的同字节包）。
        Assert.Equal(v18.Dialect.Instructions.Count, assembled.Dialect.Instructions.Count);
        Assert.NotEqual(v18.CanonicalHash, assembled.CanonicalHash);
    }

    // —— 组装段二轮回放异常安全（P1-2a）：校验期首轮回放通过、组装期第二轮抛异常的
    //    非幂等贡献——TryAssemble 自身契约必须是"返回 false + 错误清单"而非抛异常，
    //    不依赖宿主 ConfigureForLaunch 的顶层 catch ——
    //    经手工句柄（internal 构造点，InternalsVisibleTo）构造，不经程序集加载路径。

    sealed class ThrowingSurfaceContribution : ISurfaceContribution
    {
        public string ContributionId => "test.replay-throw.surface";
        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
            => throw new InvalidOperationException("surface-replay-boom");
    }

    sealed class ThrowingCapabilityContribution : ICapabilityContribution
    {
        public string ContributionId => "test.replay-throw.capability";
        public IReadOnlyList<string> CapabilityIds => throw new InvalidOperationException("capability-replay-boom");
    }

    sealed class ThrowingPolicyContribution : IPolicyContribution
    {
        public string ContributionId => "test.replay-throw.policy";
        public IReadOnlyList<EnginePolicyBinding> Policies => throw new InvalidOperationException("policy-replay-boom");
    }

    [Fact]
    public void Assemble_SurfaceApplyThrows_RejectsWithoutExceptionEscaping()
    {
        var handle = ManualHandle("test.replay-throw", ShaOf('d'), surface: new[] { new ThrowingSurfaceContribution() });

        var exception = Record.Exception(() =>
        {
            Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { handle }, out var assembled, out var errors));
            Assert.Null(assembled);
            // 文案三要素：包 id + 贡献 id + 异常消息。
            Assert.Contains(errors!, e => e.Contains("test.replay-throw")
                && e.Contains("test.replay-throw.surface") && e.Contains("surface-replay-boom"));
        });
        Assert.Null(exception);
    }

    [Fact]
    public void Assemble_CapabilityGetterThrows_RejectsWithoutExceptionEscaping()
    {
        var handle = ManualHandle("test.replay-throw", ShaOf('d'), capabilities: new[] { new ThrowingCapabilityContribution() });

        var exception = Record.Exception(() =>
        {
            Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { handle }, out var assembled, out var errors));
            Assert.Null(assembled);
            Assert.Contains(errors!, e => e.Contains("test.replay-throw")
                && e.Contains("test.replay-throw.capability") && e.Contains("capability-replay-boom"));
        });
        Assert.Null(exception);
    }

    [Fact]
    public void Assemble_PolicyGetterThrows_RejectsWithoutExceptionEscaping()
    {
        // 策略贡献经加载器路径已不可能到达组装段（v1 未接线、加载即拒载），本用例钉住
        // 手工句柄/二轮回放路径的防御性契约（异常仍不得逃逸）。
        var handle = ManualHandle("test.replay-throw", ShaOf('d'), policies: new[] { new ThrowingPolicyContribution() });

        var exception = Record.Exception(() =>
        {
            Assert.False(CompatPackPlanAssembler.TryAssemble(Baseline(), new[] { handle }, out var assembled, out var errors));
            Assert.Null(assembled);
            Assert.Contains(errors!, e => e.Contains("test.replay-throw")
                && e.Contains("test.replay-throw.policy") && e.Contains("policy-replay-boom"));
        });
        Assert.Null(exception);
    }
}
