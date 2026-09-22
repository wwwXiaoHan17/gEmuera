namespace GEmuera.Core.Compatibility;

/// <summary>
/// 内置 snake 兼容模块（lazy loading fork，Skiav12.1 血统）。
/// 会话闭包 = v24 基座 + 本模块：指令/函数表面差集由生成清单镜像（见
/// LegacyDialectInventories.SnakeDelta*），18 项 quirk capability 账本驱动
/// legacy 桥的 ISnakeCompatibilityPolicy 逐属性派生（映射穷尽性由 SurfaceSmoke
/// 反射断言把关）。文法族选择（VARI/VARS 动态路径、FOR 共享表内核、PRINTC 像素
/// 分栏算法）属模块选择语义（IsEnabled），不在 capability 账本内。
/// </summary>
public static class SnakeCompatibilityModule
{
    public const string ModuleId = "game.snake";
    public const string ModuleVersion = "1.0.0";
    public const string BaseModuleId = "gemuera.v24";

    // 迁移外观的声明专用端口类型 id：不提供 policy 值，也不绑定 legacy Parser/VM；
    // 计划要暴露某行为时仍需显式不可变 BehaviorPortSnapshot。
    public static readonly string[] PortTypeIds =
    {
        "IExtraArgumentPolicy",
        "IPrivateArgumentShapePolicy",
        "IEffectiveDisplayConfigurationProjection",
        "IDisplayRefreshTimingPolicy",
        "IExpressionFunctionCatalog",
        "IInstructionCatalogBuilder",
        "IStartupFaultPolicy",
        "IUserVariableResolutionPolicy",
        "IParserDiagnosticsSinkPolicy",
        "IResourceLazyIndexPolicy",
    };

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
            portTypeIds: PortTypeIds);
    }

    public static CompatibilityProfileDefinition CreateProfile()
    {
        return new CompatibilityProfileDefinition(
            "snake",
            new[] { ModuleId },
            requiredCapabilityIds: SnakeCompatibilityCapabilities.RequiredCapabilityIds);
    }
}
