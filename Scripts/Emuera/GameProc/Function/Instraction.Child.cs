using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameData;
using MinorShift._Library;
using MinorShift.Emuera.GameData.Function;
//using System.Drawing;
using System.IO;
using uEmuera.Drawing;

namespace MinorShift.Emuera.GameProc.Function
{
	internal sealed partial class FunctionIdentifier
	{
		#region normalFunction
		private sealed class PRINT_Instruction : AbstractInstruction
		{
			bool isLineEnd = true;
			public PRINT_Instruction(string name)
			{
				//PRINT(|V|S|FORM|FORMS)(|K)(|D)(|L|W) コレと
				//PRINTSINGLE(|V|S|FORM|FORMS)(|K)(|D) コレと
				//PRINT(|FORM)(C|LC)(|K)(|D) コレ
				//PRINTDATA(|K)(|D)(|L|W) ←は別クラス
				flag = IS_PRINT;
				StringStream st = new StringStream(name);
				st.Jump(5);//PRINT
				if (st.CurrentEqualTo("SINGLE"))
				{
					flag |= PRINT_SINGLE | EXTENDED;
					st.Jump(6);
				}

				if (st.CurrentEqualTo("V"))
				{
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_PRINTV);
					isPrintV = true;
					st.Jump(1);
				}
				else if (st.CurrentEqualTo("S"))
				{
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
					st.Jump(1);
				}
				else if (st.CurrentEqualTo("FORMS"))
				{
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
					isForms = true;
					st.Jump(5);
				}
				else if (st.CurrentEqualTo("FORM"))
				{
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
					st.Jump(4);
				}
				else
				{
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_NULLABLE);
				}
				if (st.CurrentEqualTo("LC"))
				{
					flag |= EXTENDED;
					isLC = true;
					st.Jump(2);
				}
				else if (st.CurrentEqualTo("C"))
				{
					if (name == "PRINTFORMC")
						flag |= EXTENDED;
					isC = true;
					st.Jump(1);
				}
				if (st.CurrentEqualTo("K"))
				{
					flag |= ISPRINTKFUNC | EXTENDED;
					st.Jump(1);
				}
				if (st.CurrentEqualTo("D"))
				{
					flag |= ISPRINTDFUNC | EXTENDED;
					st.Jump(1);
				}
				if (st.CurrentEqualTo("N"))
				{
					isLineEnd = false;
					flag |= PRINT_WAITINPUT;
					st.Jump(1);
				}
				if (st.CurrentEqualTo("L"))
				{
					flag |= PRINT_NEWLINE;
					flag |= METHOD_SAFE;
					st.Jump(1);
				}
				else if (st.CurrentEqualTo("W"))
				{
					flag |= PRINT_NEWLINE | PRINT_WAITINPUT;
					st.Jump(1);
				}
				else
				{
					flag |= METHOD_SAFE;
				}
				if ((ArgBuilder == null) || (!st.EOS))
					throw new ExeEE("PRINT異常");
			}

			readonly bool isPrintV;
			readonly bool isLC;
			readonly bool isC;
			readonly bool isForms;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
				exm.Console.UseUserStyle = true;
				exm.Console.UseSetColorStyle = !func.Function.IsPrintDFunction();
				string str;
				if (func.Argument.IsConst)
					str = func.Argument.ConstStr;
				else if (isPrintV)
				{
					StringBuilder builder = new StringBuilder();
					IOperandTerm[] terms = ((SpPrintVArgument)func.Argument).Terms;
					foreach (IOperandTerm termV in terms)
					{
						if (termV.GetOperandType() == typeof(Int64))
							builder.Append(termV.GetIntValue(exm).ToString());
						else
							builder.Append(termV.GetStrValue(exm));
					}
					str = builder.ToString();
				}
				else
				{
					str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
					if (isForms)
					{
						str = exm.CheckEscape(str);
						StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new StringStream(str), FormStrEndWith.EoL, false);
						StrForm strForm = StrForm.FromWordToken(wt);
						str = strForm.GetString(exm);
					}
				}
				if (func.Function.IsPrintKFunction())
					str = exm.ConvertStringType(str);
				if (isC)
					exm.Console.PrintC(str, true);
				else if (isLC)
					exm.Console.PrintC(str, false);
				else
					exm.OutputToConsole(str, func.Function, isLineEnd);
				exm.Console.UseSetColorStyle = true;
			}
		}

		private sealed class PRINT_DATA_Instruction : AbstractInstruction
		{
			public PRINT_DATA_Instruction(string name)
			{
				//PRINTDATA(|K)(|D)(|L|W)
				flag = EXTENDED | IS_PRINT | IS_PRINTDATA | PARTIAL;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VAR_INT);
				StringStream st = new StringStream(name);
				st.Jump(9);//PRINTDATA
				if (st.CurrentEqualTo("K"))
				{
					flag |= ISPRINTKFUNC | EXTENDED;
					st.Jump(1);
				}
				if (st.CurrentEqualTo("D"))
				{
					flag |= ISPRINTDFUNC | EXTENDED;
					st.Jump(1);
				}
				if (st.CurrentEqualTo("L"))
				{
					flag |= PRINT_NEWLINE;
					flag |= METHOD_SAFE;
					st.Jump(1);
				}
				else if (st.CurrentEqualTo("W"))
				{
					flag |= PRINT_NEWLINE | PRINT_WAITINPUT;
					st.Jump(1);
				}
				else
				{
					flag |= METHOD_SAFE;
				}
				if ((ArgBuilder == null) || (!st.EOS))
					throw new ExeEE("PRINTDATA異常");
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
                exm.Console.UseUserStyle = true;
				exm.Console.UseSetColorStyle = !func.Function.IsPrintDFunction();
				//表示データが空なら何もしないで飛ぶ
				if (func.dataList.Count == 0)
				{
					state.JumpTo(func.JumpTo);
					return;
				}
				int count = func.dataList.Count;
				int choice = (int)exm.VEvaluator.GetNextRand(count);
				VariableTerm iTerm = ((PrintDataArgument)func.Argument).Var;
				if (iTerm != null)
				{
					iTerm.SetValue(choice, exm);
				}
				List<InstructionLine> iList = func.dataList[choice];
				int i = 0;
				IOperandTerm term;
				string str;
				foreach (InstructionLine selectedLine in iList)
				{
					state.CurrentLine = selectedLine;
					if (selectedLine.Argument == null)
						ArgumentParser.SetArgumentTo(selectedLine);
					term = ((ExpressionArgument)selectedLine.Argument).Term;
					str = term.GetStrValue(exm);
					if (func.Function.IsPrintKFunction())
						str = exm.ConvertStringType(str);
					exm.Console.Print(str);
					if (++i < (int)iList.Count)
						exm.Console.NewLine();
				}
				if (func.Function.IsNewLine() || func.Function.IsWaitInput())
				{
					exm.Console.NewLine();
					if (func.Function.IsWaitInput())
						exm.Console.ReadAnyKey();
				}
				exm.Console.UseSetColorStyle = true;
				//ジャンプするが、流れが連続であることを保証。
				state.JumpTo(func.JumpTo);
				//state.RunningLine = null;
			}
		}
		
		private sealed class HTML_PRINT_Instruction : AbstractInstruction
		{
			public HTML_PRINT_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = SNAKE_HTML_PRINT_ArgumentBuilder.Instance;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
				if (func.Argument == null)
					return;
                string str;
				if (func.Argument is ExpressionArrayArgument snakeArg)
				{
					if (snakeArg.TermList.Length == 0 || snakeArg.TermList[0] == null)
						return;
					str = snakeArg.TermList[0].GetStrValue(exm);
					bool toPrintBuffer = snakeArg.TermList.Length > 1 && snakeArg.TermList[1] != null && snakeArg.TermList[1].GetIntValue(exm) != 0;
					exm.Console.PrintHtml(str, toPrintBuffer);
					return;
				}
				if (func.Argument.IsConst)
					str = func.Argument.ConstStr;
				else
					str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
				exm.Console.PrintHtml(str);
			}
		}

		private sealed class HTML_TAGSPLIT_Instruction : AbstractInstruction
		{
			public HTML_TAGSPLIT_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_HTMLSPLIT);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpHtmlSplitArgument spSplitArg = (SpHtmlSplitArgument)func.Argument;
				string str = spSplitArg.TargetStr.GetStrValue(exm);
				string[] strs = MinorShift.Emuera.GameView.HtmlManager.HtmlTagSplit(str);
				
				if (strs == null)
				{
					spSplitArg.Num.SetValue(-1, exm);
					return;
				}
				
				spSplitArg.Num.SetValue(strs.Length, exm);
				string[] output = (string[])spSplitArg.Var.GetArray();
				int outputlength = Math.Min(output.Length, strs.Length);
				Array.Copy(strs, output, outputlength);
			}
		}
		
		
		private sealed class PRINT_IMG_Instruction : AbstractInstruction
		{
			public PRINT_IMG_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
                string str;
				if (func.Argument.IsConst)
					str = func.Argument.ConstStr;
				else
					str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
				exm.Console.PrintImg(str);
			}
		}

		private sealed class PRINT_RECT_Instruction : AbstractInstruction
		{
			public PRINT_RECT_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
                ExpressionArrayArgument intExpArg = (ExpressionArrayArgument)func.Argument;
				int[] param = new int[intExpArg.TermList.Length];
				for (int i = 0; i < intExpArg.TermList.Length; i++)
					param[i] = FunctionIdentifier.toUInt32inArg(intExpArg.TermList[i].GetIntValue(exm), "PRINT_RECT", i + 1);

				exm.Console.PrintShape("rect", param);
			}
		}

		private sealed class PRINT_SPACE_Instruction : AbstractInstruction
		{
			public PRINT_SPACE_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
                Int64 param;
				if (func.Argument.IsConst)
					param = func.Argument.ConstInt;
				else
					param = ((ExpressionArgument)func.Argument).Term.GetIntValue(exm);
				int param32 = FunctionIdentifier.toUInt32inArg(param, "PRINT_SPACE", 1);
				exm.Console.PrintShape("space", new int[] { param32 });
			}
		}

		private sealed class DEBUGPRINT_Instruction : AbstractInstruction
		{
			public DEBUGPRINT_Instruction(bool form, bool newline)
			{
				if (form)
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_NULLABLE);
				else
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_NULLABLE);
				flag = METHOD_SAFE | EXTENDED | DEBUG_FUNC;
				if (newline)
					flag |= PRINT_NEWLINE;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				string str;
				if (func.Argument.IsConst)
					str = func.Argument.ConstStr;
				else
					str = ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
				exm.Console.DebugPrint(str);
				if (func.Function.IsNewLine())
					exm.Console.DebugNewLine();
			}
		}

		private sealed class DEBUGCLEAR_Instruction : AbstractInstruction
		{
			public DEBUGCLEAR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED | DEBUG_FUNC;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.DebugClear();
			}
		}

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
				//Type type = term.GetOperandType();
				if (term.GetOperandType() == typeof(Int64))
					exm.VEvaluator.RESULT = term.GetIntValue(exm);
				else// if (func.Argument.MethodTerm.GetOperandType() == typeof(string))
					exm.VEvaluator.RESULTS = term.GetStrValue(exm);
				//これら以外の型は現状ない
				//else
				//	throw new ExeEE(func.Function.Name + "命令の型が不明");
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

		private sealed class WAIT_Instruction : AbstractInstruction
		{
			public WAIT_Instruction(bool force)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = IS_PRINT;
				isForce = force;
			}
			bool isForce;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (isForce)
					exm.Console.ReadAnyKey(false, true);
				else
					exm.Console.ReadAnyKey();
			}
		}

		private sealed class WAITANYKEY_Instruction : AbstractInstruction
		{
			public WAITANYKEY_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = IS_PRINT;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.ReadAnyKey(true,false);
			}
		}

		private sealed class INPUTANY_Instruction : AbstractInstruction
		{
			public INPUTANY_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				InputRequest req = new InputRequest();
				req.InputType = InputType.AnyValue;
				exm.Console.WaitInput(req);
			}
		}

		private sealed class TWAIT_Instruction : AbstractInstruction
		{
			public TWAIT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SWAP);
				flag = IS_PRINT | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.Console.ReadAnyKey();
				SpSwapCharaArgument arg = (SpSwapCharaArgument)func.Argument;
				Int64 time = arg.X.GetIntValue(exm);
				Int64 flag = arg.Y.GetIntValue(exm);
				InputRequest req = new InputRequest();
				req.InputType = InputType.EnterKey;
				if (flag != 0)
					req.InputType = InputType.Void;
				req.Timelimit = time;
				exm.Console.WaitInput(req);
			}
		}

		private sealed class INPUT_Instruction : AbstractInstruction
		{
			public INPUT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUT);
				flag = IS_PRINT | IS_INPUT;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntValue;
				if (arg.Term != null)
				{
					Int64 def;
					if (arg.IsConst)
						def = arg.ConstInt;
					else
						def = arg.Term.GetIntValue(exm);
					req.HasDefValue = true;
					req.DefIntValue = def;
				}
				exm.Console.WaitInput(req);
			}
		}
		private sealed class INPUTS_Instruction : AbstractInstruction
		{
			public INPUTS_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUTS);
				flag = IS_PRINT | IS_INPUT;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrValue;
				if (arg.Term != null)
				{
					string def;
					if (arg.IsConst)
						def = arg.ConstStr;
					else
						def = arg.Term.GetStrValue(exm);
					req.HasDefValue = true;
					req.DefStrValue = def;
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class ONEINPUT_Instruction : AbstractInstruction
		{
			public ONEINPUT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUT);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntValue;
				req.OneInput = true;
				if (arg.Term != null)
				{
					//TODO:二文字以上セットできるようにするかエラー停止するか
					//少なくともONETINPUTとの仕様を統一すべき
					Int64 def;
					if (arg.IsConst)
						def = arg.ConstInt;
					else
						def = arg.Term.GetIntValue(exm);
					if (def > 9)
						def = Int64.Parse(def.ToString().Remove(1));
					if (def >= 0)
					{
						req.HasDefValue = true;
						req.DefIntValue = def;
					}
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class ONEINPUTS_Instruction : AbstractInstruction
		{
			public ONEINPUTS_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUTS);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrValue;
				req.OneInput = true;
				if (arg.Term != null)
				{
					string def;
					if (arg.IsConst)
						def = arg.ConstStr;
					else
						def = arg.Term.GetStrValue(exm);
					if (def.Length > 1)
						def = def.Remove(1);
					if (def.Length > 0)
					{
						req.HasDefValue = true;
						req.DefStrValue = def;
					}
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class BINPUT_Instruction : AbstractInstruction
		{
			public BINPUT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUT);
				flag = IS_PRINT | IS_INPUT;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntButton;
				if (arg.Term != null)
				{
					long def = arg.IsConst ? arg.ConstInt : arg.Term.GetIntValue(exm);
					req.HasDefValue = true;
					req.DefIntValue = def;
				}
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (!exm.Console.HasCurrentGenerationButton(true))
				{
					if (!req.HasDefValue)
						throw new CodeEE("BINPUTに対応する数値ボタンがありません");
					exm.VEvaluator.RESULT = req.DefIntValue;
					return;
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class BINPUTS_Instruction : AbstractInstruction
		{
			public BINPUTS_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUTS);
				flag = IS_PRINT | IS_INPUT;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrButton;
				if (arg.Term != null)
				{
					string def = arg.IsConst ? arg.ConstStr : arg.Term.GetStrValue(exm);
					req.HasDefValue = true;
					req.DefStrValue = def;
				}
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (!exm.Console.HasCurrentGenerationButton(false))
				{
					if (!req.HasDefValue)
						throw new CodeEE("BINPUTSに対応するボタンがありません");
					exm.VEvaluator.RESULTS = req.DefStrValue;
					return;
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class ONEBINPUT_Instruction : AbstractInstruction
		{
			public ONEBINPUT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUT);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntButton;
				req.OneInput = true;
				if (arg.Term != null)
				{
					long def = arg.IsConst ? arg.ConstInt : arg.Term.GetIntValue(exm);
					if (def > 9)
						def = Int64.Parse(def.ToString().Remove(1));
					if (def >= 0)
					{
						req.HasDefValue = true;
						req.DefIntValue = def;
					}
				}
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (!exm.Console.HasCurrentGenerationButton(true))
				{
					if (!req.HasDefValue)
						throw new CodeEE("ONEBINPUTに対応する数値ボタンがありません");
					exm.VEvaluator.RESULT = req.DefIntValue;
					return;
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class ONEBINPUTS_Instruction : AbstractInstruction
		{
			public ONEBINPUTS_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUTS);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrButton;
				req.OneInput = true;
				if (arg.Term != null)
				{
					string def = arg.IsConst ? arg.ConstStr : arg.Term.GetStrValue(exm);
					if (def.Length > 1)
						def = def.Remove(1);
					if (def.Length > 0)
					{
						req.HasDefValue = true;
						req.DefStrValue = def;
					}
				}
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (!exm.Console.HasCurrentGenerationButton(false))
				{
					if (!req.HasDefValue)
						throw new CodeEE("ONEBINPUTSに対応するボタンがありません");
					exm.VEvaluator.RESULTS = req.DefStrValue;
					return;
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class TINPUT_Instruction : AbstractInstruction
		{
			public TINPUT_Instruction(bool oneInput)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TINPUT);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
				this.isOne = oneInput;
			}
			bool isOne;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpTInputsArgument tinputarg = (SpTInputsArgument)func.Argument;

				InputRequest req = new InputRequest();
				req.InputType = InputType.IntValue;
				req.HasDefValue = true;
				req.OneInput = isOne;
				Int64 x = tinputarg.Time.GetIntValue(exm);
				Int64 y = tinputarg.Def.GetIntValue(exm);
				//TODO:ONEINPUTと標準の値を統一
				if (isOne)
				{
					if (y < 0)
						y = Math.Abs(y);
					if (y >= 10)
						y = y / (long)(Math.Pow(10.0, Math.Log10((double)y)));
				}
				Int64 z = (tinputarg.Disp != null) ? tinputarg.Disp.GetIntValue(exm) : 1;
				req.Timelimit = x;
				req.DefIntValue = y;
				req.DisplayTime = z != 0;
				req.TimeUpMes = (tinputarg.Timeout != null) ? tinputarg.Timeout.GetStrValue(exm) : Config.TimeupLabel;
				exm.Console.WaitInput(req);
			}
		}

		private sealed class TINPUTS_Instruction : AbstractInstruction
		{
			public TINPUTS_Instruction(bool oneInput)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TINPUTS);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
				this.isOne = oneInput;
			}
			bool isOne;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpTInputsArgument tinputarg = (SpTInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrValue;
				req.HasDefValue = true;
				req.OneInput = isOne;
				Int64 x = tinputarg.Time.GetIntValue(exm);
				string strs = tinputarg.Def.GetStrValue(exm);
				if (isOne && strs.Length > 1)
					strs = strs.Remove(1);
				Int64 z = (tinputarg.Disp != null) ? tinputarg.Disp.GetIntValue(exm) : 1;
				req.Timelimit = x;
				req.DefStrValue = strs;
				req.DisplayTime = z != 0;
				req.TimeUpMes = (tinputarg.Timeout != null) ? tinputarg.Timeout.GetStrValue(exm) : Config.TimeupLabel;
				exm.Console.WaitInput(req);
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
				if (Config.ICFunction)
					callfArg.ConstStr = callfArg.ConstStr.ToUpper();
				try
				{
					callfArg.FuncTerm = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, callfArg.ConstStr, callfArg.RowArgs, true);
				}
				catch (CodeEE e)
				{
					if (!isTry)
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
				if (Config.TimesNotRigorousCalculation)
				{
					double d = (double)var.GetIntValue(exm) * timesArg.DoubleValue;
					try
					{
						checked { var.SetValue((Int64)d, exm); }
					}
					catch (OverflowException)
					{
						GlobalStatic.EMediator.Console.PrintWarning(
							$"TIMES整数溢出: {d}", null, 1);
						var.SetValue(d > 0 ? Int64.MaxValue : Int64.MinValue, exm);
					}
				}
				else
				{
					decimal d = var.GetIntValue(exm) * (decimal)timesArg.DoubleValue;
					if (d <= Int64.MaxValue && d >= Int64.MinValue)
						var.SetValue((Int64)d, exm);
					else
					{
						GlobalStatic.EMediator.Console.PrintWarning(
							$"TIMES整数溢出: {d}", null, 1);
						var.SetValue(d > 0 ? Int64.MaxValue : Int64.MinValue, exm);
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

		private sealed class VARSET_Instruction : AbstractInstruction
		{
			public VARSET_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_VAR_SET);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{

				SpVarSetArgument spvarsetarg = (SpVarSetArgument)func.Argument;
				VariableTerm var = spvarsetarg.VariableDest;
				FixedVariableTerm p = var.GetFixedVariableTerm(exm);
				int start = 0;
				int end = 0;
				//endを先に取って判定の処理変更
				if (spvarsetarg.End != null)
					end = (int)spvarsetarg.End.GetIntValue(exm);
				else if (var.Identifier.IsArray1D)
					end = (int)var.GetLength();
				if (spvarsetarg.Start != null)
				{
					start = (int)spvarsetarg.Start.GetIntValue(exm);
					if (start > end)
					{
						int temp = start;
						start = end;
						end = temp;
					}
				}
				if (var.IsString)
				{
					string src = spvarsetarg.Term.GetStrValue(exm);
					exm.VEvaluator.SetValueAll(p, src, start, end);
				}
				else
				{
					long src = spvarsetarg.Term.GetIntValue(exm);
					exm.VEvaluator.SetValueAll(p, src, start, end);
				}
			}
		}

		private sealed class CVARSET_Instruction : AbstractInstruction
		{
			public CVARSET_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CVAR_SET);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpCVarSetArgument spvarsetarg = (SpCVarSetArgument)func.Argument;
				FixedVariableTerm p = spvarsetarg.VariableDest.GetFixedVariableTerm(exm);
				SingleTerm index = spvarsetarg.Index.GetValue(exm);
				int charaNum = (int)exm.VEvaluator.CHARANUM;
				int start = 0;
				if (spvarsetarg.Start != null)
				{
					start = (int)spvarsetarg.Start.GetIntValue(exm);
					if (start < 0 || start >= charaNum)
						throw new CodeEE("命令CVARSETの第４引数(" + start.ToString() + ")がキャラクタの範囲外です");
				}
				int end;
				if (spvarsetarg.End != null)
				{
					end = (int)spvarsetarg.End.GetIntValue(exm);
					if (end < 0 || end > charaNum)
						throw new CodeEE("命令CVARSETの第５引数(" + end.ToString() + ")がキャラクタの範囲外です");
				}
				else
					end = charaNum;
				if (start > end)
				{
					int temp = start;
					start = end;
					end = temp;
				}
				if (!p.Identifier.IsCharacterData)
					throw new CodeEE("命令CVARSETにキャラクタ変数でない変数" + p.Identifier.Name + "が渡されました");
				if (index.GetOperandType() == typeof(string) && p.Identifier.IsArray1D)
				{
					if (!GlobalStatic.ConstantData.isDefined(p.Identifier.Code, index.Str))
						throw new CodeEE("文字列" + index.Str + "は配列変数" + p.Identifier.Name + "の要素ではありません");
				}
				if (p.Identifier.IsString)
				{
					string src = spvarsetarg.Term.GetStrValue(exm);
					exm.VEvaluator.SetValueAllEachChara(p, index, src, start, end);
				}
				else
				{
					long src = spvarsetarg.Term.GetIntValue(exm);
					exm.VEvaluator.SetValueAllEachChara(p, index, src, start, end);
				}
			}
		}

		private sealed class RANDOMIZE_Instruction : AbstractInstruction
		{
			public RANDOMIZE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION_NULLABLE);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				Int64 iValue;
				if (func.Argument.IsConst)
					iValue = func.Argument.ConstInt;
				else
					iValue = ((ExpressionArgument)func.Argument).Term.GetIntValue(exm);
				exm.VEvaluator.Randomize(iValue);
			}
		}
		private sealed class INITRAND_Instruction : AbstractInstruction
		{
			public INITRAND_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.VEvaluator.InitRanddata();
			}
		}

		private sealed class DUMPRAND_Instruction : AbstractInstruction
		{
			public DUMPRAND_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.VEvaluator.DumpRanddata();
			}
		}


		private sealed class SAVEGLOBAL_Instruction : AbstractInstruction
		{
			public SAVEGLOBAL_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.VEvaluator.SaveGlobal();
			}
		}

		private sealed class LOADGLOBAL_Instruction : AbstractInstruction
		{
			public LOADGLOBAL_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (exm.VEvaluator.LoadGlobal())
					exm.VEvaluator.RESULT = 1;
				else
					exm.VEvaluator.RESULT = 0;
			}
		}

		private sealed class RESETDATA_Instruction : AbstractInstruction
		{
			public RESETDATA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.VEvaluator.ResetData();
				exm.Console.ResetStyle();
			}
		}

		private sealed class RESETGLOBAL_Instruction : AbstractInstruction
		{
			public RESETGLOBAL_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.VEvaluator.ResetGlobalData();
			}
		}

		private static int toUInt32inArg(Int64 value, string funcName, int argnum)
		{
			if (value < 0)
				throw new CodeEE(funcName + "の第" + argnum.ToString() + "引数に負の値(" + value.ToString() + ")が指定されました");
			else if (value > Int32.MaxValue)
				throw new CodeEE(funcName + "の第" + argnum.ToString() + "引数の値(" + value.ToString() + ")が大きすぎます");

			return (int)value;
		}

		private sealed class SAVECHARA_Instruction : AbstractInstruction
		{
			public SAVECHARA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SAVECHARA);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				IOperandTerm[] terms = arg.TermList;
				string datFilename = terms[0].GetStrValue(exm);
				string savMes = terms[1].GetStrValue(exm);
				int[] savCharaList = new int[terms.Length - 2];
				int charanum = (int)exm.VEvaluator.CHARANUM;
				for (int i = 0; i < savCharaList.Length; i++)
				{
					Int64 v = terms[i + 2].GetIntValue(exm);
					savCharaList[i] = FunctionIdentifier.toUInt32inArg(v, "SAVECHARA", i + 3);
					if (savCharaList[i] >= charanum)
						throw new CodeEE("SAVECHARAの第" + (i + 3).ToString() + "引数の値がキャラ登録番号の範囲を超えています");
					for (int j = 0; j < i; j++)
					{
						if (savCharaList[i] == savCharaList[j])
							throw new CodeEE("同一のキャラ登録番号(" + (savCharaList[i]).ToString() + ")が複数回指定されました");
					}
				}
				exm.VEvaluator.SaveChara(datFilename, savMes, savCharaList);
			}
		}

		private sealed class LOADCHARA_Instruction : AbstractInstruction
		{
			public LOADCHARA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				string datFilename;
				if (arg.IsConst)
					datFilename = arg.ConstStr;
				else
					datFilename = arg.Term.GetStrValue(exm);
				exm.VEvaluator.LoadChara(datFilename);
			}
		}


		private sealed class SAVEVAR_Instruction : AbstractInstruction
		{
			public SAVEVAR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_SAVEVAR);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpSaveVarArgument arg = (SpSaveVarArgument)func.Argument;
				VariableToken[] vars = arg.VarTokens;
				string datFilename = arg.Term.GetStrValue(exm);
				string savMes = arg.SavMes.GetStrValue(exm);
				exm.VEvaluator.SaveVariable(datFilename, savMes, vars);
			}
		}
		private sealed class LOADVAR_Instruction : AbstractInstruction
		{
			public LOADVAR_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument arg = (ExpressionArgument)func.Argument;
				string datFilename = null;
				if (arg.IsConst)
					datFilename = arg.ConstStr;
				else
					datFilename = arg.Term.GetStrValue(exm);
				exm.VEvaluator.LoadVariable(datFilename);

			}
		}

		private sealed class DELDATA_Instruction : AbstractInstruction
		{
			public DELDATA_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				Int64 target;
				if (func.Argument.IsConst)
					target = func.Argument.ConstInt;
				else
					target = ((ExpressionArgument)func.Argument).Term.GetIntValue(exm);

				int target32 = FunctionIdentifier.toUInt32inArg(target, "DELDATA", 1);
				exm.VEvaluator.DelData(target32);
			}
		}

		private sealed class DO_NOTHING_Instruction : AbstractInstruction
		{
			public DO_NOTHING_Instruction()
			{
				//事実上ENDIFの非フローコントロール版
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED | PARTIAL;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				//何もしない
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

		private sealed class SNAKE_TEXT_BGC_ON_Instruction : AbstractInstruction
		{
			public SNAKE_TEXT_BGC_ON_Instruction()
			{
				ArgBuilder = SNAKE_ARGS_ArgumentBuilder.Instance;
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				if (arg.TermList.Length == 0 || arg.TermList[0] == null)
					return;
				long rgb = arg.TermList[0].GetIntValue(exm);
				long alphaPercent = arg.TermList.Length > 1 && arg.TermList[1] != null ? arg.TermList[1].GetIntValue(exm) : 100;
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
				IOperandTerm[] terms = popTerms(line);
				if (terms.Length < 1 || terms.Length > 2)
				{
					warn(terms.Length < 1 ? "引数が足りません" : "引数が多すぎます", line, 2, false);
					return null;
				}
				if (terms[0] == null || terms[0].GetOperandType() != typeof(string))
				{
					warn("第１引数が文字列ではありません", line, 2, false);
					return null;
				}
				if (terms.Length > 1 && terms[1] != null && terms[1].GetOperandType() != typeof(Int64))
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
						if (varTerm.GetOperandType() == typeof(string))
							varTerm.SetValue(pluginArgs[i].strValue, exm);
						else
							varTerm.SetValue(pluginArgs[i].intValue, exm);
					}
				}
			}
		}

		private sealed class SNAKE_SETBGIMAGE_Instruction : AbstractInstruction
		{
			public SNAKE_SETBGIMAGE_Instruction()
			{
				ArgBuilder = SNAKE_ARGS_ArgumentBuilder.Instance;
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArrayArgument arg = (ExpressionArrayArgument)func.Argument;
				if (arg.TermList.Length == 0 || arg.TermList[0] == null)
					return;
				string name = arg.TermList[0].GetStrValue(exm);
				long depth = arg.TermList.Length > 1 && arg.TermList[1] != null ? arg.TermList[1].GetIntValue(exm) : 0;
				float opacity = arg.TermList.Length > 2 && arg.TermList[2] != null ? arg.TermList[2].GetIntValue(exm) / 255.0f : 1.0f;
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
				ArgBuilder = SNAKE_ARGS_ArgumentBuilder.Instance;
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
				ArgBuilder = SNAKE_ARGS_ArgumentBuilder.Instance;
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
				exm.VEvaluator.RESULT = 3;
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
				ArgBuilder = code == FunctionCode.SET_SKIA_QUALITY ? SNAKE_ARGS_ArgumentBuilder.Instance : ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
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
						exm.Console.SnakeTextDrawingMode = (int)value;
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

		private sealed class SNAKE_DT_COLUMN_OPTIONS_Instruction : AbstractInstruction
		{
			public SNAKE_DT_COLUMN_OPTIONS_Instruction()
			{
				ArgBuilder = SNAKE_COMPAT_NOOP_Instruction.RawArgBuilder.Instance;
				flag = METHOD_SAFE | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				exm.VEvaluator.RESULT = -1;
			}
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
					if (token != null && token.GetLength(0) == 1)
						token.SetValue(arg.InitialValue ?? "", new long[] { 0 });
					return;
				}
				SnakeVariArgument iarg = (SnakeVariArgument)func.Argument;
				UserDefinedVariableToken itoken = func.ParentLabelLine.GetPrivateVariable(iarg.Name);
				if (itoken != null && itoken.GetLength(0) == 1)
					itoken.SetValue(iarg.InitialValue == null ? 0 : iarg.InitialValue.GetIntValue(exm), new long[] { 0 });
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
				int comment = statement.IndexOf(';');
				if (comment >= 0)
					statement = statement.Substring(0, comment);
				int equal = statement.IndexOf('=');
				string left = equal >= 0 ? statement.Substring(0, equal) : statement;
				string right = equal >= 0 ? statement.Substring(equal + 1) : "";
				string[] leftParts = left.Split(',');
				string name = leftParts[0].Trim();
				if (string.IsNullOrEmpty(name))
				{
					warn("変数名が指定されていません", line, 2, false);
					return null;
				}
				int[] lengths = new int[Math.Max(1, leftParts.Length - 1)];
				if (leftParts.Length == 1)
				{
					lengths[0] = 1;
				}
				else
				{
					for (int i = 1; i < leftParts.Length; i++)
					{
						if (!int.TryParse(leftParts[i].Trim(), out lengths[i - 1]) || lengths[i - 1] <= 0)
						{
							warn("VARI/VARSの配列長が不正です", line, 2, false);
							return null;
						}
					}
				}
				UserDefinedVariableData varData = new UserDefinedVariableData
				{
					Name = name,
					Static = false,
					Lengths = lengths,
					Dimension = lengths.Length,
					TypeIsStr = isString
				};
				if (line.ParentLabelLine != null)
					line.ParentLabelLine.AddPrivateVariable(varData);
				if (isString)
					return new SnakeVarsArgument(name, parseStringInitialValue(right));
				IOperandTerm initial = null;
				if (!string.IsNullOrWhiteSpace(right) && lengths.Length == 1 && lengths[0] == 1)
				{
					WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(right), LexEndWith.EoL, LexAnalyzeFlag.None);
					initial = ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL);
					if (initial != null)
						initial = initial.Restructure(exm);
				}
				return new SnakeVariArgument(name, initial ?? new SingleTerm(0));
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
				flag = METHOD_SAFE | EXTENDED;
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
				flag = METHOD_SAFE | EXTENDED;
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

		private static int getOptionalInt(ExpressionArrayArgument arg, ExpressionMediator exm, int index, int defaultValue)
		{
			if (arg.TermList.Length <= index || arg.TermList[index] == null)
				return defaultValue;
			return (int)arg.TermList[index].GetIntValue(exm);
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
						refVar.SetRef((Array)srcVar.GetArray());
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

        #region flowControlFunction

		private sealed class BEGIN_Instruction : AbstractInstruction
		{
			public BEGIN_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR);
				flag = FLOW_CONTROL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				string keyword = func.Argument.ConstStr;
				if (Config.ICFunction)//1756 BEGINのキーワードは関数扱いらしい
					keyword = keyword.ToUpper();
				state.SetBegin(keyword);
				state.Return(0);
				exm.Console.ResetStyle();
			}
		}

		private sealed class FORCE_BEGIN_Instruction : AbstractInstruction
		{
			public FORCE_BEGIN_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR);
				flag = FLOW_CONTROL | EXTENDED;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				string keyword = func.Argument.ConstStr;
				if (Config.ICFunction)
					keyword = keyword.ToUpper();
				state.SetBegin(keyword, true);
				state.Return(0);
				exm.Console.ResetStyle();
			}
		}

		private sealed class SAVELOADGAME_Instruction : AbstractInstruction
		{
			public SAVELOADGAME_Instruction(bool isSave)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = FLOW_CONTROL;
				this.isSave = isSave;
			}
			readonly bool isSave;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if ((state.SystemState & SystemStateCode.__CAN_SAVE__) != SystemStateCode.__CAN_SAVE__)
				{
					string funcName = state.Scope;
					if (funcName == null)
						funcName = "";
					throw new CodeEE("@" + funcName + "中でSAVEGAME/LOADGAME命令を実行することはできません");
				}
				GlobalStatic.Process.saveCurrentState(true);
				//バックアップに入れた旧ProcessStateの方を参照するため、ここでstateは使えない
				GlobalStatic.Process.getCurrentState.SaveLoadData(isSave);
			}
		}

		private sealed class REPEAT_Instruction : AbstractInstruction
		{
			public REPEAT_Instruction(bool fornext)
			{
				flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL;
				if (fornext)
				{
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_FOR_NEXT);
					flag |= EXTENDED;
				}
				else
				{
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				}
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpForNextArgment forArg = (SpForNextArgment)func.Argument;
				func.LoopCounter = forArg.Cnt;
				//1.725 順序変更。REPEATにならう。
				func.LoopCounter.SetValue(forArg.Start.GetIntValue(exm), exm);
				func.LoopEnd = forArg.End.GetIntValue(exm);
				func.LoopStep = forArg.Step.GetIntValue(exm);
				if ((func.LoopStep > 0) && (func.LoopEnd > func.LoopCounter.GetIntValue(exm)))//まだ回数が残っているなら、
					return;//そのまま次の行へ
				else if ((func.LoopStep < 0) && (func.LoopEnd < func.LoopCounter.GetIntValue(exm)))//まだ回数が残っているなら、
					return;//そのまま次の行へ
				state.JumpTo(func.JumpTo);
			}
		}

		private sealed class WHILE_Instruction : AbstractInstruction
		{
			public WHILE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument expArg = (ExpressionArgument)func.Argument;
				if (expArg.Term.GetIntValue(exm) != 0)//式が真
					return;//そのまま中の処理へ
				state.JumpTo(func.JumpTo);
			}
		}

		private sealed class SIF_Instruction : AbstractInstruction
		{
			public SIF_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
			}

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				LogicalLine jumpto = func.NextLine;
				if ((jumpto == null) || (jumpto.NextLine == null) ||
					(jumpto is FunctionLabelLine) || (jumpto is NullLine))
				{
					ParserMediator.Warn("SIF文の次の行がありません", func, 2, true, false);
					return;
				}
				else if (jumpto is InstructionLine)
				{
					InstructionLine sifFunc = (InstructionLine)jumpto;
					if (sifFunc.Function.IsPartial())
						ParserMediator.Warn("SIF文の次の行を" + sifFunc.Function.Name + "文にすることはできません", func, 2, true, false);
					else
						func.JumpTo = func.NextLine.NextLine;
				}
				else if (jumpto is GotoLabelLine)
					ParserMediator.Warn("SIF文の次の行をラベル行にすることはできません", func, 2, true, false);
				else
					func.JumpTo = func.NextLine.NextLine;

				if ((func.JumpTo != null) && (func.Position.LineNo + 1 != func.NextLine.Position.LineNo))
					ParserMediator.Warn("SIF文の次の行が空行またはコメント行です(eramaker:SIF文は意味を失います)", func, 0, false, true);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument expArg = (ExpressionArgument)func.Argument;
				if (expArg.Term.GetIntValue(exm) == 0)//評価式が真ならそのまま流れ落ちる
					state.ShiftNextLine();//偽なら一行とばす。順に来たときと同じ扱いにする
			}
		}

		private sealed class ELSEIF_Instruction : AbstractInstruction
		{
			public ELSEIF_Instruction(FunctionArgType argtype)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(argtype);
				flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				//if (iFuncCode == FunctionCode.ELSE || iFuncCode == FunctionCode.ELSEIF
				//	|| iFuncCode == FunctionCode.CASE || iFuncCode == FunctionCode.CASEELSE)
				//チェック済み
				//if (func.JumpTo == null)
				//	throw new ExeEE(func.Function.Name + "のジャンプ先が設定されていない");
				state.JumpTo(func.JumpTo);
			}
		}
		private sealed class ENDIF_Instruction : AbstractInstruction
		{
			public ENDIF_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = FLOW_CONTROL | PARTIAL | FORCE_SETARG;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
			}
		}

		private sealed class IF_Instruction : AbstractInstruction
		{
			public IF_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				LogicalLine ifJumpto = func.JumpTo;//ENDIF
				//チェック済み
				//if (func.IfCaseList == null)
				//	throw new ExeEE("IFのIF-ELSEIFリストが適正に作成されていない");
				//if (func.JumpTo == null)
				//	throw new ExeEE("IFに対応するENDIFが設定されていない");

				InstructionLine line;
				for (int i = 0; i < func.IfCaseList.Count; i++)
				{
					line = func.IfCaseList[i];
					if (line.IsError)
						continue;
					if (line.FunctionCode == FunctionCode.ELSE)
					{
						ifJumpto = line;
						break;
					}

					//ExpressionArgument expArg = (ExpressionArgument)(line.Argument);
					//チェック済み
					//if (expArg == null)
					//	throw new ExeEE("IFチェック中。引数が解析されていない。", func.IfCaseList[i].Position);

					//1730 ELSEIFが出したエラーがIFのエラーとして検出されていた
					state.CurrentLine = line;
					Int64 value = ((ExpressionArgument)(line.Argument)).Term.GetIntValue(exm);
					if (value != 0)//式が真
					{
						ifJumpto = line;
						break;
					}
				}
				if (ifJumpto != func)//自分自身がジャンプ先ならそのまま
					state.JumpTo(ifJumpto);
				//state.RunningLine = null;
			}
		}


		private sealed class SELECTCASE_Instruction : AbstractInstruction
		{
			public SELECTCASE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.EXPRESSION);
				flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				LogicalLine caseJumpto = func.JumpTo;//ENDSELECT
				IOperandTerm selectValue = ((ExpressionArgument)func.Argument).Term;
				string sValue = null;
				Int64 iValue = 0;
				if (selectValue.IsInteger)
					iValue = selectValue.GetIntValue(exm);
				else
					sValue = selectValue.GetStrValue(exm);
				//チェック済み
				//if (func.IfCaseList == null)
				//	throw new ExeEE("SELECTCASEのCASEリストが適正に作成されていない");
				//if (func.JumpTo == null)
				//	throw new ExeEE("SELECTCASEに対応するENDSELECTが設定されていない");
				InstructionLine line;
				for (int i = 0; i < func.IfCaseList.Count; i++)
				{
					line = func.IfCaseList[i];
					if (line.IsError)
						continue;
					if (line.FunctionCode == FunctionCode.CASEELSE)
					{
						caseJumpto = line;
						break;
					}
					CaseArgument caseArg = (CaseArgument)(line.Argument);
					//チェック済み
					//if (caseArg == null)
					//	throw new ExeEE("CASEチェック中。引数が解析されていない。", func.IfCaseList[i].Position);

					state.CurrentLine = line;
					if (selectValue.IsInteger)
					{
						Int64 Is = iValue;
						foreach (CaseExpression caseExp in caseArg.CaseExps)
						{
							if (caseExp.GetBool(Is, exm))
							{
								caseJumpto = line;
								goto casefound;
							}
						}
					}
					else
					{
						string Is = sValue;
						foreach (CaseExpression caseExp in caseArg.CaseExps)
						{
							if (caseExp.GetBool(Is, exm))
							{
								caseJumpto = line;
								goto casefound;
							}
						}
					}

				}
			casefound:
				state.JumpTo(caseJumpto);
				//state.RunningLine = null;
			}
		}

		private sealed class RETURNFORM_Instruction : AbstractInstruction
		{
			public RETURNFORM_Instruction()
			{
				//ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR_ANY);
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.FORM_STR);
				flag = EXTENDED | FLOW_CONTROL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				//int termnum = 0;
				//foreach (IOperandTerm term in ((ExpressionArrayArgument)func.Argument).TermList)
				//{
				//    string arg = term.GetStrValue(exm);
				//    StringStream aSt = new StringStream(arg);
				//    WordCollection wc = LexicalAnalyzer.Analyse(aSt, LexEndWith.EoL, LexAnalyzeFlag.None);
				//    exm.VEvaluator.SetResultX((ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL).GetIntValue(exm)), termnum);
				//    termnum++;
				//}
				//state.Return(exm.VEvaluator.RESULT);
				//if (state.ScriptEnd)
				//    return;
				//int termnum = 0;
				StringStream aSt = new StringStream(((ExpressionArgument)func.Argument).Term.GetStrValue(exm));
				List<long> termList = new List<long>();
				while (!aSt.EOS)
				{
					WordCollection wc = LexicalAnalyzer.Analyse(aSt, LexEndWith.Comma, LexAnalyzeFlag.None);
					//exm.VEvaluator.SetResultX(ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL).GetIntValue(exm), termnum++);
					termList.Add(ExpressionParser.ReduceIntegerTerm(wc, TermEndWith.EoL).GetIntValue(exm));
					aSt.ShiftNext();
					LexicalAnalyzer.SkipHalfSpace(aSt);
					//termnum++;
				}
				if (termList.Count == 0)
					termList.Add(0);
				exm.VEvaluator.SetResultX(termList);
				state.Return(exm.VEvaluator.RESULT);
				if (state.ScriptEnd)
					return;
			}
		}

		private sealed class RETURN_Instruction : AbstractInstruction
		{
			public RETURN_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_ANY);
				flag = FLOW_CONTROL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				//int termnum = 0;
				ExpressionArrayArgument expArrayArg = (ExpressionArrayArgument)func.Argument;
				if (expArrayArg.TermList.Length == 0)
				{
					exm.VEvaluator.RESULT = 0;
					state.Return(0);
					return;
				}
				List<long> termList = new List<long>();
				foreach (IOperandTerm term in expArrayArg.TermList)
				{
					termList.Add(term.GetIntValue(exm));
					//exm.VEvaluator.SetResultX(term.GetIntValue(exm), termnum++);
				}
				if (termList.Count == 0)
					termList.Add(0);
				exm.VEvaluator.SetResultX(termList);
				state.Return(exm.VEvaluator.RESULT);
			}
		}

		private sealed class CATCH_Instruction : AbstractInstruction
		{
			public CATCH_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				//if (sequential)//上から流れてきたなら何もしないでENDCATCHに飛ぶ
				state.JumpTo(func.JumpToEndCatch);
			}
		}

		private sealed class RESTART_Instruction : AbstractInstruction
		{
			public RESTART_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | FLOW_CONTROL | EXTENDED;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				state.JumpTo(func.ParentLabelLine);
			}
		}

		private sealed class BREAK_Instruction : AbstractInstruction
		{
			public BREAK_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | FLOW_CONTROL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				////BREAKのJUMP先はRENDまたはNEXT。そのジャンプ先であるREPEATかFORをiLineに代入。
				//1.723 仕様変更。BREAKのJUMP先にはREPEAT、FOR、WHILEを記憶する。そのJUMP先が本当のJUMP先。
				InstructionLine jumpTo = (InstructionLine)func.JumpTo;
				InstructionLine iLine = (InstructionLine)jumpTo.JumpTo;
				//WHILEとDOはカウンタがないので、即ジャンプ
				if (jumpTo.FunctionCode != FunctionCode.WHILE && jumpTo.FunctionCode != FunctionCode.DO)
				{
					unchecked
					{//eramakerではBREAK時にCOUNTが回る
						jumpTo.LoopCounter.PlusValue(jumpTo.LoopStep, exm);
					}
				}
				state.JumpTo(iLine);
			}
		}

		private sealed class CONTINUE_Instruction : AbstractInstruction
		{
			public CONTINUE_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | FLOW_CONTROL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				InstructionLine jumpTo = (InstructionLine)func.JumpTo;
				if ((jumpTo.FunctionCode == FunctionCode.REPEAT) || (jumpTo.FunctionCode == FunctionCode.FOR))
				{
					//ループ変数が不明(REPEAT、FORを経由せずにループしようとした場合は無視してループを抜ける(eramakerがこういう仕様だったりする))
					if (jumpTo.LoopCounter == null)
					{
						state.JumpTo(jumpTo.JumpTo);
						return;
					}
					unchecked
					{
						jumpTo.LoopCounter.PlusValue(jumpTo.LoopStep, exm);
					}
					Int64 counter = jumpTo.LoopCounter.GetIntValue(exm);
					//まだ回数が残っているなら、
					if (((jumpTo.LoopStep > 0) && (jumpTo.LoopEnd > counter))
						|| ((jumpTo.LoopStep < 0) && (jumpTo.LoopEnd < counter)))
						state.JumpTo(func.JumpTo);
					else
						state.JumpTo(jumpTo.JumpTo);
					return;
				}
				if (jumpTo.FunctionCode == FunctionCode.WHILE)
				{
					if (((ExpressionArgument)jumpTo.Argument).Term.GetIntValue(exm) != 0)
						state.JumpTo(func.JumpTo);
					else
						state.JumpTo(jumpTo.JumpTo);
					return;
				}
				if (jumpTo.FunctionCode == FunctionCode.DO)
				{
					//こいつだけはCONTINUEよりも後ろに判定行があるため、判定行にエラーがあった場合に問題がある
					InstructionLine tFunc = (InstructionLine)((InstructionLine)func.JumpTo).JumpTo;//LOOP
					if (tFunc.IsError)
						throw new CodeEE(tFunc.ErrMes, tFunc.Position);
					ExpressionArgument expArg = (ExpressionArgument)tFunc.Argument;
					if (expArg.Term.GetIntValue(exm) != 0)//式が真
						state.JumpTo(jumpTo);//DO
					else
						state.JumpTo(tFunc);//LOOP
					return;
				}
				throw new ExeEE("異常なCONTINUE");
			}
		}

		private sealed class REND_Instruction : AbstractInstruction
		{
			public REND_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				InstructionLine jumpTo = (InstructionLine)func.JumpTo;
				//ループ変数が不明(REPEAT、FORを経由せずにループしようとした場合は無視してループを抜ける(eramakerがこういう仕様だったりする))
				if (jumpTo.LoopCounter == null)
				{
					state.JumpTo(jumpTo.JumpTo);
					return;
				}
				unchecked
				{
					jumpTo.LoopCounter.PlusValue(jumpTo.LoopStep, exm);
				}
				Int64 counter = jumpTo.LoopCounter.GetIntValue(exm);
				//まだ回数が残っているなら、
				if (((jumpTo.LoopStep > 0) && (jumpTo.LoopEnd > counter))
					|| ((jumpTo.LoopStep < 0) && (jumpTo.LoopEnd < counter)))
					state.JumpTo(func.JumpTo);
			}
		}

		private sealed class WEND_Instruction : AbstractInstruction
		{
			public WEND_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.VOID);
				flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				InstructionLine jumpTo = (InstructionLine)func.JumpTo;
				if (((ExpressionArgument)jumpTo.Argument).Term.GetIntValue(exm) != 0)
					state.JumpTo(func.JumpTo);
			}
		}

		private sealed class LOOP_Instruction : AbstractInstruction
		{
			public LOOP_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.INT_EXPRESSION);
				flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL | PARTIAL | FORCE_SETARG;
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				ExpressionArgument expArg = (ExpressionArgument)func.Argument;
				if (expArg.Term.GetIntValue(exm) != 0)//式が真
					state.JumpTo(func.JumpTo);
			}
		}


		private sealed class RETURNF_Instruction : AbstractInstruction
		{
			public RETURNF_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.EXPRESSION_NULLABLE);
				flag = METHOD_SAFE | EXTENDED | FLOW_CONTROL;
			}

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				FunctionLabelLine label = func.ParentLabelLine;
				if (!label.IsMethod)
				{
					ParserMediator.Warn("RETURNFは#FUNCTION以外では使用できません", func, 2, true, false);
				}
				if (func.Argument != null)
				{
					IOperandTerm term = ((ExpressionArgument)func.Argument).Term;
					if (term != null)
					{
						if (label.MethodType != term.GetOperandType())
						{
							if (label.MethodType == typeof(Int64))
								ParserMediator.Warn("#FUNCTIONで始まる関数の戻り値に整数型以外が指定されました", func, 2, true, false);
							else if (label.MethodType == typeof(string))
								ParserMediator.Warn("#FUNCTIONSで始まる関数の戻り値に文字列型以外が指定されました", func, 2, true, false);
							else if (label.MethodType == typeof(double))
								ParserMediator.Warn("#FUNCTIONFで始まる関数の戻り値に小数型以外が指定されました", func, 2, true, false);
						}
					}
				}
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				IOperandTerm term = ((ExpressionArgument)func.Argument).Term;
				SingleTerm ret = null;
				if (term != null)
				{
					ret = term.GetValue(exm);
				}
				state.ReturnF(ret);
			}
		}

		private sealed class CALLS_Instruction : AbstractInstruction
		{
			public CALLS_Instruction(bool isJump, bool isTry, bool isTryCatch)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR_EXPRESSION);
				flag = FLOW_CONTROL | FORCE_SETARG;
				if (isJump)
					flag |= IS_JUMP;
				if (isTry)
					flag |= IS_TRY;
				if (isTryCatch)
					flag |= IS_TRYC | PARTIAL;
				this.isJump = isJump;
				this.isTry = isTry;
			}
			readonly bool isJump;
			readonly bool isTry;

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				useCallForm = true;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				string scriptLine = func.Argument.IsConst
					? func.Argument.ConstStr
					: ((ExpressionArgument)func.Argument).Term.GetStrValue(exm);
				if (string.IsNullOrWhiteSpace(scriptLine))
					return;

				StringStream st = new StringStream(scriptLine);
				string labelName = LexicalAnalyzer.ReadString(st, StrEndWith.LeftParenthesis_Bracket_Comma_Semicolon).Trim();
				if (Config.ICFunction)
					labelName = labelName.ToUpper();
				char cur = st.Current;

				IOperandTerm[] args = null;
				try
				{
					WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
					if (!wc.EOL)
						wc.ShiftNext();
					if (cur == '(')
						args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.RightParenthesis, false);
					else if (cur == ',')
						args = ExpressionParser.ReduceArguments(wc, ArgsEndWith.EoL, false);
					else
						args = new IOperandTerm[0];
					for (int i = 0; i < args.Length; i++)
					{
						if (args[i] != null)
							args[i] = args[i].Restructure(exm);
					}
				}
				catch (EmueraException)
				{
					if (!isTry)
						throw;
					if (func.JumpToEndCatch != null)
						state.JumpTo(func.JumpToEndCatch);
					return;
				}

				CalledFunction call = CalledFunction.CallFunction(GlobalStatic.Process, labelName, func);
				if (call == null)
				{
					if (!isTry)
						throw new CodeEE("関数\"@" + labelName + "\"が見つかりません");
					if (func.JumpToEndCatch != null)
						state.JumpTo(func.JumpToEndCatch);
					return;
				}

				call.IsJump = isJump;
				string errMes;
				UserDefinedFunctionArgument arg = call.ConvertArg(args, out errMes);
				if (arg == null)
				{
					if (!isTry)
						throw new CodeEE(errMes);
					if (func.JumpToEndCatch != null)
						state.JumpTo(func.JumpToEndCatch);
					return;
				}
				state.IntoFunction(call, arg, exm);
			}
		}

		private sealed class CALL_Instruction : AbstractInstruction
		{
			public CALL_Instruction(bool form, bool isJump, bool isTry, bool isTryCatch)
			{
				if (form)
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLFORM);
				else
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALL);
				flag = FLOW_CONTROL | FORCE_SETARG;
				if (isJump)
					flag |= IS_JUMP;
				if (isTry)
					flag |= IS_TRY;
				if (isTryCatch)
					flag |= IS_TRYC | PARTIAL;
				this.isJump = isJump;
				this.isTry = isTry;
			}
			readonly bool isJump;
			readonly bool isTry;

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				if (!func.Argument.IsConst)
				{
					useCallForm = true;
					return;
				}
				SpCallArgment callArg = (SpCallArgment)func.Argument;
				string labelName = callArg.ConstStr;
				if (Config.ICFunction)
					labelName = labelName.ToUpper();
				CalledFunction call = CalledFunction.CallFunction(GlobalStatic.Process, labelName, func);
				if ((call == null) && (!func.Function.IsTry()))
				{
					FunctionoNotFoundName = labelName;
					return;
				}
				if (call != null)
				{
					func.JumpTo = call.TopLabel;
					if (call.TopLabel.Depth < 0)
						call.TopLabel.Depth = currentDepth + 1;
					if (call.TopLabel.IsError)
					{
						func.IsError = true;
						func.ErrMes = call.TopLabel.ErrMes;
						return;
					}
					string errMes;
					callArg.UDFArgument = call.ConvertArg(callArg.RowArgs, out errMes);
					if (callArg.UDFArgument == null)
					{
						ParserMediator.Warn(errMes, func, 2, true, false);
						return;
					}
				}
				callArg.CallFunc = call;
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpCallArgment spCallArg = (SpCallArgment)func.Argument;
				CalledFunction call;
				string labelName;
				UserDefinedFunctionArgument arg = null;
				if (spCallArg.IsConst)
				{
					call = spCallArg.CallFunc;
					labelName = spCallArg.ConstStr;
					arg = spCallArg.UDFArgument;
				}
				else
				{
					labelName = spCallArg.FuncnameTerm.GetStrValue(exm);
					if (Config.ICFunction)
						labelName = labelName.ToUpper();
					call = CalledFunction.CallFunction(GlobalStatic.Process, labelName, func);
				}
				if (call == null)
				{
					if (!isTry)
						throw new CodeEE("関数\"@" + labelName + "\"が見つかりません");
					if (func.JumpToEndCatch != null)
						state.JumpTo(func.JumpToEndCatch);
					return;
				}
				call.IsJump = isJump;
				if (arg == null)
				{
					string errMes;
					arg = call.ConvertArg(spCallArg.RowArgs, out errMes);
					if (arg == null)
						throw new CodeEE(errMes);
				}
				state.IntoFunction(call, arg, exm);
			}
		}

		private sealed class CALLEVENT_Instruction : AbstractInstruction
		{
			public CALLEVENT_Instruction()
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.STR);
				flag = FLOW_CONTROL | EXTENDED;
			}

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				//EVENT関数からCALLされた先でCALLEVENTされるようなパターンはIntoFunctionで捕まえる
				FunctionLabelLine label = func.ParentLabelLine;
				if (label.IsEvent)
				{
					ParserMediator.Warn("EVENT関数中にCALLEVENT命令は使用できません", func, 2, true, false);
				}
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				string labelName = func.Argument.ConstStr;
				if (Config.ICFunction)
					labelName = labelName.ToUpper();
				CalledFunction call = CalledFunction.CallEventFunction(GlobalStatic.Process, labelName, func);
				if (call == null)
					return;
				state.IntoFunction(call, null, null);
			}
		}

		private sealed class GOTO_Instruction : AbstractInstruction
		{
			public GOTO_Instruction(bool form, bool isTry, bool isTryCatch)
			{
				if (form)
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALLFORM);
				else
					ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_CALL);
				this.isTry = isTry;
				flag = METHOD_SAFE | FLOW_CONTROL | FORCE_SETARG;
				if (isTry)
					flag |= IS_TRY;
				if (isTryCatch)
					flag |= IS_TRYC | PARTIAL;
			}
			readonly bool isTry;

			public override void SetJumpTo(ref bool useCallForm, InstructionLine func, int currentDepth, ref string FunctionoNotFoundName)
			{
				GotoLabelLine jumpto;
				func.JumpTo = null;
				if (func.Argument.IsConst)
				{
					string labelName = func.Argument.ConstStr;
					if (Config.ICVariable)//eramakerではGOTO文は大文字小文字を区別しない
						labelName = labelName.ToUpper();
					jumpto = GlobalStatic.LabelDictionary.GetLabelDollar(labelName, func.ParentLabelLine);
					if (jumpto == null)
					{
						if (!func.Function.IsTry())
							ParserMediator.Warn("指定されたラベル名\"$" + labelName + "\"は現在の関数内に存在しません", func, 2, true, false);
						else
							return;
					}
					else if (jumpto.IsError)
						ParserMediator.Warn("指定されたラベル名\"$" + labelName + "\"は無効な$ラベル行です", func, 2, true, false);
					else if (jumpto != null)
					{
						func.JumpTo = jumpto;
					}
				}
			}
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				string label;
				LogicalLine jumpto;
				if (func.Argument.IsConst)
				{
					label = func.Argument.ConstStr;
					if (func.JumpTo != null)
						jumpto = func.JumpTo;
					else
						return;
				}
				else
				{
					label = ((SpCallArgment)func.Argument).FuncnameTerm.GetStrValue(exm);
					if (Config.ICVariable)
						label = label.ToUpper();
					jumpto = state.CurrentCalled.CallLabel(GlobalStatic.Process, label);
				}
				if (jumpto == null)
				{
					if (!func.Function.IsTry())
						throw new CodeEE("指定されたラベル名\"$" + label + "\"は現在の関数内に存在しません");
					if (func.JumpToEndCatch != null)
						state.JumpTo(func.JumpToEndCatch);
					return;
				}
				else if (jumpto.IsError)
					throw new CodeEE("指定されたラベル名\"$" + label + "\"は無効な$ラベル行です");
				state.JumpTo(jumpto);
			}
		}
		#endregion
	}
}
