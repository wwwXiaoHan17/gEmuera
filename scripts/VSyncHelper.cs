using Godot;

/// <summary>
/// 垂直同步（V-Sync）设置的持久化与应用。
/// 与 FrameRateHelper 同一约定：期望值存 user://settings.cfg 的 [Display] 节，
/// 启动时由 FirstWindow 调一次 Apply()。
/// 注意：Godot 4 的 Compatibility 渲染器只支持 VSyncMode.Enabled，请求其他模式
/// 会在运行期回退；因此 Apply() 写完后再读回实际模式，把不一致记到
/// LastRequestUnsupported，交由设置界面显示诚实提示（不承诺生效）。
/// </summary>
public static class VSyncHelper
{
    const string SettingsPath = "user://settings.cfg";
    const string Section = "Display";
    const string VSyncEnabledKey = "VSyncEnabled";

    /// <summary>保存的期望值（默认开启）。写入后需调用 Apply() 才应用到当前窗口。</summary>
    public static bool Enabled
    {
        get
        {
            if (_enabled == null)
                _enabled = LoadEnabled();
            return _enabled.Value;
        }
        set
        {
            _enabled = value;
            var cfg = new ConfigFile();
            cfg.Load(SettingsPath);
            cfg.SetValue(Section, VSyncEnabledKey, value);
            cfg.Save(SettingsPath);
        }
    }

    static bool? _enabled;

    /// <summary>
    /// 最近一次 Apply() 读回的实际模式与请求值不一致（通常因为当前渲染方法不支持）。
    /// 这是“本次尝试未生效”的事实；保存的设置值不会被丢弃，下次启动仍会重试。
    /// </summary>
    public static bool LastRequestUnsupported { get; private set; }

    /// <summary>
    /// 把保存的期望值应用到当前窗口。绝不抛异常：API 不可用时保持现状，
    /// 只把 LastRequestUnsupported 置为 true 交给 UI 提示。
    /// </summary>
    public static void Apply()
    {
        try
        {
            var target = Enabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled;
            DisplayServer.WindowSetVsyncMode(target);
            LastRequestUnsupported = DisplayServer.WindowGetVsyncMode() != target;
        }
        catch (System.Exception)
        {
            // 平台/渲染后端不支持该 API（或读回失败）时不影响启动与游戏逻辑。
            LastRequestUnsupported = true;
        }
    }

    static bool LoadEnabled()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok)
            return true;
        Variant value = cfg.GetValue(Section, VSyncEnabledKey, true);
        return value.VariantType == Variant.Type.Bool ? value.AsBool() : true;
    }
}
