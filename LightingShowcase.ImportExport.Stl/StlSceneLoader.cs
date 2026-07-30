// -----------------------------------------------------------------------------
// File: Scene/StlSceneLoader.cs
// Purpose: STL import.
//
// Imports widely available ASCII and binary STL files into the internal scene
// graph. STL carries only triangle positions, so this loader generates stable
// box-projection UVs for later texture application.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Imports ASCII and binary STL assets into the internal scene graph.</summary>
public static class StlSceneLoader
{
    private readonly struct RawTriangle
    {
        public readonly Vec3 A;
        public readonly Vec3 B;
        public readonly Vec3 C;

        public RawTriangle(Vec3 a, Vec3 b, Vec3 c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    public static ObjLoadResult LoadIntoScene(
        Scene scene,
        string filePath,
        Material fallbackMaterial,
        double targetSize = 2.15,
        Vec3? targetCenter = null,
        double floorY = -1.48,
        Action<ObjLoadProgress>? progress = null)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("STL file path is required.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("STL file was not found.", filePath);

        string fullPath = Path.GetFullPath(filePath);
        progress?.Invoke(new ObjLoadProgress("Reading STL", 5, 0, 0, 0));
        List<RawTriangle> triangles = IsProbablyBinaryStl(fullPath) ? ReadBinary(fullPath, progress) : ReadAscii(fullPath, progress);
        if (triangles.Count == 0)
            throw new InvalidDataException("STL file does not contain any triangles.");

        GetBounds(triangles, out Vec3 min, out Vec3 max);
        Vec3 size = max - min;
        double largestAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (largestAxis < 1e-8)
            throw new InvalidDataException("STL model bounds are degenerate.");

        double scale = targetSize / largestAxis;
        Vec3 sourceCenter = (min + max) * 0.5;
        Vec3 center = targetCenter ?? new Vec3(0.0, 0.0, 3.45);
        double scaledMinY = (min.Y - sourceCenter.Y) * scale + center.Y;
        Vec3 offset = new(center.X, center.Y + (floorY - scaledMinY), center.Z);

        SceneObjectGroup group = scene.AddImportedGroup(Path.GetFileNameWithoutExtension(fullPath));
        Aabb targetBounds = new(Transform(min, sourceCenter, scale, offset), Transform(max, sourceCenter, scale, offset));

        for (int i = 0; i < triangles.Count; i++)
        {
            RawTriangle tri = triangles[i];
            Vec3 a = Transform(tri.A, sourceCenter, scale, offset);
            Vec3 b = Transform(tri.B, sourceCenter, scale, offset);
            Vec3 c = Transform(tri.C, sourceCenter, scale, offset);
            Vec3 normal = (b - a).Cross(c - a).Normalize();
            if (normal.Length() > 1e-10)
                group.AddTriangle(a, b, c, GenerateBoxUv(a, normal, targetBounds), GenerateBoxUv(b, normal, targetBounds), GenerateBoxUv(c, normal, targetBounds), fallbackMaterial);

            if ((i & 2047) == 0)
            {
                int percent = 50 + (int)(40.0 * i / Math.Max(1, triangles.Count));
                progress?.Invoke(new ObjLoadProgress("Building STL scene", percent, 0, i, i));
            }
        }

        group.RecalculatePivot();
        progress?.Invoke(new ObjLoadProgress("Building acceleration structure", 94, 0, triangles.Count, triangles.Count));
        scene.RebuildWorldGeometry();
        progress?.Invoke(new ObjLoadProgress("Done", 100, 0, triangles.Count, scene.Triangles.Count));
        return new ObjLoadResult(filePath, 0, triangles.Count, scene.Triangles.Count);
    }

    private static bool IsProbablyBinaryStl(string filePath)
    {
        long length = new FileInfo(filePath).Length;
        if (length < 84) return false;

        using FileStream stream = File.OpenRead(filePath);
        Span<byte> header = stackalloc byte[84];
        int read = stream.Read(header);
        if (read < 84) return false;

        uint count = BitConverter.ToUInt32(header.Slice(80, 4));
        long expected = 84L + count * 50L;
        if (expected == length) return true;

        string prefix = Encoding.ASCII.GetString(header.Slice(0, Math.Min(5, header.Length)));
        return !prefix.Equals("solid", StringComparison.OrdinalIgnoreCase);
    }

    private static List<RawTriangle> ReadBinary(string filePath, Action<ObjLoadProgress>? progress)
    {
        List<RawTriangle> triangles = new();
        using BinaryReader reader = new(File.OpenRead(filePath));
        reader.ReadBytes(80);
        uint count = reader.ReadUInt32();
        triangles.Capacity = count > int.MaxValue ? int.MaxValue : (int)count;

        for (uint i = 0; i < count; i++)
        {
            reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle();
            Vec3 a = ReadVec3(reader);
            Vec3 b = ReadVec3(reader);
            Vec3 c = ReadVec3(reader);
            reader.ReadUInt16();
            triangles.Add(new RawTriangle(a, b, c));

            if ((i & 4095) == 0)
            {
                int percent = 5 + (int)(45.0 * i / Math.Max(1, count));
                progress?.Invoke(new ObjLoadProgress("Reading binary STL", percent, 0, (int)Math.Min(i, int.MaxValue), (int)Math.Min(i, int.MaxValue)));
            }
        }

        return triangles;
    }

    private static Vec3 ReadVec3(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static List<RawTriangle> ReadAscii(string filePath, Action<ObjLoadProgress>? progress)
    {
        List<RawTriangle> triangles = new();
        List<Vec3> vertices = new(3);
        long length = Math.Max(1, new FileInfo(filePath).Length);

        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 20, FileOptions.SequentialScan);
        using StreamReader reader = new(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("vertex ", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && TryParse(parts[1], out double x) && TryParse(parts[2], out double y) && TryParse(parts[3], out double z))
                {
                    vertices.Add(new Vec3(x, y, z));
                    if (vertices.Count == 3)
                    {
                        triangles.Add(new RawTriangle(vertices[0], vertices[1], vertices[2]));
                        vertices.Clear();
                    }
                }
            }

            if ((triangles.Count & 2047) == 0)
            {
                int percent = 5 + (int)(45.0 * stream.Position / length);
                progress?.Invoke(new ObjLoadProgress("Reading ASCII STL", percent, 0, triangles.Count, triangles.Count));
            }
        }

        return triangles;
    }

    private static bool TryParse(string value, out double number) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static void GetBounds(List<RawTriangle> triangles, out Vec3 min, out Vec3 max)
    {
        min = triangles[0].A;
        max = triangles[0].A;
        foreach (RawTriangle tri in triangles)
        {
            Add(tri.A, ref min, ref max);
            Add(tri.B, ref min, ref max);
            Add(tri.C, ref min, ref max);
        }
    }

    private static void Add(Vec3 p, ref Vec3 min, ref Vec3 max)
    {
        min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
        max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
    }

    private static Vec3 Transform(Vec3 p, Vec3 sourceCenter, double scale, Vec3 offset) => (p - sourceCenter) * scale + offset;

    private const double TextureRepeatWorldUnits = 0.25;

    private static Vec2 GenerateBoxUv(Vec3 point, Vec3 normal, Aabb bounds)
    {
        double nx = Math.Abs(normal.X), ny = Math.Abs(normal.Y), nz = Math.Abs(normal.Z);

        // STL/PLY/3DS meshes may not contain texture coordinates.  For fallback
        // UVs, use scene-space tile coordinates rather than normalized bounds so
        // textures repeat over large faces instead of stretching over them.
        if (ny >= nx && ny >= nz)
            return new Vec2(ToTileCoordinate(point.X, bounds.Min.X), ToTileCoordinate(point.Z, bounds.Min.Z));
        if (nx >= ny && nx >= nz)
            return new Vec2(ToTileCoordinate(point.Z, bounds.Min.Z), ToTileCoordinate(point.Y, bounds.Min.Y));
        return new Vec2(ToTileCoordinate(point.X, bounds.Min.X), ToTileCoordinate(point.Y, bounds.Min.Y));
    }

    private static double ToTileCoordinate(double value, double origin) =>
        (value - origin) / TextureRepeatWorldUnits;
}
