using System;

namespace MinorShift.Emuera.Modern.Script.Expressions;

internal abstract class AExpression
{
	protected AExpression(EraType eraType)
	{
		this.eraType = eraType;
	}

	public Type GetOperandType()
	{
		return eraType switch
		{
			EraType.Integer => typeof(long),
			EraType.String => typeof(string),
			EraType.Float => typeof(double),
			_ => typeof(void),
		};
	}

	public EraType GetEraType()
	{
		return eraType;
	}

	public virtual long GetIntValue(ModernExpressionContext context)
	{
		return 0;
	}

	public virtual string GetStrValue(ModernExpressionContext context)
	{
		return "";
	}

	public virtual double GetFloatValue(ModernExpressionContext context)
	{
		return 0.0;
	}

	public virtual SingleTerm GetValue(ModernExpressionContext context)
	{
		return eraType switch
		{
			EraType.Integer => new SingleLongTerm(0),
			EraType.String => new SingleStrTerm(""),
			EraType.Float => new SingleFloatTerm(0.0),
			_ => new SingleLongTerm(0),
		};
	}

	public bool IsInteger { get { return eraType == EraType.Integer; } }
	public bool IsString { get { return eraType == EraType.String; } }
	public bool IsFloat { get { return eraType == EraType.Float; } }
	public virtual bool IsConst { get { return false; } }

	public virtual AExpression Restructure(ModernExpressionContext context)
	{
		return this;
	}

	readonly EraType eraType;
}
