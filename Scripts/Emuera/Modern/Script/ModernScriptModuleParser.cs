using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Functions;
using MinorShift.Emuera.Modern.Script.Statements;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script;

internal sealed class ModernScriptModuleParser
{
	readonly ModernVariableEvaluator variableEvaluator;
	readonly ModernFunctionEvaluator functionEvaluator;
	readonly ModernExpressionParser expressionParser;

	public ModernScriptModuleParser(ModernVariableEvaluator variableEvaluator)
		: this(variableEvaluator, new ModernFunctionEvaluator())
	{
	}

	public ModernScriptModuleParser(ModernVariableEvaluator variableEvaluator, ModernFunctionEvaluator functionEvaluator)
	{
		this.variableEvaluator = variableEvaluator ?? throw new ArgumentNullException(nameof(variableEvaluator));
		this.functionEvaluator = functionEvaluator ?? throw new ArgumentNullException(nameof(functionEvaluator));
		expressionParser = new ModernExpressionParser(variableEvaluator, functionEvaluator);
	}

	public ModernScriptModule Parse(IEnumerable<string> sourceLines)
	{
		if (sourceLines == null)
			throw new ArgumentNullException(nameof(sourceLines));
		var lines = new List<string>(sourceLines);
		var sections = DiscoverSections(lines);
		var labels = new List<ModernScriptLabel>(sections.Count);
		var functions = new List<ModernScriptFunctionMethod>();

		for (int i = 0; i < sections.Count; i++)
		{
			var section = sections[i];
			section.VariableEvaluator = variableEvaluator.CreateScopedEvaluator(section.PrivateVariables);
			section.ArgumentBindings = ParseArgumentBindings(section.ArgumentSource, section.VariableEvaluator);
			labels.Add(new ModernScriptLabel(
				section.Name,
				section.MethodType,
				section.IsMethod,
				section.LineNumber,
				section.LocalIntegerLength,
				section.LocalStringLength,
				section.LocalFloatLength,
				section.ArgIntegerLength,
				section.ArgStringLength,
				section.ArgFloatLength,
				section.PrivateVariables));
			if (!section.IsMethod)
				continue;

			var method = new ModernScriptFunctionMethod(
				section.Name,
				section.MethodType,
				null,
				section.VariableEvaluator,
				localIntegerLength: section.LocalIntegerLength,
				localStringLength: section.LocalStringLength,
				localFloatLength: section.LocalFloatLength,
				argIntegerLength: section.ArgIntegerLength,
				argStringLength: section.ArgStringLength,
				argFloatLength: section.ArgFloatLength,
				privateVariableDefinitions: section.PrivateVariables,
				argumentBindings: section.ArgumentBindings);
			functionEvaluator.Register(method);
			section.Method = method;
			functions.Add(method);
		}

		for (int i = 0; i < sections.Count; i++)
		{
			var section = sections[i];
			if (!section.IsMethod)
				continue;

			var parser = new ModernScriptParser(section.VariableEvaluator, functionEvaluator);
			var bodyLines = ExtractBodyLines(lines, section);
			section.Method.SetBody(parser.ParseLines(bodyLines));
		}

		return new ModernScriptModule(labels, functions);
	}

	List<LabelSection> DiscoverSections(IReadOnlyList<string> lines)
	{
		var sections = new List<LabelSection>();
		LabelSection current = null;
		for (int i = 0; i < lines.Count; i++)
		{
			string line = ModernLineParser.NormalizeLine(lines[i]);
			if (line.Length == 0)
				continue;

			if (line[0] == '@')
			{
				if (current != null)
				current.EndLineExclusive = i;
				string labelName = ReadLabelName(line, i + 1);
				string argumentSource = ReadLabelArgumentSource(line);
				for (int j = 0; j < sections.Count; j++)
				{
					if (string.Equals(sections[j].Name, labelName, StringComparison.OrdinalIgnoreCase))
						throw new FormatException($"Duplicate label: {labelName}");
				}
				current = new LabelSection(labelName, i, i + 1);
				current.ArgumentSource = argumentSource;
				sections.Add(current);
				continue;
			}

			if (current != null && line[0] == '#')
			{
				if (TryReadFunctionType(line, out var type))
				{
					if (current.IsMethod)
						throw new FormatException($"{current.Name} already has a #FUNCTION directive.");
					current.IsMethod = true;
					current.MethodType = type;
					continue;
				}

				if (TryApplySizeDirective(current, line))
					continue;

				if (TryReadPrivateDim(current, line, out var definition))
				{
					for (int j = 0; j < current.PrivateVariables.Count; j++)
					{
						if (string.Equals(current.PrivateVariables[j].Name, definition.Name, StringComparison.OrdinalIgnoreCase))
							throw new FormatException($"Duplicate private variable {definition.Name} in {current.Name}.");
					}
					current.PrivateVariables.Add(definition);
					continue;
				}
			}
		}

		if (current != null)
			current.EndLineExclusive = lines.Count;
		return sections;
	}

	static List<string> ExtractBodyLines(IReadOnlyList<string> lines, LabelSection section)
	{
		var body = new List<string>();
		for (int i = section.StartBodyLine; i < section.EndLineExclusive; i++)
		{
			string line = ModernLineParser.NormalizeLine(lines[i]);
			if (line.Length == 0 || line[0] == '#')
				continue;
			body.Add(lines[i]);
		}

		return body;
	}

	static string ReadLabelName(string line, int lineNumber)
	{
		int start = 1;
		while (start < line.Length && char.IsWhiteSpace(line[start]))
			start++;
		if (start >= line.Length)
			throw new FormatException($"Label name is missing at line {lineNumber}.");

		int end = start;
		while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '(' && line[end] != ',')
			end++;
		string name = line.Substring(start, end - start).Trim();
		if (name.Length == 0)
			throw new FormatException($"Label name is missing at line {lineNumber}.");
		return name;
	}

	static string ReadLabelArgumentSource(string line)
	{
		int end = FindLabelNameEnd(line);
		if (end < 0)
			return "";
		while (end < line.Length && char.IsWhiteSpace(line[end]))
			end++;
		if (end >= line.Length)
			return "";

		if (line[end] == '(')
			return ReadBracketBody(line, end, '(', ')');
		if (line[end] == ',')
			return line.Substring(end + 1).Trim();
		return "";
	}

	static int FindLabelNameEnd(string line)
	{
		int start = 1;
		while (start < line.Length && char.IsWhiteSpace(line[start]))
			start++;
		if (start >= line.Length)
			return -1;

		int end = start;
		while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != '(' && line[end] != ',')
			end++;
		return end;
	}

	static string ReadBracketBody(string line, int openIndex, char open, char close)
	{
		int depth = 0;
		char quote = '\0';
		for (int i = openIndex; i < line.Length; i++)
		{
			char c = line[i];
			if (quote != '\0')
			{
				if (c == quote)
					quote = '\0';
				continue;
			}

			if (c == '"' || c == '\'')
			{
				quote = c;
				continue;
			}

			if (c == open)
				depth++;
			else if (c == close)
			{
				depth--;
				if (depth == 0)
					return line.Substring(openIndex + 1, i - openIndex - 1).Trim();
			}
		}

		throw new FormatException("Label argument list is not closed.");
	}

	List<ModernFunctionArgumentBinding> ParseArgumentBindings(string source, ModernVariableEvaluator evaluator)
	{
		var bindings = new List<ModernFunctionArgumentBinding>();
		if (string.IsNullOrWhiteSpace(source))
			return bindings;

		var parts = SplitTopLevelComma(source);
		for (int i = 0; i < parts.Count; i++)
		{
			string part = parts[i].Trim();
			if (part.Length == 0)
				throw new FormatException("Function argument list contains an empty item.");

			bool isVariadic = false;
			if (StartsWithKeyword(part, "VARIADIC"))
			{
				isVariadic = true;
				part = part.Substring("VARIADIC".Length).Trim();
				if (part.Length == 0)
					throw new FormatException("VARIADIC needs an argument variable.");
			}

			int assignment = FindTopLevelAssignment(part);
			string destinationSource = assignment < 0 ? part : part.Substring(0, assignment).Trim();
			string defaultSource = assignment < 0 ? "" : part.Substring(assignment + 1).Trim();
			var destination = ModernVariableParser.Parse(destinationSource, evaluator);
			if (destination.Identifier.IsConst)
				throw new FormatException($"{destination.Identifier.Name} cannot be used as a function argument destination.");
			SingleTerm defaultValue = null;
			if (defaultSource.Length != 0)
			{
				var expression = expressionParser.Parse(defaultSource).Restructure(null);
				defaultValue = expression as SingleTerm
					?? throw new FormatException("Function argument default value must be constant.");
				if (destination.Identifier.GetEraType() == EraType.Float && defaultValue is SingleLongTerm longTerm)
					defaultValue = new SingleFloatTerm(longTerm.Int);
				else if (destination.Identifier.GetEraType() != defaultValue.GetEraType())
					throw new FormatException($"{destination.Identifier.Name} default value type does not match.");
			}

			bindings.Add(new ModernFunctionArgumentBinding(destination, defaultValue, isVariadic));
		}

		ValidateArgumentBindings(bindings);
		return bindings;
	}

	static void ValidateArgumentBindings(IReadOnlyList<ModernFunctionArgumentBinding> bindings)
	{
		for (int i = 0; i < bindings.Count; i++)
		{
			var binding = bindings[i];
			if (!binding.IsVariadic)
				continue;
			if (i != bindings.Count - 1)
				throw new FormatException("VARIADIC must be the final function argument.");
			if (!binding.IsArgArray)
				throw new FormatException("VARIADIC can only target ARG, ARGS, or ARGF.");
			for (int j = 0; j < i; j++)
			{
				if (bindings[j].VariableCode == binding.VariableCode)
					throw new FormatException("A VARIADIC ARG/ARGS/ARGF cannot also appear in fixed arguments.");
			}
		}
	}

	static bool StartsWithKeyword(string source, string keyword)
	{
		if (!source.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
			return false;
		return source.Length == keyword.Length || char.IsWhiteSpace(source[keyword.Length]);
	}

	static int FindTopLevelAssignment(string source)
	{
		char quote = '\0';
		int depth = 0;
		for (int i = 0; i < source.Length; i++)
		{
			char c = source[i];
			if (quote != '\0')
			{
				if (c == quote)
					quote = '\0';
				continue;
			}
			if (c == '"' || c == '\'')
			{
				quote = c;
				continue;
			}
			if (c == '(')
			{
				depth++;
				continue;
			}
			if (c == ')')
			{
				depth--;
				continue;
			}
			if (depth == 0 && c == '=')
				return i;
		}
		return -1;
	}

	static List<string> SplitTopLevelComma(string source)
	{
		var result = new List<string>();
		char quote = '\0';
		int depth = 0;
		int start = 0;
		for (int i = 0; i < source.Length; i++)
		{
			char c = source[i];
			if (quote != '\0')
			{
				if (c == quote)
					quote = '\0';
				continue;
			}
			if (c == '"' || c == '\'')
			{
				quote = c;
				continue;
			}
			if (c == '(')
			{
				depth++;
				continue;
			}
			if (c == ')')
			{
				depth--;
				continue;
			}
			if (depth == 0 && c == ',')
			{
				result.Add(source.Substring(start, i - start).Trim());
				start = i + 1;
			}
		}
		result.Add(source.Substring(start).Trim());
		return result;
	}

	static bool TryReadFunctionType(string line, out EraType type)
	{
		string keyword = line.Substring(1).Trim();
		if (keyword.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
		{
			type = EraType.Integer;
			return true;
		}

		if (keyword.Equals("FUNCTIONS", StringComparison.OrdinalIgnoreCase))
		{
			type = EraType.String;
			return true;
		}

		if (keyword.Equals("FUNCTIONF", StringComparison.OrdinalIgnoreCase))
		{
			type = EraType.Float;
			return true;
		}

		type = EraType.Void;
		return false;
	}

	bool TryReadPrivateDim(LabelSection section, string line, out ModernUserVariableDefinition definition)
	{
		definition = null;
		string sharp = line.Substring(1).Trim();
		string keyword = ReadSharpKeyword(sharp, out var rest);
		if (!keyword.Equals("DIM", StringComparison.OrdinalIgnoreCase)
			&& !keyword.Equals("DIMS", StringComparison.OrdinalIgnoreCase)
			&& !keyword.Equals("DIMF", StringComparison.OrdinalIgnoreCase))
			return false;

		var parts = SplitDirectiveArguments(rest);
		if (parts.Count == 0 || string.IsNullOrWhiteSpace(parts[0]))
			throw new FormatException($"{keyword} needs a variable name.");

		string name = parts[0].Trim();
		var attributes = ModernUserVariableAttributes.Private | ModernUserVariableAttributes.Static;
		while (IsDimAttributeKeyword(name))
		{
			attributes = ApplyDimAttribute(attributes, name);
			parts.RemoveAt(0);
			if (parts.Count == 0 || string.IsNullOrWhiteSpace(parts[0]))
				throw new FormatException($"{keyword} needs a variable name.");
			name = parts[0].Trim();
		}

		if (IsReservedLocalSizingName(keyword, name))
			return false;

		var lengths = new List<int>();
		var defaults = new List<SingleTerm>();
		int assignmentPart = FindAssignmentPart(parts);
		int lengthEnd = assignmentPart >= 0 ? assignmentPart : parts.Count;
		for (int i = 1; i < lengthEnd; i++)
		{
			if (string.IsNullOrWhiteSpace(parts[i]))
				throw new FormatException($"{keyword} has an empty dimension length.");
			lengths.Add(ReadPositiveLength(parts[i], keyword));
		}

		if (lengths.Count == 0)
			lengths.Add(1);
		if (lengths.Count > 3)
			throw new FormatException($"{keyword} supports up to three dimensions.");

		if (assignmentPart >= 0)
			ReadDefaultValues(parts, assignmentPart, defaults);

		if (defaults.Count != 0 && lengths.Count > 1)
			throw new FormatException($"{keyword} initial values are only supported for scalar or 1D variables.");
		if ((attributes & ModernUserVariableAttributes.Const) != 0 && defaults.Count == 0)
			throw new FormatException($"{keyword} CONST variables need initial values.");
		if ((attributes & ModernUserVariableAttributes.Reference) != 0 && defaults.Count != 0)
			throw new FormatException($"{keyword} reference variables cannot have initial values.");
		if ((attributes & ModernUserVariableAttributes.Reference) != 0)
		{
			lengths.Clear();
			lengths.Add(1);
		}

		definition = new ModernUserVariableDefinition(
			name,
			ReadDimKind(keyword),
			ReadDimension(lengths.Count),
			lengths,
			defaults,
			attributes);
		return true;
	}

	static bool TryApplySizeDirective(LabelSection section, string line)
	{
		string sharp = line.Substring(1).Trim();
		string keyword = ReadSharpKeyword(sharp, out var rest);

		if (keyword.Equals("LOCALSIZE", StringComparison.OrdinalIgnoreCase))
		{
			section.LocalIntegerLength = ReadPositiveLength(rest, keyword);
			return true;
		}

		if (keyword.Equals("LOCALSSIZE", StringComparison.OrdinalIgnoreCase))
		{
			section.LocalStringLength = ReadPositiveLength(rest, keyword);
			return true;
		}

		if (keyword.Equals("LOCALFSIZE", StringComparison.OrdinalIgnoreCase))
		{
			section.LocalFloatLength = ReadPositiveLength(rest, keyword);
			return true;
		}

		if (keyword.Equals("ARGSIZE", StringComparison.OrdinalIgnoreCase))
		{
			section.ArgIntegerLength = ReadPositiveLength(rest, keyword);
			return true;
		}

		if (keyword.Equals("ARGSSIZE", StringComparison.OrdinalIgnoreCase))
		{
			section.ArgStringLength = ReadPositiveLength(rest, keyword);
			return true;
		}

		if (keyword.Equals("ARGFSIZE", StringComparison.OrdinalIgnoreCase))
		{
			section.ArgFloatLength = ReadPositiveLength(rest, keyword);
			return true;
		}

		if (keyword.Equals("DIM", StringComparison.OrdinalIgnoreCase)
			|| keyword.Equals("DIMS", StringComparison.OrdinalIgnoreCase)
			|| keyword.Equals("DIMF", StringComparison.OrdinalIgnoreCase))
			return TryApplyDimSizeDirective(section, keyword, rest);

		return false;
	}

	static bool TryApplyDimSizeDirective(LabelSection section, string keyword, string rest)
	{
		var parts = SplitDirectiveArguments(rest);
		if (parts.Count < 2)
			return false;

		string name = parts[0].Trim();
		int length = ReadPositiveLength(parts[1], keyword);
		if (keyword.Equals("DIM", StringComparison.OrdinalIgnoreCase) && name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase))
		{
			section.LocalIntegerLength = length;
			return true;
		}

		if (keyword.Equals("DIMS", StringComparison.OrdinalIgnoreCase) && name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase))
		{
			section.LocalStringLength = length;
			return true;
		}

		if (keyword.Equals("DIMF", StringComparison.OrdinalIgnoreCase) && name.Equals("LOCALF", StringComparison.OrdinalIgnoreCase))
		{
			section.LocalFloatLength = length;
			return true;
		}

		if (keyword.Equals("DIM", StringComparison.OrdinalIgnoreCase) && name.Equals("ARG", StringComparison.OrdinalIgnoreCase))
		{
			section.ArgIntegerLength = length;
			return true;
		}

		if (keyword.Equals("DIMS", StringComparison.OrdinalIgnoreCase) && name.Equals("ARGS", StringComparison.OrdinalIgnoreCase))
		{
			section.ArgStringLength = length;
			return true;
		}

		if (keyword.Equals("DIMF", StringComparison.OrdinalIgnoreCase) && name.Equals("ARGF", StringComparison.OrdinalIgnoreCase))
		{
			section.ArgFloatLength = length;
			return true;
		}

		return false;
	}

	static VariableKind ReadDimKind(string keyword)
	{
		if (keyword.Equals("DIMS", StringComparison.OrdinalIgnoreCase))
			return VariableKind.String;
		if (keyword.Equals("DIMF", StringComparison.OrdinalIgnoreCase))
			return VariableKind.Float;
		return VariableKind.Integer;
	}

	static VariableDimension ReadDimension(int lengthCount)
	{
		return lengthCount switch
		{
			0 => VariableDimension.Scalar,
			1 => VariableDimension.Array1D,
			2 => VariableDimension.Array2D,
			3 => VariableDimension.Array3D,
			_ => throw new FormatException("Variables support up to three dimensions."),
		};
	}

	static bool IsReservedLocalSizingName(string keyword, string name)
	{
		return keyword.Equals("DIM", StringComparison.OrdinalIgnoreCase)
				&& (name.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) || name.Equals("ARG", StringComparison.OrdinalIgnoreCase))
			|| keyword.Equals("DIMS", StringComparison.OrdinalIgnoreCase)
				&& (name.Equals("LOCALS", StringComparison.OrdinalIgnoreCase) || name.Equals("ARGS", StringComparison.OrdinalIgnoreCase))
			|| keyword.Equals("DIMF", StringComparison.OrdinalIgnoreCase)
				&& (name.Equals("LOCALF", StringComparison.OrdinalIgnoreCase) || name.Equals("ARGF", StringComparison.OrdinalIgnoreCase));
	}

	static bool IsDimAttributeKeyword(string value)
	{
		return value.Equals("STATIC", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("DYNAMIC", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("CONST", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("REF", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("OUT", StringComparison.OrdinalIgnoreCase);
	}

	static ModernUserVariableAttributes ApplyDimAttribute(ModernUserVariableAttributes attributes, string keyword)
	{
		if (keyword.Equals("STATIC", StringComparison.OrdinalIgnoreCase))
			return (attributes & ~ModernUserVariableAttributes.Dynamic) | ModernUserVariableAttributes.Static;
		if (keyword.Equals("DYNAMIC", StringComparison.OrdinalIgnoreCase))
			return (attributes & ~ModernUserVariableAttributes.Static) | ModernUserVariableAttributes.Dynamic;
		if (keyword.Equals("CONST", StringComparison.OrdinalIgnoreCase))
			return attributes | ModernUserVariableAttributes.Const | ModernUserVariableAttributes.Static;
		if (keyword.Equals("REF", StringComparison.OrdinalIgnoreCase))
			return (attributes | ModernUserVariableAttributes.Reference) & ~ModernUserVariableAttributes.Static;
		if (keyword.Equals("OUT", StringComparison.OrdinalIgnoreCase))
			return (attributes | ModernUserVariableAttributes.Out | ModernUserVariableAttributes.Reference) & ~ModernUserVariableAttributes.Static;
		return attributes;
	}

	static int FindAssignmentPart(IReadOnlyList<string> parts)
	{
		for (int i = 1; i < parts.Count; i++)
		{
			if (parts[i].Contains("="))
				return i;
		}

		return -1;
	}

	void ReadDefaultValues(IReadOnlyList<string> parts, int assignmentPart, List<SingleTerm> defaults)
	{
		string first = parts[assignmentPart];
		int assignment = first.IndexOf('=');
		if (assignment < 0)
			throw new FormatException("Initial value assignment is malformed.");

		string firstValue = first.Substring(assignment + 1).Trim();
		if (firstValue.Length != 0)
			defaults.Add(ReadConstantDefault(firstValue));

		for (int i = assignmentPart + 1; i < parts.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(parts[i]))
				throw new FormatException("Initial value list contains an empty item.");
			defaults.Add(ReadConstantDefault(parts[i]));
		}

		if (defaults.Count == 0)
			throw new FormatException("Initial value list is empty.");
	}

	SingleTerm ReadConstantDefault(string source)
	{
		if (expressionParser.Parse(source).Restructure(null) is SingleTerm term)
			return term;
		throw new FormatException("Initial values for #DIM must be constant expressions in the modern scaffold.");
	}

	static string ReadSharpKeyword(string source, out string rest)
	{
		int index = 0;
		while (index < source.Length && !char.IsWhiteSpace(source[index]))
			index++;
		rest = index < source.Length ? source.Substring(index).Trim() : "";
		return source.Substring(0, index);
	}

	static int ReadPositiveLength(string source, string keyword)
	{
		string value = source.Trim();
		int comma = value.IndexOf(',');
		if (comma >= 0)
			value = value.Substring(0, comma).Trim();
		if (!int.TryParse(value, out int length) || length <= 0)
			throw new FormatException($"{keyword} needs a positive integer length.");
		return length;
	}

	static List<string> SplitDirectiveArguments(string source)
	{
		var result = new List<string>();
		int start = 0;
		for (int i = 0; i < source.Length; i++)
		{
			if (source[i] == ',')
			{
				result.Add(source.Substring(start, i - start).Trim());
				start = i + 1;
			}
		}
		result.Add(source.Substring(start).Trim());
		return result;
	}

	sealed class LabelSection
	{
		public LabelSection(string name, int startLine, int startBodyLine)
		{
			Name = name;
			StartLine = startLine;
			StartBodyLine = startBodyLine;
			EndLineExclusive = startBodyLine;
			MethodType = EraType.Void;
		}

		public string Name { get; }
		public int StartLine { get; }
		public int StartBodyLine { get; }
		public int EndLineExclusive { get; set; }
		public int LineNumber { get { return StartLine + 1; } }
		public bool IsMethod { get; set; }
		public EraType MethodType { get; set; }
		public string ArgumentSource { get; set; }
		public ModernScriptFunctionMethod Method { get; set; }
		public ModernVariableEvaluator VariableEvaluator { get; set; }
		public List<ModernFunctionArgumentBinding> ArgumentBindings { get; set; } = new();
		public List<ModernUserVariableDefinition> PrivateVariables { get; } = new();
		public int LocalIntegerLength { get; set; }
		public int LocalStringLength { get; set; }
		public int LocalFloatLength { get; set; }
		public int ArgIntegerLength { get; set; }
		public int ArgStringLength { get; set; }
		public int ArgFloatLength { get; set; }
	}
}
