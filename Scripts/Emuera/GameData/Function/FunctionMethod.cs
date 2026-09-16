using System;
using System.Collections.Generic;
using System.Text;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
	internal abstract class FunctionMethod
	{
		public EraType ReturnType { get; protected set; }
		/// <summary>
		/// 数学系函数（MAX/MIN/ABS/POWER/SQRT 等）在任一参数为 Float 时，返回类型可动态切换为 Float。
		/// FunctionMethodTerm 依赖该标志做动态类型解析（与 snake 参考实现一致）。
		/// </summary>
		public bool CanReturnFloat { get; protected set; }
		protected EraType[] argumentTypeArray;

		#region EM_私家版_多签名参数机制（对照 v24/snake FunctionMethod.cs）
		/// <summary>
		/// 参数类型位标志。与 v24/snake 的 ArgType 枚举逐位对齐：
		/// 多签名函数用 argumentTypeArrayEx 声明多组可接受的签名（如 GCLEAR 2 参/6 参），
		/// 基类 CheckArgumentType 遍历所有签名组，任一匹配即通过。
		/// </summary>
		protected enum ArgType
		{
			Invalid = 0,
			Any = 1,
			Int = 1 << 1,
			String = 1 << 2,
			Ref = 1 << 3,
			Array = 1 << 4,
			Array1D = 1 << 5,
			Array2D = 1 << 6,
			Array3D = 1 << 7,
			Variadic = 1 << 8,
			SameAsFirst = 1 << 9,
			CharacterData = Ref | 1 << 10,
			AllowConstRef = 1 << 11,
			DisallowVoid = 1 << 12,

			RefInt = Ref | Int,
			RefAny = Ref | Any,
			RefString = Ref | String,
			RefAnyArray = RefAny | Array,
			RefIntArray = RefInt | Array,
			RefStringArray = RefString | Array,
			RefAny1D = RefAny | Array1D,
			RefInt1D = RefInt | Array1D,
			RefString1D = RefString | Array1D,
			RefAny2D = RefAny | Array2D,
			RefInt2D = RefInt | Array2D,
			RefString2D = RefString | Array2D,
			RefAny3D = RefAny | Array3D,
			RefInt3D = RefInt | Array3D,
			RefString3D = RefString | Array3D,

			VariadicAny = Variadic | Any,
			VariadicInt = Variadic | Int,
			VariadicString = Variadic | String,
			VariadicSameAsFirst = Variadic | SameAsFirst,
		}

		protected sealed class _ArgType
		{
			public _ArgType(ArgType t)
			{
				type = t;
			}
			public EraType Type { get { return Int ? EraType.Integer : EraType.String; } }
			public ArgType type = ArgType.Invalid;
			public bool AllowConstRef { get { return (type & ArgType.AllowConstRef) != 0; } }
			public bool DisallowVoid { get { return (type & ArgType.DisallowVoid) != 0; } }
			public bool Ref { get { return (type & ArgType.Ref) != 0; } }
			public bool Any { get { return (type & ArgType.Any) != 0; } }
			public bool Int { get { return (type & ArgType.Int) != 0; } }
			public bool Array { get { return (type & ArgType.Array) != 0; } }
			public bool Array1D { get { return (type & ArgType.Array1D) != 0; } }
			public bool Array2D { get { return (type & ArgType.Array2D) != 0; } }
			public bool Array3D { get { return (type & ArgType.Array3D) != 0; } }
			public bool String { get { return (type & ArgType.String) != 0; } }
			public bool Variadic { get { return (type & ArgType.Variadic) != 0; } }
			public bool SameAsFirst { get { return (type & ArgType.SameAsFirst) != 0; } }
			public bool CharacterData { get { return ((int)type & 1 << 10) != 0; } }
			public int ArrayDims
			{
				get
				{
					return Array ? -1
						: Array1D ? 1
						: Array2D ? 2
						: Array3D ? 3 : 0;
				}
			}

			public static implicit operator _ArgType(ArgType value)
			{
				return new _ArgType(value);
			}
		}

		protected sealed class ArgTypeList
		{
			public List<_ArgType> ArgTypes { get; set; } = new List<_ArgType>();
			public int OmitStart { get; set; } = -1;
			public bool MatchVariadicGroup { get; set; }

			public _ArgType[] LastVariadics
			{
				get
				{
					int count = 0;
					for (int i = ArgTypes.Count - 1; i >= 0; i--)
					{
						if (!ArgTypes[i].Variadic) break;
						count++;
					}
					if (count == 0) return null;
					var ret = new _ArgType[count];
					for (int i = 0; i < count; i++)
						ret[i] = ArgTypes[ArgTypes.Count - count + i];
					return ret;
				}
			}
		}

		protected ArgTypeList[] argumentTypeArrayEx;

		private string CheckArgumentTypeEx(string name, IOperandTerm[] arguments)
		{
			string[] errMsg = new string[argumentTypeArrayEx.Length];
			for (int idx = 0; idx < argumentTypeArrayEx.Length; idx++)
			{
				var list = argumentTypeArrayEx[idx];
				var vs = list.LastVariadics;
				bool variadic = vs != null;
				bool argsNotMoreThanRule = variadic ? true : arguments.Length <= list.ArgTypes.Count;
				bool argsNotLessThanRule = list.OmitStart > -1 ? arguments.Length >= list.OmitStart : arguments.Length >= list.ArgTypes.Count;
				if (argsNotMoreThanRule && argsNotLessThanRule)
				{
					if (list.MatchVariadicGroup && vs != null)
					{
						var variadicGroupStart = list.ArgTypes.Count - vs.Length;
						if (arguments.Length > variadicGroupStart && (arguments.Length - variadicGroupStart) % vs.Length != 0)
						{
							errMsg[idx] = string.Format(Properties.Resources.SyntaxErrMesMethodArgsNotFitExpr0, name, arguments.Length, variadicGroupStart, vs.Length);
							continue;
						}
					}
					// 引数の数が有効
					for (int i = 0; i < (variadic ? arguments.Length : Math.Min(arguments.Length, list.ArgTypes.Count)); i++)
					{
						var rule = variadic && i >= list.ArgTypes.Count ? vs[(i - list.ArgTypes.Count) % vs.Length] : list.ArgTypes[i];
						if (arguments[i] == null)
						{
							if (i < list.OmitStart || list.OmitStart > -1 && i >= list.OmitStart && rule.DisallowVoid)
							{
								errMsg[idx] = string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
								break;
							}
							else continue;
						}
						bool typeNotMatch = rule.SameAsFirst
							? arguments[0].GetEraType() != arguments[i].GetEraType()
							: !rule.Any && rule.Type != arguments[i].GetEraType();
						if (rule.Ref)
						{
							if (rule.CharacterData && (!(arguments[i] is VariableTerm cvarTerm) || !cvarTerm.Identifier.IsCharacterData))
							{
								errMsg[idx] = string.Format(Properties.Resources.SyntaxErrMesMethodArgIsNotCharacterVar0, name, i + 1);
								break;
							}
							bool error = false;
							string errText;
							var dims = rule.ArrayDims;
							switch (dims)
							{
								case 0:
									errText = rule.String ? Properties.Resources.SyntaxErrMesMethodArgIsNotStrVar0
										: rule.Int ? Properties.Resources.SyntaxErrMesMethodArgIsNotIntVar0 : Properties.Resources.SyntaxErrMesMethodArgIsNotVar0;
									errText = string.Format(errText, name, i + 1);
									break;
								case -1:
									errText = rule.String ? Properties.Resources.SyntaxErrMesMethodArgIsNotStrArray0
										: rule.Int ? Properties.Resources.SyntaxErrMesMethodArgIsNotIntArray0 : Properties.Resources.SyntaxErrMesMethodArgIsNotArray0;
									errText = string.Format(errText, name, i + 1);
									break;
								default:
									errText = rule.String ? Properties.Resources.SyntaxErrMesMethodArgIsNotNDStrArray0
										: rule.Int ? Properties.Resources.SyntaxErrMesMethodArgIsNotNDIntArray0 : Properties.Resources.SyntaxErrMesMethodArgIsNotNDArray0;
									errText = string.Format(errText, name, i + 1, dims);
									break;
							}
							if (arguments[i] is VariableTerm varTerm && !(varTerm.Identifier.IsCalc || !rule.AllowConstRef && varTerm.Identifier.IsConst))
							{
								switch (dims)
								{
									case 0: error = typeNotMatch; break;
									case -1: error = !varTerm.Identifier.IsArray1D && !varTerm.Identifier.IsArray2D && !varTerm.Identifier.IsArray3D || typeNotMatch; break;
									case 1: error = !varTerm.Identifier.IsArray1D || typeNotMatch; break;
									case 2: error = !varTerm.Identifier.IsArray2D || typeNotMatch; break;
									case 3: error = !varTerm.Identifier.IsArray3D || typeNotMatch; break;
								}
							}
							else error = true; // 変数ではない
							if (error)
							{
								errMsg[idx] = errText;
								break;
							}
						}
						else if (typeNotMatch)
						{
							var type = rule.SameAsFirst ? arguments[0].GetEraType() : rule.Type;
							errMsg[idx] = type == EraType.String
								? string.Format(Properties.Resources.SyntaxErrMesMethodArgIsNotStr0, name, i + 1)
								: string.Format(Properties.Resources.SyntaxErrMesMethodArgIsNotInt0, name, i + 1);
							break;
						}
					}
					if (errMsg[idx] == null) return null;
				}
				else if (list.OmitStart == -1 && list.ArgTypes.Count > 0 && !variadic)
				{
					// 数固定の引数が必要
					if (list.ArgTypes.Count > 0)
						errMsg[idx] = string.Format(Properties.Resources.SyntaxErrMesMethodArgsCountNotMatches0, name, list.ArgTypes.Count, arguments.Length);
					else
						errMsg[idx] = string.Format(Properties.Resources.SyntaxErrMesMethodArgsNotNeeded0, name);
					continue;
				}
				else if (!argsNotMoreThanRule)
				{
					errMsg[idx] = string.Format(Properties.Resources.SyntaxErrMesMethodTooManyFuncArgs0, name);
					continue;
				}
				else
				{
					errMsg[idx] = string.Format(Properties.Resources.SyntaxErrMesMethodNotEnoughArgs0, name, list.OmitStart < 0 ? list.ArgTypes.Count : list.OmitStart);
					continue;
				}
			}
			if (argumentTypeArrayEx.Length == 1) return errMsg[0];

			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < errMsg.Length; i++)
			{
				sb.Append(string.Format(Properties.Resources.SyntaxErrMesMethodNotValidArgsReason0, i + 1, errMsg[i]));
				if (i + 1 < errMsg.Length) sb.Append(" | ");
			}
			return string.Format(Properties.Resources.SyntaxErrMesMethodNotValidArgs0, name, sb.ToString());
		}
		#endregion

		protected string Name { get; private set; }

		//引数の数・型が一致するかどうかのテスト
		//正しくない場合はエラーメッセージを返す。
		//引数の数が不定である場合や引数の省略を許す場合にはoverrideすること。
		public virtual string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			if (argumentTypeArrayEx != null)
				return CheckArgumentTypeEx(name, arguments);
			if (arguments.Length != argumentTypeArray.Length)
				return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
			for (int i = 0; i < argumentTypeArray.Length; i++)
			{
				if (arguments[i] == null)
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i+1);
				if (argumentTypeArray[i] != arguments[i].GetEraType())
					return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
			}
			return null;
		}
		
		//Argumentが全て定数の時にMethodを解体してよいかどうか。RANDやCharaを参照するものなどは不可
		public bool CanRestructure { get; protected set; }

		//FunctionMethodが固有のRestructure()を持つかどうか
		public bool HasUniqueRestructure { get; protected set; }

		//実際の計算。
		public virtual Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments) { throw new ExeEE("戻り値の型が違う or 未実装"); }
		public virtual string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments) { throw new ExeEE("戻り値の型が違う or 未実装"); }
		public virtual double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments) { throw new ExeEE("戻り値の型が違う or 未実装"); }
		public virtual SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			if (ReturnType == EraType.Integer)
				return new SingleTerm(GetIntValue(exm, arguments));
			else if (ReturnType == EraType.Float)
				return new SingleTerm(GetFloatValue(exm, arguments));
			else
				return new SingleTerm(GetStrValue(exm, arguments));
		}

		protected bool MatchesArgumentType(int index, IOperandTerm argument)
		{
			return argumentTypeArray[index] == argument.GetEraType();
		}

		/// <summary>
		/// 戻り値は全体をRestructureできるかどうか
		/// </summary>
		/// <param name="exm"></param>
		/// <param name="arguments"></param>
		/// <returns></returns>
		public virtual bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
		{ throw new ExeEE("未実装？"); }


		internal void SetMethodName(string name)
		{
			Name = name;
		}
	}

	/// <summary>
	/// Profile-scoped projection for legacy methods whose public ERB contract differs
	/// between v24 and Snake. The legacy implementation remains the execution owner;
	/// this immutable wrapper freezes the selected argument surface when the registry
	/// is projected, so argument validation does not read the profile on every call.
	/// </summary>
	internal sealed class DialectFunctionMethod : FunctionMethod
	{
		private readonly FunctionMethod inner;
		private readonly Func<string, IOperandTerm[], string> checker;
		private readonly Func<IOperandTerm[], IOperandTerm[]> normalizer;

		internal DialectFunctionMethod(
			FunctionMethod inner,
			EraType[] declaredArgumentTypes,
			Func<string, IOperandTerm[], string> checker,
			Func<IOperandTerm[], IOperandTerm[]> normalizer = null)
		{
			this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
			this.checker = checker ?? throw new ArgumentNullException(nameof(checker));
			this.normalizer = normalizer;
			ReturnType = inner.ReturnType;
			CanReturnFloat = inner.CanReturnFloat;
			argumentTypeArray = declaredArgumentTypes;
			CanRestructure = inner.CanRestructure;
			HasUniqueRestructure = inner.HasUniqueRestructure;
		}

		private IOperandTerm[] Prepare(IOperandTerm[] arguments)
		{
			return normalizer == null ? arguments : normalizer(arguments);
		}

		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			return checker(name, arguments);
		}

		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetIntValue(exm, Prepare(arguments));
		}

		public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetStrValue(exm, Prepare(arguments));
		}

		public override double GetFloatValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetFloatValue(exm, Prepare(arguments));
		}

		public override SingleTerm GetReturnValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.GetReturnValue(exm, Prepare(arguments));
		}

		public override bool UniqueRestructure(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			return inner.UniqueRestructure(exm, Prepare(arguments));
		}
	}
}
