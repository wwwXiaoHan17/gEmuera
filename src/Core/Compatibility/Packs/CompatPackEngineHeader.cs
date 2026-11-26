using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 清单头部规则的编号：只凭清单本身与 <see cref="CompatPackEngineHeader"/> 即可判定的拒载规则。
/// 规则层（<see cref="CompatPackRules.Validate"/>）与「只看清单、不执行」的预检
/// （<see cref="CompatPackManifestPeek"/>）共用同一份判定（<see cref="CompatPackRules.CollectHeaderErrors"/>），
/// 本编号供调用方归类提示；<see cref="None"/> 表示没有命中任何规则。
/// </summary>
public enum CompatPackHeaderRule
{
    /// <summary>没有命中任何头部规则。</summary>
    None = 0,

    /// <summary>packId 撞内置方言模块保留名。</summary>
    ReservedModuleId,

    /// <summary>baseProfileId 不是 v24pure（v1 契约面只允许叠加 v24pure）。</summary>
    BaseProfileNotV24Pure,

    /// <summary>baseProfileId 与当前会话基线档案不一致。</summary>
    BaseProfileMismatch,

    /// <summary>带表面差量或变体选择却未声明 baseSurfaceHash。</summary>
    SurfaceHashMissing,

    /// <summary>声明的 baseSurfaceHash 与当前基线表面快照不一致（包按旧表面快照发布）。</summary>
    SurfaceHashMismatch,

    /// <summary>contractAbi 轴不满足：major 不等或包 minor 高于引擎 minor。</summary>
    ContractAbiMismatch,

    /// <summary>engineApi 轴不满足：major 不等或包 minor 高于引擎 minor。</summary>
    EngineApiMismatch,

    /// <summary>声明了 v1 尚未接线的 saveProfileId。</summary>
    SaveProfileDeclared,
}

/// <summary>一条头部规则错误：规则编号 + 规则层原样输出的错误文本。</summary>
public readonly record struct CompatPackHeaderError(CompatPackHeaderRule Rule, string Message);

/// <summary>
/// 引擎「头部」契约值——判定一个包是否必然被拒载所需的那部分引擎事实：
/// 双轴版本、基线档案、基线表面快照哈希与内置保留名。由宿主从完整校验上下文投影
/// （<see cref="FromContext"/>），供加载前预检与第一方包释放策略使用，避免为了「看一眼清单」
/// 构造整个 <see cref="CompatPackValidationContext"/> 或装载包程序集。
/// </summary>
public sealed record CompatPackEngineHeader(
    CompatVersion ContractAbi,
    CompatVersion EngineApi,
    string BaselineProfileId,
    string BaselineSurfaceHash,
    IReadOnlySet<string> ReservedModuleIds)
{
    /// <summary>从加载校验使用的完整上下文投影；与规则层取同一组值，保证预检与真实加载同源。</summary>
    public static CompatPackEngineHeader FromContext(CompatPackValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new CompatPackEngineHeader(
            context.CurrentContractAbi,
            context.CurrentEngineApi,
            context.BaselineProfileId,
            context.BaselineSurfaceHash,
            context.ReservedModuleIds);
    }
}
