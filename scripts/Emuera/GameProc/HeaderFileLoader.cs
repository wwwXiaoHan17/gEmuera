using System;
using System.Collections.Generic;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc.Function;
using MinorShift._Library;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Function;
using System.Text.RegularExpressions;
using System.IO;

namespace MinorShift.Emuera.GameProc
{
	internal sealed class HeaderFileLoader
	{
		public HeaderFileLoader(EmueraConsole main, IdentifierDictionary idDic, Process proc)
		{
			output = main;
			parentProcess = proc;
			this.idDic = idDic;
		}
		readonly Process parentProcess;
		readonly EmueraConsole output;
		readonly IdentifierDictionary idDic;

		bool noError = true;
		Queue<DimLineWC> dimlines;
		Dictionary<string, List<string>> erdFileNames;
		/// <summary>
		/// 
		/// </summary>
		/// <param name="erbDir"></param>
		/// <param name="displayReport"></param>
		/// <returns></returns>
		public bool LoadHeaderFiles(string headerDir, bool displayReport)
		{
			// 常数表（declare.const-table.v1）按文件名缓存了 CSV/ERB 目录索引；重新解析 ERH 时先丢弃，
			// 否则新增或改名的表文件会被旧索引挡住。单独重载 ERB 不会走到这里，索引不重建。
			ConstArrayTableDiskFiles.ClearIndex();
			List<KeyValuePair<string, string>> headerFiles = Config.GetFiles(headerDir, "*.ERH");
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            headerFiles.AddRange(Config.GetFiles(headerDir, "*.erh"));
#endif
			bool noError = true;
			dimlines = new Queue<DimLineWC>();
			if (Config.UseERD || Program.Compatibility.Snake.AllowsUserDefinedVariableResolution)
				PrepareERDFileNames();
			try
			{
				for (int i = 0; i < headerFiles.Count; i++)
				{
					string filename = headerFiles[i].Key;
					string file = headerFiles[i].Value;
					if (displayReport)
						output.PrintSystemLine(filename + "読み込み中・・・");
					noError = loadHeaderFile(file, filename);
					if (!noError)
						break;
					//System.Windows.Forms.//Application.DoEvents();
				}
				//エラーが起きてる場合でも読み込めてる分だけはチェックする
				if (dimlines.Count > 0)
				{
					//&=でないと、ここで起きたエラーをキャッチできない
					noError &= analyzeSharpDimLines();
				}

				dimlines.Clear();
			}
			finally
			{
				ParserMediator.FlushWarningList();
				erdFileNames = null;
			}
			return noError;
		}


		private bool loadHeaderFile(string filepath, string filename)
		{
			StringStream st;
			ScriptPosition position = null;
			//EraStreamReader eReader = new EraStreamReader(false);
			//1815修正 _rename.csvの適用
			//eramakerEXの仕様的には.ERHに適用するのはおかしいけど、もうEmueraの仕様になっちゃってるのでしかたないか
			EraStreamReader eReader = new EraStreamReader(true);

			if (!eReader.OpenOnCache(filepath, filename))
			{
				throw new CodeEE(eReader.Filename + "のオープンに失敗しました");
				//return false;
			}
			try
			{
				while ((st = eReader.ReadEnabledLine()) != null)
				{
					if (!noError)
						return false;
					position = new ScriptPosition(filename, eReader.LineNo);
					LexicalAnalyzer.SkipWhiteSpace(st);
					if (st.Current != '#')
						throw new CodeEE("ヘッダーの中に#で始まらない行があります", position);
					st.ShiftNext();
					string sharpID = LexicalAnalyzer.ReadSingleIdentifier(st);
					if (sharpID == null)
					{
						ParserMediator.Warn("解釈できない#行です", position, 1);
						return false;
					}
					if (Config.ICFunction)
						sharpID = sharpID.ToUpper();
					LexicalAnalyzer.SkipWhiteSpace(st);
					switch (sharpID)
					{
						case "DEFINE":
							analyzeSharpDefine(st, position);
							break;
						case "FUNCTION":
						case "FUNCTIONS":
							analyzeSharpFunction(st, position, sharpID == "FUNCTIONS", false);
							break;
						case "FUNCTIONF":
							// float 类型系统（snake 专属能力）；v24 参考落 default 致命错误。
							if (!Program.Compatibility.Snake.UsesFloatTypeSystem)
								throw new CodeEE("#" + sharpID + "は解釈できないプリプロセッサです", position);
							analyzeSharpFunction(st, position, false, true);
							break;
						case "DIM":
						case "DIMS":
							//1822 #DIMは保留しておいて後でまとめてやる
							{
								WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
								dimlines.Enqueue(new DimLineWC(wc, sharpID == "DIMS", false, position));
							}
							//analyzeSharpDim(st, position, sharpID == "DIMS");
							break;
						case "DIMF":
							// float 类型系统（snake 专属能力）；v24 参考落 default 致命错误。
							if (!Program.Compatibility.Snake.UsesFloatTypeSystem)
								throw new CodeEE("#" + sharpID + "は解釈できないプリプロセッサです", position);
							//1822 #DIMは保留しておいて後でまとめてやる
							{
								WordCollection wc = LexicalAnalyzer.Analyse(
									new StringStream(NormalizeSnakeFloatLiterals(st.Substring())),
									LexEndWith.EoL,
									LexAnalyzeFlag.AllowAssignment);
								dimlines.Enqueue(new DimLineWC(wc, false, true, false, position));
							}
							//analyzeSharpDim(st, position, sharpID == "DIMS");
							break;
						case "REF":
						case "REFS":
						case "REFF":
							{
								// #REFF 属 float 类型系统（snake 专属能力）；v24 参考落 default 致命错误。
								if (sharpID == "REFF" && !Program.Compatibility.Snake.UsesFloatTypeSystem)
									throw new CodeEE("#" + sharpID + "は解釈できないプリプロセッサです", position);
								WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
								bool isStr = sharpID == "REFS";
								bool isFloat = sharpID == "REFF";
								UserDefinedVariableData data = UserDefinedVariableData.CreateRefScalar(wc, isStr, isFloat, false, position);
								VariableToken var = parentProcess.VEvaluator.VariableData.CreateUserDefVariable(data);
								idDic.AddUseDefinedVariable(var);
							}
							break;
						default:
							throw new CodeEE("#" + sharpID + "は解釈できないプリプロセッサです", position);
					}
				}
			}
			catch (CodeEE e)
			{
				if (e.Position != null)
					position = e.Position;
				ParserMediator.Warn(e.Message, position, 2);
				return false;
			}
			finally
			{
				eReader.Close();
			}
			return true;
		}

		//#define FOO (～～)     id to wc
		//#define BAR($1) (～～)     idwithargs to wc(replaced)
		//#diseble FOOBAR             
		//#dim piyo, i
		//#dims puyo, j
		//static List<string> keywordsList = new List<string>();

		private void analyzeSharpDefine(StringStream st, ScriptPosition position)
		{
			//LexicalAnalyzer.SkipWhiteSpace(st);呼び出し前に行う。
			string srcID = LexicalAnalyzer.ReadSingleIdentifier(st);
			if (srcID == null)
				throw new CodeEE("置換元の識別子がありません", position);
			if (Config.ICVariable)
				srcID = srcID.ToUpper();

            //ここで名称重複判定しないと、大変なことになる
            string errMes = "";
            int errLevel = -1;
            idDic.CheckUserMacroName(ref errMes, ref errLevel, srcID);
            if (errLevel >= 0)
            {
                ParserMediator.Warn(errMes, position, errLevel);
                if (errLevel >= 2)
                {
                    noError = false;
                    return;
                }
            }
            
            bool hasArg = st.Current == '(';//引数を指定する場合には直後に(が続いていなければならない。ホワイトスペースも禁止。
			//1808a3 代入演算子許可（関数宣言用）
			WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
			if (wc.EOL)
			{
				//throw new CodeEE("置換先の式がありません", position);
				//1808a3 空マクロの許可
				DefineMacro nullmac = new DefineMacro(srcID, new WordCollection(), 0);
				idDic.AddMacro(nullmac);
				return;
			}

			List<string> argID = new List<string>();
			if (hasArg)//関数型マクロの引数解析
			{
				wc.ShiftNext();//'('を読み飛ばす
				if (wc.Current.Type == ')')
					throw new CodeEE("関数型マクロの引数を0個にすることはできません", position);
				while (!wc.EOL)
				{
					IdentifierWord word = wc.Current as IdentifierWord;
					if (word == null)
						throw new CodeEE("置換元の引数指定の書式が間違っています", position);
					word.SetIsMacro();
					string id = word.Code;
					if (argID.Contains(id))
						throw new CodeEE("置換元の引数に同じ文字が2回以上使われています", position);
					argID.Add(id);
					wc.ShiftNext();
					if (wc.Current.Type == ',')
					{
						wc.ShiftNext();
						continue;
					}
					if (wc.Current.Type == ')')
						break;
					throw new CodeEE("置換元の引数指定の書式が間違っています", position);
				}
				if (wc.EOL)
					throw new CodeEE("')'が閉じられていません", position);

				wc.ShiftNext();
			}
			if (wc.EOL)
				throw new CodeEE("置換先の式がありません", position);
			WordCollection destWc = new WordCollection();
			while (!wc.EOL)
			{
				destWc.Add(wc.Current);
				wc.ShiftNext();
			}
			if (hasArg)//関数型マクロの引数セット
			{
				while (!destWc.EOL)
				{
					IdentifierWord word = destWc.Current as IdentifierWord;
					if (word == null)
					{
						destWc.ShiftNext();
						continue;
					}
					for (int i = 0; i < argID.Count; i++)
					{
						if (string.Equals(word.Code, argID[i], Config.SCVariable))
						{
							destWc.Remove();
							destWc.Insert(new MacroWord(i));
							break;
						}
					}
					destWc.ShiftNext();
				}
				destWc.Pointer = 0;
			}
			if (hasArg)//1808a3 関数型マクロの封印
				throw new CodeEE("関数型マクロは宣言できません", position);
			DefineMacro mac = new DefineMacro(srcID, destWc, argID.Count);
			idDic.AddMacro(mac);
		}

		//private void analyzeSharpDim(StringStream st, ScriptPosition position, bool dims)
		//{
		//	//WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
		//	//UserDefinedVariableData data = UserDefinedVariableData.Create(wc, dims, false, position);
		//	//if (data.Reference)
		//	//	throw new NotImplCodeEE();
		//	//VariableToken var = null;
		//	//if (data.CharaData)
		//	//	var = parentProcess.VEvaluator.VariableData.CreateUserDefCharaVariable(data);
		//	//else
		//	//	var = parentProcess.VEvaluator.VariableData.CreateUserDefVariable(data);
		//	//idDic.AddUseDefinedVariable(var);
		//}

		//1822 #DIMだけまとめておいて後で処理
		private bool analyzeSharpDimLines()
		{
			bool noError = true;
			bool tryAgain = true;
			while (dimlines.Count > 0)
			{
				int count = dimlines.Count;
				for (int i = 0; i < count; i++)
				{
					DimLineWC dimline = dimlines.Dequeue();
					try
					{
						UserDefinedVariableData data = UserDefinedVariableData.Create(dimline);
						VariableToken var = null;
						if (data.CharaData)
							var = parentProcess.VEvaluator.VariableData.CreateUserDefCharaVariable(data);
						else
							var = parentProcess.VEvaluator.VariableData.CreateUserDefVariable(data);
						idDic.AddUseDefinedVariable(var);
						if (Config.UseERD || Program.Compatibility.Snake.AllowsUserDefinedVariableResolution)
							LoadUserDefinedNameData(data, dimline.SC);
						if (data.ConstTableNames != null)
							RegisterConstTableNames(data, dimline.SC);
					}
					catch (IdentifierNotFoundCodeEE e)
					{
						//繰り返すことで解決する見込みがあるならキューの最後に追加
						if (tryAgain)
						{
							dimline.WC.Pointer = 0;
							dimlines.Enqueue(dimline);
						}
						else
						{
							ParserMediator.Warn(e.Message, dimline.SC, 2);
							noError = true;
						}
					}
					catch (CodeEE e)
					{
						ParserMediator.Warn(e.Message, dimline.SC, 2);
						noError = false;
					}
				}
				if (dimlines.Count == count)
					tryAgain = false;
			}
			return noError;
		}

		private void analyzeSharpFunction(StringStream st, ScriptPosition position, bool funcs, bool funcf)
		{
			WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AllowAssignment);
			UserDefinedFunctionData data = UserDefinedFunctionData.Create(wc, funcs, funcf, position);
			idDic.AddRefMethod(UserDefinedRefMethod.Create(data));
		}

		private void PrepareERDFileNames()
		{
			erdFileNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			AddERDFileNames(uEmuera.Utils.GetFilePaths(Program.ErbDir, "*.ERD", SearchOption.AllDirectories));
			AddERDFileNames(uEmuera.Utils.GetFilePaths(Program.ErbDir, "*.erd", SearchOption.AllDirectories));
			AddERDFileNames(uEmuera.Utils.GetFilePaths(Program.CsvDir, "*.CSV", SearchOption.TopDirectoryOnly));
			AddERDFileNames(uEmuera.Utils.GetFilePaths(Program.CsvDir, "*.csv", SearchOption.TopDirectoryOnly));
		}

		private void AddERDFileNames(List<string> paths)
		{
			if (paths == null)
				return;
			for (int i = 0; i < paths.Count; i++)
			{
				string key = Path.GetFileNameWithoutExtension(paths[i]);
				if (string.IsNullOrEmpty(key))
					continue;
				List<string> list;
				if (!erdFileNames.TryGetValue(key, out list))
				{
					list = new List<string>();
					erdFileNames.Add(key, list);
				}
				if (!list.Contains(paths[i]))
					list.Add(paths[i]);
			}
		}

		private void LoadUserDefinedNameData(UserDefinedVariableData data, ScriptPosition position)
		{
			if (data == null || erdFileNames == null || data.Lengths == null)
				return;
			if (data.Dimension == 1)
			{
				List<string> info;
				if (erdFileNames.TryGetValue(data.Name, out info))
				{
					// 常数表已消费的文件（主表、值扩展、名称表）由常数表处理，不再交给名称数据路径重复解析。
					info = WithoutConstTable(info, data.ConstTableConsumed);
					if (info != null)
						GlobalStatic.ConstantData.RegisterUserDefinedNameData(info, data.Name, data.Lengths[0], Config.DisplayReport, position);
				}
				return;
			}
			for (int dim = 1; dim <= data.Dimension && dim <= data.Lengths.Length; dim++)
			{
				string key = data.Name + "@" + dim.ToString();
				List<string> info;
				if (erdFileNames.TryGetValue(key, out info))
				{
					info = WithoutConstTable(info, data.ConstTableConsumed);
					if (info != null)
						GlobalStatic.ConstantData.RegisterUserDefinedNameData(info, key, data.Lengths[dim - 1], Config.DisplayReport, position);
				}
			}
		}

		/// <summary>剔除常数表已解析的文件：这些由常数表处理，名称数据路径（EE_ERD）不再重复解析。</summary>
		private static List<string> WithoutConstTable(List<string> list, List<string> consumed)
		{
			if (list == null || consumed == null || consumed.Count == 0)
				return list;
			var used = new HashSet<string>(consumed, StringComparer.OrdinalIgnoreCase);
			var ret = new List<string>();
			foreach (string path in list)
			{
				if (!used.Contains(path))
					ret.Add(path);
			}
			return ret.Count == 0 ? null : ret;
		}

		/// <summary>
		/// 常数表（declare.const-table.v1，Emuera-SKIA Skiav18.0）的名称注册：值表、名称表与 .als 已在
		/// ConstArrayTable.Load 里按主表优先合并完，这里统一注册，并以 Lv1 报告补充表的冲突与坏格。
		/// 常数表的名称不依赖 Config.UseERD。
		/// </summary>
		private void RegisterConstTableNames(UserDefinedVariableData data, ScriptPosition position)
		{
			if (data.ConstTableWarnings != null)
			{
				foreach (string nameWarning in data.ConstTableWarnings)
					ParserMediator.Warn(nameWarning, position, 1);
			}
			GlobalStatic.ConstantData.RegisterConstArrayNames(data.Name, data.Dimension, data.ConstTableNames);
			if (Config.DisplayReport)
				output.PrintSystemLine(data.ConstTablePath + "読み込み中・・・");
		}

		private static string NormalizeSnakeFloatLiterals(string source)
		{
			if (string.IsNullOrEmpty(source))
				return source;
			return Regex.Replace(source, @"(?<![A-Za-z0-9_])([+-]?\d+)\.\d+(?![A-Za-z0-9_])", "$1");
		}
	}
}
