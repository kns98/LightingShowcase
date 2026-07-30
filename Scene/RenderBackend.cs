// -----------------------------------------------------------------------------
// File: Scene/RenderBackend.cs
// Purpose: Selects which render backend should be used by the preview renderer.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Render backend preference selected from the Render pane.</summary>
public enum RenderBackend
{
    /// <summary>Uses the CPU ray/path tracing renderer for still/final renders.</summary>
    Cpu = 0,

    /// <summary>Uses the Vulkan compute ray/path tracing renderer for still/final renders.</summary>
    VulkanGpu = 1,

    /// <summary>Uses the Vulkan compute renderer in small diagnostic batches.</summary>
    VulkanDiagnostic = 2,

    /// <summary>
    /// Uses Lighting Showcase's own software raster preview pipeline: camera
    /// projection, triangle rasterization, z-buffering, direct lighting, and
    /// shadow-map-style shadows. This is the fast AMD-style preview backend.
    /// </summary>
    ShadowRasterPreview = 3,

    /// <summary>
    /// Uses Vulkan's graphics pipeline for hardware triangle rasterization into
    /// an off-screen image, then copies the result back into the WinForms view.
    /// This is separate from the Vulkan compute path tracer.
    /// </summary>
    VulkanRasterPreview = 4
}
