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
        });
}
