// ConstantData.PresetErd.cs —— 预设变量同名 ERD 合并（Emuera-SKIA Skiav13.0 snake 语义）功能域，
// 自 ConstantData.cs 拆出（主文件已接近 2000 行只减不增约束）。规则本体在
// src/Core/Compatibility/PresetErdMerge.cs（纯函数 + xUnit），本文件只负责扫描/IO/警告。
using System;
using System.Collections.Generic;
using System.IO;
using GEmuera.Core.Compatibility;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData
{
	internal sealed partial class ConstantData
	{
		/// <summary>预设变量每个索引的名字来源标记：-1 空；0 来自 CSV；1 来自 ERD。</summary>
		private sbyte[][] presetNameSource;
		/// <summary>ItemPrice 每个索引的价格来源标记：-1 未设；0 CSV；1 ERD。
		/// long 默认 0 无法区分"未设置"与"显式 0"，必须额外追踪（CSV 显式 0 价不得被 ERD 覆盖）。</summary>
		private sbyte[] presetPriceSource;

		/// <summary>
		/// 预设变量名 → names[] 槽位。与上游 mergePresetErdFromErbDir 的
		/// presetErdSlotByName 同构：合并侧（ERD 文件主名）与查询侧（EXIST_IN_* 变量名）共用。
		/// CDFLAG 本体与角色模板名（NAME/CALLNAME 等）无独立名字槽，不支持。
		/// </summary>
		private static readonly Dictionary<string, int> presetErdSlotByName =
			new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["ABL"] = ablIndex,
			["EXP"] = expIndex,
			["TALENT"] = talentIndex,
			["PALAM"] = paramIndex,
			["UP"] = paramIndex,
			["DOWN"] = paramIndex,
			["JUEL"] = paramIndex,
			["GOTJUEL"] = paramIndex,
			["CUP"] = paramIndex,
			["CDOWN"] = paramIndex,
			["TRAIN"] = trainIndex,
			["TRAINNAME"] = trainIndex,
			["MARK"] = markIndex,
			["ITEM"] = itemIndex,
			["ITEMSALES"] = itemIndex,
			["ITEMPRICE"] = itemIndex,
			["BASE"] = baseIndex,
			["LOSEBASE"] = baseIndex,
			["MAXBASE"] = baseIndex,
			["DOWNBASE"] = baseIndex,
			["SOURCE"] = sourceIndex,
			["EX"] = exIndex,
			["NOWEX"] = exIndex,
			["STR"] = strIndex,
			["EQUIP"] = equipIndex,
			["TEQUIP"] = tequipIndex,
			["FLAG"] = flagIndex,
			["TFLAG"] = tflagIndex,
			["CFLAG"] = cflagIndex,
			["TCVAR"] = tcvarIndex,
			["CSTR"] = cstrIndex,
			["STAIN"] = stainIndex,
			["CDFLAG1"] = cdflag1Index,
			["CDFLAGNAME1"] = cdflag1Index,
			["CDFLAG2"] = cdflag2Index,
			["CDFLAGNAME2"] = cdflag2Index,
			["STRNAME"] = strnameIndex,
			["TSTR"] = tstrnameIndex,
			["SAVESTR"] = savestrnameIndex,
			["GLOBAL"] = globalIndex,
			["GLOBALS"] = globalsIndex,
			["DAY"] = dayIndex,
			["TIME"] = timeIndex,
			["MONEY"] = moneyIndex,
		};

		/// <summary>LoadData 初始化阶段调用：来源追踪数组必须在任何 loadDataTo 之前就绪。</summary>
		private void InitializePresetErdNameSources()
		{
			presetNameSource = new sbyte[countNameCsv][];
			for (int i = 0; i < countNameCsv; i++)
			{
				presetNameSource[i] = new sbyte[MaxDataList[i]];
				Array.Fill(presetNameSource[i], PresetErdMerge.NameSourceEmpty);
			}
			presetPriceSource = new sbyte[MaxDataList[itemIndex]];
			Array.Fill(presetPriceSource, PresetErdMerge.NameSourceEmpty);
		}

		/// <summary>loadDataTo 写入 CSV 名字时记录来源。并行加载按槽分离，无写竞争。</summary>
		private void MarkPresetNameFromCsv(int slot, int index)
		{
			if (presetNameSource == null || (uint)slot >= (uint)presetNameSource.Length)
				return;
			sbyte[] sources = presetNameSource[slot];
			if (sources != null && (uint)index < (uint)sources.Length)
				sources[index] = PresetErdMerge.NameSourceCsv;
		}

		/// <summary>loadDataTo 写入 CSV 价格时记录来源（仅 ITEM 槽持有价格数组）。</summary>
		private void MarkPresetPriceFromCsv(Int64[] targetI, int index)
		{
			if (presetPriceSource == null || targetI == null || !ReferenceEquals(targetI, ItemPrice))
				return;
			if ((uint)index < (uint)presetPriceSource.Length)
				presetPriceSource[index] = PresetErdMerge.NameSourceCsv;
		}

		/// <summary>
		/// 从 ERB 目录递归扫描与预设变量同名的 .erd（大小写不敏感、按路径排序保证多文件确定性），
		/// 在全部 CSV 加载后、逆引字典建立前合并。调用条件（Config.UseERD + snake capability）
		/// 由 LoadData 决定；本方法只防御性检查追踪数组是否就绪。
		/// </summary>
		private void MergePresetErdFromErbDir()
		{
			if (presetNameSource == null)
				return;
			string erbDir = Program.ErbDir;
			if (string.IsNullOrEmpty(erbDir) || !uEmuera.Utils.DirectoryExists(erbDir))
				return;

			// Android/大小写敏感文件系统上 *.erd 与 *.ERD 可能是不同集合；两种模式都查并按路径去重。
			var files = new List<string>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string pattern in PresetErdFilePatterns)
			{
				List<string> paths;
				try
				{
					paths = uEmuera.Utils.GetFilePaths(erbDir, pattern, SearchOption.AllDirectories);
				}
				catch
				{
					continue;
				}
				if (paths == null)
					continue;
				for (int i = 0; i < paths.Count; i++)
				{
					if (seen.Add(paths[i]))
						files.Add(paths[i]);
				}
			}
			if (files.Count == 0)
				return;
			files.Sort(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < files.Count; i++)
			{
				string stem = Path.GetFileNameWithoutExtension(files[i]);
				if (string.IsNullOrEmpty(stem) || !presetErdSlotByName.TryGetValue(stem, out int slot))
					continue;
				MergePresetErdFile(files[i], slot);
			}
		}

		private static readonly string[] PresetErdFilePatterns = { "*.erd", "*.ERD" };

		private void MergePresetErdFile(string erdPath, int slot)
		{
			string[] target = names[slot];
			sbyte[] nameSources = presetNameSource != null && (uint)slot < (uint)presetNameSource.Length
				? presetNameSource[slot]
				: null;
			if (target == null || nameSources == null)
				return;
			bool isItemSlot = slot == itemIndex;

			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.Open(erdPath))
			{
				if (output != null)
					output.PrintError(eReader.Filename + "のオープンに失敗しました");
				return;
			}
			try
			{
				StringStream st;
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					ScriptPosition position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					string source = st.RowString;
					int startOffset = st.CurrentPosition;
					PresetErdMerge.LineParseResult parse = PresetErdMerge.TryParseLine(
						source.AsSpan(startOffset), out PresetErdMerge.Row row);
					if (parse == PresetErdMerge.LineParseResult.MissingComma)
					{
						ParserMediator.Warn("\",\"が必要です", position, 1);
						continue;
					}
					if (parse == PresetErdMerge.LineParseResult.IndexNotInteger)
					{
						ParserMediator.Warn("一列目の値を整数値に変換できません", position, 1);
						continue;
					}
					if (target.Length == 0)
					{
						ParserMediator.Warn("禁止設定された名前配列です", position, 2);
						break;
					}
					if ((uint)row.Index >= (uint)target.Length)
					{
						ParserMediator.Warn(row.Index + "は配列の範囲外です", position, 1);
						continue;
					}

					int rowIndex = row.Index;					if (!PresetErdMerge.TryApply(
						in row,
						rowIndex,
						target,
						nameSources,
						ItemPrice,
						presetPriceSource,
						isItemSlot,
						name => NameExistsInOtherSlot(slot, rowIndex, name),
						out PresetErdMerge.ApplyResult apply))
					{
						continue;
					}

					switch (apply.Name)
					{
						case PresetErdMerge.NameApplyResult.DuplicateAcrossSlots:
							ParserMediator.Warn(
								"ERDの名前 \"" + row.Name + "\" は他の名前槽で既に使用されています（スキップしました）",
								position, 1);
							break;
						case PresetErdMerge.NameApplyResult.ConflictWithCsv:
							ParserMediator.Warn(
								"ERDの名前 \"" + row.Name + "\" はCSVの名前 \"" + target[rowIndex] + "\" と異なります（CSVを優先）",
								position, 1);
							break;
					}

					switch (apply.Price)
					{
						case PresetErdMerge.PriceApplyResult.Filled:
							break;
						case PresetErdMerge.PriceApplyResult.ConflictWithCsv:
							ParserMediator.Warn(
								"ERDの価格(" + row.Price.ToString() + ")がCSVの価格(" + ItemPrice[rowIndex].ToString() + ")と異なります（CSVを優先）",
								position, 1);
							break;
						case PresetErdMerge.PriceApplyResult.Invalid:
							ParserMediator.Warn("価格が整数値に変換できません", position, 1);
							break;
						case PresetErdMerge.PriceApplyResult.OutOfRange:
							ParserMediator.Warn(rowIndex + "は配列の範囲外です", position, 1);
							break;
					}
				}
			}
			catch
			{
				uEmuera.Media.SystemSounds.Hand.Play();
				if (output != null)
					output.PrintError("予期しないエラーが発生しました");
			}
			finally
			{
				eReader.Close();
			}
		}

		/// <summary>该名字是否已存在于其他槽（同槽同索引除外；上游跨槽检查含 STR 槽）。</summary>
		private bool NameExistsInOtherSlot(int slot, int index, string name)
		{
			if (string.IsNullOrEmpty(name))
				return false;
			for (int i = 0; i < names.Length; i++)
			{
				string[] arr = names[i];
				if (arr == null)
					continue;
				for (int j = 0; j < arr.Length; j++)
				{
					if (i == slot && j == index)
						continue;
					if (string.Equals(arr[j], name, StringComparison.Ordinal))
						return true;
				}
			}
			return false;
		}

		/// <summary>指定预设变量在指定索引的名字来源：-1 空或不支持；0 CSV；1 ERD。
		/// 别名是透明的：返回的是该槽正名的来源，而不是别名自身的来源。</summary>
		public bool ExistPresetNameInCsv(string varName, int index)
		{
			return GetPresetNameSource(varName, index) == PresetErdMerge.NameSourceCsv;
		}

		/// <summary>指定预设变量在指定索引的名字是否来自 ERD 补入。见 <see cref="ExistPresetNameInCsv"/>。</summary>
		public bool ExistPresetNameInErd(string varName, int index)
		{
			return GetPresetNameSource(varName, index) == PresetErdMerge.NameSourceErd;
		}

		private sbyte GetPresetNameSource(string varName, int index)
		{
			if (string.IsNullOrEmpty(varName) || presetNameSource == null)
				return PresetErdMerge.NameSourceEmpty;
			if (!presetErdSlotByName.TryGetValue(varName, out int slot))
				return PresetErdMerge.NameSourceEmpty;
			if ((uint)slot >= (uint)presetNameSource.Length)
				return PresetErdMerge.NameSourceEmpty;
			sbyte[] sources = presetNameSource[slot];
			if (sources == null || (uint)index >= (uint)sources.Length)
				return PresetErdMerge.NameSourceEmpty;
			return sources[index];
		}
	}
}
