using System;
using System.Globalization;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Functions;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Expressions;

internal sealed class ModernExpressionParser
{
	readonly ModernVariableEvaluator variableEvaluator;
	readonly ModernFunctionEvaluator functionEvaluator;

	public ModernExpressionParser(ModernVariableEvaluator variableEvaluator)
		: this(variableEvaluator, new ModernFunctionEvaluator())
	{
	}

	public ModernExpressionParser(ModernVariableEvaluator variableEvaluator, ModernFunctionEvaluator functionEvaluator)
	{
		this.variableEvaluator = variableEvaluator ?? throw new ArgumentNullException(nameof(variableEvaluator));
		this.functionEvaluator = functionEvaluator ?? throw new ArgumentNullException(nameof(functionEvaluator));
	}

	public AExpression Parse(string source)
	{
		if (source == null)
			throw new ArgumentNullException(nameof(source));

		var reader = new Reader(source);
		var expression = ParseExpression(reader);
		reader.SkipWhiteSpace();
		if (!reader.End)
			throw new FormatException($"Unexpected token at column {reader.Position + 1}: {reader.Current}");
		return expression;
	}

	internal AExpression ParseVariableArgument(string source)
	{
		var expression = Parse(source);
		if (expression.IsString)
			throw new FormatException("Variable array indices must be integer expressions.");
		return expression;
	}

	AExpression ParseExpression(Reader reader)
	{
		return ParseLogicalOr(reader);
	}

	AExpression ParseLogicalOr(Reader reader)
	{
		var left = ParseLogicalAnd(reader);
		while (true)
		{
			reader.SkipWhiteSpace();
			if (!reader.TryConsume("||"))
				return left;
			var right = ParseLogicalAnd(reader);
			left = new ModernLogicalExpression(left, right, "||");
		}
	}

	AExpression ParseLogicalAnd(Reader reader)
	{
		var left = ParseEquality(reader);
		while (true)
		{
			reader.SkipWhiteSpace();
			if (!reader.TryConsume("&&"))
				return left;
			var right = ParseEquality(reader);
			left = new ModernLogicalExpression(left, right, "&&");
		}
	}

	AExpression ParseEquality(Reader reader)
	{
		var left = ParseRelational(reader);
		while (true)
		{
			reader.SkipWhiteSpace();
			if (reader.TryConsume("=="))
			{
				var right = ParseRelational(reader);
				left = new ModernComparisonExpression(left, right, "==");
			}
			else if (reader.TryConsume("!="))
			{
				var right = ParseRelational(reader);
				left = new ModernComparisonExpression(left, right, "!=");
			}
			else
			{
				return left;
			}
		}
	}

	AExpression ParseRelational(Reader reader)
	{
		var left = ParseAdditive(reader);
		while (true)
		{
			reader.SkipWhiteSpace();
			if (reader.TryConsume(">="))
			{
				var right = ParseAdditive(reader);
				left = new ModernComparisonExpression(left, right, ">=");
			}
			else if (reader.TryConsume("<="))
			{
				var right = ParseAdditive(reader);
				left = new ModernComparisonExpression(left, right, "<=");
			}
			else if (reader.TryConsume(">"))
			{
				var right = ParseAdditive(reader);
				left = new ModernComparisonExpression(left, right, ">");
			}
			else if (reader.TryConsume("<"))
			{
				var right = ParseAdditive(reader);
				left = new ModernComparisonExpression(left, right, "<");
			}
			else
			{
				return left;
			}
		}
	}

	AExpression ParseAdditive(Reader reader)
	{
		var left = ParseMultiplicative(reader);
		while (true)
		{
			reader.SkipWhiteSpace();
			if (reader.Current != '+' && reader.Current != '-')
				return left;
			char op = reader.Current;
			reader.Advance();
			var right = ParseMultiplicative(reader);
			left = new ModernBinaryExpression(left, right, op);
		}
	}

	AExpression ParseMultiplicative(Reader reader)
	{
		var left = ParseUnary(reader);
		while (true)
		{
			reader.SkipWhiteSpace();
			if (reader.Current != '*' && reader.Current != '/' && reader.Current != '%')
				return left;
			char op = reader.Current;
			reader.Advance();
			var right = ParseUnary(reader);
			left = new ModernBinaryExpression(left, right, op);
		}
	}

	AExpression ParseUnary(Reader reader)
	{
		reader.SkipWhiteSpace();
		if (reader.Current == '+')
		{
			reader.Advance();
			return ParseUnary(reader);
		}

		if (reader.Current == '-')
		{
			reader.Advance();
			return new ModernUnaryExpression(ParseUnary(reader), '-');
		}

		if (reader.Current == '!')
		{
			reader.Advance();
			return new ModernUnaryExpression(ParseUnary(reader), '!');
		}

		return ParsePrimary(reader);
	}

	AExpression ParsePrimary(Reader reader)
	{
		reader.SkipWhiteSpace();
		if (reader.End)
			throw new FormatException("Expression is empty.");

		if (reader.Current == '(')
		{
			reader.Advance();
			var expression = ParseExpression(reader);
			reader.SkipWhiteSpace();
			if (reader.Current != ')')
				throw new FormatException("Parenthesized expression is not closed.");
			reader.Advance();
			return expression;
		}

		if (reader.Current == '"' || reader.Current == '\'')
			return new SingleStrTerm(ReadQuotedString(reader));

		if (IsNumberStart(reader))
			return ReadNumber(reader);

		if (IsIdentifierStart(reader.Current))
		{
			string identifier = ReadIdentifier(reader);
			reader.SkipWhiteSpace();
			if (reader.Current == '(')
				return ParseFunctionCall(identifier, reader);
			return ModernVariableParser.ReduceVariable(identifier, reader, this, variableEvaluator);
		}

		throw new FormatException($"Unexpected token at column {reader.Position + 1}: {reader.Current}");
	}

	AExpression ParseFunctionCall(string identifier, Reader reader)
	{
		reader.Advance();
		var arguments = new List<AExpression>();
		reader.SkipWhiteSpace();
		if (reader.Current == ')')
		{
			reader.Advance();
			return functionEvaluator.CreateCall(identifier, arguments);
		}

		while (true)
		{
			arguments.Add(ParseExpression(reader));
			reader.SkipWhiteSpace();
			if (reader.Current == ',')
			{
				reader.Advance();
				reader.SkipWhiteSpace();
				if (reader.Current == ')')
					throw new FormatException($"Function {identifier} has an empty argument.");
				continue;
			}

			if (reader.Current != ')')
				throw new FormatException($"Function {identifier} argument list is not closed.");
			reader.Advance();
			return functionEvaluator.CreateCall(identifier, arguments);
		}
	}

	static bool IsNumberStart(Reader reader)
	{
		if (char.IsDigit(reader.Current))
			return true;
		return reader.Current == '.' && reader.HasNext && char.IsDigit(reader.Next);
	}

	static SingleTerm ReadNumber(Reader reader)
	{
		int start = reader.Position;
		bool hasDot = false;
		bool hasExponent = false;
		while (!reader.End)
		{
			char c = reader.Current;
			if (char.IsDigit(c))
			{
				reader.Advance();
			}
			else if (c == '.' && !hasDot && !hasExponent)
			{
				hasDot = true;
				reader.Advance();
			}
			else if ((c == 'e' || c == 'E') && !hasExponent)
			{
				hasExponent = true;
				reader.Advance();
				if (!reader.End && (reader.Current == '+' || reader.Current == '-'))
					reader.Advance();
			}
			else
			{
				break;
			}
		}

		string token = reader.Source.Substring(start, reader.Position - start);
		if (hasDot || hasExponent)
			return new SingleFloatTerm(double.Parse(token, CultureInfo.InvariantCulture));
		return new SingleLongTerm(long.Parse(token, CultureInfo.InvariantCulture));
	}

	static string ReadIdentifier(Reader reader)
	{
		int start = reader.Position;
		reader.Advance();
		while (!reader.End && IsIdentifierPart(reader.Current))
			reader.Advance();
		return reader.Source.Substring(start, reader.Position - start);
	}

	static string ReadQuotedString(Reader reader)
	{
		char quote = reader.Current;
		reader.Advance();
		int start = reader.Position;
		while (!reader.End && reader.Current != quote)
			reader.Advance();
		if (reader.End)
			throw new FormatException("String literal is not closed.");
		string value = reader.Source.Substring(start, reader.Position - start);
		reader.Advance();
		return value;
	}

	static bool IsIdentifierStart(char c)
	{
		return char.IsLetter(c) || c == '_';
	}

	static bool IsIdentifierPart(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_';
	}

	internal sealed class Reader
	{
		public Reader(string source)
		{
			Source = source;
		}

		public string Source { get; }
		public int Position { get; private set; }
		public bool End { get { return Position >= Source.Length; } }
		public char Current { get { return End ? '\0' : Source[Position]; } }
		public bool HasNext { get { return Position + 1 < Source.Length; } }
		public char Next { get { return HasNext ? Source[Position + 1] : '\0'; } }

		public void Advance()
		{
			if (!End)
				Position++;
		}

		public void SkipWhiteSpace()
		{
			while (!End && char.IsWhiteSpace(Current))
				Advance();
		}

		public bool TryConsume(string token)
		{
			if (string.IsNullOrEmpty(token))
				return false;
			if (Position + token.Length > Source.Length)
				return false;
			for (int i = 0; i < token.Length; i++)
			{
				if (Source[Position + i] != token[i])
					return false;
			}
			Position += token.Length;
			return true;
		}

		public string ReadVariableArgument()
		{
			int start = Position;
			char quote = '\0';
			int parenthesisDepth = 0;
			while (!End)
			{
				char c = Current;
				if (quote != '\0')
				{
					if (c == quote)
						quote = '\0';
					Advance();
					continue;
				}

				if (c == '"' || c == '\'')
				{
					quote = c;
					Advance();
					continue;
				}

				if (c == '(')
				{
					parenthesisDepth++;
					Advance();
					continue;
				}

				if (c == ')')
				{
					if (parenthesisDepth == 0)
						break;
					parenthesisDepth--;
					Advance();
					continue;
				}

				if (c == ':')
					break;

				if (parenthesisDepth == 0 && c == ',')
					break;

				if (parenthesisDepth == 0 && IsArgumentTerminator(c))
					break;

				Advance();
			}

			return Source.Substring(start, Position - start).Trim();
		}

		bool IsArgumentTerminator(char c)
		{
			if (c == '>' || c == '<' || c == '=' || c == '!' || c == '&' || c == '|')
				return true;
			if (!IsBinaryOperator(c))
				return false;
			bool left = Position > 0 && char.IsWhiteSpace(Source[Position - 1]);
			bool right = Position + 1 < Source.Length && char.IsWhiteSpace(Source[Position + 1]);
			return left || right;
		}

		static bool IsBinaryOperator(char c)
		{
			return c == '+' || c == '-' || c == '*' || c == '/' || c == '%';
		}
	}
}
