using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using Godot;
using MinorShift.Emuera.Content;
using uEmuera.Drawing;

internal static class SpriteManager
{
	// APK/mobile is the primary runtime. Keep the texture cache bounded by both
	// estimated bytes and entry count so long CG-heavy sessions cannot grow until
	// Android terminates the process under memory pressure.
	const double kPastTime = 300.0;
	const ulong CleanupIntervalMs = 3000;
	const long MobileTextureBudgetBytes = 160L * 1024L * 1024L;
	const long DesktopTextureBudgetBytes = 512L * 1024L * 1024L;
	const int MobileTextureEntryBudget = 384;
	const int DesktopTextureEntryBudget = 1536;
	const int MobileAsyncTextureConcurrency = 1;
	const int DesktopAsyncTextureConcurrency = 2;
	const int MobileAsyncTextureCompletionBudget = 2;
	const int DesktopAsyncTextureCompletionBudget = 6;
	const int MobileCleanupDisposeBudget = 24;
	const int DesktopCleanupDisposeBudget = 96;
	const ulong PlaceholderRetryIntervalMs = 1000;

	// 主线程 GPU 上传预算：渲染路径不得同步创建 ImageTexture（否则每张新图一次卡顿），
	// 改为登记到 pending_gpu_uploads，由 ProcessPendingGpuUploads 每帧限量上传。
	const int MobileGpuUploadBudget = 2;
	const int DesktopGpuUploadBudget = 6;

	// 批量加载优化：队列积压超过此阈值时自动提升并发数
	const int BulkLoadQueueThreshold = 8;
	// 批量加载时的最大并发数（Android 限制为 2，避免内存压力）
const int MobileBulkLoadMaxConcurrency = 2;
const int DesktopBulkLoadMaxConcurrency = 4;
const int AsyncTextureWorkerQuiescenceTimeoutMs = 2000;

	internal class SpriteInfo : IDisposable
	{
		internal SpriteInfo(TextureInfo p, AtlasTexture s)
		{
			parent = p;
			sprite = s;
		}
		public void Dispose()
		{
			sprite?.Dispose();
			sprite = null;
		}
		internal AtlasTexture sprite;
		internal TextureInfo parent;
	}

	// TextureInfo is the single ownership record for one decoded image, its lazy
	// ImageTexture, and all AtlasTexture sub-regions derived from it. Several
	// dictionary keys may alias the same instance, so eviction must deduplicate by
	// object identity before disposing.
	internal class TextureInfo : IDisposable
	{
		internal TextureInfo(string b, Image img, bool isPlaceholder = false, string srcPath = null)
		{
			imagename = b;
			sourcePath = srcPath;
			IsPlaceholder = isPlaceholder;
			if (img != null)
			{
				_image = img;
				RememberSourceSize(img);
				cpuBytes = EstimateImageBytes(img);
				cachedWidth = img.GetWidth();
				cachedHeight = img.GetHeight();
			}
			if (isPlaceholder)
				placeholderRetryAfterMs = Time.GetTicksMsec() + PlaceholderRetryIntervalMs;
			Touch();
		}

		internal SpriteInfo GetSprite(ASprite src)
		{
			SpriteInfo sprite = GetOrCreateSpriteInfo(BuildSpriteCacheKey(src), GetSpriteRegion(src));
			if(sprite != null)
				refcount += 1;
			return sprite;
		}

		internal AtlasTexture GetAtlasTexture(string cacheKey, Rect2 region)
		{
			return GetOrCreateSpriteInfo(cacheKey, region)?.sprite;
		}

		SpriteInfo GetOrCreateSpriteInfo(string cacheKey, Rect2 region)
		{
			if (IsDisposed)
				return null;
			Touch();
			if(!sprites.TryGetValue(cacheKey, out var sprite))
			{
				// AtlasTexture allocation is cheap but not free on mobile. Cache by
				// exact source rectangle so repeated sprite frames reuse the same
				// Godot resource instead of creating transient wrapper objects.
				var atlas = new AtlasTexture();
				atlas.Atlas = texture;
				atlas.Region = region;
				sprite = new SpriteInfo(this, atlas);
				sprites[cacheKey] = sprite;
			}
			return sprite;
		}

		internal void Release()
		{
			if (refcount > 0)
				refcount -= 1;
			Touch();
		}

		public void Dispose()
		{
			if (IsDisposed)
				return;
			IsDisposed = true;
			if (sprites != null)
			{
				var iter = sprites.Values.GetEnumerator();
				while(iter.MoveNext())
					iter.Current.Dispose();
				sprites.Clear();
				sprites = null;
			}

			_texture?.Dispose();
			_texture = null;
			// 直接释放 _image 字段，避免走 image 属性（IsDisposed 已置位，不会再重解码）。
			var img = _image;
			_image = null;
			img?.Dispose();
			cpuBytes = 0;
		}

		internal void Touch()
		{
			pasttime = Time.GetTicksMsec() / 1000.0 + kPastTime;
		}

		static Rect2 GetSpriteRegion(ASprite src)
		{
			if (src is ASpriteSingle single)
			{
				return new Rect2(
					single.SrcRectangle.X, single.SrcRectangle.Y,
					single.SrcRectangle.Width, single.SrcRectangle.Height);
			}
			return new Rect2(
				src.Rectangle.X, src.Rectangle.Y,
				src.Rectangle.Width, src.Rectangle.Height);
		}

		static string BuildSpriteCacheKey(ASprite src)
		{
			var region = GetSpriteRegion(src);
			return $"{src.Name}:{region.Position.X},{region.Position.Y},{region.Size.X},{region.Size.Y}";
		}

		static long EstimateImageBytes(Image img)
		{
			if (img == null)
				return 0;
			return (long)System.Math.Max(1, img.GetWidth()) * System.Math.Max(1, img.GetHeight()) * 4L;
		}

		// refcount tracks legacy SpriteInfo checkout/release calls. pinCount is the
		// explicit presentation-layer ownership used by visible Godot Controls and
		// CBG nodes. Cleanup may only evict when both counters are clear.
		internal string imagename = null;
		internal int refcount = 0;
		internal int pinCount = 0;
		internal double pasttime = 0;
		internal bool IsDisposed { get; private set; }
		internal bool IsPlaceholder { get; private set; }

		// H5：预算双份核算。cpuBytes 是当前在内存中的 CPU Image 估算字节（Android 上传后
		// 释放则为 0），gpuBytes 是已上传 GPU 纹理的估算字节。estimatedBytes 取两者之和，
		// 反映 TextureInfo 实际占用的主机内存 + GPU 显存，供 UpdateCleanup 字节预算淘汰。
		long cpuBytes;
		long gpuBytes;
		internal long estimatedBytes { get { return cpuBytes + gpuBytes; } }

		// Android 上传到 GPU 后释放 CPU image 副本以省内存。GetPixel/SetPixel/Save/
		// RecreateTexture 访问 image 时会按 sourcePath 重新解码，语义与释放前一致。
		Image _image;
		readonly object imageReloadLock = new object();
		string sourcePath;
		bool cpuImageReleased;
		int cachedWidth;
		int cachedHeight;

		// 源文件解码后的原始像素尺寸，只在首次拿到整图时记录一次，之后永不改写。
		// Android 的 GPU 尺寸上限会把「上传用的副本」缩小（见 EnsureImageFitsGpu），
		// 但 CPU 侧的 image 始终是原始尺寸；CSV 里的裁剪坐标也始终以原始尺寸为基准。
		// 两者一旦混用（把原始坐标套到缩小后的位图上）就会越界采样，因此这里保留
		// 原始尺寸供 ScaleSourceRectToImageSpace 做坐标空间换算。
		int sourceWidth;
		int sourceHeight;
		internal bool HasSourceSize { get { return sourceWidth > 0 && sourceHeight > 0; } }
		internal int SourceWidth { get { return sourceWidth; } }
		internal int SourceHeight { get { return sourceHeight; } }

		/// <summary>
		/// GPU 上传副本相对原始图的缩放比。返回 false 表示当前 CPU 位图就是原始尺寸
		/// （桌面端恒为此情形，Android 上图片未超过上限时亦然）。
		/// </summary>
		internal bool TryGetImageScale(out float scaleX, out float scaleY)
		{
			scaleX = 1f;
			scaleY = 1f;
			if (!HasSourceSize)
				return false;
			Image current = Volatile.Read(ref _image);
			int w = current != null ? current.GetWidth() : cachedWidth;
			int h = current != null ? current.GetHeight() : cachedHeight;
			if (w <= 0 || h <= 0 || (w == sourceWidth && h == sourceHeight))
				return false;
			scaleX = (float)w / sourceWidth;
			scaleY = (float)h / sourceHeight;
			return true;
		}

		// 首次得到整图时记录原始尺寸。只有 sourceWidth/Height 尚未记录时才写入，
		// 后续 RecreateTexture（SetPixel 后重建）不会覆盖基准。
		void RememberSourceSize(Image img)
		{
			if (img == null || sourceWidth > 0 || sourceHeight > 0)
				return;
			sourceWidth = img.GetWidth();
			sourceHeight = img.GetHeight();
		}

		/// <summary>
		/// 把 CSV 的裁剪矩形（源文件原始图像坐标）换算到当前 CPU 位图的坐标空间。
		/// 位图即原始尺寸时原样返回（scale 保持 1）；已缩小时按比例缩放，再夹取到
		/// 位图边界内（原始矩形合法，换算后最多因取整落到界外一两像素）。
		/// 结果为保证与位图相交；完全不相交时返回退化矩形。
		/// 返回是否真正做了换算；scaleX/scaleY 恒为实际使用的缩放比，调用方可用它判断
		/// 结果是否需要放大回原尺寸以还原物理大小。
		/// </summary>
		internal bool ScaleSourceRectToImageSpace(ref Rectangle ex, out float scaleX, out float scaleY)
		{
			scaleX = 1f;
			scaleY = 1f;
			if (ex.Width <= 0 || ex.Height <= 0)
				return false;
			if (!TryGetImageScale(out scaleX, out scaleY))
			{
				scaleX = 1f;
				scaleY = 1f;
				return false;
			}

			Image current = Volatile.Read(ref _image);
			int w = current != null ? current.GetWidth() : cachedWidth;
			int h = current != null ? current.GetHeight() : cachedHeight;
			int sx = (int)System.Math.Floor(ex.X * (double)scaleX);
			int sy = (int)System.Math.Floor(ex.Y * (double)scaleY);
			int sw = (int)System.Math.Round(ex.Width * (double)scaleX);
			int sh = (int)System.Math.Round(ex.Height * (double)scaleY);
			if (sw < 1) sw = 1;
			if (sh < 1) sh = 1;

			// 夹取而不是直接放弃：原始矩形合法时它本就落在原始图内，换算后最多因
			// 取整/缩放落到边界外一两个像素，夹取即可保证仍取到同一块内容。
			if (sx < 0) { sw += sx; sx = 0; }
			if (sy < 0) { sh += sy; sy = 0; }
			if (sx + sw > w) sw = w - sx;
			if (sy + sh > h) sh = h - sy;
			if (sw <= 0 || sh <= 0)
			{
				ex = new Rectangle(0, 0, 0, 0);
				return true;
			}

			ex = new Rectangle(sx, sy, sw, sh);
			return true;
		}

		internal int width { get { Image current = Volatile.Read(ref _image); return current != null ? current.GetWidth() : cachedWidth; } }
		internal int height { get { Image current = Volatile.Read(ref _image); return current != null ? current.GetHeight() : cachedHeight; } }

		internal Image image
		{
			get
			{
				Image current = Volatile.Read(ref _image);
				if (current != null || IsDisposed || !cpuImageReleased || string.IsNullOrEmpty(sourcePath))
					return current;
				return ReloadCpuImage();
			}
		}

		// 需要像素时（GetPixel/SetPixel/Save/RecreateTexture 或 GDraw 合成）按源文件重解码。
		// 同文件同解码器，输出像素与释放前一致；Android 大图恢复首次上传时的缩放尺寸。
		Image ReloadCpuImage()
		{
			lock (imageReloadLock)
			{
				Image current = Volatile.Read(ref _image);
				if (current != null || IsDisposed)
					return current;
				try
				{
					if (!uEmuera.Utils.FileExists(sourcePath))
						return null;
					Image img = LoadImageOrPlaceholder(sourcePath, imagename, out _);
					if (img == null || img.GetWidth() <= 0 || img.GetHeight() <= 0)
					{
						img?.Dispose();
						return null;
					}
					// 与首次解码保持同一尺寸：CPU 侧 image 恒为源文件原始尺寸
					// （GPU 上传副本才受 EnsureImageFitsGpu 的尺寸上限约束），
					// 因此这里不需要再按 cachedWidth/cachedHeight 反向缩放。
					cpuBytes = EstimateImageBytes(img);
					cachedWidth = img.GetWidth();
					cachedHeight = img.GetHeight();
					Volatile.Write(ref _image, img);
					cpuImageReleased = false;
					return img;
				}
				catch
				{
					return null;
				}
			}
		}

		void ReleaseCpuImage()
		{
			if (IsDisposed)
				return;
			Image img = Volatile.Read(ref _image);
			if (img == null)
				return;
			// 先置空再释放：并发读 image 的线程拿到 null 走缓存尺寸，不会碰到已释放对象。
			Volatile.Write(ref _image, null);
			img.Dispose();
			cpuBytes = 0;
			cpuImageReleased = true;
		}

		bool ShouldReleaseCpuImageAfterUpload()
		{
			if (cpuImageReleased || IsPlaceholder)
				return false;
			try
			{
				if (OS.GetName() != "Android")
					return false;
			}
			catch
			{
				return false;
			}
			// 近期被脚本合成访问过的图保持 CPU 常驻：合成需要 CPU 像素，释放→再访问
			// 会按源文件重新解码（Android IO 税），图形密集 ERB 在此反复付税。
			if (IsRecentCompositionSource())
				return false;
			// 过小的图释放后重解码开销反而更高，保留 CPU 副本避免 GetPixel 频繁重解码。
			return (long)cachedWidth * cachedHeight >= 128L * 128L;
		}

		ulong placeholderRetryAfterMs = 0;
		// 最近一次被脚本合成路径（GDrawG/GDRAWSPRITETOG 等）访问的时间戳。
		// Android 上传后释放 CPU 副本的策略据此豁免近期合成源：G 系合成需要 CPU 像素，
		// 释放后再访问会触发按源文件重新解码的 IO 税（图形密集 ERB 反复付税）。
		ulong compositionAccessMs = 0;

		internal void MarkCompositionSource()
		{
			compositionAccessMs = (ulong)System.Environment.TickCount64;
		}

		bool IsRecentCompositionSource()
		{
			if (compositionAccessMs == 0)
				return false;
			// 120 秒滚动窗：图形密集游戏的合成源保持 CPU 常驻，窗外的图恢复原释放策略。
			return (ulong)System.Environment.TickCount64 - compositionAccessMs < 120_000UL;
		}

		private ImageTexture _texture = null;

		// 非触发检查：渲染路径用它判断 GPU 纹理是否已就绪，绝不触发同步 decode/resize/上传。
		internal bool IsGpuTextureReady => _texture != null && !IsDisposed;

		internal ImageTexture texture
		{
			get
			{
				if (_texture == null)
					CreateGpuTextureIfNeeded();
				return _texture;
			}
		}

		// 首次 GPU 上传（decode 已完成的前提下：按平台上限缩放副本 + ImageTexture.CreateFromImage）。
		// 渲染路径不要直接调用它——请用 SpriteManager.EnsureGpuTextureDeferred 走每帧限量队列。
		internal bool CreateGpuTextureIfNeeded()
		{
			if (_texture != null)
				return true;
			if (IsDisposed)
				return false;
			Image src = image;
			if (src == null)
				return false;
			Image upload = null;
			try
			{
				RememberSourceSize(src);
				upload = CreateGpuUploadImage(src);
				_texture = ImageTexture.CreateFromImage(upload);
				gpuBytes = EstimateImageBytes(upload);
				cachedWidth = upload.GetWidth();
				cachedHeight = upload.GetHeight();
				if (ShouldReleaseCpuImageAfterUpload())
					ReleaseCpuImage();
				return true;
			}
			catch (Exception ex)
			{
				GenericUtils.Warn(EmueraLogCategory.Sprite, () => $"[SpriteManager] Failed to create ImageTexture for {imagename}: {ex.Message}");
				if (GenericUtils.IsImageDebugEnabled("texture"))
					GenericUtils.ImageTrace("IMAGE.TEXTURE.CREATE_FAIL", () => "texture create failed",
						() => $"name={imagename} failure_kind=texture_create_fail error={ex.GetType().Name}");
				return false;
			}
			finally
			{
				// 只在确实生成了缩小副本时释放；源图本身由 _image 持有。
				if (upload != null && !ReferenceEquals(upload, src))
					upload.Dispose();
			}
		}
		internal void RecreateTexture()
		{
			_texture?.Dispose();
			Image upload = null;
			try
			{
				Image src = image;
				if (src != null)
				{
					RememberSourceSize(src);
					upload = CreateGpuUploadImage(src);
					_texture = ImageTexture.CreateFromImage(upload);
					gpuBytes = EstimateImageBytes(upload);
					cachedWidth = upload.GetWidth();
					cachedHeight = upload.GetHeight();
				}
				else
					_texture = null;
			}
			catch (Exception ex)
			{
				_texture = null;
				GenericUtils.Warn(EmueraLogCategory.Sprite, () => $"[SpriteManager] Failed to recreate ImageTexture for {imagename}: {ex.Message}");
				if (GenericUtils.IsImageDebugEnabled("texture"))
					GenericUtils.ImageTrace("IMAGE.TEXTURE.CREATE_FAIL", () => "texture recreate failed",
						() => $"name={imagename} failure_kind=texture_create_fail error={ex.GetType().Name}");
			}
			finally
			{
				if (upload != null && !ReferenceEquals(upload, _image))
					upload.Dispose();
			}
		}

		/// <summary>
		/// 返回一张「可安全上传」的图：超出当前平台 GPU 尺寸上限时返回缩小后的副本，
		/// 否则原样返回入参。
		/// 关键约束：**绝不原地改写入参**。入参是 TextureInfo 的共享 CPU 位图，而 CSV 的
		/// 裁剪坐标一律以源文件原始尺寸为基准；一旦原地缩小，Android 上就会出现
		/// 「原始坐标 × 缩小位图」的越界采样，表现为立绘空白或被裁掉一块
		/// （实测：完全越界的 AtlasTexture 区域整块透明）。因此缩小只作用于上传副本，
		/// CPU 侧始终保留原始尺寸供裁剪与像素读取。
		/// </summary>
		static Image CreateGpuUploadImage(Image img)
		{
			// Some Android GPUs reject or silently fail very large texture uploads.
			// Downscaling here preserves a visible placeholder-quality result instead
			// of allowing a texture creation failure to break rendering.
			int maxSize = OS.GetName() == "Android" ? 4096 : 16384;
			int w = img.GetWidth();
			int h = img.GetHeight();
			if (w <= maxSize && h <= maxSize)
				return img;
			float scale = System.Math.Min((float)maxSize / w, (float)maxSize / h);
			int newW = (int)(w * scale);
			int newH = (int)(h * scale);
			if (newW < 1) newW = 1;
			if (newH < 1) newH = 1;
			Image scaled = img.Duplicate() as Image;
			if (scaled == null)
				return img;
			scaled.Resize(newW, newH, Image.Interpolation.Bilinear);
			return scaled;
		}

		internal bool CanRetryPlaceholderLoad(ulong nowMs)
		{
			return IsPlaceholder && nowMs >= placeholderRetryAfterMs;
		}

		internal void MarkPlaceholderRetryScheduled(ulong nowMs)
		{
			if (!IsPlaceholder)
				return;
			placeholderRetryAfterMs = nowMs + PlaceholderRetryIntervalMs;
			Touch();
		}

		Dictionary<string, SpriteInfo> sprites = new Dictionary<string, SpriteInfo>();
	}

	class CallbackInfo
	{
		public CallbackInfo(ASprite src, object obj,
							Action<object, SpriteInfo> callback)
		{
			this.src = src;
			this.obj = obj;
			this.callback = callback;
		}
		public void DoCallback(SpriteInfo info)
		{
			callback(obj, info);
		}
		public ASprite src;
		object obj;
		Action<object, SpriteInfo> callback;
	}

	public static void Init()
	{
		// Godot: timer-based cleanup is handled by EmueraMain _Process or a dedicated Timer node
	}

	public static void GetSprite(ASprite src,
								object obj, Action<object, SpriteInfo> callback)
	{
		if(src == null || src.Bitmap == null)
		{
			if(callback != null)
				callback(null, null);
			return;
		}

		var textureKey = BuildTextureCacheKey(src.Bitmap.path, src.Bitmap.filename);
		TextureInfo ti = null;
		lock(dictLock)
		{
			TryGetTextureInfoAliasLocked(textureKey, out ti);
		}
		if(ti == null)
		{
			var item = new CallbackInfo(src, obj, callback);
			lock(dictLock)
			{
				List<CallbackInfo> list = null;
				if(loading_set.TryGetValue(textureKey, out list))
					list.Add(item);
				else
				{
					list = new List<CallbackInfo> { item };
					loading_set.Add(textureKey, list);
					Loading(src.Bitmap);
				}
			}
		}
		else
			callback(obj, GetSpriteInfo(ti, src));
	}

	public static TextureInfo GetTextureInfo(string name, string filename)
	{
		TextureInfo ti = null;
		if (TryGetTextureInfoCached(name, filename, out ti))
			return ti;
		if(string.IsNullOrEmpty(filename))
			return null;

		if(!uEmuera.Utils.FileExists(filename))
		{
			GenericUtils.Warn(EmueraLogCategory.Sprite, () => $"[SpriteManager.GetTextureInfo] file not found: {filename}");
			if (GenericUtils.IsImageDebugEnabled("resolve"))
				GenericUtils.ImageTrace("IMAGE.RESOLVE.FAIL", () => "image resolve failed",
					() => $"name={name} filename={GenericUtils.RedactTracePath(filename)} failure_kind=not_found");
			ti = CreatePlaceholderTextureInfo(name, filename, "file not found");
			CacheTextureInfo(name, filename, ti);
			return ti;
		}

		Image img = LoadImageOrPlaceholder(filename, name, out bool isPlaceholder);
		ti = new TextureInfo(name, img, isPlaceholder, filename);
		if (!isPlaceholder && GenericUtils.IsImageDebugEnabled("log_success"))
			GenericUtils.ImageTrace("IMAGE.TEXTURE.LOAD_OK", () => "texture loaded",
				() => $"name={name} filename={GenericUtils.RedactTracePath(filename)} size={img.GetWidth()}x{img.GetHeight()}");
		return CacheTextureInfo(name, filename, ti);
	}

	internal static TextureInfo GetTextureInfoForScriptComposition(string name, string filename)
	{
		if(string.IsNullOrEmpty(filename))
			return null;
		string resolved = uEmuera.Utils.ResolveExistingFilePath(filename);
		if(!string.IsNullOrEmpty(resolved))
			filename = resolved;
		if(!uEmuera.Utils.FileExists(filename))
			return null;

		TextureInfo ti = null;
		lock(dictLock)
		{
			ti = GetTextureInfoCachedLocked(name, filename);
			if(ti != null && !ti.IsPlaceholder && ti.image != null && !ti.IsDisposed)
			{
				ti.Touch();
				ti.MarkCompositionSource();
				return ti;
			}
		}

		Image img = LoadImageOrPlaceholder(filename, name, out bool isPlaceholder);
		if(isPlaceholder || img == null || img.GetWidth() <= 0 || img.GetHeight() <= 0)
		{
			img?.Dispose();
			return null;
		}
		ti = new TextureInfo(name, img, false, filename);
		return CacheTextureInfo(name, filename, ti);
	}

	internal static bool TryGetTextureInfoCached(string name, string filename, out TextureInfo ti)
	{
		lock(dictLock)
		{
			ti = GetTextureInfoCachedLocked(name, filename);
			if(ti != null)
			{
				ti.Touch();
				return true;
			}
		}
		return false;
	}

	internal static bool RequestTextureInfoAsync(string name, string filename)
	{
		if(string.IsNullOrEmpty(filename))
			return false;

		lock(dictLock)
		{
			var cached = GetTextureInfoCachedLocked(name, filename);
			if(cached != null)
			{
				if(!cached.IsPlaceholder)
					return false;
				ulong nowMs = Time.GetTicksMsec();
				if(!cached.CanRetryPlaceholderLoad(nowMs))
					return false;
				cached.MarkPlaceholderRetryScheduled(nowMs);
			}

			// 仅用于 UI 显示的图片先把 Android 外部存储 I/O 和图片解码移出 Godot 主线程。
			// ImageTexture 仍在 TextureInfo.texture 中按旧路径创建，避免后台线程触碰 RenderingServer/GPU 资源。
			string key = BuildAsyncTextureLoadKey(filename, name);
			if(async_loading_keys.Contains(key))
				return true;

			async_loading_keys.Add(key);
			pending_async_texture_loads.Enqueue(new AsyncTextureLoadRequest
			{
				Name = name,
				Filename = filename,
				Key = key,
				Epoch = async_texture_load_epoch,
			});
			if(async_texture_load_concurrency <= 0)
				async_texture_load_concurrency = OS.HasFeature("mobile") ? MobileAsyncTextureConcurrency : DesktopAsyncTextureConcurrency;
			StartPendingAsyncTextureLoadsLocked();
			return true;
		}
	}

	internal static long TextureLoadVersion => Volatile.Read(ref texture_load_version);

	public static TextureInfoOtherThread GetTextureInfoOtherThread(
		string name, string path, Action<TextureInfo> callback)
	{
		var ti = new TextureInfoOtherThread
		{
			name = name,
			path = path,
			callback = callback,
			mutex = null,
		};
		lock(dictLock)
		{
			texture_other_threads.Add(ti);
		}
		return ti;
	}

	public class TextureInfoOtherThread
	{
		public string name;
		public string path;
		public Action<TextureInfo> callback;
		public System.Threading.Mutex mutex;
	}

	class AsyncTextureLoadRequest
	{
		public string Name;
		public string Filename;
		public string Key;
		public long Epoch;
	}

	class AsyncTextureLoadResult
	{
		public string Name;
		public string Filename;
		public string Key;
		public long Epoch;
		public Image Image;
		public bool IsPlaceholder;
	}

	static List<TextureInfoOtherThread> texture_other_threads = new List<TextureInfoOtherThread>();

	static void Loading(uEmuera.Drawing.Bitmap baseimage)
	{
		TextureInfo ti = null;
		if(uEmuera.Utils.FileExists(baseimage.path))
		{
			Image img = LoadImageOrPlaceholder(baseimage.path, baseimage.filename, out bool isPlaceholder);
			ti = new TextureInfo(baseimage.path, img, isPlaceholder, baseimage.path);
			baseimage.size.Width = img.GetWidth();
			baseimage.size.Height = img.GetHeight();
		}
		else
		{
			ti = CreatePlaceholderTextureInfo(baseimage.path, baseimage.path, "file not found");
			baseimage.size.Width = ti.width;
			baseimage.size.Height = ti.height;
		}

		List<CallbackInfo> callbacks = null;
		TextureInfo cached = null;
		lock(dictLock)
		{
			if (ti != null)
				cached = CacheTextureInfo(baseimage.path, baseimage.path, ti);

			string textureKey = BuildTextureCacheKey(baseimage.path, baseimage.filename);
			if(loading_set.TryGetValue(textureKey, out var list))
			{
				callbacks = new List<CallbackInfo>(list);
				loading_set.Remove(textureKey);
			}
		}

		if (callbacks != null)
		{
			var count = callbacks.Count;
			for(int i=0; i<count; ++i)
			{
				var item = callbacks[i];
				item.DoCallback(GetSpriteInfo(cached ?? ti, item.src));
			}
		}
	}

	static Image LoadImageOrPlaceholder(string filename, string name, out bool isPlaceholder)
	{
		isPlaceholder = false;
		try
		{
			byte[] content = uEmuera.Utils.ReadAllBytes(filename);
			var extname = uEmuera.Utils.GetSuffix(filename).ToLower();
			Image img = new Image();
			Error err = Error.Failed;

			if (extname == "png")
				err = img.LoadPngFromBuffer(content);
			else if (extname == "jpg" || extname == "jpeg")
				err = img.LoadJpgFromBuffer(content);
			else if (extname == "webp")
				err = img.LoadWebpFromBuffer(content);
			else if (extname == "bmp")
				err = img.LoadBmpFromBuffer(content);
			else if (extname == "tga")
				err = img.LoadTgaFromBuffer(content);
			else
			{
				err = img.Load(filename);
			}

			if (err == Error.Ok && img.GetWidth() > 0 && img.GetHeight() > 0)
				return img;

			img.Dispose();
			LogSpriteWarning(() => $"[SpriteManager] image decode failed, using transparent placeholder: {filename}, err={err}");
				if (GenericUtils.IsImageDebugEnabled("texture"))
					GenericUtils.ImageTrace("IMAGE.TEXTURE.LOAD_FAIL", () => "texture load failed",
						() => $"filename={GenericUtils.RedactTracePath(filename)} failure_kind=decode_fail err={err}");
		}
		catch (Exception ex)
		{
			LogSpriteWarning(() => $"[SpriteManager] image load exception, using transparent placeholder: {filename}, error={ex.Message}");
		}
		isPlaceholder = true;
		return CreatePlaceholderImage();
	}

	static TextureInfo CreatePlaceholderTextureInfo(string name, string filename, string reason)
	{
		LogSpriteWarning(() => $"[SpriteManager] using transparent placeholder for {filename}: {reason}");
		return new TextureInfo(name, CreatePlaceholderImage(), true);
	}

	[System.Diagnostics.Conditional("DEBUG")]
	[System.Diagnostics.Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
	static void LogSpriteWarning(Func<string> messageFactory,
		[System.Runtime.CompilerServices.CallerMemberName] string member = "",
		[System.Runtime.CompilerServices.CallerFilePath] string file = "",
		[System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
	{
		GenericUtils.Warn(EmueraLogCategory.Sprite, messageFactory, member, file, line);
	}

	static Image CreatePlaceholderImage()
	{
		Image img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		img.SetPixel(0, 0, new Godot.Color(0, 0, 0, 0));
		return img;
	}

	static TextureInfo CacheTextureInfo(string name, string filename, TextureInfo ti)
	{
		TextureInfo replacedPlaceholder = null;
		lock(dictLock)
		{
			var existing = GetTextureInfoCachedLocked(name, filename);
			if(existing != null)
			{
				if(existing.IsPlaceholder && !ti.IsPlaceholder)
				{
					// Android 外部存储偶发读失败时会先得到占位纹理。后续真实纹理完成后必须覆盖占位，
					// 否则角色层会长期拿到 1x1 占位图，在 ColorMatrix 或白底下表现成闪白。
					RemoveTextureInfoAliasesLocked(existing);
					if(CanEvict(existing))
						replacedPlaceholder = existing;
				}
				else
				{
					// 同一文件可能先被同步路径按 path 命中，又被异步 UI 路径按资源名完成。
					// 任一别名已存在时都复用同一个 TextureInfo，避免移动端重复持有大图像素；也不要用占位降级真实纹理。
					ti.Dispose();
					existing.Touch();
					SetTextureInfoAliasLocked(filename, existing);
					if (ShouldAliasTextureName(name, filename))
						SetTextureInfoAliasLocked(name, existing);
					return existing;
				}
			}

			// 纹理缓存必须以完整路径作为主要身份。TW 等资源包允许不同目录下存在同名 webp；
			// 如果把 basename 当全局别名，会导致角色立绘复用到另一个文件夹里的同名图片。
			SetTextureInfoAliasLocked(filename, ti);
			if (ShouldAliasTextureName(name, filename))
				SetTextureInfoAliasLocked(name, ti);
		}
		replacedPlaceholder?.Dispose();
		return ti;
	}

	static void SetTextureInfoAliasLocked(string key, TextureInfo ti)
	{
		key = NormalizeTextureCacheKey(key);
		if (string.IsNullOrEmpty(key))
			return;
		if (texture_dict.TryGetValue(key, out var existing))
		{
			if (existing != null && !existing.IsDisposed)
				return;
			RemoveTextureInfoAliasesLocked(existing);
		}
		texture_dict[key] = ti;
	}

	static bool TryGetTextureInfoAliasLocked(string key, out TextureInfo ti)
	{
		ti = null;
		key = NormalizeTextureCacheKey(key);
		if (string.IsNullOrEmpty(key))
			return false;
		return texture_dict.TryGetValue(key, out ti) && ti != null && !ti.IsDisposed;
	}

	static TextureInfo GetTextureInfoCachedLocked(string name, string filename)
	{
		TextureInfo ti = null;
		if(!string.IsNullOrEmpty(filename) && TryGetTextureInfoAliasLocked(filename, out ti))
			return ti;
		if(ShouldAliasTextureName(name, filename) && TryGetTextureInfoAliasLocked(name, out ti))
			return ti;
		if(string.IsNullOrEmpty(filename) && !string.IsNullOrEmpty(name) && TryGetTextureInfoAliasLocked(name, out ti))
			return ti;
		return null;
	}

	static string BuildTextureCacheKey(string filename, string fallbackName)
	{
		string key = !string.IsNullOrEmpty(filename) ? filename : fallbackName;
		return NormalizeTextureCacheKey(key);
	}

	static string NormalizeTextureCacheKey(string key)
	{
		if (string.IsNullOrEmpty(key))
			return "";
		return uEmuera.Utils.NormalizePath(key.Trim());
	}

	static bool ShouldAliasTextureName(string name, string filename)
	{
		if (string.IsNullOrEmpty(name))
			return false;
		if (string.IsNullOrEmpty(filename))
			return true;
		string normalizedName = NormalizeTextureCacheKey(name);
		string normalizedFilename = NormalizeTextureCacheKey(filename);
		if (string.Equals(normalizedName, normalizedFilename, StringComparison.OrdinalIgnoreCase))
			return true;
		return IsPathLikeTextureKey(normalizedName);
	}

	static bool IsPathLikeTextureKey(string key)
	{
		if (string.IsNullOrEmpty(key))
			return false;
		return key.Contains("://", StringComparison.Ordinal)
			|| key.IndexOf('/') >= 0
			|| key.IndexOf('\\') >= 0
			|| System.IO.Path.IsPathRooted(key);
	}

	static SpriteInfo GetSpriteInfo(TextureInfo textinfo, ASprite src)
	{
		if (textinfo == null)
			return null;
		return textinfo.GetSprite(src);
	}

	internal static void PinTextureInfo(TextureInfo ti)
	{
		// Pinning is the contract between visible UI nodes and cache cleanup. The UI
		// layer owns the pin lifetime; SpriteManager only enforces that pinned
		// textures remain non-evictable.
		if (ti == null)
			return;
		lock(dictLock)
		{
			if (ti.IsDisposed)
				return;
			ti.pinCount += 1;
			ti.Touch();
		}
	}

	internal static void UnpinTextureInfo(TextureInfo ti)
	{
		if (ti == null)
			return;
		lock(dictLock)
		{
			if (ti.pinCount > 0)
				ti.pinCount -= 1;
			ti.Touch();
		}
	}

	internal static void GivebackSpriteInfo(SpriteInfo info)
	{
		if(info == null)
			return;
		info.parent.Release();
	}

	public static void UpdateCleanup()
	{
		// Perform selection under lock, then dispose outside the lock. Disposing
		// Godot resources may touch engine internals and should not block unrelated
		// texture lookups on the same monitor.
		ulong nowMs = Time.GetTicksMsec();
		if (nowMs - lastCleanupMs < CleanupIntervalMs)
			return;
		lastCleanupMs = nowMs;

		double now = nowMs / 1000.0;
		long budgetBytes = OS.HasFeature("mobile") ? MobileTextureBudgetBytes : DesktopTextureBudgetBytes;
		int budgetEntries = OS.HasFeature("mobile") ? MobileTextureEntryBudget : DesktopTextureEntryBudget;
		var disposeList = new List<TextureInfo>();

		lock(dictLock)
		{
			var unique = CollectUniqueTexturesLocked();
			long totalBytes = 0;
			for (int i = 0; i < unique.Count; i++)
				totalBytes += unique[i].estimatedBytes;
			int liveCount = unique.Count;
			bool overBudget = totalBytes > budgetBytes || liveCount > budgetEntries;

			// Visible console/CBG nodes pin their TextureInfo. Cleanup can therefore
			// reclaim old off-screen CGs without invalidating textures still assigned
			// to Godot Controls.
			if (overBudget)
				unique.Sort((a, b) => a.pasttime.CompareTo(b.pasttime));
			int cleanupBudget = OS.HasFeature("mobile") ? MobileCleanupDisposeBudget : DesktopCleanupDisposeBudget;
			for (int i = 0; i < unique.Count; i++)
			{
				var ti = unique[i];
				if (!CanEvict(ti))
					continue;
				bool expired = ti.pasttime <= now;
				overBudget = totalBytes > budgetBytes || liveCount > budgetEntries;
				if (!expired && !overBudget)
					continue;
				RemoveTextureInfoAliasesLocked(ti);
				disposeList.Add(ti);
				totalBytes -= ti.estimatedBytes;
				liveCount -= 1;
				if (disposeList.Count >= cleanupBudget)
					break;
			}
		}

		for (int i = 0; i < disposeList.Count; i++)
			disposeList[i].Dispose();
	}

	// 渲染路径在需要 GPU 纹理但尚未上传时调用。返回 true 表示该纹理已就绪；
	// false 表示已登记到每帧限量上传队列（调用方应返回占位/空并等待 texture_load_version 变化）。
	internal static bool EnsureGpuTextureDeferred(TextureInfo ti)
	{
		bool readyNow(TextureInfo t) => t != null && !t.IsDisposed && !t.IsPlaceholder && t.IsGpuTextureReady;
		if (ti == null || ti.IsDisposed || ti.IsPlaceholder || ti.IsGpuTextureReady)
			return readyNow(ti);
		lock (dictLock)
		{
			if (ti.IsDisposed || ti.IsPlaceholder || ti.IsGpuTextureReady)
				return readyNow(ti);
			pending_gpu_uploads.Add(ti);
			ti.Touch();
		}
		return false;
	}

	// 主线程每帧调用：把已解码、待上传的纹理按预算批量创建 ImageTexture。
	// 上传成功的纹理通过 texture_load_version 通知 UI 刷新占位。不在锁内做 GPU 操作。
	static void ProcessPendingGpuUploads()
	{
		if (pending_gpu_uploads.Count == 0)
			return;
		int budget = OS.HasFeature("mobile") ? MobileGpuUploadBudget : DesktopGpuUploadBudget;
		List<TextureInfo> batch = new List<TextureInfo>(budget);
		lock (dictLock)
		{
			var iter = pending_gpu_uploads.GetEnumerator();
			while (iter.MoveNext() && batch.Count < budget)
				batch.Add(iter.Current);
			for (int i = 0; i < batch.Count; i++)
				pending_gpu_uploads.Remove(batch[i]);
		}
		bool uploadedAny = false;
		for (int i = 0; i < batch.Count; i++)
		{
			var ti = batch[i];
			if (ti == null || ti.IsDisposed || ti.IsPlaceholder)
				continue;
			if (ti.CreateGpuTextureIfNeeded())
				uploadedAny = true;
		}
		if (uploadedAny)
			Interlocked.Increment(ref texture_load_version);
	}

	public static void UpdateOtherThreads()
	{
		ProcessAsyncTextureLoadCompletions();
		ProcessPendingGpuUploads();

		TextureInfoOtherThread tiot = null;
		lock(dictLock)
		{
			if(texture_other_threads.Count == 0)
				return;
			tiot = texture_other_threads[0];
			texture_other_threads.RemoveAt(0);
		}

		tiot.mutex = new System.Threading.Mutex(true);
		var ti = GetTextureInfo(tiot.name, tiot.path);
		tiot.callback(ti);
		tiot.mutex.ReleaseMutex();
	}

	static void ProcessAsyncTextureLoadCompletions()
	{
		int baseBudget = OS.HasFeature("mobile") ? MobileAsyncTextureCompletionBudget : DesktopAsyncTextureCompletionBudget;
		// 批量加载时提高每帧完成数
		int pendingCount = completed_async_texture_loads.Count;
		int budget = pendingCount > BulkLoadQueueThreshold ? Math.Min(baseBudget * 2, 12) : baseBudget;
		int processed = 0;
		while(processed < budget && completed_async_texture_loads.TryDequeue(out var result))
		{
			processed++;
			if(result == null)
				continue;

			try
			{
				if(result.Epoch != Volatile.Read(ref async_texture_load_epoch))
				{
					result.Image?.Dispose();
					continue;
				}

				Image img = result.Image ?? CreatePlaceholderImage();
				var ti = new TextureInfo(result.Name, img, result.IsPlaceholder || result.Image == null, result.Filename);
				var cached = CacheTextureInfo(result.Name, result.Filename, ti);
				// 解码完成后立即登记待上传，GPU 上传由 ProcessPendingGpuUploads 每帧限量执行，
				// 而不是等渲染路径首次访问 ti.texture 时同步创建。
				if (!cached.IsPlaceholder)
					EnsureGpuTextureDeferred(cached);
				if(!cached.IsPlaceholder && GenericUtils.IsImageDebugEnabled("log_success"))
					GenericUtils.ImageTrace("IMAGE.TEXTURE.ASYNC_READY", () => "async texture decoded",
						() => $"name={result.Name} filename={GenericUtils.RedactTracePath(result.Filename)} size={cached.width}x{cached.height}");
				Interlocked.Increment(ref texture_load_version);
			}
			finally
			{
				lock(dictLock)
				{
					async_loading_keys.Remove(result.Key);
				}
			}
		}
	}

	static void StartPendingAsyncTextureLoadsLocked()
	{
		int limit = GetEffectiveConcurrency();
		while(active_async_texture_loads < limit && pending_async_texture_loads.Count > 0)
		{
			var request = pending_async_texture_loads.Dequeue();
			if (active_async_texture_loads == 0)
				async_texture_loads_idle.Reset();
			active_async_texture_loads++;
			ThreadPool.QueueUserWorkItem(_ => RunAsyncTextureLoad(request));
		}
	}

	/// <summary>
	/// 根据队列积压情况动态调整并发数。
	/// 当队列积压超过阈值时（如角色立绘批量加载），临时提高并发数以加速加载。
	/// </summary>
	static int GetEffectiveConcurrency()
	{
		int baseConcurrency = async_texture_load_concurrency > 0
			? async_texture_load_concurrency
			: (OS.HasFeature("mobile") ? MobileAsyncTextureConcurrency : DesktopAsyncTextureConcurrency);

		int pendingCount = pending_async_texture_loads.Count;
		if(pendingCount <= BulkLoadQueueThreshold)
			return baseConcurrency;

		// 队列积压超过阈值，临时提升并发数
		int maxConcurrency = OS.HasFeature("mobile") ? MobileBulkLoadMaxConcurrency : DesktopBulkLoadMaxConcurrency;
		return Math.Min(baseConcurrency * 2, maxConcurrency);
	}

	static void RunAsyncTextureLoad(AsyncTextureLoadRequest request)
	{
		Image img = null;
		bool isPlaceholder = false;
		try
		{
			if(request.Epoch != Volatile.Read(ref async_texture_load_epoch))
				return;
			if(string.IsNullOrEmpty(request.Filename) || !uEmuera.Utils.FileExists(request.Filename))
			{
				GenericUtils.Warn(EmueraLogCategory.Sprite, () => $"[SpriteManager.AsyncTexture] file not found: {request.Filename}");
				img = CreatePlaceholderImage();
				isPlaceholder = true;
			}
			else
			{
				img = LoadImageOrPlaceholder(request.Filename, request.Name, out isPlaceholder);
			}
			completed_async_texture_loads.Enqueue(new AsyncTextureLoadResult
			{
				Name = request.Name,
				Filename = request.Filename,
				Key = request.Key,
				Epoch = request.Epoch,
				Image = img,
				IsPlaceholder = isPlaceholder,
			});
			img = null;
		}
		catch(Exception ex)
		{
			LogSpriteWarning(() => $"[SpriteManager.AsyncTexture] image load exception, using transparent placeholder: {request.Filename}, error={ex.Message}");
			completed_async_texture_loads.Enqueue(new AsyncTextureLoadResult
			{
				Name = request.Name,
				Filename = request.Filename,
				Key = request.Key,
				Epoch = request.Epoch,
				Image = CreatePlaceholderImage(),
				IsPlaceholder = true,
			});
		}
		finally
		{
			img?.Dispose();
			lock(dictLock)
			{
				if(active_async_texture_loads > 0)
					active_async_texture_loads--;
				if (active_async_texture_loads == 0)
					async_texture_loads_idle.Set();
				StartPendingAsyncTextureLoadsLocked();
			}
		}
	}

	static string BuildAsyncTextureLoadKey(string filename, string name)
	{
		string key = !string.IsNullOrEmpty(filename) ? filename : name;
		return uEmuera.Utils.NormalizePath(key ?? "").ToUpperInvariant();
	}

	internal static void ForceClear()
	{
		// Full reset is reserved for lifecycle/reload boundaries. Active display
		// paths should release their pins first; this method then disposes every
		// unique owner exactly once even when multiple aliases exist.
		var disposeList = new List<TextureInfo>();
		lock(dictLock)
		{
			Volatile.Write(ref async_texture_load_epoch, async_texture_load_epoch + 1);
			async_loading_keys.Clear();
			pending_async_texture_loads.Clear();
			pending_gpu_uploads.Clear();
			disposeList = CollectUniqueTexturesLocked();
			texture_dict.Clear();
		}
		if (!async_texture_loads_idle.Wait(AsyncTextureWorkerQuiescenceTimeoutMs))
			GenericUtils.Warn(EmueraLogCategory.Sprite,
				() => "[SpriteManager] async texture workers did not quiesce before lifecycle clear");
		while(completed_async_texture_loads.TryDequeue(out var result))
			result?.Image?.Dispose();
		for (int i = 0; i < disposeList.Count; i++)
			disposeList[i].Dispose();
	}

	static bool CanEvict(TextureInfo ti)
	{
		return ti != null && !ti.IsDisposed && ti.refcount <= 0 && ti.pinCount <= 0;
	}

	static List<TextureInfo> CollectUniqueTexturesLocked()
	{
		var uniqueSet = new HashSet<TextureInfo>();
		var unique = new List<TextureInfo>();
		foreach(var ti in texture_dict.Values)
		{
			if (ti == null || !uniqueSet.Add(ti))
				continue;
			unique.Add(ti);
		}
		return unique;
	}

	static void RemoveTextureInfoAliasesLocked(TextureInfo ti)
	{
		var keys = new List<string>();
		foreach(var pair in texture_dict)
		{
			if (object.ReferenceEquals(pair.Value, ti))
				keys.Add(pair.Key);
		}
		for (int i = 0; i < keys.Count; i++)
			texture_dict.Remove(keys[i]);
	}

	internal static void SetResourceCSVLine(string filename, string[] lines)
	{
		var cache = string.Join("\n", lines);
		// Godot: use simple file-based cache instead of PlayerPrefs
		var cacheFile = Path.Combine(OS.GetUserDataDir(), "csv_cache", filename.GetHashCode().ToString("x8") + ".txt");
		Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
		File.WriteAllText(cacheFile, cache);
		var metaFile = cacheFile + ".meta";
		File.WriteAllText(metaFile, File.GetLastWriteTime(filename).ToString());
	}

	internal static string[] GetResourceCSVLines(string filename)
	{
		var cacheFile = Path.Combine(OS.GetUserDataDir(), "csv_cache", filename.GetHashCode().ToString("x8") + ".txt");
		var metaFile = cacheFile + ".meta";
		if(!File.Exists(cacheFile) || !File.Exists(metaFile))
			return null;
		var oldwritetime = File.ReadAllText(metaFile);
		if(string.IsNullOrEmpty(oldwritetime))
			return null;
		var writetime = File.GetLastWriteTime(filename).ToString();
		if(oldwritetime != writetime)
			return null;
		var cache = File.ReadAllText(cacheFile);
		if(string.IsNullOrEmpty(cache))
			return null;
		return cache.Split('\n');
	}

	internal static void ClearResourceCSVLines(string filename)
	{
		var cacheFile = Path.Combine(OS.GetUserDataDir(), "csv_cache", filename.GetHashCode().ToString("x8") + ".txt");
		var metaFile = cacheFile + ".meta";
		if(File.Exists(cacheFile))
			File.Delete(cacheFile);
		if(File.Exists(metaFile))
			File.Delete(metaFile);
	}

	static Dictionary<string, List<CallbackInfo>> loading_set =
		new Dictionary<string, List<CallbackInfo>>(StringComparer.OrdinalIgnoreCase);
	static Dictionary<string, TextureInfo> texture_dict =
		new Dictionary<string, TextureInfo>(StringComparer.OrdinalIgnoreCase);
	static Queue<AsyncTextureLoadRequest> pending_async_texture_loads =
		new Queue<AsyncTextureLoadRequest>();
	static HashSet<string> async_loading_keys =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	static readonly ConcurrentQueue<AsyncTextureLoadResult> completed_async_texture_loads =
		new ConcurrentQueue<AsyncTextureLoadResult>();
	// 已解码但尚未上传 GPU 的 TextureInfo。渲染路径登记后由 ProcessPendingGpuUploads
	// 每帧限量上传，避免同步 CreateFromImage 在 UI 线程造成帧尖峰。
	static readonly HashSet<TextureInfo> pending_gpu_uploads = new HashSet<TextureInfo>();
	static readonly object dictLock = new object();
	static readonly ManualResetEventSlim async_texture_loads_idle = new ManualResetEventSlim(true);
	static ulong lastCleanupMs = 0;
	static int active_async_texture_loads = 0;
	static int async_texture_load_concurrency = 0;
	static long texture_load_version = 0;
	static long async_texture_load_epoch = 0;
}
