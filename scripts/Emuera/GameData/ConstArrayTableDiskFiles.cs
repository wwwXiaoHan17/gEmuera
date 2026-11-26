// ConstArrayTableDiskFiles.cs —— 常数表（declare.const-table.v1，Emuera-SKIA Skiav18.0）的磁盘文件源。
// 对应上游 ConstArrayTable 的 FindFiles / BuildIndex / ClearFileIndex 与 File.ReadAllLines / File.Exists；
// 这里改走 uEmuera.Utils（Android 走 Godot 文件 API 与大小写不敏感路径索引）和 Config.Encode，
// 与引擎读取其它 CSV / ERD 文件的口径一致。纯规则层见 ConstArrayTable.cs（xUnit 直链覆盖）。
using System;
using System.Collections.Generic;
using System.IO;

namespace MinorShift.Emuera.GameData
{
	/// <summary>
	/// CSV / ERB 目录按文件名建索引，避免每个常数表声明都递归枚举一次 ERB。
	/// 索引只在首次出现 CONSTTABLE 声明时建立；重新解析 ERH 时由 HeaderFileLoader.LoadHeaderFiles
	/// 入口清空，所以新增或改名的表文件不会被旧索引挡住；单独重载 ERB 不会重建索引。
	/// </summary>
	internal sealed class ConstArrayTableDiskFiles : IConstArrayTableFiles
	{
		public static readonly ConstArrayTableDiskFiles Instance = new ConstArrayTableDiskFiles();

		private static readonly List<string> NoFiles = new List<string>();

		private Dictionary<string, List<string>> csvFileIndex;
		private Dictionary<string, List<string>> erbFileIndex;

		private ConstArrayTableDiskFiles()
		{
		}

		/// <summary>丢弃 CSV / ERB 目录索引。每次重新解析 ERH 时调用。</summary>
		public static void ClearIndex()
		{
			Instance.csvFileIndex = null;
			Instance.erbFileIndex = null;
		}

		public IReadOnlyList<string> FindFiles(string fileName, bool fromErb)
		{
			Dictionary<string, List<string>> index;
			if (fromErb)
			{
				erbFileIndex ??= BuildIndex(Program.ErbDir, SearchOption.AllDirectories);
				index = erbFileIndex;
			}
			else
			{
				csvFileIndex ??= BuildIndex(Program.CsvDir, SearchOption.TopDirectoryOnly);
				index = csvFileIndex;
			}
			return index.TryGetValue(fileName, out List<string> list) ? list : NoFiles;
		}

		public bool FileExists(string path)
		{
			return uEmuera.Utils.FileExists(path);
		}

		public string[] ReadAllLines(string path)
		{
			// 要的是原始行：常数表只把行首 ';' 当注释，引号里的 ';' 是普通字符，
			// 不能走 EraStreamReader.ReadEnabledLine（那条路径按 ERB 规则处理注释与行连结）。
			return uEmuera.Utils.ReadAllLines(path, Config.Encode);
		}

		private static Dictionary<string, List<string>> BuildIndex(string dir, SearchOption option)
		{
			var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(dir) || !uEmuera.Utils.DirectoryExists(dir))
				return index;
			try
			{
				foreach (string path in uEmuera.Utils.GetFilePaths(dir, "*", option))
				{
					string name = Path.GetFileName(path);
					if (!index.TryGetValue(name, out List<string> list))
					{
						list = new List<string>();
						index.Add(name, list);
					}
					list.Add(path);
				}
			}
			catch (IOException)
			{
				// 目录读取失败等价于没有常数表；声明处会给出"找不到同名文件"。
			}
			catch (UnauthorizedAccessException)
			{
			}
			foreach (List<string> list in index.Values)
				list.Sort(StringComparer.OrdinalIgnoreCase);
			return index;
		}
	}
}
