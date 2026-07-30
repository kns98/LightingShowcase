using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Obj;

/// <summary>Exports visible scene geometry as Wavefront OBJ with a companion MTL file.</summary>
public static class ObjSceneSaver
{
    public static void Save(Scene scene, string filePath)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save path is required.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);

        string objDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        string objName = Path.GetFileNameWithoutExtension(fullPath);
        string mtlFileName = objName + ".mtl";
        string mtlPath = Path.Combine(objDirectory, mtlFileName);

        List<SceneObjectGroup> groups = scene.ObjectGroups.Where(g => g.Visible && g.BuildWorldTriangles().Any()).ToList();
        List<Triangle> allTriangles = groups.SelectMany(g => g.BuildWorldTriangles()).ToList();
        Dictionary<MaterialKey, string> materialNames = BuildMaterialNames(allTriangles);

        using (StreamWriter writer = new(fullPath, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("# Exported by LightingShowcase");
            writer.WriteLine($"mtllib {mtlFileName}");
            writer.WriteLine();

            int vertexIndex = 1;
            int texCoordIndex = 1;
            foreach (SceneObjectGroup group in groups)
            {
                List<Triangle> triangles = group.BuildWorldTriangles().ToList();
                if (triangles.Count == 0) continue;

                writer.WriteLine($"o {SanitizeName(group.Name)}");
                string? currentMaterial = null;
                foreach (Triangle triangle in triangles)
                {
                    string materialName = materialNames[MaterialKey.FromMaterial(triangle.Material)];
                    if (!string.Equals(currentMaterial, materialName, StringComparison.Ordinal))
                    {
                        writer.WriteLine($"usemtl {materialName}");
                        currentMaterial = materialName;
                    }

                    WriteVertex(writer, triangle.A);
                    WriteVertex(writer, triangle.B);
                    WriteVertex(writer, triangle.C);
                    WriteTexCoord(writer, triangle.UvA);
                    WriteTexCoord(writer, triangle.UvB);
                    WriteTexCoord(writer, triangle.UvC);
                    writer.WriteLine(FormattableString.Invariant($"f {vertexIndex}/{texCoordIndex} {vertexIndex + 1}/{texCoordIndex + 1} {vertexIndex + 2}/{texCoordIndex + 2}"));
                    vertexIndex += 3;
                    texCoordIndex += 3;
                }
                writer.WriteLine();
            }
        }

        WriteMaterialFile(mtlPath, materialNames, objDirectory);
    }

    private readonly record struct MaterialKey(double R, double G, double B, string TexturePath)
    {
        public static MaterialKey FromMaterial(Material material)
        {
            string texturePath = material.Texture?.SourcePath ?? string.Empty;
            return new MaterialKey(Round(material.Color.X), Round(material.Color.Y), Round(material.Color.Z), texturePath);
        }

        private static double Round(double value) => Math.Round(value, 6);
    }

    private static Dictionary<MaterialKey, string> BuildMaterialNames(List<Triangle> triangles)
    {
        Dictionary<MaterialKey, string> names = new();
        int index = 1;
        foreach (Triangle triangle in triangles)
        {
            MaterialKey key = MaterialKey.FromMaterial(triangle.Material);
            if (!names.ContainsKey(key))
                names[key] = $"mat_{index++:000}";
        }
        return names;
    }

    private static void WriteMaterialFile(string mtlPath, Dictionary<MaterialKey, string> materialNames, string objDirectory)
    {
        using StreamWriter writer = new(mtlPath, false, new UTF8Encoding(false));
        writer.WriteLine("# Exported by LightingShowcase");
        foreach (KeyValuePair<MaterialKey, string> kvp in materialNames.OrderBy(kvp => kvp.Value, StringComparer.Ordinal))
        {
            MaterialKey key = kvp.Key;
            string name = kvp.Value;
            writer.WriteLine();
            writer.WriteLine($"newmtl {name}");
            writer.WriteLine(FormattableString.Invariant($"Kd {key.R:G17} {key.G:G17} {key.B:G17}"));
            writer.WriteLine("Ka 0 0 0");
            writer.WriteLine("Ks 0 0 0");
            writer.WriteLine("d 1");
            if (!string.IsNullOrWhiteSpace(key.TexturePath))
            {
                string texturePath = Path.IsPathRooted(key.TexturePath) ? key.TexturePath : Path.GetFullPath(Path.Combine(objDirectory, key.TexturePath));
                string relative = Path.GetRelativePath(objDirectory, texturePath).Replace('\\', '/');
                writer.WriteLine($"map_Kd {relative}");
            }
        }
    }

    private static void WriteVertex(StreamWriter writer, Vec3 value) =>
        writer.WriteLine(FormattableString.Invariant($"v {value.X:G17} {value.Y:G17} {value.Z:G17}"));

    private static void WriteTexCoord(StreamWriter writer, Vec2 value) =>
        writer.WriteLine(FormattableString.Invariant($"vt {value.U:G17} {value.V:G17}"));

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Object";
        StringBuilder builder = new(value.Length);
        foreach (char ch in value)
            builder.Append(char.IsControl(ch) ? '_' : ch);
        return builder.ToString().Trim();
    }
}
