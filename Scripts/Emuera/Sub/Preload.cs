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

		public static bool TryGetFileLines(string path, out string[] lines)
		{
			return files.TryGetValue(path, out lines);
		}

		public static void Load(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;
			if (Directory.Exists(path))
			{
				var query = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
					.AsParallel();
				int degree = GetPreloadDegree();
				if (degree > 0)
					query = query.WithDegreeOfParallelism(degree);
				query.Where(IsPreloadTarget).ForAll(LoadFile);
			}
			else if (File.Exists(path) && IsPreloadTarget(path))
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

		static bool IsPreloadTarget(string path)
		{
			string ext = Path.GetExtension(path);
			return ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".erb", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".erh", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".erd", StringComparison.OrdinalIgnoreCase)
				|| ext.Equals(".als", StringComparison.OrdinalIgnoreCase);
		}

		static int GetPreloadDegree()
		{
			int selected = global::FirstWindow.SelectedPreloadThreads;
			if (selected <= 0)
				return 0;
			return Math.Max(4, selected);
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
