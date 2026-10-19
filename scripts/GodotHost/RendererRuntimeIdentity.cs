using System;
using System.Text;
using Godot;

namespace gEmuera.GodotHost;

/// <summary>
/// Captures immutable renderer identity for low-frequency diagnostics. The
/// renderer is chosen before the engine starts, so this is intentionally not a
/// live graphics-quality controller or a per-session setting.
/// </summary>
public static class RendererRuntimeIdentity
{
    static readonly object Sync = new();
    static string cachedPerformanceData;
    static string cachedRuntimeMethod;

    /// <summary>
    /// The Compatibility renderer cannot use the RenderingDevice shader-pipeline
    /// path. Host components use this immutable process-wide fact to avoid
    /// adding an offscreen GPU readback path to an OpenGL frame.
    /// </summary>
    public static bool UsesCompatibilityRenderer
    {
        get
        {
            GetPerformanceData();
            return string.Equals(cachedRuntimeMethod, "gl_compatibility", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string GetPerformanceData()
    {
        if (cachedPerformanceData != null)
            return cachedPerformanceData;

        lock (Sync)
        {
            if (cachedPerformanceData != null)
                return cachedPerformanceData;

            var data = new StringBuilder(192);
            Append(data, "renderer_config", GetProjectSetting("rendering/renderer/rendering_method"));
            Append(data, "renderer_mobile_config", GetProjectSetting("rendering/renderer/rendering_method.mobile"));
            Append(data, "runtime_mobile", OS.HasFeature("mobile") ? "true" : "false");

            string runtimeMethod;
            try
            {
                // The requested project setting can differ from Godot's effective
                // fallback. Capture the one process-wide runtime choice, but never
                // query RenderingServer in a frame hot path.
                runtimeMethod = RenderingServer.GetCurrentRenderingMethod();
                Append(data, "renderer_runtime", runtimeMethod);
                Append(data, "renderer_driver", RenderingServer.GetCurrentRenderingDriverName());
            }
            catch (Exception)
            {
                runtimeMethod = GetProjectSetting("rendering/renderer/rendering_method");
                Append(data, "renderer_runtime", runtimeMethod);
                Append(data, "renderer_driver", "unavailable");
            }

            try
            {
                // These values identify the adapter and API currently selected by
                // Godot. They are stable for the process and available to exported
                // Android builds without polling RenderingServer every frame.
                Append(data, "video_adapter", RenderingServer.GetVideoAdapterName());
                Append(data, "video_vendor", RenderingServer.GetVideoAdapterVendor());
                Append(data, "video_api", RenderingServer.GetVideoAdapterApiVersion());
            }
            catch (Exception)
            {
                Append(data, "video_adapter", "unavailable");
            }

            cachedRuntimeMethod = runtimeMethod;
            cachedPerformanceData = data.ToString().TrimEnd();
            return cachedPerformanceData;
        }
    }

    static string GetProjectSetting(string path)
    {
        return ProjectSettings.HasSetting(path)
            ? ProjectSettings.GetSetting(path).AsString()
            : "unset";
    }

    static void Append(StringBuilder data, string key, string value)
    {
        if (data.Length > 0)
            data.Append(' ');
        data.Append(key).Append('=').Append(Normalize(value));
    }

    static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        return value.Trim()
            .Replace(' ', '_')
            .Replace('\t', '_')
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace('=', '_');
    }
}
