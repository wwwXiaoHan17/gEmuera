using System;
using System.Collections.Generic;
//using System.Drawing;
using System.Text;
using uEmuera.Drawing;

namespace MinorShift.Emuera.GameView
{
	/// <summary>
	/// 描画の最小単位
	/// </summary>
	abstract class AConsoleDisplayPart
	{
		public bool Error { get; protected set; }

		public string Str { get; protected set; }
		public string AltText { get; protected set; }
		public int PointX { get; set; }
		public float XsubPixel { get; set; }
		public float WidthF { get; set; }
		public int Width { get; set; }
		public virtual int Top { get { return 0; } }
		public virtual int Bottom { get { return Config.FontSize; } }
		public abstract bool CanDivide { get; }
		
		public abstract void DrawTo(Graphics graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode);
		public abstract void GDIDrawTo(int pointY, bool isSelecting, bool isBackLog);

		public abstract void SetWidth(StringMeasure sm, float subPixel);

		/// <summary>
		/// 仅供输出日志还原显示源信息。默认保持既有纯文本语义，图片和形状可覆盖为原始 HTML 标签。
		/// </summary>
		public virtual string ToLogString()
		{
			return ToString();
		}

		public override string ToString()
		{
			if (Str == null)
				return "";
			return Str;
		}
	}

	/// <summary>
	/// 色つき
	/// </summary>
	abstract partial class AConsoleColoredPart : AConsoleDisplayPart
	{
		protected Color Color { get; set; }
		protected Color ButtonColor { get; set; }
		protected bool colorChanged;
	}
}
