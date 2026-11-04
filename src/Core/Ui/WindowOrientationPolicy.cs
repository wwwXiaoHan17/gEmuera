namespace GEmuera.Core.Ui;

/// <summary>当前窗口形态（Godot 侧检测结果，纯数据）。</summary>
public enum WindowLayoutKind
{
    Unknown = 0,
    Fullscreen = 1,
    MultiWindow = 2,
}

/// <summary>需要向系统请求的方向。</summary>
public enum OrientationRequest
{
    None = 0,
    SensorLandscape = 1,
    Sensor = 2,
}

/// <summary>
/// 系统浮动小窗方向策略：只在窗口形态发生边沿变化时产生请求。
/// - 全屏启动（Unknown→Fullscreen）不请求，避免冷启动可见跳变；
/// - 进入多窗口请求 SensorLandscape（小窗默认横屏）；
/// - 退出多窗口请求 Sensor（恢复用户可自由旋转）。
/// </summary>
public static class WindowOrientationPolicy
{
    public static OrientationRequest Decide(WindowLayoutKind previous, WindowLayoutKind current)
    {
        if (current == WindowLayoutKind.Unknown || previous == current)
            return OrientationRequest.None;
        if (previous == WindowLayoutKind.Unknown)
            return current == WindowLayoutKind.MultiWindow
                ? OrientationRequest.SensorLandscape
                : OrientationRequest.None;
        return current == WindowLayoutKind.MultiWindow
            ? OrientationRequest.SensorLandscape
            : OrientationRequest.Sensor;
    }
}
