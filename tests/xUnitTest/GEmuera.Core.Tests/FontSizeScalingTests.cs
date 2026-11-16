using GEmuera.Core.Ui;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 运行时字号缩放纯数学：覆盖字号下限钳制与行高等比换算
/// （镜像 Config.SetConfig 的 FontSize>=8 / LineHeight>=FontSize 规则）。
/// </summary>
public class FontSizeScalingTests
{
    [Theory]
    [InlineData(7, 8)]
    [InlineData(8, 8)]
    [InlineData(18, 18)]
    [InlineData(48, 48)]
    [InlineData(0, 8)]
    [InlineData(-5, 8)]
    public void ClampFontSize_enforces_minimum_8(int value, int expected)
        => Assert.Equal(expected, FontSizeScaling.ClampFontSize(value));

    [Theory]
    [InlineData(18, 19, 18, 19)]   // 同字号恒等
    [InlineData(18, 19, 24, 25)]   // 19*24/18 = 25.33 -> 25
    [InlineData(18, 19, 36, 38)]   // 19*36/18 = 38
    [InlineData(18, 19, 22, 23)]   // 19*22/18 = 23.22 -> 23
    [InlineData(18, 25, 9, 13)]    // 25*9/18 = 12.5 -> 13（AwayFromZero）
    [InlineData(24, 25, 48, 50)]   // 等比放大 2 倍
    [InlineData(18, 14, 30, 30)]   // 旧行高偏小：比例结果 23 < 新字号，钳到 30
    [InlineData(18, 16, 36, 36)]   // 比例结果 32 < 36，钳到 36
    [InlineData(0, 19, 24, 24)]    // 旧字号非法，直接返回新字号
    [InlineData(-1, 0, 18, 18)]    // 双非法输入不崩溃
    public void ComputeScaledLineHeight_scales_proportionally_and_clamps(
        int oldFontSize, int oldLineHeight, int newFontSize, int expected)
        => Assert.Equal(expected,
            FontSizeScaling.ComputeScaledLineHeight(oldFontSize, oldLineHeight, newFontSize));
}
