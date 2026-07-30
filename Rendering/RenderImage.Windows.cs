using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LightingShowcase.Rendering;

/// <summary>Windows desktop conversion helpers for the shared cross-platform render image.</summary>
public static class RenderImageWindowsExtensions
{
    public static Bitmap ToBitmap(this RenderImage image)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));

        Bitmap bitmap = new(image.Width, image.Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, image.Width, image.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] bytes = new byte[Math.Abs(stride) * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                int rowStart = stride >= 0 ? y * stride : (image.Height - 1 - y) * -stride;
                int sourceStart = y * image.Width;
                for (int x = 0; x < image.Width; x++)
                {
                    uint packed = image.PackedRgba32[sourceStart + x];
                    int destination = rowStart + x * 4;
                    bytes[destination] = (byte)((packed >> 16) & 0xff);      // B
                    bytes[destination + 1] = (byte)((packed >> 8) & 0xff);  // G
                    bytes[destination + 2] = (byte)(packed & 0xff);         // R
                    bytes[destination + 3] = (byte)((packed >> 24) & 0xff);// A
                }
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }
}
