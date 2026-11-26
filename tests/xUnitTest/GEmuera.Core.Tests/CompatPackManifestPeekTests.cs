using System.Text;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 「只看清单、不执行」的包可用性预检（<see cref="CompatPackManifestPeek"/>）门禁：
///   (a) PE 字节读出的内嵌清单与加载后的程序集 API 读到的一致；
///   (b) 判据与规则层同源——预检给出的每条头部错误都必须出现在
///       <see cref="CompatPackRules.Validate"/> 的错误里，预检通过 ⇔ 规则层头部规则通过；
///   (c) 与真实加载器对账：真实第一方包字节可读且兼容；把清单哈希改旧后加载器拒载、
///       预检同样判 SurfaceHashMismatch。
/// 包字节与过期副本见 <see cref="FirstPartyPackBytes"/>。
/// </summary>
[Collection("CompatPack")]
public class CompatPackManifestPeekTests
{
    static string StaleHash => FirstPartyPackBytes.StaleHash;

    static CompatPackValidationContext Context() => FirstPartyPackBytes.Context();

    static CompatPackEngineHeader Engine() => FirstPartyPackBytes.EngineHeader();

    static byte[] StaleSnakeBytes() => FirstPartyPackBytes.StaleSnake();

    static string MinimalJson(
        string packId = "pack.test.peek",
        string baseProfileId = "v24pure",
        string contractAbi = "1.0",
        string engineApi = "1.0",
        string surface = "{}",
        string? baseSurfaceHash = null,
        string? saveProfileId = null)
    {
        var parts = new List<string>
        {
            "\"packId\":\"" + packId + "\"",
            "\"packVersion\":\"1.0.0\"",
            "\"baseProfileId\":\"" + baseProfileId + "\"",
            "\"contractAbi\":\"" + contractAbi + "\"",
            "\"engineApi\":\"" + engineApi + "\"",
            "\"capabilities\":[]",
            "\"surface\":" + surface,
        };
        if (baseSurfaceHash is not null)
            parts.Add("\"baseSurfaceHash\":\"" + baseSurfaceHash + "\"");
        if (saveProfileId is not null)
            parts.Add("\"saveProfileId\":\"" + saveProfileId + "\"");
        return "{" + string.Join(",", parts) + "}";
    }

    static CompatPackManifest Parse(string json)
    {
        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        return manifest!;
    }

    // —— (a) PE 字节读取 ————————————————————————————————————————

    [Theory]
    [InlineData(typeof(PackGemueraMegaten.PackGemueraMegatenMarker))]
    [InlineData(typeof(PackGemueraEraBlue.PackGemueraEraBlueMarker))]
    [InlineData(typeof(PackGemueraEraFl.PackGemueraEraFlMarker))]
    [InlineData(typeof(PackGemueraSnake.PackGemueraSnakeMarker))]
    [InlineData(typeof(GemueraV18Pack.GemueraV18PackMarker))]
    public void EmbeddedManifest_ReadFromBytes_EqualsAssemblyApi(Type markerType)
    {
        byte[] bytes = File.ReadAllBytes(markerType.Assembly.Location);
        Assert.True(CompatPackManifest.TryLoadFromAssembly(markerType.Assembly, out var viaApi, out var apiErrors),
            string.Join("; ", apiErrors));

        Assert.True(CompatPackManifestPeek.TryReadEmbeddedManifest(bytes, out byte[] manifestBytes, out string error), error);
        Assert.True(CompatPackManifest.TryParse(Encoding.UTF8.GetString(manifestBytes), out var viaBytes, out var parseErrors),
            string.Join("; ", parseErrors));

        Assert.Equal(viaApi!.PackId, viaBytes!.PackId);
        Assert.Equal(viaApi.PackVersion, viaBytes.PackVersion);
        Assert.Equal(viaApi.BaseSurfaceHash, viaBytes.BaseSurfaceHash);
        Assert.Equal(viaApi.ContractAbi, viaBytes.ContractAbi);
        Assert.Equal(viaApi.EngineApi, viaBytes.EngineApi);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { (byte)'M', (byte)'Z' })]
    public void EmbeddedManifest_UnreadableBytes_ReturnsFalseWithoutThrowing(byte[] bytes)
    {
        Assert.False(CompatPackManifestPeek.TryReadEmbeddedManifest(bytes, out _, out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void EmbeddedManifest_TruncatedRealPack_ReturnsFalse()
    {
        byte[] truncated = FirstPartyPackBytes.FreshSnake()[..128];
        Assert.False(CompatPackManifestPeek.TryReadEmbeddedManifest(truncated, out _, out string error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void EmbeddedManifest_AssemblyWithoutManifestResource_ReturnsFalse()
    {
        // 本测试程序集自身是合法托管程序集，但没有内嵌 compatpack.manifest.json。
        byte[] bytes = File.ReadAllBytes(typeof(CompatPackManifestPeekTests).Assembly.Location);
        Assert.False(CompatPackManifestPeek.TryReadEmbeddedManifest(bytes, out _, out string error));
        Assert.Contains("compatpack.manifest.json", error);
    }

    // —— (b) 与规则层同源 ————————————————————————————————————————

    [Theory]
    [InlineData(typeof(PackGemueraMegaten.PackGemueraMegatenMarker))]
    [InlineData(typeof(PackGemueraEraBlue.PackGemueraEraBlueMarker))]
    [InlineData(typeof(PackGemueraEraFl.PackGemueraEraFlMarker))]
    [InlineData(typeof(PackGemueraSnake.PackGemueraSnakeMarker))]
    [InlineData(typeof(GemueraV18Pack.GemueraV18PackMarker))]
    public void FreshFirstPartyPacks_AreCompatible(Type markerType)
    {
        var result = CompatPackManifestPeek.Assess(File.ReadAllBytes(markerType.Assembly.Location), Engine());

        Assert.True(result.IsCompatible, string.Join("; ", result.Errors));
        Assert.Equal(markerType.Assembly.GetName().Name, result.Manifest!.PackId);
        Assert.Empty(result.HeaderErrors);
    }

    [Fact]
    public void StaleSurfaceHash_IsRejectedWithSurfaceHashMismatch()
    {
        var result = CompatPackManifestPeek.Assess(StaleSnakeBytes(), Engine());

        Assert.Equal(CompatPackPeekVerdict.Rejected, result.Verdict);
        Assert.Equal(CompatPackHeaderRule.SurfaceHashMismatch, result.FirstRule);
        Assert.Equal("pack.gemuera.snake", result.Manifest!.PackId);
        Assert.Contains(result.Errors, error => error.Contains("baseSurfaceHash") && error.Contains(StaleHash));
    }

    /// <summary>头部规则用例：(清单 JSON, 期望的首条规则；None 表示兼容)。用枚举本身而不是可空包装，保证可被测试发现器枚举。</summary>
    public static IEnumerable<object[]> HeaderRuleCases()
    {
        yield return new object[] { MinimalJson(), CompatPackHeaderRule.None };
        yield return new object[] { MinimalJson(contractAbi: "2.0"), CompatPackHeaderRule.ContractAbiMismatch };
        yield return new object[] { MinimalJson(contractAbi: "1.2"), CompatPackHeaderRule.ContractAbiMismatch };
        yield return new object[] { MinimalJson(engineApi: "2.0"), CompatPackHeaderRule.EngineApiMismatch };
        yield return new object[] { MinimalJson(engineApi: "1.1"), CompatPackHeaderRule.EngineApiMismatch };
        yield return new object[] { MinimalJson(baseProfileId: "v18"), CompatPackHeaderRule.BaseProfileNotV24Pure };
        yield return new object[]
        {
            MinimalJson(surface: "{\"addInstructions\":[\"FOO\"]}"),
            CompatPackHeaderRule.SurfaceHashMissing,
        };
        yield return new object[]
        {
            MinimalJson(surface: "{\"addInstructions\":[\"FOO\"]}", baseSurfaceHash: StaleHash),
            CompatPackHeaderRule.SurfaceHashMismatch,
        };
        yield return new object[] { MinimalJson(saveProfileId: "legacy.v1"), CompatPackHeaderRule.SaveProfileDeclared };
        yield return new object[] { MinimalJson(packId: "game.snake"), CompatPackHeaderRule.ReservedModuleId };
    }

    [Theory]
    [MemberData(nameof(HeaderRuleCases))]
    public void PeekVerdict_AndEveryHeaderError_AreExactlyWhatTheRulesReport(string json, CompatPackHeaderRule expectedRule)
    {
        CompatPackManifest manifest = Parse(json);
        var context = Context();

        var peek = CompatPackManifestPeek.Assess(manifest, CompatPackEngineHeader.FromContext(context));
        bool accepted = CompatPackRules.Validate(
            manifest, Array.Empty<ICompatPackContribution>(), context, out var ruleErrors, out _);

        Assert.Equal(accepted, peek.IsCompatible);
        Assert.Equal(expectedRule == CompatPackHeaderRule.None ? (CompatPackHeaderRule?)null : expectedRule, peek.FirstRule);
        // 同源判据：预检的每条头部错误都是规则层原样输出的错误文本。
        foreach (CompatPackHeaderError headerError in peek.HeaderErrors)
            Assert.Contains(headerError.Message, ruleErrors);
    }

    [Fact]
    public void Peek_GivesShortReasonForStatusBar()
    {
        Assert.Contains("表面哈希", CompatPackManifestPeek.Assess(StaleSnakeBytes(), Engine()).ShortReason);
        Assert.Contains("contractAbi", CompatPackManifestPeek.Assess(Parse(MinimalJson(contractAbi: "2.0")), Engine()).ShortReason);
        Assert.Contains("读取", CompatPackManifestPeek.Assess(new byte[] { 1, 2, 3 }, Engine()).ShortReason);
        Assert.Equal("", CompatPackManifestPeek.Assess(FirstPartyPackBytes.FreshSnake(), Engine()).ShortReason);
    }

    // —— (c) 与真实加载器对账 ————————————————————————————————————

    [Fact]
    public void PeekVerdict_MatchesLoaderVerdict_ForFreshAndStaleBytes()
    {
        var context = Context();
        using var directory = new IsolatedPackDirectory(typeof(PackGemueraSnake.PackGemueraSnakeMarker).Assembly.Location);

        // 新包：加载器接受，预检通过。
        bool loadedFresh = CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out var handle, out var freshErrors);
        Assert.True(loadedFresh, string.Join("; ", freshErrors));
        handle!.Unload();
        Assert.True(CompatPackManifestPeek.Assess(File.ReadAllBytes(directory.MainAssemblyPath), Engine()).IsCompatible);

        // 过期包：加载器按表面哈希拒载，预检给出同一条规则。
        directory.WriteMainAssembly(StaleSnakeBytes());
        bool loadedStale = CompatPackLoader.TryLoad(directory.MainAssemblyPath, context, out _, out var staleErrors);
        Assert.False(loadedStale);
        Assert.Contains(staleErrors, error => error.Contains("baseSurfaceHash"));
        var peek = CompatPackManifestPeek.Assess(File.ReadAllBytes(directory.MainAssemblyPath), Engine());
        Assert.Equal(CompatPackPeekVerdict.Rejected, peek.Verdict);
        Assert.Equal(CompatPackHeaderRule.SurfaceHashMismatch, peek.FirstRule);
    }

    [Fact]
    public void Assess_UnreadableBytes_IsUnreadableWithErrors()
    {
        var result = CompatPackManifestPeek.Assess(new byte[] { 1, 2, 3 }, Engine());

        Assert.Equal(CompatPackPeekVerdict.Unreadable, result.Verdict);
        Assert.Null(result.Manifest);
        Assert.False(result.IsCompatible);
        Assert.NotEmpty(result.Errors);
    }
}
