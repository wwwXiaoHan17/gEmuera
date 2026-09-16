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
        "{\"packId\":\"test.hello-pack\",\"packVersion\":\"1.2.3\",\"targetEngineApi\":2,"
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
        Assert.Equal(2, manifest.TargetEngineApi);
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
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\"}", "targetEngineApi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":0}", "targetEngineApi")]
    [InlineData("{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":-1}", "targetEngineApi")]
    public void Parse_InvalidRequiredFields_FailWithFieldNamed(string json, string field)
    {
        Assert.False(CompatPackManifest.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains(field));
    }

    [Fact]
    public void Parse_TargetEngineApiAsString_Succeeds()
    {
        // schema 接受字符串形式（避免包作者踩 JSON 数字精度坑），解析器同样接受。
        string json = "{\"packId\":\"test.pack\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":\"3\"}";
        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out _));
        Assert.Equal(3, manifest!.TargetEngineApi);
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
    }

    [Fact]
    public void Parse_VariantSelectionsInvalidEntries_Fail()
    {
        Assert.False(CompatPackManifest.TryParse(WithField("\"variantSelections\"", "{\"\":\"builtin:x\"}"), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"variantSelections\"", "{\"PRINT\":3}"), out _, out _));
        Assert.False(CompatPackManifest.TryParse(WithField("\"variantSelections\"", "{\"PRINT\":\"has space\"}"), out _, out _));
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
}
