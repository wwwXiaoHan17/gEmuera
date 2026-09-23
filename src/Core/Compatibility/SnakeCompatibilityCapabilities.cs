using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// snake 方言的 quirk capability 账本：每个 id 对应一处已收编的语义分叉，
/// 由 snake profile 声明进 CompatibilityPlan.CapabilityIds；legacy 桥的
/// ISnakeCompatibilityPolicy 逐属性从该集合派生（映射穷尽性由 SurfaceSmoke
/// 反射断言把关——加 policy 属性而不加 id 会在冒烟时暴露）。
/// 算法型差异（如文法族选择）属模块选择语义（IsEnabled），不在此列。
/// </summary>
public static class SnakeCompatibilityCapabilities
{
    /// <summary>解析警告去重写入 snake 启动错误日志（ParserMediator）。</summary>
    public const string ParserDiagnostics = "parse.parser-diagnostics.v1";

    /// <summary>允许用户自定义变量按名解析，无需 Config.UseERD（ExpressionParser/HeaderFileLoader）。</summary>
    public const string UserDefinedVariableResolution = "parse.user-defined-variable-resolution.v1";

    /// <summary>VARI/VARS 声明先注册为私有变量，声明可在使用之后（ErbLoader）。</summary>
    public const string ScopedVariablePreRegistration = "parse.scoped-variable-pre-registration.v1";

    /// <summary>放行 snake 私有参数形态（ErbLoader）。</summary>
    public const string PrivateArguments = "parse.private-arguments.v1";

    /// <summary>CALL 多余实参不报错（Process.CalledFunction）。</summary>
    public const string ExtraCallArguments = "call.extra-arguments.v1";

    /// <summary>启动期错误行后继续运行（Process.SystemProc）。</summary>
    public const string ContinueAfterStartupFault = "startup.continue-after-fault.v1";

    /// <summary>低于 60fps 时使用 snake 快速显示刷新策略（EmueraConsole）。</summary>
    public const string FastDisplayRefresh = "display.fast-refresh.v1";

    /// <summary>TIMES 结果钳制到 [0,1000] 并打印越界警告（v24 参考为 unchecked 直转）。</summary>
    public const string TimesClamp = "math.times-clamp.v1";

    /// <summary>RAND 最大值≤最小值时钳制为下界并告警一次（v24 参考为 CodeEE 致命错误）。</summary>
    public const string RandClamp = "math.rand-clamp.v1";

    /// <summary>PRINTBUTTON 等蒙版混合读取 Alpha 通道（v24 参考读 B 通道）。</summary>
    public const string MaskAlphaChannel = "graphics.mask-alpha-channel.v1";

    /// <summary>字面量 \e 为两字符转义序列（v24 参考中 e 按普通字符追加）。</summary>
    public const string EscapeESequence = "lex.escape-e-two-char.v1";

    /// <summary>THROW 触发 BEFORE_THROW 事件（禁用配置除外）；v24 参考直接抛出终止。</summary>
    public const string ThrowEvent = "flow.throw-event.v1";

    /// <summary>系统错误触发 BEFORE_ERROR 事件；v24 参考无此事件机制。</summary>
    public const string BeforeErrorEvent = "flow.before-error-event.v1";

    /// <summary>RANDOMIZE 指令按 Config.UseNewRand 重建随机源（v24 参考仅复位种子）。</summary>
    public const string RandomizeReseed = "random.reseed-on-randomize.v1";

    /// <summary>relationDic 不注册 Mastername（snake 参考走独立映射；v24 参考同时注册）。</summary>
    public const string RelationWithoutMastername = "csv.relation-without-mastername.v1";

    /// <summary>#DIM 声明支持 OUT 关键字。与 eraFL 模块同名共享 id（declare.out-keyword.v1）：
    /// erafl 会话经自身 AllowsOutKeyword 派生放行（EraFlCompatibilityModule.OutKeywordBehavior）。</summary>
    public const string OutKeyword = "declare.out-keyword.v1";

    /// <summary>函数声明行剥除 VARIADIC 变长参数标记（v24 参考按普通标识符解析）。</summary>
    public const string VariadicStrip = "parse.variadic-strip.v1";

    /// <summary>
    /// float 类型系统全族入口：词法浮点字面量、RESULTF/LOCALF/ARGF 变量注册、
    /// #DIMF/#REFF/#LOCALFSIZE/#FUNCTIONF（ERB 与 ERH）。v24 参考无任何浮点产物
    /// （emuera.em-master grep 为空）。eraFL 0.47 无源码且实测全库零浮点使用
    /// （2026-09-22 扫描），不随本能力放行——erafl 模块如需可自行声明。
    /// </summary>
    public const string FloatTypeSystem = "type.float-system.v1";

    /// <summary>
    /// ISnakeCompatibilityPolicy（除 IsEnabled 外）与 capability id 的一一映射。
    /// 属性名以字符串给出，由 SurfaceSmoke 反射 legacy 桥接口校验真实存在
    ///（拼写错误会在冒烟时暴露，不会静默漂移）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PolicyPropertyByCapabilityId =
        new ReadOnlyDictionary<string, string>(
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal)
            {
                [ParserDiagnostics] = "UsesParserDiagnostics",
                [UserDefinedVariableResolution] = "AllowsUserDefinedVariableResolution",
                [ScopedVariablePreRegistration] = "AllowsScopedVariablePreRegistration",
                [PrivateArguments] = "AllowsPrivateArguments",
                [ExtraCallArguments] = "AllowsExtraCallArguments",
                [ContinueAfterStartupFault] = "ContinuesAfterStartupFault",
                [FastDisplayRefresh] = "UsesFastDisplayRefresh",
                [TimesClamp] = "UsesTimesClamp",
                [RandClamp] = "UsesRandClamp",
                [MaskAlphaChannel] = "UsesMaskAlphaChannel",
                [EscapeESequence] = "UsesEscapeESequence",
                [ThrowEvent] = "UsesThrowEvent",
                [BeforeErrorEvent] = "UsesBeforeErrorEvent",
                [RandomizeReseed] = "UsesRandomizeReseed",
                [RelationWithoutMastername] = "UsesRelationWithoutMastername",
                [OutKeyword] = "AllowsOutKeyword",
                [VariadicStrip] = "UsesVariadicStrip",
                [FloatTypeSystem] = "UsesFloatTypeSystem",
            });

    public static readonly IReadOnlyList<string> RequiredCapabilityIds =
        Array.AsReadOnly(new[]
        {
            ParserDiagnostics,
            UserDefinedVariableResolution,
            ScopedVariablePreRegistration,
            PrivateArguments,
            ExtraCallArguments,
            ContinueAfterStartupFault,
            FastDisplayRefresh,
            TimesClamp,
            RandClamp,
            MaskAlphaChannel,
            EscapeESequence,
            ThrowEvent,
            BeforeErrorEvent,
            RandomizeReseed,
            RelationWithoutMastername,
            OutKeyword,
            VariadicStrip,
            FloatTypeSystem,
        });
}
