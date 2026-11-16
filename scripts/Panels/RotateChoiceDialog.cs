using Godot;

/// <summary>
/// 手动旋转的生效范围选择框。
///
/// 背景：手动旋转必须关闭「跟随手机朝向」（否则传感器在下一帧就把方向转回去），
/// 但「关闭」有两种语义：仅本次运行（重启恢复跟随）或永久（写盘）。
/// 由用户显式选择，避免一次临时操作被记成永久偏好。
/// </summary>
public partial class RotateChoiceDialog : AcceptDialog
{
    /// <summary>选择结果：true = 永久固定（写盘）。</summary>
    const string SessionAction = "rotate_session";

    static System.Action pendingCallback;

    /// <param name="onApplied">选择并应用后回调（调用方用它同步开关显示）。</param>
    public static void Show(Node parent, System.Action onApplied = null)
    {
        // 判断依据是「用户持久化偏好」而非当前内存态：点过「仅本次」后内存是关闭，
        // 但配置仍是跟随——若按内存判断会直接走固定分支，把仅本次悄悄升级成永久。
        if (!AndroidWindowOrientationController.PersistedFollowDeviceOrientation)
        {
            // 用户偏好就是固定：(永久/本次) 无区别，直接转。
            AndroidWindowOrientationController.RotateScreen(persist: true);
            onApplied?.Invoke();
            return;
        }
        pendingCallback = onApplied;
        var dialog = new RotateChoiceDialog { Name = "RotateChoiceDialog" };
        parent.AddChild(dialog);
        dialog.PopupCentered();
    }

    public override void _Ready()
    {
        Title = MultiLanguage.Get("RotateChoice.Title", "旋转屏幕");
        DialogText = MultiLanguage.Get("RotateChoice.Text",
            "旋转后要固定多久？\n「仅本次」在重启游戏后恢复跟随手机。");
        OkButtonText = MultiLanguage.Get("RotateChoice.Permanent", "永久固定");
        AddButton(MultiLanguage.Get("RotateChoice.Session", "仅本次"), true, SessionAction);
        Confirmed += () => Apply(persist: true);
        CustomAction += action =>
        {
            if (action == SessionAction)
                Apply(persist: false);
        };
        // 用户点标题栏 X 取消：既不永久也不仅本次，直接释放（否则对话框节点泄漏）。
        CloseRequested += () =>
        {
            pendingCallback = null;
            QueueFree();
        };
    }

    void Apply(bool persist)
    {
        AndroidWindowOrientationController.RotateScreen(persist);
        pendingCallback?.Invoke();
        pendingCallback = null;
        // 一次性对话框：关闭后自行释放，避免堆积。
        QueueFree();
    }
}
