using System;
using System.IO;
using System.Collections.Generic;

namespace uEmuera
{
    public static class Logger
    {
        public static void Info(object content)
        {
            if(info == null)
                return;
            info(content);
        }
        public static void Warn(object content)
        {
            if(warn == null)
                return;
            warn(content);
        }
        public static void Error(object content)
        {
            if(error == null)
                return;
            error(content);
        }
        public static System.Action<object> info;
        public static System.Action<object> warn;
        public static System.Action<object> error;
    }

    public static class Utils
    {
        static readonly object recursiveFileIndexLock = new object();
        static readonly Dictionary<string, Dictionary<string, string>> recursiveFileIndexCache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

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
            if(string.IsNullOrEmpty(result))
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
                        Godot.GD.PrintErr($"FileAccess open failed: {path}, error: {error}");
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
            CollectFilePaths(search, pattern, option, result);
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
            using var dir = Godot.DirAccess.Open(search);
            if (dir == null)
            {
                Godot.GD.PrintErr($"DirAccess open failed: {search}, error: {Godot.DirAccess.GetOpenError()}");
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

        static void CollectFilePaths(string search, string pattern, SearchOption option, List<string> result)
        {
            using var dir = Godot.DirAccess.Open(search);
            if (dir == null)
            {
                Godot.GD.PrintErr($"DirAccess open failed: {search}, error: {Godot.DirAccess.GetOpenError()}");
                return;
            }
            dir.IncludeHidden = true;
            foreach (string file in dir.GetFiles())
            {
                if (WildcardMatch(file, pattern))
                    result.Add(search.TrimEnd('/') + "/" + file);
            }
            if (option != SearchOption.AllDirectories)
                return;
            foreach (string subdir in dir.GetDirectories())
                CollectFilePaths(search.TrimEnd('/') + "/" + subdir, pattern, option, result);
        }

        static bool WildcardMatch(string value, string pattern)
        {
            string regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(value, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
            '´',
        };
        public static bool CheckFullSize(char c)
        {
            return fullsize.Contains(c);
        }
        public static readonly HashSet<char> halfsize = new HashSet<char>
        {
            '▀','▁','▂','▃','▄','▅',
            '▆','▇','█','▉','▊','▋',
            '▌','▍','▎','▏','▐','░',
            '▒','▓','▔','▕', '▮',
            '┮', '╮', '◮', '♮', '❮',
            '⟮', '⠮','⡮','⢮', '⣮', '║',
            '▤','▥','▦', '▧', '▨', '▩',
            '▪', '▫','~', '´', 'ﾄ', '｡', '･',
            '─', '━', '┄', '┅', '┈', '┉',
        };
        public static bool CheckHalfSize(char c)
        {
            return c < 0x127 || halfsize.Contains(c);
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
            var extension_checker = new HashSet<string>();
            for(int i = 0; i < extensions.Length; ++i)
                extension_checker.Add(extensions[i].ToUpper());

            var files = GetFilePaths(search, "*.???", option);
            var filecount = files.Count;
            var result = new List<string>();
            for(int i = 0; i < filecount; ++i)
            {
                var file = files[i];
                string ext = Path.GetExtension(file).ToUpper();
                if(extension_checker.Contains(ext))
                    result.Add(file);
            }
            return result;
        }
        public static Dictionary<string, string> GetContentFiles()
        {
            if(content_files != null)
                return content_files;
            content_files = new Dictionary<string, string>();

            var contentdir = MinorShift.Emuera.Program.ContentDir;
            if(!DirectoryExists(contentdir))
                return content_files;

            List<string> bmpfilelist = new List<string>();
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.png", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.bmp", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.jpg", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.gif", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.webp", SearchOption.TopDirectoryOnly));
#if(UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.PNG", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.BMP", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.JPG", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.GIF", SearchOption.TopDirectoryOnly));
            bmpfilelist.AddRange(GetFilePaths(contentdir, "*.WEBP", SearchOption.TopDirectoryOnly));

#endif
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

                    string[] tokens = str.Split(',');
                    if(tokens.Length >= 6)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(tokens[2]) &&
                                !string.IsNullOrEmpty(tokens[3]) &&
                                !string.IsNullOrEmpty(tokens[4]) &&
                                !string.IsNullOrEmpty(tokens[5]))
                            {
                                var w = int.Parse(tokens[4]);
                                var h = int.Parse(tokens[5]);
                                if (w != 0 && h != 0)
                                {
                                    newlines.Add(line);
                                    continue;
                                }
                            }
                        }
                        catch (Exception e)
                        {}
                    }
                    if (tokens.Length <= 1)
                        continue;
                    string name = tokens[1].ToUpper();
                    string imagepath = null;
                    content_files.TryGetValue(name, out imagepath);
                    if(imagepath == null)
                        continue;

                    var ti = SpriteManager.GetTextureInfo(name, imagepath);
                    if(ti == null)
                        continue;
                    line = string.Format("{0},{1},0,0,{2},{3}",
                        tokens[0], tokens[1], ti.width, ti.height);
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
        static Dictionary<string, string> content_files = null;
        static Dictionary<string, string[]> resource_csv_lines_ = null;
    }
}
