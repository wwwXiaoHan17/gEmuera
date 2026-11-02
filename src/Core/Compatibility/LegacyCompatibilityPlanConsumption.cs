using System.Collections.ObjectModel;
using GEmuera.Core.Compatibility.Packs;

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
    /// <summary>
    /// 运行时精确闭包（设计 §6.7）：两个方向都必须成立——
    /// 1. registry ⊆ plan：会话注册表暴露的每个名字都必须被计划声明（漂移即失败）；
    /// 2. plan 的非条件项 ⊆ registry：计划声明的每个非条件符号都必须真实投影。
    /// 条件符号（<paramref name="conditionalSymbols"/>，如 scoped-variable 关闭会话的
    /// VARI/VARS、snake 主动排除的 v24 函数）由宿主从会话 profile 显式给出——Core 无法
    /// 从 plan 数据本身推断"哪些缺席是有意的"，必须由投影层记录并传入。
    /// METHOD 投影的指令名（<paramref name="methodProjectedInstructionNames"/>）在指令侧
    /// 恒为条件项：其注册表条目由函数可见性驱动（表达式函数投影），而不是独立指令声明。
    /// </summary>
    public static LegacyCompatibilityConsumptionSnapshot Validate(
        CompatibilityPlan plan,
        IEnumerable<string> legacyInstructionNames,
        IEnumerable<string> legacyFunctionNames,
        IReadOnlySet<CompatSymbolKey>? conditionalSymbols = null,
        IReadOnlySet<string>? methodProjectedInstructionNames = null)
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

        // 反向检查（§6.7）：计划里的非条件符号必须在真实注册表里出现。只做单向
        //（registry ⊆ plan）会让"计划声明了但从未投影"的名字永久静默——例如基线的
        // METHOD 投影指令、或内置模块声明却被会话隐藏的函数形态。条件项与 METHOD
        // 投影指令名按会话语义跳过（豁免集必须由投影层显式记录，不能靠名字猜）。
        foreach (var descriptor in plan.Dialect.Instructions.Values)
        {
            if (methodProjectedInstructionNames is not null
                && methodProjectedInstructionNames.Contains(descriptor.Name))
                continue;
            if (conditionalSymbols is not null
                && conditionalSymbols.Contains(new CompatSymbolKey(CompatSymbolKind.Instruction, descriptor.Name)))
                continue;
            if (!instructions.Contains(descriptor.Name))
            {
                throw new InvalidOperationException(
                    $"Planned legacy instruction '{descriptor.Name}' (owner module '{descriptor.ModuleId}') "
                    + $"is not present in the runtime registry (profile '{plan.ProfileId}', plan '{plan.CanonicalHash}').");
            }
        }

        foreach (var descriptor in plan.Dialect.Functions.Values)
        {
            if (conditionalSymbols is not null
                && conditionalSymbols.Contains(new CompatSymbolKey(CompatSymbolKind.Function, descriptor.Name)))
                continue;
            if (!functions.Contains(descriptor.Name))
            {
                throw new InvalidOperationException(
                    $"Planned legacy function '{descriptor.Name}' (owner module '{descriptor.ModuleId}') "
                    + $"is not present in the runtime registry (profile '{plan.ProfileId}', plan '{plan.CanonicalHash}').");
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
