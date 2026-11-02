using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 加载器校验所需的宿主输入（fail-closed 语义校验的数据源）。结构校验在契约程序集的
/// CompatPackManifest 内完成；本上下文由宿主在会话组装期提供：capability 词汇表、
/// 内置变体名录、v24 基线表面（对账用）、引擎 handler 名全集（register 对账用）。
/// </summary>
public sealed class CompatPackValidationContext
{
    static readonly IReadOnlySet<string> NoReservedModules = new HashSet<string>(StringComparer.Ordinal);
    static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NoBuiltinVariantInstructions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
    static readonly IReadOnlySet<string> NoHandlers = new HashSet<string>(StringComparer.Ordinal);
    static readonly IReadOnlyDictionary<string, string> NoFunctionReturnTypes =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public CompatPackValidationContext(
        CompatVersion currentContractAbi,
        CompatVersion currentEngineApi,
        IReadOnlySet<string> knownCapabilityIds,
        IReadOnlySet<string> knownBuiltinVariantNames,
        IReadOnlySet<string> baselineInstructions,
        IReadOnlySet<string> baselineFunctions,
        IReadOnlySet<string>? reservedModuleIds = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? knownBuiltinVariantInstructions = null,
        IReadOnlySet<string>? knownInstructionHandlers = null,
        IReadOnlySet<string>? knownFunctionHandlers = null,
        IReadOnlyDictionary<string, string>? knownFunctionReturnTypes = null,
        string? baselineProfileId = null,
        string? baselineSurfaceHash = null)
    {
        // 引擎当前版本是宿主常量，0.x / 负 minor 只可能是接线错误：与旧 int 版本一样 fail-fast。
        if (currentContractAbi.Major <= 0 || currentContractAbi.Minor < 0)
            throw new ArgumentOutOfRangeException(nameof(currentContractAbi));
        if (currentEngineApi.Major <= 0 || currentEngineApi.Minor < 0)
            throw new ArgumentOutOfRangeException(nameof(currentEngineApi));
        CurrentContractAbi = currentContractAbi;
        CurrentEngineApi = currentEngineApi;
        KnownCapabilityIds = knownCapabilityIds ?? throw new ArgumentNullException(nameof(knownCapabilityIds));
        KnownBuiltinVariantNames = knownBuiltinVariantNames ?? throw new ArgumentNullException(nameof(knownBuiltinVariantNames));
        BaselineInstructions = baselineInstructions ?? throw new ArgumentNullException(nameof(baselineInstructions));
        BaselineFunctions = baselineFunctions ?? throw new ArgumentNullException(nameof(baselineFunctions));
        ReservedModuleIds = reservedModuleIds ?? NoReservedModules;
        KnownBuiltinVariantInstructions = knownBuiltinVariantInstructions ?? NoBuiltinVariantInstructions;
        KnownInstructionHandlers = knownInstructionHandlers ?? NoHandlers;
        KnownFunctionHandlers = knownFunctionHandlers ?? NoHandlers;
        KnownFunctionReturnTypes = knownFunctionReturnTypes ?? NoFunctionReturnTypes;
        BaselineProfileId = string.IsNullOrWhiteSpace(baselineProfileId) ? "v24pure" : baselineProfileId.Trim();
        BaselineSurfaceHash = string.IsNullOrWhiteSpace(baselineSurfaceHash)
            ? LegacySurfaceHash.ComputeV24SurfaceHash()
            : baselineSurfaceHash.Trim();
    }

    /// <summary>引擎当前契约 ABI 版本（Emuera.CompatPack 公开契约面；v1 = 1.0）。</summary>
    public CompatVersion CurrentContractAbi { get; }

    /// <summary>引擎当前行为/能力契约版本（v1 = 1.0；破坏性变更升 major，增量升 minor）。</summary>
    public CompatVersion CurrentEngineApi { get; }

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

    /// <summary>
    /// 引擎指令 handler 名全集（register 对账基准）。包注册的指令名必须落在其中——否则
    /// 名字虽进 plan/哈希，真实注册表投影时无 handler 可绑定，运行期"未知指令"静默无效，
    /// 违反 fail-at-load。缺省空集 = 跳过该对账（测试场景）；宿主必须传入真实全集
    ///（引擎侧任何会话可暴露的指令名并集，内置模块可 Declare 的名字天然包含在内）。
    /// </summary>
    public IReadOnlySet<string> KnownInstructionHandlers { get; }

    /// <summary>
    /// 引擎表达式函数 handler 名全集（register 对账基准），语义与
    /// <see cref="KnownInstructionHandlers"/> 对称：函数注册名无真实 handler 即拒载。
    /// </summary>
    public IReadOnlySet<string> KnownFunctionHandlers { get; }

    /// <summary>
    /// 引擎表达式函数名 → 真实返回类型（宿主传入时用于 manifest-only 表面贡献生成
    /// FunctionDescriptor；缺失时规则层拒载，避免 plan 元数据与 handler 不一致）。
    /// </summary>
    public IReadOnlyDictionary<string, string> KnownFunctionReturnTypes { get; }

    /// <summary>包声明的基线 profile 必须与会话基线一致；缺省 v24pure。</summary>
    public string BaselineProfileId { get; }

    /// <summary>
    /// 当前引擎侧的 v24 基线表面快照哈希（<see cref="LegacySurfaceHash.ComputeV24SurfaceHash"/>，
    /// 生成清单的规范化 SHA256）。manifest 声明非空 <c>baseSurfaceHash</c> 且与本值不一致即拒载
    ///（精确对账）；缺省（宿主未传/null/空白）取 <see cref="LegacySurfaceHash.ComputeV24SurfaceHash"/>
    /// 的现算值，测试场景可显式传入自定基准。
    /// </summary>
    public string BaselineSurfaceHash { get; }
}
