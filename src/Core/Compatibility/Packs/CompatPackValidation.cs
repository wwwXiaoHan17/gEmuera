namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 加载器校验所需的宿主输入（fail-closed 语义校验的数据源）。结构校验在契约程序集的
/// CompatPackManifest 内完成；本上下文由宿主在会话组装期提供：capability 词汇表、
/// 内置变体名录、v24 基线表面（对账用）。引擎注册表级对账（add 名单须有真实 handler）
/// 在 DOD-3 宿主接线时叠加，见 docs/designs/compat-pack-interface.md §5.3。
/// </summary>
public sealed class CompatPackValidationContext
{
    public CompatPackValidationContext(
        int engineModuleApiVersion,
        IReadOnlySet<string> knownCapabilityIds,
        IReadOnlySet<string> knownBuiltinVariantNames,
        IReadOnlySet<string> baselineInstructions,
        IReadOnlySet<string> baselineFunctions)
    {
        if (engineModuleApiVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(engineModuleApiVersion));
        EngineModuleApiVersion = engineModuleApiVersion;
        KnownCapabilityIds = knownCapabilityIds ?? throw new ArgumentNullException(nameof(knownCapabilityIds));
        KnownBuiltinVariantNames = knownBuiltinVariantNames ?? throw new ArgumentNullException(nameof(knownBuiltinVariantNames));
        BaselineInstructions = baselineInstructions ?? throw new ArgumentNullException(nameof(baselineInstructions));
        BaselineFunctions = baselineFunctions ?? throw new ArgumentNullException(nameof(baselineFunctions));
    }

    /// <summary>引擎当前 ModuleApiVersion（与 DialectModuleDefinition.ModuleApiVersion 同源）。</summary>
    public int EngineModuleApiVersion { get; }

    /// <summary>capability id 词汇表（引擎能力实现库已收录的 id）；包声明未知 id 即拒载。</summary>
    public IReadOnlySet<string> KnownCapabilityIds { get; }

    /// <summary>内置变体名（形如 builtin:setbgimage-snake；宿主桥维护与 LegacyInstructionVariant 的映射）。</summary>
    public IReadOnlySet<string> KnownBuiltinVariantNames { get; }

    /// <summary>v24 基线指令名（规范化大写）；hide 对账基准。</summary>
    public IReadOnlySet<string> BaselineInstructions { get; }

    /// <summary>v24 基线表达式函数名；hide 对账基准。</summary>
    public IReadOnlySet<string> BaselineFunctions { get; }
}
