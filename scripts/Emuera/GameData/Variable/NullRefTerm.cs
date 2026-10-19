using System;
using MinorShift.Emuera.GameData.Expression;

namespace MinorShift.Emuera.GameData.Variable
{
	internal sealed class NullRefTerm : VariableToken
	{
		public NullRefTerm(bool isInteger, bool isFloat)
			: base(isFloat ? VariableCode.REFF : (isInteger ? VariableCode.REF : VariableCode.REFS), null)
		{
			varName = "(null ref)";
			IsReference = true;
			Dimension = 0;
			CanRestructure = true;
		}

		public override Int64 GetIntValue(ExpressionMediator exm, Int64[] arguments)
		{
			return 0;
		}

		public override double GetFloatValue(ExpressionMediator exm, Int64[] arguments)
		{
			return 0.0;
		}

		public override string GetStrValue(ExpressionMediator exm, Int64[] arguments)
		{
			return "";
		}

		public override void SetValue(Int64 value, Int64[] arguments) { }
		public override void SetValue(double value, Int64[] arguments) { }
		public override void SetValue(string value, Int64[] arguments) { }
		public override void SetValue(Int64[] values, Int64[] arguments) { }
		public override void SetValue(double[] values, Int64[] arguments) { }
		public override void SetValue(string[] values, Int64[] arguments) { }
		public override void SetValueAll(Int64 value, int start, int end, int charaPos) { }
		public override void SetValueAll(double value, int start, int end, int charaPos) { }
		public override void SetValueAll(string value, int start, int end, int charaPos) { }

		public override Int64 PlusValue(Int64 value, Int64[] arguments)
		{
			return 0;
		}

		public override object GetArray()
		{
			return null;
		}

		public override void CheckElement(Int64[] arguments, bool[] doCheck) { }
	}
}
