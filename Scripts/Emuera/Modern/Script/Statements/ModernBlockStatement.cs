using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernBlockStatement : ModernStatement
{
	readonly List<ModernStatement> statements;

	public ModernBlockStatement(IEnumerable<ModernStatement> statements)
	{
		if (statements == null)
			throw new ArgumentNullException(nameof(statements));
		this.statements = new List<ModernStatement>(statements);
	}

	public IReadOnlyList<ModernStatement> Statements { get { return statements; } }

	public override void Execute(ModernExpressionContext context)
	{
		for (int i = 0; i < statements.Count; i++)
			statements[i].Execute(context);
	}
}
