// ConstantData.KeywordLookup.cs —— 承载名称/关键字查找功能域（含 ERD 用户定义名懒加载），自 ConstantData.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Variable;

namespace MinorShift.Emuera.GameData
{
	internal sealed partial class ConstantData
	{
		private readonly Dictionary<string, Dictionary<string, int>> erdNameToIntDics = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, LazyErdNameData> lazyErdNameData = new Dictionary<string, LazyErdNameData>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> lazyErdLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private sealed class LazyErdNameData
		{
			public List<string> Filepaths;
			public int VarLength;
			public bool DisplayReport;
			public ScriptPosition Position;
		}

		public bool isDefined(VariableCode varCode, string str)
		{
			if (string.IsNullOrEmpty(str))
				return false;
            Dictionary<string, int> dic;
            if (varCode == VariableCode.CDFLAG)
            {
                dic = GetKeywordDictionary(out _, VariableCode.CDFLAGNAME1, -1);
                if ((dic == null) || (!dic.ContainsKey(str)))
                    dic = GetKeywordDictionary(out _, VariableCode.CDFLAGNAME2, -1);
                if (dic == null)
                    return false;
                return dic.ContainsKey(str);
            }
            dic = GetKeywordDictionary(out _, varCode, -1);
			if (dic == null)
				return false;
			return dic.ContainsKey(str);
		}

        
		public void UserDefineLoadData(List<string> filepaths, string varname, int varlength, bool disp, ScriptPosition sc)
		{
			if (filepaths == null || filepaths.Count == 0 || string.IsNullOrEmpty(varname) || varlength <= 0)
				return;

			Dictionary<string, int> dict = new Dictionary<string, int>(varlength);
			Dictionary<string, string> definedAt = new Dictionary<string, string>(varlength);
			for (int i = 0; i < filepaths.Count; i++)
			{
				string[] nameArray = new string[varlength];
				loadUserDefinedNameData(filepaths[i], nameArray, disp);
				for (int j = 0; j < nameArray.Length; j++)
				{
					string name = nameArray[j];
					if (string.IsNullOrEmpty(name))
						continue;
					if (dict.ContainsKey(name))
					{
						string prevPath;
						definedAt.TryGetValue(name, out prevPath);
						throw new CodeEE(varname + "の識別子\"" + name + "\"が重複定義されています(" + prevPath + ", " + filepaths[i] + ")", sc);
					}
					dict.Add(name, j);
					definedAt[name] = filepaths[i];
				}
			}
			if (erdNameToIntDics.ContainsKey(varname))
				throw new CodeEE(varname + "は既に定義されています", sc);
			erdNameToIntDics.Add(varname, dict);
			// Skiav8.0：用户定义变量 CSV 旁的 .als 别名（VAR.als / 多维 VAR@N.als）。
			// 别名注入 erdNameToIntDics[varname]，不覆盖 CSV 已有同名定义。
			for (int i = 0; i < filepaths.Count; i++)
			{
				string aliasPath = Path.Combine(
					Path.GetDirectoryName(filepaths[i]),
					Path.GetFileNameWithoutExtension(filepaths[i]) + ".als");
				loadAliasesForUserDefined(aliasPath, dict);
			}
		}

		/// <summary>
		/// 为用户定义变量加载 .als 别名文件，注入 erdNameToIntDics[varname] 字典。
		/// 与系统变量的 loadAliases 不同：系统变量写入 aliases[targetIndex] 数组，
		/// 用户变量没有 VariableCode 枚举索引，只能写入字典；重复别名静默跳过（CSV 优先，Skiav8.0 语义）。
		/// </summary>
		private void loadAliasesForUserDefined(string aliasPath, Dictionary<string, int> targetDict)
		{
			string resolvedAliasPath = uEmuera.Utils.ResolveExistingFilePath(aliasPath);
			if (!string.IsNullOrEmpty(resolvedAliasPath))
				aliasPath = resolvedAliasPath;
			if (!uEmuera.Utils.FileExists(aliasPath))
				return;
			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.OpenOnCache(aliasPath))
			{
				output.PrintError(eReader.Filename + "のオープンに失敗しました");
				return;
			}
			ScriptPosition position = null;
			try
			{
				StringStream st = null;
				Span<CsvFieldRange> fields = stackalloc CsvFieldRange[2];
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					string source = st.RowString;
					int startOffset = st.CurrentPosition;
					int tokenCount = ReadCsvHeadFields(source, startOffset, fields);
					if (tokenCount < 2)
					{
						ParserMediator.Warn("\",\"が必要です", position, 1);
						continue;
					}
					int index;
					if (!Int32.TryParse(source.AsSpan(fields[0].Start, fields[0].Length), out index))
					{
						ParserMediator.Warn("一つ目の値を整数値に変換できません", position, 1);
						continue;
					}
					string aliasName = GetTrimmedFieldString(source, fields[1]);
					if (string.IsNullOrEmpty(aliasName))
						continue;
					// 别名不覆盖 CSV 中已有的同名定义
					if (!targetDict.ContainsKey(aliasName))
						targetDict.Add(aliasName, index);
				}
			}
			catch
			{
				uEmuera.Media.SystemSounds.Hand.Play();
				if (position != null)
					ParserMediator.Warn("予期しないエラーが発生しました", position, 3);
				else
					output.PrintError("予期しないエラーが発生しました");
			}
			finally
			{
				eReader.Close();
			}
		}

		public void RegisterUserDefinedNameData(List<string> filepaths, string varname, int varlength, bool disp, ScriptPosition sc)
		{
			if (filepaths == null || filepaths.Count == 0 || string.IsNullOrEmpty(varname) || varlength <= 0)
				return;
			if (erdNameToIntDics.ContainsKey(varname) || lazyErdNameData.ContainsKey(varname))
				throw new CodeEE(varname + "は既に定義されています", sc);
			lazyErdNameData.Add(varname, new LazyErdNameData
			{
				Filepaths = new List<string>(filepaths),
				VarLength = varlength,
				DisplayReport = disp,
				Position = sc
			});
		}

		private bool EnsureUserDefinedNameDataLoaded(string varname)
		{
			if (string.IsNullOrEmpty(varname))
				return false;
			if (erdNameToIntDics.ContainsKey(varname))
				return true;
			LazyErdNameData data;
			if (!lazyErdNameData.TryGetValue(varname, out data))
				return false;
			if (!lazyErdLoading.Add(varname))
				return false;
			try
			{
				lazyErdNameData.Remove(varname);
				UserDefineLoadData(data.Filepaths, varname, data.VarLength, data.DisplayReport, data.Position);
				return erdNameToIntDics.ContainsKey(varname);
			}
			finally
			{
				lazyErdLoading.Remove(varname);
			}
		}

		public bool isUserDefined(string varname, string str, int dim)
		{
			if (string.IsNullOrEmpty(varname) || string.IsNullOrEmpty(str))
				return false;
			if (dim <= 1)
			{
				EnsureUserDefinedNameDataLoaded(varname);
				return erdNameToIntDics.ContainsKey(varname) && erdNameToIntDics[varname].ContainsKey(str);
			}
			for (int i = 1; i <= dim; i++)
			{
				string key = varname + "@" + i.ToString();
				EnsureUserDefinedNameDataLoaded(key);
				Dictionary<string, int> dic;
				if (erdNameToIntDics.TryGetValue(key, out dic) && dic.ContainsKey(str))
					return true;
			}
			return false;
		}

		public bool TryKeywordToInteger(out int ret, VariableCode code, string key, int index)
		{
			return TryKeywordToInteger(out ret, code, key, index, null);
		}

		public bool TryKeywordToInteger(out int ret, VariableCode code, string key, int index, string varname)
        {
            ret = 0;
            if (string.IsNullOrEmpty(key))
                return false;
            try
            {
                Dictionary<string, int> dic = GetKeywordDictionary(out string errPos, code, index, null);
				if (dic != null && dic.TryGetValue(key, out ret))
					return true;
				if (string.IsNullOrEmpty(varname))
					return false;
				EnsureUserDefinedNameDataLoaded(varname);
				if (erdNameToIntDics.TryGetValue(varname, out dic))
					return dic.TryGetValue(key, out ret);
				dic = GetKeywordDictionary(out errPos, code, index, varname);
				return dic != null && dic.TryGetValue(key, out ret);
            }
            catch { return false; }
        }

		public bool TryIntegerToKeyword(out string ret, long value, string varname)
		{
			ret = "";
			if (value < 0 || string.IsNullOrEmpty(varname))
				return false;
			EnsureUserDefinedNameDataLoaded(varname);
			if (!erdNameToIntDics.TryGetValue(varname, out Dictionary<string, int> dic))
				return false;
			foreach (KeyValuePair<string, int> pair in dic)
			{
				if (pair.Value == value)
				{
					ret = pair.Key;
					return true;
				}
			}
			return false;
		}

		public int KeywordToInteger(VariableCode code, string key, int index)
		{
			if (string.IsNullOrEmpty(key))
				throw new CodeEE("キーワードを空には出来ません");
            Dictionary<string, int> dic = GetKeywordDictionary(out string errPos, code, index);
            if (dic.TryGetValue(key, out int ret))
                return ret;
            if (errPos == null)
				throw new CodeEE("配列変数" + code.ToString() + "の要素を文字列で指定することはできません");
			else
				throw new CodeEE(errPos + "の中に\"" + key + "\"の定義がありません");
		}

		public Dictionary<string, int> GetKeywordDictionary(out string errPos, VariableCode code, int index)
		{
			return GetKeywordDictionary(out errPos, code, index, null);
		}

		public Dictionary<string, int> GetKeywordDictionary(out string errPos, VariableCode code, int index, string varname)
		{
			errPos = null;
			int allowIndex = -1;
			Dictionary<string, int> ret = null;
			switch (code)
			{
				case VariableCode.ABL:
					ret = nameToIntDics[ablIndex];//AblName;
					errPos = "abl.csv";
					allowIndex = 1;
					break;
				case VariableCode.EXP:
					ret = nameToIntDics[expIndex];//ExpName;
					errPos = "exp.csv";
					allowIndex = 1;
					break;
				case VariableCode.TALENT:
					ret = nameToIntDics[talentIndex];//TalentName;
					errPos = "talent.csv";
					allowIndex = 1;
					break;
				case VariableCode.UP:
				case VariableCode.DOWN:
					ret = nameToIntDics[paramIndex];//ParamName　１;
					errPos = "palam.csv";
					allowIndex = 0;
					break;
				case VariableCode.PALAM:
				case VariableCode.JUEL:
				case VariableCode.GOTJUEL:
				case VariableCode.CUP:
				case VariableCode.CDOWN:
					ret = nameToIntDics[paramIndex];//ParamName　２;
					errPos = "palam.csv";
					allowIndex = 1;
					break;

				case VariableCode.TRAINNAME:
					ret = nameToIntDics[trainIndex];//TrainName;
					errPos = "train.csv";
					allowIndex = 0;
					break;
				case VariableCode.MARK:
					ret = nameToIntDics[markIndex];//MarkName;
					errPos = "mark.csv";
					allowIndex = 1;
					break;
				case VariableCode.ITEM:
				case VariableCode.ITEMSALES:
				case VariableCode.ITEMPRICE:
					ret = nameToIntDics[itemIndex];//ItemName;
					errPos = "Item.csv";
					allowIndex = 0;
					break;
				case VariableCode.LOSEBASE:
					ret = nameToIntDics[baseIndex];//BaseName;
					errPos = "base.csv";
					allowIndex = 0;
					break;
				case VariableCode.BASE:
				case VariableCode.MAXBASE:
				case VariableCode.DOWNBASE:
					ret = nameToIntDics[baseIndex];//BaseName;
					errPos = "base.csv";
					allowIndex = 1;
					break;
				case VariableCode.SOURCE:
					ret = nameToIntDics[sourceIndex];//SourceName;
					errPos = "source.csv";
					allowIndex = 1;
					break;
				case VariableCode.EX:
				case VariableCode.NOWEX:
					ret = nameToIntDics[exIndex];//ExName;
					errPos = "ex.csv";
					allowIndex = 1;
					break;


				case VariableCode.EQUIP:
					ret = nameToIntDics[equipIndex];//EquipName;
					errPos = "equip.csv";
					allowIndex = 1;
					break;
				case VariableCode.TEQUIP:
					ret = nameToIntDics[tequipIndex];//TequipName;
					errPos = "tequip.csv";
					allowIndex = 1;
					break;
				case VariableCode.FLAG:
					ret = nameToIntDics[flagIndex];//FlagName;
					errPos = "flag.csv";
					allowIndex = 0;
					break;
				case VariableCode.TFLAG:
					ret = nameToIntDics[tflagIndex];//TFlagName;
					errPos = "tflag.csv";
					allowIndex = 0;
					break;
				case VariableCode.CFLAG:
					ret = nameToIntDics[cflagIndex];//CFlagName;
					errPos = "cflag.csv";
					allowIndex = 1;
					break;
				case VariableCode.TCVAR:
					ret = nameToIntDics[tcvarIndex];//TCVarName;
					errPos = "tcvar.csv";
					allowIndex = 1;
					break;
				case VariableCode.CSTR:
					ret = nameToIntDics[cstrIndex];//CStrName;
					errPos = "cstr.csv";
					allowIndex = 1;
					break;

				case VariableCode.STAIN:
					ret = nameToIntDics[stainIndex];//StainName;
					errPos = "stain.csv";
					allowIndex = 1;
					break;
				case VariableCode.CDFLAGNAME1:
					ret = nameToIntDics[cdflag1Index];
					errPos = "cdflag1.csv";
					allowIndex = 0;
					break;
				case VariableCode.CDFLAGNAME2:
					ret = nameToIntDics[cdflag2Index];
					errPos = "cdflag2.csv";
					allowIndex = 0;
					break;
				case VariableCode.CDFLAG:
				{
					if (index == 1)
					{
						ret = nameToIntDics[cdflag1Index];//CDFlagName1
						errPos = "cdflag1.csv";
					}
					else if (index == 2)
					{
						ret = nameToIntDics[cdflag2Index];//CDFlagName2
						errPos = "cdflag2.csv";
					}
					else if (index >= 0)
						throw new CodeEE("配列変数" + code.ToString() + "の" + (index + 1).ToString() + "番目の要素を文字列で指定することはできません");
					else
						throw new CodeEE("CDFLAGの要素の取得にはCDFLAGNAME1又はCDFLAGNAME2を使用します");
					return ret;
				}
				case VariableCode.STR:
					ret = nameToIntDics[strnameIndex];
					errPos = "strname.csv";
					allowIndex = 0;
					break;
				case VariableCode.TSTR:
					ret = nameToIntDics[tstrnameIndex];
					errPos = "tstr.csv";
					allowIndex = 0;
					break;
				case VariableCode.SAVESTR:
					ret = nameToIntDics[savestrnameIndex];
					errPos = "savestr.csv";
					allowIndex = 0;
					break;
				case VariableCode.GLOBAL:
					ret = nameToIntDics[globalIndex];
					errPos = "global.csv";
					allowIndex = 0;
					break;
				case VariableCode.GLOBALS:
					ret = nameToIntDics[globalsIndex];
					errPos = "globals.csv";
					allowIndex = 0;
					break;
				case VariableCode.DAY:
					ret = nameToIntDics[dayIndex];
					errPos = "day.csv";
					allowIndex = 0;
					break;
				case VariableCode.TIME:
					ret = nameToIntDics[timeIndex];
					errPos = "time.csv";
					allowIndex = 0;
					break;
				case VariableCode.MONEY:
					ret = nameToIntDics[moneyIndex];
					errPos = "money.csv";
					allowIndex = 0;
					break;
				case VariableCode.RELATION:
					ret = relationDic;
					errPos = "chara*.csv";
					allowIndex = 1;
					break;
				case VariableCode.NAME:
					ret = relationDic;
					errPos = "chara*.csv";
					allowIndex = -1;
					break;

			}
			if (ret == null && !string.IsNullOrEmpty(varname))
			{
				switch (code)
				{
					case VariableCode.VAR:
					case VariableCode.VARS:
						EnsureUserDefinedNameDataLoaded(varname);
						if (erdNameToIntDics.TryGetValue(varname, out ret))
						{
							errPos = varname + ".csv";
							allowIndex = 0;
						}
						break;
					case VariableCode.CVAR:
					case VariableCode.CVARS:
						EnsureUserDefinedNameDataLoaded(varname);
						if (erdNameToIntDics.TryGetValue(varname, out ret))
						{
							errPos = varname + ".csv";
							allowIndex = 1;
						}
						break;
					case VariableCode.VAR2D:
					case VariableCode.VARS2D:
					case VariableCode.CVAR2D:
					case VariableCode.CVARS2D:
					{
						int dim = ((code == VariableCode.CVAR2D) || (code == VariableCode.CVARS2D)) ? index : index + 1;
						string key = varname + "@" + dim.ToString();
						EnsureUserDefinedNameDataLoaded(key);
						if (erdNameToIntDics.TryGetValue(key, out ret))
						{
							errPos = key + ".csv";
							allowIndex = index;
						}
						break;
					}
					case VariableCode.VAR3D:
					case VariableCode.VARS3D:
					{
						string key = varname + "@" + (index + 1).ToString();
						EnsureUserDefinedNameDataLoaded(key);
						if (erdNameToIntDics.TryGetValue(key, out ret))
						{
							errPos = key + ".csv";
							allowIndex = index;
						}
						break;
					}
				}
			}
			if (index < 0)
				return ret;
			if (ret == null)
				throw new CodeEE("配列変数" + code.ToString() + "の要素を文字列で指定することはできません");
			if ((index != allowIndex))
			{
				if (allowIndex < 0)//GETNUM専用
					throw new CodeEE("配列変数" + code.ToString() + "の要素を文字列で指定することはできません");
				throw new CodeEE("配列変数" + code.ToString() + "の" + (index + 1).ToString() + "番目の要素を文字列で指定することはできません");
			}
			return ret;
		}

		private void loadUserDefinedNameData(string csvPath, string[] target, bool disp)
		{
			string resolvedCsvPath = uEmuera.Utils.ResolveExistingFilePath(csvPath);
			if (!string.IsNullOrEmpty(resolvedCsvPath))
				csvPath = resolvedCsvPath;
			if (!uEmuera.Utils.FileExists(csvPath))
				return;
			EraStreamReader eReader = new EraStreamReader(false);
			if (!eReader.OpenOnCache(csvPath))
			{
				output.PrintError(eReader.Filename + "のオープンに失敗しました");
				return;
			}
			ScriptPosition position = null;
			if (disp || Program.AnalysisMode)
				output.PrintSystemLine(eReader.Filename + "読み込み中・・・");
			try
			{
				StringStream st = null;
				//stackalloc をループ外へ退避（CA2014）。各イテレーションで上書きするため再利用は安全。
				Span<CsvFieldRange> fields = stackalloc CsvFieldRange[2];
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					position = new ScriptPosition(eReader.Filename, eReader.LineNo);
					string source = st.RowString;
					int startOffset = st.CurrentPosition;
					int tokenCount = ReadCsvHeadFields(source, startOffset, fields);
					if (tokenCount < 2)
					{
						ParserMediator.Warn("\",\"が必要です", position, 1);
						continue;
					}
					int index;
					if (!Int32.TryParse(source.AsSpan(fields[0].Start, fields[0].Length), out index))
					{
						ParserMediator.Warn("一つ目の値を整数値に変換できません", position, 1);
						continue;
					}
					if ((index < 0) || (target.Length <= index))
					{
						ParserMediator.Warn(index.ToString() + "は配列の範囲外です", position, 1);
						continue;
					}
					target[index] = GetFieldString(source, fields[1]);
				}
			}
			catch
			{
				uEmuera.Media.SystemSounds.Hand.Play();
				if (position != null)
					ParserMediator.Warn("予期しないエラーが発生しました", position, 3);
				else
					output.PrintError("予期しないエラーが発生しました");
			}
			finally
			{
				eReader.Dispose();
			}
		}
	}
}
