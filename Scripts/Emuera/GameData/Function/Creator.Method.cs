using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameProc;
using MinorShift._Library;
using MinorShift.Emuera.GameData.Variable;
//using System.Drawing;
//using Microsoft.VisualBasic;
//using System.Windows.Forms;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;
using uEmuera.Drawing;
using uEmuera.VisualBasic;

namespace MinorShift.Emuera.GameData.Function
{

    internal static partial class FunctionMethodCreator
    {
        #region CSVデータ関係
        private sealed class GetcharaMethod : FunctionMethod
        {
            public GetcharaMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = false;
            }
            
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常２つ、１つ省略可能で１～２の引数が必要。
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";

                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer)
                    return name + "関数の1番目の引数の型が正しくありません";
                //2は省略可能
                if ((arguments.Length == 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の引数の型が正しくありません";
                return null;
            }
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 integer = arguments[0].GetIntValue(exm);
                if (!Config.CompatiSPChara)
				{
					//if ((arguments.Length > 1) && (arguments[1] != null) && (arguments[1].GetIntValue(exm) != 0))
					return exm.VEvaluator.GetChara(integer);
				}
				//以下互換性用の旧処理
                bool CheckSp = false;
                if ((arguments.Length > 1) && (arguments[1] != null) && (arguments[1].GetIntValue(exm) != 0))
                    CheckSp = true;
                if (CheckSp)
                {
                    long chara = exm.VEvaluator.GetChara_UseSp(integer, false);
                    if (chara != -1)
                        return chara;
                    else
                        return exm.VEvaluator.GetChara_UseSp(integer, true);
                }
                else
                    return exm.VEvaluator.GetChara_UseSp(integer, false);
            }
        }

        private sealed class GetspcharaMethod : FunctionMethod
        {
            public GetspcharaMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.Integer };
                CanRestructure = false;
            }
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
				if(!Config.CompatiSPChara)
					throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
                Int64 integer = arguments[0].GetIntValue(exm);
                return exm.VEvaluator.GetChara_UseSp(integer, true);
            }
        }

        private sealed class CsvStrDataMethod : FunctionMethod
        {
            readonly CharacterStrData charaStr;
            public CsvStrDataMethod()
            {
                ReturnType = EraType.String;
				argumentTypeArray = null;
                charaStr = CharacterStrData.NAME;
                CanRestructure = true;
            }
            public CsvStrDataMethod(CharacterStrData cStr)
            {
                ReturnType = EraType.String;
				argumentTypeArray = null;
				charaStr = cStr;
				CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!arguments[0].IsInteger)
                    return name + "関数の1番目の引数が数値ではありません";
                if (arguments.Length == 1)
                    return null;
                if ((arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の変数が数値ではありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                long x = arguments[0].GetIntValue(exm);
				long y = (arguments.Length > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
				if (!Config.CompatiSPChara && y != 0)
					throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
                return exm.VEvaluator.GetCharacterStrfromCSVData(x, charaStr, (y != 0), 0);
            }
        }

        private sealed class CsvcstrMethod : FunctionMethod
        {
            public CsvcstrMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!arguments[0].IsInteger)
                    return name + "関数の1番目の引数が数値ではありません";
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arguments[1].GetEraType() != EraType.Integer)
                    return name + "関数の2番目の変数が数値ではありません";
                if (arguments.Length == 2)
                    return null;
                if ((arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の変数が数値ではありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                long x = arguments[0].GetIntValue(exm);
                long y = arguments[1].GetIntValue(exm);
                long z = (arguments.Length == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
				if(!Config.CompatiSPChara && z != 0)
					throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
                return exm.VEvaluator.GetCharacterStrfromCSVData(x, CharacterStrData.CSTR, (z != 0), y);
            }
        }

        private sealed class CsvDataMethod : FunctionMethod
        {
            readonly CharacterIntData charaInt;
            public CsvDataMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                charaInt = CharacterIntData.BASE;
                CanRestructure = true;
            }
            public CsvDataMethod(CharacterIntData cInt)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
				charaInt = cInt;
				CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!arguments[0].IsInteger)
                    return name + "関数の1番目の引数が数値ではありません";
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arguments[1].GetEraType() != EraType.Integer)
                    return name + "関数の2番目の変数が数値ではありません";
                if (arguments.Length == 2)
                    return null;
                if ((arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の変数が数値ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                long x = arguments[0].GetIntValue(exm);
                long y = arguments[1].GetIntValue(exm);
                long z = (arguments.Length == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
				if(!Config.CompatiSPChara && z != 0)
					throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
                return exm.VEvaluator.GetCharacterIntfromCSVData(x, charaInt, (z != 0), y);
            }
        }

        private sealed class FindcharaMethod : FunctionMethod
        {
            public FindcharaMethod(bool last)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = false;
                isLast = last;
            }

            readonly bool isLast;
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常3つ、1つ省略可能で2～3の引数が必要。
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";

                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm))
                    return name + "関数の1番目の引数の型が正しくありません";
                if (!(((VariableTerm)arguments[0]).Identifier.IsCharacterData))
                    return name + "関数の1番目の引数の変数がキャラクタ変数ではありません";
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arguments[1].GetEraType() != arguments[0].GetEraType())
                    return name + "関数の2番目の引数の型が正しくありません";
                //3番目は省略可能
                if ((arguments.Length >= 3) && (arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の引数の型が正しくありません";
                //4番目は省略可能
                if ((arguments.Length >= 4) && (arguments[3] != null) && (arguments[3].GetEraType() != EraType.Integer))
                    return name + "関数の4番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm vTerm = (VariableTerm)arguments[0];
                VariableToken varID = vTerm.Identifier;

                Int64 elem = 0;
                if (vTerm.Identifier.IsArray1D)
                    elem = vTerm.GetElementInt(1, exm);
                else if (vTerm.Identifier.IsArray2D)
                {
                    elem = vTerm.GetElementInt(1, exm) << 32;
                    elem += vTerm.GetElementInt(2, exm);
                }
                Int64 startindex = 0;
                Int64 lastindex = exm.VEvaluator.CHARANUM;
                if (arguments.Length >= 3 && arguments[2] != null)
                    startindex = arguments[2].GetIntValue(exm);
                if (arguments.Length >= 4 && arguments[3] != null)
                    lastindex = arguments[3].GetIntValue(exm);
                if (startindex < 0 || startindex >= exm.VEvaluator.CHARANUM)
                    throw new CodeEE((isLast ? "" : "") + "関数の第3引数(" + startindex.ToString() + ")はキャラクタ位置の範囲外です");
                if (lastindex < 0 || lastindex > exm.VEvaluator.CHARANUM)
                    throw new CodeEE((isLast ? "" : "") + "関数の第4引数(" + lastindex.ToString() + ")はキャラクタ位置の範囲外です");
                long ret;
                if (varID.IsString)
                {
                    string word = arguments[1].GetStrValue(exm);
                    ret = exm.VEvaluator.FindChara(varID, elem, word, startindex, lastindex, isLast);
                }
                else
                {
                    Int64 word = arguments[1].GetIntValue(exm);
                    ret = exm.VEvaluator.FindChara(varID, elem, word, startindex, lastindex, isLast);
                }
                return (ret);
            }
        }

        private sealed class ExistCsvMethod : FunctionMethod
        {
            public ExistCsvMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!arguments[0].IsInteger)
                    return name + "関数の1番目の引数が数値ではありません";
                if (arguments.Length == 1)
                    return null;
                if ((arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の変数が数値ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 no = arguments[0].GetIntValue(exm);
                bool isSp =(arguments.Length == 2 && arguments[1] != null) ? (arguments[1].GetIntValue(exm) != 0) : false;
				if(!Config.CompatiSPChara && isSp)
					throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");

                return (exm.VEvaluator.ExistCsv(no, isSp));
            }
        }
        #endregion

        #region 汎用処理系
        private sealed class VarsizeMethod : FunctionMethod
        {
            public VarsizeMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = true;
				//1808beta009 参照型変数の追加によりちょっと面倒になった
				HasUniqueRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!arguments[0].IsString)
                    return name + "関数の1番目の引数が文字列ではありません";
                if (arguments[0] is SingleTerm)
                {
                    string varName = ((SingleTerm)arguments[0]).Str;
                    if (ResolveVarsizeVariable(varName) == null)
                        return name + "関数の1番目の引数が変数名ではありません";
                }
                if (arguments.Length == 1)
                    return null;
                if ((arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の変数が数値ではありません";
                if (arguments.Length == 2)
                    return null;
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string varName = arguments[0].GetStrValue(exm);
                VariableToken var = ResolveVarsizeVariable(varName);
                if (var == null)
                    throw new CodeEE("VARSIZEの1番目の引数(\"" + arguments[0].GetStrValue(exm) + "\")が変数名ではありません");
                int dim = 0;
                if (arguments.Length == 2 && arguments[1] != null)
                    dim = (int)arguments[1].GetIntValue(exm);
				if (Config.VarsizeDimConfig && dim > 0)
					dim--;
                return (var.GetLength(dim));
            }
			public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				arguments[0].Restructure(exm);
				if (arguments.Length > 1)
					arguments[1].Restructure(exm);
				if (arguments[0] is SingleTerm && (arguments.Length == 1 || arguments[1] is SingleTerm))
				{
					VariableToken var = ResolveVarsizeVariable(arguments[0].GetStrValue(exm));
					if (var == null || var.IsReference)//可変長の場合は定数化できない
						return false;
					return true;
				}
				return false;
			}

			static VariableToken ResolveVarsizeVariable(string varName)
			{
				VariableToken var = GlobalStatic.IdentifierDictionary.GetVariableToken(varName, null, true);
				if (var != null || string.IsNullOrEmpty(varName))
					return var;

				// v24/snake 系では旧名 VARSIZE("FOO") が実体配列 FOO_ENG/FOO_JP を指すケースがある。
				// 完全一致を優先し、代表的な表示名配列だけへ限定して回退する。
				string[] suffixes = new string[] { "_ENG", "_JP" };
				for (int i = 0; i < suffixes.Length; i++)
				{
					var = GlobalStatic.IdentifierDictionary.GetVariableToken(varName + suffixes[i], null, true);
					if (var != null)
						return var;
				}
				return null;
			}
        }

        private sealed class CheckfontMethod : FunctionMethod
        {
			public CheckfontMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = true;//起動中に変わることもそうそうないはず……
			}
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                //string str = arguments[0].GetStrValue(exm);
                //System.Drawing.Text.InstalledFontCollection ifc = new System.Drawing.Text.InstalledFontCollection();
                //Int64 isInstalled = 0;
                //foreach (System.Drawing.FontFamily ff in ifc.Families)
                //{
                //    if (ff.Name == str)
                //    {
                //        isInstalled = 1;
                //        break;
                //    }
                //}
                //return (isInstalled);
                //TODO
                return 1;
            }

        }

        private sealed class CheckdataMethod : FunctionMethod
        {
			public CheckdataMethod(EraSaveFileType type)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.Integer };
                CanRestructure = false;
				this.type = type;
            }

            readonly EraSaveFileType type;
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 target = arguments[0].GetIntValue(exm);
                if (target < 0)
                    throw new CodeEE(Name + "の引数に負の値(" + target.ToString() + ")が指定されました");
                else if (target > int.MaxValue)
                    throw new CodeEE(Name + "の引数(" + target.ToString() + ")が大きすぎます");
                EraDataResult result = exm.VEvaluator.CheckData((int)target, type);
                exm.VEvaluator.RESULTS = result.DataMes;
                return ((long)result.State);
            }
        }

		/// <summary>
		/// ファイル名をstringで指定する版・CHKVARDATAとCHKCHARADATAはこっちに分類
		/// </summary>
		private sealed class CheckdataStrMethod : FunctionMethod
		{
			public CheckdataStrMethod(EraSaveFileType type)
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
				this.type = type;
			}

            readonly EraSaveFileType type;
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string datFilename = arguments[0].GetStrValue(exm);
                EraDataResult result = exm.VEvaluator.CheckData(datFilename, type);
                exm.VEvaluator.RESULTS = result.DataMes;
				return ((long)result.State);
			}
		}

		/// <summary>
		/// ファイル探索関数
		/// </summary>
		private sealed class FindFilesMethod : FunctionMethod
		{
			public FindFilesMethod(EraSaveFileType type)
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
				this.type = type;
			}

            readonly EraSaveFileType type;

			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length > 1)
					return name + "関数の引数が多すぎます";
				if (arguments.Length == 0 || arguments[0] == null)
					return null;
				if (!arguments[0].IsString)
					return name + "関数の1番目の引数が文字列ではありません";
				return null;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string pattern = "*";
				if (arguments.Length > 0 && arguments[0] != null)
					pattern = arguments[0].GetStrValue(exm);
                List<string> filepathes = exm.VEvaluator.GetDatFiles(type == EraSaveFileType.CharVar, pattern);
                var results = exm.VEvaluator.VariableData.DataStringArray[(int)(VariableCode.RESULTS & VariableCode.__LOWERCASE__)];
				int count = Math.Min(filepathes.Count, results.Length);
				for (int i = 0; i < count; i++)
					results[i] = filepathes[i];
				return filepathes.Count;
			}
		}
		

        private sealed class IsSkipMethod : FunctionMethod
        {
            public IsSkipMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return exm.Process.SkipPrint ? 1L : 0L;
            }
        }

		private sealed class MesSkipMethod : FunctionMethod
		{
			public MesSkipMethod(bool warn)
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
				this.warn = warn;
			}

            readonly bool warn;
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length > 0)
					return name + "関数の引数が多すぎます";
				if (warn)
					ParserMediator.Warn("関数MOUSESKIP()は推奨されません。代わりに関数MESSKIP()を使用してください", GlobalStatic.Process.GetScaningLine(), 1, false, false, null);
                return null;
            }
			public override long GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return GlobalStatic.Console.MesSkip ? 1L : 0L;
			}
		}


        private sealed class GetColorMethod : FunctionMethod
        {
            public GetColorMethod(bool isDef)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = isDef;
                defaultColor = isDef;
            }

            readonly bool defaultColor;
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Color color = (defaultColor) ? Config.ForeColor : GlobalStatic.Console.StringStyle.Color;
                return (color.ToArgb() & 0xFFFFFF);
            }
        }

        private sealed class GetFocusColorMethod : FunctionMethod
        {
            public GetFocusColorMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (Config.FocusColor.ToArgb() & 0xFFFFFF);
            }
        }

        private sealed class GetBGColorMethod : FunctionMethod
        {
            public GetBGColorMethod(bool isDef)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = isDef;
                defaultColor = isDef;
            }

            readonly bool defaultColor;
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Color color = (defaultColor) ? Config.BackColor : GlobalStatic.Console.bgColor;
                return (color.ToArgb() & 0xFFFFFF);
            }
        }

        private sealed class GetStyleMethod : FunctionMethod
        {
            public GetStyleMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }

            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                FontStyle fontstyle = GlobalStatic.Console.StringStyle.FontStyle;
                long ret = 0;
                if ((fontstyle & FontStyle.Bold) == FontStyle.Bold)
                    ret |= 1;
                if ((fontstyle & FontStyle.Italic) == FontStyle.Italic)
                    ret |= 2;
                if ((fontstyle & FontStyle.Strikeout) == FontStyle.Strikeout)
                    ret |= 4;
                if ((fontstyle & FontStyle.Underline) == FontStyle.Underline)
                    ret |= 8;
                return (ret);
            }
        }

        private sealed class GetFontMethod : FunctionMethod
        {
            public GetFontMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (GlobalStatic.Console.StringStyle.Fontname);
            }
        }

        private sealed class BarStringMethod : FunctionMethod
        {
            public BarStringMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                long var = arguments[0].GetIntValue(exm);
                long max = arguments[1].GetIntValue(exm);
                long length = arguments[2].GetIntValue(exm);
                return exm.CreateBar(var, max, length);
            }
        }

        private sealed class CurrentAlignMethod : FunctionMethod
        {
            public CurrentAlignMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (exm.Console.Alignment == GameView.DisplayLineAlignment.LEFT)
                    return "LEFT";
                else if (exm.Console.Alignment == GameView.DisplayLineAlignment.CENTER)
                    return "CENTER";
                else
                    return "RIGHT";
            }
        }

        private sealed class CurrentRedrawMethod : FunctionMethod
        {
            public CurrentRedrawMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override long GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (exm.Console.Redraw == GameView.ConsoleRedraw.None) ? 0L : 1L;
            }
        }

		private sealed class ColorFromNameMethod : FunctionMethod
		{
			public ColorFromNameMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = true;
			}
			public override long GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string colorName = arguments[0].GetStrValue(exm);
				Color color = Color.FromName(colorName);
                int i;
                if (color.A > 0)
					i = (color.R << 16) + (color.G << 8) + color.B;
				else
				{
					if (colorName.Equals("transparent", StringComparison.OrdinalIgnoreCase))
						throw new CodeEE("無色透明(Transparent)は色として指定できません");
					//throw new CodeEE("指定された色名\"" + colorName + "\"は無効な色名です");
					i = -1;
				}
				return i;
			}
		}

		private sealed class ColorFromRGBMethod : FunctionMethod
		{
			public ColorFromRGBMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = true;
			}
			public override long GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				long r = arguments[0].GetIntValue(exm);
				if(r < 0 || r > 255)
					throw new CodeEE("第１引数が0から255の範囲外です");
				long g = arguments[1].GetIntValue(exm);
				if(g< 0 || g > 255)
					throw new CodeEE("第２引数が0から255の範囲外です");
				long b = arguments[2].GetIntValue(exm);
				if(b < 0 || b > 255)
					throw new CodeEE("第３引数が0から255の範囲外です");
				return (r << 16) + (g << 8) + b;
			}
		}
		/// <summary>
		/// 1810 作ったけど保留
		/// </summary>
		private sealed class GetRefMethod : FunctionMethod
		{
			public GetRefMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 1)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null)
					return name + "関数の1番目の引数は省略できません";
				if (!(arguments[0] is UserDefinedRefMethodNoArgTerm))
					return name + "関数の1番目の引数が関数参照ではありません";
				return null;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return ((UserDefinedRefMethodNoArgTerm)arguments[0]).GetRefName();
			}
		}
        #endregion

        #region 定数取得
        private sealed class MoneyStrMethod : FunctionMethod
        {
            public MoneyStrMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常2つ、1つ省略可能で1～2の引数が必要。
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer)
                    return name + "関数の1番目の引数の型が正しくありません";
                if ((arguments.Length >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.String))
                    return name + "関数の2番目の引数の型が正しくありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                long money = arguments[0].GetIntValue(exm);
                if ((arguments.Length < 2) || (arguments[1] == null))
                    return (Config.MoneyFirst) ? Config.MoneyLabel + money.ToString() : money.ToString() + Config.MoneyLabel;
                string format = arguments[1].GetStrValue(exm);
                string ret;
                try
                {
                    ret = money.ToString(format);
                }
                catch (FormatException)
                {
                    throw new CodeEE("MONEYSTR関数の第2引数の書式指定が間違っています");
                }
                return (Config.MoneyFirst) ? Config.MoneyLabel + ret : ret + Config.MoneyLabel;
            }
        }

        private sealed class GetPrintCPerLineMethod : FunctionMethod
        {
            public GetPrintCPerLineMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (Config.PrintCPerLine);
            }
        }

        private sealed class PrintCLengthMethod : FunctionMethod
        {
            public PrintCLengthMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = true;
            }
            public override long GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (Config.PrintCLength);
            }
        }

        private sealed class GetSaveNosMethod : FunctionMethod
        {
            public GetSaveNosMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (Config.SaveDataNos);
            }
        }

        private sealed class GettimeMethod : FunctionMethod
        {
            public GettimeMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                long date = DateTime.Now.Year;
                date = date * 100 + DateTime.Now.Month;
                date = date * 100 + DateTime.Now.Day;
                date = date * 100 + DateTime.Now.Hour;
                date = date * 100 + DateTime.Now.Minute;
                date = date * 100 + DateTime.Now.Second;
                date = date * 1000 + DateTime.Now.Millisecond;
                return (date);//17桁。2京くらい。
            }
        }

        private sealed class GettimesMethod : FunctionMethod
        {
            public GettimesMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));
            }
        }

        private sealed class GetmsMethod : FunctionMethod
        {
            public GetmsMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                //西暦0001年1月1日からの経過時間をミリ秒で。
                //Ticksは100ナノ秒単位であるが実際にはそんな精度はないので無駄。
                return (DateTime.Now.Ticks / 10000);
            }
        }

        private sealed class GetSecondMethod : FunctionMethod
        {
            public GetSecondMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                //西暦0001年1月1日からの経過時間を秒で。
                //Ticksは100ナノ秒単位であるが実際にはそんな精度はないので無駄。
                return (DateTime.Now.Ticks / 10000000);
            }
        }
        #endregion

        #region 数学関数
        private sealed class RandMethod : FunctionMethod
        {
            public RandMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = false;
            }

            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常2つ、1つ省略可能で1～2の引数が必要。
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments.Length == 1)
                {
                    if (arguments[0] == null)
                        return name + "関数には少なくとも1つの引数が必要です";
                    if (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                        return name + "関数の1番目の引数の型が正しくありません";
                    return null;
                }
                //1番目は省略可能
                if (arguments[0] != null && arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                    return name + "関数の1番目の引数の型が正しくありません";
                if (arguments[1] != null && arguments[1].GetEraType() != EraType.Integer && arguments[1].GetEraType() != EraType.Float)
                    return name + "関数の2番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 min = 0;
                long max;
                if (arguments.Length == 1)
                    max = arguments[0].GetIntValue(exm);
                else
                {
                    if (arguments[0] != null)
                        min = arguments[0].GetIntValue(exm);
                    max = arguments[1].GetIntValue(exm);
                }
                if (max <= min)
                {
                    if (min == 0)
                        throw new CodeEE("RANDの最大値に0以下の値(" + max.ToString() + ")が指定されました");
                    else
                        throw new CodeEE("RANDの最大値に最小値以下の値(" + max.ToString() + ")が指定されました");
                }
                return (exm.VEvaluator.GetNextRand(max - min) + min);
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double min = 0.0;
                double max;
                if (arguments.Length == 1)
                    max = ToDouble(arguments[0], exm);
                else
                {
                    if (arguments[0] != null)
                        min = ToDouble(arguments[0], exm);
                    max = ToDouble(arguments[1], exm);
                }
                if (max <= min)
                {
                    if (min == 0.0)
                        throw new CodeEE("RANDの最大値に0以下の値(" + max.ToString() + ")が指定されました");
                    else
                        throw new CodeEE("RANDの最大値に最小値以下の値(" + max.ToString() + ")が指定されました");
                }
                return exm.VEvaluator.GetNextRandDouble() * (max - min) + min;
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class MaxMethod : FunctionMethod
        {
            readonly bool isMax;
            public MaxMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                isMax = true;
                CanRestructure = true;
            }
            public MaxMethod(bool max)
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                isMax = max;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
                    if (arguments[i].GetEraType() != EraType.Integer && arguments[i].GetEraType() != EraType.Float)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
                }
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 ret = arguments[0].GetIntValue(exm);

                for (int i = 1; i < arguments.Length; i++)
                {
                    Int64 newRet = arguments[i].GetIntValue(exm);
                    if (isMax)
                    {
                        if (ret < newRet)
                            ret = newRet;
                    }
                    else
                    {
                        if (ret > newRet)
                            ret = newRet;
                    }
                }
                return (ret);
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double ret = ToDouble(arguments[0], exm);

                for (int i = 1; i < arguments.Length; i++)
                {
                    double newRet = ToDouble(arguments[i], exm);
                    if (isMax)
                    {
                        if (ret < newRet)
                            ret = newRet;
                    }
                    else
                    {
                        if (ret > newRet)
                            ret = newRet;
                    }
                }
                return ret;
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class AbsMethod : FunctionMethod
        {
            public AbsMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 1)
                    return name + "関数の引数の数が正しくありません";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                    return name + "関数の1番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 ret = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : (Int64)arguments[0].GetFloatValue(exm);
                if (ret == Int64.MinValue)
                    throw new CodeEE("ABS関数の引数にInt64の最小値を指定することはできません");
                return (Math.Abs(ret));
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return Math.Abs(ToDouble(arguments[0], exm));
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class PowerMethod : FunctionMethod
        {
            public PowerMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 2)
                    return name + "関数の引数の数が正しくありません";
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
                    if (arguments[i].GetEraType() != EraType.Integer && arguments[i].GetEraType() != EraType.Float)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
                }
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double x = ToDouble(arguments[0], exm);
                double y = ToDouble(arguments[1], exm);
                double pow = Math.Pow(x, y);
                if (double.IsNaN(pow))
                    throw new CodeEE("累乗結果が非数値です");
                else if (double.IsInfinity(pow))
                    throw new CodeEE("累乗結果が無限大です");
                else if ((pow >= Int64.MaxValue) || (pow <= Int64.MinValue))
                    throw new CodeEE("累乗結果(" + pow.ToString() + ")が64ビット符号付き整数の範囲外です");
                return ((long)pow);
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double pow = Math.Pow(ToDouble(arguments[0], exm), ToDouble(arguments[1], exm));
                if (double.IsNaN(pow))
                    throw new CodeEE("累乗結果が非数値です");
                else if (double.IsInfinity(pow))
                    throw new CodeEE("累乗結果が無限大です");
                return pow;
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class SqrtMethod : FunctionMethod
        {
            public SqrtMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 1)
                    return name + "関数の引数の数が正しくありません";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                    return name + "関数の1番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 ret = arguments[0].GetIntValue(exm);
                if (ret < 0)
                    throw new CodeEE("SQRT関数の引数に負の値が指定されました");
                return ((Int64)Math.Sqrt(ret));
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double ret = ToDouble(arguments[0], exm);
                if (ret < 0)
                    throw new CodeEE("SQRT関数の引数に負の値が指定されました");
                return Math.Sqrt(ret);
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class CbrtMethod : FunctionMethod
        {
            public CbrtMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 1)
                    return name + "関数の引数の数が正しくありません";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                    return name + "関数の1番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double ret = ToDouble(arguments[0], exm);
                if (ret < 0)
                    throw new CodeEE("CBRT関数の引数に負の値が指定されました");
                return ((Int64)Math.Pow(ret, 1.0 / 3.0));
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double ret = ToDouble(arguments[0], exm);
                if (ret < 0)
                    throw new CodeEE("CBRT関数の引数に負の値が指定されました");
                return Math.Pow(ret, 1.0 / 3.0);
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class LogMethod : FunctionMethod
        {
            readonly double Base;
            public LogMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                Base = Math.E;
                CanRestructure = true;
            }
            public LogMethod(double b)
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                Base = b;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 1)
                    return name + "関数の引数の数が正しくありません";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                    return name + "関数の1番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double ret = ToDouble(arguments[0], exm);
                if (ret <= 0)
                    throw new CodeEE("対数関数の引数に0以下の値が指定されました");
                if (Base <= 0.0d)
                    throw new CodeEE("対数関数の底に0以下の値が指定されました");
                double dret = ret;
                if (Base == Math.E)
                    dret = Math.Log(dret);
                else
                    dret = Math.Log10(dret);
                if (double.IsNaN(dret))
                    throw new CodeEE("計算値が非数値です");
                else if (double.IsInfinity(dret))
                    throw new CodeEE("計算値が無限大です");
                else if ((dret >= Int64.MaxValue) || (dret <= Int64.MinValue))
                    throw new CodeEE("計算結果(" + dret.ToString() + ")が64ビット符号付き整数の範囲外です");
                return ((Int64)dret);
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double ret = ToDouble(arguments[0], exm);
                if (ret <= 0)
                    throw new CodeEE("対数関数の引数に0以下の値が指定されました");
                if (Base <= 0.0d)
                    throw new CodeEE("対数関数の底に0以下の値が指定されました");
                double dret = ret;
                if (Base == Math.E)
                    dret = Math.Log(dret);
                else
                    dret = Math.Log10(dret);
                if (double.IsNaN(dret))
                    throw new CodeEE("計算値が非数値です");
                else if (double.IsInfinity(dret))
                    throw new CodeEE("計算値が無限大です");
                return dret;
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class ExpMethod : FunctionMethod
        {
            public ExpMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 1)
                    return name + "関数の引数の数が正しくありません";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                    return name + "関数の1番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double dret = Math.Exp(ToDouble(arguments[0], exm));
                if (double.IsNaN(dret))
                    throw new CodeEE("計算値が非数値です");
                else if (double.IsInfinity(dret))
                    throw new CodeEE("計算値が無限大です");
                else if ((dret >= Int64.MaxValue) || (dret <= Int64.MinValue))
                    throw new CodeEE("計算結果(" + dret.ToString() + ")が64ビット符号付き整数の範囲外です");

                return ((Int64)dret);
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double dret = Math.Exp(ToDouble(arguments[0], exm));
                if (double.IsNaN(dret))
                    throw new CodeEE("計算値が非数値です");
                else if (double.IsInfinity(dret))
                    throw new CodeEE("計算値が無限大です");
                return dret;
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class SignMethod : FunctionMethod
        {

            public SignMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 1)
                    return name + "関数の引数の数が正しくありません";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float)
                    return name + "関数の1番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return (Int64)Math.Sign(ToDouble(arguments[0], exm));
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return Math.Sign(ToDouble(arguments[0], exm));
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class GetLimitMethod : FunctionMethod
        {
            public GetLimitMethod()
            {
                ReturnType = EraType.Integer;
                CanReturnFloat = true;
                argumentTypeArray = null;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length != 3)
                    return name + "関数の引数の数が正しくありません";
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
                    if (arguments[i].GetEraType() != EraType.Integer && arguments[i].GetEraType() != EraType.Float)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
                }
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 value = arguments[0].GetIntValue(exm);
                Int64 min = arguments[1].GetIntValue(exm);
                Int64 max = arguments[2].GetIntValue(exm);
                long ret;
                if (value < min)
                    ret = min;
                else if (value > max)
                    ret = max;
                else
                    ret = value;
                return (ret);
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                double value = ToDouble(arguments[0], exm);
                double min = ToDouble(arguments[1], exm);
                double max = ToDouble(arguments[2], exm);
                if (value < min)
                    return min;
                if (value > max)
                    return max;
                return value;
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (HasFloatArg(arguments))
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }
        private static double ToDouble(IOperandTerm term, ExpressionMediator exm)
        {
            return term.GetEraType() == EraType.Integer ? term.GetIntValue(exm) : term.GetFloatValue(exm);
        }

        private static bool HasFloatArg(IOperandTerm[] arguments)
        {
            for (int i = 0; i < arguments.Length; i++)
                if (arguments[i] != null && arguments[i].GetEraType() == EraType.Float)
                    return true;
            return false;
        }
        #endregion

        #region 変数操作系
        private sealed class SumArrayMethod : FunctionMethod
        {
            readonly bool isCharaRange;
            public SumArrayMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = false;
                CanRestructure = false;
            }
            public SumArrayMethod(bool isChara)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = isChara;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm))
                    return name + "関数の1番目の引数が変数ではありません";
                VariableTerm varToken = (VariableTerm)arguments[0];
                if (varToken.IsString)
                    return name + "関数の1番目の引数が数値変数ではありません";
                if (isCharaRange && !varToken.Identifier.IsCharacterData)
                    return name + "関数の1番目の引数がキャラクタ変数ではありません";
                if (!isCharaRange && !varToken.Identifier.IsArray1D && !varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D)
                    return name + "関数の1番目の引数が配列変数ではありません";
                if (arguments.Length == 1)
                    return null;
                if ((arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の変数が数値ではありません";
                if (arguments.Length == 2)
                    return null;
                if ((arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の変数が数値ではありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm varTerm = (VariableTerm)arguments[0];
                Int64 index1 = (arguments.Length >= 2 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
                Int64 index2 = (arguments.Length == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLastLength());

                FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
                if (!isCharaRange)
                {
                    p.IsArrayRangeValid(index1, index2, "SUMARRAY", 2L, 3L);
                    return (exm.VEvaluator.GetArraySum(p, index1, index2));
                }
                else
                {
                    Int64 charaNum = exm.VEvaluator.CHARANUM;
                    if (index1 >= charaNum || index1 < 0 || index2 > charaNum || index2 < 0)
                        throw new CodeEE("SUMCARRAY関数の範囲指定がキャラクタ配列の範囲を超えています(" + index1.ToString() + "～" + index2.ToString() + ")");
                    return (exm.VEvaluator.GetArraySumChara(p, index1, index2));
                }
            }
            public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm varTerm = (VariableTerm)arguments[0];
                Int64 index1 = (arguments.Length >= 2 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
                Int64 index2 = (arguments.Length == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLastLength());

                FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
                if (!isCharaRange)
                {
                    p.IsArrayRangeValid(index1, index2, "SUMARRAY", 2L, 3L);
                    return exm.VEvaluator.GetArraySumDouble(p, index1, index2);
                }
                else
                {
                    Int64 charaNum = exm.VEvaluator.CHARANUM;
                    if (index1 >= charaNum || index1 < 0 || index2 > charaNum || index2 < 0)
                        throw new CodeEE("SUMCARRAY関数の範囲指定がキャラクタ配列の範囲を超えています(" + index1.ToString() + "～" + index2.ToString() + ")");
                    return exm.VEvaluator.GetArraySumCharaDouble(p, index1, index2);
                }
            }
            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm varTerm = (VariableTerm)arguments[0];
                if (varTerm.Identifier.GetEraType() == EraType.Float)
                    return new SingleTerm(GetFloatValue(exm, arguments));
                return new SingleTerm(GetIntValue(exm, arguments));
            }
        }

        private sealed class MatchMethod : FunctionMethod
        {
            readonly bool isCharaRange;
            public MatchMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = false;
                CanRestructure = false;
                HasUniqueRestructure = true;
            }
            public MatchMethod(bool isChara)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = isChara;
                CanRestructure = false;
                HasUniqueRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm))
                    return name + "関数の1番目の引数が変数ではありません";
                VariableTerm varToken = (VariableTerm)arguments[0];
                if (isCharaRange && !varToken.Identifier.IsCharacterData)
                    return name + "関数の1番目の引数がキャラクタ変数ではありません";
                if (!isCharaRange && (varToken.Identifier.IsArray2D || varToken.Identifier.IsArray3D))
                    return name + "関数は二重配列・三重配列には対応していません";
                if (!isCharaRange && !varToken.Identifier.IsArray1D)
                    return name + "関数の1番目の引数が配列変数ではありません";
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arguments[1].GetEraType() != arguments[0].GetEraType())
                    return name + "関数の1番目の引数と2番目の引数の型が異なります";
                if ((arguments.Length >= 3) && (arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の引数の型が正しくありません";
                if ((arguments.Length >= 4) && (arguments[3] != null) && (arguments[3].GetEraType() != EraType.Integer))
                    return name + "関数の4番目の引数の型が正しくありません";
                return null;
            }

            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm varTerm = arguments[0] as VariableTerm;
                Int64 start = (arguments.Length > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
                Int64 end = (arguments.Length > 3 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLength());

                FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
                if (!isCharaRange)
                {
                    p.IsArrayRangeValid(start, end, "MATCH", 3L, 4L);
                    if (arguments[0].GetEraType() == EraType.Integer)
                    {
                        Int64 targetValue = arguments[1].GetIntValue(exm);
                        return (exm.VEvaluator.GetMatch(p, targetValue, start, end));
                    }
                    else if (arguments[0].GetEraType() == EraType.Float)
                    {
                        double targetValue = arguments[1].GetFloatValue(exm);
                        return exm.VEvaluator.GetMatch(p, targetValue, start, end);
                    }
                    else
                    {
                        string targetStr = arguments[1].GetStrValue(exm);
                        return (exm.VEvaluator.GetMatch(p, targetStr, start, end));
                    }
                }
                else
                {
                    Int64 charaNum = exm.VEvaluator.CHARANUM;
                    if (start >= charaNum || start < 0 || end > charaNum || end < 0)
                        throw new CodeEE("CMATCH関数の範囲指定がキャラクタ配列の範囲を超えています(" + start.ToString() + "～" + end.ToString() + ")");
                    if (arguments[0].GetEraType() == EraType.Integer)
                    {
                        Int64 targetValue = arguments[1].GetIntValue(exm);
                        return (exm.VEvaluator.GetMatchChara(p, targetValue, start, end));
                    }
                    else if (arguments[0].GetEraType() == EraType.Float)
                    {
                        double targetValue = arguments[1].GetFloatValue(exm);
                        return exm.VEvaluator.GetMatchChara(p, targetValue, start, end);
                    }
                    else
                    {
                        string targetStr = arguments[1].GetStrValue(exm);
                        return (exm.VEvaluator.GetMatchChara(p, targetStr, start, end));
                    }
                }
            }

            public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                arguments[0].Restructure(exm);
                for (int i = 1; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        continue;
                    arguments[i] = arguments[i].Restructure(exm);
                }
                return false;
            }
        }

        private sealed class GroupMatchMethod : FunctionMethod
        {
            public GroupMatchMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                EraType baseType = arguments[0].GetEraType();
                for (int i = 1; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
                    if (arguments[i].GetEraType() != baseType)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
                }
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 ret = 0;
                if (arguments[0].GetEraType() == EraType.Integer)
                {
                    Int64 baseValue = arguments[0].GetIntValue(exm);
                    for (int i = 1; i < arguments.Length; i++)
                    {
                        if (baseValue == arguments[i].GetIntValue(exm))
                            ret += 1;
                    }
                }
                else if (arguments[0].GetEraType() == EraType.Float)
                {
                    double baseValue = arguments[0].GetFloatValue(exm);
                    for (int i = 1; i < arguments.Length; i++)
                    {
                        if (baseValue == arguments[i].GetFloatValue(exm))
                            ret += 1;
                    }
                }
                else
                {
                    string baseString = arguments[0].GetStrValue(exm);
                    for (int i = 1; i < arguments.Length; i++)
                    {
                        if (baseString == arguments[i].GetStrValue(exm))
                            ret += 1;
                    }
                }
                return (ret);
            }
        }

        private sealed class NosamesMethod : FunctionMethod
        {
            public NosamesMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                EraType baseType = arguments[0].GetEraType();
                for (int i = 1; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
                    if (arguments[i].GetEraType() != baseType)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
                }
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (arguments[0].GetEraType() == EraType.Integer)
                {
                    Int64[] valueArray = new Int64[arguments.Length];
                    for (int i = 0; i < arguments.Length; i++)
                    {
                        valueArray[i] = arguments[i].GetIntValue(exm);
                    }
                    var resultArray = valueArray.Distinct();
                    if (resultArray.Count() != arguments.Length)
                        return 0L;
                }
                else if (arguments[0].GetEraType() == EraType.Float)
                {
                    double[] valueArray = new double[arguments.Length];
                    for (int i = 0; i < arguments.Length; i++)
                    {
                        valueArray[i] = arguments[i].GetFloatValue(exm);
                    }
                    var resultArray = valueArray.Distinct();
                    if (resultArray.Count() != arguments.Length)
                        return 0L;
                }
                else
                {
                    string[] stringArray = new string[arguments.Length];
                    for (int i = 0; i < arguments.Length; i++)
                    {
                        stringArray[i] = arguments[i].GetStrValue(exm);
                    }
                    var resultArray = stringArray.Distinct();
                    if (resultArray.Count() != arguments.Length)
                        return 0L;
                }
                return 1L;
            }
        }

        private sealed class AllsamesMethod : FunctionMethod
        {
            public AllsamesMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                EraType baseType = arguments[0].GetEraType();
                for (int i = 1; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
                    if (arguments[i].GetEraType() != baseType)
                        return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
                }
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                if (arguments[0].GetEraType() == EraType.Integer)
                {
                    Int64 baseValue = arguments[0].GetIntValue(exm);
                    for (int i = 1; i < arguments.Length; i++)
                    {
                        if (baseValue != arguments[i].GetIntValue(exm))
                            return 0L;
                    }
                }
                else if (arguments[0].GetEraType() == EraType.Float)
                {
                    double baseValue = arguments[0].GetFloatValue(exm);
                    for (int i = 1; i < arguments.Length; i++)
                    {
                        if (baseValue != arguments[i].GetFloatValue(exm))
                            return 0L;
                    }
                }
                else
                {
                    string baseValue = arguments[0].GetStrValue(exm);
                    for (int i = 1; i < arguments.Length; i++)
                    {
                        if (baseValue != arguments[i].GetStrValue(exm))
                            return 0L;
                    }
                }
                return 1L;
            }
        }

        private sealed class MaxArrayMethod : FunctionMethod
        {
            readonly bool isCharaRange;
            readonly bool isMax;
            readonly string funcName;
            public MaxArrayMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = false;
                isMax = true;
                funcName = "MAXARRAY";
                CanRestructure = false;
            }
            public MaxArrayMethod(bool isChara)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = isChara;
                isMax = true;
                if (isCharaRange)
                    funcName = "MAXCARRAY";
                else
                    funcName = "MAXARRAY";
                CanRestructure = false;
            }
            public MaxArrayMethod(bool isChara, bool isMaxFunc)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = isChara;
                isMax = isMaxFunc;
                funcName = (isMax ? "MAX" : "MIN") + (isCharaRange ? "C" : "") + "ARRAY";
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm))
                    return name + "関数の1番目の引数が変数ではありません";
                VariableTerm varToken = (VariableTerm)arguments[0];
                if (isCharaRange && !varToken.Identifier.IsCharacterData)
                    return name + "関数の1番目の引数がキャラクタ変数ではありません";
                if (!varToken.IsInteger && !varToken.IsFloat)
                    return name + "関数の1番目の引数が数値変数ではありません";
                if (!isCharaRange && (varToken.Identifier.IsArray2D || varToken.Identifier.IsArray3D))
                    return name + "関数は二重配列・三重配列には対応していません";
                if (!varToken.Identifier.IsArray1D)
                    return name + "関数の1番目の引数が配列変数ではありません";
                if ((arguments.Length >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の引数の型が正しくありません";
                if ((arguments.Length >= 3) && (arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm vTerm = (VariableTerm)arguments[0];
                Int64 start = (arguments.Length > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
                Int64 end = (arguments.Length > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : vTerm.GetLength());
                FixedVariableTerm p = vTerm.GetFixedVariableTerm(exm);
                if (!isCharaRange)
                {
                    p.IsArrayRangeValid(start, end, funcName, 2L, 3L);
                    if (vTerm.Identifier.GetEraType() == EraType.Float)
                        return (Int64)exm.VEvaluator.GetMaxArrayDouble(p, start, end, isMax);
                    return (exm.VEvaluator.GetMaxArray(p, start, end, isMax));
                }
                else
                {
                    Int64 charaNum = exm.VEvaluator.CHARANUM;
                    if (start >= charaNum || start < 0 || end > charaNum || end < 0)
                        throw new CodeEE(funcName + "関数の範囲指定がキャラクタ配列の範囲を超えています(" + start.ToString() + "～" + end.ToString() + ")");
                    if (vTerm.Identifier.GetEraType() == EraType.Float)
                        return (Int64)exm.VEvaluator.GetMaxArrayCharaDouble(p, start, end, isMax);
                    return (exm.VEvaluator.GetMaxArrayChara(p, start, end, isMax));
                }
            }
        }

        private sealed class GetbitMethod : FunctionMethod
        {
            public GetbitMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                string ret = base.CheckArgumentType(name, arguments);
                if (ret != null)
                    return ret;
                if (arguments[1] is SingleTerm)
                {
                    Int64 m = ((SingleTerm)arguments[1]).Int;
                    if (m < 0 || m > 63)
                        return "GETBIT関数の第２引数(" + m.ToString() + ")が範囲(０～６３)を超えています";
                }
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 n = arguments[0].GetIntValue(exm);
                Int64 m = arguments[1].GetIntValue(exm);
                if ((m < 0) || (m > 63))
                    throw new CodeEE("GETBIT関数の第２引数(" + m.ToString() + ")が範囲(０～６３)を超えています");
                int mi = (int)m;
                return ((n >> mi) & 1);
            }
        }

        private sealed class GetnumMethod : FunctionMethod
        {
            public GetnumMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = true;
                HasUniqueRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2 || arguments.Length > 3)
                    return name + "関数には2つまたは3つの引数が必要です";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm))
                    return name + "関数の1番目の引数の型が正しくありません";
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arguments[1].GetEraType() != EraType.String)
                    return name + "関数の2番目の引数の型が正しくありません";
				if (arguments.Length == 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
					return name + "関数の3番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm vToken = (VariableTerm)arguments[0];
                VariableCode varCode = vToken.Identifier.Code;
				string varname = arguments.Length > 2 && arguments[2] != null
					? vToken.Identifier.Name + "@" + arguments[2].GetIntValue(exm)
					: vToken.Identifier.Name;
                string key = arguments[1].GetStrValue(exm);
                if (exm.VEvaluator.Constant.TryKeywordToInteger(out int ret, varCode, key, -1, varname))
                    return ret;
                else
                    return -1;
            }
            public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                arguments[1] = arguments[1].Restructure(exm);
                return arguments[1] is SingleTerm;
            }
        }

		private sealed class GetnumBMethod : FunctionMethod
		{
			public GetnumBMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.String };
				CanRestructure = true;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length != 2)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
				if (arguments[0] == null)
					return name + "関数の1番目の引数は省略できません";
				if (arguments[1] == null)
					return name + "関数の2番目の引数は省略できません";
				if (arguments[1].GetEraType() != EraType.String)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableToken var = GlobalStatic.IdentifierDictionary.GetVariableToken(arguments[0].GetStrValue(exm), null, true);
				if (var == null)
					throw new CodeEE("GETNUMBの1番目の引数(\"" + arguments[0].GetStrValue(exm) + "\")が変数名ではありません");
				string key = arguments[1].GetStrValue(exm);
                if (exm.VEvaluator.Constant.TryKeywordToInteger(out int ret, var.Code, key, -1, arguments[0].GetStrValue(exm)))
                    return ret;
                else
                    return -1;
            }
		}

        private sealed class GetPalamLVMethod : FunctionMethod
        {
            public GetPalamLVMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                string errStr = base.CheckArgumentType(name, arguments);
                if (errStr != null)
                    return errStr;
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 value = arguments[0].GetIntValue(exm);
                Int64 maxLv = arguments[1].GetIntValue(exm);

                return (exm.VEvaluator.getPalamLv(value, maxLv));
            }
        }

        private sealed class GetExpLVMethod : FunctionMethod
        {
            public GetExpLVMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
                CanRestructure = false;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                string errStr = base.CheckArgumentType(name, arguments);
                if (errStr != null)
                    return errStr;
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 value = arguments[0].GetIntValue(exm);
                Int64 maxLv = arguments[1].GetIntValue(exm);

                return (exm.VEvaluator.getExpLv(value, maxLv));
            }
        }

        private sealed class FindElementMethod : FunctionMethod
        {
            public FindElementMethod(bool last)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = true; //すべて定数項ならできるはず
                HasUniqueRestructure = true;
                isLast = last;
                funcName = isLast ? "FINDLASTELEMENT" : "FINDELEMENT";
            }

            readonly bool isLast;
            readonly string funcName;
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 5)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm varToken))
                    return name + "関数の1番目の引数が変数ではありません";
                if (varToken.Identifier.IsArray2D || varToken.Identifier.IsArray3D)
                    return name + "関数は二重配列・三重配列には対応していません";
                if (!varToken.Identifier.IsArray1D)
                    return name + "関数の1番目の引数が配列変数ではありません";
                EraType baseType = arguments[0].GetEraType();
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arguments[1].GetEraType() != baseType)
                    return name + "関数の2番目の引数の型が正しくありません";
                if ((arguments.Length >= 3) && (arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の引数の型が正しくありません";
                if ((arguments.Length >= 4) && (arguments[3] != null) && (arguments[3].GetEraType() != EraType.Integer))
                    return name + "関数の4番目の引数の型が正しくありません";
                if ((arguments.Length >= 5) && (arguments[4] != null) && (arguments[4].GetEraType() != EraType.Integer))
                    return name + "関数の5番目の引数の型が正しくありません";
                return null;
            }

            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                bool isExact = false;
                VariableTerm varTerm = (VariableTerm)arguments[0];

                Int64 start = (arguments.Length > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
                Int64 end = (arguments.Length > 3 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : varTerm.GetLength();
                if (arguments.Length > 4 && arguments[4] != null)
                    isExact = (arguments[4].GetIntValue(exm) != 0);

                FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
                p.IsArrayRangeValid(start, end, funcName, 3L, 4L);

                if (arguments[0].GetEraType() == EraType.Integer)
                {
                    Int64 targetValue = arguments[1].GetIntValue(exm);
                    return exm.VEvaluator.FindElement(p, targetValue, start, end, isExact, isLast);
                }
                else
                {
                    Regex targetString;
                    try
                    {
                        targetString = new Regex(arguments[1].GetStrValue(exm));
                    }
                    catch (ArgumentException)
                    {
                        throw new CodeEE("第2引数が正規表現として不正です");
                    }
                    return exm.VEvaluator.FindElement(p, targetString, start, end, isExact, isLast);
                }
            }
            
            
            public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                arguments[0].Restructure(exm);
                VariableTerm varToken = arguments[0] as VariableTerm;
                bool isConst = varToken.Identifier.IsConst;
                for (int i = 1; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        continue;
                    arguments[i] = arguments[i].Restructure(exm);
                    if (isConst && !(arguments[i] is SingleTerm))
                        isConst = false;
                }
                return isConst;
            }
        }

        private sealed class InRangeMethod : FunctionMethod
        {
            public InRangeMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 value = arguments[0].GetIntValue(exm);
                Int64 min = arguments[1].GetIntValue(exm);
                Int64 max = arguments[2].GetIntValue(exm);
                return ((value >= min) && (value <= max)) ? 1L : 0L;
            }
        }

        private sealed class InRangeArrayMethod : FunctionMethod
        {
            public InRangeArrayMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = false;
            }
            public InRangeArrayMethod(bool isChara)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                isCharaRange = isChara;
                CanRestructure = false;
            }
            private readonly bool isCharaRange = false;
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 3)
                    return name + "関数には少なくとも3つの引数が必要です";
                if (arguments.Length > 5)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm))
                    return name + "関数の1番目の引数が変数ではありません";
                VariableTerm varToken = (VariableTerm)arguments[0];
                if (isCharaRange && !varToken.Identifier.IsCharacterData)
                    return name + "関数の1番目の引数がキャラクタ変数ではありません";
                if (!isCharaRange && (varToken.Identifier.IsArray2D || varToken.Identifier.IsArray3D))
                    return name + "関数は二重配列・三重配列には対応していません";
                if (!isCharaRange && !varToken.Identifier.IsArray1D)
                    return name + "関数の1番目の引数が配列変数ではありません";
                EraType arrayType = varToken.Identifier.GetEraType();
                if (arrayType != EraType.Integer && arrayType != EraType.Float)
                    return name + "関数の1番目の引数が数値型変数ではありません";
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arrayType == EraType.Float)
                {
                    if (arguments[1].GetEraType() != EraType.Integer && arguments[1].GetEraType() != EraType.Float)
                        return name + "関数の2番目の引数が数値型ではありません";
                }
                else if (arguments[1].GetEraType() != EraType.Integer)
                    return name + "関数の2番目の引数が数値型ではありません";
                if (arguments[2] == null)
                    return name + "関数の3番目の引数は省略できません";
                if (arrayType == EraType.Float)
                {
                    if (arguments[2].GetEraType() != EraType.Integer && arguments[2].GetEraType() != EraType.Float)
                        return name + "関数の3番目の引数が数値型ではありません";
                }
                else if (arguments[2].GetEraType() != EraType.Integer)
                    return name + "関数の3番目の引数が数値型ではありません";
                if ((arguments.Length >= 4) && (arguments[3] != null) && (arguments[3].GetEraType() != EraType.Integer))
                    return name + "関数の4番目の引数の型が正しくありません";
                if ((arguments.Length >= 5) && (arguments[4] != null) && (arguments[4].GetEraType() != EraType.Integer))
                    return name + "関数の5番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm varTerm = arguments[0] as VariableTerm;
                Int64 start = (arguments.Length > 3 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : 0;
                Int64 end = (arguments.Length > 4 && arguments[4] != null) ? arguments[4].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLength());

                FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
                bool isFloatArray = varTerm.Identifier.GetEraType() == EraType.Float;

                if (!isCharaRange)
                {
                    p.IsArrayRangeValid(start, end, "INRANGEARRAY", 4L, 5L);
                    if (isFloatArray)
                    {
                        double min = ToDouble(arguments[1], exm);
                        double max = ToDouble(arguments[2], exm);
                        return exm.VEvaluator.GetInRangeArrayDouble(p, min, max, start, end);
                    }
                    else
                    {
                        Int64 min = arguments[1].GetIntValue(exm);
                        Int64 max = arguments[2].GetIntValue(exm);
                        return exm.VEvaluator.GetInRangeArray(p, min, max, start, end);
                    }
                }
                else
                {
                    Int64 charaNum = exm.VEvaluator.CHARANUM;
                    if (start >= charaNum || start < 0 || end > charaNum || end < 0)
                        throw new CodeEE("INRANGECARRAY関数の範囲指定がキャラクタ配列の範囲を超えています(" + start.ToString() + "～" + end.ToString() + ")");
                    if (isFloatArray)
                    {
                        double min = ToDouble(arguments[1], exm);
                        double max = ToDouble(arguments[2], exm);
                        return exm.VEvaluator.GetInRangeArrayCharaDouble(p, min, max, start, end);
                    }
                    else
                    {
                        Int64 min = arguments[1].GetIntValue(exm);
                        Int64 max = arguments[2].GetIntValue(exm);
                        return exm.VEvaluator.GetInRangeArrayChara(p, min, max, start, end);
                    }
                }
            }
        }

		private sealed class ArrayMultiSortMethod : FunctionMethod
		{
			public ArrayMultiSortMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
				HasUniqueRestructure = true;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2)
					return string.Format("{0}関数:少なくとも{1}の引数が必要です", name, 2);
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						return string.Format("{0}関数:{1}番目の引数は省略できません", name, i + 1);
                    if (!(arguments[i] is VariableTerm varTerm) || varTerm.Identifier.IsCalc || varTerm.Identifier.IsConst)
                        return string.Format("{0}関数:{1}番目の引数が変数ではありません", name, i + 1);
					if (varTerm.Identifier.IsCharacterData)
						return string.Format("{0}関数:{1}番目の引数がキャラクタ変数です", name, i + 1);
					if (i == 0 && !varTerm.Identifier.IsArray1D)
						return string.Format("{0}関数:{1}番目の引数が一次元配列ではありません", name, i + 1);
					if (!varTerm.Identifier.IsArray1D && !varTerm.Identifier.IsArray2D && !varTerm.Identifier.IsArray3D)
						return string.Format("{0}関数:{1}番目の引数が配列変数ではありません", name, i + 1);
					if (!varTerm.Identifier.IsInteger && !varTerm.Identifier.IsString && !varTerm.Identifier.IsFloat)
						return string.Format("{0}関数:{1}番目の引数の型が正しくありません", name, i + 1);
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableTerm varTerm = arguments[0] as VariableTerm;
				int[] sortedArray = BuildSortedIndices(varTerm, true, -1, false);
				if (sortedArray == null)
					return 0;
				foreach (VariableTerm term in arguments)//もう少し賢い方法はないものだろうか
				{
					if (!ApplySortedIndices(term, sortedArray))
						return 0;
				}
				return 1;
			}
			public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				for (int i = 0; i < arguments.Length; i++)
					arguments[i] = arguments[i].Restructure(exm);
				return false;
			}
		}

		private sealed class ArrayMultiSortExMethod : FunctionMethod
		{
			public ArrayMultiSortExMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
				HasUniqueRestructure = true;
			}

			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2)
					return string.Format("{0}関数:少なくとも{1}の引数が必要です", name, 2);
				if (arguments.Length > 4)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null)
					return name + "関数の1番目の引数は省略できません";
				if (!(arguments[0] is VariableTerm) && arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数は配列変数または変数名文字列でなければなりません";
				if (!(arguments[1] is VariableTerm namesTerm) || !namesTerm.Identifier.IsString || !namesTerm.Identifier.IsArray1D)
					return name + "関数の2番目の引数は文字列型1次元配列変数でなければなりません";
				if (namesTerm.Identifier.IsCalc || namesTerm.Identifier.IsConst || namesTerm.Identifier.IsCharacterData)
					return name + "関数の2番目の引数は通常の文字列型1次元配列変数でなければなりません";
				if (arguments.Length >= 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
					return name + "関数の3番目の引数の型が正しくありません";
				if (arguments.Length >= 4 && arguments[3] != null && arguments[3].GetEraType() != EraType.Integer)
					return name + "関数の4番目の引数の型が正しくありません";
				return null;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableTerm baseTerm = GetSortableArrayTerm(arguments[0], exm, "ARRAYMSORTEX");
				bool ascending = arguments.Length < 3 || arguments[2] == null || arguments[2].GetIntValue(exm) != 0;
				long fixedLengthInput = arguments.Length < 4 || arguments[3] == null ? -1 : arguments[3].GetIntValue(exm);
				if (fixedLengthInput == 0)
					return 0;
				if (fixedLengthInput < -1 || fixedLengthInput > int.MaxValue)
					throw new CodeEE("ARRAYMSORTEX関数の4番目の引数が範囲外です");

				int[] sortedIndices = BuildSortedIndices(baseTerm, ascending, (int)fixedLengthInput, true);
				if (sortedIndices == null)
					return 0;

				foreach (string variableName in EnumerateArrayMultiSortTargetNames((VariableTerm)arguments[1]))
				{
					if (!TryParseSnakeVariable(variableName, out VariableTerm targetTerm))
						throw new CodeEE("ARRAYMSORTEX関数:変数\"" + variableName + "\"を解釈できません");
					targetTerm = GetSortableTargetTerm(targetTerm, "ARRAYMSORTEX", variableName);
					if (!ApplySortedIndices(targetTerm, sortedIndices))
						return 0;
				}
				return 1;
			}

			public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] != null)
						arguments[i] = arguments[i].Restructure(exm);
				}
				return false;
			}
		}

		private static VariableTerm GetSortableArrayTerm(IOperandTerm argument, ExpressionMediator exm, string functionName)
		{
			VariableTerm term = argument as VariableTerm;
			if (term == null)
			{
				if (argument == null || argument.GetEraType() != EraType.String || !TryParseSnakeVariable(argument.GetStrValue(exm), out term))
					throw new CodeEE(functionName + "関数の引数が配列変数ではありません");
			}
			if (term.Identifier.IsCalc || term.Identifier.IsConst || term.Identifier.IsCharacterData)
				throw new CodeEE(functionName + "関数の引数が通常の配列変数ではありません");
			if (!term.Identifier.IsArray1D)
				throw new CodeEE(functionName + "関数は一次元配列だけに対応しています");
			if (!term.Identifier.IsInteger && !term.Identifier.IsString && !term.Identifier.IsFloat)
				throw new CodeEE(functionName + "関数の引数の型が正しくありません");
			return term;
		}

		private static VariableTerm GetSortableTargetTerm(VariableTerm term, string functionName, string variableName)
		{
			if (term.Identifier.IsCalc || term.Identifier.IsConst || term.Identifier.IsCharacterData)
				throw new CodeEE(functionName + "関数:変数\"" + variableName + "\"は通常の配列変数ではありません");
			if (!term.Identifier.IsArray1D && !term.Identifier.IsArray2D && !term.Identifier.IsArray3D)
				throw new CodeEE(functionName + "関数:変数\"" + variableName + "\"は配列変数ではありません");
			if (!term.Identifier.IsInteger && !term.Identifier.IsString && !term.Identifier.IsFloat)
				throw new CodeEE(functionName + "関数:変数\"" + variableName + "\"の型が正しくありません");
			return term;
		}

		private static IEnumerable<string> EnumerateArrayMultiSortTargetNames(VariableTerm namesTerm)
		{
			object arrayObject = namesTerm.Identifier.GetArray();
			if (arrayObject is SparseArray<string> sparseArray)
			{
				for (int i = 0; i < sparseArray.Length; i++)
				{
					string variableName = sparseArray[i];
					if (string.IsNullOrWhiteSpace(variableName))
						yield break;
					yield return variableName;
				}
				yield break;
			}

			string[] array = (string[])arrayObject;
			for (int i = 0; i < array.Length; i++)
			{
				string variableName = array[i];
				if (string.IsNullOrWhiteSpace(variableName))
					yield break;
				yield return variableName;
			}
		}

		private static int[] BuildSortedIndices(VariableTerm term, bool ascending, int fixedLength, bool useOrdinalStringComparison)
		{
			object arrayObject = term.Identifier.GetArray();
			if (term.Identifier.IsInteger)
			{
				int length = GetSortLength(GetFirstDimensionLength(arrayObject), fixedLength, i => GetIntArrayValue(arrayObject, i) == 0);
				if (length < 0)
					return null;
				var sortList = new List<KeyValuePair<Int64, int>>(length);
				for (int i = 0; i < length; i++)
					sortList.Add(new KeyValuePair<Int64, int>(GetIntArrayValue(arrayObject, i), i));
				sortList.Sort((a, b) => ascending ? a.Key.CompareTo(b.Key) : b.Key.CompareTo(a.Key));
				return sortList.Select(pair => pair.Value).ToArray();
			}
			if (term.Identifier.IsFloat)
			{
				int length = GetSortLength(GetFirstDimensionLength(arrayObject), fixedLength, i => GetFloatArrayValue(arrayObject, i) == 0.0d);
				if (length < 0)
					return null;
				var sortList = new List<KeyValuePair<double, int>>(length);
				for (int i = 0; i < length; i++)
					sortList.Add(new KeyValuePair<double, int>(GetFloatArrayValue(arrayObject, i), i));
				sortList.Sort((a, b) => ascending ? a.Key.CompareTo(b.Key) : b.Key.CompareTo(a.Key));
				return sortList.Select(pair => pair.Value).ToArray();
			}
			else
			{
				int length = GetSortLength(GetFirstDimensionLength(arrayObject), fixedLength, i => string.IsNullOrEmpty(GetStringArrayValue(arrayObject, i)));
				if (length < 0)
					return null;
				var sortList = new List<KeyValuePair<string, int>>(length);
				for (int i = 0; i < length; i++)
					sortList.Add(new KeyValuePair<string, int>(GetStringArrayValue(arrayObject, i), i));
				sortList.Sort((a, b) =>
				{
					int comparison = useOrdinalStringComparison ? string.CompareOrdinal(a.Key, b.Key) : a.Key.CompareTo(b.Key);
					return ascending ? comparison : -comparison;
				});
				return sortList.Select(pair => pair.Value).ToArray();
			}
		}

		private static int GetSortLength(int arrayLength, int fixedLength, Func<int, bool> isDefault)
		{
			if (fixedLength >= 0)
				return Math.Min(fixedLength, arrayLength);
			int length = 0;
			while (length < arrayLength && !isDefault(length))
				length++;
			return length;
		}

		private static bool ApplySortedIndices(VariableTerm term, int[] sortedIndices)
		{
			object arrayObject = term.Identifier.GetArray();
			if (arrayObject is Array array)
				return ApplySortedIndicesToDenseArray(array, sortedIndices);
			if (arrayObject is SparseArray<Int64> intSparseArray)
				return ApplySortedIndicesToSparseArray(intSparseArray, sortedIndices);
			if (arrayObject is SparseArray<double> floatSparseArray)
				return ApplySortedIndicesToSparseArray(floatSparseArray, sortedIndices);
			if (arrayObject is SparseArray<string> stringSparseArray)
				return ApplySortedIndicesToSparseArray(stringSparseArray, sortedIndices);
			throw new ExeEE("異常な配列");
		}

		private static bool ApplySortedIndicesToDenseArray(Array array, int[] sortedIndices)
		{
			int rank = array.Rank;
			if (rank < 1 || rank > 3)
				throw new ExeEE("異常な配列");
			if (array.GetLength(0) < sortedIndices.Length)
				return false;

			Array clone = (Array)array.Clone();
			for (int i = 0; i < sortedIndices.Length; i++)
			{
				int sourceIndex = sortedIndices[i];
				if (rank == 1)
					array.SetValue(clone.GetValue(sourceIndex), i);
				else if (rank == 2)
				{
					for (int x = 0; x < array.GetLength(1); x++)
						array.SetValue(clone.GetValue(sourceIndex, x), i, x);
				}
				else
				{
					for (int x = 0; x < array.GetLength(1); x++)
						for (int y = 0; y < array.GetLength(2); y++)
							array.SetValue(clone.GetValue(sourceIndex, x, y), i, x, y);
				}
			}
			return true;
		}

		private static bool ApplySortedIndicesToSparseArray<T>(SparseArray<T> array, int[] sortedIndices)
		{
			if (array.Length < sortedIndices.Length)
				return false;
			// 稀疏数组的逻辑长度可能很大，只克隆参与排序的前段，避免在 Android 上把未使用区间全部物化。
			T[] clone = new T[sortedIndices.Length];
			for (int i = 0; i < sortedIndices.Length; i++)
				clone[i] = array[i];
			for (int i = 0; i < sortedIndices.Length; i++)
				array[i] = clone[sortedIndices[i]];
			return true;
		}

		private static int GetFirstDimensionLength(object arrayObject)
		{
			if (arrayObject is SparseArray<Int64> intSparseArray)
				return intSparseArray.Length;
			if (arrayObject is SparseArray<double> floatSparseArray)
				return floatSparseArray.Length;
			if (arrayObject is SparseArray<string> stringSparseArray)
				return stringSparseArray.Length;
			return ((Array)arrayObject).GetLength(0);
		}

		private static Int64 GetIntArrayValue(object arrayObject, int index)
		{
			if (arrayObject is SparseArray<Int64> sparseArray)
				return sparseArray[index];
			return ((Int64[])arrayObject)[index];
		}

		private static double GetFloatArrayValue(object arrayObject, int index)
		{
			if (arrayObject is SparseArray<double> sparseArray)
				return sparseArray[index];
			return ((double[])arrayObject)[index];
		}

		private static string GetStringArrayValue(object arrayObject, int index)
		{
			if (arrayObject is SparseArray<string> sparseArray)
				return sparseArray[index];
			return ((string[])arrayObject)[index];
		}
        #endregion

        #region 文字列操作系
        private sealed class StrlenMethod : FunctionMethod
        {
            public StrlenMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                return (LangManager.GetStrlenLang(str));
            }
        }

        private sealed class StrlenuMethod : FunctionMethod
        {
            public StrlenuMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                return (str.Length);
            }
        }

        private sealed class SubstringMethod : FunctionMethod
        {
            public SubstringMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = null;
                CanRestructure = true;
            }

            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常３つ、２つ省略可能で１～３の引数が必要。
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";

                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.String)
                    return name + "関数の1番目の引数の型が正しくありません";
                //2、３は省略可能
                if ((arguments.Length >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の引数の型が正しくありません";
                if ((arguments.Length >= 3) && (arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の引数の型が正しくありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                int start = 0;
                int length = -1;
                if ((arguments.Length >= 2) && (arguments[1] != null))
                    start = (int)arguments[1].GetIntValue(exm);
                if ((arguments.Length >= 3) && (arguments[2] != null))
                    length = (int)arguments[2].GetIntValue(exm);

                return (LangManager.GetSubStringLang(str, start, length));
            }
        }

        private sealed class SubstringuMethod : FunctionMethod
        {
            public SubstringuMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = null;
                CanRestructure = true;
            }

            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常３つ、２つ省略可能で１～３の引数が必要。
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";

                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.String)
                    return name + "関数の1番目の引数の型が正しくありません";
                //2、３は省略可能
                if ((arguments.Length >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の引数の型が正しくありません";
                if ((arguments.Length >= 3) && (arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の引数の型が正しくありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                int start = 0;
                int length = -1;
                if ((arguments.Length >= 2) && (arguments[1] != null))
                    start = (int)arguments[1].GetIntValue(exm);
                if ((arguments.Length >= 3) && (arguments[2] != null))
                    length = (int)arguments[2].GetIntValue(exm);
                if ((start >= str.Length) || (length == 0))
                    return ("");
                if ((length < 0) || (length > str.Length))
                    length = str.Length;
                if (start <= 0)
                {
                    if (length == str.Length)
                        return (str);
                    else
                        start = 0;
                }
                if ((start + length) > str.Length)
                    length = str.Length - start;

                return (str.Substring(start, length));
            }
        }

        private sealed class StrfindMethod : FunctionMethod
        {
            public StrfindMethod(bool unicode)
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = null;
                CanRestructure = true;
				this.unicode = unicode;
            }

            readonly bool unicode = false;
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常３つ、１つ省略可能で２～３の引数が必要。
                if (arguments.Length < 2)
                    return name + "関数には少なくとも2つの引数が必要です";
                if (arguments.Length > 3)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.String)
                    return name + "関数の1番目の引数の型が正しくありません";
                if (arguments[1] == null)
                    return name + "関数の2番目の引数は省略できません";
                if (arguments[1].GetEraType() != EraType.String)
                    return name + "関数の2番目の引数の型が正しくありません";
                //3つ目は省略可能
                if ((arguments.Length >= 3) && (arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {

                string target = arguments[0].GetStrValue(exm);
                string word = arguments[1].GetStrValue(exm);
                int UFTstart = 0;
				if ((arguments.Length >= 3) && (arguments[2] != null))
				{
					if (unicode)
					{
						UFTstart = (int)arguments[2].GetIntValue(exm);
					}
					else
					{
						UFTstart = LangManager.GetUFTIndex(target, (int)arguments[2].GetIntValue(exm));
					}
				}
                if (UFTstart < 0 || UFTstart >= target.Length)
                    return (-1);
                int index = target.IndexOf(word, UFTstart);
				if (index > 0 && !unicode)
                {
                    string subStr = target.Substring(0, index);
                    index = LangManager.GetStrlenLang(subStr);
                }
                return (index);
            }
        }

        private sealed class StrCountMethod : FunctionMethod
        {
            public StrCountMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.String, EraType.String };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Regex reg;
                try
                {
                    reg = new Regex(arguments[1].GetStrValue(exm));
                }
                catch (ArgumentException e)
                {
                    throw new CodeEE("第2引数が正規表現として不正です：" + e.Message);
                }
                return (reg.Matches(arguments[0].GetStrValue(exm)).Count);
            }
        }

        private sealed class ToStrMethod : FunctionMethod
        {
            public ToStrMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = null;
                CanRestructure = true;
            }

            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常2つ、1つ省略可能で1～2の引数が必要。
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.Integer)
                    return name + "関数の1番目の引数の型が正しくありません";
                if ((arguments.Length >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.String))
                    return name + "関数の2番目の引数の型が正しくありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 i = arguments[0].GetIntValue(exm);
                if ((arguments.Length < 2) || (arguments[1] == null))
                    return (i.ToString());
                string format = arguments[1].GetStrValue(exm);
                string ret;
                try
                {
                    ret = i.ToString(format);
                }
                catch (FormatException)
                {
                    throw new CodeEE("TOSTR関数の書式指定が間違っています");
                }
                return (ret);
            }
        }

        private sealed class ToIntMethod : FunctionMethod
        {
            public ToIntMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }

            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                if (str == null || str == "")
                    return (0);
                //全角文字が入ってるなら無条件で0を返す
                if (str.Length < LangManager.GetStrlenLang(str))
                    return (0);
                StringStream st = new StringStream(str);
                if (!char.IsDigit(st.Current) && st.Current != '+' && st.Current != '-')
                    return (0);
                else if ((st.Current == '+' || st.Current == '-') && !char.IsDigit(st.Next))
                    return (0);
                Int64 ret = LexicalAnalyzer.ReadInt64(st, true);
                if (!st.EOS)
                {
                    if (st.Current == '.')
                    {
                        st.ShiftNext();
                        while (!st.EOS)
                        {
                            if (!char.IsDigit(st.Current))
                                return (0);
                            st.ShiftNext();
                        }
                    }
                    else
                        return (0);
                }
                return ret;
            }
        }

        private sealed class ToFloatMethod : FunctionMethod
        {
            public ToFloatMethod()
            {
                ReturnType = EraType.Float;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }

            public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                if (string.IsNullOrEmpty(str))
                    return new SingleTerm(0.0);
                if (str.Length < LangManager.GetStrlenLang(str))
                    return new SingleTerm(0.0);
                if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
                    return new SingleTerm(result);
                return new SingleTerm(0.0);
            }
        }

        //難読化用属性。enum.ToString()やenum.Parse()を行うなら(Exclude=true)にすること。
        [global::System.Reflection.Obfuscation(Exclude = false)]
        //TOUPPER等の処理を汎用化するためのenum
        enum StrFormType
        {
            Upper = 0,
            Lower = 1,
            Half = 2,
            Full = 3,
        };

        private sealed class StrChangeStyleMethod : FunctionMethod
        {
            readonly StrFormType strType;
            public StrChangeStyleMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String };
                strType = StrFormType.Upper;
                CanRestructure = true;
            }
            public StrChangeStyleMethod(StrFormType type)
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String };
                strType = type;
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                if (str == null || str == "")
                    return ("");
                switch (strType)
                {
                    case StrFormType.Upper:
                        return (str.ToUpper());
                    case StrFormType.Lower:
                        return (str.ToLower());
                    case StrFormType.Half:
                        return (Strings.StrConv(str, VbStrConv.Narrow, Config.Language));
                    case StrFormType.Full:
                        return (Strings.StrConv(str, VbStrConv.Wide, Config.Language));
                }
                return ("");
            }
        }

        private sealed class LineIsEmptyMethod : FunctionMethod
        {
            public LineIsEmptyMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { };
                CanRestructure = false;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return GlobalStatic.Console.EmptyLine ? 1L : 0L;
            }
        }

        private sealed class ReplaceMethod : FunctionMethod
        {
            public ReplaceMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String };
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string baseString = arguments[0].GetStrValue(exm);
                Regex reg;
                try
                {
                    reg = new Regex(arguments[1].GetStrValue(exm));
                }
                catch (ArgumentException e)
                {
                    throw new CodeEE("第２引数が正規表現として不正です：" + e.Message);
                }
                return (reg.Replace(baseString, arguments[2].GetStrValue(exm)));
            }
        }

        private sealed class UnicodeMethod : FunctionMethod
        {
            public UnicodeMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.Integer };
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 i = arguments[0].GetIntValue(exm);
                if ((i < 0) || (i > 0xFFFF))
                    throw new CodeEE("UNICODE関数に範囲外の値(" + i.ToString() + ")が渡されました");
                //改行関係以外の制御文字は警告扱いに変更
                //とはいえ、改行以外の制御文字を意図的に渡すのはそもそもコーディングに問題がありすぎるので、エラーでもいい気はする
                if ((i < 0x001F && i != 0x000A && i != 0x000D) || (i >= 0x007F && i <= 0x009F))
                {
                    //コード実行中の場合
                    if(GlobalStatic.Process.getCurrentLine != null)
                        GlobalStatic.Console.PrintSystemLine("注意:" + GlobalStatic.Process.getCurrentLine.Position.Filename + "の" + GlobalStatic.Process.getCurrentLine.Position.LineNo.ToString() + "行目でUNICODE関数に制御文字に対応する値(0x" + String.Format("{0:X}", i) + ")が渡されました");
                    else
                        ParserMediator.Warn("UNICODE関数に制御文字に対応する値(0x" + String.Format("{0:X}", i) + ")が渡されました", GlobalStatic.Process.scaningLine, 1, false, false, null);

                    return "";
                }
                string s = new string(new char[] { (char)i });

                return (s);
            }
        }

        private sealed class UnicodeByteMethod : FunctionMethod
        {
            public UnicodeByteMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string target = arguments[0].GetStrValue(exm);
                int length = Encoding.UTF32.GetEncoder().GetByteCount(target.ToCharArray(), 0, target.Length, false);
                byte[] bytes = new byte[length];
                Encoding.UTF32.GetEncoder().GetBytes(target.ToCharArray(), 0, target.Length, bytes, 0, false);
                Int64 i = (Int64)BitConverter.ToInt32(bytes, 0);

                return (i);
            }
        }

        private sealed class ConvertIntMethod : FunctionMethod
        {
            public ConvertIntMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                Int64 toBase = arguments[1].GetIntValue(exm);
                if ((toBase != 2) && (toBase != 8) && (toBase != 10) && (toBase != 16))
                    throw new CodeEE("CONVERT関数の第２引数は2, 8, 10, 16のいずれかでなければなりません");
                return Convert.ToString(arguments[0].GetIntValue(exm), (int)toBase);
            }
        }

        private sealed class IsNumericMethod : FunctionMethod
        {
            public IsNumericMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }
            public override long GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string baseStr = arguments[0].GetStrValue(exm);

                //全角文字があるなら数値ではない
                if (baseStr.Length < LangManager.GetStrlenLang(baseStr))
                    return (0);
                StringStream st = new StringStream(baseStr);
                if (!char.IsDigit(st.Current) && st.Current != '+' && st.Current != '-')
                    return (0);
                else if ((st.Current == '+' || st.Current == '-') && !char.IsDigit(st.Next))
                    return (0);
                if (!LexicalAnalyzer.NumericCheck(st))
                    return (0);
                if (!st.EOS)
                {
                    if (st.Current == '.')
                    {
                        st.ShiftNext();
                        while (!st.EOS)
                        {
                            if (!char.IsDigit(st.Current))
                                return (0);
                            st.ShiftNext();
                        }
                    }
                    else
                        return (0);
                }
                return 1;
            }
        }

        private sealed class EscapeMethod : FunctionMethod
        {
            public EscapeMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                return Regex.Escape(arguments[0].GetStrValue(exm));
            }
        }

        private sealed class EncodeToUniMethod : FunctionMethod
        {
            public EncodeToUniMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.Void };
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                //通常2つ、1つ省略可能で1～2の引数が必要。
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 2)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (arguments[0].GetEraType() != EraType.String)
                    return name + "関数の1番目の引数の型が正しくありません";
                if ((arguments.Length >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.Integer))
                    return name + "関数の2番目の引数の型が正しくありません";
                return null;
            }
            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string baseStr = arguments[0].GetStrValue(exm);
                if (baseStr.Length == 0)
                    return -1;
                Int64 position = (arguments.Length > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
                if (position < 0)
                    throw new CodeEE("ENCOIDETOUNI関数の第２引数(" + position.ToString() + ")が負の値です");
                if (position >= baseStr.Length)
                    throw new CodeEE("ENCOIDETOUNI関数の第２引数(" + position.ToString() + ")が第１引数の文字列(" + baseStr + ")の文字数を超えています");
                return char.ConvertToUtf32(baseStr, (int)position);
            }
        }

        public sealed class CharAtMethod : FunctionMethod
        {
            public CharAtMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String, EraType.Integer };
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                Int64 pos = arguments[1].GetIntValue(exm);
                if (pos < 0 || pos >= str.Length)
                    return "";
                return str[(int)pos].ToString();
            }
        }

        public sealed class GetLineStrMethod : FunctionMethod
        {
            public GetLineStrMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = true;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
                string str = arguments[0].GetStrValue(exm);
				if (string.IsNullOrEmpty(str))
					throw new CodeEE("GETLINESTR関数の引数が空文字列です");
                return exm.Console.getStBar(str);
            }
        }

		public sealed class StrFormMethod : FunctionMethod
		{
			public StrFormMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = new EraType[] { EraType.String };
                HasUniqueRestructure = true;
				CanRestructure = true;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string str = arguments[0].GetStrValue(exm);
                string destStr;
                try
				{
					StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new StringStream(str), FormStrEndWith.EoL, false);
					StrForm strForm = StrForm.FromWordToken(wt);
					destStr = strForm.GetString(exm);
				}
				catch(CodeEE e)
				{
					throw new CodeEE("STRFORM関数:文字列\"" + str + "\"の展開エラー:" + e.Message);
				}
				catch
				{
					throw new CodeEE("STRFORM関数:文字列\"" + str+ "\"の展開処理中にエラーが発生しました");
				}
				return destStr;
			}
            public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                arguments[0].Restructure(exm);
                //引数が文字列式等ならお手上げなので諦める
                if (!(arguments[0] is SingleTerm) && !(arguments[0] is VariableTerm))
                    return false;
                //引数が確定値でない文字列変数なら無条件で不可（結果が可変なため）
                if ((arguments[0] is VariableTerm) && !(((VariableTerm)arguments[0]).Identifier.IsConst))
                    return false;
                string str = arguments[0].GetStrValue(exm);
                try
                {
                    StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new StringStream(str), FormStrEndWith.EoL, false);
                    StrForm strForm = StrForm.FromWordToken(wt);
                    if (!strForm.IsConst)
                        return false;
                }
                catch
                {
                    //パースできないのはエラーがあるかここではわからないからとりあえず考えない
                    return false;
                }
                return true;
            }
        }
        public sealed class StrFormCheckMethod : FunctionMethod
        {
            public StrFormCheckMethod()
            {
                ReturnType = EraType.Integer;
                argumentTypeArray = new EraType[] { EraType.String };
                CanRestructure = false;
            }

            public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                string str = arguments[0].GetStrValue(exm);
                try
                {
                    StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new StringStream(str), FormStrEndWith.EoL, false);
                    StrForm strForm = StrForm.FromWordToken(wt);
                    strForm.GetString(exm);
                    return 1;
                }
                catch
                {
                    return 0;
                }
            }
        }

        public sealed class JoinMethod : FunctionMethod
        {
            public JoinMethod()
            {
                ReturnType = EraType.String;
                argumentTypeArray = null;
                HasUniqueRestructure = true;
                CanRestructure = true;
            }
            public override string CheckArgumentType(string name, IOperandTerm[] arguments)
            {
                if (arguments.Length < 1)
                    return name + "関数には少なくとも1つの引数が必要です";
                if (arguments.Length > 4)
                    return name + "関数の引数が多すぎます";
                if (arguments[0] == null)
                    return name + "関数の1番目の引数は省略できません";
                if (!(arguments[0] is VariableTerm))
                    return name + "関数の1番目の引数が変数ではありません";
                VariableTerm varToken = (VariableTerm)arguments[0];
                if (!varToken.Identifier.IsArray1D && !varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D)
                    return name + "関数の1番目の引数が配列変数ではありません";
                if (arguments.Length == 1)
                    return null;
                if ((arguments[1] != null) && (arguments[1].GetEraType() != EraType.String))
                    return name + "関数の2番目の変数が文字列ではありません";
                if (arguments.Length == 2)
                    return null;
                if ((arguments[2] != null) && (arguments[2].GetEraType() != EraType.Integer))
                    return name + "関数の3番目の変数が数値ではありません";
                if (arguments.Length == 3)
                    return null;
                if ((arguments[3] != null) && (arguments[3].GetEraType() != EraType.Integer))
                    return name + "関数の4番目の変数が数値ではありません";
                return null;
            }
            public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
            {
                VariableTerm varTerm = (VariableTerm)arguments[0];
                string delimiter = (arguments.Length >= 2 && arguments[1] != null) ? arguments[1].GetStrValue(exm) : ",";
                Int64 index1 = (arguments.Length >= 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
                Int64 index2 = (arguments.Length == 4 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : varTerm.GetLastLength() - index1;

                FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);

                if (index2 < 0)
                    throw new CodeEE("STRJOINの第4引数(" + index2.ToString()+ ")が負の値になっています");

                p.IsArrayRangeValid(index1, index1 + index2, "STRJOIN", 2L, 3L);
                return (exm.VEvaluator.GetJoinedStr(p, delimiter, index1, index2));
            }
            public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
            {                
                //第1変数は変数名なので、定数文字列変数だと事故が起こるので独自対応
                VariableTerm varTerm = (VariableTerm)arguments[0];
                bool canRerstructure = varTerm.Identifier.IsConst;
                for (int i = 1; i < arguments.Length; i++)
                {
                    if (arguments[i] == null)
                        continue;
                    arguments[i] = arguments[i].Restructure(exm);
                    canRerstructure &= arguments[i] is SingleTerm;
                }
                return canRerstructure;
            }
        }
		
		public sealed class GetConfigMethod : FunctionMethod
		{
			public GetConfigMethod(bool typeisInt)
			{
				if(typeisInt)
				{
					funcname = "GETCONFIG";
					ReturnType = EraType.Integer;
				}
				else
				{
					funcname = "GETCONFIGS";
					ReturnType = EraType.String;
				}
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = true;
			}
			private readonly string funcname;
			private SingleTerm GetSingleTerm(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string str = arguments[0].GetStrValue(exm);
				if(str == null || str.Length == 0)
					throw new CodeEE(funcname + "関数に空文字列が渡されました");
				string errMes = null;
				SingleTerm term = ConfigData.Instance.GetConfigValueInERB(str, ref errMes);
				if(errMes != null)
					throw new CodeEE(funcname + "関数:" + errMes);
				return term;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if(ReturnType != EraType.Integer)
					throw new ExeEE(funcname + "関数:不正な呼び出し");
				SingleTerm term = GetSingleTerm(exm, arguments);
				if(term.GetEraType() != EraType.Integer)
					throw new CodeEE(funcname + "関数:型が違います（GETCONFIGS関数を使用してください）");
				return term.Int;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if(ReturnType != EraType.String)
					throw new ExeEE(funcname + "関数:不正な呼び出し");
				SingleTerm term = GetSingleTerm(exm, arguments);
				if (term.GetEraType() != EraType.String)
					throw new CodeEE(funcname + "関数:型が違います（GETCONFIG関数を使用してください）");
				return term.Str;
			}
		}
        #endregion

		#region html系

		private sealed class HtmlGetPrintedStrMethod : FunctionMethod
		{
			public HtmlGetPrintedStrMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = null;
				CanRestructure = false;
			}

			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				//通常１つ。省略可能。
				if (arguments.Length > 1)
					return name + "関数の引数が多すぎます";
				if (arguments.Length == 0|| arguments[0] == null)
					return null;
				if (arguments[0].GetEraType() != EraType.Integer)
					return name + "関数の1番目の引数の型が正しくありません";
				return null;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				Int64 lineNo = 0;
				if (arguments.Length > 0)
					lineNo = arguments[0].GetIntValue(exm);
				if (lineNo < 0)
					throw new CodeEE("引数を0未満にできません");
				ConsoleDisplayLine[] dispLines = exm.Console.GetDisplayLines(lineNo);
				if (dispLines == null)
					return "";
				return HtmlManager.DisplayLine2Html(dispLines, true);
			}
		}

		private sealed class HtmlPopPrintingStrMethod : FunctionMethod
		{
			public HtmlPopPrintingStrMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}

			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				ConsoleDisplayLine[] dispLines = exm.Console.PopDisplayingLines();
				if (dispLines == null)
					return "";
				return HtmlManager.DisplayLine2Html(dispLines, false);
			}
		}

		private sealed class HtmlToPlainTextMethod : FunctionMethod
		{
			public HtmlToPlainTextMethod()
			{
				ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return HtmlManager.Html2PlainText(arguments[0].GetStrValue(exm));
			}
		}
		private sealed class HtmlEscapeMethod : FunctionMethod
		{
			public HtmlEscapeMethod()
			{
				ReturnType = EraType.String;
                argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return HtmlManager.Escape(arguments[0].GetStrValue(exm));
			}
		}

		private sealed class HtmlStringLenMethod : FunctionMethod
		{
			public HtmlStringLenMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1 || arguments.Length > 2)
					return name + "関数の引数の数が間違っています";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length == 2 && arguments[1] != null && arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				int len = HtmlManager.HtmlLength(arguments[0].GetStrValue(exm));
				if (arguments.Length >= 2 && arguments[1] != null && arguments[1].GetIntValue(exm) != 0)
					return len;
				if (Config.FontSize <= 0)
					return len;
				if (len >= 0)
					return 2 * len / Config.FontSize + ((2 * len % Config.FontSize != 0) ? 1 : 0);
				return 2 * len / Config.FontSize - ((2 * len % Config.FontSize != 0) ? 1 : 0);
			}
		}

		private sealed class HtmlSubstringMethod : FunctionMethod
		{
			public HtmlSubstringMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = new EraType[] { EraType.String, EraType.Integer };
				CanRestructure = false;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string[] values = HtmlManager.HtmlSubString(arguments[0].GetStrValue(exm), (int)arguments[1].GetIntValue(exm));
				var results = exm.VEvaluator.RESULTS_ARRAY;
				int count = Math.Min(values.Length, results.Length);
				for (int i = 0; i < count; i++)
					results[i] = values[i];
				return values[0];
			}
		}

		private sealed class HtmlStringLinesMethod : FunctionMethod
		{
			public HtmlStringLinesMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string str = arguments[0].GetStrValue(exm);
				int lineLength = (int)arguments[1].GetIntValue(exm);
				if (string.IsNullOrEmpty(str) || lineLength <= 0)
					return 0;
				int lines = 1;
				while (true)
				{
					string[] values = HtmlManager.HtmlSubString(str, lineLength);
					str = values[1];
					if (string.IsNullOrEmpty(str))
						break;
					lines++;
				}
				return lines;
			}
		}
		#endregion

		#region 画像処理系
		/// <summary>
		/// argNo番目の引数をGraphicsImageのIDを示す整数値として読み取り、 GraphicsImage又はnullを返す。
		/// </summary>
		private static GraphicsImage ReadGraphics(string Name, ExpressionMediator exm, IOperandTerm[] arguments, int argNo)
		{
			Int64 target = arguments[argNo].GetIntValue(exm);
			if (target < 0)//funcname + "関数:GraphicsIDに負の値(" + target.ToString() + ")が指定されました"
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGraphicsID0, Name, target));
			else if (target > int.MaxValue)//funcname + "関数:GraphicsIDの値(" + target.ToString() + ")が大きすぎます"
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGraphicsID1, Name, target));
            return AppContents.GetGraphics((int)target);
		}

		/// <summary>
		/// argNo番目の引数を整数値として読み取り、 アルファ値を含むColor構造体にして返す。
		/// </summary>
		private static Color ReadColor(string Name, ExpressionMediator exm, IOperandTerm[] arguments, int argNo)
		{
			Int64 c64 = arguments[argNo].GetIntValue(exm);
			if (c64 < 0 || c64 > 0xFFFFFFFF)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodColorARGB0, Name, c64));
			return Color.FromArgb((int)(c64 >> 24) & 0xFF, (int)(c64 >> 16) & 0xFF, (int)(c64 >> 8) & 0xFF, (int)c64 & 0xFF);
		}

		/// <summary>
		/// argNo番目を含む2つの引数を整数値として読み取り、Point形式にして返す。
		/// </summary>
		private static Point ReadPoint(string Name, ExpressionMediator exm, IOperandTerm[] arguments, int argNo)
		{
			Int64 x64 = arguments[argNo].GetIntValue(exm);
			if(x64<int.MinValue || x64>int.MaxValue)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name,x64, argNo+1));
			Int64 y64 = arguments[argNo+1].GetIntValue(exm);
			if(y64<int.MinValue || y64>int.MaxValue)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name,y64, argNo+1+1));
			return new Point((int)x64, (int)y64);
		}

		/// <summary>
		/// argNo番目を含む4つの引数を整数値として読み取り、Rectangle形式にして返す。
		/// </summary>
		private static Rectangle ReadRectangle(string Name, ExpressionMediator exm, IOperandTerm[] arguments, int argNo)
		{
			Int64 x64 = arguments[argNo].GetIntValue(exm);
			if (x64 < int.MinValue || x64 > int.MaxValue)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, x64, argNo + 1));
			Int64 y64 = arguments[argNo + 1].GetIntValue(exm);
			if (y64 < int.MinValue || y64 > int.MaxValue)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, y64, argNo + 1 + 1));

			Int64 w64 = arguments[argNo + 2].GetIntValue(exm);
			if (w64 < int.MinValue || w64 > int.MaxValue || w64 == 0)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, w64, argNo + 2 + 1));
			Int64 h64 = arguments[argNo + 3].GetIntValue(exm);
			if (h64 < int.MinValue || h64 > int.MaxValue || h64 == 0)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, h64, argNo + 3 + 1));
			return new Rectangle((int)x64, (int)y64, (int)w64, (int)h64);
		}

		/// <summary>
		/// argNo番目の引数を5x5のカラーマトリクス配列変数として読み取り、 5x5のfloat[][]形式にして返す。
		/// </summary>
		private static float[][] ReadColormatrix(string Name, ExpressionMediator exm, IOperandTerm[] arguments, int argNo)
		{
			//数値型二次元以上配列変数のはず
			FixedVariableTerm p = ((VariableTerm)arguments[argNo]).GetFixedVariableTerm(exm);
			Int64 e1, e2;
			float[][] cm = new float[5][];
			if (p.Identifier.IsArray2D)
			{
				Int64[,] array;
				if (p.Identifier.IsCharacterData)
				{
					array = p.Identifier.GetArrayChara((int)p.Index1) as Int64[,];
					e1 = p.Index2;
					e2 = p.Index3;
				}
				else
				{
					array = p.Identifier.GetArray() as Int64[,];
					e1 = p.Index1;
					e2 = p.Index2;
				}
				if (e1 < 0 || e2 < 0 || e1 + 5 > array.GetLength(0) || e2 + 5 > array.GetLength(1))
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, e1, e2));
				for (int x = 0; x < 5; x++)
				{
					cm[x] = new float[5];
					for (int y = 0; y < 5; y++)
					{
						cm[x][y] = ((float)array[e1+x, e2+y]) / 256f;
					}
				}
			}
			if(p.Identifier.IsArray3D)
			{
				Int64[, ,] array; Int64 e3;
				if (p.Identifier.IsCharacterData)
				{
					throw new NotImplCodeEE();
				}
				else
				{
					array = p.Identifier.GetArray() as Int64[,,];
					e1 = p.Index1;
					e2 = p.Index2;
					e3 = p.Index3;
				}
				if (e1 < 0 || e1 >= array.GetLength(0))
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, e2, e3));
				if (e2 < 0 || e3 < 0 || e2 + 5 > array.GetLength(1) || e3 + 5 > array.GetLength(2))
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, e2, e3));
				for (int x = 0; x < 5; x++)
				{
					cm[x] = new float[5];
					for (int y = 0; y < 5; y++)
					{
						cm[x][y] = ((float)array[e1,e2+x, e3+y]) / 256f;
					}
				}
			}
			return cm;
		}

		/// <summary>
		/// snake 兼容：CBGSETSPRITE/CBGSETCIMG 的 ColorMatrix 支持整数/小数 2D/3D 数组，
		/// 并且参数本身允许省略，因此需要单独做空值和类型检查。
		/// </summary>
		private static float[][] ReadOptionalColorMatrixForCbg(string Name, ExpressionMediator exm, IOperandTerm[] arguments, int argNo)
		{
			if (arguments.Length <= argNo || arguments[argNo] == null)
				return null;
			if (!(arguments[argNo] is VariableTerm term))
				throw new CodeEE(string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, Name));
			FixedVariableTerm fixedTerm = term.GetFixedVariableTerm(exm);
			if (!fixedTerm.Identifier.IsInteger && !fixedTerm.Identifier.IsFloat)
				throw new CodeEE(string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, Name));
			if (!fixedTerm.Identifier.IsArray2D && !fixedTerm.Identifier.IsArray3D)
				throw new CodeEE(string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, Name));

			float[][] matrix = new float[5][];
			for (int i = 0; i < matrix.Length; i++)
				matrix[i] = new float[5];

			if (fixedTerm.Identifier.IsArray2D)
			{
				Int64 row = fixedTerm.Identifier.IsCharacterData ? fixedTerm.Index2 : fixedTerm.Index1;
				Int64 col = fixedTerm.Identifier.IsCharacterData ? fixedTerm.Index3 : fixedTerm.Index2;
				if (row < 0 || col < 0)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, row, col));
				if (fixedTerm.Identifier.IsFloat)
				{
					double[,] array = fixedTerm.Identifier.IsCharacterData
						? fixedTerm.Identifier.GetArrayChara((int)fixedTerm.Index1) as double[,]
						: fixedTerm.Identifier.GetArray() as double[,];
					if (array == null || row + 5 > array.GetLength(0) || col + 5 > array.GetLength(1))
						throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, row, col));
					for (int x = 0; x < 5; x++)
						for (int y = 0; y < 5; y++)
							matrix[x][y] = (float)array[row + x, col + y];
					return matrix;
				}

				Int64[,] intArray = fixedTerm.Identifier.IsCharacterData
					? fixedTerm.Identifier.GetArrayChara((int)fixedTerm.Index1) as Int64[,]
					: fixedTerm.Identifier.GetArray() as Int64[,];
				if (intArray == null || row + 5 > intArray.GetLength(0) || col + 5 > intArray.GetLength(1))
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, row, col));
				for (int x = 0; x < 5; x++)
					for (int y = 0; y < 5; y++)
						matrix[x][y] = ((float)intArray[row + x, col + y]) / 256f;
				return matrix;
			}

			if (fixedTerm.Identifier.IsCharacterData)
				throw new NotImplCodeEE();

			Int64 layer = fixedTerm.Index1;
			Int64 row3 = fixedTerm.Index2;
			Int64 col3 = fixedTerm.Index3;
			if (layer < 0 || row3 < 0 || col3 < 0)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, row3, col3));
			if (fixedTerm.Identifier.IsFloat)
			{
				double[,,] array = fixedTerm.Identifier.GetArray() as double[,,];
				if (array == null || layer >= array.GetLength(0) || row3 + 5 > array.GetLength(1) || col3 + 5 > array.GetLength(2))
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, row3, col3));
				for (int x = 0; x < 5; x++)
					for (int y = 0; y < 5; y++)
						matrix[x][y] = (float)array[layer, row3 + x, col3 + y];
				return matrix;
			}

			Int64[,,] intArray3 = fixedTerm.Identifier.GetArray() as Int64[,,];
			if (intArray3 == null || layer >= intArray3.GetLength(0) || row3 + 5 > intArray3.GetLength(1) || col3 + 5 > intArray3.GetLength(2))
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, row3, col3));
			for (int x = 0; x < 5; x++)
				for (int y = 0; y < 5; y++)
					matrix[x][y] = ((float)intArray3[layer, row3 + x, col3 + y]) / 256f;
			return matrix;
		}

		public sealed class GraphicsStateMethod : FunctionMethod
		{
			public GraphicsStateMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				switch (Name)
				{
					case "GCREATED":
						return 1;
					case "GWIDTH":
						return g.Width;
					case "GHEIGHT":
						return g.Height;
					case "GGETFONTSIZE":
						return g.Fontsize;
					case "GGETFONTSTYLE":
						return g.Fontstyle;
					case "GGETPEN":
						return ((Int64)g.PenColor.ToArgb()) & 0xFFFFFFFFL;
					case "GGETPENWIDTH":
						return g.PenWidth;
					case "GGETBRUSH":
						return ((Int64)g.BrushColor.ToArgb()) & 0xFFFFFFFFL;
				}
				throw new ExeEE("GraphicsState:" + Name + ":異常な分岐");
			}
		}

		public sealed class GraphicsGetColorMethod : FunctionMethod
		{
			public GraphicsGetColorMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				//失敗したら負の値を返す。他と戻り値違うけど仕方ないね
				if (!g.IsCreated)
					return -1;
				Point p = ReadPoint(Name, exm, arguments, 1);
				if (p.X < 0 || p.X >= g.Width || p.X < 0 || p.Y >= g.Height)
					return -1;
				Color c = g.GGetColor(p.X,p.Y);
				//Color.ToArgb()はInt32の負の値をとることがあり、Int64にうまく変換できない?（と思ったが気のせいだった
				return ((Int64)c.ToArgb()) & 0xFFFFFFFFL;
			}
		}

		public sealed class GraphicsSetColorMethod : FunctionMethod
		{
			public GraphicsSetColorMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Color c = ReadColor(Name, exm, arguments, 1);
				Point p = ReadPoint(Name, exm, arguments, 2);
				if (p.X < 0 || p.X >= g.Width || p.X < 0 || p.Y >= g.Height)
					return 0;
				g.GSetColor(c, p.X, p.Y);
				return 1;
			}
		}

		/// <summary>
		/// GCLEARLOWALPHA(int ID, int alphaThreshold)
		/// アルファ値がalphaThreshold以下の画素を一括で透明黒(0x0)にする。
		/// ERBの画像_僅少アルファ除去が行っていたFOR二重ループ+GGETCOLOR/GSETCOLOR
		/// 逐画素呼び出しを、GraphicsImage.ClearLowAlphaのバイト配列一括処理に置き換えるための原語。
		/// </summary>
		public sealed class GraphicsClearLowAlphaMethod : FunctionMethod
		{
			public GraphicsClearLowAlphaMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Int64 threshold64 = arguments[1].GetIntValue(exm);
				if (threshold64 < 0 || threshold64 > 255)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, threshold64, 2));
				g.ClearLowAlpha((int)threshold64);
				return 1;
			}
		}

		public sealed class GraphicsSetBrushMethod : FunctionMethod
		{
			public GraphicsSetBrushMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Color c = ReadColor(Name, exm, arguments, 1);
				g.GSetBrush(new SolidBrush(c));
				return 1;
			}
		}
		public sealed class GraphicsSetFontMethod : FunctionMethod
		{
			public GraphicsSetFontMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 3)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 3);
				if (arguments.Length > 4)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				if (arguments[0] == null || !arguments[0].IsInteger)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1);
				if (arguments[1] == null || !arguments[1].IsString)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 2);
				if (arguments[2] == null || !arguments[2].IsInteger)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 3);
				if (arguments.Length == 4 && (arguments[3] == null || !arguments[3].IsInteger))
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 4);
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				string fontname = arguments[1].GetStrValue(exm);
				Int64 fontsize = arguments[2].GetIntValue(exm);
				FontStyle fontStyle = arguments.Length >= 4 ? GraphicsFontStyleFromInt(arguments[3].GetIntValue(exm)) : FontStyle.Regular;

                Font styledFont;
                try
				{
					styledFont = new Font(fontname, fontsize, fontStyle, GraphicsUnit.Pixel);
				}
				catch
				{
					return 0;
				}
				g.GSetFont(styledFont);
				return 1;
			}
		}

		static FontStyle GraphicsFontStyleFromInt(long style)
		{
			FontStyle fontStyle = FontStyle.Regular;
			if ((style & 1) != 0)
				fontStyle |= FontStyle.Bold;
			if ((style & 2) != 0)
				fontStyle |= FontStyle.Italic;
			if ((style & 4) != 0)
				fontStyle |= FontStyle.Strikeout;
			if ((style & 8) != 0)
				fontStyle |= FontStyle.Underline;
			return fontStyle;
		}
		
		public sealed class GraphicsSetPenMethod : FunctionMethod
		{
			public GraphicsSetPenMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Color c = ReadColor(Name, exm, arguments, 1);
				Int64 width = arguments[2].GetIntValue(exm);
				g.GSetPen(new Pen(c,width));
				return 1;
			}
		}

		public sealed class SpriteStateMethod : FunctionMethod
		{
			public SpriteStateMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string imgname = arguments[0].GetStrValue(exm);
				if (Name == "SPRITECREATED")
					return AppContents.SpriteExists(imgname) ? 1 : 0;

				ASprite img = AppContents.GetSprite(imgname);
				if (img == null || !img.IsCreated)
					return 0;
				if (img.DestBaseSize.Width <= 0 || img.DestBaseSize.Height <= 0)
				{
					// 防御性：某些动态创建路径（如 SPRITECREATE 后 GraphicsImage 被异常释放，
					// 或 CSV 索引与文件系统状态不一致）可能导致 DestBaseSize 为0，但底层 Bitmap 仍有有效尺寸。
					// 此时回退到 Bitmap 尺寸，避免 SPRITEWIDTH 返回0 引发后续脚本 GCREATE 报错。
					var bmp = img.Bitmap;
					if (bmp != null && bmp.Width > 0 && bmp.Height > 0)
					{
						switch (Name)
						{
							case "SPRITEWIDTH":
								return bmp.Width;
							case "SPRITEHEIGHT":
								return bmp.Height;
						}
					}
					return 0;
				}
				switch (Name)
				{
					case "SPRITEWIDTH":
						return img.DestBaseSize.Width;
					case "SPRITEHEIGHT":
						return img.DestBaseSize.Height;
					case "SPRITEPOSX":
						return img.DestBasePosition.X;
					case "SPRITEPOSY":
						return img.DestBasePosition.Y;
				}
				throw new ExeEE("SpriteStateMethod:" + Name + ":異常な分岐");
			}
		}

		public sealed class SpriteSetPosMethod : FunctionMethod
		{
			public SpriteSetPosMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String , EraType.Integer,EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string imgname = arguments[0].GetStrValue(exm);
				ASprite img = AppContents.GetSprite(imgname);
				if (img == null || !img.IsCreated)
					return 0;
				Point p = ReadPoint(Name, exm, arguments, 1);
				switch (Name)
				{
					case "SPRITEMOVE":
						img.DestBasePosition.Offset(p);
						AppContents.SetSpriteBasePosition(imgname, img.DestBasePosition);
						return 1;
					case "SPRITESETPOS":
						img.DestBasePosition = p;
						AppContents.SetSpriteBasePosition(imgname, img.DestBasePosition);
						return 1;
				}
				throw new ExeEE("SpriteStateMethod:" + Name + ":異常な分岐");
			}
		}

		public sealed class SpriteGetColorMethod : FunctionMethod
		{
			public SpriteGetColorMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string imgname = arguments[0].GetStrValue(exm);
				ASprite img = AppContents.GetSprite(imgname);
				//他と違って失敗は0ではなく負の値
				if (img == null || !img.IsCreated)
					return -1;
				Point p = ReadPoint(Name, exm, arguments, 1);
				if (p.X < 0 || p.X >= img.DestBaseSize.Width)
					return -1;
				if (p.Y < 0 || p.Y >= img.DestBaseSize.Height)
					return -1;
				Color c = img.SpriteGetColor(p.X, p.Y);
				//Color.ToArgb()はInt32の負の値をとることがあり、Int64にうまく変換できない？（と思ったが気のせいだった
				return ((Int64)c.A) << 24 + c.R << 16 + c.G << 8 + c.B;
			}
		}

		public sealed class ClientSizeMethod : FunctionMethod
		{
			public ClientSizeMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] {};
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				switch (Name)
				{
					case "CLIENTWIDTH":
						return exm.Console.ClientWidth;
					case "CLIENTHEIGHT":
						return exm.Console.ClientHeight;
				}
				throw new ExeEE("ClientSize:" + Name + ":異常な分岐");
			}
		}

		public sealed class GraphicsCreateMethod : FunctionMethod
		{
			public GraphicsCreateMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (g.IsCreated)
				{
					return 0;
				}

				Point p = ReadPoint(Name, exm, arguments, 1);
				int width = p.X; int height = p.Y;
				if (width <= 0)//{0}関数:GraphicsのWidthに0以下の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth0, Name, width));
				else if (width > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのWidthに{2}以上の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth1, Name, width, AbstractImage.MAX_IMAGESIZE));
				if (height <= 0)//{0}関数:GraphicsのHeightに0以下の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight0, Name, height));
				else if (height > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのHeightに{2}以上の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight1, Name, height, AbstractImage.MAX_IMAGESIZE));

				g.GCreate(width, height, false);
				return 1;

			}
		}

		public sealed class GraphicsCreateFromFileMethod : FunctionMethod
		{
			public GraphicsCreateFromFileMethod()
			{
				ReturnType = EraType.Integer;
				//对照 v24/snake Creator.Method.cs：GCREATEFROMFILE(ID, filename[, isRelative])，
				//OmitStart=2 表示第 3 参（isRelative）可省略。
				argumentTypeArrayEx = new ArgTypeList[]
				{
					new ArgTypeList { ArgTypes = new List<_ArgType> { ArgType.Int, ArgType.String, ArgType.Int }, OmitStart = 2 },
				};
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (g.IsCreated)
					return 0;

				string filename = arguments[1].GetStrValue(exm);
				//v24/snake 语义：第 3 参 isRelative != 0 时按相对路径直接解析，
				//否则按 ContentDir 基准解析。gEmuera 用 ResolveGraphicsResourceFilePath
				//统一解析；isRelative 分支仅在路径为相对路径时影响基准目录。
				bool isRelative = arguments.Length > 2 && arguments[2].GetIntValue(exm) != 0;
                BitmapTexture bmp = null;
				try
				{
					string filepath = isRelative
						? uEmuera.Utils.ResolveExistingFilePath(filename)
						: ResolveGraphicsResourceFilePath(filename);
					if (!uEmuera.Utils.FileExists(filepath))
						return 0;
					bmp = new BitmapTexture(filepath);
					if (bmp.Width > AbstractImage.MAX_IMAGESIZE || bmp.Height > AbstractImage.MAX_IMAGESIZE)
						return 0;
					if (bmp.Width == 0 || bmp.Height == 0)
						return 0;
					if (!g.GCreateFromF(bmp, (Config.TextDrawingMode == TextDrawingMode.WINAPI)))
						return 0;
				}
				catch (Exception e)
				{
					if (e is CodeEE)
						throw;
				}
				finally
				{
					if (bmp != null)
						bmp.Dispose();
				}
				//画像ファイルではなかった、などによる失敗
				if (!g.IsCreated)
					return 0;
				return 1;
			}
		}

		static string ResolveGraphicsResourceFilePath(string filename)
		{
			if (string.IsNullOrWhiteSpace(filename))
				return filename;
			string normalized = uEmuera.Utils.NormalizePath(filename.Trim());
			if (System.IO.Path.IsPathRooted(normalized) || normalized.Contains("://"))
				return uEmuera.Utils.ResolveExistingFilePath(normalized);

			foreach (string candidate in BuildGraphicsResourcePathCandidates(normalized))
			{
				if (string.IsNullOrEmpty(candidate))
					continue;
				string resolved = uEmuera.Utils.ResolveExistingFilePath(candidate);
				if (uEmuera.Utils.FileExists(resolved))
					return resolved;
			}
			return uEmuera.Utils.ResolveExistingFilePath(System.IO.Path.Combine(Program.ExeDir ?? "", normalized));
		}

		static IEnumerable<string> BuildGraphicsResourcePathCandidates(string normalized)
		{
			string gameDir = Program.ExeDir ?? "";
			string contentDir = Program.ContentDir ?? "";
			yield return System.IO.Path.Combine(gameDir, normalized);

			const string resourcesPrefix = "resources/";
			if (normalized.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
				yield return System.IO.Path.Combine(contentDir, normalized.Substring(resourcesPrefix.Length));
			else
				yield return System.IO.Path.Combine(contentDir, normalized);
		}

		public sealed class GraphicsDisposeMethod : FunctionMethod
		{
			public GraphicsDisposeMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				g.GDispose();
				return 1;
			}
		}
		/// <summary>
		/// SPRITECREATE(str imgName, int gID, int x, int y, int width, int height)
		/// SPRITECREATE(str imgName, int gID)
		/// </summary>
		public sealed class SpriteCreateMethod : FunctionMethod
		{
			public SpriteCreateMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{

				if (arguments.Length < 2)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
				if (arguments.Length > 6)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				if (arguments[0] == null)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, 0 + 1);
				if (arguments[1] == null)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, 1 + 1);
				if (arguments[0].GetEraType() != EraType.String)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 0 + 1);
				if (arguments[1].GetEraType() != EraType.Integer)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1 + 1);
				if (arguments.Length == 2)
					return null;
				if (arguments.Length != 6)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
				for (int i = 2; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
					if (arguments[i].GetEraType() != EraType.Integer)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				string imgname = arguments[0].GetStrValue(exm);
				if (string.IsNullOrEmpty(imgname))
					return 0;
				ASprite img = AppContents.GetSprite(imgname);
				if (img != null && img.IsCreated)
					return 0;
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 1);
				if (!g.IsCreated)
				{
					return 0;
				}

				Rectangle rect = new Rectangle(0, 0, g.Width, g.Height);
				if(arguments.Length == 6)
				{//四角形は正でも負でもよいが親画像の外を指してはいけない
					rect = ReadRectangle(Name, exm, arguments, 2);
					if (TryClipPositiveRectangleToGraphics(g, ref rect))
					{
						if (rect.Width <= 0 || rect.Height <= 0)
							return 0;
					}
					else
					if (rect.X + rect.Width < 0 || rect.X + rect.Width > g.Width || rect.Y + rect.Height < 0 || rect.Y + rect.Height > g.Height)
						throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodCIMGCreateOutOfRange0, Name));
				}
				AppContents.CreateSpriteG(imgname, g, rect);
				return 1;
			}
		}

		private static bool TryClipPositiveRectangleToGraphics(GraphicsImage g, ref Rectangle rect)
		{
			if (rect.Width <= 0 || rect.Height <= 0)
				return false;
			int left = Math.Max(0, rect.X);
			int top = Math.Max(0, rect.Y);
			int right = Math.Min(g.Width, rect.X + rect.Width);
			int bottom = Math.Min(g.Height, rect.Y + rect.Height);
			if (right <= left || bottom <= top)
			{
				rect = new Rectangle(0, 0, 0, 0);
				return true;
			}
			if (left == rect.X && top == rect.Y && right == rect.X + rect.Width && bottom == rect.Y + rect.Height)
				return false;
			rect = new Rectangle(left, top, right - left, bottom - top);
			return true;
		}

		public sealed class SpriteDisposeMethod : FunctionMethod
		{
			public SpriteDisposeMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string imgname = arguments[0].GetStrValue(exm);
				ASprite img = AppContents.GetSprite(imgname);
				if (img == null || !img.IsCreated)
					return 0;
				AppContents.SpriteDispose(imgname);
				return 1;
			}
		}


		/// <summary>
		/// GCLEAR(int ID, int cARGB)
		/// GCLEAR(int ID, int cARGB, int x, int y, int w, int h)  ← EM_私家版_GCLEAR拡張
		/// </summary>
		public sealed class GraphicsClearMethod : FunctionMethod
		{
			public GraphicsClearMethod()
			{
				ReturnType = EraType.Integer;
				//对照 v24/snake Creator.Method.cs 的 EM_私家版_GCLEAR拡張：
				//2 参全画面清除 + 6 参矩形区域清除两组签名，基类多签名机制校验。
				argumentTypeArrayEx = new ArgTypeList[]
				{
					new ArgTypeList { ArgTypes = new List<_ArgType> { ArgType.Int, ArgType.Int } },
					new ArgTypeList { ArgTypes = new List<_ArgType> { ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int } },
				};
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				Color c = ReadColor(Name, exm, arguments, 1);
				if (!g.IsCreated)
					return 0;
				if (arguments.Length == 2)
					g.GClear(c);
				else
					g.GClear(c, (int)arguments[2].GetIntValue(exm), (int)arguments[3].GetIntValue(exm),
						(int)arguments[4].GetIntValue(exm), (int)arguments[5].GetIntValue(exm));
				return 1;
			}
		}

		/// <summary>
		/// GFILLRECTANGLE(int ID, int cARGB, int x, int y, int width, int height)
		/// </summary>
		public sealed class GraphicsFillRectangleMethod : FunctionMethod
		{
			public GraphicsFillRectangleMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Rectangle rect = ReadRectangle(Name, exm, arguments, 1);
				g.GFillRectangle(rect);
				return 1;
			}
		}

		/// <summary>
		/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight)
		/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight, var CM)
		/// </summary>
		public sealed class GraphicsDrawGMethod : FunctionMethod
		{
			public GraphicsDrawGMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
				HasUniqueRestructure = true;
			}
			
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 10)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 10);
				if (arguments.Length > 11)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				for (int i = 0; i < 10; i++)
				{
					if (arguments[i] == null)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
					if (EraType.Integer != arguments[i].GetEraType())
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
				}
				if (arguments.Length == 10)
					return null;
                if (!(arguments[10] is VariableTerm varToken) || !varToken.IsInteger || (!varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D))
                    return string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, name);
                return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
				if (!dest.IsCreated)
					return 0;
				GraphicsImage src = ReadGraphics(Name, exm, arguments, 1);
				if (!src.IsCreated)
					return 0;
				Rectangle destRect = ReadRectangle(Name, exm, arguments, 2);
				Rectangle srcRect = ReadRectangle(Name, exm, arguments, 6);
				if (arguments.Length == 10 || arguments[10] == null)
				{
					dest.GDrawG(src, destRect, srcRect);
					return 1;
				}
				float[][] cm = ReadColormatrix(Name, exm, arguments, 10);
				dest.GDrawG(src, destRect, srcRect, cm);
				return 1;
			}

			public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						continue;
					//11番目の引数はColorMatrixの配列を指しているので定数にしてはいけない
					if (i == 10)
						arguments[i].Restructure(exm);
					else
						arguments[i] = arguments[i].Restructure(exm);
				}
				return false;
			}
		}
		
		/// <summary>
		/// GDRAWGWITHMASK(int ID, int srcID, int maskID, int destX, int destY)
		/// </summary>
		public sealed class GraphicsDrawGWithMaskMethod : FunctionMethod
		{
			public GraphicsDrawGWithMaskMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
				if (!dest.IsCreated)
					return 0;
				GraphicsImage src = ReadGraphics(Name, exm, arguments, 1);
				if (!src.IsCreated)
					return 0;
				GraphicsImage mask = ReadGraphics(Name, exm, arguments, 2);
				if (!mask.IsCreated)
					return 0;
				if (src.Width != mask.Width || src.Height != mask.Height)
					return 0;
				Point destPoint = ReadPoint(Name, exm, arguments, 3);
				if (destPoint.X + src.Width > dest.Width || destPoint.Y + src.Height > dest.Height)
					return 0;
				dest.GDrawGWithMask(src, mask, destPoint);
				return 1;
			}


		}

		/// <summary>
		/// GDRAWCIMG(int ID, str imgName)
		/// GDRAWCIMG(int ID, str imgName, int destX, int destY)
		/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight)
		/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight, var CM)
		/// </summary>
		public sealed class GraphicsDrawSpriteMethod : FunctionMethod
		{
			public GraphicsDrawSpriteMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
				HasUniqueRestructure = true;
			}

			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
				if (arguments.Length > 7)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				if (arguments.Length != 2 && arguments.Length != 4 && arguments.Length != 6 && arguments.Length != 7)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);

				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
					
					if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetEraType())
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
				}
				if (arguments.Length <= 6)
					return null;
                if (!(arguments[6] is VariableTerm varToken) || !varToken.IsInteger || (!varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D))
                    return string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, name);
                return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
				if (!dest.IsCreated)
					return 0;

				string imgname = arguments[1].GetStrValue(exm);
				ASprite img = AppContents.GetSprite(imgname);
				if (img == null || !img.IsCreated)
				{
					global::GenericUtils.Warn(global::EmueraLogCategory.Sprite, () => $"[GDRAWSPRITE] Sprite not found or not created: '{imgname}'");
					return 0;
				}

				Rectangle destRect = new Rectangle(0, 0, img.DestBaseSize.Width, img.DestBaseSize.Height);
				if (arguments.Length == 2)
				{
					return dest.GDrawCImg(img, destRect) ? 1 : 0;
				}
				if (arguments.Length == 4)
				{
					Point p = ReadPoint(Name, exm, arguments, 2);
					destRect.X = p.X;
					destRect.Y = p.Y;
					return dest.GDrawCImg(img, destRect) ? 1 : 0;
				}
				if (arguments.Length == 6)
				{
					destRect = ReadRectangle(Name, exm, arguments, 2);
					return dest.GDrawCImg(img, destRect) ? 1 : 0;
				}
				//if (arguments.Length == 7)
				destRect = ReadRectangle(Name, exm, arguments, 2);
				float[][] cm = ReadColormatrix(Name, exm, arguments, 6);
				return dest.GDrawCImg(img, destRect, cm) ? 1 : 0;
			}

			public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						continue;
					//7番目の引数はColorMatrixの配列を指しているので定数にしてはいけない
					if (i == 6)
						arguments[i].Restructure(exm);
					else
						arguments[i] = arguments[i].Restructure(exm);
				}
				return false;
			}
		}

		/// <summary>
		/// int SPRITEANIMECREATE (string name, int width, int height)
		/// </summary>
		public sealed class SpriteAnimeCreateMethod : FunctionMethod
		{
			public SpriteAnimeCreateMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				string imgname = arguments[0].GetStrValue(exm);
				if (string.IsNullOrEmpty(imgname))
					return 0;
				//リソースチェック・既に存在しているならば失敗
				ASprite img = AppContents.GetSprite(imgname);
				if (img != null && img.IsCreated)
					return 0;
				Point pos = ReadPoint(Name, exm, arguments, 1);
				if (pos.X <= 0)//{0}関数:GraphicsのWidthに0以下の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth0, Name, pos.X));
				else if (pos.X > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのWidthに{2}以上の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth1, Name, pos.X, AbstractImage.MAX_IMAGESIZE));
				if (pos.Y <= 0)//{0}関数:GraphicsのHeightに0以下の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight0, Name, pos.Y));
				else if (pos.Y > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのHeightに{2}以上の値({1})が指定されました
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight1, Name, pos.Y, AbstractImage.MAX_IMAGESIZE));
				AppContents.CreateSpriteAnime(imgname, pos.X, pos.Y);
				return 1;
			}
		}


		/// <summary>
		/// SPRITEANIMEADDFRAME (string name, int graphID, int x, int y, int width, int height, int offsetx, int offsety, int delay)
		/// </summary>
		public sealed class SpriteAnimeAddFrameMethod : FunctionMethod
		{
			public SpriteAnimeAddFrameMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				string imgname = arguments[0].GetStrValue(exm);
				if (string.IsNullOrEmpty(imgname))
					return 0;
				SpriteAnime img = AppContents.GetSprite(imgname) as SpriteAnime;
				if (img == null && !img.IsCreated)
					return 0;
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 1);
				if (!g.IsCreated)
					return 0;
				Rectangle rect = ReadRectangle(Name, exm, arguments, 2);
				//四角形は正でなければならず、かつ親画像の外を指してはいけない
				if (rect.Width <= 0 || rect.Height <= 0 ||
					rect.X < 0 || rect.X + rect.Width > g.Width || rect.Y < 0 || rect.Y + rect.Height > g.Height)
					return 0;
					//throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodCIMGCreateOutOfRange0, Name));
				Point offset = ReadPoint(Name, exm, arguments, 6);
				Int64 delay = arguments[8].GetIntValue(exm);
				if (delay <= 0 || delay > int.MaxValue)
					return 0;
				img.AddFrame(g, rect, offset, (int)delay);
				return 1;
			}
		}


		/// <summary>
		/// CBGCLEAR
		/// </summary>
		public sealed class CBGClearMethod : FunctionMethod
		{
			public CBGClearMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] {};
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				//if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				exm.Console.CBG_Clear();
				return 1;
			}
		}

		/// <summary>
		/// CBGREMOVERANGE(int zmin, int zmax)
		/// </summary>
		public sealed class CBGRemoveRangeMethod : FunctionMethod
		{
			public CBGRemoveRangeMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{

				Int64 x64 = arguments[0].GetIntValue(exm);
				Int64 y64 = arguments[1].GetIntValue(exm);
				unchecked
				{
					exm.Console.CBG_ClearRange((int)x64, (int)y64);
				}
				return 1;
			}
		}
		/// <summary>
		/// CBGCLEARBUTTON
		/// </summary>
		public sealed class CBGClearButtonMethod : FunctionMethod
		{
			public CBGClearButtonMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				//if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				exm.Console.CBG_ClearButton();
				return 1;
			}
		}
		/// <summary>
		/// CBGREMOVEBMAP
		/// </summary>
		public sealed class CBGRemoveBMapMethod : FunctionMethod
		{
			public CBGRemoveBMapMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				//if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				exm.Console.CBG_ClearBMap();
				return 1;
			}
		}
		/// <summary>
		/// CBGSETG(int ID, int x, int y, int zdepth)
		/// </summary>
		public sealed class CBGSetGraphicsMethod : FunctionMethod
		{
			public CBGSetGraphicsMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));

				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Point p = ReadPoint(Name, exm, arguments, 1);
				Int64 z64 = arguments[3].GetIntValue(exm);
				if (z64 < int.MinValue || z64 > int.MaxValue || z64 == 0)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, z64, 3 + 1));
				exm.Console.CBG_SetGraphics(g, p.X, p.Y, (int)z64);
				return 1;

			}
		}

		/// <summary>
		/// CBGSETBMAPG(int ID, int x, int y, int zdepth)
		/// </summary>
		public sealed class CBGSetBMapGMethod : FunctionMethod
		{
			public CBGSetBMapGMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer};
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));

				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				exm.Console.CBG_SetButtonMap(g);
				return 1;

			}
		}

		/// <summary>
		/// CBGSETCIMG / CBGSETSPRITE
		/// snake 兼容：从第 2 个参数开始都可以省略，并支持 width/height/opacity/ColorMatrix。
		/// </summary>
		public sealed class CBGSetCIMGMethod : FunctionMethod
		{
			public CBGSetCIMGMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 1);
				if (arguments.Length > 8)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1);
				for (int i = 1; i <= 6 && i < arguments.Length; i++)
				{
					if (arguments[i] != null && arguments[i].GetEraType() != EraType.Integer)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
				}
				if (arguments.Length > 7 && arguments[7] != null)
				{
					if (!(arguments[7] is VariableTerm varToken) || (!varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D) || (!varToken.IsInteger && !varToken.IsFloat))
						return string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, name);
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				//if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));

				string imgname = arguments[0].GetStrValue(exm);
				ASprite img = AppContents.GetSprite(imgname);
				if (img == null || !img.IsCreated)
					return 0;
				int x = arguments.Length > 1 && arguments[1] != null ? (int)arguments[1].GetIntValue(exm) : 0;
				int y = arguments.Length > 2 && arguments[2] != null ? (int)arguments[2].GetIntValue(exm) : 0;
				Int64 z64 = arguments.Length > 3 && arguments[3] != null ? arguments[3].GetIntValue(exm) : 1;
				int width = arguments.Length > 4 && arguments[4] != null ? (int)arguments[4].GetIntValue(exm) : 0;
				int height = arguments.Length > 5 && arguments[5] != null ? (int)arguments[5].GetIntValue(exm) : 0;
				float opacity = arguments.Length > 6 && arguments[6] != null ? arguments[6].GetIntValue(exm) / 255.0f : 1.0f;
				float[][] colorMatrix = ReadOptionalColorMatrixForCbg(Name, exm, arguments, 7);
				if (z64 < int.MinValue || z64 > int.MaxValue || z64 == 0)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, z64, 3 + 1));
				if (!exm.Console.CBG_SetImage(img, x, y, (int)z64, width, height, opacity, colorMatrix))
					return 0;
				return 1;

			}
		}

		/// <summary>
		/// CBGSETBUTTONCIMG(int button, str imgName, str imgName, int x, int y,int zdepth str tooltipmes)
		/// </summary>
		public sealed class CBGSETButtonSpriteMethod : FunctionMethod
		{
			public CBGSETButtonSpriteMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.String };
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{

				if (arguments.Length < 6)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 6);
				if (arguments.Length > 7)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				if (arguments.Length != 6 && arguments.Length != 7)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);

				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);

					if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetEraType())
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));

				Int64 b64 = arguments[0].GetIntValue(exm);
				if (b64 < 0 || b64 > 0xFFFFFF)
					return 0;
				string imgnameN = arguments[1].GetStrValue(exm);
				ASprite imgN = AppContents.GetSprite(imgnameN);
				string imgnameB = arguments[2].GetStrValue(exm);
				ASprite imgB = AppContents.GetSprite(imgnameB);

				Point p = ReadPoint(Name, exm, arguments, 3);
				Int64 z64 = arguments[5].GetIntValue(exm);
				if (z64 < int.MinValue || z64 > int.MaxValue || z64 == 0)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, z64, 5 + 1));
				string tooltip = null;
				if(arguments.Length > 6)
					tooltip = arguments[6].GetStrValue(exm);
				if (!exm.Console.CBG_SetButtonImage((int)b64, imgN, imgB, p.X, p.Y, (int)z64, tooltip))
					return 0;
				return 1;

			}
		}

		static readonly short[] keytoggle = new short[256];

		internal static void ResetCanarySessionState()
		{
			Array.Clear(keytoggle, 0, keytoggle.Length);
		}

		private sealed class GetKeyStateMethod : FunctionMethod
		{
			public GetKeyStateMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (!exm.Console.IsActive)//アクティブでないならスルー
					return 0;
				Int64 keycode = arguments[0].GetIntValue(exm);
				if (keycode < 0 || keycode > 255)
					return 0;
				short s = WinInput.GetKeyState((int)keycode);
				short toggle = keytoggle[keycode];
				keytoggle[keycode] = (short)((s & 1) + 1);//初期値0、トグル状態に応じて1か2を代入。
				switch(Name)
				{
					case "GETKEY": return (s < 0) ? 1 : 0;
					case "GETKEYTRIGGERED":
						if (WinInput.ConsumeKeyLatch((int)keycode) != 0)
							return 1;
						return (s < 0) && (toggle != keytoggle[keycode]) ? 1 : 0;//初回はtrue、2回目以降はトグル状態が前回と違う場合のみ1
				}
				throw new ExeEE("異常な分岐");
			}
		}

		private sealed class MousePosMethod : FunctionMethod
		{
			public MousePosMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				switch(Name)
				{
					case "MOUSEX": return exm.Console.GetMousePosition().X;
					case "MOUSEY": return exm.Console.GetMousePosition().Y;
				}
				throw new ExeEE("異常な名前");
			}
		}


		private sealed class IsActiveMethod : FunctionMethod
		{
			public IsActiveMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return exm.Console.IsActive ? 1 : 0;
			}
		}

		private sealed class SetAnimeTimerMethod : FunctionMethod
		{
			public SetAnimeTimerMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] {EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				Int64 i64 = arguments[0].GetIntValue(exm);
				if (i64 < int.MinValue || i64 > short.MaxValue)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, i64, 1));
				exm.Console.setRedrawTimer((int)i64);
				return 1;
			}
		}

		private sealed class GetAnimeTimerMethod : FunctionMethod
		{
			public GetAnimeTimerMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return exm.Console.AnimeTimer;
			}
		}

		private sealed class ExistSoundMethod : FunctionMethod
		{
			public ExistSoundMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return global::GenericUtils.SoundFileExists(arguments[0].GetStrValue(exm)) ? 1 : 0;
			}
		}

		private sealed class ExistsImageLayerMethod : FunctionMethod
		{
			public ExistsImageLayerMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return exm.Console.ExistsImageLayer(arguments[0].GetIntValue(exm)) ? 1 : 0;
			}
		}
		private sealed class GetLineYMethod : FunctionMethod
		{
			public GetLineYMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				long lineNo = arguments[0].GetIntValue(exm);
				if (lineNo < 0)
					throw new CodeEE("GETLINEY関数の1番目の引数に0未満の値が指定されました: " + lineNo);
				int pointY = exm.Console.GetLinePointY((int)lineNo);
				return pointY + Config.LineHeight - exm.Console.ClientHeight;
			}
		}

		private sealed class ExistFunctionMethod : FunctionMethod
		{
			public ExistFunctionMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string name = arguments[0].GetStrValue(exm);
				// megaten 门控（P1）：EXISTFUNCTION 查询大小写跟随 ICVariable；Disabled 下与仅 ICFunction 等价。
				if (Config.ICFunction || (Config.ICVariable && Program.Compatibility.Megaten.UsesVariableCaseForFunctionLabelLookup))
					name = name.ToUpper();

				if (FunctionMethodCreator.GetMethodList(Program.Compatibility).TryGetValue(name, out var method))
				{
					if (method.ReturnType == EraType.Integer)
						return 2;
					if (method.ReturnType == EraType.String)
						return 3;
					if (method.ReturnType == EraType.Float || method.ReturnType == EraType.Float)
						return 4;
					return 1;
				}

				var labelDic = exm.Process.LabelDictionary;
				if (labelDic.GetNonEventLabel(name) != null)
					return 1;
				if (labelDic.GetEventLabels(name) != null)
					return 1;

				if (exm.Process.IsFunctionInLazyLoadingTable(name))
					return 1;

				return 0;
			}
		}

		private sealed class ExistFileMethod : FunctionMethod
		{
			public ExistFileMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string path = arguments[0].GetStrValue(exm);
				string resolved = uEmuera.Utils.ResolveScriptFilePath(path);
				return !string.IsNullOrWhiteSpace(resolved) && uEmuera.Utils.FileExists(resolved) ? 1 : 0;
			}
		}

		private sealed class GetCsvNoByNameMethod : FunctionMethod
		{
			readonly CharacterStrData searchType;
			public GetCsvNoByNameMethod(CharacterStrData type)
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = true;
				searchType = type;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string name = arguments[0].GetStrValue(exm);
				return exm.VEvaluator.Constant.GetCsvNoByCharacterStr(searchType, name);
			}
		}

		private sealed class GetSoundOrBgmInfoMethod : FunctionMethod
		{
			public GetSoundOrBgmInfoMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1 || arguments.Length > 2)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.Integer)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length == 2 && arguments[1] != null && arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				var info = global::GenericUtils.GetAudioInfo((int)arguments[0].GetIntValue(exm));
				if (arguments.Length < 2 || arguments[1] == null)
				{
					exm.VEvaluator.RESULT_ARRAY[0] = info.TotalMs;
					exm.VEvaluator.RESULT_ARRAY[1] = info.CurrentMs;
					exm.VEvaluator.RESULT_ARRAY[2] = info.Playing;
					exm.VEvaluator.RESULT_ARRAY[3] = info.Volume;
					exm.VEvaluator.RESULT_ARRAY[4] = info.Speed;
					return info.TotalMs;
				}
				switch ((int)arguments[1].GetIntValue(exm))
				{
					case 1:
						return info.TotalMs;
					case 2:
						return info.CurrentMs;
					case 3:
						return info.Playing;
					case 4:
						return info.Volume;
					case 5:
						return info.Speed;
					default:
						return 0;
				}
			}
		}

		private sealed class IsPlayingSoundMethod : FunctionMethod
		{
			public IsPlayingSoundMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length != 1)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.Integer)
					return name + "関数の1番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				int channel = (int)arguments[0].GetIntValue(exm);
				return global::GenericUtils.FindPlayingSound(channel);
			}
		}

		private sealed class SoundControlMethod : FunctionMethod
		{
			public SoundControlMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2 || arguments.Length > 4)
					return name + "関数の引数の数が正しくありません";
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null || arguments[i].GetEraType() != EraType.Integer)
						return name + "関数の引数の型が正しくありません";
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				int channel = (int)arguments[0].GetIntValue(exm);
				int action = (int)arguments[1].GetIntValue(exm);
				if (action == 3)
				{
					if (arguments.Length < 3)
						return -2;
					int speed = (int)arguments[2].GetIntValue(exm);
					bool preservePitch = arguments.Length < 4 || arguments[3].GetIntValue(exm) == 0;
					return global::GenericUtils.ControlSound(channel, action, speed, preservePitch);
				}
				if (arguments.Length != 2)
					return -2;
				return global::GenericUtils.ControlSound(channel, action);
			}
		}

		private sealed class IsPlayingBgmMethod : FunctionMethod
		{
			public IsPlayingBgmMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return global::GenericUtils.IsPlayingBgm() ? 1 : 0;
			}
		}

		private sealed class BgmControlMethod : FunctionMethod
		{
			public BgmControlMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1 || arguments.Length > 3)
					return name + "関数の引数の数が正しくありません";
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null || arguments[i].GetEraType() != EraType.Integer)
						return name + "関数の引数の型が正しくありません";
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				int action = (int)arguments[0].GetIntValue(exm);
				if (action == 3)
				{
					if (arguments.Length < 2)
						return -2;
					int speed = (int)arguments[1].GetIntValue(exm);
					bool preservePitch = arguments.Length < 3 || arguments[2].GetIntValue(exm) == 0;
					return global::GenericUtils.ControlBgm(action, speed, preservePitch);
				}
				if (arguments.Length != 1)
					return -2;
				return global::GenericUtils.ControlBgm(action);
			}
		}

		private sealed class GetTextDrawingModeMethod : FunctionMethod
		{
			public GetTextDrawingModeMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return exm.Console.SnakeTextDrawingMode;
			}
		}

		private sealed class GetSkiaQualityMethod : FunctionMethod
		{
			public GetSkiaQualityMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				switch (arguments[0].GetIntValue(exm))
				{
					case 0:
						return exm.Console.SnakeImageQuality;
					case 1:
						return exm.Console.SnakeFontHinting;
					case 2:
						return exm.Console.SnakeFontEdging;
					default:
						return -1;
				}
			}
		}

		private sealed class SnakeFallenStateMethod : FunctionMethod
		{
			static readonly string[] LoverTalents = { "恋人", "戀人" };
			static readonly string[] AffectionTalents = { "恋慕", "戀慕", "愛欲", "炮友", "砲友" };
			static readonly string[] FondnessTalents = { "思慕" };
			static readonly string[] EstablishedFactFlags = { "既成事実", "既成事实", "既成事實" };

			public SnakeFallenStateMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}

			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (!Program.Compatibility.Snake.IsEnabled)
					return name + "関数はSnake互換モード専用です";
				if (arguments.Length > 1)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
				if (arguments.Length == 1 && (arguments[0] == null || arguments[0].GetEraType() != EraType.Integer))
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1);
				return null;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				long target = (arguments.Length == 1 && arguments[0] != null) ? arguments[0].GetIntValue(exm) : exm.VEvaluator.TARGET;
				if (target < 0 || target >= exm.VEvaluator.CHARANUM)
					return 0;

				CharacterData chara = exm.VEvaluator.VariableData.CharacterList[(int)target];
				SparseArray<Int64> talent = chara.DataIntegerArray[(int)(VariableCode.TALENT & VariableCode.__LOWERCASE__)];
				SparseArray<Int64> cflag = chara.DataIntegerArray[(int)(VariableCode.CFLAG & VariableCode.__LOWERCASE__)];
				ConstantData constant = exm.VEvaluator.Constant;

				// Snake/eraTW 未修正数据里，部分口上会直接调用禁用块中的私有函数名。
				// 已由 ERB 定义的同名 #FUNCTION 会优先执行，这里只补共通 TALENT/CFLAG 能推导出的兜底等级。
				if (HasAnyKeywordValue(constant, talent, VariableCode.TALENT, LoverTalents))
					return 4;
				if (HasAnyKeywordValue(constant, talent, VariableCode.TALENT, AffectionTalents))
					return 3;
				if (HasEstablishedFact(constant, cflag) || HasAnyKeywordValue(constant, talent, VariableCode.TALENT, FondnessTalents))
					return 1;
				return 0;
			}

			static bool HasAnyKeywordValue(ConstantData constant, SparseArray<Int64> values, VariableCode code, string[] keywords)
			{
				for (int i = 0; i < keywords.Length; i++)
				{
					if (constant.TryKeywordToInteger(out int index, code, keywords[i], 1) && GetArrayValue(values, index) != 0)
						return true;
				}
				return false;
			}

			static bool HasEstablishedFact(ConstantData constant, SparseArray<Int64> cflag)
			{
				for (int i = 0; i < EstablishedFactFlags.Length; i++)
				{
					if (!constant.TryKeywordToInteger(out int index, VariableCode.CFLAG, EstablishedFactFlags[i], 1))
						continue;
					long value = GetArrayValue(cflag, index);
					if (GetBit(value, 0) || GetBit(value, 1))
						return true;
				}
				return false;
			}

			static long GetArrayValue(SparseArray<Int64> values, int index)
			{
				if (values == null || index < 0 || index >= values.Length)
					return 0;
				return values[index];
			}

			static bool GetBit(long value, int bit)
			{
				return ((value >> bit) & 1L) != 0;
			}
		}
		private sealed class SequenceInputMethod : FunctionMethod
		{
			public SequenceInputMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = false;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				exm.Process.SequenceInputValue = arguments[0].GetStrValue(exm);
				exm.Process.HasSequenceInput = true;
				return 0;
			}
		}

		private sealed class DisableInputMacroMethod : FunctionMethod
		{
			public DisableInputMacroMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				exm.Process.InputMacroEnabled = false;
				return 0;
			}
		}

		private sealed class EnableInputMacroMethod : FunctionMethod
		{
			public EnableInputMacroMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = false;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				exm.Process.InputMacroEnabled = true;
				return 0;
			}
		}

		private sealed class GetPlatformMethod : FunctionMethod
		{
			public GetPlatformMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { };
				CanRestructure = true;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string platform = global::Godot.OS.GetName();
				switch (platform)
				{
					case "Windows":
						return 0;
					case "Android":
						return 1;
					case "iOS":
						return 2;
					case "macOS":
					case "OSX":
						return 3;
					case "Linux":
					case "FreeBSD":
					case "NetBSD":
					case "OpenBSD":
					case "BSD":
						return 4;
					default:
						return 5;
				}
			}
		}

		private static string CheckSqlArgs(string name, IOperandTerm[] arguments, int min, int max, EraType firstType, EraType secondType)
		{
			if (arguments.Length < min)
				return name + "関数には少なくとも" + min.ToString() + "個の引数が必要です";
			if (arguments.Length > max)
				return name + "関数の引数が多すぎます";
			if (arguments.Length >= 1 && (arguments[0] == null || arguments[0].GetEraType() != firstType))
				return name + "関数の1番目の引数の型が正しくありません";
			if (arguments.Length >= 2 && (arguments[1] == null || arguments[1].GetEraType() != secondType))
				return name + "関数の2番目の引数の型が正しくありません";
			for (int i = 2; i < arguments.Length; i++)
			{
				if (arguments[i] == null)
					continue;
				EraType type = arguments[i].GetEraType();
				if (type != EraType.Integer && type != EraType.String)
					return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
			}
			return null;
		}

		private static object[] ReadSqlParameters(ExpressionMediator exm, IOperandTerm[] arguments, int start)
		{
			if (arguments.Length <= start)
				return null;
			object[] parameters = new object[arguments.Length - start];
			for (int i = start; i < arguments.Length; i++)
			{
				if (arguments[i] == null)
				{
					parameters[i - start] = null;
					continue;
				}
				if (arguments[i].GetEraType() == EraType.String)
					parameters[i - start] = arguments[i].GetStrValue(exm);
				else
					parameters[i - start] = arguments[i].GetIntValue(exm);
			}
			return parameters;
		}

		private static bool TryParseSnakeVariable(string source, out VariableTerm term)
		{
			term = null;
			if (string.IsNullOrWhiteSpace(source))
				return false;
			try
			{
				WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(source), LexEndWith.EoL, LexAnalyzeFlag.None);
				IdentifierWord id = wc.Current as IdentifierWord;
				if (id == null)
					return false;
				wc.ShiftNext();
				VariableToken token = ExpressionParser.ReduceVariableIdentifier(wc, id.Code);
				if (token == null)
					return false;
				term = VariableParser.ReduceVariable(token, wc);
				return term != null;
			}
			catch
			{
				return false;
			}
		}

		private sealed class V24TrigMethod : FunctionMethod
		{
			public V24TrigMethod(string kind, Func<double, double> func, bool checkUnitRange = false)
			{
				this.kind = kind;
				this.func = func;
				this.checkUnitRange = checkUnitRange;
				ReturnType = EraType.Integer;
				CanReturnFloat = true;
				argumentTypeArray = null;
				CanRestructure = true;
			}
			readonly string kind;
			readonly Func<double, double> func;
			readonly bool checkUnitRange;
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length != 1)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float))
					return name + "関数の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return CheckedDoubleToLong(kind, Calculate(ToDouble(arguments[0], exm)));
			}
			public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return Calculate(ToDouble(arguments[0], exm));
			}
			public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (HasFloatArg(arguments))
					return new SingleTerm(GetFloatValue(exm, arguments));
				return new SingleTerm(GetIntValue(exm, arguments));
			}
			double Calculate(double value)
			{
				if (checkUnitRange && (value < -1.0d || value > 1.0d))
					throw new CodeEE(kind + "関数の引数が範囲外です");
				double result = func(value);
				if (double.IsNaN(result))
					throw new CodeEE(kind + "関数の結果が非数値です");
				if (double.IsInfinity(result))
					throw new CodeEE(kind + "関数の結果が無限大です");
				return result;
			}
		}

		private sealed class V24RoundMathMethod : FunctionMethod
		{
			public V24RoundMathMethod(string kind, Func<double, double> func)
			{
				this.kind = kind;
				this.func = func;
				ReturnType = EraType.Integer;
				CanReturnFloat = true;
				argumentTypeArray = null;
				CanRestructure = true;
			}
			readonly string kind;
			readonly Func<double, double> func;
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length != 1)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || (arguments[0].GetEraType() != EraType.Integer && arguments[0].GetEraType() != EraType.Float))
					return name + "関数の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return CheckedDoubleToLong(kind, func(ToDouble(arguments[0], exm)));
			}
			public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return func(ToDouble(arguments[0], exm));
			}
			public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (HasFloatArg(arguments))
					return new SingleTerm(GetFloatValue(exm, arguments));
				return new SingleTerm(GetIntValue(exm, arguments));
			}
		}

		static long CheckedDoubleToLong(string name, double value)
		{
			if (double.IsNaN(value))
				throw new CodeEE(name + "関数の結果が非数値です");
			if (double.IsInfinity(value))
				throw new CodeEE(name + "関数の結果が無限大です");
			if (value >= long.MaxValue || value <= long.MinValue)
				throw new CodeEE(name + "関数の結果が64ビット符号付き整数の範囲外です");
			return (long)value;
		}

		private sealed class ArgLengthMethod : FunctionMethod
		{
			public ArgLengthMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[0];
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				return exm.Process.getCurrentState.CurrentVariadicArgCount;
			}
		}

		private sealed class UncheckedMathMethod : FunctionMethod
		{
			public UncheckedMathMethod(string kind)
			{
				this.kind = kind;
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = true;
			}
			readonly string kind;
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if ((kind == "NEG" && arguments.Length != 1) || (kind != "NEG" && arguments.Length != 2))
					return name + "関数の引数の数が正しくありません";
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null || arguments[i].GetEraType() != EraType.Integer)
						return name + "関数の引数の型が正しくありません";
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				unchecked
				{
					long left = arguments[0].GetIntValue(exm);
					if (kind == "NEG")
						return -left;
					long right = arguments[1].GetIntValue(exm);
					switch (kind)
					{
						case "ADD":
							return left + right;
						case "SUB":
							return left - right;
						case "MUL":
							return left * right;
						default:
							return 0;
					}
				}
			}
		}

		private static readonly long[] bitArrayMasks = CreateBitArrayMasks();

		private static long[] CreateBitArrayMasks()
		{
			long[] masks = new long[65];
			for (int i = 1; i <= 64; i++)
				masks[i] = 1L << (i - 1);
			return masks;
		}

		private static bool IsInt1DVariableTerm(IOperandTerm term)
		{
			if (!(term is VariableTerm varTerm))
				return false;
			VariableToken token = varTerm.Identifier;
			return token != null && token.GetEraType() == EraType.Integer && token.IsArray1D;
		}

		private static long BitArrayGetValue(object array, long index)
		{
			if (array is SparseArray<Int64> sparseArray)
				return sparseArray[index];
			return ((Int64[])array)[index];
		}

		private static void BitArraySetValue(object array, long index, long value)
		{
			if (array is SparseArray<Int64> sparseArray)
				sparseArray[index] = value;
			else
				((Int64[])array)[index] = value;
		}

		private static void BitArraySet(object array, long arrayLength, long idx, long val, long length)
		{
			if (array == null)
				return;
			long size = arrayLength * 64L;
			for (long i = 0; i < length; i++)
			{
				long bitIndex = idx + i;
				if (bitIndex >= size)
					break;
				if (bitIndex < 0)
					continue;
				long elementIndex = bitIndex / 64L;
				long maskIndex = bitIndex % 64L + 1L;
				long current = BitArrayGetValue(array, elementIndex);
				if (val != 0)
					current |= bitArrayMasks[maskIndex];
				else
					current &= ~bitArrayMasks[maskIndex];
				BitArraySetValue(array, elementIndex, current);
			}
		}

		private static long BitArrayGet(object array, long arrayLength, long idx)
		{
			if (array == null)
				return -1;
			long size = arrayLength * 64L;
			if (idx >= size || idx < 0)
				return -1;
			long elementIndex = idx / 64L;
			long maskIndex = idx % 64L + 1L;
			return (BitArrayGetValue(array, elementIndex) & bitArrayMasks[maskIndex]) != 0 ? 1 : 0;
		}

		private static long BitArrayToggle(object array, long arrayLength, long idx)
		{
			if (array == null)
				return 0;
			long size = arrayLength * 64L;
			if (idx >= size || idx < 0)
				return 0;
			long elementIndex = idx / 64L;
			long maskIndex = idx % 64L + 1L;
			long current = BitArrayGetValue(array, elementIndex) ^ bitArrayMasks[maskIndex];
			BitArraySetValue(array, elementIndex, current);
			return 1;
		}

		private static long BitArrayIndexOfFirst(object array, long arrayLength, long val)
		{
			if (array == null)
				return -1;
			bool searchForSet = val != 0;
			long elementIndex = 0;
			while (elementIndex < arrayLength && BitArrayGetValue(array, elementIndex) == (searchForSet ? 0L : -1L))
				elementIndex++;
			for (long i = 0; i < 64; i++)
			{
				long bitVal = BitArrayGet(array, arrayLength, elementIndex * 64L + i);
				if (bitVal == -1)
					return -1;
				if ((bitVal == 1) == searchForSet)
					return elementIndex * 64L + i;
			}
			return -1;
		}

		private sealed class BitSetMethod : FunctionMethod
		{
			public BitSetMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2 || arguments.Length > 4)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || !IsInt1DVariableTerm(arguments[0]))
					return name + "関数の1番目の引数は整数1次元配列変数である必要があります";
				for (int i = 1; i < arguments.Length; i++)
				{
					if (arguments[i] != null && arguments[i].GetEraType() != EraType.Integer)
						return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableToken token = ((VariableTerm)arguments[0]).Identifier;
				object array = token.GetArray();
				long idx = arguments[1].GetIntValue(exm);
				long val = arguments.Length > 2 && arguments[2] != null ? arguments[2].GetIntValue(exm) : 1;
				long length = arguments.Length > 3 && arguments[3] != null ? arguments[3].GetIntValue(exm) : 1;
				BitArraySet(array, token.GetLength(), idx, val, length);
				return 1;
			}
		}

		private sealed class BitGetMethod : FunctionMethod
		{
			public BitGetMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length != 2)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || !IsInt1DVariableTerm(arguments[0]))
					return name + "関数の1番目の引数は整数1次元配列変数である必要があります";
				if (arguments[1] == null || arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableToken token = ((VariableTerm)arguments[0]).Identifier;
				return BitArrayGet(token.GetArray(), token.GetLength(), arguments[1].GetIntValue(exm));
			}
		}

		private sealed class BitToggleMethod : FunctionMethod
		{
			public BitToggleMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length != 2)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || !IsInt1DVariableTerm(arguments[0]))
					return name + "関数の1番目の引数は整数1次元配列変数である必要があります";
				if (arguments[1] == null || arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableToken token = ((VariableTerm)arguments[0]).Identifier;
				return BitArrayToggle(token.GetArray(), token.GetLength(), arguments[1].GetIntValue(exm));
			}
		}

		private sealed class BitIndexOfFirstMethod : FunctionMethod
		{
			public BitIndexOfFirstMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1 || arguments.Length > 2)
					return name + "関数の引数の数が正しくありません";
				if (arguments[0] == null || !IsInt1DVariableTerm(arguments[0]))
					return name + "関数の1番目の引数は整数1次元配列変数である必要があります";
				if (arguments.Length > 1 && arguments[1] != null && arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableToken token = ((VariableTerm)arguments[0]).Identifier;
				long val = arguments.Length > 1 && arguments[1] != null ? arguments[1].GetIntValue(exm) : 0;
				return BitArrayIndexOfFirst(token.GetArray(), token.GetLength(), val);
			}
		}

		/// <summary>
		/// int SAVETEXT str text, int fileNo{, int force_savdir, int force_UTF8}
		/// </summary>
		private sealed class SaveTextMethod : FunctionMethod
		{
			public SaveTextMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String ,EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{

				if (arguments.Length < 2)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
				if (arguments.Length > 4)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);

					if (i == 1 && arguments[i].GetEraType() == EraType.String)
						continue;
					if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetEraType())
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
				}
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				bool forceSavdir = arguments.Length > 2 && (arguments[2].GetIntValue(exm) != 0);
				bool forceUTF8 = arguments.Length > 3 && (arguments[3].GetIntValue(exm) != 0);
				string savText = arguments[0].GetStrValue(exm);
				if (!TryGetTextPath(arguments[1], exm, forceSavdir, true, out string filepath, out bool indexedPath))
					return 0;
				Encoding encoding = forceUTF8 ?
					Encoding.GetEncoding("UTF-8") :
					Config.SaveEncode;
				try
				{
					if (indexedPath)
					{
						if (forceSavdir)
							Config.ForceCreateSavDir();
						else
							Config.CreateSavDir();
					}
					else
					{
						string directory = System.IO.Path.GetDirectoryName(filepath);
						if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
							System.IO.Directory.CreateDirectory(directory);
					}
					System.IO.File.WriteAllText(filepath, savText, encoding);
				}
				catch { return 0; }
				return 1;
			}
		}
		/// <summary>
		/// str LOADTEXT int fileNo{, int force_savdir, int force_UTF8}
		/// </summary>
		private sealed class LoadTextMethod : FunctionMethod
		{
			public LoadTextMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{

				if (arguments.Length < 1)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 1);
				if (arguments.Length > 3)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
				for (int i = 0; i < arguments.Length; i++)
				{
					if (arguments[i] == null)
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
					if (i == 0 && arguments[i].GetEraType() == EraType.String)
						continue;
					if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetEraType())
						return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
				}
				return null;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				bool forceSavdir = arguments.Length > 1 && (arguments[1].GetIntValue(exm) != 0);
				bool forceUTF8 = arguments.Length > 2 && (arguments[2].GetIntValue(exm) != 0);
				if (!TryGetTextPath(arguments[0], exm, forceSavdir, false, out string filepath, out _))
					return "";
				Encoding encoding = forceUTF8 ? Encoding.GetEncoding("UTF-8") : null;
				if (!System.IO.File.Exists(filepath))
					return "";
                string ret;
                try
                {
                    ret = encoding != null ? System.IO.File.ReadAllText(filepath, encoding) : ReadAllTextWithDetectedEncoding(filepath);
                }
                catch { return ""; }
                //一貫性の観点で\rには死んでもらう
                return ret.Replace("\r","");
			}
		}



		private static bool TryGetTextPath(IOperandTerm pathTerm, ExpressionMediator exm, bool forceSavdir, bool forSave, out string filepath, out bool indexedPath)
		{
			filepath = null;
			indexedPath = false;
			if (pathTerm.GetEraType() == EraType.Integer)
			{
				Int64 i64 = pathTerm.GetIntValue(exm);
				if (i64 < 0 || i64 > int.MaxValue)
					return false;
				int fileIndex = (int)i64;
				filepath = forceSavdir ?
					GetSaveDataPathText(fileIndex, Config.ForceSavDir) :
					GetSaveDataPathText(fileIndex, Config.SavDir);
				indexedPath = true;
				return true;
			}
			if (pathTerm.GetEraType() != EraType.String)
				return false;
			if (!TryGetSafeRelativeTextPath(pathTerm.GetStrValue(exm), out filepath))
				return false;
			string extension = System.IO.Path.HasExtension(filepath) ? System.IO.Path.GetExtension(filepath).TrimStart('.').ToLowerInvariant() : "";
			if (!Config.IsLoadTextExtensionAllowed(extension))
			{
				if (!forSave)
					return false;
				filepath = System.IO.Path.ChangeExtension(filepath, "txt");
			}
			return true;
		}

		private static bool TryGetSafeRelativeTextPath(string relativePath, out string filepath)
		{
			filepath = null;
			if (string.IsNullOrWhiteSpace(relativePath))
				return false;
			string sanitized = relativePath.Replace('\\', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);
			try
			{
				if (System.IO.Path.IsPathRooted(sanitized))
					return false;
				string baseDir = System.IO.Path.GetFullPath(Program.ExeDir);
				if (!baseDir.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
					baseDir += System.IO.Path.DirectorySeparatorChar;
				string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, sanitized));
				if (!candidate.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
					return false;
				filepath = candidate;
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static string ReadAllTextWithDetectedEncoding(string filepath)
		{
			try
			{
				byte[] bytes = System.IO.File.ReadAllBytes(filepath);
				// eraFL 的部分 xml 实际保存为 UTF-16/UTF-32，但声明仍写 utf-8。
				// 这里先按 BOM 识别真实编码，避免 LOADTEXT 把内容读成带 NUL 的乱码。
				if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
					return Encoding.UTF32.GetString(bytes);
				if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
					return new UTF32Encoding(true, true).GetString(bytes);
				if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
					return Encoding.Unicode.GetString(bytes);
				if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
					return Encoding.BigEndianUnicode.GetString(bytes);
				if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
					return new UTF8Encoding(true, true).GetString(bytes);
				try
				{
					return new UTF8Encoding(false, true).GetString(bytes);
				}
				catch
				{
					return Config.SaveEncode.GetString(bytes);
				}
			}
			catch
			{
				return "";
			}
		}

		private static string GetSaveDataPathText(int index, string dir) { return string.Format("{0}txt{1:00}.txt", dir, index); }
		private static string GetSaveDataPathGraphics(int index) { return string.Format("{0}img{1:0000}.png", Config.SavDir, index); }

		/// <summary>
		/// int GSAVE int ID, int fileNo
		/// </summary>
		public sealed class GraphicsSaveMethod : FunctionMethod
		{
			public GraphicsSaveMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;

				Int64 i64 = arguments[1].GetIntValue(exm);
				if (i64 < 0 || i64 > int.MaxValue)
					return 0;

				string filepath = GetSaveDataPathGraphics((int)i64);
				try
				{
					Config.CreateSavDir();
					g.Bitmap.Save(filepath);
				}
				catch
				{
					return 0;
				}
				return 1;
			}
		}
		/// <summary>
		/// int GLOAD int ID, int fileNo
		/// </summary>
		public sealed class GraphicsLoadMethod : FunctionMethod
		{
			public GraphicsLoadMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
					throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (g.IsCreated)
					return 0;

				Int64 i64 = arguments[1].GetIntValue(exm);
				if (i64 < 0 || i64 > int.MaxValue)
					return 0;

				string filepath = GetSaveDataPathGraphics((int)i64);
				Bitmap bmp = null;
				try
				{
					if (!System.IO.File.Exists(filepath))
						return 0;
					bmp = new BitmapTexture(filepath);
					if (bmp.Width > AbstractImage.MAX_IMAGESIZE || bmp.Height > AbstractImage.MAX_IMAGESIZE)
						return 0;
					if (bmp.Width == 0 || bmp.Height == 0)
						return 0;
					if (!g.GCreateFromF(bmp, (Config.TextDrawingMode == TextDrawingMode.WINAPI)))
						return 0;
				}
				catch (Exception e)
				{
					if (e is CodeEE)
						throw;
				}
				finally
				{
					if (bmp != null)
						bmp.Dispose();
				}
				if (!g.IsCreated)
					return 0;
				return 1;
			}
		}


	public sealed class GraphicsGetFontMethod : FunctionMethod
	{
		public GraphicsGetFontMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return "";
			return g.Fontname ?? "";
		}
	}

	public sealed class GraphicsGetTextSizeMethod : FunctionMethod
	{
		public GraphicsGetTextSizeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		// GGETTEXTSIZE 文本,字体名,字号[,样式]——对照 snake Creator.Method.cs：参数
		// String,String,Int,Int，OmitStart=3 表示第 4 参可省略。argumentTypeArray 为
		// null 本意是支持可变长参数，但基类 FunctionMethod.CheckArgumentType 会直接取
		// argumentTypeArray.Length 判空抛 NullReferenceException（回归：ffb4835 改
		// v24/Snake 语义时漏掉覆盖，erablue resort 地下城动画一调用即崩）。这里按真实
		// 签名校验，3~4 参合法、第 4 参（样式）可省略。
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments == null || arguments.Length < 3 || arguments.Length > 4)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
			if (arguments[0] == null || !arguments[0].IsString)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1);
			if (arguments[1] == null || !arguments[1].IsString)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 2);
			if (arguments[2] == null || !arguments[2].IsInteger)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 3);
			if (arguments.Length == 4 && (arguments[3] == null || !arguments[3].IsInteger))
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 4);
			return null;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			// v24/Snake measure an explicit font rather than a GraphicsImage handle.
			// Keep this worker-thread safe and allocation-free: Godot font resources are
			// main-thread owned, while the compatibility API only needs pixel dimensions.
			string text = arguments[0].GetStrValue(exm) ?? "";
			int fontSize = Math.Max(1, (int)arguments[2].GetIntValue(exm));
			int width = uEmuera.Utils.GetDisplayLength(text, fontSize);
			int height = Math.Max(1, fontSize + 6);
			exm.VEvaluator.RESULT_ARRAY[1] = height;
			return width;
		}
	}

	public sealed class GraphicsDrawLineMethod : FunctionMethod
	{
		public GraphicsDrawLineMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			int x1 = (int)arguments[1].GetIntValue(exm);
			int y1 = (int)arguments[2].GetIntValue(exm);
			int x2 = (int)arguments[3].GetIntValue(exm);
			int y2 = (int)arguments[4].GetIntValue(exm);
			g.GDrawLine(x1, y1, x2, y2);
			return 1;
		}
	}

	public sealed class GraphicsDrawStringMethod : FunctionMethod
	{
		public GraphicsDrawStringMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length != 2 && arguments.Length != 4)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
			if (arguments[0] == null || !arguments[0].IsInteger)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1);
			if (arguments[1] == null || !arguments[1].IsString)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 2);
			if (arguments.Length == 4)
			{
				if (arguments[2] == null || !arguments[2].IsInteger)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 3);
				if (arguments[3] == null || !arguments[3].IsInteger)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 4);
			}
			return null;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			string text = arguments[1].GetStrValue(exm) ?? "";
			int x = arguments.Length >= 4 ? (int)arguments[2].GetIntValue(exm) : 0;
			int y = arguments.Length >= 4 ? (int)arguments[3].GetIntValue(exm) : 0;
			return g.GDrawString(text, x, y) ? 1 : 0;
		}
	}

	public sealed class GraphicsDashStyleMethod : FunctionMethod
	{
		public GraphicsDashStyleMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			int style = (int)arguments[1].GetIntValue(exm);
			int cap = (int)arguments[2].GetIntValue(exm);
			g.GDashStyle(style, cap);
			return 1;
		}
	}

	public sealed class GraphicsRotateMethod : FunctionMethod
	{
		public GraphicsRotateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			long angle = arguments[1].GetIntValue(exm);
			int pivotX = (int)arguments[2].GetIntValue(exm);
			int pivotY = (int)arguments[3].GetIntValue(exm);
			g.GRotate(angle, pivotX, pivotY);
			return 1;
		}
	}

	public sealed class PolygonPointAddMethod : FunctionMethod
	{
		public PolygonPointAddMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			int x = (int)arguments[1].GetIntValue(exm);
			int y = (int)arguments[2].GetIntValue(exm);
			g.GDrawPolygonAddPoint(new Point(x, y));
			return 1;
		}
	}

	public sealed class PolygonPointClearMethod : FunctionMethod
	{
		public PolygonPointClearMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			g.GDrawPolygonClearPoint();
			return 1;
		}
	}

	public sealed class PolygonDrawMethod : FunctionMethod
	{
		public PolygonDrawMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			g.GDrawPolygon();
			return 1;
		}
	}

	public sealed class PolygonFillMethod : FunctionMethod
	{
		public PolygonFillMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			g.GFillPolygon();
			return 1;
		}
	}

	public sealed class SpriteCreateFromFileMethod : FunctionMethod
	{
		public SpriteCreateFromFileMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.String, EraType.String };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string imgName = arguments[0].GetStrValue(exm) ?? "";
			string filepath = arguments[1].GetStrValue(exm) ?? "";
			return AppContents.CreateSpriteFromFileDynamic(imgName, filepath) ? 1 : 0;
		}
	}

	public sealed class SpriteDisposeAllMethod : FunctionMethod
	{
		public SpriteDisposeAllMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			bool delCsv = arguments[0].GetIntValue(exm) != 0;
			return AppContents.SpriteDisposeAll(delCsv);
		}
	}

	public sealed class SetTextBoxMethod : FunctionMethod
	{
		public SetTextBoxMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.String };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string text = arguments[0].GetStrValue(exm) ?? "";
			exm.Console.SetTextBoxText(text);
			return 1;
		}
	}

	public sealed class GetTextBoxMethod : FunctionMethod
	{
		public GetTextBoxMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = new EraType[] { };
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return exm.Console.GetTextBoxText() ?? "";
		}
	}

	public sealed class MoveTextBoxMethod : FunctionMethod
	{
		public MoveTextBoxMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			var window = GlobalStatic.MainWindow;
			if (window == null)
				return 0;
			int x = (int)arguments[0].GetIntValue(exm);
			int y = (int)arguments[1].GetIntValue(exm);
			int width = (int)arguments[2].GetIntValue(exm);
			window.SetTextBoxPos(x, y, width);
			return 1;
		}
	}

	public sealed class ResumeTextBoxMethod : FunctionMethod
	{
		public ResumeTextBoxMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			var window = GlobalStatic.MainWindow;
			if (window == null)
				return 0;
			window.ResetTextBoxPos();
			return 1;
		}
	}

	public sealed class MouseButtonMethod : FunctionMethod
	{
		public MouseButtonMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = new EraType[] { };
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return GenericUtils.GetPointingButtonInput() ?? "";
		}
	}

	public sealed class VarSetExMethod : FunctionMethod
	{
		public VarSetExMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length < 2)
				return name + "関数には少なくとも2つの引数が必要です";
			if (arguments.Length > 5)
				return name + "関数の引数が多すぎます";
			if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
				return name + "関数の1番目の引数の型が正しくありません";
			if (arguments[1] == null)
				return name + "関数の2番目の引数が必要です";
			if (arguments.Length >= 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
				return name + "関数の3番目の引数の型が正しくありません";
			if (arguments.Length >= 4 && arguments[3] != null && arguments[3].GetEraType() != EraType.Integer)
				return name + "関数の4番目の引数の型が正しくありません";
			if (arguments.Length >= 5 && arguments[4] != null && arguments[4].GetEraType() != EraType.Integer)
				return name + "関数の5番目の引数の型が正しくありません";
			return null;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string varName = arguments[0].GetStrValue(exm) ?? "";
			VariableTerm varTerm = ParseVarSetExTerm(varName);
			if (varTerm == null || varTerm.Identifier == null)
				throw new CodeEE(varName + "は未定義の変数です");
			VariableToken token = varTerm.Identifier;
			if (token.IsConst)
				throw new CodeEE(varName + "は変更できません");

			bool setAllDims = arguments.Length < 3 || arguments[2] == null || arguments[2].GetIntValue(exm) != 0;
			int start = arguments.Length >= 4 && arguments[3] != null ? (int)arguments[3].GetIntValue(exm) : 0;
			int end = arguments.Length >= 5 && arguments[4] != null ? (int)arguments[4].GetIntValue(exm) : GetVarSetExDefaultEnd(token);

			if (token.IsArray1D)
			{
				ClampVarSetExRange(token.GetLength(), ref start, ref end);
				for (int i = start; i < end; i++)
					SetVarSetExValue(token, arguments[1], exm, new long[] { i });
			}
			else if (token.IsArray2D)
			{
				int len1 = token.GetLength(0);
				int len2 = token.GetLength(1);
				int selected1 = (int)GetVarSetExIndex(varTerm, 0, exm);
				int selected2 = (int)GetVarSetExIndex(varTerm, 1, exm);
				start = Math.Max(start, selected2);
				ClampVarSetExRange(len2, ref start, ref end);
				int firstBegin = setAllDims ? 0 : selected1;
				int firstEnd = setAllDims ? len1 : selected1 + 1;
				for (int i = firstBegin; i < firstEnd; i++)
					for (int j = start; j < end; j++)
						SetVarSetExValue(token, arguments[1], exm, new long[] { i, j });
			}
			else if (token.IsArray3D)
			{
				int len1 = token.GetLength(0);
				int len2 = token.GetLength(1);
				int len3 = token.GetLength(2);
				int selected1 = (int)GetVarSetExIndex(varTerm, 0, exm);
				int selected2 = (int)GetVarSetExIndex(varTerm, 1, exm);
				int selected3 = (int)GetVarSetExIndex(varTerm, 2, exm);
				start = Math.Max(start, selected3);
				ClampVarSetExRange(len3, ref start, ref end);
				int firstBegin = setAllDims ? 0 : selected1;
				int firstEnd = setAllDims ? len1 : selected1 + 1;
				int secondBegin = setAllDims ? 0 : selected2;
				int secondEnd = setAllDims ? len2 : selected2 + 1;
				for (int i = firstBegin; i < firstEnd; i++)
					for (int j = secondBegin; j < secondEnd; j++)
						for (int k = start; k < end; k++)
							SetVarSetExValue(token, arguments[1], exm, new long[] { i, j, k });
			}
			else
			{
				SetVarSetExValue(token, arguments[1], exm, new long[0]);
			}
			return 1;
		}

		private static VariableTerm ParseVarSetExTerm(string name)
		{
			WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
			return ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL) as VariableTerm;
		}

		private static int GetVarSetExDefaultEnd(VariableToken token)
		{
			if (token.IsArray1D)
				return token.GetLength();
			if (token.IsArray2D)
				return token.GetLength(1);
			if (token.IsArray3D)
				return token.GetLength(2);
			return 0;
		}

		private static long GetVarSetExIndex(VariableTerm term, int dimension, ExpressionMediator exm)
		{
			try
			{
				return term.GetElementInt(dimension, exm);
			}
			catch
			{
				return 0;
			}
		}

		private static void ClampVarSetExRange(int length, ref int start, ref int end)
		{
			if (start < 0)
				start = 0;
			if (end < start)
				end = start;
			if (end > length)
				end = length;
		}

		private static void SetVarSetExValue(VariableToken token, IOperandTerm value, ExpressionMediator exm, long[] indexes)
		{
			if (token.GetEraType() == EraType.String)
				token.SetValue(value.GetStrValue(exm) ?? "", indexes);
			else if (token.GetEraType() == EraType.Float)
				token.SetValue(ToDouble(value, exm), indexes);
			else
				token.SetValue(value.GetIntValue(exm), indexes);
		}
	}

	public sealed class RegexpMatchMethod : FunctionMethod
	{
		public RegexpMatchMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length < 2)
				return name + "関数には少なくとも2つの引数が必要です";
			if (arguments.Length > 4)
				return name + "関数の引数が多すぎます";
			if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
				return name + "関数の1番目の引数の型が正しくありません";
			if (arguments[1] == null || arguments[1].GetEraType() != EraType.String)
				return name + "関数の2番目の引数の型が正しくありません";
			if (arguments.Length == 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
				return name + "関数の3番目の引数の型が正しくありません";
			if (arguments.Length == 4)
			{
				if (!(arguments[2] is VariableTerm) || arguments[2].GetEraType() != EraType.Integer)
					return name + "関数の3番目の引数は整数変数である必要があります";
				if (!(arguments[3] is VariableTerm) || arguments[3].GetEraType() != EraType.String)
					return name + "関数の4番目の引数は文字列配列変数である必要があります";
				VariableTerm matchArray = (VariableTerm)arguments[3];
				if (!matchArray.Identifier.IsArray1D)
					return name + "関数の4番目の引数は文字列配列変数である必要があります";
			}
			return null;
		}
		static void OutputMatches(MatchCollection matches, Regex reg, SparseArray<string> results)
		{
			int index = 0;
			foreach (Match match in matches)
			{
				foreach (string name in reg.GetGroupNames())
				{
					if (index >= results.Length)
						return;
					results[index++] = match.Groups[name].Value;
				}
			}
		}
		static void OutputMatches(MatchCollection matches, Regex reg, VariableTerm resultArray, ExpressionMediator exm)
		{
			long length = resultArray.Identifier.GetLength();
			long index = 0;
			foreach (Match match in matches)
			{
				foreach (string name in reg.GetGroupNames())
				{
					if (index >= length)
						return;
					resultArray.Identifier.SetValue(match.Groups[name].Value, new long[] { index++ });
				}
			}
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string baseString = arguments[0].GetStrValue(exm) ?? "";
			string pattern = arguments[1].GetStrValue(exm) ?? "";
			System.Text.RegularExpressions.Regex reg;
			try
			{
				reg = new System.Text.RegularExpressions.Regex(pattern);
			}
			catch (ArgumentException e)
			{
				throw new CodeEE("無効な正規表現パターン: " + e.Message);
			}
			var matches = reg.Matches(baseString);
			int ret = matches.Count;
			if (arguments.Length == 3 && arguments[2].GetIntValue(exm) != 0)
			{
				exm.VEvaluator.RESULT_ARRAY[1] = reg.GetGroupNumbers().Length;
				if (ret > 0 && matches[0].Groups.Count > 0)
					OutputMatches(matches, reg, exm.VEvaluator.RESULTS_ARRAY);
			}
			if (arguments.Length == 4)
			{
				((VariableTerm)arguments[2]).SetValue(reg.GetGroupNumbers().Length, exm);
				if (ret > 0)
					OutputMatches(matches, reg, (VariableTerm)arguments[3], exm);
			}
			return ret;
		}
	}

	public sealed class ExistMethMethod : FunctionMethod
	{
		public ExistMethMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.String };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string name = arguments[0].GetStrValue(exm) ?? "";
			try
			{
				var term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, new IOperandTerm[0], true);
				if (term == null)
					return 0;
				// Check return type
				// gEmuera's GetFunctionMethod returns IOperandTerm which doesn't have GetEraType
				// We approximate by checking if it's a UserDefinedRefMethodTerm or UserDefinedRefMethodNoArgTerm
				if (term is UserDefinedRefMethodTerm || term is UserDefinedRefMethodNoArgTerm)
					return 1; // User ref methods are typically int
				return 1;
			}
			catch (CodeEE)
			{
				return 0;
			}
		}
	}

	public sealed class EnumNameMethod : FunctionMethod
	{
		readonly EType type;
		readonly EAction action;
		public EnumNameMethod(string typeStr, string actionStr)
		{
			this.type = (EType)System.Enum.Parse(typeof(EType), typeStr);
			this.action = (EAction)System.Enum.Parse(typeof(EAction), actionStr);
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.String };
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string arg = arguments[0].GetStrValue(exm)?.ToUpperInvariant() ?? "";
			List<string> source = new List<string>();
			switch (type)
			{
				case EType.Function:
					source.AddRange(GlobalStatic.LabelDictionary.NoneventKeys);
					break;
				case EType.Variable:
					source.AddRange(GlobalStatic.IdentifierDictionary.VarKeys);
					break;
				case EType.Macro:
					source.AddRange(GlobalStatic.IdentifierDictionary.MacroNames);
					break;
			}
			List<string> strs = new List<string>();
			if (arg.Length > 0)
			{
				foreach (string item in source)
				{
					string upper = item.ToUpperInvariant();
					if (upper.Length < arg.Length)
						continue;
					switch (action)
					{
						case EAction.BeginsWith:
							if (upper.StartsWith(arg, StringComparison.Ordinal))
								strs.Add(item);
							break;
						case EAction.EndsWith:
							if (upper.EndsWith(arg, StringComparison.Ordinal))
								strs.Add(item);
							break;
						case EAction.With:
							if (upper.Contains(arg))
								strs.Add(item);
							break;
					}
				}
			}
			var results = exm.VEvaluator.RESULTS_ARRAY;
			int count = Math.Min(strs.Count, results.Length);
			for (int i = 0; i < count; i++)
				results[i] = strs[i];
			return strs.Count;
		}
		enum EType { Function, Variable, Macro }
		enum EAction { BeginsWith, EndsWith, With }
	}

	public sealed class GraphicsDrawGWithRotateMethod : FunctionMethod
	{
		public GraphicsDrawGWithRotateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length != 3 && arguments.Length != 5)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
			for (int i = 0; i < arguments.Length; i++)
			{
				if (arguments[i] == null || !arguments[i].IsInteger)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
			}
			return null;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
			if (!dest.IsCreated)
				return 0;
			GraphicsImage src = ReadGraphics(Name, exm, arguments, 1);
			if (!src.IsCreated)
				return 0;
			long angle = arguments[2].GetIntValue(exm);
			// eraFL 省略 pivot 时默认围绕源图中心旋转，避免只给角度时图像被转出画布。
			int pivotX = arguments.Length >= 5 ? (int)arguments[3].GetIntValue(exm) : src.Width / 2;
			int pivotY = arguments.Length >= 5 ? (int)arguments[4].GetIntValue(exm) : src.Height / 2;
			dest.GDrawGWithRotate(src, angle, pivotX, pivotY);
			return 1;
		}
	}

	public sealed class GetDisplayLineMethod : FunctionMethod
	{
		public GetDisplayLineMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			int index = (int)arguments[0].GetIntValue(exm);
			return exm.Console.GetDisplayLine(index);
		}
	}

	public sealed class GetDoingFunctionMethod : FunctionMethod
	{
		public GetDoingFunctionMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = new EraType[] { };
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			LogicalLine line = exm.Process.GetScaningLine();
			if (line == null || line.ParentLabelLine == null)
				return "";
			return line.ParentLabelLine.LabelName ?? "";
		}
	}

	public sealed class GetMethMethod : FunctionMethod
	{
		public GetMethMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length < 1)
				return name + "関数には少なくとも1つの引数が必要です";
			if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
				return name + "関数の1番目の引数の型が正しくありません";
			if (arguments.Length >= 2 && arguments[1] == null)
				return name + "関数の2番目の引数が必要です";
			return null;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string name = arguments[0].GetStrValue(exm) ?? "";
			bool hasDefault = arguments.Length >= 2 && arguments[1] != null;
			long defaultValue = hasDefault ? arguments[1].GetIntValue(exm) : 0L;
			int argStart = hasDefault ? 2 : 1;
			IOperandTerm[] methArgs = new IOperandTerm[arguments.Length - argStart];
			for (int i = argStart; i < arguments.Length; i++)
			{
				if (arguments[i] == null)
					methArgs[i - argStart] = new SingleTerm(0L);
				else if (arguments[i].GetEraType() == EraType.String)
					methArgs[i - argStart] = new SingleTerm(arguments[i].GetStrValue(exm) ?? "");
				else
					methArgs[i - argStart] = new SingleTerm(arguments[i].GetIntValue(exm));
			}
			try
			{
				var term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, methArgs, true);
				if (term == null)
				{
					if (hasDefault)
						return defaultValue;
					throw new CodeEE("指定された関数名\"@" + name + "\"は存在しません");
				}
				return term.GetIntValue(exm);
			}
			catch (CodeEE)
			{
				if (!hasDefault)
					throw;
				return defaultValue;
			}
		}
	}

	public sealed class GetMethFMethod : FunctionMethod
	{
		public GetMethFMethod()
		{
			ReturnType = EraType.Float;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length < 1)
				return name + "関数には少なくとも1つの引数が必要です";
			if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
				return name + "関数の1番目の引数の型が正しくありません";
			if (arguments.Length >= 2 && arguments[1] == null)
				return name + "関数の2番目の引数が必要です";
			return null;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string name = arguments[0].GetStrValue(exm) ?? "";
			bool hasDefault = arguments.Length >= 2 && arguments[1] != null;
			double defaultValue = hasDefault ? ToDouble(arguments[1], exm) : 0.0d;
			int argStart = hasDefault ? 2 : 1;
			IOperandTerm[] methArgs = new IOperandTerm[arguments.Length - argStart];
			for (int i = argStart; i < arguments.Length; i++)
			{
				if (arguments[i] == null)
					methArgs[i - argStart] = new SingleTerm(0L);
				else if (arguments[i].GetEraType() == EraType.String)
					methArgs[i - argStart] = new SingleTerm(arguments[i].GetStrValue(exm) ?? "");
				else if (arguments[i].GetEraType() == EraType.Float)
					methArgs[i - argStart] = new SingleTerm(arguments[i].GetFloatValue(exm));
				else
					methArgs[i - argStart] = new SingleTerm(arguments[i].GetIntValue(exm));
			}
			try
			{
				var term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, methArgs, true);
				if (term == null)
				{
					if (hasDefault)
						return new SingleTerm(defaultValue);
					throw new CodeEE("指定された関数名\"@" + name + "\"は存在しません");
				}
				return new SingleTerm(term.GetFloatValue(exm));
			}
			catch (CodeEE)
			{
				if (!hasDefault)
					throw;
				return new SingleTerm(defaultValue);
			}
		}
	}

	public sealed class GetMethSMethod : FunctionMethod
	{
		public GetMethSMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = null;
			CanRestructure = false;
		}
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length < 1)
				return name + "関数には少なくとも1つの引数が必要です";
			if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
				return name + "関数の1番目の引数の型が正しくありません";
			if (arguments.Length >= 2 && arguments[1] == null)
				return name + "関数の2番目の引数が必要です";
			return null;
		}
		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			string name = arguments[0].GetStrValue(exm) ?? "";
			bool hasDefault = arguments.Length >= 2 && arguments[1] != null;
			string defaultValue = hasDefault ? arguments[1].GetStrValue(exm) ?? "" : "";
			int argStart = hasDefault ? 2 : 1;
			IOperandTerm[] methArgs = new IOperandTerm[arguments.Length - argStart];
			for (int i = argStart; i < arguments.Length; i++)
			{
				if (arguments[i] == null)
					methArgs[i - argStart] = new SingleTerm(0L);
				else if (arguments[i].GetEraType() == EraType.String)
					methArgs[i - argStart] = new SingleTerm(arguments[i].GetStrValue(exm) ?? "");
				else
					methArgs[i - argStart] = new SingleTerm(arguments[i].GetIntValue(exm));
			}
			try
			{
				var term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, methArgs, true);
				if (term == null)
				{
					if (hasDefault)
						return defaultValue;
					throw new CodeEE("指定された関数名\"@" + name + "\"は存在しません");
				}
				return term.GetStrValue(exm) ?? defaultValue;
			}
			catch (CodeEE)
			{
				if (!hasDefault)
					throw;
				return defaultValue;
			}
		}
	}
		private sealed class EvalMethod : FunctionMethod
		{
			public EvalMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 2)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length >= 2 && arguments[1] != null && arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string expressionStr = arguments[0].GetStrValue(exm);
				long defaultValue = arguments.Length > 1 && arguments[1] != null ? arguments[1].GetIntValue(exm) : 0;
				if (string.IsNullOrWhiteSpace(expressionStr))
					return defaultValue;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(expressionStr), LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					if (term == null)
						return defaultValue;
					term = term.Restructure(exm);
					if (term.GetEraType() == EraType.Integer)
						return term.GetIntValue(exm);
					else if (term.GetEraType() == EraType.Float)
						return (long)term.GetFloatValue(exm);
					else
						return defaultValue;
				}
				catch
				{
					return defaultValue;
				}
			}
		}

		private sealed class EvalFMethod : FunctionMethod
		{
			public EvalFMethod()
			{
				ReturnType = EraType.Float;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 2)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length >= 2 && arguments[1] != null
					&& arguments[1].GetEraType() != EraType.Integer
					&& arguments[1].GetEraType() != EraType.Float)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string expressionStr = arguments[0].GetStrValue(exm);
				double defaultValue = 0.0d;
				if (arguments.Length > 1 && arguments[1] != null)
					defaultValue = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
				if (string.IsNullOrWhiteSpace(expressionStr))
					return defaultValue;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(expressionStr), LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					if (term == null)
						return defaultValue;
					term = term.Restructure(exm);
					if (term.GetEraType() == EraType.Float)
						return term.GetFloatValue(exm);
					if (term.GetEraType() == EraType.Integer)
						return term.GetIntValue(exm);
					return defaultValue;
				}
				catch
				{
					return defaultValue;
				}
			}
		}

		private sealed class EvalSMethod : FunctionMethod
		{
			public EvalSMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 2)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length >= 2 && arguments[1] != null && arguments[1].GetEraType() != EraType.String)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string expressionStr = arguments[0].GetStrValue(exm);
				string defaultValue = arguments.Length > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : "";
				if (string.IsNullOrWhiteSpace(expressionStr))
					return defaultValue;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(expressionStr), LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					if (term == null)
						return defaultValue;
					term = term.Restructure(exm);
					if (term.GetEraType() == EraType.String)
						return term.GetStrValue(exm);
					else if (term.GetEraType() == EraType.Float)
						return term.GetFloatValue(exm).ToString();
					else
						return defaultValue;
				}
				catch
				{
					return defaultValue;
				}
			}
		}

		private sealed class MatchAllMethod : FunctionMethod
		{
			public MatchAllMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2)
					return name + "関数には少なくとも2つの引数が必要です";
				if (arguments.Length > 5)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null)
					return name + "関数の1番目の引数は省略できません";
				if (!(arguments[0] is VariableTerm))
					return name + "関数の1番目の引数が変数ではありません";
				if (arguments[1] == null)
					return name + "関数の2番目の引数は省略できません";
				if (arguments.Length >= 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
					return name + "関数の3番目の引数の型が正しくありません";
				if (arguments.Length >= 4 && arguments[3] != null && arguments[3].GetEraType() != EraType.Integer)
					return name + "関数の4番目の引数の型が正しくありません";
				if (arguments.Length >= 5 && arguments[4] != null && !(arguments[4] is VariableTerm))
					return name + "関数の5番目の引数は変数である必要があります";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableTerm varTerm = (VariableTerm)arguments[0];
				VariableToken token = varTerm.Identifier;
				EraType targetType = arguments[1].GetEraType();
				long beg = (arguments.Length > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
				long end;
				if (token.IsCharacterData)
					end = exm.VEvaluator.CHARANUM;
				else if (token.IsArray1D)
					end = token.GetLength();
				else
					end = 1;
				if (arguments.Length > 3 && arguments[3] != null)
					end = arguments[3].GetIntValue(exm);
				if (beg < 0 || end < 0)
					throw new CodeEE("検索範囲に負の値が渡されました");
				if (beg > end)
					throw new CodeEE("検索範囲の指定が不正です");
				long maxLen = token.IsCharacterData ? exm.VEvaluator.CHARANUM : token.IsArray1D ? token.GetLength() : 1;
				if (end > maxLen)
					end = maxLen;
				VariableTerm outArr = null;
				if (arguments.Length > 4 && arguments[4] != null)
					outArr = (VariableTerm)arguments[4];
				long count = 0;
				long[] idxs = new long[2];
				if (targetType == EraType.Integer)
				{
					long targetValue = arguments[1].GetIntValue(exm);
					for (long i = beg; i < end; i++)
					{
						idxs[0] = i;
						if (token.GetIntValue(exm, idxs) == targetValue)
						{
							if (outArr != null)
							{
								try
								{
									long outLen = outArr.Identifier.GetLength();
									if (count < outLen)
										outArr.Identifier.SetValue(i, new long[] { count });
								}
								catch { }
							}
							count++;
						}
					}
				}
				else if (targetType == EraType.String)
				{
					string targetStr = arguments[1].GetStrValue(exm);
					for (long i = beg; i < end; i++)
					{
						idxs[0] = i;
						if (token.GetStrValue(exm, idxs) == targetStr)
						{
							if (outArr != null)
							{
								try
								{
									long outLen = outArr.Identifier.GetLength();
									if (count < outLen)
										outArr.Identifier.SetValue(i, new long[] { count });
								}
								catch { }
							}
							count++;
						}
					}
				}
				else
				{
					throw new CodeEE("MATCHALL: サポートされていない型です");
				}
				return count;
			}
		}

		private sealed class MatchAllExMethod : FunctionMethod
		{
			public MatchAllExMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2)
					return name + "関数には少なくとも2つの引数が必要です";
				if (arguments.Length > 5)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null)
					return name + "関数の1番目の引数は省略できません";
				if (arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数が文字列ではありません";
				if (arguments[1] == null)
					return name + "関数の2番目の引数は省略できません";
				if (arguments.Length >= 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
					return name + "関数の3番目の引数の型が正しくありません";
				if (arguments.Length >= 4 && arguments[3] != null && arguments[3].GetEraType() != EraType.Integer)
					return name + "関数の4番目の引数の型が正しくありません";
				if (arguments.Length >= 5 && arguments[4] != null && !(arguments[4] is VariableTerm))
					return name + "関数の5番目の引数は変数である必要があります";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string varName = arguments[0].GetStrValue(exm);
				VariableToken token = null;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(varName), LexEndWith.EoL, LexAnalyzeFlag.None);
					IdentifierWord id = wc.Current as IdentifierWord;
					if (id != null)
					{
						wc.ShiftNext();
						token = ExpressionParser.ReduceVariableIdentifier(wc, id.Code);
					}
				}
				catch { }
				if (token == null)
					throw new CodeEE("変数 " + varName + " が見つかりません");
				EraType targetType = arguments[1].GetEraType();
				long beg = (arguments.Length > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
				long end;
				if (token.IsCharacterData)
					end = exm.VEvaluator.CHARANUM;
				else if (token.IsArray1D)
					end = token.GetLength();
				else
					end = 1;
				if (arguments.Length > 3 && arguments[3] != null)
					end = arguments[3].GetIntValue(exm);
				if (beg < 0 || end < 0)
					throw new CodeEE("検索範囲に負の値が渡されました");
				if (beg > end)
					throw new CodeEE("検索範囲の指定が不正です");
				long maxLen = token.IsCharacterData ? exm.VEvaluator.CHARANUM : token.IsArray1D ? token.GetLength() : 1;
				if (end > maxLen)
					end = maxLen;
				VariableTerm outArr = null;
				if (arguments.Length > 4 && arguments[4] != null)
					outArr = (VariableTerm)arguments[4];
				long count = 0;
				long[] idxs = new long[2];
				if (targetType == EraType.Integer)
				{
					long targetValue = arguments[1].GetIntValue(exm);
					for (long i = beg; i < end; i++)
					{
						idxs[0] = i;
						if (token.GetIntValue(exm, idxs) == targetValue)
						{
							if (outArr != null)
							{
								try
								{
									long outLen = outArr.Identifier.GetLength();
									if (count < outLen)
										outArr.Identifier.SetValue(i, new long[] { count });
								}
								catch { }
							}
							count++;
						}
					}
				}
				else if (targetType == EraType.String)
				{
					string targetStr = arguments[1].GetStrValue(exm);
					for (long i = beg; i < end; i++)
					{
						idxs[0] = i;
						if (token.GetStrValue(exm, idxs) == targetStr)
						{
							if (outArr != null)
							{
								try
								{
									long outLen = outArr.Identifier.GetLength();
									if (count < outLen)
										outArr.Identifier.SetValue(i, new long[] { count });
								}
								catch { }
							}
							count++;
						}
					}
				}
				else
				{
					throw new CodeEE("MATCHALLEX: サポートされていない型です");
				}
				return count;
			}
		}

		private sealed class ExistVarMethod : FunctionMethod
		{
			public ExistVarMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = true;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 2)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length >= 2 && arguments[1] != null && arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string name = arguments[0].GetStrValue(exm);
				long mode = (arguments.Length > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
				if (mode == 0)
				{
					try
					{
						VariableToken token = GlobalStatic.IdentifierDictionary.GetVariableToken(name, null, true);
						if (token != null)
						{
							long res = 0;
							if (token.IsInteger)
								res |= 1;
							else if (token.IsString)
								res |= 2;
							else if (token.IsFloat)
								res |= 32;
							if (token.IsConst)
								res |= 4;
							if (token.IsArray2D)
								res |= 8;
							if (token.IsArray3D)
								res |= 16;
							return res;
						}
					}
					catch
					{
						return 0;
					}
				}
				else
				{
					try
					{
						WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
						IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
						return term != null ? 1 : 0;
					}
					catch
					{
						return 0;
					}
				}
				return 0;
			}
		}

		private sealed class SetVarMethod : FunctionMethod
		{
			public SetVarMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2)
					return name + "関数には少なくとも2つの引数が必要です";
				if (arguments.Length > 3)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments[1] == null)
					return name + "関数の2番目の引数は省略できません";
				if (arguments.Length >= 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
					return name + "関数の3番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				long defaultValue = arguments.Length > 2 && arguments[2] != null ? arguments[2].GetIntValue(exm) : 0;
				bool hasDefault = arguments.Length > 2 && arguments[2] != null;
				string name = arguments[0].GetStrValue(exm);
				VariableTerm varTerm = null;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					varTerm = term as VariableTerm;
				}
				catch { }
				if (varTerm == null || varTerm.Identifier == null || varTerm.Identifier.IsConst)
					return hasDefault ? defaultValue : throw new CodeEE(name + "は変数ではありません");
				EraType varType = varTerm.Identifier.GetEraType();
				EraType valType = arguments[1].GetEraType();
				if (varType == EraType.String)
				{
					if (valType != EraType.String)
						return hasDefault ? defaultValue : throw new CodeEE(name + "は文字列型ではありません");
					varTerm.SetValue(arguments[1].GetStrValue(exm), exm);
				}
				else if (varType == EraType.Float)
				{
					if (valType != EraType.Float && valType != EraType.Integer)
						return hasDefault ? defaultValue : throw new CodeEE(name + "は小数型ではありません");
					varTerm.SetValue(ToDouble(arguments[1], exm), exm);
				}
				else
				{
					if (valType != EraType.Integer)
						return hasDefault ? defaultValue : throw new CodeEE(name + "は整数型ではありません");
					varTerm.SetValue(arguments[1].GetIntValue(exm), exm);
				}
				return 1;
			}
		}

		private sealed class IsDefinedMethod : FunctionMethod
		{
			public IsDefinedMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String };
				CanRestructure = true;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string name = arguments[0].GetStrValue(exm);
				if (string.IsNullOrWhiteSpace(name))
					return 0;
				var macro = GlobalStatic.IdentifierDictionary.GetMacro(name);
				return macro != null ? 1 : 0;
			}
		}

		private sealed class ClearMemoryMethod : FunctionMethod
		{
			public ClearMemoryMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[0];
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				long before = GC.GetTotalMemory(false);
				GC.Collect();
				long after = GC.GetTotalMemory(false);
				return before - after;
			}
		}

		private sealed class GetMemoryUsageMethod : FunctionMethod
		{
			public GetMemoryUsageMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[0];
				CanRestructure = false;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				using (System.Diagnostics.Process proc = System.Diagnostics.Process.GetCurrentProcess())
				{
					return proc.WorkingSet64;
				}
			}
		}

		private sealed class OutputLogMethod : FunctionMethod
		{
			public OutputLogMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length > 2)
					return name + "関数の引数が多すぎます";
				if (arguments.Length >= 1 && arguments[0] != null && arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length >= 2 && arguments[1] != null && arguments[1].GetEraType() != EraType.Integer)
					return name + "関数の2番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string filename = arguments.Length > 0 && arguments[0] != null ? arguments[0].GetStrValue(exm) : null;
				bool hideInfo = arguments.Length > 1 && arguments[1] != null && arguments[1].GetIntValue(exm) != 0;
				return exm.Console.OutputLog(filename, hideInfo) ? 1 : 0;
			}
		}
		public sealed class ErdNameMethod : FunctionMethod
		{
			public ErdNameMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 2)
						return name + "関数には少なくとも2つの引数が必要です";
					if (arguments.Length > 3)
						return name + "関数の引数が多すぎます";
					if (arguments[0] == null || !(arguments[0] is VariableTerm))
						return name + "関数の1番目の引数の型が正しくありません";
					if (arguments[1] == null || arguments[1].GetEraType() != EraType.Integer)
						return name + "関数の2番目の引数の型が正しくありません";
					if (arguments.Length == 3 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
						return name + "関数の3番目の引数の型が正しくありません";
					return null;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				VariableTerm varTerm = (VariableTerm)arguments[0];
				string varName = varTerm.Identifier.Name;
				long value = arguments[1].GetIntValue(exm);
				int index = arguments.Length > 2 && arguments[2] != null ? (int)arguments[2].GetIntValue(exm) : -1;
				if (index >= 0)
					varName = varName + "@" + index;
				return TryIntegerToKeyword(varName, value, index);
			}
			static string TryIntegerToKeyword(string variableName, long value, int index)
			{
				if (string.IsNullOrWhiteSpace(variableName))
						return "";
				var constantData = MinorShift.Emuera.GlobalStatic.ConstantData;
				if (constantData != null && constantData.TryIntegerToKeyword(out string erdValue, value, variableName))
					return erdValue;
				string name = variableName.Trim();
				int atIndex = name.IndexOf('@');
				if (atIndex >= 0)
						name = name.Substring(0, atIndex);
				int colonIndex = name.IndexOf(':');
				if (colonIndex >= 0)
						name = name.Substring(0, colonIndex);
				if (!System.Enum.TryParse<MinorShift.Emuera.GameData.Variable.VariableCode>(name, true, out var legacyCode))
						return "";
				if (constantData == null)
						return "";
				Dictionary<string, int> dictionary;
				try
				{
					dictionary = constantData.GetKeywordDictionary(out _, legacyCode, index);
				}
				catch
				{
					return "";
				}
				if (dictionary == null)
						return "";
				foreach (var pair in dictionary)
				{
					if (pair.Value == value)
							return pair.Key;
				}
				return "";
			}
		}

		public sealed class ToStrfMethod : FunctionMethod
		{
			public ToStrfMethod()
			{
				ReturnType = EraType.String;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
						return name + "関数には少なくとも1つの引数が必要です";
					if (arguments.Length > 2)
						return name + "関数の引数が多すぎます";
					return null;
			}
			public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				double value = ToDouble(arguments[0], exm);
				if (arguments.Length < 2)
						return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
				string format = arguments[1].GetStrValue(exm) ?? "";
				try
				{
					return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
				}
				catch (System.FormatException)
				{
					throw new CodeEE("TOSTRF: 無効な書式指定文字列です");
				}
			}
		}



		public sealed class EnumFilesMethod : FunctionMethod
		{
			public EnumFilesMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
			}
			public override string CheckArgumentType(string name, IOperandTerm[] arguments)
			{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 4)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				if (arguments.Length > 1 && arguments[1] != null && arguments[1].GetEraType() != EraType.String)
					return name + "関数の2番目の引数の型が正しくありません";
				if (arguments.Length > 2 && arguments[2] != null && arguments[2].GetEraType() != EraType.Integer)
					return name + "関数の3番目の引数の型が正しくありません";
				if (arguments.Length > 3 && arguments[3] != null &&
					arguments[3].GetEraType() != EraType.Integer && !CanWriteStringArray(arguments[3]))
					return name + "関数の4番目の引数の型が正しくありません";
				return null;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string dir = uEmuera.Utils.ResolveScriptDirectoryPath(arguments[0].GetStrValue(exm));
				if (string.IsNullOrWhiteSpace(dir) || !uEmuera.Utils.DirectoryExists(dir))
					return -1;
				string pattern = arguments.Length > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : "*";
				if (string.IsNullOrEmpty(pattern))
					pattern = "*";
				bool recursive = arguments.Length > 2 && arguments[2] != null && arguments[2].GetIntValue(exm) != 0;
				System.IO.SearchOption option = recursive ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly;
				List<string> files;
				try
				{
					// ENUMFILES 是运行时动态枚举：会话中途新增/删除的文件必须可见，
					// 先失效目录快照缓存再枚举（与 baseline 每次即时枚举语义一致）。
					uEmuera.Utils.InvalidateRecursiveDirListing(dir);
					files = uEmuera.Utils.GetFilePaths(dir, pattern, option);
					for (int i = 0; i < files.Count; i++)
						files[i] = uEmuera.Utils.GetRelativePathFromGameDir(files[i]);
				}
				catch
				{
					return -1;
				}
				return CopyEnumFilesResult(exm, arguments, files);
			}

			static bool CanWriteStringArray(IOperandTerm argument)
			{
				if (argument is not VariableTerm variableTerm)
					return false;
				try
				{
					object output = variableTerm.Identifier.GetArray();
					return output is string[] || output is SparseArray<string>;
				}
				catch
				{
					return false;
				}
			}

			static Int64 CopyEnumFilesResult(ExpressionMediator exm, IOperandTerm[] arguments, List<string> files)
			{
				if (arguments.Length > 3 && arguments[3] != null && CanWriteStringArray(arguments[3]))
				{
					object output = ((VariableTerm)arguments[3]).Identifier.GetArray();
					if (output is string[] array)
					{
						int arrayCount = Math.Min(files.Count, array.Length);
						for (int i = 0; i < arrayCount; i++)
							array[i] = files[i];
						return arrayCount;
					}

					var sparseArray = (SparseArray<string>)output;
					int sparseCount = Math.Min(files.Count, sparseArray.Length);
					for (int i = 0; i < sparseCount; i++)
						sparseArray[i] = files[i];
					return sparseCount;
				}

				var results = exm.VEvaluator.RESULTS_ARRAY;
				int resultsIndex = arguments.Length > 3 && arguments[3] != null ? (int)arguments[3].GetIntValue(exm) : 0;
				if (resultsIndex < 0)
					resultsIndex = 0;
				int offset = Math.Min(resultsIndex, results.Length);
				int resultCount = Math.Min(files.Count, results.Length - offset);
				for (int i = 0; i < resultCount; i++)
					results[offset + i] = files[i];
				return arguments.Length > 3 ? files.Count : resultCount;
			}
		}

		public sealed class GetVarMethod : FunctionMethod
			{
			public GetVarMethod()
				{
				ReturnType = EraType.Integer;
				argumentTypeArray = null;
				CanRestructure = false;
				}
				public override string CheckArgumentType(string name, IOperandTerm[] arguments)
				{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 2)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				return null;
				}
				public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
				{
				bool hasDefault = arguments.Length > 1 && arguments[1] != null;
				long defaultValue = hasDefault ? arguments[1].GetIntValue(exm) : 0;
				string name = arguments[0].GetStrValue(exm) ?? "";
				VariableTerm varTerm = null;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					varTerm = term as VariableTerm;
				}
				catch
				{
					if (hasDefault)
						return defaultValue;
					throw new CodeEE(name + "は変数ではありません");
				}
				if (varTerm == null || varTerm.Identifier == null)
					return hasDefault ? defaultValue : throw new CodeEE(name + "は変数ではありません");
				try
				{
					if (varTerm.Identifier.GetEraType() == EraType.Integer)
						return varTerm.GetIntValue(exm);
				}
				catch
				{
					if (hasDefault)
						return defaultValue;
					throw;
				}
				return hasDefault ? defaultValue : throw new CodeEE(name + "は整数型ではありません");
				}
			}

		public sealed class GetVarSMethod : FunctionMethod
			{
			public GetVarSMethod()
				{
				ReturnType = EraType.String;
				argumentTypeArray = null;
				CanRestructure = false;
				}
				public override string CheckArgumentType(string name, IOperandTerm[] arguments)
				{
				if (arguments.Length < 1)
					return name + "関数には少なくとも1つの引数が必要です";
				if (arguments.Length > 2)
					return name + "関数の引数が多すぎます";
				if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
					return name + "関数の1番目の引数の型が正しくありません";
				return null;
				}
				public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
				{
				bool hasDefault = arguments.Length > 1 && arguments[1] != null;
				string defaultValue = hasDefault ? arguments[1].GetStrValue(exm) ?? "" : "";
				string name = arguments[0].GetStrValue(exm) ?? "";
				VariableTerm varTerm = null;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
					IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					varTerm = term as VariableTerm;
				}
				catch
				{
					if (hasDefault)
						return defaultValue;
					throw new CodeEE(name + "は変数ではありません");
				}
				if (varTerm == null || varTerm.Identifier == null)
					return hasDefault ? defaultValue : throw new CodeEE(name + "は変数ではありません");
				try
				{
					if (varTerm.Identifier.GetEraType() == EraType.String)
						return varTerm.GetStrValue(exm) ?? defaultValue;
				}
				catch
				{
					if (hasDefault)
						return defaultValue;
					throw;
				}
				return hasDefault ? defaultValue : throw new CodeEE(name + "は文字列型ではありません");
				}
			}

			public sealed class GetVarFMethod : FunctionMethod
				{
				public GetVarFMethod()
					{
					ReturnType = EraType.Float;
					argumentTypeArray = null;
					CanRestructure = false;
					}
					public override string CheckArgumentType(string name, IOperandTerm[] arguments)
					{
					if (arguments.Length < 1)
						return name + "関数には少なくとも1つの引数が必要です";
					if (arguments.Length > 2)
						return name + "関数の引数が多すぎます";
					if (arguments[0] == null || arguments[0].GetEraType() != EraType.String)
						return name + "関数の1番目の引数の型が正しくありません";
					return null;
					}
					public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
					{
					bool hasDefault = arguments.Length > 1 && arguments[1] != null;
					double defaultValue = hasDefault ? ToDouble(arguments[1], exm) : 0.0;
					string name = arguments[0].GetStrValue(exm) ?? "";
					VariableTerm varTerm = null;
					try
					{
						WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
						IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
						varTerm = term as VariableTerm;
					}
					catch
					{
						if (hasDefault)
							return new SingleTerm(defaultValue);
						throw new CodeEE(name + "は変数ではありません");
					}
					if (varTerm == null || varTerm.Identifier == null)
						return hasDefault ? new SingleTerm(defaultValue) : throw new CodeEE(name + "は変数ではありません");
					try
					{
						if (varTerm.Identifier.IsFloat)
							return new SingleTerm(varTerm.GetFloatValue(exm));
					}
					catch
					{
						if (hasDefault)
							return new SingleTerm(defaultValue);
						throw;
					}
					return hasDefault ? new SingleTerm(defaultValue) : throw new CodeEE(name + "は小数型ではありません");
					}
				}

			private sealed class BitmapCacheEnableMethod : FunctionMethod
			{
				public BitmapCacheEnableMethod()
				{
					ReturnType = EraType.Integer;
					argumentTypeArray = null;
					CanRestructure = false;
				}
				public override string CheckArgumentType(string name, IOperandTerm[] arguments)
				{
					if (arguments.Length < 1)
						return name + "関数には少なくとも1つの引数が必要です";
					if (arguments.Length > 1)
						return name + "関数の引数が多すぎます";
					if (arguments[0] == null || arguments[0].GetEraType() != EraType.Integer)
						return name + "関数の1番目の引数の型が正しくありません";
					return null;
				}
				public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
				{
					exm.Console.BitmapCacheEnabledForNextLine = arguments[0].GetIntValue(exm) != 0;
					return 0;
				}
			}

			#region FlowInput / HotkeyState

			public sealed class FlowInputMethod : FunctionMethod
			{
				public FlowInputMethod()
				{
					ReturnType = EraType.Integer;
					argumentTypeArray = null;
					CanRestructure = false;
				}
				public override string CheckArgumentType(string name, IOperandTerm[] arguments)
				{
					if (arguments.Length < 1)
						return name + "関数には少なくとも1つの引数が必要です";
					if (arguments.Length > 4)
						return name + "関数の引数が多すぎます";
					for (int i = 0; i < arguments.Length; i++)
					{
						if (arguments[i] == null || arguments[i].GetEraType() != EraType.Integer)
							return name + "関数の引数の型が正しくありません";
					}
					return null;
				}
				public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
				{
					exm.Process.flowinputDef = arguments[0].GetIntValue(exm);
					if (arguments.Length > 1)
						exm.Process.flowinput = arguments[1].GetIntValue(exm) != 0;
					if (arguments.Length > 2)
						exm.Process.flowinputCanSkip = arguments[2].GetIntValue(exm) != 0;
					if (arguments.Length > 3)
						exm.Process.flowinputForceSkip = arguments[3].GetIntValue(exm) != 0;
					return 0;
				}
			}

			public sealed class FlowInputsMethod : FunctionMethod
			{
				public FlowInputsMethod()
				{
					ReturnType = EraType.Integer;
					argumentTypeArray = null;
					CanRestructure = false;
				}
				public override string CheckArgumentType(string name, IOperandTerm[] arguments)
				{
					if (arguments.Length < 1)
						return name + "関数には少なくとも1つの引数が必要です";
					if (arguments.Length > 2)
						return name + "関数の引数が多すぎます";
					if (arguments[0] == null || arguments[0].GetEraType() != EraType.Integer)
						return name + "関数の1番目の引数の型が正しくありません";
					if (arguments.Length > 1 && (arguments[1] == null || arguments[1].GetEraType() != EraType.String))
						return name + "関数の2番目の引数の型が正しくありません";
					return null;
				}
				public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
				{
					exm.Process.flowinputString = arguments[0].GetIntValue(exm) != 0;
					if (arguments.Length > 1)
						exm.Process.flowinputDefString = arguments[1].GetStrValue(exm) ?? "";
					return 0;
				}
			}

			public sealed class HotkeyStateInitMethod : FunctionMethod
			{
				public HotkeyStateInitMethod()
				{
					ReturnType = EraType.Integer;
					argumentTypeArray = new EraType[] { EraType.Integer };
					CanRestructure = false;
				}
				public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
				{
					long size = arguments[0].GetIntValue(exm);
					exm.Console.HotkeyStateInitialize(size);
					return 0;
				}
			}

			public sealed class HotkeyStateMethod : FunctionMethod
			{
				public HotkeyStateMethod()
				{
					ReturnType = EraType.Integer;
					argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
					CanRestructure = false;
				}
				public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
				{
					long index = arguments[0].GetIntValue(exm);
					long value = arguments[1].GetIntValue(exm);
					exm.Console.HotkeyStateSet(index, value);
					return 0;
				}
			}

			#endregion

			#endregion
		}
}
