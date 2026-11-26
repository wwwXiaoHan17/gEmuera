using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 「用户手装的过期第一方包」替换策略（<see cref="CompatPackStalePackPolicy"/>）门禁：
/// 只有当内置载荷对当前引擎可用、且目标是<b>同名且必然被拒</b>的包时才允许替换；
/// 目标读不出、不是包、是别的包、或仍然可用（哪怕字节不同）都一律保留。
/// 用真实 snake 包字节与「哈希改旧」的过期副本驱动，保证判定依据是真实清单而非测试桩。
/// </summary>
public class CompatPackStalePackPolicyTests
{
    static CompatPackBundlePayload FreshPayload() => new(FirstPartyPackBytes.SnakePackId, FirstPartyPackBytes.FreshSnake());
    static CompatPackBundlePayload StalePayload() => new(FirstPartyPackBytes.SnakePackId, FirstPartyPackBytes.StaleSnake());

    [Fact]
    public void FreshPayload_StaleSamePackTarget_ShouldReplace()
    {
        bool replace = CompatPackStalePackPolicy.ShouldReplaceStalePack(
            FreshPayload().DllBytes, FirstPartyPackBytes.StaleSnake(), FirstPartyPackBytes.EngineHeader(), out string reason);

        Assert.True(replace);
        Assert.Contains("表面哈希", reason);
    }

    [Fact]
    public void FreshPayload_NonPackTarget_IsKept()
    {
        // 用户随手放进包目录的 MZ 填充物：无法证明它是过期同名包，保留。
        var filler = new byte[256];
        filler[0] = (byte)'M';
        filler[1] = (byte)'Z';

        Assert.False(CompatPackStalePackPolicy.ShouldReplaceStalePack(
            FreshPayload().DllBytes, filler, FirstPartyPackBytes.EngineHeader(), out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(64)]
    public void FreshPayload_TruncatedOrEmptyTarget_IsKept(int length)
    {
        byte[] truncated = FirstPartyPackBytes.StaleSnake()[..length];

        Assert.False(CompatPackStalePackPolicy.ShouldReplaceStalePack(
            FreshPayload().DllBytes, truncated, FirstPartyPackBytes.EngineHeader(), out _));
    }

    [Fact]
    public void FreshPayload_StillUsableTargetWithDifferentBytes_IsKept()
    {
        // 用户自己修过/换过的同名包，只要当前引擎还能用就不动它。
        byte[] userBuild = FirstPartyPackBytes.WithDifferentTimestamp(FirstPartyPackBytes.FreshSnake());

        Assert.False(CompatPackStalePackPolicy.ShouldReplaceStalePack(
            FreshPayload().DllBytes, userBuild, FirstPartyPackBytes.EngineHeader(), out string reason));
        Assert.Contains("仍可用", reason);
    }

    [Fact]
    public void FreshPayload_StaleTargetOfAnotherPack_IsKept()
    {
        // 包目录里放的是另一个（仍然可用的）第一方包：packId 不同，不是「过期同名包」。
        byte[] otherPack = File.ReadAllBytes(typeof(PackGemueraMegaten.PackGemueraMegatenMarker).Assembly.Location);

        Assert.False(CompatPackStalePackPolicy.ShouldReplaceStalePack(
            FreshPayload().DllBytes, otherPack, FirstPartyPackBytes.EngineHeader(), out _));
    }

    [Fact]
    public void StalePayload_NeverReplacesAnything()
    {
        CompatPackEngineHeader engine = FirstPartyPackBytes.EngineHeader();

        Assert.False(CompatPackStalePackPolicy.ShouldReplaceStalePack(
            StalePayload().DllBytes, FirstPartyPackBytes.StaleSnake(), engine, out string staleReason));
        Assert.Contains("内置", staleReason);
        Assert.False(CompatPackStalePackPolicy.ShouldReplaceStalePack(
            StalePayload().DllBytes, FirstPartyPackBytes.FreshSnake(), engine, out _));
    }

    [Fact]
    public void FileReplacer_DecidesFromTargetFileContent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "gemuera-stale-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string target = Path.Combine(directory, "pack.gemuera.snake.dll");
            Func<CompatPackBundlePayload, string, bool> replacer =
                CompatPackStalePackPolicy.CreateStaleFirstPartyReplacer(FirstPartyPackBytes.EngineHeader());

            // 文件不存在：交给 installer 的「目标缺失 → 写入」分支，不需要替换。
            Assert.False(replacer(FreshPayload(), target));

            File.WriteAllBytes(target, FirstPartyPackBytes.StaleSnake());
            Assert.True(replacer(FreshPayload(), target));

            File.WriteAllBytes(target, new byte[] { 1, 2, 3 });
            Assert.False(replacer(FreshPayload(), target));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { /* 测试清理失败不影响断言。 */ }
        }
    }
}
