using System.Reflection;
using Xunit;

namespace EmueraPluginAbi.Tests;

/// <summary>
/// 插件 ABI 边界钉（Task 10 契约程序集拆分）：
///   1. facade（AssemblyName=Emuera, 1.824.0.0）只承载上游插件 ABI——CompatPack 契约类型
///      不得由该程序集定义（既不能内嵌，也不能以同名类型重生；拆分后契约类型经
///      TypeForwardedTo 落在独立契约程序集 Emuera.CompatPack）；
///   2. 上游插件 ABI 面原样健在（IPluginMethod / PluginMethodParameter）。
/// 物理成员断言必须用 Assembly.DefinedTypes（只枚举本程序集 TypeDef）：.NET Core 的
/// Assembly.GetType 会跟随 TypeForwardedTo 返回转发类型，不能用作"facade 不含契约类型"
/// 的判据。同一性对照经 typeof(ICompatPack)（新契约程序集）完成（拆分 ADR：
/// docs/plans/2026-10-03-compatpack-contract-assembly-split-adr.md）。
/// T8 起还冻结"facade 转发 = 契约程序集 public 类型全集"：新增契约类型必须同步补
/// TypeForwardedTo，否则旧式引用方（AssemblyRef=Emuera）解析不到该类型。
/// </summary>
public class PluginAbiBoundaryTests
{
    static Assembly FacadeAssembly => typeof(MinorShift.Emuera.Runtime.Utils.PluginSystem.IPluginMethod).Assembly;

    [Fact]
    public void Facade_PluginAbiIntact()
    {
        Assert.Equal("Emuera", FacadeAssembly.GetName().Name);
        Assert.Equal(new Version(1, 824, 0, 0), FacadeAssembly.GetName().Version);

        Assert.NotNull(typeof(MinorShift.Emuera.Runtime.Utils.PluginSystem.IPluginMethod));
        Assert.NotNull(typeof(MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginMethodParameter));
        Assert.NotNull(typeof(MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginManifestAbstract));
    }

    [Fact]
    public void Facade_DoesNotExposeCompatPackContracts()
    {
        var contractType = typeof(Emuera.Compatibility.Packs.ICompatPack);

        // 契约类型本体必须住在独立契约程序集，而不是 facade。
        Assert.Equal("Emuera.CompatPack", contractType.Assembly.GetName().Name);
        Assert.NotSame(FacadeAssembly, contractType.Assembly);

        // facade 程序集不得定义（TypeDef）任何 CompatPack 契约类型——内嵌或同名重生都不允许。
        // TypeForwardedTo 只是绑定重定向，不会给 DefinedTypes 添加条目。
        Assert.DoesNotContain(
            FacadeAssembly.DefinedTypes,
            t => (t.FullName ?? "").StartsWith("Emuera.Compatibility.Packs.", StringComparison.Ordinal));

        // 负向复核：历史契约名下最核心的类型全名在 facade 的 TypeDef 集里缺席。
        Assert.DoesNotContain(FacadeAssembly.DefinedTypes, t => t.FullName == "Emuera.Compatibility.Packs.ICompatPack");
        Assert.DoesNotContain(FacadeAssembly.DefinedTypes, t => t.FullName == "Emuera.Compatibility.Packs.CompatPackManifest");
        Assert.DoesNotContain(FacadeAssembly.DefinedTypes, t => t.FullName == "Emuera.Compatibility.Packs.CompatPackSurface");
    }

    [Fact]
    public void Facade_ContractTypesResolveThroughForwarding()
    {
        // 旧包兼容通道：facade 装载后，其 TypeForwardedTo 必须把契约类型链到
        // Emuera.CompatPack 的同一实例（拆分 ADR §2.3）。经 Type.GetType 按限定名解析
        // （会跟随 forward），并断言解析结果与直接引用契约程序集拿到的类型同一。
        Type? viaFacade = Type.GetType(
            "Emuera.Compatibility.Packs.ICompatPack, Emuera, Version=1.824.0.0, Culture=neutral, PublicKeyToken=null",
            throwOnError: false);
        Assert.Same(typeof(Emuera.Compatibility.Packs.ICompatPack), viaFacade);
    }

    [Fact]
    public void Facade_ForwardsEveryPublicContractType()
    {
        // 不变量（T8）：facade 的 TypeForwardedTo 集合 == 契约程序集全 public 类型。
        // 契约程序集新增类型而漏补转发时，拆分前编译的包按 'Emuera' 绑定会解析失败；
        // 多转发 facade 不存在的类型则说明转发列表腐化。两个方向都由集合相等钉住。
        Type[] contractTypes = typeof(Emuera.Compatibility.Packs.ICompatPack).Assembly
            .GetExportedTypes()
            .Where(type => type.IsPublic)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        Type[] forwarded = FacadeAssembly.GetForwardedTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            contractTypes.Select(type => type.FullName),
            forwarded.Select(type => type.FullName));
        // 转发目标必须是契约程序集（不是 facade 自嵌定义，也不是别的同名类型）。
        Assert.All(forwarded, type =>
            Assert.Same(typeof(Emuera.Compatibility.Packs.ICompatPack).Assembly, type.Assembly));
        // 每个契约类型都能按旧式限定名 'Type, Emuera' 解析回同一实例（旧包视角）。
        Assert.All(contractTypes, type =>
            Assert.Same(type, Type.GetType(type.FullName + ", Emuera", throwOnError: false)));
    }
}
