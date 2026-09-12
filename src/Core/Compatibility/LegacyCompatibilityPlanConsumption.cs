using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// The legacy bridge is still the behavior owner during M1, but it must not
/// silently ignore a descriptor selected by a frozen compatibility plan. This
/// adapter validates the boundary against the registry that the legacy parser
/// actually built and returns an immutable observation for diagnostics.
/// </summary>
public sealed record LegacyCompatibilityConsumptionSnapshot(
    string ProfileId,
    string PlanHash,
    string DialectHash,
    int InstructionDescriptorCount,
    int FunctionDescriptorCount,
    int PortCount,
    int CapabilityCount,
    int ConditionallyHiddenDescriptorCount = 0)
{
    public bool HasDescriptorOverrides =>
        InstructionDescriptorCount > 0 || FunctionDescriptorCount > 0;
}

public static class LegacyCompatibilityPlanConsumption
{
    public static LegacyCompatibilityConsumptionSnapshot Validate(
        CompatibilityPlan plan,
        IEnumerable<string> legacyInstructionNames,
        IEnumerable<string> legacyFunctionNames)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(legacyInstructionNames);
        ArgumentNullException.ThrowIfNull(legacyFunctionNames);

        var instructions = NormalizeLegacyNames(legacyInstructionNames, nameof(legacyInstructionNames));
        var functions = NormalizeLegacyNames(legacyFunctionNames, nameof(legacyFunctionNames));

        // 会话驱动校验：会话注册表实际暴露的每个名字必须被计划声明，否则是引擎/清单
        // 漂移，直接失败。方向与旧实现相反——旧实现要求"计划声明的名字都在注册表中"，
        // 那会把条件可见性误判为错误：v24 的 VARI/VARS 在 scoped-variable 关闭的会话
        // 不在注册表里，但必须留在计划清单中供开启的会话路由；snake 主动排除的个别
        // v24 函数（如 BITMAP_CACHE_ENABLE）同理。描述符归属模块由 CompatibilityPlanBuilder
        // 在构建期强制（claims-an-unselected-module 检查），此处不再重复。
        foreach (var name in instructions)
        {
            if (!plan.Dialect.Instructions.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"Legacy instruction '{name}' is not declared by the compatibility plan.");
            }
        }

        foreach (var name in functions)
        {
            if (!plan.Dialect.Functions.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"Legacy function '{name}' is not declared by the compatibility plan.");
            }
        }

        return new LegacyCompatibilityConsumptionSnapshot(
            plan.ProfileId,
            plan.CanonicalHash,
            plan.Dialect.CanonicalHash,
            plan.Dialect.Instructions.Count,
            plan.Dialect.Functions.Count,
            plan.Dialect.Ports.Count,
            plan.CapabilityIds.Count,
            (plan.Dialect.Instructions.Count - instructions.Count)
                + (plan.Dialect.Functions.Count - functions.Count));
    }

    private static HashSet<string> NormalizeLegacyNames(
        IEnumerable<string> names,
        string parameterName)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Legacy registry contains an empty name.", parameterName);
            normalized.Add(name.Trim().ToUpperInvariant());
        }
        return normalized;
    }
}
