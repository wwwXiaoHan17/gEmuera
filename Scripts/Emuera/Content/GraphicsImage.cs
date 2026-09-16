using MinorShift._Library;
using System;
using System.Collections.Generic;
//using System.Drawing;
//using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
		// GraphicsImage 由 ERB 后台线程持续改写，而 Godot 主线程会把它转成显示贴图。
		// 这里用轻量互斥和版本号保证 UI 只拿到完整稳定帧，避免角色差分重绘中的半成品闪白。
		readonly object imageSync = new object();
		long displayRevision = 0;
		ulong lastMutationMs = 0;
		long suppressedDisplayRevision = -1;
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
		public long DisplayRevision => Interlocked.Read(ref displayRevision);
		bool IsCurrentDisplaySuppressed => DisplayRevision == Interlocked.Read(ref suppressedDisplayRevision);

		// M6：批量像素操作需要把 Godot 原生 SetPixel 的浮点→字节转换精确复刻到内存写入。
		// 这里用 1x1 probe 图直接向引擎查询转换结果（truncation/rounding 由引擎自身决定），
		// 保证批量化后的输出与逐点 SetPixel 逐位一致，不依赖对引擎实现的猜测。
		static readonly object colorProbeLock = new object();
		static Godot.Image colorProbe;
		static byte[] byteRoundTripTable;

		static byte[] ColorToRgba8Bytes(Godot.Color c)
		{
			lock (colorProbeLock)
			{
				if (colorProbe == null)
					colorProbe = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
				colorProbe.SetPixel(0, 0, c);
				return colorProbe.GetData();
			}
		}

		// GetPixel(byte → float) + SetPixel(float → byte) 的往返结果。
		// 对 GRotate/GDrawGWithRotate 逐像素复制：直接复制字节可能与往返结果差 1，
		// 因此按字节值建 256 项表，逐项用 probe 精确探测引擎转换。
		static byte RoundTripChannel(byte b)
		{
			var table = byteRoundTripTable;
			if (table != null)
				return table[b];
			lock (colorProbeLock)
			{
				table = byteRoundTripTable;
				if (table == null)
				{
					if (colorProbe == null)
						colorProbe = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
					table = new byte[256];
					for (int i = 0; i < 256; i++)
					{
						colorProbe.SetPixel(0, 0, new Godot.Color(i / 255f, 0, 0, 1));
						var d = colorProbe.GetData();
						table[i] = d[0];
					}
					byteRoundTripTable = table;
				}
				return table[b];
			}
		}

		#region Bitmap書き込み・作成

		/// <summary>
		/// GCREATE(int ID, int width, int height)
		/// Graphicsの基礎となるBitmapを作成する。エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GCreate(int x, int y, bool useGDI)
		{
			lock (imageSync)
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
				MarkImageMutated();
			}
		}

		internal bool GCreateFromF(Bitmap bmp, bool useGDI)
		{
			lock (imageSync)
			{
				this.GDispose();
				if (bmp == null)
					return false;
				is_created = true;
				width = bmp.Width;
				height = bmp.Height;
				renderBitmap = new BitmapRenderTexture(width, height);
				Bitmap = renderBitmap;
				if (bmp is BitmapRenderTexture rt && rt.image != null)
				{
					godotImage = rt.image.Duplicate() as Godot.Image;
				}
				else if (bmp is BitmapTexture bt)
				{
					var ti = bt.EnsureTextureInfoForScriptComposition();
					if (ti != null && !ti.IsPlaceholder && ti.image != null)
						godotImage = ti.image.Duplicate() as Godot.Image;
				}
				else if (!string.IsNullOrEmpty(bmp.path))
				{
					var ti = SpriteManager.GetTextureInfoForScriptComposition(bmp.path, bmp.path);
					if (ti == null && !string.IsNullOrEmpty(bmp.filename))
						ti = SpriteManager.GetTextureInfoForScriptComposition(bmp.filename, bmp.path);
					if (ti != null && !ti.IsPlaceholder && ti.image != null)
						godotImage = ti.image.Duplicate() as Godot.Image;
				}
				if (godotImage == null || godotImage.GetWidth() <= 0 || godotImage.GetHeight() <= 0)
				{
					this.GDispose();
					return false;
				}
				if (godotImage.GetFormat() != Godot.Image.Format.Rgba8)
					godotImage.Convert(Godot.Image.Format.Rgba8);
				renderBitmap.image = godotImage;
				MarkImageMutated();
				return true;
			}
		}

		/// <summary>
		/// GCLEAR(int ID, int cARGB)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GClear(uEmuera.Drawing.Color c)
		{
			lock (imageSync)
			{
				if (godotImage == null) return;
				godotImage.Fill(c.ToGodotColor());
				MarkImageMutated();
			}
		}

		/// <summary>
		/// GCLEAR(int ID, int cARGB, int x, int y, int w, int h) —— EM_私家版_GCLEAR拡張
		/// 对照 v24 GraphicsImage.GClear(Color, x, y, w, h)：SetClip + Clear + ResetClip。
		/// Godot 端用 FillRect 限定矩形区域填充，语义等价。
		/// </summary>
		public void GClear(uEmuera.Drawing.Color c, int x, int y, int w, int h)
		{
			lock (imageSync)
			{
				if (godotImage == null) return;
				int x1 = Math.Max(0, x);
				int y1 = Math.Max(0, y);
				int x2 = Math.Min(width, x + w);
				int y2 = Math.Min(height, y + h);
				if (x2 <= x1 || y2 <= y1)
					return;
				godotImage.FillRect(new Godot.Rect2I(x1, y1, x2 - x1, y2 - y1), c.ToGodotColor());
				MarkImageMutated();
			}
		}

		/// <summary>
		/// GFILLRECTANGLE(int ID, int x, int y, int width, int height)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GFillRectangle(Rectangle rect)
		{
			lock (imageSync)
			{
				if (godotImage == null) return;
				var c = brushColor;
				if (c.a <= 0) return;
				int x1 = Math.Max(0, rect.X);
				int y1 = Math.Max(0, rect.Y);
				int x2 = Math.Min(width, rect.X + rect.Width);
				int y2 = Math.Min(height, rect.Y + rect.Height);
				if (x2 <= x1 || y2 <= y1)
					return;
				var gc = c.ToGodotColor();
				// Use Godot's native rectangle fill instead of per-pixel writes. GFILL
				// commands are common in era UI scripts, and this keeps the hot path in
				// engine code for mobile CPU efficiency.
				godotImage.FillRect(new Godot.Rect2I(x1, y1, x2 - x1, y2 - y1), gc);
				MarkImageMutated();
			}
		}

		/// <summary>
		/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public bool GDrawCImg(ASprite img, Rectangle destRect)
		{
			lock (imageSync)
			{
				if (godotImage == null || img == null) return false;
				if (DrawSpriteTo(img, destRect, null))
				{
					MarkImageMutated();
					return true;
				}
				return false;
			}
		}

		/// <summary>
		/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight, float[][] cm)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public bool GDrawCImg(ASprite img, Rectangle destRect, float[][] cm)
		{
			lock (imageSync)
			{
				if (godotImage == null || img == null) return false;
				if (DrawSpriteTo(img, destRect, cm))
				{
					MarkImageMutated();
					return true;
				}
				return false;
			}
		}

		bool DrawSpriteTo(ASprite img, Rectangle destRect, float[][] cm)
		{
			Godot.Image srcImage = null;
			Godot.Rect2I srcRegion = new Godot.Rect2I(0, 0, img.DestBaseSize.Width, img.DestBaseSize.Height);
			Rectangle drawRect = destRect;
			bool needsCm = cm != null && cm.Length >= 5;
			// M5：源内容版本。GraphicsImage 用 DisplayRevision（每次改写递增），
			// 纹理源无可达的原地改写路径，恒为 0；两者都以图对象身份参与缓存 key。
			long srcVersion = 0;

			if (img is ASpriteSingle single)
			{
				if (single.BaseImage?.Bitmap is BitmapTexture bt && bt.sourceImage != null)
				{
					var ti = bt.EnsureTextureInfoForScriptComposition();
					if (ti == null || ti.IsPlaceholder || ti.image == null)
						return false;
					// N4：needsCm 不再整图 Duplicate；颜色矩阵只读源区域，GPU/CPU 路径
					// 各自只拷贝所需区域，共享纹理源不会被改写。
					srcImage = ti.image;
					srcRegion = new Godot.Rect2I(single.SrcRectangle.X, single.SrcRectangle.Y,
						single.SrcRectangle.Width, single.SrcRectangle.Height);
				}
				else if (single.BaseImage is GraphicsImage gImg && gImg.godotImage != null)
				{
					if (gImg.IsCurrentDisplaySuppressed)
						return false;
					srcImage = gImg.godotImage;
					srcVersion = gImg.DisplayRevision;
					srcRegion = new Godot.Rect2I(single.SrcRectangle.X, single.SrcRectangle.Y,
						single.SrcRectangle.Width, single.SrcRectangle.Height);
				}
				else if (single.BaseImage?.Bitmap is Bitmap bmp && !string.IsNullOrEmpty(bmp.path))
				{
					var ti = SpriteManager.GetTextureInfoForScriptComposition(bmp.path, bmp.path);
					if (ti == null && !string.IsNullOrEmpty(bmp.filename))
						ti = SpriteManager.GetTextureInfoForScriptComposition(bmp.filename, bmp.path);
					if (ti != null && !ti.IsPlaceholder && ti.image != null)
					{
						srcImage = ti.image;
						srcRegion = new Godot.Rect2I(single.SrcRectangle.X, single.SrcRectangle.Y,
							single.SrcRectangle.Width, single.SrcRectangle.Height);
					}
					else if (ti?.IsPlaceholder == true)
					{
						return false;
					}
				}

				drawRect = single.ApplyDestBaseCanvas(drawRect);
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
						var ti = bt.EnsureTextureInfoForScriptComposition();
						if (ti == null || ti.IsPlaceholder || ti.image == null)
							return false;
						srcImage = ti.image;
						srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
					}
					else if (baseImage is GraphicsImage gImg && gImg.godotImage != null)
					{
						if (gImg.IsCurrentDisplaySuppressed)
							return false;
						srcImage = gImg.godotImage;
						srcVersion = gImg.DisplayRevision;
						srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
					}
					else if (baseImage?.Bitmap is Bitmap bmp && !string.IsNullOrEmpty(bmp.path))
					{
						var ti = SpriteManager.GetTextureInfoForScriptComposition(bmp.path, bmp.path);
						if (ti == null && !string.IsNullOrEmpty(bmp.filename))
							ti = SpriteManager.GetTextureInfoForScriptComposition(bmp.filename, bmp.path);
						if (ti != null && !ti.IsPlaceholder && ti.image != null)
						{
							srcImage = ti.image;
							srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
						}
						else if (ti?.IsPlaceholder == true)
						{
							return false;
						}
					}
				}
			}

			if (srcImage == null)
			{
				global::GenericUtils.Warn(global::EmueraLogCategory.Sprite, () => $"[GraphicsImage.DrawSpriteTo] srcImage is null for sprite '{img.Name}' (needsCm={needsCm})");
				return false;
			}

			if (srcImage.GetFormat() != Godot.Image.Format.Rgba8)
			{
				if (needsCm)
				{
					// N4：非 Rgba8 的共享源只拷贝 srcRegion 区域并就地转换，避免整图 Duplicate。
					// 私有拷贝随后对 GPU/CPU 颜色矩阵路径只读。
					srcImage = srcImage.GetRegion(srcRegion) ?? srcImage.Duplicate() as Godot.Image;
					if (srcImage == null)
						return false;
					srcRegion = new Godot.Rect2I(0, 0, srcImage.GetWidth(), srcImage.GetHeight());
					srcVersion = 0;
				}
				else
				{
					srcImage = srcImage.Duplicate() as Godot.Image;
				}
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
				// M5：CPU 路径按 (源图, 版本, region, 矩阵位级 key) memoize，
				// 重复合成同一 (region, 矩阵) 时跳过 GetRegion+GetData+SetData 三份拷贝。
				srcImage = ApplyColorMatrixMemoized(srcImage, srcVersion, srcRegion, cm);
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
			return true;
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
			// CPU fallback is intentionally byte-buffer based. Godot GetPixel/SetPixel
			// performs bounds/format work per call, which is too expensive for large
			// CG regions on Android.
			if (src.GetFormat() != Godot.Image.Format.Rgba8)
				src.Convert(Godot.Image.Format.Rgba8);
			var sub = src.GetRegion(region);
			if (sub == null) return src;
			if (sub.GetFormat() != Godot.Image.Format.Rgba8)
				sub.Convert(Godot.Image.Format.Rgba8);
			if (IsIdentityColorMatrix(cm))
				return sub;
			int w = sub.GetWidth();
			int h = sub.GetHeight();

			// Hoist matrix entries out of the pixel loop so each pixel only performs
			// arithmetic and byte writes. This path is used when GPU submission is not
			// available or times out.
			float m00 = cm[0][0], m10 = cm[1][0], m20 = cm[2][0], m30 = cm[3][0], m40 = cm[4][0];
			float m01 = cm[0][1], m11 = cm[1][1], m21 = cm[2][1], m31 = cm[3][1], m41 = cm[4][1];
			float m02 = cm[0][2], m12 = cm[1][2], m22 = cm[2][2], m32 = cm[3][2], m42 = cm[4][2];
			float m03 = cm[0][3], m13 = cm[1][3], m23 = cm[2][3], m33 = cm[3][3], m43 = cm[4][3];

			byte[] data = sub.GetData();
			for (int i = 0; i + 3 < data.Length; i += 4)
			{
				float r = data[i] / 255.0f;
				float g = data[i + 1] / 255.0f;
				float b = data[i + 2] / 255.0f;
				float a = data[i + 3] / 255.0f;

				float nr = m00*r + m10*g + m20*b + m30*a + m40;
				float ng = m01*r + m11*g + m21*b + m31*a + m41;
				float nb = m02*r + m12*g + m22*b + m32*a + m42;
				float na = m03*r + m13*g + m23*b + m33*a + m43;

				data[i] = ToByte(nr);
				data[i + 1] = ToByte(ng);
				data[i + 2] = ToByte(nb);
				data[i + 3] = ToByte(na);
			}

			// Write the transformed buffer back once. Keeping the image mutation
			// batched avoids repeated native interop calls and minimizes GC pressure.
			sub.SetData(w, h, false, Godot.Image.Format.Rgba8, data);
			return sub;
		}

		static byte ToByte(float value)
		{
			return (byte)Godot.Mathf.Clamp(Godot.Mathf.RoundToInt(value * 255.0f), 0, 255);
		}

		#region ColorMatrix memoize

		// M5：CPU ApplyColorMatrix 结果 memoize。key = (源图对象身份, 内容版本, region, 矩阵位级 key)。
		// 内容版本：
		//  - GraphicsImage 源传 DisplayRevision（每次改写递增，版本不匹配即重新合成，保证无陈旧结果）；
		//  - 纹理源（SpriteManager TextureInfo / BitmapTexture）无可达的原地改写路径（Drawing.Bitmap.SetPixel
		//    无任何调用方），身份相同即内容相同；TextureInfo 重解码/淘汰会产生新对象 → 身份不同 → miss 重算。
		// 缓存只保存 ApplyColorMatrix 的返回值（region 私有拷贝），调用方不得改写（GDrawG-with-cm 缩放前先 Duplicate）。
		const int ColorMatrixMemoizeCapacity = 64;
		static readonly object colorMatrixMemoizeLock = new object();
		static readonly Dictionary<ColorMatrixMemoizeKey, ColorMatrixMemoizeEntry> colorMatrixMemoize =
			new Dictionary<ColorMatrixMemoizeKey, ColorMatrixMemoizeEntry>();
		static readonly LinkedList<ColorMatrixMemoizeKey> colorMatrixMemoizeLru = new LinkedList<ColorMatrixMemoizeKey>();

		readonly struct ColorMatrixMemoizeKey
		{
			public readonly Godot.Image Source;
			public readonly long Version;
			public readonly Godot.Rect2I Region;
			public readonly ulong MatrixKey;
			public ColorMatrixMemoizeKey(Godot.Image source, long version, Godot.Rect2I region, ulong matrixKey)
			{
				Source = source;
				Version = version;
				Region = region;
				MatrixKey = matrixKey;
			}
		}

		sealed class ColorMatrixMemoizeEntry
		{
			public readonly LinkedListNode<ColorMatrixMemoizeKey> LruNode;
			public readonly Godot.Image Result;
			public ColorMatrixMemoizeEntry(LinkedListNode<ColorMatrixMemoizeKey> node, Godot.Image result)
			{
				LruNode = node;
				Result = result;
			}
		}

		/// <summary>
		/// CPU ColorMatrix 合成 + memoize。version &lt; 0 时直接计算（GPU 后备路径使用，
		/// 该路径运行在主线程、源内容可能并发改写，不缓存以保证零漂移）。
		/// </summary>
		static Godot.Image ApplyColorMatrixMemoized(Godot.Image src, long version, Godot.Rect2I region, float[][] cm)
		{
			if (version < 0)
				return ApplyColorMatrix(src, region, cm);
			var key = new ColorMatrixMemoizeKey(src, version, region, ColorMatrixGPU.GetMatrixKey(cm));
			lock (colorMatrixMemoizeLock)
			{
				if (colorMatrixMemoize.TryGetValue(key, out var entry))
				{
					var node = entry.LruNode;
					colorMatrixMemoizeLru.Remove(node);
					colorMatrixMemoizeLru.AddLast(node);
					return entry.Result;
				}
				var result = ApplyColorMatrix(src, region, cm);
				// 别名保护：ApplyColorMatrix 在 GetRegion 失败时可能返回源图本身，
				// 缓存源图引用会让后续调用拿到被外部改写的对象，因此不缓存别名结果。
				if (result != null && result.GetWidth() > 0 && result.GetHeight() > 0 && !ReferenceEquals(result, src))
				{
					var node = new LinkedListNode<ColorMatrixMemoizeKey>(key);
					colorMatrixMemoize.Add(key, new ColorMatrixMemoizeEntry(node, result));
					colorMatrixMemoizeLru.AddLast(node);
					if (colorMatrixMemoize.Count > ColorMatrixMemoizeCapacity)
					{
						// 只移除引用不 Dispose：调用方可能仍持有结果图。
						var oldest = colorMatrixMemoizeLru.First;
						colorMatrixMemoizeLru.RemoveFirst();
						colorMatrixMemoize.Remove(oldest.Value);
					}
				}
				return result;
			}
		}

		/// <summary>
		/// 会话边界清理：canary 切换时丢弃旧会话合成的缓存（结果只读共享，直接丢弃引用即可）。
		/// </summary>
		internal static void ResetColorMatrixMemoize()
		{
			lock (colorMatrixMemoizeLock)
			{
				colorMatrixMemoize.Clear();
				colorMatrixMemoizeLru.Clear();
			}
		}

		#endregion

		static bool IsIdentityColorMatrix(float[][] cm)
		{
			const float epsilon = 0.00001f;
			for (int row = 0; row < 5; row++)
			{
				for (int col = 0; col < 4; col++)
				{
					float expected = row == col ? 1.0f : 0.0f;
					if (Math.Abs(cm[row][col] - expected) > epsilon)
						return false;
				}
			}
			return true;
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
			lock (imageSync)
			{
				if (godotImage == null || srcGra == null || srcGra.godotImage == null) return;
				if (srcGra.IsCurrentDisplaySuppressed)
				{
					SuppressCurrentDisplayRevision();
					return;
				}
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
						MarkImageMutated();
						return;
					}
				}
				var dstPos = new Godot.Vector2I(destRect.X, destRect.Y);
				BlendRect(godotImage, srcImage, srcRegion, dstPos);
				MarkImageMutated();
			}
		}

		/// <summary>
		/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight, float[][] cm)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect, float[][] cm)
		{
			lock (imageSync)
			{
				if (godotImage == null || srcGra == null || srcGra.godotImage == null || cm == null || cm.Length < 5) return;
				if (srcGra.IsCurrentDisplaySuppressed)
				{
					SuppressCurrentDisplayRevision();
					return;
				}
				var srcRegion = new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
				// M5：按 (源图, DisplayRevision, region, 矩阵) memoize，源未改写时直接命中缓存。
				var processed = ApplyColorMatrixMemoized(srcGra.godotImage, srcGra.DisplayRevision, srcRegion, cm);
				if (processed != null && processed.GetWidth() > 0 && processed.GetHeight() > 0)
				{
					bool needsScale = destRect.Width > 0 && destRect.Height > 0 &&
						(destRect.Width != srcRect.Width || destRect.Height != srcRect.Height);
					if (needsScale)
					{
						// 缓存结果只读共享：Resize 前先 Duplicate，绝不改写 memoize 缓存图。
						var scaled = processed.Duplicate() as Godot.Image;
						if (scaled != null)
						{
							scaled.Resize(destRect.Width, destRect.Height, Godot.Image.Interpolation.Bilinear);
							BlendRect(godotImage, scaled, new Godot.Rect2I(0, 0, destRect.Width, destRect.Height),
								new Godot.Vector2I(destRect.X, destRect.Y));
						}
					}
					else
					{
						var dstPos = new Godot.Vector2I(destRect.X, destRect.Y);
						BlendRect(godotImage, processed, new Godot.Rect2I(0, 0, processed.GetWidth(), processed.GetHeight()), dstPos);
					}
				}
				// 与原实现一致：调用后标记图像已变更（即使绘制区域退化）。
				MarkImageMutated();
			}
		}

		/// <summary>
		/// GDRAWGWITHMASK(int ID, int srcID, int maskID, int destX, int destY)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GDrawGWithMask(GraphicsImage srcGra, GraphicsImage maskGra, Point destPoint)
		{
			lock (imageSync)
			{
				if (godotImage == null || srcGra == null || srcGra.godotImage == null || maskGra == null || maskGra.godotImage == null)
					return;
				if (srcGra.IsCurrentDisplaySuppressed || maskGra.IsCurrentDisplaySuppressed)
				{
					SuppressCurrentDisplayRevision();
					return;
				}
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
				{
					// Push mask-composited bytes only when a pixel actually changed. Some
					// mask commands are no-ops after clipping, and skipping SetData avoids a
					// full image upload on mobile.
					godotImage.SetData(dw, dh, false, Godot.Image.Format.Rgba8, dstData);
					MarkImageMutated();
				}
			}
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
			lock (imageSync)
			{
				if (godotImage == null) return;
				// M6：GetData 一次 + 内存写入 + SetData 一次，替代逐点 SetPixel 原生调用。
				// 绘制语义与逐点版本一致：Bresenham 步进、DashOn 掩码、笔头方形/圆形范围与裁剪完全相同。
				var penBytes = ColorToRgba8Bytes(penColor.ToGodotColor());
				byte[] data = godotImage.GetData();
				int imgW = godotImage.GetWidth();
				int imgH = godotImage.GetHeight();
				int radius = Math.Max(0, (int)penWidth / 2);
				bool roundCap = dashCap == DashCap.Round && radius > 0;
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
						WritePenPoint(data, imgW, imgH, x, y, radius, roundCap, penBytes);
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
				godotImage.SetData(imgW, imgH, false, Godot.Image.Format.Rgba8, data);
				MarkImageMutated();
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
			lock (imageSync)
			{
				if (polygonPoints.Count < 2)
					return;
				if (godotImage == null)
					return;
				// M6：所有边共享一次 GetData/SetData，避免每条边重复整图拷贝。
				// 输出与逐边调用 GDrawLine 逐位一致（同色覆盖，写入顺序无关）。
				var penBytes = ColorToRgba8Bytes(penColor.ToGodotColor());
				byte[] data = godotImage.GetData();
				int imgW = godotImage.GetWidth();
				int imgH = godotImage.GetHeight();
				int radius = Math.Max(0, (int)penWidth / 2);
				bool roundCap = dashCap == DashCap.Round && radius > 0;
				for (int i = 0; i < polygonPoints.Count; i++)
				{
					Point a = polygonPoints[i];
					Point b = polygonPoints[(i + 1) % polygonPoints.Count];
					DrawLineInto(data, imgW, imgH, a.X, a.Y, b.X, b.Y, radius, roundCap, penBytes);
				}
				godotImage.SetData(imgW, imgH, false, Godot.Image.Format.Rgba8, data);
				MarkImageMutated();
			}
		}

		void DrawLineInto(byte[] data, int imgW, int imgH, int fromX, int fromY, int destX, int destY,
			int radius, bool roundCap, byte[] penBytes)
		{
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
				// GDrawPolygon 的每条边独立计步，与逐边调用 GDrawLine 的 DashOn 语义一致。
				if (DashOn(step))
					WritePenPoint(data, imgW, imgH, x, y, radius, roundCap, penBytes);
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

		public void GFillPolygon()
		{
			lock (imageSync)
			{
				if (godotImage == null || polygonPoints.Count < 3)
					return;
				int minY = Math.Max(0, polygonPoints.Min(p => p.Y));
				int maxY = Math.Min(Math.Min(height - 1, godotImage.GetHeight() - 1), polygonPoints.Max(p => p.Y));
				// M6：扫描线填充改为一次 GetData + 内存写入 + 一次 SetData，输出与逐点 SetPixel 逐位一致。
				var fillBytes = ColorToRgba8Bytes(brushColor.ToGodotColor());
				byte[] data = godotImage.GetData();
				int imgW = godotImage.GetWidth();
				bool anyWrite = false;
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
						int x2 = Math.Min(Math.Min(width - 1, imgW - 1), nodes[i + 1]);
						for (int x = x1; x <= x2; x++)
						{
							int di = (y * imgW + x) * 4;
							data[di] = fillBytes[0];
							data[di + 1] = fillBytes[1];
							data[di + 2] = fillBytes[2];
							data[di + 3] = fillBytes[3];
							anyWrite = true;
						}
					}
				}
				if (anyWrite)
					godotImage.SetData(imgW, godotImage.GetHeight(), false, Godot.Image.Format.Rgba8, data);
				// 与原实现一致：无论是否写出像素，调用后都标记图像已变更。
				MarkImageMutated();
			}
		}

		public void GDrawGWithRotate(GraphicsImage srcGra, long angleDegrees, int pivotX, int pivotY)
		{
			lock (imageSync)
			{
				if (godotImage == null || srcGra?.godotImage == null)
					return;
				if (srcGra.IsCurrentDisplaySuppressed)
				{
					SuppressCurrentDisplayRevision();
					return;
				}
				// M6：源与目标各一次 GetData + 一次 SetData。逐像素复制时用 256 项
				// 字节回环表精确复刻 GetPixel+SetPixel 的浮点往返，输出逐位一致。
				var srcImg = srcGra.godotImage;
				int srcW = srcImg.GetWidth();
				int srcH = srcImg.GetHeight();
				int dstW = godotImage.GetWidth();
				int dstH = godotImage.GetHeight();
				byte[] srcData = srcImg.GetData();
				byte[] dstData = godotImage.GetData();
				double radians = angleDegrees * Math.PI / 180.0;
				double cos = Math.Cos(radians);
				double sin = Math.Sin(radians);
				for (int sy = 0; sy < srcH; sy++)
				{
					for (int sx = 0; sx < srcW; sx++)
					{
						int si = (sy * srcW + sx) * 4;
						// GetPixel 返回 A<=0 即 alpha 字节为 0。
						if (srcData[si + 3] == 0)
							continue;
						double dx = sx - pivotX;
						double dy = sy - pivotY;
						int tx = pivotX + (int)Math.Round(dx * cos - dy * sin);
						int ty = pivotY + (int)Math.Round(dx * sin + dy * cos);
						if (tx >= 0 && tx < dstW && ty >= 0 && ty < dstH)
						{
							int di = (ty * dstW + tx) * 4;
							dstData[di] = RoundTripChannel(srcData[si]);
							dstData[di + 1] = RoundTripChannel(srcData[si + 1]);
							dstData[di + 2] = RoundTripChannel(srcData[si + 2]);
							dstData[di + 3] = RoundTripChannel(srcData[si + 3]);
						}
					}
				}
				godotImage.SetData(dstW, dstH, false, Godot.Image.Format.Rgba8, dstData);
				MarkImageMutated();
			}
		}

		public void GRotate(long angleDegrees, int pivotX, int pivotY)
		{
			lock (imageSync)
			{
				if (godotImage == null)
					return;
				// M6：源数据一次 GetData；目标用全零缓冲（等价于 Fill(0,0,0,0)），
				// 写入后一次 SetData 提交。逐像素字节回环与逐点 GetPixel+SetPixel 一致。
				var src = godotImage.Duplicate() as Godot.Image;
				int imgW = godotImage.GetWidth();
				int imgH = godotImage.GetHeight();
				byte[] srcData = src.GetData();
				byte[] dstData = new byte[srcData.Length];
				double radians = angleDegrees * Math.PI / 180.0;
				double cos = Math.Cos(radians);
				double sin = Math.Sin(radians);
				for (int sy = 0; sy < imgH; sy++)
				{
					for (int sx = 0; sx < imgW; sx++)
					{
						int si = (sy * imgW + sx) * 4;
						if (srcData[si + 3] == 0)
							continue;
						double dx = sx - pivotX;
						double dy = sy - pivotY;
						int tx = pivotX + (int)Math.Round(dx * cos - dy * sin);
						int ty = pivotY + (int)Math.Round(dx * sin + dy * cos);
						if (tx >= 0 && tx < imgW && ty >= 0 && ty < imgH)
						{
							int di = (ty * imgW + tx) * 4;
							dstData[di] = RoundTripChannel(srcData[si]);
							dstData[di + 1] = RoundTripChannel(srcData[si + 1]);
							dstData[di + 2] = RoundTripChannel(srcData[si + 2]);
							dstData[di + 3] = RoundTripChannel(srcData[si + 3]);
						}
					}
				}
				godotImage.SetData(imgW, imgH, false, Godot.Image.Format.Rgba8, dstData);
				src.Dispose();
				MarkImageMutated();
			}
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

		static void WritePenPoint(byte[] data, int imgW, int imgH, int x, int y, int radius, bool roundCap, byte[] penBytes)
		{
			for (int yy = y - radius; yy <= y + radius; yy++)
			{
				if (yy < 0 || yy >= imgH) continue;
				for (int xx = x - radius; xx <= x + radius; xx++)
				{
					if (xx < 0 || xx >= imgW) continue;
					if (roundCap)
					{
						int rx = xx - x;
						int ry = yy - y;
						if (rx * rx + ry * ry > radius * radius)
							continue;
					}
					int di = (yy * imgW + xx) * 4;
					data[di] = penBytes[0];
					data[di + 1] = penBytes[1];
					data[di + 2] = penBytes[2];
					data[di + 3] = penBytes[3];
				}
			}
		}

		public bool GDrawString(string text, int x, int y)
		{
			lock (imageSync)
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
				MarkImageMutated();
				return true;
			}
		}

		/// <summary>
		/// GSETCOLOR(int ID, int cARGB, int x, int y)
		/// エラーチェックは呼び出し元でのみ行う
		/// </summary>
		public void GSetColor(uEmuera.Drawing.Color c, int x, int y)
		{
			lock (imageSync)
			{
				if (godotImage == null) return;
				if (x < 0 || x >= width || y < 0 || y >= height) return;
				godotImage.SetPixel(x, y, c.ToGodotColor());
				MarkImageMutated();
			}
		}

		internal bool TryCreateDisplaySnapshot(ulong stableDelayMs, out Godot.Image snapshot, out long revision, out bool retrySoon)
		{
			snapshot = null;
			revision = DisplayRevision;
			retrySoon = false;
			if (!Monitor.TryEnter(imageSync))
			{
				retrySoon = true;
				return false;
			}
			try
			{
				long beforeRevision = DisplayRevision;
				revision = beforeRevision;
				if (!is_created || godotImage == null)
					return false;
				if (beforeRevision == Interlocked.Read(ref suppressedDisplayRevision))
					return false;
				ulong now = Godot.Time.GetTicksMsec();
				if (stableDelayMs > 0 && now - lastMutationMs < stableDelayMs)
				{
					retrySoon = true;
					return false;
				}
				snapshot = godotImage.Duplicate() as Godot.Image;
				if (beforeRevision != DisplayRevision)
				{
					snapshot?.Dispose();
					snapshot = null;
					retrySoon = true;
					return false;
				}
				return snapshot != null && snapshot.GetWidth() > 0 && snapshot.GetHeight() > 0;
			}
			finally
			{
				Monitor.Exit(imageSync);
			}
		}

		void MarkImageMutated()
		{
			lastMutationMs = Godot.Time.GetTicksMsec();
			Interlocked.Exchange(ref suppressedDisplayRevision, -1);
			Interlocked.Increment(ref displayRevision);
		}

		void SuppressCurrentDisplayRevision()
		{
			lastMutationMs = Godot.Time.GetTicksMsec();
			Interlocked.Exchange(ref suppressedDisplayRevision, DisplayRevision);
		}

		/// <summary>
		/// GGETCOLOR(int ID, int x, int y)
		/// エラーチェックは呼び出し元でのみ行う。特に画像範囲内であるかどうかチェックすること
		/// </summary>
		public uEmuera.Drawing.Color GGetColor(int x, int y)
		{
			lock (imageSync)
			{
				if (godotImage == null) throw new NullReferenceException();
				if (x < 0 || x >= width || y < 0 || y >= height) throw new ArgumentOutOfRangeException();
				var c = godotImage.GetPixel(x, y);
				return new uEmuera.Drawing.Color(c.R, c.G, c.B, c.A);
			}
		}

		/// <summary>
		/// GCLEARLOWALPHA(int ID, int alphaThreshold)
		/// 将 alpha 小于等于阈值的像素批量清为透明黑，避免 ERB 逐像素调用导致 Android 脚本线程长时间阻塞。
		/// </summary>
		public void ClearLowAlpha(int alphaThreshold)
		{
			lock (imageSync)
			{
				if (godotImage == null)
					return;
				if (godotImage.GetFormat() != Godot.Image.Format.Rgba8)
					godotImage.Convert(Godot.Image.Format.Rgba8);

				byte threshold = (byte)Math.Clamp(alphaThreshold, 0, 255);
				byte[] data = godotImage.GetData();
				bool modified = false;
				for (int i = 3; i < data.Length; i += 4)
				{
					if (data[i] <= threshold && (data[i] != 0 || data[i - 1] != 0 || data[i - 2] != 0 || data[i - 3] != 0))
					{
						data[i - 3] = 0;
						data[i - 2] = 0;
						data[i - 1] = 0;
						data[i] = 0;
						modified = true;
					}
				}
				if (modified)
				{
					godotImage.SetData(width, height, false, Godot.Image.Format.Rgba8, data);
					MarkImageMutated();
				}
			}
		}

		/// <summary>
		/// GDISPOSE(int ID)
		/// </summary>
		public void GDispose()
		{
			lock (imageSync)
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
				MarkImageMutated();
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
