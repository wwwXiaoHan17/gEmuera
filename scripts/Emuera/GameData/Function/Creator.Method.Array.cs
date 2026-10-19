// Creator.Method.Array.cs —— 承载变量操作系/数组表达式函数功能域，自 Creator.Method.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
    }
}
