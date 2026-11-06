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
        currentContractAbi: new CompatVersion(1, 1),
        currentEngineApi: new CompatVersion(1, 0),
        capabilities: EngineCapabilityCatalog.All,
        knownBuiltinVariantNames: new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
        baselineInstructions: new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
        baselineFunctions: new HashSet<string>(
            LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
        knownBuiltinVariantInstructions: new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["builtin:snake"] = new HashSet<string>(StringComparer.Ordinal) { "FOR", "SETBGIMAGE" },
        },
        methodProjectedInstructionNames: new HashSet<string>(
            LegacyDialectInventories.MethodProjectedInstructionNames, StringComparer.Ordinal));

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
    [Fact]
    public void EraBluePack_AddsSetAnimeTimer_AndOneWiredCapability()
    {
        string packPath = typeof(PackGemueraEraBlue.PackGemueraEraBlueMarker).Assembly.Location;
        using var directory = new IsolatedPackDirectory(packPath);
        var context = Context();
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out var handle, out var errors),
            string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.Equal("pack.gemuera.erablue", handle!.Manifest.PackId);
        Assert.Equal("v24pure", handle.Manifest.BaseProfileId);
        Assert.Equal(new[] { "SETANIMETIMER" }, handle.Manifest.Surface.AddInstructions);
        Assert.Empty(handle.Manifest.Surface.HideInstructions);
        Assert.Empty(handle.Manifest.Surface.AddFunctions);
        Assert.Empty(handle.Manifest.Surface.HideFunctions);
        // plugin.external-assembly.v1 是 DiagnosticOnly（宿主声明，无运行期消费者），不可入包。
        Assert.Equal(new[] { "startup.continue-after-fault.v1" }, handle.Manifest.Capabilities);
        Assert.DoesNotContain("plugin.external-assembly.v1", handle.Manifest.Capabilities);
        Assert.Equal(
            LegacySurfaceHash.ComputeV24SurfaceHash(LegacySurfaceHash.BuildV24SurfaceHashInput("v24pure")),
            handle.Manifest.BaseSurfaceHash);

        CompatibilityPlan baseline = V24Baseline();
        Assert.True(CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { handle }, context, out CompatibilityPlan assembled, out var assemblyErrors),
            string.Join("; ", assemblyErrors));
        Assert.Empty(assemblyErrors);

        var expectedInstructions = new HashSet<string>(baseline.Dialect.Instructions.Keys, StringComparer.Ordinal)
        {
            "SETANIMETIMER",
        };
        Assert.Equal(
            expectedInstructions.Order(StringComparer.Ordinal),
            assembled.Dialect.Instructions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            baseline.Dialect.Functions.Keys.Order(StringComparer.Ordinal),
            assembled.Dialect.Functions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "startup.continue-after-fault.v1" }, assembled.CapabilityIds.Order(StringComparer.Ordinal));
        Assert.Throws<InvalidOperationException>(() => BuiltInDialectCatalog.CreateLegacySessionPlan("erablue"));
        handle.Unload();
    }
    [Fact]
    public void EraFlPack_AddsSetAnimeTimer_AndTenWiredCapabilities()
    {
        string packPath = typeof(PackGemueraEraFl.PackGemueraEraFlMarker).Assembly.Location;
        using var directory = new IsolatedPackDirectory(packPath);
        var context = Context();
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out var handle, out var errors),
            string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.Equal("pack.gemuera.erafl", handle!.Manifest.PackId);
        Assert.Equal(new[] { "SETANIMETIMER" }, handle.Manifest.Surface.AddInstructions);
        string[] expectedCapabilities =
        {
            "arith.safe-arithmetic-guard.v1",
            "datatable.xml-leading-bom.v1",
            "declare.out-keyword.v1",
            "display.dynamic-map-transaction.v1",
            "display.extended-history.v1",
            "input.omitted-default-argument.v1",
            "input.pointer-blank-string.v1",
            "input.pointer-button.v1",
            "markup.font-extended-attributes.v1",
            "type.float-literals.v1",
        };
        Assert.Equal(
            expectedCapabilities.Order(StringComparer.Ordinal),
            handle.Manifest.Capabilities.Order(StringComparer.Ordinal));
        // 3 个 markup 表面 capability 是 DiagnosticOnly，不可入包。
        Assert.DoesNotContain("markup.div-v2.v1", handle.Manifest.Capabilities);
        Assert.DoesNotContain("markup.image-dual-src.v1", handle.Manifest.Capabilities);
        Assert.DoesNotContain("resource.dynamic-sprite.v1", handle.Manifest.Capabilities);

        CompatibilityPlan baseline = V24Baseline();
        Assert.True(CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { handle }, context, out CompatibilityPlan assembled, out var assemblyErrors),
            string.Join("; ", assemblyErrors));
        Assert.Empty(assemblyErrors);
        var expectedInstructions = new HashSet<string>(baseline.Dialect.Instructions.Keys, StringComparer.Ordinal)
        {
            "SETANIMETIMER",
        };
        Assert.Equal(
            expectedInstructions.Order(StringComparer.Ordinal),
            assembled.Dialect.Instructions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            baseline.Dialect.Functions.Keys.Order(StringComparer.Ordinal),
            assembled.Dialect.Functions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(expectedCapabilities.Order(StringComparer.Ordinal), assembled.CapabilityIds.Order(StringComparer.Ordinal));
        Assert.Throws<InvalidOperationException>(() => BuiltInDialectCatalog.CreateLegacySessionPlan("erafl"));
        handle.Unload();
    }

    [Fact]
    public void SnakePack_CarriesMaximalDelta_AndNineteenWiredCapabilities()
    {
        string packPath = typeof(PackGemueraSnake.PackGemueraSnakeMarker).Assembly.Location;
        using var directory = new IsolatedPackDirectory(packPath);
        var context = Context();
        Assert.True(CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out var handle, out var errors),
            string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.Equal("pack.gemuera.snake", handle!.Manifest.PackId);
        Assert.Equal("v24pure", handle.Manifest.BaseProfileId);
        Assert.Equal(new CompatVersion(1, 1), handle.Manifest.ContractAbi);
        Assert.Equal(22, handle.Manifest.Surface.AddInstructions.Count);
        Assert.Equal(LegacyDialectInventories.SnakeDeltaFunctions.Length, handle.Manifest.Surface.AddFunctions.Count);
        Assert.Equal(new[] { "BITMAP_CACHE_ENABLE", "SETANIMETIMER" }, handle.Manifest.Surface.HideFunctions);
        Assert.Equal(new[] { "BITMAP_CACHE_ENABLE" }, handle.Manifest.Surface.AddInstructionCollisions);
        Assert.Equal(new[] { "FOR", "SETBGIMAGE" }, handle.Manifest.VariantSelections.Keys.Order(StringComparer.Ordinal));

        string[] expectedCapabilities = SnakeCompatibilityCapabilities.RequiredWiredCapabilityIds
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(19, expectedCapabilities.Length);
        Assert.Equal(expectedCapabilities, handle.Manifest.Capabilities.Order(StringComparer.Ordinal));

        CompatibilityPlan baseline = V24Baseline();
        Assert.True(CompatPackPlanAssembler.TryAssemble(
                baseline, new[] { handle }, context, out CompatibilityPlan assembled, out var assemblyErrors),
            string.Join("; ", assemblyErrors));
        Assert.Empty(assemblyErrors);

        var expectedInstructions = new HashSet<string>(baseline.Dialect.Instructions.Keys, StringComparer.Ordinal);
        foreach (string name in LegacyDialectInventories.SnakeDeltaInstructionNames)
            expectedInstructions.Add(name);
        Assert.Equal(
            expectedInstructions.Order(StringComparer.Ordinal),
            assembled.Dialect.Instructions.Keys.Order(StringComparer.Ordinal));
        Assert.True(assembled.Dialect.Instructions.ContainsKey("CALLSTR"));
        Assert.True(assembled.Dialect.Instructions.ContainsKey("BITMAP_CACHE_ENABLE"));

        var expectedFunctions = new HashSet<string>(baseline.Dialect.Functions.Keys, StringComparer.Ordinal);
        foreach (var entry in LegacyDialectInventories.SnakeDeltaFunctions)
            expectedFunctions.Add(entry.Name);
        expectedFunctions.Remove("BITMAP_CACHE_ENABLE");
        expectedFunctions.Remove("SETANIMETIMER");
        Assert.Equal(
            expectedFunctions.Order(StringComparer.Ordinal),
            assembled.Dialect.Functions.Keys.Order(StringComparer.Ordinal));
        Assert.False(assembled.Dialect.Functions.ContainsKey("BITMAP_CACHE_ENABLE"));
        Assert.False(assembled.Dialect.Functions.ContainsKey("SETANIMETIMER"));
        Assert.True(assembled.Dialect.Functions.ContainsKey("陥落状態"));
        Assert.True(assembled.Dialect.Functions.ContainsKey("陷落状态"));
        Assert.True(assembled.Dialect.Functions.ContainsKey("SQL_CONNECT"));

        Assert.Equal(expectedCapabilities, assembled.CapabilityIds.Order(StringComparer.Ordinal));
        Assert.Throws<InvalidOperationException>(() => BuiltInDialectCatalog.CreateLegacySessionPlan("snake"));
        handle.Unload();
    }

}
