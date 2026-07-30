using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Obj;

/// <summary>Imports Wavefront OBJ mesh geometry, MTL diffuse colors, UVs, and diffuse texture references.</summary>
public static class ObjSceneLoader
{
    private sealed record FaceVertex(int VertexIndex, int TextureIndex);
    private sealed record RawFace(List<FaceVertex> Vertices, string MaterialName, string ObjectName);
    private sealed record ObjMaterial(Vec3 Color, string? TexturePath);

    public static ObjLoadResult LoadIntoScene(
        Scene scene,
        string filePath,
        Material fallbackMaterial,
        double targetSize = 2.15,
        Vec3? targetCenter = null,
        double floorY = -1.48,
        Action<ObjLoadProgress>? progress = null,
        double? simplifyKeepFraction = null)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("OBJ file path is required.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("OBJ file was not found.", filePath);

        string fullPath = Path.GetFullPath(filePath);
        progress?.Invoke(new ObjLoadProgress("Reading OBJ", 5, 0, 0, 0));

        List<Vec3> vertices = new();
        List<Vec2> textureCoordinates = new();
        List<RawFace> faces = new();
        Dictionary<string, ObjMaterial> materialDefs = new(StringComparer.OrdinalIgnoreCase);
        string currentMaterial = string.Empty;
        string currentObject = Path.GetFileNameWithoutExtension(fullPath);

        string baseDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        int lineNumber = 0;
        foreach (string rawLine in File.ReadLines(fullPath))
        {
            lineNumber++;
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            string[] parts = SplitWhitespace(line);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    vertices.Add(new Vec3(ParseDouble(parts[1], lineNumber), ParseDouble(parts[2], lineNumber), ParseDouble(parts[3], lineNumber)));
                    break;
                case "vt" when parts.Length >= 3:
                    textureCoordinates.Add(new Vec2(ParseDouble(parts[1], lineNumber), ParseDouble(parts[2], lineNumber)));
                    break;
                case "f" when parts.Length >= 4:
                    faces.Add(new RawFace(parts.Skip(1).Select(token => ParseFaceVertex(token, vertices.Count, textureCoordinates.Count, lineNumber)).ToList(), currentMaterial, currentObject));
                    break;
                case "usemtl" when parts.Length >= 2:
                    currentMaterial = string.Join(' ', parts.Skip(1));
                    break;
                case "o" or "g" when parts.Length >= 2:
                    currentObject = SanitizeObjectName(string.Join(' ', parts.Skip(1)), Path.GetFileNameWithoutExtension(fullPath));
                    break;
                case "mtllib" when parts.Length >= 2:
                    foreach (string mtlName in ParseMaterialLibraryNames(line.Substring(parts[0].Length).Trim()))
                    {
                        string mtlPath = ResolveRelativePath(baseDirectory, mtlName);
                        foreach (KeyValuePair<string, ObjMaterial> material in LoadMaterialLibrary(mtlPath))
                            materialDefs[material.Key] = material.Value;
                    }
                    break;
            }

            if ((lineNumber & 4095) == 0)
                progress?.Invoke(new ObjLoadProgress("Reading OBJ", Math.Min(35, 5 + lineNumber / 1000), vertices.Count, faces.Count, 0));
        }

        if (vertices.Count == 0) throw new InvalidDataException("OBJ file does not contain any vertices.");
        if (faces.Count == 0) throw new InvalidDataException("OBJ file does not contain any faces.");

        GetBounds(vertices, out Vec3 min, out Vec3 max);
        Vec3 size = max - min;
        double largestAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (largestAxis < 1e-8) throw new InvalidDataException("OBJ model bounds are degenerate.");

        double scale = targetSize / largestAxis;
        Vec3 sourceCenter = (min + max) * 0.5;
        Vec3 center = targetCenter ?? new Vec3(0.0, 0.0, 3.45);
        double scaledMinY = (min.Y - sourceCenter.Y) * scale + center.Y;
        Vec3 offset = new(center.X, center.Y + (floorY - scaledMinY), center.Z);

        Dictionary<string, Material> materialCache = BuildMaterialCache(materialDefs, baseDirectory, fallbackMaterial);
        Dictionary<string, SceneObjectGroup> groups = new(StringComparer.OrdinalIgnoreCase);
        int triangleCount = 0;

        progress?.Invoke(new ObjLoadProgress("Building OBJ mesh", 40, vertices.Count, faces.Count, 0));
        for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            RawFace face = faces[faceIndex];
            if (face.Vertices.Count < 3) continue;

            SceneObjectGroup group = GetOrCreateGroup(scene, groups, face.ObjectName);
            Material material = ResolveMaterial(materialCache, face.MaterialName, fallbackMaterial);
            FaceVertex root = face.Vertices[0];
            for (int i = 1; i + 1 < face.Vertices.Count; i++)
            {
                FaceVertex b = face.Vertices[i];
                FaceVertex c = face.Vertices[i + 1];
                Vec3 aPos = Transform(vertices[root.VertexIndex], sourceCenter, scale, offset);
                Vec3 bPos = Transform(vertices[b.VertexIndex], sourceCenter, scale, offset);
                Vec3 cPos = Transform(vertices[c.VertexIndex], sourceCenter, scale, offset);
                if (IsDegenerate(aPos, bPos, cPos)) continue;

                Vec2 uvA = GetUv(textureCoordinates, root.TextureIndex, new Vec2(0, 0));
                Vec2 uvB = GetUv(textureCoordinates, b.TextureIndex, new Vec2(1, 0));
                Vec2 uvC = GetUv(textureCoordinates, c.TextureIndex, new Vec2(0, 1));
                group.AddTriangle(aPos, bPos, cPos, uvA, uvB, uvC, material);
                triangleCount++;
            }

            if ((faceIndex & 2047) == 0)
            {
                int percent = 40 + (int)(50.0 * faceIndex / Math.Max(1, faces.Count));
                progress?.Invoke(new ObjLoadProgress("Building OBJ mesh", percent, vertices.Count, faceIndex, triangleCount));
            }
        }

        foreach (SceneObjectGroup group in groups.Values)
            group.RecalculatePivot();

        if (triangleCount == 0) throw new InvalidDataException("OBJ file did not produce any non-degenerate triangles.");

        if (simplifyKeepFraction.HasValue && simplifyKeepFraction.Value < 0.999)
        {
            progress?.Invoke(new ObjLoadProgress("Simplifying mesh", 90, vertices.Count, faces.Count, triangleCount));
            foreach (SceneObjectGroup group in groups.Values)
                group.SimplifyGeometry(simplifyKeepFraction.Value);
            triangleCount = groups.Values.Sum(g => g.CountLocalTrianglesRecursively());
        }

        progress?.Invoke(new ObjLoadProgress("Building acceleration structure", 94, vertices.Count, faces.Count, triangleCount));
        scene.RebuildWorldGeometry();
        progress?.Invoke(new ObjLoadProgress("Done", 100, vertices.Count, faces.Count, triangleCount));
        return new ObjLoadResult(filePath, vertices.Count, faces.Count, triangleCount);
    }

    private static SceneObjectGroup GetOrCreateGroup(Scene scene, Dictionary<string, SceneObjectGroup> groups, string name)
    {
        string key = string.IsNullOrWhiteSpace(name) ? "OBJ Object" : name;
        if (groups.TryGetValue(key, out SceneObjectGroup? group)) return group;
        group = scene.AddImportedGroup(key);
        groups[key] = group;
        return group;
    }

    private static Dictionary<string, Material> BuildMaterialCache(Dictionary<string, ObjMaterial> materialDefs, string baseDirectory, Material fallback)
    {
        Dictionary<string, Material> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ObjMaterial> kvp in materialDefs)
        {
            string name = kvp.Key;
            ObjMaterial definition = kvp.Value;
            TextureMap? texture = null;
            if (!string.IsNullOrWhiteSpace(definition.TexturePath))
            {
                string texturePath = ResolveRelativePath(baseDirectory, definition.TexturePath);
                try { if (File.Exists(texturePath)) texture = TextureMap.FromFile(texturePath); }
                catch { texture = null; }
            }
            result[name] = new Material(definition.Color, fallback.Emission, fallback.LightId, texture);
        }
        return result;
    }

    private static Material ResolveMaterial(Dictionary<string, Material> materialCache, string name, Material fallback) =>
        !string.IsNullOrWhiteSpace(name) && materialCache.TryGetValue(name, out Material? material) ? material : fallback;

    private static Dictionary<string, ObjMaterial> LoadMaterialLibrary(string filePath)
    {
        Dictionary<string, ObjMaterial> result = new(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath)) return result;

        string mtlDirectory = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
        string currentName = string.Empty;
        Vec3 currentColor = new(0.82, 0.82, 0.78);
        string? currentTexture = null;

        void Commit()
        {
            if (!string.IsNullOrWhiteSpace(currentName))
                result[currentName] = new ObjMaterial(currentColor, currentTexture);
        }

        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;
            string[] parts = SplitWhitespace(line);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "newmtl" when parts.Length >= 2:
                    Commit();
                    currentName = string.Join(' ', parts.Skip(1));
                    currentColor = new Vec3(0.82, 0.82, 0.78);
                    currentTexture = null;
                    break;
                case "Kd" when parts.Length >= 4:
                    currentColor = new Vec3(ParseLooseDouble(parts[1]), ParseLooseDouble(parts[2]), ParseLooseDouble(parts[3]));
                    break;
                case "map_Kd" when parts.Length >= 2:
                    currentTexture = ResolveTextureToken(mtlDirectory, parts.Skip(1));
                    break;
            }
        }
        Commit();
        return result;
    }

    private static string ResolveTextureToken(string mtlDirectory, IEnumerable<string> tokens)
    {
        List<string> useful = tokens.Where(t => !t.StartsWith('-')).ToList();
        string textureName = useful.Count == 0 ? string.Empty : useful[^1];
        return ResolveRelativePath(mtlDirectory, textureName);
    }

    private static IEnumerable<string> ParseMaterialLibraryNames(string text)
    {
        if (text.Length == 0) yield break;
        yield return text.Trim('"');
    }

    private static FaceVertex ParseFaceVertex(string token, int vertexCount, int textureCount, int lineNumber)
    {
        string[] parts = token.Split('/');
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            throw new InvalidDataException($"Invalid OBJ face vertex at line {lineNumber}.");
        int vertexIndex = ResolveIndex(ParseInt(parts[0], lineNumber), vertexCount, lineNumber, "vertex");
        int textureIndex = -1;
        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
            textureIndex = ResolveIndex(ParseInt(parts[1], lineNumber), textureCount, lineNumber, "texture coordinate");
        return new FaceVertex(vertexIndex, textureIndex);
    }

    private static int ResolveIndex(int objIndex, int count, int lineNumber, string kind)
    {
        int zeroBased = objIndex > 0 ? objIndex - 1 : count + objIndex;
        if (zeroBased < 0 || zeroBased >= count)
            throw new InvalidDataException($"OBJ {kind} index {objIndex} at line {lineNumber} is out of range.");
        return zeroBased;
    }

    private static Vec2 GetUv(List<Vec2> textureCoordinates, int index, Vec2 fallback) =>
        index >= 0 && index < textureCoordinates.Count ? textureCoordinates[index] : fallback;

    private static void GetBounds(List<Vec3> vertices, out Vec3 min, out Vec3 max)
    {
        min = vertices[0]; max = vertices[0];
        foreach (Vec3 vertex in vertices)
        {
            min = new Vec3(Math.Min(min.X, vertex.X), Math.Min(min.Y, vertex.Y), Math.Min(min.Z, vertex.Z));
            max = new Vec3(Math.Max(max.X, vertex.X), Math.Max(max.Y, vertex.Y), Math.Max(max.Z, vertex.Z));
        }
    }

    private static Vec3 Transform(Vec3 p, Vec3 sourceCenter, double scale, Vec3 offset) => (p - sourceCenter) * scale + offset;
    private static bool IsDegenerate(Vec3 a, Vec3 b, Vec3 c) => (b - a).Cross(c - a).Length() < 1e-10;

    private static string StripComment(string line)
    {
        int index = line.IndexOf('#');
        return index >= 0 ? line[..index] : line;
    }

    private static string[] SplitWhitespace(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static double ParseDouble(string text, int lineNumber) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : throw new InvalidDataException($"Invalid numeric value '{text}' at OBJ line {lineNumber}.");
    private static double ParseLooseDouble(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.0;
    private static int ParseInt(string text, int lineNumber) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw new InvalidDataException($"Invalid integer value '{text}' at OBJ line {lineNumber}.");
    private static string ResolveRelativePath(string baseDirectory, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path.Trim('"')));
    private static string SanitizeObjectName(string name, string fallback) => string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
}
