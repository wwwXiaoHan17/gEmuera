using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// §6.7 双向闭包的负向直测（Task 4 fix I2）：正向（registry ⊆ plan）由
/// <c>CompatibilityDescriptorRouteTests</c> 与 SurfaceSmoke 覆盖；本文件钉住反方向——
/// 计划里的非条件符号必须在真实注册表出现，且只有 METHOD 投影指令名享受
/// "指令侧可缺席"豁免（函数侧缺席仍然失败）。用真实内置 plan 构造，避免合成数据
/// 掩盖"计划声明了但从未投影"的漂移。
/// </summary>
public class LegacyCompatibilityPlanConsumptionTests
{
    static CompatibilityPlan V24PurePlan() => BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");

    static List<string> InstructionsOf(CompatibilityPlan plan) =>
        plan.Dialect.Instructions.Keys.ToList();

    static List<string> FunctionsOf(CompatibilityPlan plan) =>
        plan.Dialect.Functions.Keys.ToList();

    [Fact]
    public void Validate_UnconditionalInstructionMissingFromRegistry_ThrowsWithSymbolOwnerProfileAndPlanHash()
    {
        CompatibilityPlan plan = V24PurePlan();
        string missing = plan.Dialect.Instructions.Keys.First();
        List<string> instructions = InstructionsOf(plan).Where(name => name != missing).ToList();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LegacyCompatibilityPlanConsumption.Validate(plan, instructions, FunctionsOf(plan)));

        // 诊断必须能定位漂移：symbol 名、owner module、profile、plan hash（§6.7）。
        Assert.Contains(missing, exception.Message);
        Assert.Contains(plan.Dialect.Instructions[missing].ModuleId, exception.Message);
        Assert.Contains(plan.ProfileId, exception.Message);
        Assert.Contains(plan.CanonicalHash, exception.Message);
        Assert.Contains("Planned legacy instruction", exception.Message);
    }

    [Fact]
    public void Validate_ConditionallyHiddenSymbolMissingFromRegistry_DoesNotThrow()
    {
        CompatibilityPlan plan = V24PurePlan();
        string conditionallyHidden = plan.Dialect.Instructions.Keys.First();
        List<string> instructions = InstructionsOf(plan)
            .Where(name => name != conditionallyHidden)
            .ToList();
        var conditionalSymbols = new HashSet<CompatSymbolKey>
        {
            new CompatSymbolKey(CompatSymbolKind.Instruction, conditionallyHidden),
        };

        var snapshot = LegacyCompatibilityPlanConsumption.Validate(
            plan, instructions, FunctionsOf(plan), conditionalSymbols);

        Assert.Equal(plan.ProfileId, snapshot.ProfileId);
        Assert.Equal(plan.CanonicalHash, snapshot.PlanHash);
        Assert.True(snapshot.ConditionallyHiddenDescriptorCount > 0);
    }

    [Fact]
    public void Validate_MethodProjectedName_ExemptsInstructionSideOnly()
    {
        CompatibilityPlan plan = V24PurePlan();
        string name = FindMethodProjectedName(plan);
        var methodProjected = new HashSet<string>(StringComparer.Ordinal) { name };

        // 指令侧缺席合法：METHOD 投影条目由函数可见性驱动，计划仍可声明它供其他会话路由。
        List<string> instructionsWithoutName = InstructionsOf(plan)
            .Where(entry => entry != name)
            .ToList();
        LegacyCompatibilityPlanConsumption.Validate(
            plan,
            instructionsWithoutName,
            FunctionsOf(plan),
            methodProjectedInstructionNames: methodProjected);

        // 同名函数侧缺席仍失败：method-projected 豁免只限指令侧。
        List<string> functionsWithoutName = FunctionsOf(plan)
            .Where(entry => entry != name)
            .ToList();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            LegacyCompatibilityPlanConsumption.Validate(
                plan,
                InstructionsOf(plan),
                functionsWithoutName,
                methodProjectedInstructionNames: methodProjected));
        Assert.Contains(name, exception.Message);
        Assert.Contains("Planned legacy function", exception.Message);
    }

    [Fact]
    public void Validate_RealBuiltInPlan_MatchesItsOwnRegistrySurface()
    {
        CompatibilityPlan plan = V24PurePlan();

        var snapshot = LegacyCompatibilityPlanConsumption.Validate(
            plan, InstructionsOf(plan), FunctionsOf(plan));

        Assert.Equal(plan.ProfileId, snapshot.ProfileId);
        Assert.Equal(plan.CanonicalHash, snapshot.PlanHash);
        Assert.Equal(plan.Dialect.CanonicalHash, snapshot.DialectHash);
        Assert.Equal(plan.Dialect.Instructions.Count, snapshot.InstructionDescriptorCount);
        Assert.Equal(plan.Dialect.Functions.Count, snapshot.FunctionDescriptorCount);
        Assert.Equal(0, snapshot.ConditionallyHiddenDescriptorCount);
        Assert.True(snapshot.HasDescriptorOverrides);
    }

    static string FindMethodProjectedName(CompatibilityPlan plan)
    {
        string? name = LegacyDialectInventories.MethodProjectedInstructionNames
            .FirstOrDefault(candidate => plan.Dialect.Instructions.ContainsKey(candidate)
                && plan.Dialect.Functions.ContainsKey(candidate));
        Assert.False(string.IsNullOrEmpty(name),
            "v24pure 计划必须包含至少一个 METHOD 投影双形态符号（如 ABS）。");
        return name!;
    }
}
