namespace GEmuera.Core.Compatibility;

/// <summary>
/// Minimal built-in module declarations used by the migration facade. They are
/// compatibility-plan inputs only; legacy parser registrations remain owned by
/// the old runtime until the M2/M3 evidence gates are closed.
/// </summary>
public static class BuiltInDialectCatalog
{
    // snake 的声明专用端口类型 id 已随 SnakeCompatibilityModule 成模组化
    //（SnakeCompatibilityModule.PortTypeIds），此处不再内联。

    public static DialectModuleCatalog CreateLegacyBaseline()
    {
        var catalog = new DialectModuleCatalog();
        catalog.Register(new DeclaredDialectModule(
            new DialectModuleDefinition("gemuera.v24", "1.0.0", 1),
            V24Contributions()));
        catalog.Register(new DeclaredDialectModule(
            SnakeCompatibilityModule.CreateDefinition(),
            SnakeContributions()));
        catalog.Register(new DeclaredDialectModule(
            EraFlCompatibilityModule.CreateDefinition(),
            EraFlContributions()));
        // v18 基线方言（emuera1824+v18 血统）：独立基座（不依赖 gemuera.v24），会话表面 =
        // v24 引擎投影减去"v24 注册而 v18 参考未注册"的差集（桥层 LegacyV18CompatibilityModule）。
        catalog.Register(new DeclaredDialectModule(
            new DialectModuleDefinition("gemuera.v18", "1.0.0", 1),
            V18Contributions()));
        // eraBlue（碧蓝度假村）：v24 基座 + SETANIMETIMER 可见性增量 + 外部插件 capability 声明。
        catalog.Register(new DeclaredDialectModule(
            EraBlueCompatibilityModule.CreateDefinition(),
            EraBlueContributions()));
        // era megaten：v24 基座 + 启动容错 quirk（面零增量，证据见 MegatenCompatibilityModule）。
        catalog.Register(new DeclaredDialectModule(
            MegatenCompatibilityModule.CreateDefinition(),
            MegatenContributions()));
        return catalog;
    }

    // 描述符清单来自 LegacyDialectInventories.Generated.cs（引擎投影生成的镜像）。
    // v24 = v24pure 会话表面；snake/erafl = 相对 v24 表面的差集。计划闭包是所选模块
    // 清单的并集；个别 v24 名在 snake 会话被主动排除（如 BITMAP_CACHE_ENABLE 函数），
    // 会话驱动路由/校验对这类"计划⊇会话"的差异按条件可见性容忍。
    private static IDialectContribution[] V24Contributions() => new IDialectContribution[]
    {
        new LegacyInstructionInventoryContribution("gemuera.v24.instructions", "gemuera.v24", LegacyDialectInventories.V24InstructionNames),
        new LegacyFunctionInventoryContribution("gemuera.v24.functions", "gemuera.v24", LegacyDialectInventories.V24Functions),
    };

    private static IDialectContribution[] SnakeContributions() => new IDialectContribution[]
    {
        new LegacyInstructionInventoryContribution("game.snake.instructions", "game.snake", LegacyDialectInventories.SnakeDeltaInstructionNames),
        new LegacyFunctionInventoryContribution("game.snake.functions", "game.snake", LegacyDialectInventories.SnakeDeltaFunctions),
    };

    private static IDialectContribution[] EraFlContributions() => new IDialectContribution[]
    {
        new LegacyInstructionInventoryContribution("game.erafl.instructions", "game.erafl", LegacyDialectInventories.EraFlDeltaInstructionNames),
        new LegacyFunctionInventoryContribution("game.erafl.functions", "game.erafl", LegacyDialectInventories.EraFlDeltaFunctions),
    };

    private static IDialectContribution[] V18Contributions() => new IDialectContribution[]
    {
        new LegacyInstructionInventoryContribution("gemuera.v18.instructions", "gemuera.v18", LegacyDialectInventories.V18InstructionNames),
        new LegacyFunctionInventoryContribution("gemuera.v18.functions", "gemuera.v18", LegacyDialectInventories.V18Functions),
    };

    private static IDialectContribution[] EraBlueContributions() => new IDialectContribution[]
    {
        new LegacyInstructionInventoryContribution("game.erablue.instructions", "game.erablue", LegacyDialectInventories.EraBlueDeltaInstructionNames),
        new LegacyFunctionInventoryContribution("game.erablue.functions", "game.erablue", LegacyDialectInventories.EraBlueDeltaFunctions),
    };

    private static IDialectContribution[] MegatenContributions() => new IDialectContribution[]
    {
        new LegacyInstructionInventoryContribution("game.megaten.instructions", "game.megaten", LegacyDialectInventories.MegatenDeltaInstructionNames),
        new LegacyFunctionInventoryContribution("game.megaten.functions", "game.megaten", LegacyDialectInventories.MegatenDeltaFunctions),
    };

    /// <summary>
    /// Legacy launcher profile projection. It deliberately lists roots rather
    /// than a pre-expanded closure, so <see cref="DialectModuleCatalog"/>
    /// remains the single owner of dependency and version validation.
    /// </summary>
    public static CompatibilityProfileCatalog CreateLegacyProfileCatalog()
    {
        var catalog = new CompatibilityProfileCatalog();
        catalog.Register(new CompatibilityProfileDefinition(
            "v24pure",
            new[] { "gemuera.v24" }));
        catalog.Register(new CompatibilityProfileDefinition(
            "v18",
            new[] { "gemuera.v18" }));
        catalog.Register(EraBlueCompatibilityModule.CreateProfile());
        catalog.Register(MegatenCompatibilityModule.CreateProfile());
        catalog.Register(SnakeCompatibilityModule.CreateProfile());
        catalog.Register(EraFlCompatibilityModule.CreateProfile());
        return catalog;
    }

    /// <summary>
    /// Builds the single immutable plan accepted by the legacy bridge for a
    /// normal launcher session. Keeping this composition in Core means the
    /// Godot launcher supplies only a requested profile id; it cannot choose
    /// modules, ports, or a mutable registry surface.
    /// </summary>
    public static CompatibilityPlan CreateLegacySessionPlan(string profileId)
    {
        var profiles = CreateLegacyProfileCatalog();
        profiles.Freeze();
        var profile = profiles.Resolve(profileId);
        var modules = CreateLegacyBaseline();
        return new CompatibilityPlanBuilder(modules).Build(
            profile.ProfileId,
            profile.RootModuleIds,
            profile.DefaultPorts,
            profile.RequiredCapabilityIds,
            profile.DefaultSaveProfileId);
    }

    private sealed class DeclaredDialectModule : IDialectModule
    {
        public DeclaredDialectModule(
            DialectModuleDefinition definition,
            IReadOnlyList<IDialectContribution> contributions)
        {
            Definition = definition;
            Contributions = contributions;
        }

        public DialectModuleDefinition Definition { get; }
        public IReadOnlyList<IDialectContribution> Contributions { get; }
    }
}
