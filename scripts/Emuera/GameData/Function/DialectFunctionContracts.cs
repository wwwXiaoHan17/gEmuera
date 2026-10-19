using System;
using System.Collections.Generic;
using MinorShift.Emuera.Compatibility;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;

namespace MinorShift.Emuera.GameData.Function
{
	/// <summary>
	/// Freezes the public argument contract for functions whose upstream v24 and
	/// Snake registries expose the same name with different overloads.
	/// </summary>
	internal static class DialectFunctionContracts
	{
		private static readonly EraType[] Int1 = { EraType.Integer };
		private static readonly EraType[] Int2 = { EraType.Integer, EraType.Integer };
		private static readonly EraType[] Int3 = { EraType.Integer, EraType.Integer, EraType.Integer };

		internal static FunctionMethod Project(
			string name,
			FunctionMethod method,
			LegacyCompatibilityProfile compatibility)
		{
			// 蛇系契约的选择权在方言模块（snake Apply 声明重载差异名集）；
			// 本类只持有各名字的 CheckArgumentType 差异实现。
			bool snake = compatibility.UsesDialectFunctionContract(name);
			switch (name)
			{
				case "ABS":
					return snake ? Wrap(method, null, CheckAnyCount(name, 1, 1)) : Wrap(method, Int1, CheckFixedInt);
				case "ARGLEN":
					return snake ? Wrap(method, null, AllowAny) : method;
				case "CBGSETBUTTONSPRITE":
					return Wrap(method, null, CheckButtonSprite);
				case "CBGSETSPRITE":
					return snake
						? Wrap(method, null, CheckCbgSpriteSnake)
						: Wrap(method, new[] { EraType.String, EraType.Integer, EraType.Integer, EraType.Integer },
							CheckFixedStringIntIntInt);
				case "CBRT":
					return snake ? Wrap(method, null, CheckAnyCount(name, 1, 1)) : Wrap(method, Int1, CheckFixedInt);
				case "ENCODETOUNI":
					return Wrap(method, null, CheckEncodeToUni);
				case "ENUMFUNCBEGINSWITH":
				case "ENUMFUNCENDSWITH":
				case "ENUMFUNCWITH":
				case "ENUMMACROBEGINSWITH":
				case "ENUMMACROENDSWITH":
				case "ENUMMACROWITH":
				case "ENUMVARBEGINSWITH":
				case "ENUMVARENDSWITH":
				case "ENUMVARWITH":
					return Wrap(method, null, CheckEnumName);
				case "EXPONENT":
					return snake ? Wrap(method, null, CheckAnyCount(name, 1, 1)) : Wrap(method, Int1, CheckFixedInt);
				case "EXISTFUNCTION":
					return Wrap(method, null, CheckExistFunction);
				case "EXISTVAR":
					return snake
						? Wrap(method, null, CheckExistVarSnake)
						: Wrap(method, new[] { EraType.String }, CheckFixedString);
				case "GCLEAR":
					return Wrap(method, null, CheckGraphicsClear);
				case "GCREATEFROMFILE":
					return Wrap(method, null, CheckGraphicsCreateFromFile);
				case "GDRAWSPRITE":
					return Wrap(method, null, CheckGraphicsDrawSprite);
				case "GETDISPLAYLINE":
					return Wrap(method, null, CheckDisplayLine);
				case "GETVAR":
					return snake
						? Wrap(method, null, CheckGetVar)
						: Wrap(method, new[] { EraType.String }, CheckFixedString);
				case "GETVARS":
					return snake
						? Wrap(method, null, CheckGetVars)
						: Wrap(method, new[] { EraType.String }, CheckFixedString);
				case "GGETTEXTSIZE":
					return Wrap(method, null, CheckGraphicsGetTextSize);
				case "HOTKEY_STATE":
					return Wrap(method, null, CheckHotkeyState);
				case "HOTKEY_STATE_INIT":
					return Wrap(method, null, CheckHotkeyStateInit);
				case "LIMIT":
					return snake
						? Wrap(method, null, CheckAnyCount(name, 3, 3))
						: Wrap(method, Int3, CheckFixedInt3);
				case "LOADTEXT":
					return Wrap(method, null, CheckLoadText);
				case "MAX":
				case "MIN":
					// 参考侧为 argumentTypeArrayEx（[{Int,VariadicInt},OmitStart=1]），
					// 形状同为 custom；此处冻结"第 1 参不可省略、全部 Integer"的 v24 契约。
					return snake ? method : Wrap(method, null, CheckMinMaxV24);
				case "LOG":
				case "LOG10":
					return snake ? Wrap(method, null, CheckAnyCount(name, 1, 1)) : Wrap(method, Int1, CheckFixedInt);
				case "POWER":
					return snake ? Wrap(method, null, CheckAnyCount(name, 2, 2)) : Wrap(method, Int2, CheckFixedInt2);
				case "RAND":
					// 参考侧为 argumentTypeArrayEx（[{Int,Int},OmitStart=1]），
					// 形状同为 custom；此处冻结"第 1 参不可省略、1~2 个 Integer"的 v24 契约。
					return snake ? method : Wrap(method, null, CheckRandV24);
				case "REPLACE":
					return Wrap(method, null, CheckReplace);
				case "RESUMETEXTBOX":
					return Wrap(method, Int3, CheckFixedInt3);
				case "SAVETEXT":
					return Wrap(method, null, CheckSaveText);
				case "SIGN":
					return snake ? Wrap(method, null, CheckAnyCount(name, 1, 1)) : Wrap(method, Int1, CheckFixedInt);
				case "SPRITECREATE":
					return Wrap(method, null, snake ? CheckSpriteCreateSnake : CheckSpriteCreateV24);
				case "SPRITECREATEFROMFILE":
					return snake ? Wrap(method, null, CheckSpriteCreateFromFile) : method;
				case "SQRT":
					return snake ? Wrap(method, null, CheckAnyCount(name, 1, 1)) : Wrap(method, Int1, CheckFixedInt);
				case "TOINT":
					return snake ? Wrap(method, null, CheckAnyCount(name, 1, int.MaxValue)) : method;
				case "UNCHECKED_ADD":
				case "UNCHECKED_MUL":
				case "UNCHECKED_SUB":
					return snake ? Wrap(method, Int2, CheckFixedInt2) : method;
				case "UNCHECKED_NEG":
					return snake ? Wrap(method, Int1, CheckFixedInt) : method;
				default:
					return method;
			}
		}

		private static FunctionMethod Wrap(
			FunctionMethod method,
			EraType[] declared,
			Func<string, IOperandTerm[], string> checker)
		{
			return new DialectFunctionMethod(method, declared, checker);
		}

		private static string AllowAny(string name, IOperandTerm[] arguments) => null;

		private static Func<string, IOperandTerm[], string> CheckAnyCount(string name, int min, int max)
		{
			return (functionName, arguments) =>
			{
				if (arguments.Length < min)
					return functionName + " requires at least " + min + " argument(s)";
				if (max != int.MaxValue && arguments.Length > max)
					return functionName + " has too many arguments";
				return null;
			};
		}

		private static string CheckRandV24(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length < 1 || arguments.Length > 2)
				return name + " has an invalid argument count";
			return CheckMinMaxV24(name, arguments);
		}

		private static string CheckMinMaxV24(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length < 1)
				return name + " has an invalid argument count";
			if (arguments[0] == null)
				return name + " argument 1 cannot be omitted";
			for (int i = 0; i < arguments.Length; i++)
			{
				if (arguments[i] != null && arguments[i].GetEraType() != EraType.Integer)
					return name + " argument " + (i + 1) + " has the wrong type";
			}
			return null;
		}

		private static string CheckFixedString(string name, IOperandTerm[] arguments) => CheckFixed(name, arguments, new[] { EraType.String }, false);
		private static string CheckFixedInt(string name, IOperandTerm[] arguments) => CheckFixed(name, arguments, Int1, false);
		private static string CheckFixedInt2(string name, IOperandTerm[] arguments) => CheckFixed(name, arguments, Int2, false);
		private static string CheckFixedInt3(string name, IOperandTerm[] arguments) => CheckFixed(name, arguments, Int3, false);
		private static string CheckFixedStringIntIntInt(string name, IOperandTerm[] arguments) => CheckFixed(name, arguments, new[] { EraType.String, EraType.Integer, EraType.Integer, EraType.Integer }, false);

		private static string CheckFixed(string name, IOperandTerm[] arguments, EraType[] types, bool nullable)
		{
			if (arguments.Length != types.Length)
				return name + " has an invalid argument count";
			for (int i = 0; i < types.Length; i++)
			{
				if (arguments[i] == null)
				{
					if (nullable) continue;
					return name + " argument " + (i + 1) + " cannot be omitted";
				}
				if (arguments[i].GetEraType() != types[i])
					return name + " argument " + (i + 1) + " has the wrong type";
			}
			return null;
		}

		private static string CheckOptionalShape(string name, IOperandTerm[] arguments, int min, int max, EraType[] types)
		{
			if (arguments.Length < min)
				return name + " has too few arguments";
			if (arguments.Length > max)
				return name + " has too many arguments";
			for (int i = 0; i < arguments.Length; i++)
			{
				if (arguments[i] == null)
					continue;
				if (i >= types.Length || (types[i] != EraType.Void && arguments[i].GetEraType() != types[i]))
					return name + " argument " + (i + 1) + " has the wrong type";
			}
			return null;
		}

		private static string CheckButtonSprite(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 6, 7,
				new[] { EraType.Integer, EraType.String, EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.String });

		private static string CheckCbgSpriteSnake(string name, IOperandTerm[] arguments)
		{
			string error = CheckOptionalShape(name, arguments, 1, 8,
				new[] { EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Void });
			if (error != null || arguments.Length < 8 || arguments[7] == null)
				return error;
			if (!(arguments[7] is VariableTerm variable) || !variable.IsInteger && !variable.IsFloat
				|| (!variable.Identifier.IsArray2D && !variable.Identifier.IsArray3D))
				return name + " argument 8 must be a color matrix array";
			return null;
		}

		private static string CheckEncodeToUni(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 2, new[] { EraType.String, EraType.Integer });

		private static string CheckEnumName(string name, IOperandTerm[] arguments)
		{
			string error = CheckOptionalShape(name, arguments, 1, 2, new[] { EraType.String, EraType.Void });
			if (error != null || arguments.Length < 2 || arguments[1] == null)
				return error;
			return arguments[1] is VariableTerm variable && variable.Identifier.IsString && variable.Identifier.IsArray1D
				? null
				: name + " argument 2 must be a string array";
		}

		private static string CheckExistFunction(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 2, new[] { EraType.String, EraType.Integer });

		private static string CheckExistVarSnake(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 2, new[] { EraType.String, EraType.Integer });

		private static string CheckGraphicsClear(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length != 2 && arguments.Length != 6)
				return name + " requires 2 or 6 arguments";
			return CheckOptionalShape(name, arguments, arguments.Length, arguments.Length,
				new[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer });
		}

		private static string CheckGraphicsCreateFromFile(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 2, 3, new[] { EraType.Integer, EraType.String, EraType.Integer });

		private static string CheckGraphicsDrawSprite(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length != 2 && arguments.Length != 4 && arguments.Length != 6 && arguments.Length != 7)
				return name + " has an invalid argument count";
			string error = CheckOptionalShape(name, arguments, arguments.Length, arguments.Length,
				new[] { EraType.Integer, EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Void });
			if (error != null || arguments.Length < 7 || arguments[6] == null)
				return error;
			return arguments[6] is VariableTerm variable && variable.IsInteger
				&& (variable.Identifier.IsArray2D || variable.Identifier.IsArray3D)
				? null
				: name + " argument 7 must be a color matrix array";
		}

		private static string CheckDisplayLine(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 1, new[] { EraType.Integer });

		private static string CheckGetVar(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 2, new[] { EraType.String, EraType.Integer });

		private static string CheckGetVars(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 2, new[] { EraType.String, EraType.String });

		private static string CheckGraphicsGetTextSize(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 3, 4, new[] { EraType.String, EraType.String, EraType.Integer, EraType.Integer });

		private static string CheckHotkeyState(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 2, new[] { EraType.Integer, EraType.Integer });

		private static string CheckHotkeyStateInit(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 0, 1, new[] { EraType.Integer });

		private static string CheckLoadText(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 1, 3, new[] { EraType.Void, EraType.Integer, EraType.Integer });

		private static string CheckReplace(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length == 3)
				return CheckOptionalShape(name, arguments, 3, 3, new[] { EraType.String, EraType.String, EraType.String });
			if (arguments.Length == 4)
			{
				string error = CheckOptionalShape(name, arguments, 4, 4, new[] { EraType.String, EraType.String, EraType.Void, EraType.Integer });
				if (error != null || arguments[2] == null)
					return error;
				return arguments[2] is VariableTerm variable && variable.Identifier.IsString && variable.Identifier.IsArray1D
					? null
					: name + " argument 3 must be a string array";
			}
			return name + " has an invalid argument count";
		}

		private static string CheckSaveText(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 2, 4, new[] { EraType.String, EraType.Void, EraType.Integer, EraType.Integer });

		private static string CheckSpriteCreateV24(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length == 2 || arguments.Length == 6)
				return CheckOptionalShape(name, arguments, arguments.Length, arguments.Length,
					new[] { EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer });
			return name + " requires 2 or 6 arguments";
		}

		private static string CheckSpriteCreateSnake(string name, IOperandTerm[] arguments)
		{
			if (arguments.Length != 2 && arguments.Length != 6 && arguments.Length != 8 && arguments.Length != 10)
				return name + " has an invalid argument count";
			return CheckOptionalShape(name, arguments, arguments.Length, arguments.Length,
				new[] { EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer,
					EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer });
		}

		private static string CheckSpriteCreateFromFile(string name, IOperandTerm[] arguments) =>
			CheckOptionalShape(name, arguments, 2, 3, new[] { EraType.String, EraType.String, EraType.Integer });
	}
}
