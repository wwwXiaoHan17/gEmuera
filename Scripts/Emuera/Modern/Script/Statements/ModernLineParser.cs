using System;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Functions;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernLineParser
{
	readonly ModernExpressionParser expressionParser;
	readonly ModernVariableEvaluator variableEvaluator;

	public ModernLineParser(ModernVariableEvaluator variableEvaluator)
		: this(variableEvaluator, new ModernFunctionEvaluator())
	{
	}

	public ModernLineParser(ModernVariableEvaluator variableEvaluator, ModernFunctionEvaluator functionEvaluator)
	{
		this.variableEvaluator = variableEvaluator ?? throw new ArgumentNullException(nameof(variableEvaluator));
		expressionParser = new ModernExpressionParser(variableEvaluator, functionEvaluator ?? throw new ArgumentNullException(nameof(functionEvaluator)));
	}

	public ModernStatement Parse(string line)
	{
		if (line == null)
			throw new ArgumentNullException(nameof(line));

		string normalized = NormalizeLine(line);
		if (normalized.Length == 0)
			throw new FormatException("Line is empty.");

		int assignmentIndex = FindTopLevelAssignment(normalized);
		if (assignmentIndex < 0)
			throw new NotSupportedException("Only assignment statements are supported by the modern parser scaffold.");

		string leftText = normalized.Substring(0, assignmentIndex).Trim();
		string rightText = normalized.Substring(assignmentIndex + 1).Trim();
		if (leftText.Length == 0)
			throw new FormatException("Assignment target is missing.");
		if (rightText.Length == 0)
			throw new FormatException("Assignment source is missing.");

		var destination = ModernVariableParser.Parse(leftText, variableEvaluator);
		var source = expressionParser.Parse(rightText);
		return new ModernAssignmentStatement(destination, source);
	}

	public void ExecuteLine(string line, ModernExpressionContext context)
	{
		Parse(line).Execute(context);
	}

	static int FindTopLevelAssignment(string line)
	{
		char quote = '\0';
		for (int i = 0; i < line.Length; i++)
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

			if (c == '=' && IsAssignmentOperator(line, i))
				return i;
		}

		return -1;
	}

	static bool IsAssignmentOperator(string line, int index)
	{
		char prev = index > 0 ? line[index - 1] : '\0';
		char next = index + 1 < line.Length ? line[index + 1] : '\0';
		if (prev == '=' || prev == '!' || prev == '<' || prev == '>' || prev == '\'')
			return false;
		return next != '=';
	}

	internal static string NormalizeLine(string line)
	{
		return StripLineComment(line ?? "").Trim();
	}

	static string StripLineComment(string line)
	{
		char quote = '\0';
		for (int i = 0; i < line.Length; i++)
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

			if (c == ';')
				return line.Substring(0, i);
		}

		return line;
	}
}
