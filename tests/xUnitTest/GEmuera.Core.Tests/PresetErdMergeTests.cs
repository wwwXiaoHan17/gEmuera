using System;
using GEmuera.Core.Compatibility;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 预设变量同名 ERD 合并规则（上游 Emuera-SKIA Skiav13.0，snake 语义）：
/// 纯规则层测试——CSV 优先、ERD 只填空槽、跨槽重名跳过、ITEM 第三列价格来源追踪。
/// 引擎侧扫描/IO 见 Scripts/Emuera/GameData/ConstantData.PresetErd.cs（xUnit 覆盖不到）。
/// </summary>
public class PresetErdMergeTests
{
    // ---- 行解析 ----------------------------------------------------------

    [Fact]
    public void ParseLine_TwoColumns_ReadsIndexAndRawName()
    {
        var result = PresetErdMerge.TryParseLine("3,ポーション".AsSpan(), out var row);

        Assert.Equal(PresetErdMerge.LineParseResult.Ok, result);
        Assert.Equal(3, row.Index);
        Assert.Equal("ポーション", row.Name);
        Assert.False(row.HasPriceField);
    }

    [Fact]
    public void ParseLine_ThreeColumns_ReadsPrice()
    {
        var result = PresetErdMerge.TryParseLine("3,ポーション,120".AsSpan(), out var row);

        Assert.Equal(PresetErdMerge.LineParseResult.Ok, result);
        Assert.True(row.HasPriceField);
        Assert.True(row.PriceValid);
        Assert.Equal(120L, row.Price);
    }

    [Fact]
    public void ParseLine_PriceWithTrailingSpaces_IsAccepted()
    {
        var result = PresetErdMerge.TryParseLine("3,ポーション,120   ".AsSpan(), out var row);

        Assert.Equal(PresetErdMerge.LineParseResult.Ok, result);
        Assert.True(row.PriceValid);
        Assert.Equal(120L, row.Price);
    }

    [Fact]
    public void ParseLine_NonNumericPrice_IsKeptButInvalid()
    {
        var result = PresetErdMerge.TryParseLine("3,ポーション,abc".AsSpan(), out var row);

        Assert.Equal(PresetErdMerge.LineParseResult.Ok, result);
        Assert.True(row.HasPriceField);
        Assert.False(row.PriceValid);
    }

    [Fact]
    public void ParseLine_MissingComma_IsRejected()
    {
        var result = PresetErdMerge.TryParseLine("3".AsSpan(), out _);

        Assert.Equal(PresetErdMerge.LineParseResult.MissingComma, result);
    }

    [Fact]
    public void ParseLine_NonIntegerIndex_IsRejected()
    {
        var result = PresetErdMerge.TryParseLine("x,ポーション".AsSpan(), out _);

        Assert.Equal(PresetErdMerge.LineParseResult.IndexNotInteger, result);
    }

    [Fact]
    public void ParseLine_ExtraColumnsAreIgnored()
    {
        var result = PresetErdMerge.TryParseLine("3,ポーション,120,ignored".AsSpan(), out var row);

        Assert.Equal(PresetErdMerge.LineParseResult.Ok, result);
        Assert.Equal(120L, row.Price);
    }

    // ---- 合并规则 --------------------------------------------------------

    private static PresetErdMerge.Row MakeRow(int index, string name, bool hasPrice = false, bool priceValid = false, long price = 0)
        => new PresetErdMerge.Row(index, name, hasPrice, priceValid, price);

    private static PresetErdMerge.ApplyResult Apply(
        in PresetErdMerge.Row row,
        int index,
        string[] names,
        sbyte[] nameSources,
        long[] prices,
        sbyte[] priceSources,
        bool isItemSlot,
        Func<string, bool>? nameExistsInOtherSlot = null)
    {
        bool ok = PresetErdMerge.TryApply(
            in row, index, names, nameSources, prices, priceSources, isItemSlot,
            nameExistsInOtherSlot ?? (_ => false), out var result);
        Assert.True(ok);
        return result;
    }

    [Fact]
    public void Apply_EmptySlot_FillsNameAndMarksErdSource()
    {
        string[] names = ["", "", ""];
        sbyte[] sources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty];

        var result = Apply(MakeRow(1, "ポーション"), 1, names, sources, [], [], isItemSlot: false);

        Assert.Equal(PresetErdMerge.NameApplyResult.Filled, result.Name);
        Assert.Equal("ポーション", names[1]);
        Assert.Equal(PresetErdMerge.NameSourceErd, sources[1]);
    }

    [Fact]
    public void Apply_CsvNameWins_EvenWhenIdentical()
    {
        string[] names = ["", "ポーション", ""];
        sbyte[] sources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceCsv, PresetErdMerge.NameSourceEmpty];

        var result = Apply(MakeRow(1, "ポーション"), 1, names, sources, [], [], isItemSlot: false);

        Assert.Equal(PresetErdMerge.NameApplyResult.None, result.Name);
        Assert.Equal("ポーション", names[1]);
        Assert.Equal(PresetErdMerge.NameSourceCsv, sources[1]);
    }

    [Fact]
    public void Apply_CsvNameDiffers_WarnsAndKeepsCsvName()
    {
        string[] names = ["", "CSV名", ""];
        sbyte[] sources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceCsv, PresetErdMerge.NameSourceEmpty];

        var result = Apply(MakeRow(1, "ERD名"), 1, names, sources, [], [], isItemSlot: false);

        Assert.Equal(PresetErdMerge.NameApplyResult.ConflictWithCsv, result.Name);
        Assert.Equal("CSV名", names[1]);
    }

    [Fact]
    public void Apply_NameUsedByAnotherSlot_IsSkipped()
    {
        string[] names = ["", "", ""];
        sbyte[] sources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty];

        var result = Apply(MakeRow(1, "重複名"), 1, names, sources, [], [], isItemSlot: false,
            nameExistsInOtherSlot: name => name == "重複名");

        Assert.Equal(PresetErdMerge.NameApplyResult.DuplicateAcrossSlots, result.Name);
        Assert.Equal("", names[1]);
    }

    [Fact]
    public void Apply_EmptyName_FillsEmptySlotAndMarksErdSource()
    {
        // 上游 quirk：空名字行也会把槽标成"ERD 来源"，不视作跳过（该槽最终仍无有效名）。
        string[] names = [""];
        sbyte[] sources = [PresetErdMerge.NameSourceEmpty];

        var result = Apply(MakeRow(0, ""), 0, names, sources, [], [], isItemSlot: false);

        Assert.Equal(PresetErdMerge.NameApplyResult.Filled, result.Name);
        Assert.Equal("", names[0]);
        Assert.Equal(PresetErdMerge.NameSourceErd, sources[0]);
    }

    [Fact]
    public void Apply_DuplicateName_SkipsPriceProcessing()
    {
        // 与上游 continue 一致：跨槽重名时整行跳过，ITEM 第三列价格也不再写入。
        string[] names = ["", "", ""];
        sbyte[] nameSources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty];
        long[] prices = [0, 0, 0];
        sbyte[] priceSources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty];

        var result = Apply(
            MakeRow(1, "重複名", hasPrice: true, priceValid: true, price: 999), 1,
            names, nameSources, prices, priceSources, isItemSlot: true,
            nameExistsInOtherSlot: name => name == "重複名");

        Assert.Equal(PresetErdMerge.NameApplyResult.DuplicateAcrossSlots, result.Name);
        Assert.Equal(PresetErdMerge.PriceApplyResult.None, result.Price);
        Assert.Equal(0L, prices[1]);
        Assert.Equal(PresetErdMerge.NameSourceEmpty, priceSources[1]);
    }

    // ---- ITEM 第三列价格 ------------------------------------------------

    [Fact]
    public void Apply_ItemPrice_UnsetSlot_FillsPriceAndMarksErdSource()
    {
        string[] names = ["", ""];
        sbyte[] nameSources = [PresetErdMerge.NameSourceCsv, PresetErdMerge.NameSourceEmpty];
        long[] prices = [0, 0];
        sbyte[] priceSources = [PresetErdMerge.NameSourceCsv, PresetErdMerge.NameSourceEmpty];

        var result = Apply(
            MakeRow(1, "薬", hasPrice: true, priceValid: true, price: 50), 1,
            names, nameSources, prices, priceSources, isItemSlot: true);

        Assert.Equal(PresetErdMerge.PriceApplyResult.Filled, result.Price);
        Assert.Equal(50L, prices[1]);
        Assert.Equal(PresetErdMerge.NameSourceErd, priceSources[1]);
        Assert.Equal("薬", names[1]);
    }

    [Fact]
    public void Apply_ItemPrice_CsvExplicitZero_IsNotOverwritten()
    {
        // CSV 出现过价格列（含显式 0）即视为已设：ERD 不得覆盖。
        string[] names = ["", "既存"];
        sbyte[] nameSources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceCsv];
        long[] prices = [0, 0];
        sbyte[] priceSources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceCsv];

        var result = Apply(
            MakeRow(1, "既存", hasPrice: true, priceValid: true, price: 999), 1,
            names, nameSources, prices, priceSources, isItemSlot: true);

        Assert.Equal(PresetErdMerge.PriceApplyResult.ConflictWithCsv, result.Price);
        Assert.Equal(0L, prices[1]);
        Assert.Equal(PresetErdMerge.NameSourceCsv, priceSources[1]);
    }

    [Fact]
    public void Apply_ItemPrice_CsvSameValue_NoConflict()
    {
        string[] names = ["", "既存"];
        sbyte[] nameSources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceCsv];
        long[] prices = [0, 100];
        sbyte[] priceSources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceCsv];

        var result = Apply(
            MakeRow(1, "既存", hasPrice: true, priceValid: true, price: 100), 1,
            names, nameSources, prices, priceSources, isItemSlot: true);

        Assert.Equal(PresetErdMerge.PriceApplyResult.None, result.Price);
        Assert.Equal(100L, prices[1]);
    }

    [Fact]
    public void Apply_NonItemSlot_IgnoresPriceColumns()
    {
        string[] names = ["", ""];
        sbyte[] sources = [PresetErdMerge.NameSourceEmpty, PresetErdMerge.NameSourceEmpty];

        var result = Apply(
            MakeRow(1, "名前", hasPrice: true, priceValid: true, price: 999), 1,
            names, sources, [], [], isItemSlot: false);

        Assert.Equal(PresetErdMerge.PriceApplyResult.None, result.Price);
    }

    [Fact]
    public void Apply_InvalidPrice_ReportsInvalidWithoutWriting()
    {
        string[] names = [""];
        sbyte[] nameSources = [PresetErdMerge.NameSourceEmpty];
        long[] prices = [0];
        sbyte[] priceSources = [PresetErdMerge.NameSourceEmpty];

        var result = Apply(
            MakeRow(0, "薬", hasPrice: true, priceValid: false), 0,
            names, nameSources, prices, priceSources, isItemSlot: true);

        Assert.Equal(PresetErdMerge.PriceApplyResult.Invalid, result.Price);
        Assert.Equal(0L, prices[0]);
        Assert.Equal(PresetErdMerge.NameSourceEmpty, priceSources[0]);
    }

    [Fact]
    public void Apply_OutOfRangeIndex_ReturnsFalse()
    {
        string[] names = [""];
        sbyte[] sources = [PresetErdMerge.NameSourceEmpty];

        bool ok = PresetErdMerge.TryApply(MakeRow(5, "x"), 5, names, sources, [], [], false, _ => false, out _);

        Assert.False(ok);
    }
}
