//using System;
using System.Runtime.InteropServices;

namespace MinorShift._Library
{
	/// <summary>
	/// gemuera_native 掩码混合 P/Invoke（native/gemuera-native/src/blend.rs）。
	/// 仅加速，不改语义：字节结果与 GraphicsImage.BlendWithMaskBytes 托管实现
	/// 逐字节一致（三分支 + ma=mask+1 整数域 >>8 混合 + 列行裁剪），门禁
	/// tools/blend-check（参考实现对拍 + 反射真实方法互证，毒化探针取托管真参考）。
	/// 库缺失/ABI 过低/缓冲校验失败时返回 false，调用方回退托管路径。
	/// </summary>
	internal static class NativeBlend
	{
		private const string LibraryName = "gemuera_native";
		// gem_blend_* 于 ABI v6 引入
		private const uint MinAbiVersion = 6;

		private static int probeState;

		[DllImport(LibraryName, EntryPoint = "gem_native_abi_version")]
		private static extern uint AbiVersion();

		[DllImport(LibraryName, EntryPoint = "gem_blend_with_mask")]
		private static extern int BlendWithMask(
			byte[] dst, nuint dw, nuint dh,
			byte[] src, nuint srcW,
			byte[] mask, nuint maskW,
			nuint w, nuint h, int destX, int destY, int maskAlpha);

		/// <summary>原生掩码混合可用（进程内探测一次）。</summary>
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
		/// 原地掩码混合。缓冲校验对应托管 IndexOutOfRange 场景（越界即托管异常语义）：
		/// mask/src 满足 (h-1)*行宽 + w ≤ len/4、dst ≥ dw*dh*4 才走原生；
		/// 返回 false 时 dst 未被触碰（调用方走托管，异常语义由托管保持）。
		/// modified = 是否有像素经 mask≠0 且在裁剪内写入（与托管逐像素置位一致）。
		/// </summary>
		public static bool TryBlendWithMask(
			byte[] dstData, int dw, int dh,
			byte[] srcData, int srcW,
			byte[] maskData, int maskW,
			int w, int h, int destX, int destY,
			bool maskAlphaChannel, out bool modified)
		{
			modified = false;
			if (!IsAvailable
				|| dstData == null || srcData == null || maskData == null
				|| dw < 0 || dh < 0 || w < 0 || h < 0 || srcW <= 0 || maskW <= 0)
				return false;
			if (w > maskW || (long)(h - 1) * maskW + w > (long)(maskData.Length / 4))
				return false;
			if (w > srcW || (long)(h - 1) * srcW + w > (long)(srcData.Length / 4))
				return false;
			if ((long)dw * dh * 4 > dstData.Length)
				return false;
			if (dw == 0 || dh == 0 || w == 0 || h == 0)
				return true; // 空绘制：无写入，modified=false（与托管循环空转一致）
			int rc = BlendWithMask(
				dstData, (nuint)dw, (nuint)dh,
				srcData, (nuint)srcW,
				maskData, (nuint)maskW,
				(nuint)w, (nuint)h, destX, destY, maskAlphaChannel ? 1 : 0);
			if (rc < 0)
				return false;
			modified = rc == 1;
			return true;
		}
	}
}
