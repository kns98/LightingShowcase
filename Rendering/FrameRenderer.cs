// -----------------------------------------------------------------------------
// File: Rendering/FrameRenderer.cs
// Purpose: Bitmap renderer.
//
// Converts camera state into pixels by tracing rays, supports preview blocks, full renders, and progressive accumulation.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

/// <summary>Renders ray-traced images into GDI bitmaps for the WinForms preview.</summary>
public sealed class FrameRenderer
{
    private readonly RayTracer tracer;

    /// <summary>Constructs and initializes this component.</summary>
    public FrameRenderer(RayTracer tracer)
    {
        this.tracer = tracer;
    }

    /// <summary>Renders  using the current camera and scene data.</summary>
    public void Render(Bitmap target, Vec3 cameraPosition, CameraBasis basis)
    {
        int width = target.Width, height = target.Height;
        BitmapData data = target.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = data.Stride * height;
            byte[] bytes = new byte[byteCount];

            Parallel.For(0, height, y =>
                RenderRow(bytes, data.Stride, y, width, height, cameraPosition, basis));

            Marshal.Copy(bytes, 0, data.Scan0, byteCount);
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    /// <summary>Renders with canonical camera/settings so preview and final render can share the same inputs.</summary>
    public void Render(Bitmap target, CameraDefinition camera, RenderSettings settings)
    {
        if (camera == null) throw new ArgumentNullException(nameof(camera));
        Render(target, camera.Position, camera.ToBasis(), settings ?? new RenderSettings());
    }

    /// <summary>Renders with explicit render settings/debug mode.</summary>
    public void Render(Bitmap target, Vec3 cameraPosition, CameraBasis basis, RenderSettings settings)
    {
        int width = target.Width, height = target.Height;
        BitmapData data = target.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = data.Stride * height;
            byte[] bytes = new byte[byteCount];

            Parallel.For(0, height, y =>
                RenderRow(bytes, data.Stride, y, width, height, cameraPosition, basis, settings));

            Marshal.Copy(bytes, 0, data.Scan0, byteCount);
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    /// <summary>Draws a coarse preview pass so the UI updates quickly.</summary>
    public void RenderProgressivePass(Bitmap target, Vec3 cameraPosition, CameraBasis basis, int step, CancellationToken cancellationToken)
    {
        RenderProgressivePass(target, cameraPosition, basis, step, new RenderSettings(), cancellationToken);
    }

    /// <summary>Draws a coarse preview pass using explicit render settings.</summary>
    public void RenderProgressivePass(Bitmap target, Vec3 cameraPosition, CameraBasis basis, int step, RenderSettings settings, CancellationToken cancellationToken)
    {
        if (step < 1)
            throw new ArgumentOutOfRangeException(nameof(step), "Progressive render step must be at least 1.");

        int width = target.Width, height = target.Height;
        BitmapData data = target.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = data.Stride * height;
            byte[] bytes = new byte[byteCount];
            Marshal.Copy(data.Scan0, bytes, 0, byteCount);

            int rowCount = (height + step - 1) / step;
            Parallel.For(0, rowCount, (rowBlock, loopState) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    loopState.Stop();
                    return;
                }

                int y = rowBlock * step;
                for (int x = 0; x < width; x += step)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        loopState.Stop();
                        return;
                    }

                    Vec3 direction = RayTracer.RayDirection(x, y, width, height, basis);
                    Vec3 color = GammaCorrect(TraceSample(cameraPosition, direction, settings, x, y, 0));
                    byte blue = (byte)(color.Z * 255.0);
                    byte green = (byte)(color.Y * 255.0);
                    byte red = (byte)(color.X * 255.0);

                    int blockWidth = Math.Min(step, width - x);
                    int blockHeight = Math.Min(step, height - y);
                    FillBlock(bytes, data.Stride, x, y, blockWidth, blockHeight, red, green, blue);
                }
            });

            if (cancellationToken.IsCancellationRequested)
                return;

            Marshal.Copy(bytes, 0, data.Scan0, byteCount);
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    /// <summary>Adds one anti-aliased sample pass to the accumulation buffer.</summary>
    public void RenderAccumulationPass(Bitmap target, Vec3 cameraPosition, CameraBasis basis, Vec3[] accumulation, int sampleIndex, CancellationToken cancellationToken)
    {
        RenderAccumulationPass(target, cameraPosition, basis, accumulation, sampleIndex, new RenderSettings(), cancellationToken);
    }

    /// <summary>Adds one anti-aliased sample pass using explicit render settings.</summary>
    public void RenderAccumulationPass(Bitmap target, Vec3 cameraPosition, CameraBasis basis, Vec3[] accumulation, int sampleIndex, RenderSettings settings, CancellationToken cancellationToken)
    {
        if (sampleIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleIndex), "Sample index must be non-negative.");

        int width = target.Width, height = target.Height;
        if (accumulation.Length != width * height)
            throw new ArgumentException("Accumulation buffer must match the target pixel count.", nameof(accumulation));

        BitmapData data = target.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = data.Stride * height;
            byte[] bytes = new byte[byteCount];
            double sampleWeight = 1.0 / (sampleIndex + 1);

            Parallel.For(0, height, (y, loopState) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    loopState.Stop();
                    return;
                }

                for (int x = 0; x < width; x++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        loopState.Stop();
                        return;
                    }

                    double jitterX = sampleIndex == 0 ? 0.5 : Jitter01(x, y, sampleIndex, 0);
                    double jitterY = sampleIndex == 0 ? 0.5 : Jitter01(x, y, sampleIndex, 1);

                    Vec3 direction = RayTracer.RayDirection(x + jitterX, y + jitterY, width, height, basis);
                    Vec3 sampleColor = TraceSample(cameraPosition, direction, settings, x, y, sampleIndex);

                    int pixelIndex = y * width + x;
                    Vec3 accumulated = accumulation[pixelIndex] + sampleColor;
                    accumulation[pixelIndex] = accumulated;

                    Vec3 averagedColor = accumulated * sampleWeight;
                    Vec3 displayColor = GammaCorrect(averagedColor);
                    WritePixel(bytes, data.Stride, x, y, displayColor);
                }
            });

            if (cancellationToken.IsCancellationRequested)
                return;

            Marshal.Copy(bytes, 0, data.Scan0, byteCount);
        }
        finally
        {
            target.UnlockBits(data);
        }
    }

    /// <summary>Implements the jitter01 operation for this file's subsystem.</summary>
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
            return (hash & 0x00FFFFFFu) / 16777216.0;
        }
    }

    /// <summary>Implements the fill block operation for this file's subsystem.</summary>
    private static void FillBlock(byte[] bytes, int stride, int x, int y, int width, int height, byte red, byte green, byte blue)
    {
        for (int blockY = 0; blockY < height; blockY++)
        {
            int rowIndex = (y + blockY) * stride;
            for (int blockX = 0; blockX < width; blockX++)
            {
                int index = rowIndex + (x + blockX) * 4;
                bytes[index + 0] = blue;
                bytes[index + 1] = green;
                bytes[index + 2] = red;
                bytes[index + 3] = 255;
            }
        }
    }

    /// <summary>Implements the write pixel operation for this file's subsystem.</summary>
    private static void WritePixel(byte[] bytes, int stride, int x, int y, Vec3 color)
    {
        color = SanitizeColor(color);
        int index = y * stride + x * 4;
        bytes[index + 0] = ToByte(color.Z);
        bytes[index + 1] = ToByte(color.Y);
        bytes[index + 2] = ToByte(color.X);
        bytes[index + 3] = 255;
    }

    /// <summary>Converts a linear/gamma-corrected channel to a safe display byte.</summary>
    private static byte ToByte(double value)
    {
        if (!double.IsFinite(value))
            return 0;
        return (byte)System.Math.Clamp((int)System.Math.Round(value * 255.0), 0, 255);
    }

    /// <summary>Renders row using the current camera and scene data.</summary>
    private void RenderRow(byte[] bytes, int stride, int y, int width, int height, Vec3 cameraPosition, CameraBasis basis)
    {
        RenderRow(bytes, stride, y, width, height, cameraPosition, basis, new RenderSettings());
    }

    /// <summary>Renders row with optional debug render mode.</summary>
    private void RenderRow(byte[] bytes, int stride, int y, int width, int height, Vec3 cameraPosition, CameraBasis basis, RenderSettings settings)
    {
        for (int x = 0; x < width; x++)
        {
            Vec3 direction = RayTracer.RayDirection(x, y, width, height, basis);
            Vec3 color = GammaCorrect(TraceSample(cameraPosition, direction, settings, x, y, 0));
            WritePixel(bytes, stride, x, y, color);
        }
    }

    private Vec3 TraceSample(Vec3 cameraPosition, Vec3 direction, RenderSettings settings, int x, int y, int sampleIndex)
    {
        Ray ray = new(cameraPosition, direction);
        Vec3 color = (settings?.PathBounceCount ?? 0) <= 0
            ? tracer.Trace(ray, settings?.Mode ?? RenderMode.Lit)
            : tracer.TracePath(ray, settings.PathBounceCount, x, y, sampleIndex);

        return SanitizeColor(color);
    }

    /// <summary>Implements the gamma correct operation for this file's subsystem.</summary>
    private static Vec3 GammaCorrect(Vec3 c)
    {
        c = SanitizeColor(c);
        return new(
            System.Math.Pow(Clamp(c.X), 1.0 / 2.2),
            System.Math.Pow(Clamp(c.Y), 1.0 / 2.2),
            System.Math.Pow(Clamp(c.Z), 1.0 / 2.2));
    }

    /// <summary>Returns a finite, non-negative color. This prevents NaN/Infinity from poisoning progressive accumulation.</summary>
    private static Vec3 SanitizeColor(Vec3 c) => new(
        SanitizeChannel(c.X),
        SanitizeChannel(c.Y),
        SanitizeChannel(c.Z));

    private static double SanitizeChannel(double value)
    {
        if (!double.IsFinite(value) || value < 0.0)
            return 0.0;
        return System.Math.Min(value, 64.0);
    }

    /// <summary>Implements the clamp operation for this file's subsystem.</summary>
    private static double Clamp(double value)
    {
        if (!double.IsFinite(value))
            return 0.0;
        return System.Math.Min(1.0, System.Math.Max(0.0, value));
    }
}
