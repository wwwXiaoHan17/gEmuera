using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace uEmuera
{
	public static class Logger
	{
		public delegate void StructuredLogSink(global::EmueraLogLevel level, global::EmueraLogCategory category,
			object content, Func<string> messageFactory, string member, string file, int line);

		[Conditional("DEBUG")]
		[Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
		public static void Debug(object content,
			global::EmueraLogCategory category = global::EmueraLogCategory.General,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Debug, category, content, null, member, file, line);
		}

		[Conditional("DEBUG")]
		[Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
		public static void Debug(global::EmueraLogCategory category, Func<string> messageFactory,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Debug, category, null, messageFactory, member, file, line);
		}

		[Conditional("DEBUG")]
		[Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
		public static void Info(object content,
			global::EmueraLogCategory category = global::EmueraLogCategory.General,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Info, category, content, null, member, file, line);
		}

		[Conditional("DEBUG")]
		[Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
		public static void Info(global::EmueraLogCategory category, Func<string> messageFactory,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Info, category, null, messageFactory, member, file, line);
		}

		[Conditional("DEBUG")]
		[Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
		public static void Warn(object content,
			global::EmueraLogCategory category = global::EmueraLogCategory.General,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Warn, category, content, null, member, file, line);
		}

		[Conditional("DEBUG")]
		[Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
		public static void Warn(global::EmueraLogCategory category, Func<string> messageFactory,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Warn, category, null, messageFactory, member, file, line);
		}

		public static void Error(object content,
			global::EmueraLogCategory category = global::EmueraLogCategory.General,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Error, category, content, null, member, file, line);
		}

		public static void Error(global::EmueraLogCategory category, Func<string> messageFactory,
			[CallerMemberName] string member = "",
			[CallerFilePath] string file = "",
			[CallerLineNumber] int line = 0)
		{
			Emit(global::EmueraLogLevel.Error, category, null, messageFactory, member, file, line);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsEnabled(global::EmueraLogLevel level,
			global::EmueraLogCategory category = global::EmueraLogCategory.General)
		{
			return isEnabled == null || isEnabled(level, category);
		}

		static void Emit(global::EmueraLogLevel level, global::EmueraLogCategory category, object content,
			Func<string> messageFactory, string member, string file, int line)
		{
			if(sink != null)
			{
				sink(level, category, content, messageFactory, member, file, line);
				return;
			}

			// Legacy delegates keep early startup and older bridge code functional
			// before the Godot-side structured sink is installed.
			switch(level)
			{
				case global::EmueraLogLevel.Warn:
					warn?.Invoke(messageFactory != null ? messageFactory() : content);
					break;
				case global::EmueraLogLevel.Error:
					error?.Invoke(messageFactory != null ? messageFactory() : content);
					break;
				default:
					info?.Invoke(messageFactory != null ? messageFactory() : content);
					break;
			}
		}

		public static StructuredLogSink sink;
		public static Func<global::EmueraLogLevel, global::EmueraLogCategory, bool> isEnabled;
		public static System.Action<object> info;
		public static System.Action<object> warn;
		public static System.Action<object> error;
	}

	public static class Utils
	{
		static readonly object recursiveFileIndexLock = new object();
		static readonly Dictionary<string, Dictionary<string, string>> recursiveFileIndexCache =
			new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		// Android 启动期 5-6 遍整树枚举的合并缓存：每个目录只用 DirAccess 列出一次，
		// GetFilePaths/GetDirectoryPaths 的所有模式查询复用同一快照（同文件集合、同排序）。
		// 仅作用于 Erb/Csv/Content 三个静态游戏数据根，运行时的可变目录（如存档目录）
		// 仍走即时枚举，保证 ENUMFILES 等运行时函数不会读到陈旧结果。
		private sealed class DirListing
		{
			public readonly string[] Files;
			public readonly string[] Subdirs;
			public DirListing(string[] files, string[] subdirs)
			{
				Files = files;
				Subdirs = subdirs;
			}
		}

		static readonly object recursiveDirListingLock = new object();
		static readonly Dictionary<string, DirListing> recursiveDirListingCache =
			new Dictionary<string, DirListing>(StringComparer.OrdinalIgnoreCase);

		// 大小写不敏感路径索引：一次性复用 recursiveDirListingCache 建立 lower(normalize(rel)) -> actual(rel)，
		// 让 Android 上 30k 张图片的首次路径解析从逐层 DirAccess 扫描降为 O(1) 字典命中。
		// 仅覆盖 ContentDir（图片资源路径解析的主力场景）；其余目录仍走原 ResolveExistingPath 扫描。
		static Dictionary<string, string> contentPathIndex;
		static readonly object contentPathIndexLock = new object();

		static Dictionary<string, string> GetContentPathIndex()
		{
			lock (contentPathIndexLock)
			{
				if (contentPathIndex != null)
					return contentPathIndex;
				var index = new Dictionary<string, string>(StringComparer.Ordinal);
				string root = NormalizePath(MinorShift.Emuera.Program.ContentDir ?? "");
				if (root.Length > 0)
				{
					var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					BuildContentPathIndexInto(root, "", index, visited);
				}
				contentPathIndex = index;
				return contentPathIndex;
			}
		}

		static void BuildContentPathIndexInto(string dirPath, string relPrefix, Dictionary<string, string> index, HashSet<string> visited)
		{
			// 与 CollectFilePathsCached 相同的键格式，直接复用已经缓存好的目录列表，无额外 I/O。
			var listing = GetOrBuildDirListing(dirPath);
			foreach (string file in listing.Files)
			{
				string rel = relPrefix + file;
				index[NormalizePath(rel).ToLowerInvariant()] = rel;
			}
			foreach (string subdir in listing.Subdirs)
			{
				string rel = relPrefix + subdir;
				index[NormalizePath(rel).ToLowerInvariant()] = rel;
				string subPath = dirPath.TrimEnd('/') + "/" + subdir;
				if (visited.Add(subPath))
					BuildContentPathIndexInto(subPath, rel + "/", index, visited);
			}
		}

		static bool IsCacheableDirRoot(string search)
		{
			return IsUnderDirRoot(search, MinorShift.Emuera.Program.ErbDir)
				|| IsUnderDirRoot(search, MinorShift.Emuera.Program.CsvDir)
				|| IsUnderDirRoot(search, MinorShift.Emuera.Program.ContentDir);
		}

		static bool IsUnderDirRoot(string path, string root)
		{
			if (string.IsNullOrEmpty(root))
				return false;
			string p = NormalizePath(path).TrimEnd('/');
			string r = NormalizePath(root).TrimEnd('/');
			if (string.IsNullOrEmpty(r))
				return false;
			if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase))
				return true;
			// 目录边界匹配，避免 ErbDir=".../erb" 误命中 ".../erb_saves"。
			return p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
		}

		static DirListing GetOrBuildDirListing(string search)
		{
			lock (recursiveDirListingLock)
			{
				if (recursiveDirListingCache.TryGetValue(search, out var listing))
					return listing;
				listing = BuildDirListing(search);
				recursiveDirListingCache[search] = listing;
				return listing;
			}
		}

		static DirListing BuildDirListing(string search)
		{
			using var dir = Godot.DirAccess.Open(search);
			if (dir == null)
			{
				global::GenericUtils.Error(global::EmueraLogCategory.FileSystem, () => $"[FS] DirAccess open failed: {search}, error: {Godot.DirAccess.GetOpenError()}");
				return new DirListing(Array.Empty<string>(), Array.Empty<string>());
			}
			dir.IncludeHidden = true;
			return new DirListing(dir.GetFiles(), dir.GetDirectories());
		}

		static void CollectFilePathsCached(string search, System.Text.RegularExpressions.Regex pattern, List<string> result)
		{
			var listing = GetOrBuildDirListing(search);
			string prefix = search.TrimEnd('/') + "/";
			foreach (string file in listing.Files)
			{
				if (pattern.IsMatch(file))
					result.Add(prefix + file);
			}
			foreach (string subdir in listing.Subdirs)
				CollectFilePathsCached(prefix + subdir, pattern, result);
		}

		public static void SetSHIFTJIS_to_UTF8Dict(Dictionary<string, string> dict)
		{
			shiftjis_to_utf8 = dict;
		}
		public static void SetUTF8ZHCN_to_UTF8Dict(Dictionary<string, string> dict)
		{
			utf8zhcn_to_utf8 = dict;
		}
		public static string SHIFTJIS_to_UTF8(string text, string md5)
		{
			if(shiftjis_to_utf8 == null)
				return null;
			string result = null;
			shiftjis_to_utf8.TryGetValue(md5, out result);
			if(string.IsNullOrEmpty(result) && utf8zhcn_to_utf8 != null)
				utf8zhcn_to_utf8.TryGetValue(text, out result);
			return result;
		}
		static Dictionary<string, string> shiftjis_to_utf8;
		static Dictionary<string, string> utf8zhcn_to_utf8;

		/// <summary>
		/// 标准化目录
		/// </summary>
		/// <param name="path"></param>
		/// <returns></returns>
		public static string NormalizePath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return "";
			string normalized = path.Replace('\\', '/');
			int schemeIndex = normalized.IndexOf("://", StringComparison.Ordinal);
			string prefix = "";
			if (schemeIndex >= 0)
			{
				prefix = normalized.Substring(0, schemeIndex + 3);
				normalized = normalized.Substring(schemeIndex + 3);
			}
			while (normalized.Contains("//"))
				normalized = normalized.Replace("//", "/");
			return prefix + normalized;
		}

		static readonly bool _useGodotFileApi = Godot.OS.GetName() == "Android";
		static bool UseGodotFileApi()
		{
			return _useGodotFileApi;
		}

		public static bool DirectoryExists(string path)
		{
			path = NormalizePath(path);
			if (UseGodotFileApi())
			{
				using var dir = Godot.DirAccess.Open(path);
				if (dir != null)
					return true;
				// Case-insensitive fallback: check parent for matching directory name
				int lastSlash = path.TrimEnd('/').LastIndexOf('/');
				if (lastSlash <= 0)
					return false;
				string parent = path.Substring(0, lastSlash);
				string dirName = path.Substring(lastSlash + 1).TrimEnd('/');
				using var parentDir = Godot.DirAccess.Open(parent);
				if (parentDir == null)
					return false;
				parentDir.IncludeHidden = true;
				foreach (string subdir in parentDir.GetDirectories())
				{
					if (string.Equals(subdir, dirName, StringComparison.OrdinalIgnoreCase))
						return true;
				}
				return false;
			}
			return Directory.Exists(path);
		}

		public static bool FileExists(string path)
		{
			path = NormalizePath(path);
			if (UseGodotFileApi())
			{
				if (Godot.FileAccess.FileExists(path))
					return true;
				// Case-insensitive fallback: check parent directory for matching filename
				int lastSlash = path.LastIndexOf('/');
				if (lastSlash <= 0)
					return false;
				string dir = path.Substring(0, lastSlash);
				string fileName = path.Substring(lastSlash + 1);
				using var da = Godot.DirAccess.Open(dir);
				if (da == null)
					return false;
				da.IncludeHidden = true;
				foreach (string file in da.GetFiles())
				{
					if (string.Equals(file, fileName, StringComparison.OrdinalIgnoreCase))
						return true;
				}
				return false;
			}
			return File.Exists(path);
		}

		public static string ResolveScriptDirectoryPath(string path)
		{
			return ResolveScriptRelativePath(path, true);
		}

		public static string ResolveScriptFilePath(string path)
		{
			return ResolveScriptRelativePath(path, false);
		}

		public static string GetRelativePathFromGameDir(string path)
		{
			string normalizedPath = NormalizePath(path);
			string gameDir = NormalizePath(MinorShift.Emuera.Program.ExeDir ?? "");
			if (string.IsNullOrEmpty(normalizedPath) || string.IsNullOrEmpty(gameDir))
				return normalizedPath;
			if (normalizedPath.Contains("://") || gameDir.Contains("://"))
				return normalizedPath;

			try
			{
				string relative = Path.GetRelativePath(Path.GetFullPath(gameDir), Path.GetFullPath(normalizedPath));
				return CanonicalizeGameRelativePrefix(NormalizePath(relative));
			}
			catch
			{
				string prefix = gameDir.TrimEnd('/') + "/";
				if (normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					return CanonicalizeGameRelativePrefix(normalizedPath.Substring(prefix.Length));
				return CanonicalizeGameRelativePrefix(normalizedPath);
			}
		}

		static string ResolveScriptRelativePath(string path, bool directory)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;

			// ERB 文件系函数按原核心语义应以游戏目录为基准。
			// Godot/Android 下进程工作目录不等于游戏目录，所以这里显式补回
			// v24/snake 的 Utils.GetValidPath 边界，同时继续复用 Godot 文件 API。
			string candidate = RemoveParentTraversal(NormalizePath(path.Trim()));
			if (!IsRootedOrGodotPath(candidate))
			{
				if (!TryResolveKnownGameSubdirectory(candidate, out candidate))
				{
					string gameDir = MinorShift.Emuera.Program.ExeDir ?? "";
					if (!string.IsNullOrEmpty(gameDir))
						candidate = Path.Combine(gameDir, candidate);
				}
			}
			candidate = NormalizePath(candidate);

			if (directory)
				return DirectoryExists(candidate) ? ResolveExistingDirectoryPath(candidate) : candidate;
			return FileExists(candidate) ? ResolveExistingFilePath(candidate) : candidate;
		}

		static bool TryResolveKnownGameSubdirectory(string path, out string resolved)
		{
			resolved = path;
			string normalized = NormalizePath(path);
			string trimmed = normalized.TrimStart('/');
			int slash = trimmed.IndexOf('/');
			string head = slash >= 0 ? trimmed.Substring(0, slash) : trimmed;
			string rest = slash >= 0 ? trimmed.Substring(slash + 1) : "";
			string baseDir = null;

			if (string.Equals(head, "resources", StringComparison.OrdinalIgnoreCase))
				baseDir = MinorShift.Emuera.Program.ContentDir;
			else if (string.Equals(head, "csv", StringComparison.OrdinalIgnoreCase))
				baseDir = MinorShift.Emuera.Program.CsvDir;
			else if (string.Equals(head, "erb", StringComparison.OrdinalIgnoreCase))
				baseDir = MinorShift.Emuera.Program.ErbDir;
			else if (string.Equals(head, "dat", StringComparison.OrdinalIgnoreCase))
				baseDir = MinorShift.Emuera.Program.DatDir;
			else if (string.Equals(head, "debug", StringComparison.OrdinalIgnoreCase))
				baseDir = MinorShift.Emuera.Program.DebugDir;

			if (string.IsNullOrEmpty(baseDir))
				return false;
			resolved = string.IsNullOrEmpty(rest) ? baseDir : Path.Combine(baseDir, rest);
			return true;
		}

		static string CanonicalizeGameRelativePrefix(string path)
		{
			if (string.IsNullOrEmpty(path) || path.Contains("://") || IsRootedOrGodotPath(path))
				return path;

			string normalized = NormalizePath(path);
			int slash = normalized.IndexOf('/');
			string head = slash >= 0 ? normalized.Substring(0, slash) : normalized;
			string rest = slash >= 0 ? normalized.Substring(slash) : "";

			if (string.Equals(head, "resources", StringComparison.OrdinalIgnoreCase))
				return "resources" + rest;
			if (string.Equals(head, "csv", StringComparison.OrdinalIgnoreCase))
				return "csv" + rest;
			if (string.Equals(head, "erb", StringComparison.OrdinalIgnoreCase))
				return "erb" + rest;
			if (string.Equals(head, "dat", StringComparison.OrdinalIgnoreCase))
				return "dat" + rest;
			if (string.Equals(head, "debug", StringComparison.OrdinalIgnoreCase))
				return "debug" + rest;
			return normalized;
		}

		static bool IsRootedOrGodotPath(string path)
		{
			if (string.IsNullOrEmpty(path))
				return false;
			if (path.Contains("://"))
				return true;
			try
			{
				return Path.IsPathRooted(path);
			}
			catch
			{
				return false;
			}
		}

		static string RemoveParentTraversal(string path)
		{
			while (path.Contains("../", StringComparison.Ordinal))
				path = path.Replace("../", "");
			return path == ".." ? "" : path;
		}

		/// <summary>
		/// Recursively search a directory for a file matching the given name (case-insensitive).
		/// Returns the full path if found, null otherwise.
		/// </summary>
		public static string FindFileRecursive(string rootDir, string targetFileName)
		{
			if (string.IsNullOrEmpty(rootDir) || string.IsNullOrEmpty(targetFileName))
				return null;
			rootDir = NormalizePath(rootDir);
			targetFileName = NormalizePath(targetFileName);

			string direct = FindFileDirect(rootDir, targetFileName);
			if (!string.IsNullOrEmpty(direct))
				return direct;
			if (!DirectoryExists(rootDir))
				return null;

			string baseName = Path.GetFileName(targetFileName);
			string noExtName = Path.GetFileNameWithoutExtension(baseName);
			var index = GetOrBuildRecursiveFileIndex(rootDir);
			if (index.TryGetValue(targetFileName, out var found))
				return found;
			if (!string.IsNullOrEmpty(baseName) && index.TryGetValue(baseName, out found))
				return found;
			if (!string.IsNullOrEmpty(noExtName) && index.TryGetValue(noExtName, out found))
				return found;
			return null;
		}

		static string FindFileDirect(string rootDir, string targetFileName)
		{
			if (Path.IsPathRooted(targetFileName) || targetFileName.Contains("://"))
			{
				if (FileExists(targetFileName))
					return ResolveExistingFilePath(targetFileName);
			}

			string rootedTarget = Path.Combine(rootDir, targetFileName);
			if (FileExists(rootedTarget))
				return ResolveExistingFilePath(rootedTarget);

			string baseName = Path.GetFileName(targetFileName);
			if (!string.IsNullOrEmpty(baseName) && !string.Equals(baseName, targetFileName, StringComparison.OrdinalIgnoreCase))
			{
				string topLevelTarget = Path.Combine(rootDir, baseName);
				if (FileExists(topLevelTarget))
					return ResolveExistingFilePath(topLevelTarget);
			}
			return null;
		}

		static Dictionary<string, string> GetOrBuildRecursiveFileIndex(string rootDir)
		{
			lock (recursiveFileIndexLock)
			{
				if (recursiveFileIndexCache.TryGetValue(rootDir, out var index))
					return index;

				index = BuildRecursiveFileIndex(rootDir);
				recursiveFileIndexCache[rootDir] = index;
				return index;
			}
		}

		static Dictionary<string, string> BuildRecursiveFileIndex(string rootDir)
		{
			var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string path in GetFilePaths(rootDir, "*", SearchOption.AllDirectories))
			{
				string normalizedPath = NormalizePath(path);
				string fileName = Path.GetFileName(normalizedPath);
				if (string.IsNullOrEmpty(fileName))
					continue;
				AddRecursiveIndexEntry(index, fileName, normalizedPath);
				AddRecursiveIndexEntry(index, normalizedPath, normalizedPath);
			}
			return index;
		}

		static void AddRecursiveIndexEntry(Dictionary<string, string> index, string key, string path)
		{
			if (!index.ContainsKey(key))
				index[key] = path;
			string noExt = Path.GetFileNameWithoutExtension(key);
			if (!string.IsNullOrEmpty(noExt) && !index.ContainsKey(noExt))
				index[noExt] = path;
		}

		public static void CreateDirectory(string path)
		{
			path = NormalizePath(path);
			if (UseGodotFileApi())
			{
				if (DirectoryExists(path))
					return;
				var error = Godot.DirAccess.MakeDirRecursiveAbsolute(path);
				if (error != Godot.Error.Ok)
					throw new IOException($"Failed to create directory: {path}, error: {error}");
				return;
			}
			Directory.CreateDirectory(path);
		}

		public static byte[] ReadAllBytes(string path)
		{
			path = NormalizePath(path);
			if (UseGodotFileApi())
			{
				using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
				if (file == null)
				{
					// Case-insensitive fallback: resolve actual filename
					string resolved = ResolveExistingPath(path, false);
					using var file2 = Godot.FileAccess.Open(resolved, Godot.FileAccess.ModeFlags.Read);
					if (file2 == null)
					{
						var error = Godot.FileAccess.GetOpenError();
						global::GenericUtils.Error(global::EmueraLogCategory.FileSystem, () => $"[FS] FileAccess open failed: {path}, error: {error}");
						throw new IOException($"FileAccess open failed: {path}, error: {error}");
					}
					return file2.GetBuffer((long)file2.GetLength());
				}
				return file.GetBuffer((long)file.GetLength());
			}
			return File.ReadAllBytes(path);
		}

		public static string[] ReadAllLines(string path, System.Text.Encoding encoding)
		{
			if (!UseGodotFileApi())
				return File.ReadAllLines(path, encoding);

			string text = encoding.GetString(ReadAllBytes(path));
			if (text.Length > 0 && text[0] == '﻿')
				text = text.Substring(1);
			var lines = new List<string>();
			using var reader = new StringReader(text);
			string line;
			while ((line = reader.ReadLine()) != null)
				lines.Add(line);
			return lines.ToArray();
		}

		public static long GetLastWriteTimeKey(string path)
		{
			path = NormalizePath(path);
			if (UseGodotFileApi())
				return (long)Godot.FileAccess.GetModifiedTime(path);
			return File.GetLastWriteTime(path).ToBinary();
		}

		public static List<string> GetFilePaths(string search, string pattern, SearchOption option)
		{
			search = NormalizePath(search);
			if (!UseGodotFileApi())
			{
				if (IsCaseSensitiveFileSystem())
					return GetFilesCaseInsensitive(search, pattern, option);
				return new List<string>(Directory.GetFiles(search, pattern, option));
			}

			var result = new List<string>();
			if (IsCacheableDirRoot(search))
			{
				var listing = GetOrBuildDirListing(search);
				var regex = GlobToRegex(pattern);
				if (option == SearchOption.AllDirectories)
				{
					CollectFilePathsCached(search, regex, result);
				}
				else
				{
					string prefix = search.TrimEnd('/') + "/";
					foreach (string file in listing.Files)
					{
						if (regex.IsMatch(file))
							result.Add(prefix + file);
					}
				}
				return result;
			}
			CollectFilePaths(search, GlobToRegex(pattern), option, result);
			return result;
		}

		private static bool IsCaseSensitiveFileSystem()
		{
			return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
				System.Runtime.InteropServices.OSPlatform.Linux) ||
				System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
				System.Runtime.InteropServices.OSPlatform.OSX);
		}

		private static List<string> GetFilesCaseInsensitive(string search, string pattern, SearchOption option)
		{
			string[] allFiles;
			try
			{
				allFiles = Directory.GetFiles(search, "*", option);
			}
			catch (Exception ex)
			{
				GenericUtils.Error($"[FS] GetFiles failed: {search}, pattern={pattern}, error={ex.Message}");
				return new List<string>();
			}
			var regex = GlobToRegex(pattern);
			var result = new List<string>();
			foreach (var file in allFiles)
			{
				var fileName = Path.GetFileName(file);
				if (regex.IsMatch(fileName))
					result.Add(file);
			}
			return result;
		}

		private static System.Text.RegularExpressions.Regex GlobToRegex(string pattern)
		{
			var regexPattern = "^" +
				System.Text.RegularExpressions.Regex.Escape(pattern)
					.Replace("\\*", ".*")
					.Replace("\\?", ".") + "$";
			return new System.Text.RegularExpressions.Regex(regexPattern,
				System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		}

		public static List<string> GetDirectoryPaths(string search)
		{
			search = NormalizePath(search);
			if (!UseGodotFileApi())
				return new List<string>(Directory.GetDirectories(search, "*", SearchOption.TopDirectoryOnly));

			var result = new List<string>();
			if (IsCacheableDirRoot(search))
			{
				var listing = GetOrBuildDirListing(search);
				string prefix = search.TrimEnd('/') + "/";
				foreach (string entry in listing.Subdirs)
					result.Add(prefix + entry);
				return result;
			}
			using var dir = Godot.DirAccess.Open(search);
			if (dir == null)
			{
				global::GenericUtils.Error(global::EmueraLogCategory.FileSystem, () => $"[FS] DirAccess open failed: {search}, error: {Godot.DirAccess.GetOpenError()}");
				return result;
			}
			dir.IncludeHidden = true;
			foreach (string entry in dir.GetDirectories())
				result.Add(search.TrimEnd('/') + "/" + entry);
			return result;
		}

		public static string ResolveExistingDirectoryPath(string path)
		{
			if (UseGodotFileApi())
				return ResolveExistingPath(path, true);
			return NormalizePath(path);
		}

		public static string ResolveExistingFilePath(string path)
		{
			if (UseGodotFileApi())
				return ResolveExistingPath(path, false);
			return NormalizePath(path);
		}

		static string ResolveExistingPath(string path, bool directory)
		{
			path = NormalizePath(path);
			if (string.IsNullOrEmpty(path))
				return path;
			if (directory)
			{
				using var exactDir = Godot.DirAccess.Open(path);
				if (exactDir != null)
					return path;
			}
			else if (Godot.FileAccess.FileExists(path))
			{
				return path;
			}

			// 大小写索引查找（Android 资源路径主力场景）：命中即 O(1) 返回，跳过逐层 DirAccess 扫描。
			if (!directory && !string.IsNullOrEmpty(MinorShift.Emuera.Program.ContentDir))
			{
				string contentRoot = NormalizePath(MinorShift.Emuera.Program.ContentDir).TrimEnd('/') + "/";
				if (path.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase))
				{
					string rel = path.Substring(contentRoot.Length);
					if (GetContentPathIndex().TryGetValue(rel.ToLowerInvariant(), out string actualRel))
						return contentRoot + actualRel;
				}
			}

			// Try case-insensitive resolution starting from the parent directory.
			// On Android, walking from "/" often fails due to permissions,
			// so we find the deepest accessible ancestor and resolve from there.
			string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
				return path;
			bool absolute = path.StartsWith('/');

			// Find the deepest directory we can open
			int startIdx = parts.Length - 1;
			string current = "";
			for (int i = parts.Length - 1; i >= 0; i--)
			{
				string candidate = absolute ? "/" + string.Join("/", parts, 0, i) : string.Join("/", parts, 0, i);
				if (string.IsNullOrEmpty(candidate))
					candidate = absolute ? "/" : ".";
				using var testDir = Godot.DirAccess.Open(candidate);
				if (testDir != null)
				{
					current = candidate;
					startIdx = i;
					break;
				}
			}
			if (string.IsNullOrEmpty(current))
				return path;
			if (!absolute && Godot.OS.GetName() == "Android")
				return path;

			for (int i = startIdx; i < parts.Length; i++)
			{
				bool last = i == parts.Length - 1;
				using var dir = Godot.DirAccess.Open(current);
				if (dir == null)
					return path;
				dir.IncludeHidden = true;
				string match = null;
				if (last && !directory)
				{
					foreach (string file in dir.GetFiles())
					{
						if (string.Equals(file, parts[i], StringComparison.OrdinalIgnoreCase))
						{
							match = file;
							break;
						}
					}
				}
				if (match == null)
				{
					foreach (string subdir in dir.GetDirectories())
					{
						if (string.Equals(subdir, parts[i], StringComparison.OrdinalIgnoreCase))
						{
							match = subdir;
							break;
						}
					}
				}
				if (match == null)
					return path;
				current = current.TrimEnd('/') + "/" + match;
			}
			return current;
		}

		static void CollectFilePaths(string search, System.Text.RegularExpressions.Regex pattern, SearchOption option, List<string> result)
		{
			using var dir = Godot.DirAccess.Open(search);
			if (dir == null)
			{
				global::GenericUtils.Error(global::EmueraLogCategory.FileSystem, () => $"[FS] DirAccess open failed: {search}, error: {Godot.DirAccess.GetOpenError()}");
				return;
			}
			dir.IncludeHidden = true;
			foreach (string file in dir.GetFiles())
			{
				if (pattern.IsMatch(file))
					result.Add(search.TrimEnd('/') + "/" + file);
			}
			if (option != SearchOption.AllDirectories)
				return;
			foreach (string subdir in dir.GetDirectories())
				CollectFilePaths(search.TrimEnd('/') + "/" + subdir, pattern, option, result);
		}

		public static string GetSuffix(string filename)
		{
			int last_slash = filename.LastIndexOf('.');
			if(last_slash != -1)
				return filename.Substring(last_slash + 1);
			return filename;
		}
		/// <summary>
		/// 获取文本长
		/// </summary>
		/// <param name="s"></param>
		/// <param name="font"></param>
		/// <returns></returns>
		public static int GetDisplayLength(string s, uEmuera.Drawing.Font font)
		{
			return GetDisplayLength(s, font.Size);
		}

		public static readonly HashSet<char> fullsize = new HashSet<char>
		{
			'¢', '£', '¬', '§', '¨', '°', '±', '´', '¶', '·', '×', '÷',
		};
		public static bool CheckFullSize(char c)
		{
			return fullsize.Contains(c) || IsFullWidthForm(c);
		}
		public static readonly HashSet<char> halfsize = new HashSet<char>
		{
			'▀','▁','▂','▃','▄','▅',
			'▆','▇','█','▉','▊','▋',
			'▌','▍','▎','▏','▐','░',
			'▒','▓','▔','▕', '▮',
			'◮', '♮', '❮',
			'⟮', '⠮','⡮','⢮', '⣮',
			'▤','▥','▦', '▧', '▨', '▩',
			'▪', '▫','~', '´', 'ﾄ', '｡', '･',
		};
		public static bool CheckHalfSize(char c)
		{
			// 原核心的 STRLEN 以 Shift-JIS/代码页字节数为基准，绘制宽度则依赖日文字体测量。
			// Godot/Android 侧改用固定网格后，必须先排除这些在脚本中常作全角符号使用的字符，
			// 否则 ×、±、°、全角数字等会被当成半角，地图和 GDRAWTEXT 的列推进会错位。
			if(CheckFullSize(c))
				return false;
			if (IsSingleCellUnicodeArt(c))
				return true;
			// 箱线字符统一走默认全宽路径；┏━┓、╋┃ 等地图格线若混入半宽横线会破坏固定网格。
			return c < 0x127 || IsHalfWidthKatakana(c) || halfsize.Contains(c);
		}

		static bool IsSingleCellUnicodeArt(char c)
		{
			// v24/snake 使用字体实际测量；Godot 固定网格只能做分类近似。
			// TW 的 PRINT_COLORBAR/快感条使用 U+2585、U+2588、U+2592 等 Block Elements，
			// 原核心在 MS Gothic/Skia 下按单列块状字形推进；这里统一按半宽占位，避免同类条形图宽度分裂。
			if (c >= '\u2580' && c <= '\u259F')
				return true;
			// Braille 点阵常被脚本当作 ASCII Art 的单列像素块，继续按半宽推进。
			return c >= '\u2800' && c <= '\u28FF';
		}

		static bool IsFullWidthForm(char c)
		{
			return (c >= '\uFF01' && c <= '\uFF60')
				|| (c >= '\uFFE0' && c <= '\uFFE6');
		}

		static bool IsHalfWidthKatakana(char c)
		{
			return c >= '\uFF61' && c <= '\uFF9F';
		}

		public static bool CheckZeroWidth(char c)
		{
			UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
			if (category == UnicodeCategory.NonSpacingMark
				|| category == UnicodeCategory.EnclosingMark
				|| category == UnicodeCategory.Format)
				return true;
			return c == '\u200B'
				|| c == '\u200C'
				|| c == '\u200D'
				|| c == '\u2060'
				|| c == '\uFEFF'
				|| c == '\u180E'
				|| c == '\u00AD'
				|| (c >= '\uFE00' && c <= '\uFE0F');
		}

		public static string StripZeroWidth(string s)
		{
			if (string.IsNullOrEmpty(s))
				return s;

			int first = -1;
			for (int i = 0; i < s.Length; i++)
			{
				if (CheckZeroWidth(s[i]))
				{
					first = i;
					break;
				}
			}
			if (first < 0)
				return s;

			var builder = new System.Text.StringBuilder(s.Length);
			if (first > 0)
				builder.Append(s, 0, first);
			for (int i = first + 1; i < s.Length; i++)
			{
				if (!CheckZeroWidth(s[i]))
					builder.Append(s[i]);
			}
			return builder.ToString();
		}
		/// <summary>
		/// 获取文本长
		/// </summary>
		/// <param name="s"></param>
		/// <param name="font"></param>
		/// <returns></returns>
		public static int GetDisplayLength(string s, float fontsize)
		{
			float xsize = 0;
			char c = '\x0';
			for(int i = 0; i < s.Length; ++i)
			{
				c = s[i];
				if(CheckZeroWidth(c))
					continue;
				if(CheckHalfSize(c))
					xsize += fontsize / 2;
				else
					xsize += fontsize;
			}

			return (int)xsize;
		}

		public static string GetStBar(char c, uEmuera.Drawing.Font font)
		{
			return GetStBar(c, font.Size);
		}

		public static string GetStBar(char c, float fontsize)
		{
			float s = fontsize;
			if(CheckHalfSize(c))
				s /= 2;
			var w = MinorShift.Emuera.Config.DrawableWidth;
			var count = (int)System.Math.Floor(w / s);
			var build = new System.Text.StringBuilder(count);
			for(int i = 0; i < count; ++i)
				build.Append(c);
			return build.ToString();
		}

		public static int GetByteCount(string str)
		{
			if(string.IsNullOrEmpty(str))
				return 0;
			var count = 0;
			var length = str.Length;
			for(int i = 0; i < length; ++i)
			{
				if(CheckZeroWidth(str[i]))
					continue;
				if(CheckHalfSize(str[i]))
					count += 1;
				else
					count += 2;
			}
			return count;
		}
		public static List<string> GetFiles(string search, string extension, SearchOption option)
		{
			var files = GetFilePaths(search, "*.???", option);
			var filecount = files.Count;
			var result = new List<string>();
			for(int i=0; i<filecount; ++i)
			{
				var file = files[i];
				string ext = Path.GetExtension(file);
				if(string.Compare(ext, extension, true) == 0)
					result.Add(file);
			}
			return result;
		}
		public static List<string> GetFiles(string search, string[] extensions, SearchOption option)
		{
			var extension_checker = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for(int i = 0; i < extensions.Length; ++i)
				extension_checker.Add(extensions[i]);

			var files = GetFilePaths(search, "*.???", option);
			var filecount = files.Count;
			var result = new List<string>();
			for(int i = 0; i < filecount; ++i)
			{
				var file = files[i];
				string ext = Path.GetExtension(file);
				if(extension_checker.Contains(ext))
					result.Add(file);
			}
			return result;
		}

		static readonly string[] contentImageExtensionOrder =
		{
			".png",
			".bmp",
			".jpg",
			".gif",
			".webp",
		};

		public static Dictionary<string, string> GetContentFiles()
		{
			if(content_files != null)
				return content_files;
			content_files = new Dictionary<string, string>();

			var contentdir = MinorShift.Emuera.Program.ContentDir;
			if(!DirectoryExists(contentdir))
				return content_files;

			List<string> bmpfilelist = new List<string>();
			var allFiles = GetFilePaths(contentdir, "*", SearchOption.TopDirectoryOnly);
			for (int extIndex = 0; extIndex < contentImageExtensionOrder.Length; ++extIndex)
			{
				var wantedExt = contentImageExtensionOrder[extIndex];
				for (int i = 0; i < allFiles.Count; ++i)
				{
					var file = allFiles[i];
					if (string.Equals(Path.GetExtension(file), wantedExt, StringComparison.OrdinalIgnoreCase))
						bmpfilelist.Add(file);
				}
			}
			var filecount = bmpfilelist.Count;
			for(int i=0; i<filecount; ++i)
			{
				var filename = bmpfilelist[i];
				string name = Path.GetFileName(filename).ToUpper();
				content_files.Add(name, filename);
			}
			return content_files;
		}
		public static string[] GetResourceCSVLines(
			string csvpath, System.Text.Encoding encoding)
		{
			string[] lines = null;
			if (MinorShift.Emuera.Sub.Preload.TryGetFileLines(csvpath, out lines))
				return lines;
			if(resource_csv_lines_ != null &&
				resource_csv_lines_.TryGetValue(csvpath, out lines))
				return lines;
			lines = ReadAllLines(csvpath, encoding);
			return lines;
		}
		public static void ResourcePrepare()
		{
			var content_files = GetContentFiles();
			if(content_files.Count == 0)
				return;

			var contentdir = MinorShift.Emuera.Program.ContentDir;
			List<string> csvFiles = new List<string>(GetFilePaths(
				contentdir, "*.csv", SearchOption.TopDirectoryOnly));
#if(UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
			csvFiles.AddRange(GetFilePaths(
				contentdir, "*.CSV", SearchOption.TopDirectoryOnly));
#endif
			resource_csv_lines_ = new Dictionary<string, string[]>();

			var encoder = MinorShift.Emuera.Config.Encode;
			var filecount = csvFiles.Count;
			for(int index=0; index < filecount; ++index)
			{
				var filename = csvFiles[index];
				//SpriteManager.ClearResourceCSVLines(filename);
				string[] lines = SpriteManager.GetResourceCSVLines(filename);
				if(lines != null)
				{
					resource_csv_lines_.Add(filename, lines);
					continue;
				}

				List<string> newlines = new List<string>();
				lines = ReadAllLines(filename, encoder);
				int fixcount = 0;
				for(int i = 0; i < lines.Length; ++i)
				{
					var line = lines[i];
					if(line.Length == 0)
						continue;
					string str = line.Trim();
					if(str.Length == 0 || str.StartsWith(";"))
						continue;

					int tokenCount = ReadCsvHeadFields6(
						str,
						out string token0,
						out string token1,
						out string token2,
						out string token3,
						out string token4,
						out string token5);
					if(tokenCount >= 6)
					{
						try
						{
							if (!string.IsNullOrEmpty(token2) &&
								!string.IsNullOrEmpty(token3) &&
								!string.IsNullOrEmpty(token4) &&
								!string.IsNullOrEmpty(token5))
							{
								var w = int.Parse(token4);
								var h = int.Parse(token5);
								if (w != 0 && h != 0)
								{
									newlines.Add(line);
									continue;
								}
							}
						}
						catch
						{}
					}
					if (tokenCount <= 1)
						continue;
					string name = token1.ToUpper();
					string imagepath = null;
					content_files.TryGetValue(name, out imagepath);
					if(imagepath == null)
						continue;

					var ti = SpriteManager.GetTextureInfo(name, imagepath);
					if(ti == null)
						continue;
					line = string.Format("{0},{1},0,0,{2},{3}",
						token0, token1, ti.width, ti.height);
					newlines.Add(line);
					fixcount += 1;
				}
				lines = newlines.ToArray();
				resource_csv_lines_.Add(filename, lines);
				if(fixcount > 0)
					SpriteManager.SetResourceCSVLine(filename, lines);
			}
		}
		public static void ResourcePrepareSimple()
		{
			var content_files = GetContentFiles();
			if(content_files.Count == 0)
				return;

			var contentdir = MinorShift.Emuera.Program.ContentDir;
			List<string> csvFiles = new List<string>(GetFilePaths(
				contentdir, "*.csv", SearchOption.TopDirectoryOnly));
#if(UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
			csvFiles.AddRange(GetFilePaths(
				contentdir, "*.CSV", SearchOption.TopDirectoryOnly));
#endif
			resource_csv_lines_ = new Dictionary<string, string[]>();

			var encoder = MinorShift.Emuera.Config.Encode;
			var filecount = csvFiles.Count;
			for(int index = 0; index < filecount; ++index)
			{
				var filename = csvFiles[index];
				//SpriteManager.ClearResourceCSVLines(filename);
				string[] lines = SpriteManager.GetResourceCSVLines(filename);
				if(lines != null)
					resource_csv_lines_.Add(filename, lines);
			}
		}
		public static void ResourceClear()
		{
			if(content_files != null)
			{
				content_files.Clear();
				content_files = null;
			}
			if(resource_csv_lines_ != null)
			{
				resource_csv_lines_.Clear();
				resource_csv_lines_ = null;
			}
		}
		/// <summary>
		/// Clears path/resource lookup state owned by one legacy
		/// session.  The normal Legacy shutdown path intentionally keeps its
		/// historical behaviour; only the canary bridge calls this boundary
		/// after the worker has quiesced.
		/// </summary>
		/// <summary>
		/// 失效给定根目录（含子目录）的目录快照缓存。ERB reload、ENUMFILES 等运行时
		/// 动态枚举必须读到最新文件，不能停留在启动期快照（会话中途新增/删除的文件
		/// 必须对 reload 与 ENUMFILES 可见，与 baseline 每次即时枚举语义一致）。
		/// </summary>
		internal static void InvalidateRecursiveDirListing(string root)
		{
			if (string.IsNullOrEmpty(root))
				return;
			lock (recursiveDirListingLock)
			{
				if (recursiveDirListingCache.Count == 0)
					return;
				var stale = new List<string>();
				foreach (var key in recursiveDirListingCache.Keys)
				{
					if (IsUnderDirRoot(key, root))
						stale.Add(key);
				}
				foreach (var key in stale)
					recursiveDirListingCache.Remove(key);
			}
		}

		internal static void ResetCanarySessionState()
		{
			lock (recursiveFileIndexLock)
				recursiveFileIndexCache.Clear();
			lock (recursiveDirListingLock)
				recursiveDirListingCache.Clear();

			// The encoding dictionaries are populated from res:// once by
			// the startup bridge and are immutable process catalogs.  Keep
			// them alive across a game switch; clearing them here would make
			// the next canary depend on whether startup happened again.
			ResourceClear();
			MinorShift.Emuera.Sub.Preload.Clear();
		}

		static int ReadCsvHeadFields6(
			string line,
			out string token0,
			out string token1,
			out string token2,
			out string token3,
			out string token4,
			out string token5)
		{
			token0 = "";
			token1 = "";
			token2 = "";
			token3 = "";
			token4 = "";
			token5 = "";
			if(line == null)
				line = "";

			int count = 1;
			int fieldIndex = 0;
			int start = 0;
			for(int i = 0; i <= line.Length; i++)
			{
				if(i < line.Length && line[i] != ',')
					continue;
				if(fieldIndex < 6)
				{
					string value = i == start ? "" : line.Substring(start, i - start);
					if(fieldIndex == 0)
						token0 = value;
					else if(fieldIndex == 1)
						token1 = value;
					else if(fieldIndex == 2)
						token2 = value;
					else if(fieldIndex == 3)
						token3 = value;
					else if(fieldIndex == 4)
						token4 = value;
					else
						token5 = value;
				}
				fieldIndex++;
				if(i >= line.Length)
					break;
				count++;
				start = i + 1;
			}
			return count;
		}
		static Dictionary<string, string> content_files = null;
		static Dictionary<string, string[]> resource_csv_lines_ = null;
	}
}
