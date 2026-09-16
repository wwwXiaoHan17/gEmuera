// EmueraContent.HtmlGeometry.cs —— 承载 HTML div/img 几何计算功能域，自 EmueraContent.cs 拆出（原因：主文件超 2000 行只减不增约束）。
using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;
using EmuFont = uEmuera.Drawing.Font;
using EmuColor = uEmuera.Drawing.Color;

public partial class EmueraContent : Control
{

	// Resolve div coordinates into the target container's local coordinate space.
	Vector2 GetHtmlDivPosition(ConsoleDivPart div, int relX)
	{
		switch (div.Display)
		{
			case DisplayMode.Absolute:
			case DisplayMode.AbsoluteLeftBottom:
				return new Vector2(div.X, ResolveBottomOriginHtmlY(div.Y, div.DivHeight));
			case DisplayMode.AbsoluteLeftTop:
				return new Vector2(div.X, div.Y);
			default:
				return new Vector2(div.PointX - relX + div.X, div.Y);
		}
	}

	float ResolveBottomOriginHtmlY(int y, int height)
	{
		// Emuera 的 MOUSEY/CLIENTHEIGHT 坐标是底边为 0、向上为负。
		// v24/snake 的浮层会直接把这个负数作为 div 的上边缘；正数 left-bottom
		// 旧用法仍按“距底部高度”处理，避免破坏已有资源。
		if (y < 0)
			return GetContentViewportHeight() + y;
		return GetContentViewportHeight() - y - height;
	}

	bool ShouldAnchorRelativeDivToViewport(ConsoleDivPart div)
	{
		if (div == null || div.Display != DisplayMode.Relative || !div.IsRelative)
			return false;
		// eraTW/snake 的泡茶子菜单没有显式 display='absolute'，但脚本用
		// MOUSEY()-DIV_HEIGHT 得到的是 Emuera 底边原点坐标。它的下边缘仍为负数，
		// 且 depth=-1；普通行内 relative div 不满足这个形态约束，继续按 lineY 相对定位。
		return div.Depth < 0 && div.Y < 0 && div.Y + div.DivHeight < 0;
	}

	Vector2 GetViewportAnchoredRelativeDivPosition(ConsoleDivPart div)
	{
		if (div == null)
			return Vector2.Zero;
		return GetViewportAnchoredRelativeDivPosition(div.X, div.Y, div.DivHeight);
	}

	Vector2 GetViewportAnchoredRelativeDivPosition(int x, int y, int height)
	{
		var viewportPosition = new Vector2(x, ResolveBottomOriginHtmlY(y, height));
		if (scrollContainer == null)
			return viewportPosition;
		var scroll = new Vector2(NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal), scrollContainer.ScrollVertical);
		if (contentScale > 0.001f)
			scroll /= contentScale;
		return scroll + viewportPosition;
	}

	// HTML 的 depth 数值越大越靠后；Godot 的 ZIndex 需要整体抬到 Canvas 绘制面之上。
	// eraFL 的房间框使用 depth=1，若直接映射为负数会被 Canvas 背景盖住，只剩无 depth 的局部遮罩可见。
	static int GetGodotZIndexForHtmlDepth(int depth)
	{
		return System.Math.Max(1, HtmlDivZIndexBase - depth);
	}

	int GetGodotZIndexForHtmlDiv(ConsoleDivPart div)
	{
		// eraTW/snake 的角色列表会在每一行文本之后追加一个 depth=1 的空 div 作为条纹背景。
		// 这类 div 必须压在本行文字和 PRINT_RECT 槽条后面；否则 Godot 的子节点绘制顺序会让背景盖住整行数据。
		// 大尺寸/带子内容/带盒模型的状态栏与浮层 div 仍走正向基准，保留 2026-07-07 修复的跨行覆盖语义。
		if (IsInlineLineBackgroundDiv(div))
			return -1;
		return GetGodotZIndexForHtmlDepth(div?.Depth ?? 0);
	}

	bool IsInlineLineBackgroundDiv(ConsoleDivPart div)
	{
		if (div == null
			|| !div.IsRelative
			|| div.Display != DisplayMode.Relative
			|| div.Depth <= 0
			|| !div.BackgroundColor.HasValue
			|| div.StyledBox != null
			|| div.Y != 0
			|| div.DivHeight <= 0)
			return false;
		if (div.Children != null && div.Children.Length > 0)
			return false;

		int inlineHeightLimit = System.Math.Max(EffectiveLineHeight, Config.FontSize) + 2;
		return div.DivHeight <= inlineHeightLimit;
	}

	// Resolve absolute/relative image placement for HTML-style output.
	Vector2 GetHtmlImagePosition(ConsoleImagePart imagePart, int relX)
	{
		switch (imagePart.Display)
		{
			case DisplayMode.Absolute:
			case DisplayMode.AbsoluteLeftBottom:
				return new Vector2(
					imagePart.PositionX + imagePart.dest_rect.X,
					GetContentViewportHeight() + imagePart.PositionY);
			case DisplayMode.AbsoluteLeftTop:
				return new Vector2(
					imagePart.PositionX + imagePart.dest_rect.X,
					imagePart.PositionY);
			default:
				return new Vector2(
					imagePart.PointX - relX + imagePart.PositionX + imagePart.dest_rect.X,
					imagePart.dest_rect.Y);
		}
	}

	// Apply sprite-origin metadata when HTML images use emuera sprite resources.
	static bool TryGetSpriteHtmlBasePosition(ASprite sprite, string resourceName, out uEmuera.Drawing.Point basePosition)
	{
		basePosition = uEmuera.Drawing.Point.Empty;
		if (sprite != null && !sprite.DestBasePosition.IsEmpty)
		{
			basePosition = sprite.DestBasePosition;
			return true;
		}
		if (AppContents.TryGetSpriteBasePosition(resourceName, out var cachedPosition) && !cachedPosition.IsEmpty)
		{
			basePosition = cachedPosition;
			return true;
		}
		return false;
	}

	static bool ShouldUseSpriteHtmlCanvas(ASpriteSingle single, string resourceName)
	{
		if (single == null || single.DestBaseSize.Width <= 0 || single.DestBaseSize.Height <= 0)
			return false;
		// v24/snake 原核心只有在 DestBasePosition 非零时，才把裁剪图块映射到
		// DestBaseSize 表示的基准画布内部。仅仅 src 尺寸和 DestBaseSize 不同，
		// 仍应把裁剪图块拉伸到调用方给出的目标矩形；否则立绘身体/表情这类
		// 共用基准画布的资源会被错误缩小并分离显示。
		return TryGetSpriteHtmlBasePosition(single, resourceName, out _);
	}

	static Vector2 GetSpriteHtmlDrawOffset(ASprite sprite, string resourceName, int width, int height,
		SpriteAnimeFrameLayoutInfo animeFrameLayout)
	{
		if (width == 0 || height == 0)
			return Vector2.Zero;
		if (sprite == null)
			return Vector2.Zero;

		if (sprite is SpriteAnime && animeFrameLayout.IsValid)
		{
			if (sprite.DestBaseSize.Width == 0 || sprite.DestBaseSize.Height == 0)
				return Vector2.Zero;
			// SpriteAnime 的帧偏移是相对动画基准画布的位置。必须与动画自身的
			// DestBasePosition 合并后缩放，才能保持原生 GraphicsDraw 的布局语义。
			return new Vector2(
				(sprite.DestBasePosition.X + animeFrameLayout.OffsetX) * width / (float)sprite.DestBaseSize.Width,
				(sprite.DestBasePosition.Y + animeFrameLayout.OffsetY) * height / (float)sprite.DestBaseSize.Height);
		}

		if (!TryGetSpriteHtmlBasePosition(sprite, resourceName, out var basePosition))
			return Vector2.Zero;

		if (sprite is ASpriteSingle)
		{
			if (sprite.DestBaseSize.Width == 0 || sprite.DestBaseSize.Height == 0)
				return Vector2.Zero;
			return new Vector2(
				basePosition.X * width / (float)sprite.DestBaseSize.Width,
				basePosition.Y * height / (float)sprite.DestBaseSize.Height);
		}

		if (sprite.DestBaseSize.Width == 0 || sprite.DestBaseSize.Height == 0)
			return Vector2.Zero;
		return new Vector2(
			basePosition.X * width / (float)sprite.DestBaseSize.Width,
			basePosition.Y * height / (float)sprite.DestBaseSize.Height);
	}

	static Vector2 GetSpriteHtmlDrawSize(ASprite sprite, string resourceName, int width, int height,
		SpriteAnimeFrameLayoutInfo animeFrameLayout)
	{
		if (width == 0 || height == 0)
			return new Vector2(width, height);
		if (sprite is SpriteAnime && animeFrameLayout.IsValid
			&& sprite.DestBaseSize.Width > 0 && sprite.DestBaseSize.Height > 0
			&& animeFrameLayout.SourceWidth > 0 && animeFrameLayout.SourceHeight > 0)
		{
			return new Vector2(
				animeFrameLayout.SourceWidth * width / (float)sprite.DestBaseSize.Width,
				animeFrameLayout.SourceHeight * height / (float)sprite.DestBaseSize.Height);
		}
		if (sprite is ASpriteSingle single
			&& ShouldUseSpriteHtmlCanvas(single, resourceName))
		{
			int srcW = single.SrcRectangle.Width;
			int srcH = single.SrcRectangle.Height;
			if (srcW > 0 && srcH > 0)
			{
				return new Vector2(
					srcW * width / (float)sprite.DestBaseSize.Width,
					srcH * height / (float)sprite.DestBaseSize.Height);
			}
		}
		return new Vector2(width, height);
	}

	// Dynamic cut-ins are generated by script and may not exist as files yet.
	static bool IsDynamicCutinName(string name)
	{
		if (string.IsNullOrEmpty(name) || !name.StartsWith("CUTIN", StringComparison.OrdinalIgnoreCase) || name.Length == 5)
			return false;
		for (int i = 5; i < name.Length; i++)
		{
			if (!char.IsDigit(name[i]))
				return false;
		}
		return true;
	}
}
