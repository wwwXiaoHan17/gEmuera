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

    [Fact]
    public void Validate_UnknownPolicyCapability_Rejects()
    {
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
}
