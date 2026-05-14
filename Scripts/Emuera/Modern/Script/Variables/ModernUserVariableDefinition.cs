using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal sealed class ModernUserVariableDefinition
{
	readonly int[] lengths;
	readonly SingleTerm[] defaultValues;

	public ModernUserVariableDefinition(
		string name,
		VariableKind kind,
		VariableDimension dimension,
		IEnumerable<int> lengths,
		IEnumerable<SingleTerm> defaultValues,
		ModernUserVariableAttributes attributes)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Variable name is missing.", nameof(name));
		Name = name;
		Kind = kind;
		Dimension = dimension;
		this.lengths = lengths == null ? Array.Empty<int>() : new List<int>(lengths).ToArray();
		this.defaultValues = defaultValues == null ? Array.Empty<SingleTerm>() : new List<SingleTerm>(defaultValues).ToArray();
		Attributes = attributes;
	}

	public string Name { get; }
	public VariableKind Kind { get; }
	public VariableDimension Dimension { get; }
	public IReadOnlyList<int> Lengths { get { return lengths; } }
	public IReadOnlyList<SingleTerm> DefaultValues { get { return defaultValues; } }
	public ModernUserVariableAttributes Attributes { get; }
	public bool IsInteger { get { return (Kind & VariableKind.Integer) != 0; } }
	public bool IsString { get { return (Kind & VariableKind.String) != 0; } }
	public bool IsFloat { get { return (Kind & VariableKind.Float) != 0; } }
}

[Flags]
internal enum ModernUserVariableAttributes
{
	None = 0,
	Private = 0x01,
	Static = 0x02,
	Dynamic = 0x04,
	Const = 0x08,
	Reference = 0x10,
	Out = 0x20,
}
