using GEmuera.Core.Ui;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 系统浮动小窗方向策略（systematic-debugging Phase 3）：只按「窗口形态边沿变化」
/// 产生方向请求，避免全屏启动时无谓跳变、避免同状态重复请求、退出小窗恢复可旋转。
/// </summary>
public class WindowOrientationPolicyTests
{
    [Fact]
    public void Initial_fullscreen_requests_nothing()
        => Assert.Equal(OrientationRequest.None, WindowOrientationPolicy.Decide(WindowLayoutKind.Unknown, WindowLayoutKind.Fullscreen));

    [Fact]
    public void Initial_multi_window_requests_sensor_landscape()
        => Assert.Equal(OrientationRequest.SensorLandscape, WindowOrientationPolicy.Decide(WindowLayoutKind.Unknown, WindowLayoutKind.MultiWindow));

    [Fact]
    public void Entering_multi_window_requests_sensor_landscape()
        => Assert.Equal(OrientationRequest.SensorLandscape, WindowOrientationPolicy.Decide(WindowLayoutKind.Fullscreen, WindowLayoutKind.MultiWindow));

    [Fact]
    public void Leaving_multi_window_restores_sensor()
        => Assert.Equal(OrientationRequest.Sensor, WindowOrientationPolicy.Decide(WindowLayoutKind.MultiWindow, WindowLayoutKind.Fullscreen));

    [Fact]
    public void Same_state_requests_nothing()
    {
        Assert.Equal(OrientationRequest.None, WindowOrientationPolicy.Decide(WindowLayoutKind.Fullscreen, WindowLayoutKind.Fullscreen));
        Assert.Equal(OrientationRequest.None, WindowOrientationPolicy.Decide(WindowLayoutKind.MultiWindow, WindowLayoutKind.MultiWindow));
        Assert.Equal(OrientationRequest.None, WindowOrientationPolicy.Decide(WindowLayoutKind.Unknown, WindowLayoutKind.Unknown));
    }


    [Theory]
    [InlineData(1080, 2400, 2400, 1080, OrientationChangeResult.Changed)]
    [InlineData(1080, 2400, 1080, 2400, OrientationChangeResult.Ignored)]
    [InlineData(2400, 1080, 2400, 1080, OrientationChangeResult.Already)]
    [InlineData(2400, 1080, 1080, 2400, OrientationChangeResult.Ignored)]
    public void ClassifyLandscapeRequest_uses_window_aspect_not_requested_orientation(
        int beforeW, int beforeH, int afterW, int afterH, OrientationChangeResult expected)
        => Assert.Equal(expected, WindowOrientationPolicy.ClassifyLandscapeRequest(beforeW, beforeH, afterW, afterH));
}
