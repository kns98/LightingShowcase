using System.Drawing;
using System.Drawing.Imaging;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Windows-only System.Drawing adapters used by the desktop editor.</summary>
public static class TextureMapWindowsExtensions
{
    public static Bitmap CreateBitmap(this TextureMap texture)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));

        Bitmap bitmap = new(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
        Rectangle rect = new(0, 0, texture.Width, texture.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int byteCount = Math.Abs(stride) * texture.Height;
            byte[] bytes = new byte[byteCount];
            uint[] pixels = texture.CopyPackedRgba32Pixels();

            for (int y = 0; y < texture.Height; y++)
            {
                int rowStart = stride >= 0 ? y * stride : (texture.Height - 1 - y) * -stride;
                int sourceStart = y * texture.Width;
                for (int x = 0; x < texture.Width; x++)
                {
                    uint packed = pixels[sourceStart + x];
                    int offset = rowStart + x * 4;
                    bytes[offset] = (byte)((packed >> 16) & 0xff);      // B
                    bytes[offset + 1] = (byte)((packed >> 8) & 0xff);  // G
                    bytes[offset + 2] = (byte)(packed & 0xff);         // R
                    bytes[offset + 3] = (byte)((packed >> 24) & 0xff);// A
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, byteCount);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    public static Bitmap CreatePreviewBitmap(this TextureMap texture, int maxDimension = 84)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));

        maxDimension = Math.Max(8, maxDimension);
        double scale = Math.Min(maxDimension / (double)texture.Width, maxDimension / (double)texture.Height);
        int previewWidth = Math.Max(1, (int)Math.Round(texture.Width * scale));
        int previewHeight = Math.Max(1, (int)Math.Round(texture.Height * scale));
        Bitmap preview = new(previewWidth, previewHeight, PixelFormat.Format32bppArgb);

        for (int y = 0; y < previewHeight; y++)
        {
            double v = previewHeight == 1 ? 0.5 : y / (double)(previewHeight - 1);
            for (int x = 0; x < previewWidth; x++)
            {
                double u = previewWidth == 1 ? 0.5 : x / (double)(previewWidth - 1);
                Vec3 color = texture.Sample(u, 1.0 - v);
                double alpha = texture.SampleAlpha(u, 1.0 - v);
                preview.SetPixel(x, y, Color.FromArgb(
                    ToByte(alpha),
                    ToByte(color.X),
                    ToByte(color.Y),
                    ToByte(color.Z)));
            }
        }

        return preview;
    }

    private static int ToByte(double value) =>
        (int)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0);
}
