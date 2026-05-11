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
    private static Shader _shader;
    private static Shader _compositShader;

    public static Shader Shader
    {
        get
        {
            if (_shader == null)
            {
                _shader = GD.Load<Shader>("res://Scripts/Shaders/color_matrix.gdshader");
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
                _compositShader = GD.Load<Shader>("res://Scripts/Shaders/color_matrix_composit.gdshader");
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
