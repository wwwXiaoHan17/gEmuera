using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MinorShift.Emuera.GameData;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 常数表（declare.const-table.v1，上游 Emuera-SKIA Skiav18.0 的 ConstArrayTable）读取与合并规则的
/// 纯规则层测试：文件访问经 IConstArrayTableFiles 注入内存数据，不触碰磁盘。
/// 期望值取自上游 CHANGELOG [18.0.0] 的示例表与 ConstArrayTable.cs 的分支语义。
/// 引擎侧磁盘/编码访问见 scripts/Emuera/GameData/ConstArrayTableDiskFiles.cs（xUnit 覆盖不到）。
/// </summary>
public class ConstArrayTableTests
{
    /// <summary>内存文件源：CSV/ 前缀为 CSV 目录顶层，ERB/ 前缀为 ERB 目录（含子目录），按路径排序。</summary>
    private sealed class MemoryFiles : IConstArrayTableFiles
    {
        private readonly Dictionary<string, string[]> contents = new(StringComparer.OrdinalIgnoreCase);

        public MemoryFiles Csv(string fileName, params string[] lines) => Add("CSV/" + fileName, lines);

        public MemoryFiles Erb(string fileName, params string[] lines) => Add("ERB/" + fileName, lines);

        private MemoryFiles Add(string path, string[] lines)
        {
            contents[Normalize(path)] = lines;
            return this;
        }

        private static string Normalize(string path) => path.Replace('\\', '/');

        public IReadOnlyList<string> FindFiles(string fileName, bool fromErb)
        {
            string prefix = fromErb ? "ERB/" : "CSV/";
            return contents.Keys
                .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool FileExists(string path) => contents.ContainsKey(Normalize(path));

        public string[] ReadAllLines(string path)
        {
            if (!contents.TryGetValue(Normalize(path), out string[]? lines))
                throw new FileNotFoundException(path);
            return lines;
        }
    }

    private static long[] Ints(ConstArrayTable.Result result) => Assert.IsType<long[]>(result.Values);

    private static double[] Floats(ConstArrayTable.Result result) => Assert.IsType<double[]>(result.Values);

    private static string[] Strs(ConstArrayTable.Result result) => Assert.IsType<string[]>(result.Values);

    // ---- 一维表 ----------------------------------------------------------

    [Fact]
    public void Load1D_ChangelogExample_ReadsValuesAndNames()
    {
        var files = new MemoryFiles().Csv("表.csv", "1,变量列名1,1", "3,变量列名3,9");

        var result = ConstArrayTable.Load("表", new[] { 7 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal("CSV/表.csv", result.Path);
        Assert.Equal(new long[] { 0, 1, 0, 9, 0, 0, 0 }, Ints(result));
        Assert.Equal(1, result.Names[0]["变量列名1"]);
        Assert.Equal(3, result.Names[0]["变量列名3"]);
        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { "CSV/表.csv" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load1D_IndexAndNameBothEmpty_CarriesPreviousIndex()
    {
        var files = new MemoryFiles().Csv("表.csv", ",,5", ",,6", "3,,7");

        var result = ConstArrayTable.Load("表", new[] { 4 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 5, 6, 0, 7 }, Ints(result));
        Assert.Empty(result.Names[0]);
    }

    [Fact]
    public void Load1D_SkipsBlankCommentAndAllEmptyRows_AndTrimsFields()
    {
        var files = new MemoryFiles().Csv("表.csv", "", "  ; 注释行", "  , , ", " 0 , 名 , 5 ");

        var result = ConstArrayTable.Load("表", new[] { 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 5, 0 }, Ints(result));
        Assert.Equal(0, result.Names[0]["名"]);
    }

    [Fact]
    public void Load1D_ByteOrderMarkOnFirstCell_IsTrimmed()
    {
        var files = new MemoryFiles().Csv("表.csv", "\uFEFF1,\uFEFF名,5");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(5, Ints(result)[1]);
        Assert.Equal(1, result.Names[0]["名"]);
    }

    [Fact]
    public void Load1D_IndexBeyondDeclaredLength_IsIgnoredWithWarning()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,,1", "5,,2");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 1, 0, 0 }, Ints(result));
        var warning = Assert.Single(result.Warnings);
        Assert.StartsWith("常数表(CSV/表.csv)中，", warning);
        Assert.Contains("第 2 个有效行的索引 5 超出声明长度（3），该行已忽略", warning);
    }

    [Fact]
    public void Load1D_NameWithoutIndex_FailsInMainTable()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,甲,1", ",乙,2");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 2 个有效行写了注册名「乙」却没有注册数；有注册名就必须写注册数", error);
    }

    [Fact]
    public void Load1D_SameIndexTwoNames_FailsInMainTable()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,甲,1", "0,乙,2");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 2 个有效行：索引 0 已有名字「甲」，不能再取名「乙」", error);
    }

    [Fact]
    public void Load1D_BasicFormatViolations_AreNotEvenCandidates()
    {
        var files = new MemoryFiles().Csv("表.csv", "x,,1");
        ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);
        Assert.Equal("常数表的格式不合规：CSV/表.csv — 第 1 个有效行的首列不是编号", error);

        files = new MemoryFiles().Csv("表.csv", "0,1");
        ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out error);
        Assert.Equal("常数表的格式不合规：CSV/表.csv — 第 1 个有效行不足 3 列（一维表需要 [注册数,注册名,值]）", error);

        files = new MemoryFiles().Csv("表.csv", "0,甲,1", "0,乙");
        ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out error);
        Assert.Equal("常数表的格式不合规：CSV/表.csv — 第 2 个有效行不足 3 列（一维表需要 [注册数,注册名,值]）", error);
    }

    [Fact]
    public void Load1D_UnconvertibleValue_FailsMainCandidateWithPosition()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,甲,1.5");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 1 个有效行的值不是有效的整数：1.5", error);
    }

    [Fact]
    public void Load_EmptyOrCommentOnlyFile_IsNotAUsableTable()
    {
        var files = new MemoryFiles().Csv("表.csv", "", ";只有注释");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("常数表的格式不合规：CSV/表.csv — 空文件，或只有空行与注释行", error);
    }

    [Fact]
    public void Load_NoSameNameFile_ReportsMissingFile()
    {
        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, new MemoryFiles(), out string error);

        Assert.Null(result);
        Assert.Equal("常数表声明找不到同名文件（CSV/表.csv、CSV/表.erd，或 ERB 下的 表.ERD）", error);
    }

    [Fact]
    public void Load_IllegalDeclaredSize_IsRejectedBeforeReading()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,甲,1");

        ConstArrayTable.Load("表", new[] { 0 }, ConstArrayTable.KindInt, files, out string error);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 常数表对应的数组尺寸非法", error);
    }

    // ---- 二维表 ----------------------------------------------------------

    [Fact]
    public void Load2D_ChangelogExample_ReadsValuesAndNamesPerDimension()
    {
        var files = new MemoryFiles().Csv("表.csv",
            ",@2,1,2,3,4",
            "@1,,变量列名1,变量列名2,变量列名3,变量列名4",
            "0,变量名1,1,3,5,7",
            "2,变量名2,2,7,9,11",
            "3,变量名3,9,12,15,18");

        var result = ConstArrayTable.Load("表", new[] { 7, 5 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        long[] values = Ints(result);
        Assert.Equal(35, values.Length);
        // 行=第 1 维（注册数 0/2/3），列=第 2 维（注册数 1..4）；值按 [第1维, 第2维] 行优先展平。
        Assert.Equal(1, values[0 * 5 + 1]);
        Assert.Equal(7, values[0 * 5 + 4]);
        Assert.Equal(2, values[2 * 5 + 1]);
        Assert.Equal(11, values[2 * 5 + 4]);
        Assert.Equal(9, values[3 * 5 + 1]);
        Assert.Equal(18, values[3 * 5 + 4]);   // 表:3:4 == 表:变量名3:变量列名4
        Assert.Equal(0, values[0]);              // 未写值的 表:0:0
        Assert.Equal(3, result.Names[0]["变量名3"]);
        Assert.Equal(4, result.Names[1]["变量列名4"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Load2D_TransposedMarkers_SwapDimensionDirections()
    {
        // @1 落在 (1,2)、@2 落在 (2,1)：横向是第 1 维、纵向是第 2 维。
        var files = new MemoryFiles().Csv("表.csv",
            ",@1,1,2,3,4",
            "@2,,变量列名1,变量列名2,变量列名3,变量列名4",
            "0,变量名1,1,3,5,7",
            "2,变量名2,2,7,9,11",
            "3,变量名3,9,12,15,18");

        var result = ConstArrayTable.Load("表", new[] { 5, 7 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        long[] values = Ints(result);
        Assert.Equal(35, values.Length);
        Assert.Equal(18, values[4 * 7 + 3]);    // 第 1 维取列注册数 4，第 2 维取行注册数 3
        Assert.Equal(1, values[1 * 7 + 0]);
        Assert.Equal(3, result.Names[1]["变量名3"]);
        Assert.Equal(4, result.Names[0]["变量列名4"]);
    }

    [Fact]
    public void Load2D_EmptyRowAndColumnHeaders_CarryPreviousIndices()
    {
        // 顺延的行/列不能带名字（有注册名就必须写注册数），所以顺延列都是无名列。
        var files = new MemoryFiles().Csv("表.csv",
            ",@2,3,,",
            "@1,,,,",
            "0,乙,1,2",
            ",,3,4");

        var result = ConstArrayTable.Load("表", new[] { 2, 6 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        long[] values = Ints(result);
        // 列下标 3、4、5；行下标 0（乙）、1。
        Assert.Equal(1, values[0 * 6 + 3]);
        Assert.Equal(2, values[0 * 6 + 4]);
        Assert.Equal(3, values[1 * 6 + 3]);
        Assert.Equal(4, values[1 * 6 + 4]);
        Assert.Equal(0, values[0]);
        Assert.Equal(0, result.Names[0]["乙"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Load2D_UnquotedCommaInValue_OverflowsWidthWithSingleWarning()
    {
        // CHANGELOG 范例表：a,b 被切成两格，原来的 c 被挤到声明宽度（3）之外。
        var files = new MemoryFiles().Csv("范例表.csv",
            ",@2,1,2",
            "@1,,变量列名1,变量列名2",
            "0,变量名1,a,b,c",
            "1,变量名2,\"d\",\"e\"");

        var result = ConstArrayTable.Load("范例表", new[] { 2, 3 }, ConstArrayTable.KindStr, files, out string error);

        Assert.Null(error);
        string[] values = Strs(result);
        Assert.Equal("a", values[0 * 3 + 1]);
        Assert.Equal("b", values[0 * 3 + 2]);
        Assert.Equal("", values[0 * 3 + 0]);
        Assert.Equal("d", values[1 * 3 + 1]);
        Assert.Equal("e", values[1 * 3 + 2]);
        var warning = Assert.Single(result.Warnings);
        Assert.StartsWith("常数表(CSV/范例表.csv)中，", warning);
        Assert.Contains("第 3 个有效行、第 5 列的值超出声明宽度（3），该格被忽略", warning);
    }

    [Fact]
    public void Load2D_MarkersMisplaced_Fails()
    {
        var files = new MemoryFiles().Csv("表.csv", "@1,@2,1", "0,,1");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 常数表的 @1 / @2 必须放在 (2,1) 与 (1,2) 两处（哪个标记在哪一处均可）", error);
    }

    [Fact]
    public void Load2D_DuplicateMarkers_Fails()
    {
        var files = new MemoryFiles().Csv("表.csv", ",@1,1", "@1,,甲", "0,,1");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 常数表缺少 @1 / @2 标记，或两标记重复", error);
    }

    [Fact]
    public void Load2D_LayoutMarkedTableFor1DDeclaration_IsUnfit()
    {
        var files = new MemoryFiles().Csv("表.csv", ",@2,1", "@1,,名", "0,,1");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 含 @1 / @2 的常数表只能用于二维数组", error);
    }

    [Fact]
    public void Load2D_ColumnNameWithoutIndex_FailsInMainTable()
    {
        var files = new MemoryFiles().Csv("表.csv", ",@2,1,", "@1,,甲,乙", "0,,1", "1,,2");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 1 个有效行、第 4 列写了注册名「乙」却没有注册数；有注册名就必须写注册数", error);
    }

    [Fact]
    public void Load2D_RowNameWithoutIndex_FailsInMainTable()
    {
        var files = new MemoryFiles().Csv("表.csv", ",@2,1", "@1,,甲", "0,,1", ",乙,2");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 4 个有效行、第 1 列写了注册名「乙」却没有注册数；有注册名就必须写注册数", error);
    }

    [Fact]
    public void Load2D_BadRowIndexInMainTable_FailsWithPosition()
    {
        var files = new MemoryFiles().Csv("表.csv", ",@2,1", "@1,,甲", "0,,1", "x,,2");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 4 个有效行、第 1 列的注册数不是非负整数：x", error);
    }

    [Fact]
    public void Load_DimensionMismatch_FallsThroughToNextCandidateSilently()
    {
        // 二维声明下，一维表只是「不适配」而非写错：退到下一份候选，且不吵。
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1")
            .Csv("表.erd", ",@2,1", "@1,,乙", "0,,1");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal("CSV/表.erd", result.Path);
        Assert.Equal(1, Ints(result)[1]);
        Assert.Equal(1, result.Names[1]["乙"]);
        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { "CSV/表.erd", "CSV/表.csv" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load_2DDeclarationWithOnly1DCandidates_Fails()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,甲,1");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 不含 @1 / @2 的常数表只能用于一维数组", error);
    }

    // ---- 引号与切分 --------------------------------------------------------

    [Fact]
    public void Split_QuotedFieldKeepsCommasAndDoubledQuotesAreLiteral()
    {
        Assert.True(ConstArrayTable.TrySplitQuoted("a,\"b,c\",d", out string[] cells, out _, out _));
        Assert.Equal(new[] { "a", "b,c", "d" }, cells);

        Assert.True(ConstArrayTable.TrySplitQuoted("\"x\"\"y\"", out cells, out _, out _));
        Assert.Equal(new[] { "x\"y" }, cells);

        Assert.True(ConstArrayTable.TrySplitQuoted("a,", out cells, out _, out _));
        Assert.Equal(new[] { "a", "" }, cells);

        Assert.True(ConstArrayTable.TrySplitQuoted("他说\"你好\",C:\\temp", out cells, out _, out _));
        Assert.Equal(new[] { "他说\"你好\"", "C:\\temp" }, cells);

        // 字段开头的空白被跳过，引号是否生效只看第一个非空白字符。
        Assert.True(ConstArrayTable.TrySplitQuoted("  \"a,b\"  , x", out cells, out _, out _));
        Assert.Equal(new[] { "a,b", "x" }, cells);
    }

    [Fact]
    public void Split_UnclosedQuote_ReportsField()
    {
        bool ok = ConstArrayTable.TrySplitQuoted("0,名,\"abc", out string[] cells, out int badField, out string why);

        Assert.False(ok);
        Assert.Null(cells);
        Assert.Equal(3, badField);
        Assert.Equal("的双引号没有闭合（值里的引号要写成两个双引号，整段值也要用一对双引号包住）", why);
    }

    [Fact]
    public void Split_ContentAfterClosingQuote_SuggestsEscapedForm()
    {
        bool ok = ConstArrayTable.TrySplitQuoted("0,名,\"冷静\"特质", out string[] cells, out int badField, out string why);

        Assert.False(ok);
        Assert.Null(cells);
        Assert.Equal(3, badField);
        Assert.Equal("在结束引号后还有内容（值里的引号要写成两个双引号）；按这个规则整段值要写成 \"\"\"冷静\"\"特质\"", why);
    }

    [Fact]
    public void Load1D_StrTable_QuoteRulesFromChangelog()
    {
        var files = new MemoryFiles().Csv("表.csv",
            "0,名,\"a,b\"",
            "1,名,\"  a,b  \"",
            "2,名,\"\"\"\"",
            "3,名,\"\"\"abc\"",
            "4,名,\"abc\"\"\"",
            "5,名,\"a\"\"b\"",
            "6,名,他说\"你好\"",
            "7,名,it's",
            "8,名,C:\\temp",
            "9,名,\"c\"");

        var result = ConstArrayTable.Load("表", new[] { 10 }, ConstArrayTable.KindStr, files, out string error);

        Assert.Null(error);
        string[] values = Strs(result);
        Assert.Equal(new[]
        {
            "a,b", "a,b", "\"", "\"abc", "abc\"", "a\"b", "他说\"你好\"", "it's", "C:\\temp", "c",
        }, values);
        // 同一维度内注册名重复时以先出现者为准。
        Assert.Equal(0, result.Names[0]["名"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Load1D_UnquotedCommaInValue_FailsWithGuidance()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,名,a,b");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindStr, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 1 个有效行有 4 列，多于一维表固定的 3 列：值里含逗号时要用双引号把整个值包住（如 \"a,b\"）", error);
    }

    [Fact]
    public void Load_UnclosedQuoteInValue_FailsWithFieldPosition()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,甲,1", "1,名,\"abc");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindStr, files, out string error);

        Assert.Null(result);
        Assert.Equal("常数表的格式不合规：CSV/表.csv — 第 2 个有效行的第 3 个字段的双引号没有闭合（值里的引号要写成两个双引号，整段值也要用一对双引号包住）", error);
    }

    [Fact]
    public void Load_QuoteOnlyCountsWhenItIsTheFirstNonBlankCharacter()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,,x\"y\"z");

        var result = ConstArrayTable.Load("表", new[] { 1 }, ConstArrayTable.KindStr, files, out string error);

        Assert.Null(error);
        Assert.Equal("x\"y\"z", Strs(result)[0]);
    }

    // ---- 值类型 ----------------------------------------------------------

    [Fact]
    public void Load_FloatTable_ParsesDoubles()
    {
        var files = new MemoryFiles().Csv("表.csv", "0,,1.5", "1,,-2", "2,,NaN", "3,,oops");

        var result = ConstArrayTable.Load("表", new[] { 4 }, ConstArrayTable.KindFloat, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 4 个有效行的值不是有效的浮点数：oops", error);

        files = new MemoryFiles().Csv("表.csv", "0,,1.5", "1,,-2", "2,,NaN");
        result = ConstArrayTable.Load("表", new[] { 4 }, ConstArrayTable.KindFloat, files, out error);

        Assert.Null(error);
        double[] values = Floats(result);
        Assert.Equal(1.5, values[0]);
        Assert.Equal(-2.0, values[1]);
        Assert.True(double.IsNaN(values[2]));
        Assert.Equal(0.0, values[3]);
    }

    [Fact]
    public void Load_IntTable_QuotesDoNotChangeTheValueType()
    {
        // 引号只决定逗号算不算分隔符：整数表里的 "1,2" 仍是 1,2，照样报「不是有效的整数」。
        var files = new MemoryFiles().Csv("表.csv", "0,,\"1,2\"");

        var result = ConstArrayTable.Load("表", new[] { 1 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(result);
        Assert.Equal("同名常数表均不适用于该声明：CSV/表.csv — 第 1 个有效行的值不是有效的整数：1,2", error);
    }

    // ---- 主表、补充表与候选 ------------------------------------------------

    [Fact]
    public void Load_BadMainTable_FallsBackToNextCandidateAndWarns()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,x")
            .Csv("表.erd", "0,乙,7");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal("CSV/表.erd", result.Path);
        Assert.Equal(7, Ints(result)[0]);
        Assert.Equal(0, result.Names[0]["乙"]);
        Assert.False(result.Names[0].ContainsKey("甲"));
        Assert.Equal(
            "同名常数表候选 CSV/表.csv 未通过严格校验（第 1 个有效行的值不是有效的整数：x），已整份跳过",
            Assert.Single(result.Warnings));
        Assert.Equal(new[] { "CSV/表.erd", "CSV/表.csv" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load_CandidateOrder_CsvThenErdThenErbDir()
    {
        var files = new MemoryFiles()
            .Erb("表.erd", "0,,1")
            .Csv("表.erd", "0,,2")
            .Csv("表.csv", "0,,3");

        var result = ConstArrayTable.Load("表", new[] { 1 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal("CSV/表.csv", result.Path);
        Assert.Equal(3, Ints(result)[0]);
    }

    [Fact]
    public void Load_ExtensionFillsUnwrittenCellsAndConflictsKeepMain()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1", "2,,3")
            .Csv("表.erd", "0,甲2,9", "1,乙,5", "3,丙,4");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal("CSV/表.csv", result.Path);
        Assert.Equal(new long[] { 1, 5, 3 }, Ints(result));
        Assert.Equal(0, result.Names[0]["甲"]);
        Assert.Equal(1, result.Names[0]["乙"]);
        Assert.False(result.Names[0].ContainsKey("甲2"));
        Assert.False(result.Names[0].ContainsKey("丙"));
        Assert.Contains(result.Warnings, w => w == "常数表补充表(CSV/表.erd)中，索引 0 的值与主表不同（主表=1, 补充=9）；以主表为准");
        Assert.Contains(result.Warnings, w => w == "常数表补充表(CSV/表.erd)中，索引 0 的名字与主表不同（主表=\"甲\", 补充=\"甲2\"）；以主表为准");
        // 补充表里越界的第 3 行在解析阶段另有一条提示。
        Assert.Equal(3, result.Warnings.Count);
        Assert.Equal(new[] { "CSV/表.csv", "CSV/表.erd" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load_ExtensionBadCellOrNameOnly_WarnsAndSkipsJustThatPart()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1")
            .Csv("表.erd", "1,乙,x", ",丙,2", "2,丁,3");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 1, 0, 3 }, Ints(result));
        // 坏格只丢值：该行的注册名在解析值之前已登记，照样并入。
        Assert.Equal(1, result.Names[0]["乙"]);
        Assert.False(result.Names[0].ContainsKey("丙"));
        Assert.Equal(2, result.Names[0]["丁"]);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w == "常数表补充表(CSV/表.erd)中，第 1 个有效行的值不是有效的整数：x；跳过该格");
        Assert.Contains(result.Warnings, w => w == "常数表补充表(CSV/表.erd)中，第 2 个有效行写了注册名「丙」却没有注册数；跳过该行");
    }

    [Fact]
    public void Load_ExtensionNameAtOccupiedIndex_SkipsNameWithWarning()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1", "1,乙,2")
            .Csv("表.erd", "0,乙,1");

        var result = ConstArrayTable.Load("表", new[] { 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(0, result.Names[0]["甲"]);
        Assert.Equal(1, result.Names[0]["乙"]);
        Assert.Equal("常数表补充表(CSV/表.erd)中，索引 0 的名字\"乙\"已存在于索引 1；跳过", Assert.Single(result.Warnings));
    }

    [Fact]
    public void Load_ExtensionWithOppositeOrientation_FillsByDimensionNumber()
    {
        // 主表 @2 在 (1,2)；补充表 @1 在 (1,2)（行列颠倒）。按「维度编号」展平后照样补值、补名。
        var files = new MemoryFiles()
            .Csv("表.csv", ",@2,0,1", "@1,,甲,丙", "0,乙,1,1")
            .Csv("表.erd", ",@1,1", "@2,,丙", "0,,5");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 1, 1, 5, 0 }, Ints(result));
        Assert.Equal(0, result.Names[0]["乙"]);
        Assert.Equal(1, result.Names[1]["丙"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Load_ExtensionNameConflictsIn2D_AreReportedPerDimension()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", ",@2,0", "@1,,甲", "0,乙,1")
            .Csv("表.erd", ",@2,0", "@1,,丙", "0,乙,1");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(0, result.Names[0]["乙"]);
        Assert.Equal(0, result.Names[1]["甲"]);
        Assert.False(result.Names[1].ContainsKey("丙"));
        Assert.Equal("常数表补充表(CSV/表.erd)中，第 2 维索引 0 的名字与主表不同（主表=\"甲\", 补充=\"丙\"）；以主表为准",
            Assert.Single(result.Warnings));
    }

    [Fact]
    public void Load_FloatExtension_NaNEqualsNaNSoNoConflictWarning()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,,NaN")
            .Csv("表.erd", "0,,NaN");

        var result = ConstArrayTable.Load("表", new[] { 1 }, ConstArrayTable.KindFloat, files, out string error);

        Assert.Null(error);
        Assert.True(double.IsNaN(Floats(result)[0]));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Load_TwoColumnCsv_IsNotANameTableAndIsReportedAsUnusable()
    {
        // 名称表只从 ERB 目录读：CSV 目录下的两列同名文件不会被当作 [编号,名称]，
        // 按「不是可用的常数表」报告并记为已消费。
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1")
            .Csv("表.erd", "0,乙");

        var result = ConstArrayTable.Load("表", new[] { 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(1, Ints(result)[0]);
        Assert.Equal(0, result.Names[0]["甲"]);
        Assert.False(result.Names[0].ContainsKey("乙"));
        Assert.Equal("同名文件 CSV/表.erd 不是可用的常数表（第 1 个有效行不足 3 列（一维表需要 [注册数,注册名,值]）），已跳过",
            Assert.Single(result.Warnings));
        Assert.Equal(new[] { "CSV/表.csv", "CSV/表.erd" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load_UnrelatedFiles_AreNeverConsumed()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1")
            .Erb("其他.erd", "0,乙,1");

        var result = ConstArrayTable.Load("表", new[] { 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new[] { "CSV/表.csv" }, result.ConsumedPaths);
        Assert.Empty(result.Warnings);
    }

    // ---- 名称表与别名表 ----------------------------------------------------

    [Fact]
    public void Load_NameTableInErbDir_AddsNamesOnly()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1")
            .Erb("表.erd", "5,丁", ";注释", "6,", "x,戊", ",,")
            .Erb("表@1.erd", "7,\"己,庚\"");

        var result = ConstArrayTable.Load("表", new[] { 9 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }, Ints(result));
        Assert.Equal(0, result.Names[0]["甲"]);
        Assert.Equal(5, result.Names[0]["丁"]);
        Assert.False(result.Names[0].ContainsKey("戊"));
        Assert.Equal(7, result.Names[0]["己,庚"]);
        Assert.Equal("ERB/表.erd 有 1 个有效行不是「编号,名称」格式（首个在 第 3 个有效行（首列不是编号：x）），已跳过；名称里含逗号时要用双引号把名称包住",
            Assert.Single(result.Warnings));
        Assert.Equal(new[] { "CSV/表.csv", "ERB/表.erd", "ERB/表@1.erd" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load_NameTable_DimensionNamesGoToTheirOwnDimension()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", ",@2,1", "@1,,甲", "0,,1")
            .Erb("表@2.erd", "3,乙")
            .Erb("表@1.erd", "4,丙");

        var result = ConstArrayTable.Load("表", new[] { 5, 5 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(1, result.Names[1]["甲"]);
        Assert.Equal(4, result.Names[0]["丙"]);
        Assert.Equal(3, result.Names[1]["乙"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Load_LayoutMarkedNameFile_IsConsumedWithoutReadingRowsAsNames()
    {
        // 含 @1 / @2 布局标记的文件只可能是二维值表；一维声明下不会被当成名称表读走行名。
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1")
            .Erb("表.erd", ",@2,1", "@1,,乙", "0,丙,10");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 1, 0, 0 }, Ints(result));
        Assert.False(result.Names[0].ContainsKey("乙"));
        Assert.False(result.Names[0].ContainsKey("丙"));
        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { "CSV/表.csv", "ERB/表.erd" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load_AliasTable_AddsSynonymsWithoutOverridingNames()
    {
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1", "1,,2")
            .Csv("表.als", "0,alias", "0,甲", "1,second", "9,outofrange", "bad", "2,\"quoted,alias\"");

        var result = ConstArrayTable.Load("表", new[] { 3 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(new long[] { 1, 2, 0 }, Ints(result));
        Assert.Equal(0, result.Names[0]["甲"]);
        Assert.Equal(0, result.Names[0]["alias"]);
        Assert.Equal(1, result.Names[0]["second"]);
        Assert.Equal(2, result.Names[0]["quoted,alias"]);
        Assert.False(result.Names[0].ContainsKey("outofrange"));
        var warning = Assert.Single(result.Warnings);
        Assert.EndsWith(" 有 1 个有效行不是「编号,别名」格式（首个在 第 5 个有效行（不足「编号,别名」两列）），已跳过；别名里含逗号时要用双引号把别名包住", warning);
    }

    [Fact]
    public void Load_AliasOfUnmergedNameTable_IsNotApplied()
    {
        // 别名表只为「真正参与合并」的文件加载：没被当成名称表的表@1.erd，其 .als 不生效。
        var files = new MemoryFiles()
            .Csv("表.csv", "0,甲,1")
            .Erb("表.erd", "2,丁")
            .Erb("表.als", "0,keep")
            .Erb("表@1.erd", "not-a-name-table")
            .Erb("表@1.als", "0,ghost");

        var result = ConstArrayTable.Load("表", new[] { 4 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.Equal(0, result.Names[0]["keep"]);
        Assert.False(result.Names[0].ContainsKey("ghost"));
        Assert.Equal(2, result.Names[0]["丁"]);
        Assert.Equal(new[] { "CSV/表.csv", "ERB/表.erd" }, result.ConsumedPaths);
    }

    [Fact]
    public void Load_2DAliasFiles_FollowDimensionNameTablesOnly()
    {
        // 上游 18.0 行为：别名表按「已消费文件」的同名 .als 加载，二维只认名称@N；主表 表.csv 的
        // stem 没有 @N，所以它旁边的 表.als / 表@N.als 都不读，别名只跟着 ERB 下的 表@N.erd 走。
        var files = new MemoryFiles()
            .Csv("表.csv", ",@2,0", "@1,,甲", "0,乙,1")
            .Csv("表.als", "0,wrongShape")
            .Csv("表@1.als", "0,wrongPlace")
            .Erb("表@1.erd", "1,丙")
            .Erb("表@1.als", "1,rowAlias")
            .Erb("表@2.erd", "1,丁")
            .Erb("表@2.als", "1,colAlias", "3,outofrange");

        var result = ConstArrayTable.Load("表", new[] { 2, 2 }, ConstArrayTable.KindInt, files, out string error);

        Assert.Null(error);
        Assert.False(result.Names[0].ContainsKey("wrongShape"));
        Assert.False(result.Names[0].ContainsKey("wrongPlace"));
        Assert.Equal(0, result.Names[0]["乙"]);
        Assert.Equal(1, result.Names[0]["丙"]);
        Assert.Equal(1, result.Names[0]["rowAlias"]);
        Assert.Equal(0, result.Names[1]["甲"]);
        Assert.Equal(1, result.Names[1]["丁"]);
        Assert.Equal(1, result.Names[1]["colAlias"]);
        Assert.False(result.Names[1].ContainsKey("outofrange"));
        Assert.Empty(result.Warnings);
    }
}
