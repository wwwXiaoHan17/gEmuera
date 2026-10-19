using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameProc
{
	internal sealed partial class Process
	{
		private readonly Dictionary<string, List<string>> lazyLoadingTable =
			new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HashSet<string>> lazyLoadingFileToFunctions =
			new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, long> lazyLoadingFilesTable =
			new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

		public readonly HashSet<string> LazyLoadingFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly HashSet<string> DeletedFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly HashSet<string> ChangedFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		int lazyLoadingRuntimeBatchCount = 0;
		int lazyLoadingRuntimeFileCount = 0;
		int lazyLoadingRuntimeElapsedMs = 0;
		int lazyLoadingRuntimeMaxMs = 0;
		string lazyLoadingRuntimeMaxFunction = "";

		static string cachedLazyLoadingSourceDir;
		static string cachedLazyLoadingWorkingDir;

		static string LazyLoadingDataFilePath { get { return Path.Combine(GetLazyLoadingWorkingDir(), "lazyloading.bin"); } }
		static string LazyLoadingFilesFilePath { get { return Path.Combine(GetLazyLoadingWorkingDir(), "lazyloadingfiles.bin"); } }
		static string LazyLoadingConfigFilePath { get { return Path.Combine(Program.ExeDir, "lazyloading.cfg"); } }

		const uint LazyMagicNumber = 0x4C415A59;
		//v4: lazyloadingfiles.bin 追加记录文件长度。部分压缩包解压/资源管理器复制会保留
		//归档内 mtime，导致"内容已更新但时间戳未变"，旧索引被误信后 #FUNCTION 文件既不进
		//索引也不在启动加载（eraTW 华扇口上 K43_K56_TSMIKO_WORKCHECK 解釈できない識別子 实证）。
		//长度是最廉价的第二信号：新增函数几乎必然改变文件大小。
		const uint LazyVersion = 4;
		//仍可读取的最低版本：snake 参考实现写 v3（无长度字段），双向共存时按 v3 语义降级。
		const uint LazyMinReadableVersion = 3;
		const int LazyRuntimeSlowLoadThresholdMs = 50;

		public enum LazyStatus
		{
			Disabled,
			NoLazy,
			BuildTable,
			Loaded,
			Error,
			UpdateTable,
		}

		public LazyStatus LazyCurrentLazyStatus = LazyStatus.Disabled;

		public void ResetLazyLoadingState()
		{
			lazyLoadingTable.Clear();
			lazyLoadingFileToFunctions.Clear();
			lazyLoadingFilesTable.Clear();
			LazyLoadingFiles.Clear();
			DeletedFiles.Clear();
			ChangedFiles.Clear();
			LazyCurrentLazyStatus = LazyStatus.Disabled;
		}

		/// <summary>
		/// Invalidates the static Android working-directory memo used by lazy
		/// loading.  The table itself is instance-owned, but the memo can survive
		/// a process-wide canary switch and otherwise point a new candidate at the
		/// previous game's fallback directory.
		/// </summary>
		internal static void ResetCanarySessionState()
		{
			cachedLazyLoadingSourceDir = null;
			cachedLazyLoadingWorkingDir = null;
		}

		public bool TryLazyLoadErb(string functionName)
		{
			if (LazyCurrentLazyStatus == LazyStatus.Disabled)
				return false;
			if (!lazyLoadingTable.TryGetValue(functionName, out List<string> files) || files.Count == 0)
				return false;

			List<string> filesToLoad = new List<string>(files);
			var loader = new ErbLoader(console, exm, this);
			int start = Environment.TickCount;
			if (loader.LoadErbsAsync(filesToLoad, labelDic, true).GetAwaiter().GetResult())
			{
				int elapsedMs = Environment.TickCount - start;
				RecordLazyLoadingRuntime(functionName, filesToLoad.Count, elapsedMs);
				LogSlowLazyLoadingRuntime(functionName, filesToLoad.Count, elapsedMs, true);
				RemoveLazyLoadingEntriesForFiles(filesToLoad);
				return true;
			}

			LogSlowLazyLoadingRuntime(functionName, filesToLoad.Count, Environment.TickCount - start, false);
			console.PrintSystemLine("LazyLoading: failed to load ERB for @" + functionName);
			return false;
		}

		public void ResetLazyLoadingRuntimeStats()
		{
			lazyLoadingRuntimeBatchCount = 0;
			lazyLoadingRuntimeFileCount = 0;
			lazyLoadingRuntimeElapsedMs = 0;
			lazyLoadingRuntimeMaxMs = 0;
			lazyLoadingRuntimeMaxFunction = "";
		}

		public void LogLazyLoadingRuntimeStats(string phase)
		{
			if (lazyLoadingRuntimeBatchCount == 0)
				return;
			GenericUtils.Info(
				$"[LOADSAVE] {phase} lazyload: batches={lazyLoadingRuntimeBatchCount}, files={lazyLoadingRuntimeFileCount}, total={lazyLoadingRuntimeElapsedMs}ms, max={lazyLoadingRuntimeMaxMs}ms @{lazyLoadingRuntimeMaxFunction}");
		}

		private void RecordLazyLoadingRuntime(string functionName, int fileCount, int elapsedMs)
		{
			lazyLoadingRuntimeBatchCount++;
			lazyLoadingRuntimeFileCount += fileCount;
			lazyLoadingRuntimeElapsedMs += elapsedMs;
			if (elapsedMs > lazyLoadingRuntimeMaxMs)
			{
				lazyLoadingRuntimeMaxMs = elapsedMs;
				lazyLoadingRuntimeMaxFunction = functionName;
			}
		}

		private static void LogSlowLazyLoadingRuntime(string functionName, int fileCount, int elapsedMs, bool success)
		{
			if (elapsedMs < LazyRuntimeSlowLoadThresholdMs)
				return;
			GenericUtils.Warn(EmueraLogCategory.Load, () =>
				$"[LOADSAVE] lazy ERB runtime load slow: function=@{functionName}, files={fileCount}, elapsed={elapsedMs}ms, success={success}, platform={Godot.OS.GetName()}");
		}

		public bool PreloadEventLoadLazyErbs()
		{
			if (LazyCurrentLazyStatus == LazyStatus.Disabled || LazyCurrentLazyStatus == LazyStatus.NoLazy)
				return false;
			if (lazyLoadingTable.Count == 0)
				return false;

			List<string> filesToLoad = lazyLoadingTable
				.Where(pair => IsEventLoadHotLazyLabel(pair.Key))
				.SelectMany(pair => pair.Value)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (filesToLoad.Count == 0)
				return false;

			int start = Environment.TickCount;
			var loader = new ErbLoader(console, exm, this);
			if (loader.LoadErbsAsync(filesToLoad, labelDic, true).GetAwaiter().GetResult())
			{
				int elapsed = Environment.TickCount - start;
				RecordLazyLoadingRuntime("EVENTLOAD_PRELOAD", filesToLoad.Count, elapsed);
				RemoveLazyLoadingEntriesForFiles(filesToLoad);
				GenericUtils.Info($"[LOADSAVE] EVENTLOAD lazy preload: {filesToLoad.Count} files, {elapsed}ms");
				return true;
			}

			console.PrintSystemLine("LazyLoading: failed to preload EVENTLOAD ERB files");
			return false;
		}

		private static bool IsEventLoadHotLazyLabel(string functionName)
		{
			if (string.IsNullOrEmpty(functionName))
				return false;
			if (!functionName.StartsWith("M_KOJO", StringComparison.OrdinalIgnoreCase))
				return false;
			return functionName.IndexOf("FLAGSETTING", StringComparison.OrdinalIgnoreCase) >= 0
				|| functionName.IndexOf("KOJO_VERSION", StringComparison.OrdinalIgnoreCase) >= 0
				|| functionName.IndexOf("CUSTOM_TALENT", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void RemoveLazyLoadingEntriesForFiles(IReadOnlyCollection<string> loadedFiles)
		{
			foreach (string file in loadedFiles)
			{
				string relative = RelativeErbPath(file);
				string normalizedFull = NormalizeFullPath(ErbPath(relative));
				if (lazyLoadingFileToFunctions.TryGetValue(relative, out HashSet<string> functions))
				{
					foreach (string functionName in functions)
					{
						if (!lazyLoadingTable.TryGetValue(functionName, out List<string> paths))
							continue;
						paths.RemoveAll(path => string.Equals(NormalizeFullPath(path), normalizedFull, StringComparison.OrdinalIgnoreCase));
						if (paths.Count == 0)
							lazyLoadingTable.Remove(functionName);
					}
					lazyLoadingFileToFunctions.Remove(relative);
				}
				else
				{
					foreach (string functionName in lazyLoadingTable.Keys.ToList())
					{
						List<string> paths = lazyLoadingTable[functionName];
						paths.RemoveAll(path => string.Equals(NormalizeFullPath(path), normalizedFull, StringComparison.OrdinalIgnoreCase));
						if (paths.Count == 0)
							lazyLoadingTable.Remove(functionName);
					}
				}
				LazyLoadingFiles.Remove(normalizedFull);
			}
		}

		private void AddLazyLoadingEntry(string functionName, string fileName)
		{
			// lazy 表需要同时支持“按函数找文件”和“按文件删除所有函数映射”。
			// 运行时命中一个角色 ERB 后，如果只保存 function -> files，就必须扫描整张表；
			// 上千角色文件在手机端会把一次按需加载放大成明显尖峰，所以这里维护反向索引。
			if (string.IsNullOrEmpty(functionName) || string.IsNullOrEmpty(fileName))
				return;

			string relative = NormalizeRelativePath(fileName);
			string fullPath = ErbPath(relative);
			if (!lazyLoadingTable.TryGetValue(functionName, out List<string> paths))
			{
				paths = new List<string>();
				lazyLoadingTable.Add(functionName, paths);
			}
			if (!ContainsIgnoreCase(paths, fullPath))
				paths.Add(fullPath);

			if (!lazyLoadingFileToFunctions.TryGetValue(relative, out HashSet<string> functions))
			{
				functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				lazyLoadingFileToFunctions.Add(relative, functions);
			}
			functions.Add(functionName);

			LazyLoadingFiles.Add(NormalizeFullPath(fullPath));
		}

		private static bool ContainsIgnoreCase(List<string> values, string value)
		{
			for (int i = 0; i < values.Count; i++)
			{
				if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		public bool IsFunctionInLazyLoadingTable(string functionName)
		{
			if (LazyCurrentLazyStatus == LazyStatus.Disabled)
				return false;
			return lazyLoadingTable.ContainsKey(functionName);
		}

		public bool IsLazyLoadingFile(string path)
		{
			return LazyLoadingFiles.Contains(NormalizeFullPath(path));
		}

		private List<string> LoadLazyLoadingFolders()
		{
			if (!uEmuera.Utils.FileExists(LazyLoadingConfigFilePath))
			{
				console.PrintSystemLine("LazyLoading: lazyloading.cfg not found; using normal full load");
				return null;
			}

			try
			{
				var result = new List<string>();
				foreach (string line in uEmuera.Utils.ReadAllLines(LazyLoadingConfigFilePath, Encoding.UTF8))
				{
					string value = NormalizeRelativePath(line.Trim());
					if (value.Length != 0 && !value.StartsWith(";"))
						result.Add(value);
				}
				return result;
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to read lazyloading.cfg: " + e.Message);
				return null;
			}
		}

		public void LoadLazyLoadingTable(List<KeyValuePair<string, string>> erbFiles)
		{
			ResetLazyLoadingState();

			if (!uEmuera.Utils.FileExists(LazyLoadingConfigFilePath))
				return;
			if (!File.Exists(LazyLoadingDataFilePath) || !File.Exists(LazyLoadingFilesFilePath))
			{
				RebuildLazyLoadingIndex(erbFiles);
				return;
			}

			try
			{
				HashSet<string> files = GetLazyFiles(erbFiles);

				//N6: 合并读档——单次流读取（整文件读入内存后解析），消除逐条 ReadString 反复跨越文件边界。
				//逐文件 stat（存在性 + 最后写入时间）并行化，结果按原文件顺序串行回填，
				//DeletedFiles/ChangedFiles/lazyLoadingFilesTable 的内容与顺序语义与串行完全一致。
				byte[] metaBytes = ReadWholeFile(LazyLoadingFilesFilePath);
				string[] names;
				long[] lastWrites;
				long[] lengths;
				bool hasLengths;
				using (var metaStream = new MemoryStream(metaBytes, false))
				using (var metaReader = new BinaryReader(metaStream, Encoding.UTF8))
				{
					uint magic = metaReader.ReadUInt32();
					uint version = metaReader.ReadUInt32();
					//v4 起带长度字段；v3（snake 参考实现写入）无长度字段，按时间戳语义降级读取。
					if (magic != LazyMagicNumber || version < LazyMinReadableVersion || version > LazyVersion)
					{
						RebuildLazyLoadingIndex(erbFiles);
						return;
					}
					hasLengths = version >= 4;

					int fileCount = metaReader.ReadInt32();
					names = new string[fileCount];
					lastWrites = new long[fileCount];
					lengths = hasLengths ? new long[fileCount] : null;
					for (int i = 0; i < fileCount; i++)
					{
						names[i] = NormalizeRelativePath(metaReader.ReadString());
						lastWrites[i] = metaReader.ReadInt64();
						if (hasLengths)
							lengths[i] = metaReader.ReadInt64();
					}
				}

				//0=文件缺失, 1=时间戳或长度已变(Changed), 2=未变
				byte[] statResults = new byte[names.Length];
				Exception firstError = null;
				Parallel.For(0, names.Length, GetLazyIndexParallelOptions(), i =>
				{
					try
					{
						string path = ErbPath(names[i]);
						if (!uEmuera.Utils.FileExists(path))
						{
							statResults[i] = 0;
							return;
						}
						if (GetLazyFileTimestamp(path) != lastWrites[i])
						{
							statResults[i] = 1;
							return;
						}
						//v4：mtime 相同再比对长度。压缩包解压保留归档 mtime 时，
						//内容更新仍会体现为长度变化，防止旧索引被误信。
						if (hasLengths)
						{
							var info = new FileInfo(path);
							if (info.Length != lengths[i])
							{
								statResults[i] = 1;
								return;
							}
						}
						statResults[i] = 2;
					}
					catch (Exception e)
					{
						//与原串行实现相同：stat 抛出的首个异常原样上抛，
						//由外层 catch 走“重建索引表”路径（消息与行为一致）。
						System.Threading.Interlocked.CompareExchange(ref firstError, e, null);
					}
				});
				if (firstError != null)
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstError).Throw();

				for (int i = 0; i < statResults.Length; i++)
				{
					switch (statResults[i])
					{
						case 0:
							DeletedFiles.Add(names[i]);
							break;
						case 1:
							ChangedFiles.Add(names[i]);
							break;
						default:
							lazyLoadingFilesTable[names[i]] = lastWrites[i];
							break;
					}
				}

				files.ExceptWith(lazyLoadingFilesTable.Keys);
				ChangedFiles.UnionWith(files);

				byte[] dataBytes = ReadWholeFile(LazyLoadingDataFilePath);
				using (var dataStream = new MemoryStream(dataBytes, false))
				using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8))
				{
					uint dataMagic = dataReader.ReadUInt32();
					uint dataVersion = dataReader.ReadUInt32();
					if (dataMagic != LazyMagicNumber || dataVersion < LazyMinReadableVersion || dataVersion > LazyVersion)
					{
						RebuildLazyLoadingIndex(erbFiles);
						return;
					}
					//旧版本索引（v3，无长度字段）完整性验证：仅凭 mtime 无法发现"内容已更新
					//但时间戳被归档解压保留"的文件（20260821 华扇口上 WORKCHECK 实证——
					//手机索引含 自用函数.ERB 旧条目，启动跳过、函数不可解析）。
					//v4 索引有长度第二信号，跳过验证；v3 索引对时间戳匹配的文件做标签扫描，
					//发现含 #FUNCTION/事件标签（本应被排除）的文件 → 从索引剔除并正常加载。
					if (!hasLengths)
						ValidateLazyIndexFilesForLegacyVersion();

					int funcCount = dataReader.ReadInt32();
					for (int i = 0; i < funcCount; i++)
					{
						string funcName = dataReader.ReadString();
						string fileName = NormalizeRelativePath(dataReader.ReadString());
						if (ChangedFiles.Contains(fileName) || DeletedFiles.Contains(fileName))
							continue;

						AddLazyLoadingEntry(funcName, fileName);
					}
				}
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to read index table: " + e.Message);
				RebuildLazyLoadingIndex(erbFiles);
				return;
			}

			//与原实现一致：无条件回填状态。
			LazyCurrentLazyStatus =
				ChangedFiles.Count != 0 || DeletedFiles.Count != 0 ? LazyStatus.UpdateTable : LazyStatus.Loaded;
		}

		private static ParallelOptions GetLazyIndexParallelOptions()
		{
			return new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 8)) };
		}

		private static byte[] ReadWholeFile(string path)
		{
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
			{
				long length = stream.Length;
				byte[] buffer = new byte[length];
				int offset = 0;
				while (offset < buffer.Length)
				{
					int read = stream.Read(buffer, offset, buffer.Length - offset);
					if (read <= 0)
						break;
					offset += read;
				}
				if (offset < buffer.Length)
					Array.Resize(ref buffer, offset);
				return buffer;
			}
		}

		/// <summary>
		/// v3 旧索引完整性验证：对时间戳匹配（将被跳过加载）的文件做标签扫描，
		/// 剔除含 #FUNCTION(S/F) 或事件标签的文件——这些文件本应被索引排除并在启动时
		/// 正常加载（eraTW 华扇口上 K43_K56_TSMIKO_WORKCHECK 解釈できない識別子 实证：
		/// 归档解压保留 mtime，旧索引把已更新为 #FUNCTION 的文件误留在跳过集）。
		/// 剔除仅作用于内存索引，不写盘：下次由任何引擎重建索引时自然按当前内容排除，
		/// 避免与 snake 双引擎互相重建形成每次切换全量加载的震荡。
		/// </summary>
		private void ValidateLazyIndexFilesForLegacyVersion()
		{
			if (lazyLoadingFilesTable.Count == 0)
				return;
			string[] relativeNames = lazyLoadingFilesTable.Keys.ToArray();
			bool[] needsFullLoad = new bool[relativeNames.Length];
			Exception firstError = null;
			Parallel.For(0, relativeNames.Length, GetLazyIndexParallelOptions(), i =>
			{
				try
				{
					needsFullLoad[i] = LazyIndexFileNeedsFullLoad(ErbPath(relativeNames[i]), relativeNames[i]);
				}
				catch (Exception e)
				{
					System.Threading.Interlocked.CompareExchange(ref firstError, e, null);
				}
			});
			if (firstError != null)
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstError).Throw();

			for (int i = 0; i < relativeNames.Length; i++)
			{
				if (!needsFullLoad[i])
					continue;
				//剔除：不在跳过集、不在不变表，标记为已变更 → 启动正常加载，
				//后续 SavePartialLazyLoadingList 会按 IsEvent||IsMethod 语义将其排除出新索引。
				LazyLoadingFiles.Remove(NormalizeFullPath(ErbPath(relativeNames[i])));
				lazyLoadingFilesTable.Remove(relativeNames[i]);
				ChangedFiles.Add(relativeNames[i]);
			}
			if (needsFullLoad.Any(v => v))
				console.PrintSystemLine("LazyLoading: legacy index validation dropped " + needsFullLoad.Count(v => v) + " files with #FUNCTION/event labels");
		}

		/// <summary>
		/// 与 TryScanLazyFileLabels 同源的完整性检查：文件内含 #FUNCTION(S/F) 或事件标签时
		/// 返回 true（该文件必须启动全量加载，不能跳过）。
		/// </summary>
		private static bool LazyIndexFileNeedsFullLoad(string path, string relativePath)
		{
			using (var reader = new EraStreamReader(Config.UseRenameFile && ParserMediator.RenameDic != null))
			{
				if (!reader.Open(path, relativePath))
					return false;
				bool hasCurrentLabel = false;
				StringStream line;
				while ((line = reader.ReadEnabledLine()) != null)
				{
					if (line.Current == '@')
					{
						hasCurrentLabel = false;
						string labelName = ReadLazyScanLabelName(line);
						if (string.IsNullOrEmpty(labelName))
							continue;
						if (IdentifierDictionary.IsEventLabelName(labelName))
							return true;
						hasCurrentLabel = true;
					}
					else if (line.Current == '#' && hasCurrentLabel)
					{
						if (IsLazyScanMethodToken(ReadLazyScanSharpToken(line)))
							return true;
					}
				}
			}
			return false;
		}

		private void RebuildLazyLoadingIndex(List<KeyValuePair<string, string>> erbFiles)
		{
			lazyLoadingTable.Clear();
			lazyLoadingFileToFunctions.Clear();
			lazyLoadingFilesTable.Clear();
			LazyLoadingFiles.Clear();
			DeletedFiles.Clear();
			ChangedFiles.Clear();

			if (IsAndroid() && TryBuildLazyLoadingTableFromLabels(erbFiles))
				return;

			LazyCurrentLazyStatus = LazyStatus.BuildTable;
		}

		private HashSet<string> GetLazyFiles(IEnumerable<KeyValuePair<string, string>> erbFiles)
		{
			List<string> paths = LoadLazyLoadingFolders();
			if (paths == null || paths.Count == 0)
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pair in erbFiles)
			{
				string relative = NormalizeRelativePath(pair.Key);
				foreach (string path in paths)
				{
					if (relative.StartsWith(path, StringComparison.OrdinalIgnoreCase))
					{
						files.Add(relative);
						break;
					}
				}
			}
			return files;
		}

		private bool TryBuildLazyLoadingTableFromLabels(List<KeyValuePair<string, string>> erbFiles)
		{
			HashSet<string> files = GetLazyFiles(erbFiles);
			if (files.Count == 0)
			{
				LazyCurrentLazyStatus = LazyStatus.NoLazy;
				return true;
			}

			var validLabels = new List<KeyValuePair<string, string>>();
			var validFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				foreach (string relative in files)
				{
					string path = ErbPath(relative);
					if (!uEmuera.Utils.FileExists(path))
						continue;
					// 标签扫描只建立 function -> ERB 文件映射；首次真正命中时仍走完整 ERB 解析、
					// setLabelsArg/checkScript，避免为了启动速度跳过原核心语义检查。
					if (!TryScanLazyFileLabels(path, relative, validLabels, out bool canLazyLoad))
						return false;
					if (canLazyLoad)
						validFiles.Add(relative);
				}

				if (validLabels.Count == 0 || validFiles.Count == 0)
				{
					LazyCurrentLazyStatus = LazyStatus.NoLazy;
					return true;
				}

				EnsureLazyLoadingWorkingDir();
				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
				using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8))
				{
					dataWriter.Write(LazyMagicNumber);
					dataWriter.Write(LazyVersion);
					dataWriter.Write(validLabels.Count);
					foreach (var label in validLabels)
					{
						dataWriter.Write(label.Key);
						dataWriter.Write(label.Value);
					}
				}

				WriteLazyFileMeta(validFiles);
				foreach (var label in validLabels)
				{
					AddLazyLoadingEntry(label.Key, label.Value);
					lazyLoadingFilesTable[label.Value] = GetLazyFileTimestamp(ErbPath(label.Value));
				}

				console.PrintSystemLine("LazyLoading: index table created from labels without full ERB load");
				LazyCurrentLazyStatus = LazyStatus.Loaded;
				return true;
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to create label-scan index table: " + e.Message);
				lazyLoadingTable.Clear();
				lazyLoadingFileToFunctions.Clear();
				lazyLoadingFilesTable.Clear();
				LazyLoadingFiles.Clear();
				return false;
			}
		}

		private bool TryScanLazyFileLabels(
			string path,
			string relativePath,
			List<KeyValuePair<string, string>> labels,
			out bool canLazyLoad)
		{
			canLazyLoad = true;
			var fileLabels = new List<string>();
			bool hasCurrentLabel = false;
			using (var reader = new EraStreamReader(Config.UseRenameFile && ParserMediator.RenameDic != null))
			{
				if (!reader.Open(path, relativePath))
					return false;

				StringStream line;
				while ((line = reader.ReadEnabledLine()) != null)
				{
					if (line.Current == '@')
					{
						string labelName = ReadLazyScanLabelName(line);
						hasCurrentLabel = false;
						if (string.IsNullOrEmpty(labelName))
							continue;
						if (IdentifierDictionary.IsEventLabelName(labelName))
						{
							canLazyLoad = false;
							break;
						}
						fileLabels.Add(labelName);
						hasCurrentLabel = true;
					}
					else if (line.Current == '#' && hasCurrentLabel)
					{
						string token = ReadLazyScanSharpToken(line);
						if (IsLazyScanMethodToken(token))
						{
							canLazyLoad = false;
							break;
						}
					}
				}
			}

			if (!canLazyLoad)
				return true;
			foreach (string label in fileLabels)
				labels.Add(new KeyValuePair<string, string>(label, NormalizeRelativePath(relativePath)));
			return true;
		}

		private static string ReadLazyScanLabelName(StringStream line)
		{
			// 这里仅用于 Android 首次建立 lazy 索引：只抽取 label 名和 #FUNCTION* 标记，
			// 真正命中 lazy 文件时仍会调用 ErbLoader 做完整解析、警告和语义检查。
			line.ShiftNext();
			string labelName = LexicalAnalyzer.ReadSingleIdentifier(line);
			if (Config.ICVariable && !string.IsNullOrEmpty(labelName))
				labelName = labelName.ToUpper();
			return labelName;
		}

		private static string ReadLazyScanSharpToken(StringStream line)
		{
			line.ShiftNext();
			string token = LexicalAnalyzer.ReadSingleIdentifier(line);
			if (Config.ICFunction && !string.IsNullOrEmpty(token))
				token = token.ToUpper();
			return token;
		}

		private static bool IsLazyScanMethodToken(string token)
		{
			return string.Equals(token, "FUNCTION", StringComparison.Ordinal)
				|| string.Equals(token, "FUNCTIONS", StringComparison.Ordinal)
				|| string.Equals(token, "FUNCTIONF", StringComparison.Ordinal);
		}

		public void SaveLazyLoadingList(List<FunctionLabelLine> labels, List<KeyValuePair<string, string>> erbFiles)
		{
			HashSet<string> files = GetLazyFiles(erbFiles);
			if (files.Count == 0)
			{
				LazyCurrentLazyStatus = LazyStatus.NoLazy;
				return;
			}

			foreach (FunctionLabelLine label in labels)
			{
				if (label.Position == null || !files.Contains(NormalizeRelativePath(label.Position.Filename)))
					continue;
				if (label.IsEvent || label.IsMethod)
					files.Remove(NormalizeRelativePath(label.Position.Filename));
			}

			try
			{
				EnsureLazyLoadingWorkingDir();
				var validLabels = labels
					.Where(label => label.Position != null && files.Contains(NormalizeRelativePath(label.Position.Filename)))
					.ToList();
				var metaFiles = new HashSet<string>(
					validLabels.Select(label => NormalizeRelativePath(label.Position.Filename)),
					StringComparer.OrdinalIgnoreCase);

				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
				using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8))
				{
					dataWriter.Write(LazyMagicNumber);
					dataWriter.Write(LazyVersion);
					dataWriter.Write(validLabels.Count);
					foreach (FunctionLabelLine label in validLabels)
					{
						dataWriter.Write(label.LabelName);
						dataWriter.Write(NormalizeRelativePath(label.Position.Filename));
					}
				}

				WriteLazyFileMeta(metaFiles);
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to save index table: " + e.Message);
				LazyCurrentLazyStatus = LazyStatus.Error;
				return;
			}

			LazyCurrentLazyStatus = LazyStatus.Loaded;
		}

		public bool SavePartialLazyLoadingList(List<FunctionLabelLine> labels)
		{
			var validLabelsToAppend = new List<FunctionLabelLine>();
			var labelFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string file in ChangedFiles.ToList())
			{
				bool valid = true;
				bool anyLabel = false;
				foreach (FunctionLabelLine label in labels)
				{
					if (label.Position == null || !string.Equals(NormalizeRelativePath(label.Position.Filename), file, StringComparison.OrdinalIgnoreCase))
						continue;

					anyLabel = true;
					if (label.IsEvent || label.IsMethod)
					{
						valid = false;
						break;
					}

					validLabelsToAppend.Add(label);
					labelFiles.Add(file);
				}

				if (!valid || !anyLabel)
					ChangedFiles.Remove(file);
			}

			if (ChangedFiles.Count == 0 && DeletedFiles.Count == 0)
			{
				LazyCurrentLazyStatus = LazyStatus.Loaded;
				return false;
			}

			try
			{
				var unchangedLabels = lazyLoadingTable
					.SelectMany(item => item.Value.Select(path => new KeyValuePair<string, string>(item.Key, RelativeErbPath(path))))
					.ToList();

				EnsureLazyLoadingWorkingDir();
				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
				using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8))
				{
					dataWriter.Write(LazyMagicNumber);
					dataWriter.Write(LazyVersion);
					dataWriter.Write(validLabelsToAppend.Count + unchangedLabels.Count);

					foreach (FunctionLabelLine label in validLabelsToAppend)
					{
						dataWriter.Write(label.LabelName);
						dataWriter.Write(NormalizeRelativePath(label.Position.Filename));
					}

					foreach (var item in unchangedLabels)
					{
						dataWriter.Write(item.Key);
						dataWriter.Write(item.Value);
					}
				}

				var metaFiles = new HashSet<string>(lazyLoadingFilesTable.Keys, StringComparer.OrdinalIgnoreCase);
				metaFiles.UnionWith(labelFiles);
				WriteLazyFileMeta(metaFiles);
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to update index table: " + e.Message);
				LazyCurrentLazyStatus = LazyStatus.Error;
				return false;
			}

			LazyCurrentLazyStatus = LazyStatus.Loaded;
			return true;
		}

		private static void WriteLazyFileMeta(IEnumerable<string> files)
		{
			var list = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			//v4 起每条目附带文件长度，写前需逐文件 stat（时间戳 + 长度）。
			//与读取侧相同地并行化 stat，结果按原顺序串行回填，写出内容与串行一致；
			//stat 异常先于建文件抛出，不再留下半写的索引。
			long[] timestamps = new long[list.Count];
			long[] lengths = new long[list.Count];
			Exception firstError = null;
			Parallel.For(0, list.Count, GetLazyIndexParallelOptions(), i =>
			{
				try
				{
					string fullPath = ErbPath(list[i]);
					timestamps[i] = GetLazyFileTimestamp(fullPath);
					lengths[i] = new FileInfo(fullPath).Length;
				}
				catch (Exception e)
				{
					System.Threading.Interlocked.CompareExchange(ref firstError, e, null);
				}
			});
			if (firstError != null)
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstError).Throw();

			using (var metaStream = new FileStream(LazyLoadingFilesFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
			using (var metaWriter = new BinaryWriter(metaStream, Encoding.UTF8))
			{
				metaWriter.Write(LazyMagicNumber);
				metaWriter.Write(LazyVersion);
				metaWriter.Write(list.Count);
				for (int i = 0; i < list.Count; i++)
				{
					metaWriter.Write(NormalizeRelativePath(list[i]));
					metaWriter.Write(timestamps[i]);
					//v4：长度字段。mtime 被归档解压保留时，长度是检测内容变更的第二信号。
					metaWriter.Write(lengths[i]);
				}
			}
		}

		private static string ErbPath(string relativePath)
		{
			return Path.Combine(Program.ErbDir, NormalizeRelativePath(relativePath));
		}

		private static string GetLazyLoadingWorkingDir()
		{
			string gameDir = !string.IsNullOrEmpty(Program.WorkingDir) ? Program.WorkingDir : Program.ExeDir;
			gameDir = uEmuera.Utils.NormalizePath(gameDir);

			if (string.Equals(cachedLazyLoadingSourceDir, gameDir, StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(cachedLazyLoadingWorkingDir))
				return cachedLazyLoadingWorkingDir;

			if (!IsAndroid())
				return CacheLazyLoadingWorkingDir(gameDir, gameDir);

			if (CanWriteLazyLoadingIndexTo(gameDir))
				return CacheLazyLoadingWorkingDir(gameDir, gameDir);

			string userRoot = Godot.OS.GetUserDataDir();
			if (string.IsNullOrEmpty(userRoot))
				userRoot = Godot.ProjectSettings.GlobalizePath("user://");
			string fallbackDir = Path.Combine(userRoot, "lazyloading", StablePathId(gameDir));
			return CacheLazyLoadingWorkingDir(gameDir, uEmuera.Utils.NormalizePath(fallbackDir));
		}

		private static string CacheLazyLoadingWorkingDir(string sourceDir, string workingDir)
		{
			cachedLazyLoadingSourceDir = sourceDir;
			cachedLazyLoadingWorkingDir = workingDir;
			return cachedLazyLoadingWorkingDir;
		}

		private static void EnsureLazyLoadingWorkingDir()
		{
			string dir = GetLazyLoadingWorkingDir();
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);
		}

		private static bool IsAndroid()
		{
			return string.Equals(Godot.OS.GetName(), "Android", StringComparison.OrdinalIgnoreCase);
		}

		private static bool CanWriteLazyLoadingIndexTo(string path)
		{
			if (string.IsNullOrEmpty(path))
				return false;
			string normalized = uEmuera.Utils.NormalizePath(path);
			if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
				return false;
			if (!Directory.Exists(normalized))
				return false;

			string probePath = Path.Combine(normalized, ".lazyloading_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
			try
			{
				using (var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1))
					stream.WriteByte(0);
				File.Delete(probePath);
				return true;
			}
			catch
			{
				try
				{
					if (File.Exists(probePath))
						File.Delete(probePath);
				}
				catch
				{
				}
				return false;
			}
		}

		private static string StablePathId(string path)
		{
			unchecked
			{
				ulong hash = 14695981039346656037UL;
				string value = (path ?? "").ToUpperInvariant();
				for (int i = 0; i < value.Length; i++)
				{
					hash ^= value[i];
					hash *= 1099511628211UL;
				}
				return hash.ToString("X16");
			}
		}

		private static long GetLazyFileTimestamp(string path)
		{
			if (IsAndroid())
				return uEmuera.Utils.GetLastWriteTimeKey(path);
			return File.GetLastWriteTime(path).ToFileTimeUtc();
		}

		private static string RelativeErbPath(string path)
		{
			string fullPath = Path.GetFullPath(path);
			string erbDir = Path.GetFullPath(Program.ErbDir);
			if (fullPath.StartsWith(erbDir, StringComparison.OrdinalIgnoreCase))
				return NormalizeRelativePath(fullPath.Substring(erbDir.Length));
			return NormalizeRelativePath(path);
		}

		private static string NormalizeRelativePath(string path)
		{
			return (path ?? "").Trim().Replace('\\', '/').TrimStart('/');
		}

		private static string NormalizeFullPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return "";
			return Path.GetFullPath(path).Replace('\\', '/');
		}
	}
}
