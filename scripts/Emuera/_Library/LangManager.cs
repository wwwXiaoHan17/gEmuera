//using System;
//using System.Collections.Generic;
using System.Text;

namespace MinorShift._Library
{
	//マルチ言語に対応可能な形式に変更
	internal static class LangManager
	{
		static Encoding lang;

		public static void setEncode(int code)
		{
			//lang = Encoding.GetEncoding(code);
			lang = Encoding.UTF8;
		}

		public static int GetStrlenLang(string str)
		{
			//return lang.GetByteCount(str);
			return uEmuera.Utils.GetByteCount(str);
		}
		public static int GetUFTIndex(string str, int LangIndex)
		{
			if (LangIndex <= 0)
				return 0;
			int totalByte = GetStrlenLang(str);
			if (LangIndex >= totalByte)
				return str.Length;
			int UTFcnt = 0;
			int JIScnt = 0;
			for (int i = 0; i < str.Length; i++)
			{
				//JIScnt += lang.GetByteCount(str[UTFcnt].ToString());
				// 原生库逐单元宽度快速路径（省去每字符 ToString 分配 + UnicodeCategory/哈希表查询），库缺失回退托管
				JIScnt += NativeTextWidth.TryGetWidthUnit(str[UTFcnt], out int uftWidth)
					? uftWidth
					: uEmuera.Utils.GetByteCount(str[UTFcnt].ToString());
				UTFcnt++;
				if (JIScnt >= LangIndex)
					break;
			}
			return UTFcnt;
		}

		public static string GetSubStringLang(string str, int startindex, int length)
		{
			int totalByte = GetStrlenLang(str);
			if ((startindex >= totalByte) || (length == 0))
				return "";
			if ((length < 0) || (length > totalByte))
				length = totalByte;

			StringBuilder ret = new StringBuilder();
			int UTFcnt = 0;
			int JIScnt = 0;

			if (startindex <= 0)
			{
				if (length == totalByte)
					return str;
			}
			else
			{
				for (int i = 0; i < str.Length; i++)
				{
					//JIScnt += lang.GetByteCount(str[UTFcnt].ToString());
					// 原生库逐单元宽度快速路径（同 GetUFTIndex），库缺失回退托管
					JIScnt += NativeTextWidth.TryGetWidthUnit(str[UTFcnt], out int startWidth)
						? startWidth
						: uEmuera.Utils.GetByteCount(str[UTFcnt].ToString());
					UTFcnt++;
					if (JIScnt >= startindex)
						break;
				}
				if (UTFcnt >= str.Length)
					return "";
			}

			JIScnt = 0;
			while (true)
			{
				ret.Append(str[UTFcnt]);
				//JIScnt += lang.GetByteCount(str[UTFcnt].ToString());
				// 原生库逐单元宽度快速路径（同 GetUFTIndex），库缺失回退托管
				JIScnt += NativeTextWidth.TryGetWidthUnit(str[UTFcnt], out int subWidth)
					? subWidth
					: uEmuera.Utils.GetByteCount(str[UTFcnt].ToString());
				UTFcnt++;
				if (JIScnt >= length)
					break;
				if (UTFcnt >= str.Length)
					break;
			}
			return ret.ToString();
		}
	}
}
