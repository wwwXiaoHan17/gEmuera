using Godot;

public static class UiScaleHelper
{
    const string SettingsPath = "user://settings.cfg";
    const string Section = "Display";
    const string UiScaleKey = "UiScale";

    // 系统级 UI 缩放（启动器 + 游戏内悬浮面板），走 root.ContentScaleFactor，
    // 与游戏画面内容的独立缩放（EmueraContent.SetContentScale）互不影响。
    public const float MinScale = 0.75f;
    public const float MaxScale = 2.0f;
    public const float DefaultScale = 1.0f;

    /// <summary>启动器头部与设置窗共用的预设档位（百分比）。</summary>
    public static readonly int[] PresetPercents = { 75, 90, 100, 125, 150, 175, 200 };

    public static void Apply()
    {
        SetScale(current, persist: false);
    }

    /// <summary>钳制并应用缩放；persist=false 时只改运行态不写文件（滑条拖动中逐帧调用）。</summary>
    public static void SetScale(float scale, bool persist)
    {
        current = Mathf.Clamp(scale, MinScale, MaxScale);
        var root = (Engine.GetMainLoop() as SceneTree)?.Root;
        if (root == null)
            return;
        root.ContentScaleFactor = current;
        if (!persist)
            return;
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue(Section, UiScaleKey, current);
        cfg.Save(SettingsPath);
    }

    public static float current
    {
        get
        {
            if (_current < 0)
            {
                var cfg = new ConfigFile();
                cfg.Load(SettingsPath);
                _current = Mathf.Clamp(
                    (float)(double)cfg.GetValue(Section, UiScaleKey, DefaultScale),
                    MinScale,
                    MaxScale);
            }
            return _current;
        }
        private set => _current = value;
    }
    static float _current = -1f;
}
