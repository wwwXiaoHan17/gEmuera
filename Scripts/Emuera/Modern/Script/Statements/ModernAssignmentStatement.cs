using System;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernAssignmentStatement : ModernStatement
{
	public ModernAssignmentStatement(ModernVariableTerm destination, AExpression source)
	{
		Destination = destination ?? throw new ArgumentNullException(nameof(destination));
		Source = source ?? throw new ArgumentNullException(nameof(source));
	}

	public ModernVariableTerm Destination { get; }
	public AExpression Source { get; }

	public override void Execute(ModernExpressionContext context)
	{
		if (Destination.Identifier.IsConst)
			throw new InvalidOperationException($"{Destination.Identifier.Name} is read-only.");
		Destination.SetValue(Source, context);
	}
}
