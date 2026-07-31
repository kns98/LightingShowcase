using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Preview;

internal static class CpuPreviewRenderer
{
    public static RenderImage Render(
        Scene scene,
        CameraDefinition camera,
        int width,
        int height,
        int samples,
        int bounces,
        double exposure,
        CancellationToken cancellationToken,
        out string details)
    {
        RayTracer tracer = new(scene, new LightingState());
        CameraBasis basis = camera.ToBasis();
        uint[] pixels = new uint[checked(width * height)];
        samples = Math.Clamp(samples, 1, 64);
        double sampleWeight = 1.0 / samples;
        ParallelOptions options = new() { CancellationToken = cancellationToken };

        Parallel.For(0, height, options, y =>
        {
            for (int x = 0; x < width; x++)
            {
                Vec3 accumulated = Vec3.Zero;
                for (int sampleIndex = 0; sampleIndex < samples; sampleIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double jitterX = samples == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 0);
                    double jitterY = samples == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 1);
                    Vec3 direction = RayTracer.RayDirection(
                        x + jitterX,
                        y + jitterY,
                        width,
                        height,
                        basis,
                        camera.FieldOfViewDegrees);
                    Ray ray = new(camera.Position, direction);
                    accumulated += bounces <= 0
                        ? tracer.Trace(ray, RenderMode.Lit)
                        : tracer.TracePath(ray, bounces, x, y, sampleIndex);
                }

                pixels[y * width + x] = PackDisplayColor(accumulated * sampleWeight * exposure);
            }
        });

        details = $"CPU preview - {width}x{height}, samples={samples}, bounces={bounces}";
        return new RenderImage(width, height, pixels);
    }

    private static uint PackDisplayColor(Vec3 linear)
    {
        Vec3 display = RayTracer.ToDisplayColor(linear);
        byte red = ToDisplayByte(display.X);
        byte green = ToDisplayByte(display.Y);
        byte blue = ToDisplayByte(display.Z);
        return red | ((uint)green << 8) | ((uint)blue << 16) | 0xff000000u;
    }

    private static byte ToDisplayByte(double value)
    {
        if (!double.IsFinite(value))
            return 0;
        return (byte)Math.Clamp((int)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0), 0, 255);
    }

    private static double Jitter01(int x, int y, int sampleIndex, int axis)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)x) * 16777619u;
            hash = (hash ^ (uint)y) * 16777619u;
            hash = (hash ^ (uint)sampleIndex) * 16777619u;
            hash = (hash ^ (uint)(axis * 374761393)) * 16777619u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return (hash & 0x00ffffffu) / 16777216.0;
        }
    }
}
