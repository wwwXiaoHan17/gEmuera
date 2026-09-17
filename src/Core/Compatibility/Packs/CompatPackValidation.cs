namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 加载器校验所需的宿主输入（fail-closed 语义校验的数据源）。结构校验在契约程序集的
/// CompatPackManifest 内完成；本上下文由宿主在会话组装期提供：capability 词汇表、
/// 内置变体名录、v24 基线表面（对账用）。引擎注册表级对账（add 名单须有真实 handler）
/// 在 DOD-3 宿主接线时叠加，见 docs/designs/compat-pack-interface.md §5.3。
/// </summary>
public sealed class CompatPackValidationContext
{
    static readonly IReadOnlySet<string> NoReservedModules = new HashSet<string>(StringComparer.Ordinal);
    static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NoBuiltinVariantInstructions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

    public CompatPackValidationContext(
        int engineModuleApiVersion,
        IReadOnlySet<string> knownCapabilityIds,
        IReadOnlySet<string> knownBuiltinVariantNames,
        IReadOnlySet<string> baselineInstructions,
        IReadOnlySet<string> baselineFunctions,
        IReadOnlySet<string>? reservedModuleIds = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? knownBuiltinVariantInstructions = null)
    {
        if (engineModuleApiVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(engineModuleApiVersion));
        EngineModuleApiVersion = engineModuleApiVersion;
        KnownCapabilityIds = knownCapabilityIds ?? throw new ArgumentNullException(nameof(knownCapabilityIds));
        KnownBuiltinVariantNames = knownBuiltinVariantNames ?? throw new ArgumentNullException(nameof(knownBuiltinVariantNames));
        BaselineInstructions = baselineInstructions ?? throw new ArgumentNullException(nameof(baselineInstructions));
        BaselineFunctions = baselineFunctions ?? throw new ArgumentNullException(nameof(baselineFunctions));
        ReservedModuleIds = reservedModuleIds ?? NoReservedModules;
        KnownBuiltinVariantInstructions = knownBuiltinVariantInstructions ?? NoBuiltinVariantInstructions;
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

    /// <summary>
    /// 内置方言模块保留名（gemuera.v24/game.snake 等）。packId 撞保留名即拒载——否则
    /// 组装后 Compose 的白名单校验会以未捕获异常炸掉会话绑定（违反降级不变量）。
    /// 缺省为空集（测试场景）；宿主必须传入真实保留名全集。
    /// </summary>
    public IReadOnlySet<string> ReservedModuleIds { get; }

    /// <summary>
    /// 内置变体的组合级名录（变体名 → 该变体可作用的指令集）。缺省空表 = 只做名字级校验
    /// （<see cref="KnownBuiltinVariantNames"/>）；宿主必须传入真实组合表——
    /// builtin:snake 只对 SETBGIMAGE/FOR 等注册过 handler 变体，选其它指令必须在校验段拒载。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> KnownBuiltinVariantInstructions { get; }
}
