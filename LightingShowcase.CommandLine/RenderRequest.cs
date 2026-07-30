using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

public sealed class RenderRequest
{
    private static readonly string[] KnownOptions =
    [
        "input", "output", "width", "height", "samples", "bounces",
        "camera-position", "camera-target", "camera-up", "fov",
        "exposure", "ambient", "background-top", "background-bottom",
        "shadows", "no-shadows", "renderer", "help"
    ];

    public string? Input { get; set; }
    public string? Output { get; set; }
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Samples { get; set; } = 1;
    public int Bounces { get; set; } = 2;
    public string? CameraPosition { get; set; }
    public string? CameraTarget { get; set; }
    public string? CameraUp { get; set; }
    public double FieldOfViewDegrees { get; set; } = 72.0;
    public double Exposure { get; set; } = 1.0;
    public double AmbientStrength { get; set; } = 1.0;
    public string? BackgroundTop { get; set; }
    public string? BackgroundBottom { get; set; }
    public bool UseShadows { get; set; } = true;
    public RenderBackend Backend { get; set; } = RenderBackend.VulkanGpu;

    internal static RenderRequest FromCommandLine(CommandLine values)
    {
        values.ValidateKnownOptions(KnownOptions);
        if (values.Positionals.Count > 1)
            throw new ArgumentException("Only one positional scene path is allowed.");
        if (!string.IsNullOrWhiteSpace(values.Get("input")) && values.Positionals.Count > 0)
            throw new ArgumentException("Specify the scene either positionally or with --input, not both.");

        bool useShadows = values.GetBool("shadows", true);
        if (values.Has("no-shadows")) useShadows = false;

        RenderRequest request = new()
        {
            Input = values.Get("input") ?? values.Positionals.FirstOrDefault(),
            Output = values.Get("output"),
            Width = values.GetInt("width", 1920, 1, 32768),
            Height = values.GetInt("height", 1080, 1, 32768),
            Samples = values.GetInt("samples", 1, 1, 4096),
            Bounces = values.GetInt("bounces", 2, 0, 8),
            CameraPosition = values.Get("camera-position"),
            CameraTarget = values.Get("camera-target"),
            CameraUp = values.Get("camera-up"),
            FieldOfViewDegrees = values.GetDouble("fov", 72.0, 1.0, 179.0),
            Exposure = values.GetDouble("exposure", 1.0, 0.01, 100.0),
            AmbientStrength = values.GetDouble("ambient", 1.0, 0.0, 100.0),
            BackgroundTop = values.Get("background-top"),
            BackgroundBottom = values.Get("background-bottom"),
            UseShadows = useShadows,
            Backend = ParseBackend(values.Get("renderer"))
        };
        request.Validate();
        return request;
    }

    public RenderSettings ToRenderSettings() => new()
    {
        Width = Width,
        Height = Height,
        Exposure = Exposure,
        AmbientStrength = AmbientStrength,
        BackgroundTop = ParseColor(BackgroundTop, nameof(BackgroundTop)) ?? new Vec3(0.055, 0.060, 0.072),
        BackgroundBottom = ParseColor(BackgroundBottom, nameof(BackgroundBottom)) ?? new Vec3(0.010, 0.012, 0.016),
        UseShadows = UseShadows,
        PathBounceCount = Bounces,
        Backend = Backend
    };


    public static RenderBackend ParseBackend(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "vulkan"
            : value.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');

        return normalized switch
        {
            "raster" or "shadow-raster" or "software-raster" or "cpu-raster" => RenderBackend.ShadowRasterPreview,
            "raster-vulkan" or "vulkan-raster" or "gpu-raster" => RenderBackend.VulkanRasterPreview,
            "vulkan" or "vulkan-compute" or "gpu" => RenderBackend.VulkanGpu,
            "cpu" or "cpu-ray" or "cpu-path" => RenderBackend.Cpu,
            _ => throw new ArgumentException(
                "--renderer must be one of: raster, raster-vulkan, vulkan, or cpu.")
        };
    }

    public static string BackendName(RenderBackend backend) => backend switch
    {
        RenderBackend.ShadowRasterPreview => "raster",
        RenderBackend.VulkanRasterPreview => "raster-vulkan",
        RenderBackend.VulkanGpu => "vulkan",
        RenderBackend.Cpu => "cpu",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unsupported command-line renderer.")
    };

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Input))
            throw new ArgumentException("Input must be a local scene/model file path.");
        if (Width is < 1 or > 32768) throw new ArgumentOutOfRangeException(nameof(Width), "Width must be between 1 and 32768.");
        if (Height is < 1 or > 32768) throw new ArgumentOutOfRangeException(nameof(Height), "Height must be between 1 and 32768.");
        if ((long)Width * Height * 4L > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Width), "The RGBA output buffer must be smaller than 4 GiB.");
        if (Samples is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(Samples), "Samples must be between 1 and 4096.");
        if (Bounces is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(Bounces), "Bounces must be between 0 and 8.");
        if (FieldOfViewDegrees is < 1.0 or > 179.0) throw new ArgumentOutOfRangeException(nameof(FieldOfViewDegrees), "Field of view must be between 1 and 179 degrees.");
        if (Exposure is < 0.01 or > 100.0) throw new ArgumentOutOfRangeException(nameof(Exposure), "Exposure must be between 0.01 and 100.");
        if (AmbientStrength is < 0.0 or > 100.0) throw new ArgumentOutOfRangeException(nameof(AmbientStrength), "Ambient strength must be between 0 and 100.");
        if (Backend is RenderBackend.VulkanDiagnostic) throw new ArgumentOutOfRangeException(nameof(Backend), "Vulkan diagnostic is not one of the four command-line renderers.");
        _ = ParseVector(CameraPosition, nameof(CameraPosition));
        _ = ParseVector(CameraTarget, nameof(CameraTarget));
        _ = ParseVector(CameraUp, nameof(CameraUp));
        _ = ParseColor(BackgroundTop, nameof(BackgroundTop));
        _ = ParseColor(BackgroundBottom, nameof(BackgroundBottom));
    }

    public static Vec3? ParseVector(string? value, string name) => ParseTriple(value, name, requireNonNegative: false);

    public static Vec3? ParseColor(string? value, string name) => ParseTriple(value, name, requireNonNegative: true);

    private static Vec3? ParseTriple(string? value, string name, bool requireNonNegative)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] parts = value.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z) ||
            !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
            (requireNonNegative && (x < 0.0 || y < 0.0 || z < 0.0)))
        {
            string constraint = requireNonNegative ? " three non-negative" : " three";
            string example = requireNonNegative ? "0.1,0.2,0.3" : "0,1,-3";
            throw new ArgumentException($"{name} must contain{constraint} invariant-culture numbers, for example {example}.");
        }
        return new Vec3(x, y, z);
    }
}

public sealed record RenderJobResult(
    string Backend,
    string Scene,
    string AssetDirectory,
    int Width,
    int Height,
    int Samples,
    int Bounces,
    double FieldOfViewDegrees,
    double Exposure,
    double AmbientStrength,
    bool Shadows,
    int TriangleCount,
    int LightCount,
    double ElapsedSeconds,
    string Details,
    string Output);
