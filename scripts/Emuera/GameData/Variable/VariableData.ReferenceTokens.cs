// VariableData.ReferenceTokens.cs —— 承载 Reference 系列 token 嵌套类功能域，自 VariableToken.cs 拆出（原因：主文件超 2000 行只减不增约束；该区间实为 VariableData 的实现，故按 VariableData 命名）。
using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameData.Expression;

namespace MinorShift.Emuera.GameData.Variable
{
	internal sealed partial class VariableData
	{
		#region ref
		//1808beta009で追加
		/// <summary>
		/// public staticとprivate dynamicをクラスレベルでは区別しない
		/// 1808beta009時点ではprivate dynamicのみ
		/// </summary>
		private sealed class ReferenceIntScalarToken : ReferenceToken
		{
			public ReferenceIntScalarToken(UserDefinedVariableData data)
				: base(VariableCode.REF, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
				Dimension = 0;
			}

			public override Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (!elementRef.IsNull)
					return elementRef.GetIntValue(exm);
				if (isNullRef)
					return 0;
				if (scalarRefToken != null)
					return scalarRefToken.GetIntValue(exm, scalarRefArgs);
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((Int64[])array)[0];
			}

			public override void SetValue(Int64 value, Int64[] arguments)
			{
				if (!elementRef.IsNull)
				{
					elementRef.SetValue(value);
					return;
				}
				if (isNullRef)
					return;
				if (scalarRefToken != null)
				{
					scalarRefToken.SetValue(value, scalarRefArgs);
					return;
				}
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((Int64[])array)[0] = value;
			}

			public override Int64 PlusValue(Int64 value, Int64[] arguments)
			{
				if (!elementRef.IsNull)
					return elementRef.PlusValue(value);
				if (isNullRef)
					return 0;
				if (scalarRefToken != null)
					return scalarRefToken.PlusValue(value, scalarRefArgs);
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((Int64[])array)[0] = SafeArithmetic.SafeAdd(((Int64[])array)[0], value);
				return ((Int64[])array)[0];
			}

			public override Int32 GetLength()
			{
				throw new CodeEE("非配列型の参照型変数" + varName + "の長さを取得しようとしました");
			}

			public override Int32 GetLength(int dimension)
			{
				throw new CodeEE("非配列型の参照型変数" + varName + "の長さを取得しようとしました");
			}

			public override void CheckElement(Int64[] arguments, bool[] doCheck) { }
		}

		private sealed class ReferenceFloatScalarToken : ReferenceToken
		{
			public ReferenceFloatScalarToken(UserDefinedVariableData data)
				: base(VariableCode.REFF, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
				Dimension = 0;
			}

			public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (!elementRef.IsNull)
					return elementRef.GetFloatValue(exm);
				if (isNullRef)
					return 0.0;
				if (scalarRefToken != null)
					return scalarRefToken.GetFloatValue(exm, scalarRefArgs);
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((double[])array)[0];
			}

			public override Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments)
			{
				return (Int64)GetFloatValue(exm, arguments);
			}

			public override void SetValue(double value, Int64[] arguments)
			{
				if (!elementRef.IsNull)
				{
					elementRef.SetValue(value);
					return;
				}
				if (isNullRef)
					return;
				if (scalarRefToken != null)
				{
					scalarRefToken.SetValue(value, scalarRefArgs);
					return;
				}
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((double[])array)[0] = value;
			}

			public override Int32 GetLength()
			{
				throw new CodeEE("非配列型の参照型変数" + varName + "の長さを取得しようとしました");
			}

			public override Int32 GetLength(int dimension)
			{
				throw new CodeEE("非配列型の参照型変数" + varName + "の長さを取得しようとしました");
			}

			public override void CheckElement(Int64[] arguments, bool[] doCheck) { }
		}

		private sealed class ReferenceStrScalarToken : ReferenceToken
		{
			public ReferenceStrScalarToken(UserDefinedVariableData data)
				: base(VariableCode.REFS, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
				Dimension = 0;
			}

			public override string GetStrValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (!elementRef.IsNull)
					return elementRef.GetStrValue(exm);
				if (isNullRef)
					return "";
				if (scalarRefToken != null)
					return scalarRefToken.GetStrValue(exm, scalarRefArgs);
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((string[])array)[0];
			}

			public override void SetValue(string value, Int64[] arguments)
			{
				if (!elementRef.IsNull)
				{
					elementRef.SetValue(value);
					return;
				}
				if (isNullRef)
					return;
				if (scalarRefToken != null)
				{
					scalarRefToken.SetValue(value, scalarRefArgs);
					return;
				}
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((string[])array)[0] = value;
			}

			public override Int32 GetLength()
			{
				throw new CodeEE("非配列型の参照型変数" + varName + "の長さを取得しようとしました");
			}

			public override Int32 GetLength(int dimension)
			{
				throw new CodeEE("非配列型の参照型変数" + varName + "の長さを取得しようとしました");
			}

			public override void CheckElement(Int64[] arguments, bool[] doCheck) { }
		}

		private sealed class ReferenceInt1DToken : ReferenceToken
		{
			public ReferenceInt1DToken(UserDefinedVariableData data)
				: base(VariableCode.REF, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (isNullRef)
					return 0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return GetInt1D(array, arguments[0]);
			}

			public override void SetValue(Int64 value, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				SetInt1D(array, arguments[0], value);
			}

			public override void SetValue(Int64[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[0];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					SetInt1D(array, i, values[i - start]);
			}

			public override void SetValueAll(long value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				for (int i = start; i < end; i++)
					SetInt1D(array, i, value);
			}

			public override Int64 PlusValue(Int64 value, Int64[] arguments)
			{
				if (isNullRef)
					return 0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				Int64 newValue = SafeArithmetic.SafeAdd(GetInt1D(array, arguments[0]), value);
				SetInt1D(array, arguments[0], newValue);
				return newValue;
			}

		}

		private sealed class ReferenceInt2DToken : ReferenceToken
		{
			public ReferenceInt2DToken(UserDefinedVariableData data)
				: base(VariableCode.REF2D, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (isNullRef)
					return 0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((Int64[,])array)[arguments[0], arguments[1]];
			}

			public override void SetValue(Int64 value, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((Int64[,])array)[arguments[0], arguments[1]] = value;
			}

			public override void SetValue(Int64[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[1];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					((Int64[,])array)[arguments[0], i] = values[i - start];
			}

			public override void SetValueAll(long value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int a1 = GetArrayLength(array, 0);
				int a2 = GetArrayLength(array, 1);
				for (int i = 0; i < a1; i++)
					for (int j = 0; j < a2; j++)
						((Int64[,])array)[i, j] = value;
			}


			public override Int64 PlusValue(Int64 value, Int64[] arguments)
			{
				if (isNullRef)
					return 0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((Int64[,])array)[arguments[0], arguments[1]] = SafeArithmetic.SafeAdd(((Int64[,])array)[arguments[0], arguments[1]], value);
				return ((Int64[,])array)[arguments[0], arguments[1]];
			}
		}

		private sealed class ReferenceInt3DToken : ReferenceToken
		{
			public ReferenceInt3DToken(UserDefinedVariableData data)
				: base(VariableCode.REF3D, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (isNullRef)
					return 0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((Int64[, ,])array)[arguments[0], arguments[1], arguments[2]];
			}

			public override void SetValue(Int64 value, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((Int64[, ,])array)[arguments[0], arguments[1], arguments[2]] = value;
			}

			public override void SetValue(Int64[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[2];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					((Int64[, ,])array)[arguments[0], arguments[1], i] = values[i - start];
			}

			public override void SetValueAll(long value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int a1 = GetArrayLength(array, 0);
				int a2 = GetArrayLength(array, 1);
				int a3 = GetArrayLength(array, 2);
				for (int i = 0; i < a1; i++)
					for (int j = 0; j < a2; j++)
						for (int k = 0; k < a3; k++)
							((Int64[, ,])array)[i, j, k] = value;
			}


			public override Int64 PlusValue(Int64 value, Int64[] arguments)
			{
				if (isNullRef)
					return 0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((Int64[, ,])array)[arguments[0], arguments[1], arguments[2]] = SafeArithmetic.SafeAdd(((Int64[, ,])array)[arguments[0], arguments[1], arguments[2]], value);
				return ((Int64[, ,])array)[arguments[0], arguments[1], arguments[2]];
			}

		}
		private sealed class ReferenceFloat1DToken : ReferenceToken
		{
			public ReferenceFloat1DToken(UserDefinedVariableData data)
				: base(VariableCode.REFF, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (!elementRef.IsNull)
					return elementRef.GetFloatValue(exm);
				if (isNullRef)
					return 0.0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return GetFloat1D(array, arguments[0]);
			}
			public override void SetValue(double value, Int64[] arguments)
			{
				if (!elementRef.IsNull)
				{
					elementRef.SetValue(value);
					return;
				}
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				SetFloat1D(array, arguments[0], value);
			}
			public override void SetValue(double[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[0];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					SetFloat1D(array, i, values[i - start]);
			}
			public override void SetValueAll(double value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				for (int i = start; i < end; i++)
					SetFloat1D(array, i, value);
			}
		}

		private sealed class ReferenceFloat2DToken : ReferenceToken
		{
			public ReferenceFloat2DToken(UserDefinedVariableData data)
				: base(VariableCode.REFF2D, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (isNullRef)
					return 0.0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((double[,])array)[arguments[0], arguments[1]];
			}
			public override void SetValue(double value, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((double[,])array)[arguments[0], arguments[1]] = value;
			}
			public override void SetValue(double[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[1];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					((double[,])array)[arguments[0], i] = values[i - start];
			}
			public override void SetValueAll(double value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int a1 = GetArrayLength(array, 0);
				int a2 = GetArrayLength(array, 1);
				for (int i = 0; i < a1; i++)
					for (int j = 0; j < a2; j++)
						((double[,])array)[i, j] = value;
			}
		}

		private sealed class ReferenceFloat3DToken : ReferenceToken
		{
			public ReferenceFloat3DToken(UserDefinedVariableData data)
				: base(VariableCode.REFF3D, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (isNullRef)
					return 0.0;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((double[, ,])array)[arguments[0], arguments[1], arguments[2]];
			}
			public override void SetValue(double value, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((double[, ,])array)[arguments[0], arguments[1], arguments[2]] = value;
			}
			public override void SetValue(double[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[2];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					((double[, ,])array)[arguments[0], arguments[1], i] = values[i - start];
			}
			public override void SetValueAll(double value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int a1 = GetArrayLength(array, 0);
				int a2 = GetArrayLength(array, 1);
				int a3 = GetArrayLength(array, 2);
				for (int i = 0; i < a1; i++)
					for (int j = 0; j < a2; j++)
						for (int k = 0; k < a3; k++)
							((double[, ,])array)[i, j, k] = value;
			}
		}
		private sealed class ReferenceStr1DToken : ReferenceToken
		{
			public ReferenceStr1DToken(UserDefinedVariableData data)
				: base(VariableCode.REFS, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override string GetStrValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (!elementRef.IsNull)
					return elementRef.GetStrValue(exm);
				if (isNullRef)
					return "";
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return GetStr1D(array, arguments[0]);
			}

			public override void SetValue(string value, Int64[] arguments)
			{
				if (!elementRef.IsNull)
				{
					elementRef.SetValue(value);
					return;
				}
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				SetStr1D(array, arguments[0], value);
			}

			public override void SetValue(string[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[0];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					SetStr1D(array, i, values[i - start]);
			}

			public override void SetValueAll(string value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				for (int i = start; i < end; i++)
					SetStr1D(array, i, value);
			}
		}

		private sealed class ReferenceStr2DToken : ReferenceToken
		{
			public ReferenceStr2DToken(UserDefinedVariableData data)
				: base(VariableCode.REFS2D, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override string GetStrValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (isNullRef)
					return "";
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((string[,])array)[arguments[0], arguments[1]];
			}

			public override void SetValue(string value, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((string[,])array)[arguments[0], arguments[1]] = value;
			}

			public override void SetValue(string[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[1];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					((string[,])array)[arguments[0], i] = values[i - start];
			}

			public override void SetValueAll(string value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int a1 = GetArrayLength(array, 0);
				int a2 = GetArrayLength(array, 1);
				for (int i = 0; i < a1; i++)
					for (int j = 0; j < a2; j++)
						((string[,])array)[i, j] = value;
			}
		}

		private sealed class ReferenceStr3DToken : ReferenceToken
		{
			public ReferenceStr3DToken(UserDefinedVariableData data)
				: base(VariableCode.REFS3D, data)
			{
				CanRestructure = false;
				IsStatic = !data.Private;
			}
			public override string GetStrValue(ExpressionMediator exm, Int64[] arguments)
			{
				if (isNullRef)
					return "";
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				return ((string[, ,])array)[arguments[0], arguments[1], arguments[2]];
			}

			public override void SetValue(string value, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				((string[, ,])array)[arguments[0], arguments[1], arguments[2]] = value;
			}

			public override void SetValue(string[] values, Int64[] arguments)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int start = (int)arguments[2];
				int end = start + values.Length;
				for (int i = start; i < end; i++)
					((string[, ,])array)[arguments[0], arguments[1], i] = values[i - start];
			}

			public override void SetValueAll(string value, int start, int end, int charaPos)
			{
				if (isNullRef)
					return;
				if (array == null)
					throw new CodeEE("参照型変数" + varName + "は何も参照していません");
				int a1 = GetArrayLength(array, 0);
				int a2 = GetArrayLength(array, 1);
				int a3 = GetArrayLength(array, 2);
				for (int i = 0; i < a1; i++)
					for (int j = 0; j < a2; j++)
						for (int k = 0; k < a3; k++)
							((string[, ,])array)[i, j, k] = value;
			}

		}
		#endregion
	}
}
