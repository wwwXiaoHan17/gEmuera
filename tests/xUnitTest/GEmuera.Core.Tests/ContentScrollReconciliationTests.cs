using GEmuera.Core.Ui;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 内容滚动对账纯数学（systematic-debugging Phase 3/4）：覆盖缩放后内容高度上限、
/// 真实滚动范围、贴底判定与旋转/尺寸变化后的滚动目标策略。
/// </summary>
public class ContentScrollReconciliationTests
{
    [Theory]
    [InlineData(940, 0.69f, 648, 649)]
    [InlineData(940, 1.0f, 648, 940)]
    [InlineData(100, 0.5f, 648, 648)]
    [InlineData(0, 0.5f, 0, 0)]
    public void ComputeScaledExtentY_clamps_to_viewport(int layout, float scale, int viewport, int expected)
        => Assert.Equal(expected, ContentScrollReconciliation.ComputeScaledExtentY(layout, scale, viewport));

    [Theory]
    [InlineData(649, 648, 1)]
    [InlineData(648, 648, 0)]
    [InlineData(0, 648, 0)]
    public void ComputeScrollMax_never_negative(int extent, int viewport, int expected)
        => Assert.Equal(expected, ContentScrollReconciliation.ComputeScrollMax(extent, viewport));

    [Theory]
    [InlineData(120, 120, true)]
    [InlineData(118, 120, true)]
    [InlineData(100, 120, false)]
    [InlineData(0, 0, true)]
    public void IsAtBottom_uses_tolerance(int scroll, int max, bool expected)
        => Assert.Equal(expected, ContentScrollReconciliation.IsAtBottom(scroll, max));

    [Fact]
    public void TargetScroll_keeps_ratio_when_not_at_bottom()
        => Assert.Equal(500, ContentScrollReconciliation.ComputeTargetScrollY(100, 120, 600, false));

    [Fact]
    public void TargetScroll_sticks_to_bottom_when_was_at_bottom()
        => Assert.Equal(600, ContentScrollReconciliation.ComputeTargetScrollY(100, 120, 600, true));

    [Fact]
    public void TargetScroll_clamps_stale_previous_to_old_max()
        => Assert.Equal(600, ContentScrollReconciliation.ComputeTargetScrollY(590, 120, 600, false));

    [Fact]
    public void TargetScroll_zero_when_next_max_zero()
        => Assert.Equal(0, ContentScrollReconciliation.ComputeTargetScrollY(0, 0, 0, false));
}
