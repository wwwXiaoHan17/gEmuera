using Godot;

/// <summary>
/// Static notification system for the sprite debug viewer.
/// Hooks placed at image loading points broadcast each image
/// so the debug viewer can display it.
/// </summary>
public static class SpriteDebugNotifier
{
    public delegate void ImageLoadedHandler(string name, Image image, string info);
    public static event ImageLoadedHandler OnImageLoaded;

    public static void Notify(string name, Image image, string info)
    {
        OnImageLoaded?.Invoke(name, image, info);
    }
}
