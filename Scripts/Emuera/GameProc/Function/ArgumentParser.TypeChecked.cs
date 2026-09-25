// ArgumentParser.TypeChecked.cs —— 承载"正規型"定式构建器功能域（同时执行 popTerms() 与 checkArgumentType() 的 32 个 ArgumentBuilder），自 ArgumentBuilder.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using System;
using System.Collections.Generic;
//using System.Drawing;
//using Microsoft.VisualBasic;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Function;
using uEmuera.Drawing;
using uEmuera.VisualBasic;

namespace MinorShift.Emuera.GameProc.Function
{
	internal static partial class ArgumentParser
	{
		#region 正規型 popTerms()とcheckArgumentType()を両方行うもの。考えることは最低限でよい。

		private sealed class INT_EXPRESSION_ArgumentBuilder : ArgumentBuilder
		{
			public INT_EXPRESSION_ArgumentBuilder(bool nullable)
			{
				argumentTypeArray = new EraType[] { EraType.Integer };
				//if (nullable)妥協
				minArg = 0;
				this.nullable = nullable;
			}

            readonly bool nullable;

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				IOperandTerm term;
				if (terms.Length == 0)
				{
					term = new SingleTerm(0);
					if (!nullable)
					{
						if (line.Function.IsExtended())
							warn("省略できない引数が省略されています。Emueraは0を補います", line, 1, false);
						else
							warn("省略できない引数が省略されています。Emueraは0を補いますがeramakerの動作は不定です", line, 1, false);
					}
				}
				else
				{
					term = terms[0];
				}
				
				if (line.FunctionCode == FunctionCode.REPEAT)
				{
					// 参考侧：COUNT 变量被禁用时 REPEAT 不可用（解析期致命错误，参考文案）
					if (GlobalStatic.IdentifierDictionary.getVarTokenIsForbid("COUNT"))
						throw new CodeEE("COUNTが使用禁止変数になっているため、REPEATは使用できません");
					if ((term is SingleTerm) && (term.GetIntValue(null) <= 0L))
					{
						warn("0回以下のREPEATです。(eramakerではエラーになります)", line, 0, true);
					}
					VariableToken count = GlobalStatic.VariableData.GetSystemVariableToken("COUNT");
					VariableTerm repCount = new VariableTerm(count, new IOperandTerm[] { new SingleTerm(0) });
					repCount.Restructure(exm);
					return new SpForNextArgment(repCount, new SingleTerm(0), term, new SingleTerm(1));
				}
				ExpressionArgument ret = new ExpressionArgument(term);
				if (term is SingleTerm)
				{
					Int64 i = term.GetIntValue(null);
					ret.ConstInt = i;
					ret.IsConst = true;
					if (line.FunctionCode == FunctionCode.CLEARLINE)
					{
						if (i <= 0L)
							warn("引数に0以下の値が渡されています(この行は何もしません)", line, 1, false);
					}
					else if (line.FunctionCode == FunctionCode.FONTSTYLE)
					{
						if (i < 0L)
							warn("引数に負の値が渡されています(結果は不定です)", line, 1, false);
					}
				}
				return ret;
			}
		}

		private sealed class INT_ANY_ArgumentBuilder : ArgumentBuilder
		{
			public INT_ANY_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer };
				minArg = 0;
				argAny = true;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;

				List<IOperandTerm> termList = new List<IOperandTerm>();
				termList.AddRange(terms);
				ExpressionArrayArgument ret = new ExpressionArrayArgument(termList);
				if (terms.Length == 0)
				{
					if (line.FunctionCode == FunctionCode.RETURN)
					{
						termList.Add(new SingleTerm(0));
						ret.IsConst = true;
						ret.ConstInt = 0;
						return ret;
					}
					warn("引数が設定されていません", line, 2, false);
					return null;
				}
                else if (terms.Length == 1)
                {
                    if (terms[0] is SingleTerm s)
                    {
                        ret.IsConst = true;
                        ret.ConstInt = s.Int;
                        return ret;
                    }
                    else if (line.FunctionCode == FunctionCode.RETURN)
                    {
                        //定数式は定数化してしまうので現行システムでは見つけられない
                        if (terms[0] is VariableTerm)
                            warn("RETURNの引数に変数が渡されています(eramaker：常に0を返します)", line, 0, true);
                        else
                            warn("RETURNの引数に数式が渡されています(eramaker：Emueraとは異なる値を返します)", line, 0, true);
                    }
                }
                else
                {
                    warn(line.Function.Name + "の引数に複数の値が与えられています(eramaker：非対応です)", line, 0, true);
                }
				return ret;
			}
		}
		
		private sealed class STR_EXPRESSION_ArgumentBuilder : ArgumentBuilder
		{
			public STR_EXPRESSION_ArgumentBuilder(bool nullable)
			{
				argumentTypeArray = new EraType[] { EraType.String };
				if (nullable)
					minArg = 0;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				ExpressionArgument ret;
				if (terms.Length == 0)
				{
                    ret = new ExpressionArgument(new SingleTerm(""))
                    {
                        ConstStr = "",
                        IsConst = true
                    };
                    return ret;
				}
				return new ExpressionArgument(terms[0]);
			}
		}

		private sealed class EXPRESSION_ArgumentBuilder : ArgumentBuilder
		{
			public EXPRESSION_ArgumentBuilder(bool nullable)
			{
				argumentTypeArray = new EraType[] { EraType.Void };
				if (nullable)
					minArg = 0;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				if (terms.Length == 0)
				{
                    ExpressionArgument ret = new ExpressionArgument(null)
                    {
                        ConstStr = "",
                        ConstInt = 0,
                        IsConst = true
                    };
                    return ret;
				}
				return new ExpressionArgument(terms[0]);
			}
		}

		private sealed class SP_BAR_ArgumentBuilder : ArgumentBuilder
		{
			public SP_BAR_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				//minArg = 3;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				return new SpBarArgument(terms[0], terms[1], terms[2]);
			}
		}

		private sealed class SP_SWAP_ArgumentBuilder : ArgumentBuilder
		{
            //emuera1803beta2+v1 第2引数省略型に対応
			public SP_SWAP_ArgumentBuilder(bool nullable)
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
                if (nullable)
                    minArg = 1;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
                //上の判定で省略不可時はここに来ないので即さばける
                if (terms.Length == 1)
                    terms = new IOperandTerm[] { terms[0], null };
				return new SpSwapCharaArgument(terms[0], terms[1]);
			}
		}

        private sealed class SP_SAVEDATA_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SAVEDATA_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.String };
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				return new SpSaveDataArgument(terms[0], terms[1]);
			}
		}

        private sealed class SP_TINPUT_ArgumentBuilder : ArgumentBuilder
        {
            public SP_TINPUT_ArgumentBuilder()
            {
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.String, EraType.Integer, EraType.Integer };
                minArg = 2;
            }
            public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
            {
                IOperandTerm[] terms = popTerms(line);
                IOperandTerm term3 = null, term4 = null, term5 = null, term6 = null;
                if (!checkArgumentType(line, exm, terms))
                    return null;
                if (terms.Length > 2)
                    term3 = terms[2];
                if (terms.Length > 3)
                    term4 = terms[3];
                if (terms.Length > 4)
                    term5 = terms[4];
                if (terms.Length > 5)
                    term6 = terms[5];

                return new SpTInputsArgument(terms[0], terms[1], term3, term4, term5, term6);
            }
        }
        
        private sealed class SP_TINPUTS_ArgumentBuilder : ArgumentBuilder
		{
			public SP_TINPUTS_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.Integer, EraType.String, EraType.Integer, EraType.Integer };
				minArg = 2;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
                IOperandTerm term3 = null, term4 = null, term5 = null, term6 = null;
                if (!checkArgumentType(line, exm, terms))
					return null;
                if (terms.Length > 2)
                    term3 = terms[2];
                if (terms.Length > 3)
                    term4 = terms[3];
                if (terms.Length > 4)
                    term5 = terms[4];
                if (terms.Length > 5)
                    term6 = terms[5];
                return new SpTInputsArgument(terms[0], terms[1], term3, term4, term5, term6);
			}
		}

		private sealed class SP_FOR_NEXT_ArgumentBuilder : ArgumentBuilder
		{
			public SP_FOR_NEXT_ArgumentBuilder(bool allowsOmittedStart)
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Void, EraType.Integer, EraType.Integer };
				if (allowsOmittedStart)
					nullableArgumentArray = new bool[] { false, true, false, false };
				minArg = 3;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm varTerm = getChangeableVariable(terms, 1, line);
				if (varTerm == null)
					return null;
				if (varTerm.Identifier.IsCharacterData)
				{ warn("第1引数にキャラクタ変数を指定することはできません", line, 2, false); return null; }

				IOperandTerm start = terms[1];
				IOperandTerm end = terms[2];
				IOperandTerm step;
				if (start == null)
					start = new SingleTerm(0);
				if ((terms.Length > 3) && (terms[3] != null))
					step = terms[3];
				else
					step = new SingleTerm(1);
				if (!start.IsInteger)
				{ warn("第2引数の型が違います", line, 2, false); return null; }
				return new SpForNextArgment(varTerm, start, end, step);
			}
		}

		private sealed class SP_POWER_ArgumentBuilder : ArgumentBuilder
		{
			public SP_POWER_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				//minArg = 2;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm varTerm = getChangeableVariable(terms, 1, line);
				if (varTerm == null)
					return null;

				return new SpPowerArgument(varTerm, terms[1], terms[2]);
			}
		}

        private sealed class SP_SWAPVAR_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SWAPVAR_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Void, EraType.Void };
				//minArg = 2;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm x = getChangeableVariable(terms, 1, line);
				if (x == null)
					return null;
				VariableTerm y = getChangeableVariable(terms, 2, line);
				if (y == null)
					return null;
				if (x.GetEraType() != y.GetEraType())
				{
					warn("引数の型が異なります", line, 2, false);
					return null;
				}
				return new SpSwapVarArgument(x, y);
			}
		}
		
		private sealed class VAR_INT_ArgumentBuilder : ArgumentBuilder
		{
			public VAR_INT_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer };
				minArg = 0;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (terms.Length == 0)
					return new PrintDataArgument(null);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm varTerm = getChangeableVariable(terms, 1, line);
				if (varTerm == null)
					return null;
				return new PrintDataArgument(varTerm);
			}
		}

        private sealed class VAR_STR_ArgumentBuilder : ArgumentBuilder
        {
            public VAR_STR_ArgumentBuilder()
            {
                argumentTypeArray = new EraType[] { EraType.String };
                minArg = 0;
            }
            public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
            {
                IOperandTerm[] terms = popTerms(line);
                if (terms.Length == 0)
                {
                    VariableToken varToken = GlobalStatic.VariableData.GetSystemVariableToken("RESULTS");
                    VariableTerm varTerm = new VariableTerm(varToken, new IOperandTerm[] { new SingleTerm(0) });
                    return new StrDataArgument(varTerm);
                }
                if (!checkArgumentType(line, exm, terms))
                    return null;
                VariableTerm x = getChangeableVariable(terms, 1, line);
                if (x == null)
                    return null;
                return new StrDataArgument(x);
            }
        }

		private sealed class BIT_ARG_ArgumentBuilder : ArgumentBuilder
		{
			public BIT_ARG_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				minArg = 2;
                argAny = true;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
                VariableTerm varTerm = getChangeableVariable(terms, 1, line);
				if (varTerm == null)
					return null;
                List<IOperandTerm> termList = new List<IOperandTerm>();
                termList.AddRange(terms);
                //最初の項はいらない
                termList.RemoveAt(0);
				BitArgument ret = new BitArgument(varTerm, termList.ToArray());
                for (int i = 0; i < termList.Count; i++)
                {
                    if (termList[i] is SingleTerm term)
                    {
                        Int64 bit = term.Int;
                        if ((bit < 0) || (bit > 63))
                        {
                            warn("第" + Strings.StrConv((i + 2).ToString(), VbStrConv.Wide, Config.Language) + "引数(" + bit.ToString() + ")が範囲(０～６３)を超えています", line, 2, false);
                            return null;
                        }
                    }
                }
				return ret;
			}
		}

		private sealed class SP_VAR_SET_ArgumentBuilder : ArgumentBuilder
		{
			public SP_VAR_SET_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Void, EraType.Void, EraType.Integer, EraType.Integer };
				minArg = 1;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
				VariableToken nakedArrayTarget = tryGetNakedArray1DVarSetTarget(wc);
				IOperandTerm[] terms = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm varTerm = getChangeableVariable(terms, 1, line);
				if (varTerm == null)
					return null;
                if (varTerm.Identifier.IsConst)
                {
					warn("値を変更できない変数" + varTerm.Identifier.Name + "が指定されました", line, 2, false);
                    return null;
                }

				IOperandTerm term, term3 = null, term4 = null;
				if (terms.Length > 1)
					term = terms[1];
				else
				{
					if (varTerm.IsString)
						term = new SingleTerm("");
					else
						term = new SingleTerm(0);
				}
				if (varTerm is VariableNoArgTerm)
				{
					if (terms.Length > 2)
					{
						warn("対象となる変数" + varTerm.Identifier.Name + "の要素を省略する場合には第3引数以降を設定できません", line, 2, false);
						return null;
					}
					return new SpVarSetArgument(new FixedVariableTerm(varTerm.Identifier), term, null, null);
				}
				if (terms.Length > 2)
					term3 = terms[2];
				if (terms.Length > 3)
					term4 = terms[3];
				if (terms.Length >= 3 && !varTerm.Identifier.IsArray1D)
					warn("第３引数以降は1次元配列以外では無視されます", line, 1, false);
				if (term.GetEraType() != varTerm.GetEraType())
				{
					warn("２つの引数の型が一致していません", line, 2, false);
					return null;
				}
				if (nakedArrayTarget != null)
					return new SpVarSetArgument(new FixedVariableTerm(nakedArrayTarget), term, term3, term4);
				return new SpVarSetArgument(varTerm, term, term3, term4);
			}

			private static VariableToken tryGetNakedArray1DVarSetTarget(WordCollection wc)
			{
				// 仅 VARSET 第 1 参数需要兼容原 Emuera：裸 1D 数组名表示整数组写入目标，而不是第 0 项。
				if (wc == null || wc.Collection.Count == 0 || !(wc.Collection[0] is IdentifierWord idWord))
					return null;

				int index = 1;
				string subKey = null;
				if (index < wc.Collection.Count && wc.Collection[index].Type == '@')
				{
					index++;
					if (index >= wc.Collection.Count || !(wc.Collection[index] is IdentifierWord subIdWord))
						return null;
					subKey = subIdWord.Code;
					index++;
				}
				if (index < wc.Collection.Count && wc.Collection[index].Type != ',')
					return null;

				VariableToken token;
				try
				{
					token = GlobalStatic.IdentifierDictionary.GetVariableToken(idWord.Code, subKey, true);
				}
				catch
				{
					return null;
				}
				if (token == null || !token.IsArray1D || token.IsCharacterData)
					return null;
				return token;
			}
		}

		private sealed class SP_CVAR_SET_ArgumentBuilder : ArgumentBuilder
		{
			public SP_CVAR_SET_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Void, EraType.Void, EraType.Void, EraType.Integer, EraType.Integer };
				minArg = 1;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;

				VariableTerm varTerm = getChangeableVariable(terms, 1, line);
				if (varTerm == null)
					return null;
				if (!varTerm.Identifier.IsCharacterData)
				{ warn("第１引数にキャラクタ変数以外の変数を指定することはできません", line, 2, false); return null; }
				//1803beta004 暫定CDFLAGを弾く
				if (varTerm.Identifier.IsArray2D)
				{ warn("第１引数に二次元配列の変数を指定することはできません", line, 2, false); return null; }
				IOperandTerm index, term, term4 = null, term5 = null;
				if (terms.Length > 1)
					index = terms[1];
				else
					index = new SingleTerm(0);
				if (terms.Length > 2)
					term = terms[2];
				else
				{
					if (varTerm.IsString)
						term = new SingleTerm("");
					else
						term = new SingleTerm(0);
				}
				if (terms.Length > 3)
					term4 = terms[3];
				if (terms.Length > 4)
					term5 = terms[4];
				if (index is SingleTerm term1 && index.GetEraType() == EraType.String && varTerm.Identifier.IsArray1D)
				{
					if (!GlobalStatic.ConstantData.isDefined(varTerm.Identifier.Code, term1.Str))
					{ warn("文字列" + index.GetStrValue(null) + "は変数" + varTerm.Identifier.Name + "の要素ではありません", line, 2, false); return null; }
				}
				if (terms.Length > 3 && !varTerm.Identifier.IsArray1D)
					warn("第４引数以降は1次元配列以外では無視されます", line, 1, false);
				if (term.GetEraType() != varTerm.GetEraType())
				{
					warn("２つの引数の型が一致していません", line, 2, false);
					return null;
				}
				return new SpCVarSetArgument(varTerm, index, term, term4, term5);
			}
		}

		private sealed class SP_BUTTON_ArgumentBuilder : ArgumentBuilder
		{
			public SP_BUTTON_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.String, EraType.Void };
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				return new SpButtonArgument(terms[0], terms[1]);
			}
		}

		private sealed class SP_COLOR_ArgumentBuilder : ArgumentBuilder
		{
			public SP_COLOR_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
				minArg = 1;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
                if (terms.Length == 2)
                { warn("SETCOLORの引数の数が不正です(SETCOLORの引数は1個もしくは3個です)", line, 2, false); return null; }
                SpColorArgument arg;
                if (terms.Length == 1)
                {
                    arg = new SpColorArgument(terms[0]);
                    if (terms[0] is SingleTerm)
                    {
                        arg.ConstInt = terms[0].GetIntValue(exm);
                        arg.IsConst = true;
                    }
                }
                else
                {
                    arg = new SpColorArgument(terms[0], terms[1], terms[2]);
                    if ((terms[0] is SingleTerm) && (terms[1] is SingleTerm) && (terms[2] is SingleTerm))
                    {
                        arg.ConstInt = (terms[0].GetIntValue(exm) << 16) + (terms[1].GetIntValue(exm) << 8) + (terms[2].GetIntValue(exm));
                        arg.IsConst = true;
                    }
                }
                return arg;
			}
		}

		private sealed class SP_COLOR_ALPHA_ArgumentBuilder : ArgumentBuilder
		{
			public SP_COLOR_ALPHA_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
				minArg = 2;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (terms != null && terms.Length > 2)
				{
					warn("引数が多すぎます", line, 2, false);
					return null;
				}
				if (!checkArgumentType(line, exm, terms))
					return null;
				return new SpColorAlphaArgument(terms[0], terms[1]);
			}
		}

		private sealed class SP_SPLIT_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SPLIT_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String, EraType.Integer };
				minArg = 3;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm x = getChangeableVariable(terms, 3, line);
				if (x == null)
					return null;
				if (!x.Identifier.IsArray1D && !x.Identifier.IsArray2D && !x.Identifier.IsArray3D)
				{ warn("第３引数は配列変数でなければなりません", line, 2, false); return null; }
                VariableTerm term = (terms.Length >= 4) ? getChangeableVariable(terms, 4, line) : new VariableTerm(GlobalStatic.VariableData.GetSystemVariableToken("RESULT"), new IOperandTerm[]{new SingleTerm(0)});
				return new SpSplitArgument(terms[0], terms[1], x.Identifier, term);
			}
		}
		
		private sealed class SP_HTMLSPLIT_ArgumentBuilder : ArgumentBuilder
		{
			public SP_HTMLSPLIT_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.Integer };
				minArg = 1;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableToken destVar;
				VariableTerm destVarTerm = null;
				VariableTerm term = null;
				if (terms.Length >= 2)
					destVarTerm = getChangeableVariable(terms, 2, line);
				if (destVarTerm != null)
					destVar = destVarTerm.Identifier;
				else
					destVar = GlobalStatic.VariableData.GetSystemVariableToken("RESULTS");
				if (!destVar.IsArray1D || destVar.IsCharacterData)
				{ warn("第２引数は非キャラ型の1次元配列変数でなければなりません", line, 2, false); return null; }
				if (terms.Length >= 3)
					term = getChangeableVariable(terms, 3, line);
				if (term == null)
				{
                    VariableToken varToken = GlobalStatic.VariableData.GetSystemVariableToken("RESULT");
                    term = new VariableTerm(varToken, new IOperandTerm[] { new SingleTerm(0) });
				}
				return new SpHtmlSplitArgument(terms[0], destVar, term);
			}
		}

		private sealed class SP_SETBGIMAGE_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SETBGIMAGE_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.String, EraType.Integer, EraType.Integer };
				minArg = 1;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				List<IOperandTerm> termList = new List<IOperandTerm>();
				LexicalAnalyzer.SkipHalfSpace(st);
				if (st.EOS)
				{
					warn("引数が設定されていません", line, 2, false);
					return null;
				}

				// SETBGIMAGE 的第 1 参数是 v24 资源名语义，title 这类裸词
				// 必须按图片名处理，不能进入普通表达式后被当成变量标识符。
				StrFormWord nameWord = LexicalAnalyzer.AnalyseFormattedString(st, FormStrEndWith.Comma, false);
				termList.Add(ExpressionParser.ToStrFormTerm(nameWord));
				if (!st.EOS)
				{
					st.ShiftNext();
					LexicalAnalyzer.SkipHalfSpace(st);
					if (!st.EOS)
					{
						WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
						termList.AddRange(ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false));
					}
				}

				IOperandTerm[] terms = termList.ToArray();
				if (!checkArgumentType(line, exm, terms))
					return null;

				return new SpSetBgImageArgument(
					terms[0],
					terms.Length > 1 ? terms[1] : null,
					terms.Length > 2 ? terms[2] : null);
			}
		}

		private sealed class SP_SETIMAGELAYERL_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SETIMAGELAYERL_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[]
				{
					EraType.String, EraType.Integer, EraType.Integer, EraType.Integer,
					EraType.Integer, EraType.Integer, EraType.Integer, EraType.Void
				};
				minArg = 2;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (terms == null)
				{
					warn("引数がありません", line, 2, false);
					return null;
				}
				if (terms.Length < minArg)
				{
					warn("引数が足りません", line, 2, false);
					return null;
				}
				if (terms[0] == null || terms[0].GetEraType() != EraType.String)
				{
					warn("第1引数の型が正しくありません", line, 2, false);
					return null;
				}
				if (terms[1] == null || terms[1].GetEraType() != EraType.Integer)
				{
					warn("第2引数の型が正しくありません", line, 2, false);
					return null;
				}

				return new SpSetImageLayerArgument(
					terms[0],
					terms[1],
					terms.Length > 2 ? terms[2] : null,
					terms.Length > 3 ? terms[3] : null,
					terms.Length > 4 ? terms[4] : null,
					terms.Length > 5 ? terms[5] : null,
					terms.Length > 6 ? terms[6] : null,
					terms.Length > 7 ? terms[7] : null,
					null);
			}
		}

		private sealed class SP_GETINT_ArgumentBuilder : ArgumentBuilder
		{
			public SP_GETINT_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer };
				minArg = 0;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (terms.Length == 0)
				{
					VariableToken varToken = GlobalStatic.VariableData.GetSystemVariableToken("RESULT");
					return new SpGetIntArgument(new VariableTerm(varToken, new IOperandTerm[]{new SingleTerm(0)}));
				}
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm x = getChangeableVariable(terms, 1, line);
				if (x == null)
					return null;
				return new SpGetIntArgument(x);
			}
		}

		private sealed class SP_CONTROL_ARRAY_ArgumentBuilder : ArgumentBuilder
		{
			public SP_CONTROL_ARRAY_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Void, EraType.Integer, EraType.Integer };
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				VariableTerm x = getChangeableVariable(terms, 1, line);
				if (x == null)
					return null;
				return new SpArrayControlArgument(x, terms[1], terms[2]);
			}
		}

		private sealed class SP_SHIFT_ARRAY_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SHIFT_ARRAY_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Void, EraType.Integer, EraType.Void, EraType.Integer, EraType.Integer };
				minArg = 3;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;

				VariableTerm x = getChangeableVariable(terms, 1, line);
				if (x == null)
					return null;
				if (!x.Identifier.IsArray1D)
				{ warn("第１引数に１次元配列もしくは配列型キャラクタ変数以外を指定することはできません", line, 2, false); return null; }

				if (line.FunctionCode == FunctionCode.ARRAYSHIFT)
				{
					if (terms[0].GetEraType() != terms[2].GetEraType())
					{ warn("第１引数と第３引数の型が違います", line, 2, false); return null; }
				}
				IOperandTerm term4 = terms.Length >= 4 ? terms[3] : new SingleTerm(0);
				IOperandTerm term5 = terms.Length >= 5 ? terms[4] : null;
				return new SpArrayShiftArgument(x, terms[1], terms[2], term4, term5);
			}
		}

		private sealed class SP_SAVEVAR_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SAVEVAR_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.Void};
				argAny = true;
				minArg = 3;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;
				List<VariableToken> varTokens = new List<VariableToken>();
				for (int i = 2; i < terms.Length; i++)
				{
					if (terms[i] == null)
					{ warn("第" + (i + 1) + "引数を省略できません", line, 2, false); return null; }
					VariableTerm vTerm = getChangeableVariable(terms, i + 1, line);
					if (vTerm == null)
						return null;
					VariableToken vToken = vTerm.Identifier;
					if (vToken.IsCharacterData)
					{ warn("キャラクタ変数"+ vToken.Name+"はセーブできません(キャラクタ変数のSAVEにはSAVECHARAを使用します)", line, 2, false); return null; }
					if (vToken.IsPrivate)
					{ warn("プライベート変数" + vToken.Name + "はセーブできません", line, 2, false); return null; }
					if (vToken.IsLocal)
					{ warn("ローカル変数" + vToken.Name + "はセーブできません", line, 2, false); return null; }
					if (vToken.IsConst)
					{ warn("値を変更できない変数はセーブできません", line, 2, false); return null; }
					if (vToken.IsCalc)
					{ warn("疑似変数はセーブできません", line, 2, false); return null; }
					if (vToken.IsReference)
					{ warn("参照型変数はセーブできません", line, 2, false); return null; }
					varTokens.Add(vToken);
				}
				for (int i = 0; i < varTokens.Count; i++)
				{
					for (int j = i + 1; j < varTokens.Count; j++)
						if (varTokens[i] == varTokens[j])
						{
							warn("変数" + varTokens[i].Name + "を二度以上保存しようとしています", line, 1, false);
							return null;
						}
				}
				VariableToken[] arg3 = new VariableToken[varTokens.Count];
				varTokens.CopyTo(arg3);
				return new SpSaveVarArgument(terms[0], terms[1], arg3);
			}
		}

		private sealed class SP_SAVECHARA_ArgumentBuilder : ArgumentBuilder
		{
			public SP_SAVECHARA_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.Integer };
				minArg = 3;
				argAny = true;
			}
			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				if (!checkArgumentType(line, exm, terms))
					return null;

				List<IOperandTerm> termList = new List<IOperandTerm>();
				termList.AddRange(terms);
				ExpressionArrayArgument ret = new ExpressionArrayArgument(termList);

				for (int i = 2; i < termList.Count; i++)
				{
					if (!(termList[i] is SingleTerm))
						continue;
					Int64 iValue = termList[i].GetIntValue(null);
					if (iValue < 0)
					{ warn("キャラ登録番号は正の値でなければなりません", line, 2, false); return null; }
					if (iValue > Int32.MaxValue)
					{ warn("キャラ登録番号が32bit符号付整数の上限を超えています", line, 2, false); return null; }
					for (int j = i + 1; j < termList.Count; j++)
					{
						if (!(termList[j] is SingleTerm))
							continue;
						if (iValue == termList[j].GetIntValue(null))
						{
							warn("キャラ登録番号" + iValue.ToString() + "を二度以上保存しようとしています", line, 1, false);
							return null;
						}
					}
				}
				return ret;
			}
		}

		private sealed class SP_REF_ArgumentBuilder : ArgumentBuilder
		{
			public SP_REF_ArgumentBuilder(bool byname)
			{
				argumentTypeArray = new EraType[] { EraType.Void, EraType.Void };
				minArg = 2;
				this.byname = byname;
			}

            readonly bool byname;

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				WordCollection wc = popWords(line);
                wc.ShiftNext();
                if (!(wc.Current is IdentifierWord id) || wc.Current.Type != ',')
				{ warn("書式が間違っています", line, 2, false); return null; }
				wc.ShiftNext();
				IOperandTerm name = null;
                string srcCode = null;
				if (byname)
				{
					name = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
					if (name == null || name.IsInteger || !wc.EOL)
					{ warn("書式が間違っています", line, 2, false); return null; }
					name = name.Restructure(exm);
					if (name is SingleTerm)
						srcCode = name.GetStrValue(exm);
				}
				else
				{
                    wc.ShiftNext();
                    if (!(wc.Current is IdentifierWord id2) || !wc.EOL)
					{ warn("書式が間違っています", line, 2, false); return null; }
					srcCode = id2.Code;
				}
				UserDefinedRefMethod refm = GlobalStatic.IdentifierDictionary.GetRefMethod(id.Code);
				ReferenceToken refVar = null;
				if (refm == null)
				{
					VariableToken token = GlobalStatic.IdentifierDictionary.GetVariableToken(id.Code, null, true);
					if (token == null || !token.IsReference)
					{ warn("第一引数は関数参照か参照型変数でなければなりません", line, 2, false); return null; }
					refVar = (ReferenceToken)token;
				}

				if (refm != null)
				{
					if (srcCode == null)
						return new RefArgument(refm, name);
					UserDefinedRefMethod srcRef = GlobalStatic.IdentifierDictionary.GetRefMethod(srcCode);
					if (srcRef != null)
					{
						return new RefArgument(refm, srcRef);
					}
					FunctionLabelLine label = GlobalStatic.LabelDictionary.GetNonEventLabel(srcCode);
					if (label == null)
					{ warn("式中関数" + srcCode + "が見つかりません", line, 2, false); return null; }
					if (!label.IsMethod)
					{ warn("#FUNCTION(S)属性を持たない関数" + srcCode + "は参照できません", line, 2, false); return null; }
					CalledFunction called = CalledFunction.CreateCalledFunctionMethod(label, label.LabelName);
					return new RefArgument(refm, called);
				}
				else
				{
					if (srcCode == null)
						return new RefArgument(refVar, name);
					VariableToken srcVar = GlobalStatic.IdentifierDictionary.GetVariableToken(srcCode, null, true);
					if (srcVar == null)
					{ warn("変数" + srcCode + "が見つかりません", line, 2, false); return null; }
					return new RefArgument(refVar, srcVar);
				}
			}
		}

        private sealed class SP_INPUT_ArgumentBuilder : ArgumentBuilder
        {
            public SP_INPUT_ArgumentBuilder()
            {
                argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
                //if (nullable)妥協
                minArg = 0;
            }
            public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
            {
                IOperandTerm[] terms = popTerms(line);
                // 参考（EM_私家版_INPUT系機能拡張＆ONEINPUT系制限解除）：活动代码不走
                // checkArgumentType（其 null-省略与多余参数判定会误拒/误警），手工循环
                // 只查"非 null 且非整型"——null 表示省略位，越界参数静默丢弃。
                for (int i = 0; i < terms.Length; i++)
                {
                    if (terms[i] != null && terms[i].GetEraType() != EraType.Integer)
                    {
                        warn("第" + (i + 1) + "引数の型が違います", line, 2, false);
                        return null;
                    }
                }
                if (terms.Length == 0)
                    return new SpInputsArgument(null, null, null);
                if (terms.Length == 1)
                    return new SpInputsArgument(terms[0], null, null);
                if (terms.Length == 2)
                    return new SpInputsArgument(terms[0], terms[1], null);
                return new SpInputsArgument(terms[0], terms[1], terms[2]);
            }
        }

        private sealed class SP_COPY_ARRAY_Arguments : ArgumentBuilder
        {
            public SP_COPY_ARRAY_Arguments()
            {
                argumentTypeArray = new EraType[] { EraType.String, EraType.String };
                minArg = 2;
            }

            public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
            {
                IOperandTerm[] terms = popTerms(line);
                if (!checkArgumentType(line, exm, terms))
                    return null;
                VariableToken[] vars = new VariableToken[2] { null, null };
                if (terms[0] is SingleTerm term)
                {
                    if ((vars[0] = GlobalStatic.IdentifierDictionary.GetVariableToken(term.Str, null, true)) == null)
                    {
                        warn("ARRAYCOPY命令の第１引数\"" + term.Str + "\"は変数名として存在しません", line, 2, false);
                        return null;
                    }
                    if (!vars[0].IsArray1D && !vars[0].IsArray2D && !vars[0].IsArray3D)
                    {
                        warn("ARRAYCOPY命令の第１引数\"" + term.Str + "\"は配列変数ではありません", line, 2, false);
                        return null;
                    }
                    if (vars[0].IsCharacterData)
                    {
                        warn("ARRAYCOPY命令の第１引数\"" + term.Str + "\"はキャラクタ変数です（対応していません）", line, 2, false);
                        return null;
                    }
                }
                if (terms[1] is SingleTerm term1)
                {
                    if ((vars[1] = GlobalStatic.IdentifierDictionary.GetVariableToken(term1.Str, null, true)) == null)
                    {
                        warn("ARRAYCOPY命令の第２引数\"" + term1.Str + "\"は変数名として存在しません", line, 2, false);
                        return null;
                    }
                    if (!vars[1].IsArray1D && !vars[1].IsArray2D && !vars[1].IsArray3D)
                    {
                        warn("ARRAYCOPY命令の第２引数\"" + term1.Str + "\"は配列変数ではありません", line, 2, false);
                    }
                    if (vars[1].IsCharacterData)
                    {
                        warn("ARRAYCOPY命令の第２引数\"" + term1.Str + "\"はキャラクタ変数です（対応していません）", line, 2, false);
                        return null;
                    }
                    if (vars[1].IsConst)
                    {
                        warn("ARRAYCOPY命令の第２引数\"" + term1.Str + "\"は値を変更できない変数です", line, 2, false);
                        return null;
                    }
                }
                if ((vars[0] != null) && (vars[1] != null))
                {
                    if ((vars[0].IsArray1D && !vars[1].IsArray1D) || (vars[0].IsArray2D && !vars[1].IsArray2D) || (vars[0].IsArray3D && !vars[1].IsArray3D))
                    {
                        warn("ARRAYCOPY命令の2つの引数の次元が異なります", line, 2, false);
                        return null;
                    }
                    if ((vars[0].IsInteger && vars[1].IsString) || (vars[0].IsString && vars[1].IsInteger))
                    {
                        warn("ARRAYCOPY命令の２つの配列変数の型が一致していません", line, 2, false);
                        return null;
                    }
                }
                return new SpCopyArrayArgument(terms[0], terms[1]);
            }
        }
        #endregion		
	}
}
