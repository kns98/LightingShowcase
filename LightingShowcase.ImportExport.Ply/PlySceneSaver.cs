// -----------------------------------------------------------------------------
// File: Scene/PlySceneSaver.cs
// Purpose: PLY export.
//
// Writes the current scene to ASCII or binary_little_endian PLY. The exporter
// preserves vertex position, UV coordinates, and per-vertex RGB color.
// Colors are written using common red/green/blue uchar vertex properties so they
// round-trip through this project and common mesh tools.
// -----------------------------------------------------------------------------

using System.IO;
using System.Globalization;
using System.Text;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Exports the current scene as a triangle-mesh PLY file.</summary>
public static class PlySceneSaver
{
    public static void Save(Scene scene, string filePath, bool binary = true)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save path is required.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        List<Triangle> triangles = scene.ObjectGroups.SelectMany(g => g.BuildWorldTriangles()).ToList();

        if (binary)
            SaveBinary(fullPath, triangles);
        else
            SaveAscii(fullPath, triangles);
    }

    private static void SaveAscii(string filePath, List<Triangle> triangles)
    {
        using StreamWriter writer = new(filePath, false, Encoding.UTF8);
        WriteHeader(writer, "ascii", triangles);

        foreach (Triangle triangle in triangles)
        {
            WriteAsciiVertex(writer, triangle.A, triangle.UvA, triangle.Material.Color);
            WriteAsciiVertex(writer, triangle.B, triangle.UvB, triangle.Material.Color);
            WriteAsciiVertex(writer, triangle.C, triangle.UvC, triangle.Material.Color);
        }

        for (int i = 0; i < triangles.Count; i++)
        {
            int baseIndex = i * 3;
            writer.WriteLine(FormattableString.Invariant($"3 {baseIndex} {baseIndex + 1} {baseIndex + 2}"));
        }
    }

    private static void SaveBinary(string filePath, List<Triangle> triangles)
    {
        using FileStream stream = File.Create(filePath);
        using BinaryWriter writer = new(stream, Encoding.ASCII);
        string header = BuildHeader("binary_little_endian", triangles);
        writer.Write(Encoding.ASCII.GetBytes(header));

        foreach (Triangle triangle in triangles)
        {
            WriteBinaryVertex(writer, triangle.A, triangle.UvA, triangle.Material.Color);
            WriteBinaryVertex(writer, triangle.B, triangle.UvB, triangle.Material.Color);
            WriteBinaryVertex(writer, triangle.C, triangle.UvC, triangle.Material.Color);
        }

        for (int i = 0; i < triangles.Count; i++)
        {
            int baseIndex = i * 3;
            writer.Write((byte)3);
            writer.Write(baseIndex);
            writer.Write(baseIndex + 1);
            writer.Write(baseIndex + 2);
        }
    }

    private static void WriteHeader(TextWriter writer, string format, List<Triangle> triangles) => writer.Write(BuildHeader(format, triangles));

    private static string BuildHeader(string format, List<Triangle> triangles)
    {
        StringBuilder builder = new();
        builder.AppendLine("ply");
        builder.AppendLine($"format {format} 1.0");
        builder.AppendLine("comment Exported by LightingShowcase");
        builder.AppendLine($"element vertex {triangles.Count * 3}");
        builder.AppendLine("property float x");
        builder.AppendLine("property float y");
        builder.AppendLine("property float z");
        builder.AppendLine("property float u");
        builder.AppendLine("property float v");
        builder.AppendLine("property uchar red");
        builder.AppendLine("property uchar green");
        builder.AppendLine("property uchar blue");
        builder.AppendLine($"element face {triangles.Count}");
        builder.AppendLine("property list uchar int vertex_indices");
        builder.AppendLine("end_header");
        return builder.ToString();
    }

    private static void WriteAsciiVertex(TextWriter writer, Vec3 position, Vec2 uv, Vec3 color)
    {
        (byte r, byte g, byte b) = ToRgb(color);
        writer.WriteLine(FormattableString.Invariant($"{position.X:G17} {position.Y:G17} {position.Z:G17} {uv.U:G17} {uv.V:G17} {r} {g} {b}"));
    }

    private static void WriteBinaryVertex(BinaryWriter writer, Vec3 position, Vec2 uv, Vec3 color)
    {
        writer.Write((float)position.X);
        writer.Write((float)position.Y);
        writer.Write((float)position.Z);
        writer.Write((float)uv.U);
        writer.Write((float)uv.V);
        (byte r, byte g, byte b) = ToRgb(color);
        writer.Write(r);
        writer.Write(g);
        writer.Write(b);
    }

    private static (byte R, byte G, byte B) ToRgb(Vec3 color) =>
        ((byte)Math.Clamp((int)Math.Round(color.X * 255.0), 0, 255),
         (byte)Math.Clamp((int)Math.Round(color.Y * 255.0), 0, 255),
         (byte)Math.Clamp((int)Math.Round(color.Z * 255.0), 0, 255));

}
