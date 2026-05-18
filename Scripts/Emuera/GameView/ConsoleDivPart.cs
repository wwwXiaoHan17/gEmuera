using System;
using System.Text;
using uEmuera.Drawing;

namespace MinorShift.Emuera.GameView
{
	internal static class BoxDirection
	{
		public const int Top = 0;
		public const int Right = 1;
		public const int Bottom = 2;
		public const int Left = 3;
	}

	internal sealed class StyledBoxModel
	{
		public int[] Margin;
		public int[] Padding;
		public int[] Border;
		public int[] Radius;
		public int[] BorderColor;
	}

	internal sealed class ConsoleDivPart : AConsoleDisplayPart
	{
		public ConsoleDivPart(MixedNum xPos, MixedNum yPos, MixedNum width, MixedNum height, int depth, int color, StyledBoxModel box, bool isRelative, DisplayMode displayMode, ConsoleDisplayLine[] children)
		{
			X = ToPixel(xPos);
			Y = ToPixel(yPos);
			DivWidth = Math.Abs(ToPixel(width));
			DivHeight = Math.Abs(ToPixel(height));
			Depth = depth;
			BackgroundColor = color >= 0 ? Color.FromArgb(color >> 16, (color >> 8) & 0xFF, color & 0xFF) : (Color?)null;
			StyledBox = box;
			IsRelative = isRelative;
			Display = displayMode;
			Children = children ?? new ConsoleDisplayLine[0];
			Str = "";
			AltText = BuildAltText(xPos, yPos, width, height, depth, color, box, displayMode);
			Width = 0;
		}

		public int X { get; private set; }
		public int Y { get; private set; }
		public int DivWidth { get; private set; }
		public int DivHeight { get; private set; }
		public int Depth { get; private set; }
		public Color? BackgroundColor { get; private set; }
		public StyledBoxModel StyledBox { get; private set; }
		public bool IsRelative { get; private set; }
		public DisplayMode Display { get; private set; }
		public ConsoleDisplayLine[] Children { get; private set; }

		public override int Top { get { return IsRelative ? Y : 0; } }
		public override int Bottom { get { return IsRelative ? Y + DivHeight : 0; } }
		public override bool CanDivide { get { return false; } }

		public override void SetWidth(StringMeasure sm, float subPixel)
		{
			Width = 0;
			XsubPixel = 0;
		}

		public override void DrawTo(Graphics graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode)
		{
		}

		public override void GDIDrawTo(int pointY, bool isSelecting, bool isBackLog)
		{
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(AltText ?? "<div>");
			foreach (ConsoleDisplayLine line in Children)
			{
				if (line == null)
					continue;
				sb.Append(line.ToString());
				sb.Append("\r\n");
			}
			sb.Append("</div>");
			return sb.ToString();
		}

		public static int ToPixel(MixedNum value)
		{
			if (value == null)
				return 0;
			return value.isPx ? value.num : (value.num * Config.FontSize / 100);
		}

		static string BuildAltText(MixedNum xPos, MixedNum yPos, MixedNum width, MixedNum height, int depth, int color, StyledBoxModel box, DisplayMode display)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("<div");
			AddMixedNumArg(sb, "xpos", xPos);
			AddMixedNumArg(sb, "ypos", yPos);
			AddMixedNumArg(sb, "width", width);
			AddMixedNumArg(sb, "height", height);
			if (depth != 0)
				sb.Append(" depth='").Append(depth).Append("'");
			if (color >= 0)
				sb.Append(" color='#").Append(color.ToString("X6")).Append("'");
			if (display != DisplayMode.Relative)
				sb.Append(" display='").Append(DisplayModeToHtml(display)).Append("'");
			if (box != null)
			{
				AddBoxArg(sb, "margin", box.Margin);
				AddBoxArg(sb, "padding", box.Padding);
				AddBoxArg(sb, "border", box.Border);
				AddBoxArg(sb, "radius", box.Radius);
				AddColorBoxArg(sb, "bcolor", box.BorderColor);
			}
			sb.Append(">");
			return sb.ToString();
		}

		static void AddMixedNumArg(StringBuilder sb, string name, MixedNum value)
		{
			if (value == null || value.num == 0)
				return;
			sb.Append(" ").Append(name).Append("='").Append(value.num);
			if (value.isPx)
				sb.Append("px");
			sb.Append("'");
		}

		static void AddBoxArg(StringBuilder sb, string name, int[] values)
		{
			if (values == null)
				return;
			sb.Append(" ").Append(name).Append("='");
			for (int i = 0; i < values.Length; i++)
			{
				if (i > 0)
					sb.Append(",");
				sb.Append(values[i]).Append("px");
			}
			sb.Append("'");
		}

		static void AddColorBoxArg(StringBuilder sb, string name, int[] values)
		{
			if (values == null)
				return;
			sb.Append(" ").Append(name).Append("='");
			for (int i = 0; i < values.Length; i++)
			{
				if (i > 0)
					sb.Append(",");
				sb.Append("#").Append(values[i].ToString("X6"));
			}
			sb.Append("'");
		}

		static string DisplayModeToHtml(DisplayMode display)
		{
			switch (display)
			{
				case DisplayMode.Absolute:
					return "absolute";
				case DisplayMode.AbsoluteLeftTop:
					return "absolute-lefttop";
				case DisplayMode.AbsoluteLeftBottom:
					return "absolute-leftbottom";
				default:
					return "relative";
			}
		}
	}
}
