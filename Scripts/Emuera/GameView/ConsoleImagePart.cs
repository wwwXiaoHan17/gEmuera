using Godot;
using MinorShift._Library;
using MinorShift.Emuera.Content;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Sub;
using System;
using System.Collections.Generic;
//using System.Drawing;
//using System.Drawing.Imaging;
using System.Text;
using uEmuera.Drawing;
using uEmuera.Forms;

namespace MinorShift.Emuera.GameView
{
	class ConsoleImagePart : AConsoleDisplayPart
	{

		public ConsoleImagePart(string resName, string resNameb, MixedNum raw_height, MixedNum raw_width, MixedNum raw_ypos)
			: this(resName, resNameb, raw_height, raw_width, raw_ypos, 0, DisplayMode.Relative, null)
		{
		}

		public ConsoleImagePart(string resName, string resNameb, MixedNum raw_height, MixedNum raw_width, MixedNum raw_ypos, MixedNum raw_xpos, DisplayMode display)
			: this(resName, resNameb, raw_height, raw_width, raw_ypos, raw_xpos, display, null)
		{
		}

		public ConsoleImagePart(string resName, string resNameb, MixedNum raw_height, MixedNum raw_width, MixedNum raw_ypos, MixedNum raw_xpos, DisplayMode display, string colorMatrixVariableName)
		{
			top = 0;
			bottom = Config.FontSize;
			Str = "";
			ResourceName = resName ?? "";
			ButtonResourceName = resNameb;
			Display = display;
			PositionX = raw_xpos.isPx ? raw_xpos.num : (raw_xpos.num * Config.FontSize / 100);
			ColorMatrixVariableName = colorMatrixVariableName;
			ColorMatrix = ResolveColorMatrix(colorMatrixVariableName);

			// Compute desired dimensions from HTML attributes regardless of whether sprite exists.
			// This allows external renderers to size the image correctly even when loading dynamically.
			int height = 0;
			if (raw_height.num == 0)
				height = Config.FontSize;
			else
				height = raw_height.isPx ? raw_height.num : (Config.FontSize * raw_height.num / 100);

			int width = 0;
			if (raw_width.num == 0)
				width = 0; // will use natural size when sprite is loaded dynamically
			else
				width = raw_width.isPx ? raw_width.num : (Config.FontSize * raw_width.num / 100);

			top = raw_ypos.isPx ? raw_ypos.num : (raw_ypos.num * Config.FontSize / 100);
			int rectX = 0;
			int rectY = top;
			int rectW = width;
			int rectH = height;
			FlipX = false;
			FlipY = false;
			if (rectW < 0)
			{
				rectX = -rectW;
				width = -rectW;
				rectW = width;
				FlipX = true;
			}
			if (rectH < 0)
			{
				rectY = rectY - rectH;
				height = -rectH;
				rectH = height;
				FlipY = true;
			}
			destRect = new Rectangle(rectX, rectY, rectW, rectH);
			bottom = top + height;
			Width = width;
			if (raw_width.num == 0)
				XsubPixel = 0;
			else if (raw_width.isPx)
				XsubPixel = 0;
			else
				XsubPixel = ((float)Config.FontSize * raw_width.num / 100f) - Width;

            cImage = AppContents.GetSprite(ResourceName);
#if !UNITY_EDITOR
            if(cImage == null)
            {
#endif
                StringBuilder sb = new StringBuilder();
                sb.Append("<img src='");
                sb.Append(ResourceName);
                if(ButtonResourceName != null)
                {
                    sb.Append("' srcb='");
                    sb.Append(ButtonResourceName);
                }
			if(raw_height.num != 0)
			{
				sb.Append("' height='");
				sb.Append(raw_height.num.ToString());
				if (raw_height.isPx)
					sb.Append("px");
			}
			if(raw_width.num != 0)
			{
				sb.Append("' width='");
				sb.Append(raw_width.num.ToString());
				if (raw_width.isPx)
					sb.Append("px");
			}
			if(raw_ypos.num != 0)
			{
				sb.Append("' ypos='");
				sb.Append(raw_ypos.num.ToString());
				if (raw_ypos.isPx)
					sb.Append("px");
			}
			if(raw_xpos.num != 0)
			{
				sb.Append("' xpos='");
				sb.Append(raw_xpos.num.ToString());
				if (raw_xpos.isPx)
					sb.Append("px");
			}
			if(display != DisplayMode.Relative)
			{
				sb.Append("' display='");
				sb.Append(DisplayModeToHtml(display));
			}
			if(!string.IsNullOrEmpty(colorMatrixVariableName))
			{
				sb.Append("' cm='");
				sb.Append(colorMatrixVariableName);
			}
                sb.Append("'>");
                AltText = sb.ToString();
				if (raw_width.num == 0 && TryResolveDynamicImageWidth(ResourceName, height, out int resolvedWidth, out float resolvedSubPixel))
				{
					Width = resolvedWidth;
					XsubPixel = resolvedSubPixel;
					destRect = new Rectangle(0, top, Width, height);
					Str = "";
				}
#if !UNITY_EDITOR
                else
                {
                    Str = AltText;
                }
                return;
            }
#else
            if(cImage == null)
            {
				if (raw_width.num == 0 && TryResolveDynamicImageWidth(ResourceName, height, out int resolvedWidth, out float resolvedSubPixel))
				{
					Width = resolvedWidth;
					XsubPixel = resolvedSubPixel;
					destRect = new Rectangle(0, top, Width, height);
					Str = "";
				}
				else
				{
					Str = AltText;
				}
                return;
            }
#endif
			// If sprite was found, fix up width for aspect ratio when raw_width was 0
			if (raw_width.num == 0 && cImage != null && cImage.DestBaseSize.Height > 0)
			{
				Width = cImage.DestBaseSize.Width * height / cImage.DestBaseSize.Height;
				XsubPixel = ((float)cImage.DestBaseSize.Width * height) / cImage.DestBaseSize.Height - Width;
				destRect = new Rectangle(0, top, Width, height);
			}
			if (ButtonResourceName != null)
			{
                if(ButtonResourceName == ResourceName)
                    cImageB = cImage;
                else
                {
                    cImageB = AppContents.GetSprite(ButtonResourceName);
                }
			}
		}

        public ASprite Image { get { return cImage; } }
        public ASprite ImageBackground { get { return cImageB; } }
        public Rectangle rect { get { return cImage.Rectangle; } }
        public Rectangle dest_rect { get { return destRect; } }
		public DisplayMode Display { get; private set; }
		public int PositionX { get; private set; }
		public int PositionY { get { return top; } }
		public string ColorMatrixVariableName { get; private set; }
		public float[][] ColorMatrix { get; private set; }
		public bool FlipX { get; private set; }
		public bool FlipY { get; private set; }

		private readonly ASprite cImage;
		private readonly ASprite cImageB;
		private readonly int top;
		private readonly int bottom;
		private Rectangle destRect;
//#pragma warning disable CS0649 // フィールド 'ConsoleImagePart.ia' は割り当てられません。常に既定値 null を使用します。
//		private readonly ImageAttributes ia;
//#pragma warning restore CS0649 // フィールド 'ConsoleImagePart.ia' は割り当てられません。常に既定値 null を使用します。
		public readonly string ResourceName;
		public readonly string ButtonResourceName;
		public override int Top { get { return top; } }
		public override int Bottom { get { return bottom; } }

		public override bool CanDivide { get { return false; } }
		public override void SetWidth(StringMeasure sm, float subPixel)
		{
			if (this.Error)
			{
				Width = 0;
				return;
			}
			if (cImage != null)
				return;
			if (destRect.Width > 0)
				return;
			Width = sm.GetDisplayLength(Str, Config.Font);
			XsubPixel = subPixel;
		}

		public override string ToString()
		{
			if (AltText == null)
				return "";
			return AltText;
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

		static bool TryResolveDynamicImageWidth(string resourceName, int targetHeight, out int width, out float subPixel)
		{
			width = 0;
			subPixel = 0;
			if (string.IsNullOrEmpty(resourceName) || targetHeight <= 0)
				return false;

			ASprite sprite = AppContents.GetSprite(resourceName);
			if (sprite != null && sprite.DestBaseSize.Height > 0)
			{
				float rawWidth = (float)sprite.DestBaseSize.Width * targetHeight / sprite.DestBaseSize.Height;
				width = Math.Max(1, (int)rawWidth);
				subPixel = rawWidth - width;
				return true;
			}

			foreach (string path in BuildImagePathCandidates(resourceName))
			{
				if (string.IsNullOrEmpty(path) || !uEmuera.Utils.FileExists(path))
					continue;
				var ti = global::SpriteManager.GetTextureInfo(resourceName, path);
				if (ti == null || ti.height <= 0)
					continue;
				float rawWidth = (float)ti.width * targetHeight / ti.height;
				width = Math.Max(1, (int)rawWidth);
				subPixel = rawWidth - width;
				return true;
			}
			return false;
		}

		static IEnumerable<string> BuildImagePathCandidates(string resourceName)
		{
			string resolved = uEmuera.Utils.ResolveExistingFilePath(resourceName);
			if (!string.IsNullOrEmpty(resolved))
				yield return resolved;

			yield return resourceName;
			if (!string.IsNullOrEmpty(Program.ContentDir))
				yield return System.IO.Path.Combine(Program.ContentDir, resourceName);
			if (!string.IsNullOrEmpty(Program.ExeDir))
			{
				yield return System.IO.Path.Combine(Program.ExeDir, resourceName);
				yield return System.IO.Path.Combine(Program.ExeDir, "resources", resourceName);
			}

			bool hasExt = resourceName.IndexOf('.') >= 0;
			if (!hasExt)
			{
				foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" })
				{
					string nameWithExt = resourceName + ext;
					if (!string.IsNullOrEmpty(Program.ContentDir))
						yield return System.IO.Path.Combine(Program.ContentDir, nameWithExt);
					if (!string.IsNullOrEmpty(Program.ExeDir))
						yield return System.IO.Path.Combine(Program.ExeDir, "resources", nameWithExt);
					string found = uEmuera.Utils.FindFileRecursive(Program.ContentDir, nameWithExt);
					if (!string.IsNullOrEmpty(found))
						yield return found;
				}
			}
		}

		static float[][] ResolveColorMatrix(string variableName)
		{
			if (string.IsNullOrWhiteSpace(variableName) || GlobalStatic.EMediator == null)
				return null;
			try
			{
				WordCollection wc = LexicalAnalyzer.Analyse(new StringStream(variableName), LexEndWith.EoL, LexAnalyzeFlag.None);
				IOperandTerm term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
				if (!(term is VariableTerm variableTerm))
					return null;
				FixedVariableTerm fixedTerm = variableTerm.GetFixedVariableTerm(GlobalStatic.EMediator);
				return ReadColorMatrix(fixedTerm);
			}
			catch
			{
				return null;
			}
		}

		static float[][] ReadColorMatrix(FixedVariableTerm term)
		{
			float[][] matrix = new float[5][];
			for (int i = 0; i < matrix.Length; i++)
				matrix[i] = new float[5];

			if (term.Identifier.IsArray2D)
			{
				long row = term.Identifier.IsCharacterData ? term.Index2 : term.Index1;
				long col = term.Identifier.IsCharacterData ? term.Index3 : term.Index2;
				if (row < 0 || col < 0)
					return null;
				if (term.Identifier.IsFloat)
				{
					double[,] array = term.Identifier.IsCharacterData
						? term.Identifier.GetArrayChara((int)term.Index1) as double[,]
						: term.Identifier.GetArray() as double[,];
					if (array == null || row + 5 > array.GetLength(0) || col + 5 > array.GetLength(1))
						return null;
					for (int x = 0; x < 5; x++)
						for (int y = 0; y < 5; y++)
							matrix[x][y] = (float)array[row + x, col + y];
					return matrix;
				}
				else
				{
					Int64[,] array = term.Identifier.IsCharacterData
						? term.Identifier.GetArrayChara((int)term.Index1) as Int64[,]
						: term.Identifier.GetArray() as Int64[,];
					if (array == null || row + 5 > array.GetLength(0) || col + 5 > array.GetLength(1))
						return null;
					for (int x = 0; x < 5; x++)
						for (int y = 0; y < 5; y++)
							matrix[x][y] = ((float)array[row + x, col + y]) / 256f;
					return matrix;
				}
			}

			if (term.Identifier.IsArray3D && !term.Identifier.IsCharacterData)
			{
				long layer = term.Index1;
				long row = term.Index2;
				long col = term.Index3;
				if (layer < 0 || row < 0 || col < 0)
					return null;
				if (term.Identifier.IsFloat)
				{
					double[,,] array = term.Identifier.GetArray() as double[,,];
					if (array == null || layer >= array.GetLength(0) || row + 5 > array.GetLength(1) || col + 5 > array.GetLength(2))
						return null;
					for (int x = 0; x < 5; x++)
						for (int y = 0; y < 5; y++)
							matrix[x][y] = (float)array[layer, row + x, col + y];
					return matrix;
				}
				else
				{
					Int64[,,] array = term.Identifier.GetArray() as Int64[,,];
					if (array == null || layer >= array.GetLength(0) || row + 5 > array.GetLength(1) || col + 5 > array.GetLength(2))
						return null;
					for (int x = 0; x < 5; x++)
						for (int y = 0; y < 5; y++)
							matrix[x][y] = ((float)array[layer, row + x, col + y]) / 256f;
					return matrix;
				}
			}
			return null;
		}

		public override void DrawTo(Graphics graph, int pointY, bool isSelecting, bool isBackLog, TextDrawingMode mode)
		{
			//if (this.Error)
			//	return;
			//ASprite img = cImage;
			//if (isSelecting && cImageB != null)
			//	img = cImageB;
            //
			//if (img != null && img.IsCreated)
			//{
			//	Rectangle rect = destRect;
			//	//PointX微調整
			//	rect.X = destRect.X + PointX + Config.DrawingParam_ShapePositionShift;
			//	rect.Y = destRect.Y + pointY;
			//	img.GraphicsDraw(graph, rect);
			//}
			//else
			//{
			//	if (mode == TextDrawingMode.GRAPHICS)
			//		graph.DrawString(AltText, Config.Font, new SolidBrush(Config.ForeColor), new Point(PointX, pointY));
			//	else
			//		System.Windows.Forms.TextRenderer.DrawText(graph, AltText, Config.Font, new Point(PointX, pointY), Config.ForeColor, System.Windows.Forms.TextFormatFlags.NoPrefix);
			//}
		}

		public override void GDIDrawTo(int pointY, bool isSelecting, bool isBackLog)
		{
			//if (this.Error)
			//	return;
			//SpriteF img = cImage as SpriteF;//Graphicsから作成したImageはGDI対象外
			//if (isSelecting && cImageB != null)
			//	img = cImageB as SpriteF;
			//if (img != null && img.IsCreated)
			//{
			//	int x = PointX + destRect.X;
			//	int y = pointY + destRect.Y;
			//	if (!img.DestBasePosition.IsEmpty)
			//	{
			//		x = x + img.DestBasePosition.X * destRect.Width / img.SrcRectangle.Width;
			//		y = y + img.DestBasePosition.Y * destRect.Height / img.SrcRectangle.Height;
			//	}
			//	GDI.DrawImage(x, y, Width, destRect.Height, img.BaseImage.GDIhDC, img.SrcRectangle);
			//}
			//else
			//	GDI.TabbedTextOutFull(Config.Font, Config.ForeColor, AltText, PointX, pointY);
		}
	}
}
