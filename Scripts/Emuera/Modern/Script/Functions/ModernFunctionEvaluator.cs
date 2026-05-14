using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MinorShift.Emuera.Modern.Script.Expressions;
using MinorShift.Emuera.Modern.Script.Statements;
using MinorShift.Emuera.Modern.Script.Variables;

namespace MinorShift.Emuera.Modern.Script.Functions;

internal sealed class ModernFunctionEvaluator
{
	readonly Dictionary<string, ModernFunctionMethod> methods = new(StringComparer.OrdinalIgnoreCase);

	public ModernFunctionEvaluator()
	{
		RegisterBuiltIns();
	}

	public bool TryGetMethod(string name, out ModernFunctionMethod method)
	{
		return methods.TryGetValue(name, out method);
	}

	public ModernFunctionCallExpression CreateCall(string name, IReadOnlyList<AExpression> arguments)
	{
		if (!TryGetMethod(name, out var method))
			throw new KeyNotFoundException($"Unknown function: {name}");
		return new ModernFunctionCallExpression(method, arguments);
	}

	public IReadOnlyDictionary<string, ModernFunctionMethod> Methods { get { return methods; } }

	public void Register(ModernFunctionMethod method)
	{
		if (method == null)
			throw new ArgumentNullException(nameof(method));
		methods[method.Name] = method;
	}

	public ModernScriptFunctionMethod RegisterScriptFunction(
		string name,
		EraType returnType,
		ModernBlockStatement body,
		ModernVariableEvaluator variableEvaluator,
		int minArgumentCount = 0,
		int maxArgumentCount = int.MaxValue,
		ModernVariableSizing sizing = null,
		int localIntegerLength = 0,
		int localStringLength = 0,
		int localFloatLength = 0,
		int argIntegerLength = 0,
		int argStringLength = 0,
		int argFloatLength = 0,
		IEnumerable<ModernUserVariableDefinition> privateVariableDefinitions = null,
		IEnumerable<ModernFunctionArgumentBinding> argumentBindings = null)
	{
		var method = new ModernScriptFunctionMethod(
			name,
			returnType,
			body,
			variableEvaluator,
			minArgumentCount,
			maxArgumentCount,
			sizing,
			localIntegerLength,
			localStringLength,
			localFloatLength,
			argIntegerLength,
			argStringLength,
			argFloatLength,
			privateVariableDefinitions,
			argumentBindings);
		Register(method);
		return method;
	}

	void RegisterBuiltIns()
	{
		Register(new ToFloatMethod());
		Register(new ToStrfMethod());
		Register(new AbsMethod());
		Register(new PowerMethod());
		Register(new SqrtMethod());
		Register(new CbrtMethod());
		Register(new LogMethod("LOG", Math.E));
		Register(new LogMethod("LOG10", 10.0d));
		Register(new ExpMethod());
		Register(new SignMethod());
		Register(new TrigMethod("SIN", Math.Sin));
		Register(new TrigMethod("COS", Math.Cos));
		Register(new TrigMethod("TAN", Math.Tan));
		Register(new TrigMethod("ASIN", Math.Asin));
		Register(new TrigMethod("ACOS", Math.Acos));
		Register(new TrigMethod("ATAN", Math.Atan));
		Register(new RoundLikeMethod("FLOOR", Math.Floor));
		Register(new RoundLikeMethod("CEIL", Math.Ceiling));
		Register(new RoundLikeMethod("ROUND", Math.Round));
		Register(new UncheckedBinaryMethod("UNCHECKED_ADD", UncheckedBinaryOperation.Add));
		Register(new UncheckedBinaryMethod("UNCHECKED_SUB", UncheckedBinaryOperation.Subtract));
		Register(new UncheckedBinaryMethod("UNCHECKED_MUL", UncheckedBinaryOperation.Multiply));
		Register(new UncheckedNegateMethod());
		Register(new ArgLengthMethod("ARGLEN"));
		Register(new ArgLengthMethod("GETARGCOUNT"));
		Register(new RegexpMatchMethod());
		Register(new GetMemoryUsageMethod());
		Register(new ClearMemoryMethod());
		Register(new ExistVarMethod());
		Register(new ExistFunctionMethod(this));
		Register(new EnumNameMethod("ENUMFUNCBEGINSWITH", this, EnumNameTarget.Function, EnumNameAction.BeginsWith));
		Register(new EnumNameMethod("ENUMFUNCENDSWITH", this, EnumNameTarget.Function, EnumNameAction.EndsWith));
		Register(new EnumNameMethod("ENUMFUNCWITH", this, EnumNameTarget.Function, EnumNameAction.Contains));
		Register(new EnumNameMethod("ENUMVARBEGINSWITH", this, EnumNameTarget.Variable, EnumNameAction.BeginsWith));
		Register(new EnumNameMethod("ENUMVARENDSWITH", this, EnumNameTarget.Variable, EnumNameAction.EndsWith));
		Register(new EnumNameMethod("ENUMVARWITH", this, EnumNameTarget.Variable, EnumNameAction.Contains));
		Register(new EnumFilesMethod());
		Register(new ExistFileMethod());
		Register(new GetVarMethod("GETVAR", EraType.Integer));
		Register(new GetVarMethod("GETVARF", EraType.Float));
		Register(new GetVarMethod("GETVARS", EraType.String));
		Register(new SetVarMethod());
		Register(new BitSetMethod());
		Register(new BitGetMethod());
		Register(new BitToggleMethod());
		Register(new MapManagementMethod("MAP_CREATE", MapManagementOperation.Create));
		Register(new MapManagementMethod("MAP_EXIST", MapManagementOperation.Check));
		Register(new MapManagementMethod("MAP_RELEASE", MapManagementOperation.Release));
		Register(new MapGetMethod());
		Register(new MapDataOperationMethod("MAP_CLEAR", MapDataOperation.Clear));
		Register(new MapDataOperationMethod("MAP_SIZE", MapDataOperation.Size));
		Register(new MapDataOperationMethod("MAP_HAS", MapDataOperation.Has));
		Register(new MapDataOperationMethod("MAP_SET", MapDataOperation.Set));
		Register(new MapDataOperationMethod("MAP_REMOVE", MapDataOperation.Remove));
		Register(new MapGetStringListMethod("MAP_GETKEYS", MapStringListOperation.Keys));
		Register(new MapGetStringListMethod("MAP_VALUES", MapStringListOperation.Values));
		Register(new MapToXmlMethod());
		Register(new MapFromXmlMethod());
		Register(new MapMergeMethod());
		Register(new MapRemoveIfMethod());
		Register(new MapFindKeyMethod());
		Register(new MapToStringMethod());
		Register(new MapFromStringMethod());
	}

	sealed class ToFloatMethod : ModernFunctionMethod
	{
		public ToFloatMethod() : base("TOFLOAT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Float;
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return ToDouble(arguments[0], context);
		}
	}

	sealed class ToStrfMethod : ModernFunctionMethod
	{
		public ToStrfMethod() : base("TOSTRF", 1, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.String;
		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			double value = ToDouble(arguments[0], context);
			if (arguments.Count < 2)
				return value.ToString(CultureInfo.InvariantCulture);
			string format = arguments[1].GetStrValue(context);
			try
			{
				return value.ToString(format, CultureInfo.InvariantCulture);
			}
			catch (FormatException)
			{
				throw new FormatException($"{Name} received an invalid format string.");
			}
		}
	}

	sealed class AbsMethod : ModernFunctionMethod
	{
		public AbsMethod() : base("ABS", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Abs(ToLong(arguments[0], context));
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Abs(ToDouble(arguments[0], context));
	}

	sealed class PowerMethod : ModernFunctionMethod
	{
		public PowerMethod() : base("POWER", 2, 2) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Pow(ToLong(arguments[0], context), ToLong(arguments[1], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Pow(ToDouble(arguments[0], context), ToDouble(arguments[1], context));
		}
	}

	sealed class SqrtMethod : ModernFunctionMethod
	{
		public SqrtMethod() : base("SQRT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Sqrt(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Sqrt(ToDouble(arguments[0], context));
		}
	}

	sealed class CbrtMethod : ModernFunctionMethod
	{
		public CbrtMethod() : base("CBRT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Pow(ToLong(arguments[0], context), 1.0d / 3.0d));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Pow(ToDouble(arguments[0], context), 1.0d / 3.0d);
		}
	}

	sealed class LogMethod : ModernFunctionMethod
	{
		readonly double logBase;
		public LogMethod(string name, double logBase) : base(name, 1, 1)
		{
			this.logBase = logBase;
		}
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Calculate(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Calculate(ToDouble(arguments[0], context));
		}
		double Calculate(double value)
		{
			if (value <= 0)
				throw new ArgumentOutOfRangeException(nameof(value), $"{Name} argument must be greater than zero.");
			return logBase == Math.E ? Math.Log(value) : Math.Log10(value);
		}
	}

	sealed class ExpMethod : ModernFunctionMethod
	{
		public ExpMethod() : base("EXPONENT", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, Math.Exp(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Math.Exp(ToDouble(arguments[0], context));
		}
	}

	sealed class SignMethod : ModernFunctionMethod
	{
		public SignMethod() : base("SIGN", 1, 1) { }
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Sign(ToLong(arguments[0], context));
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments) => Math.Sign(ToDouble(arguments[0], context));
	}

	sealed class TrigMethod : ModernFunctionMethod
	{
		readonly Func<double, double> func;
		public TrigMethod(string name, Func<double, double> func) : base(name, 1, 1)
		{
			this.func = func;
		}
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => HasFloatArg(arguments) ? EraType.Float : EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, func(ToLong(arguments[0], context)));
		}
		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return func(ToDouble(arguments[0], context));
		}
	}

	sealed class RoundLikeMethod : ModernFunctionMethod
	{
		readonly Func<double, double> func;
		public RoundLikeMethod(string name, Func<double, double> func) : base(name, 1, 1)
		{
			this.func = func;
		}
		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments) => EraType.Integer;
		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return CheckedDoubleToLong(Name, func(ToDouble(arguments[0], context)));
		}
	}

	enum UncheckedBinaryOperation
	{
		Add,
		Subtract,
		Multiply,
	}

	sealed class UncheckedBinaryMethod : ModernFunctionMethod
	{
		readonly UncheckedBinaryOperation operation;

		public UncheckedBinaryMethod(string name, UncheckedBinaryOperation operation) : base(name, 2, 2)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long left = arguments[0].GetIntValue(context);
			long right = arguments[1].GetIntValue(context);
			return operation switch
			{
				UncheckedBinaryOperation.Add => unchecked(left + right),
				UncheckedBinaryOperation.Subtract => unchecked(left - right),
				_ => unchecked(left * right),
			};
		}
	}

	sealed class UncheckedNegateMethod : ModernFunctionMethod
	{
		public UncheckedNegateMethod() : base("UNCHECKED_NEG", 1, 1) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return unchecked(-arguments[0].GetIntValue(context));
		}
	}

	sealed class ArgLengthMethod : ModernFunctionMethod
	{
		public ArgLengthMethod(string name) : base(name, 0, 0) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return context?.ExecutionContext?.CurrentVariadicArgCount ?? 0;
		}
	}

	sealed class RegexpMatchMethod : ModernFunctionMethod
	{
		public RegexpMatchMethod() : base("REGEXPMATCH", 2, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count == 4)
			{
				if (arguments[2] is not ModernVariableTerm groupCountTerm || !groupCountTerm.Identifier.IsInteger)
					throw new FormatException("REGEXPMATCH third argument must be an integer variable when four arguments are supplied.");
				if (arguments[3] is not ModernVariableTerm outputTerm || !outputTerm.Identifier.IsString)
					throw new FormatException("REGEXPMATCH fourth argument must be a string array variable.");
			}
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string baseString = arguments[0].GetStrValue(context) ?? "";
			Regex regex;
			try
			{
				regex = new Regex(arguments[1].GetStrValue(context) ?? "");
			}
			catch (ArgumentException e)
			{
				throw new ArgumentException($"REGEXPMATCH received an invalid regex pattern: {e.Message}", e);
			}

			var matches = regex.Matches(baseString);
			int groupCount = regex.GetGroupNumbers().Length;
			if (arguments.Count == 3 && arguments[2].GetIntValue(context) != 0)
			{
				SetResultGroupCount(context, groupCount);
				if (matches.Count > 0)
					WriteStringResults(context, null, FlattenRegexCaptures(matches, regex));
			}
			else if (arguments.Count == 4)
			{
				((ModernVariableTerm)arguments[2]).SetValue(groupCount, context);
				if (matches.Count > 0)
					WriteStringResults(context, arguments[3], FlattenRegexCaptures(matches, regex));
			}

			return matches.Count;
		}
	}

	sealed class GetMemoryUsageMethod : ModernFunctionMethod
	{
		public GetMemoryUsageMethod() : base("GETMEMORYUSAGE", 0, 0) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			return Process.GetCurrentProcess().WorkingSet64;
		}
	}

	sealed class ClearMemoryMethod : ModernFunctionMethod
	{
		public ClearMemoryMethod() : base("CLEARMEMORY", 0, 0) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			long before = Process.GetCurrentProcess().WorkingSet64;
			GC.Collect();
			long after = Process.GetCurrentProcess().WorkingSet64;
			return before - after;
		}
	}

	sealed class ExistVarMethod : ModernFunctionMethod
	{
		public ExistVarMethod() : base("EXISTVAR", 1, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			long mode = arguments.Count > 1 ? arguments[1].GetIntValue(context) : 0;
			var evaluator = context?.VariableEvaluator;
			if (evaluator == null)
				return 0;

			if (mode != 0)
			{
				try
				{
					new ModernExpressionParser(evaluator).Parse(name);
					return 1;
				}
				catch
				{
					return 0;
				}
			}

			if (!evaluator.TryGetToken(name, out var token))
				return 0;

			long result = token.GetEraType() switch
			{
				EraType.Integer => 1,
				EraType.String => 2,
				EraType.Float => 32,
				_ => 0,
			};
			if (token.IsConst)
				result |= 4;
			if (token.Dimension == VariableDimension.Array2D)
				result |= 8;
			if (token.Dimension == VariableDimension.Array3D)
				result |= 16;
			return result;
		}
	}

	sealed class GetVarMethod : ModernFunctionMethod
	{
		readonly EraType returnType;

		public GetVarMethod(string name, EraType returnType) : base(name, 1, 2)
		{
			this.returnType = returnType;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return returnType;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetVariable(context, arguments, out var term))
				return arguments[1].GetIntValue(context);
			if (!term.IsInteger)
				return DefaultOrThrow(arguments, context, $"GETVAR target {term.Identifier.Name} is not integer.").GetIntValue(context);
			return term.GetIntValue(context);
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetVariable(context, arguments, out var term))
				return arguments[1].GetStrValue(context) ?? "";
			if (!term.IsString)
				return DefaultOrThrow(arguments, context, $"GETVARS target {term.Identifier.Name} is not string.").GetStrValue(context);
			return term.GetStrValue(context) ?? "";
		}

		public override double GetFloatValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetVariable(context, arguments, out var term))
				return ToDouble(arguments[1], context);
			if (!term.IsFloat)
				return ToDouble(DefaultOrThrow(arguments, context, $"GETVARF target {term.Identifier.Name} is not float."), context);
			return term.GetFloatValue(context);
		}
	}

	sealed class SetVarMethod : ModernFunctionMethod
	{
		public SetVarMethod() : base("SETVAR", 2, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			bool hasDefault = arguments.Count > 2;
			long defaultValue = hasDefault ? arguments[2].GetIntValue(context) : 0;
			try
			{
				string name = arguments[0].GetStrValue(context);
				var term = ParseVariable(name, context);
				if (term.Identifier.IsConst)
					return hasDefault ? defaultValue : throw new InvalidOperationException($"{name} is read-only.");
				term.SetValue(arguments[1], context);
				return 1;
			}
			catch
			{
				if (hasDefault)
					return defaultValue;
				throw;
			}
		}
	}

	sealed class BitSetMethod : ModernFunctionMethod
	{
		public BitSetMethod() : base("BITSET", 2, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var target = GetIntegerVariableArgument(arguments[0], "BITSET");
			long index = arguments[1].GetIntValue(context);
			long value = arguments.Count > 2 ? arguments[2].GetIntValue(context) : 1;
			long length = arguments.Count > 3 ? arguments[3].GetIntValue(context) : 1;
			BitSet(target, context, index, value, length);
			return 1;
		}
	}

	sealed class BitGetMethod : ModernFunctionMethod
	{
		public BitGetMethod() : base("BITGET", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var target = GetIntegerVariableArgument(arguments[0], "BITGET");
			return BitGet(target, context, arguments[1].GetIntValue(context));
		}
	}

	sealed class BitToggleMethod : ModernFunctionMethod
	{
		public BitToggleMethod() : base("BITTOGGLE", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var target = GetIntegerVariableArgument(arguments[0], "BITTOGGLE");
			long index = arguments[1].GetIntValue(context);
			long slot = index / 64;
			int bit = (int)(index % 64);
			if (index < 0 || slot >= GetIntegerSlotLength(target.Identifier, context))
				return 0;
			long value = target.Identifier.GetIntValue(context, new[] { slot });
			target.Identifier.SetValue(value ^ (1L << bit), context, new[] { slot });
			return 1;
		}
	}

	enum MapManagementOperation
	{
		Create,
		Check,
		Release,
	}

	sealed class MapManagementMethod : ModernFunctionMethod
	{
		readonly MapManagementOperation operation;

		public MapManagementMethod(string name, MapManagementOperation operation) : base(name, 1, 1)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			var maps = GetMaps(context);
			string mapName = arguments[0].GetStrValue(context) ?? "";
			bool contains = maps.ContainsKey(mapName);
			switch (operation)
			{
				case MapManagementOperation.Check:
					return contains ? 1 : 0;
				case MapManagementOperation.Release:
					if (contains)
						maps.Remove(mapName);
					return 1;
				default:
					if (contains)
						return 0;
					maps[mapName] = new Dictionary<string, string>();
					return 1;
			}
		}
	}

	enum MapDataOperation
	{
		Set,
		Has,
		Remove,
		Clear,
		Size,
	}

	sealed class MapDataOperationMethod : ModernFunctionMethod
	{
		readonly MapDataOperation operation;

		public MapDataOperationMethod(string name, MapDataOperation operation)
			: base(name, operation == MapDataOperation.Set ? 3 : operation == MapDataOperation.Clear || operation == MapDataOperation.Size ? 1 : 2, operation == MapDataOperation.Set ? 3 : operation == MapDataOperation.Clear || operation == MapDataOperation.Size ? 1 : 2)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return -1;
			if (operation == MapDataOperation.Clear)
			{
				map.Clear();
				return 1;
			}
			if (operation == MapDataOperation.Size)
				return map.Count;

			string key = arguments[1].GetStrValue(context) ?? "";
			bool contains = map.ContainsKey(key);
			if (operation == MapDataOperation.Has)
				return contains ? 1 : 0;
			if (operation == MapDataOperation.Remove)
			{
				map.Remove(key);
				return 1;
			}

			map[key] = arguments[2].GetStrValue(context) ?? "";
			return 1;
		}
	}

	sealed class MapGetMethod : ModernFunctionMethod
	{
		public MapGetMethod() : base("MAP_GET", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			string key = arguments[1].GetStrValue(context) ?? "";
			return map.TryGetValue(key, out var value) ? value ?? "" : "";
		}
	}

	enum MapStringListOperation
	{
		Keys,
		Values,
	}

	sealed class MapGetStringListMethod : ModernFunctionMethod
	{
		readonly MapStringListOperation operation;

		public MapGetStringListMethod(string name, MapStringListOperation operation) : base(name, 1, 3)
		{
			this.operation = operation;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		protected override void ValidateArguments(IReadOnlyList<AExpression> arguments)
		{
			if (arguments.Count == 3 && (arguments[1] is not ModernVariableTerm term || !term.Identifier.IsString))
				throw new FormatException($"{Name} second argument must be a string array variable when three arguments are supplied.");
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			var values = operation == MapStringListOperation.Keys ? map.Keys.ToArray() : map.Values.ToArray();
			if (arguments.Count == 1)
				return string.Join(",", values);
			if (arguments.Count == 2)
			{
				if (arguments[1].GetIntValue(context) == 0)
					return "";
				WriteStringResults(context, null, values);
				SetIntegerResult(context, 0, values.Length);
				return values.Length > 0 ? values[0] : "";
			}
			if (arguments[2].GetIntValue(context) == 0)
				return "";
			WriteStringResults(context, arguments[1], values);
			SetIntegerResult(context, 0, values.Length);
			return "";
		}
	}

	sealed class MapToXmlMethod : ModernFunctionMethod
	{
		public MapToXmlMethod() : base("MAP_TOXML", 1, 1) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			var builder = new StringBuilder();
			builder.Append("<map>");
			foreach (var pair in map)
				builder.Append("<p><k>").Append(pair.Key).Append("</k><v>").Append(pair.Value).Append("</v></p>");
			builder.Append("</map>");
			return builder.ToString();
		}
	}

	sealed class MapFromXmlMethod : ModernFunctionMethod
	{
		public MapFromXmlMethod() : base("MAP_FROMXML", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return 0;
			var document = new XmlDocument();
			string xml = arguments[1].GetStrValue(context) ?? "";
			try
			{
				document.LoadXml(xml);
			}
			catch (XmlException e)
			{
				throw new FormatException($"MAP_FROMXML received invalid XML: {e.Message}", e);
			}

			var nodes = document.SelectNodes("/map/p");
			if (nodes == null)
				return 1;
			foreach (XmlNode node in nodes)
			{
				var key = node.SelectSingleNode("./k");
				var value = node.SelectSingleNode("./v");
				if (key == null || value == null)
					continue;
				map[key.InnerText] = value.InnerXml;
			}
			return 1;
		}
	}

	sealed class MapMergeMethod : ModernFunctionMethod
	{
		public MapMergeMethod() : base("MAP_MERGE", 2, 2) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var destination))
				return 0;
			if (!TryGetMap(context, arguments[1].GetStrValue(context), out var source))
				return 0;
			foreach (var pair in source)
				destination[pair.Key] = pair.Value;
			return 1;
		}
	}

	sealed class MapRemoveIfMethod : ModernFunctionMethod
	{
		public MapRemoveIfMethod() : base("MAP_REMOVEIF", 3, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return 0;
			string matchValue = arguments[1].GetStrValue(context) ?? "";
			string mode = arguments[2].GetStrValue(context) ?? "";
			var toRemove = map.Where(pair => MapPredicate(pair, matchValue, mode)).Select(pair => pair.Key).ToArray();
			if (toRemove.Length == 0 && !IsKnownMapPredicateMode(mode))
				return -1;
			for (int i = 0; i < toRemove.Length; i++)
				map.Remove(toRemove[i]);
			return toRemove.Length;
		}
	}

	sealed class MapFindKeyMethod : ModernFunctionMethod
	{
		public MapFindKeyMethod() : base("MAP_FINDKEY", 3, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			string matchValue = arguments[1].GetStrValue(context) ?? "";
			string mode = arguments[2].GetStrValue(context) ?? "";
			if (!IsKnownMapPredicateMode(mode))
			{
				SetIntegerResult(context, 0, 0);
				return "";
			}
			var keys = map.Where(pair => MapPredicate(pair, matchValue, mode)).Select(pair => pair.Key).ToArray();
			SetIntegerResult(context, 0, keys.Length);
			return string.Join(",", keys);
		}
	}

	sealed class MapToStringMethod : ModernFunctionMethod
	{
		public MapToStringMethod() : base("MAP_TOSTRING", 1, 3) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.String;
		}

		public override string GetStrValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return "";
			string entrySeparator = arguments.Count > 1 ? arguments[1].GetStrValue(context) ?? "" : ",";
			string keyValueSeparator = arguments.Count > 2 ? arguments[2].GetStrValue(context) ?? "" : "=";
			return string.Join(entrySeparator, map.Select(pair => pair.Key + keyValueSeparator + pair.Value));
		}
	}

	sealed class MapFromStringMethod : ModernFunctionMethod
	{
		public MapFromStringMethod() : base("MAP_FROMSTRING", 2, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			if (!TryGetMap(context, arguments[0].GetStrValue(context), out var map))
				return 0;
			string data = arguments[1].GetStrValue(context) ?? "";
			if (data.Length == 0)
				return 0;
			string entrySeparator = arguments.Count > 2 ? arguments[2].GetStrValue(context) ?? "" : ",";
			string keyValueSeparator = arguments.Count > 3 ? arguments[3].GetStrValue(context) ?? "" : "=";
			if (entrySeparator.Length == 0 || keyValueSeparator.Length == 0)
				return 0;

			int count = 0;
			var entries = data.Split(new[] { entrySeparator }, StringSplitOptions.None);
			foreach (string entry in entries)
			{
				if (entry.Length == 0)
					continue;
				int index = entry.IndexOf(keyValueSeparator, StringComparison.Ordinal);
				if (index < 0)
					continue;
				map[entry.Substring(0, index)] = entry.Substring(index + keyValueSeparator.Length);
				count++;
			}
			return count;
		}
	}

	sealed class ExistFunctionMethod : ModernFunctionMethod
	{
		readonly ModernFunctionEvaluator evaluator;

		public ExistFunctionMethod(ModernFunctionEvaluator evaluator) : base("EXISTFUNCTION", 1, 2)
		{
			this.evaluator = evaluator;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(context);
			if (!evaluator.TryGetMethod(name, out var method))
				return 0;

			return method.GetReturnType(Array.Empty<AExpression>()) switch
			{
				EraType.Integer => 2,
				EraType.String => 3,
				EraType.Float => 4,
				_ => 1,
			};
		}
	}

	enum EnumNameTarget
	{
		Function,
		Variable,
	}

	enum EnumNameAction
	{
		BeginsWith,
		EndsWith,
		Contains,
	}

	sealed class EnumNameMethod : ModernFunctionMethod
	{
		readonly ModernFunctionEvaluator functionEvaluator;
		readonly EnumNameTarget target;
		readonly EnumNameAction action;

		public EnumNameMethod(string name, ModernFunctionEvaluator functionEvaluator, EnumNameTarget target, EnumNameAction action)
			: base(name, 1, 2)
		{
			this.functionEvaluator = functionEvaluator;
			this.target = target;
			this.action = action;
		}

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string pattern = arguments[0].GetStrValue(context) ?? "";
			IEnumerable<string> names = target == EnumNameTarget.Function
				? functionEvaluator.Methods.Keys
				: context?.VariableEvaluator?.Tokens.Keys ?? Array.Empty<string>();

			var matches = names.Where(name => Matches(name, pattern)).ToArray();
			WriteStringResults(context, arguments.Count > 1 ? arguments[1] : null, matches);
			return matches.Length;
		}

		bool Matches(string name, string pattern)
		{
			if (pattern.Length == 0)
				return false;
			return action switch
			{
				EnumNameAction.BeginsWith => name.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
				EnumNameAction.EndsWith => name.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
				_ => name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0,
			};
		}
	}

	sealed class EnumFilesMethod : ModernFunctionMethod
	{
		public EnumFilesMethod() : base("ENUMFILES", 1, 4) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string dir = arguments[0].GetStrValue(context);
			if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
				return -1;

			string pattern = arguments.Count > 1 ? arguments[1].GetStrValue(context) : "*";
			var option = arguments.Count > 2 && arguments[2].GetIntValue(context) != 0
				? SearchOption.AllDirectories
				: SearchOption.TopDirectoryOnly;
			string[] files;
			try
			{
				files = Directory.EnumerateFiles(dir, string.IsNullOrEmpty(pattern) ? "*" : pattern, option).ToArray();
			}
			catch
			{
				return -1;
			}

			WriteStringResults(context, arguments.Count > 3 ? arguments[3] : null, files);
			return files.Length;
		}
	}

	sealed class ExistFileMethod : ModernFunctionMethod
	{
		public ExistFileMethod() : base("EXISTFILE", 1, 1) { }

		public override EraType GetReturnType(IReadOnlyList<AExpression> arguments)
		{
			return EraType.Integer;
		}

		public override long GetIntValue(ModernExpressionContext context, IReadOnlyList<AExpression> arguments)
		{
			string path = arguments[0].GetStrValue(context);
			return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? 1 : 0;
		}
	}

	static void WriteStringResults(ModernExpressionContext context, AExpression destination, IReadOnlyList<string> values)
	{
		if (context?.VariableEvaluator == null)
			return;
		ModernVariableToken token = null;
		if (destination is ModernVariableTerm term && term.Identifier.IsString)
			token = term.Identifier;
		if (token == null && context.VariableEvaluator.TryGetToken("RESULTS", out var resultsToken))
			token = resultsToken;
		if (token == null)
			return;

		int length = 0;
		try
		{
			length = token.GetLength();
		}
		catch
		{
			length = values.Count;
		}
		int count = Math.Min(length, values.Count);
		for (int i = 0; i < count; i++)
			token.SetValue(values[i], context, new long[] { i });
	}

	static void SetResultGroupCount(ModernExpressionContext context, int groupCount)
	{
		SetIntegerResult(context, 1, groupCount);
	}

	static IReadOnlyList<string> FlattenRegexCaptures(MatchCollection matches, Regex regex)
	{
		var groupNames = regex.GetGroupNames();
		var values = new List<string>(matches.Count * groupNames.Length);
		foreach (Match match in matches)
		{
			foreach (string name in groupNames)
				values.Add(match.Groups[name].Value);
		}
		return values;
	}

	static void SetIntegerResult(ModernExpressionContext context, long index, long value)
	{
		if (context?.VariableEvaluator == null)
			return;
		var result = context.VariableEvaluator.CreateTerm("RESULT", new SingleLongTerm(index));
		result.SetValue(value, context);
	}

	static Dictionary<string, Dictionary<string, string>> GetMaps(ModernExpressionContext context)
	{
		if (context?.VariableEvaluator?.VariableData == null)
			throw new InvalidOperationException("No modern variable data is available.");
		return context.VariableEvaluator.VariableData.DataStringMaps;
	}

	static bool TryGetMap(ModernExpressionContext context, string name, out Dictionary<string, string> map)
	{
		return GetMaps(context).TryGetValue(name ?? "", out map);
	}

	static bool IsKnownMapPredicateMode(string mode)
	{
		return mode == "KEY_CONTAINS"
			|| mode == "KEY_PREFIX"
			|| mode == "KEY_SUFFIX"
			|| mode == "VAL_CONTAINS"
			|| mode == "VAL_EQ"
			|| mode == "VAL_NE";
	}

	static bool MapPredicate(KeyValuePair<string, string> pair, string matchValue, string mode)
	{
		return mode switch
		{
			"KEY_CONTAINS" => pair.Key.Contains(matchValue),
			"KEY_PREFIX" => pair.Key.StartsWith(matchValue),
			"KEY_SUFFIX" => pair.Key.EndsWith(matchValue),
			"VAL_CONTAINS" => (pair.Value ?? "").Contains(matchValue),
			"VAL_EQ" => pair.Value == matchValue,
			"VAL_NE" => pair.Value != matchValue,
			_ => false,
		};
	}

	static ModernVariableTerm GetIntegerVariableArgument(AExpression expression, string functionName)
	{
		if (expression is not ModernVariableTerm term || !term.Identifier.IsInteger)
			throw new InvalidOperationException($"{functionName} needs an integer variable as the first argument.");
		if (term.ArgumentCount != 0)
			throw new InvalidOperationException($"{functionName} needs a whole integer array variable.");
		return term;
	}

	static void BitSet(ModernVariableTerm target, ModernExpressionContext context, long index, long value, long length)
	{
		if (length <= 0)
			return;
		long slots = GetIntegerSlotLength(target.Identifier, context);
		long bitSize = slots * 64;
		for (long i = 0; i < length; i++)
		{
			long bitIndex = index + i;
			if (bitIndex < 0)
				continue;
			if (bitIndex >= bitSize)
				break;
			long slot = bitIndex / 64;
			int bit = (int)(bitIndex % 64);
			long current = target.Identifier.GetIntValue(context, new[] { slot });
			long next = value != 0 ? current | (1L << bit) : current & ~(1L << bit);
			target.Identifier.SetValue(next, context, new[] { slot });
		}
	}

	static long BitGet(ModernVariableTerm target, ModernExpressionContext context, long index)
	{
		long slots = GetIntegerSlotLength(target.Identifier, context);
		if (index < 0 || index >= slots * 64)
			return -1;
		long slot = index / 64;
		int bit = (int)(index % 64);
		long value = target.Identifier.GetIntValue(context, new[] { slot });
		return (value & (1L << bit)) != 0 ? 1 : 0;
	}

	static int GetIntegerSlotLength(ModernVariableToken token, ModernExpressionContext context)
	{
		object array = token.GetArray(context);
		return array switch
		{
			long[] dense => dense.Length,
			SparseArray<long> sparse => sparse.Length,
			_ => token.GetLength(),
		};
	}

	static bool TryGetVariable(ModernExpressionContext context, IReadOnlyList<AExpression> arguments, out ModernVariableTerm term)
	{
		try
		{
			term = ParseVariable(arguments[0].GetStrValue(context), context);
			return true;
		}
		catch
		{
			if (arguments.Count > 1)
			{
				term = null;
				return false;
			}
			throw;
		}
	}

	static ModernVariableTerm ParseVariable(string source, ModernExpressionContext context)
	{
		var evaluator = context?.VariableEvaluator ?? throw new InvalidOperationException("No variable evaluator is available.");
		var expression = new ModernExpressionParser(evaluator).Parse(source);
		if (expression is ModernVariableTerm term)
			return term;
		throw new FormatException($"{source} is not a variable expression.");
	}

	static AExpression DefaultOrThrow(IReadOnlyList<AExpression> arguments, ModernExpressionContext context, string message)
	{
		if (arguments.Count > 1)
			return arguments[1];
		throw new InvalidOperationException(message);
	}
}
