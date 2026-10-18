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
    // P4：HTML <font color> 的颜色名解析对齐 System.Drawing 的标准调色板（141 名）。
    // 父项目参考实现用 System.Drawing.Color.FromName，认识全部标准名；本仓垫片只实现了
    // 32 个，其余落到 Black 回退——eraMegaten 汉化版背景为黑（emuera.config
    // BACKGROUND COLOR:0,0,0），Violet/Brown/ForestGreen 等名字因此渲染成黑底黑字而
    // "消失"（2026-10-03 实测：PARTY 面板的名字、当前HP/MP、状态串全部隐形）。
    // 未声明该 behavior 的会话保持 32 名表，逐点等价于基线。
    public const string StandardColorNameBehavior = "ui.standard-color-name.v1";
    // eraMegaten 的 TOSTR_HTML 将普通 RGB 值格式化为 #00RRGGBB；原引擎
    // 将该形式作为不透明 RGB 处理，否则 PARTY 的 HP/MP 当前值会变成全透明。
    public const string HtmlRgbZeroAlphaBehavior = "ui.html-rgb-zero-alpha.v1";

    /// <summary>
    /// 启动容错 quirk（与 snake/erablue 同源）：私改文法行在严格 v24 下解释不了时
    /// 继续运行（实测 2026-09-12，见类注释；详见 SnakeCompatibilityCapabilities）。
    /// </summary>
    public const string ContinueAfterStartupFaultCapability =
        SnakeCompatibilityCapabilities.ContinueAfterStartupFault;

    // 五个策略端口类型 id（erafl 用 5 个具名端口；megaten 有 5 个门控行为）。
    public const string LabelLookupPortType = "ILabelLookupCasePolicy";
    public const string OutNamePortType = "IOutNamePolicy";
    public const string PrivateShadowPortType = "IPrivateShadowPolicy";
    public const string StandardColorNamePortType = "IStandardColorNamePolicy";
    public const string HtmlRgbZeroAlphaPortType = "IHtmlRgbZeroAlphaPolicy";

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
            new BehaviorPortSnapshot(
                StandardColorNameBehavior,
                StandardColorNamePortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "ui.standard-color-name-policy.v1",
                ModuleId,
                "DIA-MEGATEN-04"),
            new BehaviorPortSnapshot(
                HtmlRgbZeroAlphaBehavior,
                HtmlRgbZeroAlphaPortType,
                PortContractKind.PolicyDecision,
                ModuleId,
                "ui.html-rgb-zero-alpha-policy.v1",
                ModuleId,
                "DIA-MEGATEN-05"),
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
                StandardColorNamePortType,
                HtmlRgbZeroAlphaPortType,
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
                LabelLookupCaseBehavior,
                RefOutNameBehavior,
                PrivateSystemShadowBehavior,
                StandardColorNameBehavior,
                HtmlRgbZeroAlphaBehavior,
            },
            defaultSaveProfileId: SaveProfileId);
    }
}
