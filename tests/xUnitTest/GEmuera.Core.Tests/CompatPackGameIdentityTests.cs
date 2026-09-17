using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>gameIdentity CSV 解析与比对（设计 §3.3：不匹配拒载回退纯 v24）。</summary>
public class CompatPackGameIdentityTests
{
    // CompatPackGameIdentity 构造器在 Emuera 契约程序集内为 internal，经 public TryParse 构造。
    static CompatPackGameIdentity Identity(string accept = "exact")
    {
        string json = "{\"packId\":\"test.identity\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"gameIdentity\":{\"gameCode\":\"20250628\",\"version\":\"305\",\"versionAccept\":\"" + accept + "\"}}";
        return CompatPackManifest.TryParse(json, out var manifest, out var errors)
            ? manifest!.GameIdentity!
            : throw new InvalidOperationException(string.Join("; ", errors));
    }

    [Theory]
    [InlineData("コード,20250628\r\nバージョン,305\r\nタイトル,テスト", true, "20250628", "305")]
    [InlineData("代码,20250628\n版本,305", true, "20250628", "305")]
    [InlineData("\"コード\",\"20250628\"\n\"バージョン\",\"305\"", true, "20250628", "305")]
    [InlineData("タイトル,テスト\nコード,999\n", false, "", "")]
    [InlineData("", false, "", "")]
    public void ParseGameBaseCsv_Variants(string csv, bool expected, string code, string version)
    {
        Assert.Equal(expected, CompatPackGameIdentityCheck.TryParseGameBaseCsv(csv, out var parsedCode, out var parsedVersion));
        Assert.Equal(code, parsedCode);
        Assert.Equal(version, parsedVersion);
    }

    [Fact]
    public void ParseGameBaseCsv_NullText_ReturnsFalse()
    {
        Assert.False(CompatPackGameIdentityCheck.TryParseGameBaseCsv(null, out _, out _));
    }

    [Fact]
    public void Matches_Exact_Succeeds()
    {
        Assert.True(CompatPackGameIdentityCheck.Matches(Identity(), "20250628", "305"));
    }

    [Fact]
    public void Matches_WrongCode_Fails()
    {
        Assert.False(CompatPackGameIdentityCheck.Matches(Identity(), "11111111", "305"));
    }

    [Fact]
    public void Matches_ExactVersionMismatch_Fails()
    {
        Assert.False(CompatPackGameIdentityCheck.Matches(Identity(), "20250628", "306"));
    }

    [Fact]
    public void Matches_MinorAcceptsSameFirstSegment()
    {
        string json = "{\"packId\":\"test.identity\",\"packVersion\":\"1.0.0\",\"targetEngineApi\":1,"
            + "\"gameIdentity\":{\"gameCode\":\"20250628\",\"version\":\"3.43\",\"versionAccept\":\"minor\"}}";
        var minor = CompatPackManifest.TryParse(json, out var manifest, out var errors)
            ? manifest!.GameIdentity!
            : throw new InvalidOperationException(string.Join("; ", errors));
        Assert.True(CompatPackGameIdentityCheck.Matches(minor, "20250628", "3.50"));
        Assert.False(CompatPackGameIdentityCheck.Matches(minor, "20250628", "4.50"));
    }

    [Fact]
    public void Matches_MissingValues_FailClosed()
    {
        Assert.False(CompatPackGameIdentityCheck.Matches(Identity(), null, "305"));
        Assert.False(CompatPackGameIdentityCheck.Matches(Identity(), "20250628", ""));
    }
}
