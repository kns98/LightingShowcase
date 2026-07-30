// -----------------------------------------------------------------------------
// File: Scene/PlySceneLoader.cs
// Purpose: PLY import.
//
// Imports common ASCII and binary PLY polygon meshes into the internal scene
// graph. The importer reads vertex positions and polygon faces, triangulates
// n-gons, and generates box-projection UVs because most PLY files do not carry
// portable texture coordinates. Binary support covers the common
// binary_little_endian and binary_big_endian PLY variants used by Blender,
// MeshLab, CloudCompare, 3D scan datasets, and many free model repositories.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Imports common ASCII and binary PLY assets into the internal scene graph.</summary>
public static class PlySceneLoader
{
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
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("PLY file path is required.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("PLY file was not found.", filePath);

        string fullPath = Path.GetFullPath(filePath);
        progress?.Invoke(new ObjLoadProgress("Reading PLY header", 5, 0, 0, 0));

        byte[] bytes = File.ReadAllBytes(fullPath);
        PlyHeader header = ReadHeader(bytes, out int dataOffset);
        if (header.VertexCount <= 0 || header.FaceCount <= 0)
            throw new InvalidDataException("PLY file must contain vertices and faces.");

        progress?.Invoke(new ObjLoadProgress("Reading PLY geometry", 10, 0, 0, 0));
        ReadGeometry(bytes, dataOffset, header, progress, out List<PlyVertex> vertices, out List<int[]> faces);

        if (vertices.Count == 0)
            throw new InvalidDataException("PLY file does not contain valid vertex positions.");
        if (faces.Count == 0)
            throw new InvalidDataException("PLY file does not contain valid faces.");

        GetBounds(vertices.Select(v => v.Position).ToList(), out Vec3 min, out Vec3 max);
        Vec3 size = max - min;
        double largestAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (largestAxis < 1e-8)
            throw new InvalidDataException("PLY model bounds are degenerate.");

        double scale = targetSize / largestAxis;
        Vec3 sourceCenter = (min + max) * 0.5;
        Vec3 center = targetCenter ?? new Vec3(0.0, 0.0, 3.45);
        double scaledMinY = (min.Y - sourceCenter.Y) * scale + center.Y;
        Vec3 offset = new(center.X, center.Y + (floorY - scaledMinY), center.Z);
        Aabb targetBounds = new(Transform(min, sourceCenter, scale, offset), Transform(max, sourceCenter, scale, offset));

        SceneObjectGroup group = scene.AddImportedGroup(Path.GetFileNameWithoutExtension(fullPath));
        int triangleCount = BuildFaces(faces, vertices, group, fallbackMaterial, sourceCenter, scale, offset, targetBounds, progress);
        if (triangleCount == 0)
            throw new InvalidDataException("PLY file did not produce any non-degenerate triangles.");

        group.RecalculatePivot();
        progress?.Invoke(new ObjLoadProgress("Building acceleration structure", 94, vertices.Count, faces.Count, triangleCount));
        scene.RebuildWorldGeometry();
        progress?.Invoke(new ObjLoadProgress("Done", 100, vertices.Count, faces.Count, triangleCount));
        return new ObjLoadResult(filePath, vertices.Count, faces.Count, triangleCount);
    }

    private enum PlyFormat
    {
        Ascii,
        BinaryLittleEndian,
        BinaryBigEndian
    }

    private sealed class PlyHeader
    {
        public PlyFormat Format { get; set; }
        public int VertexCount { get; set; }
        public int FaceCount { get; set; }
        public List<PlyElement> Elements { get; } = new();
    }

    private sealed class PlyElement
    {
        public string Name { get; }
        public int Count { get; }
        public List<PlyProperty> Properties { get; } = new();

        public PlyElement(string name, int count)
        {
            Name = name;
            Count = count;
        }
    }

    private sealed class PlyVertex
    {
        public Vec3 Position { get; }
        public Vec3? Color { get; }

        public PlyVertex(Vec3 position, Vec3? color)
        {
            Position = position;
            Color = color;
        }
    }

    private sealed class PlyProperty
    {
        public string Name { get; }
        public string Type { get; }
        public bool IsList { get; }
        public string CountType { get; }
        public string ItemType { get; }

        private PlyProperty(string name, string type, bool isList, string countType, string itemType)
        {
            Name = name;
            Type = type;
            IsList = isList;
            CountType = countType;
            ItemType = itemType;
        }

        public static PlyProperty Scalar(string type, string name) => new(name, type, false, string.Empty, string.Empty);
        public static PlyProperty List(string countType, string itemType, string name) => new(name, string.Empty, true, countType, itemType);
    }

    private static PlyHeader ReadHeader(byte[] bytes, out int dataOffset)
    {
        if (bytes.Length < 4)
            throw new InvalidDataException("File is too small to be a PLY file.");

        List<string> lines = new();
        int lineStart = 0;
        dataOffset = -1;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n')
                continue;

            int length = i - lineStart;
            if (length > 0 && bytes[i - 1] == (byte)'\r')
                length--;
            string line = Encoding.ASCII.GetString(bytes, lineStart, length);
            lines.Add(line);
            lineStart = i + 1;

            if (line.Trim().Equals("end_header", StringComparison.OrdinalIgnoreCase))
            {
                dataOffset = i + 1;
                break;
            }
        }

        if (dataOffset < 0)
            throw new InvalidDataException("PLY file is missing end_header.");
        if (lines.Count == 0 || !string.Equals(lines[0].Trim(), "ply", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File is not a PLY file.");

        PlyHeader header = new();
        PlyElement? currentElement = null;

        for (int lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            string trimmed = lines[lineIndex].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("comment ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("obj_info ", StringComparison.OrdinalIgnoreCase))
                continue;
            if (trimmed.Equals("end_header", StringComparison.OrdinalIgnoreCase))
                break;

            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            if (parts[0].Equals("format", StringComparison.OrdinalIgnoreCase) && parts.Length >= 2)
            {
                header.Format = parts[1].ToLowerInvariant() switch
                {
                    "ascii" => PlyFormat.Ascii,
                    "binary_little_endian" => PlyFormat.BinaryLittleEndian,
                    "binary_big_endian" => PlyFormat.BinaryBigEndian,
                    _ => throw new NotSupportedException($"Unsupported PLY format: {parts[1]}")
                };
            }
            else if (parts[0].Equals("element", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
            {
                currentElement = new PlyElement(parts[1], ParseInt(parts[2], "element count"));
                header.Elements.Add(currentElement);
                if (currentElement.Name.Equals("vertex", StringComparison.OrdinalIgnoreCase))
                    header.VertexCount = currentElement.Count;
                else if (currentElement.Name.Equals("face", StringComparison.OrdinalIgnoreCase))
                    header.FaceCount = currentElement.Count;
            }
            else if (parts[0].Equals("property", StringComparison.OrdinalIgnoreCase) && currentElement != null)
            {
                if (parts.Length >= 5 && parts[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateType(parts[2]);
                    ValidateType(parts[3]);
                    currentElement.Properties.Add(PlyProperty.List(parts[2], parts[3], parts[4]));
                }
                else if (parts.Length >= 3)
                {
                    ValidateType(parts[1]);
                    currentElement.Properties.Add(PlyProperty.Scalar(parts[1], parts[2]));
                }
            }
        }

        PlyElement? vertex = FindElement(header, "vertex");
        if (vertex == null)
            throw new InvalidDataException("PLY file is missing a vertex element.");
        if (FindProperty(vertex, "x") == null || FindProperty(vertex, "y") == null || FindProperty(vertex, "z") == null)
            throw new InvalidDataException("PLY vertex element must contain x, y, and z properties.");
        if (FindElement(header, "face") == null)
            throw new InvalidDataException("PLY file is missing a face element.");

        return header;
    }

    private static void ReadGeometry(
        byte[] bytes,
        int dataOffset,
        PlyHeader header,
        Action<ObjLoadProgress>? progress,
        out List<PlyVertex> vertices,
        out List<int[]> faces)
    {
        vertices = new List<PlyVertex>(header.VertexCount);
        faces = new List<int[]>(header.FaceCount);

        if (header.Format == PlyFormat.Ascii)
        {
            string data = Encoding.ASCII.GetString(bytes, dataOffset, bytes.Length - dataOffset);
            using StringReader reader = new(data);
            ReadAsciiElements(reader, header, vertices, faces, progress);
            return;
        }

        int offset = dataOffset;
        bool littleEndian = header.Format == PlyFormat.BinaryLittleEndian;
        ReadBinaryElements(bytes, ref offset, header, littleEndian, vertices, faces, progress);
    }

    private static void ReadAsciiElements(
        StringReader reader,
        PlyHeader header,
        List<PlyVertex> vertices,
        List<int[]> faces,
        Action<ObjLoadProgress>? progress)
    {
        foreach (PlyElement element in header.Elements)
        {
            if (element.Name.Equals("vertex", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < element.Count; i++)
                {
                    string line = reader.ReadLine() ?? throw new InvalidDataException("PLY file ended while reading vertices.");
                    vertices.Add(ReadAsciiVertex(line, element));
                    if ((i & 4095) == 0)
                    {
                        int percent = 10 + (int)(35.0 * i / Math.Max(1, element.Count));
                        progress?.Invoke(new ObjLoadProgress("Reading PLY vertices", percent, i, 0, 0));
                    }
                }
            }
            else if (element.Name.Equals("face", StringComparison.OrdinalIgnoreCase))
            {
                PlyProperty faceList = SelectFaceIndexProperty(element);
                for (int i = 0; i < element.Count; i++)
                {
                    string line = reader.ReadLine() ?? throw new InvalidDataException("PLY file ended while reading faces.");
                    int[] indices = ReadAsciiFace(line, element, faceList, vertices.Count);
                    if (indices.Length >= 3)
                        faces.Add(indices);
                    if ((i & 2047) == 0)
                    {
                        int percent = 45 + (int)(25.0 * i / Math.Max(1, element.Count));
                        progress?.Invoke(new ObjLoadProgress("Reading PLY faces", percent, vertices.Count, i, 0));
                    }
                }
            }
            else
            {
                for (int i = 0; i < element.Count; i++)
                {
                    if (reader.ReadLine() == null)
                        throw new InvalidDataException($"PLY file ended while skipping {element.Name} element data.");
                }
            }
        }
    }

    private static PlyVertex ReadAsciiVertex(string line, PlyElement element)
    {
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int tokenIndex = 0;
        double x = 0, y = 0, z = 0;
        double red = 0, green = 0, blue = 0;
        bool hasX = false, hasY = false, hasZ = false;
        bool hasRed = false, hasGreen = false, hasBlue = false;

        foreach (PlyProperty property in element.Properties)
        {
            if (property.IsList)
            {
                if (tokenIndex >= tokens.Length) throw new InvalidDataException("PLY vertex list property is missing its count.");
                int count = ParseInt(tokens[tokenIndex++], "vertex list count");
                tokenIndex += count;
            }
            else
            {
                if (tokenIndex >= tokens.Length) throw new InvalidDataException("PLY vertex line is shorter than its declared properties.");
                double value = ParseDouble(tokens[tokenIndex++]);
                if (property.Name.Equals("x", StringComparison.OrdinalIgnoreCase)) { x = value; hasX = true; }
                else if (property.Name.Equals("y", StringComparison.OrdinalIgnoreCase)) { y = value; hasY = true; }
                else if (IsRedProperty(property.Name)) { red = NormalizeColorComponent(value, property.Type); hasRed = true; }
                else if (IsGreenProperty(property.Name)) { green = NormalizeColorComponent(value, property.Type); hasGreen = true; }
                else if (IsBlueProperty(property.Name)) { blue = NormalizeColorComponent(value, property.Type); hasBlue = true; }
                else if (property.Name.Equals("z", StringComparison.OrdinalIgnoreCase)) { z = value; hasZ = true; }
            }
        }

        if (!hasX || !hasY || !hasZ)
            throw new InvalidDataException("PLY vertex record did not include x/y/z values.");
        Vec3? color = hasRed && hasGreen && hasBlue ? new Vec3(red, green, blue) : null;
        return new PlyVertex(new Vec3(x, y, z), color);
    }

    private static int[] ReadAsciiFace(string line, PlyElement element, PlyProperty faceList, int vertexCount)
    {
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int tokenIndex = 0;

        foreach (PlyProperty property in element.Properties)
        {
            if (property.IsList)
            {
                if (tokenIndex >= tokens.Length) throw new InvalidDataException("PLY face list property is missing its count.");
                int count = ParseInt(tokens[tokenIndex++], "face vertex count");
                if (tokenIndex + count > tokens.Length)
                    throw new InvalidDataException("PLY face line is shorter than its declared vertex count.");

                if (ReferenceEquals(property, faceList))
                {
                    int[] values = new int[count];
                    for (int i = 0; i < count; i++)
                        values[i] = ParseIndex(tokens[tokenIndex++], vertexCount);
                    return values;
                }

                tokenIndex += count;
            }
            else
            {
                if (tokenIndex >= tokens.Length) throw new InvalidDataException("PLY face line is shorter than its declared properties.");
                tokenIndex++;
            }
        }

        return Array.Empty<int>();
    }

    private static void ReadBinaryElements(
        byte[] bytes,
        ref int offset,
        PlyHeader header,
        bool littleEndian,
        List<PlyVertex> vertices,
        List<int[]> faces,
        Action<ObjLoadProgress>? progress)
    {
        foreach (PlyElement element in header.Elements)
        {
            if (element.Name.Equals("vertex", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < element.Count; i++)
                {
                    vertices.Add(ReadBinaryVertex(bytes, ref offset, element, littleEndian));
                    if ((i & 4095) == 0)
                    {
                        int percent = 10 + (int)(35.0 * i / Math.Max(1, element.Count));
                        progress?.Invoke(new ObjLoadProgress("Reading binary PLY vertices", percent, i, 0, 0));
                    }
                }
            }
            else if (element.Name.Equals("face", StringComparison.OrdinalIgnoreCase))
            {
                PlyProperty faceList = SelectFaceIndexProperty(element);
                for (int i = 0; i < element.Count; i++)
                {
                    int[] indices = ReadBinaryFace(bytes, ref offset, element, faceList, littleEndian, vertices.Count);
                    if (indices.Length >= 3)
                        faces.Add(indices);
                    if ((i & 2047) == 0)
                    {
                        int percent = 45 + (int)(25.0 * i / Math.Max(1, element.Count));
                        progress?.Invoke(new ObjLoadProgress("Reading binary PLY faces", percent, vertices.Count, i, 0));
                    }
                }
            }
            else
            {
                for (int i = 0; i < element.Count; i++)
                    SkipBinaryRecord(bytes, ref offset, element, littleEndian);
            }
        }
    }

    private static PlyVertex ReadBinaryVertex(byte[] bytes, ref int offset, PlyElement element, bool littleEndian)
    {
        double x = 0, y = 0, z = 0;
        double red = 0, green = 0, blue = 0;
        bool hasX = false, hasY = false, hasZ = false;
        bool hasRed = false, hasGreen = false, hasBlue = false;

        foreach (PlyProperty property in element.Properties)
        {
            if (property.IsList)
            {
                int count = ReadBinaryListCount(bytes, ref offset, property.CountType, littleEndian);
                for (int i = 0; i < count; i++)
                    SkipBinaryScalar(bytes, ref offset, property.ItemType);
            }
            else
            {
                double value = ReadBinaryDouble(bytes, ref offset, property.Type, littleEndian);
                if (property.Name.Equals("x", StringComparison.OrdinalIgnoreCase)) { x = value; hasX = true; }
                else if (property.Name.Equals("y", StringComparison.OrdinalIgnoreCase)) { y = value; hasY = true; }
                else if (IsRedProperty(property.Name)) { red = NormalizeColorComponent(value, property.Type); hasRed = true; }
                else if (IsGreenProperty(property.Name)) { green = NormalizeColorComponent(value, property.Type); hasGreen = true; }
                else if (IsBlueProperty(property.Name)) { blue = NormalizeColorComponent(value, property.Type); hasBlue = true; }
                else if (property.Name.Equals("z", StringComparison.OrdinalIgnoreCase)) { z = value; hasZ = true; }
            }
        }

        if (!hasX || !hasY || !hasZ)
            throw new InvalidDataException("PLY vertex record did not include x/y/z values.");
        Vec3? color = hasRed && hasGreen && hasBlue ? new Vec3(red, green, blue) : null;
        return new PlyVertex(new Vec3(x, y, z), color);
    }

    private static int[] ReadBinaryFace(byte[] bytes, ref int offset, PlyElement element, PlyProperty faceList, bool littleEndian, int vertexCount)
    {
        foreach (PlyProperty property in element.Properties)
        {
            if (property.IsList)
            {
                int count = ReadBinaryListCount(bytes, ref offset, property.CountType, littleEndian);
                if (ReferenceEquals(property, faceList))
                {
                    int[] values = new int[count];
                    for (int i = 0; i < count; i++)
                        values[i] = ReadBinaryIndex(bytes, ref offset, property.ItemType, littleEndian, vertexCount);
                    return values;
                }

                checked { offset += count * TypeSize(property.ItemType); }
                EnsureAvailable(bytes, offset, 0);
            }
            else
            {
                SkipBinaryScalar(bytes, ref offset, property.Type);
            }
        }

        return Array.Empty<int>();
    }

    private static void SkipBinaryRecord(byte[] bytes, ref int offset, PlyElement element, bool littleEndian)
    {
        foreach (PlyProperty property in element.Properties)
        {
            if (property.IsList)
            {
                int count = ReadBinaryListCount(bytes, ref offset, property.CountType, littleEndian);
                checked { offset += count * TypeSize(property.ItemType); }
                EnsureAvailable(bytes, offset, 0);
            }
            else
            {
                SkipBinaryScalar(bytes, ref offset, property.Type);
            }
        }
    }

    private static int BuildFaces(
        List<int[]> faces,
        List<PlyVertex> vertices,
        SceneObjectGroup group,
        Material material,
        Vec3 sourceCenter,
        double scale,
        Vec3 offset,
        Aabb targetBounds,
        Action<ObjLoadProgress>? progress)
    {
        int triangleCount = 0;
        for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            int[] face = faces[faceIndex];
            if (face.Length < 3) continue;

            Vec3 a = Transform(vertices[face[0]].Position, sourceCenter, scale, offset);
            for (int i = 1; i < face.Length - 1; i++)
            {
                Vec3 b = Transform(vertices[face[i]].Position, sourceCenter, scale, offset);
                Vec3 c = Transform(vertices[face[i + 1]].Position, sourceCenter, scale, offset);
                Vec3 normal = (b - a).Cross(c - a).Normalize();
                if (normal.Length() > 1e-10)
                {
                    Material triangleMaterial = MaterialForTriangleColor(material, vertices[face[0]].Color, vertices[face[i]].Color, vertices[face[i + 1]].Color);
                    group.AddTriangle(a, b, c, GenerateBoxUv(a, normal, targetBounds), GenerateBoxUv(b, normal, targetBounds), GenerateBoxUv(c, normal, targetBounds), triangleMaterial);
                    triangleCount++;
                }
            }

            if ((faceIndex & 2047) == 0)
            {
                int percent = 70 + (int)(20.0 * faceIndex / Math.Max(1, faces.Count));
                progress?.Invoke(new ObjLoadProgress("Building PLY faces", percent, vertices.Count, faceIndex, triangleCount));
            }
        }
        return triangleCount;
    }


    private static Material MaterialForTriangleColor(Material fallback, Vec3? a, Vec3? b, Vec3? c)
    {
        if (a == null || b == null || c == null)
            return fallback;

        Vec3 average = (a.Value + b.Value + c.Value) / 3.0;
        return new Material(average, fallback.Emission, fallback.LightId, fallback.Texture);
    }

    private static bool IsRedProperty(string name) =>
        name.Equals("red", StringComparison.OrdinalIgnoreCase) || name.Equals("r", StringComparison.OrdinalIgnoreCase) || name.Equals("diffuse_red", StringComparison.OrdinalIgnoreCase);

    private static bool IsGreenProperty(string name) =>
        name.Equals("green", StringComparison.OrdinalIgnoreCase) || name.Equals("g", StringComparison.OrdinalIgnoreCase) || name.Equals("diffuse_green", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlueProperty(string name) =>
        name.Equals("blue", StringComparison.OrdinalIgnoreCase) || name.Equals("b", StringComparison.OrdinalIgnoreCase) || name.Equals("diffuse_blue", StringComparison.OrdinalIgnoreCase);

    private static double NormalizeColorComponent(double value, string propertyType)
    {
        string normalized = NormalizeType(propertyType);
        double scaled = normalized is "uint8" or "int8" or "uint16" or "int16" or "uint32" or "int32" || value > 1.0
            ? value / 255.0
            : value;
        return Math.Clamp(scaled, 0.0, 1.0);
    }

    private static PlyElement? FindElement(PlyHeader header, string name)
    {
        foreach (PlyElement element in header.Elements)
        {
            if (element.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return element;
        }
        return null;
    }

    private static PlyProperty? FindProperty(PlyElement element, string name)
    {
        foreach (PlyProperty property in element.Properties)
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property;
        }
        return null;
    }

    private static PlyProperty SelectFaceIndexProperty(PlyElement faceElement)
    {
        PlyProperty? firstList = null;
        foreach (PlyProperty property in faceElement.Properties)
        {
            if (!property.IsList)
                continue;
            firstList ??= property;
            if (property.Name.Equals("vertex_indices", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("vertex_index", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("vertices", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("indices", StringComparison.OrdinalIgnoreCase))
                return property;
        }

        return firstList ?? throw new InvalidDataException("PLY face element must contain a list property for vertex indices.");
    }

    private static int ParseIndex(string value, int vertexCount)
    {
        int index = ParseInt(value, "face index");
        ValidateIndex(index, vertexCount);
        return index;
    }

    private static int ReadBinaryIndex(byte[] bytes, ref int offset, string type, bool littleEndian, int vertexCount)
    {
        int index = ReadBinaryInt(bytes, ref offset, type, littleEndian);
        ValidateIndex(index, vertexCount);
        return index;
    }

    private static void ValidateIndex(int index, int vertexCount)
    {
        if (index < 0 || index >= vertexCount)
            throw new InvalidDataException($"PLY face references vertex index {index}, but valid range is 0..{vertexCount - 1}.");
    }

    private static int ParseInt(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw new InvalidDataException($"Invalid PLY {name}: {value}");
        return result;
    }

    private static double ParseDouble(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            throw new InvalidDataException($"Invalid PLY number: {value}");
        return result;
    }

    private static void ValidateType(string type) => _ = TypeSize(type);

    private static int TypeSize(string type) => NormalizeType(type) switch
    {
        "int8" => 1,
        "uint8" => 1,
        "int16" => 2,
        "uint16" => 2,
        "int32" => 4,
        "uint32" => 4,
        "float32" => 4,
        "float64" => 8,
        _ => throw new NotSupportedException($"Unsupported PLY property type: {type}")
    };

    private static string NormalizeType(string type) => type.ToLowerInvariant() switch
    {
        "char" => "int8",
        "int8" => "int8",
        "uchar" => "uint8",
        "uint8" => "uint8",
        "short" => "int16",
        "int16" => "int16",
        "ushort" => "uint16",
        "uint16" => "uint16",
        "int" => "int32",
        "int32" => "int32",
        "uint" => "uint32",
        "uint32" => "uint32",
        "float" => "float32",
        "float32" => "float32",
        "double" => "float64",
        "float64" => "float64",
        _ => type.ToLowerInvariant()
    };

    private static void SkipBinaryScalar(byte[] bytes, ref int offset, string type)
    {
        offset += TypeSize(type);
        EnsureAvailable(bytes, offset, 0);
    }


    private static int ReadBinaryListCount(byte[] bytes, ref int offset, string type, bool littleEndian)
    {
        int count = ReadBinaryInt(bytes, ref offset, type, littleEndian);
        if (count < 0)
            throw new InvalidDataException($"PLY list count cannot be negative: {count}");
        return count;
    }

    private static int ReadBinaryInt(byte[] bytes, ref int offset, string type, bool littleEndian)
    {
        double value = ReadBinaryDouble(bytes, ref offset, type, littleEndian);
        if (value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException($"PLY integer value is outside Int32 range: {value}");
        return (int)value;
    }

    private static double ReadBinaryDouble(byte[] bytes, ref int offset, string type, bool littleEndian)
    {
        string normalized = NormalizeType(type);
        switch (normalized)
        {
            case "int8":
                EnsureAvailable(bytes, offset, 1);
                return unchecked((sbyte)bytes[offset++]);
            case "uint8":
                EnsureAvailable(bytes, offset, 1);
                return bytes[offset++];
            case "int16":
                return unchecked((short)ReadUInt16(bytes, ref offset, littleEndian));
            case "uint16":
                return ReadUInt16(bytes, ref offset, littleEndian);
            case "int32":
                return unchecked((int)ReadUInt32(bytes, ref offset, littleEndian));
            case "uint32":
                return ReadUInt32(bytes, ref offset, littleEndian);
            case "float32":
                return ReadSingle(bytes, ref offset, littleEndian);
            case "float64":
                return ReadDouble(bytes, ref offset, littleEndian);
            default:
                throw new NotSupportedException($"Unsupported PLY property type: {type}");
        }
    }

    private static ushort ReadUInt16(byte[] bytes, ref int offset, bool littleEndian)
    {
        EnsureAvailable(bytes, offset, 2);
        ushort value = littleEndian
            ? (ushort)(bytes[offset] | (bytes[offset + 1] << 8))
            : (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(byte[] bytes, ref int offset, bool littleEndian)
    {
        EnsureAvailable(bytes, offset, 4);
        uint value = littleEndian
            ? (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24))
            : (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
        offset += 4;
        return value;
    }

    private static float ReadSingle(byte[] bytes, ref int offset, bool littleEndian)
    {
        uint raw = ReadUInt32(bytes, ref offset, littleEndian);
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    private static double ReadDouble(byte[] bytes, ref int offset, bool littleEndian)
    {
        EnsureAvailable(bytes, offset, 8);
        ulong raw;
        if (littleEndian)
        {
            raw = bytes[offset]
                | ((ulong)bytes[offset + 1] << 8)
                | ((ulong)bytes[offset + 2] << 16)
                | ((ulong)bytes[offset + 3] << 24)
                | ((ulong)bytes[offset + 4] << 32)
                | ((ulong)bytes[offset + 5] << 40)
                | ((ulong)bytes[offset + 6] << 48)
                | ((ulong)bytes[offset + 7] << 56);
        }
        else
        {
            raw = ((ulong)bytes[offset] << 56)
                | ((ulong)bytes[offset + 1] << 48)
                | ((ulong)bytes[offset + 2] << 40)
                | ((ulong)bytes[offset + 3] << 32)
                | ((ulong)bytes[offset + 4] << 24)
                | ((ulong)bytes[offset + 5] << 16)
                | ((ulong)bytes[offset + 6] << 8)
                | bytes[offset + 7];
        }
        offset += 8;
        return BitConverter.Int64BitsToDouble(unchecked((long)raw));
    }

    private static void EnsureAvailable(byte[] bytes, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > bytes.Length)
            throw new InvalidDataException("PLY file ended before all declared binary data was read.");
    }

    private static void GetBounds(List<Vec3> vertices, out Vec3 min, out Vec3 max)
    {
        min = vertices[0];
        max = vertices[0];
        foreach (Vec3 vertex in vertices)
        {
            min = new Vec3(Math.Min(min.X, vertex.X), Math.Min(min.Y, vertex.Y), Math.Min(min.Z, vertex.Z));
            max = new Vec3(Math.Max(max.X, vertex.X), Math.Max(max.Y, vertex.Y), Math.Max(max.Z, vertex.Z));
        }
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
