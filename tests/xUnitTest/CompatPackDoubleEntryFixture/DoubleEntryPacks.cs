using Emuera.Compatibility.Packs;

namespace CompatPackDoubleEntryFixture;

/// <summary>
/// M9 夹具：同一程序集声明两个 ICompatPack 实现类。加载器 ResolvePackEntry 必须拒载
/// （"恰好一个入口"），而不是按类型枚举顺序挑一个。本夹具故意不内嵌清单——它只用于
/// 直测 <see cref="CompatPackLoader.ResolvePackEntry(Assembly, CompatPackManifest, List{string})"/>
/// 的多入口分支，不参与真实程序集加载。
/// </summary>
public sealed class DoubleEntryPackA : ICompatPack
{
	public DoubleEntryPackA()
	{
		Manifest = CreateManifest();
	}

	public CompatPackManifest Manifest { get; }

	public IReadOnlyList<ICompatPackContribution> Contributions { get; } = Array.Empty<ICompatPackContribution>();

	internal static CompatPackManifest CreateManifest()
	{
		const string json = "{\"packId\":\"test.double-entry\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";
		return CompatPackManifest.TryParse(json, out var manifest, out var errors)
			? manifest!
			: throw new InvalidOperationException(string.Join("; ", errors));
	}
}

/// <summary>第二个入口（与 <see cref="DoubleEntryPackA"/> 一起构成"多入口必须拒载"的输入）。</summary>
public sealed class DoubleEntryPackB : ICompatPack
{
	public DoubleEntryPackB()
	{
		Manifest = DoubleEntryPackA.CreateManifest();
	}

	public CompatPackManifest Manifest { get; }

	public IReadOnlyList<ICompatPackContribution> Contributions { get; } = Array.Empty<ICompatPackContribution>();
}

/// <summary>仅为定位夹具程序集的 public 标记类型。</summary>
public static class DoubleEntryFixtureMarker
{
}
