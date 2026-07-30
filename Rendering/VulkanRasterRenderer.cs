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
                _ => 0
            };
        }
    }

    private static readonly object DeviceSync = new();
    private static readonly object RenderSync = new();
    private static GraphicsDevice? sharedGraphicsDevice;
    private static SharedRasterResources? sharedRasterResources;
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RasterVertex
    {
        public readonly Vector3 Position;
        public readonly Vector3 Normal;
        public readonly Vector4 ColorAlpha;
        public readonly Vector4 UvTexture;
        public readonly Vector4 AtlasTransform;
        public readonly Vector4 TextureAddress;
        public readonly Vector4 TextureTransform;
        public readonly Vector4 Emission;

        public RasterVertex(Vec3 position, Vec3 normal, Vec2 uv, Material material, RasterTexturePlacement texturePlacement)
        {
            Position = ToVector3(position);
            Normal = ToVector3(normal.Normalize());
            ColorAlpha = texturePlacement.HasTexture
                ? MaterialColorAlpha(material)
                : BakedMaterialColorAlpha(material, uv);
            UvTexture = new Vector4(
                (float)uv.U,
                (float)uv.V,
                0.0f,
                texturePlacement.HasTexture ? 1.0f : 0.0f);
            AtlasTransform = texturePlacement.AtlasTransform;
            TextureAddress = texturePlacement.TextureAddress;
            TextureTransform = texturePlacement.TextureTransform;
            Emission = texturePlacement.HasTexture
                ? MaterialEmission(material)
                : BakedMaterialEmission(material, uv);
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

        public RasterTexturePlacement(float offsetX, float offsetY, float scaleX, float scaleY, TextureMap texture)
        {
            AtlasTransform = new Vector4(offsetX, offsetY, scaleX, scaleY);
            TextureAddress = new Vector4(AddressModeCode(texture.WrapU), AddressModeCode(texture.WrapV), (float)texture.Rotation, 0.0f);
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
        public readonly Vector4 ColorAlpha;
        public readonly Vector4 Emission;

        public RasterMaterial(Triangle triangle)
        {
            Material material = triangle.Material;
            ColorAlpha = MaterialColorAlpha(material);
            Emission = MaterialEmission(material);
        }
    }

    private sealed class TextureBuildResult
    {
        public required IReadOnlyDictionary<TextureMap, RasterTexturePlacement> TexturePlacements { get; init; }
        public required uint[] Pixels { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int TextureCount { get; init; }
    }

    private sealed class RasterGeometryBuildResult
    {
        public required RasterVertex[] OpaqueVertices { get; init; }
        public required RasterVertex[] TransparentVertices { get; init; }
        public required RasterMaterial[] Materials { get; init; }
        public int NearClippedTriangles { get; init; }
        public int TotalVertices => OpaqueVertices.Length + TransparentVertices.Length;
        public int TotalTriangles => TotalVertices / 3;
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

                if (device != null)
                {
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice: WaitForIdle"); device.WaitForIdle(); } catch { }
                }

                try { resources?.Dispose(); } catch { }
                if (device != null)
                {
                    try { Stage("Dispose shared Vulkan raster GraphicsDevice"); device.Dispose(); } catch { }
                }
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

        ResetStageLog(width, height);
        Stopwatch stopwatch = Stopwatch.StartNew();
        Stage("Vulkan raster render entered");

        ThrowIfCancellationRequested(cancellationToken, "build texture atlas");
        TextureBuildResult textures = BuildTextureAtlas(scene);
        Stage($"Texture atlas built: textures={textures.TextureCount}, size={textures.Width}x{textures.Height}, pixels={textures.Pixels.Length}");

        bool useGpuTextureSampling = UseGpuTextureSampling;
        int debugMode = RasterDebugMode;
        Stage($"Vulkan raster texture mode: {(useGpuTextureSampling ? "gpu texture atlas sampling" : "CPU-baked per-vertex texture colors")}, debug={DebugModeName(debugMode)}");

        ThrowIfCancellationRequested(cancellationToken, "build vertex buffers");
        RasterGeometryBuildResult geometry = BuildGeometryBuffers(scene, textures.TexturePlacements, cameraPosition, basis, useGpuTextureSampling);
        RasterLight[] lights = BuildLightBuffer(scene);
        Stage($"Packed scene: opaqueVertices={geometry.OpaqueVertices.Length}, transparentVertices={geometry.TransparentVertices.Length}, triangles={geometry.TotalTriangles}, materials={geometry.Materials.Length}, nearClipped={geometry.NearClippedTriangles}, lights={lights.Length}");

        ThrowIfCancellationRequested(cancellationToken, "create Vulkan device");
        GraphicsDevice gd = GetOrCreateSharedDevice();
        ResourceFactory factory = gd.ResourceFactory;

        int vertexStride = Marshal.SizeOf<RasterVertex>();
        int lightStride = Marshal.SizeOf<RasterLight>();
        int materialStride = Marshal.SizeOf<RasterMaterial>();
        uint opaqueVertexBytes = checked((uint)Math.Max(vertexStride, geometry.OpaqueVertices.Length * vertexStride));
        uint transparentVertexBytes = checked((uint)Math.Max(vertexStride, geometry.TransparentVertices.Length * vertexStride));
        uint lightBytes = checked((uint)Math.Max(lightStride, lights.Length * lightStride));
        uint materialBytes = checked((uint)Math.Max(materialStride, geometry.Materials.Length * materialStride));
        uint cameraBytes = checked((uint)Marshal.SizeOf<RasterCameraConstants>());

        using DeviceBuffer opaqueVertexBuffer = factory.CreateBuffer(new BufferDescription(opaqueVertexBytes, BufferUsage.VertexBuffer));
        using DeviceBuffer transparentVertexBuffer = factory.CreateBuffer(new BufferDescription(transparentVertexBytes, BufferUsage.VertexBuffer));
        using DeviceBuffer cameraBuffer = factory.CreateBuffer(new BufferDescription(cameraBytes, BufferUsage.UniformBuffer));
        using DeviceBuffer lightBuffer = factory.CreateBuffer(new BufferDescription(lightBytes, BufferUsage.StructuredBufferReadOnly, structureByteStride: (uint)lightStride));
        using DeviceBuffer materialBuffer = factory.CreateBuffer(new BufferDescription(materialBytes, BufferUsage.StructuredBufferReadOnly, structureByteStride: (uint)materialStride));
        using Texture atlasTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)textures.Width,
            (uint)textures.Height,
            mipLevels: 1,
            arrayLayers: 1,
            format: Veldrid.PixelFormat.R8_G8_B8_A8_UNorm,
            usage: TextureUsage.Sampled));
        using Sampler atlasSampler = factory.CreateSampler(SamplerDescription.Linear);

        if (geometry.OpaqueVertices.Length > 0)
            gd.UpdateBuffer(opaqueVertexBuffer, 0, geometry.OpaqueVertices);
        if (geometry.TransparentVertices.Length > 0)
            gd.UpdateBuffer(transparentVertexBuffer, 0, geometry.TransparentVertices);

        double cameraFar = ComputeCameraFarPlane(scene, cameraPosition, basis);
        gd.UpdateBuffer(cameraBuffer, 0, new RasterCameraConstants(cameraPosition, basis, width, height, lights.Length, textures.TextureCount, debugMode, geometry.Materials.Length, cameraFar));
        gd.UpdateBuffer(lightBuffer, 0, lights.Length == 0 ? new[] { default(RasterLight) } : lights);
        gd.UpdateBuffer(materialBuffer, 0, geometry.Materials.Length == 0 ? new[] { default(RasterMaterial) } : geometry.Materials);
        gd.UpdateTexture(atlasTexture, textures.Pixels, 0, 0, 0, (uint)textures.Width, (uint)textures.Height, 1, 0, 0);

        using Texture colorTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            mipLevels: 1,
            arrayLayers: 1,
            format: Veldrid.PixelFormat.R8_G8_B8_A8_UNorm,
            usage: TextureUsage.RenderTarget));

        using Texture depthTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            mipLevels: 1,
            arrayLayers: 1,
            format: Veldrid.PixelFormat.D32_Float_S8_UInt,
            usage: TextureUsage.DepthStencil));

        using Texture stagingTexture = factory.CreateTexture(TextureDescription.Texture2D(
            (uint)width,
            (uint)height,
            mipLevels: 1,
            arrayLayers: 1,
            format: Veldrid.PixelFormat.R8_G8_B8_A8_UNorm,
            usage: TextureUsage.Staging));

        using Framebuffer framebuffer = factory.CreateFramebuffer(new FramebufferDescription(depthTexture, colorTexture));
        SharedRasterResources resources = GetOrCreateSharedRasterResources(gd, framebuffer.OutputDescription);

        using ResourceSet resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
            resources.Layout,
            cameraBuffer,
            lightBuffer,
            materialBuffer,
            atlasTexture,
            atlasSampler));

        ThrowIfCancellationRequested(cancellationToken, "record Vulkan raster command list");
        using CommandList commandList = factory.CreateCommandList();
        commandList.Begin();
        commandList.SetFramebuffer(framebuffer);
        commandList.ClearColorTarget(0, new RgbaFloat(0.010f, 0.012f, 0.016f, 1.0f));
        commandList.ClearDepthStencil(1.0f);

        // Veldrid requires a graphics pipeline to be active before binding a
        // graphics resource set, because the resource set is validated against
        // the pipeline's resource layouts. Bind the shared resource set after
        // each pipeline switch.
        if (geometry.OpaqueVertices.Length > 0)
        {
            commandList.SetPipeline(resources.OpaquePipeline);
            commandList.SetGraphicsResourceSet(0, resourceSet);
            commandList.SetVertexBuffer(0, opaqueVertexBuffer);
            commandList.Draw((uint)geometry.OpaqueVertices.Length);
        }
        if (geometry.TransparentVertices.Length > 0)
        {
            commandList.SetPipeline(resources.TransparentPipeline);
            commandList.SetGraphicsResourceSet(0, resourceSet);
            commandList.SetVertexBuffer(0, transparentVertexBuffer);
            commandList.Draw((uint)geometry.TransparentVertices.Length);
        }
        commandList.CopyTexture(
            colorTexture,
            0, 0, 0,
            0, 0,
            stagingTexture,
            0, 0, 0,
            0, 0,
            (uint)width,
            (uint)height,
            1,
            1);
        commandList.End();

        ThrowIfCancellationRequested(cancellationToken, "submit Vulkan raster command list");
        gd.SubmitCommands(commandList);
        gd.WaitForIdle();

        ThrowIfCancellationRequested(cancellationToken, "read back Vulkan raster texture");
        RenderImage image = ReadBackImage(gd, stagingTexture, width, height);

        stopwatch.Stop();
        details = $"VULKAN RASTER PREVIEW OK - {width}x{height}, triangles={geometry.TotalTriangles}, opaque={geometry.OpaqueVertices.Length / 3}, transparent={geometry.TransparentVertices.Length / 3}, nearClipped={geometry.NearClippedTriangles}, lights={lights.Length}, textures={textures.TextureCount}, texMode={(useGpuTextureSampling ? "gpu-atlas" : "baked-vertex")}, debug={DebugModeName(debugMode)}, materials={geometry.Materials.Length}, far={cameraFar:0.###}, cpuZParity=true, materialBuffer={useGpuTextureSampling}, frame={stopwatch.ElapsedMilliseconds}ms";
        Stage("Vulkan raster render completed successfully");
        return image;
    }

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
                        new VertexElementDescription("ColorAlpha", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
                        new VertexElementDescription("UvTexture", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
                        new VertexElementDescription("AtlasTransform", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
                        new VertexElementDescription("TextureAddress", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
                        new VertexElementDescription("TextureTransform", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
                        new VertexElementDescription("Emission", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

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

    private static RasterGeometryBuildResult BuildGeometryBuffers(
        Scene scene,
        IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements,
        Vec3 cameraPosition,
        CameraBasis basis,
        bool useGpuTextureSampling)
    {
        List<RasterVertex> opaqueVertices = new(scene.Triangles.Count * 3);
        List<RasterMaterial> materials = new(scene.Triangles.Count);
        int nearClippedTriangles = 0;

        foreach (Triangle tri in scene.Triangles)
        {
            // Match ShadowRasterRenderer: reject whole triangles that fail the
            // CPU camera projection rule, then let a normal z-buffer resolve
            // visibility. Do not split transmissive/glass materials into a
            // blended pass here; the CPU raster preview treats non-discarded
            // material pixels as z-buffered surfaces, which gives much more
            // reliable editor occlusion.
            if (!IsRenderableTriangle(tri, cameraPosition, basis))
            {
                nearClippedTriangles++;
                continue;
            }

            AddTriangleVertices(opaqueVertices, tri, texturePlacements, useGpuTextureSampling);
            if (useGpuTextureSampling)
                materials.Add(new RasterMaterial(tri));
        }

        return new RasterGeometryBuildResult
        {
            OpaqueVertices = opaqueVertices.ToArray(),
            TransparentVertices = Array.Empty<RasterVertex>(),
            Materials = materials.ToArray(),
            NearClippedTriangles = nearClippedTriangles
        };
    }

    private static void AddTriangleVertices(List<RasterVertex> vertices, Triangle tri, IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements, bool useGpuTextureSampling)
    {
        RasterTexturePlacement texturePlacement = useGpuTextureSampling ? TexturePlacement(texturePlacements, tri.Material.Texture) : RasterTexturePlacement.None;
        Vec3 normal = tri.Normal.Normalize();
        vertices.Add(new RasterVertex(tri.A, normal, tri.UvA, tri.Material, texturePlacement));
        vertices.Add(new RasterVertex(tri.B, normal, tri.UvB, tri.Material, texturePlacement));
        vertices.Add(new RasterVertex(tri.C, normal, tri.UvC, tri.Material, texturePlacement));
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
        return material.AlphaBlend ||
               material.Alpha < 0.999 ||
               material.Transmission > 0.001 ||
               EffectiveMaterialAlpha(material) < 0.999;
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

    private static TextureBuildResult BuildTextureAtlas(Scene scene)
    {
        List<TextureMap> textures = new();
        HashSet<TextureMap> seen = new();

        foreach (Triangle triangle in scene.Triangles)
        {
            TextureMap? texture = triangle.Material.Texture;
            if (texture != null && seen.Add(texture))
                textures.Add(texture);
        }

        if (textures.Count == 0)
        {
            return new TextureBuildResult
            {
                TexturePlacements = new Dictionary<TextureMap, RasterTexturePlacement>(),
                Pixels = new[] { 0xffffffffu },
                Width = 1,
                Height = 1,
                TextureCount = 0
            };
        }

        const int padding = 1;
        const int targetRowWidth = 4096;
        int atlasWidth = Math.Max(targetRowWidth, textures.Max(t => t.Width + padding * 2));
        List<(TextureMap Texture, int X, int Y)> placements = new();
        int cursorX = 0;
        int cursorY = 0;
        int rowHeight = 0;

        foreach (TextureMap texture in textures.OrderByDescending(t => t.Height).ThenByDescending(t => t.Width))
        {
            int entryWidth = texture.Width + padding * 2;
            int entryHeight = texture.Height + padding * 2;
            if (cursorX > 0 && cursorX + entryWidth > atlasWidth)
            {
                cursorY += rowHeight;
                cursorX = 0;
                rowHeight = 0;
            }

            placements.Add((texture, cursorX + padding, cursorY + padding));
            cursorX += entryWidth;
            rowHeight = Math.Max(rowHeight, entryHeight);
        }

        int atlasHeight = Math.Max(1, cursorY + rowHeight);
        uint[] atlasPixels = Enumerable.Repeat(0xffffffffu, checked(atlasWidth * atlasHeight)).ToArray();
        Dictionary<TextureMap, RasterTexturePlacement> texturePlacements = new();

        foreach ((TextureMap texture, int x, int y) in placements)
        {
            uint[] source = texture.CopyPackedRgba32Pixels();
            CopyTextureIntoAtlas(source, texture.Width, texture.Height, atlasPixels, atlasWidth, atlasHeight, x, y);
            float offsetX = (x + 0.5f) / atlasWidth;
            float offsetY = (y + 0.5f) / atlasHeight;
            float scaleX = texture.Width <= 1 ? 0.0f : (texture.Width - 1.0f) / atlasWidth;
            float scaleY = texture.Height <= 1 ? 0.0f : (texture.Height - 1.0f) / atlasHeight;
            texturePlacements[texture] = new RasterTexturePlacement(offsetX, offsetY, scaleX, scaleY, texture);
        }

        return new TextureBuildResult
        {
            TexturePlacements = texturePlacements,
            Pixels = atlasPixels,
            Width = atlasWidth,
            Height = atlasHeight,
            TextureCount = texturePlacements.Count
        };
    }

    private static void CopyTextureIntoAtlas(uint[] source, int sourceWidth, int sourceHeight, uint[] atlas, int atlasWidth, int atlasHeight, int destX, int destY)
    {
        if (source.Length != sourceWidth * sourceHeight)
            return;

        for (int y = 0; y < sourceHeight; y++)
        {
            int atlasRow = (destY + y) * atlasWidth;
            int sourceRow = y * sourceWidth;
            Array.Copy(source, sourceRow, atlas, atlasRow + destX, sourceWidth);
        }

        for (int x = 0; x < sourceWidth; x++)
        {
            atlas[Math.Clamp(destY - 1, 0, atlasHeight - 1) * atlasWidth + destX + x] = source[x];
            atlas[Math.Clamp(destY + sourceHeight, 0, atlasHeight - 1) * atlasWidth + destX + x] = source[(sourceHeight - 1) * sourceWidth + x];
        }

        for (int y = -1; y <= sourceHeight; y++)
        {
            int sourceY = Math.Clamp(y, 0, sourceHeight - 1);
            int atlasY = Math.Clamp(destY + y, 0, atlasHeight - 1);
            atlas[atlasY * atlasWidth + Math.Clamp(destX - 1, 0, atlasWidth - 1)] = source[sourceY * sourceWidth];
            atlas[atlasY * atlasWidth + Math.Clamp(destX + sourceWidth, 0, atlasWidth - 1)] = source[sourceY * sourceWidth + sourceWidth - 1];
        }
    }

    private static RasterTexturePlacement TexturePlacement(IReadOnlyDictionary<TextureMap, RasterTexturePlacement> texturePlacements, TextureMap? texture)
    {
        if (texture == null)
            return RasterTexturePlacement.None;
        return texturePlacements.TryGetValue(texture, out RasterTexturePlacement placement) ? placement : RasterTexturePlacement.None;
    }

    private static RenderImage ReadBackImage(GraphicsDevice gd, Texture stagingTexture, int width, int height)
    {
        uint[] pixels = new uint[checked(width * height)];
        byte[] sourceRow = new byte[checked(width * 4)];
        MappedResource mapped = gd.Map(stagingTexture, MapMode.Read);
        try
        {
            for (int y = 0; y < height; y++)
            {
                IntPtr source = IntPtr.Add(mapped.Data, checked((int)(y * mapped.RowPitch)));
                Marshal.Copy(source, sourceRow, 0, sourceRow.Length);
                int destination = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = x * 4;
                    byte r = sourceRow[i + 0];
                    byte g = sourceRow[i + 1];
                    byte b = sourceRow[i + 2];
                    pixels[destination + x] = (uint)r | ((uint)g << 8) | ((uint)b << 16) | 0xff000000u;
                }
            }
        }
        finally
        {
            gd.Unmap(stagingTexture);
        }
        return new RenderImage(width, height, pixels);
    }

    private static void ResetStageLog(int width, int height)
    {
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
        _ => "off"
    };

    private static Vector3 ToVector3(Vec3 value) => new((float)value.X, (float)value.Y, (float)value.Z);
    private static Vector4 ToVector4(Vec3 value, float w) => new((float)value.X, (float)value.Y, (float)value.Z, w);

    private const string VertexShaderSource = @"
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec4 ColorAlpha;
layout(location = 3) in vec4 UvTexture;
layout(location = 4) in vec4 AtlasTransform;
layout(location = 5) in vec4 TextureAddress;
layout(location = 6) in vec4 TextureTransform;
layout(location = 7) in vec4 Emission;

layout(location = 0) out vec3 fsWorldPos;
layout(location = 1) out vec3 fsNormal;
layout(location = 2) out vec4 fsColorAlpha;
layout(location = 3) out vec4 fsUvTexture;
layout(location = 4) out vec4 fsAtlasTransform;
layout(location = 5) out vec4 fsTextureAddress;
layout(location = 6) out vec4 fsTextureTransform;
layout(location = 7) out vec4 fsEmission;

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

    // Hardware perspective projection. Keep w proportional to camera-space
    // depth so Vulkan performs correct clipping, depth interpolation, and
    // perspective-correct interpolation of normals/UVs/colors.
    gl_Position = vec4(
        -vx / (aspect * tanHalfFov),
         vy / tanHalfFov,
         ndcDepth * z,
         z);

    // Vulkan's framebuffer clip-space Y is inverted relative to the CPU raster
    // screen convention. Flip here instead of pre-dividing by z or relying on
    // a backend viewport workaround.
    gl_Position.y = -gl_Position.y;

    fsWorldPos = Position;
    fsNormal = Normal;
    fsColorAlpha = ColorAlpha;
    fsUvTexture = UvTexture;
    fsAtlasTransform = AtlasTransform;
    fsTextureAddress = TextureAddress;
    fsTextureTransform = TextureTransform;
    fsEmission = Emission;
}
";

    private const string FragmentShaderSource = @"
#version 450

struct RasterLight
{
    vec4 PositionKind;
    vec4 DirectionRange;
    vec4 ColorIntensity;
    vec4 ConeEnabled;
};

struct RasterMaterial
{
    vec4 ColorAlpha;
    vec4 Emission;
};

layout(location = 0) in vec3 fsWorldPos;
layout(location = 1) in vec3 fsNormal;
layout(location = 2) in vec4 fsColorAlpha;
layout(location = 3) in vec4 fsUvTexture;
layout(location = 4) in vec4 fsAtlasTransform;
layout(location = 5) in vec4 fsTextureAddress;
layout(location = 6) in vec4 fsTextureTransform;
layout(location = 7) in vec4 fsEmission;

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

layout(set = 0, binding = 3) uniform texture2D AtlasTexture;
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

vec2 addressUv(vec2 uv, vec4 textureAddress)
{
    vec2 transformed = uv * fsTextureTransform.zw;
    float rotation = textureAddress.z;
    if (abs(rotation) > 0.000001)
    {
        float c = cos(rotation);
        float s = sin(rotation);
        transformed = vec2(transformed.x * c - transformed.y * s, transformed.x * s + transformed.y * c);
    }
    transformed += fsTextureTransform.xy;
    return vec2(addressTexture(transformed.x, textureAddress.x), addressTexture(transformed.y, textureAddress.y));
}

vec4 sampleAtlasTexture(vec2 uv, vec4 atlasTransform, vec4 textureAddress)
{
    vec2 addressed = addressUv(uv, textureAddress);
    float u = addressed.x;
    float v = addressed.y;
    vec2 atlasUv = atlasTransform.xy + vec2(u, v) * atlasTransform.zw;
    return texture(sampler2D(AtlasTexture, AtlasSampler), atlasUv);
}

void main()
{
    vec4 materialColorAlpha = fsColorAlpha;
    vec4 materialEmission = fsEmission;
    int materialCount = max(0, int(Camera.Counts.w + 0.5));
    int debugMode = int(Camera.Counts.z + 0.5);
    int primitiveId = gl_PrimitiveID;
    if (primitiveId >= 0 && primitiveId < materialCount)
    {
        materialColorAlpha = Materials[primitiveId].ColorAlpha;
        materialEmission = Materials[primitiveId].Emission;
    }

    vec4 texel = fsUvTexture.w > 0.5 ? sampleAtlasTexture(fsUvTexture.xy, fsAtlasTransform, fsTextureAddress) : vec4(1.0);
    if (debugMode == 1)
    {
        vec2 uv = addressUv(fsUvTexture.xy, fsTextureAddress);
        outColor = vec4(uv.x, uv.y, 0.0, 1.0);
        return;
    }
    if (debugMode == 2)
    {
        vec2 uv = addressUv(fsUvTexture.xy, fsTextureAddress);
        vec2 atlasUv = fsAtlasTransform.xy + uv * fsAtlasTransform.zw;
        outColor = vec4(fract(atlasUv.x * 8.0), fract(atlasUv.y * 8.0), fsUvTexture.w > 0.5 ? 1.0 : 0.0, 1.0);
        return;
    }
    if (debugMode == 3)
    {
        outColor = fsUvTexture.w > 0.5 ? vec4(texel.rgb, 1.0) : vec4(materialColorAlpha.rgb, 1.0);
        return;
    }
    if (debugMode == 4)
    {
        outColor = vec4(materialColorAlpha.rgb, 1.0);
        return;
    }

    vec3 baseColor = materialColorAlpha.rgb * texel.rgb;
    float alpha = materialColorAlpha.a * texel.a;
    if (alpha < 0.04)
        discard;

    vec3 normal = normalize(fsNormal);
    vec3 viewDir = normalize(Camera.CameraPosition.xyz - fsWorldPos);
    // Culling is disabled for editor preview, so do not trust winding-dependent
    // gl_FrontFacing for material lighting. Match the CPU preview's view-facing
    // normal rule: flip only when the geometric normal faces away from camera.
    if (dot(normal, viewDir) < 0.0)
        normal = -normal;

    vec3 linear = baseColor * 0.110;
    int lightCount = min(32, int(Camera.Counts.x + 0.5));
    for (int i = 0; i < lightCount; i++)
    {
        RasterLight light = Lights[i];
        if (light.ConeEnabled.w < 0.5)
            continue;

        float kind = light.PositionKind.w;
        vec3 L;
        float attenuation = 1.0;
        float cone = 1.0;

        if (kind > 0.5 && kind < 1.5)
        {
            L = normalize(-light.DirectionRange.xyz);
        }
        else
        {
            vec3 toLight = light.PositionKind.xyz - fsWorldPos;
            float distanceToLight = length(toLight);
            if (distanceToLight < 0.0001)
                continue;

            L = toLight / distanceToLight;
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

        float ndotl = max(dot(normal, L), 0.0);
        if (ndotl <= 0.0)
            continue;

        float strength = light.ColorIntensity.w * attenuation * cone * 0.18;
        linear += baseColor * light.ColorIntensity.rgb * (ndotl * strength);

        // Small CPU-raster-style Blinn highlight so spheres and glossy surfaces
        // read closer to the software preview without adding a full material pass.
        vec3 H = normalize(L + viewDir);
        float spec = pow(max(dot(normal, H), 0.0), 16.0) * ndotl * strength * 0.04;
        linear += light.ColorIntensity.rgb * spec;
    }

    // Match Material.SampleEmission() for the common glTF case used by this
    // preview: without a separate emissive texture, the already sampled base
    // color is the emission source. Using emissionColor alone turns textured
    // emissive surfaces into solid white.
    vec3 emission = baseColor * materialEmission.rgb * materialEmission.a;
    linear += emission;
    vec3 srgb = pow(clamp(linear, 0.0, 1.0), vec3(1.0 / 2.2));
    outColor = vec4(srgb, 1.0);
}
";
}
