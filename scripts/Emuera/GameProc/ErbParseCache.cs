using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc.Function;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameProc
{
	/// <summary>
	/// ERB 解析产物磁盘缓存（Phase 1：行骨架序列化，不含 Argument/JumpTo 等 check 产物）。
	/// 设计文档 docs/plans/2026-11-21-parse-cache-design.md。
	/// 安全哲学：任何格式异常/未知类型 → 抛 CacheFormatException → 调用方回退全量解析（最坏后果是没加速）。
	/// Phase 1 语义约定（加载后由既有流程重建，行为不变）：
	///  - InstructionLine.Argument = null（checkScript.setArgument 重付，含 FORCE_SETARG）
	///  - JumpTo/JumpToEndCatch/IfCaseList/SelectCaseJumpTable/dataList/callList = null（checkScript 重算）
	///  - LoopCounter = null；FunctionLabelLine.Arg = null（setLabelsArg/setArgument 重建）
	///  - WordCollection 仅支持非 StrFormWord/TermWord 子类（遇之抛格式异常 → 该文件不落缓存）
	/// </summary>
	internal static class ErbParseCache
	{
		internal const int ParserCacheVersion = 2;
		private static readonly byte[] Magic = { (byte)'G', (byte)'P', (byte)'C', (byte)'2' };

		// 行类型 tag
		private const byte TagNullLine = 0;
		private const byte TagInstructionLine = 1;
		private const byte TagGotoLabelLine = 2;
		private const byte TagFunctionLabelLine = 3;
		private const byte TagInvalidLine = 4;
		private const byte TagInvalidLabelLine = 5;

		// Word kind tag（由 C# 类型分派，独立于 Word.Type 字符以消除 Symbol 歧义）
		private const byte WordKindNull = 0;
		private const byte WordKindIdentifier = 1;
		private const byte WordKindLiteralInteger = 2;
		private const byte WordKindLiteralFloat = 3;
		private const byte WordKindLiteralString = 4;
		private const byte WordKindOperator = 5;
		private const byte WordKindSymbol = 6;
		private const byte WordKindMacro = 7;
		// StrFormWord / TermWord 不支持 → CacheFormatException

		internal sealed class CacheFormatException : Exception
		{
			public CacheFormatException(string message) : base(message) { }
		}

		/// <summary>计算缓存文件全路径。键=解析器版本+相对路径+mtime+长度+方言哈希+ERH 快照哈希。</summary>
		public static string BuildCacheFilePath(string cacheDir, string relativePath, DateTime mtimeUtc, long byteLength, string planHash, string erhSnapshotHash)
		{
			string key = string.Join("|",
				ParserCacheVersion.ToString(),
				relativePath,
				mtimeUtc.Ticks.ToString(),
				byteLength.ToString(),
				planHash ?? "",
				erhSnapshotHash ?? "");
			using (var sha = System.Security.Cryptography.SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
				var sb = new StringBuilder(hash.Length * 2);
				foreach (byte b in hash)
					sb.Append(b.ToString("x2"));
				return Path.Combine(cacheDir, sb.ToString(0, 32) + ".bin");
			}
		}

		/// <summary>ERH 文件集快照哈希：宏变化会改变 ERB 解析结果，必须参与缓存键。</summary>
		public static string BuildErhSnapshotHash(IEnumerable<KeyValuePair<string, DateTime>> erhFiles)
		{
			var sb = new StringBuilder();
			foreach (var kv in erhFiles)
				sb.Append(kv.Key).Append(':').Append(kv.Value.Ticks).Append(';');
			using (var sha = System.Security.Cryptography.SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
				var s = new StringBuilder(hash.Length * 2);
				foreach (byte b in hash)
					s.Append(b.ToString("x2"));
				return s.ToString(0, 32);
			}
		}

		/// <summary>从链头序列化行序列。任何不支持的图形状抛 CacheFormatException。</summary>
		public static void WriteLines(Stream stream, LogicalLine head)
		{
			using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
			{
				w.Write(Magic);
				w.Write((byte)2); // 条目内格式版本（v2：AssignOperator 由 byte 改 int，修 (byte) 截断 510→254）
				int count = 0;
				for (var line = head; line != null; line = line.NextLine)
					count++;
				w.Write(count);
				for (var line = head; line != null; line = line.NextLine)
					WriteLine(w, line);
				w.Flush();
			}
		}

		/// <summary>反序列化行序列（原序；调用方负责串 NextLine 链、label 注册与 privateVar 重放）。</summary>
		public static List<LogicalLine> ReadLines(Stream stream)
		{
			using (var r = new BinaryReader(stream, Encoding.UTF8, true))
			{
				byte[] magic = r.ReadBytes(4);
				if (magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
					throw new CacheFormatException("bad magic");
				byte entryVersion = r.ReadByte();
				if (entryVersion != 2)
					throw new CacheFormatException("bad entry version");
				int count = r.ReadInt32();
				if (count < 0 || count > 1_000_000)
					throw new CacheFormatException("bad line count");
				var lines = new List<LogicalLine>(count);
				for (int i = 0; i < count; i++)
					lines.Add(ReadLine(r));
				return lines;
			}
		}

		private static void WriteLine(BinaryWriter w, LogicalLine line)
		{
			switch (line)
			{
				case NullLine:
					w.Write(TagNullLine);
					WritePosition(w, line);
					break;
				case InstructionLine ins:
					w.Write(TagInstructionLine);
					WritePosition(w, line);
					WriteInstruction(w, ins);
					break;
				case GotoLabelLine gotoLabel:
					w.Write(TagGotoLabelLine);
					WritePosition(w, line);
					w.Write(gotoLabel.LabelName ?? "");
					break;
				case InvalidLabelLine invalidLabel:
					w.Write(TagInvalidLabelLine);
					WritePosition(w, line);
					w.Write(invalidLabel.LabelName ?? "");
					w.Write(invalidLabel.ErrMes ?? "");
					break;
				case InvalidLine invalid:
					w.Write(TagInvalidLine);
					WritePosition(w, line);
					w.Write(invalid.ErrMes ?? "");
					break;
				case FunctionLabelLine label:
					w.Write(TagFunctionLabelLine);
					WritePosition(w, line);
					WriteFunctionLabel(w, label);
					break;
				default:
					throw new CacheFormatException("unsupported line type: " + line.GetType().Name);
			}
		}

		private static LogicalLine ReadLine(BinaryReader r)
		{
			byte tag = r.ReadByte();
			ScriptPosition position = ReadPosition(r);
			switch (tag)
			{
				case TagNullLine:
					return new NullLine();
				case TagInstructionLine:
					return ReadInstruction(r, position);
				case TagGotoLabelLine:
					return new GotoLabelLine(position, r.ReadString());
				case TagInvalidLabelLine:
					return new InvalidLabelLine(position, r.ReadString(), r.ReadString());
				case TagInvalidLine:
					return new InvalidLine(position, r.ReadString());
				case TagFunctionLabelLine:
					return ReadFunctionLabel(r, position);
				default:
					throw new CacheFormatException("unknown line tag " + tag);
			}
		}

		private static void WritePosition(BinaryWriter w, LogicalLine line)
		{
			ScriptPosition pos = line.Position;
			w.Write(pos == null ? "" : (pos.Filename ?? ""));
			w.Write(pos == null ? -1 : pos.LineNo);
		}

		private static ScriptPosition ReadPosition(BinaryReader r)
		{
			string filename = r.ReadString();
			int lineNo = r.ReadInt32();
			return filename == "" && lineNo == -1 ? null : new ScriptPosition(filename, lineNo);
		}

		private static void WriteInstruction(BinaryWriter w, InstructionLine ins)
		{
			w.Write((int)ins.FunctionCode);
			w.Write(ins.Function.Name ?? "");
			// OperatorCode 值含标志位（Assignment=0x01FE 等，远超 byte）——必须 int 序列化
			w.Write((int)ins.AssignOperator);
			WordCollection dest = ins.AssignmentDest;
			w.Write(dest == null ? (byte)0 : (byte)1);
			if (dest != null)
				WriteWordCollection(w, dest);
			w.Write(ins.PeekArgumentPrimitive() ?? "");
			w.Write(ins.LoopEnd);
			w.Write(ins.LoopStep);
			w.Write((byte)0); // LoopCounter：Phase 1 恒 null（FOR 语义由 check/运行期重建）
		}

		private static InstructionLine ReadInstruction(BinaryReader r, ScriptPosition position)
		{
			int funcCode = r.ReadInt32();
			string funcName = r.ReadString();
			// SET（代入文）是静态单例直引（FunctionIdentifier.SETFunction），不在字典查找链里
			// （解析端赋值行同样直引该单例）——按名查字典会得到 null → "drifted"。其余指令按名重建。
			FunctionIdentifier func = (FunctionCode)funcCode == FunctionCode.SET
				? FunctionIdentifier.SETFunction
				: GlobalStatic.IdentifierDictionary.GetFunctionIdentifier(funcName);
			if (func == null || (int)func.Code != funcCode)
				throw new CacheFormatException("function identifier drifted: " + funcName);
			byte assignOpUnused = 0;
			var assignOpCode = (OperatorCode)r.ReadInt32();
			byte hasDest = r.ReadByte();
			WordCollection dest = null;
			if (hasDest != 0)
				dest = ReadWordCollection(r);
			string argPrimitive = r.ReadString();
			InstructionLine line = dest == null
				? new InstructionLine(position, func, new StringStream(argPrimitive))
				: new InstructionLine(position, func, assignOpCode, dest, new StringStream(argPrimitive));
			line.LoopEnd = r.ReadInt64();
			line.LoopStep = r.ReadInt64();
			r.ReadByte(); // LoopCounter null 标记
			return line;
		}

		private static void WriteFunctionLabel(BinaryWriter w, FunctionLabelLine label)
		{
			w.Write(label.LabelName ?? "");
			w.Write(label.IsEvent);
			w.Write(label.IsSystem);
			w.Write(label.IsSingle);
			w.Write(label.IsPri);
			w.Write(label.IsLater);
			w.Write(label.IsOnly);
			w.Write(label.hasPrivDynamicVar);
			w.Write(label.LocalLength);
			w.Write(label.LocalsLength);
			w.Write(label.LocalFloatLength);
			w.Write(label.ArgLength);
			w.Write(label.ArgsLength);
			w.Write(label.ArgFloatLength);
			w.Write(label.VariadicArgIndex);
			w.Write(label.IsMethod);
			w.Write((byte)label.MethodType);
			w.Write((byte)0); // Arg：Phase 1 恒 null（setLabelsArg/setArgument 重建）
			WordCollection wc = label.PeekRowArgs;
			w.Write(wc == null ? (byte)0 : (byte)1);
			if (wc != null)
				WriteWordCollection(w, wc);
			w.Write(label.Depth);
			w.Write(label.Index);
			w.Write(label.FileIndex);
			IReadOnlyList<UserDefinedVariableData> privData = label.PrivateVarDataCache;
			w.Write(privData == null ? 0 : privData.Count);
			if (privData != null)
				foreach (UserDefinedVariableData data in privData)
					WriteUserDefinedVariableData(w, data);
		}

		private static FunctionLabelLine ReadFunctionLabel(BinaryReader r, ScriptPosition position)
		{
			var label = new FunctionLabelLine(position, r.ReadString(), null);
			label.IsEvent = r.ReadBoolean();
			label.IsSystem = r.ReadBoolean();
			label.IsSingle = r.ReadBoolean();
			label.IsPri = r.ReadBoolean();
			label.IsLater = r.ReadBoolean();
			label.IsOnly = r.ReadBoolean();
			label.hasPrivDynamicVar = r.ReadBoolean();
			label.LocalLength = r.ReadInt32();
			label.LocalsLength = r.ReadInt32();
			label.LocalFloatLength = r.ReadInt32();
			label.ArgLength = r.ReadInt32();
			label.ArgsLength = r.ReadInt32();
			label.ArgFloatLength = r.ReadInt32();
			label.VariadicArgIndex = r.ReadInt32();
			label.IsMethod = r.ReadBoolean();
			label.MethodType = (EraType)r.ReadByte();
			r.ReadByte(); // Arg null 标记
			byte hasWc = r.ReadByte();
			if (hasWc != 0)
			{
				label.RestoreRowArgs(ReadWordCollection(r));
			}
			label.Depth = r.ReadInt32();
			label.Index = r.ReadInt32();
			label.FileIndex = r.ReadInt32();
			int privCount = r.ReadInt32();
			for (int i = 0; i < privCount; i++)
			{
				UserDefinedVariableData data = ReadUserDefinedVariableData(r);
				label.AddPrivateVariable(data);
			}
			return label;
		}

		private static void WriteWordCollection(BinaryWriter w, WordCollection wc)
		{
			w.Write(wc.Collection.Count);
			foreach (Word word in wc.Collection)
			{
				switch (word)
				{
					case NullWord:
						w.Write(WordKindNull);
						break;
					case IdentifierWord identifier:
						w.Write(WordKindIdentifier);
						w.Write(identifier.Code ?? "");
						break;
					case LiteralIntegerWord literalInt:
						w.Write(WordKindLiteralInteger);
						w.Write(literalInt.Int);
						break;
					case LiteralFloatWord literalFloat:
						w.Write(WordKindLiteralFloat);
						w.Write(literalFloat.Float);
						break;
					case LiteralStringWord literalString:
						w.Write(WordKindLiteralString);
						w.Write(literalString.Str ?? "");
						break;
					case OperatorWord op:
						w.Write(WordKindOperator);
						w.Write((int)op.Code);
						break;
					case SymbolWord symbol:
						w.Write(WordKindSymbol);
						w.Write(symbol.Type);
						break;
					case MacroWord macro:
						w.Write(WordKindMacro);
						w.Write(macro.Number);
						break;
					default:
						// StrFormWord / TermWord 等未支持子类 → 该文件放弃缓存
						throw new CacheFormatException("unsupported word type: " + word.GetType().Name);
				}
			}
		}

		private static WordCollection ReadWordCollection(BinaryReader r)
		{
			var wc = new WordCollection();
			int count = r.ReadInt32();
			if (count < 0 || count > 1_000_000)
				throw new CacheFormatException("bad word count");
			for (int i = 0; i < count; i++)
			{
				byte kind = r.ReadByte();
				switch (kind)
				{
					case WordKindNull:
						wc.Add(new NullWord());
						break;
					case WordKindIdentifier:
						wc.Add(new IdentifierWord(r.ReadString()));
						break;
					case WordKindLiteralInteger:
						wc.Add(new LiteralIntegerWord(r.ReadInt64()));
						break;
					case WordKindLiteralFloat:
						wc.Add(new LiteralFloatWord(r.ReadDouble()));
						break;
					case WordKindLiteralString:
						wc.Add(new LiteralStringWord(r.ReadString()));
						break;
					case WordKindOperator:
						wc.Add(new OperatorWord((OperatorCode)r.ReadInt32()));
						break;
					case WordKindSymbol:
						wc.Add(new SymbolWord(r.ReadChar()));
						break;
					case WordKindMacro:
						wc.Add(new MacroWord(r.ReadInt32()));
						break;
					default:
						throw new CacheFormatException("unknown word kind " + kind);
				}
			}
			return wc;
		}

		private static void WriteUserDefinedVariableData(BinaryWriter w, UserDefinedVariableData data)
		{
			w.Write(data.Name ?? "");
			w.Write(data.TypeIsStr);
			w.Write(data.TypeIsFloat);
			w.Write(data.Reference);
			w.Write(data.Out);
			w.Write(data.Dimension);
			WriteInt32Array(w, data.Lengths);
			WriteInt64Array(w, data.DefaultInt);
			WriteStringArray(w, data.DefaultStr);
			WriteDoubleArray(w, data.DefaultFloat);
			w.Write(data.Global);
			w.Write(data.Save);
			w.Write(data.Static);
			w.Write(data.Private);
			w.Write(data.CharaData);
			w.Write(data.Const);
		}

		private static UserDefinedVariableData ReadUserDefinedVariableData(BinaryReader r)
		{
			return new UserDefinedVariableData
			{
				Name = r.ReadString(),
				TypeIsStr = r.ReadBoolean(),
				TypeIsFloat = r.ReadBoolean(),
				Reference = r.ReadBoolean(),
				Out = r.ReadBoolean(),
				Dimension = r.ReadInt32(),
				Lengths = ReadInt32Array(r),
				DefaultInt = ReadInt64Array(r),
				DefaultStr = ReadStringArray(r),
				DefaultFloat = ReadDoubleArray(r),
				Global = r.ReadBoolean(),
				Save = r.ReadBoolean(),
				Static = r.ReadBoolean(),
				Private = r.ReadBoolean(),
				CharaData = r.ReadBoolean(),
				Const = r.ReadBoolean(),
			};
		}

		private static void WriteInt32Array(BinaryWriter w, int[] a)
		{
			w.Write(a == null ? -1 : a.Length);
			if (a != null)
				foreach (int v in a)
					w.Write(v);
		}

		/// <summary>诊断转储：把行序列的关键状态展开为多行文本（缓存恢复 vs 全量解析 对比用）。</summary>
		internal static string DumpLines(IEnumerable<LogicalLine> lines)
		{
			var sb = new StringBuilder();
			foreach (var line in lines)
			{
				switch (line)
				{
					case InstructionLine ins:
						sb.Append("INSTR ").Append(ins.FunctionCode).Append(" name=").Append(ins.Function.Name)
							.Append(" op=").Append((int)ins.AssignOperator)
							.Append(" destWords=").Append(ins.AssignmentDest == null ? -1 : ins.AssignmentDest.Collection.Count)
							.Append(" argPrim=").Append(ins.PeekArgumentPrimitive() == null ? "<null>" : "\"" + ins.PeekArgumentPrimitive() + "\"")
							.Append(" arg=").Append(ins.Argument == null ? "null" : ins.Argument.GetType().Name)
							.Append(" jumpTo=").Append(ins.JumpTo == null ? "null" : "set");
						break;
					case InvalidLabelLine invalidLabel:
						sb.Append("INVALID_LABEL err=").Append(invalidLabel.ErrMes ?? "");
						break;
					case FunctionLabelLine label:
						sb.Append("LABEL ").Append(label.LabelName).Append(" idx=").Append(label.Index).Append('/').Append(label.FileIndex)
							.Append(" depth=").Append(label.Depth).Append(" localLen=").Append(label.LocalLength)
							.Append(" argsLen=").Append(label.ArgsLength).Append(" argFloatLen=").Append(label.ArgFloatLength)
							.Append(" method=").Append(label.IsMethod).Append(" priv=").Append(label.PrivateVarDataCache == null ? -1 : label.PrivateVarDataCache.Count)
							.Append(" wc=").Append(label.PeekRowArgs == null ? -1 : label.PeekRowArgs.Collection.Count);
						if (label.PeekRowArgs != null)
						{
							sb.Append(" wcWords=[");
							foreach (Word word in label.PeekRowArgs.Collection)
							{
								string payload = word switch
								{
									IdentifierWord ident => ident.Code ?? "",
									LiteralIntegerWord litInt => litInt.Int.ToString(),
									LiteralFloatWord litFloat => litFloat.Float.ToString(),
									LiteralStringWord litStr => litStr.Str ?? "",
									OperatorWord opWord => ((int)opWord.Code).ToString(),
									SymbolWord symWord => symWord.Type.ToString(),
									MacroWord macroWord => "M" + macroWord.Number,
									_ => word.GetType().Name,
								};
								sb.Append('(').Append(word.Type).Append(')').Append(payload).Append(' ');
							}
							sb.Append(']');
						}
						break;
					case GotoLabelLine gotoLabel:
						sb.Append("GOTO ").Append(gotoLabel.LabelName);
						break;
					case InvalidLine invalid:
						sb.Append("INVALID err=").Append(invalid.ErrMes ?? "");
						break;
					case NullLine:
						sb.Append("NULL");
						break;
					default:
						sb.Append("UNKNOWN ").Append(line.GetType().Name);
						break;
				}
				sb.Append('\n');
			}
			return sb.ToString();
		}

		private static int[] ReadInt32Array(BinaryReader r)
		{
			int len = r.ReadInt32();
			if (len < 0)
				return null;
			var a = new int[len];
			for (int i = 0; i < len; i++)
				a[i] = r.ReadInt32();
			return a;
		}

		private static void WriteInt64Array(BinaryWriter w, long[] a)
		{
			w.Write(a == null ? -1 : a.Length);
			if (a != null)
				foreach (long v in a)
					w.Write(v);
		}

		private static long[] ReadInt64Array(BinaryReader r)
		{
			int len = r.ReadInt32();
			if (len < 0)
				return null;
			var a = new long[len];
			for (int i = 0; i < len; i++)
				a[i] = r.ReadInt64();
			return a;
		}

		private static void WriteStringArray(BinaryWriter w, string[] a)
		{
			w.Write(a == null ? -1 : a.Length);
			if (a != null)
				foreach (string v in a)
					w.Write(v ?? "");
		}

		private static string[] ReadStringArray(BinaryReader r)
		{
			int len = r.ReadInt32();
			if (len < 0)
				return null;
			var a = new string[len];
			for (int i = 0; i < len; i++)
				a[i] = r.ReadString();
			return a;
		}

		private static void WriteDoubleArray(BinaryWriter w, double[] a)
		{
			w.Write(a == null ? -1 : a.Length);
			if (a != null)
				foreach (double v in a)
					w.Write(v);
		}

		private static double[] ReadDoubleArray(BinaryReader r)
		{
			int len = r.ReadInt32();
			if (len < 0)
				return null;
			var a = new double[len];
			for (int i = 0; i < len; i++)
				a[i] = r.ReadDouble();
			return a;
		}
	}
}
