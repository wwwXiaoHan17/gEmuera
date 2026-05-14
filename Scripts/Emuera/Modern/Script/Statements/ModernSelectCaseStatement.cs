using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernSelectCaseStatement : ModernStatement
{
	readonly List<ModernCaseBranch> branches;

	public ModernSelectCaseStatement(AExpression selector, IEnumerable<ModernCaseBranch> branches)
	{
		Selector = selector ?? throw new ArgumentNullException(nameof(selector));
		if (branches == null)
			throw new ArgumentNullException(nameof(branches));
		this.branches = new List<ModernCaseBranch>(branches);
	}

	public AExpression Selector { get; }
	public IReadOnlyList<ModernCaseBranch> Branches { get { return branches; } }

	public override void Execute(ModernExpressionContext context)
	{
		SingleTerm selected = Selector.GetValue(context);
		for (int i = 0; i < branches.Count; i++)
		{
			if (branches[i].IsDefault || branches[i].Matches(selected, context))
			{
				branches[i].Body.Execute(context);
				return;
			}
		}
	}
}

internal sealed class ModernCaseBranch
{
	readonly List<ModernCaseExpression> cases;

	public ModernCaseBranch(IEnumerable<ModernCaseExpression> cases, ModernBlockStatement body, bool isDefault)
	{
		if (cases == null)
			throw new ArgumentNullException(nameof(cases));
		this.cases = new List<ModernCaseExpression>(cases);
		Body = body ?? throw new ArgumentNullException(nameof(body));
		IsDefault = isDefault;
	}

	public IReadOnlyList<ModernCaseExpression> Cases { get { return cases; } }
	public ModernBlockStatement Body { get; }
	public bool IsDefault { get; }

	public bool Matches(SingleTerm selected, ModernExpressionContext context)
	{
		for (int i = 0; i < cases.Count; i++)
		{
			if (cases[i].Matches(selected, context))
				return true;
		}

		return false;
	}
}

internal enum ModernCaseExpressionType
{
	Normal,
	To,
	Is,
}

internal sealed class ModernCaseExpression
{
	public ModernCaseExpression(AExpression left)
	{
		Left = left ?? throw new ArgumentNullException(nameof(left));
		CaseType = ModernCaseExpressionType.Normal;
	}

	public ModernCaseExpression(AExpression left, AExpression right)
	{
		Left = left ?? throw new ArgumentNullException(nameof(left));
		Right = right ?? throw new ArgumentNullException(nameof(right));
		CaseType = ModernCaseExpressionType.To;
	}

	public ModernCaseExpression(string op, AExpression left)
	{
		Operator = NormalizeOperator(op);
		Left = left ?? throw new ArgumentNullException(nameof(left));
		CaseType = ModernCaseExpressionType.Is;
	}

	public ModernCaseExpressionType CaseType { get; }
	public AExpression Left { get; }
	public AExpression Right { get; }
	public string Operator { get; }

	public bool Matches(SingleTerm selected, ModernExpressionContext context)
	{
		return selected switch
		{
			SingleStrTerm stringTerm => MatchesString(stringTerm.Str, context),
			SingleFloatTerm floatTerm => MatchesFloat(floatTerm.Float, context),
			SingleLongTerm longTerm => MatchesInt(longTerm.Int, context),
			_ => false,
		};
	}

	bool MatchesInt(long selected, ModernExpressionContext context)
	{
		if (CaseType == ModernCaseExpressionType.To)
			return Left.GetIntValue(context) <= selected && selected <= Right.GetIntValue(context);
		if (CaseType == ModernCaseExpressionType.Is)
			return CompareLong(selected, Left.GetIntValue(context), Operator);
		return Left.GetIntValue(context) == selected;
	}

	bool MatchesFloat(double selected, ModernExpressionContext context)
	{
		if (CaseType == ModernCaseExpressionType.To)
			return Left.GetFloatValue(context) <= selected && selected <= Right.GetFloatValue(context);
		if (CaseType == ModernCaseExpressionType.Is)
			return CompareDouble(selected, Left.GetFloatValue(context), Operator);
		return Left.GetFloatValue(context) == selected;
	}

	bool MatchesString(string selected, ModernExpressionContext context)
	{
		if (CaseType == ModernCaseExpressionType.To)
			return string.Compare(Left.GetStrValue(context), selected, StringComparison.Ordinal) <= 0
				&& string.Compare(selected, Right.GetStrValue(context), StringComparison.Ordinal) <= 0;
		if (CaseType == ModernCaseExpressionType.Is)
			return CompareString(selected, Left.GetStrValue(context), Operator);
		return string.Equals(Left.GetStrValue(context), selected, StringComparison.Ordinal);
	}

	static bool CompareLong(long left, long right, string op)
	{
		return op switch
		{
			"==" => left == right,
			"!=" => left != right,
			">" => left > right,
			"<" => left < right,
			">=" => left >= right,
			"<=" => left <= right,
			_ => throw new NotSupportedException($"Unsupported CASE IS operator {op}."),
		};
	}

	static bool CompareDouble(double left, double right, string op)
	{
		return op switch
		{
			"==" => left == right,
			"!=" => left != right,
			">" => left > right,
			"<" => left < right,
			">=" => left >= right,
			"<=" => left <= right,
			_ => throw new NotSupportedException($"Unsupported CASE IS operator {op}."),
		};
	}

	static bool CompareString(string left, string right, string op)
	{
		int compare = string.Compare(left, right, StringComparison.Ordinal);
		return op switch
		{
			"==" => compare == 0,
			"!=" => compare != 0,
			">" => compare > 0,
			"<" => compare < 0,
			">=" => compare >= 0,
			"<=" => compare <= 0,
			_ => throw new NotSupportedException($"Unsupported CASE IS operator {op}."),
		};
	}

	static string NormalizeOperator(string op)
	{
		return op switch
		{
			"=" => "==",
			"<>" => "!=",
			_ => op,
		};
	}
}
