using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameProc
{
	internal sealed partial class Process
	{
		private readonly Dictionary<string, List<string>> lazyLoadingTable =
			new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, long> lazyLoadingFilesTable =
			new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

		public readonly HashSet<string> LazyLoadingFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly HashSet<string> DeletedFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public readonly HashSet<string> ChangedFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		static string cachedLazyLoadingSourceDir;
		static string cachedLazyLoadingWorkingDir;

		static string LazyLoadingDataFilePath { get { return Path.Combine(GetLazyLoadingWorkingDir(), "lazyloading.bin"); } }
		static string LazyLoadingFilesFilePath { get { return Path.Combine(GetLazyLoadingWorkingDir(), "lazyloadingfiles.bin"); } }
		static string LazyLoadingConfigFilePath { get { return Path.Combine(Program.ExeDir, "lazyloading.cfg"); } }

		const uint LazyMagicNumber = 0x4C415A59;
		const uint LazyVersion = 1;

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

		public bool TryLazyLoadErb(string functionName)
		{
			if (LazyCurrentLazyStatus == LazyStatus.Disabled)
				return false;
			if (!lazyLoadingTable.TryGetValue(functionName, out List<string> files))
				return false;

			var loader = new ErbLoader(console, exm, this);
			if (loader.loadErbs(files, labelDic))
				return labelDic.GetNonEventLabel(functionName) != null;

			console.PrintSystemLine("LazyLoading: failed to load ERB for @" + functionName);
			return false;
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
			lazyLoadingTable.Clear();
			lazyLoadingFilesTable.Clear();
			LazyLoadingFiles.Clear();
			DeletedFiles.Clear();
			ChangedFiles.Clear();
			LazyCurrentLazyStatus = LazyStatus.Disabled;

			if (!uEmuera.Utils.FileExists(LazyLoadingConfigFilePath))
				return;
			if (!File.Exists(LazyLoadingDataFilePath) || !File.Exists(LazyLoadingFilesFilePath))
			{
				if (IsAndroid() && TryBuildMobileLazyLoadingTable(erbFiles))
					return;
				LazyCurrentLazyStatus = LazyStatus.BuildTable;
				return;
			}

			try
			{
				HashSet<string> files = GetLazyFiles(erbFiles);

				using (var metaStream = new FileStream(LazyLoadingFilesFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
				using (var metaReader = new BinaryReader(metaStream, Encoding.UTF8))
				{
					if (metaReader.ReadUInt32() != LazyMagicNumber || metaReader.ReadUInt32() != LazyVersion)
					{
						LazyCurrentLazyStatus = LazyStatus.BuildTable;
						return;
					}

					int fileCount = metaReader.ReadInt32();
					for (int i = 0; i < fileCount; i++)
					{
						string name = NormalizeRelativePath(metaReader.ReadString());
						long lastWrite = metaReader.ReadInt64();
						string path = ErbPath(name);

						if (!uEmuera.Utils.FileExists(path))
						{
							DeletedFiles.Add(name);
							continue;
						}

						if (GetLazyFileTimestamp(path) != lastWrite)
							ChangedFiles.Add(name);
						else
							lazyLoadingFilesTable[name] = lastWrite;
					}
				}

				files.ExceptWith(lazyLoadingFilesTable.Keys);
				ChangedFiles.UnionWith(files);

				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
				using (var dataReader = new BinaryReader(dataStream, Encoding.UTF8))
				{
					if (dataReader.ReadUInt32() != LazyMagicNumber || dataReader.ReadUInt32() != LazyVersion)
					{
						LazyCurrentLazyStatus = LazyStatus.BuildTable;
						return;
					}

					int funcCount = dataReader.ReadInt32();
					for (int i = 0; i < funcCount; i++)
					{
						string funcName = dataReader.ReadString();
						string fileName = NormalizeRelativePath(dataReader.ReadString());
						if (ChangedFiles.Contains(fileName) || DeletedFiles.Contains(fileName))
							continue;

						if (!lazyLoadingTable.TryGetValue(funcName, out List<string> paths))
						{
							paths = new List<string>();
							lazyLoadingTable.Add(funcName, paths);
						}

						string path = ErbPath(fileName);
						paths.Add(path);
						LazyLoadingFiles.Add(NormalizeFullPath(path));
					}
				}
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to read index table: " + e.Message);
				LazyCurrentLazyStatus = LazyStatus.BuildTable;
				return;
			}

			LazyCurrentLazyStatus =
				ChangedFiles.Count != 0 || DeletedFiles.Count != 0 ? LazyStatus.UpdateTable : LazyStatus.Loaded;
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

		private bool TryBuildMobileLazyLoadingTable(List<KeyValuePair<string, string>> erbFiles)
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
					if (!lazyLoadingTable.TryGetValue(label.Key, out List<string> paths))
					{
						paths = new List<string>();
						lazyLoadingTable.Add(label.Key, paths);
					}

					string path = ErbPath(label.Value);
					paths.Add(path);
					LazyLoadingFiles.Add(NormalizeFullPath(path));
					lazyLoadingFilesTable[label.Value] = GetLazyFileTimestamp(path);
				}

				console.PrintSystemLine("LazyLoading: mobile index table created without full ERB load");
				LazyCurrentLazyStatus = LazyStatus.Loaded;
				return true;
			}
			catch (Exception e)
			{
				console.PrintSystemLine("LazyLoading: failed to create mobile index table: " + e.Message);
				lazyLoadingTable.Clear();
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
			var onlyEvents = new List<string>();
			FunctionLabelLine currentLabel = null;
			using (var reader = new EraStreamReader(Config.UseRenameFile && ParserMediator.RenameDic != null))
			{
				if (!reader.Open(path, relativePath))
					return false;

				StringStream line;
				while ((line = reader.ReadEnabledLine()) != null)
				{
					var position = new ScriptPosition(reader.Filename, reader.LineNo);
					if (line.Current == '@')
					{
						var parsed = LogicalLineParser.ParseLabelLine(line, position, console);
						currentLabel = parsed as FunctionLabelLine;
						if (currentLabel == null || currentLabel is InvalidLabelLine)
							continue;
						if (currentLabel.IsEvent)
						{
							canLazyLoad = false;
							break;
						}
						fileLabels.Add(currentLabel.LabelName);
					}
					else if (line.Current == '#' && currentLabel != null)
					{
						LogicalLineParser.ParseSharpLine(currentLabel, line, position, onlyEvents);
						if (currentLabel.IsMethod || currentLabel.IsEvent)
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
				if (label.IsMethod || label.IsEvent)
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
					if (label.IsMethod || label.IsEvent)
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
				EnsureLazyLoadingWorkingDir();
				using (var dataStream = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
				using (var dataWriter = new BinaryWriter(dataStream, Encoding.UTF8))
				{
					int unchangedCount = lazyLoadingTable.Sum(item => item.Value.Count);
					dataWriter.Write(LazyMagicNumber);
					dataWriter.Write(LazyVersion);
					dataWriter.Write(validLabelsToAppend.Count + unchangedCount);

					foreach (FunctionLabelLine label in validLabelsToAppend)
					{
						dataWriter.Write(label.LabelName);
						dataWriter.Write(NormalizeRelativePath(label.Position.Filename));
					}

					foreach (var item in lazyLoadingTable)
					{
						foreach (string path in item.Value)
						{
							dataWriter.Write(item.Key);
							dataWriter.Write(RelativeErbPath(path));
						}
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
			using (var metaStream = new FileStream(LazyLoadingFilesFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
			using (var metaWriter = new BinaryWriter(metaStream, Encoding.UTF8))
			{
				var list = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					metaWriter.Write(LazyMagicNumber);
					metaWriter.Write(LazyVersion);
					metaWriter.Write(list.Count);
					foreach (string name in list)
					{
						metaWriter.Write(NormalizeRelativePath(name));
						metaWriter.Write(GetLazyFileTimestamp(ErbPath(name)));
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
