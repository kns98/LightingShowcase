// -----------------------------------------------------------------------------
// Headless, cross-platform texture storage for LightingShowcase.RenderWorker.
// Uses a managed image decoder so it runs in Linux GPU containers.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using StbImageSharp;

namespace LightingShowcase.SceneGraph;

public sealed partial class TextureMap
{
    private static readonly object AssetIndexGate = new();
    private static Dictionary<string, string>? assetIndex;

    /// <summary>Indexes the scene directory so local assets can be resolved from relative paths or relocated filenames.</summary>
    public static void ConfigureAssetRoots(IEnumerable<string> roots)
    {
        Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (!string.IsNullOrWhiteSpace(name) && !index.ContainsKey(name))
                    index[name] = file;
            }
        }

        lock (AssetIndexGate)
            assetIndex = index;
    }

    /// <summary>Resolves an existing path, a path relative to the scene directory, or a local file with the same leaf name.</summary>
    public static string? ResolveFilePath(string? path, params string[] relativeRoots)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(normalized))
            return Path.GetFullPath(normalized);

        foreach (string root in relativeRoots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            string candidate;
            try { candidate = Path.Combine(root, normalized); }
            catch { continue; }
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        string leaf = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(leaf))
            return null;

        lock (AssetIndexGate)
            return assetIndex != null && assetIndex.TryGetValue(leaf, out string? match) ? match : null;
    }

    private readonly Vec3[] pixels;
    private readonly double[] alpha;
    private readonly TextureAddressMode wrapU;
    private readonly TextureAddressMode wrapV;
    private readonly double offsetU;
    private readonly double offsetV;
    private readonly double scaleU;
    private readonly double scaleV;
    private readonly double rotation;

    public int Width { get; }
    public int Height { get; }
    public string Name { get; }
    public string? SourcePath { get; }
    public bool IsBuiltInChecker { get; }
    public TextureAddressMode WrapU => wrapU;
    public TextureAddressMode WrapV => wrapV;
    public double OffsetU => offsetU;
    public double OffsetV => offsetV;
    public double ScaleU => scaleU;
    public double ScaleV => scaleV;
    public double Rotation => rotation;

    /// <summary>Constructs and initializes this component.</summary>
    private TextureMap(
        string name,
        int width,
        int height,
        Vec3[] pixels,
        double[]? alpha = null,
        string? sourcePath = null,
        bool isBuiltInChecker = false,
        TextureAddressMode wrapU = TextureAddressMode.Repeat,
        TextureAddressMode wrapV = TextureAddressMode.Repeat,
        double offsetU = 0.0,
        double offsetV = 0.0,
        double scaleU = 1.0,
        double scaleV = 1.0,
        double rotation = 0.0)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Texture width must be greater than zero.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Texture height must be greater than zero.");

        Name = string.IsNullOrWhiteSpace(name) ? "Texture" : name;
        Width = width;
        Height = height;
        SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : sourcePath;
        IsBuiltInChecker = isBuiltInChecker;
        this.wrapU = wrapU;
        this.wrapV = wrapV;
        this.offsetU = offsetU;
        this.offsetV = offsetV;
        this.scaleU = scaleU;
        this.scaleV = scaleV;
        this.rotation = rotation;

        int expectedLength = checked(width * height);
        this.pixels = pixels.Length == expectedLength
            ? pixels
            : throw new ArgumentException("Texture pixel buffer size does not match dimensions.", nameof(pixels));
        this.alpha = alpha == null
            ? Enumerable.Repeat(1.0, expectedLength).ToArray()
            : alpha.Length == expectedLength
                ? alpha
                : throw new ArgumentException("Texture alpha buffer size does not match dimensions.", nameof(alpha));
    }

    public TextureMap WithAddressing(TextureAddressMode wrapU, TextureAddressMode wrapV) =>
        new(Name, Width, Height, pixels, alpha, SourcePath, IsBuiltInChecker, wrapU, wrapV, offsetU, offsetV, scaleU, scaleV, rotation);

    public TextureMap WithTextureTransform(double offsetU, double offsetV, double scaleU, double scaleV, double rotation) =>
        new(Name, Width, Height, pixels, alpha, SourcePath, IsBuiltInChecker, wrapU, wrapV, offsetU, offsetV, scaleU, scaleV, rotation);

    /// <summary>Loads an encoded image with a managed, cross-platform decoder.</summary>
    public static TextureMap FromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Texture file path is required.", nameof(path));

        string? resolved = ResolveFilePath(path);
        if (resolved == null)
            throw new FileNotFoundException("Texture image file was not found.", path);

        using FileStream stream = File.OpenRead(resolved);
        return FromEncodedStream(Path.GetFileName(resolved), stream, resolved);
    }

    /// <summary>Loads an encoded PNG/JPEG/BMP/TGA/PSD/GIF/HDR image from memory.</summary>
    public static TextureMap FromBytes(string name, byte[] encodedBytes, string? sourcePath = null)
    {
        if (encodedBytes == null || encodedBytes.Length == 0)
            throw new ArgumentException("Encoded texture bytes are required.", nameof(encodedBytes));

        using MemoryStream stream = new(encodedBytes, writable: false);
        return FromEncodedStream(name, stream, sourcePath);
    }

    private static TextureMap FromEncodedStream(string name, Stream stream, string? sourcePath)
    {
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        int width = image.Width;
        int height = image.Height;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Texture image has invalid dimensions.");

        Vec3[] pixels = new Vec3[checked(width * height)];
        double[] alpha = new double[pixels.Length];
        byte[] rgba = image.Data;
        for (int i = 0; i < pixels.Length; i++)
        {
            int offset = i * 4;
            pixels[i] = new Vec3(rgba[offset] / 255.0, rgba[offset + 1] / 255.0, rgba[offset + 2] / 255.0);
            alpha[i] = rgba[offset + 3] / 255.0;
        }

        return new TextureMap(name, width, height, pixels, alpha, sourcePath);
    }

    /// <summary>Creates checker for use by the renderer or editor.</summary>
    public static TextureMap CreateChecker(string name = "Built-in checker", int width = 160, int height = 96, int cellsX = 10, int cellsY = 6)
    {
        width = Math.Max(2, width);
        height = Math.Max(2, height);
        cellsX = Math.Max(1, cellsX);
        cellsY = Math.Max(1, cellsY);

        Vec3[] pixels = new Vec3[checked(width * height)];
        Vec3 a = new(0.95, 0.92, 0.78);
        Vec3 b = new(0.10, 0.18, 0.32);
        Vec3 line = new(0.02, 0.02, 0.025);

        int cellWidth = Math.Max(1, width / cellsX);
        int cellHeight = Math.Max(1, height / cellsY);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cx = x * cellsX / width;
                int cy = y * cellsY / height;
                bool border = x % cellWidth == 0 || y % cellHeight == 0;
                pixels[y * width + x] = border ? line : (((cx + cy) & 1) == 0 ? a : b);
            }
        }

        return new TextureMap(name, width, height, pixels, isBuiltInChecker: true);
    }

    /// <summary>Implements the sample operation for this file's subsystem.</summary>
    public Vec3 Sample(double u, double v)
    {
        // Match the Helix/WPF viewport's normalized texture-coordinate behavior:
        // U/V values in [0,1] address the bitmap directly, with V=0 at the top
        // of the bitmap.  This is important for glTF atlas textures because the
        // raytracer must sample the same atlas rectangles that Helix previews.
        //
        // The old renderer flipped V and wrapped exact 1.0 back to 0.0.  That can
        // make an atlas look like the full bitmap is being projected/tiled over
        // faces even though the imported glTF UVs are correct.  Values outside
        // [0,1] still repeat, so editor box-projected/checker textures continue
        // to tile.
        ApplyTransform(ref u, ref v);
        u = Address(u, wrapU);
        v = Address(v, wrapV);

        double fx = Width == 1 ? 0.0 : u * (Width - 1);
        double fy = Height == 1 ? 0.0 : v * (Height - 1);
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        int x1 = Math.Min(Width - 1, x0 + 1);
        int y1 = Math.Min(Height - 1, y0 + 1);
        double tx = fx - x0;
        double ty = fy - y0;

        Vec3 c00 = pixels[y0 * Width + x0];
        Vec3 c10 = pixels[y0 * Width + x1];
        Vec3 c01 = pixels[y1 * Width + x0];
        Vec3 c11 = pixels[y1 * Width + x1];
        Vec3 top = Vec3.Lerp(c00, c10, tx);
        Vec3 bottom = Vec3.Lerp(c01, c11, tx);
        return Vec3.Lerp(top, bottom, ty);
    }

    /// <summary>Samples alpha by UV coordinate using the same filtering as Sample().</summary>
    public double SampleAlpha(double u, double v)
    {
        ApplyTransform(ref u, ref v);
        u = Address(u, wrapU);
        v = Address(v, wrapV);

        double fx = Width == 1 ? 0.0 : u * (Width - 1);
        double fy = Height == 1 ? 0.0 : v * (Height - 1);
        int x0 = (int)Math.Floor(fx);
        int y0 = (int)Math.Floor(fy);
        int x1 = Math.Min(Width - 1, x0 + 1);
        int y1 = Math.Min(Height - 1, y0 + 1);
        double tx = fx - x0;
        double ty = fy - y0;

        double a00 = alpha[y0 * Width + x0];
        double a10 = alpha[y0 * Width + x1];
        double a01 = alpha[y1 * Width + x0];
        double a11 = alpha[y1 * Width + x1];
        double top = a00 + (a10 - a00) * tx;
        double bottom = a01 + (a11 - a01) * tx;
        return Math.Clamp(top + (bottom - top) * ty, 0.0, 1.0);
    }


    /// <summary>Copies the texture into the same packed R/G/B/A byte order used by the Vulkan compute shader.</summary>
    public uint[] CopyPackedRgba32Pixels()
    {
        uint[] packed = new uint[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            Vec3 c = pixels[i];
            uint r = ToByte(c.X);
            uint g = ToByte(c.Y);
            uint b = ToByte(c.Z);
            uint a = ToByte(alpha[i]);
            packed[i] = r | (g << 8) | (b << 16) | (a << 24);
        }
        return packed;
    }

    private static byte ToByte(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0);

    /// <summary>Implements the wrap01 operation for this file's subsystem.</summary>
    private static double Address(double value, TextureAddressMode mode)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;

        if (mode == TextureAddressMode.ClampToEdge)
            return Math.Clamp(value, 0.0, 1.0);

        if (mode == TextureAddressMode.MirroredRepeat)
        {
            double mirrored = value - Math.Floor(value);
            if (((long)Math.Floor(value) & 1L) != 0L)
                mirrored = 1.0 - mirrored;
            return mirrored;
        }

        // Keep authored atlas coordinates at the exact texture edge on that edge.
        // A UV of 1.0 should sample the final texel, not wrap to the first texel.
        if (value >= 0.0 && value <= 1.0)
            return value;

        value -= Math.Floor(value);
        return value < 0 ? value + 1.0 : value;
    }

    private void ApplyTransform(ref double u, ref double v)
    {
        double scaledU = u * scaleU;
        double scaledV = v * scaleV;
        if (Math.Abs(rotation) > 1e-12)
        {
            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);
            double rotatedU = scaledU * cos - scaledV * sin;
            double rotatedV = scaledU * sin + scaledV * cos;
            scaledU = rotatedU;
            scaledV = rotatedV;
        }

        u = scaledU + offsetU;
        v = scaledV + offsetV;
    }
}

public enum TextureAddressMode
{
    Repeat,
    ClampToEdge,
    MirroredRepeat
}
