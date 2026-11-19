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
		private const uint ExpectedAbiVersion = 1;

		[DllImport(LibraryName, EntryPoint = "gem_native_abi_version")]
		private static extern uint AbiVersion();

		[DllImport(LibraryName, EntryPoint = "gem_text_width_unit")]
		private static extern int WidthUnit(ushort unit);

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
