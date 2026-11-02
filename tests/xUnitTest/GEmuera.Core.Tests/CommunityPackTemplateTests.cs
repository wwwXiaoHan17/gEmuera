using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 社区包脚手架端到端用例：packs/CommunityPackTemplate（manifest-only 数据包模板，
/// 无入口类）与 tests/xUnitTest/CompatPackContractOnlyFixture（最小 ICompatPack 契约夹具）
/// 都能经 CompatPackLoader 完整加载，作为社区包作者与后续 ALC 正向用例的可复制基线。
/// </summary>
[Collection("CompatPack")]
public class CommunityPackTemplateTests
{
    static CompatPackValidationContext CommunityContext() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        knownCapabilityIds: new HashSet<string>(StringComparer.Ordinal)
        {
            "math.times-clamp.v1",
        },
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal),
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

    public CommunityPackTemplateTests()
    {
        // 前置事实自检：模板 manifest 的表面/capability 与真实 v24 基线的关系（防清单漂移后测试假绿）。
        Assert.DoesNotContain("SETANIMETIMER", LegacyDialectInventories.V24InstructionNames);
    }

    [Fact]
    public void ManifestOnlyTemplate_LoadsAndValidates()
    {
        string packPath = typeof(CommunityPackTemplate.CommunityPackTemplateMarker).Assembly.Location;
        Assert.True(CompatPackLoader.TryLoad(packPath, CommunityContext(), out var handle, out var errors),
            string.Join("; ", errors));

        Assert.Empty(errors);
        Assert.Equal("community.template", handle!.Manifest.PackId);
        Assert.IsType<ManifestOnlyCompatPack>(handle.Pack);
        Assert.NotEmpty(handle.Surface);
        handle.Unload();
    }

    [Fact]
    public void ContractOnlyFixture_LoadsAndSurfaceApplies()
    {
        string packPath = typeof(CompatPackContractOnlyFixture.ContractOnlyPack).Assembly.Location;
        Assert.True(CompatPackLoader.TryLoad(packPath, CommunityContext(), out var handle, out var errors),
            string.Join("; ", errors));

        Assert.Empty(errors);
        // 有入口类的包不走 manifest-only 分叉；表面贡献来自入口类代码而非清单合成。
        Assert.IsNotType<ManifestOnlyCompatPack>(handle!.Pack);
        // 包程序集被加载进独立 ALC，其自带类型与测试工程静态引用的同名类型不是同一 Type，
        // 只能按包内唯一 ContributionId 断言入口贡献确实进入表面集合（契约类型跨 ALC 统一）。
        var surface = Assert.Single(handle.Surface);
        Assert.Equal("community.contract-fixture.surface", surface.ContributionId);
        handle.Unload();
    }
}
