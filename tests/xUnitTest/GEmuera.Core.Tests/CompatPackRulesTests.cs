using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
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
        + "\"capabilities\":[\"math.times-clamp.v1\"],"
        + "\"variantSelections\":{\"SETBGIMAGE\":\"builtin:snake\"}}";

    static CompatPackManifest Manifest() =>
        CompatPackManifest.TryParse(ManifestJson, out var manifest, out var errors)
            ? manifest!
            : throw new InvalidOperationException(string.Join("; ", errors));

    static CompatPackValidationContext Context() => new(
        new CompatVersion(1, 0),
        new CompatVersion(1, 0),
        EngineCapabilityCatalog.All,
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
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
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
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
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
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
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
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal), // 空目录：策略绑定的 id 即未知
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
        new CompatVersion(1, 0),
        new CompatVersion(1, 0),
        EngineCapabilityCatalog.All,
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
    [Fact]
    public void Validate_BaseProfileMismatch_Rejects()
    {
        string json = "{\"packId\":\"test.profile\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"baseProfileId\":\"snake\"}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(StringComparer.Ordinal) { "CALLSHARP", "SETBGIMAGE", "PRINT" },
            new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" },
            baselineProfileId: "v24pure");

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), context, out var errors));
        Assert.Contains(errors, e => e.Contains("baseProfileId"));
    }

    [Fact]
    public void Validate_BaseSurfaceHashMismatch_Rejects()
    {
        // 精确对账（E2-R6）：manifest 声明非空 baseSurfaceHash 且与当前 v24 表面快照不一致
        // → 拒载（错误文案含 baseSurfaceHash）；声明与基线一致 → 放行；不声明 → 不触发本规则。
        string mismatchJson = "{\"packId\":\"test.hash\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"baseSurfaceHash\":\"" + new string('0', 64) + "\"}";
        var mismatchManifest = CompatPackManifest.TryParse(mismatchJson, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));

        Assert.False(CompatPackRules.Validate(mismatchManifest, Array.Empty<ICompatPackContribution>(), Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("baseSurfaceHash"));

        string matchingJson = "{\"packId\":\"test.hash\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"baseSurfaceHash\":\"" + LegacySurfaceHash.ComputeV24SurfaceHash() + "\"}";
        var matchingManifest = CompatPackManifest.TryParse(matchingJson, out var parsedMatch, out _)
            ? parsedMatch! : throw new InvalidOperationException();
        Assert.True(CompatPackRules.Validate(matchingManifest, Array.Empty<ICompatPackContribution>(), Context(), out var matchErrors),
            string.Join("; ", matchErrors));
    }

    [Fact]
    public void Validate_ManifestSurfaceFunctionWithoutReturnTypeMetadata_Rejects()
    {
        string json = "{\"packId\":\"test.surface-return\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"surface\":{\"addFunctions\":[\"SQL_CONNECT\"]}}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            EngineCapabilityCatalog.All,
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(StringComparer.Ordinal) { "CALLSHARP", "SETBGIMAGE", "PRINT" },
            new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" },
            knownFunctionHandlers: new HashSet<string>(StringComparer.Ordinal) { "SQL_CONNECT" },
            knownFunctionReturnTypes: new Dictionary<string, string>(StringComparer.Ordinal) { ["OTHER"] = "Integer" });
        var contribution = new ManifestSurfaceContribution(manifest.Surface, context.KnownFunctionReturnTypes);

        Assert.False(CompatPackRules.Validate(manifest, new ICompatPackContribution[] { contribution }, context, out var errors));
        Assert.Contains(errors, e => e.Contains("返回类型元数据") && e.Contains("SQL_CONNECT"));
    }

    [Fact]
    public void Validate_SaveProfileIdV1_RejectsInsteadOfSilentNoOp()
    {
        string json = "{\"packId\":\"test.save-profile\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"saveProfileId\":\"gemuera.custom\"}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("saveProfileId") && e.Contains("v1"));
    }

    // —— engine capability catalog（设计 §6.3）：未知 / DiagnosticOnly 一律拒载 ——

    [Fact]
    public void Validate_UnknownCapability_Rejects()
    {
        string json = "{\"packId\":\"test.unknown-cap\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"capabilities\":[\"does.not.exist.v1\"]}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("未知 capability id") && e.Contains("does.not.exist.v1"));
    }

    [Fact]
    public void Validate_DiagnosticOnlyCapability_Rejects()
    {
        // 表面 capability 无真实行为消费者：包声明它不是"已实现"，必须在加载段拒载
        // （fail-at-load），否则包作者会得到静默 no-op。
        string json = "{\"packId\":\"test.diagnostic-cap\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"capabilities\":[\"markup.div-v2.v1\"]}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), Context(), out var errors));
        Assert.Contains(errors, e => e.Contains("markup.div-v2.v1") && e.Contains("DiagnosticOnly"));
    }

    [Fact]
    public void Validate_ConflictingCapabilities_Rejects()
    {
        // 合成目录覆盖"规则层确实把 context.Capabilities 交给 TryResolveForPack 做闭包"；
        // 当前 engine catalog 无天然冲突对（见 EngineCapabilityCatalogTests 的 R4 说明）。
        var catalog = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal)
        {
            ["a.conflict.v1"] = new(
                "a.conflict.v1", CapabilityStatus.Wired, Array.Empty<string>(), Array.Empty<string>(),
                new[] { "b.conflict.v1" }, "test.consumer", new CompatVersion(1, 0)),
            ["b.conflict.v1"] = new(
                "b.conflict.v1", CapabilityStatus.Wired, Array.Empty<string>(), Array.Empty<string>(),
                new[] { "a.conflict.v1" }, "test.consumer", new CompatVersion(1, 0)),
        };
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            catalog,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
        string json = "{\"packId\":\"test.conflict\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"capabilities\":[\"a.conflict.v1\",\"b.conflict.v1\"]}";
        var manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), context, out var errors));
        Assert.Contains(errors, e => e.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    // —— R18：capability 闭包输出尚未接入 v1 组装器。规则层解析出 Require/Implies
    //    展开的 id 后必须拒载，否则"规则通过、plan 缺 id"会静默漂移。——

    static CompatPackValidationContext ClosureContext()
    {
        var catalog = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal)
        {
            ["root.v1"] = new(
                "root.v1", CapabilityStatus.Wired, Array.Empty<string>(), new[] { "implied.v1" },
                Array.Empty<string>(), "test.consumer", new CompatVersion(1, 0)),
            ["required.v1"] = new(
                "required.v1", CapabilityStatus.Wired, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), "test.consumer", new CompatVersion(1, 0)),
            ["implied.v1"] = new(
                "implied.v1", CapabilityStatus.Wired, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), "test.consumer", new CompatVersion(1, 0)),
        };
        return new CompatPackValidationContext(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            catalog,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
    }

    static CompatPackManifest ManifestWithCapabilities(params string[] capabilities)
    {
        string json = "{\"packId\":\"test.closure\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"capabilities\":[" + string.Join(",", capabilities.Select(id => "\"" + id + "\"")) + "]}";
        return CompatPackManifest.TryParse(json, out var manifest, out var errors)
            ? manifest! : throw new InvalidOperationException(string.Join("; ", errors));
    }

    [Fact]
    public void Validate_ImpliesClosureNotWiredIntoV1Assembler_Rejects()
    {
        // root.v1 implies implied.v1，但包里只显式写了 root.v1：解析结果会多出 implied.v1，
        // 而 CompatPackPlanAssembler 只并集显式声明的 id → 必须拒载（fail-closed）。
        Assert.False(CompatPackRules.Validate(
            ManifestWithCapabilities("root.v1"),
            Array.Empty<ICompatPackContribution>(),
            ClosureContext(),
            out var errors));
        Assert.Contains(errors, e => e.Contains("implied.v1") && e.Contains("Implies 闭包输出"));
    }

    [Fact]
    public void Validate_RequiresClosureNotWiredIntoV1Assembler_Rejects()
    {
        // Requires 展开同理：required.v1 不会自动进 plan，必须显式声明。
        var catalog = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal)
        {
            ["root-requires.v1"] = new(
                "root-requires.v1", CapabilityStatus.Wired, new[] { "required.v1" }, Array.Empty<string>(),
                Array.Empty<string>(), "test.consumer", new CompatVersion(1, 0)),
            ["required.v1"] = new(
                "required.v1", CapabilityStatus.Wired, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), "test.consumer", new CompatVersion(1, 0)),
        };
        var context = new CompatPackValidationContext(
            new CompatVersion(1, 0), new CompatVersion(1, 0), catalog,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));

        Assert.False(CompatPackRules.Validate(
            ManifestWithCapabilities("root-requires.v1"),
            Array.Empty<ICompatPackContribution>(),
            context,
            out var errors));
        Assert.Contains(errors, e => e.Contains("required.v1") && e.Contains("闭包输出"));
    }

    [Fact]
    public void Validate_ExpandedCapabilityExplicitlyDeclared_Succeeds()
    {
        // 显式写出闭包 id 后，v1 组装器并集即可得到完整集合 → 放行。
        Assert.True(CompatPackRules.Validate(
            ManifestWithCapabilities("root.v1", "implied.v1"),
            Array.Empty<ICompatPackContribution>(),
            ClosureContext(),
            out var errors), string.Join("; ", errors));
    }

    // —— 双轴版本模型（contractAbi / engineApi）：major 必须相等、包 minor <= 引擎 minor，
    //    两轴独立判定，错误文案必须指明是哪一轴。——

    static CompatPackValidationContext AxesContext(
        CompatVersion currentContractAbi,
        CompatVersion currentEngineApi,
        string baselineProfileId = "v24pure") => new(
        currentContractAbi,
        currentEngineApi,
        EngineCapabilityCatalog.All,
        new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        new HashSet<string>(StringComparer.Ordinal) { "CALLSHARP", "SETBGIMAGE", "PRINT" },
        new HashSet<string>(StringComparer.Ordinal) { "EXISTVAR" },
        baselineProfileId: baselineProfileId);

    static CompatPackManifest AxesManifest(string contractAbi, string engineApi)
    {
        string json = "{\"packId\":\"test.axes\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"" + contractAbi + "\",\"engineApi\":\"" + engineApi + "\"}";
        return CompatPackManifest.TryParse(json, out var manifest, out var errors)
            ? manifest! : throw new InvalidOperationException(string.Join("; ", errors));
    }

    [Fact]
    public void Validate_ContractMinorHigherThanEngine_Rejects()
    {
        // 包声明 contractAbi 1.2 意味着可能用到 1.2 新增契约；引擎 1.1 缺少该增量 → 拒载。
        // engine 轴（包 1.0 <= 引擎 1.1）通过，错误文案必须只指向 contract 轴。
        CompatPackManifest manifest = AxesManifest(contractAbi: "1.2", engineApi: "1.0");
        var context = AxesContext(new CompatVersion(1, 1), new CompatVersion(1, 1));

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), context, out var errors));
        Assert.Contains(errors, e => e.Contains("contractAbi") && e.Contains("1.2"));
        Assert.DoesNotContain(errors, e => e.Contains("engineApi"));
    }

    [Fact]
    public void Validate_EngineMinorHigherThanEngine_Rejects()
    {
        // engine 轴方向同 contract：包声明引擎行为 1.3，引擎 1.2 缺少增量 → 拒载；
        // contract 轴（包 1.0 <= 引擎 1.2）通过。
        CompatPackManifest manifest = AxesManifest(contractAbi: "1.0", engineApi: "1.3");
        var context = AxesContext(new CompatVersion(1, 2), new CompatVersion(1, 2));

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), context, out var errors));
        Assert.Contains(errors, e => e.Contains("engineApi") && e.Contains("1.3"));
        Assert.DoesNotContain(errors, e => e.Contains("contractAbi"));
    }

    [Fact]
    public void Validate_MajorMismatch_Rejects()
    {
        // contract major 不同：包 2.0 vs 引擎 1.5（minor 方向无法挽救 major 分裂）。
        var contractContext = AxesContext(new CompatVersion(1, 5), new CompatVersion(1, 0));
        CompatPackManifest contractManifest = AxesManifest(contractAbi: "2.0", engineApi: "1.0");
        Assert.False(CompatPackRules.Validate(contractManifest, Array.Empty<ICompatPackContribution>(), contractContext, out var contractErrors));
        Assert.Contains(contractErrors, e => e.Contains("contractAbi") && e.Contains("2.0"));

        // engine major 不同：包 2.0 vs 引擎 1.5。
        var engineContext = AxesContext(new CompatVersion(1, 0), new CompatVersion(1, 5));
        CompatPackManifest engineManifest = AxesManifest(contractAbi: "1.0", engineApi: "2.0");
        Assert.False(CompatPackRules.Validate(engineManifest, Array.Empty<ICompatPackContribution>(), engineContext, out var engineErrors));
        Assert.Contains(engineErrors, e => e.Contains("engineApi") && e.Contains("2.0"));
    }

    [Fact]
    public void Validate_BaseProfileNotV24Pure_Rejects()
    {
        // v1 契约面只允许叠加 v24pure。此处把宿主基线也设为 snake——既有的
        // 「manifest vs 会话基线不一致」规则不会触发，从而证明 v1 的 v24pure 硬约束
        // 是独立判定（不是被基线比对顺带挡下的）。
        string json = "{\"packId\":\"test.profile\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\",\"baseProfileId\":\"snake\"}";
        CompatPackManifest manifest = CompatPackManifest.TryParse(json, out var parsed, out var parseErrors)
            ? parsed! : throw new InvalidOperationException(string.Join("; ", parseErrors));
        var context = AxesContext(new CompatVersion(1, 0), new CompatVersion(1, 0), baselineProfileId: "snake");

        Assert.False(CompatPackRules.Validate(manifest, Array.Empty<ICompatPackContribution>(), context, out var errors));
        Assert.Contains(errors, e => e.Contains("baseProfileId") && e.Contains("v24pure"));
    }

}
