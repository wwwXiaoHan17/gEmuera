using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// v18 第一方数据包（packs/gemuera.v18）的清单等价门禁：包清单由生成器从
/// <see cref="LegacyDialectInventories"/>（唯一事实源）的 v24−v18 差集计算，
/// 组装后的 "v24pure + v18 包" 会话表面必须与内置 v18 profile 逐名相等。
/// 不硬编码 LegacyV18CompatibilityModule 的手抄差量名单（仅作交叉诊断）。
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
    public void GeneratedManifest_ReproducesBuiltinV18Surface()
    {
        string packPath = typeof(GemueraV18Pack.GemueraV18PackMarker).Assembly.Location;
        using var directory = new IsolatedPackDirectory(packPath);

        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, V18PackContext(), out var handle, out var errors),
            string.Join("; ", errors));
        Assert.Empty(errors);
        // 身份与差量方向自检：包只隐藏（v18 ⊆ v24 由生成清单取证），不声明 capability。
        Assert.Equal("pack.gemuera.v18", handle!.Manifest.PackId);
        Assert.Equal("v24pure", handle.Manifest.BaseProfileId);
        Assert.Empty(handle.Manifest.Capabilities);
        // 官方包已完成双轴迁移：不再依赖 legacy targetEngineApi 的 1.0/1.0 映射。
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
                out CompatibilityPlan assembled,
                out var assemblyErrors),
            string.Join("; ", assemblyErrors));
        Assert.Empty(assemblyErrors);

        // 核心断言：v24pure + v18 包的会话表面 == 内置 v18 profile（指令/函数键集合逐名相等）。
        CompatibilityPlan builtinV18 = BuiltInDialectCatalog.CreateLegacySessionPlan("v18");
        Assert.Equal(
            builtinV18.Dialect.Instructions.Keys.Order(StringComparer.Ordinal).ToArray(),
            assembled.Dialect.Instructions.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            builtinV18.Dialect.Functions.Keys.Order(StringComparer.Ordinal).ToArray(),
            assembled.Dialect.Functions.Keys.Order(StringComparer.Ordinal).ToArray());
        handle.Unload();
    }
}
