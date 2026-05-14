using System;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernSifStatement : ModernStatement
{
	public ModernSifStatement(AExpression condition, ModernStatement statement)
	{
		Condition = condition ?? throw new ArgumentNullException(nameof(condition));
		Statement = statement ?? throw new ArgumentNullException(nameof(statement));
	}

	public AExpression Condition { get; }
	public ModernStatement Statement { get; }

	public override void Execute(ModernExpressionContext context)
	{
		if (ModernIfStatement.IsTrue(Condition, context))
			Statement.Execute(context);
	}
}
