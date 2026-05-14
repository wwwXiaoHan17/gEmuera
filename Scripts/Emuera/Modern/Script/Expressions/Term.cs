namespace MinorShift.Emuera.Modern.Script.Expressions;

internal sealed class NullTerm : AExpression
{
	public NullTerm(long value)
		: base(EraType.Integer)
	{
	}

	public NullTerm(string value)
		: base(EraType.String)
	{
	}
}

internal abstract class SingleTerm : AExpression
{
	protected SingleTerm(EraType eraType)
		: base(eraType)
	{
	}

	public override bool IsConst { get { return true; } }

	public override AExpression Restructure(ModernExpressionContext context)
	{
		return this;
	}
}

internal sealed class SingleLongTerm : SingleTerm
{
	public SingleLongTerm(long value)
		: base(EraType.Integer)
	{
		this.value = value;
	}

	public long Int { get { return value; } }

	public override long GetIntValue(ModernExpressionContext context)
	{
		return value;
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		return this;
	}

	public override string ToString()
	{
		return value.ToString();
	}

	readonly long value;
}

internal sealed class SingleStrTerm : SingleTerm
{
	public SingleStrTerm(string value)
		: base(EraType.String)
	{
		this.value = value ?? "";
	}

	public string Str { get { return value; } }

	public override string GetStrValue(ModernExpressionContext context)
	{
		return value;
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		return this;
	}

	public override string ToString()
	{
		return value;
	}

	readonly string value;
}

internal sealed class SingleFloatTerm : SingleTerm
{
	public SingleFloatTerm(double value)
		: base(EraType.Float)
	{
		this.value = value;
	}

	public double Float { get { return value; } }

	public override double GetFloatValue(ModernExpressionContext context)
	{
		return value;
	}

	public override long GetIntValue(ModernExpressionContext context)
	{
		return (long)value;
	}

	public override SingleTerm GetValue(ModernExpressionContext context)
	{
		return this;
	}

	public override string ToString()
	{
		return value.ToString();
	}

	readonly double value;
}
