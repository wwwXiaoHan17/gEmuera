// VariableData.FloatTokens.cs —— 承载 #DIMF 浮点用户定义变量 token（Static/Private 一至三维与角色浮点一维/二维）嵌套类功能域，自 VariableToken.cs 拆出（原因：主文件超 2000 行只减不增约束；该区间实为 VariableData 的实现，故按 VariableData 命名）。
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
			#region float

			private sealed class StaticFloat1DVariableToken : UserDefinedVariableToken
			{
				public StaticFloat1DVariableToken(UserDefinedVariableData data)
					: base(VariableCode.VAR, data)
				{
					int[] sizes = data.Lengths;
					IsStatic = true;
					array = new double[sizes[0]];
					defArray = data.DefaultFloat;
					if (defArray != null)
						Array.Copy(defArray, array, defArray.Length);
				}
				double[] array = null;
				double[] defArray = null;
				public override void SetDefault()
				{
					Array.Clear(array, 0, totalSize);
					if (defArray != null)
						Array.Copy(defArray, array, defArray.Length);
				}
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					return array[arguments[0]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					array[arguments[0]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					int start = (int)arguments[0];
					int end = start + values.Length;
					for (int i = start; i < end; i++)
						array[i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					for (int i = start; i < end; i++)
						array[i] = value;
				}
				public override object GetArray() { return array; }
				public override void In() { }
				public override void Out() { }
			}

			private sealed class StaticFloat2DVariableToken : UserDefinedVariableToken
			{
				public StaticFloat2DVariableToken(UserDefinedVariableData data)
					: base(VariableCode.VAR2D, data)
				{
					int[] sizes = data.Lengths;
					IsStatic = true;
					array = new double[sizes[0], sizes[1]];
					// declare.const-table.v1：二维浮点常数表行优先回填（同 StaticInt2DVariableToken）。
					defArray = Program.Compatibility.Snake.SupportsConstTable ? data.DefaultFloat : null;
					RestoreDefault();
				}
				double[,] array = null;
				readonly double[] defArray;
				void RestoreDefault()
				{
					Array.Clear(array, 0, totalSize);
					if (defArray == null)
						return;
					int n = Math.Min(defArray.Length, totalSize);
					for (int k = 0; k < n; k++)
						array[k / sizes[1], k % sizes[1]] = defArray[k];
				}
				public override void SetDefault()
				{
					RestoreDefault();
				}
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					return array[arguments[0], arguments[1]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					array[arguments[0], arguments[1]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					int start = (int)arguments[1];
					int end = start + values.Length;
					for (int i = start; i < end; i++)
						array[arguments[0], i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					int a1 = array.GetLength(0);
					int a2 = array.GetLength(1);
					for (int i = 0; i < a1; i++)
						for (int j = 0; j < a2; j++)
							array[i, j] = value;
				}
				public override object GetArray() { return array; }
				public override void In() { }
				public override void Out() { }
			}

			private sealed class StaticFloat3DVariableToken : UserDefinedVariableToken
			{
				public StaticFloat3DVariableToken(UserDefinedVariableData data)
					: base(VariableCode.VAR3D, data)
				{
					int[] sizes = data.Lengths;
					IsStatic = true;
					array = new double[sizes[0], sizes[1], sizes[2]];
					// 上游 Skiav18.0 的 StaticFloat3DVariableToken 同样带回填（常数表最多二维，此处默认值数组恒为 null）。
					defArray = Program.Compatibility.Snake.SupportsConstTable ? data.DefaultFloat : null;
					RestoreDefault();
				}
				double[, ,] array = null;
				readonly double[] defArray;
				void RestoreDefault()
				{
					Array.Clear(array, 0, totalSize);
					if (defArray == null)
						return;
					int n = Math.Min(defArray.Length, totalSize);
					int d1 = sizes[1];
					int d2 = sizes[2];
					for (int k = 0; k < n; k++)
						array[k / (d1 * d2), (k / d2) % d1, k % d2] = defArray[k];
				}
				public override void SetDefault()
				{
					RestoreDefault();
				}
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					return array[arguments[0], arguments[1], arguments[2]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					array[arguments[0], arguments[1], arguments[2]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					int start = (int)arguments[2];
					int end = start + values.Length;
					for (int i = start; i < end; i++)
						array[arguments[0], arguments[1], i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					int a1 = array.GetLength(0);
					int a2 = array.GetLength(1);
					int a3 = array.GetLength(2);
					for (int i = 0; i < a1; i++)
						for (int j = 0; j < a2; j++)
							for (int k = 0; k < a3; k++)
								array[i, j, k] = value;
				}
				public override object GetArray() { return array; }
				public override void In() { }
				public override void Out() { }
			}

			private sealed class PrivateFloat1DVariableToken : UserDefinedVariableToken
			{
				public PrivateFloat1DVariableToken(UserDefinedVariableData data)
					: base(VariableCode.VAR, data)
				{
					int[] sizes = data.Lengths;
					IsStatic = false;
					arrayList = new List<double[]>();
					defArray = data.DefaultFloat;
				}
				readonly List<double[]> arrayList = null;
				double[] array = null;
				double[] defArray = null;
				public override void SetDefault() { }
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					return array[arguments[0]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					array[arguments[0]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					int start = (int)arguments[0];
					int end = start + values.Length;
					for (int i = start; i < end; i++)
						array[i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					for (int i = start; i < end; i++)
						array[i] = value;
				}
				public override object GetArray() { return array; }
				public override void In()
				{
					if (array != null)
						arrayList.Add(array);
					array = new double[sizes[0]];
					if (defArray != null)
						Array.Copy(defArray, array, defArray.Length);
				}
				public override void Out()
				{
					if (arrayList.Count > 0)
					{
						array = arrayList[arrayList.Count - 1];
						arrayList.RemoveAt(arrayList.Count - 1);
					}
					else
						array = null;
				}
			}

			private sealed class PrivateFloat2DVariableToken : UserDefinedVariableToken
			{
				public PrivateFloat2DVariableToken(UserDefinedVariableData data)
					: base(VariableCode.VAR2D, data)
				{
					int[] sizes = data.Lengths;
					IsStatic = false;
					arrayList = new List<double[,]>();
				}
				readonly List<double[,]> arrayList = null;
				double[,] array = null;
				public override void SetDefault() { }
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					return array[arguments[0], arguments[1]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					array[arguments[0], arguments[1]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					int start = (int)arguments[1];
					int end = start + values.Length;
					for (int i = start; i < end; i++)
						array[arguments[0], i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					int a1 = array.GetLength(0);
					int a2 = array.GetLength(1);
					for (int i = 0; i < a1; i++)
						for (int j = 0; j < a2; j++)
							array[i, j] = value;
				}
				public override object GetArray() { return array; }
				public override void In()
				{
					if (array != null)
						arrayList.Add(array);
					array = new double[sizes[0], sizes[1]];
				}
				public override void Out()
				{
					if (arrayList.Count > 0)
					{
						array = arrayList[arrayList.Count - 1];
						arrayList.RemoveAt(arrayList.Count - 1);
					}
					else
						array = null;
				}
			}

			private sealed class PrivateFloat3DVariableToken : UserDefinedVariableToken
			{
				public PrivateFloat3DVariableToken(UserDefinedVariableData data)
					: base(VariableCode.VAR3D, data)
				{
					int[] sizes = data.Lengths;
					IsStatic = false;
					arrayList = new List<double[, ,]>();
				}
				readonly List<double[, ,]> arrayList = null;
				double[, ,] array = null;
				public override void SetDefault() { }
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					return array[arguments[0], arguments[1], arguments[2]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					array[arguments[0], arguments[1], arguments[2]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					int start = (int)arguments[2];
					int end = start + values.Length;
					for (int i = start; i < end; i++)
						array[arguments[0], arguments[1], i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					int a1 = array.GetLength(0);
					int a2 = array.GetLength(1);
					int a3 = array.GetLength(2);
					for (int i = 0; i < a1; i++)
						for (int j = 0; j < a2; j++)
							for (int k = 0; k < a3; k++)
								array[i, j, k] = value;
				}
				public override object GetArray() { return array; }
				public override void In()
				{
					if (array != null)
						arrayList.Add(array);
					array = new double[sizes[0], sizes[1], sizes[2]];
				}
				public override void Out()
				{
					if (arrayList.Count > 0)
					{
						array = arrayList[arrayList.Count - 1];
						arrayList.RemoveAt(arrayList.Count - 1);
					}
					else
						array = null;
				}
			}

			private sealed class UserDefinedCharaFloat1DVariableToken : UserDefinedCharaVariableToken
			{
				public UserDefinedCharaFloat1DVariableToken(UserDefinedVariableData data, VariableData varData, int arrayIndex)
					: base(VariableCode.CVAR, data, varData, arrayIndex)
				{
				}
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					double[] array = (double[])GetArrayChara((int)arguments[0]);
					return array[arguments[1]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					double[] array = (double[])GetArrayChara((int)arguments[0]);
					array[arguments[1]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					double[] array = (double[])GetArrayChara((int)arguments[0]);
					int start = (int)arguments[1];
					int end = start + values.Length;
					for (int i = start; i < end; i++)
						array[i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					double[] array = (double[])GetArrayChara(charaPos);
					for (int i = start; i < end; i++)
						array[i] = value;
				}
			}

			private sealed class UserDefinedCharaFloat2DVariableToken : UserDefinedCharaVariableToken
			{
				public UserDefinedCharaFloat2DVariableToken(UserDefinedVariableData data, VariableData varData, int arrayIndex)
					: base(VariableCode.CVAR2D, data, varData, arrayIndex)
				{
				}
				public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
				{
					double[,] array = (double[,])GetArrayChara((int)arguments[0]);
					return array[arguments[1], arguments[2]];
				}
				public override void SetValue(double value, Int64[] arguments)
				{
					double[,] array = (double[,])GetArrayChara((int)arguments[0]);
					array[arguments[1], arguments[2]] = value;
				}
				public override void SetValue(double[] values, Int64[] arguments)
				{
					double[,] array = (double[,])GetArrayChara((int)arguments[0]);
					int start = (int)arguments[2];
					int end = start + values.Length;
					int index1 = (int)arguments[1];
					for (int i = start; i < end; i++)
						array[index1, i] = values[i - start];
				}
				public override void SetValueAll(double value, int start, int end, int charaPos)
				{
					double[,] array = (double[,])GetArrayChara(charaPos);
					int a1 = sizes[0];
					int a2 = sizes[1];
					for (int i = 0; i < a1; i++)
						for (int j = 0; j < a2; j++)
							array[i, j] = value;
				}
			}
			#endregion
	}
}
