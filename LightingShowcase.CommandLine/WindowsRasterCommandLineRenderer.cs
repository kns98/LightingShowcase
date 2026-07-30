#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using LightingShowcase.CameraSystem;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

internal static class WindowsRasterCommandLineRenderer
{
    public static string Render(
        RenderBackend backend,
        Scene scene,
        CameraDefinition camera,
        int width,
        int height,
        string outputPath,
        CancellationToken cancellationToken)
    {
        Bitmap bitmap;
        string details;
        switch (backend)
        {
            case RenderBackend.ShadowRasterPreview:
                bitmap = ShadowRasterRenderer.Render(
                    scene, camera.Position, camera.ToBasis(), width, height, cancellationToken, out details);
                break;
            case RenderBackend.VulkanRasterPreview:
                bitmap = VulkanRasterRenderer.Render(
                    scene, camera.Position, camera.ToBasis(), width, height, cancellationToken, out details);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "A raster renderer was required.");
        }

        using (bitmap)
            bitmap.Save(outputPath, ImageFormat.Png);
        return details;
    }

    public static void DisposeSharedResources() => VulkanRasterRenderer.DisposeSharedDevice();
}
#endif
