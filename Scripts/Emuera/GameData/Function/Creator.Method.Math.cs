// Creator.Method.Math.cs —— 承载数学関数功能域（Rand/Max/Abs/Power/Sqrt/Cbrt/Log/Exp/Sign/GetLimit 及 ToDouble/HasFloatArg 共享 helper），自 Creator.Method.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
    }
}
