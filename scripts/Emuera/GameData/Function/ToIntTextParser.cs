using System;
using MinorShift.Emuera.Sub;

namespace MinorShift.Emuera.GameData.Function
{
	/// <summary>
	/// TOINT 的字符串→整数解析壳（参考 Creator.Method.cs:6119-6168 ToIntMethod）。
	/// 只依赖 StringStream（无 Godot 依赖），宿主注入 LexicalAnalyzer.ReadInt64，
	/// 使"解析失败返 0"这条参考语义可以在 xUnit 里直测。
	/// </summary>
	internal static class ToIntTextParser
	{
		/// <param name="str">实参字符串（可为 null）。</param>
		/// <param name="displayLength">LangManager.GetStrlenLang(str)：含全角字符时大于 str.Length。</param>
		/// <param name="readInt64">宿主注入的整数读取器（参考实现为 LexicalAnalyzer.ReadInt64(st, true)）。</param>
		internal static long Parse(string str, int displayLength, Func<StringStream, long> readInt64)
		{
			if (str == null || str.Length == 0)
				return 0;
			//全角文字が入ってるなら無条件で0を返す
			if (str.Length < displayLength)
				return 0;
			StringStream st = new StringStream(str);
			if (!char.IsDigit(st.Current) && st.Current != '+' && st.Current != '-')
				return 0;
			else if ((st.Current == '+' || st.Current == '-') && !char.IsDigit(st.Next))
				return 0;
			long ret;
			try
			{
				ret = readInt64(st);
			}
			catch (Exception)
			{
				// 参考实现：解析失败（溢出/非法串）按 0 处理，不把 CodeEE 抛给 ERB。
				return 0;
			}
			if (!st.EOS)
			{
				if (st.Current == '.')
				{
					st.ShiftNext();
					while (!st.EOS)
					{
						if (!char.IsDigit(st.Current))
							return 0;
						st.ShiftNext();
					}
				}
				else
					return 0;
			}
			return ret;
		}
	}
}
