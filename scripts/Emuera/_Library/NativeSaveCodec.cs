//using System;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	/// <summary>
	/// gemuera_native 存档编码/解码 P/Invoke（native/gemuera-native/src/save.rs）。
	/// 仅加速，不改格式：字节序列与 EraBinaryDataWriter/EraBinaryDataReader 托管实现
	/// 逐字节一致，门禁 tools/save-codec-check（参考实现对拍 + 反射真实 Writer/Reader 对拍）。
	/// 库缺失/ABI 过低时 IsAvailable/DecodeAvailable=false，调用方回退托管路径。
	/// </summary>
	internal static class NativeSaveCodec
	{
		private const string LibraryName = "gemuera_native";
		// gem_save_encode_* 于 ABI v3 引入；gem_save_decode_* 于 ABI v5 引入
		private const uint MinAbiVersion = 3;
		private const uint DecodeMinAbiVersion = 5;
		// 编码输出缓冲上界余量：长度前缀 4 + EoD 1 + 游程标记冗余（每元素本身最坏 9 字节另行计入）
		internal const int EncodeBufferOverhead = 16;

		private static int probeState;
		private static int decodeProbeState;

		[DllImport(LibraryName, EntryPoint = "gem_native_abi_version")]
		private static extern uint AbiVersion();

		[DllImport(LibraryName, EntryPoint = "gem_save_encode_int64_array")]
		private static extern int EncodeInt64Array(long[] array, nuint len, byte[] outBuf, nuint cap);

		[DllImport(LibraryName, EntryPoint = "gem_save_decode_int64_segment")]
		private static extern int DecodeInt64Segment(
			byte[] seg, nuint segLen, long[] outArr, nuint outLen, nuint length0, int writeZeros, out nuint consumed);

		private static bool Probe(uint minAbi, ref int state)
		{
			if (state != 0)
				return state == 0;
			try
			{
				state = AbiVersion() >= minAbi ? 0 : 1;
			}
			catch (System.Exception)
			{
				state = 1;
			}
			return state == 0;
		}

		/// <summary>原生存档**编码**可用（进程内探测一次，≥ABI v3）。</summary>
		public static bool IsAvailable => Probe(MinAbiVersion, ref probeState);

		/// <summary>原生存档**读侧解码**可用（进程内探测一次，≥ABI v5）。</summary>
		public static bool DecodeAvailable => Probe(DecodeMinAbiVersion, ref decodeProbeState);

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

		/// <summary>
		/// 读侧解码一维 Int64 段（语义 = EraBinaryDataReader.ReadIntArray(Int64[])）。
		/// seg = Int32 长度前缀之后的负载 + EoD（允许带尾随字节——consumed 给出实际消耗，
		/// 段被 EoD 终止）；outArr = 工作数组（长度 ≥ 存档长度）；length0 = min(旧长, 存档长)；
		/// writeZeros = needInit && !isNewArray。成功返回 true 并给出消耗字节数（含 EoD）。
		/// false 时 errorCode：-2 格式错或段被截断（EOF）；-3 越界（仅损坏存档）。
		/// 调用方对 -2 需区分"窗口不够大需扩窗重试"与"真截断"（比较窗口大小与流剩余）。
		/// </summary>
		public static bool TryDecodeInt64Segment(
			byte[] seg, int segLen, long[] outArr, int length0, bool writeZeros,
			out int consumed, out int errorCode)
		{
			consumed = 0;
			errorCode = 0;
			if (!DecodeAvailable || seg == null || outArr == null)
			{
				errorCode = -1;
				return false;
			}
			if (segLen < 0 || (nuint)segLen > (nuint)seg.Length)
			{
				errorCode = -1;
				return false;
			}
			if (length0 < 0 || (nuint)length0 > (nuint)outArr.Length)
			{
				errorCode = -1;
				return false;
			}
			int rc = DecodeInt64Segment(
				seg, (nuint)segLen, outArr, (nuint)outArr.Length, (nuint)length0,
				writeZeros ? 1 : 0, out nuint n);
			if (rc != 0)
			{
				errorCode = rc;
				return false;
			}
			consumed = checked((int)n);
			return true;
		}

		/// <summary>原生解码失败码（TryDecodeInt64Segment 返回 false 时的 rc 语义）。</summary>
		internal const int DecodeErrorFormat = -2;   // EOF/未知 marker → 托管 FileEE
		internal const int DecodeErrorOverflow = -3; // 越界写 → 托管 IndexOutOfRange（仅损坏存档）
	}
}
