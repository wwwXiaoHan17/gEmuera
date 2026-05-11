using Godot;
using System.Collections.Generic;

public static class ResolutionHelper
{
    public static readonly string kResolutionIndex = "ResolutionIndex";
    public static List<int> resolutions = new List<int>
    {
        2160,
        1080,
        900,
        720,
        540,
    };

    public static void Apply()
    {
        if(resolution_index < 0)
        {
            var current_height = DisplayServer.WindowGetSize().Y;
            if(current_height >= 1000)
                resolution_index = 1;
            else if(current_height >= 900)
                resolution_index = 2;
            else
                resolution_index = 3;
        }
        ApplyResolution();
    }

    static void ApplyResolution()
    {
        var height = resolutions[resolution_index];
        var width = Mathf.CeilToInt(height * aspect);
        var win = DisplayServer.WindowGetSize();
        if(win.X > win.Y)
            DisplayServer.WindowSetSize(new Vector2I(width, height));
        else
            DisplayServer.WindowSetSize(new Vector2I(height, width));
    }

    static float aspect
    {
        get
        {
            if(_aspect < 0.0f)
            {
                var win = DisplayServer.WindowGetSize();
                _aspect = win.X * 1.0f / win.Y;
                if(_aspect < 1)
                    _aspect = 1 / _aspect;
            }
            return _aspect;
        }
    }
    static float _aspect = -1.0f;

    public static int resolution_index
    {
        get
        {
            if(_resolution_index < 0)
            {
                var cfg = new ConfigFile();
                cfg.Load("user://settings.cfg");
                _resolution_index = (int)cfg.GetValue("Display", kResolutionIndex, -1);
            }
            return _resolution_index;
        }
        set
        {
            _resolution_index = value;
            if(_resolution_index < 0)
                return;
            var cfg = new ConfigFile();
            cfg.Load("user://settings.cfg");
            cfg.SetValue("Display", kResolutionIndex, _resolution_index);
            cfg.Save("user://settings.cfg");
        }
    }
    static int _resolution_index = -1;
}
