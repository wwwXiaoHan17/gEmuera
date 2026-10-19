using System;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Variable
{
	internal readonly struct ElementRefInfo
	{
		public readonly VariableToken TargetVar;
		public readonly Int64[] Indices;
		private readonly object capturedArray;

		public ElementRefInfo(VariableToken targetVar, Int64[] indices)
		{
			TargetVar = targetVar;
			Indices = indices == null ? Array.Empty<Int64>() : (Int64[])indices.Clone();
			capturedArray = null;
			// 非キャラ配列は呼び出し時点の配列実体を保持する。private dynamic 変数の入れ子呼び出しでも、
			// 後続のスコープ入れ替えに巻き込まれず、渡された要素そのものを参照し続けるため。
			if (targetVar != null && !targetVar.IsCharacterData && targetVar.Dimension > 0)
				capturedArray = targetVar.GetArray();
		}

		public bool IsNull
		{
			get { return TargetVar == null; }
		}

		public Int64 GetIntValue(ExpressionMediator exm)
		{
			if (capturedArray is Int64[] arr1)
				return arr1[Indices[0]];
			if (capturedArray is SparseArray<Int64> sparse1)
				return sparse1[Indices[0]];
			if (capturedArray is Int64[,] arr2)
				return arr2[Indices[0], Indices[1]];
			if (capturedArray is Int64[,,] arr3)
				return arr3[Indices[0], Indices[1], Indices[2]];
			return TargetVar.GetIntValue(exm, Indices);
		}

		public double GetFloatValue(ExpressionMediator exm)
		{
			if (capturedArray is double[] arr1)
				return arr1[Indices[0]];
			if (capturedArray is SparseArray<double> sparse1)
				return sparse1[Indices[0]];
			if (capturedArray is double[,] arr2)
				return arr2[Indices[0], Indices[1]];
			if (capturedArray is double[,,] arr3)
				return arr3[Indices[0], Indices[1], Indices[2]];
			return TargetVar.GetFloatValue(exm, Indices);
		}

		public string GetStrValue(ExpressionMediator exm)
		{
			if (capturedArray is string[] arr1)
				return arr1[Indices[0]];
			if (capturedArray is SparseArray<string> sparse1)
				return sparse1[Indices[0]];
			if (capturedArray is string[,] arr2)
				return arr2[Indices[0], Indices[1]];
			if (capturedArray is string[,,] arr3)
				return arr3[Indices[0], Indices[1], Indices[2]];
			return TargetVar.GetStrValue(exm, Indices);
		}

		public void SetValue(Int64 value)
		{
			if (capturedArray is Int64[] arr1)
				arr1[Indices[0]] = value;
			else if (capturedArray is SparseArray<Int64> sparse1)
				sparse1[Indices[0]] = value;
			else if (capturedArray is Int64[,] arr2)
				arr2[Indices[0], Indices[1]] = value;
			else if (capturedArray is Int64[,,] arr3)
				arr3[Indices[0], Indices[1], Indices[2]] = value;
			else
				TargetVar.SetValue(value, Indices);
		}

		public void SetValue(double value)
		{
			if (capturedArray is double[] arr1)
				arr1[Indices[0]] = value;
			else if (capturedArray is SparseArray<double> sparse1)
				sparse1[Indices[0]] = value;
			else if (capturedArray is double[,] arr2)
				arr2[Indices[0], Indices[1]] = value;
			else if (capturedArray is double[,,] arr3)
				arr3[Indices[0], Indices[1], Indices[2]] = value;
			else
				TargetVar.SetValue(value, Indices);
		}

		public void SetValue(string value)
		{
			if (capturedArray is string[] arr1)
				arr1[Indices[0]] = value;
			else if (capturedArray is SparseArray<string> sparse1)
				sparse1[Indices[0]] = value;
			else if (capturedArray is string[,] arr2)
				arr2[Indices[0], Indices[1]] = value;
			else if (capturedArray is string[,,] arr3)
				arr3[Indices[0], Indices[1], Indices[2]] = value;
			else
				TargetVar.SetValue(value, Indices);
		}

		public Int64 PlusValue(Int64 value)
		{
			if (capturedArray is Int64[] arr1)
			{
				arr1[Indices[0]] = SafeArithmetic.SafeAdd(arr1[Indices[0]], value);
				return arr1[Indices[0]];
			}
			if (capturedArray is SparseArray<Int64> sparse1)
			{
				sparse1[Indices[0]] = SafeArithmetic.SafeAdd(sparse1[Indices[0]], value);
				return sparse1[Indices[0]];
			}
			if (capturedArray is Int64[,] arr2)
			{
				arr2[Indices[0], Indices[1]] = SafeArithmetic.SafeAdd(arr2[Indices[0], Indices[1]], value);
				return arr2[Indices[0], Indices[1]];
			}
			if (capturedArray is Int64[,,] arr3)
			{
				arr3[Indices[0], Indices[1], Indices[2]] = SafeArithmetic.SafeAdd(arr3[Indices[0], Indices[1], Indices[2]], value);
				return arr3[Indices[0], Indices[1], Indices[2]];
			}
			TargetVar.PlusValue(value, Indices);
			return TargetVar.GetIntValue(null, Indices);
		}

		public override string ToString()
		{
			return IsNull ? "ElementRefInfo(null)" : "ElementRefInfo(" + TargetVar.Name + "[" + string.Join(",", Indices) + "])";
		}
	}
}
