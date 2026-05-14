using System;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal class ModernVariableTerm : AExpression
{
	readonly AExpression[] arguments;
	readonly long[] argumentValues;
	readonly bool allArgumentsAreConst;

	public ModernVariableTerm(ModernVariableToken token, AExpression[] arguments)
		: base(token.GetEraType())
	{
		Identifier = token ?? throw new ArgumentNullException(nameof(token));
		this.arguments = arguments ?? Array.Empty<AExpression>();
		argumentValues = new long[this.arguments.Length];

		allArgumentsAreConst = true;
		for (int i = 0; i < this.arguments.Length; i++)
		{
			if (this.arguments[i] is SingleLongTerm singleLongTerm)
				argumentValues[i] = singleLongTerm.Int;
			else
				allArgumentsAreConst = false;
		}
	}

	public ModernVariableToken Identifier { get; }
	public int ArgumentCount { get { return arguments.Length; } }
	public bool IsAllArgumentsConst { get { return allArgumentsAreConst; } }

	public bool TryGetConstantArguments(out long[] values)
	{
		if (!allArgumentsAreConst)
		{
			values = Array.Empty<long>();
			return false;
		}
		values = new long[argumentValues.Length];
		Array.Copy(argumentValues, values, argumentValues.Length);
		return true;
	}

	public ModernVariableReference CaptureReference(ModernExpressionContext context)
	{
		UpdateArguments(context);
		var values = new long[argumentValues.Length];
		Array.Copy(argumentValues, values, argumentValues.Length);
		return new ModernVariableReference(Identifier, values, context);
	}

	public override long GetIntValue(ModernExpressionContext context)
	{
		UpdateArguments(context);
		return Identifier.GetIntValue(context, argumentValues);
	}

	public override string GetStrValue(ModernExpressionContext context)
	{
		UpdateArguments(context);
		return Identifier.GetStrValue(context, argumentValues) ?? "";
	}

	public override double GetFloatValue(ModernExpressionContext context)
	{
		UpdateArguments(context);
		return Identifier.GetFloatValue(context, argumentValues);
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		return Identifier.GetEraType() switch
		{
			EraType.Integer => new SingleLongTerm(GetIntValue(context)),
			EraType.Float => new SingleFloatTerm(GetFloatValue(context)),
			_ => new SingleStrTerm(GetStrValue(context)),
		};
	}

	public virtual void SetValue(long value, ModernExpressionContext context)
	{
		UpdateArguments(context);
		Identifier.SetValue(value, context, argumentValues);
	}

	public virtual void SetValue(string value, ModernExpressionContext context)
	{
		UpdateArguments(context);
		Identifier.SetValue(value, context, argumentValues);
	}

	public virtual void SetValue(double value, ModernExpressionContext context)
	{
		UpdateArguments(context);
		Identifier.SetValue(value, context, argumentValues);
	}

	public virtual void SetValue(SingleTerm value, ModernExpressionContext context)
	{
		switch (value)
		{
			case SingleLongTerm singleLongTerm:
				SetValue(singleLongTerm.Int, context);
				break;
			case SingleFloatTerm singleFloatTerm:
				SetValue(singleFloatTerm.Float, context);
				break;
			case SingleStrTerm singleStrTerm:
				SetValue(singleStrTerm.Str, context);
				break;
			default:
				throw new ArgumentException("Unsupported value term.", nameof(value));
		}
	}

	public virtual void SetValue(AExpression value, ModernExpressionContext context)
	{
		if (value == null)
			throw new ArgumentNullException(nameof(value));
		switch (Identifier.GetEraType())
		{
			case EraType.Integer:
				if (value.IsInteger)
					SetValue(value.GetIntValue(context), context);
				else if (value.IsFloat)
					SetValue((long)value.GetFloatValue(context), context);
				else
					throw new InvalidOperationException($"{Identifier.Name} cannot receive a string value.");
				break;
			case EraType.Float:
				if (value.IsFloat)
					SetValue(value.GetFloatValue(context), context);
				else if (value.IsInteger)
					SetValue((double)value.GetIntValue(context), context);
				else
					throw new InvalidOperationException($"{Identifier.Name} cannot receive a string value.");
				break;
			default:
				if (!value.IsString)
					throw new InvalidOperationException($"{Identifier.Name} cannot receive a numeric value.");
				SetValue(value.GetStrValue(context), context);
				break;
		}
	}

	public virtual long ChangeValue(long value, ModernExpressionContext context)
	{
		UpdateArguments(context);
		return Identifier.PlusValue(value, context, argumentValues);
	}

	public virtual double ChangeValue(double value, ModernExpressionContext context)
	{
		UpdateArguments(context);
		return Identifier.PlusValue(value, context, argumentValues);
	}

	void UpdateArguments(ModernExpressionContext context)
	{
		if (allArgumentsAreConst)
			return;
		for (int i = 0; i < arguments.Length; i++)
			argumentValues[i] = arguments[i].GetIntValue(context);
	}
}
