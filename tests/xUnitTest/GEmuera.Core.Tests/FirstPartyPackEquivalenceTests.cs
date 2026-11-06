using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 第一方兼容包的"可安装形态"等价门禁（P-D 退役后口径）：内置 profile 已删除，
/// 用真实包 DLL 走加载/组装链，断言（a）表面差量与冻结事实一致、（b）包声明的
/// capability 恰好等于内置 profile 的 Wired 子集（DiagnosticOnly 不可入包）、
/// （c）v24pure 基线不受未选择侧影响、（d）退役 profile 不可再被引擎解析。
/// </summary>
[Collection("CompatPack")]
public class FirstPartyPackEquivalenceTests
{
    static CompatPackValidationContext Context() => new(
        currentContractAbi: new CompatVersion(1, 0),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal),
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal));

    static CompatibilityPlan V24Baseline() => BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");

    [Fact]
    public void MegatenPack_DeclaresFiveWiredCapabilities_AndZeroSurface()
    {
        string packPath = typeof(PackGemueraMegaten.PackGemueraMegatenMarker).Assembly.Location;
        using var directory = new IsolatedPackDirectory(packPath);
        var context = Context();
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out var handle, out var errors),
            string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.Equal("pack.gemuera.megaten", handle!.Manifest.PackId);
        Assert.Equal("v24pure", handle.Manifest.BaseProfileId);
        Assert.Empty(handle.Manifest.Surface.AddInstructions);
        Assert.Empty(handle.Manifest.Surface.HideInstructions);
        Assert.Empty(handle.Manifest.Surface.AddFunctions);
        Assert.Empty(handle.Manifest.Surface.HideFunctions);
        Assert.Empty(handle.Manifest.VariantSelections);

        // 内置 megaten profile 的 6 个 RequiredCapabilityIds 中，ui.standard-color-name.v1 是
        // DiagnosticOnly（无运行期消费者，包不可声明）；其余 5 个 Wired 才是包契约词汇。
        string[] expectedCapabilities =
        {
            "parser.label-lookup-case.v1",
            "parser.private-system-shadow.v1",
            "parser.ref-out-name.v1",
            "startup.continue-after-fault.v1",
            "ui.html-rgb-zero-alpha.v1",
        };
        Assert.Equal(
            expectedCapabilities.Order(StringComparer.Ordinal),
            handle.Manifest.Capabilities.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("ui.standard-color-name.v1", handle.Manifest.Capabilities);

        CompatibilityPlan baseline = V24Baseline();
        Assert.True(CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { handle }, context, out CompatibilityPlan assembled, out var assemblyErrors),
            string.Join("; ", assemblyErrors));
        Assert.Empty(assemblyErrors);

        // 面零增量：assembled 表面与 v24pure 逐名相等。
        Assert.Equal(
            baseline.Dialect.Instructions.Keys.Order(StringComparer.Ordinal),
            assembled.Dialect.Instructions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            baseline.Dialect.Functions.Keys.Order(StringComparer.Ordinal),
            assembled.Dialect.Functions.Keys.Order(StringComparer.Ordinal));
        // capability 账本 = v24pure 空账本 ∪ 包 5 个 Wired。
        Assert.Equal(expectedCapabilities.Order(StringComparer.Ordinal), assembled.CapabilityIds.Order(StringComparer.Ordinal));

        // 退役后不可再解析内置 profile。
        Assert.Throws<InvalidOperationException>(() => BuiltInDialectCatalog.CreateLegacySessionPlan("megaten"));
        handle.Unload();
    }
}
