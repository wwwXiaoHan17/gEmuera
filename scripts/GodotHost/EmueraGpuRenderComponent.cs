using Godot;
using MinorShift.Emuera.Content;
using System.Collections.Concurrent;
using System.Threading;
using gEmuera.GodotHost;

/// <summary>
/// Godot 宿主 GPU 渲染组件，负责处理后台线程提交的 ColorMatrix 离屏渲染请求。
/// Emuera 核心只通过 EmueraMain 的静态门面提交任务，避免核心层直接依赖 Godot 节点生命周期。
/// </summary>
public sealed partial class EmueraGpuRenderComponent : Node
{
    static readonly ConcurrentQueue<EmueraMain.GpuWorkItem> workQueue = new ConcurrentQueue<EmueraMain.GpuWorkItem>();
    static EmueraGpuRenderComponent currentInstance;
    static int workIdCounter = 0;

    SubViewport gpuViewport;
    TextureRect gpuTextureRect;
    ShaderMaterial gpuShaderMaterial;
    EmueraMain.GpuWorkItem pendingGpuItem;
    int gpuRenderFrameCount = 0;
    bool gpuWaitingForRender = false;

    public static bool GpuReady { get; private set; } = false;
    public static int QueuedWorkCount => workQueue.Count;

    /// <summary>
    /// Completes queued requests with a harmless fallback image before a
    /// canary session transition.  The component may outlive the legacy view,
    /// so its static queue needs the same boundary as EmueraMain's queue.
    /// </summary>
    internal static void ResetCanarySessionState()
    {
        while (workQueue.TryDequeue(out var item))
        {
            item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
            item.Completed.Set();
        }
        currentInstance?.ResetPendingRenderState();
        // M5：丢弃旧会话合成的 CPU ColorMatrix 结果缓存，避免新会话引用过期内容。
        GraphicsImage.ResetColorMatrixMemoize();
        GpuReady = false;
    }

    void ResetPendingRenderState()
    {
        if (pendingGpuItem != null)
        {
            pendingGpuItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
            pendingGpuItem.Completed.Set();
            pendingGpuItem = null;
        }
        gpuWaitingForRender = false;
        gpuRenderFrameCount = 0;
        if (gpuViewport != null)
            gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    public static EmueraMain.GpuWorkItem Submit(Godot.Image src, Godot.Rect2I region, float[][] colorMatrix)
    {
        var item = new EmueraMain.GpuWorkItem
        {
            Id = Interlocked.Increment(ref workIdCounter),
            SrcImage = src,
            SrcRegion = region,
            ColorMatrix = colorMatrix
        };
        workQueue.Enqueue(item);
        return item;
    }

    public void MarkFrameReady()
    {
        if (ShouldUseGpuRenderer())
            GpuReady = true;
    }

    public override void _Ready()
    {
        currentInstance = this;
    }

    public override void _Process(double delta)
    {
        MarkFrameReady();
        ProcessQueue();
    }

    public void ProcessQueue()
    {
        if (!ShouldUseGpuRenderer())
        {
            while (workQueue.TryDequeue(out var queuedItem))
                CompleteWithCpuFallback(queuedItem);
            return;
        }

        if (!gpuWaitingForRender && workQueue.IsEmpty)
            return;

        if (gpuViewport == null)
            SetupGpuRenderer();
        if (gpuViewport == null)
            return;

        if (gpuWaitingForRender)
        {
            gpuRenderFrameCount++;
            if (gpuRenderFrameCount >= 2)
                CompletePendingRender();
        }

        if (!gpuWaitingForRender && workQueue.TryDequeue(out var item))
            StartRender(item);
    }

    public override void _ExitTree()
    {
        // 场景重载或退出时必须唤醒后台等待者，否则 ERB 执行线程可能卡在旧场景的渲染任务上。
        if (pendingGpuItem != null)
        {
            CompleteWithCpuFallback(pendingGpuItem);
            pendingGpuItem = null;
        }

        while (workQueue.TryDequeue(out var queuedItem))
            CompleteWithCpuFallback(queuedItem);

        GpuReady = false;
        if (currentInstance == this)
            currentInstance = null;
    }

    void SetupGpuRenderer()
    {
        if (!ShouldUseGpuRenderer() || gpuViewport != null)
            return;

        gpuViewport = new SubViewport();
        gpuViewport.TransparentBg = true;
        gpuViewport.Size = new Vector2I(16, 16);
        gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        gpuViewport.Name = "GpuRenderViewport";

        gpuTextureRect = new TextureRect();
        gpuTextureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        gpuTextureRect.Name = "GpuTextureRect";

        gpuShaderMaterial = ColorMatrixGPU.CreateCompositMaterial();
        gpuTextureRect.Material = gpuShaderMaterial;

        gpuViewport.AddChild(gpuTextureRect);
        AddChild(gpuViewport);
    }

    void CompletePendingRender()
    {
        if (pendingGpuItem == null)
        {
            gpuWaitingForRender = false;
            if (gpuViewport != null)
                gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            return;
        }

        var vpTex = gpuViewport.GetTexture();
        if (vpTex != null)
        {
            var resultImg = vpTex.GetImage();
            if (resultImg != null && resultImg.GetWidth() > 0 && resultImg.GetHeight() > 0)
            {
                if (resultImg.GetFormat() != Godot.Image.Format.Rgba8)
                    resultImg.Convert(Godot.Image.Format.Rgba8);
                pendingGpuItem.ResultImage = resultImg;
            }
            else
            {
                pendingGpuItem.ResultImage = BuildCpuFallbackImage(pendingGpuItem);
            }
        }
        else
        {
            pendingGpuItem.ResultImage = BuildCpuFallbackImage(pendingGpuItem);
        }

        pendingGpuItem.Completed.Set();
        pendingGpuItem = null;
        gpuWaitingForRender = false;
        gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    void StartRender(EmueraMain.GpuWorkItem item)
    {
        var srcW = item.SrcRegion.Size.X;
        var srcH = item.SrcRegion.Size.Y;
        if (srcW <= 0 || srcH <= 0)
        {
            item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
            item.Completed.Set();
            return;
        }

        var subImg = item.SrcImage.GetRegion(item.SrcRegion);
        if (subImg == null)
        {
            CompleteWithCpuFallback(item);
            return;
        }

        var imgTex = ImageTexture.CreateFromImage(subImg);
        gpuTextureRect.Texture = imgTex;
        gpuTextureRect.Size = new Vector2(srcW, srcH);
        gpuTextureRect.Position = Vector2.Zero;
        gpuViewport.Size = item.SrcRegion.Size;

        ColorMatrixGPU.SetMatrixUniforms(gpuShaderMaterial, item.ColorMatrix);

        gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        gpuRenderFrameCount = 0;
        gpuWaitingForRender = true;
        pendingGpuItem = item;
    }

    static void CompleteWithCpuFallback(EmueraMain.GpuWorkItem item)
    {
        item.ResultImage = BuildCpuFallbackImage(item);
        item.Completed.Set();
    }

    static Godot.Image BuildCpuFallbackImage(EmueraMain.GpuWorkItem item)
    {
        if (item?.SrcImage == null || item.SrcRegion.Size.X <= 0 || item.SrcRegion.Size.Y <= 0)
            return Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);

        return GraphicsImage.ApplyColorMatrixGPU(item.SrcImage, item.SrcRegion, item.ColorMatrix);
    }

    static bool ShouldUseGpuRenderer()
    {
        // OpenGL Compatibility has no RenderingDevice pipeline cache. Avoid a
        // texture upload, shader draw, and GPU readback for an image that the
        // legacy worker immediately needs on the CPU.
        return !OS.HasFeature("mobile") && !RendererRuntimeIdentity.UsesCompatibilityRenderer;
    }
}
