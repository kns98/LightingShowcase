// -----------------------------------------------------------------------------
// File: Rendering/VulkanRasterRenderer.cs
// Purpose: Vulkan graphics-pipeline raster preview.
//
// This renderer is intentionally separate from VulkanSceneComputeRenderer.  It
// uses Vulkan's normal vertex/fragment raster pipeline for fast preview frames:
// vertex buffer -> depth-tested triangle rasterization -> fragment lighting ->
// off-screen color texture -> staging readback -> cross-platform RGBA image.
// -----------------------------------------------------------------------------

using System.Diagnostics;
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

/// <summary>Vulkan hardware rasterizer for the Render tab preview.</summary>
public static class VulkanRasterRenderer
{
    public static string StageLogPath => Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-stage-log.txt");

    private const int MaxGpuLights = 32;
    private const double CameraFovDegrees = 72.0;
    private const double CameraNear = 0.035;
    private const double CameraFar = 5000.0;

    // Default to per-fragment GPU texture sampling so high-frequency glTF atlas
    // textures match the CPU shadow rasterizer. Set
    // LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES=1 only as a diagnostic fallback.
    private static bool UseGpuTextureSampling =>
        !string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES"), "1", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_BAKED_TEXTURES"), "true", StringComparison.OrdinalIgnoreCase);

    private static int RasterDebugMode
    {
        get
        {
            string? value = Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_RASTER_DEBUG");
            return value?.Trim().ToLowerInvariant() switch
            {
                "uv" => 1,
                "atlas" => 2,
                "texture" => 3,
                "material" => 4,
                "normal" => 5,
                "metallic" => 6,
                "roughness" => 7,
                "occlusion" => 8,
                "emissive" => 9,
                "direct" => 10,
                "ibl" => 11,
                _ => 0
            };
        }
    }

    private static readonly object DeviceSync = new();
    private static readonly object RenderSync = new();
    private static GraphicsDevice? sharedGraphicsDevice;
    private static SharedRasterResources? sharedRasterResources;
    private static PreparedRasterScene? preparedScene;
    // Interactive and settled frames use different resolutions. Keep a small
    // LRU so orbit/release does not recreate color, depth, staging and sync
    // resources every time the viewer switches between those sizes.
    private const int MaxCachedTargetSizes = 1;
    private static readonly Dictionary<(int Width, int Height), RasterTargets> sharedTargets = new();
    private static long targetUseSerial;
    private static bool preflightCompleted;

    private sealed class SharedRasterResources : IDisposable
    {
        public required ResourceLayout Layout { get; init; }
        public required Shader[] Shaders { get; init; }
        public required Pipeline OpaquePipeline { get; init; }
        public required Pipeline TransparentPipeline { get; init; }

        public void Dispose()
        {
            try { OpaquePipeline.Dispose(); } catch { }
            try { TransparentPipeline.Dispose(); } catch { }
            if (Shaders != null)
            {
                foreach (Shader shader in Shaders)
                {
                    try { shader.Dispose(); } catch { }
                }
            }
            try { Layout.Dispose(); } catch { }
        }
    }


    private sealed class PreparedRasterScene : IDisposable
    {
        public required Scene Scene { get; init; }
        public required DeviceBuffer OpaqueVertexBuffer { get; init; }
        public required DeviceBuffer TransparentVertexBuffer { get; init; }
        public required DeviceBuffer CameraBuffer { get; init; }
        public required DeviceBuffer LightBuffer { get; init; }
        public required DeviceBuffer MaterialBuffer { get; init; }
        public required Texture AtlasTexture { get; init; }
        public required Sampler AtlasSampler { get; init; }
        public required ResourceSet ResourceSet { get; init; }
        public required int LightCount { get; init; }
        public required int OpaqueVertexCount { get; init; }
        public required int TransparentVertexCount { get; init; }
        public required int MaterialCount { get; init; }
        public required int SourceTriangleCount { get; init; }
        public required int TotalTriangleCount { get; init; }
        public required int NearClippedTriangleCount { get; init; }
        public required int TextureCount { get; init; }
        public required int TexturePageCount { get; init; }
        public required bool GpuTextureSamplingRequested { get; init; }
        public required bool UsesGpuTextureSampling { get; init; }
        public string? TextureFallbackReason { get; init; }
        public required double BoundingRadius { get; init; }
        public required Vec3 BoundingCenter { get; init; }

        public void Dispose()
        {
            try { ResourceSet.Dispose(); } catch { }
            try { AtlasSampler.Dispose(); } catch { }
            try { AtlasTexture.Dispose(); } catch { }
            try { MaterialBuffer.Dispose(); } catch { }
            try { LightBuffer.Dispose(); } catch { }
            try { CameraBuffer.Dispose(); } catch { }
            try { TransparentVertexBuffer.Dispose(); } catch { }
            try { OpaqueVertexBuffer.Dispose(); } catch { }
        }
    }

    private sealed class RasterTargets : IDisposable
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required Texture ColorTexture { get; init; }
        public required Texture DepthTexture { get; init; }
        public required Texture StagingTexture { get; init; }
        public required Framebuffer Framebuffer { get; init; }
        public required CommandList CommandList { get; init; }
        public required Fence Fence { get; init; }
        public long LastUseSerial { get; set; }

        public void Dispose()
        {
            try { Fence.Dispose(); } catch { }
            try { CommandList.Dispose(); } catch { }
            try { Framebuffer.Dispose(); } catch { }
            try { StagingTexture.Dispose(); } catch { }
            try { DepthTexture.Dispose(); } catch { }
            try { ColorTexture.Dispose(); } catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RasterVertex
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector2 Uv;
        public readonly float MaterialIndex;

        public RasterVertex(Vec3 position, Vec3 normal, Vec2 uv, int materialIndex)
        {
            Position = ToVector3(position);
            Normal = ToVector3(normal.Normalize());
            Uv = new Vector2((float)uv.U, (float)uv.V);
            MaterialIndex = materialIndex;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RasterCameraConstants
    {
        public readonly Vector4 CameraPosition;
        public readonly Vector4 CameraForward;
        public readonly Vector4 CameraRight;
        public readonly Vector4 CameraUp;
        public readonly Vector4 Projection; // x=aspect, y=tanHalfFov, z=near, w=far
        public readonly Vector4 Counts;     // x=lightCount, y=textureCount, z=debugMode, w=materialCount

        public RasterCameraConstants(Vec3 position, CameraBasis basis, int width, int height, int lightCount, int textureCount, int debugMode, int materialCount, double farPlane)
        {
            CameraPosition = ToVector4(position, 1.0f);
            CameraForward = ToVector4(basis.Forward, 0.0f);
            CameraRight = ToVector4(basis.Right, 0.0f);
            CameraUp = ToVector4(basis.Up, 0.0f);
            float aspect = width / (float)Math.Max(1, height);
            float tanHalfFov = (float)Math.Tan((CameraFovDegrees * Math.PI / 180.0) * 0.5);
            Projection = new Vector4(aspect, tanHalfFov, (float)CameraNear, (float)Math.Clamp(farPlane, CameraNear + 1.0, CameraFar));
            Counts = new Vector4(
                Math.Clamp(lightCount, 0, MaxGpuLights),
                Math.Max(0, textureCount),
                Math.Max(0, debugMode),
                Math.Max(0, materialCount));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RasterLight
    {
        public readonly Vector4 PositionKind;   // xyz=position, w=0 point / 1 directional / 2 spot
        public readonly Vector4 DirectionRange; // xyz=direction light travels, w=range
        public readonly Vector4 ColorIntensity; // rgb=color, w=intensity
        public readonly Vector4 ConeEnabled;    // x=cos inner, y=cos outer, z=castsShadow placeholder, w=enabled

        public RasterLight(SceneLight light)
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
            ConeEnabled = new Vector4(
                (float)Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)),
                (float)Math.Cos(light.OuterConeAngle),
                light.CastsShadow ? 1.0f : 0.0f,
                light.Enabled ? 1.0f : 0.0f);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RasterTexturePlacement
    {
        public readonly Vector4 AtlasTransform; // xy=atlas UV offset, zw=atlas UV scale
        public readonly Vector4 TextureAddress; // x=wrapU, y=wrapV: 0 repeat, 1 clamp, 2 mirror
        public readonly Vector4 TextureTransform; // xy=offset, zw=scale; rotation in TextureAddress.z
        public readonly bool HasTexture;

        public RasterTexturePlacement(float offsetX, float offsetY, float scaleX, float scaleY, int pageIndex, TextureMap texture)
        {
            AtlasTransform = new Vector4(offsetX, offsetY, scaleX, scaleY);
            // w stores the texture-array page. z remains the glTF texture-transform rotation.
            TextureAddress = new Vector4(AddressModeCode(texture.WrapU), AddressModeCode(texture.WrapV), (float)texture.Rotation, pageIndex);
            TextureTransform = new Vector4((float)texture.OffsetU, (float)texture.OffsetV, (float)texture.ScaleU, (float)texture.ScaleV);
            HasTexture = true;
        }

        public static RasterTexturePlacement None => new();

        private static float AddressModeCode(TextureAddressMode mode) => mode switch
        {
            TextureAddressMode.ClampToEdge => 1.0f,
            TextureAddressMode.MirroredRepeat => 2.0f,
            _ => 0.0f
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RasterMaterial
    {
        public readonly Vector4 BaseColorAlpha;
        public readonly Vector4 EmissiveFactor;
        public readonly Vector4 PbrFactors;      // x=metallic, y=roughness, z=normal scale, w=occlusion strength
        public readonly Vector4 AlphaFlags;      // x=alpha mode, y=cutoff, z=double sided, w=transmission
        public readonly Vector4 OpticalFactors;  // x=ior, y=thickness, z=clearcoat, w=clearcoat roughness
        public readonly Vector4 AttenuationColorDistance; // rgb=attenuation color, w=distance (0=infinite)
        public readonly Vector4 TextureFlags;    // base, metallic-roughness, normal, emissive
        public readonly Vector4 TextureFlags2;   // x=occlusion, y=transmission, z=clearcoat reuses transmission mask

        public readonly Vector4 BaseAtlasTransform;
        public readonly Vector4 BaseTextureAddress;
        public readonly Vector4 BaseTextureTransform;

        public readonly Vector4 MrAtlasTransform;
        public readonly Vector4 MrTextureAddress;
        public readonly Vector4 MrTextureTransform;

        public readonly Vector4 NormalAtlasTransform;
        public readonly Vector4 NormalTextureAddress;
        public readonly Vector4 NormalTextureTransform;

        public readonly Vector4 EmissiveAtlasTransform;
        public readonly Vector4 EmissiveTextureAddress;
        public readonly Vector4 EmissiveTextureTransform;

        public readonly Vector4 OcclusionAtlasTransform;
        public readonly Vector4 OcclusionTextureAddress;
        public readonly Vector4 OcclusionTextureTransform;

        public readonly Vector4 TransmissionAtlasTransform;
        public readonly Vector4 TransmissionTextureAddress;
        public readonly Vector4 TransmissionTextureTransform;

        public RasterMaterial(
            Material material,
            RasterTexturePlacement basePlacement,
            RasterTexturePlacement mrPlacement,
            RasterTexturePlacement normalPlacement,
            RasterTexturePlacement emissivePlacement,
            RasterTexturePlacement occlusionPlacement,
            RasterTexturePlacement transmissionPlacement)
        {
            BaseColorAlpha = MaterialColorAlpha(material);
            EmissiveFactor = MaterialEmission(material);
            PbrFactors = new Vector4(
                (float)material.Metallic,
                (float)material.Roughness,
                (float)material.NormalScale,
                (float)material.OcclusionStrength);
            AlphaFlags = new Vector4(
                (float)(int)material.AlphaMode,
                (float)material.AlphaCutoff,
                material.DoubleSided ? 1.0f : 0.0f,
                (float)material.Transmission);
            OpticalFactors = new Vector4(
                (float)material.Ior,
                (float)material.Thickness,
                (float)material.Clearcoat,
                (float)material.ClearcoatRoughness);
            AttenuationColorDistance = new Vector4(
                (float)material.AttenuationColor.X,
                (float)material.AttenuationColor.Y,
                (float)material.AttenuationColor.Z,
                (float)material.AttenuationDistance);
            TextureFlags = new Vector4(
                basePlacement.HasTexture ? 1.0f : 0.0f,
                mrPlacement.HasTexture ? 1.0f : 0.0f,
                normalPlacement.HasTexture ? 1.0f : 0.0f,
                emissivePlacement.HasTexture ? 1.0f : 0.0f);
            TextureFlags2 = new Vector4(
                occlusionPlacement.HasTexture ? 1.0f : 0.0f,
                transmissionPlacement.HasTexture ? 1.0f : 0.0f,
                material.ClearcoatUsesTransmissionTexture ? 1.0f : 0.0f,
                0.0f);

            BaseAtlasTransform = basePlacement.AtlasTransform;
            BaseTextureAddress = basePlacement.TextureAddress;
            BaseTextureTransform = basePlacement.TextureTransform;
            MrAtlasTransform = mrPlacement.AtlasTransform;
            MrTextureAddress = mrPlacement.TextureAddress;
            MrTextureTransform = mrPlacement.TextureTransform;
            NormalAtlasTransform = normalPlacement.AtlasTransform;
            NormalTextureAddress = normalPlacement.TextureAddress;
            NormalTextureTransform = normalPlacement.TextureTransform;
            EmissiveAtlasTransform = emissivePlacement.AtlasTransform;
            EmissiveTextureAddress = emissivePlacement.TextureAddress;
            EmissiveTextureTransform = emissivePlacement.TextureTransform;
            OcclusionAtlasTransform = occlusionPlacement.AtlasTransform;
            OcclusionTextureAddress = occlusionPlacement.TextureAddress;
            OcclusionTextureTransform = occlusionPlacement.TextureTransform;
            TransmissionAtlasTransform = transmissionPlacement.AtlasTransform;
            TransmissionTextureAddress = transmissionPlacement.TextureAddress;
            TransmissionTextureTransform = transmissionPlacement.TextureTransform;
        }
    }

    private sealed class RasterTextureUpload
    {
        public required TextureMap Texture { get; init; }
        public required int X { get; init; }
        public required int Y { get; init; }
        public required int Page { get; init; }
    }

    private sealed class TextureBuildResult
    {
        public required IReadOnlyDictionary<TextureMap, RasterTexturePlacement> TexturePlacements { get; init; }
        public required IReadOnlyList<RasterTextureUpload> Uploads { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int TextureCount { get; init; }
        public required int PageCount { get; init; }
        public required bool UsesGpuTextureSampling { get; init; }
        public string? FallbackReason { get; init; }
    }

    private sealed class RasterGeometryBuildResult
    {
        public required IReadOnlyDictionary<Material, int> MaterialIds { get; init; }
        public required RasterMaterial[] Materials { get; set; }
        public int OpaqueTriangleCount { get; init; }
        public int TransparentTriangleCount { get; init; }
        public int NearClippedTriangles { get; init; }
        public int SourceTriangleCount { get; init; }
        public int OpaqueVertexCount => checked(OpaqueTriangleCount * 3);
        public int TransparentVertexCount => checked(TransparentTriangleCount * 3);
        public int MaterialCount => Materials.Length;
        public int TotalTriangles => checked(OpaqueTriangleCount + TransparentTriangleCount);
    }

    /// <summary>Releases the shared Vulkan graphics device and compiled raster pipeline.</summary>
    public static void DisposeSharedDevice()
    {
        // Disposal must not race an in-flight render. Veldrid object disposal during
        // command submission/readback can produce native driver crashes that bypass
        // normal C# exception handling.
        lock (RenderSync)
        {
            lock (DeviceSync)
            {
                GraphicsDevice? device = sharedGraphicsDevice;
                sharedGraphicsDevice = null;
                SharedRasterResources? resources = sharedRasterResources;
                sharedRasterResources = null;
                PreparedRasterScene? sceneResources = preparedScene;
                preparedScene = null;
                RasterTargets[] targets = sharedTargets.Values.ToArray();
                sharedTargets.Clear();

                if (device != null)
                {
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice: WaitForIdle"); device.WaitForIdle(); } catch { }
                }

                foreach (RasterTargets target in targets)
                {
                    try { target.Dispose(); } catch { }
                }
                try { sceneResources?.Dispose(); } catch { }
                try { resources?.Dispose(); } catch { }
                if (device != null)
                {
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice"); device.Dispose(); } catch { }
                }
            }
        }
    }

    /// <summary>
    /// Releases scene-sized Vulkan buffers while keeping the device, pipelines,
    /// and small render-target cache alive. Call this before replacing a large
    /// scene so the old GPU allocation does not overlap the new scene load.
    /// </summary>
    public static void ReleasePreparedScene()
    {
        lock (RenderSync)
        {
            lock (DeviceSync)
            {
                PreparedRasterScene? sceneResources = preparedScene;
                preparedScene = null;
                if (sceneResources == null)
                    return;

                try { sharedGraphicsDevice?.WaitForIdle(); } catch { }
                try { sceneResources.Dispose(); } catch { }
            }
        }
    }

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    public static RenderImage Render(
        Scene scene,
        Vec3 cameraPosition,
        CameraBasis basis,
        int width,
        int height,
        CancellationToken cancellationToken,
        out string details)
    {
        // Serialize the Veldrid/Vulkan raster path against disposal and resize/backend
        // switches. Without this, closing the form or switching renderers while a
        // preview frame is submitting can dispose the shared device underneath the
        // worker thread and crash inside the graphics driver instead of raising a
        // managed exception.
        lock (RenderSync)
        {
            return RenderLocked(scene, cameraPosition, basis, width, height, cancellationToken, out details);
        }
    }

    /// <summary>Renders one hardware-rasterized preview frame into a cross-platform RGBA image.</summary>
    private static RenderImage RenderLocked(
        Scene scene,
        Vec3 cameraPosition,
        CameraBasis basis,
        int width,
        int height,
        CancellationToken cancellationToken,
        out string details)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        Stopwatch total = Stopwatch.StartNew();
        Stopwatch phase = Stopwatch.StartNew();
        ResetStageLog(width, height);
        ThrowIfCancellationRequested(cancellationToken, "create Vulkan device");
        GraphicsDevice gd = GetOrCreateSharedDevice();
        long deviceMs = phase.ElapsedMilliseconds;

        phase.Restart();
        RasterTargets targets = GetOrCreateTargets(gd, width, height);
        SharedRasterResources resources = GetOrCreateSharedRasterResources(gd, targets.Framebuffer.OutputDescription);
        long targetMs = phase.ElapsedMilliseconds;

        phase.Restart();
        PreparedRasterScene prepared = GetOrCreatePreparedScene(gd, resources, scene, cancellationToken);
        long prepareMs = phase.ElapsedMilliseconds;

        phase.Restart();
        double cameraFar = ComputeCachedCameraFarPlane(prepared, cameraPosition);
        gd.UpdateBuffer(prepared.CameraBuffer, 0, new RasterCameraConstants(
            cameraPosition, basis, width, height, prepared.LightCount, prepared.TextureCount,
            RasterDebugMode, prepared.MaterialCount, cameraFar));
        long uniformMs = phase.ElapsedMilliseconds;

        ThrowIfCancellationRequested(cancellationToken, "record Vulkan raster command list");
        phase.Restart();
        CommandList commandList = targets.CommandList;
        commandList.Begin();
        commandList.SetFramebuffer(targets.Framebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0.035f, 0.040f, 0.050f, 1.0f));
        commandList.ClearDepthStencil(1.0f);
        if (prepared.OpaqueVertexCount > 0)
        {
            commandList.SetPipeline(resources.OpaquePipeline);
            commandList.SetGraphicsResourceSet(0, prepared.ResourceSet);
            commandList.SetVertexBuffer(0, prepared.OpaqueVertexBuffer);
            commandList.Draw((uint)prepared.OpaqueVertexCount);
        }
        if (prepared.TransparentVertexCount > 0)
        {
            commandList.SetPipeline(resources.TransparentPipeline);
            commandList.SetGraphicsResourceSet(0, prepared.ResourceSet);
            commandList.SetVertexBuffer(0, prepared.TransparentVertexBuffer);
            commandList.Draw((uint)prepared.TransparentVertexCount);
        }
        commandList.CopyTexture(targets.ColorTexture, 0, 0, 0, 0, 0,
            targets.StagingTexture, 0, 0, 0, 0, 0, (uint)width, (uint)height, 1, 1);
        commandList.End();
        long recordMs = phase.ElapsedMilliseconds;

        ThrowIfCancellationRequested(cancellationToken, "submit Vulkan raster command list");
        phase.Restart();
        gd.ResetFence(targets.Fence);
        gd.SubmitCommands(commandList, targets.Fence);
        gd.WaitForFence(targets.Fence);
        long gpuWaitMs = phase.ElapsedMilliseconds;

        ThrowIfCancellationRequested(cancellationToken, "read back Vulkan raster texture");
        phase.Restart();
        RenderImage image = ReadBackImage(gd, targets.StagingTexture, width, height);
        long readbackMs = phase.ElapsedMilliseconds;
        total.Stop();

        string textureMode = prepared.UsesGpuTextureSampling
            ? $"textures={prepared.TextureCount}, pages={prepared.TexturePageCount}"
            : prepared.GpuTextureSamplingRequested && !string.IsNullOrWhiteSpace(prepared.TextureFallbackReason)
                ? $"textures=base-color fallback ({prepared.TextureFallbackReason})"
                : "textures=base color";
        string triangleMode = prepared.NearClippedTriangleCount > 0
            ? $"triangles={prepared.TotalTriangleCount}/{prepared.SourceTriangleCount} ({prepared.NearClippedTriangleCount} invalid skipped)"
            : $"triangles={prepared.TotalTriangleCount}";
        details = $"VULKAN RASTER PBR CACHED - {width}x{height}, {triangleMode}, lights={prepared.LightCount}, {textureMode}, cache={(prepareMs == 0 ? "hot" : "ready")}, device={deviceMs}ms, targets={targetMs}ms, prepare={prepareMs}ms, uniform={uniformMs}ms, record={recordMs}ms, gpu+wait={gpuWaitMs}ms, readback={readbackMs}ms, total={total.ElapsedMilliseconds}ms";
        Stage(details);
        return image;
    }

    private static PreparedRasterScene GetOrCreatePreparedScene(GraphicsDevice gd, SharedRasterResources resources, Scene scene, CancellationToken cancellationToken)
    {
        bool gpuTextureSamplingRequested = UseGpuTextureSampling;
        if (preparedScene != null &&
            ReferenceEquals(preparedScene.Scene, scene) &&
            preparedScene.GpuTextureSamplingRequested == gpuTextureSamplingRequested)
            return preparedScene;

        preparedScene?.Dispose();
        preparedScene = null;
        ThrowIfCancellationRequested(cancellationToken, "build cached Vulkan scene");

        // Build only lightweight atlas placement metadata first. Pixel data is
        // transferred one texture at a time after the Vulkan texture exists, so
        // a full CPU-side atlas never coexists with the GPU atlas.
        TextureBuildResult textures = BuildTextureAtlas(gd, scene, gpuTextureSamplingRequested);
        RasterGeometryBuildResult geometry = BuildGeometryMetadata(scene, textures.TexturePlacements, textures.UsesGpuTextureSampling);
        RasterLight[] lights = BuildLightBuffer(scene);
        ResourceFactory factory = gd.ResourceFactory;
        int vertexStride = Marshal.SizeOf<RasterVertex>();
        int lightStride = Marshal.SizeOf<RasterLight>();
        int materialStride = Marshal.SizeOf<RasterMaterial>();
        int opaqueVertexCount = geometry.OpaqueVertexCount;
        int transparentVertexCount = geometry.TransparentVertexCount;
        int materialCount = geometry.MaterialCount;
        int totalTriangleCount = geometry.TotalTriangles;
        int nearClippedTriangleCount = geometry.NearClippedTriangles;

        uint opaqueBytes = CheckedBufferSize((long)opaqueVertexCount * vertexStride, vertexStride, "opaque vertex");
        uint transparentBytes = CheckedBufferSize((long)transparentVertexCount * vertexStride, vertexStride, "transparent vertex");
        uint lightBytes = CheckedBufferSize((long)lights.Length * lightStride, lightStride, "light");
        uint materialBytes = CheckedBufferSize((long)materialCount * materialStride, materialStride, "material");

        DeviceBuffer? opaque = null;
        DeviceBuffer? transparent = null;
        DeviceBuffer? camera = null;
        DeviceBuffer? light = null;
        DeviceBuffer? material = null;
        Texture? atlas = null;
        Sampler? sampler = null;
        ResourceSet? set = null;
        try
        {
            opaque = factory.CreateBuffer(new BufferDescription(opaqueBytes, BufferUsage.VertexBuffer));
            transparent = factory.CreateBuffer(new BufferDescription(transparentBytes, BufferUsage.VertexBuffer));
            camera = factory.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<RasterCameraConstants>(), BufferUsage.UniformBuffer));
            light = factory.CreateBuffer(new BufferDescription(lightBytes, BufferUsage.StructuredBufferReadOnly, (uint)lightStride));
            material = factory.CreateBuffer(new BufferDescription(materialBytes, BufferUsage.StructuredBufferReadOnly, (uint)materialStride));
            atlas = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)textures.Width, (uint)textures.Height, 1, (uint)textures.PageCount,
                Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            sampler = factory.CreateSampler(SamplerDescription.Linear);

            UploadRasterGeometry(gd, opaque, transparent, scene, geometry.MaterialIds, cancellationToken);
            gd.UpdateBuffer(material, 0, geometry.Materials.Length == 0 ? new[] { default(RasterMaterial) } : geometry.Materials);
            geometry.Materials = Array.Empty<RasterMaterial>();
            gd.UpdateBuffer(light, 0, lights.Length == 0 ? new[] { default(RasterLight) } : lights);
            UploadTextureAtlas(gd, atlas, textures, cancellationToken);

            set = factory.CreateResourceSet(new ResourceSetDescription(resources.Layout, camera, light, material, atlas, sampler));
            ComputeSceneBounds(scene, out Vec3 center, out double radius);
            preparedScene = new PreparedRasterScene
            {
                Scene = scene,
                OpaqueVertexBuffer = opaque,
                TransparentVertexBuffer = transparent,
                CameraBuffer = camera,
                LightBuffer = light,
                MaterialBuffer = material,
                AtlasTexture = atlas,
                AtlasSampler = sampler,
                ResourceSet = set,
                LightCount = lights.Length,
                OpaqueVertexCount = opaqueVertexCount,
                TransparentVertexCount = transparentVertexCount,
                MaterialCount = materialCount,
                SourceTriangleCount = geometry.SourceTriangleCount,
                TotalTriangleCount = totalTriangleCount,
                NearClippedTriangleCount = nearClippedTriangleCount,
                TextureCount = textures.TextureCount,
                TexturePageCount = textures.PageCount,
                GpuTextureSamplingRequested = gpuTextureSamplingRequested,
                UsesGpuTextureSampling = textures.UsesGpuTextureSampling,
                TextureFallbackReason = textures.FallbackReason,
                BoundingCenter = center,
                BoundingRadius = radius
            };
            return preparedScene;
        }
        catch
        {
            try { set?.Dispose(); } catch { }
            try { sampler?.Dispose(); } catch { }
            try { atlas?.Dispose(); } catch { }
            try { material?.Dispose(); } catch { }
            try { light?.Dispose(); } catch { }
            try { camera?.Dispose(); } catch { }
            try { transparent?.Dispose(); } catch { }
            try { opaque?.Dispose(); } catch { }
            throw;
        }
    }

    private static void UploadRasterGeometry(
        GraphicsDevice gd,
        DeviceBuffer opaqueVertexBuffer,
        DeviceBuffer transparentVertexBuffer,
        Scene scene,
        IReadOnlyDictionary<Material, int> materialIds,
        CancellationToken cancellationToken)
    {
        // Keep upload memory constant while preserving the separate transparent
        // pass. The previous low-memory rewrite uploaded every triangle into the
        // opaque buffer and hard-coded the transparent count to zero, which made
        // transmission and alpha-blended materials depth-write as solid white.
        const int TrianglesPerChunk = 2048;
        int vertexStride = Marshal.SizeOf<RasterVertex>();
        RasterVertex[] opaqueVertices = new RasterVertex[TrianglesPerChunk * 3];
        RasterVertex[] transparentVertices = new RasterVertex[TrianglesPerChunk * 3];
        int opaqueTrianglesInChunk = 0;
        int transparentTrianglesInChunk = 0;
        int uploadedOpaqueTriangles = 0;
        int uploadedTransparentTriangles = 0;
        int processedTriangles = 0;

        foreach (Triangle triangle in scene.Triangles)
        {
            if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
                continue;

            int materialIndex = materialIds[triangle.Material];
            bool transparent = IsTransparentMaterial(triangle.Material);
            RasterVertex[] target = transparent ? transparentVertices : opaqueVertices;
            int trianglesInChunk = transparent ? transparentTrianglesInChunk : opaqueTrianglesInChunk;
            int vertexIndex = trianglesInChunk * 3;
            target[vertexIndex] = new RasterVertex(triangle.A, triangle.NormalA, triangle.UvA, materialIndex);
            target[vertexIndex + 1] = new RasterVertex(triangle.B, triangle.NormalB, triangle.UvB, materialIndex);
            target[vertexIndex + 2] = new RasterVertex(triangle.C, triangle.NormalC, triangle.UvC, materialIndex);

            if (transparent)
            {
                transparentTrianglesInChunk++;
                if (transparentTrianglesInChunk == TrianglesPerChunk)
                {
                    UploadChunk(transparentVertexBuffer, transparentVertices, transparentTrianglesInChunk, ref uploadedTransparentTriangles);
                    transparentTrianglesInChunk = 0;
                }
            }
            else
            {
                opaqueTrianglesInChunk++;
                if (opaqueTrianglesInChunk == TrianglesPerChunk)
                {
                    UploadChunk(opaqueVertexBuffer, opaqueVertices, opaqueTrianglesInChunk, ref uploadedOpaqueTriangles);
                    opaqueTrianglesInChunk = 0;
                }
            }

            processedTriangles++;
            if (processedTriangles % (TrianglesPerChunk * 2) == 0)
                ThrowIfCancellationRequested(cancellationToken, "upload Vulkan raster geometry");
        }

        if (opaqueTrianglesInChunk > 0)
            UploadChunk(opaqueVertexBuffer, opaqueVertices, opaqueTrianglesInChunk, ref uploadedOpaqueTriangles);
        if (transparentTrianglesInChunk > 0)
            UploadChunk(transparentVertexBuffer, transparentVertices, transparentTrianglesInChunk, ref uploadedTransparentTriangles);

        void UploadChunk(DeviceBuffer destination, RasterVertex[] vertices, int triangleCount, ref int uploadedTriangles)
        {
            RasterVertex[] vertexUpload = vertices;
            if (triangleCount != TrianglesPerChunk)
            {
                vertexUpload = new RasterVertex[triangleCount * 3];
                Array.Copy(vertices, vertexUpload, vertexUpload.Length);
            }

            uint vertexOffset = checked((uint)((long)uploadedTriangles * 3L * vertexStride));
            gd.UpdateBuffer(destination, vertexOffset, vertexUpload);
            uploadedTriangles += triangleCount;
        }
    }

    private static void UploadTextureAtlas(GraphicsDevice gd, Texture atlas, TextureBuildResult textures, CancellationToken cancellationToken)
    {
        if (textures.Uploads.Count == 0)
        {
            gd.UpdateTexture(atlas, new[] { 0xffffffffu }, 0, 0, 0, 1, 1, 1, 0, 0);
            return;
        }

        foreach (RasterTextureUpload upload in textures.Uploads)
        {
            ThrowIfCancellationRequested(cancellationToken, "upload Vulkan raster texture atlas");
            TextureMap texture = upload.Texture;
            int width = Math.Max(1, texture.Width);
            int height = Math.Max(1, texture.Height);
            uint[] source = texture.CopyPackedRgba32Pixels();
            int expectedPixels = checked(width * height);
            if (source.Length != expectedPixels)
                throw new InvalidOperationException($"Texture '{texture.Name}' returned {source.Length} pixels; expected {expectedPixels}.");

            gd.UpdateTexture(atlas, source, (uint)upload.X, (uint)upload.Y, 0, (uint)width, (uint)height, 1, 0, (uint)upload.Page);

            // Upload only the one-pixel borders needed by linear filtering.
            // This avoids allocating a second padded copy of the whole texture.
            uint[] top = new uint[width];
            uint[] bottom = new uint[width];
            Array.Copy(source, 0, top, 0, width);
            Array.Copy(source, (height - 1) * width, bottom, 0, width);
            gd.UpdateTexture(atlas, top, (uint)upload.X, (uint)(upload.Y - 1), 0, (uint)width, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas, bottom, (uint)upload.X, (uint)(upload.Y + height), 0, (uint)width, 1, 1, 0, (uint)upload.Page);

            uint[] left = new uint[height];
            uint[] right = new uint[height];
            for (int y = 0; y < height; y++)
            {
                left[y] = source[y * width];
                right[y] = source[y * width + width - 1];
            }
            gd.UpdateTexture(atlas, left, (uint)(upload.X - 1), (uint)upload.Y, 0, 1, (uint)height, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas, right, (uint)(upload.X + width), (uint)upload.Y, 0, 1, (uint)height, 1, 0, (uint)upload.Page);

            gd.UpdateTexture(atlas, new[] { source[0] }, (uint)(upload.X - 1), (uint)(upload.Y - 1), 0, 1, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas, new[] { source[width - 1] }, (uint)(upload.X + width), (uint)(upload.Y - 1), 0, 1, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas, new[] { source[(height - 1) * width] }, (uint)(upload.X - 1), (uint)(upload.Y + height), 0, 1, 1, 1, 0, (uint)upload.Page);
            gd.UpdateTexture(atlas, new[] { source[source.Length - 1] }, (uint)(upload.X + width), (uint)(upload.Y + height), 0, 1, 1, 1, 0, (uint)upload.Page);
            source = Array.Empty<uint>();
        }
    }

    private static uint CheckedBufferSize(long requestedBytes, int minimumBytes, string label)
    {
        long bytes = Math.Max(minimumBytes, requestedBytes);
        if (bytes > uint.MaxValue)
            throw new InvalidOperationException($"The Vulkan {label} buffer would require {bytes / (1024.0 * 1024.0):F0} MB, which exceeds the 4 GB buffer limit.");
        return checked((uint)bytes);
    }

    private static RasterTargets GetOrCreateTargets(GraphicsDevice gd, int width, int height)
    {
        (int Width, int Height) key = (width, height);
        if (sharedTargets.TryGetValue(key, out RasterTargets? cached))
        {
            cached.LastUseSerial = ++targetUseSerial;
            return cached;
        }

        if (sharedTargets.Count >= MaxCachedTargetSizes)
        {
            KeyValuePair<(int Width, int Height), RasterTargets> oldest =
                sharedTargets.MinBy(pair => pair.Value.LastUseSerial);
            sharedTargets.Remove(oldest.Key);
            oldest.Value.Dispose();
            Stage($"Evict Vulkan raster target cache: {oldest.Key.Width}x{oldest.Key.Height}");
        }

        ResourceFactory factory = gd.ResourceFactory;
        Texture color = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        Texture depth = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.D32_Float_S8_UInt, TextureUsage.DepthStencil));
        Texture staging = factory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        RasterTargets created = new()
        {
            Width = width,
            Height = height,
            ColorTexture = color,
            DepthTexture = depth,
            StagingTexture = staging,
            Framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depth, color)),
            CommandList = factory.CreateCommandList(),
            Fence = factory.CreateFence(false),
            LastUseSerial = ++targetUseSerial
        };
        sharedTargets.Add(key, created);
        Stage($"Cache Vulkan raster targets: {width}x{height} ({sharedTargets.Count}/{MaxCachedTargetSizes})");
        return created;
    }

    private static void ComputeSceneBounds(Scene scene, out Vec3 center, out double radius)
    {
        if (scene.Triangles.Count == 0) { center = Vec3.Zero; radius = 10; return; }
        Vec3 min = scene.Triangles[0].A, max = min;
        foreach (Triangle t in scene.Triangles)
        {
            Include(t.A);
            Include(t.B);
            Include(t.C);
        }
        center = (min + max) / 2.0;
        radius = Math.Max(1.0, (max - center).Length());

        void Include(Vec3 p)
        {
            min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
    }

    private static double ComputeCachedCameraFarPlane(PreparedRasterScene scene, Vec3 cameraPosition) =>
        Math.Clamp((scene.BoundingCenter - cameraPosition).Length() + scene.BoundingRadius * 1.1 + 1.0, 10.0, CameraFar);

    private static GraphicsDevice GetOrCreateSharedDevice()
    {
        lock (DeviceSync)
        {
            if (sharedGraphicsDevice != null)
            {
                Stage("Reuse shared Veldrid Vulkan raster GraphicsDevice");
                return sharedGraphicsDevice;
            }

            if (!preflightCompleted)
            {
                Stage("Preflight Veldrid Vulkan raster GraphicsDevice in child process");
                VeldridVulkanDevicePreflight.VerifyInChildProcess(Stage);
                preflightCompleted = true;
            }

            Stage("Create shared Veldrid Vulkan raster GraphicsDevice");
            GraphicsDevice gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions
            {
                Debug = IsVulkanDebugEnabled(),
                // Use Vulkan-native clip-space Y and flip Y explicitly in the vertex shader.
                // This avoids depending on backend-specific viewport normalization while
                // keeping the projection math easy to audit.
                PreferStandardClipSpaceYDirection = false,
                PreferDepthRangeZeroToOne = true,
                SyncToVerticalBlank = false
            });

            if (gd.BackendType != GraphicsBackend.Vulkan)
            {
                gd.Dispose();
                throw new InvalidOperationException("Veldrid did not create a Vulkan graphics device for the raster backend.");
            }

            sharedGraphicsDevice = gd;
            return gd;
        }
    }

    private static SharedRasterResources GetOrCreateSharedRasterResources(GraphicsDevice gd, OutputDescription outputDescription)
    {
        lock (DeviceSync)
        {
            if (sharedRasterResources != null)
                return sharedRasterResources;

            ResourceFactory factory = gd.ResourceFactory;
            Stage("Create Vulkan raster resource layout");
            ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("CameraConstants", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
                new ResourceLayoutElementDescription("LightBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MaterialBuffer", ResourceKind.StructuredBufferReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("AtlasTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("AtlasSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            try
            {
                Shader[] shaders = CreateRasterShaders(factory);
                try
                {
                    VertexLayoutDescription vertexLayout = new(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                        new VertexElementDescription("Uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                        new VertexElementDescription("MaterialIndex", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1));

                    RasterizerStateDescription rasterizerState = new(
                        cullMode: FaceCullMode.None,
                        fillMode: PolygonFillMode.Solid,
                        frontFace: FrontFace.Clockwise,
                        depthClipEnabled: true,
                        scissorTestEnabled: false);

                    ShaderSetDescription shaderSet = new(new[] { vertexLayout }, shaders);

                    GraphicsPipelineDescription opaquePipelineDescription = new()
                    {
                        BlendState = BlendStateDescription.SingleDisabled,
                        DepthStencilState = new DepthStencilStateDescription(
                            depthTestEnabled: true,
                            depthWriteEnabled: true,
                            comparisonKind: ComparisonKind.Less),
                        RasterizerState = rasterizerState,
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        ResourceLayouts = new[] { layout },
                        ShaderSet = shaderSet,
                        Outputs = outputDescription
                    };

                    GraphicsPipelineDescription transparentPipelineDescription = new()
                    {
                        BlendState = BlendStateDescription.SingleAlphaBlend,
                        DepthStencilState = new DepthStencilStateDescription(
                            depthTestEnabled: true,
                            depthWriteEnabled: false,
                            comparisonKind: ComparisonKind.Less),
                        RasterizerState = rasterizerState,
                        PrimitiveTopology = PrimitiveTopology.TriangleList,
                        ResourceLayouts = new[] { layout },
                        ShaderSet = shaderSet,
                        Outputs = outputDescription
                    };

                    Pipeline opaquePipeline = factory.CreateGraphicsPipeline(opaquePipelineDescription);
                    Pipeline transparentPipeline;
                    try
                    {
                        transparentPipeline = factory.CreateGraphicsPipeline(transparentPipelineDescription);
                    }
                    catch
                    {
                        try { opaquePipeline.Dispose(); } catch { }
                        throw;
                    }

                    sharedRasterResources = new SharedRasterResources
                    {
                        Layout = layout,
                        Shaders = shaders,
                        OpaquePipeline = opaquePipeline,
                        TransparentPipeline = transparentPipeline
                    };
                    Stage("Vulkan raster graphics pipelines created: CPU-raster parity opaque z-buffer path");
                    return sharedRasterResources;
                }
                catch
                {
                    foreach (Shader shader in shaders)
                    {
                        try { shader.Dispose(); } catch { }
                    }
                    throw;
                }
            }
            catch
            {
                layout.Dispose();
                throw;
            }
        }
    }

    private static Shader[] CreateRasterShaders(ResourceFactory factory)
    {
        ShaderDescription vertex = new(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(VertexShaderSource),
            "main");
        ShaderDescription fragment = new(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(FragmentShaderSource),
            "main");

        try
        {
            Stage("Compile/create Vulkan raster vertex+fragment shaders");
            return factory.CreateFromSpirv(vertex, fragment);
        }
        catch (Exception ex)
        {
            Stage("Vulkan raster shader compile/create failed");
            Stage(ex.ToString());
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-failed.vert"), VertexShaderSource);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-failed.frag"), FragmentShaderSource);
            }
            catch { }
            throw new InvalidOperationException("Vulkan raster shader compilation failed. See " + StageLogPath + " and %TEMP%\\LightingShowcase-vulkan-raster-failed.vert/.frag.", ex);
        }
    }

    private static RasterGeometryBuildResult BuildGeometryMetadata(
        Scene scene,
        IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements,
        bool useGpuTextureSampling)
    {
        int opaqueTriangleCount = 0;
        int transparentTriangleCount = 0;
        int nearClippedTriangles = 0;
        Dictionary<Material, int> materialIds = new();
        List<RasterMaterial> materials = new();

        foreach (Triangle triangle in scene.Triangles)
        {
            if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C) || !IsFinite(triangle.Normal))
            {
                nearClippedTriangles++;
                continue;
            }

            if (IsTransparentMaterial(triangle.Material))
                transparentTriangleCount++;
            else
                opaqueTriangleCount++;

            if (!materialIds.ContainsKey(triangle.Material))
            {
                RasterTexturePlacement basePlacement = useGpuTextureSampling
                    ? TexturePlacement(texturePlacements, triangle.Material.Texture)
                    : RasterTexturePlacement.None;
                RasterTexturePlacement mrPlacement = useGpuTextureSampling
                    ? TexturePlacement(texturePlacements, triangle.Material.MetallicRoughnessTexture)
                    : RasterTexturePlacement.None;
                RasterTexturePlacement normalPlacement = useGpuTextureSampling
                    ? TexturePlacement(texturePlacements, triangle.Material.NormalTexture)
                    : RasterTexturePlacement.None;
                RasterTexturePlacement emissivePlacement = useGpuTextureSampling
                    ? TexturePlacement(texturePlacements, triangle.Material.EmissiveTexture)
                    : RasterTexturePlacement.None;
                RasterTexturePlacement occlusionPlacement = useGpuTextureSampling
                    ? TexturePlacement(texturePlacements, triangle.Material.OcclusionTexture)
                    : RasterTexturePlacement.None;
                RasterTexturePlacement transmissionPlacement = useGpuTextureSampling
                    ? TexturePlacement(texturePlacements, triangle.Material.TransmissionTexture)
                    : RasterTexturePlacement.None;
                materialIds.Add(triangle.Material, materials.Count);
                materials.Add(new RasterMaterial(
                    triangle.Material, basePlacement, mrPlacement, normalPlacement, emissivePlacement, occlusionPlacement, transmissionPlacement));
            }
        }

        return new RasterGeometryBuildResult
        {
            MaterialIds = materialIds,
            Materials = materials.ToArray(),
            OpaqueTriangleCount = opaqueTriangleCount,
            TransparentTriangleCount = transparentTriangleCount,
            NearClippedTriangles = nearClippedTriangles,
            SourceTriangleCount = scene.Triangles.Count
        };
    }

    private static double TriangleViewDepth(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
    {
        Vec3 centroid = (triangle.A + triangle.B + triangle.C) / 3.0;
        return (centroid - cameraPosition).Dot(basis.Forward);
    }

    private static bool IsRenderableTriangle(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
    {
        if (!IsFinite(triangle.A) || !IsFinite(triangle.B) || !IsFinite(triangle.C))
            return false;
        if (!IsFinite(triangle.Normal))
            return false;

        // Avoid sending wildly distant coordinates or invalid camera-space values
        // into the Vulkan clipper. The CPU rasterizer naturally rejects these via
        // TryProjectCamera; doing it on the CPU side keeps the GPU path from ever
        // seeing NaN/Inf/overflow-prone vertices.
        return IsTriangleInFrontOfCamera(triangle, cameraPosition, basis);
    }

    private static bool IsFinite(Vec3 value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    private static bool IsTriangleInFrontOfCamera(Triangle triangle, Vec3 cameraPosition, CameraBasis basis)
    {
        return CameraDepth(triangle.A, cameraPosition, basis) > CameraNear &&
               CameraDepth(triangle.B, cameraPosition, basis) > CameraNear &&
               CameraDepth(triangle.C, cameraPosition, basis) > CameraNear;
    }

    private static double CameraDepth(Vec3 point, Vec3 cameraPosition, CameraBasis basis)
    {
        double depth = (point - cameraPosition).Dot(basis.Forward);
        return double.IsFinite(depth) ? depth : double.NegativeInfinity;
    }

    private static Vector4 MaterialColorAlpha(Material material)
    {
        double effectiveAlpha = EffectiveMaterialAlpha(material);
        return new Vector4(
            (float)Math.Clamp(material.Color.X, 0.0, 1.0),
            (float)Math.Clamp(material.Color.Y, 0.0, 1.0),
            (float)Math.Clamp(material.Color.Z, 0.0, 1.0),
            (float)Math.Clamp(effectiveAlpha, 0.0, 1.0));
    }

    private static Vector4 BakedMaterialColorAlpha(Material material, Vec2 uv)
    {
        Vec3 color = material.Sample(uv.U, uv.V);
        double alpha = material.SampleAlpha(uv.U, uv.V);
        return new Vector4(
            (float)Math.Clamp(color.X, 0.0, 1.0),
            (float)Math.Clamp(color.Y, 0.0, 1.0),
            (float)Math.Clamp(color.Z, 0.0, 1.0),
            (float)Math.Clamp(alpha, 0.0, 1.0));
    }

    private static Vector4 MaterialEmission(Material material)
    {
        return new Vector4(
            (float)Math.Max(0.0, material.EmissionColor.X),
            (float)Math.Max(0.0, material.EmissionColor.Y),
            (float)Math.Max(0.0, material.EmissionColor.Z),
            (float)Math.Max(0.0, material.Emission));
    }

    private static Vector4 BakedMaterialEmission(Material material, Vec2 uv)
    {
        return MaterialEmission(material);
    }

    private static bool IsTransparentMaterial(Material material)
    {
        // KHR_materials_transmission does not make a surface geometrically
        // transparent. OPAQUE and MASK transmission materials still represent
        // a covered surface and must write depth. Sending them through the
        // alpha-blended, depth-write-disabled pass lets farther lamp parts draw
        // over the nearer glass as the camera orbits. Only genuine alpha BLEND
        // materials belong in the transparent pass.
        return material.AlphaMode == MaterialAlphaMode.Blend;
    }

    private static double EffectiveMaterialAlpha(Material material)
    {
        // Match ShadowRasterRenderer, not the ray tracer: the fast CPU raster mode
        // uses base alpha only as a discard mask and does not reduce opacity for
        // transmission.  Treating transmissive/glass materials as blended in the
        // Vulkan preview was the main source of objects looking like they were
        // drawing through each other.
        return Math.Clamp(material.Alpha, 0.0, 1.0);
    }

    private static RasterLight[] BuildLightBuffer(Scene scene)
    {
        List<RasterLight> lights = new();
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;
            lights.Add(new RasterLight(light));
            if (lights.Count >= MaxGpuLights)
                break;
        }
        return lights.ToArray();
    }

    private static double ComputeCameraFarPlane(Scene scene, Vec3 cameraPosition, CameraBasis basis)
    {
        // The CPU shadow rasterizer stores camera-space depth directly in a
        // double z-buffer. The old Vulkan preview used a fixed 5000-unit far
        // plane, which compressed the room scene into a tiny part of a D24
        // depth buffer and made close/copied/coplanar props fight for depth.
        // Pick the far plane from the current camera view so Vulkan gets depth
        // precision close to the CPU path.
        double maxDepth = CameraNear + 1.0;
        foreach (Triangle triangle in scene.Triangles)
        {
            IncludeDepth(triangle.A);
            IncludeDepth(triangle.B);
            IncludeDepth(triangle.C);
        }

        double margin = Math.Max(1.0, maxDepth * 0.08);
        return Math.Clamp(maxDepth + margin, 10.0, CameraFar);

        void IncludeDepth(Vec3 point)
        {
            double depth = CameraDepth(point, cameraPosition, basis);
            if (double.IsFinite(depth) && depth > maxDepth)
                maxDepth = depth;
        }
    }

    private static TextureBuildResult BuildTextureAtlas(GraphicsDevice gd, Scene scene, bool gpuTextureSamplingRequested)
    {
        if (!gpuTextureSamplingRequested)
            return CreateBakedTextureResult(null);

        List<TextureMap> textures = new();
        HashSet<TextureMap> seen = new();
        foreach (Triangle triangle in scene.Triangles)
        {
            AddTexture(triangle.Material.Texture);
            AddTexture(triangle.Material.MetallicRoughnessTexture);
            AddTexture(triangle.Material.NormalTexture);
            AddTexture(triangle.Material.EmissiveTexture);
            AddTexture(triangle.Material.OcclusionTexture);
            AddTexture(triangle.Material.TransmissionTexture);
        }

        void AddTexture(TextureMap? texture)
        {
            if (texture != null && seen.Add(texture))
                textures.Add(texture);
        }

        if (textures.Count == 0)
        {
            return new TextureBuildResult
            {
                TexturePlacements = new Dictionary<TextureMap, RasterTexturePlacement>(),
                Uploads = Array.Empty<RasterTextureUpload>(),
                Width = 1,
                Height = 1,
                TextureCount = 0,
                PageCount = 1,
                UsesGpuTextureSampling = true
            };
        }

        const int padding = 1;
        const Veldrid.PixelFormat atlasFormat = Veldrid.PixelFormat.R8_G8_B8_A8_UNorm;
        if (!gd.GetPixelFormatSupport(atlasFormat, TextureType.Texture2D, TextureUsage.Sampled, out PixelFormatProperties support))
            throw new InvalidOperationException("The Vulkan device does not support sampled RGBA8 texture arrays.");

        int maxPageWidth = checked((int)Math.Min((uint)int.MaxValue, support.MaxWidth));
        int maxPageHeight = checked((int)Math.Min((uint)int.MaxValue, support.MaxHeight));
        int maxPageCount = checked((int)Math.Min((uint)int.MaxValue, support.MaxArrayLayers));
        if (maxPageWidth <= 0 || maxPageHeight <= 0 || maxPageCount <= 0)
            throw new InvalidOperationException("The Vulkan device reported invalid sampled-texture array limits.");

        List<TextureMap> sorted = textures
            .OrderByDescending(texture => Math.Max(texture.Width, texture.Height))
            .ThenByDescending(texture => checked((long)Math.Max(1, texture.Width) * Math.Max(1, texture.Height)))
            .ToList();

        long totalArea = 0;
        int largestEntryWidth = 1;
        int largestEntryHeight = 1;
        foreach (TextureMap texture in sorted)
        {
            int entryWidth = checked(Math.Max(1, texture.Width) + padding * 2);
            int entryHeight = checked(Math.Max(1, texture.Height) + padding * 2);
            if (entryWidth > maxPageWidth || entryHeight > maxPageHeight)
            {
                throw new InvalidOperationException(
                    $"Texture '{texture.Name}' is {texture.Width}x{texture.Height}; with filtering borders it exceeds " +
                    $"this Vulkan device's {maxPageWidth}x{maxPageHeight} sampled-texture limit.");
            }

            totalArea = checked(totalArea + checked((long)entryWidth * entryHeight));
            largestEntryWidth = Math.Max(largestEntryWidth, entryWidth);
            largestEntryHeight = Math.Max(largestEntryHeight, entryHeight);
        }

        List<int> candidateWidths = BuildDimensionCandidates(largestEntryWidth, maxPageWidth, totalArea);
        List<int> candidateHeights = BuildDimensionCandidates(largestEntryHeight, maxPageHeight, totalArea);

        List<RasterTextureUpload>? bestUploads = null;
        int bestWidth = 0;
        int bestHeight = 0;
        int bestPageCount = 0;
        long bestAllocatedPixels = long.MaxValue;
        long bestLargestPage = long.MaxValue;

        foreach (int candidateWidth in candidateWidths)
        {
            foreach (int candidateHeight in candidateHeights)
            {
                if (!TryPackPages(candidateWidth, candidateHeight, out List<RasterTextureUpload> uploads, out int pageCount))
                    continue;

                long pagePixels = checked((long)candidateWidth * candidateHeight);
                long allocatedPixels = checked(pagePixels * pageCount);
                if (allocatedPixels > bestAllocatedPixels ||
                    (allocatedPixels == bestAllocatedPixels && pagePixels >= bestLargestPage))
                {
                    continue;
                }

                bestAllocatedPixels = allocatedPixels;
                bestLargestPage = pagePixels;
                bestWidth = candidateWidth;
                bestHeight = candidateHeight;
                bestPageCount = pageCount;
                bestUploads = uploads;
            }
        }

        if (bestUploads == null)
        {
            throw new InvalidOperationException(
                $"The scene's {textures.Count} textures cannot be packed into the Vulkan device's " +
                $"{maxPageWidth}x{maxPageHeight}, {maxPageCount}-layer sampled-texture array limits.");
        }

        Dictionary<TextureMap, RasterTexturePlacement> texturePlacements = new(bestUploads.Count);
        foreach (RasterTextureUpload upload in bestUploads)
        {
            TextureMap texture = upload.Texture;
            float offsetX = (upload.X + 0.5f) / bestWidth;
            float offsetY = (upload.Y + 0.5f) / bestHeight;
            float scaleX = texture.Width <= 1 ? 0.0f : (texture.Width - 1.0f) / bestWidth;
            float scaleY = texture.Height <= 1 ? 0.0f : (texture.Height - 1.0f) / bestHeight;
            texturePlacements[texture] = new RasterTexturePlacement(
                offsetX, offsetY, scaleX, scaleY, upload.Page, texture);
        }

        Stage(
            $"Vulkan raster texture pages: {textures.Count} textures -> " +
            $"{bestPageCount} layer(s) of {bestWidth}x{bestHeight}; " +
            $"allocated={bestAllocatedPixels * 4.0 / (1024.0 * 1024.0):F1} MB");

        return new TextureBuildResult
        {
            TexturePlacements = texturePlacements,
            Uploads = bestUploads,
            Width = bestWidth,
            Height = bestHeight,
            TextureCount = texturePlacements.Count,
            PageCount = bestPageCount,
            UsesGpuTextureSampling = true
        };

        List<int> BuildDimensionCandidates(int minimum, int maximum, long area)
        {
            SortedSet<int> candidates = new();
            AddCandidate(minimum);
            AddCandidate(NextPowerOfTwo(minimum));

            int squareEstimate = checked((int)Math.Min(int.MaxValue, Math.Ceiling(Math.Sqrt(Math.Max(1L, area)))));
            AddCandidate(squareEstimate);
            AddCandidate(NextPowerOfTwo(squareEstimate));

            long power = NextPowerOfTwo(minimum);
            while (power > 0 && power <= maximum)
            {
                AddCandidate((int)power);
                if (power > int.MaxValue / 2)
                    break;
                power *= 2;
            }

            // The physical device limit is included as a last-resort candidate;
            // the score still chooses the smallest total allocation that fits.
            AddCandidate(maximum);
            return candidates.ToList();

            void AddCandidate(int value)
            {
                if (value < minimum)
                    value = minimum;
                if (value > maximum)
                    value = maximum;
                candidates.Add(value);
            }
        }

        bool TryPackPages(int pageWidth, int pageHeight, out List<RasterTextureUpload> uploads, out int pageCount)
        {
            uploads = new List<RasterTextureUpload>(sorted.Count);
            int page = 0;
            int cursorX = 0;
            int cursorY = 0;
            int rowHeight = 0;

            foreach (TextureMap texture in sorted)
            {
                int entryWidth = checked(Math.Max(1, texture.Width) + padding * 2);
                int entryHeight = checked(Math.Max(1, texture.Height) + padding * 2);

                if (cursorX > 0 && cursorX + entryWidth > pageWidth)
                {
                    cursorY = checked(cursorY + rowHeight);
                    cursorX = 0;
                    rowHeight = 0;
                }

                if (cursorY + entryHeight > pageHeight)
                {
                    page++;
                    if (page >= maxPageCount)
                    {
                        pageCount = 0;
                        uploads.Clear();
                        return false;
                    }
                    cursorX = 0;
                    cursorY = 0;
                    rowHeight = 0;
                }

                uploads.Add(new RasterTextureUpload
                {
                    Texture = texture,
                    X = cursorX + padding,
                    Y = cursorY + padding,
                    Page = page
                });
                cursorX = checked(cursorX + entryWidth);
                rowHeight = Math.Max(rowHeight, entryHeight);
            }

            pageCount = page + 1;
            return pageCount <= maxPageCount;
        }

        static int NextPowerOfTwo(int value)
        {
            if (value <= 1)
                return 1;
            uint v = checked((uint)value - 1u);
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            return v >= 0x7fffffffu ? int.MaxValue : checked((int)(v + 1u));
        }
    }

    private static TextureBuildResult CreateBakedTextureResult(string? reason) => new()
    {
        TexturePlacements = new Dictionary<TextureMap, RasterTexturePlacement>(),
        Uploads = Array.Empty<RasterTextureUpload>(),
        Width = 1,
        Height = 1,
        TextureCount = 0,
        PageCount = 1,
        UsesGpuTextureSampling = false,
        FallbackReason = reason
    };

    private static RasterTexturePlacement TexturePlacement(IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements, TextureMap? texture)
    {
        if (texture == null)
            return RasterTexturePlacement.None;
        return texturePlacements.TryGetValue(texture, out RasterTexturePlacement placement) ? placement : RasterTexturePlacement.None;
    }

    private static unsafe RenderImage ReadBackImage(GraphicsDevice gd, Texture stagingTexture, int width, int height)
    {
        uint[] pixels = new uint[checked(width * height)];
        MappedResource mapped = gd.Map(stagingTexture, MapMode.Read);
        try
        {
            fixed (uint* destinationBase = pixels)
            {
                nuint rowBytes = checked((nuint)(width * sizeof(uint)));
                for (int y = 0; y < height; y++)
                {
                    byte* source = (byte*)mapped.Data + checked((nint)(y * mapped.RowPitch));
                    uint* destination = destinationBase + y * width;
                    Buffer.MemoryCopy(source, destination, rowBytes, rowBytes);
                    for (int x = 0; x < width; x++)
                        destination[x] |= 0xff000000u;
                }
            }
        }
        finally
        {
            gd.Unmap(stagingTexture);
        }
        return new RenderImage(width, height, pixels);
    }

    private static bool StageLoggingEnabled => IsVulkanDebugEnabled() || string.Equals(Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_STAGE_LOG"), "1", StringComparison.OrdinalIgnoreCase);

    private static void ResetStageLog(int width, int height)
    {
        if (!StageLoggingEnabled) return;
        try
        {
            File.WriteAllText(StageLogPath,
                $"LightingShowcase Vulkan raster stage log{Environment.NewLine}" +
                $"Resolution: {width}x{height}{Environment.NewLine}" +
                $"Started: {DateTime.Now:O}{Environment.NewLine}");
        }
        catch { }
    }

    private static void Stage(string name)
    {
        if (!StageLoggingEnabled) return;
        try { File.AppendAllText(StageLogPath, $"{DateTime.Now:O} - {name}{Environment.NewLine}"); }
        catch { }
    }

    private static void ThrowIfCancellationRequested(CancellationToken cancellationToken, string stageName)
    {
        if (!cancellationToken.IsCancellationRequested)
            return;
        Stage("Cancellation requested before/at: " + stageName);
        throw new OperationCanceledException(cancellationToken);
    }


    private static bool IsVulkanDebugEnabled()
    {
        string? value = Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VULKAN_DEBUG");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string DebugModeName(int mode) => mode switch
    {
        1 => "uv",
        2 => "atlas",
        3 => "texture",
        4 => "material",
        5 => "normal",
        6 => "metallic",
        7 => "roughness",
        8 => "occlusion",
        9 => "emissive",
        10 => "direct",
        11 => "ibl",
        _ => "off"
    };

    private static Vector3 ToVector3(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
    private static Vector4 ToVector4(Vec3 value, float w) => new((float)value.X, (float)value.Y, (float)value.Z, w);

    private const string VertexShaderSource = @"
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 Uv;
layout(location = 3) in float MaterialIndex;

layout(location = 0) out vec3 fsWorldPos;
layout(location = 1) out vec3 fsNormal;
layout(location = 2) out vec2 fsUv;
layout(location = 3) flat out int fsMaterialIndex;

layout(std140, set = 0, binding = 0) uniform CameraConstants
{
    vec4 CameraPosition;
    vec4 CameraForward;
    vec4 CameraRight;
    vec4 CameraUp;
    vec4 Projection;
    vec4 Counts;
} Camera;

void main()
{
    vec3 rel = Position - Camera.CameraPosition.xyz;
    float vx = dot(rel, Camera.CameraRight.xyz);
    float vy = dot(rel, Camera.CameraUp.xyz);
    float vz = dot(rel, Camera.CameraForward.xyz);

    float aspect = max(Camera.Projection.x, 0.0001);
    float tanHalfFov = max(Camera.Projection.y, 0.0001);
    float nearPlane = max(Camera.Projection.z, 0.0001);
    float farPlane = max(Camera.Projection.w, nearPlane + 1.0);
    float z = vz;
    float ndcDepth = (z - nearPlane) / (farPlane - nearPlane);

    gl_Position = vec4(
        -vx / (aspect * tanHalfFov),
         vy / tanHalfFov,
         ndcDepth * z,
         z);
    gl_Position.y = -gl_Position.y;

    fsWorldPos = Position;
    fsNormal = Normal;
    fsUv = Uv;
    fsMaterialIndex = int(MaterialIndex + 0.5);
}
";

    private const string FragmentShaderSource = @"
#version 450

const float PI = 3.14159265358979323846;

struct RasterLight
{
    vec4 PositionKind;
    vec4 DirectionRange;
    vec4 ColorIntensity;
    vec4 ConeEnabled;
};

struct RasterMaterial
{
    vec4 BaseColorAlpha;
    vec4 EmissiveFactor;
    vec4 PbrFactors;
    vec4 AlphaFlags;
    vec4 OpticalFactors;
    vec4 AttenuationColorDistance;
    vec4 TextureFlags;
    vec4 TextureFlags2;

    vec4 BaseAtlasTransform;
    vec4 BaseTextureAddress;
    vec4 BaseTextureTransform;

    vec4 MrAtlasTransform;
    vec4 MrTextureAddress;
    vec4 MrTextureTransform;

    vec4 NormalAtlasTransform;
    vec4 NormalTextureAddress;
    vec4 NormalTextureTransform;

    vec4 EmissiveAtlasTransform;
    vec4 EmissiveTextureAddress;
    vec4 EmissiveTextureTransform;

    vec4 OcclusionAtlasTransform;
    vec4 OcclusionTextureAddress;
    vec4 OcclusionTextureTransform;

    vec4 TransmissionAtlasTransform;
    vec4 TransmissionTextureAddress;
    vec4 TransmissionTextureTransform;
};

layout(location = 0) in vec3 fsWorldPos;
layout(location = 1) in vec3 fsNormal;
layout(location = 2) in vec2 fsUv;
layout(location = 3) flat in int fsMaterialIndex;

layout(location = 0) out vec4 outColor;

layout(std140, set = 0, binding = 0) uniform CameraConstants
{
    vec4 CameraPosition;
    vec4 CameraForward;
    vec4 CameraRight;
    vec4 CameraUp;
    vec4 Projection;
    vec4 Counts;
} Camera;

layout(std430, set = 0, binding = 1) readonly buffer LightBuffer
{
    RasterLight Lights[];
};

layout(std430, set = 0, binding = 2) readonly buffer MaterialBuffer
{
    RasterMaterial Materials[];
};

layout(set = 0, binding = 3) uniform texture2DArray AtlasTexture;
layout(set = 0, binding = 4) uniform sampler AtlasSampler;

float addressTexture(float v, float mode)
{
    if (mode > 0.5 && mode < 1.5)
        return clamp(v, 0.0, 1.0);
    if (mode > 1.5)
    {
        float floorValue = floor(v);
        float local = fract(v);
        return mod(floorValue, 2.0) == 0.0 ? local : 1.0 - local;
    }
    if (v >= 0.0 && v <= 1.0)
        return v;
    return fract(v);
}

vec2 addressUv(vec2 uv, vec4 textureAddress, vec4 textureTransform)
{
    vec2 transformed = uv * textureTransform.zw;
    float rotation = textureAddress.z;
    if (abs(rotation) > 0.000001)
    {
        float c = cos(rotation);
        float s = sin(rotation);
        transformed = vec2(transformed.x * c - transformed.y * s, transformed.x * s + transformed.y * c);
    }
    transformed += textureTransform.xy;
    return vec2(addressTexture(transformed.x, textureAddress.x), addressTexture(transformed.y, textureAddress.y));
}

vec4 sampleAtlasTexture(vec2 uv, vec4 atlasTransform, vec4 textureAddress, vec4 textureTransform)
{
    vec2 addressed = addressUv(uv, textureAddress, textureTransform);
    vec2 atlasUv = atlasTransform.xy + addressed * atlasTransform.zw;
    return texture(sampler2DArray(AtlasTexture, AtlasSampler), vec3(atlasUv, textureAddress.w));
}

float srgbChannelToLinear(float value)
{
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
}

vec3 srgbToLinear(vec3 value)
{
    return vec3(
        srgbChannelToLinear(value.r),
        srgbChannelToLinear(value.g),
        srgbChannelToLinear(value.b));
}

float linearChannelToSrgb(float value)
{
    value = max(value, 0.0);
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
}

vec3 linearToSrgb(vec3 value)
{
    return vec3(
        linearChannelToSrgb(value.r),
        linearChannelToSrgb(value.g),
        linearChannelToSrgb(value.b));
}

vec3 pbrNeutralToneMap(vec3 color)
{
    const float startCompression = 0.76;
    const float desaturation = 0.15;
    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;

    float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression)
        return max(color, vec3(0.0));

    float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / max(peak, 0.000001);
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return max(mix(color, newPeak * vec3(1.0), g), vec3(0.0));
}

float distributionGgx(float nDotH, float alphaRoughness)
{
    float a2 = alphaRoughness * alphaRoughness;
    float f = nDotH * nDotH * (a2 - 1.0) + 1.0;
    return a2 / max(PI * f * f, 0.000001);
}

float visibilitySmithGgxCorrelated(float nDotV, float nDotL, float alphaRoughness)
{
    float a2 = alphaRoughness * alphaRoughness;
    float gv = nDotL * sqrt(max(nDotV * nDotV * (1.0 - a2) + a2, 0.0));
    float gl = nDotV * sqrt(max(nDotL * nDotL * (1.0 - a2) + a2, 0.0));
    return 0.5 / max(gv + gl, 0.000001);
}

vec3 fresnelSchlick(float vDotH, vec3 f0)
{
    float f = pow(1.0 - clamp(vDotH, 0.0, 1.0), 5.0);
    return f0 + (1.0 - f0) * f;
}

mat3 cotangentFrame(vec3 normal, vec3 position, vec2 uv)
{
    vec3 dp1 = dFdx(position);
    vec3 dp2 = dFdy(position);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);
    vec3 dp2Perp = cross(dp2, normal);
    vec3 dp1Perp = cross(normal, dp1);
    vec3 tangent = dp2Perp * duv1.x + dp1Perp * duv2.x;
    vec3 bitangent = dp2Perp * duv1.y + dp1Perp * duv2.y;
    float maxLength = max(dot(tangent, tangent), dot(bitangent, bitangent));
    if (maxLength < 0.00000001)
    {
        vec3 axis = abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
        tangent = normalize(cross(axis, normal));
        bitangent = cross(normal, tangent);
        return mat3(tangent, bitangent, normal);
    }
    float inverseLength = inversesqrt(maxLength);
    return mat3(tangent * inverseLength, bitangent * inverseLength, normal);
}

vec3 studioEnvironment(vec3 direction)
{
    direction = normalize(direction);
    float skyAmount = smoothstep(-0.25, 0.65, direction.y);
    vec3 ground = vec3(0.035, 0.040, 0.050);
    vec3 sky = vec3(0.32, 0.39, 0.49);
    vec3 radiance = mix(ground, sky, skyAmount);

    vec3 keyDirection = normalize(vec3(-0.55, 0.62, -0.56));
    vec3 rimDirection = normalize(vec3(0.78, 0.28, 0.56));
    float key = pow(max(dot(direction, keyDirection), 0.0), 96.0);
    float rim = pow(max(dot(direction, rimDirection), 0.0), 180.0);
    radiance += vec3(5.2, 4.8, 4.2) * key;
    radiance += vec3(2.0, 2.7, 3.8) * rim;
    return radiance;
}

vec3 diffuseEnvironment(vec3 normal)
{
    float skyAmount = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    return mix(vec3(0.055, 0.060, 0.070), vec3(0.42, 0.47, 0.54), skyAmount);
}

void main()
{
    int debugMode = int(Camera.Counts.z + 0.5);
    RasterMaterial material = Materials[fsMaterialIndex];

    bool hasBase = material.TextureFlags.x > 0.5;
    bool hasMr = material.TextureFlags.y > 0.5;
    bool hasNormal = material.TextureFlags.z > 0.5;
    bool hasEmissive = material.TextureFlags.w > 0.5;
    bool hasOcclusion = material.TextureFlags2.x > 0.5;
    bool hasTransmissionTexture = material.TextureFlags2.y > 0.5;

    vec4 baseTexel = hasBase
        ? sampleAtlasTexture(fsUv, material.BaseAtlasTransform, material.BaseTextureAddress, material.BaseTextureTransform)
        : vec4(1.0);
    vec3 baseColor = material.BaseColorAlpha.rgb * (hasBase ? srgbToLinear(baseTexel.rgb) : vec3(1.0));
    float sourceAlpha = material.BaseColorAlpha.a * baseTexel.a;
    float alpha = sourceAlpha;
    float sampledTransmission = 1.0;
    float transmission = clamp(material.AlphaFlags.w, 0.0, 1.0);
    if (hasTransmissionTexture)
    {
        sampledTransmission = sampleAtlasTexture(
            fsUv,
            material.TransmissionAtlasTransform,
            material.TransmissionTextureAddress,
            material.TransmissionTextureTransform).r;
        transmission *= sampledTransmission;
    }

    float ior = clamp(material.OpticalFactors.x, 1.0, 2.333);
    float thickness = max(material.OpticalFactors.y, 0.0);
    float clearcoat = clamp(material.OpticalFactors.z, 0.0, 1.0);
    if (material.TextureFlags2.z > 0.5)
        clearcoat *= sampledTransmission;
    float clearcoatRoughness = clamp(material.OpticalFactors.w, 0.045, 1.0);
    int alphaMode = int(material.AlphaFlags.x + 0.5);
    if (alphaMode == 0)
        alpha = 1.0;
    else if (alphaMode == 1 && alpha < material.AlphaFlags.y)
        discard;

    // The shared rasterizer disables fixed-function culling so double-sided
    // materials remain possible. Respect glTF single-sided semantics here for
    // transparent surfaces; otherwise every back face is alpha blended too and
    // thin stained-glass meshes quickly accumulate toward white.
    bool transparentSurface = alphaMode == 2 || transmission > 0.001;
    if (transparentSurface && material.AlphaFlags.z < 0.5 && !gl_FrontFacing)
        discard;

    vec4 mrTexel = hasMr
        ? sampleAtlasTexture(fsUv, material.MrAtlasTransform, material.MrTextureAddress, material.MrTextureTransform)
        : vec4(1.0);
    float roughness = clamp(material.PbrFactors.y * (hasMr ? mrTexel.g : 1.0), 0.045, 1.0);
    float metallic = clamp(material.PbrFactors.x * (hasMr ? mrTexel.b : 1.0), 0.0, 1.0);

    float occlusion = 1.0;
    if (hasOcclusion)
    {
        float sampledOcclusion = sampleAtlasTexture(
            fsUv, material.OcclusionAtlasTransform, material.OcclusionTextureAddress, material.OcclusionTextureTransform).r;
        occlusion = mix(1.0, sampledOcclusion, clamp(material.PbrFactors.w, 0.0, 1.0));
    }

    vec3 viewDir = normalize(Camera.CameraPosition.xyz - fsWorldPos);
    vec3 normal = normalize(fsNormal);
    if (material.AlphaFlags.z > 0.5)
    {
        if (dot(normal, viewDir) < 0.0)
            normal = -normal;
    }
    else if (dot(normal, viewDir) < 0.0)
    {
        normal = -normal;
    }

    if (hasNormal)
    {
        vec2 normalUv = addressUv(fsUv, material.NormalTextureAddress, material.NormalTextureTransform);
        vec3 tangentNormal = sampleAtlasTexture(
            fsUv, material.NormalAtlasTransform, material.NormalTextureAddress, material.NormalTextureTransform).xyz * 2.0 - 1.0;
        tangentNormal.xy *= material.PbrFactors.z;
        tangentNormal = normalize(tangentNormal);
        normal = normalize(cotangentFrame(normal, fsWorldPos, normalUv) * tangentNormal);
    }

    vec3 emissive = vec3(0.0);
    if (material.EmissiveFactor.a > 0.0)
    {
        vec3 emissionSource = hasEmissive
            ? srgbToLinear(sampleAtlasTexture(
                fsUv, material.EmissiveAtlasTransform, material.EmissiveTextureAddress, material.EmissiveTextureTransform).rgb)
            : baseColor;
        emissive = emissionSource * material.EmissiveFactor.rgb * material.EmissiveFactor.a;
    }

    if (debugMode == 1)
    {
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        outColor = vec4(uv.x, uv.y, 0.0, 1.0);
        return;
    }
    if (debugMode == 2)
    {
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        vec2 atlasUv = material.BaseAtlasTransform.xy + uv * material.BaseAtlasTransform.zw;
        outColor = vec4(fract(atlasUv.x * 8.0), fract(atlasUv.y * 8.0), hasBase ? 1.0 : 0.0, 1.0);
        return;
    }
    if (debugMode == 3)
    {
        outColor = vec4(hasBase ? baseTexel.rgb : linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        return;
    }
    if (debugMode == 4)
    {
        outColor = vec4(linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        return;
    }
    if (debugMode == 5)
    {
        outColor = vec4(normal * 0.5 + 0.5, 1.0);
        return;
    }
    if (debugMode == 6)
    {
        outColor = vec4(vec3(metallic), 1.0);
        return;
    }
    if (debugMode == 7)
    {
        outColor = vec4(vec3(roughness), 1.0);
        return;
    }
    if (debugMode == 8)
    {
        outColor = vec4(vec3(occlusion), 1.0);
        return;
    }
    if (debugMode == 9)
    {
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(emissive)), 1.0);
        return;
    }

    float nDotV = max(dot(normal, viewDir), 0.0001);
    float dielectricF0 = pow((ior - 1.0) / max(ior + 1.0, 0.0001), 2.0);
    vec3 f0 = mix(vec3(dielectricF0), baseColor, metallic);
    vec3 directLighting = vec3(0.0);
    vec3 clearcoatDirect = vec3(0.0);
    int lightCount = min(32, int(Camera.Counts.x + 0.5));
    for (int i = 0; i < lightCount; i++)
    {
        RasterLight light = Lights[i];
        if (light.ConeEnabled.w < 0.5)
            continue;

        float kind = light.PositionKind.w;
        vec3 lightDirection;
        float attenuation = 1.0;
        float cone = 1.0;

        if (kind > 0.5 && kind < 1.5)
        {
            lightDirection = normalize(-light.DirectionRange.xyz);
        }
        else
        {
            vec3 toLight = light.PositionKind.xyz - fsWorldPos;
            float distanceToLight = length(toLight);
            if (distanceToLight < 0.0001)
                continue;

            lightDirection = toLight / distanceToLight;
            float range = light.DirectionRange.w;
            attenuation = 1.0 / (1.0 + 0.11 * distanceToLight * distanceToLight);
            if (range > 0.0001)
            {
                float normalizedDistance = clamp(distanceToLight / range, 0.0, 1.0);
                float edgeFade = 1.0 - normalizedDistance * normalizedDistance;
                attenuation *= edgeFade * edgeFade;
            }

            if (kind >= 1.5)
            {
                vec3 spotForward = normalize(light.DirectionRange.xyz);
                float coneDot = dot(normalize(fsWorldPos - light.PositionKind.xyz), spotForward);
                float inner = light.ConeEnabled.x;
                float outer = light.ConeEnabled.y;
                cone = clamp((coneDot - outer) / max(0.0001, inner - outer), 0.0, 1.0);
                cone *= cone;
            }
        }

        float nDotL = max(dot(normal, lightDirection), 0.0);
        if (nDotL <= 0.0)
            continue;

        vec3 halfVector = normalize(lightDirection + viewDir);
        float nDotH = max(dot(normal, halfVector), 0.0);
        float vDotH = max(dot(viewDir, halfVector), 0.0);
        float alphaRoughness = roughness * roughness;
        float distribution = distributionGgx(nDotH, alphaRoughness);
        float visibility = visibilitySmithGgxCorrelated(nDotV, nDotL, alphaRoughness);
        vec3 fresnel = fresnelSchlick(vDotH, f0);
        vec3 specular = distribution * visibility * fresnel;
        vec3 diffuse = (vec3(1.0) - fresnel) * (1.0 - metallic) * baseColor / PI;
        vec3 radiance = light.ColorIntensity.rgb * light.ColorIntensity.w * attenuation * cone * 0.18;
        directLighting += (diffuse + specular) * radiance * nDotL;

        // KHR_materials_clearcoat adds a dielectric layer. Reuse the already
        // sampled transmission mask when the asset shares that texture, so the
        // StainedGlassLamp path adds ALU only and no extra texture fetch.
        if (clearcoat > 0.001)
        {
            float clearcoatAlpha = clearcoatRoughness * clearcoatRoughness;
            float clearcoatDistribution = distributionGgx(nDotH, clearcoatAlpha);
            float clearcoatVisibility = visibilitySmithGgxCorrelated(nDotV, nDotL, clearcoatAlpha);
            vec3 clearcoatFresnel = fresnelSchlick(vDotH, vec3(0.04));
            clearcoatDirect += clearcoat * clearcoatDistribution * clearcoatVisibility *
                clearcoatFresnel * radiance * nDotL;
        }
    }

    vec3 reflection = reflect(-viewDir, normal);
    vec3 fresnelIbl = f0 + (max(vec3(1.0 - roughness), f0) - f0) * pow(1.0 - nDotV, 5.0);
    vec3 diffuseIbl = diffuseEnvironment(normal) * baseColor * (vec3(1.0) - fresnelIbl) * (1.0 - metallic);
    vec3 sharpSpecularIbl = studioEnvironment(reflection);
    vec3 broadSpecularIbl = diffuseEnvironment(normal);
    vec3 specularIbl = mix(sharpSpecularIbl, broadSpecularIbl, roughness * roughness) * fresnelIbl;
    vec3 indirectLighting = (diffuseIbl + specularIbl) * occlusion * 0.72;

    if (debugMode == 10)
    {
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(directLighting)), 1.0);
        return;
    }
    if (debugMode == 11)
    {
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(indirectLighting)), 1.0);
        return;
    }

    vec3 surfaceLighting = directLighting + indirectLighting;

    // Keep the fast single-pass raster path: transmission changes the shaded
    // light, never surface coverage. This follows glTF's separation of optical
    // transmission from alpha while avoiding a framebuffer resolve, mip chain,
    // additional render pass, or any extra texture sample for StainedGlassLamp.
    if (transmission > 0.001)
    {
        vec3 refractedDirection = refract(-viewDir, normal, 1.0 / ior);
        if (dot(refractedDirection, refractedDirection) < 0.000001)
            refractedDirection = reflection;

        float transmissionBlur = roughness * roughness;
        vec3 transmittedEnvironment = mix(
            studioEnvironment(refractedDirection),
            diffuseEnvironment(-normal) * 1.35,
            transmissionBlur);

        vec3 glassTint = mix(vec3(1.0), max(baseColor, vec3(0.001)), 0.72);
        vec3 volumeAttenuation = vec3(1.0);
        float attenuationDistance = material.AttenuationColorDistance.w;
        if (thickness > 0.0 && attenuationDistance > 0.0)
        {
            float pathLength = thickness / max(abs(dot(normal, viewDir)), 0.15);
            volumeAttenuation = pow(
                max(material.AttenuationColorDistance.rgb, vec3(0.0001)),
                vec3(pathLength / attenuationDistance));
        }

        vec3 transmittedLight = transmittedEnvironment * glassTint * volumeAttenuation;
        vec3 transmissionFresnel = fresnelSchlick(nDotV, vec3(dielectricF0));
        vec3 opticalResult = mix(transmittedLight, surfaceLighting, transmissionFresnel);
        surfaceLighting = mix(surfaceLighting, opticalResult, transmission);
    }

    vec3 linearColor = surfaceLighting + emissive;
    if (clearcoat > 0.001)
    {
        float clearcoatFresnelView = 0.04 + 0.96 * pow(1.0 - nDotV, 5.0);
        vec3 clearcoatIbl = mix(
            sharpSpecularIbl,
            broadSpecularIbl,
            clearcoatRoughness * clearcoatRoughness) * clearcoatFresnelView * clearcoat;
        linearColor *= 1.0 - clearcoat * clearcoatFresnelView;
        linearColor += clearcoatDirect + clearcoatIbl;
    }

    // Alpha remains geometric coverage. OPAQUE and MASK transmission materials
    // therefore stay visibly present instead of becoming ordinary transparency.
    float outputAlpha = alphaMode == 2 ? alpha : 1.0;

    vec3 outputColor = linearToSrgb(pbrNeutralToneMap(linearColor));
    outColor = vec4(outputColor, outputAlpha);
}
";
}
