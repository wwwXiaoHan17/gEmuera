// FunctionIdentifier.InputInstruction.cs —— 承载输入等待指令族功能域，自 Instraction.Child.cs 拆出（原因：主文件超 2000 行只减不增约束）。
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
				// 指针元数据选项（eraFL 的 INPUT ,1 / MOUSE）→ 请求级标志。对照源码
				// Instraction.Child.cs 的 req.MouseInput=arg.Mouse!=0：让 IsWaitingInputWithMouse
				// 能识别"INPUT 接受鼠标输入"，空白右键/中键才提交（RESULT:1 判定）。
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
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
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
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
				// 同上：ONEINPUT 也接受指针元数据，空白右键/中键按鼠标输入协议提交。
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
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
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
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
			public TINPUT_Instruction(bool oneInput, bool noFocus = false)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TINPUT);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
				this.isOne = oneInput;
				this.noFocus = noFocus;
			}
			bool isOne;
			readonly bool noFocus;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpTInputsArgument tinputarg = (SpTInputsArgument)func.Argument;

				InputRequest req = new InputRequest();
				req.InputType = InputType.IntValue;
				req.HasDefValue = true;
				req.OneInput = isOne;
				req.NoFocus = noFocus;
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
				if (tinputarg.Mouse != null)
					req.MouseInput = tinputarg.Mouse.GetIntValue(exm) == 1;
				Int64 z = (tinputarg.Disp != null) ? tinputarg.Disp.GetIntValue(exm) : 1;
				req.Timelimit = x;
				req.DefIntValue = y;
				req.DisplayTime = z != 0;
				req.TimeUpMes = (tinputarg.Timeout != null) ? tinputarg.Timeout.GetStrValue(exm) : Config.TimeupLabel;
				// EE_INPUT 機能拡張：消息跳过且指定了 CanSkip 时不阻塞，直接写 RESULT。
				if (tinputarg.CanSkip != null && exm.Console.MesSkip)
				{
					if (tinputarg.Mouse == null || tinputarg.Mouse.GetIntValue(exm) == 0)
						GlobalStatic.VEvaluator.RESULT = y;
					else
						GlobalStatic.VEvaluator.RESULT_ARRAY[1] = y;
					return;
				}
				exm.Console.WaitInput(req);
			}
		}

		private sealed class TINPUTS_Instruction : AbstractInstruction
		{
			public TINPUTS_Instruction(bool oneInput, bool noFocus = false)
			{
				ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_TINPUTS);
				flag = IS_PRINT | IS_INPUT | EXTENDED;
				this.isOne = oneInput;
				this.noFocus = noFocus;
			}
			bool isOne;
			readonly bool noFocus;
			public override void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state)
			{
				SpTInputsArgument tinputarg = (SpTInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrValue;
				req.HasDefValue = true;
				req.OneInput = isOne;
				req.NoFocus = noFocus;
				Int64 x = tinputarg.Time.GetIntValue(exm);
				string strs = tinputarg.Def.GetStrValue(exm);
				if (isOne && strs.Length > 1)
					strs = strs.Remove(1);
				if (tinputarg.Mouse != null)
					req.MouseInput = tinputarg.Mouse.GetIntValue(exm) == 1;
				Int64 z = (tinputarg.Disp != null) ? tinputarg.Disp.GetIntValue(exm) : 1;
				req.Timelimit = x;
				req.DefStrValue = strs;
				req.DisplayTime = z != 0;
				req.TimeUpMes = (tinputarg.Timeout != null) ? tinputarg.Timeout.GetStrValue(exm) : Config.TimeupLabel;
				// EE_INPUT 機能拡張：消息跳过且指定了 CanSkip 时不阻塞，直接写 RESULTS。
				if (tinputarg.CanSkip != null && exm.Console.MesSkip)
				{
					if (tinputarg.Mouse == null || tinputarg.Mouse.GetIntValue(exm) == 0)
						GlobalStatic.VEvaluator.RESULTS = strs;
					else
						GlobalStatic.VEvaluator.RESULTS_ARRAY[1] = strs;
					return;
				}
				exm.Console.WaitInput(req);
			}
		}
	}
}
