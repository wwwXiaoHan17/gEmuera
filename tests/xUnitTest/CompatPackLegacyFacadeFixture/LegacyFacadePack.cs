using Emuera.Compatibility.Packs;

namespace CompatPackLegacyFacadeFixture;

/// <summary>
/// 旧 facade 包夹具入口（T8）：实现 ICompatPack + 表面/能力贡献，源码只使用
/// Emuera.Compatibility.Packs 契约类型，工程只引用 src/EmueraFacade/Emuera.csproj。
/// 本夹具用于证明两件事：
///   1. facade 预载时，拆分前形态（AssemblyRef=Emuera）的旧包能经 TypeForwardedTo
///      解析到宿主同一份契约类型，贡献进入冻结差量；
///   2. facade 未预载的干净进程里，加载器按 ALC 允许清单 fail-closed 拒载，并给出
///      "提供 Emuera facade" 的可操作提示（CompatPackLegacyFacadeProbe 验证）。
/// 贡献动作同时覆盖 add/hide 两向（SETANIMETIMER/CALLSHARP/SQL_CONNECT/EXISTVAR），
/// 与 HelloCompatPackFixture 的 v24 基线关系一致，便于复用同一 ValidationContext。
/// </summary>
public sealed class LegacyFacadePack : ICompatPack
{
    public LegacyFacadePack()
    {
        if (!CompatPackManifest.TryLoadFromAssembly(typeof(LegacyFacadePack).Assembly, out var manifest, out _))
            throw new InvalidOperationException("旧 facade 夹具的内嵌清单缺失。");
        Manifest = manifest!;
    }

    public CompatPackManifest Manifest { get; }

    public IReadOnlyList<ICompatPackContribution> Contributions { get; } = new ICompatPackContribution[]
    {
        new LegacyFacadeSurfaceContribution(),
        new LegacyFacadeCapabilityContribution(),
    };
}

public sealed class LegacyFacadeSurfaceContribution : ISurfaceContribution
{
    public string ContributionId => "community.legacy-facade.surface";

    public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
    {
        // add 基线外名字 + hide 基线内名字：两向都经真实 v24 基线对账。
        instructions.RegisterInstruction("SETANIMETIMER");
        instructions.HideInstruction("CALLSHARP");
        functions.RegisterFunction("SQL_CONNECT", "Int64");
        functions.HideFunction("EXISTVAR");
    }
}

public sealed class LegacyFacadeCapabilityContribution : ICapabilityContribution
{
    public string ContributionId => "community.legacy-facade.capabilities";

    public IReadOnlyList<string> CapabilityIds { get; } = new[] { "math.times-clamp.v1" };
}
