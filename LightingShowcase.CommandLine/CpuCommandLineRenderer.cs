using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

/// <summary>Cross-platform CPU ray/path tracer used by the command-line application.</summary>
internal static class CpuCommandLineRenderer
{
    public static RenderImage Render(
        Scene scene,
        CameraDefinition camera,
        RenderRequest request,
        CancellationToken cancellationToken,
        out string details)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        if (request == null) throw new ArgumentNullException(nameof(request));

        RayTracer tracer = new(scene, new LightingState());
        CameraBasis basis = camera.ToBasis();
        uint[] pixels = new uint[checked(request.Width * request.Height)];
        int sampleCount = request.Samples;
        double sampleWeight = 1.0 / sampleCount;
        ParallelOptions options = new() { CancellationToken = cancellationToken };

        Parallel.For(0, request.Height, options, y =>
        {
            for (int x = 0; x < request.Width; x++)
            {
                Vec3 accumulated = Vec3.Zero;
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double jitterX = sampleCount == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 0);
                    double jitterY = sampleCount == 1 ? 0.5 : Jitter01(x, y, sampleIndex, 1);
                    Vec3 direction = RayTracer.RayDirection(
                        x + jitterX,
                        y + jitterY,
                        request.Width,
                        request.Height,
                        basis,
                        request.FieldOfViewDegrees);
                    Ray ray = new(camera.Position, direction);
                    Vec3 sample = request.Bounces <= 0
                        ? tracer.Trace(ray, RenderMode.Lit)
                        : tracer.TracePath(ray, request.Bounces, x, y, sampleIndex);
                    accumulated += sample;
                }

                Vec3 linear = accumulated * sampleWeight * request.Exposure;
                pixels[y * request.Width + x] = PackDisplayColor(linear);
            }
        });

        details = $"CPU ray/path tracer OK - {request.Width}x{request.Height}, samples={request.Samples}, bounces={request.Bounces}";
        return new RenderImage(request.Width, request.Height, pixels);
    }

    private static uint PackDisplayColor(Vec3 linear)
    {
        byte red = ToDisplayByte(linear.X);
        byte green = ToDisplayByte(linear.Y);
        byte blue = ToDisplayByte(linear.Z);
        return red | ((uint)green << 8) | ((uint)blue << 16) | 0xff000000u;
    }

    private static byte ToDisplayByte(double value)
    {
        if (!double.IsFinite(value) || value <= 0.0) return 0;
        double mapped = value / (1.0 + value);
        double gamma = Math.Pow(Math.Clamp(mapped, 0.0, 1.0), 1.0 / 2.2);
        return (byte)Math.Clamp((int)Math.Round(gamma * 255.0), 0, 255);
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
