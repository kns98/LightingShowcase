using System.Diagnostics;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

public sealed class RenderJobRunner
{
    private static readonly SemaphoreSlim RenderGate = new(1, 1);

    public static bool TryHandleRendererProcessArgument(string[] args, out int exitCode)
    {
        if (args.Length == 1 && string.Equals(args[0], VeldridVulkanDevicePreflight.ChildArgument, StringComparison.Ordinal))
        {
            exitCode = VeldridVulkanDevicePreflight.RunChildDeviceCreationTest();
            return true;
        }

        exitCode = 0;
        return false;
    }

    public static void DisposeSharedResources()
    {
        VulkanSceneComputeRenderer.DisposeSharedDevice();
        VulkanRasterRenderer.DisposeSharedDevice();
    }

    public async Task<RenderJobResult> RunAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        await RenderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return RunExclusive(request, cancellationToken);
        }
        finally
        {
            RenderGate.Release();
        }
    }

    private static RenderJobResult RunExclusive(RenderRequest request, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ResolvedSceneInput input = SceneInputResolver.Resolve(request.Input!);
        TextureMap.ConfigureAssetRoots([input.AssetDirectory]);

        Scene scene = LoadScene(input.ScenePath);
        if (scene.Triangles.Count == 0)
            throw new InvalidDataException("The scene contains no renderable triangles.");

        CameraDefinition camera = BuildCamera(scene, request);
        RenderSettings renderSettings = request.ToRenderSettings();
        int triangleCount = scene.Triangles.Count;
        int lightCount = scene.Lights.Count;
        string rendererName = RenderRequest.BackendName(request.Backend);
        string outputPath = ResolveOutputPath(request.Output, input.ScenePath);
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        Console.WriteLine(
            $"Rendering with {rendererName}: {triangleCount:N0} triangles, {lightCount:N0} lights at " +
            $"{request.Width}x{request.Height}; samples={request.Samples}, bounces={request.Bounces}, " +
            $"fov={request.FieldOfViewDegrees:0.##}, assets={input.AssetDirectory}.");

        string details;
        switch (request.Backend)
        {
            case RenderBackend.VulkanGpu:
            {
                // The editable hierarchy is redundant after the Vulkan compute
                // renderer has packed the world triangles for a headless render.
                scene.ReleaseEditorGeometryForHeadlessRender();
                RenderImage image = VulkanSceneComputeRenderer.Render(
                    scene,
                    camera.Position,
                    camera.ToBasis(),
                    request.Width,
                    request.Height,
                    request.Bounces,
                    0,
                    request.Samples,
                    cancellationToken,
                    out details,
                    progressCallback: null,
                    settings: renderSettings,
                    fieldOfViewDegrees: request.FieldOfViewDegrees);
                image.SavePng(outputPath);
                break;
            }
            case RenderBackend.Cpu:
            {
                RenderImage image = CpuCommandLineRenderer.Render(
                    scene, camera, request, cancellationToken, out details);
                image.SavePng(outputPath);
                break;
            }
            case RenderBackend.ShadowRasterPreview:
            {
                RenderImage image = ShadowRasterRenderer.Render(
                    scene,
                    camera.Position,
                    camera.ToBasis(),
                    request.Width,
                    request.Height,
                    cancellationToken,
                    out details);
                image.SavePng(outputPath);
                break;
            }
            case RenderBackend.VulkanRasterPreview:
            {
                RenderImage image = VulkanRasterRenderer.Render(
                    scene,
                    camera.Position,
                    camera.ToBasis(),
                    request.Width,
                    request.Height,
                    cancellationToken,
                    out details);
                image.SavePng(outputPath);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request.Backend), request.Backend, "Unsupported command-line renderer.");
        }

        stopwatch.Stop();
        return new RenderJobResult(
            rendererName,
            Path.GetFileName(input.ScenePath),
            input.AssetDirectory,
            request.Width,
            request.Height,
            request.Samples,
            request.Bounces,
            request.FieldOfViewDegrees,
            request.Exposure,
            request.AmbientStrength,
            request.UseShadows,
            triangleCount,
            lightCount,
            stopwatch.Elapsed.TotalSeconds,
            details,
            outputPath);
    }

    private static Scene LoadScene(string scenePath)
    {
        PluginBootstrap.EnsureLoaded();
        Scene scene = new();
        if (SupportedSceneFormats.IsBinaryScenePath(scenePath))
        {
            string description = BinarySceneFile.LoadIntoScene(scene, scenePath);
            scene.SetDescription(description);
            return scene;
        }

        scene.OpenModelFile(scenePath, progress =>
        {
            if (progress.Percent % 10 == 0 || progress.Percent == 100)
                Console.WriteLine($"Import {progress.Percent,3}%: {progress.Stage} ({progress.TriangleCount:N0} triangles)");
        });
        return scene;
    }

    private static CameraDefinition BuildCamera(Scene scene, RenderRequest request)
    {
        Aabb? bounds = ComputeTriangleBounds(scene.Triangles);
        Vec3 center = bounds.HasValue ? (bounds.Value.Min + bounds.Value.Max) * 0.5 : new Vec3(0, 0.55, 0);
        Vec3 extent = bounds.HasValue ? bounds.Value.Max - bounds.Value.Min : new Vec3(2, 2, 2);
        double radius = Math.Max(0.25, extent.Length() * 0.5);

        Vec3 target = RenderRequest.ParseVector(request.CameraTarget, nameof(request.CameraTarget)) ?? center;
        Vec3 position = RenderRequest.ParseVector(request.CameraPosition, nameof(request.CameraPosition))
            ?? target + new Vec3(radius * 0.75, radius * 0.45, -radius * 2.25);
        Vec3 up = RenderRequest.ParseVector(request.CameraUp, nameof(request.CameraUp)) ?? new Vec3(0, 1, 0);

        return new CameraDefinition
        {
            Position = position,
            Target = target,
            Up = up,
            FieldOfViewDegrees = request.FieldOfViewDegrees,
            FarPlane = Math.Max(5000.0, radius * 20.0)
        };
    }

    private static Aabb? ComputeTriangleBounds(IReadOnlyList<Triangle> triangles)
    {
        if (triangles.Count == 0) return null;

        Vec3 first = triangles[0].A;
        double minX = first.X, minY = first.Y, minZ = first.Z;
        double maxX = first.X, maxY = first.Y, maxZ = first.Z;
        foreach (Triangle triangle in triangles)
        {
            Expand(triangle.A);
            Expand(triangle.B);
            Expand(triangle.C);
        }
        return new Aabb(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));

        void Expand(Vec3 value)
        {
            minX = Math.Min(minX, value.X);
            minY = Math.Min(minY, value.Y);
            minZ = Math.Min(minZ, value.Z);
            maxX = Math.Max(maxX, value.X);
            maxY = Math.Max(maxY, value.Y);
            maxZ = Math.Max(maxZ, value.Z);
        }
    }

    private static string ResolveOutputPath(string? requested, string scenePath)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return Path.GetFullPath(requested);

        string directory = Path.GetDirectoryName(scenePath) ?? Environment.CurrentDirectory;
        string fileName = Path.GetFileNameWithoutExtension(scenePath);
        if (scenePath.EndsWith(".prop.xml", StringComparison.OrdinalIgnoreCase))
            fileName = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(directory, $"{fileName}-render.png");
    }
}
