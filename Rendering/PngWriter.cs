using System.Buffers.Binary;
using System.IO.Compression;

namespace LightingShowcase.Rendering;

/// <summary>Minimal streaming PNG writer for 8-bit RGBA images.</summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, RenderImage image)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("An output path is required.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);

        using FileStream output = File.Create(fullPath);
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], checked((uint)image.Width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), checked((uint)image.Height));
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // no interlace
        WriteChunk(output, "IHDR"u8, ihdr);

        using (ChunkedIdatStream idat = new(output, 1024 * 1024))
        {
            using (ZLibStream zlib = new(idat, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] row = new byte[checked(image.Width * 4 + 1)];
                for (int y = 0; y < image.Height; y++)
                {
                    row[0] = 0;
                    int basePixel = y * image.Width;
                    for (int x = 0; x < image.Width; x++)
                    {
                        uint packed = image.PackedRgba32[basePixel + x];
                        int offset = 1 + x * 4;
                        row[offset] = (byte)(packed & 0xFF);
                        row[offset + 1] = (byte)((packed >> 8) & 0xFF);
                        row[offset + 2] = (byte)((packed >> 16) & 0xFF);
                        row[offset + 3] = (byte)((packed >> 24) & 0xFF);
                    }
                    zlib.Write(row);
                }
            }
            idat.Complete();
        }

        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        output.Write(number);
        output.Write(type);
        output.Write(data);

        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFF);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>Turns one continuous zlib stream into bounded consecutive PNG IDAT chunks.</summary>
    private sealed class ChunkedIdatStream : Stream
    {
        private readonly Stream destination;
        private readonly byte[] buffer;
        private int count;
        private bool completed;

        public ChunkedIdatStream(Stream destination, int chunkSize)
        {
            this.destination = destination;
            buffer = new byte[Math.Max(4096, chunkSize)];
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => FlushChunk();

        public override void Write(byte[] source, int offset, int length) => Write(source.AsSpan(offset, length));

        public override void Write(ReadOnlySpan<byte> source)
        {
            ObjectDisposedException.ThrowIf(completed, this);
            while (!source.IsEmpty)
            {
                int copy = Math.Min(buffer.Length - count, source.Length);
                source[..copy].CopyTo(buffer.AsSpan(count));
                count += copy;
                source = source[copy..];
                if (count == buffer.Length) FlushChunk();
            }
        }

        public void Complete()
        {
            if (completed) return;
            FlushChunk();
            completed = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Complete();
            base.Dispose(disposing);
        }

        private void FlushChunk()
        {
            if (count == 0) return;
            WriteChunk(destination, "IDAT"u8, buffer.AsSpan(0, count));
            count = 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
