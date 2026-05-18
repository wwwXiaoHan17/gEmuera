using MinorShift._Library;
using System;
using System.Collections.Generic;
//using System.Drawing;
//using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using uEmuera.Drawing;
//using System.Threading.Tasks;

namespace MinorShift.Emuera.Content
{
	internal sealed class GraphicsImage : AbstractImage
	{
		public GraphicsImage(int id)
		{
			ID = id;
		}
		public readonly int ID;
		public Godot.Image godotImage;
		uEmuera.Drawing.Color brushColor = uEmuera.Drawing.Color.Transparent;
		uEmuera.Drawing.Color penColor = Config.ForeColor;
		long penWidth = 1;
		DashStyle dashStyle = DashStyle.Solid;
		DashCap dashCap = DashCap.Flat;
		string fontName = Config.FontName;
		int fontSize = Config.FontSize;
		FontStyle fontStyle = FontStyle.Regular;
		List<Point> polygonPoints = new List<Point>();
		BitmapRenderTexture renderBitmap;

		#region Bitmap書き込み・作成

		/// <summary>
		/// GCREATE(int ID, int width, int height)
		/// Graphicsの基礎となるBitmapを作成する。エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GCreate(int x, int y, bool useGDI)
		{
			this.GDispose();
			is_created = true;
			width = x;
			height = y;
			renderBitmap = new BitmapRenderTexture(x, y);
			Bitmap = renderBitmap;
			godotImage = Godot.Image.CreateEmpty(x, y, false, Godot.Image.Format.Rgba8);
			renderBitmap.image = godotImage;
			godotImage.Fill(new Godot.Color(0, 0, 0, 0));
		}

		internal void GCreateFromF(Bitmap bmp, bool useGDI)
		{
			this.GDispose();
			is_created = true;
			width = bmp.Width;
			height = bmp.Height;
			renderBitmap = new BitmapRenderTexture(width, height);
			Bitmap = renderBitmap;
			if (bmp is BitmapRenderTexture rt && rt.image != null)
			{
				godotImage = rt.image.Duplicate() as Godot.Image;
			}
			else if (bmp is BitmapTexture bt && bt.sourceImage != null)
			{
				godotImage = bt.sourceImage.Duplicate() as Godot.Image;
			}
			else if (!string.IsNullOrEmpty(bmp.path))
			{
				var ti = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
				if (ti == null && !string.IsNullOrEmpty(bmp.filename))
					ti = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
				if (ti?.image != null)
				{
					godotImage = ti.image.Duplicate() as Godot.Image;
				}
			}
			if (godotImage != null && godotImage.GetFormat() != Godot.Image.Format.Rgba8)
				godotImage.Convert(Godot.Image.Format.Rgba8);
			renderBitmap.image = godotImage;
		}

		/// <summary>
		/// GCLEAR(int ID, int cARGB)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GClear(uEmuera.Drawing.Color c)
		{
			if (godotImage == null) return;
			godotImage.Fill(new Godot.Color(c.r, c.g, c.b, c.a));
		}

		/// <summary>
		/// GFILLRECTANGLE(int ID, int x, int y, int width, int height)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GFillRectangle(Rectangle rect)
		{
			if (godotImage == null) return;
			var c = brushColor;
			if (c.a <= 0) return;
			int x1 = Math.Max(0, rect.X);
			int y1 = Math.Max(0, rect.Y);
			int x2 = Math.Min(width, rect.X + rect.Width);
			int y2 = Math.Min(height, rect.Y + rect.Height);
			var gc = new Godot.Color(c.r, c.g, c.b, c.a);
			for (int y = y1; y < y2; y++)
			{
				for (int x = x1; x < x2; x++)
				{
					godotImage.SetPixel(x, y, gc);
				}
			}
		}

		/// <summary>
		/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GDrawCImg(ASprite img, Rectangle destRect)
		{
			if (godotImage == null || img == null) return;
			DrawSpriteTo(img, destRect, null);
		}

		/// <summary>
		/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight, float[][] cm)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GDrawCImg(ASprite img, Rectangle destRect, float[][] cm)
		{
			if (godotImage == null || img == null) return;
			DrawSpriteTo(img, destRect, cm);
		}

		void DrawSpriteTo(ASprite img, Rectangle destRect, float[][] cm)
		{
			Godot.Image srcImage = null;
			Godot.Rect2I srcRegion = new Godot.Rect2I(0, 0, img.DestBaseSize.Width, img.DestBaseSize.Height);
			Rectangle drawRect = destRect;
			bool needsCm = cm != null && cm.Length >= 5;

			if (img is ASpriteSingle single)
			{
				if (single.BaseImage?.Bitmap is BitmapTexture bt && bt.sourceImage != null)
				{
					srcImage = needsCm ? bt.sourceImage.Duplicate() as Godot.Image : bt.sourceImage;
					srcRegion = new Godot.Rect2I(single.SrcRectangle.X, single.SrcRectangle.Y,
						single.SrcRectangle.Width, single.SrcRectangle.Height);
				}
				else if (single.BaseImage is GraphicsImage gImg && gImg.godotImage != null)
				{
					srcImage = needsCm ? gImg.godotImage.Duplicate() as Godot.Image : gImg.godotImage;
					srcRegion = new Godot.Rect2I(single.SrcRectangle.X, single.SrcRectangle.Y,
						single.SrcRectangle.Width, single.SrcRectangle.Height);
				}
				else if (single.BaseImage?.Bitmap is Bitmap bmp && !string.IsNullOrEmpty(bmp.path))
				{
					var ti = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
					if (ti == null && !string.IsNullOrEmpty(bmp.filename))
						ti = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
					if (ti?.image != null)
					{
						srcImage = needsCm ? ti.image.Duplicate() as Godot.Image : ti.image;
						srcRegion = new Godot.Rect2I(single.SrcRectangle.X, single.SrcRectangle.Y,
							single.SrcRectangle.Width, single.SrcRectangle.Height);
					}
				}

				if (!single.DestBasePosition.IsEmpty)
				{
					drawRect.X = drawRect.X + single.DestBasePosition.X * drawRect.Width / single.SrcRectangle.Width;
					drawRect.Y = drawRect.Y + single.DestBasePosition.Y * drawRect.Height / single.SrcRectangle.Height;
				}
			}
			else if (img is SpriteAnime anime)
			{
				AbstractImage baseImage;
				uEmuera.Drawing.Rectangle srcRect;
				uEmuera.Drawing.Point offset;
				if (anime.GetCurrentFrameInfo(out baseImage, out srcRect, out offset))
				{
					drawRect.X = drawRect.X + (anime.DestBasePosition.X + offset.X) * drawRect.Width / anime.DestBaseSize.Width;
					drawRect.Y = drawRect.Y + (anime.DestBasePosition.Y + offset.Y) * drawRect.Height / anime.DestBaseSize.Height;
					drawRect.Width = srcRect.Width * drawRect.Width / anime.DestBaseSize.Width;
					drawRect.Height = srcRect.Height * drawRect.Height / anime.DestBaseSize.Height;

					if (baseImage?.Bitmap is BitmapTexture bt && bt.sourceImage != null)
					{
						srcImage = needsCm ? bt.sourceImage.Duplicate() as Godot.Image : bt.sourceImage;
						srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
					}
					else if (baseImage is GraphicsImage gImg && gImg.godotImage != null)
					{
						srcImage = needsCm ? gImg.godotImage.Duplicate() as Godot.Image : gImg.godotImage;
						srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
					}
					else if (baseImage?.Bitmap is Bitmap bmp && !string.IsNullOrEmpty(bmp.path))
					{
						var ti = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
						if (ti == null && !string.IsNullOrEmpty(bmp.filename))
							ti = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
						if (ti?.image != null)
						{
							srcImage = needsCm ? ti.image.Duplicate() as Godot.Image : ti.image;
							srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
						}
					}
				}
			}

			if (srcImage == null)
			{
				Godot.GD.PushWarning($"[GraphicsImage.DrawSpriteTo] srcImage is null for sprite '{img.Name}' (needsCm={needsCm})");
				return;
			}

			if (srcImage.GetFormat() != Godot.Image.Format.Rgba8)
			{
				if (!needsCm)
					srcImage = srcImage.Duplicate() as Godot.Image;
				srcImage.Convert(Godot.Image.Format.Rgba8);
			}

			if (needsCm)
			{
				if (EmueraMain.GpuReady)
				{
					var gpuItem = EmueraMain.GpuSubmitColorMatrix(srcImage, srcRegion, cm);
					if (gpuItem.Completed.Wait(500))
					{
						if (gpuItem.ResultImage != null && gpuItem.ResultImage.GetWidth() > 0)
						{
							srcImage = gpuItem.ResultImage;
							srcRegion = new Godot.Rect2I(0, 0, srcImage.GetWidth(), srcImage.GetHeight());
							goto skip_cpu_cm;
						}
					}
				}
				srcImage = ApplyColorMatrix(srcImage, srcRegion, cm);
				srcRegion = new Godot.Rect2I(0, 0, srcImage.GetWidth(), srcImage.GetHeight());
				skip_cpu_cm: ;
			}

			var dstPos = new Godot.Vector2I(drawRect.X, drawRect.Y);
			bool needsScale = drawRect.Width > 0 && drawRect.Height > 0 &&
				(drawRect.Width != srcRegion.Size.X || drawRect.Height != srcRegion.Size.Y);

			if (needsScale)
			{
				var sub = srcImage.GetRegion(srcRegion);
				if (sub != null)
				{
					sub.Resize(drawRect.Width, drawRect.Height, Godot.Image.Interpolation.Bilinear);
					BlendRect(godotImage, sub, new Godot.Rect2I(0, 0, drawRect.Width, drawRect.Height), dstPos);
				}
				else
				{
					BlendRect(godotImage, srcImage, srcRegion, dstPos);
				}
			}
			else
			{
				BlendRect(godotImage, srcImage, srcRegion, dstPos);
			}
		}

		static void BlendRect(Godot.Image dst, Godot.Image src, Godot.Rect2I srcRect, Godot.Vector2I dstPos)
		{
			if (src.GetFormat() != Godot.Image.Format.Rgba8)
				src.Convert(Godot.Image.Format.Rgba8);
			if (dst.GetFormat() != Godot.Image.Format.Rgba8)
				dst.Convert(Godot.Image.Format.Rgba8);
			dst.BlendRect(src, srcRect, dstPos);
		}

		static Godot.Image ApplyColorMatrix(Godot.Image src, Godot.Rect2I region, float[][] cm)
		{
			if (src.GetFormat() != Godot.Image.Format.Rgba8)
				src.Convert(Godot.Image.Format.Rgba8);
			var sub = src.GetRegion(region);
			if (sub == null) return src;
			if (sub.GetFormat() != Godot.Image.Format.Rgba8)
				sub.Convert(Godot.Image.Format.Rgba8);
			int w = sub.GetWidth();
			int h = sub.GetHeight();

			float m00 = cm[0][0], m10 = cm[1][0], m20 = cm[2][0], m30 = cm[3][0], m40 = cm[4][0];
			float m01 = cm[0][1], m11 = cm[1][1], m21 = cm[2][1], m31 = cm[3][1], m41 = cm[4][1];
			float m02 = cm[0][2], m12 = cm[1][2], m22 = cm[2][2], m32 = cm[3][2], m42 = cm[4][2];
			float m03 = cm[0][3], m13 = cm[1][3], m23 = cm[2][3], m33 = cm[3][3], m43 = cm[4][3];

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					var color = sub.GetPixel(x, y);
					float r = color.R, g = color.G, b = color.B, a = color.A;

					float nr = m00*r + m10*g + m20*b + m30*a + m40;
					float ng = m01*r + m11*g + m21*b + m31*a + m41;
					float nb = m02*r + m12*g + m22*b + m32*a + m42;
					float na = m03*r + m13*g + m23*b + m33*a + m43;

					sub.SetPixel(x, y, new Godot.Color(
						Godot.Mathf.Clamp(nr, 0f, 1f),
						Godot.Mathf.Clamp(ng, 0f, 1f),
						Godot.Mathf.Clamp(nb, 0f, 1f),
						Godot.Mathf.Clamp(na, 0f, 1f)));
				}
			}

			return sub;
		}

		/// <summary>
		/// GPU-work-queue entry point called from main thread.
		/// Currently uses optimized byte-level CPU processing.
		/// Future: replace with RenderingDevice compute shader for true GPU-side processing.
		/// </summary>
		public static Godot.Image ApplyColorMatrixGPU(Godot.Image src, Godot.Rect2I region, float[][] cm)
		{
			return ApplyColorMatrix(src, region, cm);
		}

		/// <summary>
		/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect)
		{
			if (godotImage == null || srcGra == null || srcGra.godotImage == null) return;
			var srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
			var srcImage = srcGra.godotImage;
			bool needsScale = destRect.Width > 0 && destRect.Height > 0 &&
				(destRect.Width != srcRect.Width || destRect.Height != srcRect.Height);
			if (needsScale)
			{
				var sub = srcImage.GetRegion(srcRegion);
				if (sub != null)
				{
					sub.Resize(destRect.Width, destRect.Height, Godot.Image.Interpolation.Bilinear);
					BlendRect(godotImage, sub, new Godot.Rect2I(0, 0, destRect.Width, destRect.Height),
						new Godot.Vector2I(destRect.X, destRect.Y));
					return;
				}
			}
			var dstPos = new Godot.Vector2I(destRect.X, destRect.Y);
			BlendRect(godotImage, srcImage, srcRegion, dstPos);
		}

		/// <summary>
		/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight, float[][] cm)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect, float[][] cm)
		{
			if (godotImage == null || srcGra == null || srcGra.godotImage == null || cm == null || cm.Length < 5) return;
			var srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
			var processed = ApplyColorMatrix(srcGra.godotImage, srcRegion, cm);
			bool needsScale = destRect.Width > 0 && destRect.Height > 0 &&
				(destRect.Width != srcRect.Width || destRect.Height != srcRect.Height);
			if (needsScale)
			{
				processed.Resize(destRect.Width, destRect.Height, Godot.Image.Interpolation.Bilinear);
				BlendRect(godotImage, processed, new Godot.Rect2I(0, 0, destRect.Width, destRect.Height),
					new Godot.Vector2I(destRect.X, destRect.Y));
			}
			else
			{
				var dstPos = new Godot.Vector2I(destRect.X, destRect.Y);
				BlendRect(godotImage, processed, new Godot.Rect2I(0, 0, processed.GetWidth(), processed.GetHeight()), dstPos);
			}
		}

		/// <summary>
		/// GDRAWGWITHMASK(int ID, int srcID, int maskID, int destX, int destY)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GDrawGWithMask(GraphicsImage srcGra, GraphicsImage maskGra, Point destPoint)
		{
			if (godotImage == null || srcGra == null || srcGra.godotImage == null || maskGra == null || maskGra.godotImage == null)
				return;
			if (godotImage.GetFormat() != Godot.Image.Format.Rgba8)
				godotImage.Convert(Godot.Image.Format.Rgba8);
			if (srcGra.godotImage.GetFormat() != Godot.Image.Format.Rgba8)
				srcGra.godotImage.Convert(Godot.Image.Format.Rgba8);
			if (maskGra.godotImage.GetFormat() != Godot.Image.Format.Rgba8)
				maskGra.godotImage.Convert(Godot.Image.Format.Rgba8);
			int w = Math.Min(srcGra.Width, maskGra.Width);
			int h = Math.Min(srcGra.Height, maskGra.Height);
			int dw = width;
			int dh = height;
			int srcW = srcGra.Width;
			int maskW = maskGra.Width;
			byte[] dstData = godotImage.GetData();
			byte[] srcData = srcGra.godotImage.GetData();
			byte[] maskData = maskGra.godotImage.GetData();
			bool modified = false;
			for (int y = 0; y < h; y++)
			{
				int dy = destPoint.Y + y;
				if (dy < 0 || dy >= dh) continue;
				for (int x = 0; x < w; x++)
				{
					int dx = destPoint.X + x;
					if (dx < 0 || dx >= dw) continue;
					int mi = (y * maskW + x) * 4;
					int maskByte = maskData[mi];
					if (maskData[mi + 3] < 255)
						maskByte = maskData[mi + 3];
					if (maskByte == 0) continue;
					int si = (y * srcW + x) * 4;
					int di = (dy * dw + dx) * 4;
					if (maskByte == 255)
					{
						dstData[di] = srcData[si]; dstData[di+1] = srcData[si+1];
						dstData[di+2] = srcData[si+2]; dstData[di+3] = srcData[si+3];
					}
					else
					{
						int ma = maskByte + 1;
						int ia = 256 - ma;
						dstData[di]   = (byte)((srcData[si]   * ma + dstData[di]   * ia) >> 8);
						dstData[di+1] = (byte)((srcData[si+1] * ma + dstData[di+1] * ia) >> 8);
						dstData[di+2] = (byte)((srcData[si+2] * ma + dstData[di+2] * ia) >> 8);
						dstData[di+3] = (byte)((srcData[si+3] * ma + dstData[di+3] * ia) >> 8);
					}
					modified = true;
				}
			}
			if (modified)
				godotImage.SetData(dw, dh, false, Godot.Image.Format.Rgba8, dstData);
		}

		public void GSetFont(uEmuera.Drawing.Font r)
		{
			if (r == null) return;
			fontName = r.FontFamily?.Name ?? Config.FontName;
			fontSize = Math.Max(1, (int)r.Size);
			fontStyle = r.Style;
		}
		public void GSetBrush(Brush r)
		{
			if (r is SolidBrush sb)
				brushColor = sb.Color;
		}
		public void GSetPen(Pen r)
		{
			if (r == null) return;
			penColor = r.Color;
			penWidth = Math.Max(1, r.Width);
			r.DashStyle = dashStyle;
			r.DashCap = dashCap;
		}

		public void GDashStyle(long style, long cap)
		{
			dashStyle = Enum.IsDefined(typeof(DashStyle), (int)style) ? (DashStyle)(int)style : DashStyle.Solid;
			dashCap = Enum.IsDefined(typeof(DashCap), (int)cap) ? (DashCap)(int)cap : DashCap.Flat;
		}

		public void GDrawLine(int fromX, int fromY, int destX, int destY)
		{
			if (godotImage == null) return;
			int dx = Math.Abs(destX - fromX);
			int dy = Math.Abs(destY - fromY);
			int sx = fromX < destX ? 1 : -1;
			int sy = fromY < destY ? 1 : -1;
			int err = dx - dy;
			int step = 0;
			int x = fromX;
			int y = fromY;
			while (true)
			{
				if (DashOn(step))
					DrawPenPoint(x, y);
				if (x == destX && y == destY)
					break;
				int e2 = err * 2;
				if (e2 > -dy)
				{
					err -= dy;
					x += sx;
				}
				if (e2 < dx)
				{
					err += dx;
					y += sy;
				}
				step++;
			}
		}

		public void GDrawPolygonAddPoint(Point point)
		{
			polygonPoints.Add(point);
		}

		public void GDrawPolygonClearPoint()
		{
			polygonPoints.Clear();
		}

		public void GDrawPolygon()
		{
			if (polygonPoints.Count < 2)
				return;
			for (int i = 0; i < polygonPoints.Count; i++)
			{
				Point a = polygonPoints[i];
				Point b = polygonPoints[(i + 1) % polygonPoints.Count];
				GDrawLine(a.X, a.Y, b.X, b.Y);
			}
		}

		public void GFillPolygon()
		{
			if (godotImage == null || polygonPoints.Count < 3)
				return;
			int minY = Math.Max(0, polygonPoints.Min(p => p.Y));
			int maxY = Math.Min(height - 1, polygonPoints.Max(p => p.Y));
			var fill = new Godot.Color(brushColor.r, brushColor.g, brushColor.b, brushColor.a);
			for (int y = minY; y <= maxY; y++)
			{
				var nodes = new List<int>();
				int j = polygonPoints.Count - 1;
				for (int i = 0; i < polygonPoints.Count; i++)
				{
					Point pi = polygonPoints[i];
					Point pj = polygonPoints[j];
					if ((pi.Y < y && pj.Y >= y) || (pj.Y < y && pi.Y >= y))
					{
						int x = pi.X + (y - pi.Y) * (pj.X - pi.X) / (pj.Y - pi.Y);
						nodes.Add(x);
					}
					j = i;
				}
				nodes.Sort();
				for (int i = 0; i + 1 < nodes.Count; i += 2)
				{
					int x1 = Math.Max(0, nodes[i]);
					int x2 = Math.Min(width - 1, nodes[i + 1]);
					for (int x = x1; x <= x2; x++)
						godotImage.SetPixel(x, y, fill);
				}
			}
		}

		public void GDrawGWithRotate(GraphicsImage srcGra, long angleDegrees, int pivotX, int pivotY)
		{
			if (godotImage == null || srcGra?.godotImage == null)
				return;
			double radians = angleDegrees * Math.PI / 180.0;
			double cos = Math.Cos(radians);
			double sin = Math.Sin(radians);
			for (int sy = 0; sy < srcGra.Height; sy++)
			{
				for (int sx = 0; sx < srcGra.Width; sx++)
				{
					var c = srcGra.godotImage.GetPixel(sx, sy);
					if (c.A <= 0f)
						continue;
					double dx = sx - pivotX;
					double dy = sy - pivotY;
					int tx = pivotX + (int)Math.Round(dx * cos - dy * sin);
					int ty = pivotY + (int)Math.Round(dx * sin + dy * cos);
					if (tx >= 0 && tx < width && ty >= 0 && ty < height)
						godotImage.SetPixel(tx, ty, c);
				}
			}
		}

		public void GRotate(long angleDegrees, int pivotX, int pivotY)
		{
			if (godotImage == null)
				return;
			var src = godotImage.Duplicate() as Godot.Image;
			godotImage.Fill(new Godot.Color(0, 0, 0, 0));
			double radians = angleDegrees * Math.PI / 180.0;
			double cos = Math.Cos(radians);
			double sin = Math.Sin(radians);
			for (int sy = 0; sy < height; sy++)
			{
				for (int sx = 0; sx < width; sx++)
				{
					var c = src.GetPixel(sx, sy);
					if (c.A <= 0f)
						continue;
					double dx = sx - pivotX;
					double dy = sy - pivotY;
					int tx = pivotX + (int)Math.Round(dx * cos - dy * sin);
					int ty = pivotY + (int)Math.Round(dx * sin + dy * cos);
					if (tx >= 0 && tx < width && ty >= 0 && ty < height)
						godotImage.SetPixel(tx, ty, c);
				}
			}
			src.Dispose();
		}

		bool DashOn(int step)
		{
			int unit = Math.Max(1, (int)penWidth);
			return dashStyle switch
			{
				DashStyle.Dash => step % (unit * 4) < unit * 3,
				DashStyle.Dot => step % (unit * 2) < unit,
				DashStyle.DashDot => step % (unit * 6) < unit * 3 || step % (unit * 6) >= unit * 4 && step % (unit * 6) < unit * 5,
				DashStyle.DashDotDot => step % (unit * 8) < unit * 3 || step % (unit * 8) >= unit * 4 && step % (unit * 8) < unit * 5 || step % (unit * 8) >= unit * 6 && step % (unit * 8) < unit * 7,
				_ => true,
			};
		}

		void DrawPenPoint(int x, int y)
		{
			int radius = Math.Max(0, (int)penWidth / 2);
			var c = new Godot.Color(penColor.r, penColor.g, penColor.b, penColor.a);
			for (int yy = y - radius; yy <= y + radius; yy++)
			{
				if (yy < 0 || yy >= height) continue;
				for (int xx = x - radius; xx <= x + radius; xx++)
				{
					if (xx < 0 || xx >= width) continue;
					if (dashCap == DashCap.Round && radius > 0)
					{
						int rx = xx - x;
						int ry = yy - y;
						if (rx * rx + ry * ry > radius * radius)
							continue;
					}
					godotImage.SetPixel(xx, yy, c);
				}
			}
		}

		public bool GDrawString(string text, int x, int y)
		{
			if (godotImage == null)
				return false;
			text ??= "";
			int renderWidth;
			using (var font = new Font(Fontname, Math.Max(1, Fontsize), fontStyle, GraphicsUnit.Pixel))
				renderWidth = Math.Max(1, (int)uEmuera.Utils.GetDisplayLength(text, font));
			int renderHeight = Math.Max(1, Fontsize + 6);
			var item = EmueraMain.SubmitTextRender(text, Fontname, Fontsize, Fontstyle, brushColor, renderWidth, renderHeight);
			if (item == null || !item.Completed.Wait(500) || item.ResultImage == null)
				return false;
			BlendRect(godotImage, item.ResultImage,
				new Godot.Rect2I(0, 0, item.ResultImage.GetWidth(), item.ResultImage.GetHeight()),
				new Godot.Vector2I(x, y));
			return true;
		}

		/// <summary>
		/// GSETCOLOR(int ID, int cARGB, int x, int y)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GSetColor(uEmuera.Drawing.Color c, int x, int y)
		{
			if (godotImage == null) return;
			if (x < 0 || x >= width || y < 0 || y >= height) return;
			godotImage.SetPixel(x, y, new Godot.Color(c.r, c.g, c.b, c.a));
		}

		/// <summary>
		/// GGETCOLOR(int ID, int x, int y)
		/// エラーチェックは呼び出し元でのみ行う。特に画像範囲内であるかどうかチェックすること
		/// </summary>
		public uEmuera.Drawing.Color GGetColor(int x, int y)
		{
			if (godotImage == null) throw new NullReferenceException();
			if (x < 0 || x >= width || y < 0 || y >= height) throw new ArgumentOutOfRangeException();
			var c = godotImage.GetPixel(x, y);
			return new uEmuera.Drawing.Color(c.R, c.G, c.B, c.A);
		}

		/// <summary>
		/// GDISPOSE(int ID)
		/// </summary>
		public void GDispose()
		{
			is_created = false;
			width = 0;
			height = 0;
			if (renderBitmap != null)
				renderBitmap.image = null;
			renderBitmap = null;
			Bitmap = null;
			if (godotImage != null)
			{
				godotImage.Dispose();
				godotImage = null;
			}
		}

		public override void Dispose()
		{
			this.GDispose();
		}

		~GraphicsImage()
		{
			Dispose();
		}
		#endregion

		public override bool IsCreated { get { return is_created; } }
		bool is_created = false;

		public int Width { get { return width; } }
		int width = 0;
		public int Height { get { return height; } }
		int height = 0;
		public string Fontname { get { return fontName ?? Config.FontName; } }
		public int Fontsize { get { return fontSize; } }
		public int Fontstyle { get { return FontStyleToInt(fontStyle); } }
		public long PenWidth { get { return penWidth; } }
		public uEmuera.Drawing.Color PenColor { get { return penColor; } }
		public uEmuera.Drawing.Color BrushColor { get { return brushColor; } }

		static int FontStyleToInt(FontStyle style)
		{
			int value = 0;
			if ((style & FontStyle.Bold) != 0) value |= 1;
			if ((style & FontStyle.Italic) != 0) value |= 2;
			if ((style & FontStyle.Strikeout) != 0) value |= 4;
			if ((style & FontStyle.Underline) != 0) value |= 8;
			return value;
		}
	}
}
