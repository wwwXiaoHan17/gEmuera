using System;

namespace GEmuera.Core.Ui;

/// <summary>
/// 内容区滚动对账纯数学（无 Godot 依赖，供移动端视口布局状态机与 xUnit 共用）。
/// 语义：缩放后的内容高度至少占满视口；滚动上限 = 内容高度 - 视口高度；
/// 旋转/尺寸变化后，原本贴底的保持贴底，否则按内容坐标比例保留阅读位置。
/// </summary>
public static class ContentScrollReconciliation
{
    /// <summary>缩放后的内容高度（向上取整），且不小于视口高度。</summary>
    public static int ComputeScaledExtentY(int layoutHeight, float scale, int viewportHeight)
    {
        double scaled = layoutHeight > 0 && scale > 0f ? layoutHeight * (double)scale : 0.0;
        double extent = Math.Max(scaled, Math.Max(0, viewportHeight));
        return (int)Math.Ceiling(extent);
    }

    /// <summary>可滚动上限（内容高度 - 视口高度，最小 0）。</summary>
    public static int ComputeScrollMax(int scaledExtentY, int viewportHeight)
        => Math.Max(0, scaledExtentY - Math.Max(0, viewportHeight));

    /// <summary>当前滚动位置是否已贴底（容差内视为贴底）。</summary>
    public static bool IsAtBottom(int scrollY, int scrollMax, int tolerancePx = 2)
        => scrollY >= Math.Max(0, scrollMax - Math.Max(0, tolerancePx));

    /// <summary>
    /// 尺寸/缩放变化后的目标滚动位置：原本贴底则继续贴底；否则按旧范围比例映射；
    /// 旧 scroll 超出旧上限（过渡态被钳前的值）时按旧上限处理。
    /// </summary>
    public static int ComputeTargetScrollY(int previousScrollY, int previousScrollMax, int nextScrollMax, bool wasAtBottom)
    {
        if (nextScrollMax <= 0)
            return 0;
        if (wasAtBottom)
            return nextScrollMax;
        if (previousScrollMax <= 0)
            return 0;
        int clampedPrevious = Math.Clamp(previousScrollY, 0, previousScrollMax);
        double ratio = (double)clampedPrevious / previousScrollMax;
        int target = (int)Math.Round(ratio * nextScrollMax, MidpointRounding.AwayFromZero);
        return Math.Clamp(target, 0, nextScrollMax);
    }
}
