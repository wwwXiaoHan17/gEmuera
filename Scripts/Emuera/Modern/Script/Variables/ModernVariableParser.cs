using System;
using System.Collections.Generic;
using MinorShift.Emuera.Modern.Script.Expressions;

namespace MinorShift.Emuera.Modern.Script.Variables;

internal static class ModernVariableParser
{
	static readonly AExpression ZeroTerm = new SingleLongTerm(0);

	public static bool IsVariable(string source, ModernVariableEvaluator evaluator)
	{
		if (string.IsNullOrWhiteSpace(source) || evaluator == null)
			return false;
		string name = source.Split(':', 2)[0].Trim();
		return evaluator.TryGetToken(name, out _);
	}

	public static ModernVariableTerm Parse(string source, ModernVariableEvaluator evaluator)
	{
		var expressionParser = new ModernExpressionParser(evaluator);
		if (expressionParser.Parse(source) is ModernVariableTerm term)
			return term;
		throw new FormatException("Expression is not a variable term.");
	}

	internal static ModernVariableTerm ReduceVariable(
		string identifier,
		ModernExpressionParser.Reader reader,
		ModernExpressionParser expressionParser,
		ModernVariableEvaluator evaluator)
	{
		if (!evaluator.TryGetToken(identifier, out var token))
			throw new KeyNotFoundException($"Unknown variable: {identifier}");

		var arguments = new List<AExpression>();
		while (true)
		{
			reader.SkipWhiteSpace();
			if (reader.Current != ':')
				break;

			reader.Advance();
			string argumentSource = reader.ReadVariableArgument();
			if (string.IsNullOrWhiteSpace(argumentSource))
				throw new FormatException($"Missing index after ':' for {identifier}.");
			arguments.Add(expressionParser.ParseVariableArgument(argumentSource));
		}

		return ReduceVariable(token, arguments);
	}

	public static ModernVariableTerm ReduceVariable(ModernVariableToken token, IReadOnlyList<AExpression> arguments)
	{
		if (token == null)
			throw new ArgumentNullException(nameof(token));
		arguments ??= Array.Empty<AExpression>();

		if (token.IsCharacterData)
			return ReduceCharacterVariable(token, arguments);

		return token.Dimension switch
		{
			VariableDimension.Scalar => ReduceScalar(token, arguments),
			VariableDimension.Array1D => ReduceArray1D(token, arguments),
			VariableDimension.Array2D => ReduceFixedDimension(token, arguments, 2),
			VariableDimension.Array3D => ReduceFixedDimension(token, arguments, 3),
			_ => throw new NotSupportedException($"{token.Name} has unsupported dimension {token.Dimension}."),
		};
	}

	static ModernVariableTerm ReduceScalar(ModernVariableToken token, IReadOnlyList<AExpression> arguments)
	{
		if (arguments.Count != 0)
			throw new FormatException($"{token.Name} is scalar and does not accept indices.");
		return new ModernVariableTerm(token, Array.Empty<AExpression>());
	}

	static ModernVariableTerm ReduceCharacterVariable(ModernVariableToken token, IReadOnlyList<AExpression> arguments)
	{
		return token.Dimension switch
		{
			VariableDimension.Scalar => ReduceCharacterScalar(token, arguments),
			VariableDimension.Array1D => ReduceCharacterArray1D(token, arguments),
			VariableDimension.Array2D => ReduceCharacterFixedDimension(token, arguments, 3),
			VariableDimension.Array3D => ReduceCharacterFixedDimension(token, arguments, 4),
			_ => throw new NotSupportedException($"{token.Name} has unsupported dimension {token.Dimension}."),
		};
	}

	static ModernVariableTerm ReduceCharacterScalar(ModernVariableToken token, IReadOnlyList<AExpression> arguments)
	{
		if (arguments.Count > 1)
			throw new FormatException($"{token.Name} is a character scalar and accepts only a character index.");
		var terms = new AExpression[1];
		terms[0] = arguments.Count == 0 ? TargetTerm() : arguments[0];
		return new ModernVariableTerm(token, terms);
	}

	static ModernVariableTerm ReduceCharacterArray1D(ModernVariableToken token, IReadOnlyList<AExpression> arguments)
	{
		if (arguments.Count > 2)
			throw new FormatException($"{token.Name} is a character array and accepts only character and element indices.");
		var terms = new AExpression[2];
		if (arguments.Count == 0)
		{
			terms[0] = TargetTerm();
			terms[1] = ZeroTerm;
		}
		else if (arguments.Count == 1)
		{
			terms[0] = TargetTerm();
			terms[1] = arguments[0];
		}
		else
		{
			terms[0] = arguments[0];
			terms[1] = arguments[1];
		}
		return new ModernVariableTerm(token, terms);
	}

	static ModernVariableTerm ReduceCharacterFixedDimension(ModernVariableToken token, IReadOnlyList<AExpression> arguments, int requiredCount)
	{
		if (arguments.Count == 0)
			throw new FormatException($"{token.Name} needs {requiredCount} indices.");
		if (arguments.Count != requiredCount)
			throw new FormatException($"{token.Name} needs exactly {requiredCount} indices.");
		var terms = new AExpression[requiredCount];
		for (int i = 0; i < requiredCount; i++)
			terms[i] = arguments[i];
		return new ModernVariableTerm(token, terms);
	}

	static AExpression TargetTerm()
	{
		return TargetCharacterTerm.Instance;
	}

	sealed class TargetCharacterTerm : AExpression
	{
		public static readonly TargetCharacterTerm Instance = new();

		TargetCharacterTerm()
			: base(EraType.Integer)
		{
		}

		public override long GetIntValue(ModernExpressionContext context)
		{
			return GlobalStatic.VEvaluator?.TARGET ?? 0;
		}
	}

	static ModernVariableTerm ReduceArray1D(ModernVariableToken token, IReadOnlyList<AExpression> arguments)
	{
		if (arguments.Count > 1)
			throw new FormatException($"{token.Name} is one-dimensional and accepts only one index.");
		var terms = new AExpression[1];
		terms[0] = arguments.Count == 0 ? ZeroTerm : arguments[0];
		return new ModernVariableTerm(token, terms);
	}

	static ModernVariableTerm ReduceFixedDimension(ModernVariableToken token, IReadOnlyList<AExpression> arguments, int dimension)
	{
		if (arguments.Count == 0)
			throw new FormatException($"{token.Name} needs {dimension} indices.");
		if (arguments.Count != dimension)
			throw new FormatException($"{token.Name} needs exactly {dimension} indices.");
		var terms = new AExpression[dimension];
		for (int i = 0; i < dimension; i++)
			terms[i] = arguments[i];
		return new ModernVariableTerm(token, terms);
	}
}
