// FunctionIdentifier.DataInstruction.cs —— 承载数据与存档指令族功能域，自 Instraction.Child.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
				if (var.GetEraType() == EraType.String)
				{
					string src = spvarsetarg.Term.GetStrValue(exm);
					exm.VEvaluator.SetValueAll(p, src, start, end);
				}
				else if (var.GetEraType() == EraType.Float)
				{
					double src = spvarsetarg.Term.GetFloatValue(exm);
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
				if (index.GetEraType() == EraType.String && p.Identifier.IsArray1D)
				{
					if (!GlobalStatic.ConstantData.isDefined(p.Identifier.Code, index.Str))
						throw new CodeEE("文字列" + index.Str + "は配列変数" + p.Identifier.Name + "の要素ではありません");
				}
				if (p.Identifier.GetEraType() == EraType.String)
				{
					string src = spvarsetarg.Term.GetStrValue(exm);
					exm.VEvaluator.SetValueAllEachChara(p, index, src, start, end);
				}
				else if (p.Identifier.GetEraType() == EraType.Float)
				{
					double src = spvarsetarg.Term.GetFloatValue(exm);
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
				// v24 参考：UseNewRandom 时 RANDOMIZE 只警告不重播种；snake 参考则无条件执行（含 newRand 重播种）
				if (!Program.Compatibility.Snake.IsEnabled && Config.UseNewRandom)
				{
					ParserMediator.Warn("新しい乱数アルゴリズムではRANDOMIZEは無視されます", null, 0);
					ParserMediator.FlushWarningList();
					return;
				}
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
	}
}
