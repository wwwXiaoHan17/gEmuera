// FunctionIdentifier.PrintInstruction.cs —— 承载打印/输出指令族功能域，自 Instraction.Child.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
						if (termV.GetEraType() == EraType.Integer)
							builder.Append(termV.GetIntValue(exm).ToString());
						else if (termV.GetEraType() == EraType.Float)
							builder.Append(termV.GetFloatValue(exm).ToString());
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
						// 快速路径：运行时字符串不含任何格式令牌时，
						// CheckEscape + AnalyseFormattedString + StrForm 是恒等变换，直接跳过完整管道。
						// 判定条件与 AnalyseFormattedString 的 SubWord 触发条件一一对应，
						// 覆盖 %、{}、\@、三连符号以及 \n/\0 的截断行为，保证输出文本完全不变。
						if (NeedsFormattedStringProcessing(str))
						{
							str = exm.CheckEscape(str);
							StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new StringStream(str), FormStrEndWith.EoL, false);
							StrForm strForm = StrForm.FromWordToken(wt);
							str = strForm.GetString(exm);
						}
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

			// 判断运行时格式串是否需要进入 CheckEscape + AnalyseFormattedString 管道。
			// 返回 false 表示管道是恒等变换（输出 == 输入）。必须与 LexicalAnalyzer
			// AnalyseFormattedString 的 SubWord 触发条件逐条对应，任何漏判都会改变输出。
			static bool NeedsFormattedStringProcessing(string str)
			{
				if (string.IsNullOrEmpty(str))
					return false;
				// '%' 与 '{' 无条件触发 PercentSubWord / CurlyBraceSubWord。
				if (str.IndexOf('%') >= 0 || str.IndexOf('{') >= 0)
					return true;
				// '\' 触发 CheckEscape 转义；与字面换行组合时 AnalyseFormattedString
				// 会截断剩余文本，因此命中 '\' 也必须走完整管道。
				if (str.IndexOf('\\') >= 0)
					return true;
				// '\n' 与 '\0' 在 FormStrEndWith.EoL 下会提前结束解析（截断），必须走完整管道。
				if (str.IndexOf('\n') >= 0 || str.IndexOf('\0') >= 0)
					return true;
				// 三连符号（***/+++/===///$$$）在未禁用时触发 TripleSymbolSubWord。
				if (!Config.SystemIgnoreTripleSymbol && ContainsTripleSymbol(str))
					return true;
				return false;
			}

			static bool ContainsTripleSymbol(string str)
			{
				int len = str.Length;
				for (int i = 0; i + 2 < len; i++)
				{
					char c = str[i];
					if (c == '*' || c == '+' || c == '=' || c == '/' || c == '$')
					{
						if (str[i + 1] == c && str[i + 2] == c)
							return true;
					}
				}
				return false;
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
				// RESULTS 等系统字符串数组在懒加载后端可能是 SparseArray<string>，
				// 与上游同样按两种后端分别写入（对照 snake 参考 Instraction.Child.cs）。
				object arrObj = spSplitArg.Var.GetArray();
				if (arrObj is SparseArray<string> sparse)
				{
					int outputlength = Math.Min(sparse.Length, strs.Length);
					for (int i = 0; i < outputlength; i++)
						sparse[i] = strs[i];
				}
				else
				{
					string[] output = (string[])arrObj;
					int outputlength = Math.Min(output.Length, strs.Length);
					Array.Copy(strs, output, outputlength);
				}
			}
		}
		
		
		private sealed class PRINT_IMG_Instruction : AbstractInstruction
		{
			public PRINT_IMG_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_PRINT_IMG);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
				SpPrintImgArgument arg = (SpPrintImgArgument)func.Argument;
				string name = arg.Name.GetStrValue(exm);
				string buttonName = arg.ButtonName?.GetStrValue(exm);
				string mappingName = arg.MappingName?.GetStrValue(exm);
				if (string.IsNullOrEmpty(buttonName))
					buttonName = null;
				if (string.IsNullOrEmpty(mappingName))
					mappingName = null;
				exm.Console.PrintImg(
					name,
					buttonName,
					mappingName,
					getMixedNum(arg.Parameters, exm, 1),
					getMixedNum(arg.Parameters, exm, 0),
					getMixedNum(arg.Parameters, exm, 2));
			}
		}

		private sealed class PRINT_RECT_Instruction : AbstractInstruction
		{
			public PRINT_RECT_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_PRINT_RECT);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
				SpPrintShapeArgument arg = (SpPrintShapeArgument)func.Argument;
				exm.Console.PrintShape("rect", getMixedNums(arg.Parameters, exm));
			}
		}

		private sealed class PRINT_SPACE_Instruction : AbstractInstruction
		{
			public PRINT_SPACE_Instruction()
			{
				flag = EXTENDED | METHOD_SAFE;
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_PRINT_SPACE);
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
                if (GlobalStatic.Process.SkipPrint)
                    return;
				SpPrintShapeArgument arg = (SpPrintShapeArgument)func.Argument;
				exm.Console.PrintShape("space", getMixedNums(arg.Parameters, exm));
			}
		}

		private sealed class CUSTOMDRAWLINE_Instruction : AbstractInstruction
		{
			public CUSTOMDRAWLINE_Instruction()
			{
				ArgBuilder = null;
				flag = METHOD_SAFE | EXTENDED;
			}

			public override Argument CreateArgument(InstructionLine line, ExpressionMediator exm)
			{
				StringStream st = line.PopArgumentPrimitive();
				if (st == null || st.EOS)
					throw new CodeEE("引数が足りません");

				// v24/snake 原核心把 CUSTOMDRAWLINE 后面的整段原始文本当作线条种子，
				// 不能走普通 STR 表达式解析，否则未加引号的箱线字符会被误判为语法。
				string rowStr = GlobalStatic.Console.getStBar(st.Substring());
				return new ExpressionArgument(new SingleTerm(rowStr))
				{
					ConstStr = rowStr,
					IsConst = true
				};
			}

			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				if (GlobalStatic.Process.SkipPrint)
					return;

				GlobalStatic.Console.printCustomBar(func.Argument.ConstStr, true);
				exm.Console.NewLine();
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

	}
}
