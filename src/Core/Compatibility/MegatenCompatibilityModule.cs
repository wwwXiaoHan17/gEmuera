using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// 内置 eraMegaten 兼容包（eraFL 模式的完整镜像骨架）。纯声明模块：
/// 只描述端口与依赖闭包，不包含任何算法静态方法；全部行为差异由
/// legacy bridge 侧的 IMegatenCompatibilityPolicy 在会话级门控，
/// 保证 v24pure/snake/erafl 三个既有 profile 零行为变化。
/// </summary>
public static class MegatenCompatibilityModule
{
    public const string ModuleId = "game.megaten";
    public const string ModuleVersion = "1.0.0";
    public const string BaseModuleId = "gemuera.v24";
    public const string SaveProfileId = "gemuera.megaten";

    // P1：函数标签查询大小写归一化跟随 ICVariable（定义侧存大写、查询侧同样转大写）。
    public const string LabelLookupCaseBehavior = "parser.label-lookup-case.v1";
    // P2：#DIM REF OUT 中 REF 前缀后的 OUT 允许作为变量名（名字位）。
    public const string RefOutNameBehavior = "parser.ref-out-name.v1";
    // P3：私有 #DIM 遮蔽内置变量（SystemVariable 冲突）降为警告级 1。
    public const string PrivateSystemShadowBehavior = "parser.private-system-shadow.v1";

    // 三个策略端口类型 id（erafl 用 5 个具名端口；megaten 只有 3 个门控行为）。
    public const string LabelLookupPortType = "ILabelLookupCasePolicy";
    public const string OutNamePortType = "IOutNamePolicy";
    public const string PrivateShadowPortType = "IPrivateShadowPolicy";

    private static readonly ReadOnlyCollection<BehaviorPortSnapshot> DefaultBehaviorPorts =
        Array.AsReadOnly(new[]
        {
            // 证据号占位：待 eraMegaten 实测日志补齐后替换为正式 DIA-MEGATEN-* 编号。
            new BehaviorPortSnapshot(
                LabelLookupCaseBehavior,
                LabelLookupPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "parser.label-lookup-case-policy.v1",
                ModuleId,
                "DIA-MEGATEN-01"),
            new BehaviorPortSnapshot(
                RefOutNameBehavior,
                OutNamePortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "parser.ref-out-name-policy.v1",
                ModuleId,
                "DIA-MEGATEN-02"),
            new BehaviorPortSnapshot(
                PrivateSystemShadowBehavior,
                PrivateShadowPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "parser.private-shadow-policy.v1",
                ModuleId,
                "DIA-MEGATEN-03"),
        });

    public static IReadOnlyList<BehaviorPortSnapshot> DefaultPorts => DefaultBehaviorPorts;

    public static DialectModuleDefinition CreateDefinition()
    {
        return new DialectModuleDefinition(
            ModuleId,
            ModuleVersion,
            1,
            dependencies: new[]
            {
                new ModuleDependencySnapshot(BaseModuleId, "[1.0.0,2.0.0)"),
            },
            portTypeIds: new[]
            {
                LabelLookupPortType,
                OutNamePortType,
                PrivateShadowPortType,
            });
    }

    public static CompatibilityProfileDefinition CreateProfile()
    {
        return new CompatibilityProfileDefinition(
            "megaten",
            new[] { ModuleId },
            defaultPorts: DefaultPorts,
            requiredCapabilityIds: Array.Empty<string>(),
            defaultSaveProfileId: SaveProfileId);
    }
}
