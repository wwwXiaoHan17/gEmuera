using Emuera.Compatibility.Packs;

namespace CompatPackContractOnlyFixture;

/// <summary>
/// 契约夹具入口：只依赖 Emuera 契约程序集即可实现的最小 ICompatPack 样本，
/// 供 ALC 正向加载用例复用（对照 manifest-only 数据包的分叉路径）。
/// 清单经 CompatPackManifest.TryLoadFromAssembly 从内嵌资源自读，保证入口类自报
/// 的清单与内嵌清单同源（加载器 ValidatePackEntry 会核对 packId/packVersion 一致）。
/// </summary>
public sealed class ContractOnlyPack : ICompatPack
{
	public ContractOnlyPack()
	{
		if (!CompatPackManifest.TryLoadFromAssembly(typeof(ContractOnlyPack).Assembly, out var manifest, out _))
			throw new InvalidOperationException("契约夹具内嵌清单缺失。");
		Manifest = manifest!;
	}

	public CompatPackManifest Manifest { get; }

	public IReadOnlyList<ICompatPackContribution> Contributions { get; } = new ICompatPackContribution[]
	{
		new ContractOnlySurfaceContribution(),
	};
}

/// <summary>最小表面贡献：注册一个 v24 基线外指令，验证入口类贡献可经 ALC 加载并被表面收集。</summary>
public sealed class ContractOnlySurfaceContribution : ISurfaceContribution
{
	public string ContributionId => "community.contract-fixture.surface";

	public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
	{
		instructions.RegisterInstruction("SETANIMETIMER");
	}
}
