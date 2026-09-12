using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// Creates the immutable adapter view from a frozen descriptor plan to an
/// already-built legacy handler registry. The adapter never creates or
/// replaces handlers; it only makes the selected descriptor keys reachable.
/// </summary>
public sealed class CompatibilityDescriptorRoute<TInstruction, TFunction>
{
    private CompatibilityDescriptorRoute(
        IReadOnlyDictionary<string, TInstruction> instructions,
        IReadOnlyDictionary<string, TFunction> functions)
    {
        Instructions = instructions;
        Functions = functions;
    }

    public IReadOnlyDictionary<string, TInstruction> Instructions { get; }
    public IReadOnlyDictionary<string, TFunction> Functions { get; }

    public static CompatibilityDescriptorRoute<TInstruction, TFunction> Create(
        CompatibilityPlan plan,
        IReadOnlyDictionary<string, TInstruction> legacyInstructions,
        IReadOnlyDictionary<string, TFunction> legacyFunctions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(legacyInstructions);
        ArgumentNullException.ThrowIfNull(legacyFunctions);

        // Keep the comparer of the legacy handler registry so mixed-case
        // instruction spellings resolve exactly as the upstream engine does.
        var instructions = new Dictionary<string, TInstruction>(
            GetLegacyComparer(legacyInstructions));
        foreach (var descriptor in plan.Dialect.Instructions.Values)
        {
            if (!legacyInstructions.TryGetValue(descriptor.Name, out var handler))
            {
                throw new InvalidOperationException(
                    $"Compatibility plan instruction '{descriptor.Name}' is not registered by the legacy parser.");
            }
            instructions.Add(descriptor.Name, handler);
        }

        var functions = new Dictionary<string, TFunction>(
            GetLegacyComparer(legacyFunctions));
        foreach (var descriptor in plan.Dialect.Functions.Values)
        {
            if (!legacyFunctions.TryGetValue(descriptor.Name, out var handler))
            {
                throw new InvalidOperationException(
                    $"Compatibility plan function '{descriptor.Name}' is not registered by the legacy parser.");
            }
            functions.Add(descriptor.Name, handler);
        }

        return new CompatibilityDescriptorRoute<TInstruction, TFunction>(
            new ReadOnlyDictionary<string, TInstruction>(instructions),
            new ReadOnlyDictionary<string, TFunction>(functions));
    }

    /// <summary>
    /// 会话驱动的路由视图：以会话注册表（已按 profile 投影、含 handler 变体）为基准，
    /// 要求其中每个名字都被计划声明（漂移门禁）；计划多出的名字视为条件可见性
    /// （如 v24 的 VARI/VARS 在 scoped-variable 关闭的会话中隐藏、snake 主动排除的
    /// 个别 v24 函数），按会话语义跳过。返回 false 表示会话表面存在计划未声明的
    /// 名字——调用方应记错并回退到投影注册表，而不是静默截断解析面。
    /// </summary>
    public static bool TryCreateSessionView(
        CompatibilityPlan plan,
        IReadOnlyDictionary<string, TInstruction> legacyInstructions,
        IReadOnlyDictionary<string, TFunction> legacyFunctions,
        out CompatibilityDescriptorRoute<TInstruction, TFunction> route)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(legacyInstructions);
        ArgumentNullException.ThrowIfNull(legacyFunctions);

        foreach (var name in legacyInstructions.Keys)
        {
            if (!plan.Dialect.Instructions.ContainsKey(name))
                return Missing(name, "instruction", out route);
        }
        foreach (var name in legacyFunctions.Keys)
        {
            if (!plan.Dialect.Functions.ContainsKey(name))
                return Missing(name, "function", out route);
        }

        route = new CompatibilityDescriptorRoute<TInstruction, TFunction>(
            new ReadOnlyDictionary<string, TInstruction>(
                new Dictionary<string, TInstruction>(legacyInstructions, GetLegacyComparer(legacyInstructions))),
            new ReadOnlyDictionary<string, TFunction>(
                new Dictionary<string, TFunction>(legacyFunctions, GetLegacyComparer(legacyFunctions))));
        return true;

        bool Missing(string name, string kind, out CompatibilityDescriptorRoute<TInstruction, TFunction> missing)
        {
            Console.Error.WriteLine($"Compatibility plan does not declare legacy {kind} '{name}'.");
            missing = null!;
            return false;
        }
    }

    private static IEqualityComparer<string> GetLegacyComparer<TValue>(IReadOnlyDictionary<string, TValue> registry)
    {
        return registry is Dictionary<string, TValue> dictionary
            ? dictionary.Comparer
            : StringComparer.Ordinal;
    }
}
