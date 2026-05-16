using Godot;
using MinorShift._Library;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using System.Collections.Concurrent;
using System.Threading;
using MinorShift.Emuera.Content;

public partial class EmueraMain : Node
{
    [Export] public bool debug = false;
    [Export] public bool use_coroutine = false;
    [Export] public bool enable_sprite_debug_viewer = true;

    // GPU work queue for cross-thread ColorMatrix rendering from background thread
    public class GpuWorkItem
    {
        public int Id;
        public Godot.Image SrcImage;
        public Godot.Rect2I SrcRegion;
        public float[][] ColorMatrix;
        public Godot.Image ResultImage;
        public ManualResetEventSlim Completed = new ManualResetEventSlim(false);
    }

    public class TextRenderItem
    {
        public int Id;
        public string Text;
        public string FontName;
        public int FontSize;
        public int FontStyle;
        public uEmuera.Drawing.Color Color;
        public int Width;
        public int Height;
        public Godot.Image ResultImage;
        public ManualResetEventSlim Completed = new ManualResetEventSlim(false);
    }

    static ConcurrentQueue<GpuWorkItem> gpuQueue = new ConcurrentQueue<GpuWorkItem>();
    static ConcurrentQueue<TextRenderItem> textRenderQueue = new ConcurrentQueue<TextRenderItem>();
    static int gpuWorkIdCounter = 0;
    static int textRenderIdCounter = 0;

    /// <summary>
    /// True once _Process has been called at least once, indicating the main loop is running
    /// and the SubViewport render pipeline is ready to process GPU work.
    /// </summary>
    public static bool GpuReady { get; private set; } = false;

    /// Submit ColorMatrix work from any thread. Returns the GpuWorkItem for direct wait.
    public static GpuWorkItem GpuSubmitColorMatrix(Godot.Image src, Godot.Rect2I region, float[][] cm)
    {
        var item = new GpuWorkItem
        {
            Id = Interlocked.Increment(ref gpuWorkIdCounter),
            SrcImage = src,
            SrcRegion = region,
            ColorMatrix = cm
        };
        gpuQueue.Enqueue(item);
        return item;
    }

    public static TextRenderItem SubmitTextRender(string text, string fontName, int fontSize, int fontStyle, uEmuera.Drawing.Color color, int width, int height)
    {
        if (GenericUtils.IsOnMainThread())
            return null;
        var item = new TextRenderItem
        {
            Id = Interlocked.Increment(ref textRenderIdCounter),
            Text = text ?? "",
            FontName = fontName,
            FontSize = System.Math.Max(1, fontSize),
            FontStyle = fontStyle,
            Color = color,
            Width = System.Math.Max(1, width),
            Height = System.Math.Max(1, height)
        };
        textRenderQueue.Enqueue(item);
        return item;
    }

    // SubViewport-based GPU rendering for ColorMatrix
    SubViewport gpuViewport;
    TextureRect gpuTextureRect;
    ShaderMaterial gpuShaderMaterial;
    GpuWorkItem pendingGpuItem;
    int gpuRenderFrameCount = 0;
    bool gpuWaitingForRender = false;
    SubViewport textViewport;
    Label textRenderLabel;
    FontFile textRenderFont;
    TextRenderItem pendingTextRenderItem;
    int textRenderFrameCount = 0;
    bool textWaitingForRender = false;
    bool startupStarted = false;
    Control startupOverlay;
    Label startupStatusLabel;

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

    static bool ShouldUseGpuRenderer()
    {
        return !OS.HasFeature("mobile");
    }

    void SetupTextRenderer()
    {
        if (textViewport != null)
            return;

        textRenderFont = ResourceLoader.Load<FontFile>("res://Fonts/MS Gothic.ttf");
        textViewport = new SubViewport();
        textViewport.TransparentBg = true;
        textViewport.Size = new Vector2I(16, 16);
        textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        textViewport.Name = "TextRenderViewport";

        textRenderLabel = new Label();
        textRenderLabel.Name = "TextRenderLabel";
        textRenderLabel.Position = Vector2.Zero;
        textRenderLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        textRenderLabel.VerticalAlignment = VerticalAlignment.Top;
        textRenderLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textRenderLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        textRenderLabel.ClipText = true;
        if (textRenderFont != null)
            textRenderLabel.AddThemeFontOverride("font", textRenderFont);

        textViewport.AddChild(textRenderLabel);
        AddChild(textViewport);
    }

    void ProcessTextRenderQueue()
    {
        if (textViewport == null)
            SetupTextRenderer();
        if (textViewport == null)
            return;

        if (textWaitingForRender)
        {
            textRenderFrameCount++;
            if (textRenderFrameCount >= 2)
            {
                var vpTex = textViewport.GetTexture();
                var resultImg = vpTex?.GetImage();
                if (resultImg != null && resultImg.GetWidth() > 0 && resultImg.GetHeight() > 0)
                {
                    if (resultImg.GetFormat() != Godot.Image.Format.Rgba8)
                        resultImg.Convert(Godot.Image.Format.Rgba8);
                    pendingTextRenderItem.ResultImage = resultImg;
                }
                else
                {
                    pendingTextRenderItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
                }
                pendingTextRenderItem.Completed.Set();
                pendingTextRenderItem = null;
                textWaitingForRender = false;
                textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            }
        }

        if (!textWaitingForRender && textRenderQueue.TryDequeue(out var item))
        {
            textViewport.Size = new Vector2I(item.Width, item.Height);
            textRenderLabel.Text = item.Text ?? "";
            textRenderLabel.Size = new Vector2(item.Width, item.Height);
            textRenderLabel.CustomMinimumSize = textRenderLabel.Size;
            textRenderLabel.AddThemeFontSizeOverride("font_size", item.FontSize);
            textRenderLabel.AddThemeColorOverride("font_color", new Color(item.Color.r, item.Color.g, item.Color.b, item.Color.a));
            textRenderLabel.Position = Vector2.Zero;

            textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            textRenderFrameCount = 0;
            textWaitingForRender = true;
            pendingTextRenderItem = item;
        }
    }

    /// Process pending GPU work. Called from _Process on the main thread.
    /// Frame-counted cycle: setup render → wait 2 frames → retrieve result.
    /// Falls back to CPU processing if GPU render produces no output.
    void ProcessGpuQueue()
    {
        if (!ShouldUseGpuRenderer())
        {
            while (gpuQueue.TryDequeue(out var queuedItem))
            {
                queuedItem.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
                    queuedItem.SrcImage, queuedItem.SrcRegion, queuedItem.ColorMatrix);
                queuedItem.Completed.Set();
            }
            return;
        }

        if (gpuViewport == null)
            SetupGpuRenderer();
        if (gpuViewport == null)
            return;

        // Phase 1: Wait for render to complete (2 frames after setup)
        if (gpuWaitingForRender)
        {
            gpuRenderFrameCount++;
            if (gpuRenderFrameCount >= 2)
            {
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
                        pendingGpuItem.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
                            pendingGpuItem.SrcImage, pendingGpuItem.SrcRegion, pendingGpuItem.ColorMatrix);
                    }
                }
                else
                {
                    pendingGpuItem.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
                        pendingGpuItem.SrcImage, pendingGpuItem.SrcRegion, pendingGpuItem.ColorMatrix);
                }
                pendingGpuItem.Completed.Set();
                pendingGpuItem = null;
                gpuWaitingForRender = false;
                gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            }
        }

        // Phase 2: Set up next render if idle
        if (!gpuWaitingForRender && gpuQueue.TryDequeue(out var item))
        {
            var srcW = item.SrcRegion.Size.X;
            var srcH = item.SrcRegion.Size.Y;
            if (srcW > 0 && srcH > 0)
            {
                var subImg = item.SrcImage.GetRegion(item.SrcRegion);
                if (subImg != null)
                {
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
                else
                {
                    // GetRegion failed, use CPU fallback
                    item.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
                        item.SrcImage, item.SrcRegion, item.ColorMatrix);
                    item.Completed.Set();
                }
            }
            else
            {
                item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
                item.Completed.Set();
            }
        }
    }

    public override void _Ready()
    {
        FrameRateHelper.Apply();
        ResolutionHelper.Apply();
        GenericUtils.SetMainThread();
        uEmuera.Logger.info = GenericUtils.Info;
        uEmuera.Logger.warn = GenericUtils.Warn;
        uEmuera.Logger.error = GenericUtils.Error;

        CreateStartupOverlay();
        CallDeferred(nameof(StartGameDeferred));
    }

    async void StartGameDeferred()
    {
        if (startupStarted)
            return;
        startupStarted = true;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("正在准备游戏目录...");

        // Setup path resolution
        string eraPath = FirstWindow.ResolveStartupGamePath();
        if (string.IsNullOrEmpty(eraPath) || !uEmuera.Utils.DirectoryExists(eraPath))
        {
            eraPath = ProjectSettings.GlobalizePath("res://eraAkumaMaid0.305-CH-正式版");
        }
        if (!string.IsNullOrEmpty(eraPath) && uEmuera.Utils.DirectoryExists(eraPath))
        {
            Sys.ExeDir = uEmuera.Utils.NormalizePath(eraPath + "/");
        }
        else
        {
            Sys.ExeDir = uEmuera.Utils.NormalizePath(OS.GetExecutablePath().GetBaseDir() + "/");
        }

        // Load SHIFT-JIS / UTF-8 config maps
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("Loading config...");
        LoadConfigMaps();

        // Reset global state for clean restart
        GlobalStatic.Reset();

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("Creating interface...");

        // Sprite debug viewer — press F3 to toggle
        if (enable_sprite_debug_viewer && OS.GetName() != "Android")
        {
            var debugViewer = new SpriteDebugViewer();
            debugViewer.Name = "SpriteDebugViewer";
            AddChild(debugViewer);
        }

        // Create content renderer
        var content = new EmueraContent();
        content.Name = "EmueraContent";
        AddChild(content);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("Starting game...");
        // Start the engine
        EmueraThread.instance.Start(debug, use_coroutine);
        working = true;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        HideStartupOverlay();
    }

    public override void _Process(double delta)
    {
        if (ShouldUseGpuRenderer())
            GpuReady = true;
        GenericUtils.FlushLogs();
        GenericUtils.FlushUI();
        ProcessTextRenderQueue();

        if (!working)
            return;

        if (clearRequested)
        {
            clearRequested = false;
            GenericUtils.ClearText();
        }

        if (restartRequested)
        {
            restartRequested = false;
            GetTree().ReloadCurrentScene();
            return;
        }

        if (GlobalStatic.MainWindow != null)
            GlobalStatic.MainWindow.Update();

        ProcessGpuQueue();

        SpriteManager.UpdateCleanup();
        SpriteManager.UpdateOtherThreads();
        MinorShift._Library.WinInput.UpdateKeyState();

        var console = GlobalStatic.Console;
        var content = EmueraContent.instance;
        if (console != null && content != null)
        {
            bool needsInput = console.IsWaitingInputSomething;
            if (!needsInput && content.IsInputVisible())
                content.ShowInput(false);
        }
    }

    public override void _ExitTree()
    {
        EmueraThread.instance.End();
        working = false;
        GlobalStatic.Reset();
        uEmuera.Utils.ResourceClear();
    }

    public void Run()
    {
        EmueraThread.instance.Start(debug, use_coroutine);
        working = true;
    }

    public void Clear()
    {
        if (working)
        {
            // Request clear on next process
            clearRequested = true;
        }
    }

    public void Restart()
    {
        if (working)
        {
            restartRequested = true;
        }
    }

    bool working = false;
    bool clearRequested = false;
    bool restartRequested = false;

    void CreateStartupOverlay()
    {
        startupOverlay = new Control();
        startupOverlay.Name = "StartupOverlay";
        startupOverlay.AnchorLeft = 0;
        startupOverlay.AnchorTop = 0;
        startupOverlay.AnchorRight = 1;
        startupOverlay.AnchorBottom = 1;
        startupOverlay.MouseFilter = Control.MouseFilterEnum.Stop;

        var bg = new ColorRect();
        bg.AnchorLeft = 0;
        bg.AnchorTop = 0;
        bg.AnchorRight = 1;
        bg.AnchorBottom = 1;
        bg.Color = Colors.Black;
        startupOverlay.AddChild(bg);

        startupStatusLabel = new Label();
        startupStatusLabel.AnchorLeft = 0;
        startupStatusLabel.AnchorTop = 0;
        startupStatusLabel.AnchorRight = 1;
        startupStatusLabel.AnchorBottom = 1;
        startupStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        startupStatusLabel.VerticalAlignment = VerticalAlignment.Center;
        startupStatusLabel.Text = "Loading game...";
        startupStatusLabel.AddThemeFontSizeOverride("font_size", 20);
        startupStatusLabel.AddThemeColorOverride("font_color", Colors.White);
        startupOverlay.AddChild(startupStatusLabel);

        AddChild(startupOverlay);
    }

    void UpdateStartupStatus(string status)
    {
        if (startupStatusLabel != null)
            startupStatusLabel.Text = status;
    }

    void HideStartupOverlay()
    {
        if (startupOverlay == null)
            return;

        startupOverlay.QueueFree();
        startupOverlay = null;
        startupStatusLabel = null;
    }

    void LoadConfigMaps()
    {
        char[] split = new char[] { '\r', '\n' };
        var shiftjisPath = "res://Text/emuera_config_shiftjis.bytes";
        var utf8Path = "res://Text/emuera_config_utf8.txt";
        var utf8CnPath = "res://Text/emuera_config_utf8_zhcn.txt";

        if (!Godot.FileAccess.FileExists(shiftjisPath) ||
            !Godot.FileAccess.FileExists(utf8Path) ||
            !Godot.FileAccess.FileExists(utf8CnPath))
            return;

        var shiftjisBytes = Godot.FileAccess.GetFileAsBytes(shiftjisPath);
        var utf8Text = Godot.FileAccess.GetFileAsString(utf8Path);
        var utf8CnText = Godot.FileAccess.GetFileAsString(utf8CnPath);

        var jis_md5_strs = GenericUtils.CalcMd5List(shiftjisBytes);

        var utf8_strs = utf8Text.Split(split, System.StringSplitOptions.RemoveEmptyEntries);
        var utf8_str_list = new System.Collections.Generic.List<string>();
        foreach (var str in utf8_strs)
        {
            if (string.IsNullOrWhiteSpace(str))
                continue;
            utf8_str_list.Add(str);
        }

        var utf8cn_strs = utf8CnText.Split(split, System.StringSplitOptions.RemoveEmptyEntries);
        var utf8cn_str_list = new System.Collections.Generic.List<string>();
        foreach (var str in utf8cn_strs)
        {
            if (string.IsNullOrWhiteSpace(str))
                continue;
            utf8cn_str_list.Add(str);
        }

        if (jis_md5_strs.Count != utf8cn_str_list.Count)
            return;

        var jis_map = new System.Collections.Generic.Dictionary<string, string>();
        for (int i = 0; i < jis_md5_strs.Count; ++i)
        {
            jis_map[jis_md5_strs[i]] = utf8_str_list[i];
        }
        var utf8cn_map = new System.Collections.Generic.Dictionary<string, string>();
        for (int i = 0; i < utf8cn_str_list.Count; ++i)
        {
            utf8cn_map[utf8cn_str_list[i]] = utf8_str_list[i];
        }
        uEmuera.Utils.SetSHIFTJIS_to_UTF8Dict(jis_map);
        uEmuera.Utils.SetUTF8ZHCN_to_UTF8Dict(utf8cn_map);
    }
}
