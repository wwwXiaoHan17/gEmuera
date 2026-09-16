// Creator.Method.Csv.cs —— 承载 CSV / キャラクタデータ関数功能域（GETCHARA/GETSPCHARA/CSVSTRDATA/CSVCSTR/CSVDATA/FINDCHARA/EXISTCSV），自 Creator.Method.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
    }
}
