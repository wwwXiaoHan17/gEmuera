// FunctionIdentifier.FlowControlInstruction.cs —— 承载流程控制指令族功能域，自 Instraction.Child.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
				// v24/snake 核心允许普通 BEGIN 中断当前系统函数栈。
				// erablue resort 的一日结束报告会在 @SHOW_SHOP 栈内执行 BEGIN ABLUP，
				// 若按当前 SystemState 的 __CAN_BEGIN__ 严格检查会误报。
				state.SetBegin(keyword, true);
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
				: this(fornext, null)
			{
			}

			public REPEAT_Instruction(bool fornext, ArgumentBuilder forNextArgumentBuilder)
			{
				flag = METHOD_SAFE | FLOW_CONTROL | PARTIAL;
				if (fornext)
				{
					ArgBuilder = forNextArgumentBuilder ?? ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_FOR_NEXT);
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
				double fValue = 0.0;
				if (selectValue.IsInteger)
					iValue = selectValue.GetIntValue(exm);
				else if (selectValue.IsFloat)
					fValue = selectValue.GetFloatValue(exm);
				else
					sValue = selectValue.GetStrValue(exm);
				if (func.SelectCaseJumpTable != null)
				{
					if (selectValue.IsInteger)
						caseJumpto = func.SelectCaseJumpTable.Lookup(iValue);
					else if (selectValue.IsFloat)
						caseJumpto = func.SelectCaseJumpTable.Lookup(fValue);
					else
						caseJumpto = func.SelectCaseJumpTable.Lookup(sValue);
					state.JumpTo(caseJumpto);
					return;
				}
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
					else if (selectValue.IsFloat)
					{
						double Is = fValue;
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
						if (label.MethodType != term.GetEraType())
						{
							// snake 参考：#FUNCTIONF 函数 RETURNF 整型表达式静默自动提升，不告警
							if (label.MethodType == EraType.Integer)
								ParserMediator.Warn("#FUNCTIONで始まる関数の戻り値に整数型以外が指定されました", func, 2, true, false);
							else if (label.MethodType == EraType.String)
								ParserMediator.Warn("#FUNCTIONSで始まる関数の戻り値に文字列型以外が指定されました", func, 2, true, false);
							else if (label.MethodType == EraType.Float && term.GetEraType() != EraType.Integer)
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
				// megaten 门控（P1）：CALLS 动态脚本行函数名查询大小写跟随 ICVariable；Disabled 下与仅 ICFunction 等价。
				if (Config.ICFunction || (Config.ICVariable && Program.Compatibility.Megaten.UsesVariableCaseForFunctionLabelLookup))
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
				UserDefinedFunctionArgument arg = call.ConvertArg(args, out errMes, isTry);
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
				// megaten 门控（P1）：CALL/JUMP 常量标签查询大小写跟随 ICVariable；Disabled 下与仅 ICFunction 等价。
				if (Config.ICFunction || (Config.ICVariable && Program.Compatibility.Megaten.UsesVariableCaseForFunctionLabelLookup))
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
					callArg.UDFArgument = call.ConvertArg(callArg.RowArgs, out errMes, func.Function.IsTry());
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
					// SetJumpTo 阶段缓存的是可复用模板；每次执行必须克隆为独立调用帧。
					call = spCallArg.CallFunc?.Clone();
					labelName = spCallArg.ConstStr;
					arg = spCallArg.UDFArgument;
				}
				else
				{
					labelName = spCallArg.FuncnameTerm.GetStrValue(exm);
					// megaten 门控（P1）：CALLFORM 动态函数名查询大小写跟随 ICVariable；Disabled 下与仅 ICFunction 等价。
					if (Config.ICFunction || (Config.ICVariable && Program.Compatibility.Megaten.UsesVariableCaseForFunctionLabelLookup))
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
					arg = call.ConvertArg(spCallArg.RowArgs, out errMes, isTry);
					if (arg == null)
					{
						if (!isTry)
							throw new CodeEE(errMes);
						if (func.JumpToEndCatch != null)
							state.JumpTo(func.JumpToEndCatch);
						return;
					}
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
				// megaten 门控（P1）：CALLEVENT 事件函数名查询大小写跟随 ICVariable；Disabled 下与仅 ICFunction 等价。
				if (Config.ICFunction || (Config.ICVariable && Program.Compatibility.Megaten.UsesVariableCaseForFunctionLabelLookup))
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
