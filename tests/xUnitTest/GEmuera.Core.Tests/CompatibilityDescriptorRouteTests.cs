using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using GEmuera.Core.Runtime;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 会话驱动路由视图（运行时精确闭包，设计 §6.7）的直测：registry 必须被 final plan
/// 完整声明；发现未声明名字时返回 false 且不产出可用 route（调用方据 symbol 诊断降级）。
/// 反方向——计划多出的条件符号——由 LegacyCompatibilityPlanConsumption 的消费测试覆盖。
/// </summary>
public class CompatibilityDescriptorRouteTests
{
    static CompatibilityPlan Plan()
    {
        // 单符号计划：A 指令 + RESULT 函数（其余字段不参与路由视图，仅需合法）。
        var dialect = new DialectPlan(
            Array.Empty<DialectModuleSnapshot>(),
            Array.Empty<BehaviorPortSnapshot>(),
            new Dictionary<string, InstructionDescriptor>(StringComparer.Ordinal)
            {
                ["A"] = new InstructionDescriptor("A", "", "gemuera.v24", VmCompletionMode.CoreImmediate),
            },
            new Dictionary<string, FunctionDescriptor>(StringComparer.Ordinal)
            {
                ["RESULT"] = new FunctionDescriptor("RESULT", "()", "gemuera.v24", "Integer"),
            },
            new string('a', 64));
        return new CompatibilityPlan(
            "v24pure",
            dialect,
            Array.Empty<string>(),
            null,
            new string('b', 64));
    }

    [Fact]
    public void TryCreateSessionView_WhenRegistryMatchesPlan_ReturnsTrue()
    {
        bool created = CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
            Plan(),
            new Dictionary<string, string> { ["A"] = "legacy-a" },
            StringComparer.Ordinal,
            new Dictionary<string, string> { ["RESULT"] = "legacy-result" },
            StringComparer.Ordinal,
            out var route);

        Assert.True(created);
        Assert.NotNull(route);
        Assert.Equal("legacy-a", route.Instructions["A"]);
        Assert.Equal("legacy-result", route.Functions["RESULT"]);
    }

    [Fact]
    public void TryCreateSessionView_WhenRegistryHasUndeclaredName_ReturnsFalse()
    {
        // plan 只有 A，registry 有 A+B：B 不得被"静默放行"（旧行为是把 registry 全量
        // 交给解析器、绕过 plan 哈希）；必须 false + 无可用 route + symbol 诊断。
        bool created = CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
            Plan(),
            new Dictionary<string, string> { ["A"] = "legacy-a", ["B"] = "legacy-b" },
            StringComparer.Ordinal,
            new Dictionary<string, string> { ["RESULT"] = "legacy-result" },
            StringComparer.Ordinal,
            out var route,
            out CompatSymbolKey? undeclared);

        Assert.False(created);
        Assert.Null(route);
        Assert.NotNull(undeclared);
        Assert.Equal(CompatSymbolKind.Instruction, undeclared!.Value.Kind);
        Assert.Equal("B", undeclared.Value.Name);
    }

    [Fact]
    public void TryCreateSessionView_WhenRegistryHasUndeclaredFunction_ReturnsFalse()
    {
        bool created = CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
            Plan(),
            new Dictionary<string, string> { ["A"] = "legacy-a" },
            StringComparer.Ordinal,
            new Dictionary<string, string> { ["RESULT"] = "legacy-result", ["UNDECLARED_FN"] = "legacy-fn" },
            StringComparer.Ordinal,
            out var route,
            out CompatSymbolKey? undeclared);

        Assert.False(created);
        Assert.Null(route);
        Assert.Equal(new CompatSymbolKey(CompatSymbolKind.Function, "UNDECLARED_FN"), undeclared);
    }

    [Fact]
    public void TryCreateSessionView_EmptyRegistry_MatchesEmptyView()
    {
        // 空注册表是合法基线（无 legacy 名字可解析）：route 非 null、两个视图为空。
        bool created = CompatibilityDescriptorRoute<string, string>.TryCreateSessionView(
            Plan(),
            new Dictionary<string, string>(),
            StringComparer.Ordinal,
            new Dictionary<string, string>(),
            StringComparer.Ordinal,
            out var route);

        Assert.True(created);
        Assert.Empty(route!.Instructions);
        Assert.Empty(route.Functions);
    }
}
