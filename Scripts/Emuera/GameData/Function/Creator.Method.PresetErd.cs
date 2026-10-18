// Creator.Method.PresetErd.cs —— 预设变量名来源探针（Emuera-SKIA Skiav13.0 snake 语义：
// EXIST_IN_CSV / EXIST_IN_ERD），自 Creator.Method.cs 拆出（主文件超 2000 行只减不增约束）。
using System;
using MinorShift.Emuera.GameData.Expression;

namespace MinorShift.Emuera.GameData.Function
{
	internal static partial class FunctionMethodCreator
	{
		/// <summary>
		/// 探针：指定预设变量在指定索引的名字来源（合并后的有效来源，而非"谁申请过"）。
		/// 首参为字符串变量名（GETNUMB 风格，可反射拼接）；第二参只收整数索引，不做名字重载。
		/// 不支持的变量名/越界索引返回 0（探针语义：答不出即"不是"）。别名透明：查的是槽正名的来源。
		/// </summary>
		private abstract class PresetNameSourceMethod : FunctionMethod
		{
			protected PresetNameSourceMethod()
			{
				ReturnType = EraType.Integer;
				argumentTypeArray = new EraType[] { EraType.String, EraType.Integer };
				CanRestructure = true;
			}

			protected abstract bool Check(ConstantData constant, string varName, int index);

			public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
			{
				string varName = arguments[0].GetStrValue(exm);
				Int64 rawIndex = arguments[1].GetIntValue(exm);
				if (rawIndex < 0 || rawIndex > int.MaxValue)
					return 0;
				return Check(exm.VEvaluator.Constant, varName, (int)rawIndex) ? 1 : 0;
			}
		}

		private sealed class ExistPresetNameInCsvMethod : PresetNameSourceMethod
		{
			protected override bool Check(ConstantData constant, string varName, int index)
				=> constant.ExistPresetNameInCsv(varName, index);
		}

		private sealed class ExistPresetNameInErdMethod : PresetNameSourceMethod
		{
			protected override bool Check(ConstantData constant, string varName, int index)
				=> constant.ExistPresetNameInErd(varName, index);
		}
	}
}
