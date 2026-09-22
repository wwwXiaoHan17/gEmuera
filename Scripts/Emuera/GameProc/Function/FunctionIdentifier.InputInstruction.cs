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
				// 参考（v24 与 snake 一致）：INPUTANY 不带 IS_PRINT/IS_INPUT（SKIPDISP 下照常执行等待）
				flag = EXTENDED;
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntValue;
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
				if (arg.Def != null)
				{
					req.HasDefValue = true;
					req.DefIntValue = arg.Def.GetIntValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				// 参考（EE_INPUT機能拡張）：阅读跳过中且指定 CanSkip 时以默认值直通，不打断跳过
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULT_ARRAY[1] = arg.Def.GetIntValue(exm);
					else
						GlobalStatic.VEvaluator.RESULT = arg.Def.GetIntValue(exm);
				}
				else
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrValue;
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
				if (arg.Def != null)
				{
					req.HasDefValue = true;
					req.DefStrValue = arg.Def.GetStrValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULTS_ARRAY[1] = arg.Def.GetStrValue(exm);
					else
						GlobalStatic.VEvaluator.RESULTS = arg.Def.GetStrValue(exm);
				}
				else
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntValue;
				req.OneInput = true;
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
				if (arg.Def != null)
				{
					// 参考（ONEINPUT系制限解除）：默认值不再截断/过滤负数，原样生效
					req.HasDefValue = true;
					req.DefIntValue = arg.Def.GetIntValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULT_ARRAY[1] = arg.Def.GetIntValue(exm);
					else
						GlobalStatic.VEvaluator.RESULT = arg.Def.GetIntValue(exm);
				}
				else
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrValue;
				req.OneInput = true;
				req.EnablePointerInputMetadata = arg.EnablePointerInputMetadata;
				if (arg.Def != null)
				{
					req.HasDefValue = true;
					req.DefStrValue = arg.Def.GetStrValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULTS_ARRAY[1] = arg.Def.GetStrValue(exm);
					else
						GlobalStatic.VEvaluator.RESULTS = arg.Def.GetStrValue(exm);
				}
				else
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntButton;
				if (arg.Def != null)
				{
					req.HasDefValue = true;
					req.DefIntValue = arg.Def.GetIntValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULT_ARRAY[1] = arg.Def.GetIntValue(exm);
					else
						GlobalStatic.VEvaluator.RESULT = arg.Def.GetIntValue(exm);
					return;
				}
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrButton;
				if (arg.Def != null)
				{
					req.HasDefValue = true;
					req.DefStrValue = arg.Def.GetStrValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULTS_ARRAY[1] = arg.Def.GetStrValue(exm);
					else
						GlobalStatic.VEvaluator.RESULTS = arg.Def.GetStrValue(exm);
					return;
				}
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.IntButton;
				req.OneInput = true;
				if (arg.Def != null)
				{
					// 参考（ONEINPUT系制限解除）：默认值不再截断/过滤负数
					req.HasDefValue = true;
					req.DefIntValue = arg.Def.GetIntValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULT_ARRAY[1] = arg.Def.GetIntValue(exm);
					else
						GlobalStatic.VEvaluator.RESULT = arg.Def.GetIntValue(exm);
					return;
				}
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
				SpInputsArgument arg = (SpInputsArgument)func.Argument;
				InputRequest req = new InputRequest();
				req.InputType = InputType.StrButton;
				req.OneInput = true;
				if (arg.Def != null)
				{
					req.HasDefValue = true;
					req.DefStrValue = arg.Def.GetStrValue(exm);
				}
				if (arg.Mouse != null)
					req.MouseInput = arg.Mouse.GetIntValue(exm) != 0;
				if (!exm.Console.EmptyLine)
					exm.Console.NewLine();
				exm.Console.RefreshStrings(true);
				if (arg.CanSkip != null && exm.Console.MesSkip && arg.Def != null)
				{
					if (arg.Mouse != null && arg.Mouse.GetIntValue(exm) != 0)
						GlobalStatic.VEvaluator.RESULTS_ARRAY[1] = arg.Def.GetStrValue(exm);
					else
						GlobalStatic.VEvaluator.RESULTS = arg.Def.GetStrValue(exm);
					return;
				}
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
				// 参考（EM_私家版_INPUT系機能拡張）：ONEINPUT 系默认值截位逻辑已解除，原样生效
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
				// 参考（ONEINPUT系制限解除）：默认字符串不再截断为单字符
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
