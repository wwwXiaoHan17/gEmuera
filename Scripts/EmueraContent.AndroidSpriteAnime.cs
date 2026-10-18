using System;
using System.Collections.Generic;
using Godot;
using MinorShift.Emuera.Content;

public partial class EmueraContent
{
	const int AndroidCroppedAtlasTextureCacheTarget = 16;
	const long AndroidCroppedAtlasTextureBudgetBytes = 32L * 1024L * 1024L;
	const ulong AndroidCroppedAtlasTextureIdleMs = 30000;
	const ulong AndroidCroppedAtlasTextureCleanupIntervalMs = 3000;
	const int AndroidFullAtlasTextureMaxSize = 4096;

	sealed class AndroidCroppedAtlasTextureEntry : IDisposable
	{
		public SpriteManager.TextureInfo SourceInfo;
		public Image FrameImage;
		public ImageTexture Texture;
		public Rect2I SourceRegion;
		public ulong LastUsedMs;
		public long EstimatedBytes;

		public void Dispose()
		{
			Texture?.Dispose();
			Texture = null;
			FrameImage?.Dispose();
			FrameImage = null;
			SourceInfo = null;
		}
	}

	// key 同时接受 SpriteAnime 和静态 ASpriteSingle。两者都从同一份 CPU 图集中裁小块，
	// 从而不会把 8192px 宽的图集直接上传到 Android GPU。
	readonly Dictionary<object, AndroidCroppedAtlasTextureEntry> androidCroppedAtlasTextures = new();
	ulong lastAndroidCroppedAtlasTextureCleanupMs;

	static bool UseAndroidCroppedAtlasTexture => OS.GetName() == "Android";

	// Android 设备不一定能可靠上传 8000px 宽的图集，而且单张完整 RGBA 纹理会占用
	// 约 72 MiB 显存。这里保留 CPU 侧整图供裁剪，但 GPU 只持有实际显示的帧/图块；
	// 动画同一对象复用一张 ImageTexture，静态图块则按 sprite 缓存，避免逐帧创建纹理和无界缓存。
	Texture2D GetAndroidSpriteAnimeFrameTexture(SpriteAnime anime,
		uEmuera.Drawing.BitmapTexture bitmap, uEmuera.Drawing.Rectangle srcRect)
	{
		if (!UseAndroidCroppedAtlasTexture || anime == null || bitmap == null)
			return null;

		if (!TryGetAndroidAtlasSource(bitmap, out var ti, out var sourceImage))
		{
			return null;
		}
		return GetOrCreateAndroidCroppedAtlasTexture(anime, ti, sourceImage, srcRect);
	}

	// 静态 CSV sprite 的源矩形使用**原始图集坐标**（CSV 即以源文件像素尺寸书写）。
	// Android 的 GPU 尺寸上限会让上传副本缩小（如 8000x2000 -> 4096x1024），
	// 但 CSV 坐标不会随之改变；若直接拿原始坐标去裁「已缩小的位图」必然越界，
	// 表现为立绘空白或被裁掉一块。这里先把坐标按缩放比换算到当前位图空间再裁剪，
	// 裁出的小图块再放大回原尺寸，使下游（AtlasTexture 命中判定、DrawSize 计算）
	// 与桌面端走完全相同的几何，只是源像素分辨率受 4096 上限约束。
	// 返回 true 表示调用方不能再回退到整图 AtlasTexture 路径。
	bool TryGetAndroidStaticAtlasRegionTexture(ASpriteSingle sprite,
		uEmuera.Drawing.BitmapTexture bitmap, uEmuera.Drawing.Rectangle srcRect, out Texture2D texture)
	{
		texture = null;
		if (!UseAndroidCroppedAtlasTexture || sprite == null || bitmap == null)
			return false;
		if (!TryGetAndroidAtlasSource(bitmap, out var ti, out var sourceImage))
			return false;

		int sourceWidth = sourceImage.GetWidth();
		int sourceHeight = sourceImage.GetHeight();
		if (sourceWidth <= AndroidFullAtlasTextureMaxSize && sourceHeight <= AndroidFullAtlasTextureMaxSize)
			return false;

		// 完整大图仍交给既有的缩放上传逻辑；这里只处理能安全变成小纹理的 Atlas 子区域。
		if (srcRect.X == 0 && srcRect.Y == 0
			&& srcRect.Width == sourceWidth && srcRect.Height == sourceHeight)
			return false;

		texture = GetOrCreateAndroidCroppedAtlasTexture(sprite, ti, sourceImage, srcRect);
		return true;
	}

	bool TryGetAndroidAtlasSource(uEmuera.Drawing.BitmapTexture bitmap,
		out SpriteManager.TextureInfo ti, out Image sourceImage)
	{
		ti = null;
		sourceImage = null;
		if (bitmap == null)
			return false;

		ti = bitmap.CachedTextureInfo;
		if (ti == null || ti.IsPlaceholder)
		{
			if (bitmap.RequestTextureInfoAsync())
				TrackAsyncTextureRequestForCurrentRender();
			return false;
		}

		sourceImage = ti.image;
		return sourceImage != null;
	}

	Texture2D GetOrCreateAndroidCroppedAtlasTexture(object cacheKey, SpriteManager.TextureInfo ti,
		Image sourceImage, uEmuera.Drawing.Rectangle srcRect)
	{
		if (cacheKey == null || ti == null || sourceImage == null || srcRect.Width <= 0 || srcRect.Height <= 0
			|| srcRect.X < 0 || srcRect.Y < 0
			|| srcRect.X + srcRect.Width > sourceImage.GetWidth()
			|| srcRect.Y + srcRect.Height > sourceImage.GetHeight())
		{
			if (GenericUtils.IsImageDebugEnabled("texture"))
			{
				GenericUtils.ImageTrace("IMAGE.ATLAS.CROP_FAIL", () => "atlas crop source rectangle is invalid",
					() => $"name={ti?.imagename ?? string.Empty} region={srcRect.X},{srcRect.Y},{srcRect.Width},{srcRect.Height} source={sourceImage?.GetWidth() ?? 0}x{sourceImage?.GetHeight() ?? 0} failure_kind=invalid_region");
			}
			return null;
		}

		// CSV 坐标以源文件原始尺寸为准，而 sourceImage 受 GPU 上限约束可能已缩小
		// （8000x2000 -> 4096x1024）。先换算坐标空间再裁剪，否则必然越界。
		int srcPixelW = srcRect.Width;
		int srcPixelH = srcRect.Height;
		float scaleX = 1f;
		float scaleY = 1f;
		var scaledRect = srcRect;
		// 未超过 GPU 上限的图集（桌面端全部、Android 上 <=4096px 的图）不会换算，
		// scale 保持 1，下面的裁剪与改动前逐位等价。
		ti.ScaleSourceRectToImageSpace(ref scaledRect, out scaleX, out scaleY);
		if (scaledRect.Width <= 0 || scaledRect.Height <= 0
			|| scaledRect.X + scaledRect.Width > sourceImage.GetWidth()
			|| scaledRect.Y + scaledRect.Height > sourceImage.GetHeight())
		{
			if (GenericUtils.IsImageDebugEnabled("texture"))
			{
				GenericUtils.ImageTrace("IMAGE.ATLAS.CROP_FAIL", () => "atlas crop source rectangle is invalid after scale mapping",
					() => $"name={ti.imagename} region={srcRect.X},{srcRect.Y},{srcRect.Width},{srcRect.Height} mapped={scaledRect.X},{scaledRect.Y},{scaledRect.Width},{scaledRect.Height} source={sourceImage.GetWidth()}x{sourceImage.GetHeight()} scale={scaleX:0.####},{scaleY:0.####} failure_kind=invalid_region_scaled");
			}
			return null;
		}

		TrackTexturePin(ti);
		var region = new Rect2I(scaledRect.X, scaledRect.Y, scaledRect.Width, scaledRect.Height);
		Image.Format format = sourceImage.GetFormat();
		bool recreate = !androidCroppedAtlasTextures.TryGetValue(cacheKey, out var entry)
			|| entry == null
			|| !ReferenceEquals(entry.SourceInfo, ti)
			|| entry.FrameImage == null
			|| entry.Texture == null
			|| entry.FrameImage.GetWidth() != srcPixelW
			|| entry.FrameImage.GetHeight() != srcPixelH
			|| entry.FrameImage.GetFormat() != format;

		if (recreate)
		{
			entry?.Dispose();
			entry = new AndroidCroppedAtlasTextureEntry
			{
				SourceInfo = ti,
				FrameImage = Image.CreateEmpty(srcPixelW, srcPixelH, false, format),
				SourceRegion = region,
				EstimatedBytes = (long)srcPixelW * srcPixelH * 4L,
			};
			try
			{
				BlitScaledAtlasRegion(entry.FrameImage, sourceImage, region, srcPixelW, srcPixelH);
				entry.Texture = ImageTexture.CreateFromImage(entry.FrameImage);
				androidCroppedAtlasTextures[cacheKey] = entry;
			}
			catch (Exception ex)
			{
				entry.Dispose();
				if (GenericUtils.IsImageDebugEnabled("texture"))
				{
					GenericUtils.ImageTrace("IMAGE.ATLAS.CROP_FAIL", () => "atlas crop texture creation failed",
						() => $"name={ti.imagename} region={srcRect.X},{srcRect.Y},{srcRect.Width},{srcRect.Height} mapped={scaledRect.X},{scaledRect.Y},{scaledRect.Width},{scaledRect.Height} failure_kind=texture_create_fail error={ex.GetType().Name}");
				}
				return null;
			}
		}
		else if (entry.SourceRegion != region)
		{
			try
			{
				BlitScaledAtlasRegion(entry.FrameImage, sourceImage, region, srcPixelW, srcPixelH);
				entry.Texture.Update(entry.FrameImage);
				entry.SourceRegion = region;
			}
			catch (Exception ex)
			{
				if (GenericUtils.IsImageDebugEnabled("texture"))
				{
					GenericUtils.ImageTrace("IMAGE.ATLAS.CROP_FAIL", () => "atlas crop texture update failed",
						() => $"name={ti.imagename} region={srcRect.X},{srcRect.Y},{srcRect.Width},{srcRect.Height} mapped={scaledRect.X},{scaledRect.Y},{scaledRect.Width},{scaledRect.Height} failure_kind=texture_update_fail error={ex.GetType().Name}");
				}
				return null;
			}
		}

		entry.LastUsedMs = Time.GetTicksMsec();
		return entry.Texture;
	}

	// 从（可能已缩小的）CPU 位图裁出 region，并把结果还原成 targetW x targetH。
	// 尺寸一致时是纯 BlitRect（与改动前逐位等价，桌面端与未超上限的图不受影响）；
	// 尺寸不一致时（Android 大图集）多做一次放大，使纹理仍是 CSV 记录的物理尺寸，
	// 从而让下游几何计算与桌面端完全一致，只损失源像素分辨率。
	static void BlitScaledAtlasRegion(Image target, Image source, Rect2I region, int targetW, int targetH)
	{
		if (region.Size.X == targetW && region.Size.Y == targetH)
		{
			target.BlitRect(source, region, Vector2I.Zero);
			return;
		}
		target.BlitRect(source, region, Vector2I.Zero);
		target.Resize(targetW, targetH, Image.Interpolation.Bilinear);
	}

	// 仅在缓存超过常用规模后清理长时间未触达的动画或静态图块。可见 Canvas 动画每 50ms
	// 会触达；离屏历史行重新进入可见区时会按当前帧或源矩形自动重建小纹理。
	void CleanupAndroidSpriteAnimeFrameTextures()
	{
		if (!UseAndroidCroppedAtlasTexture || androidCroppedAtlasTextures.Count == 0)
		{
			return;
		}
		long estimatedBytes = 0;
		foreach (var entry in androidCroppedAtlasTextures.Values)
			estimatedBytes += entry?.EstimatedBytes ?? 0;
		if (androidCroppedAtlasTextures.Count <= AndroidCroppedAtlasTextureCacheTarget
			&& estimatedBytes <= AndroidCroppedAtlasTextureBudgetBytes)
			return;

		ulong now = Time.GetTicksMsec();
		if (lastAndroidCroppedAtlasTextureCleanupMs != 0
			&& now - lastAndroidCroppedAtlasTextureCleanupMs < AndroidCroppedAtlasTextureCleanupIntervalMs)
		{
			return;
		}
		lastAndroidCroppedAtlasTextureCleanupMs = now;

		var removals = new List<object>();
		foreach (var item in androidCroppedAtlasTextures)
		{
			if (now - item.Value.LastUsedMs > AndroidCroppedAtlasTextureIdleMs)
				removals.Add(item.Key);
		}
		for (int i = 0; i < removals.Count; i++)
		{
			if (androidCroppedAtlasTextures.Remove(removals[i], out var entry))
				entry.Dispose();
		}
	}

	void DisposeAndroidSpriteAnimeFrameTextures()
	{
		foreach (var entry in androidCroppedAtlasTextures.Values)
			entry?.Dispose();
		androidCroppedAtlasTextures.Clear();
		lastAndroidCroppedAtlasTextureCleanupMs = 0;
	}
}
