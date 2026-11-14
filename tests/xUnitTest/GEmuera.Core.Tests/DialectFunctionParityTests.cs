using System.Globalization;
using System.IO;
using MinorShift.Emuera.Content;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.Sub;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 蛇系函数（SPRITECREATE / SPRITECREATEFROMFILE / TOINT / ARGLEN）与 Skiav17.2
/// 参考实现的语义对齐门禁。被测逻辑是宿主里抽出的无 Godot 依赖判定类，测试工程
/// 按 tools/core-contracts 的既有做法直链同一份源码（见 csproj），避免"测试自证"。
/// </summary>
public class ArgLenSnakeContractTests
{
    // 参考：Emuera/Runtime/Script/Statements/Function/Creator.Method.cs:10816-10825
    // ArgLengthMethod 的 argumentTypeArrayEx = [{ ArgTypes = { } }] → 恰好 0 参。
    [Fact]
    public void CheckNoArguments_ZeroArguments_IsAccepted()
    {
        Assert.Null(DialectArgumentShape.CheckNoArguments("ARGLEN", 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CheckNoArguments_AnyArgument_IsRejected(int argumentCount)
    {
        string error = DialectArgumentShape.CheckNoArguments("ARGLEN", argumentCount);

        Assert.NotNull(error);
        Assert.Contains("ARGLEN", error);
    }
}
public class ToIntOverflowParityTests
{
    // 参考 ToIntMethod 用 try/catch 包 LexicalAnalyzer.ReadInt64（Creator.Method.cs:6142-6150），
    // 溢出/非法串一律返回 0；我方修复前裸调会把 CodeEE 抛给 ERB。
    static long ReadInt64LikeReference(StringStream st)
    {
        int start = st.CurrentPosition;
        if (st.Current == '+' || st.Current == '-')
            st.ShiftNext();
        while (!st.EOS && char.IsDigit(st.Current))
            st.ShiftNext();
        string digits = st.RowString.Substring(start, st.CurrentPosition - start);
        return long.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    static long ToInt(string str, int displayLength = -1)
        => ToIntTextParser.Parse(str, displayLength < 0 ? str?.Length ?? 0 : displayLength, ReadInt64LikeReference);

    [Fact]
    public void DecimalOverflow_ReturnsZero()
    {
        Assert.Equal(0, ToInt("99999999999999999999"));
    }

    [Fact]
    public void TrailingFraction_Truncates()
    {
        Assert.Equal(123, ToInt("123.45"));
    }

    [Fact]
    public void TrailingGarbage_ReturnsZero()
    {
        Assert.Equal(0, ToInt("123abc"));
    }

    [Fact]
    public void EmptyOrNull_ReturnsZero()
    {
        Assert.Equal(0, ToInt(""));
        Assert.Equal(0, ToInt(null!));
    }

    [Fact]
    public void FullWidthCharacters_ReturnZero()
    {
        // GetStrlenLang 对全角字符返回更长的显示宽度。
        Assert.Equal(0, ToInt("\uFF11\uFF12\uFF13", displayLength: 6));
    }

    [Theory]
    [InlineData("+", 0)]
    [InlineData("-", 0)]
    [InlineData("-42", -42)]
    [InlineData("007", 7)]
    public void LeadingSignAndDigits_FollowReference(string input, long expected)
    {
        Assert.Equal(expected, ToInt(input));
    }
}
public class SpriteCreateFromFileRelativePathTests
{
    // 参考 SpriteCreateFromFileMethod（Creator.Method.cs:8059-8106）：
    // 绝对路径原样；相对路径 isRelative=true → 相对（我方取游戏根），false → ContentDir。
    // 2 参（isRelative 未提供）保持我方平台适配：游戏根优先、再 resources（有意差异，不在此测）。
    const string GameRoot = @"C:\games\era";
    const string ContentDir = @"C:\games\era\resources";

    static string[] Candidates(string path, DynamicSpritePathMode mode)
        => DynamicSpritePathResolver.BuildCandidates(path, mode, GameRoot, ContentDir);

    [Fact]
    public void ResolveMode_FollowsThirdArgument()
    {
        // 未提供第 3 参 → 2 参兼容模式；true/false 分别对应参考的 relative / ContentDir 分支。
        Assert.Equal(DynamicSpritePathMode.LegacyGameRootThenContent, DynamicSpritePathResolver.ResolveMode(null));
        Assert.Equal(DynamicSpritePathMode.GameRoot, DynamicSpritePathResolver.ResolveMode(true));
        Assert.Equal(DynamicSpritePathMode.ContentDir, DynamicSpritePathResolver.ResolveMode(false));
    }

    [Fact]
    public void TwoArgumentForm_KeepsGameRootThenResources()
    {
        Assert.Equal(
            new[]
            {
                Path.Combine(GameRoot, "portrait/a.png"),
                Path.Combine(ContentDir, "portrait/a.png"),
            },
            Candidates("portrait/a.png", DynamicSpritePathResolver.ResolveMode(null)));
    }

    [Fact]
    public void IsRelativeTrue_ResolvesAgainstGameRootOnly()
    {
        Assert.Equal(
            new[] { Path.Combine(GameRoot, "portrait/a.png") },
            Candidates("portrait/a.png", DynamicSpritePathResolver.ResolveMode(true)));
    }

    [Fact]
    public void IsRelativeFalse_ResolvesAgainstContentDirOnly()
    {
        Assert.Equal(
            new[] { Path.Combine(ContentDir, "portrait/a.png") },
            Candidates("portrait/a.png", DynamicSpritePathResolver.ResolveMode(false)));
    }

    [Fact]
    public void IsRelativeFalse_StripsResourcesPrefixBeforeJoiningContentDir()
    {
        Assert.Equal(
            new[] { Path.Combine(ContentDir, "portrait/a.png") },
            Candidates("resources/portrait/a.png", DynamicSpritePathResolver.ResolveMode(false)));
    }

    [Fact]
    public void AbsolutePath_IsUsedAsIsForEveryMode()
    {
        string absolute = Path.Combine(GameRoot, "abs", "a.png");
        foreach (DynamicSpritePathMode mode in Enum.GetValues<DynamicSpritePathMode>())
            Assert.Equal(new[] { absolute }, Candidates(absolute, mode));
    }

    [Fact]
    public void UriLikePath_IsUsedAsIs()
    {
        Assert.Equal(
            new[] { "res://portrait/a.png" },
            Candidates("res://portrait/a.png", DynamicSpritePathResolver.ResolveMode(true)));
    }
}
public class SpriteCreateLayoutTests
{
    // 参考 SpriteCreateMethod（Creator.Method.cs:7995-8056）：2/6/8/10 参形态；
    // 6 参裁剪、8 参加偏移、10 参加目标尺寸（负值取绝对值）。修复前 8/10 参被静默忽略。
    static long Unexpected(int index) => throw new InvalidOperationException("不应读取实参 " + index);

    static SpriteCreateLayout Resolve(int argumentCount, int sx, int sy, int sw, int sh, Func<int, long> readArgument)
    {
        Assert.True(SpriteCreateArgumentLayout.TryResolve(argumentCount, sx, sy, sw, sh, readArgument, out SpriteCreateLayout layout));
        return layout;
    }

    [Fact]
    public void TwoArguments_UsesWholeImageWithoutReadingGeometry()
    {
        SpriteCreateLayout layout = Resolve(2, 0, 0, 200, 100, Unexpected);

        Assert.False(layout.HasSourceRectangle);
        Assert.Equal((0, 0, 200, 100), (layout.SourceX, layout.SourceY, layout.SourceWidth, layout.SourceHeight));
        Assert.Equal((0, 0), (layout.PositionX, layout.PositionY));
        Assert.Equal((200, 100), (layout.DestWidth, layout.DestHeight));
    }

    [Fact]
    public void SixArguments_CropsWithDestinationEqualToSourceRect()
    {
        SpriteCreateLayout layout = Resolve(6, 10, 20, 30, 40, Unexpected);

        Assert.True(layout.HasSourceRectangle);
        Assert.Equal((10, 20, 30, 40), (layout.SourceX, layout.SourceY, layout.SourceWidth, layout.SourceHeight));
        Assert.Equal((0, 0), (layout.PositionX, layout.PositionY));
        Assert.Equal((30, 40), (layout.DestWidth, layout.DestHeight));
    }

    [Fact]
    public void EightArguments_AppliesPositionOnTopOfCrop()
    {
        SpriteCreateLayout layout = Resolve(8, 10, 20, 30, 40, index => index == 6 ? 5 : 6);

        Assert.True(layout.HasSourceRectangle);
        Assert.Equal((10, 20, 30, 40), (layout.SourceX, layout.SourceY, layout.SourceWidth, layout.SourceHeight));
        Assert.Equal((5, 6), (layout.PositionX, layout.PositionY));
        Assert.Equal((30, 40), (layout.DestWidth, layout.DestHeight));
    }

    [Fact]
    public void TenArguments_AppliesPositionAndDestinationSize()
    {
        SpriteCreateLayout layout = Resolve(10, 10, 20, 30, 40, index => index switch
        {
            6 => 5,
            7 => 6,
            8 => 100,
            _ => 200,
        });

        Assert.Equal((5, 6), (layout.PositionX, layout.PositionY));
        Assert.Equal((100, 200), (layout.DestWidth, layout.DestHeight));
    }

    [Fact]
    public void TenArguments_TakesAbsoluteDestinationSize()
    {
        // 参考：if (dw < 0) dw = -dw; if (dh < 0) dh = -dh;（Creator.Method.cs:8043-8044）。
        SpriteCreateLayout layout = Resolve(10, 0, 0, 30, 40, index => index switch
        {
            8 => -100,
            _ => -200,
        });

        Assert.Equal((100, 200), (layout.DestWidth, layout.DestHeight));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(11)]
    public void UnsupportedArgumentCount_IsRejected(int argumentCount)
    {
        Assert.False(SpriteCreateArgumentLayout.TryResolve(argumentCount, 0, 0, 10, 10, Unexpected, out _));
    }
}
