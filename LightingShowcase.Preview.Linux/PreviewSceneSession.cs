using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.ImportExport.Fbx;
using LightingShowcase.ImportExport.Gltf;
using LightingShowcase.ImportExport.Obj;
using LightingShowcase.ImportExport.Ply;
using LightingShowcase.ImportExport.PropXml;
using LightingShowcase.ImportExport.Stl;
using LightingShowcase.ImportExport.ThreeDs;
using LightingShowcase.ObjectLibrary.BuiltIns;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Preview;

internal sealed class PreviewSceneSession : IDisposable
{
    private static readonly string[] SupportedExtensions =
    [
        ".lscene", ".lsb", ".prop.xml", ".xml", ".glb", ".gltf",
        ".fbx", ".obj", ".3ds", ".ply", ".stl"
    ];

    private static int pluginsLoaded;
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private Scene? scene;
    private ShadowRasterRenderer.PreviewCache? rasterCache;

    public PreviewCamera Camera { get; } = new();
    public string? ScenePath { get; private set; }
    public int TriangleCount => scene?.Triangles.Count ?? 0;
    public int LightCount => scene?.Lights.Count ?? 0;

    public void ResetCamera()
    {
        Scene activeScene = scene ?? throw new InvalidOperationException("Load a scene before resetting the camera.");
        Camera.Reset(activeScene);
    }

    public void Load(string inputPath, CancellationToken cancellationToken)
    {
        string path = ResolveScenePath(inputPath);
        string assetDirectory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The scene path has no parent directory.");

        renderGate.Wait(cancellationToken);
        try
        {
            EnsurePluginsLoaded();
            TextureMap.ConfigureAssetRoots([assetDirectory]);

            Scene loaded = new();
            if (IsBinaryScenePath(path))
            {
                loaded.SetDescription(BinarySceneFile.LoadIntoScene(loaded, path));
            }
            else
            {
                loaded.OpenModelFile(path, progress => cancellationToken.ThrowIfCancellationRequested());
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (loaded.Triangles.Count == 0)
                throw new InvalidDataException("The scene contains no renderable triangles.");

            ShadowRasterRenderer.PreviewCache cache = ShadowRasterRenderer.BuildCache(loaded, cancellationToken);
            scene = loaded;
            rasterCache = cache;
            ScenePath = path;
            Camera.Reset(loaded);
        }
        finally
        {
            renderGate.Release();
        }
    }

    public PreviewFrame Render(
        PreviewRendererKind renderer,
        CameraDefinition camera,
        int width,
        int height,
        bool interactive,
        CancellationToken cancellationToken)
    {
        Scene activeScene = scene ?? throw new InvalidOperationException("Load a scene before rendering.");
        ShadowRasterRenderer.PreviewCache activeCache = rasterCache
            ?? throw new InvalidOperationException("The software raster cache is unavailable.");

        renderGate.Wait(cancellationToken);
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string details = string.Empty;
            RenderImage image = renderer switch
            {
                PreviewRendererKind.Raster => ShadowRasterRenderer.Render(
                    activeCache,
                    camera.Position,
                    camera.ToBasis(),
                    width,
                    height,
                    cancellationToken,
                    out details,
                    interactiveFast: interactive),

                PreviewRendererKind.VulkanRaster => VulkanRasterRenderer.Render(
                    activeScene,
                    camera.Position,
                    camera.ToBasis(),
                    width,
                    height,
                    cancellationToken,
                    out details),

                PreviewRendererKind.VulkanCompute => VulkanSceneComputeRenderer.Render(
                    activeScene,
                    camera.Position,
                    camera.ToBasis(),
                    width,
                    height,
                    bounceCount: 0,
                    sampleIndex: 0,
                    sampleCount: 1,
                    cancellationToken: cancellationToken,
                    details: out details,
                    progressCallback: null,
                    settings: new RenderSettings
                    {
                        Width = width,
                        Height = height,
                        Backend = RenderBackend.VulkanGpu,
                        PathBounceCount = 0,
                        Exposure = 1.0,
                        AmbientStrength = 1.0,
                        UseShadows = true
                    },
                    fieldOfViewDegrees: camera.FieldOfViewDegrees),

                PreviewRendererKind.Cpu => CpuPreviewRenderer.Render(
                    activeScene,
                    camera,
                    width,
                    height,
                    samples: 1,
                    bounces: 1,
                    exposure: 1.0,
                    cancellationToken: cancellationToken,
                    details: out details),

                _ => throw new ArgumentOutOfRangeException(nameof(renderer), renderer, "Unknown preview renderer.")
            };

            stopwatch.Stop();
            return new PreviewFrame(image, stopwatch.Elapsed.TotalMilliseconds, details);
        }
        finally
        {
            renderGate.Release();
        }
    }

    public void Dispose()
    {
        renderGate.Wait();
        try
        {
            VulkanSceneComputeRenderer.DisposeSharedDevice();
            VulkanRasterRenderer.DisposeSharedDevice();
            scene = null;
            rasterCache = null;
        }
        finally
        {
            renderGate.Release();
            renderGate.Dispose();
        }
    }

    private static string ResolveScenePath(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Enter a local scene/model path.", nameof(inputPath));

        if (Uri.TryCreate(inputPath, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
            throw new NotSupportedException("Remote scene URLs are not supported.");

        string path = Path.GetFullPath(inputPath.Trim());
        if (!File.Exists(path))
            throw new FileNotFoundException("Scene input was not found.", path);
        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("ZIP scene packages are not supported. Extract the scene first.");
        if (!SupportedExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException($"Unsupported scene/model format: {Path.GetExtension(path)}");

        return path;
    }

    private static bool IsBinaryScenePath(string path) =>
        path.EndsWith(".lscene", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".lsb", StringComparison.OrdinalIgnoreCase);

    private static void EnsurePluginsLoaded()
    {
        if (Interlocked.Exchange(ref pluginsLoaded, 1) != 0)
            return;

        _ = typeof(FbxSceneFormatPlugin).Assembly;
        _ = typeof(GltfSceneFormatPlugin).Assembly;
        _ = typeof(ObjSceneFormatPlugin).Assembly;
        _ = typeof(PlySceneFormatPlugin).Assembly;
        _ = typeof(PropXmlSceneFormatPlugin).Assembly;
        _ = typeof(StlSceneFormatPlugin).Assembly;
        _ = typeof(ThreeDsSceneFormatPlugin).Assembly;
        _ = typeof(BuiltInObjectLibraryPlugin).Assembly;
        _ = typeof(DiningTableObject).Assembly;

        SceneFormatRegistry.EnsureInitialized();
        ObjectLibraryRegistry.EnsureInitialized();
    }
}

internal sealed record PreviewFrame(RenderImage Image, double ElapsedMilliseconds, string Details);
