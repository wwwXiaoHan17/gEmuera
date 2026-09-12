namespace GEmuera.Core.Compatibility;

/// <summary>
/// 内置 eraBlue（碧蓝度假村）兼容包。血统证据：游戏自带启动器
/// Emuera.NET 1824+v24+EMv18+EEv55.exe 与 EmueraFL_v1.45-preview.exe——
/// v24(EM/EE) 基座，非 snake 系；ERB 实测只用一条 snake 系指令 SETANIMETIMER
///（おさわりエフェクト.ERB:69 等），插件经 CALLSHARP 调用（NEWGAME.ERB:99
/// CALLSHARP LAUNCH_BROWSER(...)），函数面零 snake 依赖。
/// 插件加载本体（Plugins/*.dll 每插件独立 ALC）是宿主能力，不由此模块门控；
/// 本模块在方言面上声明该依赖（capability），使 [LOAD] 账本与诊断可核对。
/// </summary>
public static class EraBlueCompatibilityModule
{
    public const string ModuleId = "game.erablue";
    public const string ModuleVersion = "1.0.0";
    public const string BaseModuleId = "gemuera.v24";

    /// <summary>
    /// 外部插件程序集加载能力（Phase C：每插件独立 AssemblyLoadContext +
    /// Emuera 1.824 契约程序集重定向 + 容错 GetTypes）。
    /// </summary>
    public const string ExternalPluginCapability = "plugin.external-assembly.v1";

    /// <summary>
    /// 启动容错 quirk（与 snake 同源，见 SnakeCompatibilityCapabilities）：
    /// 汉化 mod 存在 v24 严格文法下解释不了的行，游戏自带 emuera.config 不开
    /// CompatiErrorLine，其原生 EM/EE 启动器家族语义即继续运行（实测
    /// 2026-09-12：v24 基座无此 quirk 时走致命退出路径，标题菜单不可达）。
    /// </summary>
    public const string ContinueAfterStartupFaultCapability =
        SnakeCompatibilityCapabilities.ContinueAfterStartupFault;

    public static DialectModuleDefinition CreateDefinition()
    {
        return new DialectModuleDefinition(
            ModuleId,
            ModuleVersion,
            1,
            dependencies: new[]
            {
                new ModuleDependencySnapshot(BaseModuleId, "[1.0.0,2.0.0)"),
            });
    }

    public static CompatibilityProfileDefinition CreateProfile()
    {
        return new CompatibilityProfileDefinition(
            "erablue",
            new[] { ModuleId },
            requiredCapabilityIds: new[]
            {
                ExternalPluginCapability,
                ContinueAfterStartupFaultCapability,
            });
    }
}
