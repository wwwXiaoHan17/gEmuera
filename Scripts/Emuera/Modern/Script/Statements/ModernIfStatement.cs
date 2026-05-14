using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernIfStatement : ModernStatement
{
	readonly List<ModernIfBranch> branches;

	public ModernIfStatement(IEnumerable<ModernIfBranch> branches, ModernBlockStatement elseBlock)
	{
		if (branches == null)
			throw new ArgumentNullException(nameof(branches));
		this.branches = new List<ModernIfBranch>(branches);
		ElseBlock = elseBlock;
	}

	public IReadOnlyList<ModernIfBranch> Branches { get { return branches; } }
	public ModernBlockStatement ElseBlock { get; }

	public override void Execute(ModernExpressionContext context)
	{
		for (int i = 0; i < branches.Count; i++)
		{
			if (IsTrue(branches[i].Condition, context))
			{
				branches[i].Body.Execute(context);
				return;
			}
		}

		ElseBlock?.Execute(context);
	}

	internal static bool IsTrue(AExpression expression, ModernExpressionContext context)
	{
		if (expression.IsString)
			throw new InvalidOperationException("String expressions cannot be used as conditions.");
		return expression.IsFloat ? expression.GetFloatValue(context) != 0.0 : expression.GetIntValue(context) != 0;
	}
}

internal readonly struct ModernIfBranch
{
	public ModernIfBranch(AExpression condition, ModernBlockStatement body)
	{
		Condition = condition ?? throw new ArgumentNullException(nameof(condition));
		Body = body ?? throw new ArgumentNullException(nameof(body));
	}

	public AExpression Condition { get; }
	public ModernBlockStatement Body { get; }
}
