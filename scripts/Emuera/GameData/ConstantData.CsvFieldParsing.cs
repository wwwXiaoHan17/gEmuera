// ConstantData.CsvFieldParsing.cs —— 承载 CSV 字段解析原语与并行载入副作用重放功能域，自 ConstantData.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.GameData.Variable;

namespace MinorShift.Emuera.GameData
{
	internal sealed partial class ConstantData
	{
		private static ParallelOptions GetCsvParallelOptions()
		{
			// 移动端压低并发（与 Preload 预读同一策略），桌面限制在 4 以内避免启动期争抢。
			int degree = global::Godot.OS.HasFeature("mobile") ? 2 : Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
			return new ParallelOptions { MaxDegreeOfParallelism = degree };
		}

		/// <summary>CSV 字段在源字符串中的 (start,len) 切片。零分配：只在真正需要时 materialize 成 string。</summary>
		private readonly struct CsvFieldRange
		{
			public readonly int Start;
			public readonly int Length;
			public CsvFieldRange(int start, int length)
			{
				Start = start;
				Length = length;
			}
		}

		/// <summary>
		/// 直接从 StringStream 底层 source 按 (start,len) 切片解析 CSV 头部字段，不复制整行，也不为未使用的字段分配字符串。
		/// 语义与原版裸逗号 Split 完全一致：分隔符 ','、无引号处理、空字段 → 空串、不做任何 Trim。
		/// 返回字段总数（逗号数+1），与源行的字段数一致。
		/// </summary>
		private static int ReadCsvHeadFields(string source, int startOffset, Span<CsvFieldRange> fields)
		{
			if (source == null)
				source = "";
			int count = 1;
			int fieldIndex = 0;
			int start = startOffset;
			for (int i = startOffset; i <= source.Length; i++)
			{
				if (i < source.Length && source[i] != ',')
					continue;
				if (fieldIndex < fields.Length)
					fields[fieldIndex] = new CsvFieldRange(start, i - start);
				fieldIndex++;
				if (i >= source.Length)
					break;
				count++;
				start = i + 1;
			}
			return count;
		}

		private static string GetFieldString(string source, CsvFieldRange f)
		{
			return source.Substring(f.Start, f.Length);
		}

		/// <summary>等价于 token.Trim()：去掉首尾 char.IsWhiteSpace 后再 materialize。</summary>
		private static string GetTrimmedFieldString(string source, CsvFieldRange f)
		{
			int start = f.Start;
			int end = f.Start + f.Length;
			while (start < end && char.IsWhiteSpace(source[start]))
				start++;
			while (end > start && char.IsWhiteSpace(source[end - 1]))
				end--;
			return source.Substring(start, end - start);
		}

		/// <summary>返回去掉尾随空白后的字段长度（等价于 token.TrimEnd().Length）。</summary>
		private static int TrimEndLength(string source, CsvFieldRange f)
		{
			int end = f.Start + f.Length;
			while (end > f.Start && char.IsWhiteSpace(source[end - 1]))
				end--;
			return end - f.Start;
		}

		/// <summary>零分配字段比较：source[Start..Start+Length] 与 word 按 comp 比较。</summary>
		private static bool FieldEquals(string source, CsvFieldRange f, string word, StringComparison comp)
		{
			return source.AsSpan(f.Start, f.Length).Equals(word.AsSpan(), comp);
		}

		private static bool FieldEqualsRange(string source, int start, int length, string word, StringComparison comp)
		{
			return source.AsSpan(start, length).Equals(word.AsSpan(), comp);
		}

		/// <summary>
		/// varname 判断：把字段与 ASCII 大写/日文常量逐字符比较，'a'-'z' 视为大写。
		/// 与 token0.ToUpper() 的 switch 结果等价：所有可能命中的常量只含 ASCII 大写字母与无大小写的日文。
		/// 零分配（相对原 token0.ToUpper() 每行分配一个字符串）。
		/// </summary>
		private static bool FieldMatchesUpper(string source, CsvFieldRange f, string upperWord)
		{
			if (f.Length != upperWord.Length)
				return false;
			for (int i = 0; i < f.Length; i++)
			{
				char c = source[f.Start + i];
				if (c >= 'a' && c <= 'z')
					c = (char)(c - 32);
				if (c != upperWord[i])
					return false;
			}
			return true;
		}

		private static bool StartsWithDigitAfterTrim(string source, CsvFieldRange f)
		{
			for (int i = f.Start; i < f.Start + f.Length; i++)
			{
				if (!char.IsWhiteSpace(source[i]))
					return char.IsDigit(source[i]);
			}
			return false;
		}

		private static readonly IList<char> hexadecimalDigits = new char[] { 'a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', 'C', 'D', 'E', 'F' };

		private bool tryToInt64(string str, out Int64 p)
		{
			if (string.IsNullOrEmpty(str))
			{
				p = -1;
				return false;
			}
			return tryToInt64(str, 0, str.Length, out p);
		}

		/// <summary>
		/// (source, start, length) 版本：不再为每次解析 new StringStream，避免每行 2 次短命对象分配。
		/// 语义与旧 tryToInt64 完全一致（含 0x/0b 前缀与 p/e 指数、char.IsDigit 全角数字、Convert.ToInt64 异常捕获）。
		/// </summary>
		private bool tryToInt64(string source, int start, int length, out Int64 p)
		{
			p = -1;
			if (length <= 0)
				return false;
			int i = start;
			int end = start + length;
			int sign = 1;
			if (source[i] == '+')
				i++;
			else if (source[i] == '-')
			{
				sign = -1;
				i++;
			}
			//1803beta005 char.IsDigitは全角数字とかまでひろってしまうので･･･
			//if (!char.IsDigit(st.Current))
			// return false;
			if (i >= end)
				return false;
			switch (source[i])
			{
				case '0':
				case '1':
				case '2':
				case '3':
				case '4':
				case '5':
				case '6':
				case '7':
				case '8':
				case '9':
					break;
				default:
					return false;
			}
			try
			{
				p = readInt64Range(source, i, end);
				p *= sign;
			}
			catch
			{
				return false;
			}
			return true;
		}

		/// <summary>LexicalAnalyzer.ReadInt64(st, false) 的 (source,pos,end) 等价实现（从已确认是数字首字符的位置开始）。</summary>
		private static Int64 readInt64Range(string source, int pos, int end)
		{
			Int64 significand;
			int expBase = 0;
			int exponent = 0;
			int fromBase = 10;
			if (pos < end && source[pos] == '0')
			{
				if (pos + 1 < end)
				{
					char c = source[pos + 1];
					if ((c == 'x') || (c == 'X'))
					{
						fromBase = 16;
						pos += 2;
					}
					else if ((c == 'b') || (c == 'B'))
					{
						fromBase = 2;
						pos += 2;
					}
				}
				//8進法は互換性の問題から採用しない。
			}
			significand = readDigitsRange(source, ref pos, end, fromBase);
			if (pos < end && ((source[pos] == 'p') || (source[pos] == 'P')))
				expBase = 2;
			else if (pos < end && ((source[pos] == 'e') || (source[pos] == 'E')))
				expBase = 10;
			if (expBase != 0)
			{
				pos++;
				unchecked { exponent = (int)readDigitsRange(source, ref pos, end, fromBase); }
			}
			if ((expBase != 0) && (exponent != 0))
			{
				double d = significand * Math.Pow(expBase, exponent);
				if ((double.IsNaN(d)) || (double.IsInfinity(d)) || (d > Int64.MaxValue) || (d < Int64.MinValue))
					throw new CodeEE("64ビット符号付整数の範囲を超えています");
				significand = (Int64)d;
			}
			return significand;
		}

		/// <summary>LexicalAnalyzer.readDigits 的 (source,ref pos,end) 等价实现。</summary>
		private static Int64 readDigitsRange(string source, ref int pos, int end, int fromBase)
		{
			int start = pos;
			char c = (pos < end) ? source[pos] : '\0';
			if ((c == '-') || (c == '+'))
			{
				pos++;
			}
			if (fromBase == 10)
			{
				while (pos < end)
				{
					c = source[pos];
					if (char.IsDigit(c))
					{
						pos++;
						continue;
					}
					break;
				}
			}
			else if (fromBase == 16)
			{
				while (pos < end)
				{
					c = source[pos];
					if (char.IsDigit(c) || hexadecimalDigits.Contains(c))
					{
						pos++;
						continue;
					}
					break;
				}
			}
			else if (fromBase == 2)
			{
				while (pos < end)
				{
					c = source[pos];
					if (char.IsDigit(c))
					{
						if ((c != '0') && (c != '1'))
							throw new CodeEE("二進法表記の中で使用できない文字が使われています");
						pos++;
						continue;
					}
					break;
				}
			}
			string strInt = source.Substring(start, pos - start);
			try
			{
				return Convert.ToInt64(strInt, fromBase);
			}
			catch (FormatException)
			{
				throw new CodeEE("\"" + strInt + "\"は整数値に変換できません");
			}
			catch (OverflowException)
			{
				throw new CodeEE("\"" + strInt + "\"は64ビット符号付き整数の範囲を超えています");
			}
			catch (ArgumentOutOfRangeException)
			{
				if (string.IsNullOrEmpty(strInt))
					throw new CodeEE("数値として認識できる文字が必要です");
				throw new CodeEE("文字列\"" + strInt + "\"は数値として認識できません");
			}
		}

		private enum CsvEffectKind : byte
		{
			Print,
			PrintError,
			Warn,
			HandSound,
		}

		private readonly struct CsvSideEffect
		{
			public readonly CsvEffectKind Kind;
			public readonly string Message;
			public readonly ScriptPosition Position;
			public readonly int Level;
			public CsvSideEffect(CsvEffectKind kind, string message, ScriptPosition position, int level)
			{
				Kind = kind;
				Message = message;
				Position = position;
				Level = level;
			}
		}

		/// <summary>
		/// N5: CSV 并行解析的副作用捕获器。警告/控制台输出/提示音等副作用不再于工作线程直接执行，
		/// 而是按每文件顺序记录，全部文件解析完成后由主线程按原文件顺序统一重放。
		/// ParserMediator.warningList 与 EmueraConsole 输出均非线程安全，因此必须串行重放。
		/// </summary>
		private sealed class CsvLoadContext
		{
			private readonly List<CsvSideEffect> effects = new List<CsvSideEffect>();

			public void Print(string message)
			{
				effects.Add(new CsvSideEffect(CsvEffectKind.Print, message, null, 0));
			}

			public void PrintError(string message)
			{
				effects.Add(new CsvSideEffect(CsvEffectKind.PrintError, message, null, 0));
			}

			public void Warn(string message, ScriptPosition position, int level)
			{
				effects.Add(new CsvSideEffect(CsvEffectKind.Warn, message, position, level));
			}

			public void PlayHandSound()
			{
				effects.Add(new CsvSideEffect(CsvEffectKind.HandSound, null, null, 0));
			}

			public void Replay(EmueraConsole output)
			{
				for (int i = 0; i < effects.Count; i++)
				{
					CsvSideEffect effect = effects[i];
					switch (effect.Kind)
					{
						case CsvEffectKind.Print:
							output.PrintSystemLine(effect.Message);
							break;
						case CsvEffectKind.PrintError:
							output.PrintError(effect.Message);
							break;
						case CsvEffectKind.Warn:
							// 与原串行流程走完全相同的过滤与入队路径；LoadData 期间
							// Config.DisplayWarningLevel / console.RunERBFromMemory 等状态不变，
							// 因此延迟重放与串行即时调用结果一致。
							ParserMediator.Warn(effect.Message, effect.Position, effect.Level);
							break;
						case CsvEffectKind.HandSound:
							uEmuera.Media.SystemSounds.Hand.Play();
							break;
					}
				}
				effects.Clear();
			}
		}
	}
}
