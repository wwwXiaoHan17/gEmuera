using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// v18 第一方数据包（packs/pack.gemuera.v18）的清单等价门禁（P-D 步 1 退役后口径）：
/// 内置 v18 profile 已删除，golden 改为冻结表面（LegacyDialectInventories.V18*，源自
/// tools/dialect-inventory/retired-dialect-surface-universe.json）。包清单由生成器产出，
/// v24pure + 包 的会话表面必须与冻结 v18 表面逐名相等；投影为 LegacyCompatibilityProfile
/// 后可见性与冻结表面一致。清单漂移（生成清单再生成后未重跑生成器/手改 JSON）在此先红。
/// </summary>
[Collection("CompatPack")]
public class V18PackManifestTests
{
    static CompatPackValidationContext V18PackContext() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal),
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

    [Fact]
    public void GeneratedManifest_ReproducesFrozenV18Surface()
    {
        string packPath = typeof(GemueraV18Pack.GemueraV18PackMarker).Assembly.Location;
        using var directory = new IsolatedPackDirectory(packPath);
        var context = V18PackContext();

        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out var handle, out var errors),
            string.Join("; ", errors));
        Assert.Empty(errors);
        // 身份与差量方向自检：包只隐藏（冻结证据 v18 ⊆ v24），不声明 capability。
        Assert.Equal("pack.gemuera.v18", handle!.Manifest.PackId);
        Assert.Equal("v24pure", handle.Manifest.BaseProfileId);
        Assert.False(string.IsNullOrWhiteSpace(handle.Manifest.BaseSurfaceHash));
        Assert.Matches("^[0-9a-f]{64}$", handle.Manifest.BaseSurfaceHash);
        Assert.Equal(
            LegacySurfaceHash.ComputeV24SurfaceHash(LegacySurfaceHash.BuildV24SurfaceHashInput("v24pure")),
            handle.Manifest.BaseSurfaceHash);
        Assert.Empty(handle.Manifest.Capabilities);
        Assert.Equal(new CompatVersion(1, 0), handle.Manifest.ContractAbi);
        Assert.Equal(new CompatVersion(1, 0), handle.Manifest.EngineApi);
        Assert.False(handle.Manifest.UsedLegacyTargetEngineApi);
        Assert.Empty(handle.Manifest.Surface.AddInstructions);
        Assert.Empty(handle.Manifest.Surface.AddFunctions);
        Assert.Equal(
            LegacyDialectInventories.V24InstructionNames.Except(
                LegacyDialectInventories.V18InstructionNames, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            handle.Manifest.Surface.HideInstructions);
        Assert.Equal(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name)
                .Except(LegacyDialectInventories.V18Functions.Select(entry => entry.Name), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
            handle.Manifest.Surface.HideFunctions);

        Assert.True(CompatPackPlanAssembler.TryAssemble(
                BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure"),
                new[] { handle },
                context,
                out CompatibilityPlan assembled,
                out var assemblyErrors),
            string.Join("; ", assemblyErrors));
        Assert.Empty(assemblyErrors);

        // 核心断言：v24pure + v18 包的会话表面 == 冻结 v18 表面（指令/函数键集合逐名相等）。
        string[] expectedInstructions = LegacyDialectInventories.V18InstructionNames
            .Order(StringComparer.Ordinal).ToArray();
        string[] expectedFunctions = LegacyDialectInventories.V18Functions
            .Select(entry => entry.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedInstructions, assembled.Dialect.Instructions.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(expectedFunctions, assembled.Dialect.Functions.Keys.Order(StringComparer.Ordinal).ToArray());

        // 会话投影可见性由 SurfaceSmoke.AssertV18PackEquivalence 验证（本测试工程不引用
        // legacy 宿主程序集；plan 键集合断言已覆盖表面对账核心）。

        // P-D 步 1 硬证据：内置 v18 profile 已注销，禁止再被引擎解析。
        Assert.Throws<InvalidOperationException>(() => BuiltInDialectCatalog.CreateLegacySessionPlan("v18"));
        handle.Unload();
    }
}
