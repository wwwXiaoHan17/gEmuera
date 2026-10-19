using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// GPU-based ColorMatrix application using canvas_item shader.
/// Provides ShaderMaterial creation and display-time color adjustment.
///
/// Usage:
///   var mat = ColorMatrixGPU.CreateMaterial(cm);
///   emueraImage.Material = mat;
///
/// For non-display (compositing) use on background thread:
///   GraphicsImage.ApplyColorMatrix() handles CPU-side processing.
///   GPU compositing via RenderingDevice is planned for future.
/// </summary>
public static class ColorMatrixGPU
{
	// Shared display materials are immutable after creation and keyed by the exact
	// matrix bits. The hard cap prevents rare script-generated matrix floods from
	// turning ShaderMaterial reuse into an unbounded APK memory sink.
	const int SharedMaterialCacheLimit = 128;
	private static Shader _shader;
	private static Shader _compositShader;
	private static readonly object materialCacheLock = new object();
	private static readonly Dictionary<ulong, ShaderMaterial> sharedMaterialCache = new Dictionary<ulong, ShaderMaterial>();
	private static readonly Dictionary<ulong, LinkedListNode<ulong>> sharedMaterialLruNodes = new Dictionary<ulong, LinkedListNode<ulong>>();
	private static readonly LinkedList<ulong> sharedMaterialLru = new LinkedList<ulong>();

	public static Shader Shader
	{
		get
		{
			if (_shader == null)
			{
				_shader = GD.Load<Shader>("res://scripts/Shaders/color_matrix.gdshader");
			}
			return _shader;
		}
	}

	public static Shader CompositShader
	{
		get
		{
			if (_compositShader == null)
			{
				_compositShader = GD.Load<Shader>("res://scripts/Shaders/color_matrix_composit.gdshader");
			}
			return _compositShader;
		}
	}

	/// <summary>
	/// Create a ShaderMaterial with ColorMatrix uniforms set from a 5x5 float[][].
	/// The material can be assigned to any CanvasItem.Material for display-time application.
	/// </summary>
	public static ShaderMaterial CreateMaterial(float[][] cm)
	{
		var mat = new ShaderMaterial();
		mat.Shader = Shader;
		SetMatrixUniforms(mat, cm);
		return mat;
	}

	/// <summary>
	/// Return an immutable shared material for display-only ColorMatrix usage.
	/// EmueraImage swaps shared materials by matrix key instead of mutating them,
	/// so multiple visible images can safely reuse the same ShaderMaterial.
	/// </summary>
	public static ShaderMaterial GetSharedMaterial(float[][] cm, ulong matrixKey)
	{
		lock (materialCacheLock)
		{
			if (sharedMaterialCache.TryGetValue(matrixKey, out var cached))
			{
				TouchSharedMaterialKey(matrixKey);
				return cached;
			}
			if (sharedMaterialCache.Count >= SharedMaterialCacheLimit)
				EvictOldestSharedMaterial();
			var mat = CreateMaterial(cm);
			sharedMaterialCache[matrixKey] = mat;
			AddSharedMaterialKey(matrixKey);
			return mat;
		}
	}

	static void TouchSharedMaterialKey(ulong matrixKey)
	{
		if (!sharedMaterialLruNodes.TryGetValue(matrixKey, out var node))
			return;
		sharedMaterialLru.Remove(node);
		sharedMaterialLru.AddLast(node);
	}

	static void AddSharedMaterialKey(ulong matrixKey)
	{
		var node = sharedMaterialLru.AddLast(matrixKey);
		sharedMaterialLruNodes[matrixKey] = node;
	}

	static void EvictOldestSharedMaterial()
	{
		var node = sharedMaterialLru.First;
		if (node == null)
			return;

		// Do not Dispose the evicted material here. Visible EmueraImage nodes may
		// still reference it as CanvasItem.Material; eviction only removes the cache
		// root so new matrices do not force a full ShaderMaterial cache rebuild.
		ulong evictedKey = node.Value;
		sharedMaterialLru.RemoveFirst();
		sharedMaterialLruNodes.Remove(evictedKey);
		sharedMaterialCache.Remove(evictedKey);
	}

	/// <summary>
	/// Drops matrix-derived materials owned by the active legacy session. Shader
	/// resources remain process-scoped, but matrix materials are produced from
	/// game input and must not keep old session Resource references alive.
	/// </summary>
	internal static void ResetCanarySessionState()
	{
		lock (materialCacheLock)
		{
			sharedMaterialCache.Clear();
			sharedMaterialLruNodes.Clear();
			sharedMaterialLru.Clear();
		}
	}

	/// <summary>
	/// Build a stable bit-level key for a 5x4 GDI+ ColorMatrix payload.
	/// Float bits are hashed directly so semantically different script matrices do
	/// not accidentally share a mutable material state.
	/// </summary>
	public static ulong GetMatrixKey(float[][] cm)
	{
		unchecked
		{
			ulong hash = 14695981039346656037UL;
			for (int row = 0; row < 5; row++)
			{
				for (int col = 0; col < 4; col++)
				{
					uint bits = (uint)BitConverter.SingleToInt32Bits(cm[row][col]);
					hash ^= bits;
					hash *= 1099511628211UL;
				}
			}
			return hash;
		}
	}

	/// <summary>
	/// Update an existing ShaderMaterial's ColorMatrix uniforms.
	/// </summary>
	public static void SetMatrixUniforms(ShaderMaterial mat, float[][] cm)
	{
		// GDI+ convention: cm[input][output]. Shader needs output-major vectors.
		// cm_r = coefficients for R output = column 0 of the matrix
		mat.SetShaderParameter("cm_r", new Vector4(cm[0][0], cm[1][0], cm[2][0], cm[3][0]));
		mat.SetShaderParameter("cm_g", new Vector4(cm[0][1], cm[1][1], cm[2][1], cm[3][1]));
		mat.SetShaderParameter("cm_b", new Vector4(cm[0][2], cm[1][2], cm[2][2], cm[3][2]));
		mat.SetShaderParameter("cm_a", new Vector4(cm[0][3], cm[1][3], cm[2][3], cm[3][3]));
		mat.SetShaderParameter("cm_offset", new Vector4(cm[4][0], cm[4][1], cm[4][2], cm[4][3]));
	}

	/// <summary>
	/// Reset a ShaderMaterial to identity (no color transformation).
	/// </summary>
	public static void SetIdentity(ShaderMaterial mat)
	{
		mat.SetShaderParameter("cm_r", new Vector4(1f, 0f, 0f, 0f));
		mat.SetShaderParameter("cm_g", new Vector4(0f, 1f, 0f, 0f));
		mat.SetShaderParameter("cm_b", new Vector4(0f, 0f, 1f, 0f));
		mat.SetShaderParameter("cm_a", new Vector4(0f, 0f, 0f, 1f));
		mat.SetShaderParameter("cm_offset", new Vector4(0f, 0f, 0f, 0f));
	}

	/// <summary>
	/// Create a ShaderMaterial with identity matrix (default, no transformation).
	/// Used for the GPU render viewport's base material.
	/// </summary>
	public static ShaderMaterial CreateDefaultMaterial()
	{
		var mat = new ShaderMaterial();
		mat.Shader = Shader;
		SetIdentity(mat);
		return mat;
	}

	/// <summary>
	/// Create a ShaderMaterial using the compositing shader (blend_disabled).
	/// Used for SubViewport-based GPU color matrix processing where we need
	/// straight-alpha output without premultiplied blending.
	/// </summary>
	public static ShaderMaterial CreateCompositMaterial()
	{
		var mat = new ShaderMaterial();
		mat.Shader = CompositShader;
		SetIdentity(mat);
		return mat;
	}
}
