// -----------------------------------------------------------------------------
// File: Scene/StlSceneSaver.cs
// Purpose: STL export.
//
// Writes the current scene to ASCII or binary STL. STL is geometry-only, so
// materials, lights, object hierarchy, transforms, and texture coordinates are
// intentionally baked down to world-space triangles.
// -----------------------------------------------------------------------------

using System.IO;
using System.Globalization;
using System.Text;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Exports the current scene as STL triangle geometry.</summary>
public static class StlSceneSaver
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

    private static void SaveBinary(string filePath, List<Triangle> triangles)
    {
        using BinaryWriter writer = new(File.Create(filePath), Encoding.ASCII);
        byte[] header = new byte[80];
        Encoding.ASCII.GetBytes("Exported by LightingShowcase binary STL").CopyTo(header, 0);
        writer.Write(header);
        writer.Write((uint)triangles.Count);

        foreach (Triangle triangle in triangles)
        {
            Vec3 normal = SurfaceNormal(triangle);
            WriteFloatVec3(writer, normal);
            WriteFloatVec3(writer, triangle.A);
            WriteFloatVec3(writer, triangle.B);
            WriteFloatVec3(writer, triangle.C);
            writer.Write((ushort)0);
        }
    }

    private static void SaveAscii(string filePath, List<Triangle> triangles)
    {
        using StreamWriter writer = new(filePath, false, Encoding.UTF8);
        writer.WriteLine("solid LightingShowcase");
        foreach (Triangle triangle in triangles)
        {
            Vec3 normal = SurfaceNormal(triangle);
            writer.WriteLine(FormattableString.Invariant($"  facet normal {normal.X:G17} {normal.Y:G17} {normal.Z:G17}"));
            writer.WriteLine("    outer loop");
            WriteAsciiVertex(writer, triangle.A);
            WriteAsciiVertex(writer, triangle.B);
            WriteAsciiVertex(writer, triangle.C);
            writer.WriteLine("    endloop");
            writer.WriteLine("  endfacet");
        }
        writer.WriteLine("endsolid LightingShowcase");
    }

    private static Vec3 SurfaceNormal(Triangle triangle)
    {
        Vec3 normal = (triangle.B - triangle.A).Cross(triangle.C - triangle.A).Normalize();
        return normal.Length() < 1e-10 ? new Vec3(0.0, 1.0, 0.0) : normal;
    }

    private static void WriteFloatVec3(BinaryWriter writer, Vec3 value)
    {
        writer.Write((float)value.X);
        writer.Write((float)value.Y);
        writer.Write((float)value.Z);
    }

    private static void WriteAsciiVertex(StreamWriter writer, Vec3 value) =>
        writer.WriteLine(FormattableString.Invariant($"      vertex {value.X:G17} {value.Y:G17} {value.Z:G17}"));
}
