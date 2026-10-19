using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// Godot 宿主文字渲染组件，负责把后台线程的 GDrawString 请求转成主线程 SubViewport 渲染。
/// 组件只拥有离屏渲染节点和队列处理，调用方仍通过 EmueraMain 静态门面保持兼容。
/// N3：结果按 (text/fontName/fontSize/fontStyle/color/width/height) 做 LRU 缓存，
/// HUD/菜单等重复文本直接命中缓存、零渲染；渲染槽池让每 2 帧批量完成多项，
/// 队列吞吐从“单 item 串行”提升为多 item 并行，缩短 GDrawString 的 500ms 等待窗口。
/// </summary>
public sealed partial class EmueraTextRenderComponent : Node
{
	// 渲染槽数量：每槽一个 SubViewport，多个槽在同一帧内并行渲染不同 item。
	const int RenderSlotCount = 4;
	// 与旧实现一致的 2 帧渲染完成启发式（StartRender 后第 2 帧读取纹理）。
	const int RenderCompleteFrameCount = 2;
	// LRU 容量上限；文本图尺寸小（几十 KB 级），128 项上限避免 Android 内存膨胀。
	const int TextCacheCapacity = 128;

	static readonly ConcurrentQueue<EmueraMain.TextRenderItem> renderQueue = new ConcurrentQueue<EmueraMain.TextRenderItem>();
	static readonly object textCacheLock = new object();
	static readonly Dictionary<TextRenderCacheKey, Godot.Image> textRenderCache = new Dictionary<TextRenderCacheKey, Godot.Image>(TextCacheCapacity);
	static readonly LinkedList<TextRenderCacheKey> textCacheLru = new LinkedList<TextRenderCacheKey>();
	static EmueraTextRenderComponent currentInstance;
	static int renderIdCounter = 0;

	FontFile textRenderFont;
	readonly List<TextRenderSlot> renderSlots = new List<TextRenderSlot>();

	sealed class TextRenderSlot
	{
		public SubViewport Viewport;
		public Label Label;
		public EmueraMain.TextRenderItem Item;
		public int FrameCount;
		public bool WaitingForRender;
	}

	// 缓存 key：text/font/size/style/颜色位级/width/height。颜色用浮点位级编码，
	// 不同浮点颜色（即使字节级相同）不会共用同一渲染结果。
	readonly struct TextRenderCacheKey : IEquatable<TextRenderCacheKey>
	{
		public readonly string Text;
		public readonly string FontName;
		public readonly int FontSize;
		public readonly int FontStyle;
		public readonly uint ColorR;
		public readonly uint ColorG;
		public readonly uint ColorB;
		public readonly uint ColorA;
		public readonly int Width;
		public readonly int Height;

		public TextRenderCacheKey(EmueraMain.TextRenderItem item)
		{
			Text = item.Text;
			FontName = item.FontName;
			FontSize = item.FontSize;
			FontStyle = item.FontStyle;
			ColorR = BitConverter.ToUInt32(BitConverter.GetBytes(item.Color.r), 0);
			ColorG = BitConverter.ToUInt32(BitConverter.GetBytes(item.Color.g), 0);
			ColorB = BitConverter.ToUInt32(BitConverter.GetBytes(item.Color.b), 0);
			ColorA = BitConverter.ToUInt32(BitConverter.GetBytes(item.Color.a), 0);
			Width = item.Width;
			Height = item.Height;
		}

		public bool Equals(TextRenderCacheKey other)
		{
			return FontSize == other.FontSize && FontStyle == other.FontStyle
				&& ColorR == other.ColorR && ColorG == other.ColorG && ColorB == other.ColorB && ColorA == other.ColorA
				&& Width == other.Width && Height == other.Height
				&& string.Equals(Text, other.Text) && string.Equals(FontName, other.FontName);
		}

		public override bool Equals(object obj)
		{
			return obj is TextRenderCacheKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + (Text?.GetHashCode() ?? 0);
				hash = hash * 31 + (FontName?.GetHashCode() ?? 0);
				hash = hash * 31 + FontSize;
				hash = hash * 31 + FontStyle;
				hash = hash * 31 + (int)ColorR;
				hash = hash * 31 + (int)ColorG;
				hash = hash * 31 + (int)ColorB;
				hash = hash * 31 + (int)ColorA;
				hash = hash * 31 + Width;
				hash = hash * 31 + Height;
				return hash;
			}
		}
	}

	public static int QueuedWorkCount => renderQueue.Count;

	/// <summary>
	/// Completes and drops text render requests belonging to the previous
	/// session so a later view cannot observe stale text output.
	/// </summary>
	internal static void ResetCanarySessionState()
	{
		while (renderQueue.TryDequeue(out var item))
		{
			item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
			item.Completed.Set();
		}
		currentInstance?.ResetPendingRenderState();
	}

	void ResetPendingRenderState()
	{
		foreach (var slot in renderSlots)
		{
			if (slot.Item != null)
			{
				slot.Item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
				slot.Item.Completed.Set();
				slot.Item = null;
			}
			slot.WaitingForRender = false;
			slot.FrameCount = 0;
			if (slot.Viewport != null)
				slot.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		}
	}

	public static EmueraMain.TextRenderItem Submit(string text, string fontName, int fontSize, int fontStyle, uEmuera.Drawing.Color color, int width, int height)
	{
		if (GenericUtils.IsOnMainThread())
			return null;

		var item = new EmueraMain.TextRenderItem
		{
			Id = Interlocked.Increment(ref renderIdCounter),
			Text = text ?? "",
			FontName = fontName,
			FontSize = System.Math.Max(1, fontSize),
			FontStyle = fontStyle,
			Color = color,
			Width = System.Math.Max(1, width),
			Height = System.Math.Max(1, height)
		};

		// 相同 key 的渲染结果直接命中缓存：结果图只读共享（GDrawString 仅 BlendRect 读取），
		// 与“重新渲染”逐像素一致，且调用方无需等待主线程渲染。
		var cacheKey = new TextRenderCacheKey(item);
		lock (textCacheLock)
		{
			if (textRenderCache.TryGetValue(cacheKey, out var cached))
			{
				item.ResultImage = cached;
				item.Completed.Set();
				var lruNode = textCacheLru.Find(cacheKey);
				if (lruNode != null)
				{
					textCacheLru.Remove(lruNode);
					textCacheLru.AddLast(lruNode);
				}
				return item;
			}
		}
		renderQueue.Enqueue(item);
		return item;
	}

	public override void _Ready()
	{
		currentInstance = this;
	}

	public override void _Process(double delta)
	{
		// Do not create SubViewports or load the fallback font until legacy
		// GDRAWSTRING actually submits work. This keeps the Compatibility
		// startup path free of otherwise unused offscreen render targets.
		if (renderQueue.IsEmpty && !AnySlotWaiting())
			return;
		ProcessQueue();
	}

	public void ProcessQueue()
	{
		if (textRenderFont == null)
			SetupTextRenderer();
		if (textRenderFont == null)
			return;

		// 先完成已到期的槽，再在同一个 _Process 帧内批量领取新任务。
		for (int i = 0; i < renderSlots.Count; i++)
		{
			var slot = renderSlots[i];
			if (!slot.WaitingForRender)
				continue;
			slot.FrameCount++;
			if (slot.FrameCount >= RenderCompleteFrameCount)
				CompletePendingRender(slot);
		}

		foreach (var slot in renderSlots)
		{
			if (slot.WaitingForRender)
				continue;
			if (!renderQueue.TryDequeue(out var item))
				break;
			StartRender(slot, item);
		}
	}

	public override void _ExitTree()
	{
		// 无法在退出阶段可靠继续绘制文字，返回空图可以释放后台等待并保持调用方 fallback 语义。
		foreach (var slot in renderSlots)
		{
			if (slot.Item != null)
			{
				CompleteWithEmptyImage(slot.Item);
				slot.Item = null;
			}
		}

		while (renderQueue.TryDequeue(out var queuedItem))
			CompleteWithEmptyImage(queuedItem);
		if (currentInstance == this)
			currentInstance = null;
	}

	bool AnySlotWaiting()
	{
		for (int i = 0; i < renderSlots.Count; i++)
		{
			if (renderSlots[i].WaitingForRender)
				return true;
		}
		return false;
	}

	void SetupTextRenderer()
	{
		if (textRenderFont != null)
			return;

		textRenderFont = ResourceLoader.Load<FontFile>("res://assets/fonts/MS Gothic.ttf");
		if (textRenderFont == null)
			return;

		for (int i = 0; i < RenderSlotCount; i++)
		{
			var slot = new TextRenderSlot();
			slot.Viewport = new SubViewport();
			slot.Viewport.TransparentBg = true;
			slot.Viewport.Size = new Vector2I(16, 16);
			slot.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
			slot.Viewport.Name = "TextRenderViewport" + i;

			slot.Label = new Label();
			slot.Label.Name = "TextRenderLabel" + i;
			slot.Label.Position = Vector2.Zero;
			slot.Label.MouseFilter = Control.MouseFilterEnum.Ignore;
			slot.Label.VerticalAlignment = VerticalAlignment.Top;
			slot.Label.HorizontalAlignment = HorizontalAlignment.Left;
			slot.Label.AutowrapMode = TextServer.AutowrapMode.Off;
			slot.Label.ClipText = true;
			slot.Label.AddThemeFontOverride("font", textRenderFont);

			slot.Viewport.AddChild(slot.Label);
			AddChild(slot.Viewport);
			renderSlots.Add(slot);
		}
	}

	void CompletePendingRender(TextRenderSlot slot)
	{
		var item = slot.Item;
		slot.Item = null;
		slot.WaitingForRender = false;
		if (item == null)
		{
			if (slot.Viewport != null)
				slot.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
			return;
		}

		var vpTex = slot.Viewport.GetTexture();
		var resultImg = vpTex?.GetImage();
		if (slot.Viewport != null)
			slot.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		if (resultImg != null && resultImg.GetWidth() > 0 && resultImg.GetHeight() > 0)
		{
			if (resultImg.GetFormat() != Godot.Image.Format.Rgba8)
				resultImg.Convert(Godot.Image.Format.Rgba8);
			item.ResultImage = resultImg;
			InsertTextCache(new TextRenderCacheKey(item), resultImg);
		}
		else
		{
			item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
		}

		item.Completed.Set();
	}

	void StartRender(TextRenderSlot slot, EmueraMain.TextRenderItem item)
	{
		slot.Viewport.Size = new Vector2I(item.Width, item.Height);
		slot.Label.Text = item.Text ?? "";
		slot.Label.Size = new Vector2(item.Width, item.Height);
		slot.Label.CustomMinimumSize = slot.Label.Size;
		slot.Label.AddThemeFontSizeOverride("font_size", item.FontSize);
		slot.Label.AddThemeColorOverride("font_color", new Color(item.Color.r, item.Color.g, item.Color.b, item.Color.a));
		slot.Label.Position = Vector2.Zero;

		slot.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		slot.FrameCount = 0;
		slot.WaitingForRender = true;
		slot.Item = item;
	}

	static void CompleteWithEmptyImage(EmueraMain.TextRenderItem item)
	{
		item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
		item.Completed.Set();
	}

	static void InsertTextCache(TextRenderCacheKey key, Godot.Image result)
	{
		lock (textCacheLock)
		{
			if (textRenderCache.ContainsKey(key))
				return;
			textRenderCache.Add(key, result);
			textCacheLru.AddLast(key);
			if (textCacheLru.Count > TextCacheCapacity)
			{
				// 只移除引用，不 Dispose：调用方可能仍持有该图（已完成 item 的 ResultImage）。
				var oldest = textCacheLru.First;
				textCacheLru.RemoveFirst();
				textRenderCache.Remove(oldest.Value);
			}
		}
	}
}
