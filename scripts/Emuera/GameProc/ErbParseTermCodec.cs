using System;
using System.Collections.Generic;
using System.IO;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameProc
{
	/// <summary>
	/// Phase 2a（P0-C）：IOperandTerm 表达式树 + 高频 Argument 类的二进制 codec。
	/// 契约：写入/读取任何不支持的类型 → 抛 <see cref="ErbParseCache.CacheFormatException"/>
	/// → 调用方按行降级（Argument 不缓存、恢复后由 checkScript.setArgument 重建，Phase 1 行为）。
	///
	/// P2a 覆盖（按真机 dump 指令频率 ≈56% 行）：SingleTerm 三态/NullTerm/VariableTerm（按名重绑+
	/// 名校验）/内建 FunctionMethodTerm（GetFunctionMethod 重建+method 引用相等校验）/VariadicArgTerm/
	/// StrFormTerm（含 Format 三槽位与五个静态槽位特例）；Argument：Expression/Expressions/Void/
	/// ExpressionArray/SpTimes。
	/// 降级（P2b 待覆盖）：OperatorMethod 运算节点（无 op 反查）、UserDefinedMethodTerm/CalledFunction
	/// （CALLF/CALL 系）、VariableStrArgTerm、三元、其余 40 余 Argument 类。
	/// </summary>
	internal static class ErbParseTermCodec
	{
		// 内建 FunctionMethod 注册表（name→单例）的"类型全名→实例"视图。
		// 以 methodDic 引用为缓存键：会话兼容面变化（引用更换）时自动重建。
		static IReadOnlyDictionary<string, FunctionMethod> methodDicSource;
		static Dictionary<string, FunctionMethod> methodByTypeName;

		static FunctionMethod ResolveMethodByTypeName(string typeFullName)
		{
			var source = GlobalStatic.IdentifierDictionary.CodecMethodDic;
			if (source == null)
				return null;
			if (methodByTypeName == null || !ReferenceEquals(methodDicSource, source))
			{
				var map = new Dictionary<string, FunctionMethod>(StringComparer.Ordinal);
				foreach (var pair in source)
				{
					string full = pair.Value.GetType().FullName ?? "";
					if (full.Length > 0 && !map.ContainsKey(full))
						map[full] = pair.Value;
				}
				methodByTypeName = map;
				methodDicSource = source;
			}
			return methodByTypeName.TryGetValue(typeFullName, out FunctionMethod m) ? m : null;
		}

		// ---- term tags ----
		private const byte TNull = 0x00;          // 数组槽位 null
		private const byte TSingleInt = 0x01;
		private const byte TSingleStr = 0x02;
		private const byte TSingleFloat = 0x03;
		private const byte TNullTerm = 0x04;      // NullTerm + eraType
		private const byte TVariable = 0x10;      // name + args
		private const byte TFuncMethod = 0x11;    // 内建 method：name + args（重建后引用校验）
		private const byte TVariadic = 0x12;      // eraType + args
		private const byte TOperator = 0x14;      // kind + op + eraType + 子树（Reduce 系重建）
		private const byte TStrForm = 0x13;       // strs + terms（槽位特例见下）
		// StrForm 槽位特例（terms 元素）
		private const byte TFormatCurly = 0x21;
		private const byte TFormatPercent = 0x22;
		private const byte TFormatYenAt = 0x23;
		private const byte TSlotNameTarget = 0x24;
		private const byte TSlotCallnameMaster = 0x25;
		private const byte TSlotCallnamePlayer = 0x26;
		private const byte TSlotNameAssi = 0x27;
		private const byte TSlotCallnameTarget = 0x28;

		// ---- argument kinds ----
		private const byte ARebuild = 0;          // 不缓存：恢复后 setArgument 重建（Phase 1 行为）
		private const byte AExpression = 1;
		private const byte AExpressions = 2;
		private const byte AVoid = 3;
		private const byte AExpressionArray = 4;
		private const byte ASpTimes = 5;
		private const byte ACase = 6;
		private const byte APrintData = 7;
		private const byte AStrData = 8;

		internal static void WriteTerm(BinaryWriter w, IOperandTerm term)
		{
			switch (term)
			{
				case null:
					w.Write(TNull);
					return;
				case SingleTerm single:
					switch (single.GetEraType())
					{
						case EraType.Integer:
							w.Write(TSingleInt);
							w.Write(single.Int);
							return;
						case EraType.String:
							w.Write(TSingleStr);
							w.Write(single.Str ?? "");
							return;
						case EraType.Float:
							w.Write(TSingleFloat);
							w.Write(single.Float);
							return;
						default:
							throw new ErbParseCache.CacheFormatException("single term eraType");
					}
				case NullTerm nullTerm:
					w.Write(TNullTerm);
					w.Write((byte)nullTerm.GetEraType());
					return;
				case VariableTerm variable:
					// 子类（FixedVariableTerm 等）走 default 降级；精确匹配才走变量路径
					if (variable.GetType() != typeof(VariableTerm))
						goto default;
					if (variable.Identifier == null || string.IsNullOrEmpty(variable.Identifier.Name))
						throw new ErbParseCache.CacheFormatException("variable token name");
					w.Write(TVariable);
					w.Write(variable.Identifier.Name);
					WriteTerms(w, variable.CodecArguments);
					return;
				case FunctionMethodTerm funcMethod:
					var method = funcMethod.CodecMethod;
					if (method is OperatorMethod opMethod)
					{
						// 运算节点：反查 (kind, op)，重建走 Reduce 系纯函数（按子树类型重选实现）
						if (!OperatorMethodManager.TryGetOperatorInfo(opMethod, out byte opKind, out OperatorCode opCode))
							throw new ErbParseCache.CacheFormatException("operator method unknown");
						byte arity = opKind == 2 ? (byte)2 : opKind == 3 ? (byte)3 : (byte)1;
						w.Write(TOperator);
						w.Write(opKind);
						w.Write((int)opCode);
						w.Write((byte)funcMethod.GetEraType());
						w.Write(arity);
						var opArgs = funcMethod.CodecArguments;
						for (int i = 0; i < arity; i++)
							WriteTerm(w, i < opArgs.Length ? opArgs[i] : null);
						return;
					}
					if (method is UserDefinedRefMethod)
						goto default; // 用户 ref method：P2b
					// FunctionMethod.Name 是 protected——经重绑接口取不到；缓存内建方法名用类型注册名：
					// 写侧通过 GetFunctionMethod 反查不可行，改存 method 类型全名 + 重建时按类型注册表反查。
					// 事实上内建 FunctionMethod 集合以单例注册在 FunctionMethodCreator；类型全名即稳定标识。
					string methodTypeName = method.GetType().FullName ?? "";
					if (string.IsNullOrEmpty(methodTypeName))
						throw new ErbParseCache.CacheFormatException("method type");
					w.Write(TFuncMethod);
					w.Write(methodTypeName);
					w.Write((byte)funcMethod.GetEraType());
					WriteTerms(w, funcMethod.CodecArguments);
					return;
				case VariadicArgTerm variadic:
					w.Write(TVariadic);
					w.Write((byte)variadic.GetEraType());
					WriteTermList(w, variadic.CodecArgs);
					return;
				case StrFormTerm strForm:
					StrForm sf = strForm.StrForm;
					if (sf.CodecStrs == null || sf.CodecTerms == null)
						throw new ErbParseCache.CacheFormatException("strform state");
					w.Write(TStrForm);
					w.Write(sf.CodecStrs.Length);
					foreach (string s in sf.CodecStrs)
						w.Write(s ?? "");
					WriteTerms(w, sf.CodecTerms);
					return;
				default:
					throw new ErbParseCache.CacheFormatException("unsupported term " + term.GetType().Name);
			}
		}

		internal static IOperandTerm ReadTerm(BinaryReader r, LabelDictionary labelDic)
		{
			byte tag = r.ReadByte();
			switch (tag)
			{
				case TNull:
					return null;
				case TSingleInt:
					return new SingleTerm(r.ReadInt64());
				case TSingleStr:
					return new SingleTerm(r.ReadString());
				case TSingleFloat:
					return new SingleTerm(r.ReadDouble());
				case TNullTerm:
					// NullTerm 只有 Int64/string 两个构造；Float 型占位极罕见，直接降级
					var nullEraType = (EraType)r.ReadByte();
					return nullEraType switch
					{
						EraType.Integer => new NullTerm(0L),
						EraType.String => new NullTerm(""),
						_ => throw new ErbParseCache.CacheFormatException("nullterm eraType"),
					};
				case TVariable:
				{
					string name = r.ReadString();
					IOperandTerm[] args = ReadTerms(r, labelDic);
					VariableToken token = GlobalStatic.IdentifierDictionary.GetVariableToken(name, null, true);
					// 名校验：带 @ 子键/大小写折叠等场景下重绑结果与原名不一致 → 降级该行
					if (token == null || !string.Equals(token.Name, name, StringComparison.Ordinal))
						throw new ErbParseCache.CacheFormatException("variable rebind drifted: " + name);
					return new VariableTerm(token, args);
				}
				case TFuncMethod:
				{
					string methodTypeFullName = r.ReadString();
					var expectedEraType = (EraType)r.ReadByte();
					IOperandTerm[] args = ReadTerms(r, labelDic);
					// 内建单例按类型全名重绑；项类型经构造器 ResolveEraType 重算后必须与写侧一致，
					// 不一致（重绑漂移/方言差异）→ 降级该行
					FunctionMethod method = ResolveMethodByTypeName(methodTypeFullName);
					if (method == null)
						throw new ErbParseCache.CacheFormatException("method type unknown: " + methodTypeFullName);
					var rebuilt = new FunctionMethodTerm(method, args);
					if (rebuilt.GetEraType() != expectedEraType)
						throw new ErbParseCache.CacheFormatException("method eraType drifted: " + methodTypeFullName);
					return rebuilt;
				}
				case TOperator:
				{
					byte opKind = r.ReadByte();
					var opCode = (OperatorCode)r.ReadInt32();
					var expectedEraType = (EraType)r.ReadByte();
					byte arity = r.ReadByte();
					var opArgs = new IOperandTerm[arity];
					for (int i = 0; i < arity; i++)
						opArgs[i] = ReadTerm(r, labelDic);
					// Reduce 系按子树类型重选实现；重建结果类型必须与写侧一致（折叠/类型组合变化→降级）
					IOperandTerm rebuiltOp = OperatorMethodManager.ReduceByKind(opKind, opCode, opArgs);
					if (rebuiltOp == null || rebuiltOp.GetEraType() != expectedEraType)
						throw new ErbParseCache.CacheFormatException("operator rebuild drifted");
					return rebuiltOp;
				}
				case TVariadic:
				{
					var eraType = (EraType)r.ReadByte();
					var args = ReadTermList(r, labelDic);
					return new VariadicArgTerm(args, eraType);
				}
				case TStrForm:
				{
					int len = r.ReadInt32();
					if (len < 0 || len > 1_000_000)
						throw new ErbParseCache.CacheFormatException("strform strs");
					var strs = new string[len];
					for (int i = 0; i < len; i++)
						strs[i] = r.ReadString();
					IOperandTerm[] terms = ReadStrFormTerms(r, labelDic, out int termCount);
					var sf = StrForm.CodecRestore(strs, terms);
					return new StrFormTerm(sf);
				}
				default:
					throw new ErbParseCache.CacheFormatException("unknown term tag " + tag);
			}
		}

		private static void WriteTerms(BinaryWriter w, IOperandTerm[] terms)
		{
			w.Write(terms == null ? -1 : terms.Length);
			if (terms != null)
				foreach (IOperandTerm t in terms)
					WriteTerm(w, t);
		}

		private static IOperandTerm[] ReadTerms(BinaryReader r, LabelDictionary labelDic)
		{
			int len = r.ReadInt32();
			if (len < 0)
				return null;
			if (len > 1_000_000)
				throw new ErbParseCache.CacheFormatException("terms len");
			var terms = new IOperandTerm[len];
			for (int i = 0; i < len; i++)
				terms[i] = ReadTerm(r, labelDic);
			return terms;
		}

		private static void WriteTermList(BinaryWriter w, List<IOperandTerm> terms)
		{
			w.Write(terms == null ? -1 : terms.Count);
			if (terms != null)
				foreach (IOperandTerm t in terms)
					WriteTerm(w, t);
		}

		private static List<IOperandTerm> ReadTermList(BinaryReader r, LabelDictionary labelDic)
		{
			int len = r.ReadInt32();
			if (len < 0)
				return null;
			if (len > 1_000_000)
				throw new ErbParseCache.CacheFormatException("term list len");
			var terms = new List<IOperandTerm>(len);
			for (int i = 0; i < len; i++)
				terms.Add(ReadTerm(r, labelDic));
			return terms;
		}

		/// <summary>StrForm 槽位：普通子树 + Format 三槽 + 五静态特例。</summary>
		private static void WriteStrFormTerms(BinaryWriter w, IOperandTerm[] terms)
		{
			w.Write(terms.Length);
			foreach (IOperandTerm t in terms)
			{
				if (t is FunctionMethodTerm fm)
				{
					var method = fm.CodecMethod;
					if (ReferenceEquals(method, StrForm.CodecFormatCurlyBrace)) { w.Write(TFormatCurly); WriteTerms(w, fm.CodecArguments); continue; }
					if (ReferenceEquals(method, StrForm.CodecFormatPercent)) { w.Write(TFormatPercent); WriteTerms(w, fm.CodecArguments); continue; }
					if (ReferenceEquals(method, StrForm.CodecFormatYenAt)) { w.Write(TFormatYenAt); WriteTerms(w, fm.CodecArguments); continue; }
					if (ReferenceEquals(t, StrForm.CodecNameTarget)) { w.Write(TSlotNameTarget); continue; }
					if (ReferenceEquals(t, StrForm.CodecCallnameMaster)) { w.Write(TSlotCallnameMaster); continue; }
					if (ReferenceEquals(t, StrForm.CodecCallnamePlayer)) { w.Write(TSlotCallnamePlayer); continue; }
					if (ReferenceEquals(t, StrForm.CodecNameAssi)) { w.Write(TSlotNameAssi); continue; }
					if (ReferenceEquals(t, StrForm.CodecCallnameTarget)) { w.Write(TSlotCallnameTarget); continue; }
				}
				WriteTerm(w, t);
			}
		}

		private static IOperandTerm[] ReadStrFormTerms(BinaryReader r, LabelDictionary labelDic, out int count)
		{
			count = r.ReadInt32();
			if (count < 0 || count > 1_000_000)
				throw new ErbParseCache.CacheFormatException("strform terms len");
			var terms = new IOperandTerm[count];
			for (int i = 0; i < count; i++)
			{
				// 先窥探槽位特例 tag（无子树），否则按普通 term 读
				byte tag = r.ReadByte();
				IOperandTerm term = tag switch
				{
					TFormatCurly => new FunctionMethodTerm(StrForm.CodecFormatCurlyBrace, ReadTerms(r, labelDic)),
					TFormatPercent => new FunctionMethodTerm(StrForm.CodecFormatPercent, ReadTerms(r, labelDic)),
					TFormatYenAt => new FunctionMethodTerm(StrForm.CodecFormatYenAt, ReadTerms(r, labelDic)),
					TSlotNameTarget => StrForm.CodecNameTarget,
					TSlotCallnameMaster => StrForm.CodecCallnameMaster,
					TSlotCallnamePlayer => StrForm.CodecCallnamePlayer,
					TSlotNameAssi => StrForm.CodecNameAssi,
					TSlotCallnameTarget => StrForm.CodecCallnameTarget,
					_ => ReadTermBody(r, labelDic, tag),
				};
				terms[i] = term;
			}
			return terms;
		}

		/// <summary>已消费 tag 的 term 分派（供槽位读取器复用主体逻辑）。</summary>
		private static IOperandTerm ReadTermBody(BinaryReader r, LabelDictionary labelDic, byte tag)
		{
			switch (tag)
			{
				case TNull: return null;
				case TSingleInt: return new SingleTerm(r.ReadInt64());
				case TSingleStr: return new SingleTerm(r.ReadString());
				case TSingleFloat: return new SingleTerm(r.ReadDouble());
				default:
					throw new ErbParseCache.CacheFormatException("strform slot tag " + tag);
			}
		}

		// ============ Argument 层 ============

		/// <summary>写行级 Argument。不支持类型/主体失败 → 写 ARebuild 让该行降级重建（不污染流）。</summary>
		internal static void WriteArgument(BinaryWriter w, Argument arg)
		{
			try
			{
				// 内存试写：主体任何失败都不会在真流留下半个条目（Seek 回退法会流错位）
				using (var probe = new MemoryStream())
				using (var pw = new BinaryWriter(probe))
				{
					WriteArgumentCore(pw, arg);
					pw.Flush();
					w.Write(probe.ToArray(), 0, (int)probe.Length);
				}
			}
			catch (Exception)
			{
				w.Write(ARebuild);
			}
		}

		private static void WriteArgumentCore(BinaryWriter w, Argument arg)
		{
			switch (arg)
			{
				case null:
					w.Write(ARebuild);
					return;
				case ExpressionArgument expression:
					w.Write(AExpression);
					WriteBase(w, expression);
					w.Write(expression.EnablePointerInputMetadata);
					WriteTerm(w, expression.Term);
					return;
				case ExpressionsArgument expressions:
					w.Write(AExpressions);
					WriteBase(w, expressions);
					w.Write(expressions.ArgumentTypeArray.Length);
					foreach (EraType t in expressions.ArgumentTypeArray)
						w.Write((byte)t);
					WriteTerms(w, expressions.ArgumentArray);
					return;
				case VoidArgument:
					w.Write(AVoid);
					WriteBase(w, arg);
					return;
				case ExpressionArrayArgument array:
					w.Write(AExpressionArray);
					WriteBase(w, array);
					WriteTerms(w, array.TermList);
					return;
				case SpTimesArgument times:
					w.Write(ASpTimes);
					WriteBase(w, times);
					WriteTerm(w, times.VariableDest);
					WriteTerm(w, times.Multiplier);
					return;
				case CaseArgument caseArg:
					w.Write(ACase);
					WriteBase(w, caseArg);
					w.Write(caseArg.CaseExps.Length);
					foreach (CaseExpression ce in caseArg.CaseExps)
					{
						w.Write((byte)ce.CaseType);
						w.Write((int)ce.Operator);
						WriteTerm(w, ce.LeftTerm);
						WriteTerm(w, ce.RightTerm);
					}
					return;
				case PrintDataArgument printData:
					w.Write(APrintData);
					WriteBase(w, printData);
					WriteTerm(w, printData.Var);
					return;
				case StrDataArgument strData:
					w.Write(AStrData);
					WriteBase(w, strData);
					WriteTerm(w, strData.Var);
					return;
				default:
					throw new ErbParseCache.CacheFormatException("unsupported argument " + arg.GetType().Name);
			}
		}

		private static void WriteBase(BinaryWriter w, Argument arg)
		{
			w.Write(arg.IsConst);
			w.Write(arg.ConstStr ?? "");
			w.Write(arg.ConstInt);
			w.Write(arg.ConstFloat);
		}

		/// <summary>读行级 Argument；kind==ARebuild 返回 null（调用方走 setArgument 重建）。</summary>
		internal static Argument ReadArgument(BinaryReader r, LabelDictionary labelDic)
		{
			byte kind = r.ReadByte();
			switch (kind)
			{
				case ARebuild:
					return null;
				case AExpression:
				{
					var b = ReadBase(r);
					bool pointerMeta = r.ReadBoolean();
					var term = ReadTerm(r, labelDic);
					return new ExpressionArgument(term, pointerMeta) { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				case AExpressions:
				{
					var b = ReadBase(r);
					int len = r.ReadInt32();
					var types = new EraType[len];
					for (int i = 0; i < len; i++)
						types[i] = (EraType)r.ReadByte();
					var terms = ReadTerms(r, labelDic);
					return new ExpressionsArgument(types, terms) { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				case AVoid:
				{
					var b = ReadBase(r);
					return new VoidArgument() { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				case AExpressionArray:
				{
					var b = ReadBase(r);
					var terms = ReadTerms(r, labelDic);
					return new ExpressionArrayArgument(terms == null ? null : new List<IOperandTerm>(terms)) { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				case ASpTimes:
				{
					var b = ReadBase(r);
					var dest = ReadTerm(r, labelDic);
					var mult = ReadTerm(r, labelDic);
					return new SpTimesArgument((VariableTerm)dest, mult) { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				case ACase:
				{
					var b = ReadBase(r);
					int len = r.ReadInt32();
					if (len < 0 || len > 1_000_000)
						throw new ErbParseCache.CacheFormatException("case len");
					var exps = new CaseExpression[len];
					for (int i = 0; i < len; i++)
					{
						var caseType = (CaseExpressionType)r.ReadByte();
						var op = (OperatorCode)r.ReadInt32();
						var left = ReadTerm(r, labelDic);
						var right = ReadTerm(r, labelDic);
						exps[i] = new CaseExpression { CaseType = caseType, Operator = op, LeftTerm = left, RightTerm = right };
					}
					return new CaseArgument(exps) { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				case APrintData:
				{
					var b = ReadBase(r);
					return new PrintDataArgument((VariableTerm)ReadTerm(r, labelDic)) { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				case AStrData:
				{
					var b = ReadBase(r);
					return new StrDataArgument((VariableTerm)ReadTerm(r, labelDic)) { IsConst = b.isConst, ConstStr = b.constStr, ConstInt = b.constInt, ConstFloat = b.constFloat };
				}
				default:
					throw new ErbParseCache.CacheFormatException("unknown argument kind " + kind);
			}
		}

		private static (bool isConst, string constStr, long constInt, double constFloat) ReadBase(BinaryReader r)
		{
			bool isConst = r.ReadBoolean();
			string constStr = r.ReadString();
			long constInt = r.ReadInt64();
			double constFloat = r.ReadDouble();
			return (isConst, constStr.Length == 0 ? null : constStr, constInt, constFloat);
		}
	}
}
