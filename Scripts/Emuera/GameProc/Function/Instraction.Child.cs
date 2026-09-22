using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData;
using MinorShift._Library;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.Content;
using MinorShift.Emuera.GameView;
//using System.Drawing;
using System.IO;
using uEmuera.Drawing;

namespace MinorShift.Emuera.GameProc.Function
{
	internal sealed partial class FunctionIdentifier
	{
		#region normalFunction
		private sealed class METHOD_Instruction : AbstractInstruction
		{
			public METHOD_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.METHOD);
				flag = METHOD_SAFE | EXTENDED;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				IOperandTerm term = ((MethodArgument)func.Argument).MethodTerm;
				if (term.GetEraType() == EraType.Integer)
					exm.VEvaluator.RESULT = term.GetIntValue(exm);
				else if (term.GetEraType() == EraType.Float)
					exm.VEvaluator.RESULTF = term.GetFloatValue(exm);
				else
					exm.VEvaluator.RESULTS = term.GetStrValue(exm);
			}
		}

		/// <summary>
		/// 代入文
		/// </summary>
		private sealed class SET_Instruction : AbstractInstruction
		{
			public SET_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SET);
				flag = METHOD_SAFE;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (func.Argument is SpSetArrayArgument)
				{
					SpSetArrayArgument arg = (SpSetArrayArgument)func.Argument;
					if (arg.VariableDest.IsInteger)
					{
						if (arg.IsConst)
							arg.VariableDest.SetValue(arg.ConstIntList, exm);
						else
						{
							Int64[] values = new Int64[arg.TermList.Length];
							for (int i = 0; i < values.Length; i++)
							{
								values[i] = arg.TermList[i].GetIntValue(exm);
							}
							arg.VariableDest.SetValue(values, exm);
						}
					}
					else if (arg.VariableDest.IsFloat)
					{
						if (arg.IsConst)
							arg.VariableDest.SetValue(arg.ConstFloatList, exm);
						else
						{
							double[] values = new double[arg.TermList.Length];
							for (int i = 0; i < values.Length; i++)
							{
								values[i] = arg.TermList[i].GetFloatValue(exm);
							}
							arg.VariableDest.SetValue(values, exm);
						}
					}
					else
					{
						if (arg.IsConst)
							arg.VariableDest.SetValue(arg.ConstStrList, exm);
						else
						{
							string[] values = new string[arg.TermList.Length];
							for (int i = 0; i < values.Length; i++)
							{
								values[i] = arg.TermList[i].GetStrValue(exm);
							}
							arg.VariableDest.SetValue(values, exm);
						}
					}
					return;
				}
				SpSetArgument spsetarg = (SpSetArgument)func.Argument;
				if (spsetarg.VariableDest.IsInteger)
				{
					Int64 src = spsetarg.IsConst ? spsetarg.ConstInt : spsetarg.Term.GetIntValue(exm);
					if (spsetarg.AddConst)
						spsetarg.VariableDest.PlusValue(src, exm);
					else
						spsetarg.VariableDest.SetValue(src, exm);
				}
				else if (spsetarg.VariableDest.IsFloat)
				{
					double src = spsetarg.IsConst ? spsetarg.ConstFloat : spsetarg.Term.GetFloatValue(exm);
					if (spsetarg.AddConst)
						spsetarg.VariableDest.SetValue(spsetarg.VariableDest.GetFloatValue(exm) + src, exm);
					else
						spsetarg.VariableDest.SetValue(src, exm);
				}
				else
				{
					string src = spsetarg.IsConst ? spsetarg.ConstStr : spsetarg.Term.GetStrValue(exm);
					spsetarg.VariableDest.SetValue(src, exm);
				}
			}
		}

		private sealed class REUSELASTLINE_Instruction : AbstractInstruction
		{
			public REUSELASTLINE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
				flag = METHOD_SAFE | EXTENDED | IS_PRINT;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				IOperandTerm term = ((ExpressionArgument)func.Argument).Term;
				string str = term.GetStrValue(exm);
				exm.Console.PrintTemporaryLine(str);
			}
		}

		private sealed class CLEARLINE_Instruction : AbstractInstruction
		{
			public CLEARLINE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED | IS_PRINT;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument intExpArg = (ExpressionArgument)func.Argument;
				Int32 delNum = (Int32)intExpArg.Term.GetIntValue(exm);
				exm.Console.deleteLine(delNum);
				exm.Console.RefreshStrings(false);
			}
		}

		private sealed class STRLEN_Instruction : AbstractInstruction
		{
			public STRLEN_Instruction(bool argisform, bool unicode)
			{
				if (argisform)
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
				else
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_NULLABLE);
				flag = METHOD_SAFE | EXTENDED;
				this.unicode = unicode;
			}
			bool unicode;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				string str;
				if (func.Argument.IsConst)
					str = func.Argument.ConstStr;
				else
					str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
				if (unicode)
					exm.VEvaluator.RESULT = str.Length;
				else
					exm.VEvaluator.RESULT = LangManager.GetStrlenLang(str);
			}
		}

		private sealed class SETBIT_Instruction : AbstractInstruction
		{
			public SETBIT_Instruction(int op)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.BIT_ARG);
				flag = METHOD_SAFE | EXTENDED;
				this.op = op;
			}
			int op;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				BitArgument spsetarg = (BitArgument)func.Argument;
				VariableTerm varTerm = spsetarg.VariableDest;
				IOperandTerm[] terms = spsetarg.Term;
				for (int i = 0; i < terms.Length; i++)
				{
					Int64 x = terms[i].GetIntValue(exm);
					if ((x < 0) || (x > 63))
						throw new CodeEE("第2引数がビットのレンジ(0から63)を超えています");
					Int64 baseValue = varTerm.GetIntValue(exm);
					Int64 shift = 1L << (int)x;
					if (op == 1)
						baseValue |= shift;
					else if (op == 0)
						baseValue &= ~shift;
					else
						baseValue ^= shift;
					varTerm.SetValue(baseValue, exm);
				}
			}
		}


		private sealed class CALLF_Instruction : AbstractInstruction
		{
			public CALLF_Instruction(bool form)
				: this(form, false)
			{
			}

			public CALLF_Instruction(bool form, bool isTry)
			{
				if (form)
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLFORMF);
				else
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLF);
				flag = EXTENDED | METHOD_SAFE | FORCE_SETARG;
				if (isTry)
					flag |= IS_TRY;
				this.isTry = isTry;
			}
			readonly bool isTry;

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				if (!func.Argument.IsConst)
				{
					useCallForm = true;
					return;
				}
				SpCallFArgment callfArg = (SpCallFArgment)func.Argument;
				// megaten 门控（P1）：CALLF 常量函数名查询大小写跟随 ICVariable；Disabled 下与仅 ICFunction 等价。
				if (Config.ICFunction || (Config.ICVariable && Program.Compatibility.Megaten.UsesVariableCaseForFunctionLabelLookup))
					callfArg.ConstStr = callfArg.ConstStr.ToUpper();
				try
				{
					callfArg.FuncTerm = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, callfArg.ConstStr, callfArg.RowArgs, true);
				}
				catch (Exception) when (isTry)
				{
					// TRYCALLF/TRYCALLFORMF 在预解析阶段必须和 snake/v24 一样静默失败，
					// 否则带有无效签名或懒加载解析异常的可选函数会提前产生警告或中断加载。
					return;
				}
				catch (CodeEE e)
				{
					ParserMediator.Warn(e.Message, func, 2, true, false);
					return;
				}
				if (callfArg.FuncTerm == null)
				{
					if (!isTry)
					{
						if (!Program.AnalysisMode)
							ParserMediator.Warn("指定された関数名\"@" + callfArg.ConstStr + "\"は存在しません", func, 2, true, false);
						else
							ParserMediator.Warn(callfArg.ConstStr, func, 2, true, false);
					}
					return;
				}
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				IOperandTerm mToken;
				string labelName;
				if ((!func.Argument.IsConst) || (exm.Console.RunERBFromMemory))
				{
					SpCallFArgment spCallformArg = (SpCallFArgment)func.Argument;
					labelName = spCallformArg.FuncnameTerm.GetStrValue(exm);
					mToken = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, labelName, spCallformArg.RowArgs, true);
				}
				else
				{
					labelName = func.Argument.ConstStr;
					mToken = ((SpCallFArgment)func.Argument).FuncTerm;
				}
				if (mToken == null)
				{
					if (isTry)
						return;
					throw new CodeEE("式中関数\"@" + labelName + "\"が見つかりません");
				}
				mToken.GetValue(exm);
			}
		}

		private sealed class BAR_Instruction : AbstractInstruction
		{
			public BAR_Instruction(bool newline)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_BAR);
				flag = IS_PRINT | METHOD_SAFE | EXTENDED;
				this.newline = newline;
			}
			bool newline;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpBarArgument barArg = (SpBarArgument)func.Argument;
				Int64 var = barArg.Terms[0].GetIntValue(exm);
				Int64 max = barArg.Terms[1].GetIntValue(exm);
				Int64 length = barArg.Terms[2].GetIntValue(exm);
				exm.Console.Print(exm.CreateBar(var, max, length));
				if (newline)
					exm.Console.NewLine();
			}
		}
		
		private sealed class TIMES_Instruction : AbstractInstruction
		{
			public TIMES_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TIMES);
				flag = METHOD_SAFE;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpTimesArgument timesArg = (SpTimesArgument)func.Argument;
				VariableTerm var = timesArg.VariableDest;
				double multiplier = timesArg.Multiplier.IsFloat
					? timesArg.Multiplier.GetFloatValue(exm)
					: (double)timesArg.Multiplier.GetIntValue(exm);
				if (var.IsFloat)
				{
					var.SetValue(var.GetFloatValue(exm) * multiplier, exm);
				}
				else if (Program.Compatibility.Snake.IsEnabled)
				{
					// snake 参考：checked + 溢出告警 + 钳位（skia fork 有意行为）
					if (Config.TimesNotRigorousCalculation)
					{
						double d = (double)var.GetIntValue(exm) * multiplier;
						try
						{
							checked { var.SetValue((Int64)d, exm); }
						}
						catch (OverflowException)
						{
							GlobalStatic.EMediator.Console.PrintWarning($"TIMES整数溢出: {d}", null, 1);
							var.SetValue(d > 0 ? Int64.MaxValue : Int64.MinValue, exm);
						}
					}
					else
					{
						decimal d = var.GetIntValue(exm) * (decimal)multiplier;
						if (d <= Int64.MaxValue && d >= Int64.MinValue)
							var.SetValue((Int64)d, exm);
						else
						{
							GlobalStatic.EMediator.Console.PrintWarning($"TIMES整数溢出: {d}", null, 1);
							var.SetValue(d > 0 ? Int64.MaxValue : Int64.MinValue, exm);
						}
					}
				}
				else if (Config.TimesNotRigorousCalculation)
				{
					// v24 参考：unchecked 回绕，无警告
					double d = (double)var.GetIntValue(exm) * multiplier;
					unchecked
					{
						var.SetValue((Int64)d, exm);
					}
				}
				else
				{
					decimal d = var.GetIntValue(exm) * (decimal)multiplier;
					unchecked
					{
						//decimal型は強制的にOverFlowExceptionを投げるので対策が必要
						//OverFlowの場合は昔の挙動に近づけてみる
						if (d <= Int64.MaxValue && d >= Int64.MinValue)
							var.SetValue((Int64)d, exm);
						else
							var.SetValue((Int64)(double)d, exm);
					}
				}
			}
		}


		private sealed class ADDCHARA_Instruction : AbstractInstruction
		{
			public ADDCHARA_Instruction(bool flagSp, bool flagDel)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
				flag = METHOD_SAFE;
				isDel = flagDel;
				isSp = flagSp;
			}
			bool isDel;
			bool isSp;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if(!Config.CompatiSPChara && isSp)
					throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
				ExpressionArrayArgument intExpArg = (ExpressionArrayArgument)func.Argument;
				Int64 integer;
				Int64[] charaNoList = new Int64[intExpArg.TermList.Length];
				int i = 0;
				foreach (IOperandTerm int64Term in intExpArg.TermList)
				{
					integer = int64Term.GetIntValue(exm);
					if (isDel)
					{
						charaNoList[i] = integer;
						i++;
					}
					else
					{
						if(Config.CompatiSPChara)
							exm.VEvaluator.AddCharacter_UseSp(integer, isSp);
						else
							exm.VEvaluator.AddCharacter(integer);
					}
				}
				if (isDel)
				{
					if(charaNoList.Length == 1)
						exm.VEvaluator.DelCharacter(charaNoList[0]);
					else
						exm.VEvaluator.DelCharacter(charaNoList);
				}
			}
		}

		private sealed class ADDVOIDCHARA_Instruction : AbstractInstruction
		{
			public ADDVOIDCHARA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.VEvaluator.AddPseudoCharacter();
			}
		}

		private sealed class SWAPCHARA_Instruction : AbstractInstruction
		{
			public SWAPCHARA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
				long x = arg.X.GetIntValue(exm);
				long y = arg.Y.GetIntValue(exm);
				exm.VEvaluator.SwapChara(x, y);
			}
		}
		private sealed class COPYCHARA_Instruction : AbstractInstruction
		{
			public COPYCHARA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
				long x = arg.X.GetIntValue(exm);
				long y = arg.Y.GetIntValue(exm);
				exm.VEvaluator.CopyChara(x, y);
			}
		}

		private sealed class ADDCOPYCHARA_Instruction : AbstractInstruction
		{
			public ADDCOPYCHARA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
				flag = METHOD_SAFE;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument intExpArg = (ExpressionArrayArgument)func.Argument;
				foreach (IOperandTerm int64Term in intExpArg.TermList)
					exm.VEvaluator.AddCopyChara(int64Term.GetIntValue(exm));
			}
		}

		private sealed class SORTCHARA_Instruction : AbstractInstruction
		{
			public SORTCHARA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SORTCHARA);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpSortcharaArgument spSortArg = (SpSortcharaArgument)func.Argument;
				Int64 elem = 0;
				VariableTerm sortKey = spSortArg.SortKey;
				if (sortKey.Identifier.IsArray1D)
					elem = sortKey.GetElementInt(1, exm);
				else if (sortKey.Identifier.IsArray2D)
				{
					elem = sortKey.GetElementInt(1, exm) << 32;
					elem += sortKey.GetElementInt(2, exm);
				}

				exm.VEvaluator.SortChara(sortKey.Identifier, elem, spSortArg.SortOrder, true);
			}
		}

		private sealed class RESETCOLOR_Instruction : AbstractInstruction
		{
			public RESETCOLOR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.SetStringStyle(Config.ForeColor);
			}
		}

		private sealed class RESETBGCOLOR_Instruction : AbstractInstruction
		{
			public RESETBGCOLOR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.SetBgColor(Config.BackColor);
			}
		}

		private sealed class FONTBOLD_Instruction : AbstractInstruction
		{
			public FONTBOLD_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.SetStringStyle(exm.Console.StringStyle.FontStyle | FontStyle.Bold);
			}
		}
		private sealed class FONTITALIC_Instruction : AbstractInstruction
		{
			public FONTITALIC_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.SetStringStyle(exm.Console.StringStyle.FontStyle | FontStyle.Italic);
			}
		}
		private sealed class FONTREGULAR_Instruction : AbstractInstruction
		{
			public FONTREGULAR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.SetStringStyle(FontStyle.Regular);
			}
		}


		private sealed class SNAKE_COMPAT_NOOP_Instruction : AbstractInstruction
		{
			public SNAKE_COMPAT_NOOP_Instruction()
				: this(METHOD_SAFE | EXTENDED)
			{
			}

			public SNAKE_COMPAT_NOOP_Instruction(int instFlag)
			{
				ArgBuilder = RawArgBuilder.Instance;
				flag = instFlag;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
			}

			public sealed class RawArgBuilder : ArgumentBuilder
			{
				public static readonly RawArgBuilder Instance = new RawArgBuilder();

				public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
				{
					line.PopArgumentPrimitive();
					return new VoidArgument();
				}
			}
		}

		private sealed class SNAKE_ARGS_ArgumentBuilder : ArgumentBuilder
		{
			public static readonly SNAKE_ARGS_ArgumentBuilder Instance = new SNAKE_ARGS_ArgumentBuilder();

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				IOperandTerm[] terms = popTerms(line);
				List<IOperandTerm> list = new List<IOperandTerm>(terms.Length);
				for (int i = 0; i < terms.Length; i++)
				{
					if (terms[i] != null)
						terms[i] = terms[i].Restructure(exm);
					list.Add(terms[i]);
				}
				return new ExpressionArrayArgument(list);
			}
		}

		private sealed class SNAKE_SETIMAGELAYER_ArgumentBuilder : ArgumentBuilder
		{
			public static readonly SNAKE_SETIMAGELAYER_ArgumentBuilder Instance = new SNAKE_SETIMAGELAYER_ArgumentBuilder();

			private SNAKE_SETIMAGELAYER_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[]
				{
					EraType.String, EraType.Integer, EraType.Integer, EraType.Integer,
					EraType.Integer, EraType.Integer, EraType.Integer, EraType.Void, EraType.Integer,
				};
				minArg = 2;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream stream = line.PopArgumentPrimitive();
				WordCollection words = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.None);
				IOperandTerm[] terms = ExpressionParser.ReduceArguments(words, ArgsEndWith.EoL, false);
				var list = new List<IOperandTerm>(terms.Length);
				for (int i = 0; i < terms.Length; i++)
					list.Add(terms[i]?.Restructure(exm));
				return new ExpressionArrayArgument(list);
			}
		}

		private sealed class SNAKE_SKIA_ArgumentBuilder : ArgumentBuilder
		{
			public static readonly SNAKE_SKIA_ArgumentBuilder Instance = new SNAKE_SKIA_ArgumentBuilder();

			private SNAKE_SKIA_ArgumentBuilder()
			{
				argumentTypeArray = new EraType[] { EraType.Integer };
				minArg = 0;
				argAny = true;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream stream = line.PopArgumentPrimitive();
				WordCollection words = LexicalAnalyzer.Analyse(stream, LexEndWith.EoL, LexAnalyzeFlag.None);
				IOperandTerm[] terms = ExpressionParser.ReduceArguments(words, ArgsEndWith.EoL, false);
				var list = new List<IOperandTerm>(terms.Length);
				for (int i = 0; i < terms.Length; i++)
					list.Add(terms[i]?.Restructure(exm));
				return new ExpressionArrayArgument(list);
			}
		}

		private sealed class SNAKE_TEXT_BGC_ON_Instruction : AbstractInstruction
		{
			public SNAKE_TEXT_BGC_ON_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_COLOR_ALPHA);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpColorAlphaArgument arg = (SpColorAlphaArgument)func.Argument;
				long rgb = arg.RGB.GetIntValue(exm);
				long alphaPercent = arg.Alpha.GetIntValue(exm);
				if (rgb < 0 || rgb > 0xFFFFFF)
					throw new CodeEE("TEXT_BGC_ONの第１引数が色を表す整数の範囲外です");
				if (alphaPercent < 0 || alphaPercent > 100)
					throw new CodeEE("TEXT_BGC_ONの第２引数が透明度の範囲外です");
				int a = (int)(alphaPercent * 255 / 100);
				exm.Console.TextBackgroundColor = Color.FromArgb(a, (int)(rgb >> 16) & 0xFF, (int)(rgb >> 8) & 0xFF, (int)rgb & 0xFF);
			}
		}

		private sealed class SNAKE_TEXT_BGC_OFF_Instruction : AbstractInstruction
		{
			public SNAKE_TEXT_BGC_OFF_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.TextBackgroundColor = null;
			}
		}

		private sealed class SNAKE_HTML_PRINT_ArgumentBuilder : ArgumentBuilder
		{
			public static readonly SNAKE_HTML_PRINT_ArgumentBuilder Instance = new SNAKE_HTML_PRINT_ArgumentBuilder();

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.AnalyzePrintV);
				IOperandTerm[] terms = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
				if (terms.Length < 1 || terms.Length > 2)
				{
					warn(terms.Length < 1 ? "引数が足りません" : "引数が多すぎます", line, 2, false);
					return null;
				}
				if (terms[0] == null || terms[0].GetEraType() != EraType.String)
				{
					warn("第１引数が文字列ではありません", line, 2, false);
					return null;
				}
				if (terms.Length > 1 && terms[1] != null && terms[1].GetEraType() != EraType.Integer)
				{
					warn("第２引数が数値ではありません", line, 2, false);
					return null;
				}
				List<IOperandTerm> list = new List<IOperandTerm>(terms.Length);
				for (int i = 0; i < terms.Length; i++)
					list.Add(terms[i] == null ? null : terms[i].Restructure(exm));
				return new ExpressionArrayArgument(list);
			}
		}

		private sealed class SNAKE_CALLSHARP_ArgumentBuilder : ArgumentBuilder
		{
			public static readonly SNAKE_CALLSHARP_ArgumentBuilder Instance = new SNAKE_CALLSHARP_ArgumentBuilder();

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				string str = LexicalAnalyzer.ReadString(st, StrEndWith.LeftParenthesis_Bracket_Comma_Semicolon);
				str = str.Trim(new char[] { ' ', '\t' });
				IOperandTerm funcname = new SingleTerm(str);
				char cur = st.Current;
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
				wc.ShiftNext();

				IOperandTerm[] subNames = null;
				IOperandTerm[] args = null;
				if (cur == '[')
				{
					subNames = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightBracket, false);
					if (!wc.EOL)
					{
						if (wc.Current.Type != '(')
							wc.ShiftNext();
						args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightParenthesis, false);
					}
				}
				if ((cur == '(') || (cur == ','))
				{
					if (cur == '(')
						args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightParenthesis, false);
					else
						args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
					if (!wc.EOL)
					{
						warn("書式が間違っています", line, 2, false);
						return null;
					}
				}
				if (subNames == null)
					subNames = new IOperandTerm[0];
				if (args == null)
					args = new IOperandTerm[0];
				for (int i = 0; i < subNames.Length; i++)
					if (subNames[i] != null)
						subNames[i] = subNames[i].Restructure(exm);
				for (int i = 0; i < args.Length; i++)
					if (args[i] != null)
						args[i] = args[i].Restructure(exm);

				SpCallSharpArgment ret = new SpCallSharpArgment(funcname, subNames, args);
				if (funcname is SingleTerm)
				{
					ret.IsConst = true;
					ret.ConstStr = funcname.GetStrValue(null);
					if (ret.ConstStr == "")
					{
						warn("関数名が指定されていません", line, 2, false);
						return null;
					}
				}
				return ret;
			}
		}

		private sealed class SNAKE_CALLSHARP_Instruction : AbstractInstruction
		{
			public SNAKE_CALLSHARP_Instruction()
			{
				ArgBuilder = SNAKE_CALLSHARP_ArgumentBuilder.Instance;
				flag = EXTENDED | METHOD_SAFE | FORCE_SETARG;
			}

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				if (!func.Argument.IsConst)
				{
					useCallForm = true;
					return;
				}
				SpCallSharpArgment arg = (SpCallSharpArgment)func.Argument;
				MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginManager manager = MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginManager.GetInstance();
				if (!manager.HasMethod(arg.ConstStr))
				{
					ParserMediator.Warn("No native method " + arg.ConstStr + " found", func, 2, true, false);
					return;
				}
				arg.CallFunc = manager.GetMethod(arg.ConstStr);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpCallSharpArgment arg = (SpCallSharpArgment)func.Argument;
				MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginManager manager = MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginManager.GetInstance();
				MinorShift.Emuera.Runtime.Utils.PluginSystem.IPluginMethod method = arg.CallFunc;
				string methodName = arg.ConstStr;
				if (method == null)
				{
					methodName = arg.FuncnameTerm.GetStrValue(exm);
					if (!manager.HasMethod(methodName))
						throw new CodeEE("No native method " + methodName + " found");
					method = manager.GetMethod(methodName);
				}

				MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginMethodParameter[] pluginArgs = new MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginMethodParameter[arg.RowArgs.Length];
				for (int i = 0; i < arg.RowArgs.Length; i++)
				{
					if (arg.RowArgs[i] == null)
						pluginArgs[i] = new MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginMethodParameter(0L);
					else
						pluginArgs[i] = MinorShift.Emuera.Runtime.Utils.PluginSystem.PluginMethodParameterBuilder.ConvertTerm(arg.RowArgs[i], exm);
				}
				method.Execute(pluginArgs);
				for (int i = 0; i < arg.RowArgs.Length; i++)
				{
					if (arg.RowArgs[i] is VariableTerm varTerm)
					{
						switch (varTerm.GetEraType())
						{
							case EraType.String:
								varTerm.SetValue(pluginArgs[i].strValue, exm);
								break;
							case EraType.Float:
								varTerm.SetValue(pluginArgs[i].floatValue, exm);
								break;
							default:
								varTerm.SetValue(pluginArgs[i].intValue, exm);
								break;
						}
					}
				}
			}
		}

		private sealed class SNAKE_SETBGIMAGE_Instruction : AbstractInstruction
		{
			public SNAKE_SETBGIMAGE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SETBGIMAGE);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpSetBgImageArgument arg = (SpSetBgImageArgument)func.Argument;
				string name = arg.Name.GetStrValue(exm);
				long depth = arg.Depth != null ? arg.Depth.GetIntValue(exm) : 0;
				float opacity = arg.Opacity != null ? arg.Opacity.GetIntValue(exm) / 255.0f : 1.0f;
				exm.Console.AddBackgroundImage(name, depth, opacity);
			}
		}

		private sealed class V24_SETBGIMAGE_Instruction : AbstractInstruction
		{
			public V24_SETBGIMAGE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_ANY);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				string name = arg.TermList[0].GetStrValue(exm);
				long depth = arg.TermList.Length >= 2
					? long.Parse(arg.TermList[1].GetStrValue(exm))
					: 0;
				float opacity = arg.TermList.Length >= 3
					? long.Parse(arg.TermList[2].GetStrValue(exm)) / 255.0f
					: 1.0f;
				exm.Console.AddBackgroundImage(name, depth, opacity);
			}
		}

		private sealed class SNAKE_CLEARBGIMAGE_Instruction : AbstractInstruction
		{
			public SNAKE_CLEARBGIMAGE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.ClearBackgroundImage();
			}
		}

		private sealed class SNAKE_REMOVEBGIMAGE_Instruction : AbstractInstruction
		{
			public SNAKE_REMOVEBGIMAGE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_ANY);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				if (arg.TermList.Length == 0 || arg.TermList[0] == null)
					return;
				exm.Console.RemoveBackground(arg.TermList[0].GetStrValue(exm));
			}
		}

		private sealed class SETIMAGELAYER_Instruction : AbstractInstruction
		{
			public SETIMAGELAYER_Instruction()
			{
				ArgBuilder = SNAKE_SETIMAGELAYER_ArgumentBuilder.Instance;
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				if (arg.TermList.Length < 2 || arg.TermList[0] == null || arg.TermList[1] == null)
					return;
				string name = arg.TermList[0].GetStrValue(exm);
				long depth = arg.TermList[1].GetIntValue(exm);
				int x = getOptionalInt(arg, exm, 2, 0);
				int y = getOptionalInt(arg, exm, 3, 0);
				int width = getOptionalInt(arg, exm, 4, 0);
				int height = getOptionalInt(arg, exm, 5, 0);
				int opacity = getOptionalInt(arg, exm, 6, 255);
				float[][] colorMatrix = readOptionalColorMatrix(arg, exm, 7);
				bool followScroll = getOptionalInt(arg, exm, 8, 0) != 0;
				exm.Console.SetImageLayer(name, depth, x, y, width, height, opacity, colorMatrix, followScroll);
			}
		}

		private sealed class SETIMAGELAYERL_Instruction : AbstractInstruction
		{
			public SETIMAGELAYERL_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SETIMAGELAYERL);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpSetImageLayerArgument arg = (SpSetImageLayerArgument)func.Argument;
				string spriteName = arg.SpriteName.GetStrValue(exm);
				long depth = arg.Depth.GetIntValue(exm);

				int xpos = arg.X != null ? (int)arg.X.GetIntValue(exm) : 0;
				int ypos = arg.Y != null ? (int)arg.Y.GetIntValue(exm) : 0;
				int width = arg.Width != null ? (int)arg.Width.GetIntValue(exm) : 0;
				int height = arg.Height != null ? (int)arg.Height.GetIntValue(exm) : 0;
				int opacity = arg.Opacity != null ? (int)arg.Opacity.GetIntValue(exm) : 255;
				float[][] colorMatrix = arg.CMArray != null ? readOptionalColorMatrix(arg.CMArray, exm) : null;

				int lineNo = exm.Console.GetLineNo;
				int imageHeight = height;
				if (imageHeight <= 0)
				{
					var sprite = AppContents.GetSprite(spriteName);
					if (sprite != null && sprite.IsCreated)
						imageHeight = sprite.DestBaseSize.Height;
				}

				int lineHeight = Config.LineHeight;
				int pointY = exm.Console.GetLinePointY(lineNo);
				int x = Config.DrawingParam_ShapePositionShift + xpos;
				int y = pointY + lineHeight - exm.Console.ClientHeight + (imageHeight - lineHeight) + ypos;

				exm.Console.SetImageLayer(spriteName, depth, x, y, width, height, opacity, colorMatrix, true);
			}
		}

		private sealed class CLEARIMAGELAYER_Instruction : AbstractInstruction
		{
			public CLEARIMAGELAYER_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				exm.Console.ClearImageLayer(arg.Term.GetIntValue(exm));
			}
		}

		private sealed class CLEARIMAGELAYER_ALL_Instruction : AbstractInstruction
		{
			public CLEARIMAGELAYER_ALL_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.ClearImageLayerAll();
			}
		}

		private sealed class SNAKE_PLAYSOUND_Instruction : AbstractInstruction
		{
			public SNAKE_PLAYSOUND_Instruction()
			{
				ArgBuilder = SNAKE_ARGS_ArgumentBuilder.Instance;
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				if (arg.TermList.Length == 0 || arg.TermList[0] == null)
					return;
				string path = global::GenericUtils.ResolveSoundPath(arg.TermList[0].GetStrValue(exm));
				int repeat = 1;
				if (arg.TermList.Length > 1 && arg.TermList[1] != null)
					repeat = (int)Math.Max(arg.TermList[1].GetIntValue(exm), 1);
				global::GenericUtils.PlaySoundFile(path, repeat);
			}
		}

		private sealed class SNAKE_STOPSOUND_Instruction : AbstractInstruction
		{
			public SNAKE_STOPSOUND_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				global::GenericUtils.StopSounds();
			}
		}

		private sealed class SNAKE_PLAYBGM_Instruction : AbstractInstruction
		{
			public SNAKE_PLAYBGM_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				string name = arg.IsConst ? arg.ConstStr : arg.Term.GetStrValue(exm);
				global::GenericUtils.PlayBgmFile(global::GenericUtils.ResolveSoundPath(name));
			}
		}

		private sealed class SNAKE_STOPBGM_Instruction : AbstractInstruction
		{
			public SNAKE_STOPBGM_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				global::GenericUtils.StopBgm();
			}
		}

		private sealed class SNAKE_SETVOLUME_Instruction : AbstractInstruction
		{
			public SNAKE_SETVOLUME_Instruction(bool bgm)
			{
				this.bgm = bgm;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			readonly bool bgm;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				int volume = (int)arg.Term.GetIntValue(exm);
				if (bgm)
					global::GenericUtils.SetBgmVolume(volume);
				else
					global::GenericUtils.SetSoundVolume(volume);
			}
		}

		private sealed class SNAKE_HTML_PRINTC_Instruction : AbstractInstruction
		{
			public SNAKE_HTML_PRINTC_Instruction(bool alignRight)
			{
				this.alignRight = alignRight;
				ArgBuilder = SNAKE_HTML_PRINT_ArgumentBuilder.Instance;
				flag = IS_PRINT | METHOD_SAFE | EXTENDED;
			}

			readonly bool alignRight;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (GlobalStatic.Process.SkipPrint)
					return;
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				if (arg.TermList.Length == 0 || arg.TermList[0] == null)
					return;
				string html = arg.TermList[0].GetStrValue(exm);
				int cellWidth = getOptionalInt(arg, exm, 1, 0);
				exm.Console.PrintHtmlC(html, alignRight, cellWidth);
			}
		}

		private sealed class SNAKE_HTML_PRINT_ISLAND_Instruction : AbstractInstruction
		{
			public SNAKE_HTML_PRINT_ISLAND_Instruction()
			{
				ArgBuilder = SNAKE_HTML_PRINT_ArgumentBuilder.Instance;
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (GlobalStatic.Process.SkipPrint)
					return;
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				if (arg.TermList.Length == 0 || arg.TermList[0] == null)
					return;
				exm.Console.PrintHTMLIsland(arg.TermList[0].GetStrValue(exm));
			}
		}

		private sealed class SNAKE_HTML_PRINT_ISLAND_CLEAR_Instruction : AbstractInstruction
		{
			public SNAKE_HTML_PRINT_ISLAND_CLEAR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.ClearHTMLIsland();
			}
		}

		private sealed class SNAKE_UPDATECHECK_Instruction : AbstractInstruction
		{
			public SNAKE_UPDATECHECK_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (Config.ForbidUpdateCheck)
				{
					exm.VEvaluator.RESULT = 4;
					return;
				}

				try
				{
					if (!NetworkInterface.GetIsNetworkAvailable())
					{
						exm.VEvaluator.RESULT = 5;
						return;
					}
				}
				catch
				{
					// 一部 Android 環境では NetworkInterface が例外を投げるため、
					// ここでは通信本体の失敗判定に任せる。
				}

				string url = GlobalStatic.GameBaseData == null ? "" : GlobalStatic.GameBaseData.UpdateCheckURL;
				if (string.IsNullOrWhiteSpace(url))
				{
					exm.VEvaluator.RESULT = 3;
					return;
				}

				try
				{
					using HttpClient client = new HttpClient();
					client.Timeout = TimeSpan.FromSeconds(5);
					string text = client.GetStringAsync(url).GetAwaiter().GetResult();
					using StringReader reader = new StringReader(text);
					string version = reader.ReadLine();
					string link = reader.ReadLine();
					// 参考侧空白判定为 link == null || link == ""（版本行同样）
					if ((version == null || version.Length == 0) || (link == null || link.Length == 0))
					{
						exm.VEvaluator.RESULT = 3;
						return;
					}
					exm.VEvaluator.RESULT = string.Equals(version, GlobalStatic.GameBaseData.VersionName, StringComparison.Ordinal) ? 0 : 1;
				}
				catch
				{
					exm.VEvaluator.RESULT = 3;
				}
			}
		}

		private sealed class SETANIMETIMER_Instruction : AbstractInstruction
		{
			public SETANIMETIMER_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				long value = arg.IsConst ? arg.ConstInt : arg.Term.GetIntValue(exm);
				if (value < int.MinValue || value > short.MaxValue)
					throw new CodeEE("SETANIMETIMERの第１引数が範囲外です");
				exm.Console.setRedrawTimer((int)value);
			}
		}

		private sealed class SNAKE_UI_SETTING_Instruction : AbstractInstruction
		{
			public SNAKE_UI_SETTING_Instruction(FunctionCode code)
			{
				this.code = code;
				ArgBuilder = code == FunctionCode.SET_SKIA_QUALITY ? SNAKE_SKIA_ArgumentBuilder.Instance : ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			readonly FunctionCode code;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (code == FunctionCode.SET_SKIA_QUALITY)
				{
					ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
					exm.Console.SetSnakeSkiaQuality(
						getOptionalInt(arg, exm, 0, exm.Console.SnakeImageQuality),
						getOptionalInt(arg, exm, 1, exm.Console.SnakeFontHinting),
						getOptionalInt(arg, exm, 2, exm.Console.SnakeFontEdging));
					return;
				}
				ExpressionArgument exp = (ExpressionArgument)func.Argument;
				long value = exp.IsConst ? exp.ConstInt : exp.Term.GetIntValue(exm);
				switch (code)
				{
					case FunctionCode.STRICT_FONT_FALLBACK:
						exm.Console.StrictFontFallback = value != 0;
						break;
					case FunctionCode.SET_TEXT_DRAWING_MODE:
						exm.Console.SetSnakeTextDrawingMode((int)value);
						break;
					case FunctionCode.BITMAP_CACHE_ENABLE:
						exm.Console.BitmapCacheEnabledForNextLine = value != 0;
						break;
				}
			}
		}

		private sealed class SNAKE_SKIPLOG_Instruction : AbstractInstruction
		{
			public SNAKE_SKIPLOG_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				long value = arg.IsConst ? arg.ConstInt : arg.Term.GetIntValue(exm);
				exm.Console.MesSkip = value != 0;
			}
		}

		private sealed class BREAKBUTTON_Instruction : AbstractInstruction
		{
			public BREAKBUTTON_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.EXPRESSION_NULLABLE);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.forceUpdateGeneration();
			}
		}

		private sealed class SNAKE_DT_COLUMN_OPTIONS_Instruction : AbstractInstruction
		{
			public SNAKE_DT_COLUMN_OPTIONS_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_DT_COLUMN_OPTIONS);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpDtColumnOptionsArgument arg = (SpDtColumnOptionsArgument)func.Argument;
				string key = arg.DataTable.GetStrValue(exm) ?? "";
				if (!RuntimeDataStore.DataTables.TryGetValue(key, out DataTable table))
				{
					exm.VEvaluator.RESULT = -1;
					return;
				}
				string columnName = arg.Column.GetStrValue(exm) ?? "";
				if (!table.Columns.Contains(columnName))
				{
					exm.VEvaluator.RESULT = 0;
					return;
				}

				DataColumn column = table.Columns[columnName];
				for (int i = 0; i < arg.Options.Length; i++)
				{
					switch (arg.Options[i])
					{
						case SpDtColumnOptionsArgument.OptionType.Default:
							column.DefaultValue = ConvertDataTableDefaultValue(arg.Values[i], column, exm, key);
							break;
					}
				}
				// 参考侧成功路径不写 RESULT（保持旧值）；失败路径维持 -1/0。
			}
		}

		private static object ConvertDataTableDefaultValue(IOperandTerm value, DataColumn column, ExpressionMediator exm, string tableKey)
		{
			if (value == null)
				return DBNull.Value;
			if (column.DataType == typeof(string))
			{
				if (!value.IsString)
					throw new CodeEE("DT_COLUMN_OPTIONSのDEFAULT値の型が列の型と一致しません: " + tableKey + "." + column.ColumnName);
				return value.GetStrValue(exm) ?? "";
			}
			if (column.DataType == typeof(double))
			{
				if (value.IsString)
					throw new CodeEE("DT_COLUMN_OPTIONSのDEFAULT値の型が列の型と一致しません: " + tableKey + "." + column.ColumnName);
				return value.GetFloatValue(exm);
			}
			if (!value.IsInteger)
				throw new CodeEE("DT_COLUMN_OPTIONSのDEFAULT値の型が列の型と一致しません: " + tableKey + "." + column.ColumnName);
			long intValue = value.GetIntValue(exm);
			if (column.DataType == typeof(sbyte))
				return (sbyte)Math.Min(Math.Max(intValue, sbyte.MinValue), sbyte.MaxValue);
			if (column.DataType == typeof(short))
				return (short)Math.Min(Math.Max(intValue, short.MinValue), short.MaxValue);
			if (column.DataType == typeof(int))
				return (int)Math.Min(Math.Max(intValue, int.MinValue), int.MaxValue);
			return intValue;
		}

		private sealed class SNAKE_VARI_Instruction : AbstractInstruction
		{
			public SNAKE_VARI_Instruction(bool isString)
			{
				this.isString = isString;
				ArgBuilder = new SNAKE_VARI_ArgumentBuilder(isString);
				flag = METHOD_SAFE | EXTENDED | FORCE_SETARG;
			}

			readonly bool isString;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (isString)
				{
					SnakeVarsArgument arg = (SnakeVarsArgument)func.Argument;
					UserDefinedVariableToken token = func.ParentLabelLine.GetPrivateVariable(arg.Name);
					if (token != null)
					{
						token.In();
						if (token.GetLength(0) == 1)
							token.SetValue(arg.InitialValue ?? "", new long[] { 0 });
					}
					return;
				}
				SnakeVariArgument iarg = (SnakeVariArgument)func.Argument;
				UserDefinedVariableToken itoken = func.ParentLabelLine.GetPrivateVariable(iarg.Name);
				if (itoken != null)
				{
					itoken.In();
					if (itoken.GetLength(0) == 1)
						itoken.SetValue(iarg.InitialValue == null ? 0 : iarg.InitialValue.GetIntValue(exm), new long[] { 0 });
				}
			}
		}

		private sealed class SNAKE_VARI_ArgumentBuilder : ArgumentBuilder
		{
			public SNAKE_VARI_ArgumentBuilder(bool isString)
			{
				this.isString = isString;
			}

			readonly bool isString;

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				string statement = st == null ? "" : st.Substring();
				if (!UserDefinedVariableData.TryCreateSnakeDynamic(statement, isString, out UserDefinedVariableData varData, out string right, out string errorMessage))
				{
					warn(errorMessage, line, 2, false);
					return null;
				}
				if (line.ParentLabelLine != null)
					line.ParentLabelLine.AddPrivateVariable(varData);
				if (isString)
					return new SnakeVarsArgument(varData.Name, parseStringInitialValue(right));
				IOperandTerm initial = null;
				if (!string.IsNullOrWhiteSpace(right) && varData.Lengths.Length == 1 && varData.Lengths[0] == 1)
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(right), LexEndWith.EoL, LexAnalyzeFlag.None);
					initial = ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL);
					if (initial != null)
						initial = initial.Restructure(exm);
				}
				return new SnakeVariArgument(varData.Name, initial ?? new SingleTerm(0));
			}

			static string parseStringInitialValue(string right)
			{
				if (string.IsNullOrWhiteSpace(right))
					return null;
				int start = right.IndexOf('"');
				int end = right.LastIndexOf('"');
				if (start >= 0 && end > start)
					return right.Substring(start + 1, end - start - 1);
				return right.Trim();
			}
		}

		private sealed class SNAKE_TOOLTIP_SETFONT_Instruction : AbstractInstruction
		{
			public SNAKE_TOOLTIP_SETFONT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
				// 参考（v24 与 snake 均同）：TOOLTIP 系不设 METHOD_SAFE（#FUNCTION 体内使用应在加载期报错）
				flag = EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				exm.Console.SetToolTipFontName(arg.IsConst ? arg.ConstStr : arg.Term.GetStrValue(exm));
			}
		}

		private sealed class SNAKE_TOOLTIP_INT_Instruction : AbstractInstruction
		{
			public SNAKE_TOOLTIP_INT_Instruction(FunctionCode code)
			{
				this.code = code;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				// 参考（v24 与 snake 均同）：TOOLTIP 系不设 METHOD_SAFE
				flag = EXTENDED;
			}

			readonly FunctionCode code;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				long value = arg.IsConst ? arg.ConstInt : arg.Term.GetIntValue(exm);
				switch (code)
				{
					case FunctionCode.TOOLTIP_SETFONTSIZE:
						exm.Console.SetToolTipFontSize(value);
						break;
					case FunctionCode.TOOLTIP_CUSTOM:
						exm.Console.CustomToolTip(value != 0);
						break;
					case FunctionCode.TOOLTIP_FORMAT:
						exm.Console.SetToolTipFormat(value);
						break;
					case FunctionCode.TOOLTIP_IMG:
						exm.Console.SetToolTipImg(value != 0);
						break;
				}
			}
		}

		private static MixedNum getMixedNum(MixedIntegerExprTerm[] parameters, ExpressionMediator exm, int index)
		{
			if (parameters == null || index < 0 || index >= parameters.Length)
				return 0;
			MixedIntegerExprTerm parameter = parameters[index];
			return new MixedNum
			{
				num = (int)parameter.Num.GetIntValue(exm),
				isPx = parameter.IsPx,
			};
		}

		private static MixedNum[] getMixedNums(MixedIntegerExprTerm[] parameters, ExpressionMediator exm)
		{
			if (parameters == null || parameters.Length == 0)
				return Array.Empty<MixedNum>();
			var result = new MixedNum[parameters.Length];
			for (int index = 0; index < parameters.Length; index++)
				result[index] = getMixedNum(parameters, exm, index);
			return result;
		}

		private static int getOptionalInt(ExpressionArrayArgument arg, ExpressionMediator exm, int index, int defaultValue)
		{
			if (arg.TermList.Length <= index || arg.TermList[index] == null)
				return defaultValue;
			return (int)arg.TermList[index].GetIntValue(exm);
		}

		private static float[][] readOptionalColorMatrix(IOperandTerm term, ExpressionMediator exm)
		{
			if (term == null)
				return null;
			return readOptionalColorMatrix(new ExpressionArrayArgument(new List<IOperandTerm> { term }), exm, 0);
		}

		private static float[][] readOptionalColorMatrix(ExpressionArrayArgument arg, ExpressionMediator exm, int index)
		{
			if (arg.TermList.Length <= index || arg.TermList[index] == null)
				return null;
			VariableTerm term = arg.TermList[index] as VariableTerm;
			if (term == null)
				throw new CodeEE("SETIMAGELAYER命令:ColorMatrixには5x5以上の二次元数値型配列変数を指定してください");
			FixedVariableTerm fixedTerm = term.GetFixedVariableTerm(exm);
			if (!fixedTerm.Identifier.IsInteger && !fixedTerm.Identifier.IsFloat)
				throw new CodeEE("SETIMAGELAYER命令:ColorMatrixには数値型配列変数を指定してください");
			if (!fixedTerm.Identifier.IsArray2D && !fixedTerm.Identifier.IsArray3D)
				throw new CodeEE("SETIMAGELAYER命令:ColorMatrixには5x5以上の二次元数値型配列変数を指定してください");
			float[][] matrix = new float[5][];
			for (int i = 0; i < matrix.Length; i++)
				matrix[i] = new float[5];
			if (fixedTerm.Identifier.IsArray2D)
			{
				long row = fixedTerm.Identifier.IsCharacterData ? fixedTerm.Index2 : fixedTerm.Index1;
				long col = fixedTerm.Identifier.IsCharacterData ? fixedTerm.Index3 : fixedTerm.Index2;
				if (row < 0 || col < 0)
					throw new CodeEE("SETIMAGELAYER命令:ColorMatrixの添字が範囲外です");
				if (fixedTerm.Identifier.IsFloat)
				{
					double[,] array = fixedTerm.Identifier.IsCharacterData
						? fixedTerm.Identifier.GetArrayChara((int)fixedTerm.Index1) as double[,]
						: fixedTerm.Identifier.GetArray() as double[,];
					if (array == null || row + 5 > array.GetLength(0) || col + 5 > array.GetLength(1))
						throw new CodeEE("SETIMAGELAYER命令:ColorMatrixが5x5に足りていません");
					for (int x = 0; x < 5; x++)
						for (int y = 0; y < 5; y++)
							matrix[x][y] = (float)array[row + x, col + y];
					return matrix;
				}
				else
				{
					Int64[,] array = fixedTerm.Identifier.IsCharacterData
						? fixedTerm.Identifier.GetArrayChara((int)fixedTerm.Index1) as Int64[,]
						: fixedTerm.Identifier.GetArray() as Int64[,];
					if (array == null || row + 5 > array.GetLength(0) || col + 5 > array.GetLength(1))
						throw new CodeEE("SETIMAGELAYER命令:ColorMatrixが5x5に足りていません");
					for (int x = 0; x < 5; x++)
						for (int y = 0; y < 5; y++)
							matrix[x][y] = ((float)array[row + x, col + y]) / 256f;
					return matrix;
				}
			}
			if (fixedTerm.Identifier.IsCharacterData)
				throw new CodeEE("SETIMAGELAYER命令:キャラ型3次元ColorMatrixは未対応です");
			long layer = fixedTerm.Index1;
			long row3 = fixedTerm.Index2;
			long col3 = fixedTerm.Index3;
			if (layer < 0 || row3 < 0 || col3 < 0)
				throw new CodeEE("SETIMAGELAYER命令:ColorMatrixの添字が範囲外です");
			if (fixedTerm.Identifier.IsFloat)
			{
				double[,,] array = fixedTerm.Identifier.GetArray() as double[,,];
				if (array == null || layer >= array.GetLength(0) || row3 + 5 > array.GetLength(1) || col3 + 5 > array.GetLength(2))
					throw new CodeEE("SETIMAGELAYER命令:ColorMatrixが5x5に足りていません");
				for (int x = 0; x < 5; x++)
					for (int y = 0; y < 5; y++)
						matrix[x][y] = (float)array[layer, row3 + x, col3 + y];
				return matrix;
			}
			else
			{
				Int64[,,] array = fixedTerm.Identifier.GetArray() as Int64[,,];
				if (array == null || layer >= array.GetLength(0) || row3 + 5 > array.GetLength(1) || col3 + 5 > array.GetLength(2))
					throw new CodeEE("SETIMAGELAYER命令:ColorMatrixが5x5に足りていません");
				for (int x = 0; x < 5; x++)
					for (int y = 0; y < 5; y++)
						matrix[x][y] = ((float)array[layer, row3 + x, col3 + y]) / 256f;
				return matrix;
			}
		}

		private static string resolveSoundPath(string filename)
		{
			if (string.IsNullOrEmpty(filename))
				return filename;
			if (Path.IsPathRooted(filename) && File.Exists(filename))
				return filename;
			string[] candidates = new string[]
			{
				Path.Combine(Program.ExeDir ?? "", "sound", filename),
				Path.Combine(Program.ExeDir ?? "", "Sound", filename),
				Path.Combine(Program.ExeDir ?? "", filename),
				filename,
			};
			foreach (string candidate in candidates)
			{
				string resolved = uEmuera.Utils.ResolveExistingFilePath(candidate);
				if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
					return resolved;
			}
			return candidates[0];
		}

		private sealed class REF_Instruction : AbstractInstruction
		{
			public REF_Instruction(bool byname)
			{
				this.byname = byname;
				if (byname)
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_REFBYNAME);
				else
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_REF);

				flag = METHOD_SAFE | EXTENDED;
			}
			bool byname;

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				RefArgument arg = (RefArgument)func.Argument;
				string str = null;
				if (arg.SrcTerm != null)
					str = arg.SrcTerm.GetStrValue(exm);
				if (arg.RefMethodToken != null)
				{
					UserDefinedRefMethod srcRef = arg.SrcRefMethodToken;
					CalledFunction call = arg.SrcCalledFunction;
					if (str != null)//REFBYNAMEかつ第二引数が定数でない
					{
						srcRef = GlobalStatic.IdentifierDictionary.GetRefMethod(str);
						if (srcRef == null)
						{
							FunctionLabelLine label = GlobalStatic.LabelDictionary.GetNonEventLabel(str);
							//if (label == null)
							//    throw new CodeEE("式中関数" + str + "が見つかりません");
							//if (!label.IsMethod)
							//    throw new CodeEE("#FUNCTION(S)属性を持たない関数" + str + "は参照できません");
							if (label != null && label.IsMethod)
								call = CalledFunction.CreateCalledFunctionMethod(label, str);
						}
					}
					else if (srcRef != null)
						call = srcRef.CalledFunction;//第二引数が関数参照。callがnullならエラー
					if (call == null || !arg.RefMethodToken.MatchType(call))
					{
						arg.RefMethodToken.SetReference(null);
						exm.VEvaluator.RESULT = 0;
					}
					else
					{
						arg.RefMethodToken.SetReference(call);
						exm.VEvaluator.RESULT = 1;
					}
					return;
				}

				ReferenceToken refVar = arg.RefVarToken;
				VariableToken srcVar = arg.SrcVarToken;
				string errmes;
				if (str != null)
				{
					srcVar = GlobalStatic.IdentifierDictionary.GetVariableToken(str, null, true);

					//if (srcVar == null)
					//    throw new CodeEE("変数" + str + "が見つかりません");
				}
				if (srcVar == null || !refVar.MatchType(srcVar, false, out errmes))
				{
					refVar.SetRef(null);
					exm.VEvaluator.RESULT = 0;
				}
				else
				{
					if (refVar.Dimension == 0 && srcVar.Dimension == 0)
						refVar.SetScalarRef(srcVar, new Int64[0]);
					else
						refVar.SetRef(srcVar.GetArray());
					exm.VEvaluator.RESULT = 1;
				}
				return;
			}
		}

		private sealed class TOOLTIP_SETCOLOR_Instruction : AbstractInstruction
		{
			public TOOLTIP_SETCOLOR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
				flag = METHOD_SAFE | EXTENDED;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
				long foreColor = arg.X.GetIntValue(exm);
				long backColor = arg.Y.GetIntValue(exm);
				if (foreColor < 0 || foreColor > 0xFFFFFF)
					throw new CodeEE("第１引数が色を表す整数の範囲外です");
				if (backColor < 0 || backColor > 0xFFFFFF)
					throw new CodeEE("第２引数が色を表す整数の範囲外です");
				Color fc = Color.FromArgb((int)foreColor >>16, (int)foreColor>>8 &0xFF,(int)foreColor &0xFF);
				Color bc = Color.FromArgb((int)backColor >>16, (int)backColor>>8 &0xFF,(int)backColor &0xFF);
				exm.Console.SetToolTipColor(fc, bc);
				return;
			}
		}

		private sealed class TOOLTIP_SETDELAY_Instruction : AbstractInstruction
		{
			public TOOLTIP_SETDELAY_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				long delay;
				if(arg.IsConst)
					delay = arg.ConstInt;
				else
					delay = arg.Term.GetIntValue(exm);
				if (delay < 0 || delay > int.MaxValue)
					throw new CodeEE("引数の値が適切な範囲外です");
				exm.Console.SetToolTipDelay((int)delay);
				return;
			}
		}

        private sealed class TOOLTIP_SETDURATION_Instruction : AbstractInstruction
        {
            public TOOLTIP_SETDURATION_Instruction()
            {
                ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
                flag = METHOD_SAFE | EXTENDED;
            }
            public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
            {
                ExpressionArgument arg = (ExpressionArgument)func.Argument;
                long duration;
                if (arg.IsConst)
                    duration = arg.ConstInt;
                else
                    duration = arg.Term.GetIntValue(exm);
                if (duration < 0 || duration > int.MaxValue)
                    throw new CodeEE("引数の値が適切な範囲外です");
                if (duration > short.MaxValue)
                    duration = short.MaxValue;
                exm.Console.SetToolTipDuration((int)duration);
                return;
            }
        }
		
		private sealed class INPUTMOUSEKEY_Instruction : AbstractInstruction
		{
			public INPUTMOUSEKEY_Instruction()
			{
				ArgBuilder = ArgumentParser.GetNormalArgumentBuilder("I", 0);
				//スキップ不可
				//flag = IS_PRINT | IS_INPUT | EXTENDED;
				flag =EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionsArgument arg = (ExpressionsArgument)func.Argument;
				Int64 time = 0;
				if (arg.ArgumentArray.Length > 0)
					time = arg.ArgumentArray[0].GetIntValue(exm);
				InputRequest req = new InputRequest();
				req.InputType = InputType.PrimitiveMouseKey; 
				if (time > 0)
					req.Timelimit = (int)time;
				exm.Console.WaitInput(req);
			}
		}
		
		private sealed class AWAIT_Instruction : AbstractInstruction
		{
			public AWAIT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.EXPRESSION_NULLABLE);
				//スキップ不可
				//flag = IS_PRINT | IS_INPUT | EXTENDED;
				flag = EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				Int64 waittime = -1;
				ExpressionArgument arg = func.Argument as ExpressionArgument;
				if (arg != null && arg.Term != null)
				{
					waittime = arg.Term.GetIntValue(exm);
					if (waittime < 0)
						throw new CodeEE("AWAIT命令:負の値(" + waittime.ToString() + ")が指定されました");
					if (waittime > 10000)
						throw new CodeEE("AWAIT命令:10秒以上の待機時間(" + waittime.ToString() + " ms)が指定されました");
				}

				exm.Console.Await((int)waittime);
			}
		}
        #endregion

	}
}
