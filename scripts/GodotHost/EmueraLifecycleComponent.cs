using Godot;

/// <summary>
/// Godot 宿主生命周期组件，集中处理 Android/移动端后台与恢复逻辑。
/// 它不持有 Emuera 核心状态，只负责平台生命周期对帧率和音频暂停的影响。
/// </summary>
public sealed partial class EmueraLifecycleComponent : Node
{
    bool applicationPauseActive = false;
    int maxFpsBeforeApplicationPause = -1;

    public override void _EnterTree()
    {
        // AgentBridge 组合宿主：生命周期组件全程存活于 main 场景，是检查点队列消费方的挂载点。
        AddChild(new gEmuera.AgentBridge.AgentBridgeHost { Name = "AgentBridgeHost" });
    }

    public override void _Notification(int what)
    {
        HandleNotification(what);
    }

    public bool HandleNotification(int what)
    {
        if (what == NotificationApplicationPaused)
        {
            SetApplicationPaused(true);
            GenericUtils.NotifyLifecycleState("android_pause");
            return true;
        }

        if (what == NotificationApplicationResumed)
        {
            SetApplicationPaused(false);
            GenericUtils.NotifyLifecycleState("android_resume");
            return true;
        }

        return false;
    }

    void SetApplicationPaused(bool paused)
    {
        if (applicationPauseActive == paused)
            return;

        applicationPauseActive = paused;
        if (paused)
        {
            // 后台保留低频主循环，避免恢复时一次性处理大量堆积队列，同时降低 APK 后台耗电。
            maxFpsBeforeApplicationPause = Engine.MaxFps;
            Engine.MaxFps = 5;
            EmueraContent.instance?.SetApplicationPaused(true);
            return;
        }

        // 恢复用户配置的帧率策略；如果后台期间配置变更，FrameRateHelper 会重新应用。
        Engine.MaxFps = maxFpsBeforeApplicationPause > 0
            ? maxFpsBeforeApplicationPause
            : FrameRateHelper.CurrentFrameRate;
        FrameRateHelper.ApplyConfigFps();
        EmueraContent.instance?.SetApplicationPaused(false);
    }
}
