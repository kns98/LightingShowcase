namespace LightingShowcase.Rendering;

/// <summary>Cross-platform RGBA render result. Each uint stores R, G, B, A in low-to-high byte order.</summary>
public sealed class RenderImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] PackedRgba32 { get; }

    public RenderImage(int width, int height, uint[] packedRgba32)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (packedRgba32 == null) throw new ArgumentNullException(nameof(packedRgba32));
        if (packedRgba32.Length != checked(width * height))
            throw new ArgumentException("Pixel buffer length does not match image dimensions.", nameof(packedRgba32));

        Width = width;
        Height = height;
        PackedRgba32 = packedRgba32;
    }

    public void SavePng(string path) => PngWriter.Write(path, this);
}
