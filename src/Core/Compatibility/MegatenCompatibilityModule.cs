namespace GEmuera.Core.Compatibility;

/// <summary>
/// 内置 era megaten 兼容包。血统证据：游戏自带启动器 Emuera1824+v8.1.exe
///（"根据 Emuera 改良后的 Emuera 启动器"，私家魔改、无源码）；8396 个 ERB
/// 全库扫描（2026-09-12）：snake 系指令/函数、v24 后增指令零使用——纯
/// vanilla-1824 用法面。实测（v24pure 无 quirk 时）：加载至 VELVET_ROOM.ERB:2860
/// 撞"DITEMTYPE:ARG:Persona(LOCALS)"（变量索引内嵌函数调用，其私改文法）
/// 致命退出；原生启动器语义即容错继续——与 erablue 同款 quirk 证据形态。
/// 面增量：无（启动器方言面无源码不臆测裁剪；方言外名零使用）。
/// </summary>
public static class MegatenCompatibilityModule
{
    public const string ModuleId = "game.megaten";
    public const string ModuleVersion = "1.0.0";
    public const string BaseModuleId = "gemuera.v24";

    /// <summary>
    /// 启动容错 quirk（与 snake/erablue 同源）：私改文法行在严格 v24 下解释不了，
    /// 原生 Emuera1824+v8.1 启动器继续运行（实测 2026-09-12，详见类注释）。
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
            "megaten",
            new[] { ModuleId },
            requiredCapabilityIds: new[]
            {
                ContinueAfterStartupFaultCapability,
            });
    }
}
