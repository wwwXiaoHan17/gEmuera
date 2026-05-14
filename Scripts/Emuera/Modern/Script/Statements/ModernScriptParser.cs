using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Functions;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Statements;

internal sealed class ModernScriptParser
{
	readonly ModernLineParser lineParser;
	readonly ModernExpressionParser expressionParser;
	readonly ModernVariableEvaluator variableEvaluator;
	readonly ModernFunctionEvaluator functionEvaluator;

	public ModernScriptParser(ModernVariableEvaluator variableEvaluator)
		: this(variableEvaluator, new ModernFunctionEvaluator())
	{
	}

	public ModernScriptParser(ModernVariableEvaluator variableEvaluator, ModernFunctionEvaluator functionEvaluator)
	{
		this.variableEvaluator = variableEvaluator ?? throw new ArgumentNullException(nameof(variableEvaluator));
		this.functionEvaluator = functionEvaluator ?? throw new ArgumentNullException(nameof(functionEvaluator));
		lineParser = new ModernLineParser(variableEvaluator, functionEvaluator);
		expressionParser = new ModernExpressionParser(variableEvaluator, functionEvaluator);
	}

	public ModernBlockStatement ParseLines(IEnumerable<string> sourceLines)
	{
		if (sourceLines == null)
			throw new ArgumentNullException(nameof(sourceLines));
		var lines = new List<string>(sourceLines);
		int index = 0;
		var block = ParseBlock(lines, ref index, out var stopLine);
		if (stopLine != null)
			throw new FormatException($"Unexpected {ReadKeyword(stopLine)} without matching block opener.");
		return block;
	}

	public void ExecuteLines(IEnumerable<string> sourceLines, ModernExpressionContext context)
	{
		ParseLines(sourceLines).Execute(context);
	}

	ModernBlockStatement ParseBlock(IReadOnlyList<string> lines, ref int index, out string stopLine)
	{
		var statements = new List<ModernStatement>();
		stopLine = null;

		while (index < lines.Count)
		{
			string line = ModernLineParser.NormalizeLine(lines[index]);
			if (line.Length == 0)
			{
				index++;
				continue;
			}

			string keyword = ReadKeyword(line);
			if (IsBlockTerminator(keyword))
			{
				stopLine = line;
				return new ModernBlockStatement(statements);
			}

			if (IsKeyword(keyword, "BREAK"))
			{
				EnsureNoArguments(line, keyword);
				statements.Add(new ModernBreakStatement());
				index++;
				continue;
			}

			if (IsKeyword(keyword, "CONTINUE"))
			{
				EnsureNoArguments(line, keyword);
				statements.Add(new ModernContinueStatement());
				index++;
				continue;
			}

			if (IsKeyword(keyword, "RETURN"))
			{
				statements.Add(ParseReturn(line, keyword, ModernReturnKind.Integer));
				index++;
				continue;
			}

			if (IsKeyword(keyword, "RETURNF"))
			{
				statements.Add(ParseReturn(line, keyword, ModernReturnKind.Value));
				index++;
				continue;
			}

			if (IsKeyword(keyword, "IF"))
			{
				statements.Add(ParseIf(lines, ref index));
				continue;
			}

			if (IsKeyword(keyword, "SIF"))
			{
				statements.Add(ParseSif(lines, ref index));
				continue;
			}

			if (IsKeyword(keyword, "SELECTCASE"))
			{
				statements.Add(ParseSelectCase(lines, ref index));
				continue;
			}

			if (IsKeyword(keyword, "WHILE"))
			{
				statements.Add(ParseWhile(lines, ref index));
				continue;
			}

			if (IsKeyword(keyword, "DO"))
			{
				statements.Add(ParseDoLoop(lines, ref index));
				continue;
			}

			if (IsKeyword(keyword, "FOR"))
			{
				statements.Add(ParseFor(lines, ref index));
				continue;
			}

			if (IsKeyword(keyword, "REPEAT"))
			{
				statements.Add(ParseRepeat(lines, ref index));
				continue;
			}

			statements.Add(lineParser.Parse(line));
			index++;
		}

		return new ModernBlockStatement(statements);
	}

	ModernIfStatement ParseIf(IReadOnlyList<string> lines, ref int index)
	{
		string line = ModernLineParser.NormalizeLine(lines[index]);
		var branches = new List<ModernIfBranch>();
		branches.Add(new ModernIfBranch(ParseConditionAfterKeyword(line, "IF"), ParseNextBlock(lines, ref index, "IF")));

		ModernBlockStatement elseBlock = null;
		while (index < lines.Count)
		{
			string stopLine = ModernLineParser.NormalizeLine(lines[index]);
			string keyword = ReadKeyword(stopLine);
			if (IsKeyword(keyword, "ELSEIF"))
			{
				branches.Add(new ModernIfBranch(ParseConditionAfterKeyword(stopLine, "ELSEIF"), ParseNextBlock(lines, ref index, "ELSEIF")));
				continue;
			}

			if (IsKeyword(keyword, "ELSE"))
			{
				string rest = stopLine.Substring(keyword.Length).Trim();
				if (rest.Length != 0)
					throw new FormatException("ELSE does not accept a condition.");
				index++;
				elseBlock = ParseBlock(lines, ref index, out stopLine);
				if (stopLine == null || !IsKeyword(ReadKeyword(stopLine), "ENDIF"))
					throw new FormatException("ELSE block is missing ENDIF.");
				index++;
				return new ModernIfStatement(branches, elseBlock);
			}

			if (IsKeyword(keyword, "ENDIF"))
			{
				index++;
				return new ModernIfStatement(branches, elseBlock);
			}

			throw new FormatException($"Unexpected IF terminator: {stopLine}");
		}

		throw new FormatException("IF block is missing ENDIF.");
	}

	ModernSifStatement ParseSif(IReadOnlyList<string> lines, ref int index)
	{
		string line = ModernLineParser.NormalizeLine(lines[index]);
		var condition = ParseConditionAfterKeyword(line, "SIF");
		index++;
		if (index >= lines.Count)
			throw new FormatException("SIF needs one following statement.");

		string nextLine = ModernLineParser.NormalizeLine(lines[index]);
		if (nextLine.Length == 0)
			throw new FormatException("SIF cannot target an empty line.");

		var statement = ParseSingleStatementLine(nextLine, "SIF");
		index++;
		return new ModernSifStatement(condition, statement);
	}

	ModernStatement ParseSingleStatementLine(string line, string ownerKeyword)
	{
		string keyword = ReadKeyword(line);
		if (IsBlockTerminator(keyword)
			|| IsKeyword(keyword, "IF")
			|| IsKeyword(keyword, "SIF")
			|| IsKeyword(keyword, "SELECTCASE")
			|| IsKeyword(keyword, "WHILE")
			|| IsKeyword(keyword, "DO")
			|| IsKeyword(keyword, "FOR")
			|| IsKeyword(keyword, "REPEAT"))
			throw new FormatException($"{ownerKeyword} cannot target block control statement: {keyword}.");

		if (IsKeyword(keyword, "BREAK"))
		{
			EnsureNoArguments(line, keyword);
			return new ModernBreakStatement();
		}

		if (IsKeyword(keyword, "CONTINUE"))
		{
			EnsureNoArguments(line, keyword);
			return new ModernContinueStatement();
		}

		if (IsKeyword(keyword, "RETURN"))
			return ParseReturn(line, keyword, ModernReturnKind.Integer);

		if (IsKeyword(keyword, "RETURNF"))
			return ParseReturn(line, keyword, ModernReturnKind.Value);

		return lineParser.Parse(line);
	}

	ModernReturnStatement ParseReturn(string line, string keyword, ModernReturnKind kind)
	{
		string rest = line.Substring(keyword.Length).Trim();
		if (rest.Length == 0)
			return new ModernReturnStatement(kind, Array.Empty<AExpression>());

		var parts = SplitTopLevelComma(rest);
		var values = new List<AExpression>(parts.Count);
		for (int i = 0; i < parts.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(parts[i]))
				throw new FormatException($"{keyword} has an empty value.");
			values.Add(expressionParser.Parse(parts[i]));
		}

		if (kind == ModernReturnKind.Value && values.Count > 1)
			throw new FormatException("RETURNF accepts at most one expression.");
		return new ModernReturnStatement(kind, values);
	}

	ModernSelectCaseStatement ParseSelectCase(IReadOnlyList<string> lines, ref int index)
	{
		string line = ModernLineParser.NormalizeLine(lines[index]);
		string selectorText = line.Substring(ReadKeyword(line).Length).Trim();
		if (selectorText.Length == 0)
			throw new FormatException("SELECTCASE expression is missing.");

		var selector = expressionParser.Parse(selectorText);
		var branches = new List<ModernCaseBranch>();
		bool hasDefault = false;
		index++;

		while (index < lines.Count)
		{
			string branchLine = ModernLineParser.NormalizeLine(lines[index]);
			if (branchLine.Length == 0)
			{
				index++;
				continue;
			}

			string keyword = ReadKeyword(branchLine);
			if (IsKeyword(keyword, "CASE"))
			{
				if (hasDefault)
					throw new FormatException("CASE cannot appear after CASEELSE.");
				var cases = ParseCaseExpressions(branchLine.Substring(keyword.Length).Trim());
				index++;
				var body = ParseBlock(lines, ref index, out var stopLine);
				if (stopLine == null)
					throw new FormatException("SELECTCASE block is missing ENDSELECT.");
				branches.Add(new ModernCaseBranch(cases, body, false));
				continue;
			}

			if (IsKeyword(keyword, "CASEELSE"))
			{
				if (hasDefault)
					throw new FormatException("SELECTCASE cannot contain more than one CASEELSE.");
				EnsureNoArguments(branchLine, keyword);
				hasDefault = true;
				index++;
				var body = ParseBlock(lines, ref index, out var stopLine);
				if (stopLine == null)
					throw new FormatException("SELECTCASE block is missing ENDSELECT.");
				branches.Add(new ModernCaseBranch(Array.Empty<ModernCaseExpression>(), body, true));
				continue;
			}

			if (IsKeyword(keyword, "ENDSELECT"))
			{
				EnsureNoArguments(branchLine, keyword);
				index++;
				return new ModernSelectCaseStatement(selector, branches);
			}

			throw new FormatException($"SELECTCASE expects CASE, CASEELSE, or ENDSELECT but found: {branchLine}");
		}

		throw new FormatException("SELECTCASE block is missing ENDSELECT.");
	}

	ModernWhileStatement ParseWhile(IReadOnlyList<string> lines, ref int index)
	{
		string line = ModernLineParser.NormalizeLine(lines[index]);
		var condition = ParseConditionAfterKeyword(line, "WHILE");
		index++;
		var body = ParseBlock(lines, ref index, out var stopLine);
		if (stopLine == null)
			throw new FormatException("WHILE block is missing WEND.");
		if (!IsKeyword(ReadKeyword(stopLine), "WEND"))
			throw new FormatException($"Unexpected WHILE terminator: {stopLine}");
		index++;
		return new ModernWhileStatement(condition, body);
	}

	ModernDoLoopStatement ParseDoLoop(IReadOnlyList<string> lines, ref int index)
	{
		string line = ModernLineParser.NormalizeLine(lines[index]);
		EnsureNoArguments(line, ReadKeyword(line));
		index++;
		var body = ParseBlock(lines, ref index, out var stopLine);
		if (stopLine == null)
			throw new FormatException("DO block is missing LOOP.");
		if (!IsKeyword(ReadKeyword(stopLine), "LOOP"))
			throw new FormatException($"Unexpected DO terminator: {stopLine}");

		var condition = ParseConditionAfterKeyword(stopLine, "LOOP");
		index++;
		return new ModernDoLoopStatement(body, condition);
	}

	ModernForStatement ParseFor(IReadOnlyList<string> lines, ref int index)
	{
		string line = ModernLineParser.NormalizeLine(lines[index]);
		var arguments = SplitTopLevelComma(line.Substring(ReadKeyword(line).Length).Trim());
		if (arguments.Count < 3 || arguments.Count > 4)
			throw new FormatException("FOR needs a counter, start, end, and optional step.");
		if (string.IsNullOrWhiteSpace(arguments[0]))
			throw new FormatException("FOR counter is missing.");
		if (string.IsNullOrWhiteSpace(arguments[2]))
			throw new FormatException("FOR end value is missing.");

		var counter = ModernVariableParser.Parse(arguments[0], variableEvaluator);
		if (!counter.Identifier.IsInteger)
			throw new FormatException("FOR counter must be an integer variable.");
		if (counter.Identifier.IsConst)
			throw new FormatException("FOR counter must be changeable.");

		AExpression start = string.IsNullOrWhiteSpace(arguments[1]) ? new SingleLongTerm(0) : expressionParser.Parse(arguments[1]);
		AExpression end = expressionParser.Parse(arguments[2]);
		AExpression step = arguments.Count > 3 && !string.IsNullOrWhiteSpace(arguments[3])
			? expressionParser.Parse(arguments[3])
			: new SingleLongTerm(1);

		if (!start.IsInteger || !end.IsInteger || !step.IsInteger)
			throw new FormatException("FOR start, end, and step must be integer expressions.");

		index++;
		var body = ParseBlock(lines, ref index, out var stopLine);
		if (stopLine == null)
			throw new FormatException("FOR block is missing NEXT.");
		if (!IsKeyword(ReadKeyword(stopLine), "NEXT"))
			throw new FormatException($"Unexpected FOR terminator: {stopLine}");
		index++;
		return new ModernForStatement(counter, start, end, step, body);
	}

	ModernForStatement ParseRepeat(IReadOnlyList<string> lines, ref int index)
	{
		string line = ModernLineParser.NormalizeLine(lines[index]);
		string countText = line.Substring(ReadKeyword(line).Length).Trim();
		if (countText.Length == 0)
			throw new FormatException("REPEAT count is missing.");

		AExpression count = expressionParser.Parse(countText);
		if (!count.IsInteger)
			throw new FormatException("REPEAT count must be an integer expression.");

		var counter = ModernVariableParser.Parse("COUNT", variableEvaluator);
		index++;
		var body = ParseBlock(lines, ref index, out var stopLine);
		if (stopLine == null)
			throw new FormatException("REPEAT block is missing REND.");
		if (!IsKeyword(ReadKeyword(stopLine), "REND"))
			throw new FormatException($"Unexpected REPEAT terminator: {stopLine}");
		index++;

		return new ModernForStatement(counter, new SingleLongTerm(0), count, new SingleLongTerm(1), body);
	}

	ModernBlockStatement ParseNextBlock(IReadOnlyList<string> lines, ref int index, string keyword)
	{
		index++;
		var body = ParseBlock(lines, ref index, out var stopLine);
		if (stopLine == null)
			throw new FormatException($"{keyword} block is missing ENDIF.");
		return body;
	}

	AExpression ParseConditionAfterKeyword(string line, string keyword)
	{
		string conditionText = line.Substring(keyword.Length).Trim();
		if (conditionText.EndsWith("THEN", StringComparison.OrdinalIgnoreCase))
			conditionText = conditionText.Substring(0, conditionText.Length - 4).TrimEnd();
		if (conditionText.Length == 0)
			throw new FormatException($"{keyword} condition is missing.");
		var condition = expressionParser.Parse(conditionText);
		if (condition.IsString)
			throw new FormatException($"{keyword} condition cannot be a string expression.");
		return condition;
	}

	static bool IsBlockTerminator(string keyword)
	{
		return IsKeyword(keyword, "ELSEIF")
			|| IsKeyword(keyword, "ELSE")
			|| IsKeyword(keyword, "ENDIF")
			|| IsKeyword(keyword, "CASE")
			|| IsKeyword(keyword, "CASEELSE")
			|| IsKeyword(keyword, "ENDSELECT")
			|| IsKeyword(keyword, "WEND")
			|| IsKeyword(keyword, "LOOP")
			|| IsKeyword(keyword, "NEXT")
			|| IsKeyword(keyword, "REND");
	}

	List<ModernCaseExpression> ParseCaseExpressions(string source)
	{
		if (string.IsNullOrWhiteSpace(source))
			throw new FormatException("CASE expression is missing.");
		var parts = SplitTopLevelComma(source);
		var expressions = new List<ModernCaseExpression>(parts.Count);
		for (int i = 0; i < parts.Count; i++)
		{
			if (string.IsNullOrWhiteSpace(parts[i]))
				throw new FormatException("CASE expression is missing.");
			expressions.Add(ParseCaseExpression(parts[i]));
		}

		return expressions;
	}

	ModernCaseExpression ParseCaseExpression(string source)
	{
		string text = source.Trim();
		if (StartsWithKeyword(text, "IS"))
		{
			string rest = text.Substring(2).TrimStart();
			string op = ReadCaseOperator(rest, out var expressionStart);
			string expressionText = rest.Substring(expressionStart).TrimStart();
			if (expressionText.Length == 0)
				throw new FormatException("CASE IS expression is missing.");
			return new ModernCaseExpression(op, expressionParser.Parse(expressionText));
		}

		int toIndex = IndexOfTopLevelKeyword(text, "TO");
		if (toIndex >= 0)
		{
			string left = text.Substring(0, toIndex).Trim();
			string right = text.Substring(toIndex + 2).Trim();
			if (left.Length == 0 || right.Length == 0)
				throw new FormatException("CASE TO needs both range endpoints.");
			return new ModernCaseExpression(expressionParser.Parse(left), expressionParser.Parse(right));
		}

		return new ModernCaseExpression(expressionParser.Parse(text));
	}

	static List<string> SplitTopLevelComma(string source)
	{
		var result = new List<string>();
		int start = 0;
		int depth = 0;
		char quote = '\0';

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
				if (depth > 0)
					depth--;
				continue;
			}

			if (c == ',' && depth == 0)
			{
				result.Add(source.Substring(start, i - start).Trim());
				start = i + 1;
			}
		}

		result.Add(source.Substring(start).Trim());
		return result;
	}

	static int IndexOfTopLevelKeyword(string source, string keyword)
	{
		int depth = 0;
		char quote = '\0';
		for (int i = 0; i <= source.Length - keyword.Length; i++)
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
				if (depth > 0)
					depth--;
				continue;
			}

			if (depth == 0
				&& string.Compare(source, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0
				&& IsKeywordBoundary(source, i - 1)
				&& IsKeywordBoundary(source, i + keyword.Length))
				return i;
		}

		return -1;
	}

	static bool StartsWithKeyword(string source, string keyword)
	{
		return source.Length >= keyword.Length
			&& string.Compare(source, 0, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0
			&& IsKeywordBoundary(source, keyword.Length);
	}

	static string ReadCaseOperator(string source, out int nextIndex)
	{
		string[] operators = { ">=", "<=", "==", "!=", "<>", "=", ">", "<" };
		for (int i = 0; i < operators.Length; i++)
		{
			string op = operators[i];
			if (source.StartsWith(op, StringComparison.Ordinal))
			{
				nextIndex = op.Length;
				return op;
			}
		}

		throw new FormatException("CASE IS needs a comparison operator.");
	}

	static bool IsKeywordBoundary(string source, int index)
	{
		if (index < 0 || index >= source.Length)
			return true;
		char c = source[index];
		return !char.IsLetterOrDigit(c) && c != '_';
	}

	static string ReadKeyword(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
			return "";
		int i = 0;
		while (i < line.Length && !char.IsWhiteSpace(line[i]))
			i++;
		return line.Substring(0, i);
	}

	static bool IsKeyword(string actual, string expected)
	{
		return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
	}

	static void EnsureNoArguments(string line, string keyword)
	{
		string rest = line.Substring(keyword.Length).Trim();
		if (rest.Length != 0)
			throw new FormatException($"{keyword} does not accept arguments.");
	}
}
