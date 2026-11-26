#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MinorShift.Emuera.GameData;

/// <summary>
/// 常数表的文件访问抽象。纯规则层只依赖本接口，不触碰磁盘；
/// 引擎侧的磁盘/编码实现见 ConstArrayTableDiskFiles，xUnit 用内存实现注入。
/// </summary>
internal interface IConstArrayTableFiles
{
	/// <summary>
	/// 按文件名查找：fromErb = false 查 CSV 目录（顶层），true 查 ERB 目录（含子目录）；
	/// 文件名比较不区分大小写，结果按路径排序；找不到返回空表。
	/// </summary>
	IReadOnlyList<string> FindFiles(string fileName, bool fromErb);

	bool FileExists(string path);

	/// <summary>读取全部原始行（不做注释/空白处理）；I/O 失败抛异常。</summary>
	string[] ReadAllLines(string path);
}

/// <summary>
/// 常数表 CSV 的读取规则：
/// - 二维表用 @1 / @2 两个单元格定位。规范位置是 (1,2) 与 (2,1)（行列均从 1 起算），
///   位于 (2,1) 的标记决定纵向维度，位于 (1,2) 的标记决定横向维度；因此两种摆法对应两种维度方向。
///   纵向标记下方：同列橙色为注册数、右一列蓝色为注册名；
///   横向标记右方：同行橙色为注册数、下一行蓝色为注册名；
///   两标记夹出的灰色区域为数值区，数值所在行/列各自取对应方向的注册数。
/// - 一维表无 @ 标记，每行形如 [注册数, 注册名, 值]。
/// - 空行与以 ';' 开头的注释行忽略；注册数与注册名都留空时才按前一格顺延（有注册名就必须写注册数），
///   留空的单元格保持默认值
///   （0 / 0.0 / ""，并可由补充表填入）；非空但解析不了的内容才算坏格。
/// - 超出声明长度的索引不读，未写入的元素保持 0（字符串为 ""）。
/// - 同一维度内注册名重复时以先出现者为准。
/// - 切分规则只有一套，就是 CSV 那套：逗号是分隔符，但**以双引号开头的单元格按引号字段解析**
///   （引号内的逗号、单引号、反斜杠都是普通字符，只有连续两个双引号 `""` 表示一个字面双引号），
///   其余单元格照原样取到下一个逗号。所以值里要带逗号就写成 `"a,b"`；值里要写字面引号，
///   只要不是出现在值的最开头就没问题（如 `他说"你好"`），出现在开头时写成 `"""你好"""`。
///   标记、注册数、注册名照常裸写、不需要引号（加了也等价）。引号没闭合、或结束引号后还有
///   内容，按格式错误报出行列位置，并给出按规则应当写成什么。
/// - 所有字段（包括引号内的内容）首尾空白一律去掉，所以值不能靠空格保留边距。
/// - 值按数组类型转换：#DIM 取整数、#DIMS 取原样字符串、#DIMF 取浮点。
///
/// 多份同名表的合并规则与预设变量的 ERD 扩展一致：CSV 目录下的 .csv 是主表，
/// CSV 目录下的 .erd 与 ERB 目录（含子目录）下的 .ERD 是同类表的补充来源；
/// 主表没写的格子、没命名的索引由补充表填入，冲突（同索引不同名、同名不同索引、
/// 同一格不同值）一律给 Lv1 警告并以主表为准，不中断加载。
/// 主表缺失或不适配声明尺寸时，按同一顺序退到下一份候选当主表。
///
/// 移植自 Emuera-SKIA（蛇版）Skiav18.0 的 Emuera/Runtime/Script/Data/ConstArrayTable.cs
///（上游提交 7154549）；分支、文案与上游逐行一致，仅把文件访问抽成 IConstArrayTableFiles。
/// </summary>
internal static class ConstArrayTable
{
	private const string Marker1 = "@1";
	private const string Marker2 = "@2";
	internal const int KindInt = 0;
	internal const int KindStr = 1;
	internal const int KindFloat = 2;

	/// <summary>加载结果。Names 为各维 name→index，Values 为按 [第1维, 第2维] 展平的数组。</summary>
	internal sealed class Result
	{
		public string Path;
		public Dictionary<string, int>[] Names;
		public object Values;
		/// <summary>补充表冲突、补充表坏格等 Lv1 提示。</summary>
		public List<string> Warnings = [];
		/// <summary>已被常数表消费的文件（主表、值扩展、名称表），调用方不要再交给 EE_ERD 解析。</summary>
		public List<string> ConsumedPaths = [];
	}

	/// <summary>单份表解析后的中间结果。</summary>
	private sealed class Parsed
	{
		public object Values;
		/// <summary>该格是否在文件里被显式写过；用于区分「写了 0」与「没写」。</summary>
		public bool[] Written;
		/// <summary>各维 name→index。</summary>
		public Dictionary<string, int>[] NameIndex;
		/// <summary>各维 index→name（该索引的第一个名字），用于冲突判定。</summary>
		public string[][] NameAt;
	}

	/// <summary>内容不合基本表格式、连候选都算不上的同名文件。</summary>
	private sealed class Invalid
	{
		public string Path;
		public string Reason;
	}

	private sealed class Candidate
	{
		public string Path;
		public List<string[]> Rows;
	}

	/// <summary>
	/// 按声明尺寸加载同名常数表：先选主表，再把其余同名表作为补充合并进来。
	/// 返回 null 表示没有任何候选可用，error 说明原因（文件缺失 / 格式不合规 / 全部不适配）。
	/// </summary>
	internal static Result Load(string varName, int[] lengths, int valueKind, IConstArrayTableFiles files, out string error)
	{
		error = null;
		List<Candidate> candidates = FindCandidates(files, varName, out List<Invalid> invalids, out string discoveryError);
		if (candidates.Count == 0)
		{
			error = discoveryError;
			return null;
		}

		var reasons = new List<string>();
		// 主表严格校验失败的候选不再参与后面的宽松合并：内容写坏的表整份作废，
		// 不能因为「还有备份表能当主表」就把它的坏格悄悄跳过、其余数据并进来。
		var rejected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var unfitCandidates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		Parsed parsedMain = null;
		string mainPath = null;
		int mainIndex = -1;
		List<string> mainWarnings = null;
		for (int i = 0; i < candidates.Count; i++)
		{
			Candidate candidate = candidates[i];
			var trialMainWarnings = new List<string>();
			if (TryParse(candidate.Path, candidate.Rows, lengths, valueKind, true, trialMainWarnings,
				out Parsed parsed, out string reason, out bool unfit))
			{
				mainPath = candidate.Path;
				parsedMain = parsed;
				mainIndex = i;
				mainWarnings = trialMainWarnings;
				break;
			}
			reasons.Add(candidate.Path + " — " + reason);
			rejected[candidate.Path] = reason;
			unfitCandidates[candidate.Path] = unfit;
		}
		if (parsedMain == null)
		{
			error = "同名常数表均不适用于该声明：" + string.Join("；", reasons);
			return null;
		}

		var result = new Result
		{
			Path = mainPath,
			Names = parsedMain.NameIndex,
			Values = parsedMain.Values
		};
		if (mainWarnings != null)
			result.Warnings.AddRange(mainWarnings);
		var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mainPath };
		result.ConsumedPaths.Add(mainPath);
		// 别名表只为「真正参与合并」的文件加载：被作废的候选其 .als 指向的索引已无意义。
		var aliasSources = new List<string> { mainPath };

		// 主表阶段失败的候选：原因要报出来（否则「主表写错、另有备份」时用户完全看不到问题），
		// 并记入已消费，避免这些文件又被 EE_ERD 当成 [编号,名称] 名称表读一遍。
		foreach (KeyValuePair<string, string> pair in rejected)
		{
			// 只是维度/布局不适配的候选是正常的多候选场景，不吵；内容写错才提醒。
			if (!unfitCandidates.TryGetValue(pair.Key, out bool unfit) || !unfit)
				result.Warnings.Add("同名常数表候选 " + pair.Key + " 未通过严格校验（" + pair.Value + "），已整份跳过");
			if (consumed.Add(pair.Key))
				result.ConsumedPaths.Add(pair.Key);
		}

		// 其余同名表作为补充：补数值与名称。补充表解析失败只当它是纯名称表，交给下面。
		for (int i = 0; i < candidates.Count; i++)
		{
			if (i == mainIndex)
				continue;
			Candidate candidate = candidates[i];
			// 主表阶段已判为内容错误的候选，整份跳过（见上面 rejected 的说明）。
			if (rejected.ContainsKey(candidate.Path))
				continue;
			// 先记在临时表里：整份表被拒时这些坏格警告已无意义，不该再报给用户。
			var trialWarnings = new List<string>();
			if (!TryParse(candidate.Path, candidate.Rows, lengths, valueKind, false, trialWarnings,
				out Parsed extension, out _, out _))
				continue;
			result.Warnings.AddRange(trialWarnings);
			MergeValueTable(parsedMain, extension, lengths, valueKind, candidate.Path, result.Warnings);
			aliasSources.Add(candidate.Path);
			if (consumed.Add(candidate.Path))
				result.ConsumedPaths.Add(candidate.Path);
		}

		// EE_ERD 名称表：<名称>.ERD（一维）与 <名称>@N.ERD，只补名称。
		// 这里的 <名称>.ERD 与值表候选是同一批文件：`[编号,名称]` 两列的名称表在候选阶段
		// 会因「不足 3 列」被排除，所以不能因为它在 invalids 里就跳过——先当名称表读，
		// 读成功（真读到了 [编号,名称] 行）才算被常数表接管。
		int dimension = lengths.Length;
		// 含 @1 / @2 布局标记的文件只可能是二维值表，不可能是 [编号,名称] 名称表。
		// 一维声明下这类文件在补充解析阶段被判「维度不适配」而不进 consumed，若不加这一层
		// 就会被 MergeNameFile 把 `0,行1,10` 当成名称读掉（主表该索引没有名字时还会真的注册）。
		// 所以直接记为已消费：不读，也不让 EE_ERD 再按名称表读一遍。
		var layoutMarked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Candidate candidate in candidates)
			if (HasLayoutMarkers(candidate.Rows))
				layoutMarked.Add(candidate.Path);
		for (int d = 0; d < dimension; d++)
		{
			string[] suffixes = dimension == 1 ? ["", "@1"] : ["@" + (d + 1)];
			foreach (string suffix in suffixes)
			{
				foreach (string path in files.FindFiles(varName + suffix + ".erd", true))
				{
					if (consumed.Contains(path))
						continue;
					if (layoutMarked.Contains(path))
					{
						if (consumed.Add(path))
							result.ConsumedPaths.Add(path);
						continue;
					}
					if (!MergeNameFile(files, path, d, lengths[d], parsedMain, result.Warnings,
						out int skippedRows, out string firstBadNote))
						continue;
					aliasSources.Add(path);
					if (consumed.Add(path))
						result.ConsumedPaths.Add(path);
					if (skippedRows > 0)
						result.Warnings.Add(path + " 有 " + skippedRows + " 个有效行不是「编号,名称」格式（首个在 "
							+ firstBadNote + "），已跳过；名称里含逗号时要用双引号把名称包住");
				}
			}
		}
		// 走到这里既没当成值表、也没当成名称表的同名文件，才真的是「不是可用的常数表」。
		// 报告放在名称表之后，避免把合法的两列名称表误报成坏文件。
		foreach (Invalid invalid in invalids)
		{
			if (consumed.Contains(invalid.Path))
				continue;
			result.Warnings.Add("同名文件 " + invalid.Path + " 不是可用的常数表（" + invalid.Reason + "），已跳过");
			if (consumed.Add(invalid.Path))
				result.ConsumedPaths.Add(invalid.Path);
		}
		// 同名 .als 别名表：常数表接管了这些 CSV/ERD，EE_ERD 侧不会再走 UserDefineLoadData，
		// 别名必须在这里补上，否则「同义名写 .als」会静默失效。
		LoadAliasFiles(files, aliasSources, dimension, lengths, parsedMain, result.Warnings);
		return result;
	}

	/// <summary>
	/// 加载同目录下与已消费文件同名的 .als（格式与 EE_ERD 一致：`索引,别名`）。
	/// 正式名优先：已有同名映射的别名直接忽略；索引可以对应多个名字，所以不查索引占用。
	/// 二维表只认 `名称@N.als`（N 从 1 起），因为值表 `名称.csv` 的 .als 无法判断属于哪一维。
	/// </summary>
	private static void LoadAliasFiles(IConstArrayTableFiles files, IEnumerable<string> consumedPaths, int dimension, int[] lengths,
		Parsed main, List<string> warnings)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string source in consumedPaths)
		{
			string stem = Path.GetFileNameWithoutExtension(source);
			int dim = 0;
			if (dimension > 1)
			{
				int at = stem.LastIndexOf('@');
				if (at < 0 || !int.TryParse(stem[(at + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
					|| n < 1 || n > dimension)
					continue;
				dim = n - 1;
			}
			string aliasPath = Path.Combine(Path.GetDirectoryName(source) ?? "", stem + ".als");
			if (!seen.Add(aliasPath) || !files.FileExists(aliasPath))
				continue;
			MergeAliasFile(files, aliasPath, dim, lengths[dim], main, warnings);
		}
	}

	private static void MergeAliasFile(IConstArrayTableFiles files, string path, int dim, int length, Parsed main, List<string> warnings)
	{
		int skipped = 0;
		string firstBadNote = null;
		int validLine = 0;
		try
		{
			foreach (string raw in files.ReadAllLines(path))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == ';')
					continue;
				if (!TrySplitQuoted(line, out string[] cells, out _, out string why))
				{
					// 引号写坏的行有内容但解析不了，按坏行计，原因带出去（与值表同款提示）。
					// why 原本接在「第 M 个字段」后面，单独引用时去掉开头的「的」。
					if (why.StartsWith("的", StringComparison.Ordinal))
						why = why[1..];
					validLine++;
					if (skipped++ == 0)
						firstBadNote = "第 " + validLine + " 个有效行（" + why + "）";
					continue;
				}
				if (IsAllEmpty(cells))
					// 全空字段的行（`,,`）是视觉分隔：跳过、不占有效行号，与值表 rows 的口径一致。
					continue;
				validLine++;
				string badWhy = null;
				int index = 0;
				if (cells.Length < 2)
					badWhy = "不足「编号,别名」两列";
				else if (!int.TryParse(cells[0].Trim().Trim('\uFEFF').Trim(), NumberStyles.Integer,
					CultureInfo.InvariantCulture, out index))
					badWhy = "首列不是编号：" + cells[0].Trim().Trim('\uFEFF').Trim();
				if (badWhy != null)
				{
					if (skipped++ == 0)
						firstBadNote = "第 " + validLine + " 个有效行（" + badWhy + "）";
					continue;
				}
				string name = cells[1].Trim().Trim('\uFEFF').Trim();
				if (name.Length == 0)
					continue;                    // 只写了编号没有别名，与名称表的空名称一样静默跳过
				if (index < 0 || index >= length)
					continue;
				if (main.NameIndex[dim].ContainsKey(name))
					continue;
				main.NameIndex[dim].Add(name, index);
				main.NameAt[dim][index] ??= name;
			}
		}
		catch (Exception e)
		{
			warnings.Add(path + " 读取失败（" + e.Message + "），该别名表未生效");
			return;
		}
		if (skipped > 0)
			warnings.Add(path + " 有 " + skipped + " 个有效行不是「编号,别名」格式（首个在 " + firstBadNote
				+ "），已跳过；别名里含逗号时要用双引号把别名包住");
	}

	/// <summary>
	/// 收集内容符合基本表格式的候选，按 CSV/*.csv → CSV/*.erd → ERB/**/*.erd 排序。
	/// 维度与值类型不在此处判断，由 Load 结合声明逐个校验。
	/// </summary>
	private static List<Candidate> FindCandidates(IConstArrayTableFiles files, string varName, out List<Invalid> invalids, out string error)
	{
		var ret = new List<Candidate>();
		invalids = [];
		error = null;
		if (string.IsNullOrEmpty(varName))
		{
			error = "常数表名称为空";
			return ret;
		}
		var candidates = new List<string>();
		candidates.AddRange(files.FindFiles(varName + ".csv", false));
		candidates.AddRange(files.FindFiles(varName + ".erd", false));
		candidates.AddRange(files.FindFiles(varName + ".erd", true));
		foreach (string candidatePath in candidates)
		{
			if (TryReadTable(files, candidatePath, out List<string[]> tableRows, out string reason))
				ret.Add(new Candidate { Path = candidatePath, Rows = tableRows });
			else
				invalids.Add(new Invalid { Path = candidatePath, Reason = reason });
		}
		if (ret.Count > 0)
			return ret;
		if (invalids.Count > 0)
		{
			var sb = new System.Text.StringBuilder("常数表的格式不合规：");
			for (int i = 0; i < invalids.Count; i++)
			{
				if (i > 0)
					sb.Append("；");
				sb.Append(invalids[i].Path).Append(" — ").Append(invalids[i].Reason);
			}
			error = sb.ToString();
		}
		else
			error = "常数表声明找不到同名文件（CSV/" + varName + ".csv、CSV/" + varName + ".erd，或 ERB 下的 " + varName + ".ERD）";
		return ret;
	}

	// CSV / ERB 目录的文件名索引属于磁盘实现（ConstArrayTableDiskFiles）：按文件名建一次索引，
	// 避免每个常数表声明都递归枚举一次 ERB；重新解析 ERH 时由 HeaderFileLoader.LoadHeaderFiles
	// 入口清空，所以新增或改名的表文件不会被旧索引挡住。

	/// <summary>
	/// 解析单份表。strict 用于主表：非法单元格直接失败并给出位置；
	/// 补充表用宽松模式：非法单元格只记一条警告并跳过，不影响其余数据。
	/// </summary>
	private static bool TryParse(string path, List<string[]> rows, int[] lengths, int valueKind,
		bool strict, List<string> warnings, out Parsed parsed, out string error, out bool unfit)
	{
		parsed = null;
		error = null;
		// unfit：维度、尺寸、标记位置这类「这张表不是给这个声明用的」失败，回退到下一份即可；
		// 其余是内容写错（坏格、名字冲突等），要报给用户，不能当补充表宽松合并。
		unfit = true;
		warnings ??= [];                       // 主表传 null，统一成空表以免新增分支踩空
		int dimension = lengths.Length;
		long size = 1;
		foreach (int len in lengths)
			size *= len;
		if (size <= 0 || size > 1000000)
		{
			error = "常数表对应的数组尺寸非法";
			return false;
		}

		var table = new Parsed
		{
			Values = valueKind == KindStr ? new string[size] : valueKind == KindFloat ? new double[size] : new long[size],
			Written = new bool[size],
			NameIndex = new Dictionary<string, int>[dimension],
			NameAt = new string[dimension][]
		};
		for (int d = 0; d < dimension; d++)
		{
			table.NameIndex[d] = [];
			table.NameAt[d] = new string[lengths[d]];
		}
		if (valueKind == KindStr)
		{
			var strValues = (string[])table.Values;
			for (int i = 0; i < strValues.Length; i++)
				strValues[i] = "";
		}

		bool ok;
		bool hasMarkers = HasMarkers(rows);
		if (hasMarkers && dimension != 2)
		{
			// 只有 (2,1) 与 (1,2) 两处都有标记，才是「二维表配了一维声明」。
			// 否则可能只是字符串值里恰好写了 @1，仍按一维表处理。
			if (HasLayoutMarkers(rows))
			{
				error = "含 @1 / @2 的常数表只能用于二维数组";
				return false;
			}
			hasMarkers = false;
		}
		if (hasMarkers)
		{
			if (!TryLocate(rows, out int vDim, out int hDim, out int vRow, out int vCol, out int hRow, out int hCol, out error))
				return false;
			unfit = false;                      // 尺寸与标记都对上了，之后的失败都算内容写错
			ok = Fill2D(rows, lengths, valueKind, vDim, hDim, vRow, vCol, hRow, hCol,
				table, strict, warnings, path, out error);
		}
		else
		{
			if (dimension != 1)
			{
				error = "不含 @1 / @2 的常数表只能用于一维数组";
				return false;
			}
			unfit = false;
			ok = Fill1D(rows, lengths, valueKind, table, strict, warnings, path, out error);
		}
		if (!ok)
			return false;
		parsed = table;
		return true;
	}

	/// <summary>
	/// @1 / @2 必须分别占据 (2,1) 与 (1,2)。(2,1) 上的标记给出纵向维度编号，
	/// (1,2) 上的标记给出横向维度编号；两者互换即为转置写法。
	/// </summary>
	private static bool TryLocate(List<string[]> rows, out int vDim, out int hDim,
		out int vRow, out int vCol, out int hRow, out int hCol, out string error)
	{
		vDim = hDim = -1;
		vRow = vCol = hRow = hCol = -1;
		error = null;
		for (int r = 0; r < rows.Count; r++)
		{
			for (int c = 0; c < rows[r].Length; c++)
			{
				string cell = rows[r][c];
				if (cell != Marker1 && cell != Marker2)
					continue;
				int dim = cell == Marker1 ? 0 : 1;
				if (r == 1 && c == 0)
				{
					vDim = dim;
					vRow = r;
					vCol = c;
				}
				else if (r == 0 && c == 1)
				{
					hDim = dim;
					hRow = r;
					hCol = c;
				}
				else
				{
					error = "常数表的 @1 / @2 必须放在 (2,1) 与 (1,2) 两处（哪个标记在哪一处均可）";
					return false;
				}
			}
		}
		if (vDim < 0 || hDim < 0 || vDim == hDim)
		{
			error = "常数表缺少 @1 / @2 标记，或两标记重复";
			return false;
		}
		return true;
	}

	private static bool Fill2D(List<string[]> rows, int[] lengths, int valueKind, int vDim, int hDim,
		int vRow, int vCol, int hRow, int hCol, Parsed table, bool strict, List<string> warnings,
		string path, out string error)
	{
		error = null;
		int valueStartRow = vRow + 1;
		int valueStartCol = hCol + 1;
		int maxCols = 0;
		foreach (string[] row in rows)
			maxCols = Math.Max(maxCols, row.Length);

		// 纵向：橙色注册数在标记所在列，蓝色注册名在右一列；蓝色名所在行即横向标记下一行。
		// 有注册名却没写注册数的，主表报错、补充表跳过该行。
		var rowIndex = new int[rows.Count];
		int lastRowIndex = -1;
		for (int r = valueStartRow; r < rows.Count; r++)
		{
			rowIndex[r] = -1;
			string[] row = rows[r];
			string cell = vCol < row.Length ? row[vCol] : "";
			int index;
			if (cell.Length > 0)
			{
				if (!int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
				{
					if (strict)
					{
						error = "第 " + (r + 1) + " 个有效行、第 " + (vCol + 1) + " 列的注册数不是非负整数：" + cell;
						return false;
					}
					warnings.Add(ExtensionNote(path) + "第 " + (r + 1) + " 个有效行、第 " + (vCol + 1)
						+ " 列的注册数不是非负整数：" + cell + "；跳过该行");
					continue;
				}
				index = n;
				lastRowIndex = n;
			}
			else
			{
				// 有注册名就必须写明注册数；两者都空才按前一格顺延。
				string nameCell = vCol + 1 < row.Length ? row[vCol + 1] : "";
				if (nameCell.Length > 0)
				{
					string msg = "第 " + (r + 1) + " 个有效行、第 " + (vCol + 1) + " 列写了注册名「" + nameCell
						+ "」却没有注册数";
					if (strict)
					{
						error = msg + "；有注册名就必须写注册数";
						return false;
					}
					warnings.Add(ExtensionNote(path) + msg + "；跳过该行");
					continue;
				}
				index = ++lastRowIndex;
			}
			rowIndex[r] = index;
			if (index >= 0 && index < lengths[vDim] && vCol + 1 < row.Length && row[vCol + 1].Length > 0)
			{
				string conflict = TryAddName(table, vDim, index, row[vCol + 1]);
				if (conflict != null)
				{
					string where = "第 " + (r + 1) + " 个有效行、第 " + (vCol + 2) + " 列";
					if (strict)
					{
						error = where + "：" + conflict;
						return false;
					}
					warnings.Add(ExtensionNote(path) + where + "：" + conflict + "；跳过该名字");
				}
			}
		}

		// 横向：橙色注册数在标记所在行，蓝色注册名在下一行；注册数与注册名都空时才按前一列顺延。
		string[] numRow = hRow < rows.Count ? rows[hRow] : [];
		string[] nameRow = hRow + 1 < rows.Count ? rows[hRow + 1] : [];
		var colIndex = new int[maxCols];
		int lastColIndex = -1;
		for (int c = valueStartCol; c < maxCols; c++)
		{
			colIndex[c] = -1;
			string cell = c < numRow.Length ? numRow[c] : "";
			int index;
			if (cell.Length > 0)
			{
				if (!int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
				{
					if (strict)
					{
						error = "第 " + (hRow + 1) + " 个有效行、第 " + (c + 1) + " 列的注册数不是非负整数：" + cell;
						return false;
					}
					warnings.Add(ExtensionNote(path) + "第 " + (hRow + 1) + " 个有效行、第 " + (c + 1)
						+ " 列的注册数不是非负整数：" + cell + "；跳过该列");
					continue;
				}
				index = n;
				lastColIndex = n;
			}
			else
			{
				// 有注册名就必须写明注册数；两者都空才按前一列顺延。
				string nameCell = c < nameRow.Length ? nameRow[c] : "";
				if (nameCell.Length > 0)
				{
					string msg = "第 " + (hRow + 1) + " 个有效行、第 " + (c + 1) + " 列写了注册名「" + nameCell
						+ "」却没有注册数";
					if (strict)
					{
						error = msg + "；有注册名就必须写注册数";
						return false;
					}
					warnings.Add(ExtensionNote(path) + msg + "；跳过该列");
					continue;
				}
				index = ++lastColIndex;
			}
			colIndex[c] = index;
			if (index >= 0 && index < lengths[hDim] && c < nameRow.Length && nameRow[c].Length > 0)
			{
				string conflict = TryAddName(table, hDim, index, nameRow[c]);
				if (conflict != null)
				{
					string where = "第 " + (hRow + 2) + " 个有效行、第 " + (c + 1) + " 列";
					if (strict)
					{
						error = where + "：" + conflict;
						return false;
					}
					warnings.Add(ExtensionNote(path) + where + "：" + conflict + "；跳过该名字");
				}
			}
		}

		bool warnedOverflow = false;
		// 数据行比表头长不构成错误：表头故意留空靠顺延列是合法写法（注册数/注册名都空 = 顺延），
		// 所以「值里的逗号被切开」只能靠越界判定（下表 ci >= lengths[hDim]）间接发现。
		for (int r = valueStartRow; r < rows.Count; r++)
		{
			string[] row = rows[r];
			int ri = rowIndex[r];
			if (ri < 0 || ri >= lengths[vDim])
				continue;
			for (int c = valueStartCol; c < row.Length && c < maxCols; c++)
			{
				int ci = colIndex[c];
				if (ci < 0)
					continue;
				if (ci >= lengths[hDim])
				{
					// 顺延列或「值里的逗号被切开」都会跑到声明宽度之外，后者是典型的不建议写法。
					if (!warnedOverflow && row[c].Length > 0)
					{
						warnedOverflow = true;
						warnings.Add(TableNote(path, strict) + "第 " + (r + 1) + " 个有效行、第 " + (c + 1)
							+ " 列的值超出声明宽度（" + lengths[hDim] + "），该格被忽略；值里含逗号时要用双引号把整个值包住（如 \"a,b\"）");
					}
					continue;
				}
				if (!TryConvert(row[c], valueKind, out object value, out string convertError))
				{
					if (strict)
					{
						error = "第 " + (r + 1) + " 个有效行、第 " + (c + 1) + " 列的值" + convertError + "：" + row[c];
						return false;
					}
					warnings.Add(ExtensionNote(path) + "第 " + (r + 1) + " 个有效行、第 " + (c + 1)
						+ " 列的值" + convertError + "：" + row[c] + "；跳过该格");
					continue;
				}
				if (value == null)
					continue;
				int first = vDim == 0 ? ri : ci;
				int second = vDim == 0 ? ci : ri;
				int flat = first * lengths[1] + second;
				SetValue(table.Values, flat, valueKind, value);
				table.Written[flat] = true;
			}
		}
		return true;
	}

	private static bool Fill1D(List<string[]> rows, int[] lengths, int valueKind, Parsed table,
		bool strict, List<string> warnings, string path, out string error)
	{
		error = null;
		int lastIndex = -1;
		bool warnedOverflow = false;
		for (int r = 0; r < rows.Count; r++)
		{
			string[] row = rows[r];
			int line = r + 1;
			if (row.Length < 3)
			{
				if (strict)
				{
					error = "第 " + line + " 个有效行不足 3 列";
					return false;
				}
				warnings.Add(ExtensionNote(path) + "第 " + line + " 个有效行不足 3 列；跳过该行");
				continue;
			}
			// 一维表固定三列，多出来只可能是值里的逗号没加引号被切开了。
			if (row.Length > 3)
			{
				string msg = "第 " + line + " 个有效行有 " + row.Length + " 列，多于一维表固定的 3 列："
					+ "值里含逗号时要用双引号把整个值包住（如 \"a,b\"）";
				if (strict)
				{
					error = msg;
					return false;
				}
				warnings.Add(ExtensionNote(path) + msg + "；多出的列被忽略");
			}
			int index;
			if (row[0].Length > 0)
			{
				if (!int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
				{
					if (strict)
					{
						error = "第 " + line + " 个有效行的注册数不是非负整数：" + row[0];
						return false;
					}
					warnings.Add(ExtensionNote(path) + "第 " + line + " 个有效行的注册数不是非负整数："
						+ row[0] + "；跳过该行");
					continue;
				}
				index = n;
				lastIndex = n;
			}
			else
			{
				// 有注册名就必须写明注册数；两者都空才按前一行顺延。
				if (row[1].Length > 0)
				{
					string msg = "第 " + line + " 个有效行写了注册名「" + row[1] + "」却没有注册数";
					if (strict)
					{
						error = msg + "；有注册名就必须写注册数";
						return false;
					}
					warnings.Add(ExtensionNote(path) + msg + "；跳过该行");
					continue;
				}
				index = ++lastIndex;
			}
			if (index < 0 || index >= lengths[0])
			{
				if (!warnedOverflow && row[2].Length > 0)
				{
					warnedOverflow = true;
					warnings.Add(TableNote(path, strict) + "第 " + line + " 个有效行的索引 " + index
						+ " 超出声明长度（" + lengths[0] + "），该行已忽略；值里含逗号时要用双引号把整个值包住（如 \"a,b\"）");
				}
				continue;
			}
			if (row[1].Length > 0)
			{
				string conflict = TryAddName(table, 0, index, row[1]);
				if (conflict != null)
				{
					if (strict)
					{
						error = "第 " + line + " 个有效行：" + conflict;
						return false;
					}
					warnings.Add(ExtensionNote(path) + "第 " + line + " 个有效行：" + conflict + "；跳过该名字");
				}
			}
			if (!TryConvert(row[2], valueKind, out object value, out string convertError))
			{
				if (strict)
				{
					error = "第 " + line + " 个有效行的值" + convertError + "：" + row[2];
					return false;
				}
				warnings.Add(ExtensionNote(path) + "第 " + line + " 个有效行的值" + convertError
					+ "：" + row[2] + "；跳过该格");
				continue;
			}
			if (value != null)
			{
				SetValue(table.Values, index, valueKind, value);
				table.Written[index] = true;
			}
		}
		return true;
	}

	/// <summary>
	/// 把补充表的名称与数值并进主表：主表没写的格子、没命名的索引才填，
	/// 冲突一律记警告并以主表为准（与预设变量的 ERD 扩展同一套规则）。
	/// </summary>
	private static void MergeValueTable(Parsed main, Parsed extension, int[] lengths, int valueKind,
		string extensionPath, List<string> warnings)
	{
		// 两种布局（@1 / @2 谁在 (2,1)）在 Fill2D 里都已换算成「按维度编号」的展平索引，
		// 所以这里直接按 flat 合并即可；名字的键 likewise 是维度编号，与表格摆放方向无关。
		for (int d = 0; d < lengths.Length; d++)
			MergeNames(main, extension.NameIndex[d], d, lengths[d], extensionPath, warnings);

		for (int i = 0; i < main.Written.Length; i++)
		{
			if (!extension.Written[i])
				continue;
			object value = GetValue(extension.Values, i, valueKind);
			if (!main.Written[i])
			{
				SetValue(main.Values, i, valueKind, value);
				main.Written[i] = true;
				continue;
			}
			object current = GetValue(main.Values, i, valueKind);
			if (!SameValue(current, value, valueKind))
				warnings.Add(ExtensionNote(extensionPath) + "索引 " + IndexText(i, lengths) + " 的值与主表不同（主表="
					+ FormatValue(current, valueKind) + ", 补充=" + FormatValue(value, valueKind) + "）；以主表为准");
		}
	}

	private static void MergeNames(Parsed main, Dictionary<string, int> extensionIndex,
		int dim, int length, string extensionPath, List<string> warnings)
	{
		foreach (KeyValuePair<string, int> pair in extensionIndex)
			MergeOneName(main, dim, length, pair.Value, pair.Key, extensionPath, warnings);
	}

	/// <summary>合并一条「名字→索引」，冲突只警告并以主表为准。</summary>
	private static void MergeOneName(Parsed main, int dim, int length, int index, string name,
		string extensionPath, List<string> warnings)
	{
		if (main.NameIndex[dim].TryGetValue(name, out int exists))
		{
			if (exists != index)
				warnings.Add(ExtensionNote(extensionPath) + DimText(dim) + "索引 " + index + " 的名字\"" + name
				+ "\"已存在于索引 " + exists + "；跳过");
			return;
		}
		if (index < 0 || index >= length)
			return;
		string current = main.NameAt[dim][index];
		if (current != null)
		{
			if (!string.Equals(current, name, StringComparison.Ordinal))
				warnings.Add(ExtensionNote(extensionPath) + DimText(dim) + "索引 " + index + " 的名字与主表不同（主表=\""
					+ current + "\", 补充=\"" + name + "\"）；以主表为准");
			return;
		}
		main.NameIndex[dim].Add(name, index);
		main.NameAt[dim][index] = name;
	}

	/// <summary>
	/// 读取 EE_ERD 风格的维度名称表（&lt;名称&gt;.ERD / &lt;名称&gt;@N.ERD）。
	/// 每行为 [编号, 名称]，切分与值表同一套规则（名称含逗号时用双引号包住），
	/// 只补名称，冲突规则同上；读取失败记一条警告，不静默吞掉。
	/// 返回 true 表示这份文件确实是名称表（至少有一行是 [编号,名称]），由调用方记为已消费；
	/// false 表示里面没有一行像名称表，交给调用方按「不是可用的常数表」报告——所以坏行信息
	/// 用出参交给调用方，只在它真的被当成名称表时才报，避免同一个文件收到两条重复警告。
	/// firstBadNote 是首个坏行的位置与原因（如「第 2 个有效行（双引号没有闭合）」），
	/// 不让「引号没闭合」这种具体原因被笼统的「格式不对」吞掉。
	/// </summary>
	private static bool MergeNameFile(IConstArrayTableFiles files, string path, int dim, int length, Parsed main, List<string> warnings,
		out int skippedRows, out string firstBadNote)
	{
		bool sawRow = false;
		skippedRows = 0;
		firstBadNote = null;
		int validLine = 0;
		try
		{
			foreach (string raw in files.ReadAllLines(path))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == ';')
					continue;
				if (!TrySplitQuoted(line, out string[] cells, out _, out string why))
				{
					// 引号写坏的行有内容但解析不了，按坏行计，原因带出去（与值表同款提示）。
					// why 原本接在「第 M 个字段」后面，单独引用时去掉开头的「的」。
					if (why.StartsWith("的", StringComparison.Ordinal))
						why = why[1..];
					validLine++;
					if (skippedRows++ == 0)
						firstBadNote = "第 " + validLine + " 个有效行（" + why + "）";
					continue;
				}
				if (IsAllEmpty(cells))
					// 全空字段的行（`,,`）是视觉分隔：跳过、不占有效行号，与值表 rows 的口径一致。
					continue;
				validLine++;
				string badWhy = null;
				int index = 0;
				if (cells.Length < 2)
					badWhy = "不足「编号,名称」两列";
				else if (!int.TryParse(cells[0].Trim().Trim('\uFEFF').Trim(), NumberStyles.Integer,
					CultureInfo.InvariantCulture, out index))
					badWhy = "首列不是编号：" + cells[0].Trim().Trim('\uFEFF').Trim();
				if (badWhy != null)
				{
					if (skippedRows++ == 0)
						firstBadNote = "第 " + validLine + " 个有效行（" + badWhy + "）";
					continue;
				}
				sawRow = true;
				string name = cells[1].Trim().Trim('\uFEFF').Trim();
				if (name.Length == 0)
					continue;
				MergeOneName(main, dim, length, index, name, path, warnings);
			}
		}
		catch (Exception e)
		{
			// 名称表是可选补充，读取失败不影响值表；但要告知用户，避免名称下标莫名找不到。
			// 读不出来的文件既判不了是名称表还是坏值表，按名称表处理（不再补一条误导性的
			// 「不是可用的常数表」）。
			warnings.Add(path + " 读取失败（" + e.Message + "），该名称表未生效");
			return true;
		}
		return sawRow;
	}

	/// <summary>该行的所有字段是否都为空（`,,` 这类视觉分隔行），与值表丢弃空行的口径一致。</summary>
	private static bool IsAllEmpty(string[] cells)
	{
		foreach (string cell in cells)
			if (cell.Trim().Length > 0)
				return false;
		return true;
	}

	/// <summary>转换非空值；空数值返回 value=null，表示保持默认值。非空但非法时返回 false。</summary>
	private static bool TryConvert(string cell, int valueKind, out object value, out string error)
	{
		value = null;
		error = null;
		// 留空 = 保持默认值（0 / 0.0 / ""）。三种类型一致地当作「没写」，
		// 既保持“空格就是默认值”的约定，也让补充表还能填这一格。
		if (cell.Length == 0)
			return true;
		if (valueKind == KindStr)
		{
			value = cell;
			return true;
		}
		if (valueKind == KindFloat)
		{
			if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double f))
			{
				error = "不是有效的浮点数";
				return false;
			}
			value = f;
			return true;
		}
		if (!long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i))
		{
			error = "不是有效的整数";
			return false;
		}
		value = i;
		return true;
	}

	/// <summary>
	/// 登记一条「索引→名字」。同名已在别处登记时保留先出现者（返回 null，不算冲突）；
	/// 该索引已被别的名字占用时返回冲突说明，由调用方按主表报错 / 补充表警告处理。
	/// </summary>
	private static string TryAddName(Parsed table, int dim, int index, string name)
	{
		if (table.NameIndex[dim].ContainsKey(name))
			return null;
		string current = table.NameAt[dim][index];
		if (current != null)
			return "索引 " + index + " 已有名字「" + current + "」，不能再取名「" + name + "」";
		table.NameIndex[dim].Add(name, index);
		table.NameAt[dim][index] = name;
		return null;
	}

	private static void SetValue(object values, int index, int valueKind, object value)
	{
		if (valueKind == KindStr)
			((string[])values)[index] = (string)value;
		else if (valueKind == KindFloat)
			((double[])values)[index] = (double)value;
		else
			((long[])values)[index] = (long)value;
	}

	private static object GetValue(object values, int index, int valueKind)
	{
		if (valueKind == KindStr)
			return ((string[])values)[index];
		if (valueKind == KindFloat)
			return ((double[])values)[index];
		return ((long[])values)[index];
	}

	private static bool SameValue(object a, object b, int valueKind)
	{
		if (valueKind == KindStr)
			return string.Equals((string)a, (string)b, StringComparison.Ordinal);
		if (valueKind == KindFloat)
		{
			// NaN != NaN，直接比较会一直报「值不同」；两边都是 NaN 视为相同。
			double x = (double)a, y = (double)b;
			return x == y || (double.IsNaN(x) && double.IsNaN(y));
		}
		return (long)a == (long)b;
	}

	private static string FormatValue(object value, int valueKind)
	{
		if (valueKind == KindStr)
			return "\"" + value + "\"";
		return Convert.ToString(value, CultureInfo.InvariantCulture);
	}

	private static string IndexText(int flat, int[] lengths)
	{
		if (lengths.Length < 2)
			return flat.ToString(CultureInfo.InvariantCulture);
		return (flat / lengths[1]).ToString(CultureInfo.InvariantCulture) + ":"
			+ (flat % lengths[1]).ToString(CultureInfo.InvariantCulture);
	}

	private static string DimText(int dim)
	{
		return dim <= 0 ? "" : "第 " + (dim + 1) + " 维";
	}

	private static string ExtensionNote(string path)
	{
		return "常数表补充表(" + path + ")中，";
	}

	private static string TableNote(string path)
	{
		return "常数表(" + path + ")中，";
	}

	/// <summary>主表用「常数表(...)」，补充表用「常数表补充表(...)」，与同一次合并里其它警告的称呼一致。</summary>
	private static string TableNote(string path, bool strict)
	{
		return strict ? TableNote(path) : ExtensionNote(path);
	}

	/// <summary>
	/// 读取表格文本。切分规则与 CSV 一致，没有开关：以双引号开头的字段按引号字段解析
	/// （引号内的逗号属于值，`""` 表示一个字面引号），其余字段取到下一个逗号。
	/// 引号没闭合、或结束引号后还有内容，算格式错误并指出第几个字段。
	/// 「第 N 个有效行」的口径与 rows 一致：全空字段的行（`,,`）不占行号，
	/// 所以引号错误与后面 Fill / Check 报的行号可以互相对照。
	/// </summary>
	private static List<string[]> ReadGrid(IConstArrayTableFiles files, string path, out string error)
	{
		error = null;
		var lines = new List<string>();
		foreach (string raw in files.ReadAllLines(path))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == ';')
				continue;
			lines.Add(line);
		}

		var rows = new List<string[]>();
		for (int n = 0; n < lines.Count; n++)
		{
			if (!TrySplitQuoted(lines[n], out string[] cells, out int badField, out string why))
			{
				// 行号取「下一个有效行」：能切开且有内容的行会排在 rows.Count + 1。
				error = "第 " + (rows.Count + 1) + " 个有效行的第 " + badField + " 个字段" + why;
				return null;
			}
			bool any = false;
			for (int i = 0; i < cells.Length; i++)
			{
				cells[i] = cells[i].Trim().Trim('\uFEFF').Trim();
				if (cells[i].Length > 0)
					any = true;
			}
			if (any)
				rows.Add(cells);
		}
		return rows;
	}

	/// <summary>
	/// CSV 切分（RFC 4180 的读取侧，去掉「引号里不许出现裸引号」的严格性）：逗号只在引号外部分隔。
	/// 只有字段的第一个非空白字符是双引号时，该字段才按引号字段解析；引号内的逗号、单引号、
	/// 反斜杠、`;`、制表符都是普通字符，只有连续两个双引号 `""` 表示一个字面双引号（没有反斜杠转义）。
	/// 结束引号之后只允许空白，然后是逗号或行尾；否则就是写错了，给出按规则应当的写法。
	/// 不以双引号开头的字段照原样取到下一个逗号，所以标记、注册数、注册名都不用加引号，
	/// 值里中间位置的双引号（如 `他说"你好"`）也原样保留。
	/// </summary>
	internal static bool TrySplitQuoted(string line, out string[] cells, out int badField, out string why)
	{
		var list = new List<string>();
		var sb = new System.Text.StringBuilder();
		badField = 0;
		why = null;
		cells = null;
		int i = 0;
		while (true)
		{
			while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
				i++;
			int fieldStart = i;
			sb.Clear();
			if (i < line.Length && line[i] == '"')
			{
				i++;
				bool closed = false;
				while (i < line.Length)
				{
					char ch = line[i];
					if (ch == '"')
					{
						if (i + 1 < line.Length && line[i + 1] == '"')
						{
							sb.Append('"');
							i += 2;
							continue;
						}
						i++;
						closed = true;
						break;
					}
					sb.Append(ch);
					i++;
				}
				if (!closed)
				{
					badField = list.Count + 1;
					why = "的双引号没有闭合（值里的引号要写成两个双引号，整段值也要用一对双引号包住）";
					return false;
				}
				while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
					i++;
				if (i < line.Length && line[i] != ',')
				{
					// 到这里引号是成对的，所以 fieldStart..i 是一段完整、可原样转义的内容。
					int end = line.IndexOf(',', i);
					string raw = end < 0 ? line[fieldStart..] : line[fieldStart..end];
					badField = list.Count + 1;
					why = "在结束引号后还有内容（值里的引号要写成两个双引号）；按这个规则整段值要写成 "
						+ "\"" + raw.Replace("\"", "\"\"") + "\"";
					return false;
				}
			}
			else
			{
				int start = i;
				while (i < line.Length && line[i] != ',')
					i++;
				sb.Append(line, start, i - start);
			}
			list.Add(sb.ToString());
			if (i >= line.Length)
				break;
			i++;                                    // 跳过逗号
			if (i >= line.Length)
			{
				list.Add("");                       // 行尾逗号 = 最后一格留空，与 Split(',') 一致
				break;
			}
		}
		cells = [.. list];
		return true;
	}

	private static bool TryReadTable(IConstArrayTableFiles files, string path, out List<string[]> rows, out string reason)
	{
		reason = null;
		try
		{
			rows = ReadGrid(files, path, out reason);
			if (rows == null)
				return false;
		}
		catch (Exception e)
		{
			rows = null;
			reason = "读取失败（" + e.Message + "）";
			return false;
		}
		return CheckTable(rows, out reason);
	}

	/// <summary>
	/// 判定内容是否为本表格式，失败时给出具体原因。
	/// 含 @1 / @2 的按二维表；否则要求每行 [注册数, 注册名, 值] 三列。
	/// </summary>
	private static bool CheckTable(List<string[]> rows, out string reason)
	{
		reason = null;
		if (rows.Count == 0)
		{
			reason = "空文件，或只有空行与注释行";
			return false;
		}
		if (HasMarkers(rows))
			return true;
		for (int i = 0; i < rows.Count; i++)
		{
			if (rows[i].Length < 3)
			{
				reason = "第 " + (i + 1) + " 个有效行不足 3 列（一维表需要 [注册数,注册名,值]）";
				return false;
			}
			if (rows[i][0].Length > 0 && !int.TryParse(rows[i][0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
			{
				reason = "第 " + (i + 1) + " 个有效行的首列不是编号";
				return false;
			}
		}
		// 注册数与注册名都留空的行按前一行顺延，所以整表全是顺延行也合法（首行落到索引 0）。
		return true;
	}

	/// <summary>二维布局的两个标记位置：(2,1) 与 (1,2) 是否都有 @1 / @2。</summary>
	private static bool HasLayoutMarkers(List<string[]> rows)
	{
		bool vertical = rows.Count > 1 && rows[1].Length > 0 && IsMarker(rows[1][0]);
		bool horizontal = rows.Count > 0 && rows[0].Length > 1 && IsMarker(rows[0][1]);
		return vertical && horizontal;
	}

	private static bool IsMarker(string cell)
	{
		return cell == Marker1 || cell == Marker2;
	}

	private static bool HasMarkers(List<string[]> rows)
	{
		foreach (string[] row in rows)
			foreach (string cell in row)
				if (cell == Marker1 || cell == Marker2)
					return true;
		return false;
	}
}
