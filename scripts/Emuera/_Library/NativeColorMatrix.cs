//using System;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	/// <summary>
	/// gemuera_native RGBA8 ColorMatrix 像素变换 P/Invoke（native/gemuera-native/src/colormatrix.rs）。
	/// 仅加速，不改语义：与 GraphicsImage.ApplyColorMatrixBytes 托管实现逐字节一致
	///（banker's 舍入 + 左结合求和 + cvttsd2si 病理值语义），门禁 tools/colormatrix-check
	///（反射真实托管实现对拍 + 随机 sweep + 病理 golden）。
	/// 库缺失/ABI 过低/缓冲异常时返回 false，调用方回退托管路径。
	/// </summary>
	internal static class NativeColorMatrix
	{
		private const string LibraryName = "gemuera_native";
		// gem_cm_* 于 ABI v4 引入
		private const uint MinAbiVersion = 4;

		private static int probeState;

		[DllImport(LibraryName, EntryPoint = "gem_native_abi_version")]
		private static extern uint AbiVersion();

		[DllImport(LibraryName, EntryPoint = "gem_cm_apply_rgba8")]
		private static extern int ApplyRgba8(byte[] data, nuint len, float[] matrix);

		/// <summary>原生 ColorMatrix 可用（进程内探测一次）。</summary>
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
		/// 原地变换 RGBA8 缓冲（data.Length 必须 ≥4 且为 4 的倍数）。
		/// matrix20 行优先 = cm[0..5][0..4]（cm[输入行][输出通道]，与托管索引同序）。
		/// 成功返回 true（data 已被变换）；失败返回 false（data 未被触碰，调用方走托管）。
		/// cm 行缺失/过短时返回 false（托管回退路径保持原异常语义）。
		/// </summary>
		public static bool TryApplyColorMatrix(byte[] data, float[][] cm)
		{
			if (!IsAvailable || data == null || cm == null)
				return false;
			int len = data.Length;
			if (len < 4 || len % 4 != 0)
				return false;
			var m = new float[20];
			for (int row = 0; row < 5; row++)
			{
				var src = cm[row];
				if (src == null || src.Length < 4)
					return false;
				m[row * 4] = src[0];
				m[row * 4 + 1] = src[1];
				m[row * 4 + 2] = src[2];
				m[row * 4 + 3] = src[3];
			}
			return ApplyRgba8(data, (nuint)len, m) == 0;
		}
	}
}
