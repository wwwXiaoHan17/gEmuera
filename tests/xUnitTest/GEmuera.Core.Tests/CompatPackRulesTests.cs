using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 语义规则验证器的直测（不经程序集加载）：结构类贡献规则（重复贡献 id/重复绑定/
/// 变体选择冲突/非 builtin 选择/Apply 抛异常）用手工构造的清单与贡献直接驱动，
/// 补齐加载器矩阵覆盖不到的分支。
/// </summary>
public class CompatPackRulesTests
{
    const string ManifestJson =
        "{\"packId\":\"test.rules\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
        + "\"capabilities\":[\"parse.diagnostics.v1\"],"
        + "\"variantSelections\":{\"SETBGIMAGE\":\"builtin:snake\"}}";

    static CompatPackManifest Manifest() =>
        CompatPackManifest.TryParse(ManifestJson, out var manifest, out var errors)
            ? manifest!
            : throw new InvalidOperationException(string.Join("; ", errors));

    static CompatPackValidationContext Context() => new(
        1,
        new HashSet<string>(StringComparer.Ordinal) { "parse.diagnostics.v1", "markup.div-v2.v1" },
        new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        new HashSet<string>(StringComparer.Ordinal) { "CALLSHARP", "SETBGIMAGE", "PRINT" },
        new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" });

    sealed class StubSurface : ISurfaceContribution
    {
        public string ContributionId => "stub.surface";
        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
            => throw new InvalidOperationException("boom");
    }

    sealed class StubSurfaceDo : ISurfaceContribution
    {
        public string ContributionId => "stub.surface.do";
        public Action<IInstructionSurfaceRegistry, IFunctionSurfaceRegistry> Do { get; init; } = (_, _) => { };
        public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions) => Do(instructions, functions);
    }

    sealed class StubVariant : IInstructionVariantContribution
    {
        public string ContributionId { get; init; } = "stub.variant";
        public IReadOnlyList<InstructionVariantBinding> Bindings { get; init; } = Array.Empty<InstructionVariantBinding>();
    }

    sealed class StubCapability : ICapabilityContribution
    {
        public string ContributionId { get; init; } = "stub.capability";
        public IReadOnlyList<string>? CapabilityIds { get; init; } = Array.Empty<string>();
    }

    sealed class StubPolicy : IPolicyContribution
    {
        public string ContributionId { get; init; } = "stub.policy";
        public IReadOnlyList<EnginePolicyBinding>? Policies { get; init; } = Array.Empty<EnginePolicyBinding>();
    }

    [Fact]
    public void Validate_PackIdMatchesReservedModuleName_Rejects()
    {
        // packId 撞内置方言模块保留名（设计文档示例 packId 即 game.erafl 形态）必须在校验段拒载，
        // 否则组装后 Compose 白名单校验会以未捕获异常炸掉会话绑定（违反降级不变量）。
        string json = "{\"packId\":\"game.erafl\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";
        var manifest = CompatPackManifest.TryParse(json, out var m, out _)
            ? m! : throw new InvalidOperationException();
        var context = new CompatPackValidationContext(
            1,
            new HashSet<string>(StringComparer.Ordinal) { "parse.diagnostics.v1" },
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(StringComparer.Ordinal) { "CALLSHARP" },
            new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" },
            new HashSet<string>(StringComparer.Ordinal) { "gemuera.v24", "game.erafl" });
        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), context, out var errors));
        Assert.Contains(errors, e => e.Contains("保留名冲突"));
    }

    [Fact]
    public void Validate_NullContributionElement_Rejects()
    {
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { null! }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("null 元素"));
    }

    [Fact]
    public void Validate_NullCapabilityIds_Rejects()
    {
        var stub = new StubCapability { CapabilityIds = null };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { stub! }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("CapabilityIds 为 null"));
    }

    [Fact]
    public void Validate_HidePlusVariantBinding_Rejects()
    {
        var surface = new StubSurfaceDo { Do = (instructions, _) => instructions.HideInstruction("CALLSHARP") };
        var variant = new StubVariant
        {
            ContributionId = "variant.a",
            Bindings = new[] { new InstructionVariantBinding("CALLSHARP", new HelloVariantFactory()) },
        };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface, variant }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("同时被隐藏与变体绑定"));
    }

    [Fact]
    public void Validate_RegisterPlusVariantBinding_Rejects()
    {
        // NEWTHING 不在基线（注册本身合法），但同指令又绑变体 → handler 来源歧义拒载。
        var surface = new StubSurfaceDo { Do = (instructions, _) => instructions.RegisterInstruction("NEWTHING") };
        var variant = new StubVariant
        {
            ContributionId = "variant.a",
            Bindings = new[] { new InstructionVariantBinding("NEWTHING", new HelloVariantFactory()) },
        };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface, variant }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("同时被表面注册与变体绑定"));
    }

    [Fact]
    public void Validate_SelectionKeyOutsideBaseline_Rejects()
    {
        string json = "{\"packId\":\"test.rules\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"variantSelections\":{\"NOSUCH\":\"builtin:snake\"}}";
        var manifest = CompatPackManifest.TryParse(json, out var m, out _)
            ? m! : throw new InvalidOperationException();
        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("不在 v24 基线内"));
    }

    [Fact]
    public void Validate_HidePlusSelection_Rejects()
    {
        var surface = new StubSurfaceDo { Do = (instructions, _) => instructions.HideInstruction("SETBGIMAGE") };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("同时被隐藏与清单变体选择"));
    }

    [Fact]
    public void Validate_HelloContributions_AgainstRealBaseline_Succeeds()
    {
        // 直测入口：夹具贡献 + 手工清单（不含 gameIdentity，表面动作同 HelloCompatPack）。
        string json = "{\"packId\":\"test.hello-pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"variantSelections\":{\"SETBGIMAGE\":\"builtin:snake\"}}";
        var manifest = CompatPackManifest.TryParse(json, out var m, out var errors)
            ? m! : throw new InvalidOperationException(string.Join("; ", errors));
        var context = new CompatPackValidationContext(
            1,
            new HashSet<string>(StringComparer.Ordinal) { "markup.div-v2.v1", "parse.diagnostics.v1" },
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(GEmuera.Core.Compatibility.LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(GEmuera.Core.Compatibility.LegacyDialectInventories.V24Functions.Select(e => e.Name), StringComparer.Ordinal));

        var hello = new HelloCompatPack();
        Assert.True(CompatPackRules.Validate(manifest, (IReadOnlyList<ICompatPackContribution>)hello.Contributions, context, out errors),
            string.Join("; ", errors));
    }

    [Fact]
    public void Validate_DuplicateContributionId_Rejects()
    {
        var first = new StubVariant { ContributionId = "same.id" };
        var second = new StubCapability { ContributionId = "same.id" };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { first, second }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("重复"));
    }

    [Fact]
    public void Validate_DuplicateVariantBindingAcrossContributions_Rejects()
    {
        var first = new StubVariant
        {
            ContributionId = "variant.a",
            Bindings = new[] { new InstructionVariantBinding("PRINT", new HelloVariantFactory()) },
        };
        var second = new StubVariant
        {
            ContributionId = "variant.b",
            Bindings = new[] { new InstructionVariantBinding(" print ", new HelloVariantFactory()) },
        };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { first, second }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("重复绑定"));
    }

    [Fact]
    public void Validate_VariantSelectionConflictsWithBinding_Rejects()
    {
        var binding = new StubVariant
        {
            ContributionId = "variant.a",
            // 清单 variantSelections 已选 SETBGIMAGE → 同指令再绑自带变体即冲突。
            Bindings = new[] { new InstructionVariantBinding("SETBGIMAGE", new HelloVariantFactory()) },
        };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { binding }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("同时出现"));
    }

    [Fact]
    public void Validate_NonBuiltinVariantSelection_Rejects()
    {
        string json = "{\"packId\":\"test.rules\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"variantSelections\":{\"SETBGIMAGE\":\"custom-fancy\"}}";
        var manifest = CompatPackManifest.TryParse(json, out var m, out _)
            ? m! : throw new InvalidOperationException();
        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("builtin"));
    }

    static CompatPackValidationContext ContextWithVariantInstructions()
    {
        // 组合表与引擎 BuiltinCompatPackVariants 注册表同源（此处测试用等值副本）。
        var table = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["builtin:snake"] = new HashSet<string>(StringComparer.Ordinal) { "SETBGIMAGE", "FOR" },
        };
        return new CompatPackValidationContext(
            1,
            new HashSet<string>(StringComparer.Ordinal) { "parse.diagnostics.v1" },
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(StringComparer.Ordinal) { "SETBGIMAGE", "CALLSHARP", "PRINT" },
            new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" },
            reservedModuleIds: null,
            knownBuiltinVariantInstructions: table);
    }

    [Fact]
    public void Validate_BuiltinVariantRegisteredCombination_Succeeds()
    {
        Assert.True(CompatPackRules.Validate(Manifest(), Array.Empty<ICompatPackContribution>(), ContextWithVariantInstructions(), out _));
    }

    [Fact]
    public void Validate_BuiltinVariantUnregisteredInstruction_Rejects()
    {
        // builtin:snake 只注册了 SETBGIMAGE/FOR 的 handler 变体；选 PRINT 必须在校验段拒载。
        string json = "{\"packId\":\"test.rules\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"variantSelections\":{\"PRINT\":\"builtin:snake\"}}";
        var manifest = CompatPackManifest.TryParse(json, out var m, out _)
            ? m! : throw new InvalidOperationException();
        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), ContextWithVariantInstructions(), out var errors));
        Assert.Contains(errors, e => e.Contains("未注册指令"));
    }

    [Fact]
    public void Validate_UnknownPolicyCapability_Rejects()
    {
        // 注：v1 策略贡献整体拒载（见 Validate_PolicyContribution_V1NotWired_Rejects），
        // 本用例钉住结构性校验（未知 capability id）在拒载之外仍照常报告（错误全量收集）。
        var policy = new HelloPolicyContribution(); // 绑定 parse.diagnostics.v1
        string json = "{\"packId\":\"test.rules\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";
        var manifest = CompatPackManifest.TryParse(json, out var m, out _)
            ? m! : throw new InvalidOperationException();
        var stricter = new CompatPackValidationContext(
            1,
            new HashSet<string>(StringComparer.Ordinal), // 空词汇表：策略绑定的 id 即未知
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(StringComparer.Ordinal) { "CALLSHARP" },
            new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" });
        Assert.False(CompatPackRules.Validate(manifest, new ICompatPackContribution[] { policy }, stricter, out var errors));
        Assert.Contains(errors, e => e.Contains("parse.diagnostics.v1"));
    }

    // —— v1 死契约显式拒载（评审 P2-6，用户裁定 fail-closed）：IInstructionVariantContribution
    //    与 IPolicyContribution 已发布为公共契约但宿主零消费——按文档写只会静默 no-op，
    //    必须加载期拒载并指引 manifest 通道（v2 接线后移除）。——

    [Fact]
    public void Validate_VariantContribution_V1NotWired_Rejects()
    {
        var variant = new StubVariant
        {
            ContributionId = "variant.dead",
            Bindings = new[] { new InstructionVariantBinding("PRINT", new HelloVariantFactory()) },
        };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { variant }, Context(), out var errors));
        // 文案含包 id 与贡献 id，并明确 v1 未接线 + manifest 通道指引。
        Assert.Contains(errors, e => e.Contains("v1 未接线") && e.Contains("test.rules") && e.Contains("variant.dead"));
        Assert.Contains(errors, e => e.Contains("variantSelections"));
    }

    [Fact]
    public void Validate_PolicyContribution_V1NotWired_Rejects()
    {
        var policy = new StubPolicy
        {
            ContributionId = "policy.dead",
            Policies = new[] { new EnginePolicyBinding("parse.diagnostics.v1") },
        };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { policy }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("v1 未接线") && e.Contains("test.rules") && e.Contains("policy.dead"));
        Assert.Contains(errors, e => e.Contains("variantSelections"));
    }

    [Fact]
    public void Validate_SurfaceApplyThrows_Rejects()
    {
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { new StubSurface() }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("boom"));
    }

    [Fact]
    public void Validate_EmptyVariantFactory_Rejects()
    {
        var binding = new StubVariant
        {
            ContributionId = "variant.a",
            Bindings = new InstructionVariantBinding[] { new("PRINT", null!) },
        };
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { binding }, Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("工厂为空"));
    }

    // —— 引擎 handler 对账（宿主传入 Known{Instruction,Function}Handlers 全集时启用）——

    static CompatPackValidationContext ContextWithHandlers(
        IReadOnlySet<string> instructionHandlers,
        IReadOnlySet<string> functionHandlers) => new(
        1,
        new HashSet<string>(StringComparer.Ordinal) { "parse.diagnostics.v1" },
        new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        new HashSet<string>(StringComparer.Ordinal) { "CALLSHARP", "SETBGIMAGE", "PRINT" },
        new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" },
        reservedModuleIds: null,
        knownBuiltinVariantInstructions: null,
        knownInstructionHandlers: instructionHandlers,
        knownFunctionHandlers: functionHandlers);

    [Fact]
    public void Validate_RegisteredInstructionWithoutEngineHandler_Rejects()
    {
        // 拼错/未收录的指令名若穿过校验，会进 plan 哈希却在会话注册表投影时静默无
        // handler 可绑（运行期"未知指令"）——必须在加载段拒载，文案含名字与包 id。
        var surface = new StubSurfaceDo { Do = (instructions, _) => instructions.RegisterInstruction("TOTALLY_MISSPELED") };
        var context = ContextWithHandlers(
            new HashSet<string>(StringComparer.Ordinal) { "SETANIMETIMER", "SETIMAGELAYER" },
            new HashSet<string>(StringComparer.Ordinal));
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface }, context, out var errors));
        Assert.Contains(errors, e => e.Contains("无引擎 handler") && e.Contains("TOTALLY_MISSPELED") && e.Contains("test.rules"));
    }

    [Fact]
    public void Validate_RegisteredFunctionWithoutEngineHandler_Rejects()
    {
        var surface = new StubSurfaceDo { Do = (_, functions) => functions.RegisterFunction("NOSUCHFUNC", "Int64") };
        var context = ContextWithHandlers(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal) { "SQL_CONNECT" });
        Assert.False(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface }, context, out var errors));
        Assert.Contains(errors, e => e.Contains("无引擎 handler") && e.Contains("NOSUCHFUNC") && e.Contains("函数"));
    }

    [Fact]
    public void Validate_RegisteredNamesWithEngineHandlers_Succeeds()
    {
        // 有 handler 的注册名放行；SETANIMETIMER 同时是内置模块（snake/erafl）可 Declare
        // 的合法名——基准并集必须覆盖它，不得误拒"复暴露内置模块声明名"的包。
        var surface = new StubSurfaceDo
        {
            Do = (instructions, functions) =>
            {
                instructions.RegisterInstruction("SETANIMETIMER");
                functions.RegisterFunction("SQL_CONNECT", "Int64");
            }
        };
        var context = ContextWithHandlers(
            new HashSet<string>(StringComparer.Ordinal) { "SETANIMETIMER", "SETIMAGELAYER" },
            new HashSet<string>(StringComparer.Ordinal) { "SQL_CONNECT" });
        Assert.True(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface }, context, out var errors),
            string.Join("; ", errors));
    }

    [Fact]
    public void Validate_HandlerSetsOmitted_SkipsHandlerReconciliation()
    {
        // 缺省（空集）= 跳过 handler 对账（测试/轻量场景兼容）；名字仍受基线同名规则约束。
        var surface = new StubSurfaceDo { Do = (instructions, _) => instructions.RegisterInstruction("TOTALLY_MISSPELED") };
        Assert.True(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface }, Context(), out var errors),
            string.Join("; ", errors));
    }

    // —— 函数名规范化语义钉住：引擎 IsFunctionVisible 按 Trim 原样（Ordinal）比对，
    //    规则层 Trim+ToUpper 之所以等价，依赖引擎函数注册表键全为"规范化不变"形态
    //    （纯大写 ASCII / 无大小写 CJK）。生成清单由引擎真实投影导出，在此钉住该假设。——

    public static IEnumerable<object[]> EngineFunctionInventoryNames()
    {
        IEnumerable<string> Collect(params IEnumerable<GEmuera.Core.Compatibility.LegacyFunctionInventoryEntry>[] inventories)
        {
            foreach (IEnumerable<GEmuera.Core.Compatibility.LegacyFunctionInventoryEntry> inventory in inventories)
                foreach (GEmuera.Core.Compatibility.LegacyFunctionInventoryEntry entry in inventory)
                    yield return entry.Name;
        }
        // 注意包一层 object[]：直接 yield string[] 会因数组协变被 xUnit 拆成 N 个参数。
        yield return new object[] { Collect(
            GEmuera.Core.Compatibility.LegacyDialectInventories.V24Functions,
            GEmuera.Core.Compatibility.LegacyDialectInventories.SnakeDeltaFunctions,
            GEmuera.Core.Compatibility.LegacyDialectInventories.EraFlDeltaFunctions,
            GEmuera.Core.Compatibility.LegacyDialectInventories.EraBlueDeltaFunctions,
            GEmuera.Core.Compatibility.LegacyDialectInventories.MegatenDeltaFunctions,
            GEmuera.Core.Compatibility.LegacyDialectInventories.V18Functions).ToArray() };
    }

    [Theory]
    [MemberData(nameof(EngineFunctionInventoryNames))]
    public void EngineFunctionInventories_AreNormalizationStable(string[] names)
    {
        // 任一函数名经 Trim().ToUpperInvariant() 后必须与自身一致——否则规则层的规范化
        // 与引擎 Trim 原样比对语义分歧（现状被注册表全大写掩盖），本测试先红以示警。
        foreach (string name in names)
            Assert.Equal(name, name.Trim().ToUpperInvariant());
    }

    [Fact]
    public void Validate_HideFunction_NormalizesLowercaseInputToBaselineForm()
    {
        // 小写/带空白输入经规范化后命中基线名（EXISTVAR）——与引擎注册表全大写键等价。
        var surface = new StubSurfaceDo { Do = (_, functions) => functions.HideFunction(" existvar ") };
        Assert.True(CompatPackRules.Validate(Manifest(), new ICompatPackContribution[] { surface }, Context(), out var errors),
            string.Join("; ", errors));
    }
}
