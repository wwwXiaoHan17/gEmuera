using Godot;

// Lightweight CanvasItem used for CBG/image presentation. It owns draw parameters
// only; texture lifetime remains in SpriteManager/EmueraContent so cache eviction
// and UI ownership stay centralized.
public partial class EmueraImage : Control
{
	public readonly struct ImageSourceState
	{
		public readonly Texture2D Texture;
		public readonly Rect2 Region;
		public readonly string AnimatedWebpPath;
		public readonly Vector2 DrawOffset;
		public readonly Vector2 DrawSize;

		public ImageSourceState(Texture2D texture, Rect2 region, string animatedWebpPath, Vector2 drawOffset, Vector2 drawSize)
		{
			Texture = texture;
			Region = region;
			AnimatedWebpPath = animatedWebpPath;
			DrawOffset = drawOffset;
			DrawSize = drawSize;
		}

		public bool IsAvailable => Texture != null || !string.IsNullOrWhiteSpace(AnimatedWebpPath);
	}

	string animatedWebpPath;
	AnimatedWebpFrameSequence animatedWebpFrames;
	Texture2D animatedWebpFrameTexture;
	int animatedWebpFrameIndex;
	double animatedWebpFrameElapsed;
	bool animatedWebpPaused;
	ImageSourceState normalButtonSource;
	ImageSourceState selectedButtonSource;
	bool hasButtonSources;
	bool hasSelectedButtonSource;
	bool buttonSelected;
	private Texture2D _sourceTexture;
	private Rect2 _sourceRegion;
	private Vector2 _drawOffset;
	private Vector2 _drawSize;
	private bool _flipX;
	private bool _flipY;
	// Last applied ColorMatrix key. Avoiding redundant Material assignment reduces
	// CanvasItem invalidation when animated CBG layers refresh with the same matrix.
	private ulong _colorMatrixKey;
	private bool _hasColorMatrixKey;

	public Texture2D SourceTexture
	{
		get => _sourceTexture;
		set
		{
			if (_sourceTexture == value)
				return;
			_sourceTexture = value;
			QueueRedraw();
		}
	}

	public bool HasAnimatedWebpSource => !string.IsNullOrEmpty(animatedWebpPath);

	public Rect2 SourceRegion
	{
		get => _sourceRegion;
		set
		{
			if (_sourceRegion == value)
				return;
			_sourceRegion = value;
			QueueRedraw();
		}
	}

	public Vector2 DrawOffset
	{
		get => _drawOffset;
		set
		{
			if (_drawOffset == value)
				return;
			_drawOffset = value;
			QueueRedraw();
		}
	}

	public Vector2 DrawSize
	{
		get => _drawSize;
		set
		{
			if (_drawSize == value)
				return;
			_drawSize = value;
			QueueRedraw();
		}
	}

	public bool FlipX
	{
		get => _flipX;
		set
		{
			if (_flipX == value)
				return;
			_flipX = value;
			QueueRedraw();
		}
	}

	public bool FlipY
	{
		get => _flipY;
		set
		{
			if (_flipY == value)
				return;
			_flipY = value;
			QueueRedraw();
		}
	}

	/// <summary>
	/// Set the ColorMatrix shader material for display-time color transformation.
	/// Pass null to remove the shader. The material is applied as CanvasItem.Material.
	/// </summary>
	public void SetColorMatrix(float[][] cm)
	{
		if (cm == null)
		{
			if (!_hasColorMatrixKey && Material == null)
				return;
			Material = null;
			_hasColorMatrixKey = false;
			return;
		}
		ulong matrixKey = ColorMatrixGPU.GetMatrixKey(cm);
		if (_hasColorMatrixKey && _colorMatrixKey == matrixKey && Material is ShaderMaterial sm && sm.Shader == ColorMatrixGPU.Shader)
			return;

		// Shared materials are treated as read-only. A new key selects a different
		// cached ShaderMaterial instead of mutating uniforms that may be used by
		// other visible EmueraImage nodes.
		Material = ColorMatrixGPU.GetSharedMaterial(cm, matrixKey);
		_colorMatrixKey = matrixKey;
		_hasColorMatrixKey = true;
	}

	public EmueraImage()
	{
		TextureFilter = TextureFilterEnum.Nearest;
	}

	public override void _Ready()
	{
		QueueRedraw();
	}

	// HTML 的 src/srcb 共用同一个 EmueraImage。悬浮时只切换显示源，裁剪、翻转、
	// ColorMatrix 与节点层级保持不变，和原 Emuera 的 ConsoleImagePart 语义一致。
	public void ConfigureButtonSources(ImageSourceState normalSource, ImageSourceState selectedSource, bool hasSelectedSource)
	{
		normalButtonSource = normalSource;
		selectedButtonSource = selectedSource;
		hasButtonSources = normalSource.IsAvailable;
		hasSelectedButtonSource = hasSelectedSource && selectedSource.IsAvailable;
		buttonSelected = false;
		if (hasButtonSources)
			ApplyImageSource(normalButtonSource);
	}

	public void SetSelected(bool selected)
	{
		if (!hasButtonSources || buttonSelected == selected)
			return;
		buttonSelected = selected;
		ApplyImageSource(selected && hasSelectedButtonSource ? selectedButtonSource : normalButtonSource);
	}

	public void SetImageSource(ImageSourceState source)
	{
		ApplyImageSource(source);
	}

	void ApplyImageSource(ImageSourceState source)
	{
		// 切回静态图时先写入新纹理，再释放旧动画引用，避免清理旧动画帧时把新图擦掉。
		// 切向动画时则保留当前静态图作为解码期间的占位，首帧上传后会自动替换。
		if (source.Texture != null)
		{
			SourceTexture = source.Texture;
			SourceRegion = source.Region;
		}
		else if (string.IsNullOrWhiteSpace(source.AnimatedWebpPath))
		{
			SourceTexture = null;
			SourceRegion = default;
		}
		DrawOffset = source.DrawOffset;
		DrawSize = source.DrawSize;
		SetAnimatedWebpSource(source.AnimatedWebpPath);
	}

	// 动画帧直接替换本 Control 的 SourceTexture。这样裁剪、翻转、ColorMatrix 和层级
	// 全部继续由 EmueraImage 负责，避免额外子节点改变 HTML/Canvas/CBG 的显示契约。
	public void SetAnimatedWebpSource(string sourcePath)
	{
		if (string.Equals(animatedWebpPath, sourcePath, System.StringComparison.OrdinalIgnoreCase))
			return;
		ClearAnimatedWebpSource();
		if (string.IsNullOrWhiteSpace(sourcePath))
			return;

		animatedWebpPath = sourcePath;
		animatedWebpPaused = false;
		string requestedPath = sourcePath;
		if (!AnimatedWebpSpriteFrames.Acquire(sourcePath, frames => OnAnimatedWebpFirstFrameReady(requestedPath, frames)))
		{
			animatedWebpPath = null;
			return;
		}
		SetProcess(true);
	}

	public void SetAnimatedWebpPaused(bool paused)
	{
		animatedWebpPaused = paused;
	}

	void OnAnimatedWebpFirstFrameReady(string requestedPath, AnimatedWebpFrameSequence frames)
	{
		if (!GodotObject.IsInstanceValid(this)
			|| !string.Equals(animatedWebpPath, requestedPath, System.StringComparison.OrdinalIgnoreCase)
			|| frames == null)
			return;
		animatedWebpFrames = frames;
		animatedWebpFrameIndex = 0;
		animatedWebpFrameElapsed = 0.0;
		ApplyAnimatedWebpFrame();
	}

	void ClearAnimatedWebpSource()
	{
		Texture2D oldFrameTexture = animatedWebpFrameTexture;
		animatedWebpFrames = null;
		animatedWebpFrameTexture = null;
		animatedWebpFrameIndex = 0;
		animatedWebpFrameElapsed = 0.0;
		animatedWebpPaused = false;
		// 调用方复用节点切换到静态图时，静态 SourceTexture 已经先写入；只有仍指向
		// 旧动画帧时才清空，避免 Clear 把即将显示的新静态图一起擦掉。
		if (_sourceTexture == oldFrameTexture)
		{
			_sourceTexture = null;
			_sourceRegion = default;
		}
		string oldPath = animatedWebpPath;
		animatedWebpPath = null;
		if (!string.IsNullOrEmpty(oldPath))
			AnimatedWebpSpriteFrames.Release(oldPath);
		SetProcess(false);
		QueueRedraw();
	}

	void ApplyAnimatedWebpFrame()
	{
		if (animatedWebpFrames == null
			|| !animatedWebpFrames.TryGetFrame(animatedWebpFrameIndex, out Texture2D frame, out _)
			|| frame == null)
			return;
		if (_sourceTexture == frame && _sourceRegion.Size == Vector2.Zero)
			return;
		animatedWebpFrameTexture = frame;
		_sourceTexture = frame;
		_sourceRegion = default;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (animatedWebpFrames == null || animatedWebpPaused || !Visible)
			return;
		if (!animatedWebpFrames.TryGetFrame(animatedWebpFrameIndex, out _, out double frameDuration))
		{
			animatedWebpFrameIndex = 0;
			animatedWebpFrameElapsed = 0.0;
			return;
		}

		animatedWebpFrameElapsed += delta;
		// 一帧内最多跳过 8 帧，避免窗口恢复后用长时间积累做无界循环；下一帧会继续追赶。
		for (int advances = 0; advances < 8 && animatedWebpFrameElapsed >= frameDuration; advances++)
		{
			animatedWebpFrameElapsed -= frameDuration;
			int frameCount = animatedWebpFrames.FrameCount;
			if (frameCount <= 0)
				return;
			animatedWebpFrameIndex = (animatedWebpFrameIndex + 1) % frameCount;
			ApplyAnimatedWebpFrame();
			if (!animatedWebpFrames.TryGetFrame(animatedWebpFrameIndex, out _, out frameDuration))
				return;
		}
	}

	public override void _ExitTree()
	{
		ClearAnimatedWebpSource();
		base._ExitTree();
	}

	public override void _Draw()
	{
		if (SourceTexture == null) return;

		var drawSize = DrawSize.X > 0 && DrawSize.Y > 0 ? DrawSize : Size;
		var destRect = new Rect2(DrawOffset.X, DrawOffset.Y, drawSize.X, drawSize.Y);

		bool flip = FlipX || FlipY;
		if (flip)
		{
			var center = destRect.Position + destRect.Size / 2;
			DrawSetTransform(center, 0, new Vector2(FlipX ? -1 : 1, FlipY ? -1 : 1));
			destRect.Position = -destRect.Size / 2;
		}

		if (SourceRegion.Size.X > 0 && SourceRegion.Size.Y > 0)
		{
			DrawTextureRectRegion(SourceTexture, destRect, SourceRegion);
		}
		else
		{
			DrawTextureRect(SourceTexture, destRect, false);
		}

		if (flip)
			DrawSetTransform(Vector2.Zero, 0, Vector2.One);
	}
}
