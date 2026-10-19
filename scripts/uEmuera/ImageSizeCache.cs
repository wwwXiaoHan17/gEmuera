using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace uEmuera.Drawing
{
	/// <summary>
	/// 图片尺寸磁盘缓存：BitmapTexture.TryReadImageSize 每次打开文件读头，30k 张独立图片
	/// 首次全量引用时是 30k 次串行文件打开（Android 外部存储 I/O 是加载卡顿主因之一）。
	/// 本类把 (路径, mtime) → (宽, 高) 缓存到 user://data/imagesize_cache.txt，二次启动命中
	/// 则只做一次 stat（FileAccess.GetModifiedTime）校验，跳过读头。mtime 不匹配/缓存缺失时
	/// 安全回退读头，避免尺寸 stale 造成显示漂移。
	/// 线程安全：惰性加载 + 全 lock 保护；Save 失败静默（下次重新读头）。
	/// </summary>
	internal static class ImageSizeCache
	{
		struct Entry
		{
			public int Width;
			public int Height;
			public ulong ModifiedSec;
		}

		static readonly Dictionary<string, Entry> cache = new(StringComparer.OrdinalIgnoreCase);
		static readonly object cacheLock = new();
		static string filePath = "";
		static bool loaded;
		static bool dirty;

		static void EnsureLoaded()
		{
			if (loaded)
				return;
			loaded = true;
			try
			{
				filePath = Path.Combine(OS.GetUserDataDir(), "data", "imagesize_cache.txt");
				if (!File.Exists(filePath))
					return;
				foreach (string line in File.ReadAllLines(filePath))
				{
					if (string.IsNullOrEmpty(line))
						continue;
					// path|w|h|mtimeSec —— 图片路径通常不含 '|'
					int p1 = line.IndexOf('|');
					if (p1 <= 0)
						continue;
					int p2 = line.IndexOf('|', p1 + 1);
					int p3 = line.IndexOf('|', p2 + 1);
					if (p2 <= p1 || p3 <= p2)
						continue;
					string path = line.Substring(0, p1);
					if (path.Length == 0
						|| !int.TryParse(line.Substring(p1 + 1, p2 - p1 - 1), out int w)
						|| w <= 0
						|| !int.TryParse(line.Substring(p2 + 1, p3 - p2 - 1), out int h)
						|| h <= 0
						|| !ulong.TryParse(line.Substring(p3 + 1), out ulong ms))
						continue;
					cache[path] = new Entry { Width = w, Height = h, ModifiedSec = ms };
				}
			}
			catch
			{
				// 缓存不可用则安全回退读头
			}
		}

		/// <summary>命中且 mtime 匹配时返回 true；否则返回 false 触发读头。线程安全。</summary>
		public static bool TryGet(string path, out int width, out int height)
		{
			width = 0;
			height = 0;
			if (string.IsNullOrEmpty(path))
				return false;
			lock (cacheLock)
			{
				EnsureLoaded();
				if (!cache.TryGetValue(path, out Entry entry))
					return false;
				try
				{
					if (Godot.FileAccess.GetModifiedTime(path) != entry.ModifiedSec)
						return false; // 文件已变更，读头重建
				}
				catch
				{
					return false;
				}
				width = entry.Width;
				height = entry.Height;
				return true;
			}
		}

		/// <summary>读头成功后记录尺寸与 mtime。线程安全。</summary>
		public static void Set(string path, int width, int height)
		{
			if (string.IsNullOrEmpty(path) || width <= 0 || height <= 0)
				return;
			lock (cacheLock)
			{
				EnsureLoaded();
				ulong ms;
				try
				{
					ms = Godot.FileAccess.GetModifiedTime(path);
				}
				catch
				{
					return;
				}
				cache[path] = new Entry { Width = width, Height = height, ModifiedSec = ms };
				dirty = true;
			}
		}

		/// <summary>把脏缓存写盘。启动完成/退出时调用；失败静默（下次重新读头）。</summary>
		public static void Save()
		{
			lock (cacheLock)
			{
				if (!loaded || !dirty || cache.Count == 0)
					return;
				try
				{
					string dir = Path.GetDirectoryName(filePath);
					if (!string.IsNullOrEmpty(dir))
						Directory.CreateDirectory(dir);
					using var writer = new StreamWriter(filePath, false, new System.Text.UTF8Encoding(false));
					foreach (KeyValuePair<string, Entry> kv in cache)
						writer.WriteLine(kv.Key + "|" + kv.Value.Width + "|" + kv.Value.Height + "|" + kv.Value.ModifiedSec);
					dirty = false;
				}
				catch
				{
					// 写盘失败静默：下次重新读头即可
				}
			}
		}
	}
}
