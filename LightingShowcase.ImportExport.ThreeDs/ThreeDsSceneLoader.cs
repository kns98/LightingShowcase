// -----------------------------------------------------------------------------
// File: Scene/ThreeDsSceneLoader.cs
// Purpose: Autodesk 3DS import.
//
// Imports the legacy .3ds interchange format used by old 3D Studio / 3ds Max
// model libraries. Native .max files are proprietary scene files and cannot be
// read safely without Autodesk 3ds Max or an Autodesk SDK/export step, but many
// free "3ds Max" asset sites provide .3ds files. This loader covers the common
// static mesh subset: object meshes, vertices, triangle faces, UVs, diffuse
// material colors, and diffuse texture filenames.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Imports common legacy Autodesk 3DS static mesh files into the internal scene graph.</summary>
public static class ThreeDsSceneLoader
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
    private const ushort ColorFloat = 0x0010;
    private const ushort Color24 = 0x0011;
    private const ushort LinColor24 = 0x0012;
    private const ushort LinColorFloat = 0x0013;

    private sealed class ThreeDsDocument
    {
        public List<ThreeDsMesh> Meshes { get; } = new();
        public Dictionary<string, ThreeDsMaterialInfo> Materials { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ThreeDsMesh
    {
        public string Name { get; }
        public List<Vec3> Vertices { get; } = new();
        public List<Vec2> Uvs { get; } = new();
        public List<ThreeDsFace> Faces { get; } = new();

        public ThreeDsMesh(string name)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "3DS Object" : name;
        }
    }

    private sealed class ThreeDsFace
    {
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public string? MaterialName { get; set; }

        public ThreeDsFace(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    private sealed class ThreeDsMaterialInfo
    {
        public string Name { get; set; } = "Material";
        public Vec3 Diffuse { get; set; } = new(0.82, 0.82, 0.82);
        public string? TextureFileName { get; set; }
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
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("3DS file path is required.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("3DS file was not found.", filePath);

        string fullPath = Path.GetFullPath(filePath);
        progress?.Invoke(new ObjLoadProgress("Reading 3DS", 5, 0, 0, 0));
        ThreeDsDocument document = ReadDocument(fullPath, progress);

        int vertexCount = 0;
        int faceCount = 0;
        foreach (ThreeDsMesh mesh in document.Meshes)
        {
            vertexCount += mesh.Vertices.Count;
            faceCount += mesh.Faces.Count;
        }

        if (vertexCount == 0 || faceCount == 0)
            throw new InvalidDataException("3DS file does not contain supported triangle mesh geometry.");

        GetBounds(document.Meshes, out Vec3 min, out Vec3 max);
        Vec3 size = max - min;
        double largestAxis = Math.Max(size.X, Math.Max(size.Y, size.Z));
        if (largestAxis < 1e-8)
            throw new InvalidDataException("3DS model bounds are degenerate.");

        double scale = targetSize / largestAxis;
        Vec3 sourceCenter = (min + max) * 0.5;
        Vec3 center = targetCenter ?? new Vec3(0.0, 0.0, 3.45);
        double scaledMinY = (min.Y - sourceCenter.Y) * scale + center.Y;
        Vec3 offset = new(center.X, center.Y + (floorY - scaledMinY), center.Z);
        Aabb targetBounds = new(Transform(min, sourceCenter, scale, offset), Transform(max, sourceCenter, scale, offset));

        Dictionary<string, Material> materialCache = BuildMaterials(document.Materials, fullPath, fallbackMaterial);
        int triangleCount = BuildScene(scene, document.Meshes, materialCache, fallbackMaterial, sourceCenter, scale, offset, targetBounds, progress);
        if (triangleCount == 0)
            throw new InvalidDataException("3DS file did not produce any non-degenerate triangles.");

        progress?.Invoke(new ObjLoadProgress("Building acceleration structure", 94, vertexCount, faceCount, triangleCount));
        scene.RebuildWorldGeometry();
        progress?.Invoke(new ObjLoadProgress("Done", 100, vertexCount, faceCount, triangleCount));
        return new ObjLoadResult(filePath, vertexCount, faceCount, triangleCount);
    }

    private static ThreeDsDocument ReadDocument(string filePath, Action<ObjLoadProgress>? progress)
    {
        using BinaryReader reader = new(File.OpenRead(filePath), Encoding.ASCII);
        ThreeDsDocument document = new();
        ReadChunks(reader, reader.BaseStream.Length, document, null, progress);
        return document;
    }

    private static void ReadChunks(
        BinaryReader reader,
        long endPosition,
        ThreeDsDocument document,
        ThreeDsMesh? currentMesh,
        Action<ObjLoadProgress>? progress)
    {
        while (reader.BaseStream.Position + 6 <= endPosition)
        {
            long chunkStart = reader.BaseStream.Position;
            ushort id = reader.ReadUInt16();
            uint length = reader.ReadUInt32();
            if (length < 6)
                throw new InvalidDataException($"Invalid 3DS chunk length {length} at byte {chunkStart}.");

            long chunkEnd = chunkStart + length;
            if (chunkEnd > reader.BaseStream.Length)
                throw new InvalidDataException("3DS chunk extends beyond the end of the file.");

            switch (id)
            {
                case MainChunk:
                case EditorChunk:
                case TriangularMesh:
                    ReadChunks(reader, chunkEnd, document, currentMesh, progress);
                    break;

                case ObjectBlock:
                    string objectName = ReadNullTerminatedString(reader, chunkEnd);
                    ThreeDsMesh mesh = new(objectName);
                    document.Meshes.Add(mesh);
                    ReadChunks(reader, chunkEnd, document, mesh, progress);
                    break;

                case VertexList:
                    if (currentMesh != null)
                        ReadVertexList(reader, chunkEnd, currentMesh);
                    break;

                case MappingCoords:
                    if (currentMesh != null)
                        ReadMappingCoords(reader, chunkEnd, currentMesh);
                    break;

                case FaceList:
                    if (currentMesh != null)
                        ReadFaceList(reader, chunkEnd, currentMesh);
                    break;

                case MaterialBlock:
                    ThreeDsMaterialInfo material = ReadMaterial(reader, chunkEnd);
                    if (!string.IsNullOrWhiteSpace(material.Name))
                        document.Materials[material.Name] = material;
                    break;

                default:
                    break;
            }

            reader.BaseStream.Position = chunkEnd;
            if ((reader.BaseStream.Position & 0x3FFFF) == 0)
            {
                int percent = 5 + (int)(45.0 * reader.BaseStream.Position / Math.Max(1, reader.BaseStream.Length));
                progress?.Invoke(new ObjLoadProgress("Reading 3DS chunks", percent, 0, 0, 0));
            }
        }
    }

    private static void ReadVertexList(BinaryReader reader, long chunkEnd, ThreeDsMesh mesh)
    {
        if (reader.BaseStream.Position + 2 > chunkEnd) return;
        int count = reader.ReadUInt16();
        mesh.Vertices.Clear();
        mesh.Vertices.Capacity = Math.Max(mesh.Vertices.Capacity, count);
        for (int i = 0; i < count && reader.BaseStream.Position + 12 <= chunkEnd; i++)
            mesh.Vertices.Add(new Vec3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
    }

    private static void ReadMappingCoords(BinaryReader reader, long chunkEnd, ThreeDsMesh mesh)
    {
        if (reader.BaseStream.Position + 2 > chunkEnd) return;
        int count = reader.ReadUInt16();
        mesh.Uvs.Clear();
        mesh.Uvs.Capacity = Math.Max(mesh.Uvs.Capacity, count);
        for (int i = 0; i < count && reader.BaseStream.Position + 8 <= chunkEnd; i++)
            mesh.Uvs.Add(new Vec2(reader.ReadSingle(), reader.ReadSingle()));
    }

    private static void ReadFaceList(BinaryReader reader, long chunkEnd, ThreeDsMesh mesh)
    {
        if (reader.BaseStream.Position + 2 > chunkEnd) return;
        int count = reader.ReadUInt16();
        mesh.Faces.Clear();
        mesh.Faces.Capacity = Math.Max(mesh.Faces.Capacity, count);
        for (int i = 0; i < count && reader.BaseStream.Position + 8 <= chunkEnd; i++)
        {
            int a = reader.ReadUInt16();
            int b = reader.ReadUInt16();
            int c = reader.ReadUInt16();
            reader.ReadUInt16(); // face flags; not needed for raytracing import.
            mesh.Faces.Add(new ThreeDsFace(a, b, c));
        }

        while (reader.BaseStream.Position + 6 <= chunkEnd)
        {
            long subStart = reader.BaseStream.Position;
            ushort subId = reader.ReadUInt16();
            uint subLength = reader.ReadUInt32();
            if (subLength < 6) throw new InvalidDataException($"Invalid 3DS face subchunk length {subLength} at byte {subStart}.");
            long subEnd = subStart + subLength;
            if (subEnd > chunkEnd) throw new InvalidDataException("3DS face subchunk extends beyond its parent chunk.");

            if (subId == FaceMaterial)
                ReadFaceMaterial(reader, subEnd, mesh);

            reader.BaseStream.Position = subEnd;
        }
    }

    private static void ReadFaceMaterial(BinaryReader reader, long chunkEnd, ThreeDsMesh mesh)
    {
        string materialName = ReadNullTerminatedString(reader, chunkEnd);
        if (reader.BaseStream.Position + 2 > chunkEnd) return;
        int count = reader.ReadUInt16();
        for (int i = 0; i < count && reader.BaseStream.Position + 2 <= chunkEnd; i++)
        {
            int faceIndex = reader.ReadUInt16();
            if ((uint)faceIndex < (uint)mesh.Faces.Count)
                mesh.Faces[faceIndex].MaterialName = materialName;
        }
    }

    private static ThreeDsMaterialInfo ReadMaterial(BinaryReader reader, long chunkEnd)
    {
        ThreeDsMaterialInfo material = new();
        while (reader.BaseStream.Position + 6 <= chunkEnd)
        {
            long subStart = reader.BaseStream.Position;
            ushort id = reader.ReadUInt16();
            uint length = reader.ReadUInt32();
            if (length < 6) throw new InvalidDataException($"Invalid 3DS material subchunk length {length} at byte {subStart}.");
            long subEnd = subStart + length;
            if (subEnd > chunkEnd) throw new InvalidDataException("3DS material subchunk extends beyond its parent chunk.");

            switch (id)
            {
                case MaterialName:
                    material.Name = ReadNullTerminatedString(reader, subEnd);
                    break;
                case MaterialDiffuse:
                    material.Diffuse = ReadColorContainer(reader, subEnd, material.Diffuse);
                    break;
                case TextureMap1:
                    material.TextureFileName = ReadTextureMap(reader, subEnd);
                    break;
            }

            reader.BaseStream.Position = subEnd;
        }

        return material;
    }

    private static Vec3 ReadColorContainer(BinaryReader reader, long chunkEnd, Vec3 fallback)
    {
        Vec3 color = fallback;
        while (reader.BaseStream.Position + 6 <= chunkEnd)
        {
            long subStart = reader.BaseStream.Position;
            ushort id = reader.ReadUInt16();
            uint length = reader.ReadUInt32();
            if (length < 6) throw new InvalidDataException($"Invalid 3DS color subchunk length {length} at byte {subStart}.");
            long subEnd = subStart + length;
            if (subEnd > chunkEnd) throw new InvalidDataException("3DS color subchunk extends beyond its parent chunk.");

            if ((id == ColorFloat || id == LinColorFloat) && reader.BaseStream.Position + 12 <= subEnd)
                color = new Vec3(Clamp01(reader.ReadSingle()), Clamp01(reader.ReadSingle()), Clamp01(reader.ReadSingle()));
            else if ((id == Color24 || id == LinColor24) && reader.BaseStream.Position + 3 <= subEnd)
                color = new Vec3(reader.ReadByte() / 255.0, reader.ReadByte() / 255.0, reader.ReadByte() / 255.0);

            reader.BaseStream.Position = subEnd;
        }

        return color;
    }

    private static string? ReadTextureMap(BinaryReader reader, long chunkEnd)
    {
        string? fileName = null;
        while (reader.BaseStream.Position + 6 <= chunkEnd)
        {
            long subStart = reader.BaseStream.Position;
            ushort id = reader.ReadUInt16();
            uint length = reader.ReadUInt32();
            if (length < 6) throw new InvalidDataException($"Invalid 3DS texture subchunk length {length} at byte {subStart}.");
            long subEnd = subStart + length;
            if (subEnd > chunkEnd) throw new InvalidDataException("3DS texture subchunk extends beyond its parent chunk.");

            if (id == MappingFilename)
                fileName = ReadNullTerminatedString(reader, subEnd);

            reader.BaseStream.Position = subEnd;
        }

        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static Dictionary<string, Material> BuildMaterials(
        Dictionary<string, ThreeDsMaterialInfo> materialInfos,
        string modelPath,
        Material fallbackMaterial)
    {
        Dictionary<string, Material> materials = new(StringComparer.OrdinalIgnoreCase);
        foreach (ThreeDsMaterialInfo info in materialInfos.Values)
        {
            TextureMap? texture = null;
            string? texturePath = ResolveTexturePath(modelPath, info.TextureFileName);
            if (texturePath != null)
            {
                try { texture = TextureMap.FromFile(texturePath); }
                catch { texture = null; }
            }

            materials[info.Name] = new Material(info.Diffuse, fallbackMaterial.Emission, fallbackMaterial.LightId, texture);
        }

        return materials;
    }

    private static string? ResolveTexturePath(string modelPath, string? textureFileName)
    {
        if (string.IsNullOrWhiteSpace(textureFileName)) return null;

        string cleaned = textureFileName.Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(cleaned) && File.Exists(cleaned))
            return cleaned;

        string directory = Path.GetDirectoryName(modelPath) ?? Environment.CurrentDirectory;
        string direct = Path.Combine(directory, cleaned);
        if (File.Exists(direct))
            return direct;

        string fileName = Path.GetFileName(cleaned);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        try
        {
            foreach (string candidate in Directory.EnumerateFiles(directory, fileName, SearchOption.AllDirectories))
                return candidate;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static int BuildScene(
        Scene scene,
        List<ThreeDsMesh> meshes,
        Dictionary<string, Material> materials,
        Material fallbackMaterial,
        Vec3 sourceCenter,
        double scale,
        Vec3 offset,
        Aabb targetBounds,
        Action<ObjLoadProgress>? progress)
    {
        int triangleCount = 0;
        int totalFaces = 0;
        foreach (ThreeDsMesh mesh in meshes)
            totalFaces += mesh.Faces.Count;

        foreach (ThreeDsMesh mesh in meshes)
        {
            if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0)
                continue;

            SceneObjectGroup group = scene.AddImportedGroup(mesh.Name);
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                ThreeDsFace face = mesh.Faces[i];
                if (!IsValidFace(face, mesh.Vertices.Count))
                    continue;

                Vec3 a = Transform(mesh.Vertices[face.A], sourceCenter, scale, offset);
                Vec3 b = Transform(mesh.Vertices[face.B], sourceCenter, scale, offset);
                Vec3 c = Transform(mesh.Vertices[face.C], sourceCenter, scale, offset);
                Vec3 normal = (b - a).Cross(c - a).Normalize();
                if (normal.Length() <= 1e-10)
                    continue;

                Material material = fallbackMaterial;
                if (face.MaterialName != null && materials.TryGetValue(face.MaterialName, out Material? mapped))
                    material = mapped;

                Vec2 uvA = GetUv(mesh, face.A, a, normal, targetBounds);
                Vec2 uvB = GetUv(mesh, face.B, b, normal, targetBounds);
                Vec2 uvC = GetUv(mesh, face.C, c, normal, targetBounds);
                group.AddTriangle(a, b, c, uvA, uvB, uvC, material);
                triangleCount++;

                if ((triangleCount & 2047) == 0)
                {
                    int percent = 50 + (int)(40.0 * triangleCount / Math.Max(1, totalFaces));
                    progress?.Invoke(new ObjLoadProgress("Building 3DS scene", percent, 0, i, triangleCount));
                }
            }

            group.RecalculatePivot();
        }

        return triangleCount;
    }

    private static bool IsValidFace(ThreeDsFace face, int vertexCount) =>
        (uint)face.A < (uint)vertexCount &&
        (uint)face.B < (uint)vertexCount &&
        (uint)face.C < (uint)vertexCount &&
        face.A != face.B && face.B != face.C && face.A != face.C;

    private static Vec2 GetUv(ThreeDsMesh mesh, int index, Vec3 transformedPoint, Vec3 normal, Aabb bounds)
    {
        if ((uint)index < (uint)mesh.Uvs.Count)
            return mesh.Uvs[index];
        return GenerateBoxUv(transformedPoint, normal, bounds);
    }

    private static void GetBounds(List<ThreeDsMesh> meshes, out Vec3 min, out Vec3 max)
    {
        bool hasPoint = false;
        min = Vec3.Zero;
        max = Vec3.Zero;
        foreach (ThreeDsMesh mesh in meshes)
        {
            foreach (Vec3 p in mesh.Vertices)
            {
                if (!hasPoint)
                {
                    min = p;
                    max = p;
                    hasPoint = true;
                }
                else
                {
                    min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                    max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                }
            }
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
    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    private static string ReadNullTerminatedString(BinaryReader reader, long endPosition)
    {
        List<byte> bytes = new();
        while (reader.BaseStream.Position < endPosition)
        {
            byte b = reader.ReadByte();
            if (b == 0) break;
            bytes.Add(b);
        }
        return Encoding.ASCII.GetString(bytes.ToArray()).Trim();
    }
}
