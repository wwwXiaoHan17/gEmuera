using Godot;

/// <summary>
/// 安全区应用组件：把系统安全区（刘海/挖孔/圆角裁切）换算成四边 inset，
/// 再按目标控件的应用模式写回布局。启动器（FirstWindow）与游戏系统菜单
/// （EmueraContent）共用同一套换算与刷新机制，一个组件多处挂载。
///
/// 两种应用模式：
/// - ThemeMargins：把 "基值 + inset" 写入 MarginContainer 的 margin_* 主题常量；
/// - OffsetOverrides：把 inset 写入目标 Control 的四个 Offset（右/下边为
///   负偏移，适合锚定右上角的菜单）。
///
/// 数值与时机等价于此前各调用方的内联实现：inset = max(0, 安全区超出屏幕
/// 边缘的距离)，桌面端安全区等于全屏故 inset 恒为 0。纯结构复用，行为零漂移。
/// </summary>
public partial class SafeAreaApplicator : Node
{
    public enum ApplyMode
    {
        /// <summary>写入 MarginContainer 的 margin_left/top/right/bottom 主题常量。</summary>
        ThemeMargins,

        /// <summary>写入 Control 的 OffsetLeft/Top/Right/Bottom（右/下为负偏移）。</summary>
        OffsetOverrides,
    }

    /// <summary>
    /// 目标控件。ThemeMargins 模式需要 MarginContainer；OffsetOverrides
    /// 模式任意 Control 均可。可在场景编辑器挂载，也可在代码里创建后赋值。
    /// </summary>
    [Export]
    public Control Target;

    [Export]
    public ApplyMode Mode = ApplyMode.ThemeMargins;

    // 按边开关：只应用需要的边（例如菜单只贴右上角时关闭左/下，保持其余
    // offset 由场景自行管理）。
    [Export]
    public bool ApplyLeft = true;

    [Export]
    public bool ApplyTop = true;

    [Export]
    public bool ApplyRight = true;

    [Export]
    public bool ApplyBottom = true;

    // 各边基值：最终应用值 = 基值 + inset。
    [Export]
    public int BaseLeft;

    [Export]
    public int BaseTop;

    [Export]
    public int BaseRight;

    [Export]
    public int BaseBottom;

    /// <summary>
    /// 自动刷新：进入场景树时应用一次，并监听视口 SizeChanged（旋转/分屏/
    /// 系统栏变化）。由宿主在既有布局路径中显式驱动的场景应保持 false，
    /// 避免应用时机被重复触发改变。
    /// </summary>
    [Export]
    public bool AutoRefresh = true;

    /// <summary>每次成功应用后发出（纯增量信号，供场景联动；未连接者无影响）。</summary>
    [Signal]
    public delegate void SafeAreaAppliedEventHandler();

    public override void _Ready()
    {
        if (!AutoRefresh)
            return;
        var viewport = GetViewport();
        if (viewport != null)
            viewport.SizeChanged += OnViewportSizeChanged;
        Apply();
    }

    public override void _ExitTree()
    {
        if (!AutoRefresh)
            return;
        var viewport = GetViewport();
        if (viewport != null)
            viewport.SizeChanged -= OnViewportSizeChanged;
    }

    void OnViewportSizeChanged()
    {
        Apply();
    }

    /// <summary>
    /// 重新读取视口安全区并应用到目标控件。幂等；数值与旧内联实现逐位一致。
    /// </summary>
    public void Apply()
    {
        if (Target == null || !GodotObject.IsInstanceValid(Target))
            return;

        var viewport = GetViewport();
        if (viewport == null)
            return;

        var insets = GetInsets(viewport);

        switch (Mode)
        {
            case ApplyMode.ThemeMargins:
                if (ApplyLeft)
                    Target.AddThemeConstantOverride("margin_left", BaseLeft + Mathf.RoundToInt(insets.Left));
                if (ApplyTop)
                    Target.AddThemeConstantOverride("margin_top", BaseTop + Mathf.RoundToInt(insets.Top));
                if (ApplyRight)
                    Target.AddThemeConstantOverride("margin_right", BaseRight + Mathf.RoundToInt(insets.Right));
                if (ApplyBottom)
                    Target.AddThemeConstantOverride("margin_bottom", BaseBottom + Mathf.RoundToInt(insets.Bottom));
                break;

            case ApplyMode.OffsetOverrides:
                if (ApplyLeft)
                    Target.OffsetLeft = BaseLeft + insets.Left;
                if (ApplyTop)
                    Target.OffsetTop = BaseTop + insets.Top;
                if (ApplyRight)
                    Target.OffsetRight = -(BaseRight + insets.Right);
                if (ApplyBottom)
                    Target.OffsetBottom = -(BaseBottom + insets.Bottom);
                break;
        }

        EmitSignal(SignalName.SafeAreaApplied);
    }

    /// <summary>
    /// 计算四边安全区 inset（像素，≥0）：安全区与屏幕边缘的距离。
    /// 桌面端 GetSafeViewportRect 返回全屏，故恒为 0。供其他布局代码复用
    /// 同一套换算。
    /// </summary>
    public static SafeInsets GetInsets(Viewport viewport)
    {
        if (viewport == null)
            return default;

        Rect2 safeRect = EmueraContent.GetSafeViewportRect(viewport);
        Vector2 viewportSize = viewport.GetVisibleRect().Size;
        return new SafeInsets(
            Mathf.Max(0, safeRect.Position.X),
            Mathf.Max(0, safeRect.Position.Y),
            Mathf.Max(0, viewportSize.X - (safeRect.Position.X + safeRect.Size.X)),
            Mathf.Max(0, viewportSize.Y - (safeRect.Position.Y + safeRect.Size.Y)));
    }
}

/// <summary>四边安全区 inset 容器（left/top/right/bottom，单位像素）。</summary>
public readonly struct SafeInsets
{
    public readonly float Left;
    public readonly float Top;
    public readonly float Right;
    public readonly float Bottom;

    public SafeInsets(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
}
