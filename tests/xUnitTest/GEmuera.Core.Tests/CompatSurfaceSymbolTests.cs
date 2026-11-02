using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 表面符号键/状态的契约直测（Task 4 双形态闭包的最小数据单元）：
/// 契约字符串往返、引擎同语义规范化（指令 Trim+Upper、函数只 Trim）、非法输入拒绝、
/// 以及生成清单 MethodProjectedInstructionNames 的稳定性哨兵。
/// </summary>
public class CompatSurfaceSymbolTests
{
    [Theory]
    [InlineData(CompatSymbolKind.Instruction, "ABS", "instruction:ABS")]
    [InlineData(CompatSymbolKind.Function, "ABS", "function:ABS")]
    [InlineData(CompatSymbolKind.Variant, "SETBGIMAGE", "variant:SETBGIMAGE")]
    public void ToContractString_UsesCanonicalPrefixAndName(CompatSymbolKind kind, string name, string expected)
    {
        Assert.Equal(expected, new CompatSymbolKey(kind, name).ToContractString());
    }

    [Theory]
    [InlineData("instruction:ABS", CompatSymbolKind.Instruction, "ABS")]
    [InlineData("function:ABS", CompatSymbolKind.Function, "ABS")]
    [InlineData("variant:SETBGIMAGE", CompatSymbolKind.Variant, "SETBGIMAGE")]
    public void TryParse_AcceptsCanonicalContractStrings(string text, CompatSymbolKind kind, string name)
    {
        Assert.True(CompatSymbolKey.TryParse(text, out CompatSymbolKey key));
        Assert.Equal(kind, key.Kind);
        Assert.Equal(name, key.Name);
        Assert.Equal(text, key.ToContractString());
    }

    [Theory]
    [InlineData("instruction:abs", CompatSymbolKind.Instruction, "ABS")]
    [InlineData("  INSTRUCTION:  abs  ", CompatSymbolKind.Instruction, "ABS")]
    [InlineData("Function: existvar", CompatSymbolKind.Function, "existvar")]
    public void TryParse_NormalizesPrefixAndNameLikeEngineVisibility(string text, CompatSymbolKind kind, string name)
    {
        Assert.True(CompatSymbolKey.TryParse(text, out CompatSymbolKey key));
        Assert.Equal(new CompatSymbolKey(kind, name), key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABS")]
    [InlineData(":ABS")]
    [InlineData("instruction:")]
    [InlineData("instruction:   ")]
    [InlineData("macro:ABS")]
    // 契约字符串只有 kind:name 两段：名字含 ':' 会让 ToContractString 歧义（M2）。
    [InlineData("instruction:A:B")]
    [InlineData("function:instruction:ABS")]
    [InlineData("variant:SETBGIMAGE:extra")]
    // 名字内空白/全角空白不可解析（Trim 只处理两端）。
    [InlineData("function:A B")]
    [InlineData("instruction:ABS\tX")]
    [InlineData("function:AB\u3000S")]
    public void TryParse_RejectsMalformedText(string? text)
    {
        Assert.False(CompatSymbolKey.TryParse(text, out _));
    }

    [Theory]
    [InlineData("A:B")]
    [InlineData("A B")]
    [InlineData("ABS\tX")]
    [InlineData("AB\u3000S")]
    public void Constructor_RejectsNamesThatTryParseCannotRoundTrip(string name)
    {
        // final review M11：构造器只 Trim 会接受 ':'/内部空白，ToContractString 于是产出
        // TryParse 拒绝的串（"instruction:A:B" 无法解回同一符号）。构造器与 TryParse 必须
        // 同一套校验——负向入口也 fail-closed。
        var exception = Record.Exception(() => new CompatSymbolKey(CompatSymbolKind.Instruction, name));
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void Constructor_OutputAlwaysParsesBackToSameKey()
    {
        foreach (CompatSymbolKind kind in new[]
        {
            CompatSymbolKind.Instruction, CompatSymbolKind.Function, CompatSymbolKind.Variant,
        })
        {
            var key = new CompatSymbolKey(kind, "  setbgimage  ");
            Assert.True(CompatSymbolKey.TryParse(key.ToContractString(), out CompatSymbolKey roundTripped));
            Assert.Equal(key, roundTripped);
        }
    }

    [Fact]
    public void InstructionAndFunctionKeys_WithSameName_RemainDistinct()
    {
        // 双形态闭包的核心：同一裸名字的两侧状态不能折叠成一条。
        var instruction = new CompatSymbolKey(CompatSymbolKind.Instruction, "ABS");
        var function = new CompatSymbolKey(CompatSymbolKind.Function, "ABS");
        Assert.NotEqual(instruction, function);
        Assert.NotEqual(instruction.ToContractString(), function.ToContractString());
    }

    [Fact]
    public void Constructor_NormalizesInstructionNameButPreservesFunctionSpelling()
    {
        // 与引擎 IsInstructionVisible / IsFunctionVisible 的规范化语义逐名对齐：
        // 指令名 Trim+Upper；函数名只 Trim（CJK 函数名无大小写语义，原样保留）。
        Assert.Equal("ABS", new CompatSymbolKey(CompatSymbolKind.Instruction, " abs ").Name);
        Assert.Equal("陥落状態", new CompatSymbolKey(CompatSymbolKind.Function, " 陥落状態 ").Name);
    }

    [Fact]
    public void CompatSymbolState_CarriesOwnerReturnTypeAndVariantPayload()
    {
        var function = new CompatSymbolState(
            new CompatSymbolKey(CompatSymbolKind.Function, "SQL_CONNECT"),
            CompatSymbolStateKind.Present,
            "test.pack",
            "Integer",
            null);
        Assert.Equal("function:SQL_CONNECT", function.Key.ToContractString());
        Assert.Equal(CompatSymbolStateKind.Present, function.Kind);
        Assert.Equal("test.pack", function.OwnerPackId);
        Assert.Equal("Integer", function.ReturnType);
        Assert.Null(function.BuiltinVariant);

        var selection = new CompatSymbolState(
            new CompatSymbolKey(CompatSymbolKind.Variant, "SETBGIMAGE"),
            CompatSymbolStateKind.VariantSelection,
            "test.pack",
            null,
            "builtin:snake");
        Assert.Equal("SETBGIMAGE", selection.Key.Name);
        Assert.Equal("builtin:snake", selection.BuiltinVariant);
    }

    [Fact]
    public void MethodProjectedInventory_IsSortedAndContainsRealProjections()
    {
        // 生成清单的稳定性哨兵：ABS/EXISTVAR 是 v24 基线里"函数可见 → METHOD 指令"的
        // 真实投影；ACOS/SQL_CONNECT 只在 snake 面可见，证明清单取六 profile 并集。
        string[] names = LegacyDialectInventories.MethodProjectedInstructionNames;
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Contains("ABS", names);
        Assert.Contains("EXISTVAR", names);
        Assert.Contains("ACOS", names);
        Assert.Contains("SQL_CONNECT", names);
    }

    [Fact]
    public void MethodProjectedInventory_NamesAreInstructionNormalizationStable()
    {
        // 清单名来自指令注册表键（Trim+Upper 不变形态）；规则层的镜像按同一规范化比较，
        // 这条哨兵保证未来注册表引入混合大小写时先红。
        foreach (string name in LegacyDialectInventories.MethodProjectedInstructionNames)
            Assert.Equal(name, name.Trim().ToUpperInvariant());
    }

    [Fact]
    public void MethodProjectedInventory_EveryNameAppearsInSomeInstructionInventory()
    {
        // METHOD 投影名必须真实存在于某个 profile 的指令清单——否则镜像会产生幽灵符号。
        var union = new HashSet<string>(
            LegacyDialectInventories.V24InstructionNames
                .Concat(LegacyDialectInventories.SnakeDeltaInstructionNames)
                .Concat(LegacyDialectInventories.EraFlDeltaInstructionNames)
                .Concat(LegacyDialectInventories.EraBlueDeltaInstructionNames)
                .Concat(LegacyDialectInventories.MegatenDeltaInstructionNames)
                .Concat(LegacyDialectInventories.V18InstructionNames),
            StringComparer.Ordinal);
        foreach (string name in LegacyDialectInventories.MethodProjectedInstructionNames)
            Assert.Contains(name, union);
    }
}
