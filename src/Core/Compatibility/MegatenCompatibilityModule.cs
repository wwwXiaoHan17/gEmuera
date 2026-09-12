using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// 内置 eraMegaten 兼容包。两条独立取证线合并（2026-09-07 524122 / 2026-09-12 本仓）：
///  - 大小写/遮蔽三端口（eraMegaten 3.54 汉化版，code 666 宿主）：IC 配置组合
///    「大文字小文字の違いを無視する:YES + 関数・属性については大文字小文字を
///    無視しない:YES」下 823 条解釈できない警告拒跑——标签查询大小写跟随 ICVariable、
///    REF-OUT 名字位、私有 #DIM 遮蔽内置降级，13 处引擎门控（Disabled 下与基线逐点等价）。
///  - 启动容错 quirk（Var.157_3.43 安卓版，Emuera1824+v8.1 宿主）：8396 ERB 全库扫描
///    方言外名零使用；VELVET_ROOM.ERB:2860 的 DITEMTYPE:ARG:Persona(LOCALS) 私改文法
///    在严格 v24 下致命退出，原生启动器语义即容错继续——与 erablue 同款证据形态。
/// 面增量：无（两版游戏均零方言外名使用；启动器方言面无源码不臆测裁剪）。
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

    /// <summary>
    /// 启动容错 quirk（与 snake/erablue 同源）：私改文法行在严格 v24 下解释不了时
    /// 继续运行（实测 2026-09-12，见类注释；详见 SnakeCompatibilityCapabilities）。
    /// </summary>
    public const string ContinueAfterStartupFaultCapability =
        SnakeCompatibilityCapabilities.ContinueAfterStartupFault;

    // 三个策略端口类型 id（erafl 用 5 个具名端口；megaten 只有 3 个门控行为）。
    public const string LabelLookupPortType = "ILabelLookupCasePolicy";
    public const string OutNamePortType = "IOutNamePolicy";
    public const string PrivateShadowPortType = "IPrivateShadowPolicy";

    private static readonly ReadOnlyCollection<BehaviorPortSnapshot> DefaultBehaviorPorts =
        Array.AsReadOnly(new[]
        {
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
            requiredCapabilityIds: new[]
            {
                ContinueAfterStartupFaultCapability,
            },
            defaultSaveProfileId: SaveProfileId);
    }
}
