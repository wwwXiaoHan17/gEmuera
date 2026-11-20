//using System;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	/// <summary>
	/// gemuera_native（Rust，native/gemuera-native）P/Invoke 探针。
	/// 仅用于启动期诊断：报告原生库是否加载、ABI 是否匹配、宽度抽查是否与托管规则一致。
	/// 不接入任何行为路径（宽度计算仍走 uEmuera.Utils 托管实现）；库缺失时降级为不可用，
	/// 任何异常都被吞掉并转成日志文案，绝不影响启动流程。
	/// 语义契约与等价门禁见 native/README.md 与 tools/native-width-check。
	/// </summary>
	internal static class NativeTextWidth
	{
		private const string LibraryName = "gemuera_native";
		private const uint ExpectedAbiVersion = 2;

		// 探测缓存：0=可用，1=不可用。库缺失只探测一次，热路径零额外开销。
		private static int probeState;

		[DllImport(LibraryName, EntryPoint = "gem_native_abi_version")]
		private static extern uint AbiVersion();

		[DllImport(LibraryName, EntryPoint = "gem_text_width_unit")]
		private static extern int WidthUnit(ushort unit);

		[DllImport(LibraryName, EntryPoint = "gem_text_width_utf16")]
		private static extern long WidthUtf16(ushort[] units, nuint len);

		[DllImport(LibraryName, EntryPoint = "gem_text_width_units_utf16")]
		private static extern long WidthUnitsUtf16(ushort[] units, nuint len, byte[] widths);

		/// <summary>原生库可用且 ABI 匹配（结果进程内缓存，首次调用探测一次）。</summary>
		public static bool IsAvailable
		{
			get
			{
				if (probeState != 0)
					return probeState == 0;
				try
				{
					probeState = AbiVersion() == ExpectedAbiVersion ? 0 : 1;
				}
				catch (System.Exception)
				{
					// DllNotFound/EntryPoint/BadImageFormat 等：无原生库环境（含未来未分发 ABI 的平台）走托管回退
					probeState = 1;
				}
				return probeState == 0;
			}
		}

		/// <summary>单个 UTF-16 码元宽度（0/1/2）。库不可用时返回 false。</summary>
		public static bool TryGetWidthUnit(char c, out int width)
		{
			if (!IsAvailable)
			{
				width = 0;
				return false;
			}
			width = WidthUnit(c);
			return true;
		}

		/// <summary>整串宽度（对应 Utils.GetByteCount 语义）。库不可用或空串时返回 false（空串由调用方原逻辑早退）。</summary>
		public static bool TryGetByteCount(string s, out int count)
		{
			count = 0;
			if (!IsAvailable || string.IsNullOrEmpty(s))
				return false;
			count = (int)WidthUtf16(Utf16Units(s), (nuint)s.Length);
			return true;
		}

		/// <summary>
		/// 填充逐单元宽度序列（widths.Length 必须 ≥ s.Length），返回是否成功。
		/// 库不可用时返回 false，调用方走托管逐字符路径。
		/// </summary>
		public static bool TryFillWidthUnits(string s, byte[] widths)
		{
			if (!IsAvailable || string.IsNullOrEmpty(s) || widths.Length < s.Length)
				return false;
			WidthUnitsUtf16(Utf16Units(s), (nuint)s.Length, widths);
			return true;
		}

		// string → UTF-16 单元数组：一次 O(n) 拷贝换来 DllImport 数组自动 pin（宿主工程未开 AllowUnsafeBlocks），
		// 仍远优于原逐字符路径（每字符 ToString 分配 + UnicodeCategory/哈希查询）。
		private static ushort[] Utf16Units(string s)
		{
			var units = new ushort[s.Length];
			for (int i = 0; i < s.Length; i++)
				units[i] = s[i];
			return units;
		}

		/// <summary>
		/// 启动期自检，返回一行日志文案（[LOAD] 前缀对齐既有启动日志）。
		/// 抽查四个代表字符：'A'=1（ASCII 半宽）、'あ'=2（全宽）、
		/// '•'=1（CP932 特例，Utils.cs:921）、U+FEFF=0（零宽）。
		/// </summary>
		public static string ProbeForLog()
		{
			try
			{
				uint abi = AbiVersion();
				if (abi != ExpectedAbiVersion)
				{
					return $"[LOAD] gemuera_native ABI mismatch: {abi} != {ExpectedAbiVersion}";
				}
				int ascii = WidthUnit('A');
				int fullWidth = WidthUnit('あ');
				int bullet = WidthUnit('•');
				int zeroWidth = WidthUnit('\uFEFF');
				if (ascii == 1 && fullWidth == 2 && bullet == 1 && zeroWidth == 0)
				{
					return $"[LOAD] gemuera_native OK (abi={abi}, A={ascii},HIRAGANA_A={fullWidth},BULLET={bullet},U+FEFF={zeroWidth})";
				}
				return $"[LOAD] gemuera_native width mismatch: A={ascii} HIRAGANA_A={fullWidth} BULLET={bullet} U+FEFF={zeroWidth}";
			}
			catch (System.Exception ex)
			{
				return "[LOAD] gemuera_native unavailable: " + ex.GetType().Name + ": " + ex.Message;
			}
		}
	}
}
