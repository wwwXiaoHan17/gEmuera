using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MinorShift.Emuera.Sub
{
	internal static class Preload
	{
		static readonly ConcurrentDictionary<string, string[]> files = new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

		public static void Clear()
		{
			files.Clear();
		}

		/// <summary>
		/// M8: 移除单个已解码文本条目。调用方保证该文件后续不再需要从缓存读取；
		/// 未命中（或已被移除）时静默成功，EraStreamReader 会回退到直接磁盘读取。
		/// </summary>
		public static void Remove(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;
			files.TryRemove(path, out _);
		}

		/// <summary>
		/// M8: 按扩展名移除全部已解码文本条目（大小写不敏感，扩展名需带点，如 ".csv"）。
		/// 仅用于解析阶段结束后释放整会话驻留的文本缓存；漏删时缓存仍可命中，
		/// 行为等价于未移除，因此释放是纯内存优化，不影响语义。
		/// </summary>
		public static void RemoveByExtension(string extension)
		{
			if (string.IsNullOrEmpty(extension))
				return;
			foreach (string key in files.Keys)
			{
				if (Path.GetExtension(key).Equals(extension, StringComparison.OrdinalIgnoreCase))
					files.TryRemove(key, out _);
			}
		}

		public static bool TryGetFileLines(string path, out string[] lines)
		{
			return files.TryGetValue(path, out lines);
		}

		public static void Load(string path)
		{
			Load(path, true);
		}

		public static void Load(string path, bool includeErb)
		{
			if (string.IsNullOrEmpty(path))
				return;
			if (Directory.Exists(path))
			{
				var query = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
					.AsParallel()
					.WithDegreeOfParallelism(GetPreloadDegree());
				query.Where(file => IsPreloadTarget(file, includeErb)).ForAll(LoadFile);
			}
			else if (File.Exists(path) && IsPreloadTarget(path, includeErb))
			{
				LoadFile(path);
			}
		}

		public static void Load(IEnumerable<string> paths)
		{
			if (paths == null)
				return;
			foreach (string path in paths)
				Load(path);
		}

		static bool IsPreloadTarget(string path, bool includeErb)
		{
			string ext = Path.GetExtension(path);
			if (!includeErb && ext.Equals(".erb", StringComparison.OrdinalIgnoreCase))
				return false;
			return ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".erb", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".erh", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".erd", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".als", StringComparison.OrdinalIgnoreCase);
		}

		static int GetPreloadDegree()
		{
			// Android 外部存储在老设备上很容易被并行预读打出 I/O 和内存尖峰；
			// 保留后台预热收益，但把并发控制在手机可承受的范围内。
			if (global::Godot.OS.HasFeature("mobile"))
				return 2;
			return Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
		}

		static void LoadFile(string path)
		{
			try
			{
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					int length = (int)stream.Length;
					if (length == 0)
					{
						files[path] = new string[0];
						return;
					}
					byte[] buffer = new byte[length];
					int offset = 0;
					while (offset < buffer.Length)
					{
						int read = stream.Read(buffer, offset, buffer.Length - offset);
						if (read <= 0)
							break;
						offset += read;
					}
					using (var memory = new MemoryStream(buffer, 0, offset))
					using (var reader = new StreamReader(memory, Config.Encode, true))
					{
						var lines = new List<string>();
						string line;
						while ((line = reader.ReadLine()) != null)
							lines.Add(line);
						files[path] = lines.ToArray();
					}
				}
			}
			catch
			{
				// Keep startup tolerant: EraStreamReader.OpenOnCache falls back to direct Open.
			}
		}
	}
}
