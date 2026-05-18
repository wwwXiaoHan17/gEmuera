using Godot;

public partial class EmueraImage : Control
{
	private Texture2D _sourceTexture;
	private Rect2 _sourceRegion;
	private Vector2 _drawOffset;
	private bool _flipX;
	private bool _flipY;

	public Texture2D SourceTexture
	{
		get => _sourceTexture;
		set
		{
			_sourceTexture = value;
			QueueRedraw();
		}
	}

	public Rect2 SourceRegion
	{
		get => _sourceRegion;
		set
		{
			_sourceRegion = value;
			QueueRedraw();
		}
	}

	public Vector2 DrawOffset
	{
		get => _drawOffset;
		set
		{
			_drawOffset = value;
			QueueRedraw();
		}
	}

	public bool FlipX
	{
		get => _flipX;
		set
		{
			_flipX = value;
			QueueRedraw();
		}
	}

	public bool FlipY
	{
		get => _flipY;
		set
		{
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
			Material = null;
			return;
		}
		if (Material is ShaderMaterial sm && sm.Shader == ColorMatrixGPU.Shader)
		{
			ColorMatrixGPU.SetMatrixUniforms(sm, cm);
		}
		else
		{
			Material = ColorMatrixGPU.CreateMaterial(cm);
		}
	}

	public EmueraImage()
	{
		TextureFilter = TextureFilterEnum.Nearest;
	}

	public override void _Ready()
	{
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (SourceTexture == null) return;

		var destRect = new Rect2(DrawOffset.X, DrawOffset.Y, Size.X, Size.Y);

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
