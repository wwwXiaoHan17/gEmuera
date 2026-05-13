using Godot;
using System.Collections.Generic;

public static class ResolutionHelper
{
    public static readonly string kResolutionIndex = "ResolutionIndex";
    public static readonly string kResolutionHeight = "ResolutionHeight";
    const string SettingsPath = "user://settings.cfg";
    const string Section = "Display";
    static readonly int[] StandardHeights = new int[] { 1080, 900, 720, 540 };
    public static List<int> resolutions = BuildResolutionList();

    public static void Apply()
    {
        RefreshResolutions();
        if(resolution_index < 0)
        {
            var current_height = DisplayServer.WindowGetSize().Y;
            resolution_index = FindBestResolutionIndex(current_height);
        }
        ApplyResolution();
    }

    public static void RefreshResolutions()
    {
        resolutions = BuildResolutionList();
    }

    static void ApplyResolution()
    {
        RefreshResolutions();
        if (resolution_index < 0 || resolution_index >= resolutions.Count)
            resolution_index = FindBestResolutionIndex(DisplayServer.WindowGetSize().Y);
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

    static List<int> BuildResolutionList()
    {
        int maxHeight = GetMaxScreenHeight();
        var list = new List<int>();
        AddResolution(list, maxHeight);
        foreach (var height in StandardHeights)
        {
            if (height < maxHeight)
                AddResolution(list, height);
        }
        if (list.Count == 0)
            list.Add(540);
        return list;
    }

    static void AddResolution(List<int> list, int height)
    {
        if (height <= 0 || list.Contains(height))
            return;
        list.Add(height);
    }

    static int GetMaxScreenHeight()
    {
        var screen = DisplayServer.ScreenGetSize();
        var window = DisplayServer.WindowGetSize();
        int max = System.Math.Max(System.Math.Max(screen.X, screen.Y), System.Math.Max(window.X, window.Y));
        return Mathf.Max(360, max);
    }

    static int FindBestResolutionIndex(int targetHeight)
    {
        RefreshResolutions();
        int normalized = Mathf.Max(targetHeight, 0);
        for (int i = 0; i < resolutions.Count; i++)
        {
            if (resolutions[i] <= normalized)
                return i;
        }
        return resolutions.Count - 1;
    }

    static int FindResolutionIndexByHeight(int savedHeight)
    {
        RefreshResolutions();
        if (savedHeight <= 0)
            return -1;
        int bestIndex = 0;
        int bestDiff = int.MaxValue;
        for (int i = 0; i < resolutions.Count; i++)
        {
            int diff = Mathf.Abs(resolutions[i] - savedHeight);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    public static int resolution_index
    {
        get
        {
            if(_resolution_index < 0)
            {
                var cfg = new ConfigFile();
                cfg.Load(SettingsPath);
                int savedHeight = (int)cfg.GetValue(Section, kResolutionHeight, -1);
                _resolution_index = FindResolutionIndexByHeight(savedHeight);
                if (_resolution_index < 0)
                    _resolution_index = (int)cfg.GetValue(Section, kResolutionIndex, -1);
                if (_resolution_index >= resolutions.Count)
                    _resolution_index = resolutions.Count - 1;
            }
            return _resolution_index;
        }
        set
        {
            RefreshResolutions();
            _resolution_index = Mathf.Clamp(value, 0, resolutions.Count - 1);
            if(_resolution_index < 0)
                return;
            var cfg = new ConfigFile();
            cfg.Load(SettingsPath);
            cfg.SetValue(Section, kResolutionIndex, _resolution_index);
            cfg.SetValue(Section, kResolutionHeight, resolutions[_resolution_index]);
            cfg.Save(SettingsPath);
        }
    }
    static int _resolution_index = -1;
}
