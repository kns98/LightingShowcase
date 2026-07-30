// -----------------------------------------------------------------------------
// File: Scene/ThreeDsSceneSaver.cs
// Purpose: 3DS export.
//
// Writes a broadly compatible legacy Autodesk 3DS mesh file. The 3DS format is
// limited to 65,535 vertices/faces per mesh, so large scenes are automatically
// split into multiple object chunks. Geometry and UVs are baked to world space;
// lights, hierarchy, and advanced material properties are not represented by
// this interchange format.
// -----------------------------------------------------------------------------

using System.IO;
using System.Text;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Exports the current scene as a legacy Autodesk 3DS static mesh.</summary>
public static class ThreeDsSceneSaver
{
    private const ushort MainChunk = 0x4D4D;
    private const ushort EditorChunk = 0x3D3D;
    private const ushort ObjectBlock = 0x4000;
    private const ushort TriangularMesh = 0x4100;
    private const ushort VertexList = 0x4110;
    private const ushort FaceList = 0x4120;
    private const ushort FaceMaterial = 0x4130;
    private const ushort MappingCoords = 0x4140;
    private const ushort MaterialBlock = 0xAFFF;
    private const ushort MaterialName = 0xA000;
    private const ushort MaterialDiffuse = 0xA020;
    private const ushort TextureMap1 = 0xA200;
    private const ushort MappingFilename = 0xA300;
    private const ushort Color24 = 0x0011;
    private const int MaxTrianglesPerObject = 21_000;

    private sealed class ExportMaterial
    {
        public string Name { get; }
        public Material Material { get; }

        public ExportMaterial(string name, Material material)
        {
            Name = name;
            Material = material;
        }
    }

    private sealed class ExportTriangle
    {
        public Triangle Triangle { get; }
        public string MaterialName { get; }

        public ExportTriangle(Triangle triangle, string materialName)
        {
            Triangle = triangle;
            MaterialName = materialName;
        }
    }

    public static void Save(Scene scene, string filePath)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save path is required.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);

        Dictionary<string, ExportMaterial> materials = new(StringComparer.Ordinal);
        List<(string Name, List<ExportTriangle> Triangles)> objects = BuildExportObjects(scene, materials);

        byte[] editor = BuildChunk(EditorChunk, writer =>
        {
            foreach (ExportMaterial material in materials.Values)
                WriteBytes(writer, BuildMaterialChunk(material));

            foreach ((string name, List<ExportTriangle> triangles) in objects)
            {
                int part = 1;
                foreach (List<ExportTriangle> batch in Batch(triangles, MaxTrianglesPerObject))
                    WriteBytes(writer, BuildObjectChunk($"{name}_{part++:000}", batch));
            }
        });

        byte[] main = BuildChunk(MainChunk, writer => WriteBytes(writer, editor));
        File.WriteAllBytes(fullPath, main);
    }

    private static List<(string Name, List<ExportTriangle> Triangles)> BuildExportObjects(Scene scene, Dictionary<string, ExportMaterial> materials)
    {
        List<(string Name, List<ExportTriangle> Triangles)> objects = new();
        foreach (SceneObjectGroup group in scene.ObjectGroups)
        {
            List<ExportTriangle> triangles = new();
            foreach (Triangle triangle in group.BuildWorldTriangles())
            {
                string materialName = MaterialNameFor(triangle.Material, materials);
                triangles.Add(new ExportTriangle(triangle, materialName));
            }

            if (triangles.Count > 0)
                objects.Add((SanitizeObjectName(group.Name), triangles));
        }

        if (objects.Count == 0)
            objects.Add(("Empty", new List<ExportTriangle>()));

        return objects;
    }

    private static byte[] BuildMaterialChunk(ExportMaterial exportMaterial) =>
        BuildChunk(MaterialBlock, writer =>
        {
            WriteBytes(writer, BuildChunk(MaterialName, materialWriter => WriteCString(materialWriter, exportMaterial.Name)));
            WriteBytes(writer, BuildChunk(MaterialDiffuse, diffuseWriter =>
                WriteBytes(diffuseWriter, BuildChunk(Color24, colorWriter =>
                {
                    (byte r, byte g, byte b) = ToRgb(exportMaterial.Material.Color);
                    colorWriter.Write(r);
                    colorWriter.Write(g);
                    colorWriter.Write(b);
                }))));

            string? texturePath = exportMaterial.Material.Texture?.SourcePath;
            if (!string.IsNullOrWhiteSpace(texturePath))
            {
                string textureFileName = Path.GetFileName(texturePath);
                WriteBytes(writer, BuildChunk(TextureMap1, textureWriter =>
                    WriteBytes(textureWriter, BuildChunk(MappingFilename, fileWriter => WriteCString(fileWriter, textureFileName)))));
            }
        });

    private static byte[] BuildObjectChunk(string objectName, List<ExportTriangle> triangles) =>
        BuildChunk(ObjectBlock, writer =>
        {
            WriteCString(writer, SanitizeObjectName(objectName));
            WriteBytes(writer, BuildChunk(TriangularMesh, meshWriter =>
            {
                WriteBytes(meshWriter, BuildVertexListChunk(triangles));
                WriteBytes(meshWriter, BuildMappingCoordsChunk(triangles));
                WriteBytes(meshWriter, BuildFaceListChunk(triangles));
            }));
        });

    private static byte[] BuildVertexListChunk(List<ExportTriangle> triangles) =>
        BuildChunk(VertexList, writer =>
        {
            writer.Write(checked((ushort)(triangles.Count * 3)));
            foreach (ExportTriangle exportTriangle in triangles)
            {
                WriteFloatVec3(writer, exportTriangle.Triangle.A);
                WriteFloatVec3(writer, exportTriangle.Triangle.B);
                WriteFloatVec3(writer, exportTriangle.Triangle.C);
            }
        });

    private static byte[] BuildMappingCoordsChunk(List<ExportTriangle> triangles) =>
        BuildChunk(MappingCoords, writer =>
        {
            writer.Write(checked((ushort)(triangles.Count * 3)));
            foreach (ExportTriangle exportTriangle in triangles)
            {
                WriteFloatVec2(writer, exportTriangle.Triangle.UvA);
                WriteFloatVec2(writer, exportTriangle.Triangle.UvB);
                WriteFloatVec2(writer, exportTriangle.Triangle.UvC);
            }
        });

    private static byte[] BuildFaceListChunk(List<ExportTriangle> triangles) =>
        BuildChunk(FaceList, writer =>
        {
            writer.Write(checked((ushort)triangles.Count));
            for (int i = 0; i < triangles.Count; i++)
            {
                int baseIndex = i * 3;
                writer.Write(checked((ushort)baseIndex));
                writer.Write(checked((ushort)(baseIndex + 1)));
                writer.Write(checked((ushort)(baseIndex + 2)));
                writer.Write((ushort)0);
            }

            foreach (IGrouping<string, int> materialFaces in triangles.Select((tri, index) => new { tri.MaterialName, index }).GroupBy(item => item.MaterialName, item => item.index))
                WriteBytes(writer, BuildChunk(FaceMaterial, materialWriter =>
                {
                    int[] indices = materialFaces.ToArray();
                    WriteCString(materialWriter, materialFaces.Key);
                    materialWriter.Write(checked((ushort)indices.Length));
                    foreach (int index in indices)
                        materialWriter.Write(checked((ushort)index));
                }));
        });

    private static IEnumerable<List<T>> Batch<T>(List<T> values, int size)
    {
        for (int offset = 0; offset < values.Count; offset += size)
            yield return values.GetRange(offset, Math.Min(size, values.Count - offset));
    }

    private static byte[] BuildChunk(ushort id, Action<BinaryWriter> writePayload)
    {
        using MemoryStream payloadStream = new();
        using (BinaryWriter payloadWriter = new(payloadStream, Encoding.ASCII, leaveOpen: true))
            writePayload(payloadWriter);

        using MemoryStream chunkStream = new();
        using BinaryWriter chunkWriter = new(chunkStream, Encoding.ASCII, leaveOpen: true);
        chunkWriter.Write(id);
        chunkWriter.Write(checked((uint)(payloadStream.Length + 6)));
        chunkWriter.Write(payloadStream.ToArray());
        return chunkStream.ToArray();
    }

    private static string MaterialNameFor(Material material, Dictionary<string, ExportMaterial> materials)
    {
        string key = FormattableString.Invariant($"{material.Color.X:G17}|{material.Color.Y:G17}|{material.Color.Z:G17}|{material.Texture?.SourcePath ?? string.Empty}");
        if (materials.TryGetValue(key, out ExportMaterial? existing))
            return existing.Name;

        string name = $"mat{materials.Count + 1:000}";
        materials[key] = new ExportMaterial(name, material);
        return name;
    }

    private static void WriteBytes(BinaryWriter writer, byte[] bytes) => writer.Write(bytes);

    private static void WriteCString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(value.Where(ch => ch >= 32 && ch <= 126).ToArray());
        writer.Write(bytes);
        writer.Write((byte)0);
    }

    private static void WriteFloatVec3(BinaryWriter writer, Vec3 value)
    {
        writer.Write((float)value.X);
        writer.Write((float)value.Y);
        writer.Write((float)value.Z);
    }

    private static void WriteFloatVec2(BinaryWriter writer, Vec2 value)
    {
        writer.Write((float)value.U);
        writer.Write((float)value.V);
    }

    private static (byte R, byte G, byte B) ToRgb(Vec3 color) =>
        ((byte)Math.Round(Clamp01(color.X) * 255.0),
         (byte)Math.Round(Clamp01(color.Y) * 255.0),
         (byte)Math.Round(Clamp01(color.Z) * 255.0));

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    private static string SanitizeObjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Object";

        StringBuilder builder = new();
        foreach (char ch in name)
        {
            if (builder.Length >= 40)
                break;
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        }
        return builder.Length == 0 ? "Object" : builder.ToString();
    }
}
