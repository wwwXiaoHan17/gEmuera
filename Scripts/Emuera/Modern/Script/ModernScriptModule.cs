using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Functions;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script;

internal sealed class ModernScriptModule
{
	readonly Dictionary<string, ModernScriptLabel> labels;
	readonly Dictionary<string, ModernScriptFunctionMethod> functions;

	public ModernScriptModule(
		IEnumerable<ModernScriptLabel> labels,
		IEnumerable<ModernScriptFunctionMethod> functions)
	{
		this.labels = new Dictionary<string, ModernScriptLabel>(StringComparer.OrdinalIgnoreCase);
		this.functions = new Dictionary<string, ModernScriptFunctionMethod>(StringComparer.OrdinalIgnoreCase);

		if (labels != null)
		{
			foreach (var label in labels)
				this.labels[label.Name] = label;
		}

		if (functions != null)
		{
			foreach (var function in functions)
				this.functions[function.Name] = function;
		}
	}

	public IReadOnlyDictionary<string, ModernScriptLabel> Labels { get { return labels; } }
	public IReadOnlyDictionary<string, ModernScriptFunctionMethod> Functions { get { return functions; } }
}

internal sealed class ModernScriptLabel
{
	readonly List<ModernUserVariableDefinition> privateVariables;

	public ModernScriptLabel(
		string name,
		EraType methodType,
		bool isMethod,
		int lineNumber,
		int localIntegerLength = 0,
		int localStringLength = 0,
		int localFloatLength = 0,
		int argIntegerLength = 0,
		int argStringLength = 0,
		int argFloatLength = 0,
		IEnumerable<ModernUserVariableDefinition> privateVariables = null)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Label name is missing.", nameof(name));
		Name = name;
		MethodType = methodType;
		IsMethod = isMethod;
		LineNumber = lineNumber;
		LocalIntegerLength = localIntegerLength;
		LocalStringLength = localStringLength;
		LocalFloatLength = localFloatLength;
		ArgIntegerLength = argIntegerLength;
		ArgStringLength = argStringLength;
		ArgFloatLength = argFloatLength;
		this.privateVariables = privateVariables == null
			? new List<ModernUserVariableDefinition>()
			: new List<ModernUserVariableDefinition>(privateVariables);
	}

	public string Name { get; }
	public EraType MethodType { get; }
	public bool IsMethod { get; }
	public int LineNumber { get; }
	public int LocalIntegerLength { get; }
	public int LocalStringLength { get; }
	public int LocalFloatLength { get; }
	public int ArgIntegerLength { get; }
	public int ArgStringLength { get; }
	public int ArgFloatLength { get; }
	public IReadOnlyList<ModernUserVariableDefinition> PrivateVariables { get { return privateVariables; } }
}
