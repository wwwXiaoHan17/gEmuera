using System;

using System.IO;

namespace uEmuera.Drawing
{
	public class Bitmap : IDisposable
	{
		public Bitmap(string path)
		{
			this.path = path;
			this.filename = System.IO.Path.GetFileName(path);
		}

		public readonly string path;
		public readonly string filename;
		public string name;
		public Size size;

		public void Dispose()
		{ }
		public virtual int Width
		{
			get { return size.Width; }
		}
		public virtual int Height
		{
			get { return size.Height; }
		}
		public virtual Size Size { get { return size; } }
		public Color GetPixel(int x, int y)
		{
			if (this is BitmapRenderTexture rt && rt.image != null)
			{
				var c = rt.image.GetPixel(x, y);
				return new Color(c.R, c.G, c.B, c.A);
			}
			var ti = SpriteManager.GetTextureInfo(name, path);
			if(ti == null || ti.image == null)
				return Color.Transparent;
			var uc = ti.image.GetPixel(x, y);
			return new Color(uc.R, uc.G, uc.B, uc.A);
		}
		public void SetPixel(Color c, int x, int y)
		{
			if (this is BitmapRenderTexture rt && rt.image != null)
			{
				rt.image.SetPixel(x, y, c.ToGodotColor());
				return;
			}
			var ti = SpriteManager.GetTextureInfo(name, path);
			if(ti == null || ti.image == null)
				return;
			ti.image.SetPixel(x, y, c.ToGodotColor());
			ti.RecreateTexture();
		}
		public void Save(string path)
		{
			if (this is BitmapRenderTexture rt && rt.image != null)
			{
				var renderData = rt.image.SavePngToBuffer();
				System.IO.File.WriteAllBytes(path, renderData);
				return;
			}
			var ti = SpriteManager.GetTextureInfo(name, path);
			if(ti == null || ti.image == null)
				return;
			var data = ti.image.SavePngToBuffer();
			System.IO.File.WriteAllBytes(path, data);
		}
	}

	// BitmapTexture is a file-backed image handle that defers full decode until a
	// texture or source pixels are actually required. This keeps APK/mobile startup
	// and script parsing from paying texture upload costs for unused assets.
	public class BitmapTexture : Bitmap
	{
		public BitmapTexture(string path)
			:base(path)
		{
			if (TryReadImageSize(path, out var imageSize))
				size = imageSize;
		}
		public Godot.ImageTexture texture
		{
			get { return EnsureTextureInfo()?.texture; }
		}
		public Godot.Image sourceImage
		{
			get { return EnsureTextureInfo()?.image; }
		}
		// Expose the SpriteManager owner record to rendering code so visible Godot
		// Controls can pin the underlying texture without duplicating cache lookup
		// or ownership logic in the UI layer.
		internal SpriteManager.TextureInfo TextureInfo
		{
			get { return EnsureTextureInfo(); }
		}
		internal SpriteManager.TextureInfo CachedTextureInfo
		{
			get { return TryResolveCachedTextureInfo(); }
		}
		internal SpriteManager.TextureInfo EnsureTextureInfoForScriptComposition()
		{
			var ti = SpriteManager.GetTextureInfoForScriptComposition(path, path);
			if (ti == null && !string.IsNullOrEmpty(filename))
				ti = SpriteManager.GetTextureInfoForScriptComposition(filename, path);
			textureinfo = ti;
			if (textureinfo != null)
			{
				size.Width = textureinfo.width;
				size.Height = textureinfo.height;
			}
			return textureinfo;
		}
		SpriteManager.TextureInfo textureinfo = null;

		SpriteManager.TextureInfo EnsureTextureInfo()
		{
			// SpriteManager may evict an old TextureInfo between frames. Treat a
			// disposed cached handle as a cache miss and resolve it again by path/name.
			if (textureinfo != null && !textureinfo.IsDisposed)
				return textureinfo;
			textureinfo = null;
			textureinfo = SpriteManager.GetTextureInfo(path, path);
			if (textureinfo == null && !string.IsNullOrEmpty(filename))
				textureinfo = SpriteManager.GetTextureInfo(filename, path);
			if (textureinfo != null)
			{
				size.Width = textureinfo.width;
				size.Height = textureinfo.height;
			}
			return textureinfo;
		}

		internal bool RequestTextureInfoAsync()
		{
			var cached = TryResolveCachedTextureInfo();
			if (cached != null && !cached.IsPlaceholder)
				return false;
			return SpriteManager.RequestTextureInfoAsync(path, path)
				|| (!string.IsNullOrEmpty(filename) && SpriteManager.RequestTextureInfoAsync(filename, path));
		}

		SpriteManager.TextureInfo TryResolveCachedTextureInfo()
		{
			if (textureinfo != null && !textureinfo.IsDisposed)
				return textureinfo;
			textureinfo = null;
			if (!SpriteManager.TryGetTextureInfoCached(path, path, out textureinfo) && !string.IsNullOrEmpty(filename))
				SpriteManager.TryGetTextureInfoCached(filename, path, out textureinfo);
			if (textureinfo != null)
			{
				size.Width = textureinfo.width;
				size.Height = textureinfo.height;
			}
			return textureinfo;
		}

		static bool TryReadImageSize(string path, out Size imageSize)
		{
			imageSize = new Size();
			if (string.IsNullOrEmpty(path))
				return false;

			string resolvedPath = uEmuera.Utils.ResolveExistingFilePath(path);
			if (!uEmuera.Utils.FileExists(resolvedPath))
				return false;

			// 图片尺寸磁盘缓存：二次启动命中则跳过文件头读取（Android 30k 张图片的关键优化）。
			// mtime 不匹配/缓存缺失时安全回退到下方读头。
			if (ImageSizeCache.TryGet(resolvedPath, out int cachedW, out int cachedH))
			{
				imageSize = new Size(cachedW, cachedH);
				return true;
			}

			try
			{
				// 企业级说明：Android/APK 主运行环境会从 /storage/emulated/0 等外部目录读取游戏资源。
				// 这里不能直接使用 System.IO.File.OpenRead，否则在 Godot FileAccess 可读但 .NET 路径探测失败时，
				// CSV 图像会被误判为 0x0，后续 SPRITECREATED/HTML 立绘显示链路会出现空白。
				// 仍然只读取图片头，不提前解码整张图，避免低配手机在资源扫描阶段产生纹理上传和大内存峰值。
				string ext = Path.GetExtension(resolvedPath).ToLowerInvariant();
				using var stream = OpenImageHeaderStream(resolvedPath);
				if (stream == null)
					return false;
				bool ok = ext switch
				{
					".png" => TryReadPngSize(stream, out imageSize),
					".jpg" or ".jpeg" => TryReadJpegSize(stream, out imageSize),
					".bmp" => TryReadBmpSize(stream, out imageSize),
					".webp" => TryReadWebpSize(stream, out imageSize),
					_ => false,
				};
				if (ok)
					ImageSizeCache.Set(resolvedPath, imageSize.Width, imageSize.Height);
				return ok;
			}
			catch
			{
				return false;
			}
		}

		static Stream OpenImageHeaderStream(string path)
		{
			if (Godot.OS.GetName() == "Android")
			{
				var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
				if (file == null)
				{
					string resolved = uEmuera.Utils.ResolveExistingFilePath(path);
					if (!string.Equals(resolved, path, StringComparison.Ordinal))
						file = Godot.FileAccess.Open(resolved, Godot.FileAccess.ModeFlags.Read);
				}
				return file != null ? new GodotFileAccessReadStream(file) : null;
			}
			return File.OpenRead(path);
		}

		sealed class GodotFileAccessReadStream : Stream
		{
			readonly Godot.FileAccess file;

			public GodotFileAccessReadStream(Godot.FileAccess file)
			{
				this.file = file;
			}

			public override bool CanRead { get { return true; } }
			public override bool CanSeek { get { return true; } }
			public override bool CanWrite { get { return false; } }
			public override long Length { get { return (long)file.GetLength(); } }
			public override long Position
			{
				get { return (long)file.GetPosition(); }
				set { file.Seek((ulong)Math.Max(0, value)); }
			}

			public override int Read(byte[] buffer, int offset, int count)
			{
				if (buffer == null)
					throw new ArgumentNullException(nameof(buffer));
				if (offset < 0 || count < 0 || offset + count > buffer.Length)
					throw new ArgumentOutOfRangeException();
				long remaining = Length - Position;
				if (remaining <= 0 || count <= 0)
					return 0;
				int readCount = (int)Math.Min(count, remaining);
				byte[] data = file.GetBuffer(readCount);
				Array.Copy(data, 0, buffer, offset, data.Length);
				return data.Length;
			}

			public override int Read(Span<byte> buffer)
			{
				long remaining = Length - Position;
				if (remaining <= 0 || buffer.Length <= 0)
					return 0;
				int readCount = (int)Math.Min(buffer.Length, remaining);
				byte[] data = file.GetBuffer(readCount);
				data.AsSpan().CopyTo(buffer);
				return data.Length;
			}

			public override long Seek(long offset, SeekOrigin origin)
			{
				long target = origin switch
				{
					SeekOrigin.Begin => offset,
					SeekOrigin.Current => Position + offset,
					SeekOrigin.End => Length + offset,
					_ => Position,
				};
				target = Math.Max(0, Math.Min(target, Length));
				file.Seek((ulong)target);
				return target;
			}

			public override void Flush() { }
			public override void SetLength(long value) { throw new NotSupportedException(); }
			public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

			protected override void Dispose(bool disposing)
			{
				if (disposing)
					file.Dispose();
				base.Dispose(disposing);
			}
		}

		static bool TryReadPngSize(Stream stream, out Size imageSize)
		{
			imageSize = new Size();
			Span<byte> header = stackalloc byte[24];
			if (stream.Read(header) < header.Length)
				return false;
			if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47)
				return false;
			imageSize.Width = ReadInt32BigEndian(header.Slice(16, 4));
			imageSize.Height = ReadInt32BigEndian(header.Slice(20, 4));
			return imageSize.Width > 0 && imageSize.Height > 0;
		}

		static bool TryReadBmpSize(Stream stream, out Size imageSize)
		{
			imageSize = new Size();
			Span<byte> header = stackalloc byte[26];
			if (stream.Read(header) < header.Length || header[0] != 'B' || header[1] != 'M')
				return false;
			imageSize.Width = Math.Abs(BitConverter.ToInt32(header.Slice(18, 4)));
			imageSize.Height = Math.Abs(BitConverter.ToInt32(header.Slice(22, 4)));
			return imageSize.Width > 0 && imageSize.Height > 0;
		}

		static bool TryReadWebpSize(Stream stream, out Size imageSize)
		{
			imageSize = new Size();
			Span<byte> header = stackalloc byte[30];
			if (stream.Read(header) < 30)
				return false;
			if (header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F' ||
				header[8] != 'W' || header[9] != 'E' || header[10] != 'B' || header[11] != 'P')
				return false;
			string chunk = "" + (char)header[12] + (char)header[13] + (char)header[14] + (char)header[15];
			if (chunk == "VP8X")
			{
				imageSize.Width = ReadUInt24LittleEndian(header.Slice(24, 3)) + 1;
				imageSize.Height = ReadUInt24LittleEndian(header.Slice(27, 3)) + 1;
				return imageSize.Width > 0 && imageSize.Height > 0;
			}
			if (chunk == "VP8L" && header[20] == 0x2F)
			{
				uint bits = (uint)(header[21] | (header[22] << 8) | (header[23] << 16) | (header[24] << 24));
				imageSize.Width = (int)((bits & 0x3FFF) + 1);
				imageSize.Height = (int)(((bits >> 14) & 0x3FFF) + 1);
				return imageSize.Width > 0 && imageSize.Height > 0;
			}
			if (chunk == "VP8 " && header[23] == 0x9D && header[24] == 0x01 && header[25] == 0x2A)
			{
				imageSize.Width = (header[26] | (header[27] << 8)) & 0x3FFF;
				imageSize.Height = (header[28] | (header[29] << 8)) & 0x3FFF;
				return imageSize.Width > 0 && imageSize.Height > 0;
			}
			return false;
		}

		static bool TryReadJpegSize(Stream stream, out Size imageSize)
		{
			imageSize = new Size();
			if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
				return false;
			while (stream.Position < stream.Length)
			{
				int prefix;
				do
				{
					prefix = stream.ReadByte();
					if (prefix < 0)
						return false;
				}
				while (prefix != 0xFF);

				int marker;
				do
				{
					marker = stream.ReadByte();
					if (marker < 0)
						return false;
				}
				while (marker == 0xFF);

				if (marker == 0xD9 || marker == 0xDA)
					return false;

				int length = (stream.ReadByte() << 8) + stream.ReadByte();
				if (length < 2)
					return false;

				if ((marker >= 0xC0 && marker <= 0xC3) || (marker >= 0xC5 && marker <= 0xC7) ||
					(marker >= 0xC9 && marker <= 0xCB) || (marker >= 0xCD && marker <= 0xCF))
				{
					stream.ReadByte();
					int height = (stream.ReadByte() << 8) + stream.ReadByte();
					int width = (stream.ReadByte() << 8) + stream.ReadByte();
					imageSize.Width = width;
					imageSize.Height = height;
					return width > 0 && height > 0;
				}
				stream.Seek(length - 2, SeekOrigin.Current);
			}
			return false;
		}

		static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes)
		{
			return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
		}

		static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes)
		{
			return bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
		}
	}

	public class BitmapRenderTexture : Bitmap
	{
		public BitmapRenderTexture(int x, int y)
			:base(null)
		{
			size.Width = x;
			size.Height = y;
		}

		public Godot.Image image;
	}

	public enum GraphicsUnit
	{
		World = 0,
		Display = 1,
		Pixel = 2,
		Point = 3,
		Inch = 4,
		Document = 5,
		Millimeter = 6
	}

	public sealed class Graphics
	{
		public static Graphics instance
		{
			get
			{
				if(instance_ == null)
					instance_ = new Graphics();
				return instance_;
			}
		}
		static Graphics instance_ = null;

		private Graphics() { }

		public void Clear() { }
		public void DrawImage(Bitmap texture, Rectangle destrect,
							Rectangle srcrect, GraphicsUnit unit)
		{
			uEmuera.Logger.Debug(EmueraLogCategory.UI, () => "Graphics.DrawImage " + texture.name);
		}
		public void DrawImage(Bitmap texture, Rectangle destrect,
							int x, int y, int w, int h, GraphicsUnit unit, ImageAttributes ia)
		{
			uEmuera.Logger.Debug(EmueraLogCategory.UI, () => "Graphics.DrawImage " + texture.name);
		}
		public void DrawString(string s, Font font, Brush brush, Point point)
		{
			uEmuera.Logger.Debug(EmueraLogCategory.UI, () => "Graphics.DrawString " + s);
		}
		public void FillRectangel(SolidBrush brush, Rectangle rect)
		{ }
		public void Clear(Color color)
		{
			uEmuera.Logger.Debug(EmueraLogCategory.UI, () => "Graphics.Clear " + color.ToArgb());
		}
	}

	public class Brush
	{ }

	public sealed class SolidBrush : Brush
	{
		public SolidBrush(Color color)
		{
			Color = color;
		}

		public Color Color { get; set; }
	}

	public sealed class Pen
	{
		public Pen()
			: this(Color.Black, 1)
		{ }
		public Pen(Color c, Int64 width)
		{
			Color = c;
			Width = Math.Max(1, width);
			DashStyle = DashStyle.Solid;
			DashCap = DashCap.Flat;
		}

		public Color Color { get; set; }
		public Int64 Width { get; set; }
		public DashStyle DashStyle { get; set; }
		public DashCap DashCap { get; set; }
	}

	public enum DashStyle
	{
		Solid = 0,
		Dash = 1,
		Dot = 2,
		DashDot = 3,
		DashDotDot = 4,
	}

	public enum DashCap
	{
		Flat = 0,
		Round = 2,
		Triangle = 3,
	}

	public enum FontStyle
	{
		Regular = 0,
		Bold = 1,
		Italic = 2,
		Underline = 4,
		Strikeout = 8
	}

	public class FontFamily
	{
		public FontFamily(string name)
		{
			this.name = name;
		}
		public string Name { get { return name; } }
		string name;
	}

	public sealed class Font : IDisposable
	{
		static bool GetMonospaced(string name)
		{
			return !monospaced_disable_set.Contains(name);
		}
		static readonly System.Collections.Generic.HashSet<string> monospaced_disable_set = 
			new System.Collections.Generic.HashSet<string>
		{
			"ＭＳ Ｐゴシック",
			"MS PGothic",
		};

		public Font(string familyName, float emSize, FontStyle style, 
			GraphicsUnit unit)
		{
			fontFamily = new FontFamily(familyName);
			monospaced = GetMonospaced(familyName);
			size = emSize;
			fontStyle = style;
			graphicsUnit = unit;
		}
		public Font(string familyName, float emSize, FontStyle style, 
			GraphicsUnit unit, byte gdiCharSet)
		{
			fontFamily = new FontFamily(familyName);
			monospaced = GetMonospaced(familyName);
			size = emSize;
			fontStyle = style;
			graphicsUnit = unit;
		}
		public Font(string familyName, float emSize, FontStyle style,
			GraphicsUnit unit, byte gdiCharSet, bool gdiVericalFont)
		{
			fontFamily = new FontFamily(familyName);
			monospaced = GetMonospaced(familyName);
			size = emSize;
			fontStyle = style;
			graphicsUnit = unit;
		}

		public void Dispose()
		{ }

		public FontFamily FontFamily { get { return fontFamily; } }
		FontFamily fontFamily;

		public bool Monospaced { get { return monospaced; } }
		bool monospaced = true;

		public float Size { get { return size; } }
		float size;

		public FontStyle Style { get { return fontStyle; } }
		FontStyle fontStyle;

		public bool Bold { get { return (fontStyle & FontStyle.Bold) > 0; } }
		public bool Italic { get { return (fontStyle & FontStyle.Italic) > 0; } }
		public bool Underline { get { return (fontStyle & FontStyle.Underline) > 0; } }
		public bool Strikeout { get { return (fontStyle & FontStyle.Strikeout) > 0; } }

		public GraphicsUnit Unit { get { return graphicsUnit; } }
		GraphicsUnit graphicsUnit;
	}

	public struct Color
	{
		public static Color FromArgb(int argb)
		{
			return FromArgb(
					((argb >> 24) & 0xFF),
					((argb >> 16) & 0xFF),
					((argb >> 8) & 0xFF),
					(argb & 0xFF));
		}
		//public static Color FromArgb(int alpha, Color baseColor);
		public static Color FromArgb(int red, int green, int blue)
		{
			return FromArgb(255, red, green, blue);
		}
		public static Color FromRgbInt(int rgb)
		{
			return FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
		}
		public static Color FromArgb(int alpha, int red, int green, int blue)
		{
			return new Color
			{
				a = alpha / 255.0f,
				r = red / 255.0f,
				g = green / 255.0f,
				b = blue / 255.0f,
			};
		}

		public Color(int R, int G, int B)
		{
			r = R / 255.0f;
			g = G / 255.0f;
			b = B / 255.0f;
			a = 1.0f;
		}
		public Color(int R, int G, int B, int A)
		{
			r = R / 255.0f;
			g = G / 255.0f;
			b = B / 255.0f;
			a = A / 255.0f;
		}
		public Color(float R, float G, float B, float A)
		{
			r = R;
			g = G;
			b = B;
			a = A;
		}

		//public Color(uColor c) { a = c.a; r = c.r; g = c.g; b = c.b; }
		public int R { get { return (int)(r * 255); } }
		public int G { get { return (int)(g * 255); } }
		public int B { get { return (int)(b * 255); } }
		public int A { get { return (int)(a * 255); } }
		public int ToArgb()
		{
			return (A << 24) + (R << 16) + (G << 8) + B;
		}
		public int ToRGBA()
		{
			return (R << 24) + (G << 16) + (B << 8) + A;
		}

		// 企业级说明：Godot 侧渲染热路径统一通过该入口转换颜色，避免各模块重复手写通道顺序。
		public global::Godot.Color ToGodotColor()
		{
			return new global::Godot.Color(r, g, b, a);
		}

		public float a;
		public float r;
		public float g;
		public float b;

		public static readonly Color Black = new Color(0, 0, 0);
		public static readonly Color White = new Color(255, 255, 255);
		public static readonly Color Blue = new Color(0, 0, 255);
		public static readonly Color Red = new Color(255, 0, 0);
		public static readonly Color Green = new Color(0, 255, 0);
		public static readonly Color Grey = new Color(128, 128, 128);
		public static readonly Color Gray = Grey;
		public static readonly Color Transparent = new Color(0, 0, 0, 0);

		//public static Color Clear { get { return new Color(uColor.clear); } }
		//public static Color Cyan { get { return new Color(uColor.cyan); } }
		//public static Color Magenta { get { return new Color(uColor.magenta); } }
		//public static Color Yellow { get { return new Color(uColor.yellow); } }
		public static Color FromName(string name)
		{
			string key = (name ?? "").Trim().ToLowerInvariant();
			switch(key)
			{
			case "black":
				return Black;
			case "blue":
				return Blue;
		//    case "Clear":
		//        return Clear;
			case "aqua":
			case "cyan":
				return new Color(0x00, 0xFF, 0xFF);
			case "gray":
				return Gray;
			case "green":
				return Green;
			case "grey":
				return Grey;
			case "fuchsia":
			case "magenta":
				return new Color(0xFF, 0x00, 0xFF);
			case "red":
				return Red;
			case "white":
				return White;
			case "yellow":
				return new Color(0xFF, 0xFF, 0x00);
			case "aquamarine":
				return new Color(0x7F, 0xFF, 0xD4);
			case "chocolate":
				return new Color(0xD2, 0x69, 0x1E);
			case "darkturquoise":
				return new Color(0x00, 0xCE, 0xD1);
			case "deepskyblue":
				return new Color(0x00, 0xBF, 0xFF);
			case "dimgray":
			case "dimgrey":
				return new Color(0x69, 0x69, 0x69);
			case "dodgerblue":
				return new Color(0x1E, 0x90, 0xFF);
			case "gold":
				return new Color(0xFF, 0xD7, 0x00);
			case "hotpink":
				return new Color(0xFF, 0x69, 0xB4);
			case "lawngreen":
				return new Color(0x7C, 0xFC, 0x00);
			case "lightgray":
			case "lightgrey":
				return new Color(0xD3, 0xD3, 0xD3);
			case "lime":
				return new Color(0x00, 0xFF, 0x00);
			case "limegreen":
				return new Color(0x32, 0xCD, 0x32);
			case "midnightblue":
				return new Color(0x19, 0x19, 0x70);
			case "orange":
				return new Color(0xFF, 0xA5, 0x00);
			case "pink":
				return new Color(0xFF, 0xC0, 0xCB);
			case "plum":
				return new Color(0xDD, 0xA0, 0xDD);
			case "royalblue":
				return new Color(0x41, 0x69, 0xE1);
			case "tomato":
				return new Color(0xFF, 0x63, 0x47);
			}
			// megaten 门控（P4）：会话声明 ui.standard-color-name.v1 时，颜色名解析改用
			// System.Drawing 标准调色板（参考实现用 Color.FromName，认识全部 141 个标准名）。
			// 未声明的会话保持上面的 32 名表与 Black 回退，逐点等价于基线。
			if (MinorShift.Emuera.Program.Compatibility.Megaten.UsesStandardColorNameTable
				&& StandardColorNames.TryGet(key, out Color standard))
				return standard;
			uEmuera.Logger.Warn(EmueraLogCategory.UI, () => "Not Match Color '" + name + "'");
			return Black;
		}

		//public uColor ucolor { get { return new uColor(r, g, b, a); } }

		public static bool operator ==(Color left, Color right)
		{
			return  left.A == right.A &&
					left.R == right.R &&
					left.G == right.G &&
					left.B == right.B;
		}
		public static bool operator !=(Color left, Color right)
		{
			return  left.A != right.A ||
					left.R != right.R ||
					left.G != right.G ||
					left.B != right.B;
		}
		public override bool Equals(object obj)
		{
			if(!(obj is Color))
				return false;
			return ((Color)obj) == this;
		}
		public override int GetHashCode()
		{
			return 0;
		}
	}

	public struct Point
	{

		public static readonly Point Empty = new Point(0, 0);

		public Point(Size size)
		{
			X = size.Width;
			Y = size.Height;
		}
		public Point(int x, int y)
		{
			X = x;
			Y = y;
		}
		public int X { get; set; }
		public int Y { get; set; }
		public void Offset(Point pt)
		{
			X += pt.X;
			Y += pt.Y;
		}
		public bool IsEmpty
		{
			get { return X == 0 && Y == 0; }
		}
	}

	public struct Size
	{
		public static readonly Size zero;

		public Size(Point pt)
		{
			Width = pt.X;
			Height = pt.Y;
		}
		public Size(int width, int height)
		{
			Width = width;
			Height = height;
		}

		public int Width { get; set; }
		public int Height { get; set; }
		public bool IsEmpty
		{
			get { return Width == 0 && Height == 0; }
		}
	}

	public struct Rectangle
	{
		public static Rectangle Intersect(Rectangle left, Rectangle right)
		{
			int l = Math.Max(left.Left, right.Left);
			int r = Math.Min(left.Right, right.Right);
			int t = Math.Max(left.Top, right.Top);
			int b = Math.Min(left.Bottom, right.Bottom);
			if(l < r && t < b)
				return new Rectangle(l, t, r - l, b - t);
			else
				return new Rectangle(0, 0, 0, 0);
		}

		public Rectangle(Point location, Size size)
		{
			X = location.X;
			Y = location.Y;
			Width = size.Width;
			Height = size.Height;
		}
		//public Rectangle(Point location, Vector2 size)
		//{
		//    X = location.X;
		//    Y = location.Y;
		//    Width = (int)size.x;
		//    Height = (int)size.y;
		//}
		public Rectangle(int x, int y, int width, int height)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
		}

		public int X { get; set; }
		public int Y { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }

		public int Top { get { return Y; } }
		public int Bottom { get { return Y + Height; } }
		public int Left { get { return X; } }
		public int Right { get { return X + Width; } }

		public Size Size { get { return new Size(Width, Height); } }
		public bool IsEmpty { get { return Width == 0 && Height == 0; } }

		public bool Contains(Point point)
		{
			return Left <= point.X && point.X < Right &&
				Top <= point.Y && point.Y < Bottom;
		}
		public bool IntersectsWith(Rectangle rect)
		{
			return !(rect.Bottom <= Top ||
					rect.Top > Bottom ||
					rect.Right <= Left ||
					rect.Left > Right);
		}
	}

	public struct RectangleF
	{
		public RectangleF(Point location, Size size)
		{
			X = location.X;
			Y = location.Y;
			Width = size.Width;
			Height = size.Height;
		}
		//public RectangleF(Point location, Vector2 size)
		//{
		//    X = location.X;
		//    Y = location.Y;
		//    Width = (int)size.x;
		//    Height = (int)size.y;
		//}
		public RectangleF(int x, int y, int width, int height)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
		}
		public RectangleF(float x, float y, float width, float height)
		{
			X = x;
			Y = y;
			Width = width;
			Height = height;
		}

		public float X { get; set; }
		public float Y { get; set; }
		public float Width { get; set; }
		public float Height { get; set; }

		public float Top { get { return Y; } }
		public float Bottom { get { return Y + Height; } }
		public float Left { get { return X; } }
		public float Right { get { return X + Width; } }
	}

	public class ImageAttributes
	{ }

	public enum StringFormatFlags
	{
		DirectionRightToLeft = 1,
		DirectionVertical = 2,
		FitBlackBox = 4,
		DisplayFormatControl = 32,
		NoFontFallback = 1024,
		MeasureTrailingSpaces = 2048,
		NoWrap = 4096,
		LineLimit = 8192,
		NoClip = 16384
	}

	public class StringFormat
	{

	}

	//public class Bitmap
	//{ }

	public struct CharacterRange
	{
		public CharacterRange(int first, int length)
		{
			First = first;
			Length = length;
		}

		public int First { get; set; }
		public int Length { get; set; }

		//public override bool Equals(object obj);
		//public override int GetHashCode();

		//public static bool operator ==(CharacterRange cr1, CharacterRange cr2);
		//public static bool operator !=(CharacterRange cr1, CharacterRange cr2);
	}
}
