// -----------------------------------------------------------------------------
// File: Rendering/VulkanSceneComputeRenderer.cs
// Purpose: Vulkan compute scene ray/path tracer.
//
// This renderer uses Vulkan compute through Veldrid rather than Vulkan RT
// BLAS/TLAS. It traces the current Scene.Triangles buffer on the GPU, applies
// scene lights, simple material properties, hard shadows, emissive surfaces, and
// optional stochastic bounces selected from the Render pane.
// -----------------------------------------------------------------------------

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using Veldrid;
using Veldrid.SPIRV;

namespace LightingShowcase.Rendering;

/// <summary>Vulkan compute ray/path tracer for the current triangle scene.</summary>
public static class VulkanSceneComputeRenderer
{
    public static string StageLogPath => Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-stage-log.txt");

    private static void ResetStageLog(int width, int height, int bounceCount, int sampleIndex)
    {
        try
        {
            File.WriteAllText(StageLogPath, $"LightingShowcase Vulkan stage log{Environment.NewLine}Resolution: {width}x{height}{Environment.NewLine}Bounces: {bounceCount}{Environment.NewLine}Sample: {sampleIndex}{Environment.NewLine}Started: {DateTime.Now:O}{Environment.NewLine}");
        }
        catch { }
    }

    private static void Stage(string name)
    {
        try
        {
            File.AppendAllText(StageLogPath, $"{DateTime.Now:O} - {name}{Environment.NewLine}");
        }
        catch { }
    }

    private static void StageMemory(string label)
    {
        try
        {
            long managed = GC.GetTotalMemory(forceFullCollection: false);
            Stage($"Memory {label}: managed={managed / (1024.0 * 1024.0):F1} MB");
        }
        catch { }
    }

    // This simple compute renderer is O(pixels * triangles * bounces), but the
    // Vulkan path must not silently truncate geometry. Older builds capped this
    // at 65,535 triangles, which caused partial geometry for larger scenes.
    // Keep all scene triangles unless the buffer byte-size check below overflows.
    private const int MaxGpuLights = 32;

    // Veldrid/Vulkan device creation is expensive and, on some drivers, unsafe
    // when repeated rapidly during progressive sampling. Keep one device alive
    // for the app lifetime and only recreate it after an explicit cleanup.
    private static readonly object DeviceSync = new();
    private static GraphicsDevice? sharedGraphicsDevice;
    private static SharedComputeResources? sharedComputeResources;
    private static bool preflightCompleted;

    private sealed class SharedComputeResources : IDisposable
    {
        public required ResourceLayout Layout { get; init; }
        public required Shader Shader { get; init; }
        public required Pipeline Pipeline { get; init; }

        public void Dispose()
        {
            try { Pipeline.Dispose(); } catch { }
            try { Shader.Dispose(); } catch { }
            try { Layout.Dispose(); } catch { }
        }
    }

    public static void DisposeSharedDevice()
    {
        lock (DeviceSync)
        {
            GraphicsDevice? device = sharedGraphicsDevice;
            sharedGraphicsDevice = null;
            SharedComputeResources? computeResources = sharedComputeResources;
            sharedComputeResources = null;
            if (device == null)
            {
                computeResources?.Dispose();
                return;
            }

            try
            {
                Stage("Dispose shared Vulkan GraphicsDevice: WaitForIdle");
                device.WaitForIdle();
            }
            catch (Exception ex)
            {
                Stage($"Dispose shared Vulkan GraphicsDevice: WaitForIdle failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                Stage("Dispose shared Vulkan compute resources");
                computeResources?.Dispose();
            }
            catch (Exception ex)
            {
                Stage($"Dispose shared Vulkan compute resources failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                Stage("Dispose shared Vulkan GraphicsDevice");
                device.Dispose();
            }
            catch (Exception ex)
            {
                Stage($"Dispose shared Vulkan GraphicsDevice failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Initializes and validates the shared Vulkan device for command-line readiness checks.</summary>
    public static string EnsureDeviceReady()
    {
        GraphicsDevice device = GetOrCreateSharedDevice();
        return device.BackendType.ToString();
    }

    private static GraphicsDevice GetOrCreateSharedDevice()
    {
        lock (DeviceSync)
        {
            if (sharedGraphicsDevice != null)
            {
                Stage("Reuse shared Veldrid Vulkan GraphicsDevice");
                return sharedGraphicsDevice;
            }

            if (!preflightCompleted)
            {
                Stage("Preflight Veldrid Vulkan GraphicsDevice in child process");
                VeldridVulkanDevicePreflight.VerifyInChildProcess(Stage);
                preflightCompleted = true;
            }

            Stage("Create shared Veldrid Vulkan GraphicsDevice in main process");
            GraphicsDevice gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            {
                Debug = false,
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                SyncToVerticalBlank = false
            });

            Stage($"Shared GraphicsDevice created: backend={gd.BackendType}");
            if (gd.BackendType != GraphicsBackend.Vulkan)
            {
                gd.Dispose();
                throw new InvalidOperationException("Veldrid did not create a Vulkan graphics device.");
            }

            sharedGraphicsDevice = gd;
            return gd;
        }
    }

    private static void ThrowIfCancellationRequested(CancellationToken cancellationToken, string stageName)
    {
        if (!cancellationToken.IsCancellationRequested)
            return;

        Stage($"Cancellation requested before/at: {stageName}");
        throw new OperationCanceledException(cancellationToken);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GpuTriangle
    {
        public readonly Vector4 A;
        public readonly Vector4 B;
        public readonly Vector4 C;
        public readonly Vector4 BaseColor;
        public readonly Vector4 Emission;
        public readonly Vector4 MaterialParams; // x=alpha factor, y=metallic, z=roughness, w=transmission
        public readonly Vector4 UvA;
        public readonly Vector4 UvB;
        public readonly Vector4 UvC;
        public readonly Vector4 TextureInfo; // x=base texture index, y=has base, z=emissive texture index, w=has emissive
        public readonly Vector4 TextureInfo2; // x=metallic/roughness texture index, y=has MR, z=normal texture index, w=has normal

        public GpuTriangle(Triangle triangle, IReadOnlyDictionary<TextureMap, int> textureIds)
        {
            A = ToVector4(triangle.A, 0.0f);
            B = ToVector4(triangle.B, 0.0f);
            C = ToVector4(triangle.C, 0.0f);
            UvA = new Vector4((float)triangle.UvA.U, (float)triangle.UvA.V, 0.0f, 0.0f);
            UvB = new Vector4((float)triangle.UvB.U, (float)triangle.UvB.V, 0.0f, 0.0f);
            UvC = new Vector4((float)triangle.UvC.U, (float)triangle.UvC.V, 0.0f, 0.0f);

            // Do not bake a single texture sample per triangle.  That was the
            // source of the visible triangular/mosaic artifact on textured lamp
            // shades and glTF atlas materials.  Store the material base-color
            // factor here and let the shader sample the texture at the actual
            // ray-hit UV coordinate.
            Vec3 colorFactor = triangle.Material.Color;

            // Texture-dependent material channels are sampled in the shader
            // at the actual ray hit.  Do not bake them at the triangle centroid:
            // that causes visible triangle mosaics on glTF atlas materials.
            double metallic = triangle.Material.Metallic;
            double roughness = triangle.Material.Roughness;
            double alpha = triangle.Material.Alpha;
            double transmission = triangle.Material.Transmission;

            BaseColor = new Vector4(
                (float)Math.Clamp(colorFactor.X, 0.0, 1.0),
                (float)Math.Clamp(colorFactor.Y, 0.0, 1.0),
                (float)Math.Clamp(colorFactor.Z, 0.0, 1.0),
                1.0f);

            Emission = new Vector4(
                (float)Math.Max(0.0, triangle.Material.EmissionColor.X),
                (float)Math.Max(0.0, triangle.Material.EmissionColor.Y),
                (float)Math.Max(0.0, triangle.Material.EmissionColor.Z),
                (float)Math.Max(0.0, triangle.Material.Emission));

            MaterialParams = new Vector4(
                (float)Math.Clamp(alpha, 0.0, 1.0),
                (float)Math.Clamp(metallic, 0.0, 1.0),
                (float)Math.Clamp(roughness, 0.02, 1.0),
                (float)Math.Clamp(transmission, 0.0, 1.0));

            int baseTextureIndex = TextureIndex(textureIds, triangle.Material.Texture);
            int emissiveTextureIndex = TextureIndex(textureIds, triangle.Material.EmissiveTexture);
            int metallicRoughnessTextureIndex = TextureIndex(textureIds, triangle.Material.MetallicRoughnessTexture);
            int normalTextureIndex = TextureIndex(textureIds, triangle.Material.NormalTexture);
            TextureInfo = new Vector4(
                baseTextureIndex,
                baseTextureIndex >= 0 ? 1.0f : 0.0f,
                emissiveTextureIndex,
                emissiveTextureIndex >= 0 ? 1.0f : 0.0f);
            TextureInfo2 = new Vector4(
                metallicRoughnessTextureIndex,
                metallicRoughnessTextureIndex >= 0 ? 1.0f : 0.0f,
                normalTextureIndex,
                normalTextureIndex >= 0 ? 1.0f : 0.0f);
        }

        private static int TextureIndex(IReadOnlyDictionary<TextureMap, int> textureIds, TextureMap? texture)
        {
            if (texture == null)
                return -1;
            return textureIds.TryGetValue(texture, out int textureIndex) ? textureIndex : -1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GpuTextureInfo
    {
        public readonly Vector4 Data; // x=pixel offset, y=width, z=height, w=reserved

        public GpuTextureInfo(int pixelOffset, int width, int height)
        {
            Data = new Vector4(pixelOffset, width, height, 0.0f);
        }
    }

    private sealed class TextureBuildResult
    {
        public required Dictionary<TextureMap, int> TextureIds { get; init; }
        public required uint[] Pixels { get; init; }
        public required GpuTextureInfo[] Infos { get; init; }
        public required int TextureCount { get; init; }
    }




    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GpuBvhNode
    {
        public readonly Vector4 BoundsMin;
        public readonly Vector4 BoundsMax;
        // x=left child or first triangle, y=triangle count (0 for internal), z=right child, w=reserved
        public readonly Vector4 Data;

        public GpuBvhNode(Vector3 boundsMin, Vector3 boundsMax, int leftOrFirst, int triangleCount, int rightChild)
        {
            BoundsMin = new Vector4(boundsMin, 0.0f);
            BoundsMax = new Vector4(boundsMax, 0.0f);
            Data = new Vector4(leftOrFirst, triangleCount, rightChild, 0.0f);
        }
    }

    private struct BvhPrimitive
    {
        // Keep BVH build records inline in one array. A class-per-triangle adds
        // object and reference overhead that becomes substantial on multi-million
        // triangle scenes.
        public int SourceIndex;
        public Vector3 Min;
        public Vector3 Max;
        public Vector3 Centroid;
    }

    private static readonly IComparer<BvhPrimitive>[] BvhAxisComparers =
    [
        Comparer<BvhPrimitive>.Create((a, b) => a.Centroid.X.CompareTo(b.Centroid.X)),
        Comparer<BvhPrimitive>.Create((a, b) => a.Centroid.Y.CompareTo(b.Centroid.Y)),
        Comparer<BvhPrimitive>.Create((a, b) => a.Centroid.Z.CompareTo(b.Centroid.Z))
    ];

    private sealed class BvhBuildResult
    {
        public required GpuTriangle[] Triangles { get; init; }
        public required GpuBvhNode[] Nodes { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GpuLight
    {
        public readonly Vector4 PositionKind;   // xyz=position, w=0 point / 1 directional / 2 spot
        public readonly Vector4 DirectionRange; // xyz=direction light travels, w=range
        public readonly Vector4 ColorIntensity; // rgb=color, w=intensity
        public readonly Vector4 ConeShadow;     // x=cos inner, y=cos outer, z=castsShadow, w=enabled

        public GpuLight(SceneLight light)
        {
            float kind = light.Kind switch
            {
                SceneLightKind.Directional => 1.0f,
                SceneLightKind.Spot => 2.0f,
                _ => 0.0f
            };

            PositionKind = ToVector4(light.Position, kind);
            DirectionRange = ToVector4(light.Direction.Normalize(), (float)light.Range);
            ColorIntensity = ToVector4(light.Color, (float)light.Intensity);
            ConeShadow = new Vector4(
                (float)Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)),
                (float)Math.Cos(light.OuterConeAngle),
                light.CastsShadow ? 1.0f : 0.0f,
                light.Enabled ? 1.0f : 0.0f);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CameraConstants
    {
        public readonly Vector4 CameraPosition;
        public readonly Vector4 CameraForward;
        public readonly Vector4 CameraRight;
        public readonly Vector4 CameraUp;
        public readonly uint Width;
        public readonly uint Height;
        public readonly uint TriangleCount;
        public readonly uint LightCount;
        public readonly uint BounceCount;
        public readonly uint SampleIndex;
        public readonly uint TileYOffset;
        public readonly uint TileHeight;
        public readonly uint BvhNodeCount;
        public readonly uint SampleCount;
        public readonly uint Reserved1;
        public readonly uint Reserved2;
        public readonly Vector4 BackgroundTopFov;
        public readonly Vector4 BackgroundBottomExposure;
        public readonly Vector4 LightingOptions;

        public CameraConstants(
            Vec3 position,
            CameraBasis basis,
            int width,
            int height,
            int triangleCount,
            int lightCount,
            int bounceCount,
            int sampleIndex,
            int tileYOffset,
            int tileHeight,
            int bvhNodeCount,
            int sampleCount,
            RenderSettings settings,
            double fieldOfViewDegrees)
        {
            CameraPosition = ToVector4(position, 1.0f);
            CameraForward = ToVector4(basis.Forward, 0.0f);
            CameraRight = ToVector4(basis.Right, 0.0f);
            CameraUp = ToVector4(basis.Up, 0.0f);
            Width = checked((uint)width);
            Height = checked((uint)height);
            TriangleCount = checked((uint)triangleCount);
            LightCount = checked((uint)lightCount);
            BounceCount = checked((uint)Math.Clamp(bounceCount, 0, 8));
            SampleIndex = checked((uint)Math.Max(0, sampleIndex));
            TileYOffset = checked((uint)Math.Max(0, tileYOffset));
            TileHeight = checked((uint)Math.Max(1, tileHeight));
            BvhNodeCount = checked((uint)Math.Max(0, bvhNodeCount));
            SampleCount = checked((uint)Math.Clamp(sampleCount, 1, 4096));
            Reserved1 = 0;
            Reserved2 = 0;

            double clampedFov = Math.Clamp(fieldOfViewDegrees, 1.0, 179.0);
            float fovScale = (float)Math.Tan(clampedFov * Math.PI / 360.0);
            BackgroundTopFov = new Vector4(
                (float)Math.Max(0.0, settings.BackgroundTop.X),
                (float)Math.Max(0.0, settings.BackgroundTop.Y),
                (float)Math.Max(0.0, settings.BackgroundTop.Z),
                fovScale);
            BackgroundBottomExposure = new Vector4(
                (float)Math.Max(0.0, settings.BackgroundBottom.X),
                (float)Math.Max(0.0, settings.BackgroundBottom.Y),
                (float)Math.Max(0.0, settings.BackgroundBottom.Z),
                (float)Math.Clamp(settings.Exposure, 0.01, 100.0));
            LightingOptions = new Vector4(
                (float)Math.Clamp(settings.AmbientStrength, 0.0, 100.0),
                settings.UseShadows ? 1.0f : 0.0f,
                0.0f,
                0.0f);
        }
    }

    /// <summary>Tries to render the scene with Vulkan compute. Throws on unsupported systems; this renderer never falls back to CPU rendering.</summary>
    public static RenderImage Render(
        Scene scene,
        Vec3 cameraPosition,
        CameraBasis basis,
        int width,
        int height,
        int bounceCount,
        int sampleIndex,
        int sampleCount,
        CancellationToken cancellationToken,
        out string details,
        Action<RenderImage, string>? progressCallback = null,
        RenderSettings? settings = null,
        double fieldOfViewDegrees = 72.0)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Vulkan render target dimensions must be positive.");
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "Vulkan sample count must be positive.");

        sampleCount = Math.Clamp(sampleCount, 1, 4096);
        settings ??= new RenderSettings
        {
            Width = width,
            Height = height,
            PathBounceCount = bounceCount,
            Backend = RenderBackend.VulkanGpu
        };
        fieldOfViewDegrees = Math.Clamp(fieldOfViewDegrees, 1.0, 179.0);
        ResetStageLog(width, height, bounceCount, sampleIndex);
        Stage($"Sample batch: start={sampleIndex}, count={sampleCount}");
        Stage("Render entered");
        StageMemory("start");

        Stage("Build texture buffers");
        ThrowIfCancellationRequested(cancellationToken, "Build texture buffers");
        TextureBuildResult textureBuild = BuildTextureBuffers(scene);
        Stage($"Texture buffers built: textures={textureBuild.TextureCount}, pixels={textureBuild.Pixels.Length}, infos={textureBuild.Infos.Length}");

        Stage("Build triangle buffer");
        ThrowIfCancellationRequested(cancellationToken, "Build triangle buffer");
        GpuTriangle[] sourceTriangles = BuildTriangleBuffer(scene, textureBuild.TextureIds, out int sourceTriangleCount);
        if (sourceTriangles.Length == 0)
            throw new InvalidOperationException("Vulkan scene render requires at least one triangle.");

        // Everything needed from the editable/world scene is now packed.
        // Do not mutate the caller-owned scene here: the progressive Vulkan
        // accumulation path can invoke Render(...) multiple times with the same
        // scene snapshot. Clearing its triangles would make the second sample
        // batch fail with an empty scene.
        textureBuild.TextureIds.Clear();
        StageMemory("after source triangle packing");

        Stage("Build traversal BVH for GPU");
        BvhBuildResult bvh = BuildGpuBvh(sourceTriangles);
        GpuTriangle[] triangles = bvh.Triangles;
        GpuBvhNode[] bvhNodes = bvh.Nodes;
        // Release the unordered source triangle array as soon as the ordered
        // GPU upload buffer has been produced. This lowers peak managed memory
        // for large scenes before Vulkan buffers are allocated.
        sourceTriangles = Array.Empty<GpuTriangle>();
        StageMemory("after BVH build and source triangle release");
        Stage($"Triangle buffer built: source={sourceTriangleCount}, uploaded={triangles.Length}");
        Stage($"Triangle index path: uint32, no 65,535 cap");
        Stage($"GPU traversal BVH built: nodes={bvhNodes.Length}");
        Stage("Build light buffer");
        ThrowIfCancellationRequested(cancellationToken, "Build light buffer");
        GpuLight[] lights = BuildLightBuffer(scene, out int sourceLightCount);
        Stage($"Light buffer built: source={sourceLightCount}, uploaded={lights.Length}");

        uint outputBytes = checked((uint)((long)width * height * 4L));
        int triangleStride = Marshal.SizeOf<GpuTriangle>();
        int bvhStride = Marshal.SizeOf<GpuBvhNode>();
        int lightStride = Marshal.SizeOf<GpuLight>();
        int constantsStride = Marshal.SizeOf<CameraConstants>();
        int textureInfoStride = Marshal.SizeOf<GpuTextureInfo>();
        uint triangleBytes = checked((uint)(triangles.LongLength * triangleStride));
        uint bvhBytes = checked((uint)(Math.Max(1, bvhNodes.LongLength) * bvhStride));
        uint lightBytes = checked((uint)(Math.Max(1, lights.LongLength) * lightStride));
        uint texturePixelBytes = checked((uint)(Math.Max(1, textureBuild.Pixels.LongLength) * sizeof(uint)));
        uint textureInfoBytes = checked((uint)(Math.Max(1, textureBuild.Infos.LongLength) * textureInfoStride));
        uint constantBytes = checked((uint)constantsStride);
        Stage($"GPU buffer sizes: output={outputBytes} bytes, triangles={triangleBytes} bytes ({triangles.Length} x {triangleStride}), bvh={bvhBytes} bytes ({bvhNodes.Length} x {bvhStride}), lights={lightBytes} bytes ({lights.Length} x {lightStride}), texturePixels={texturePixelBytes} bytes, textureInfos={textureInfoBytes} bytes ({textureBuild.Infos.Length} x {textureInfoStride}), constants={constantBytes} bytes");
        double estimatedUploadBytes = (double)triangleBytes + bvhBytes + lightBytes + texturePixelBytes + textureInfoBytes + outputBytes;
        Stage($"Estimated Vulkan CPU-side upload memory: {estimatedUploadBytes / (1024.0 * 1024.0):F1} MB before driver copies");

        ThrowIfCancellationRequested(cancellationToken, "before shared Vulkan device");
        GraphicsDevice gd = GetOrCreateSharedDevice();
        ResourceFactory factory = gd.ResourceFactory;

        Stage("Create output buffer");
        ThrowIfCancellationRequested(cancellationToken, "Create output buffer");
        using DeviceBuffer outputBuffer = factory.CreateBuffer(new BufferDescription(
            outputBytes,
            BufferUsage.StructuredBufferReadWrite,
            structureByteStride: 4));

        Stage("Create triangle buffer");
        using DeviceBuffer triangleBuffer = factory.CreateBuffer(new BufferDescription(
            triangleBytes,
            BufferUsage.StructuredBufferReadOnly,
            structureByteStride: (uint)Marshal.SizeOf<GpuTriangle>()));

        Stage("Create BVH buffer");
        using DeviceBuffer bvhBuffer = factory.CreateBuffer(new BufferDescription(
            bvhBytes,
            BufferUsage.StructuredBufferReadOnly,
            structureByteStride: (uint)Marshal.SizeOf<GpuBvhNode>()));

        Stage("Create light buffer");
        using DeviceBuffer lightBuffer = factory.CreateBuffer(new BufferDescription(
            lightBytes,
            BufferUsage.StructuredBufferReadOnly,
            structureByteStride: (uint)Marshal.SizeOf<GpuLight>()));

        Stage("Create texture pixel buffer");
        using DeviceBuffer texturePixelBuffer = factory.CreateBuffer(new BufferDescription(
            texturePixelBytes,
            BufferUsage.StructuredBufferReadOnly,
            structureByteStride: sizeof(uint)));

        Stage("Create texture info buffer");
        using DeviceBuffer textureInfoBuffer = factory.CreateBuffer(new BufferDescription(
            textureInfoBytes,
            BufferUsage.StructuredBufferReadOnly,
            structureByteStride: (uint)Marshal.SizeOf<GpuTextureInfo>()));

        Stage("Create constants buffer");
        using DeviceBuffer constantsBuffer = factory.CreateBuffer(new BufferDescription(
            constantBytes,
            BufferUsage.UniformBuffer));

        Stage("Create staging readback buffer");
        using DeviceBuffer stagingBuffer = factory.CreateBuffer(new BufferDescription(
            outputBytes,
            BufferUsage.Staging));

        Stage("Upload triangle buffer");
        ThrowIfCancellationRequested(cancellationToken, "Upload triangle buffer");
        gd.UpdateBuffer(triangleBuffer, 0, triangles);
        Stage("Upload BVH buffer");
        gd.UpdateBuffer(bvhBuffer, 0, bvhNodes.Length == 0 ? new[] { default(GpuBvhNode) } : bvhNodes);
        Stage("Upload light buffer");
        gd.UpdateBuffer(lightBuffer, 0, lights.Length == 0 ? new[] { default(GpuLight) } : lights);
        Stage("Upload texture buffers");
        gd.UpdateBuffer(texturePixelBuffer, 0, textureBuild.Pixels.Length == 0 ? new uint[] { 0xffffffffu } : textureBuild.Pixels);
        gd.UpdateBuffer(textureInfoBuffer, 0, textureBuild.Infos.Length == 0 ? new[] { new GpuTextureInfo(0, 1, 1) } : textureBuild.Infos);
        StageMemory("after GPU buffer upload");
        Stage("Initialize output buffer");
        ZeroOutputBuffer(gd, outputBuffer, outputBytes);

        Stage("Get shared compute pipeline");
        ThrowIfCancellationRequested(cancellationToken, "Get shared compute pipeline");
        SharedComputeResources computeResources = GetOrCreateSharedComputeResources(gd);
        ResourceLayout layout = computeResources.Layout;
        Pipeline pipeline = computeResources.Pipeline;

        Stage("Create resource set");
        using ResourceSet resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            layout,
            outputBuffer,
            triangleBuffer,
            bvhBuffer,
            lightBuffer,
            constantsBuffer,
            texturePixelBuffer,
            textureInfoBuffer));

        // Submit the compute shader in row tiles.  When no per-tile preview is
        // needed, batch all tile dispatches into one command-list submission and
        // wait only once.  The previous implementation called WaitForIdle after
        // every tile, which serialized the GPU and made the Vulkan path slower
        // than the CPU path on many scenes.
        int uploadedTriangleCount = triangles.Length;
        int uploadedBvhNodeCount = bvhNodes.Length;
        int uploadedLightCount = lights.Length;
        int tileRows = ChooseTileRows(width, height, uploadedTriangleCount, bounceCount, uploadedLightCount, sampleCount);
        Stage($"Dispatch compute shader in row tiles: tileRows={tileRows}");

        if (progressCallback == null)
        {
            DispatchTilesBatched(
                gd,
                factory,
                pipeline,
                layout,
                outputBuffer,
                triangleBuffer,
                bvhBuffer,
                lightBuffer,
                texturePixelBuffer,
                textureInfoBuffer,
                width,
                height,
                uploadedTriangleCount,
                uploadedBvhNodeCount,
                uploadedLightCount,
                tileRows,
                cameraPosition,
                basis,
                bounceCount,
                sampleIndex,
                sampleCount,
                constantBytes,
                settings,
                fieldOfViewDegrees,
                cancellationToken);
        }
        else
        {
            DispatchTilesWithPreview(
                gd,
                factory,
                pipeline,
                resourceSet,
                constantsBuffer,
                outputBuffer,
                stagingBuffer,
                outputBytes,
                width,
                height,
                uploadedTriangleCount,
                uploadedBvhNodeCount,
                uploadedLightCount,
                tileRows,
                cameraPosition,
                basis,
                bounceCount,
                sampleIndex,
                sampleCount,
                settings,
                fieldOfViewDegrees,
                cancellationToken,
                progressCallback);
        }

        Stage("All compute tiles completed");
        triangles = Array.Empty<GpuTriangle>();
        bvhNodes = Array.Empty<GpuBvhNode>();
        lights = Array.Empty<GpuLight>();
        StageMemory("after managed scene arrays released before final readback");

        Stage("Copy output buffer to staging buffer");
        RenderImage image = ReadBackOutputImage(gd, factory, outputBuffer, stagingBuffer, outputBytes, width, height, cancellationToken, "final readback");
        string triangleTruncated = sourceTriangleCount > uploadedTriangleCount ? $", triangles truncated from {sourceTriangleCount}" : string.Empty;
        string lightTruncated = sourceLightCount > uploadedLightCount ? $", lights truncated from {sourceLightCount}" : string.Empty;
        details = $"VULKAN GPU COMPUTE BVH TRACE - {width}x{height}, {uploadedTriangleCount} triangles{triangleTruncated}, {uploadedBvhNodeCount} BVH nodes, {uploadedLightCount} lights{lightTruncated}, textures={textureBuild.TextureCount}, material=uv+mr+normal, bounces={Math.Clamp(bounceCount, 0, 8)}, samples={sampleIndex + 1}-{sampleIndex + sampleCount}, fov={fieldOfViewDegrees:0.##}, exposure={settings.Exposure:0.###}, ambient={settings.AmbientStrength:0.###}, shadows={settings.UseShadows}, tileRows={tileRows}";
        Stage("Render completed successfully");
        return image;
    }

    private static void DispatchTilesBatched(
        GraphicsDevice gd,
        ResourceFactory factory,
        Pipeline pipeline,
        ResourceLayout layout,
        DeviceBuffer outputBuffer,
        DeviceBuffer triangleBuffer,
        DeviceBuffer bvhBuffer,
        DeviceBuffer lightBuffer,
        DeviceBuffer texturePixelBuffer,
        DeviceBuffer textureInfoBuffer,
        int width,
        int height,
        int triangleCount,
        int bvhNodeCount,
        int lightCount,
        int tileRows,
        Vec3 cameraPosition,
        CameraBasis basis,
        int bounceCount,
        int sampleIndex,
        int sampleCount,
        uint constantBytes,
        RenderSettings settings,
        double fieldOfViewDegrees,
        CancellationToken cancellationToken)
    {
        int tilesPerSubmit = ChooseTilesPerSubmit(tileRows, bounceCount, sampleCount, triangleCount, width, lightCount);
        Stage($"Batch Vulkan tile dispatches in bounded submissions: tileRows={tileRows}, tilesPerSubmit={tilesPerSubmit}");

        for (int tileY = 0; tileY < height;)
        {
            List<(DeviceBuffer Constants, ResourceSet Set, int TileY, int Rows)> tileResources = new();
            try
            {
                for (int tileIndex = 0; tileIndex < tilesPerSubmit && tileY < height; tileIndex++, tileY += tileRows)
                {
                    ThrowIfCancellationRequested(cancellationToken, $"Prepare batched tile y={tileY}");
                    int currentTileRows = Math.Min(tileRows, height - tileY);
                    DeviceBuffer tileConstants = factory.CreateBuffer(new BufferDescription(constantBytes, BufferUsage.UniformBuffer));
                    gd.UpdateBuffer(tileConstants, 0, new CameraConstants(cameraPosition, basis, width, height, triangleCount, lightCount, bounceCount, sampleIndex, tileY, currentTileRows, bvhNodeCount, sampleCount, settings, fieldOfViewDegrees));
                    ResourceSet tileSet = factory.CreateResourceSet(new ResourceSetDescription(
                        layout,
                        outputBuffer,
                        triangleBuffer,
                        bvhBuffer,
                        lightBuffer,
                        tileConstants,
                        texturePixelBuffer,
                        textureInfoBuffer));
                    tileResources.Add((tileConstants, tileSet, tileY, currentTileRows));
                }

                using CommandList commandList = factory.CreateCommandList();
                commandList.Begin();
                commandList.SetPipeline(pipeline);
                foreach (var tile in tileResources)
                {
                    ThrowIfCancellationRequested(cancellationToken, $"Record batched tile y={tile.TileY}");
                    commandList.SetComputeResourceSet(0, tile.Set);
                    commandList.Dispatch(((uint)width + 7u) / 8u, ((uint)tile.Rows + 7u) / 8u, 1u);
                }
                commandList.End();

                int firstTileY = tileResources.Count == 0 ? tileY : tileResources[0].TileY;
                int lastTileY = tileResources.Count == 0 ? tileY : tileResources[^1].TileY;
                Stage($"Submit bounded Vulkan tile batch: count={tileResources.Count}, y={firstTileY}-{lastTileY}");
                gd.SubmitCommands(commandList);
                gd.WaitForIdle();
                Stage("Bounded Vulkan tile batch idle returned");
            }
            finally
            {
                for (int i = tileResources.Count - 1; i >= 0; i--)
                {
                    try { tileResources[i].Set.Dispose(); } catch { }
                    try { tileResources[i].Constants.Dispose(); } catch { }
                }
            }
        }
    }

    private static int ChooseTilesPerSubmit(int tileRows, int bounceCount, int sampleCount, int triangleCount, int width, int lightCount)
    {
        // tileRows limits the size of one dispatch.  It does not limit the size
        // of a command-list submission when every tile is recorded into a single
        // command list.  Deep-bounce scenes can therefore still hit a driver
        // watchdog and come back black even though each individual dispatch is
        // small.  Bound the number of tile dispatches per submit as well.
        long directShadowRays = Math.Min(Math.Max(0L, lightCount), 4L);
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + directShadowRays;
        long approximateNodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        long workPerTile = Math.Max(1L, width) * Math.Max(1L, tileRows) * approximateNodeVisits * raysPerPixel * Math.Max(1L, sampleCount);

        const long targetWorkPerSubmit = 96_000_000L;
        int computed = (int)Math.Clamp(targetWorkPerSubmit / Math.Max(1L, workPerTile), 1L, 8L);

        if (bounceCount >= 6)
            computed = Math.Min(computed, 1);
        else if (sampleCount > 1 || bounceCount >= 3)
            computed = Math.Min(computed, 2);

        return Math.Max(1, computed);
    }

    private static void DispatchTilesWithPreview(
        GraphicsDevice gd,
        ResourceFactory factory,
        Pipeline pipeline,
        ResourceSet resourceSet,
        DeviceBuffer constantsBuffer,
        DeviceBuffer outputBuffer,
        DeviceBuffer stagingBuffer,
        uint outputBytes,
        int width,
        int height,
        int triangleCount,
        int bvhNodeCount,
        int lightCount,
        int tileRows,
        Vec3 cameraPosition,
        CameraBasis basis,
        int bounceCount,
        int sampleIndex,
        int sampleCount,
        RenderSettings settings,
        double fieldOfViewDegrees,
        CancellationToken cancellationToken,
        Action<RenderImage, string> progressCallback)
    {
        for (int tileY = 0; tileY < height; tileY += tileRows)
        {
            ThrowIfCancellationRequested(cancellationToken, $"Dispatch tile y={tileY}");
            int currentTileRows = Math.Min(tileRows, height - tileY);
            gd.UpdateBuffer(constantsBuffer, 0, new CameraConstants(cameraPosition, basis, width, height, triangleCount, lightCount, bounceCount, sampleIndex, tileY, currentTileRows, bvhNodeCount, sampleCount, settings, fieldOfViewDegrees));

            using CommandList tileCommandList = factory.CreateCommandList();
            tileCommandList.Begin();
            tileCommandList.SetPipeline(pipeline);
            tileCommandList.SetComputeResourceSet(0, resourceSet);
            tileCommandList.Dispatch(((uint)width + 7u) / 8u, ((uint)currentTileRows + 7u) / 8u, 1u);
            tileCommandList.End();

            gd.SubmitCommands(tileCommandList);
            gd.WaitForIdle();

            bool shouldPublishTile = ShouldPublishTileProgress(tileY, currentTileRows, tileRows, height, enabled: true, width, triangleCount);
            if ((tileY / Math.Max(1, tileRows)) % 8 == 0)
                Stage($"Tile completed: y={tileY}, rows={currentTileRows}");

            if (shouldPublishTile)
            {
                Stage($"Publish partial Vulkan tile preview: y={tileY}, rows={currentTileRows}");
                RenderImage partial = ReadBackOutputImage(gd, factory, outputBuffer, stagingBuffer, outputBytes, width, height, cancellationToken, "partial tile readback");
                progressCallback(partial, $"VULKAN GPU - tile {Math.Min(height, tileY + currentTileRows)}/{height}, samples {sampleIndex + 1}-{sampleIndex + sampleCount}");
            }
        }
    }

    private static bool ShouldPublishTileProgress(int tileY, int currentTileRows, int tileRows, int height, bool enabled, int width, int triangleCount)
    {
        if (!enabled)
            return false;

        int completedRows = tileY + currentTileRows;
        if (completedRows >= height)
            return true;

        // Large scenes/resolutions caused very high apparent memory usage because
        // every partial preview creates a full Bitmap and hands it to the UI.
        // For heavy scenes, only publish the final tile for each sample.
        long pixelCount = (long)Math.Max(1, width) * Math.Max(1, height);
        if (triangleCount > 50000 || pixelCount > 1_000_000)
            return false;

        if (tileY == 0)
            return true;

        // Publish about 8 partial images per sample for modest scenes.
        int publishEveryRows = Math.Max(Math.Max(1, tileRows), Math.Max(1, height / 8));
        return completedRows / publishEveryRows != tileY / publishEveryRows;
    }

    private static void ZeroOutputBuffer(GraphicsDevice gd, DeviceBuffer outputBuffer, uint outputBytes)
    {
        // Avoid allocating one giant byte[width * height * 4] just to clear the
        // output.  Update the GPU buffer in small reusable chunks instead.
        const int ChunkBytes = 1024 * 1024;
        byte[] zeroChunk = new byte[Math.Min(ChunkBytes, checked((int)Math.Min(outputBytes, int.MaxValue)))];
        uint offset = 0;
        while (offset < outputBytes)
        {
            int count = checked((int)Math.Min((uint)zeroChunk.Length, outputBytes - offset));
            if (count == zeroChunk.Length)
            {
                gd.UpdateBuffer(outputBuffer, offset, zeroChunk);
            }
            else
            {
                // Last chunk only; keep this allocation below 1 MB.
                gd.UpdateBuffer(outputBuffer, offset, new byte[count]);
            }
            offset += (uint)count;
        }
    }

    private static RenderImage ReadBackOutputImage(GraphicsDevice gd, ResourceFactory factory, DeviceBuffer outputBuffer, DeviceBuffer stagingBuffer, uint outputBytes, int width, int height, CancellationToken cancellationToken, string stageName)
    {
        Stage($"Copy output buffer to staging buffer ({stageName})");
        using (CommandList copyCommandList = factory.CreateCommandList())
        {
            copyCommandList.Begin();
            copyCommandList.CopyBuffer(outputBuffer, 0, stagingBuffer, 0, outputBytes);
            copyCommandList.End();
            gd.SubmitCommands(copyCommandList);
            gd.WaitForIdle();
        }
        Stage($"GPU copy/readback idle returned ({stageName})");

        ThrowIfCancellationRequested(cancellationToken, "Map staging buffer for readback: " + stageName);
        MappedResourceView<uint> mapped = gd.Map<uint>(stagingBuffer, MapMode.Read);
        try
        {
            Stage("Convert readback to bitmap: " + stageName);
            return ToRenderImage(mapped, width, height);
        }
        finally
        {
            gd.Unmap(stagingBuffer);
        }
    }

    private static SharedComputeResources GetOrCreateSharedComputeResources(GraphicsDevice gd)
    {
        lock (DeviceSync)
        {
            if (sharedComputeResources != null)
            {
                Stage("Reuse shared Vulkan compute shader/layout/pipeline");
                return sharedComputeResources;
            }

            ResourceFactory factory = gd.ResourceFactory;
            Stage("Locate compute shader source");
            string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "vulkan_scene_primary.comp");
            if (!File.Exists(shaderPath))
                shaderPath = Path.Combine("Shaders", "vulkan_scene_primary.comp");
            if (!File.Exists(shaderPath))
                throw new FileNotFoundException("Vulkan scene compute shader was not found.", shaderPath);

            Stage($"Read compute shader: {shaderPath}");
            string shaderSource = File.ReadAllText(shaderPath);
            ShaderDescription computeDesc = new(
                ShaderStages.Compute,
                Encoding.UTF8.GetBytes(shaderSource),
                "main");

            Stage("Compile GLSL to SPIR-V / create shared shader");
            Shader shader = CreateComputeShaderWithDiagnostics(factory, computeDesc, shaderPath, shaderSource);

            try
            {
                Stage("Create shared resource layout");
                ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("OutputBuffer", ResourceKind.StructuredBufferReadWrite, ShaderStages.Compute),
                    new ResourceLayoutElementDescription("TriangleBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    new ResourceLayoutElementDescription("BvhBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    new ResourceLayoutElementDescription("LightBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    new ResourceLayoutElementDescription("CameraConstants", ResourceKind.UniformBuffer, ShaderStages.Compute),
                    new ResourceLayoutElementDescription("TexturePixelBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
                    new ResourceLayoutElementDescription("TextureInfoBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute)));

                try
                {
                    Stage("Create shared compute pipeline");
                    Pipeline pipeline = factory.CreateComputePipeline(new ComputePipelineDescription(
                        shader,
                        layout,
                        8,
                        8,
                        1));

                    sharedComputeResources = new SharedComputeResources
                    {
                        Layout = layout,
                        Shader = shader,
                        Pipeline = pipeline
                    };
                    Stage("Shared Vulkan compute resources created");
                    return sharedComputeResources;
                }
                catch
                {
                    layout.Dispose();
                    throw;
                }
            }
            catch
            {
                shader.Dispose();
                throw;
            }
        }
    }

    private static Shader CreateComputeShaderWithDiagnostics(ResourceFactory factory, ShaderDescription computeDesc, string shaderPath, string shaderSource)
    {
        try
        {
            Stage($"Shader source length: {shaderSource.Length} chars");
            Stage("Shader first line: " + (shaderSource.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<empty>"));
            Shader shader = factory.CreateFromSpirv(computeDesc);
            Stage("Shader compile/create succeeded");
            return shader;
        }
        catch (Exception ex)
        {
            Stage("Shader compile/create failed");
            Stage("Shader path: " + shaderPath);
            Stage("Shader exception type: " + ex.GetType().FullName);
            Stage("Shader exception message: " + ex.Message);
            Stage("Shader exception full text:");
            Stage(ex.ToString());

            try
            {
                string diagnosticShaderCopy = Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-failed-shader.comp");
                File.WriteAllText(diagnosticShaderCopy, shaderSource);
                Stage("Failed shader source copied to: " + diagnosticShaderCopy);
            }
            catch (Exception copyEx)
            {
                Stage("Could not copy failed shader source: " + copyEx.GetType().Name + ": " + copyEx.Message);
            }

            throw new InvalidOperationException(
                "Vulkan compute shader compilation failed. See " + StageLogPath + " and " + Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-failed-shader.comp") + " for details.",
                ex);
        }
    }

    private static int ChooseTileRows(int width, int height, int triangleCount, int bounceCount, int lightCount, int sampleCount)
    {
        // The GPU shader traverses a BVH, so it can use larger tiles than the
        // original brute-force shader.  However, sample batching multiplies the
        // work inside every dispatch.  The previous performance patch chose 168
        // rows for a 899x672 / 8-bounce / 8-sample batch, which can make Vulkan
        // return an all-black buffer on watchdog-sensitive drivers.  Include the
        // sample count in the scheduler so each dispatch stays bounded.
        long directShadowRays = Math.Min(Math.Max(0L, lightCount), 4L);
        long raysPerPixel = Math.Max(1L, bounceCount + 1L) + directShadowRays;
        long approximateNodeVisits = Math.Max(16L, (long)Math.Ceiling(Math.Log(Math.Max(2, triangleCount), 2.0)) * 10L);
        long samples = Math.Max(1L, sampleCount);
        long workPerRow = Math.Max(1L, width) * approximateNodeVisits * raysPerPixel * samples;

        // Target a modest per-dispatch workload.  Command-list batching still
        // removes most WaitForIdle overhead, so many small dispatches are safer
        // than one watchdog-prone giant dispatch.
        const long targetWorkPerDispatch = 64_000_000L;
        long computedRows = Math.Max(1L, targetWorkPerDispatch / Math.Max(1L, workPerRow));

        long maxRows = 128L;
        if (bounceCount >= 6 || samples >= 4)
            maxRows = 32L;
        if (bounceCount >= 6 && samples >= 4)
            maxRows = 16L;

        int rows = (int)Math.Clamp(computedRows, 1L, maxRows);
        return Math.Min(Math.Max(1, rows), Math.Max(1, height));
    }


    private static BvhBuildResult BuildGpuBvh(GpuTriangle[] inputTriangles)
    {
        // Match the CPU BVH's conservative triangle bounds.  The CPU Aabb.Around()
        // pads every triangle by 1e-5, but the first Vulkan BVH implementation
        // used exact float min/max.  Exact bounds are risky for thin, flat, or
        // diagonal triangles: the shader can reject the BVH node in intersectAabb()
        // before intersectTriangle() ever runs.  On cylinder/quad meshes this can
        // look exactly like every other half-triangle of a face is missing.
        float boundsPad = ComputeGpuBoundsPad(inputTriangles);
        Vector3 pad = new(boundsPad);

        BvhPrimitive[] primitives = new BvhPrimitive[inputTriangles.Length];
        for (int triangleIndex = 0; triangleIndex < inputTriangles.Length; triangleIndex++)
        {
            GpuTriangle tri = inputTriangles[triangleIndex];
            Vector3 a = new(tri.A.X, tri.A.Y, tri.A.Z);
            Vector3 b = new(tri.B.X, tri.B.Y, tri.B.Z);
            Vector3 c = new(tri.C.X, tri.C.Y, tri.C.Z);
            primitives[triangleIndex] = new BvhPrimitive
            {
                SourceIndex = triangleIndex,
                Min = Vector3.Min(a, Vector3.Min(b, c)) - pad,
                Max = Vector3.Max(a, Vector3.Max(b, c)) + pad,
                Centroid = (a + b + c) / 3.0f
            };
        }

        Stage($"GPU BVH conservative triangle bounds pad={boundsPad:G6}");

        GpuTriangle[] orderedTriangles = new GpuTriangle[inputTriangles.Length];
        GpuBvhNode[] nodes = new GpuBvhNode[CountGpuBvhNodes(inputTriangles.Length)];
        int orderedTriangleCount = 0;
        int nodeCount = 0;
        BuildGpuBvhRecursive(
            primitives,
            0,
            primitives.Length,
            inputTriangles,
            orderedTriangles,
            ref orderedTriangleCount,
            nodes,
            ref nodeCount);

        if (orderedTriangleCount != orderedTriangles.Length || nodeCount != nodes.Length)
            throw new InvalidOperationException($"Internal BVH packing mismatch: triangles={orderedTriangleCount}/{orderedTriangles.Length}, nodes={nodeCount}/{nodes.Length}.");

        return new BvhBuildResult
        {
            Triangles = orderedTriangles,
            Nodes = nodes
        };
    }

    private static int BuildGpuBvhRecursive(
        BvhPrimitive[] primitives,
        int start,
        int count,
        GpuTriangle[] sourceTriangles,
        GpuTriangle[] orderedTriangles,
        ref int orderedTriangleCount,
        GpuBvhNode[] nodes,
        ref int nodeCount)
    {
        int nodeIndex = nodeCount++;
        GetBounds(primitives, start, count, out Vector3 boundsMin, out Vector3 boundsMax, out Vector3 centroidMin, out Vector3 centroidMax);

        const int LeafSize = 4;
        if (count <= LeafSize)
        {
            int firstTriangle = orderedTriangleCount;
            for (int i = start; i < start + count; i++)
                orderedTriangles[orderedTriangleCount++] = sourceTriangles[primitives[i].SourceIndex];

            nodes[nodeIndex] = new GpuBvhNode(boundsMin, boundsMax, firstTriangle, count, 0);
            return nodeIndex;
        }

        Vector3 extent = centroidMax - centroidMin;
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(primitives, start, count, BvhAxisComparers[axis]);

        int leftCount = count / 2;
        int rightCount = count - leftCount;
        int left = BuildGpuBvhRecursive(primitives, start, leftCount, sourceTriangles, orderedTriangles, ref orderedTriangleCount, nodes, ref nodeCount);
        int right = BuildGpuBvhRecursive(primitives, start + leftCount, rightCount, sourceTriangles, orderedTriangles, ref orderedTriangleCount, nodes, ref nodeCount);
        nodes[nodeIndex] = new GpuBvhNode(boundsMin, boundsMax, left, 0, right);
        return nodeIndex;
    }

    private static int CountGpuBvhNodes(int triangleCount)
    {
        Dictionary<int, int> cache = new();
        return Count(triangleCount);

        int Count(int count)
        {
            if (count <= 4) return 1;
            if (cache.TryGetValue(count, out int cached)) return cached;
            int leftCount = count / 2;
            int result = checked(1 + Count(leftCount) + Count(count - leftCount));
            cache[count] = result;
            return result;
        }
    }

    private static void GetBounds(BvhPrimitive[] primitives, int start, int count, out Vector3 boundsMin, out Vector3 boundsMax, out Vector3 centroidMin, out Vector3 centroidMax)
    {
        boundsMin = new Vector3(float.PositiveInfinity);
        boundsMax = new Vector3(float.NegativeInfinity);
        centroidMin = new Vector3(float.PositiveInfinity);
        centroidMax = new Vector3(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            BvhPrimitive p = primitives[i];
            boundsMin = Vector3.Min(boundsMin, p.Min);
            boundsMax = Vector3.Max(boundsMax, p.Max);
            centroidMin = Vector3.Min(centroidMin, p.Centroid);
            centroidMax = Vector3.Max(centroidMax, p.Centroid);
        }
    }

    private static float GetAxis(Vector3 value, int axis)
        => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

    private static float ComputeGpuBoundsPad(GpuTriangle[] triangles)
    {
        if (triangles.Length == 0)
            return 1e-4f;

        Vector3 globalMin = new(float.PositiveInfinity);
        Vector3 globalMax = new(float.NegativeInfinity);
        for (int i = 0; i < triangles.Length; i++)
        {
            GpuTriangle tri = triangles[i];
            Vector3 a = new(tri.A.X, tri.A.Y, tri.A.Z);
            Vector3 b = new(tri.B.X, tri.B.Y, tri.B.Z);
            Vector3 c = new(tri.C.X, tri.C.Y, tri.C.Z);
            globalMin = Vector3.Min(globalMin, Vector3.Min(a, Vector3.Min(b, c)));
            globalMax = Vector3.Max(globalMax, Vector3.Max(a, Vector3.Max(b, c)));
        }

        float sceneDiagonal = Vector3.Distance(globalMin, globalMax);
        if (!float.IsFinite(sceneDiagonal) || sceneDiagonal <= 0.0f)
            return 1e-4f;

        // Use a scale-aware pad so imported models with large coordinates also
        // keep enough tolerance after float conversion and GLSL slab math.
        return Math.Clamp(sceneDiagonal * 1e-6f, 1e-4f, 0.01f);
    }

    private static TextureBuildResult BuildTextureBuffers(Scene scene)
    {
        Dictionary<TextureMap, int> textureIds = new();
        List<GpuTextureInfo> infos = new();
        List<uint> pixels = new();

        foreach (Triangle triangle in scene.Triangles)
        {
            AddTexture(triangle.Material.Texture, textureIds, infos, pixels);
            AddTexture(triangle.Material.EmissiveTexture, textureIds, infos, pixels);
            AddTexture(triangle.Material.MetallicRoughnessTexture, textureIds, infos, pixels);
            AddTexture(triangle.Material.NormalTexture, textureIds, infos, pixels);
        }

        if (pixels.Count == 0)
            pixels.Add(0xffffffffu);
        if (infos.Count == 0)
            infos.Add(new GpuTextureInfo(0, 1, 1));

        return new TextureBuildResult
        {
            TextureIds = textureIds,
            Pixels = pixels.ToArray(),
            Infos = infos.ToArray(),
            TextureCount = textureIds.Count
        };
    }

    private static void AddTexture(TextureMap? texture, Dictionary<TextureMap, int> textureIds, List<GpuTextureInfo> infos, List<uint> pixels)
    {
        if (texture == null || textureIds.ContainsKey(texture))
            return;

        int pixelOffset = pixels.Count;
        uint[] texturePixels = texture.CopyPackedRgba32Pixels();
        if (texturePixels.Length == 0)
            return;

        textureIds.Add(texture, infos.Count);
        pixels.AddRange(texturePixels);
        infos.Add(new GpuTextureInfo(pixelOffset, texture.Width, texture.Height));
    }

    private static GpuTriangle[] BuildTriangleBuffer(Scene scene, IReadOnlyDictionary<TextureMap, int> textureIds, out int sourceTriangleCount)
    {
        sourceTriangleCount = scene.Triangles.Count;
        if (sourceTriangleCount > 65535)
            Stage($"Triangle count exceeds 65,535: {sourceTriangleCount}. Uploading full scene using uint32 counts/indices; no UInt16 triangle cap is applied.");

        // Do not clamp, Take(), or downcast the triangle count.  Large imported
        // scenes must upload every triangle; otherwise the GPU renderer shows
        // only partial geometry.
        GpuTriangle[] result = new GpuTriangle[sourceTriangleCount];
        for (int i = 0; i < sourceTriangleCount; i++)
            result[i] = new GpuTriangle(scene.Triangles[i], textureIds);
        return result;
    }

    private static GpuLight[] BuildLightBuffer(Scene scene, out int sourceLightCount)
    {
        sourceLightCount = scene.Lights.Count;
        int count = Math.Min(sourceLightCount, MaxGpuLights);
        List<GpuLight> result = new(count);
        for (int i = 0; i < count; i++)
        {
            SceneLight light = scene.Lights[i];
            if (light.Enabled)
                result.Add(new GpuLight(light));
        }
        return result.ToArray();
    }

    private static RenderImage ToRenderImage(MappedResourceView<uint> pixels, int width, int height)
    {
        uint[] copy = new uint[checked(width * height)];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = pixels[i];
        return new RenderImage(width, height, copy);
    }

    private static Vector4 ToVector4(Vec3 value, float w)
        => new((float)value.X, (float)value.Y, (float)value.Z, w);
}
