using System;

namespace MinorShift.Emuera.GameData.Function
{
	/// <summary>
	/// SPRITECREATE 几何实参的布局解码（参考 Skiav17.2 Creator.Method.cs:7951-8056
	/// SpriteCreateMethod）。只做纯数值判定，不依赖 uEmuera.Drawing/Godot，
	/// 宿主与 xUnit 契约测试直链同一份源码。
	///
	/// 参考形态：
	///   2 参：整图，无裁剪/偏移/目标尺寸；
	///   6 参：(imgName, gID, srcX, srcY, srcWidth, srcHeight)，目标尺寸 = 裁剪尺寸；
	///   8 参：再追加 (posX, posY)；
	///   10 参：再追加 (destWidth, destHeight)，负值取绝对值（= 放大绘制语义）。
	/// </summary>
	internal readonly struct SpriteCreateLayout
	{
		internal readonly bool HasSourceRectangle;
		internal readonly int SourceX;
		internal readonly int SourceY;
		internal readonly int SourceWidth;
		internal readonly int SourceHeight;
		internal readonly int PositionX;
		internal readonly int PositionY;
		internal readonly int DestWidth;
		internal readonly int DestHeight;

		internal SpriteCreateLayout(
			bool hasSourceRectangle,
			int sourceX, int sourceY, int sourceWidth, int sourceHeight,
			int positionX, int positionY, int destWidth, int destHeight)
		{
			HasSourceRectangle = hasSourceRectangle;
			SourceX = sourceX;
			SourceY = sourceY;
			SourceWidth = sourceWidth;
			SourceHeight = sourceHeight;
			PositionX = positionX;
			PositionY = positionY;
			DestWidth = destWidth;
			DestHeight = destHeight;
		}
	}

	internal static class SpriteCreateArgumentLayout
	{
		/// <summary>
		/// 把 SPRITECREATE 实参个数与已解析的源矩形解码成最终布局。
		/// </summary>
		/// <param name="argumentCount">实参总数（2/6/8/10）。</param>
		/// <param name="sourceX">整图形态时为 0；裁剪形态时为 srcX。</param>
		/// <param name="readArgument">按 0 基实参索引读取整数值（仅在需要时调用，避免多余求值）。</param>
		internal static bool TryResolve(
			int argumentCount,
			int sourceX, int sourceY, int sourceWidth, int sourceHeight,
			Func<int, long> readArgument,
			out SpriteCreateLayout layout)
		{
			switch (argumentCount)
			{
				case 2:
					layout = new SpriteCreateLayout(false, sourceX, sourceY, sourceWidth, sourceHeight,
						0, 0, sourceWidth, sourceHeight);
					return true;
				case 6:
					layout = new SpriteCreateLayout(true, sourceX, sourceY, sourceWidth, sourceHeight,
						0, 0, sourceWidth, sourceHeight);
					return true;
				case 8:
				case 10:
				{
					// 参考：8 参追加 (PosX, PosY)（索引 6/7），10 参再追加 (DestW, DestH)（索引 8/9）。
					int positionX = unchecked((int)readArgument(6));
					int positionY = unchecked((int)readArgument(7));
					int destWidth = sourceWidth;
					int destHeight = sourceHeight;
					if (argumentCount == 10)
					{
						destWidth = unchecked((int)readArgument(8));
						destHeight = unchecked((int)readArgument(9));
						// 参考 Creator.Method.cs:8043-8044：负值取绝对值，保持放大绘制语义。
						if (destWidth < 0)
							destWidth = -destWidth;
						if (destHeight < 0)
							destHeight = -destHeight;
					}
					layout = new SpriteCreateLayout(true, sourceX, sourceY, sourceWidth, sourceHeight,
						positionX, positionY, destWidth, destHeight);
					return true;
				}
				default:
					layout = default;
					return false;
			}
		}
	}
}
