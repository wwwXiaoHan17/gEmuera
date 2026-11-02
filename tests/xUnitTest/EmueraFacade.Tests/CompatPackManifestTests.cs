using System.Text.Json;
using Emuera.Compatibility.Packs;
using Xunit;

namespace EmueraFacade.Tests;

/// <summary>
/// 清单结构校验的 fail-closed 矩阵：任一字段不合法即整体拒绝并全量报告错误，
/// 绝不半解析静默继续（docs/designs/compat-pack-interface.md §3/§5.3）。
/// </summary>
public class CompatPackManifestTests
{
    private const string ValidMinimal =
        "{\"packId\":\"test.hello-pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}";

    private const string ValidFull =
        "{\"packId\":\"test.hello-pack\",\"packVersion\":\"1.2.3\","
        + "\"contractAbi\":\"1.2\",\"engineApi\":\"1.3\","
        + "\"baseSurfaceHash\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\","
        + "\"capabilities\":[\"input.pointer-button.v1\",\"startup.continue-after-fault.v1\"],"
        + "\"saveProfileId\":\"gemuera.hello\","
        + "\"variantSelections\":{\" setbgimage \":\"builtin:snake\"},"
        + "\"gameIdentity\":{\"gameCode\":\"20250628\",\"version\":\"305\",\"versionAccept\":\"minor\"}}";

    private static string WithField(string field, string value) =>
        ValidMinimal.Substring(0, ValidMinimal.Length - 1) + "," + field + ":" + value + "}";

    [Fact]
    public void Parse_FullManifest_Succeeds()
    {
        Assert.True(CompatPackManifest.TryParse(ValidFull, out var manifest, out var errors), string.Join("; ", errors));

        Assert.Equal("test.hello-pack", manifest!.PackId);
        Assert.Equal("1.2.3", manifest.PackVersion);
        Assert.Equal(new CompatVersion(1, 2), manifest.ContractAbi);
        Assert.Equal(new CompatVersion(1, 3), manifest.EngineApi);
        Assert.False(manifest.UsedLegacyTargetEngineApi);
        Assert.NotNull(manifest.BaseSurfaceHash);
        Assert.Equal(2, manifest.Capabilities.Count);
        Assert.Equal("gemuera.hello", manifest.SaveProfileId);
        // 指令名规范化为 Trim+Upper（与引擎 IsInstructionVisible 同语义）。
        Assert.Equal("builtin:snake", manifest.VariantSelections["SETBGIMAGE"]);
        Assert.Equal("20250628", manifest.GameIdentity!.GameCode);
        Assert.Equal("305", manifest.GameIdentity.Version);
        Assert.Equal("minor", manifest.GameIdentity.VersionAccept);
    }

    [Fact]
    public void Parse_MinimalManifest_SucceedsWithDefaults()
    {
        Assert.True(CompatPackManifest.TryParse(ValidMinimal, out var manifest, out _));

        Assert.Empty(manifest!.Capabilities);
        Assert.Empty(manifest.VariantSelections);
        Assert.Null(manifest.SaveProfileId);
        Assert.Null(manifest.BaseSurfaceHash);
        Assert.Null(manifest.GameIdentity);
    }

    [Fact]
    public void Parse_BomPrefixedJson_Succeeds()
    {
        Assert.True(CompatPackManifest.TryParse("\uFEFF" + ValidMinimal, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyInput_Fails(string? json)
    {
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("\"string\"")]
    public void Parse_NonObjectOrInvalidJson_Fails(string json)
    {
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Parse_UnknownRootProperty_Fails()
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"extra\"", "1"), out _, out var errors));
        Assert.Contains(errors, e => e.Contains("未知字段"));
    }

    [Theory]
    [InlineData("{\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}", "packId")]
    [InlineData("{\"packId\":\"Test.UPPER\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}", "packId")]
    [InlineData("{\"packId\":\"has space\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1}", "packId")]
    [InlineData("{\"packId\":\"test.pack\",\"targetEngineApi\":1}", "packVersion")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0\",\"targetEngineApi\":1}", "packVersion")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0.0\",\"targetEngineApi\":1}", "packVersion")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\"}", "contractAbi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":0}", "targetEngineApi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":-1}", "targetEngineApi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"contractAbi\":\"1.0\"}", "engineApi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"contractAbi\":\"1\",\"engineApi\":\"1.0\"}", "contractAbi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"contractAbi\":\"01.0\",\"engineApi\":\"1.0\"}", "contractAbi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"contractAbi\":\"1.0\",\"engineApi\":\"1.2.3\"}", "engineApi")]
    public void Parse_InvalidRequiredFields_FailWithFieldNamed(string json, string field)
    {
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(field));
    }

    [Fact]
    public void Parse_TargetEngineApiAsString_Succeeds()
    {
        // 旧解析器接受字符串形式（避免包作者踩 JSON 数字精度坑）；legacy 迁移窗口
        // 只接受值 1，字符串形式同样映射为双轴 1.0/1.0。
        string json = "{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":\"1\"}";
        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out _));
        Assert.Equal(new CompatVersion(1, 0), manifest!.ContractAbi);
        Assert.Equal(new CompatVersion(1, 0), manifest.EngineApi);
        Assert.True(manifest.UsedLegacyTargetEngineApi);
    }

    [Fact]
    public void Parse_DualVersionAxes_AreRequiredTogether()
    {
        const string json = """
        {
          "packId": "test.dual",
          "packVersion": "1.0.0",
          "contractAbi": "1.2",
          "engineApi": "1.3",
          "baseProfileId": "v24pure"
        }
        """;

        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.Equal(new CompatVersion(1, 2), manifest!.ContractAbi);
        Assert.Equal(new CompatVersion(1, 3), manifest.EngineApi);
        Assert.False(manifest.UsedLegacyTargetEngineApi);
    }

    [Fact]
    public void Parse_LegacyTargetEngineApiOnly1_MapsToDualAxes()
    {
        const string json = """
        {
          "packId": "test.legacy",
          "packVersion": "1.0.0",
          "targetEngineApi": 1
        }
        """;

        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        Assert.Empty(errors);
        // legacy 迁移只映射到契约 1.0 / 引擎 1.0 这一条已知的已发布线。
        Assert.Equal(new CompatVersion(1, 0), manifest!.ContractAbi);
        Assert.Equal(new CompatVersion(1, 0), manifest.EngineApi);
        Assert.True(manifest.UsedLegacyTargetEngineApi);
    }

    [Fact]
    public void Parse_TargetEngineApiMixedWithDualAxes_Rejects()
    {
        const string json = """
        {
          "packId": "test.mixed",
          "packVersion": "1.0.0",
          "contractAbi": "1.0",
          "engineApi": "1.0",
          "targetEngineApi": 1
        }
        """;

        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        // 新旧字段混用无法判断作者意图（legacy 映射会覆盖/冲突双轴声明），整体拒载。
        Assert.Contains(errors, e => e.Contains("targetEngineApi") && e.Contains("混用"));
    }

    [Fact]
    public void Parse_DuplicateCapability_Fails()
    {
        string json = WithField("\"capabilities\"", "[\"parse.diagnostics.v1\",\"parse.diagnostics.v1\"]");
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("重复"));
    }

    [Theory]
    [InlineData("\"Capabilities\"")]
    [InlineData("3")]
    public void Parse_CapabilitiesWrongShapeOrCharset_Fails(string value)
    {
        string wrongShape = WithField("\"capabilities\"", value);
        Assert.False(CompatPackManifest.TryParse(wrongShape, out _, out var errors));
        Assert.NotEmpty(errors);

        string wrongCharset = WithField("\"capabilities\"", "[\"UPPER.CASE\"]");
        Assert.False(CompatPackManifest.TryParse(wrongCharset, out _, out _));
    }

    [Fact]
    public void Parse_BadSaveProfileIdOrHash_Fails()
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"saveProfileId\"", "\"UPPER\""), out _, out _));
        // 大写十六进制不接受：哈希一律小写。
        Assert.False(CompatPackManifest.TryParse(WithField("\"baseSurfaceHash\"", "\"ABCDEF0123\""), out _, out _));
        // 宽度必须是 SHA256 的恰好 64 位（旧 8-64 宽进已收紧；schema 同步）。
        Assert.False(CompatPackManifest.TryParse(WithField("\"baseSurfaceHash\"", "\"01234567\""), out _, out _));
        Assert.False(CompatPackManifest.TryParse(
            WithField("\"baseSurfaceHash\"", "\"" + new string('a', 63) + "\""), out _, out _));
    }

    [Fact]
    public void Parse_VariantSelectionsInvalidEntries_Fail()
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"variantSelections\"", "{\"\":\"builtin:x\"}"), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"variantSelections\"", "{\"PRINT\":3}"), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"variantSelections\"", "{\"PRINT\":\"has space\"}"), out _, out _));
    }

    [Fact]
    public void Parse_VariantSelectionsExactDuplicateKey_Fails()
    {
        // JSON 对象原生重复键（System.Text.Json 允许出现两次）也必须整体拒绝，不做 last-wins。
        string json = "{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"variantSelections\":{\"PRINT\":\"builtin:a\",\"PRINT\":\"builtin:b\"}}";
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("重复"));
    }

    [Fact]
    public void Parse_VariantSelectionsNormalizedKeyConflict_Fails()
    {
        // Trim+Upper 规范化后冲突（" print " 与 "PRINT"）同样拒绝。
        string json = "{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"variantSelections\":{\"PRINT\":\"builtin:a\",\" print \":\"builtin:b\"}}";
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("重复"));
    }

    [Fact]
    public void Parse_GameIdentityProblems_Fail()
    {
        string missingCode = WithField("\"gameIdentity\"", "{\"version\":\"305\"}");
        Assert.False(CompatPackManifest.TryParse(missingCode, out _, out var errors));
        Assert.Contains(errors, e => e.Contains("gameIdentity.gameCode"));

        string badAccept = WithField("\"gameIdentity\"", "{\"gameCode\":\"1\",\"version\":\"2\",\"versionAccept\":\"loose\"}");
        Assert.False(CompatPackManifest.TryParse(badAccept, out _, out _));

        string unknownMember = WithField("\"gameIdentity\"", "{\"gameCode\":\"1\",\"version\":\"2\",\"encoding\":\"sjis\"}");
        Assert.False(CompatPackManifest.TryParse(unknownMember, out _, out _));
    }

    [Fact]
    public void Parse_MultipleProblems_AccumulateAllErrors()
    {
        string json = "{\"packId\":\"BAD ID\",\"targetEngineApi\":0,\"extra\":true}";
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.True(errors.Count >= 3, "应至少报告 packVersion 缺失、packId 形式、targetEngineApi、未知字段四类错误：" + string.Join("; ", errors));
    }
    [Fact]
    public void Parse_SurfaceAndBaseProfile_Succeeds()
    {
        string json = "{\"packId\":\"test.surface\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"baseProfileId\":\"v24pure\","
            + "\"surface\":{\"addInstructions\":[\" setanimtimer \"],\"hideInstructions\":[\"CALLSHARP\"],"
            + "\"addFunctions\":[\"SQL_CONNECT\"],\"hideFunctions\":[\"EXISTVAR\"]}}";
        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        Assert.Equal("v24pure", manifest!.BaseProfileId);
        Assert.Equal(new[] { "SETANIMTIMER" }, manifest.Surface.AddInstructions);
        Assert.Equal(new[] { "CALLSHARP" }, manifest.Surface.HideInstructions);
        Assert.Equal(new[] { "SQL_CONNECT" }, manifest.Surface.AddFunctions);
        Assert.Equal(new[] { "EXISTVAR" }, manifest.Surface.HideFunctions);
    }

    [Fact]
    public void Parse_SurfaceInvalid_Fails()
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"baseProfileId\"", "\"UPPER\""), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"surface\"", "{\"addInstructions\":\"SETANIMTIMER\"}"), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"surface\"", "{\"addInstructions\":[\"SETANIMTIMER\",\" setanimtimer \"]}"), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"surface\"", "{\"addInstructions\":[\"\"]}"), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"surface\"", "{\"unknown\":[\"X\"]}"), out _, out _));
    }

    [Fact]
    public void Parse_CompositionFields_AreImmutableAndOrdered()
    {
        const string json = """
        {
          "packId": "test.compose",
          "packVersion": "1.0.0",
          "contractAbi": "1.0",
          "engineApi": "1.0",
          "baseProfileId": "v24pure",
          "requires": {
            "packs": [
              { "packId": "community.shared", "versionRange": "[1.0.0,2.0.0)" }
            ]
          },
          "conflicts": { "packs": ["community.legacy"] },
          "overrides": [
            { "symbol": "instruction:SETANIMETIMER", "priority": 20 }
          ]
        }
        """;

        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        Assert.Empty(errors);
        Assert.Single(manifest!.RequiredPacks);
        Assert.Equal("community.shared", manifest.RequiredPacks[0].PackId);
        Assert.Equal("[1.0.0,2.0.0)", manifest.RequiredPacks[0].VersionRange);
        Assert.Equal(new[] { "community.legacy" }, manifest.ConflictingPacks);
        Assert.Equal(20, manifest.Overrides[0].Priority);
        Assert.Equal("instruction:SETANIMETIMER", manifest.Overrides[0].Symbol);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("[1.0.0,2.0.0)")]
    [InlineData("(1.0.0,2.0.0]")]
    [InlineData("[1.0.0,1.0.0]")]
    public void Parse_RequiresVersionRange_AcceptsSemverAndIntervals(string range)
    {
        string json = "{\"packId\":\"test.range\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\","
            + "\"requires\":{\"packs\":[{\"packId\":\"community.b\",\"versionRange\":\""
            + range + "\"}]}}";

        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        Assert.Equal(range, manifest!.RequiredPacks[0].VersionRange);
    }

    [Theory]
    [InlineData("[1.0.0,1.0.0)")]
    [InlineData("(1.0.0,1.0.0]")]
    [InlineData("[2.0.0,1.0.0]")]
    public void Parse_RequiresVersionRange_RejectsDegenerateIntervals(string range)
    {
        // 与 Core SemanticVersionRange.IsValid 同语义：min==max 仅双闭合法、min>max 非法。
        string json = "{\"packId\":\"test.range\",\"packVersion\":\"1.0.0\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\","
            + "\"requires\":{\"packs\":[{\"packId\":\"community.b\",\"versionRange\":\""
            + range + "\"}]}}";

        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, error => error.Contains("版本范围不合法"));
    }

    [Fact]
    public void Parse_CompositionFields_DefaultToEmpty()
    {
        Assert.True(CompatPackManifest.TryParse(ValidMinimal, out var manifest, out _));

        Assert.Empty(manifest!.RequiredPacks);
        Assert.Empty(manifest.ConflictingPacks);
        Assert.Empty(manifest.Overrides);
    }

    [Theory]
    [InlineData("{\"unknown\":[]}")]
    [InlineData("[{\"packId\":\"community.b\",\"versionRange\":\"1.0.0\"}]")]
    [InlineData("{\"packs\":[{\"packId\":\"community.b\"}]}")]
    [InlineData("{\"packs\":[{\"versionRange\":\"1.0.0\"}]}")]
    [InlineData("{\"packs\":[{\"packId\":\"community.b\",\"versionRange\":\"1.0.0\",\"extra\":true}]}")]
    [InlineData("{\"packs\":[{\"packId\":\"community.b\",\"versionRange\":\"latest\"}]}")]
    [InlineData("{\"packs\":[{\"packId\":\"BAD ID\",\"versionRange\":\"1.0.0\"}]}")]
    [InlineData("{\"packs\":[{\"packId\":\"community.b\",\"versionRange\":\"1.0.0\"},{\"packId\":\"community.b\",\"versionRange\":\"[1.0.0,2.0.0)\"}]}")]
    public void Parse_RequiresInvalid_Fails(string requiresJson)
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"requires\"", requiresJson), out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("{\"unknown\":[]}")]
    [InlineData("[\"community.legacy\"]")]
    [InlineData("{\"packs\":[\"UPPER\"]}")]
    [InlineData("{\"packs\":[\"community.legacy\",\"community.legacy\"]}")]
    public void Parse_ConflictsInvalid_Fails(string conflictsJson)
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"conflicts\"", conflictsJson), out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"symbol\":\"INSTRUCTION:SETANIMETIMER\",\"priority\":1}]")]
    [InlineData("[{\"symbol\":\"command:SETANIMETIMER\",\"priority\":1}]")]
    [InlineData("[{\"symbol\":\"instruction:\",\"priority\":1}]")]
    [InlineData("[{\"symbol\":\"instruction:HAS SPACE\",\"priority\":1}]")]
    [InlineData("[{\"symbol\":\"instruction:SETANIMETIMER\",\"priority\":-1}]")]
    [InlineData("[{\"symbol\":\"instruction:SETANIMETIMER\",\"priority\":1.5}]")]
    [InlineData("[{\"symbol\":\"instruction:SETANIMETIMER\",\"priority\":1,\"extra\":true}]")]
    [InlineData("[{\"symbol\":\"instruction:SETANIMETIMER\",\"priority\":1},{\"symbol\":\"instruction:setanimetimer\",\"priority\":2}]")]
    public void Parse_OverridesInvalid_Fails(string overridesJson)
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"overrides\"", overridesJson), out _, out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Parse_OverrideWithoutPriority_DefaultsToZero()
    {
        // spec §5.2：priority 为非负整数、缺省 0（final review M2）。只有需要仲裁时才写。
        Assert.True(
            CompatPackManifest.TryParse(
                WithField("\"overrides\"", "[{\"symbol\":\"instruction:SETANIMETIMER\"}]"),
                out var manifest, out var errors),
            string.Join("; ", errors));
        CompatPackOverride entry = Assert.Single(manifest!.Overrides);
        Assert.Equal("instruction:SETANIMETIMER", entry.Symbol);
        Assert.Equal(0, entry.Priority);
    }

    [Fact]
    public void Parse_OverrideSymbol_IsCanonicalized()
    {
        // instruction/variant 名字按引擎语义 Trim+Upper；函数名只 Trim（CJK 原样）。
        const string json = """
        {
          "packId": "test.canonical",
          "packVersion": "1.0.0",
          "contractAbi": "1.0",
          "engineApi": "1.0",
          "overrides": [
            { "symbol": "instruction: setanimetimer ", "priority": 1 },
            { "symbol": "function:sql_connect", "priority": 2 },
            { "symbol": "variant:setbgimage", "priority": 3 }
          ]
        }
        """;

        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        Assert.Equal("instruction:SETANIMETIMER", manifest!.Overrides[0].Symbol);
        Assert.Equal("function:sql_connect", manifest.Overrides[1].Symbol);
        Assert.Equal("variant:SETBGIMAGE", manifest.Overrides[2].Symbol);
    }

    [Fact]
    public void Schema_RequiresBaseSurfaceHashForSurfaceOrVariants()
    {
        string path = Path.Combine(
            FindRepositoryRoot(), "src", "EmueraCompatPack", "compatpack.manifest.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        // M1：hash 形态与 parser/文档一致，恰好 64 位小写十六进制。
        Assert.Equal(
            "^[0-9a-f]{64}$",
            root.GetProperty("properties").GetProperty("baseSurfaceHash").GetProperty("pattern").GetString());

        // I1：draft-07 if/then —— 非空 surface 或 variantSelections → required baseSurfaceHash。
        JsonElement condition = root.GetProperty("if");
        Assert.Equal(2, condition.GetProperty("anyOf").GetArrayLength());
        JsonElement then = root.GetProperty("then");
        Assert.Contains(
            "baseSurfaceHash",
            then.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        // M3：then 分支必须收紧为 string（null 会在运行期被规则层拒载，schema 不得放行）。
        JsonElement thenHash = then.GetProperty("properties").GetProperty("baseSurfaceHash");
        Assert.Equal("string", thenHash.GetProperty("type").GetString());
        Assert.Equal("^[0-9a-f]{64}$", thenHash.GetProperty("pattern").GetString());
    }

    [Fact]
    public void Schema_DeclaresCompositionFields()
    {
        string path = Path.Combine(
            FindRepositoryRoot(), "src", "EmueraCompatPack", "compatpack.manifest.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        // R2：组合字段不升 schema 版本（仍为 v1.1），字段扩展只加定义。
        Assert.Equal("gemuera:compatpack:manifest:schema:v1.1", root.GetProperty("$id").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());

        JsonElement properties = root.GetProperty("properties");
        JsonElement requires = properties.GetProperty("requires");
        JsonElement conflicts = properties.GetProperty("conflicts");
        JsonElement overrides = properties.GetProperty("overrides");
        Assert.False(requires.GetProperty("additionalProperties").GetBoolean());
        Assert.False(conflicts.GetProperty("additionalProperties").GetBoolean());

        JsonElement requiresPacks = requires.GetProperty("properties").GetProperty("packs");
        JsonElement requiresItem = requiresPacks.GetProperty("items");
        Assert.False(requiresItem.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "packId", "versionRange" },
            requiresItem.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(
            "^[a-z0-9][a-z0-9.\\-]*$",
            requiresItem.GetProperty("properties").GetProperty("packId").GetProperty("pattern").GetString());
        string versionRangePattern = requiresItem.GetProperty("properties").GetProperty("versionRange")
            .GetProperty("pattern").GetString()!;
        // 与运行期 IsValidVersionRange 对齐：合法精确 SemVer 或四种区间；退化区间
        //（min==max 但非双闭）由 schema 预先拒绝（运行期仍做语义比较兜底）。
        Assert.Matches(versionRangePattern, "1.0.0");
        Assert.Matches(versionRangePattern, "[1.0.0,2.0.0)");
        Assert.Matches(versionRangePattern, "(1.0.0,2.0.0]");
        Assert.Matches(versionRangePattern, "[1.0.0,2.0.0]");
        Assert.Matches(versionRangePattern, "(1.0.0,2.0.0)");
        Assert.Matches(versionRangePattern, "[1.0.0,1.0.0]");
        Assert.DoesNotMatch(versionRangePattern, "[1.0.0,1.0.0)");
        Assert.DoesNotMatch(versionRangePattern, "(1.0.0,1.0.0]");
        Assert.DoesNotMatch(versionRangePattern, "(1.0.0,1.0.0)");

        JsonElement conflictItem = conflicts.GetProperty("properties").GetProperty("packs").GetProperty("items");
        Assert.Equal("^[a-z0-9][a-z0-9.\\-]*$", conflictItem.GetProperty("pattern").GetString());

        Assert.True(overrides.GetProperty("uniqueItems").GetBoolean());
        JsonElement overrideItem = overrides.GetProperty("items");
        Assert.False(overrideItem.GetProperty("additionalProperties").GetBoolean());
        // M2：priority 可省略（缺省 0），schema 与 parser 一致。
        Assert.Equal(
            new[] { "symbol" },
            overrideItem.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(
            "^(instruction|function|variant):[^\\s:]+$",
            overrideItem.GetProperty("properties").GetProperty("symbol").GetProperty("pattern").GetString());
        JsonElement prioritySchema = overrideItem.GetProperty("properties").GetProperty("priority");
        Assert.Equal(0, prioritySchema.GetProperty("minimum").GetInt32());
        Assert.Equal(0, prioritySchema.GetProperty("default").GetInt32());
    }

    [Fact]
    public void Schema_DeclaresDualAxisAndLegacyBranches()
    {
        string path = Path.Combine(
            FindRepositoryRoot(), "src", "EmueraCompatPack", "compatpack.manifest.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        // v1.1：双轴为规范格式，legacy targetEngineApi 仅作迁移期兼容。
        Assert.Equal("gemuera:compatpack:manifest:schema:v1.1", root.GetProperty("$id").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "packId", "packVersion" },
            root.GetProperty("required").EnumerateArray().Select(item => item.GetString()));

        JsonElement properties = root.GetProperty("properties");
        Assert.True(properties.TryGetProperty("contractAbi", out JsonElement contractAbi));
        Assert.True(properties.TryGetProperty("engineApi", out JsonElement engineApi));
        Assert.True(properties.TryGetProperty("targetEngineApi", out JsonElement targetEngineApi));
        const string versionPattern = "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$";
        Assert.Equal(versionPattern, contractAbi.GetProperty("pattern").GetString());
        Assert.Equal(versionPattern, engineApi.GetProperty("pattern").GetString());

        // legacy 字段只接受 1（JSON 数字或字符串 "1"），与运行期解析器一致。
        JsonElement[] legacyValues = targetEngineApi.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, legacyValues.Length);
        Assert.Contains(legacyValues, value => value.TryGetProperty("const", out JsonElement constant)
            && constant.ValueKind == JsonValueKind.Number && constant.GetInt32() == 1);
        Assert.Contains(legacyValues, value => value.TryGetProperty("const", out JsonElement constant)
            && constant.ValueKind == JsonValueKind.String && constant.GetString() == "1");

        // 顶层 oneOf：双轴分支要求双轴同存且禁 targetEngineApi；legacy 分支要求 targetEngineApi 且禁双轴。
        JsonElement[] branches = root.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, branches.Length);

        JsonElement dualAxisBranch = Assert.Single(
            branches,
            branch => RequiredFields(branch).SetEquals(new[] { "contractAbi", "engineApi" }));
        Assert.Equal(
            JsonValueKind.False,
            dualAxisBranch.GetProperty("properties").GetProperty("targetEngineApi").ValueKind);

        JsonElement legacyBranch = Assert.Single(
            branches,
            branch => RequiredFields(branch).SetEquals(new[] { "targetEngineApi" }));
        Assert.Equal(JsonValueKind.False, legacyBranch.GetProperty("properties").GetProperty("contractAbi").ValueKind);
        Assert.Equal(JsonValueKind.False, legacyBranch.GetProperty("properties").GetProperty("engineApi").ValueKind);

        static HashSet<string> RequiredFields(JsonElement branch) =>
            branch.GetProperty("required").EnumerateArray()
                .Select(item => item.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>从测试输出目录向上查找含 AGENTS.md 的仓库根（不硬编码机器路径）。</summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "未能从测试输出目录向上找到含 AGENTS.md 的仓库根：" + AppContext.BaseDirectory);
    }
}
