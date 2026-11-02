using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;

namespace GEmuera.Core.Tests;

/// <summary>
/// T6 组合解析/组装测试的手工夹具构造器（不依赖包程序集加载路径）：清单走 public
/// <see cref="CompatPackManifest.TryParse"/>（组合字段与生产解析同源），差量用
/// <see cref="ValidatedPackDelta"/> 直接冻结，句柄用 internal 构造点（GEmuera.Core 对
/// GEmuera.Core.Tests 有 InternalsVisibleTo）。所有默认 owner 省略——解析器以句柄
/// packId 为归属事实源，正好覆盖"差量 owner 为空"的规范化分支。
/// </summary>
internal static class CompatPackCompositionTestData
{
    public static CompatPackValidationContext Context(
        IReadOnlyDictionary<string, CapabilityDescriptor>? capabilities = null,
        IReadOnlySet<string>? methodProjectedInstructionNames = null,
        string? baselineProfileId = null)
        => new(
            new CompatVersion(1, 0),
            new CompatVersion(1, 0),
            capabilities ?? EngineCapabilityCatalog.All,
            new HashSet<string>(StringComparer.Ordinal) { "builtin:snake" },
            new HashSet<string>(LegacyDialectInventories.V24InstructionNames, StringComparer.Ordinal),
            new HashSet<string>(
                LegacyDialectInventories.V24Functions.Select(entry => entry.Name), StringComparer.Ordinal),
            knownInstructionHandlers: EngineInstructionHandlers(),
            knownFunctionHandlers: EngineFunctionHandlers(),
            methodProjectedInstructionNames: methodProjectedInstructionNames
                ?? new HashSet<string>(
                    LegacyDialectInventories.MethodProjectedInstructionNames, StringComparer.Ordinal),
            baselineProfileId: baselineProfileId);

    public static HashSet<string> EngineInstructionHandlers()
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        union.UnionWith(LegacyDialectInventories.V24InstructionNames);
        union.UnionWith(LegacyDialectInventories.SnakeDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.EraFlDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.EraBlueDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.MegatenDeltaInstructionNames);
        union.UnionWith(LegacyDialectInventories.V18InstructionNames);
        return union;
    }

    public static HashSet<string> EngineFunctionHandlers()
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        union.UnionWith(LegacyDialectInventories.V24Functions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.SnakeDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.EraFlDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.EraBlueDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.MegatenDeltaFunctions.Select(entry => entry.Name));
        union.UnionWith(LegacyDialectInventories.V18Functions.Select(entry => entry.Name));
        return union;
    }

    public static CompatPackManifest Manifest(
        string packId,
        string packVersion = "1.0.0",
        IEnumerable<CompatPackDependency>? requires = null,
        IEnumerable<string>? conflicts = null,
        IEnumerable<CompatPackOverride>? overrides = null,
        IReadOnlyDictionary<string, string>? variantSelections = null,
        string? saveProfileId = null)
    {
        string json = "{\"packId\":\"" + packId + "\",\"packVersion\":\"" + packVersion
            + "\",\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\",\"baseProfileId\":\"v24pure\"";
        if (requires is not null && requires.Any())
        {
            json += ",\"requires\":{\"packs\":["
                + string.Join(",", requires.Select(dependency =>
                    "{\"packId\":\"" + dependency.PackId + "\",\"versionRange\":\""
                    + dependency.VersionRange + "\"}"))
                + "]}";
        }
        if (conflicts is not null && conflicts.Any())
            json += ",\"conflicts\":{\"packs\":["
                + string.Join(",", conflicts.Select(id => "\"" + id + "\"")) + "]}";
        if (overrides is not null && overrides.Any())
            json += ",\"overrides\":["
                + string.Join(",", overrides.Select(entry =>
                    "{\"symbol\":\"" + entry.Symbol + "\",\"priority\":" + entry.Priority + "}"))
                + "]";
        if (variantSelections is { Count: > 0 })
            json += ",\"variantSelections\":{"
                + string.Join(",", variantSelections.Select(pair =>
                    "\"" + pair.Key + "\":\"" + pair.Value + "\""))
                + "}";
        if (!string.IsNullOrWhiteSpace(saveProfileId))
            json += ",\"saveProfileId\":\"" + saveProfileId + "\"";
        json += "}";

        return CompatPackManifest.TryParse(json, out var manifest, out var errors)
            ? manifest!
            : throw new InvalidOperationException("构造测试清单失败：" + string.Join("; ", errors));
    }

    public static CompatPackHandle Handle(
        string packId,
        string packSha256,
        IEnumerable<CompatSymbolState>? surfaceStates = null,
        IEnumerable<string>? capabilityIds = null,
        IReadOnlyDictionary<string, string>? variantSelections = null,
        string packVersion = "1.0.0",
        IEnumerable<CompatPackDependency>? requires = null,
        IEnumerable<string>? conflicts = null,
        IEnumerable<CompatPackOverride>? overrides = null,
        string? saveProfileId = null,
        string? deltaPackVersion = null)
    {
        var manifest = Manifest(
            packId, packVersion, requires, conflicts, overrides, variantSelections, saveProfileId);
        var delta = new ValidatedPackDelta(
            packId,
            deltaPackVersion ?? packVersion,
            packSha256,
            capabilityIds ?? Array.Empty<string>(),
            surfaceStates ?? Array.Empty<CompatSymbolState>(),
            variantSelections ?? new Dictionary<string, string>(StringComparer.Ordinal));
        return new CompatPackHandle(
            manifest,
            null!,
            "Z:/manual/" + packId + ".dll",
            packSha256,
            delta,
            Array.Empty<CompatPackDependencyInfo>(),
            new CompatPackLoadContext("Z:/manual/" + packId + ".dll", new Dictionary<string, byte[]>()));
    }

    public static string ShaOf(char fill) => new(fill, 64);

    public static CompatSymbolState State(
        CompatSymbolKind kind,
        string name,
        CompatSymbolStateKind stateKind,
        string? owner = null,
        string? returnType = null,
        string? builtinVariant = null)
        => new(new CompatSymbolKey(kind, name), stateKind, owner, returnType, builtinVariant);

    public static CompatSymbolState PresentInstruction(string name, string? owner = null)
        => State(CompatSymbolKind.Instruction, name, CompatSymbolStateKind.Present, owner);

    public static CompatSymbolState HiddenInstruction(string name, string? owner = null)
        => State(CompatSymbolKind.Instruction, name, CompatSymbolStateKind.Hidden, owner);

    public static CompatSymbolState PresentFunction(string name, string? owner = null, string? returnType = null)
        => State(CompatSymbolKind.Function, name, CompatSymbolStateKind.Present, owner, returnType);

    public static CompatSymbolState HiddenFunction(string name, string? owner = null)
        => State(CompatSymbolKind.Function, name, CompatSymbolStateKind.Hidden, owner);

    public static CompatSymbolState VariantSelection(string instruction, string variant, string? owner = null)
        => State(CompatSymbolKind.Variant, instruction, CompatSymbolStateKind.VariantSelection, owner,
            builtinVariant: variant);

    public static CapabilityDescriptor Wired(
        string capabilityId,
        IEnumerable<string>? requires = null,
        IEnumerable<string>? implies = null,
        IEnumerable<string>? conflictsWith = null)
        => new(
            capabilityId,
            CapabilityStatus.Wired,
            requires?.ToArray() ?? Array.Empty<string>(),
            implies?.ToArray() ?? Array.Empty<string>(),
            conflictsWith?.ToArray() ?? Array.Empty<string>(),
            "test.consumer." + capabilityId,
            new CompatVersion(1, 0));
}
