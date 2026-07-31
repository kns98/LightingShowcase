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
    private static readonly object RenderSync = new();
    private static readonly object DeviceSync = new();
    private static GraphicsDevice? sharedGraphicsDevice;
    private static SharedComputeResources? sharedComputeResources;
    private static PreparedComputeScene? preparedScene;
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

    private sealed class PreparedComputeScene : IDisposable
    {
        public required Scene Scene { get; init; }
        public required DeviceBuffer TriangleBuffer { get; init; }
        public required DeviceBuffer BvhBuffer { get; init; }
        public required DeviceBuffer LightBuffer { get; init; }
        public required DeviceBuffer TexturePixelBuffer { get; init; }
        public required DeviceBuffer TextureInfoBuffer { get; init; }
        public required int SourceTriangleCount { get; init; }
        public required int TriangleCount { get; init; }
        public required int BvhNodeCount { get; init; }
        public required int SourceLightCount { get; init; }
        public required int LightCount { get; init; }
        public required int TextureCount { get; init; }
        public required long SceneBufferBytes { get; init; }

        public void Dispose()
        {
            try { TextureInfoBuffer.Dispose(); } catch { }
            try { TexturePixelBuffer.Dispose(); } catch { }
            try { LightBuffer.Dispose(); } catch { }
            try { BvhBuffer.Dispose(); } catch { }
            try { TriangleBuffer.Dispose(); } catch { }
        }
    }

    public static void DisposeSharedDevice()
    {
        lock (RenderSync)
        {
            lock (DeviceSync)
            {
                GraphicsDevice? device = sharedGraphicsDevice;
                sharedGraphicsDevice = null;
                SharedComputeResources? computeResources = sharedComputeResources;
                sharedComputeResources = null;
                PreparedComputeScene? sceneResources = preparedScene;
                preparedScene = null;
                if (device == null)
                {
                    sceneResources?.Dispose();
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
                    Stage("Dispose prepared Vulkan compute scene");
                    sceneResources?.Dispose();
                }
                catch (Exception ex)
                {
                    Stage($"Dispose prepared Vulkan compute scene failed: {ex.GetType().Name}: {ex.Message}");
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
    }


    /// <summary>
    /// Releases scene-sized Vulkan buffers while preserving the shared device
    /// and pipeline. This avoids overlapping the old GPU scene with a newly
    /// loaded model.
    /// </summary>
    public static void ReleasePreparedScene()
    {
        lock (RenderSync)
        {
            lock (DeviceSync)
            {
                PreparedComputeScene? sceneResources = preparedScene;
                preparedScene = null;
                if (sceneResources == null)
                    return;

                try { sharedGraphicsDevice?.WaitForIdle(); } catch { }
                try { sceneResources.Dispose(); } catch { }
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
        // Position and UV padding components carry the three authored vertex
        // normals. This preserves the original 11-vec4 record size while giving
        // the ray tracer smooth glTF shading without another scene-sized buffer.
        public readonly Vector4 A; // xyz=position A, w=normal A.x
        public readonly Vector4 B; // xyz=position B, w=normal A.y
        public readonly Vector4 C; // xyz=position C, w=normal A.z
        public readonly Vector4 BaseColor; // rgb=linear factor, a=alpha factor
        public readonly Vector4 Emission; // rgb=linear factor, a=strength
        public readonly Vector4 MaterialParams; // x=metallic, y=roughness, z=transmission, w=normal scale
        public readonly Vector4 UvA; // xy=UV A, zw=normal B.xy
        public readonly Vector4 UvB; // xy=UV B, z=normal B.z, w=normal C.x
        public readonly Vector4 UvC; // xy=UV C, zw=normal C.yz
        public readonly Vector4 TextureInfo; // x=base index, y=emissive index, z=alpha/double-sided flags, w=alpha cutoff
        public readonly Vector4 TextureInfo2; // x=MR index, y=normal index, z=occlusion index, w=occlusion strength

        public GpuTriangle(Triangle triangle, IReadOnlyDictionary<TextureMap, int> textureIds)
        {
            Vec3 normalA = triangle.NormalA.Normalize();
            Vec3 normalB = triangle.NormalB.Normalize();
            Vec3 normalC = triangle.NormalC.Normalize();
            A = ToVector4(triangle.A, (float)normalA.X);
            B = ToVector4(triangle.B, (float)normalA.Y);
            C = ToVector4(triangle.C, (float)normalA.Z);
            UvA = new Vector4((float)triangle.UvA.U, (float)triangle.UvA.V, (float)normalB.X, (float)normalB.Y);
            UvB = new Vector4((float)triangle.UvB.U, (float)triangle.UvB.V, (float)normalB.Z, (float)normalC.X);
            UvC = new Vector4((float)triangle.UvC.U, (float)triangle.UvC.V, (float)normalC.Y, (float)normalC.Z);

            Material material = triangle.Material;
            Vec3 colorFactor = material.Color;
            BaseColor = new Vector4(
                (float)Math.Clamp(colorFactor.X, 0.0, 64.0),
                (float)Math.Clamp(colorFactor.Y, 0.0, 64.0),
                (float)Math.Clamp(colorFactor.Z, 0.0, 64.0),
                (float)Math.Clamp(material.Alpha, 0.0, 1.0));

            Emission = new Vector4(
                (float)Math.Max(0.0, material.EmissionColor.X),
                (float)Math.Max(0.0, material.EmissionColor.Y),
                (float)Math.Max(0.0, material.EmissionColor.Z),
                (float)Math.Max(0.0, material.Emission));

            MaterialParams = new Vector4(
                (float)Math.Clamp(material.Metallic, 0.0, 1.0),
                (float)Math.Clamp(material.Roughness, 0.02, 1.0),
                (float)Math.Clamp(material.Transmission, 0.0, 1.0),
                (float)material.NormalScale);

            int baseTextureIndex = TextureIndex(textureIds, material.Texture);
            int emissiveTextureIndex = TextureIndex(textureIds, material.EmissiveTexture);
            int metallicRoughnessTextureIndex = TextureIndex(textureIds, material.MetallicRoughnessTexture);
            int normalTextureIndex = TextureIndex(textureIds, material.NormalTexture);
            int occlusionTextureIndex = TextureIndex(textureIds, material.OcclusionTexture);
            int alphaFlags = (int)material.AlphaMode | (material.DoubleSided ? 4 : 0);
            TextureInfo = new Vector4(
                baseTextureIndex,
                emissiveTextureIndex,
                alphaFlags,
                (float)material.AlphaCutoff);
            TextureInfo2 = new Vector4(
                metallicRoughnessTextureIndex,
                normalTextureIndex,
                occlusionTextureIndex,
                (float)material.OcclusionStrength);
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

    private sealed class TextureUpload
    {
        public required TextureMap Texture { get; init; }
        public required int PixelOffset { get; init; }
    }

    private sealed class TextureBuildResult
    {
        public required Dictionary<TextureMap, int> TextureIds { get; init; }
        public required IReadOnlyList<TextureUpload> Uploads { get; init; }
        public required GpuTextureInfo[] Infos { get; set; }
        public required long PixelCount { get; init; }
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CpuBvhNode
    {
        public readonly Vector3 BoundsMin;
        public readonly Vector3 BoundsMax;
        public readonly int LeftOrFirst;
        public readonly int TriangleCount;
        public readonly int RightChild;

        public CpuBvhNode(Vector3 boundsMin, Vector3 boundsMax, int leftOrFirst, int triangleCount, int rightChild)
        {
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            LeftOrFirst = leftOrFirst;
            TriangleCount = triangleCount;
            RightChild = rightChild;
        }

        public GpuBvhNode ToGpu() => new(BoundsMin, BoundsMax, LeftOrFirst, TriangleCount, RightChild);
    }

    private sealed class TriangleCentroidComparer : IComparer<int>
    {
        private readonly IReadOnlyList<Triangle> triangles;
        public int Axis { get; set; }

        public TriangleCentroidComparer(IReadOnlyList<Triangle> triangles)
        {
            this.triangles = triangles;
        }

        public int Compare(int leftIndex, int rightIndex)
        {
            Vec3 left = triangles[leftIndex].Centroid;
            Vec3 right = triangles[rightIndex].Centroid;
            double leftValue = Axis == 0 ? left.X : Axis == 1 ? left.Y : left.Z;
            double rightValue = Axis == 0 ? right.X : Axis == 1 ? right.Y : right.Z;
            return leftValue.CompareTo(rightValue);
        }
    }

    private sealed class BvhBuildResult
    {
        public required int[] TriangleIndices { get; set; }
        public required CpuBvhNode[] Nodes { get; set; }
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
        lock (RenderSync)
        {
            return RenderLocked(
                scene,
                cameraPosition,
                basis,
                width,
                height,
                bounceCount,
                sampleIndex,
                sampleCount,
                cancellationToken,
                out details,
                progressCallback,
                settings,
                fieldOfViewDegrees);
        }
    }

    private static RenderImage RenderLocked(
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

        ThrowIfCancellationRequested(cancellationToken, "before shared Vulkan device");
        GraphicsDevice gd = GetOrCreateSharedDevice();
        ResourceFactory factory = gd.ResourceFactory;

        Stage("Get or build cached Vulkan compute scene");
        PreparedComputeScene prepared = GetOrCreatePreparedScene(gd, scene, cancellationToken);
        DeviceBuffer triangleBuffer = prepared.TriangleBuffer;
        DeviceBuffer bvhBuffer = prepared.BvhBuffer;
        DeviceBuffer lightBuffer = prepared.LightBuffer;
        DeviceBuffer texturePixelBuffer = prepared.TexturePixelBuffer;
        DeviceBuffer textureInfoBuffer = prepared.TextureInfoBuffer;
        int sourceTriangleCount = prepared.SourceTriangleCount;
        int uploadedTriangleCount = prepared.TriangleCount;
        int uploadedBvhNodeCount = prepared.BvhNodeCount;
        int sourceLightCount = prepared.SourceLightCount;
        int uploadedLightCount = prepared.LightCount;

        uint outputBytes = checked((uint)((long)width * height * sizeof(uint)));
        uint constantBytes = checked((uint)Marshal.SizeOf<CameraConstants>());
        Stage($"Cached scene buffers={prepared.SceneBufferBytes / (1024.0 * 1024.0):F1} MB; per-frame output+readback={(outputBytes * 2.0) / (1024.0 * 1024.0):F1} MB");

        Stage("Create output buffer");
        ThrowIfCancellationRequested(cancellationToken, "Create output buffer");
        using DeviceBuffer outputBuffer = factory.CreateBuffer(new BufferDescription(
            outputBytes,
            BufferUsage.StructuredBufferReadWrite,
            structureByteStride: sizeof(uint)));

        Stage("Create constants buffer");
        using DeviceBuffer constantsBuffer = factory.CreateBuffer(new BufferDescription(
            constantBytes,
            BufferUsage.UniformBuffer));

        Stage("Create staging readback buffer");
        using DeviceBuffer stagingBuffer = factory.CreateBuffer(new BufferDescription(
            outputBytes,
            BufferUsage.Staging));

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

        Stage("Copy output buffer to staging buffer");
        RenderImage image = ReadBackOutputImage(gd, factory, outputBuffer, stagingBuffer, outputBytes, width, height, cancellationToken, "final readback");
        string triangleTruncated = sourceTriangleCount > uploadedTriangleCount ? $", triangles truncated from {sourceTriangleCount}" : string.Empty;
        string lightTruncated = sourceLightCount > uploadedLightCount ? $", lights truncated from {sourceLightCount}" : string.Empty;
        details = $"VULKAN GPU COMPUTE BVH TRACE - {width}x{height}, {uploadedTriangleCount} triangles{triangleTruncated}, {uploadedBvhNodeCount} BVH nodes, {uploadedLightCount} lights{lightTruncated}, textures={prepared.TextureCount}, material=linear-srgb+pbr+smooth-normal+normal-map+occlusion+ibl, bounces={Math.Clamp(bounceCount, 0, 8)}, samples={sampleIndex + 1}-{sampleIndex + sampleCount}, fov={fieldOfViewDegrees:0.##}, exposure={settings.Exposure:0.###}, ambient={settings.AmbientStrength:0.###}, shadows={settings.UseShadows}, tileRows={tileRows}";
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

    private static PreparedComputeScene GetOrCreatePreparedScene(GraphicsDevice gd, Scene scene, CancellationToken cancellationToken)
    {
        lock (DeviceSync)
        {
            if (preparedScene != null && ReferenceEquals(preparedScene.Scene, scene))
            {
                Stage("Reuse cached Vulkan compute scene buffers");
                return preparedScene;
            }

            if (preparedScene != null)
            {
                try { gd.WaitForIdle(); } catch { }
                try { preparedScene.Dispose(); } catch { }
                preparedScene = null;
            }

            Stage("Build cached texture metadata");
            TextureBuildResult textureBuild = BuildTextureBuffers(scene);
            Stage($"Texture metadata built: textures={textureBuild.TextureCount}, pixels={textureBuild.PixelCount}, infos={textureBuild.Infos.Length}");

            int sourceTriangleCount = scene.Triangles.Count;
            if (sourceTriangleCount == 0)
                throw new InvalidOperationException("Vulkan scene render requires at least one triangle.");

            // The BVH keeps only an integer triangle-order array and compact CPU
            // nodes. GPU records are generated directly into small upload chunks.
            Stage("Build cached traversal BVH");
            BvhBuildResult bvh = BuildGpuBvh(scene);
            GpuLight[] lights = BuildLightBuffer(scene, out int sourceLightCount);

            int triangleStride = Marshal.SizeOf<GpuTriangle>();
            int bvhStride = Marshal.SizeOf<GpuBvhNode>();
            int lightStride = Marshal.SizeOf<GpuLight>();
            int textureInfoStride = Marshal.SizeOf<GpuTextureInfo>();
            uint triangleBytes = CheckedSceneBufferSize((long)sourceTriangleCount * triangleStride, triangleStride, "triangle");
            uint bvhBytes = CheckedSceneBufferSize((long)bvh.Nodes.Length * bvhStride, bvhStride, "BVH");
            uint lightBytes = CheckedSceneBufferSize((long)lights.Length * lightStride, lightStride, "light");
            uint texturePixelBytes = CheckedSceneBufferSize(textureBuild.PixelCount * sizeof(uint), sizeof(uint), "texture pixel");
            uint textureInfoBytes = CheckedSceneBufferSize((long)textureBuild.Infos.Length * textureInfoStride, textureInfoStride, "texture info");
            long sceneBufferBytes = (long)triangleBytes + bvhBytes + lightBytes + texturePixelBytes + textureInfoBytes;
            Stage($"Create cached Vulkan compute scene: {sceneBufferBytes / (1024.0 * 1024.0):F1} MB, triangles={sourceTriangleCount}, BVH nodes={bvh.Nodes.Length}, textures={textureBuild.TextureCount}");

            ResourceFactory factory = gd.ResourceFactory;
            DeviceBuffer? triangleBuffer = null;
            DeviceBuffer? bvhBuffer = null;
            DeviceBuffer? lightBuffer = null;
            DeviceBuffer? texturePixelBuffer = null;
            DeviceBuffer? textureInfoBuffer = null;
            try
            {
                triangleBuffer = factory.CreateBuffer(new BufferDescription(triangleBytes, BufferUsage.StructuredBufferReadOnly, (uint)triangleStride));
                bvhBuffer = factory.CreateBuffer(new BufferDescription(bvhBytes, BufferUsage.StructuredBufferReadOnly, (uint)bvhStride));
                lightBuffer = factory.CreateBuffer(new BufferDescription(lightBytes, BufferUsage.StructuredBufferReadOnly, (uint)lightStride));
                texturePixelBuffer = factory.CreateBuffer(new BufferDescription(texturePixelBytes, BufferUsage.StructuredBufferReadOnly, sizeof(uint)));
                textureInfoBuffer = factory.CreateBuffer(new BufferDescription(textureInfoBytes, BufferUsage.StructuredBufferReadOnly, (uint)textureInfoStride));

                ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan triangle buffer");
                UploadGpuTriangles(gd, triangleBuffer, scene.Triangles, bvh.TriangleIndices, textureBuild.TextureIds, cancellationToken);
                bvh.TriangleIndices = Array.Empty<int>();
                textureBuild.TextureIds.Clear();

                UploadGpuBvhNodes(gd, bvhBuffer, bvh.Nodes, bvhStride, cancellationToken);
                int bvhNodeCount = bvh.Nodes.Length;
                bvh.Nodes = Array.Empty<CpuBvhNode>();

                gd.UpdateBuffer(lightBuffer, 0, lights.Length == 0 ? new[] { default(GpuLight) } : lights);
                UploadTexturePixels(gd, texturePixelBuffer, textureBuild, cancellationToken);
                gd.UpdateBuffer(textureInfoBuffer, 0, textureBuild.Infos.Length == 0 ? new[] { new GpuTextureInfo(0, 1, 1) } : textureBuild.Infos);
                textureBuild.Infos = Array.Empty<GpuTextureInfo>();

                preparedScene = new PreparedComputeScene
                {
                    Scene = scene,
                    TriangleBuffer = triangleBuffer,
                    BvhBuffer = bvhBuffer,
                    LightBuffer = lightBuffer,
                    TexturePixelBuffer = texturePixelBuffer,
                    TextureInfoBuffer = textureInfoBuffer,
                    SourceTriangleCount = sourceTriangleCount,
                    TriangleCount = sourceTriangleCount,
                    BvhNodeCount = bvhNodeCount,
                    SourceLightCount = sourceLightCount,
                    LightCount = lights.Length,
                    TextureCount = textureBuild.TextureCount,
                    SceneBufferBytes = sceneBufferBytes
                };
                StageMemory("after cached Vulkan scene upload");
                return preparedScene;
            }
            catch
            {
                try { textureInfoBuffer?.Dispose(); } catch { }
                try { texturePixelBuffer?.Dispose(); } catch { }
                try { lightBuffer?.Dispose(); } catch { }
                try { bvhBuffer?.Dispose(); } catch { }
                try { triangleBuffer?.Dispose(); } catch { }
                throw;
            }
        }
    }

    private static void UploadGpuTriangles(
        GraphicsDevice gd,
        DeviceBuffer destination,
        IReadOnlyList<Triangle> sourceTriangles,
        IReadOnlyList<int> orderedTriangleIndices,
        IReadOnlyDictionary<TextureMap, int> textureIds,
        CancellationToken cancellationToken)
    {
        const int TrianglesPerChunk = 2048;
        int stride = Marshal.SizeOf<GpuTriangle>();
        GpuTriangle[] chunk = new GpuTriangle[TrianglesPerChunk];
        int chunkCount = 0;
        int uploaded = 0;

        for (int i = 0; i < orderedTriangleIndices.Count; i++)
        {
            chunk[chunkCount++] = new GpuTriangle(sourceTriangles[orderedTriangleIndices[i]], textureIds);
            if (chunkCount == chunk.Length)
            {
                gd.UpdateBuffer(destination, checked((uint)((long)uploaded * stride)), chunk);
                uploaded += chunkCount;
                chunkCount = 0;
                ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan triangle buffer");
            }
        }

        if (chunkCount > 0)
        {
            GpuTriangle[] finalChunk = new GpuTriangle[chunkCount];
            Array.Copy(chunk, finalChunk, chunkCount);
            gd.UpdateBuffer(destination, checked((uint)((long)uploaded * stride)), finalChunk);
        }
    }

    private static void UploadGpuBvhNodes(
        GraphicsDevice gd,
        DeviceBuffer destination,
        CpuBvhNode[] source,
        int gpuStride,
        CancellationToken cancellationToken)
    {
        if (source.Length == 0)
        {
            gd.UpdateBuffer(destination, 0, new[] { default(GpuBvhNode) });
            return;
        }

        const int NodesPerChunk = 4096;
        GpuBvhNode[] chunk = new GpuBvhNode[Math.Min(NodesPerChunk, source.Length)];
        int offset = 0;
        while (offset < source.Length)
        {
            int count = Math.Min(chunk.Length, source.Length - offset);
            for (int i = 0; i < count; i++)
                chunk[i] = source[offset + i].ToGpu();

            GpuBvhNode[] upload = chunk;
            if (count != chunk.Length)
            {
                upload = new GpuBvhNode[count];
                Array.Copy(chunk, upload, count);
            }

            gd.UpdateBuffer(destination, checked((uint)((long)offset * gpuStride)), upload);
            offset += count;
            ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan BVH buffer");
        }
    }

    private static void UploadTexturePixels(
        GraphicsDevice gd,
        DeviceBuffer destination,
        TextureBuildResult textures,
        CancellationToken cancellationToken)
    {
        if (textures.Uploads.Count == 0)
        {
            gd.UpdateBuffer(destination, 0, new[] { 0xffffffffu });
            return;
        }

        foreach (TextureUpload upload in textures.Uploads)
        {
            ThrowIfCancellationRequested(cancellationToken, "upload cached Vulkan texture pixels");
            uint[] pixels = upload.Texture.CopyPackedRgba32Pixels();
            int expectedPixels = checked(Math.Max(1, upload.Texture.Width) * Math.Max(1, upload.Texture.Height));
            if (pixels.Length != expectedPixels)
                throw new InvalidOperationException($"Texture '{upload.Texture.Name}' returned {pixels.Length} pixels; expected {expectedPixels}.");
            gd.UpdateBuffer(destination, checked((uint)((long)upload.PixelOffset * sizeof(uint))), pixels);
            pixels = Array.Empty<uint>();
        }
    }

    private static uint CheckedSceneBufferSize(long requestedBytes, int minimumBytes, string label)
    {
        long bytes = Math.Max(minimumBytes, requestedBytes);
        if (bytes > uint.MaxValue)
            throw new InvalidOperationException($"The Vulkan compute {label} buffer would require {bytes / (1024.0 * 1024.0):F0} MB, exceeding the 4 GB Vulkan buffer limit.");
        return checked((uint)bytes);
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


    private static BvhBuildResult BuildGpuBvh(Scene scene)
    {
        int triangleCount = scene.Triangles.Count;
        float boundsPad = ComputeGpuBoundsPad(scene);
        Vector3 pad = new(boundsPad);

        // Store only the final triangle order. Triangle bounds and centroids are
        // already present in the scene, so duplicating them in another large
        // per-triangle structure only increases peak memory.
        int[] triangleIndices = new int[triangleCount];
        for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            triangleIndices[triangleIndex] = triangleIndex;

        Stage($"GPU BVH conservative triangle bounds pad={boundsPad:G6}");

        CpuBvhNode[] nodes = new CpuBvhNode[CountGpuBvhNodes(triangleCount)];
        TriangleCentroidComparer comparer = new(scene.Triangles);
        int nodeCount = 0;
        BuildGpuBvhRecursive(scene.Triangles, triangleIndices, 0, triangleIndices.Length, pad, comparer, nodes, ref nodeCount);

        if (nodeCount != nodes.Length)
            throw new InvalidOperationException($"Internal BVH packing mismatch: nodes={nodeCount}/{nodes.Length}.");

        return new BvhBuildResult
        {
            TriangleIndices = triangleIndices,
            Nodes = nodes
        };
    }

    private static int BuildGpuBvhRecursive(
        IReadOnlyList<Triangle> triangles,
        int[] triangleIndices,
        int start,
        int count,
        Vector3 boundsPad,
        TriangleCentroidComparer comparer,
        CpuBvhNode[] nodes,
        ref int nodeCount)
    {
        int nodeIndex = nodeCount++;

        const int LeafSize = 4;
        if (count <= LeafSize)
        {
            GetLeafBounds(
                triangles,
                triangleIndices,
                start,
                count,
                boundsPad,
                out Vector3 leafBoundsMin,
                out Vector3 leafBoundsMax);
            nodes[nodeIndex] = new CpuBvhNode(leafBoundsMin, leafBoundsMax, start, count, 0);
            return nodeIndex;
        }

        GetCentroidBounds(triangles, triangleIndices, start, count, out Vector3 centroidMin, out Vector3 centroidMax);
        Vector3 extent = centroidMax - centroidMin;
        int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        comparer.Axis = axis;
        Array.Sort(triangleIndices, start, count, comparer);

        int leftCount = count / 2;
        int rightCount = count - leftCount;
        int left = BuildGpuBvhRecursive(triangles, triangleIndices, start, leftCount, boundsPad, comparer, nodes, ref nodeCount);
        int right = BuildGpuBvhRecursive(triangles, triangleIndices, start + leftCount, rightCount, boundsPad, comparer, nodes, ref nodeCount);
        Vector3 boundsMin = Vector3.Min(nodes[left].BoundsMin, nodes[right].BoundsMin);
        Vector3 boundsMax = Vector3.Max(nodes[left].BoundsMax, nodes[right].BoundsMax);
        nodes[nodeIndex] = new CpuBvhNode(boundsMin, boundsMax, left, 0, right);
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

    private static void GetLeafBounds(
        IReadOnlyList<Triangle> triangles,
        int[] triangleIndices,
        int start,
        int count,
        Vector3 pad,
        out Vector3 boundsMin,
        out Vector3 boundsMax)
    {
        boundsMin = new Vector3(float.PositiveInfinity);
        boundsMax = new Vector3(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            Triangle triangle = triangles[triangleIndices[i]];
            Vector3 a = ToVector3(triangle.A);
            Vector3 b = ToVector3(triangle.B);
            Vector3 c = ToVector3(triangle.C);
            boundsMin = Vector3.Min(boundsMin, Vector3.Min(a, Vector3.Min(b, c)) - pad);
            boundsMax = Vector3.Max(boundsMax, Vector3.Max(a, Vector3.Max(b, c)) + pad);
        }
    }

    private static void GetCentroidBounds(
        IReadOnlyList<Triangle> triangles,
        int[] triangleIndices,
        int start,
        int count,
        out Vector3 centroidMin,
        out Vector3 centroidMax)
    {
        centroidMin = new Vector3(float.PositiveInfinity);
        centroidMax = new Vector3(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            Vector3 centroid = ToVector3(triangles[triangleIndices[i]].Centroid);
            centroidMin = Vector3.Min(centroidMin, centroid);
            centroidMax = Vector3.Max(centroidMax, centroid);
        }
    }

    private static float ComputeGpuBoundsPad(Scene scene)
    {
        if (scene.Triangles.Count == 0)
            return 1e-4f;

        Vector3 globalMin = new(float.PositiveInfinity);
        Vector3 globalMax = new(float.NegativeInfinity);
        foreach (Triangle tri in scene.Triangles)
        {
            Vector3 a = new((float)tri.A.X, (float)tri.A.Y, (float)tri.A.Z);
            Vector3 b = new((float)tri.B.X, (float)tri.B.Y, (float)tri.B.Z);
            Vector3 c = new((float)tri.C.X, (float)tri.C.Y, (float)tri.C.Z);
            globalMin = Vector3.Min(globalMin, Vector3.Min(a, Vector3.Min(b, c)));
            globalMax = Vector3.Max(globalMax, Vector3.Max(a, Vector3.Max(b, c)));
        }

        float sceneDiagonal = Vector3.Distance(globalMin, globalMax);
        if (!float.IsFinite(sceneDiagonal) || sceneDiagonal <= 0.0f)
            return 1e-4f;

        return Math.Clamp(sceneDiagonal * 1e-6f, 1e-4f, 0.01f);
    }

    private static TextureBuildResult BuildTextureBuffers(Scene scene)
    {
        List<TextureMap> textures = new();
        HashSet<TextureMap> seen = new();
        foreach (Triangle triangle in scene.Triangles)
        {
            AddUnique(triangle.Material.Texture);
            AddUnique(triangle.Material.EmissiveTexture);
            AddUnique(triangle.Material.MetallicRoughnessTexture);
            AddUnique(triangle.Material.NormalTexture);
            AddUnique(triangle.Material.OcclusionTexture);
        }

        if (textures.Count == 0)
        {
            return new TextureBuildResult
            {
                TextureIds = new Dictionary<TextureMap, int>(),
                Uploads = Array.Empty<TextureUpload>(),
                Infos = new[] { new GpuTextureInfo(0, 1, 1) },
                PixelCount = 1,
                TextureCount = 0
            };
        }

        GpuTextureInfo[] infos = new GpuTextureInfo[textures.Count];
        Dictionary<TextureMap, int> textureIds = new(textures.Count);
        List<TextureUpload> uploads = new(textures.Count);
        long pixelOffset = 0;

        foreach (TextureMap texture in textures)
        {
            int width = Math.Max(1, texture.Width);
            int height = Math.Max(1, texture.Height);
            long pixelLength = checked((long)width * height);
            if (pixelOffset > int.MaxValue || pixelOffset + pixelLength > int.MaxValue)
                throw new InvalidOperationException("Vulkan compute texture offsets exceed the shader's 32-bit indexing range.");

            int textureIndex = textureIds.Count;
            int offset = checked((int)pixelOffset);
            textureIds.Add(texture, textureIndex);
            infos[textureIndex] = new GpuTextureInfo(offset, width, height);
            uploads.Add(new TextureUpload { Texture = texture, PixelOffset = offset });
            pixelOffset = checked(pixelOffset + pixelLength);
        }

        return new TextureBuildResult
        {
            TextureIds = textureIds,
            Uploads = uploads,
            Infos = infos,
            PixelCount = Math.Max(1, pixelOffset),
            TextureCount = textureIds.Count
        };

        void AddUnique(TextureMap? texture)
        {
            if (texture != null && seen.Add(texture))
                textures.Add(texture);
        }
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

    private static Vector3 ToVector3(Vec3 value) =>
        new((float)value.X, (float)value.Y, (float)value.Z);

    private static Vector4 ToVector4(Vec3 value, float w)
        => new((float)value.X, (float)value.Y, (float)value.Z, w);
}
