using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace MinorShift.Emuera.GameProc
{
	/// <summary>
	/// 解析缓存批量存储（Phase 1.5）：全部条目合并为单一 bundle.bin，替代 1800+ 散文件，
	/// 消除每文件 open/create/move 的 IO 惩罚（实测散文件首启写 +4~11.6s、命中读多付 ~2s）。
	///
	/// 生命周期：Load（首次取用时一次性读入内存索引）→ 会话中 TryGet（内存字节）/
	/// Store（pending）→ Flush（锚点处全量重写：旧条目全保留 + pending 合入；tmp+Replace 原子）。
	/// 键仍由 ErbParseCache.BuildCacheFilePath 的 SHA-256 截断逻辑生成（沿用同一键空间，
	/// 由 ErbLoader 经 ErbParseCache.BuildCacheKey 统一产出，避免两套键算法漂移）。
	///
	/// 膨胀策略：旧条目全保留（lazy 文件的条目本会话未 touch 但运行期要用，不能按 touch 清理）；
	/// bundle 超过 <see cref="MaxBundleBytes"/> 时 Flush 退化为只写 pending+本会话 touch 过的键（重建）。
	/// 所有方法线程安全：启动线程与运行期懒加载（VM 线程）都会进出。
	/// </summary>
	internal static class ErbParseBundle
	{
		private const string BundleFileName = "bundle.bin";
		// 重建阈值：正常增长（游戏改文件）远低于此；超限说明大量陈旧条目累积（如反复换版本），重建优于膨胀
		private const long MaxBundleBytes = 256L * 1024 * 1024;

		private static readonly object Gate = new object();
		private static Dictionary<string, byte[]> loaded;   // 已落盘条目（键=32hex）
		private static readonly Dictionary<string, byte[]> Pending = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		private static readonly HashSet<string> TouchedKeys = new HashSet<string>(StringComparer.Ordinal);
		private static string bundlePath;
		private static bool loadFailed; // bundle 不可读（IO/格式）→ 本进程禁用缓存读（写仍可重建）

		/// <summary>bundle 全路径；目录不存在时创建。失败返回 null（缓存禁用）。</summary>
		public static string EnsureBundlePath(string cacheDir)
		{
			try
			{
				Directory.CreateDirectory(cacheDir);
				return Path.Combine(cacheDir, BundleFileName);
			}
			catch
			{
				return null;
			}
		}

		/// <summary>取条目字节（命中则同时记录 touch，供超限重建时保留）。bundle 首次访问时惰性整读。</summary>
		public static byte[] TryGet(string path, string key)
		{
			if (string.IsNullOrEmpty(key))
				return null;
			lock (Gate)
			{
				if (!EnsureLoaded(path))
					return null;
				if (loaded.TryGetValue(key, out byte[] bytes))
				{
					TouchedKeys.Add(key);
					return bytes;
				}
				return null;
			}
		}

		/// <summary>暂存新条目（内存 pending；由 <see cref="Flush"/> 在锚点落盘）。</summary>
		public static void Store(string path, string key, byte[] entryBytes)
		{
			if (string.IsNullOrEmpty(key) || entryBytes == null || entryBytes.Length == 0)
				return;
			lock (Gate)
			{
				if (!EnsureLoaded(path))
					return; // 读失败（权限/损坏）环境下写也放弃——保持"缓存整体可用或整体禁用"
				Pending[key] = entryBytes;
				TouchedKeys.Add(key);
			}
		}

		/// <summary>
		/// 锚点落盘：启动期全部 ERB 处理完、以及每批运行期懒加载完成后调用。
		/// 无 pending 且 bundle 未损坏时是零开销；失败静默（下次 Flush 重试）。
		/// </summary>
		public static void Flush(string path)
		{
			if (string.IsNullOrEmpty(path))
				return;
			lock (Gate)
			{
				if (!EnsureLoaded(path))
					return;
				if (Pending.Count == 0 && !loadFailed)
					return; // 无新条目且磁盘状态健康：零写
				try
				{
					bool rebuildOnly = loadFailed || EstimateBundleBytes() > MaxBundleBytes;
					string tmp = path + ".tmp";
					using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
					using (var w = new BinaryWriter(fs))
					{
					 WriteHeaderAndEntries(w, rebuildOnly);
					}
					if (File.Exists(path))
						File.Replace(tmp, path, null);
					else
						File.Move(tmp, path);
					loadFailed = false;
					// 已写条目并入 loaded 记账：本进程后续 Flush 的 survivors 必须含它们
					//（第二个轮次 bug：首建轮 loaded 为空，运行期懒加载的第二次 Flush 把
					// 首写的 1820 条覆盖丢失）。内存驻留 = 本进程 touch 的条目字节，
					// 与行对象图同量级，会话结束随静态一起释放。
					foreach (var kv in Pending)
						loaded[kv.Key] = kv.Value;
					Pending.Clear();
				}
				catch
				{
					try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { }
				}
			}
		}

		private static void WriteHeaderAndEntries(BinaryWriter w, bool rebuildOnly)
		{
			// count 必须与实际写入条目数一致（首轮 bug：CountSurvivors 数 loaded（首建时空）导致
			// header 写 0 却跟随 1820 条数据，下一进程按 count=0 读取全 miss）
			int count = Pending.Count;
			if (!rebuildOnly)
			{
				foreach (var kv in loaded)
					if (!Pending.ContainsKey(kv.Key))
						count++;
			}
			w.Write((byte)'G'); w.Write((byte)'P'); w.Write((byte)'B'); w.Write((byte)'4');
			w.Write(count);
			if (!rebuildOnly)
			{
				foreach (var kv in loaded)
				{
					if (Pending.ContainsKey(kv.Key))
						continue; // pending 覆盖同键旧条目，避免双写
					WriteEntry(w, kv.Key, kv.Value);
				}
			}
			foreach (var kv in Pending)
				WriteEntry(w, kv.Key, kv.Value);
		}

		private static void WriteEntry(BinaryWriter w, string key, byte[] bytes)
		{
			w.Write(key);
			w.Write(bytes.Length);
			w.Write(bytes);
		}

		private static long EstimateBundleBytes()
		{
			long total = 16;
			foreach (var kv in loaded)
				total += kv.Key.Length + 4 + kv.Value.Length;
			foreach (var kv in Pending)
				if (!loaded.ContainsKey(kv.Key))
					total += kv.Key.Length + 4 + kv.Value.Length;
			return total;
		}

		/// <summary>bundle 惰性整读（一次）；损坏/IO 失败置 loadFailed 并返回 false。</summary>
		private static bool EnsureLoaded(string path)
		{
			if (loaded != null)
				return true;
			bundlePath = path;
			loaded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			try
			{
				if (!File.Exists(path))
					return true; // 空 bundle：loaded 已置，后续 Store→Flush 首建
				using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
				using (var r = new BinaryReader(fs))
				{
					if (fs.Length < 8 || r.ReadByte() != (byte)'G' || r.ReadByte() != (byte)'P' || r.ReadByte() != (byte)'B' || r.ReadByte() != (byte)'4')
						throw new IOException("bad bundle magic");
					int count = r.ReadInt32();
					if (count < 0 || count > 500_000)
						throw new IOException("bad bundle count");
					for (int i = 0; i < count; i++)
					{
						string key = r.ReadString();
						int len = r.ReadInt32();
						if (key == null || key.Length != 32 || len < 0 || len > 64 * 1024 * 1024 || fs.Position + len > fs.Length)
							throw new IOException("bad bundle entry");
						loaded[key] = r.ReadBytes(len);
					}
				}
				return true;
			}
			catch
			{
				loaded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
				loadFailed = true; // 损坏：读全禁；Flush 走重建（pending-only）
				return false;
			}
		}
	}
}
