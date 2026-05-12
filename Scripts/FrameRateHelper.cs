using Godot;
using System.Collections.Generic;
using System.Reflection;

public static class FrameRateHelper
{
    const string SettingsPath = "user://settings.cfg";
    const string Section = "Display";
    const string FrameRateIndexKey = "FrameRateIndex";

    public static readonly List<int> FrameRates = new List<int> { 24, 30, 48, 60, 90, 120 };

    public static void Apply()
    {
        Engine.MaxFps = CurrentFrameRate;
        ApplyConfigFps();
    }

    public static void ApplyConfigFps()
    {
        var prop = typeof(MinorShift.Emuera.Config).GetProperty(
            "FPS",
            BindingFlags.Public | BindingFlags.Static);
        prop?.SetValue(null, CurrentFrameRate);
    }

    public static int CurrentFrameRate
    {
        get
        {
            int index = frame_rate_index;
            if (index < 0 || index >= FrameRates.Count)
                return 60;
            return FrameRates[index];
        }
    }

    public static int frame_rate_index
    {
        get
        {
            if (_frameRateIndex < 0)
            {
                var cfg = new ConfigFile();
                cfg.Load(SettingsPath);
                _frameRateIndex = (int)cfg.GetValue(Section, FrameRateIndexKey, FrameRates.IndexOf(60));
            }
            return _frameRateIndex;
        }
        set
        {
            _frameRateIndex = Mathf.Clamp(value, 0, FrameRates.Count - 1);
            var cfg = new ConfigFile();
            cfg.Load(SettingsPath);
            cfg.SetValue(Section, FrameRateIndexKey, _frameRateIndex);
            cfg.Save(SettingsPath);
        }
    }

    static int _frameRateIndex = -1;
}
