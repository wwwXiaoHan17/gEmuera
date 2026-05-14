using System;
using System.IO;
using System.Collections.Generic;
using Godot;
using MinorShift.Emuera.Content;
using uEmuera.Drawing;

internal static class SpriteManager
{
	static double kPastTime = 300.0;

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

	internal class TextureInfo : IDisposable
	{
		internal TextureInfo(string b, Image img)
		{
			imagename = b;
			image = img;
			pasttime = Time.GetTicksMsec() / 1000.0 + kPastTime;
		}

		internal SpriteInfo GetSprite(ASprite src)
		{
			SpriteInfo sprite = null;
			if(!sprites.TryGetValue(src.Name, out sprite))
			{
				var atlas = new AtlasTexture();
				atlas.Atlas = texture;
				if (src is ASpriteSingle single)
				{
					atlas.Region = new Rect2(
						single.SrcRectangle.X, single.SrcRectangle.Y,
						single.SrcRectangle.Width, single.SrcRectangle.Height);
				}
				else
				{
					atlas.Region = new Rect2(
						src.Rectangle.X, src.Rectangle.Y,
						src.Rectangle.Width, src.Rectangle.Height);
				}
				sprite = new SpriteInfo(this, atlas);
				sprites[src.Name] = sprite;
			}
			if(sprite != null)
				refcount += 1;
			return sprite;
		}

		internal void Release()
		{
			refcount -= 1;
			pasttime = Time.GetTicksMsec() / 1000.0 + kPastTime;
		}

		public void Dispose()
		{
			var iter = sprites.Values.GetEnumerator();
			while(iter.MoveNext())
			{
				iter.Current.Dispose();
			}
			sprites.Clear();
			sprites = null;

			_texture?.Dispose();
			_texture = null;
			image?.Dispose();
			image = null;
		}

		internal string imagename = null;
		internal int refcount = 0;
		internal double pasttime = 0;
		internal int width { get { return image?.GetWidth() ?? 0; } }
		internal int height { get { return image?.GetHeight() ?? 0; } }
		internal Image image = null;
		private ImageTexture _texture = null;
		internal ImageTexture texture
		{
			get
			{
				if (_texture == null && image != null)
				{
					try
					{
						EnsureImageFitsGpu(image);
						_texture = ImageTexture.CreateFromImage(image);
					}
					catch (Exception ex)
					{
						GD.PushWarning($"[SpriteManager] Failed to create ImageTexture for {imagename}: {ex.Message}");
					}
				}
				return _texture;
			}
		}
		internal void RecreateTexture()
		{
			_texture?.Dispose();
			try
			{
				if (image != null)
				{
					EnsureImageFitsGpu(image);
					_texture = ImageTexture.CreateFromImage(image);
				}
				else
					_texture = null;
			}
			catch (Exception ex)
			{
				_texture = null;
				GD.PushWarning($"[SpriteManager] Failed to recreate ImageTexture for {imagename}: {ex.Message}");
			}
		}

		static void EnsureImageFitsGpu(Image img)
		{
			int maxSize = OS.GetName() == "Android" ? 4096 : 16384;
			int w = img.GetWidth();
			int h = img.GetHeight();
			if (w <= maxSize && h <= maxSize)
				return;
			float scale = System.Math.Min((float)maxSize / w, (float)maxSize / h);
			int newW = (int)(w * scale);
			int newH = (int)(h * scale);
			if (newW < 1) newW = 1;
			if (newH < 1) newH = 1;
			img.Resize(newW, newH, Image.Interpolation.Bilinear);
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

		var basename = src.Bitmap.filename;
		TextureInfo ti = null;
		lock(dictLock)
		{
			texture_dict.TryGetValue(basename, out ti);
		}
		if(ti == null)
		{
			var item = new CallbackInfo(src, obj, callback);
			lock(dictLock)
			{
				List<CallbackInfo> list = null;
				if(loading_set.TryGetValue(basename, out list))
					list.Add(item);
				else
				{
					list = new List<CallbackInfo> { item };
					loading_set.Add(basename, list);
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
		lock(dictLock)
		{
			if(texture_dict.TryGetValue(name, out ti))
				return ti;
		}
		if(string.IsNullOrEmpty(filename))
			return null;

		if(!uEmuera.Utils.FileExists(filename))
		{
			GD.PushWarning($"[SpriteManager.GetTextureInfo] file not found: {filename}");
			ti = CreatePlaceholderTextureInfo(name, filename, "file not found");
			CacheTextureInfo(name, filename, ti);
			return ti;
		}

		Image img = LoadImageOrPlaceholder(filename, name);
		ti = new TextureInfo(name, img);
		return CacheTextureInfo(name, filename, ti);
	}

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

	static List<TextureInfoOtherThread> texture_other_threads = new List<TextureInfoOtherThread>();

	static void Loading(uEmuera.Drawing.Bitmap baseimage)
	{
		TextureInfo ti = null;
		if(uEmuera.Utils.FileExists(baseimage.path))
		{
			Image img = LoadImageOrPlaceholder(baseimage.path, baseimage.filename);
			ti = new TextureInfo(baseimage.filename, img);
			baseimage.size.Width = img.GetWidth();
			baseimage.size.Height = img.GetHeight();
		}
		else
		{
			ti = CreatePlaceholderTextureInfo(baseimage.filename, baseimage.path, "file not found");
			baseimage.size.Width = ti.width;
			baseimage.size.Height = ti.height;
		}

		List<CallbackInfo> callbacks = null;
		lock(dictLock)
		{
			if (ti != null)
			{
				// Index by both filename and full path for consistent lookup
				texture_dict[baseimage.filename] = ti;
				if (!string.IsNullOrEmpty(baseimage.path) && baseimage.path != baseimage.filename)
					texture_dict[baseimage.path] = ti;
			}

			if(loading_set.TryGetValue(baseimage.filename, out var list))
			{
				callbacks = new List<CallbackInfo>(list);
				loading_set.Remove(baseimage.filename);
			}
		}

		if (callbacks != null)
		{
			var count = callbacks.Count;
			for(int i=0; i<count; ++i)
			{
				var item = callbacks[i];
				item.DoCallback(GetSpriteInfo(ti, item.src));
			}
		}
	}

	static Image LoadImageOrPlaceholder(string filename, string name)
	{
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
			GD.PushWarning($"[SpriteManager] image decode failed, using transparent placeholder: {filename}, err={err}");
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[SpriteManager] image load exception, using transparent placeholder: {filename}, error={ex.Message}");
		}
		return CreatePlaceholderImage();
	}

	static TextureInfo CreatePlaceholderTextureInfo(string name, string filename, string reason)
	{
		GD.PushWarning($"[SpriteManager] using transparent placeholder for {filename}: {reason}");
		return new TextureInfo(name, CreatePlaceholderImage());
	}

	static Image CreatePlaceholderImage()
	{
		Image img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		img.SetPixel(0, 0, new Godot.Color(0, 0, 0, 0));
		return img;
	}

	static TextureInfo CacheTextureInfo(string name, string filename, TextureInfo ti)
	{
		lock(dictLock)
		{
			if(texture_dict.TryGetValue(name, out var existing))
			{
				ti.Dispose();
				return existing;
			}
			texture_dict[name] = ti;
			if (!string.IsNullOrEmpty(filename) && filename != name && !texture_dict.ContainsKey(filename))
				texture_dict[filename] = ti;
			var fileOnly = System.IO.Path.GetFileName(filename);
			if (!string.IsNullOrEmpty(fileOnly) && fileOnly != name && !texture_dict.ContainsKey(fileOnly))
				texture_dict[fileOnly] = ti;
		}
		return ti;
	}

	static SpriteInfo GetSpriteInfo(TextureInfo textinfo, ASprite src)
	{
		if (textinfo == null)
			return null;
		return textinfo.GetSprite(src);
	}

	internal static void GivebackSpriteInfo(SpriteInfo info)
	{
		if(info == null)
			return;
		info.parent.Release();
	}

	public static void UpdateCleanup()
	{
		// Disabled: preloaded textures are accessed directly via BitmapTexture.texture
		// without going through SpriteManager.GetSprite(), so refcount never increases.
		// Auto-cleanup would dispose textures that are still in use.
		// Textures are only cleared explicitly via ForceClear() on game switch.
		return;
	}

	public static void UpdateOtherThreads()
	{
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

	internal static void ForceClear()
	{
		lock(dictLock)
		{
			foreach(var ti in texture_dict.Values)
			{
				ti.Dispose();
			}
			texture_dict.Clear();
		}
		GC.Collect();
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
		new Dictionary<string, List<CallbackInfo>>();
	static Dictionary<string, TextureInfo> texture_dict =
		new Dictionary<string, TextureInfo>();
	static readonly object dictLock = new object();
}
