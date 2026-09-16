// EmueraConsole.Cbg.cs —— 承载客户端背景图层（CBG）提交/刷新功能域，自 EmueraConsole.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;
using System.Threading;
using Godot;
using MinorShift._Library;
using MinorShift.Emuera.Sub;
using MinorShift.Emuera.GameData;
using MinorShift.Emuera.GameProc;
//using System.Drawing.Imaging;
//using MinorShift.Emuera.Forms;
using MinorShift.Emuera.Content;
using MinorShift.Emuera.GameData.Expression;
using MinorShift.Emuera.GameProc.Function;
using uEmuera.Forms;
using uEmuera.Drawing;
using uEmuera.Window;

namespace MinorShift.Emuera.GameView
{
	internal sealed partial class EmueraConsole :IDisposable
	{
		private readonly object cbgLock = new object();
		private readonly List<ClientBackGroundImage> cbgList = new List<ClientBackGroundImage>();
		// CBG 列表变化与 GraphicsImage 像素变化都可能要求 Godot 重提背景层。
		// 该 revision 只描述展示快照，不参与 ERB 可见状态，避免普通文本刷新重复提交静态背景。
		private int cbgPresentationRevision;
		private GraphicsImage cbgButtonMap = null;
		private int selectingCBGButtonInt = -1;
		private int lastSelectingCBGButtonInt = -1;
		//ConsoleButtonString selectingButton = null;
		//ConsoleButtonString lastSelectingButton = null;
		public class ClientBackGroundImage : IComparable<ClientBackGroundImage>
		{
			/// <summary>
			/// zdepth == 0は文字列用ダミーなので他で使ってはいけない
			/// </summary>
			/// <param name="zdepth"></param>
			internal ClientBackGroundImage(int zdepth)
			{ this.zdepth = zdepth; }
			public ASprite Img = null;
			public ASprite ImgB = null;
			public int x;
			public int y;
			public int width;
			public int height;
			public float opacity = 1.0f;
			public float[][] colorMatrix = null;
			public bool followScroll = false;
			public int initialScrollY = int.MinValue;
			public bool isSnakeImageLayer = false;
			public long snakeImageDepth = 0;
			public string snakeImageName = null;
			public readonly int zdepth;
			public bool isButton = false;
			public int buttonValue;
			public string tooltipString = null;
			internal long ObservedImageDisplayRevision = long.MinValue;
			internal long ObservedButtonImageDisplayRevision = long.MinValue;
			public int CompareTo(ClientBackGroundImage other)
			{
				if (other == null)
					return -1;
				if (isSnakeImageLayer && other.isSnakeImageLayer)
				{
					int snakeDepthOrder = -snakeImageDepth.CompareTo(other.snakeImageDepth);
					if (snakeDepthOrder != 0)
						return snakeDepthOrder;
				}
				//逆順でSort
				return -zdepth.CompareTo(other.zdepth);
			}
		}
		public void CBG_Clear()
		{
			bool changed = false;
			lock (cbgLock)
			{
				for(var i=0; i<cbgList.Count; ++i)
				{
					ClientBackGroundImage cimg = cbgList[i];
					if (cimg.isSnakeImageLayer)
						continue;
					//使い捨て無名Imageを一応disposeしておく
					if (cimg.Img != null && cimg.Img.Name.Length == 0)
						cimg.Img.Dispose();
					cbgList.RemoveAt(i);
					i--;
					changed = true;
				}
				changed |= ClearCbgButtonMapState();
				cbgList.Add(new ClientBackGroundImage(0));
				cbgList.Sort();
				changed = true;
			}
			if (changed)
				RequestCbgRefresh();
		}

		public void CBG_ClearRange(int zmin, int zmax)
		{
			if (zmin > zmax)
				return;
			bool changed = false;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count;i++)
				{
					ClientBackGroundImage cimg = cbgList[i];
					// Snake 的 SETIMAGELAYER 在原实现里由独立 ImageLayerManager 管理。
					// Godot 版复用 CBG 列表渲染时，CBGREMOVERANGE 仍只能影响 CBG 自己的层。
					if (cimg.isSnakeImageLayer || cimg.zdepth < zmin || cimg.zdepth > zmax || cimg.zdepth == 0)//0はダミーなので削除しない
						continue;

					//使い捨て無名Imageを一応disposeしておく
					if (cimg.Img != null && cimg.Img.Name.Length == 0)
						cimg.Img.Dispose();
					cbgList.RemoveAt(i);
					i--;
					changed = true;
				}
			}
			if (changed)
				RequestCbgRefresh();
		}

		public void CBG_ClearButton()
		{
			bool changed = false;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					ClientBackGroundImage cimg = cbgList[i];
					if (!cimg.isButton)
						continue;

					//使い捨て無名Imageを一応disposeしておく
					if (cimg.Img != null && cimg.Img.Name.Length == 0)
						cimg.Img.Dispose();
					cbgList.RemoveAt(i);
					i--;
					changed = true;
				}
				changed |= ClearCbgButtonMapState();
			}
			if (changed)
				RequestCbgRefresh();
		}

		public void CBG_ClearBMap()
		{
			bool changed;
			lock (cbgLock)
				changed = ClearCbgButtonMapState();
			if (changed)
				RequestCbgRefresh();
		}
		public List<ClientBackGroundImage> GetCBGList()
		{
			lock (cbgLock)
				return new List<ClientBackGroundImage>(cbgList);
		}

		/// <summary>
		/// 仅在 CBG 结构、GraphicsImage 像素或逐帧 SpriteAnime 发生变化时复制图层列表。
		/// Window.Update 会随地图文本高频调用本入口；静态背景不能因此在 Android 每帧重新 pin 纹理并失效 CanvasItem。
		/// </summary>
		public bool TryGetCBGListSnapshot(int knownRevision, out int revision, out List<ClientBackGroundImage> snapshot)
		{
			lock (cbgLock)
			{
				if (HasCbgDisplayContentChangedLocked())
					Interlocked.Increment(ref cbgPresentationRevision);

				revision = Volatile.Read(ref cbgPresentationRevision);
				if (revision == knownRevision)
				{
					snapshot = null;
					return false;
				}

				snapshot = new List<ClientBackGroundImage>(cbgList);
				return true;
			}
		}

		bool HasCbgDisplayContentChangedLocked()
		{
			bool changed = false;
			for (int i = 0; i < cbgList.Count; i++)
			{
				var layer = cbgList[i];
				if (layer == null)
					continue;

				// SpriteAnime 的当前帧由绘制时钟决定，无法只依赖列表结构 revision。
				// 保持每次已有显示刷新都可提交新帧，不能因为去重而冻结动画。
				if (layer.Img is SpriteAnime || layer.ImgB is SpriteAnime)
					return true;

				changed |= UpdateObservedCbgGraphicsRevision(layer.Img, ref layer.ObservedImageDisplayRevision);
				changed |= UpdateObservedCbgGraphicsRevision(layer.ImgB, ref layer.ObservedButtonImageDisplayRevision);
			}
			return changed;
		}

		static bool UpdateObservedCbgGraphicsRevision(ASprite sprite, ref long observedRevision)
		{
			if (sprite is not ASpriteSingle single || single.BaseImage is not GraphicsImage graphics)
				return false;

			long revision = graphics.DisplayRevision;
			if (observedRevision == revision)
				return false;
			observedRevision = revision;
			return true;
		}

		private bool ClearCbgButtonMapState()
		{
			bool changed = cbgButtonMap != null || selectingCBGButtonInt != -1 || lastSelectingCBGButtonInt != -1;
			cbgButtonMap = null;
			selectingCBGButtonInt = -1;
			lastSelectingCBGButtonInt = -1;
			return changed;
		}

		private void RequestCbgRefresh()
		{
			// CBG/SETIMAGELAYER 只改背景列表时可能没有文本输出触发刷新。
			// 这里只唤醒 uEmuera 窗口，实际 Godot 节点重建仍由 Window.Update 合并到下一帧执行。
			Interlocked.Increment(ref cbgPresentationRevision);
			window?.Refresh();
		}

		public bool CBG_SetGraphics(GraphicsImage gra, int x, int y, int zdepth, int width = 0, int height = 0, float opacity = 1.0f, float[][] colorMatrix = null)
		{
			if (gra == null || !gra.IsCreated)
				return false;
			return CBG_SetImage(new SpriteG("", gra, new Rectangle(0, 0, gra.Width, gra.Height)), x, y, zdepth, width, height, opacity, colorMatrix);
		}
		public bool CBG_SetImage(ASprite image, int x, int y, int zdepth, int width = 0, int height = 0, float opacity = 1.0f, float[][] colorMatrix = null)
		{
			if (image == null || !image.IsCreated)
				return false;
			if (zdepth == 0)
				throw new ArgumentOutOfRangeException();
			lock (cbgLock)
			{
				ClientBackGroundImage cbg = new ClientBackGroundImage(zdepth);
				cbg.Img = image;
				cbg.x = x;
				cbg.y = y;
				cbg.width = width;
				cbg.height = height;
				cbg.opacity = clampOpacity(opacity);
				cbg.colorMatrix = colorMatrix;
				//cbg.zdepth = zdepth;
				cbgList.Add(cbg);
				cbgList.Sort();
			}
			RequestCbgRefresh();
			return true;
		}

		public void AddBackgroundImage(string name, long depth, float opacity)
		{
			ASprite sprite = GetSnakeSprite(name);
			if (sprite == null || !sprite.IsCreated)
				return;
			int zdepth = normalizeSnakeDepth(depth);
			lock (cbgLock)
			{
				ClientBackGroundImage cbg = new ClientBackGroundImage(zdepth);
				cbg.Img = sprite;
				cbg.opacity = clampOpacity(opacity);
				cbg.snakeImageName = name;
				cbgList.Add(cbg);
				cbgList.Sort();
			}
			RequestCbgRefresh();
		}

		public void ClearBackgroundImage()
		{
			bool changed = false;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					ClientBackGroundImage cimg = cbgList[i];
					if (cimg.zdepth == 0)
						continue;
					if (cimg.isSnakeImageLayer)
						continue;
					cbgList.RemoveAt(i);
					i--;
					changed = true;
				}
			}
			if (changed)
				RequestCbgRefresh();
		}

		public void RemoveBackground(string key)
		{
			if (string.IsNullOrEmpty(key))
				return;
			bool changed = false;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					ClientBackGroundImage cimg = cbgList[i];
					if (cimg.zdepth == 0 || cimg.isSnakeImageLayer)
						continue;
					string name = cimg.snakeImageName ?? cimg.Img?.Name;
					if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
						continue;
					cbgList.RemoveAt(i);
					i--;
					changed = true;
				}
			}
			if (changed)
				RequestCbgRefresh();
		}

		public void SetImageLayer(string spriteName, long depth, int x, int y, int width, int height, int opacity, float[][] colorMatrix, bool followScroll)
		{
			ASprite sprite = GetSnakeSprite(spriteName);
			if (sprite == null || !sprite.IsCreated)
				return;
			int zdepth = normalizeSnakeDepth(depth);
			bool changed = false;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					// SETIMAGELAYER 的脚本可见 depth 是 long，允许 0 和 -1 同时存在。
					// CBG 渲染层内部保留 zdepth==0 作为文字哑元，因此这里只能用原始 depth 做逻辑匹配。
					if (cbgList[i].isSnakeImageLayer && cbgList[i].snakeImageDepth == depth)
					{
						cbgList.RemoveAt(i);
						i--;
						changed = true;
					}
				}
				ClientBackGroundImage cbg = new ClientBackGroundImage(zdepth);
				cbg.Img = sprite;
				cbg.x = x;
				cbg.y = y;
				cbg.width = width;
				cbg.height = height;
				cbg.opacity = clampOpacity(opacity / 255.0f);
				cbg.colorMatrix = colorMatrix;
				cbg.followScroll = followScroll;
				cbg.isSnakeImageLayer = true;
				cbg.snakeImageDepth = depth;
				cbg.snakeImageName = spriteName;
				cbgList.Add(cbg);
				cbgList.Sort();
				changed = true;
			}
			if (changed)
				RequestCbgRefresh();
		}

		public void ClearImageLayer(long depth)
		{
			bool changed = false;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					// SETIMAGELAYER 的脚本可见 depth 是 long，允许 0 和 -1 同时存在。
					// CBG 渲染层内部保留 zdepth==0 作为文字哑元，因此这里只能用原始 depth 做逻辑匹配。
					if (cbgList[i].isSnakeImageLayer && cbgList[i].snakeImageDepth == depth)
					{
						cbgList.RemoveAt(i);
						i--;
						changed = true;
					}
				}
			}
			if (changed)
				RequestCbgRefresh();
		}

		public void ClearImageLayerAll()
		{
			bool changed = false;
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					if (cbgList[i].isSnakeImageLayer)
					{
						cbgList.RemoveAt(i);
						i--;
						changed = true;
					}
				}
			}
			if (changed)
				RequestCbgRefresh();
		}

		public bool ExistsImageLayer(long depth)
		{
			lock (cbgLock)
			{
				for (int i = 0; i < cbgList.Count; i++)
				{
					if (cbgList[i].isSnakeImageLayer && cbgList[i].snakeImageDepth == depth)
						return true;
				}
			}
			return false;
		}

		private static int normalizeSnakeDepth(long depth)
		{
			if (depth == 0)
				return -1;
			if (depth > int.MaxValue)
				return int.MaxValue;
			if (depth < int.MinValue)
				return int.MinValue;
			return (int)depth;
		}

		private static float clampOpacity(float opacity)
		{
			if (opacity < 0)
				return 0;
			if (opacity > 1)
				return 1;
			return opacity;
		}

		private static ASprite GetSnakeSprite(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;
			ASprite sprite = AppContents.GetSprite(name);
			if (sprite != null && sprite.IsCreated)
				return sprite;
			string path = ResolveSnakeImagePath(name);
			if (string.IsNullOrEmpty(path))
				return null;
			BitmapTexture bmp = new BitmapTexture(path);
			if (bmp.Width <= 0 || bmp.Height <= 0)
				return null;
			ConstImage img = new ConstImage(path);
			img.CreateFrom(bmp, false);
			if (!img.IsCreated)
				return null;
			return new SpriteF(name, img, new Rectangle(0, 0, bmp.Width, bmp.Height), new Point());
		}

		private static string ResolveSnakeImagePath(string name)
		{
			List<string> candidates = new List<string>();
			candidates.Add(name);
			candidates.Add(Path.Combine(Program.ContentDir ?? "", name));
			candidates.Add(Path.Combine(Program.ExeDir ?? "", name));
			candidates.Add(Path.Combine(Program.ExeDir ?? "", "resources", name));
			if (Path.GetExtension(name).Length == 0)
			{
				string[] exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" };
				foreach (string ext in exts)
				{
					candidates.Add(name + ext);
					candidates.Add(Path.Combine(Program.ContentDir ?? "", name + ext));
					candidates.Add(Path.Combine(Program.ExeDir ?? "", "resources", name + ext));
				}
			}
			foreach (string candidate in candidates)
			{
				string resolved = uEmuera.Utils.ResolveExistingFilePath(candidate);
				if (!string.IsNullOrEmpty(resolved) && uEmuera.Utils.FileExists(resolved))
					return resolved;
			}
			if (!string.IsNullOrEmpty(Program.ContentDir))
			{
				if (Path.GetExtension(name).Length > 0)
					return uEmuera.Utils.FindFileRecursive(Program.ContentDir, name);
				foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" })
				{
					string found = uEmuera.Utils.FindFileRecursive(Program.ContentDir, name + ext);
					if (!string.IsNullOrEmpty(found))
						return found;
				}
			}
			return null;
		}

		public bool CBG_SetButtonMap(GraphicsImage gra)
		{
			if (gra == null || !gra.IsCreated)
				return false;
			if (cbgButtonMap == gra)
				return false;
			cbgButtonMap = gra;
			selectingCBGButtonInt = -1;
			lastSelectingCBGButtonInt = -1;
			RequestCbgRefresh();
			return true;
		}

		public bool CBG_SetButtonImage(int buttonValue, ASprite imageN, ASprite imageB, int x, int y, int zdepth, string tooltip = null)
		{
			if (zdepth == 0)
				throw new ArgumentOutOfRangeException();
			lock (cbgLock)
			{
				ClientBackGroundImage cbg = new ClientBackGroundImage(zdepth);
				cbg.Img = imageN;
				cbg.ImgB = imageB;
				cbg.x = x;
				cbg.y = y;
				//cbg.zdepth = zdepth;
				cbg.isButton = true;
				cbg.buttonValue = buttonValue;
				cbg.tooltipString = tooltip;
				cbgList.Add(cbg);
				cbgList.Sort();
			}
			RequestCbgRefresh();
			return true;
		}
	}
}
