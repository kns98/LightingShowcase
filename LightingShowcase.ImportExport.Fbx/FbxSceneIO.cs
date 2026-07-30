// -----------------------------------------------------------------------------
// File: Scene/FbxSceneIO.cs
// Purpose: Lightweight FBX import/export for ASCII and binary mesh geometry.
//
// The importer reads common FBX mesh Geometry nodes containing Vertices and
// PolygonVertexIndex arrays. It supports ASCII FBX and binary FBX 7.x files with
// little-endian scalar/array properties. Binary array payloads may be stored
// plain or zlib-compressed. The exporter can write either ASCII FBX or binary FBX
// 7.4 mesh geometry so the files can round-trip through common DCC tools without
// requiring Autodesk FBX SDK redistribution.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

public static class FbxSceneIO
{
    private static readonly byte[] BinaryHeader = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1A\0");
    private const uint BinaryVersion = 7400;

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
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("FBX file path is required.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("FBX file was not found.", filePath);

        FbxMeshData mesh = IsBinaryFbx(filePath)
            ? LoadBinaryMesh(filePath)
            : LoadAsciiMesh(filePath);

        return AddMeshToScene(scene, filePath, mesh.Vertices, mesh.PolygonVertexIndices, mesh.PolygonVertexColors, fallbackMaterial, targetSize, targetCenter, floorY, progress);
    }

    public static void Save(Scene scene, string filePath, string? variant = null)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save path is required.", nameof(filePath));

        if (string.Equals(variant, "ascii", StringComparison.OrdinalIgnoreCase))
            SaveAscii(scene, filePath);
        else
            SaveBinary(scene, filePath);
    }

    public static void SaveAscii(Scene scene, string filePath)
    {
        string fullPath = PrepareSavePath(filePath);
        List<Triangle> triangles = scene.ObjectGroups.SelectMany(g => g.BuildWorldTriangles()).ToList();

        StringBuilder vertices = new();
        StringBuilder indices = new();
        StringBuilder colors = new();
        for (int i = 0; i < triangles.Count; i++)
        {
            Triangle tri = triangles[i];
            if (i > 0)
            {
                vertices.Append(',');
                indices.Append(',');
            }

            int baseIndex = i * 3;
            AppendVertex(vertices, tri.A); vertices.Append(',');
            AppendVertex(vertices, tri.B); vertices.Append(',');
            AppendVertex(vertices, tri.C);
            AppendColor(colors, tri.Material.Color); colors.Append(',');
            AppendColor(colors, tri.Material.Color); colors.Append(',');
            AppendColor(colors, tri.Material.Color);
            indices.Append(FormattableString.Invariant($"{baseIndex},{baseIndex + 1},{~(baseIndex + 2)}"));
        }

        string content = $$"""
; FBX 7.4.0 project file
; Exported by LightingShowcase
FBXHeaderExtension:  {
    FBXHeaderVersion: 1003
    FBXVersion: 7400
}
Objects:  {
    Geometry: 1000, "Geometry::LightingShowcaseScene", "Mesh" {
        Vertices: *{{triangles.Count * 9}} {
            a: {{vertices}}
        }
        PolygonVertexIndex: *{{triangles.Count * 3}} {
            a: {{indices}}
        }
        LayerElementColor: 0 {
            Version: 101
            Name: ""
            MappingInformationType: "ByPolygonVertex"
            ReferenceInformationType: "Direct"
            Colors: *{{triangles.Count * 12}} {
                a: {{colors}}
            }
        }
    }
}
""";
        File.WriteAllText(fullPath, content, Encoding.UTF8);
    }

    public static void SaveBinary(Scene scene, string filePath)
    {
        string fullPath = PrepareSavePath(filePath);
        List<Triangle> triangles = scene.ObjectGroups.SelectMany(g => g.BuildWorldTriangles()).ToList();
        double[] vertices = new double[triangles.Count * 9];
        int[] indices = new int[triangles.Count * 3];
        double[] colors = new double[triangles.Count * 12];

        for (int i = 0; i < triangles.Count; i++)
        {
            Triangle tri = triangles[i];
            int v = i * 9;
            vertices[v + 0] = tri.A.X; vertices[v + 1] = tri.A.Y; vertices[v + 2] = tri.A.Z;
            vertices[v + 3] = tri.B.X; vertices[v + 4] = tri.B.Y; vertices[v + 5] = tri.B.Z;
            vertices[v + 6] = tri.C.X; vertices[v + 7] = tri.C.Y; vertices[v + 8] = tri.C.Z;

            int baseIndex = i * 3;
            int p = i * 3;
            indices[p + 0] = baseIndex;
            indices[p + 1] = baseIndex + 1;
            indices[p + 2] = ~(baseIndex + 2);

            int c = i * 12;
            WriteColorArrayEntry(colors, c + 0, tri.Material.Color);
            WriteColorArrayEntry(colors, c + 4, tri.Material.Color);
            WriteColorArrayEntry(colors, c + 8, tri.Material.Color);
        }

        using FileStream stream = File.Create(fullPath);
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(BinaryHeader);
        writer.Write(BinaryVersion);

        WriteNode(writer, "FBXHeaderExtension", Array.Empty<Action<BinaryWriter>>(), () =>
        {
            WriteNode(writer, "FBXHeaderVersion", new Action<BinaryWriter>[] { w => WriteIntProperty(w, 1003) }, null);
            WriteNode(writer, "FBXVersion", new Action<BinaryWriter>[] { w => WriteIntProperty(w, (int)BinaryVersion) }, null);
        });

        WriteNode(writer, "Objects", Array.Empty<Action<BinaryWriter>>(), () =>
        {
            WriteNode(writer, "Geometry", new Action<BinaryWriter>[]
            {
                w => WriteLongProperty(w, 1000),
                w => WriteStringProperty(w, "Geometry::LightingShowcaseScene"),
                w => WriteStringProperty(w, "Mesh")
            }, () =>
            {
                WriteNode(writer, "Vertices", new Action<BinaryWriter>[] { w => WriteDoubleArrayProperty(w, vertices) }, null);
                WriteNode(writer, "PolygonVertexIndex", new Action<BinaryWriter>[] { w => WriteIntArrayProperty(w, indices) }, null);
                WriteNode(writer, "LayerElementColor", new Action<BinaryWriter>[] { w => WriteIntProperty(w, 0) }, () =>
                {
                    WriteNode(writer, "Version", new Action<BinaryWriter>[] { w => WriteIntProperty(w, 101) }, null);
                    WriteNode(writer, "Name", new Action<BinaryWriter>[] { w => WriteStringProperty(w, string.Empty) }, null);
                    WriteNode(writer, "MappingInformationType", new Action<BinaryWriter>[] { w => WriteStringProperty(w, "ByPolygonVertex") }, null);
                    WriteNode(writer, "ReferenceInformationType", new Action<BinaryWriter>[] { w => WriteStringProperty(w, "Direct") }, null);
                    WriteNode(writer, "Colors", new Action<BinaryWriter>[] { w => WriteDoubleArrayProperty(w, colors) }, null);
                });
            });
        });

        WriteNullRecord(writer);
    }

    private static ObjLoadResult AddMeshToScene(
        Scene scene,
        string filePath,
        List<Vec3> sourceVertices,
        List<int> polygonIndices,
        List<Vec3>? polygonVertexColors,
        Material fallbackMaterial,
        double targetSize,
        Vec3? targetCenter,
        double floorY,
        Action<ObjLoadProgress>? progress)
    {
        if (sourceVertices.Count == 0 || polygonIndices.Count == 0)
            throw new InvalidDataException("FBX file does not contain a readable mesh with Vertices and PolygonVertexIndex arrays.");

        GetBounds(sourceVertices, out Vec3 min, out Vec3 max);
        Vec3 size = max - min;
        double largestAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (largestAxis < 1e-8)
            throw new InvalidDataException("FBX model bounds are degenerate.");

        double scale = targetSize / largestAxis;
        Vec3 sourceCenter = (min + max) * 0.5;
        Vec3 center = targetCenter ?? new Vec3(0.0, 0.0, 3.45);
        double scaledMinY = (min.Y - sourceCenter.Y) * scale + center.Y;
        Vec3 offset = new(center.X, center.Y + (floorY - scaledMinY), center.Z);

        Vec3 Transform(Vec3 v) => (v - sourceCenter) * scale + offset;

        SceneObjectGroup group = scene.AddImportedGroup(Path.GetFileNameWithoutExtension(filePath));
        int faceCount = 0;
        int triangleCount = 0;
        List<int> face = new();
        List<Vec3> faceColors = new();
        int polygonVertexCursor = 0;
        foreach (int rawIndex in polygonIndices)
        {
            int index = rawIndex < 0 ? ~rawIndex : rawIndex;
            if (index < 0 || index >= sourceVertices.Count)
                throw new InvalidDataException($"FBX polygon references vertex {index}, but only {sourceVertices.Count} vertices exist.");

            face.Add(index);
            if (polygonVertexColors != null && polygonVertexCursor < polygonVertexColors.Count)
                faceColors.Add(polygonVertexColors[polygonVertexCursor]);
            polygonVertexCursor++;

            if (rawIndex < 0)
            {
                triangleCount += AddPolygon(group, sourceVertices, face, faceColors.Count == face.Count ? faceColors : null, Transform, fallbackMaterial);
                faceCount++;
                if (faceCount % 1000 == 0)
                    progress?.Invoke(new ObjLoadProgress("Reading FBX polygons", Math.Min(90, 20 + faceCount / 100), sourceVertices.Count, faceCount, triangleCount));
                face.Clear();
                faceColors.Clear();
            }
        }

        if (face.Count > 0)
        {
            triangleCount += AddPolygon(group, sourceVertices, face, faceColors.Count == face.Count ? faceColors : null, Transform, fallbackMaterial);
            faceCount++;
        }

        group.RecalculatePivot();
        scene.RebuildWorldGeometry();
        progress?.Invoke(new ObjLoadProgress("Done", 100, sourceVertices.Count, faceCount, triangleCount));
        return new ObjLoadResult(filePath, sourceVertices.Count, faceCount, triangleCount);
    }

    private static FbxMeshData LoadAsciiMesh(string filePath)
    {
        string text = File.ReadAllText(filePath);
        return new FbxMeshData(ParseAsciiVertices(text), ParseAsciiPolygonVertexIndices(text), ParseAsciiPolygonVertexColors(text));
    }

    private static FbxMeshData LoadBinaryMesh(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
        byte[] header = reader.ReadBytes(BinaryHeader.Length);
        if (!header.SequenceEqual(BinaryHeader))
            throw new InvalidDataException("File does not use the expected binary FBX header.");

        uint version = reader.ReadUInt32();
        bool wideOffsets = version >= 7500;
        List<Vec3>? vertices = null;
        List<int>? indices = null;
        List<Vec3>? polygonVertexColors = null;

        while (stream.Position < stream.Length)
        {
            FbxBinaryNode? node = ReadBinaryNode(reader, wideOffsets);
            if (node == null) break;
            FindMeshArrays(node, ref vertices, ref indices, ref polygonVertexColors);
            if (vertices != null && indices != null) break;
        }

        if (vertices == null || indices == null)
            throw new InvalidDataException("Binary FBX file does not contain readable Vertices and PolygonVertexIndex arrays.");

        return new FbxMeshData(vertices, indices, polygonVertexColors);
    }

    private static void FindMeshArrays(FbxBinaryNode node, ref List<Vec3>? vertices, ref List<int>? indices, ref List<Vec3>? polygonVertexColors)
    {
        if (node.Name.Equals("Vertices", StringComparison.OrdinalIgnoreCase) && node.Properties.FirstOrDefault() is double[] doubleArray)
            vertices = ConvertVertices(doubleArray);
        else if (node.Name.Equals("PolygonVertexIndex", StringComparison.OrdinalIgnoreCase) && node.Properties.FirstOrDefault() is int[] intArray)
            indices = intArray.ToList();
        else if (node.Name.Equals("PolygonVertexIndex", StringComparison.OrdinalIgnoreCase) && node.Properties.FirstOrDefault() is long[] longArray)
            indices = longArray.Select(v => checked((int)v)).ToList();
        else if (node.Name.Equals("Colors", StringComparison.OrdinalIgnoreCase) && node.Properties.FirstOrDefault() is double[] colorArray)
            polygonVertexColors = ConvertColors(colorArray);
        else if (node.Name.Equals("Colors", StringComparison.OrdinalIgnoreCase) && node.Properties.FirstOrDefault() is float[] colorFloatArray)
            polygonVertexColors = ConvertColors(colorFloatArray.Select(v => (double)v).ToArray());

        foreach (FbxBinaryNode child in node.Children)
        {
            if (vertices != null && indices != null && polygonVertexColors != null) return;
            FindMeshArrays(child, ref vertices, ref indices, ref polygonVertexColors);
        }
    }

    private static FbxBinaryNode? ReadBinaryNode(BinaryReader reader, bool wideOffsets)
    {
        long nodeStart = reader.BaseStream.Position;
        long endOffset = wideOffsets ? checked((long)reader.ReadUInt64()) : reader.ReadUInt32();
        long propertyCount = wideOffsets ? checked((long)reader.ReadUInt64()) : reader.ReadUInt32();
        long propertyListLength = wideOffsets ? checked((long)reader.ReadUInt64()) : reader.ReadUInt32();
        byte nameLength = reader.ReadByte();

        if (endOffset == 0 && propertyCount == 0 && propertyListLength == 0 && nameLength == 0)
            return null;
        if (endOffset <= nodeStart || endOffset > reader.BaseStream.Length)
            throw new InvalidDataException("Binary FBX node offsets are invalid or unsupported.");

        string name = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));
        List<object> properties = new();
        for (long i = 0; i < propertyCount; i++)
            properties.Add(ReadBinaryProperty(reader));

        List<FbxBinaryNode> children = new();
        while (reader.BaseStream.Position < endOffset)
        {
            if (IsNullRecordAhead(reader, wideOffsets))
            {
                reader.BaseStream.Position += wideOffsets ? 25 : 13;
                break;
            }

            FbxBinaryNode? child = ReadBinaryNode(reader, wideOffsets);
            if (child == null) break;
            children.Add(child);
        }

        reader.BaseStream.Position = endOffset;
        return new FbxBinaryNode(name, properties, children);
    }

    private static object ReadBinaryProperty(BinaryReader reader)
    {
        char type = (char)reader.ReadByte();
        return type switch
        {
            'C' => reader.ReadByte() != 0,
            'Y' => reader.ReadInt16(),
            'I' => reader.ReadInt32(),
            'L' => reader.ReadInt64(),
            'F' => reader.ReadSingle(),
            'D' => reader.ReadDouble(),
            'S' => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())),
            'R' => reader.ReadBytes(reader.ReadInt32()),
            'd' => ReadArrayProperty(reader, 8, r => r.ReadDouble()),
            'f' => ReadArrayProperty(reader, 4, r => r.ReadSingle()),
            'i' => ReadArrayProperty(reader, 4, r => r.ReadInt32()),
            'l' => ReadArrayProperty(reader, 8, r => r.ReadInt64()),
            'b' => ReadArrayProperty(reader, 1, r => r.ReadByte() != 0),
            'c' => ReadArrayProperty(reader, 1, r => r.ReadByte() != 0),
            _ => throw new InvalidDataException($"Unsupported binary FBX property type '{type}'.")
        };
    }

    private static T[] ReadArrayProperty<T>(BinaryReader reader, int elementSize, Func<BinaryReader, T> readValue)
    {
        int count = reader.ReadInt32();
        int encoding = reader.ReadInt32();
        int encodedLength = reader.ReadInt32();
        byte[] bytes = reader.ReadBytes(encodedLength);
        Stream payload = new MemoryStream(bytes, writable: false);
        if (encoding == 1)
            payload = new ZLibStream(payload, CompressionMode.Decompress);
        else if (encoding != 0)
            throw new InvalidDataException($"Unsupported binary FBX array encoding {encoding}.");

        using (payload)
        using (BinaryReader arrayReader = new(payload, Encoding.UTF8, leaveOpen: false))
        {
            T[] result = new T[count];
            for (int i = 0; i < count; i++)
                result[i] = readValue(arrayReader);
            return result;
        }
    }

    private static bool IsNullRecordAhead(BinaryReader reader, bool wideOffsets)
    {
        int length = wideOffsets ? 25 : 13;
        if (reader.BaseStream.Position + length > reader.BaseStream.Length)
            return false;

        long position = reader.BaseStream.Position;
        byte[] bytes = reader.ReadBytes(length);
        reader.BaseStream.Position = position;
        return bytes.All(b => b == 0);
    }

    private static bool IsBinaryFbx(string filePath)
    {
        byte[] prefix = File.ReadAllBytes(filePath).Take(BinaryHeader.Length).ToArray();
        return prefix.SequenceEqual(BinaryHeader);
    }

    private static List<Vec3> ParseAsciiVertices(string text)
    {
        string array = ExtractArray(text, "Vertices");
        List<double> values = ParseDoubleArray(array);
        return ConvertVertices(values.ToArray());
    }

    private static List<int> ParseAsciiPolygonVertexIndices(string text)
    {
        string array = ExtractArray(text, "PolygonVertexIndex");
        return Regex.Matches(array, @"[-+]?\d+").Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
    }

    private static List<Vec3>? ParseAsciiPolygonVertexColors(string text)
    {
        string? array = TryExtractArray(text, "Colors");
        if (array == null) return null;
        return ConvertColors(ParseDoubleArray(array));
    }

    private static List<Vec3> ConvertVertices(IReadOnlyList<double> values)
    {
        List<Vec3> vertices = new(values.Count / 3);
        for (int i = 0; i + 2 < values.Count; i += 3)
            vertices.Add(new Vec3(values[i], values[i + 1], values[i + 2]));
        return vertices;
    }

    private static List<Vec3> ConvertColors(IReadOnlyList<double> values)
    {
        List<Vec3> colors = new(values.Count / 4);
        for (int i = 0; i + 2 < values.Count; i += 4)
            colors.Add(new Vec3(Math.Clamp(values[i], 0.0, 1.0), Math.Clamp(values[i + 1], 0.0, 1.0), Math.Clamp(values[i + 2], 0.0, 1.0)));
        return colors;
    }

    private static string? TryExtractArray(string text, string name)
    {
        Match header = Regex.Match(text, name + @"\s*:\s*\*?\d*\s*\{\s*a\s*:", RegexOptions.IgnoreCase);
        if (!header.Success) return null;

        int start = header.Index + header.Length;
        int end = text.IndexOf('}', start);
        return end < 0 ? null : text[start..end];
    }

    private static string ExtractArray(string text, string name)
    {
        Match header = Regex.Match(text, name + @"\s*:\s*\*?\d*\s*\{\s*a\s*:", RegexOptions.IgnoreCase);
        if (!header.Success)
            throw new InvalidDataException($"FBX {name} array was not found.");

        int start = header.Index + header.Length;
        int end = text.IndexOf('}', start);
        if (end < 0)
            throw new InvalidDataException($"FBX {name} array is not closed.");

        return text[start..end];
    }

    private static List<double> ParseDoubleArray(string array) =>
        Regex.Matches(array, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToList();

    private static int AddPolygon(SceneObjectGroup group, List<Vec3> vertices, List<int> face, List<Vec3>? faceColors, Func<Vec3, Vec3> transform, Material material)
    {
        if (face.Count < 3) return 0;
        Vec3 a = transform(vertices[face[0]]);
        int count = 0;
        for (int i = 1; i < face.Count - 1; i++)
        {
            Vec3 b = transform(vertices[face[i]]);
            Vec3 c = transform(vertices[face[i + 1]]);
            Material triangleMaterial = material;
            if (faceColors != null && faceColors.Count == face.Count)
            {
                Vec3 averageColor = (faceColors[0] + faceColors[i] + faceColors[i + 1]) / 3.0;
                triangleMaterial = new Material(averageColor, material.Emission, material.LightId, material.Texture);
            }
            group.AddTriangle(a, b, c, triangleMaterial);
            count++;
        }
        return count;
    }

    private static void GetBounds(List<Vec3> vertices, out Vec3 min, out Vec3 max)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        foreach (Vec3 v in vertices)
        {
            minX = Math.Min(minX, v.X); minY = Math.Min(minY, v.Y); minZ = Math.Min(minZ, v.Z);
            maxX = Math.Max(maxX, v.X); maxY = Math.Max(maxY, v.Y); maxZ = Math.Max(maxZ, v.Z);
        }
        min = new Vec3(minX, minY, minZ);
        max = new Vec3(maxX, maxY, maxZ);
    }

    private static string PrepareSavePath(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        return fullPath;
    }

    private static void WriteNode(BinaryWriter writer, string name, IReadOnlyList<Action<BinaryWriter>> writeProperties, Action? writeChildren)
    {
        long nodeStart = writer.BaseStream.Position;
        writer.Write(0u);
        writer.Write((uint)writeProperties.Count);
        long propertyLengthPosition = writer.BaseStream.Position;
        writer.Write(0u);
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        if (nameBytes.Length > byte.MaxValue)
            throw new InvalidDataException($"FBX node name is too long: {name}");
        writer.Write((byte)nameBytes.Length);
        writer.Write(nameBytes);

        long propertyStart = writer.BaseStream.Position;
        foreach (Action<BinaryWriter> writeProperty in writeProperties)
            writeProperty(writer);
        long propertyEnd = writer.BaseStream.Position;

        if (writeChildren != null)
        {
            writeChildren();
            WriteNullRecord(writer);
        }

        long endOffset = writer.BaseStream.Position;
        if (endOffset > uint.MaxValue)
            throw new InvalidDataException("Binary FBX export exceeded the FBX 7.4 32-bit node offset limit.");

        writer.BaseStream.Position = nodeStart;
        writer.Write((uint)endOffset);
        writer.BaseStream.Position = propertyLengthPosition;
        writer.Write((uint)(propertyEnd - propertyStart));
        writer.BaseStream.Position = endOffset;
    }

    private static void WriteNullRecord(BinaryWriter writer) => writer.Write(new byte[13]);

    private static void WriteIntProperty(BinaryWriter writer, int value)
    {
        writer.Write((byte)'I');
        writer.Write(value);
    }

    private static void WriteLongProperty(BinaryWriter writer, long value)
    {
        writer.Write((byte)'L');
        writer.Write(value);
    }

    private static void WriteStringProperty(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((byte)'S');
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteDoubleArrayProperty(BinaryWriter writer, double[] values)
    {
        writer.Write((byte)'d');
        writer.Write(values.Length);
        writer.Write(0);
        writer.Write(checked(values.Length * sizeof(double)));
        foreach (double value in values)
            writer.Write(value);
    }

    private static void WriteIntArrayProperty(BinaryWriter writer, int[] values)
    {
        writer.Write((byte)'i');
        writer.Write(values.Length);
        writer.Write(0);
        writer.Write(checked(values.Length * sizeof(int)));
        foreach (int value in values)
            writer.Write(value);
    }

    private static void AppendVertex(StringBuilder builder, Vec3 value) =>
        builder.Append(FormattableString.Invariant($"{value.X:G17},{value.Y:G17},{value.Z:G17}"));

    private static void AppendColor(StringBuilder builder, Vec3 value) =>
        builder.Append(FormattableString.Invariant($"{Math.Clamp(value.X, 0.0, 1.0):G17},{Math.Clamp(value.Y, 0.0, 1.0):G17},{Math.Clamp(value.Z, 0.0, 1.0):G17},1"));

    private static void WriteColorArrayEntry(double[] values, int offset, Vec3 color)
    {
        values[offset + 0] = Math.Clamp(color.X, 0.0, 1.0);
        values[offset + 1] = Math.Clamp(color.Y, 0.0, 1.0);
        values[offset + 2] = Math.Clamp(color.Z, 0.0, 1.0);
        values[offset + 3] = 1.0;
    }

    private sealed record FbxMeshData(List<Vec3> Vertices, List<int> PolygonVertexIndices, List<Vec3>? PolygonVertexColors);
    private sealed record FbxBinaryNode(string Name, List<object> Properties, List<FbxBinaryNode> Children);
}
