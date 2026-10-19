using Godot;
using GEmuera.Core.Resources;
using GEmuera.Core.Session;
using System.Collections.Generic;

/// <summary>
/// Projects immutable Core pixel snapshots into generation/revision guarded
/// Godot textures. The Core PixelStore remains the only pixel authority.
/// </summary>
public partial class PrototypeResourceBridge : Node
{
    [Signal]
    public delegate void TextureProjectedEventHandler(long handle, long generation, long revision);

    [Signal]
    public delegate void TextureRejectedEventHandler(long handle, long generation, long revision, string reason);

    private readonly Dictionary<PixelHandle, Projection> projections = new();
    private SessionGeneration generation = new(1);
    private bool generationAttached;

    public long CurrentGeneration => generation.Value;
    public bool IsGenerationAttached => generationAttached;

    private sealed class Projection
    {
        public Projection(ImageTexture texture, PixelRevision revision)
        {
            Texture = texture;
            Revision = revision;
        }

        public ImageTexture Texture { get; }
        public PixelRevision Revision { get; }
    }

    public Texture2D GetTexture(PixelHandle handle)
    {
        return projections.TryGetValue(handle, out var projection) ? projection.Texture : null;
    }

    public bool Project(PixelHandle handle, PixelSurfaceSnapshot surface, long requestedGeneration)
    {
        if (!handle.IsValid || surface is null)
        {
            Reject(handle, requestedGeneration, 0, "invalid_surface");
            return false;
        }
        if (!generationAttached || requestedGeneration != generation.Value)
        {
            Reject(handle, requestedGeneration, surface.Revision.Value, generationAttached ? "stale_generation" : "detached");
            return false;
        }
        if (projections.TryGetValue(handle, out var current) && surface.Revision.Value <= current.Revision.Value)
        {
            Reject(handle, requestedGeneration, surface.Revision.Value, "older_revision");
            return false;
        }

        var image = Image.CreateFromData(
            surface.Width,
            surface.Height,
            false,
            Image.Format.Rgba8,
            surface.Bytes.ToArray());
        var texture = ImageTexture.CreateFromImage(image);
        if (projections.TryGetValue(handle, out current))
            current.Texture.Dispose();
        projections[handle] = new Projection(texture, surface.Revision);
        EmitSignal(SignalName.TextureProjected, handle.Value, requestedGeneration, surface.Revision.Value);
        return true;
    }

    public void Remove(PixelHandle handle)
    {
        if (projections.Remove(handle, out var projection))
            projection.Texture.Dispose();
    }

    public void ResetGeneration(long value)
    {
        var next = new SessionGeneration(value);
        if (next.Value <= generation.Value)
            throw new System.ArgumentOutOfRangeException(nameof(value));
        Clear();
        generation = next;
        generationAttached = true;
    }

    public bool AttachGeneration(long value)
    {
        var next = new SessionGeneration(value);
        if (next.Value < generation.Value)
            return false;
        if (next.Value > generation.Value)
        {
            Clear();
            generation = next;
        }
        generationAttached = true;
        return true;
    }

    public bool DetachGeneration(long value = -1)
    {
        var expectedGeneration = value < 0 ? generation.Value : value;
        if (expectedGeneration != generation.Value)
            return false;
        if (!generationAttached)
            return true;

        Clear();
        generationAttached = false;
        return true;
    }

    public void Clear()
    {
        foreach (var projection in projections.Values)
            projection.Texture.Dispose();
        projections.Clear();
    }

    public override void _ExitTree()
    {
        generationAttached = false;
        Clear();
    }

    private void Reject(PixelHandle handle, long requestedGeneration, long revision, string reason)
    {
        EmitSignal(SignalName.TextureRejected, handle.Value, requestedGeneration, revision, reason);
    }
}
