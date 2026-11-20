//using System;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	/// <summary>
	/// gemuera_native 存档编码 P/Invoke（native/gemuera-native/src/save.rs）。
	/// 仅加速，不改格式：字节序列与 EraBinaryDataWriter 托管实现逐字节一致，
	/// 门禁 tools/save-codec-check（参考实现对拍 + 反射真实 Writer 对拍）。
	/// 库缺失/ABI 过低时 IsAvailable=false，调用方回退托管路径。
	/// </summary>
	internal static class NativeSaveCodec
	{
		private const string LibraryName = "gemuera_native";
		// gem_save_* 于 ABI v3 引入
		private const uint MinAbiVersion = 3;
		// 编码输出缓冲上界余量：长度前缀 4 + EoD 1 + 游程标记冗余（每元素本身最坏 9 字节另行计入）
		internal const int EncodeBufferOverhead = 16;

		private static int probeState;

		[DllImport(LibraryName, EntryPoint = "gem_native_abi_version")]
		private static extern uint AbiVersion();

		[DllImport(LibraryName, EntryPoint = "gem_save_encode_int64_array")]
		private static extern int EncodeInt64Array(long[] array, nuint len, byte[] outBuf, nuint cap);

		/// <summary>原生存档编码可用（进程内探测一次）。</summary>
		public static bool IsAvailable
		{
			get
			{
				if (probeState != 0)
					return probeState == 0;
				try
				{
					probeState = AbiVersion() >= MinAbiVersion ? 0 : 1;
				}
				catch (System.Exception)
				{
					probeState = 1;
				}
				return probeState == 0;
			}
		}

		/// <summary>
		/// 编码 Int64[] 完整段（长度前缀 + Zero 游程 + m_WriteInt + EoD）。
		/// outBuf 需 ≥ array.Length*10+16；成功返回 true 并给出有效长度。
		/// </summary>
		public static bool TryEncodeInt64Array(long[] array, byte[] outBuf, out int written)
		{
			written = 0;
			if (!IsAvailable || array == null)
				return false;
			int required = checked(array.Length * 10 + EncodeBufferOverhead);
			if (outBuf.Length < required)
				return false;
			int n = EncodeInt64Array(array, (nuint)array.Length, outBuf, (nuint)outBuf.Length);
			if (n < 0)
				return false;
			written = n;
			return true;
		}
	}
}
