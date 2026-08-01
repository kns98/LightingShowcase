// -----------------------------------------------------------------------------
// File: Scene/BinarySceneFile.cs
// Purpose: Compact native binary scene save/load.
//
// The .lscene format is the fast/default project format.  It preserves editor
// objects, transforms, lights, materials, textures, hierarchy, and semantic
// primitives where possible.  XML remains available for manual editing, but this
// file avoids verbose triangle XML for normal saves.
// -----------------------------------------------------------------------------

using System.IO.Compression;
using System.Text;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Saves and loads the compact Lighting Showcase binary scene format.</summary>
public static class BinarySceneFile
{
    private const string Magic = "LSCN";
    private const int Version = 9;

    private enum GeometryKind : byte
    {
        None = 0,
        Cuboid = 1,
        Rectangle = 2,
        ReadyMadePrimitive = 3,
        Mesh = 4
    }

    public static void Save(Scene scene, string filePath)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save path is required.", nameof(filePath));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Environment.CurrentDirectory);
        using FileStream stream = File.Create(filePath);

        // Keep the header writer's lifetime separate from the compressed body.
        // A using declaration here can be disposed after DeflateStream has closed
        // the underlying FileStream, which causes "Cannot access a closed file".
        using (BinaryWriter headerWriter = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            headerWriter.Write(Magic);
            headerWriter.Write(Version);
            headerWriter.Flush();
        }

        // Version 3 compresses the body.  The DeflateStream must leave the
        // FileStream open so the outer FileStream owner can dispose it cleanly.
        // The BinaryWriter also leaves the DeflateStream open; disposing the
        // DeflateStream is what writes the final compressed bytes.
        using (DeflateStream compressedBody = new(stream, CompressionLevel.SmallestSize, leaveOpen: true))
        using (BinaryWriter writer = new(compressedBody, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(scene.Description ?? string.Empty);

            TextureWriteTable textureTable = TextureWriteTable.Build(scene);
            MaterialWriteTable materialTable = MaterialWriteTable.Build(scene, textureTable);
            WriteTextureTable(writer, textureTable);
            WriteMaterialTable(writer, materialTable, textureTable);

            writer.Write(scene.Lights.Count);
            foreach (SceneLight light in scene.Lights)
                WriteLight(writer, light);

            writer.Write(scene.ObjectGroups.Count);
            foreach (SceneObjectGroup group in scene.ObjectGroups)
                WriteObject(writer, group, textureTable, materialTable);

            writer.Flush();
        }
    }

    public static string LoadIntoScene(Scene scene, string filePath)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A load path is required.", nameof(filePath));

        using FileStream stream = File.OpenRead(filePath);
        using BinaryReader headerReader = new(stream, Encoding.UTF8, leaveOpen: true);

        string magic = headerReader.ReadString();
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            throw new InvalidDataException("This is not a Lighting Showcase binary scene file.");

        int version = headerReader.ReadInt32();
        if (version < 1 || version > Version)
            throw new InvalidDataException($"Unsupported Lighting Showcase binary scene version: {version}.");

        Stream bodyStream = version >= 3
            ? new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true)
            : stream;

        using BinaryReader reader = version >= 3
            ? new BinaryReader(bodyStream, Encoding.UTF8, leaveOpen: false)
            : headerReader;

        scene.Clear();
        string description = reader.ReadString();
        TextureReadTable? textureTable = version >= 2 ? ReadTextureTable(reader, filePath, version) : null;
        MaterialReadTable? materialTable = version >= 3 ? ReadMaterialTable(reader, textureTable, version) : null;

        int lightCount = reader.ReadInt32();
        for (int i = 0; i < lightCount; i++)
            scene.Lights.Add(ReadLight(reader, version));

        int objectCount = reader.ReadInt32();
        for (int i = 0; i < objectCount; i++)
            ReadObject(reader, scene, parent: null, sceneFilePath: filePath, version: version, textureTable: textureTable, materialTable: materialTable);

        scene.RebuildWorldGeometry();
        return string.IsNullOrWhiteSpace(description) ? $"Binary scene: {Path.GetFileName(filePath)}" : description;
    }

    private static void WriteLight(BinaryWriter writer, SceneLight light)
    {
        writer.Write(light.Id ?? string.Empty);
        WriteVec3(writer, light.Position);
        WriteVec3(writer, light.Color);
        writer.Write(light.Intensity);
        writer.Write(light.Enabled);
        writer.Write((byte)light.Kind);
        WriteVec3(writer, light.Direction);
        writer.Write(light.Range);
        writer.Write(light.InnerConeAngle);
        writer.Write(light.OuterConeAngle);
    }

    private static SceneLight ReadLight(BinaryReader reader, int version)
    {
        string id = reader.ReadString();
        Vec3 position = ReadVec3(reader);
        Vec3 color = ReadVec3(reader);
        double intensity = reader.ReadDouble();
        bool enabled = reader.ReadBoolean();
        if (version < 4)
            return new SceneLight(id, position, color, intensity, enabled);

        SceneLightKind kind = (SceneLightKind)reader.ReadByte();
        Vec3 direction = ReadVec3(reader);
        double range = reader.ReadDouble();
        double innerConeAngle = reader.ReadDouble();
        double outerConeAngle = reader.ReadDouble();
        return new SceneLight(id, position, color, intensity, enabled, kind, direction, range, innerConeAngle, outerConeAngle);
    }

    private static void WriteObject(BinaryWriter writer, SceneObjectGroup group, TextureWriteTable textureTable, MaterialWriteTable materialTable)
    {
        writer.Write(group.Name ?? string.Empty);
        writer.Write(group.IsSelectable);
        writer.Write(group.PrimitiveKind ?? string.Empty);
        writer.Write(group.PrimitiveSourceName ?? string.Empty);
        WritePrimitiveParameters(writer, group);
        WriteVec3(writer, group.Position);
        WriteVec3(writer, group.Rotation);
        WriteVec3(writer, group.Scale);
        WriteOptionalMaterial(writer, group.ColorOverride, textureTable, materialTable);

        List<GeometryRecord> records = CreateGeometryRecords(group).ToList();
        writer.Write(records.Count);
        foreach (GeometryRecord record in records)
            WriteGeometryRecord(writer, record, textureTable, materialTable);

        writer.Write(group.Children.Count);
        foreach (SceneObjectGroup child in group.Children)
            WriteObject(writer, child, textureTable, materialTable);
    }

    private static void WritePrimitiveParameters(BinaryWriter writer, SceneObjectGroup group)
    {
        writer.Write(group.PrimitiveParameters.Count);
        foreach (KeyValuePair<string, double> parameter in group.PrimitiveParameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.Write(parameter.Key);
            writer.Write(parameter.Value);
        }
    }

    private static Dictionary<string, double> ReadPrimitiveParameters(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        Dictionary<string, double> parameters = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            string key = reader.ReadString();
            double value = reader.ReadDouble();
            if (!string.IsNullOrWhiteSpace(key) && double.IsFinite(value))
                parameters[key] = value;
        }
        return parameters;
    }

    private static SceneObjectGroup ReadObject(BinaryReader reader, Scene scene, SceneObjectGroup? parent, string sceneFilePath, int version, TextureReadTable? textureTable, MaterialReadTable? materialTable)
    {
        string name = reader.ReadString();
        bool selectable = reader.ReadBoolean();
        string primitiveKind = reader.ReadString();
        string primitiveSourceName = reader.ReadString();
        Dictionary<string, double> primitiveParameters = version >= 6 ? ReadPrimitiveParameters(reader) : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        Vec3 position = ReadVec3(reader);
        Vec3 rotation = ReadVec3(reader);
        Vec3 scale = ReadVec3(reader);
        Material? colorOverride = ReadOptionalMaterial(reader, sceneFilePath, version, textureTable, materialTable);

        SceneObjectGroup group = scene.AddImportedGroup(name, selectable);
        if (parent != null)
        {
            scene.ObjectGroups.Remove(group);
            parent.AddChild(group);
        }

        if (!string.IsNullOrWhiteSpace(primitiveKind))
            group.PrimitiveKind = primitiveKind;
        if (!string.IsNullOrWhiteSpace(primitiveSourceName))
            group.PrimitiveSourceName = primitiveSourceName;
        foreach (KeyValuePair<string, double> parameter in primitiveParameters)
            group.PrimitiveParameters[parameter.Key] = parameter.Value;
        group.ColorOverride = colorOverride;

        int geometryCount = reader.ReadInt32();
        for (int i = 0; i < geometryCount; i++)
            ReadGeometryRecord(reader, scene, group, sceneFilePath, version, textureTable, materialTable);

        int childCount = reader.ReadInt32();
        for (int i = 0; i < childCount; i++)
            ReadObject(reader, scene, group, sceneFilePath, version, textureTable, materialTable);

        if (group.PrimitiveParameters.Count > 0 && group.Children.Count == 0)
            scene.RebuildPrimitiveShadowGeometry(group);

        group.RecalculatePivot();
        group.Position = position;
        group.Rotation = rotation;
        group.Scale = scale;
        return group;
    }

    private static IEnumerable<GeometryRecord> CreateGeometryRecords(SceneObjectGroup group)
    {
        if (group.LocalTriangles.Count == 0)
            yield break;

        if (!string.IsNullOrWhiteSpace(group.PrimitiveKind) &&
            !string.Equals(group.PrimitiveKind, "rectangle", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(group.PrimitiveKind, "cuboid", StringComparison.OrdinalIgnoreCase))
        {
            yield return GeometryRecord.ReadyMade(group.PrimitiveKind!, group.PrimitiveSourceName, RepresentativeMaterial(group));
            yield break;
        }

        List<List<Triangle>> components = FindConnectedTriangleComponents(group.LocalTriangles);
        foreach (List<Triangle> component in components)
        {
            if (TryCreateCuboidRecord(component, out GeometryRecord cuboid))
            {
                yield return cuboid;
                continue;
            }

            if (TryCreateRectangleRecord(component, out GeometryRecord rectangle))
            {
                yield return rectangle;
                continue;
            }

            yield return GeometryRecord.Mesh(component);
        }
    }

    private static void WriteGeometryRecord(BinaryWriter writer, GeometryRecord record, TextureWriteTable textureTable, MaterialWriteTable materialTable)
    {
        writer.Write((byte)record.Kind);
        writer.Write(record.Name ?? string.Empty);

        switch (record.Kind)
        {
            case GeometryKind.Cuboid:
                WriteVec3(writer, record.Min);
                WriteVec3(writer, record.Max);
                WriteMaterialRef(writer, record.Material ?? new Material(new Vec3(0.78, 0.76, 0.72)), materialTable);
                break;
            case GeometryKind.Rectangle:
                WriteVec3(writer, record.P0);
                WriteVec3(writer, record.P1);
                WriteVec3(writer, record.P2);
                WriteVec3(writer, record.P3);
                WriteMaterialRef(writer, record.Material ?? new Material(new Vec3(0.78, 0.76, 0.72)), materialTable);
                break;
            case GeometryKind.ReadyMadePrimitive:
                writer.Write(record.PrimitiveKind ?? string.Empty);
                writer.Write(record.PrimitiveSourceName ?? string.Empty);
                WriteMaterialRef(writer, record.Material ?? new Material(new Vec3(0.78, 0.76, 0.72)), materialTable);
                break;
            case GeometryKind.Mesh:
                WriteIndexedMesh(writer, record.Triangles, materialTable);
                break;
        }
    }

    private static void ReadGeometryRecord(BinaryReader reader, Scene scene, SceneObjectGroup group, string sceneFilePath, int version, TextureReadTable? textureTable, MaterialReadTable? materialTable)
    {
        GeometryKind kind = (GeometryKind)reader.ReadByte();
        _ = reader.ReadString(); // reserved per-part name for future object browser support

        switch (kind)
        {
            case GeometryKind.Cuboid:
            {
                Vec3 min = ReadVec3(reader);
                Vec3 max = ReadVec3(reader);
                Material material = ReadMaterialOrRef(reader, sceneFilePath, version, textureTable, materialTable);
                AddBox(group, min, max, material);
                group.PrimitiveKind ??= "cuboid";
                break;
            }
            case GeometryKind.Rectangle:
            {
                Vec3 p0 = ReadVec3(reader);
                Vec3 p1 = ReadVec3(reader);
                Vec3 p2 = ReadVec3(reader);
                Vec3 p3 = ReadVec3(reader);
                Material material = ReadMaterialOrRef(reader, sceneFilePath, version, textureTable, materialTable);
                AddQuad(group, p0, p1, p2, p3, material);
                group.PrimitiveKind ??= "rectangle";
                break;
            }
            case GeometryKind.ReadyMadePrimitive:
            {
                string primitiveKind = reader.ReadString();
                string sourceName = reader.ReadString();
                Material material = ReadMaterialOrRef(reader, sceneFilePath, version, textureTable, materialTable);
                string readyMadeName = ObjectLibraryRegistry.ReadyMadeNameForPrimitiveKind(primitiveKind, sourceName);
                SceneObjectGroup temporary = ObjectLibraryRegistry.Insert(scene, new SceneMaterials(), readyMadeName);
                temporary.ApplyColor(material);
                scene.ObjectGroups.Remove(temporary);
                foreach (Triangle tri in temporary.LocalTriangles)
                    group.AddTriangle(tri.A, tri.B, tri.C, tri.UvA, tri.UvB, tri.UvC, tri.Material);
                foreach (SceneObjectGroup child in temporary.Children.ToList())
                {
                    temporary.RemoveChild(child);
                    group.AddChild(child);
                }
                group.PrimitiveKind = string.IsNullOrWhiteSpace(primitiveKind) ? ObjectLibraryRegistry.PrimitiveKindForReadyMade(readyMadeName) : primitiveKind;
                group.PrimitiveSourceName = readyMadeName;
                break;
            }
            case GeometryKind.Mesh:
            {
                if (version >= 3)
                    ReadIndexedMesh(reader, group, materialTable, version);
                else
                {
                    int triangleCount = reader.ReadInt32();
                    for (int i = 0; i < triangleCount; i++)
                        ReadTriangle(reader, group, sceneFilePath, version, textureTable);
                }
                break;
            }
            default:
                throw new InvalidDataException($"Unknown binary geometry record kind: {kind}.");
        }
    }

    private readonly record struct MeshVertex(Vec3 Position, Vec2 Uv, Vec3 Normal);

    private static void WriteIndexedMesh(BinaryWriter writer, IReadOnlyList<Triangle> triangles, MaterialWriteTable materialTable)
    {
        Dictionary<string, int> indexByVertex = new(StringComparer.Ordinal);
        List<MeshVertex> vertices = new();
        List<(int A, int B, int C, int MaterialId)> faces = new(triangles.Count);

        foreach (Triangle triangle in triangles)
        {
            int a = AddMeshVertex(vertices, indexByVertex, triangle.A, triangle.UvA, triangle.NormalA);
            int b = AddMeshVertex(vertices, indexByVertex, triangle.B, triangle.UvB, triangle.NormalB);
            int c = AddMeshVertex(vertices, indexByVertex, triangle.C, triangle.UvC, triangle.NormalC);
            faces.Add((a, b, c, materialTable.IdFor(triangle.Material)));
        }

        writer.Write(vertices.Count);
        foreach (MeshVertex vertex in vertices)
        {
            WriteVec3Single(writer, vertex.Position);
            WriteVec2Single(writer, vertex.Uv);
            WriteVec3Single(writer, vertex.Normal);
        }

        writer.Write(faces.Count);
        foreach ((int a, int b, int c, int materialId) in faces)
        {
            WriteCompactInt(writer, a);
            WriteCompactInt(writer, b);
            WriteCompactInt(writer, c);
            WriteCompactInt(writer, materialId);
        }
    }

    private static void ReadIndexedMesh(BinaryReader reader, SceneObjectGroup group, MaterialReadTable? materialTable, int version)
    {
        int vertexCount = reader.ReadInt32();
        MeshVertex[] vertices = new MeshVertex[Math.Max(0, vertexCount)];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vec3 position = ReadVec3Single(reader);
            Vec2 uv = ReadVec2Single(reader);
            Vec3 normal = version >= 8 ? ReadVec3Single(reader) : Vec3.Zero;
            vertices[i] = new MeshVertex(position, uv, normal);
        }

        int faceCount = reader.ReadInt32();
        for (int i = 0; i < faceCount; i++)
        {
            int ia = ReadCompactInt(reader);
            int ib = ReadCompactInt(reader);
            int ic = ReadCompactInt(reader);
            int materialId = ReadCompactInt(reader);
            if ((uint)ia >= vertices.Length || (uint)ib >= vertices.Length || (uint)ic >= vertices.Length)
                throw new InvalidDataException("Mesh index is outside the vertex table.");

            Material material = materialTable?.ById(materialId) ?? new Material(new Vec3(0.78, 0.76, 0.72));
            MeshVertex a = vertices[ia];
            MeshVertex b = vertices[ib];
            MeshVertex c = vertices[ic];
            if (version >= 8)
                group.AddTriangle(a.Position, b.Position, c.Position, a.Uv, b.Uv, c.Uv, a.Normal, b.Normal, c.Normal, material);
            else
                group.AddTriangle(a.Position, b.Position, c.Position, a.Uv, b.Uv, c.Uv, material);
        }
    }

    private static int AddMeshVertex(List<MeshVertex> vertices, Dictionary<string, int> indexByVertex, Vec3 position, Vec2 uv, Vec3 normal)
    {
        string key = MeshVertexKey(position, uv, normal);
        if (indexByVertex.TryGetValue(key, out int index))
            return index;

        index = vertices.Count;
        vertices.Add(new MeshVertex(position, uv, normal));
        indexByVertex[key] = index;
        return index;
    }

    private static string MeshVertexKey(Vec3 position, Vec2 uv, Vec3 normal) =>
        $"{RoundKey(position.X)}|{RoundKey(position.Y)}|{RoundKey(position.Z)}|{RoundKey(uv.U)}|{RoundKey(uv.V)}|" +
        $"{RoundKey(normal.X)}|{RoundKey(normal.Y)}|{RoundKey(normal.Z)}";

    private static string RoundKey(double value) => Math.Round(value, 6).ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

    private static void WriteCompactInt(BinaryWriter writer, int value)
    {
        uint unsigned = (uint)value;
        while (unsigned >= 0x80)
        {
            writer.Write((byte)(unsigned | 0x80));
            unsigned >>= 7;
        }
        writer.Write((byte)unsigned);
    }

    private static int ReadCompactInt(BinaryReader reader)
    {
        uint result = 0;
        int shift = 0;
        while (shift < 35)
        {
            byte value = reader.ReadByte();
            result |= (uint)(value & 0x7F) << shift;
            if ((value & 0x80) == 0)
                return (int)result;
            shift += 7;
        }
        throw new InvalidDataException("Invalid compact integer in binary scene file.");
    }

    private static void WriteTriangle(BinaryWriter writer, Triangle triangle, TextureWriteTable textureTable)
    {
        WriteVec3(writer, triangle.A);
        WriteVec3(writer, triangle.B);
        WriteVec3(writer, triangle.C);
        WriteVec2(writer, triangle.UvA);
        WriteVec2(writer, triangle.UvB);
        WriteVec2(writer, triangle.UvC);
        WriteMaterial(writer, triangle.Material, textureTable);
    }

    private static void ReadTriangle(BinaryReader reader, SceneObjectGroup group, string sceneFilePath, int version, TextureReadTable? textureTable)
    {
        Vec3 a = ReadVec3(reader);
        Vec3 b = ReadVec3(reader);
        Vec3 c = ReadVec3(reader);
        Vec2 uvA = ReadVec2(reader);
        Vec2 uvB = ReadVec2(reader);
        Vec2 uvC = ReadVec2(reader);
        Material material = ReadMaterial(reader, sceneFilePath, version, textureTable);
        group.AddTriangle(a, b, c, uvA, uvB, uvC, material);
    }

    private static void WriteOptionalMaterial(BinaryWriter writer, Material? material, TextureWriteTable textureTable, MaterialWriteTable materialTable)
    {
        writer.Write(material != null);
        if (material != null)
            WriteMaterialRef(writer, material, materialTable);
    }

    private static Material? ReadOptionalMaterial(BinaryReader reader, string sceneFilePath, int version, TextureReadTable? textureTable, MaterialReadTable? materialTable) =>
        reader.ReadBoolean() ? ReadMaterialOrRef(reader, sceneFilePath, version, textureTable, materialTable) : null;

    private static void WriteMaterialRef(BinaryWriter writer, Material material, MaterialWriteTable materialTable)
    {
        WriteCompactInt(writer, materialTable.IdFor(material));
    }

    private static Material ReadMaterialOrRef(BinaryReader reader, string sceneFilePath, int version, TextureReadTable? textureTable, MaterialReadTable? materialTable)
    {
        if (version >= 3)
            return materialTable?.ById(ReadCompactInt(reader)) ?? new Material(new Vec3(0.78, 0.76, 0.72));

        return ReadMaterial(reader, sceneFilePath, version, textureTable);
    }

    private static void WriteMaterial(BinaryWriter writer, Material material, TextureWriteTable textureTable)
    {
        WriteVec3(writer, material.Color);
        writer.Write(material.Emission);
        writer.Write(material.LightId ?? string.Empty);
        writer.Write(textureTable.IdFor(material.Texture));

        // Version 5 preserves the glTF/PBR material fields that can now be
        // edited from the Selection tab. Older readers will reject v5 rather
        // than silently losing these properties.
        WriteVec3(writer, material.EmissionColor);
        writer.Write(textureTable.IdFor(material.EmissiveTexture));
        writer.Write(material.Alpha);
        writer.Write(material.AlphaBlend);
        writer.Write(material.Metallic);
        writer.Write(material.Roughness);
        writer.Write(material.Transmission);
        writer.Write(textureTable.IdFor(material.MetallicRoughnessTexture));
        writer.Write(textureTable.IdFor(material.NormalTexture));

        // Version 8 preserves the remaining core glTF material controls.
        writer.Write(textureTable.IdFor(material.OcclusionTexture));
        writer.Write(material.NormalScale);
        writer.Write(material.OcclusionStrength);
        writer.Write((byte)material.AlphaMode);
        writer.Write(material.AlphaCutoff);
        writer.Write(material.DoubleSided);

        // Version 9 preserves transmission texture and the low-cost optical
        // extension parameters used by the Vulkan raster preview.
        writer.Write(textureTable.IdFor(material.TransmissionTexture));
        writer.Write(material.Ior);
        writer.Write(material.Thickness);
        WriteVec3(writer, material.AttenuationColor);
        writer.Write(material.AttenuationDistance);
        writer.Write(material.Clearcoat);
        writer.Write(material.ClearcoatRoughness);
        writer.Write(material.ClearcoatUsesTransmissionTexture);
    }

    private static Material ReadMaterial(BinaryReader reader, string sceneFilePath, int version, TextureReadTable? textureTable)
    {
        Vec3 color = ReadVec3(reader);
        double emission = reader.ReadDouble();
        string lightId = reader.ReadString();
        TextureMap? texture = version >= 2
            ? textureTable?.ById(reader.ReadInt32())
            : ReadTextureV1(reader, sceneFilePath);

        if (version < 5)
            return new Material(color, emission, string.IsNullOrWhiteSpace(lightId) ? null : lightId, texture);

        Vec3 emissionColor = ReadVec3(reader);
        TextureMap? emissiveTexture = textureTable?.ById(reader.ReadInt32());
        double alpha = reader.ReadDouble();
        bool alphaBlend = reader.ReadBoolean();
        double metallic = reader.ReadDouble();
        double roughness = reader.ReadDouble();
        double transmission = reader.ReadDouble();
        TextureMap? metallicRoughnessTexture = textureTable?.ById(reader.ReadInt32());
        TextureMap? normalTexture = textureTable?.ById(reader.ReadInt32());
        TextureMap? occlusionTexture = version >= 8 ? textureTable?.ById(reader.ReadInt32()) : null;
        double normalScale = version >= 8 ? reader.ReadDouble() : 1.0;
        double occlusionStrength = version >= 8 ? reader.ReadDouble() : 1.0;
        MaterialAlphaMode alphaMode = version >= 8
            ? (MaterialAlphaMode)reader.ReadByte()
            : alphaBlend ? MaterialAlphaMode.Blend : MaterialAlphaMode.Opaque;
        double alphaCutoff = version >= 8 ? reader.ReadDouble() : 0.5;
        bool doubleSided = version >= 8 && reader.ReadBoolean();
        TextureMap? transmissionTexture = version >= 9 ? textureTable?.ById(reader.ReadInt32()) : null;
        double ior = version >= 9 ? reader.ReadDouble() : 1.5;
        double thickness = version >= 9 ? reader.ReadDouble() : 0.0;
        Vec3 attenuationColor = version >= 9 ? ReadVec3(reader) : new Vec3(1.0, 1.0, 1.0);
        double attenuationDistance = version >= 9 ? reader.ReadDouble() : 0.0;
        double clearcoat = version >= 9 ? reader.ReadDouble() : 0.0;
        double clearcoatRoughness = version >= 9 ? reader.ReadDouble() : 0.0;
        bool clearcoatUsesTransmissionTexture = version >= 9 && reader.ReadBoolean();

        return new Material(
            color,
            emission,
            string.IsNullOrWhiteSpace(lightId) ? null : lightId,
            texture,
            emissionColor,
            emissiveTexture,
            alpha,
            alphaBlend,
            metallic,
            roughness,
            transmission,
            metallicRoughnessTexture,
            normalTexture,
            occlusionTexture,
            normalScale,
            occlusionStrength,
            alphaMode,
            alphaCutoff,
            doubleSided,
            transmissionTexture,
            ior,
            thickness,
            attenuationColor,
            attenuationDistance,
            clearcoat,
            clearcoatRoughness,
            clearcoatUsesTransmissionTexture);
    }

    private static void WriteTextureTable(BinaryWriter writer, TextureWriteTable textureTable)
    {
        writer.Write(textureTable.Textures.Count);
        foreach (TextureMap texture in textureTable.Textures)
            WriteTextureRecord(writer, texture);
    }

    private static TextureReadTable ReadTextureTable(BinaryReader reader, string sceneFilePath, int version)
    {
        int count = reader.ReadInt32();
        TextureMap?[] textures = new TextureMap?[Math.Max(0, count)];
        for (int i = 0; i < textures.Length; i++)
            textures[i] = ReadTextureRecord(reader, sceneFilePath, version);
        return new TextureReadTable(textures);
    }

    private static void WriteTextureRecord(BinaryWriter writer, TextureMap texture)
    {
        writer.Write(texture.Name ?? string.Empty);
        writer.Write(texture.Width);
        writer.Write(texture.Height);
        writer.Write(texture.SourcePath ?? string.Empty);
        writer.Write(texture.IsBuiltInChecker);
        writer.Write((int)texture.WrapU);
        writer.Write((int)texture.WrapV);
        writer.Write(texture.OffsetU);
        writer.Write(texture.OffsetV);
        writer.Write(texture.ScaleU);
        writer.Write(texture.ScaleV);
        writer.Write(texture.Rotation);
    }

    private static TextureMap? ReadTextureRecord(BinaryReader reader, string sceneFilePath, int version)
    {
        string name = reader.ReadString();
        int width = Math.Max(2, reader.ReadInt32());
        int height = Math.Max(2, reader.ReadInt32());
        string sourcePath = reader.ReadString();
        bool checker = reader.ReadBoolean();
        TextureAddressMode wrapU = TextureAddressMode.Repeat;
        TextureAddressMode wrapV = TextureAddressMode.Repeat;
        double offsetU = 0.0;
        double offsetV = 0.0;
        double scaleU = 1.0;
        double scaleV = 1.0;
        double rotation = 0.0;
        if (version >= 7)
        {
            wrapU = ReadTextureAddressMode(reader.ReadInt32());
            wrapV = ReadTextureAddressMode(reader.ReadInt32());
            offsetU = reader.ReadDouble();
            offsetV = reader.ReadDouble();
            scaleU = reader.ReadDouble();
            scaleV = reader.ReadDouble();
            rotation = reader.ReadDouble();
        }

        return ResolveTexture(name, width, height, sourcePath, checker, sceneFilePath, wrapU, wrapV, offsetU, offsetV, scaleU, scaleV, rotation);
    }

    private static TextureMap? ReadTextureV1(BinaryReader reader, string sceneFilePath)
    {
        if (!reader.ReadBoolean())
            return null;

        string name = reader.ReadString();
        int width = Math.Max(2, reader.ReadInt32());
        int height = Math.Max(2, reader.ReadInt32());
        string sourcePath = reader.ReadString();
        bool checker = reader.ReadBoolean();
        return ResolveTexture(name, width, height, sourcePath, checker, sceneFilePath);
    }

    private static TextureMap? ResolveTexture(
        string name,
        int width,
        int height,
        string sourcePath,
        bool checker,
        string sceneFilePath,
        TextureAddressMode wrapU = TextureAddressMode.Repeat,
        TextureAddressMode wrapV = TextureAddressMode.Repeat,
        double offsetU = 0.0,
        double offsetV = 0.0,
        double scaleU = 1.0,
        double scaleV = 1.0,
        double rotation = 0.0)
    {
        static TextureMap ApplyMetadata(TextureMap texture, TextureAddressMode wrapU, TextureAddressMode wrapV, double offsetU, double offsetV, double scaleU, double scaleV, double rotation) =>
            texture.WithAddressing(wrapU, wrapV).WithTextureTransform(offsetU, offsetV, scaleU, scaleV, rotation);

        if (checker)
            return ApplyMetadata(TextureMap.CreateChecker(string.IsNullOrWhiteSpace(name) ? "Built-in checker" : name, width, height), wrapU, wrapV, offsetU, offsetV, scaleU, scaleV, rotation);

        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        string sceneDirectory = Path.GetDirectoryName(Path.GetFullPath(sceneFilePath)) ?? Environment.CurrentDirectory;
        string? candidate = TextureMap.ResolveFilePath(sourcePath, sceneDirectory);

        try
        {
            return candidate != null ? ApplyMetadata(TextureMap.FromFile(candidate), wrapU, wrapV, offsetU, offsetV, scaleU, scaleV, rotation) : null;
        }
        catch
        {
            return null;
        }
    }

    private static TextureAddressMode ReadTextureAddressMode(int value) =>
        Enum.IsDefined(typeof(TextureAddressMode), value)
            ? (TextureAddressMode)value
            : TextureAddressMode.Repeat;

    private static void WriteMaterialTable(BinaryWriter writer, MaterialWriteTable materialTable, TextureWriteTable textureTable)
    {
        writer.Write(materialTable.Materials.Count);
        foreach (Material material in materialTable.Materials)
            WriteMaterial(writer, material, textureTable);
    }

    private static MaterialReadTable ReadMaterialTable(BinaryReader reader, TextureReadTable? textureTable, int version)
    {
        int count = reader.ReadInt32();
        Material[] materials = new Material[Math.Max(0, count)];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = ReadMaterial(reader, string.Empty, version, textureTable);
        return new MaterialReadTable(materials);
    }

    private sealed class MaterialWriteTable
    {
        private readonly Dictionary<string, int> idsByKey = new(StringComparer.Ordinal);
        private readonly TextureWriteTable textureTable;

        public List<Material> Materials { get; } = new();

        private MaterialWriteTable(TextureWriteTable textureTable)
        {
            this.textureTable = textureTable;
        }

        public static MaterialWriteTable Build(Scene scene, TextureWriteTable textureTable)
        {
            MaterialWriteTable table = new(textureTable);
            foreach (SceneObjectGroup group in scene.ObjectGroups)
                table.CollectGroup(group);
            return table;
        }

        public int IdFor(Material material)
        {
            string key = KeyFor(material);
            if (idsByKey.TryGetValue(key, out int id))
                return id;

            id = Materials.Count;
            idsByKey[key] = id;
            Materials.Add(material);
            return id;
        }

        private void CollectGroup(SceneObjectGroup group)
        {
            if (group.ColorOverride != null)
                IdFor(group.ColorOverride);
            foreach (Triangle triangle in group.LocalTriangles)
                IdFor(triangle.Material);
            foreach (SceneObjectGroup child in group.Children)
                CollectGroup(child);
        }

        private string KeyFor(Material material) =>
            $"{RoundKey(material.Color.X)}|{RoundKey(material.Color.Y)}|{RoundKey(material.Color.Z)}|{RoundKey(material.Emission)}|{material.LightId ?? string.Empty}|" +
            $"{textureTable.IdFor(material.Texture)}|{RoundKey(material.EmissionColor.X)}|{RoundKey(material.EmissionColor.Y)}|{RoundKey(material.EmissionColor.Z)}|" +
            $"{textureTable.IdFor(material.EmissiveTexture)}|{RoundKey(material.Alpha)}|{material.AlphaBlend}|{RoundKey(material.Metallic)}|{RoundKey(material.Roughness)}|" +
            $"{RoundKey(material.Transmission)}|{textureTable.IdFor(material.MetallicRoughnessTexture)}|{textureTable.IdFor(material.NormalTexture)}|" +
            $"{textureTable.IdFor(material.OcclusionTexture)}|{RoundKey(material.NormalScale)}|{RoundKey(material.OcclusionStrength)}|" +
            $"{(int)material.AlphaMode}|{RoundKey(material.AlphaCutoff)}|{material.DoubleSided}|{textureTable.IdFor(material.TransmissionTexture)}|" +
            $"{RoundKey(material.Ior)}|{RoundKey(material.Thickness)}|{RoundKey(material.AttenuationColor.X)}|{RoundKey(material.AttenuationColor.Y)}|" +
            $"{RoundKey(material.AttenuationColor.Z)}|{RoundKey(material.AttenuationDistance)}|{RoundKey(material.Clearcoat)}|" +
            $"{RoundKey(material.ClearcoatRoughness)}|{material.ClearcoatUsesTransmissionTexture}";
    }

    private sealed class MaterialReadTable
    {
        private readonly Material[] materials;

        public MaterialReadTable(Material[] materials)
        {
            this.materials = materials;
        }

        public Material? ById(int id) => id >= 0 && id < materials.Length ? materials[id] : null;
    }

    private sealed class TextureWriteTable
    {
        private readonly Dictionary<string, int> idsByKey = new(StringComparer.Ordinal);

        public List<TextureMap> Textures { get; } = new();

        public static TextureWriteTable Build(Scene scene)
        {
            TextureWriteTable table = new();
            foreach (SceneObjectGroup group in scene.ObjectGroups)
                table.CollectGroup(group);
            return table;
        }

        public int IdFor(TextureMap? texture)
        {
            if (texture == null)
                return -1;

            string key = KeyFor(texture);
            if (idsByKey.TryGetValue(key, out int id))
                return id;

            // This is a safety net for textures added after the table was built.
            // Normal Save() collects all scene textures before it writes the table.
            id = Textures.Count;
            idsByKey[key] = id;
            Textures.Add(texture);
            return id;
        }

        private void CollectGroup(SceneObjectGroup group)
        {
            CollectMaterial(group.ColorOverride);
            foreach (Triangle triangle in group.LocalTriangles)
                CollectMaterial(triangle.Material);
            foreach (SceneObjectGroup child in group.Children)
                CollectGroup(child);
        }

        private void CollectMaterial(Material? material)
        {
            if (material == null)
                return;

            IdFor(material.Texture);
            IdFor(material.EmissiveTexture);
            IdFor(material.MetallicRoughnessTexture);
            IdFor(material.NormalTexture);
            IdFor(material.OcclusionTexture);
            IdFor(material.TransmissionTexture);
        }

        private static string KeyFor(TextureMap texture)
        {
            string path = string.IsNullOrWhiteSpace(texture.SourcePath)
                ? string.Empty
                : Path.GetFullPath(texture.SourcePath).ToUpperInvariant();
            return $"{texture.IsBuiltInChecker}|{path}|{texture.Name}|{texture.Width}|{texture.Height}|{(int)texture.WrapU}|{(int)texture.WrapV}|" +
                $"{RoundKey(texture.OffsetU)}|{RoundKey(texture.OffsetV)}|{RoundKey(texture.ScaleU)}|{RoundKey(texture.ScaleV)}|{RoundKey(texture.Rotation)}";
        }
    }

    private sealed class TextureReadTable
    {
        private readonly TextureMap?[] textures;

        public TextureReadTable(TextureMap?[] textures)
        {
            this.textures = textures;
        }

        public TextureMap? ById(int id)
        {
            return id >= 0 && id < textures.Length ? textures[id] : null;
        }
    }

    private static bool TryCreateCuboidRecord(IReadOnlyList<Triangle> triangles, out GeometryRecord record)
    {
        record = default;
        if (triangles.Count != 12)
            return false;

        Aabb bounds = BoundsOf(triangles);
        List<Vec3> unique = UniqueVertices(triangles);
        if (unique.Count != 8)
            return false;

        foreach (Vec3 point in unique)
        {
            bool xOk = NearlyEqual(point.X, bounds.Min.X) || NearlyEqual(point.X, bounds.Max.X);
            bool yOk = NearlyEqual(point.Y, bounds.Min.Y) || NearlyEqual(point.Y, bounds.Max.Y);
            bool zOk = NearlyEqual(point.Z, bounds.Min.Z) || NearlyEqual(point.Z, bounds.Max.Z);
            if (!xOk || !yOk || !zOk)
                return false;
        }

        Material material = RepresentativeMaterial(triangles);
        if (!triangles.All(t => SameMaterial(material, t.Material)))
            return false;

        record = GeometryRecord.Cuboid(bounds.Min, bounds.Max, material);
        return true;
    }

    private static bool TryCreateRectangleRecord(IReadOnlyList<Triangle> triangles, out GeometryRecord record)
    {
        record = default;
        if (triangles.Count != 2 || !IsRectanglePair(triangles[0], triangles[1]))
            return false;

        List<Vec3> unique = UniqueVertices(triangles);
        if (unique.Count != 4)
            return false;

        List<Vec3> ordered = OrderRectangleVertices(unique);
        record = GeometryRecord.Rectangle(ordered[0], ordered[1], ordered[2], ordered[3], triangles[0].Material);
        return true;
    }

    private static List<Vec3> OrderRectangleVertices(List<Vec3> vertices)
    {
        Vec3 center = new(vertices.Average(p => p.X), vertices.Average(p => p.Y), vertices.Average(p => p.Z));
        Vec3 normal = (vertices[1] - vertices[0]).Cross(vertices[2] - vertices[0]).Normalize();
        Vec3 axisU = (vertices[0] - center).Normalize();
        if (axisU.Length() < 1e-8)
            axisU = (vertices[1] - center).Normalize();
        Vec3 axisV = normal.Cross(axisU).Normalize();

        return vertices
            .OrderBy(p => Math.Atan2((p - center).Dot(axisV), (p - center).Dot(axisU)))
            .ToList();
    }

    private static List<List<Triangle>> FindConnectedTriangleComponents(IReadOnlyList<Triangle> triangles)
    {
        int count = triangles.Count;
        int[] parent = Enumerable.Range(0, count).ToArray();
        Dictionary<string, int> firstTriangleByVertex = new(StringComparer.Ordinal);

        for (int i = 0; i < count; i++)
        {
            foreach (Vec3 vertex in TriangleVertices(triangles[i]))
            {
                string key = VertexKey(vertex);
                if (firstTriangleByVertex.TryGetValue(key, out int previous))
                    Union(parent, previous, i);
                else
                    firstTriangleByVertex[key] = i;
            }
        }

        Dictionary<int, List<Triangle>> components = new();
        for (int i = 0; i < count; i++)
        {
            int root = Find(parent, i);
            if (!components.TryGetValue(root, out List<Triangle>? list))
            {
                list = new List<Triangle>();
                components[root] = list;
            }
            list.Add(triangles[i]);
        }
        return components.Values.ToList();
    }

    private static int Find(int[] parent, int value)
    {
        while (parent[value] != value)
        {
            parent[value] = parent[parent[value]];
            value = parent[value];
        }
        return value;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);
        if (rootA != rootB)
            parent[rootB] = rootA;
    }

    private static string VertexKey(Vec3 value) =>
        $"{Math.Round(value.X, 6):G17}|{Math.Round(value.Y, 6):G17}|{Math.Round(value.Z, 6):G17}";

    private static Aabb BoundsOf(IEnumerable<Triangle> triangles)
    {
        using IEnumerator<Vec3> points = triangles.SelectMany(TriangleVertices).GetEnumerator();
        if (!points.MoveNext())
            return new Aabb(Vec3.Zero, Vec3.Zero);

        Vec3 min = points.Current;
        Vec3 max = points.Current;
        while (points.MoveNext())
        {
            min = Min(min, points.Current);
            max = Max(max, points.Current);
        }
        return new Aabb(min, max);
    }

    private static List<Vec3> UniqueVertices(IEnumerable<Triangle> triangles)
    {
        List<Vec3> unique = new();
        foreach (Vec3 point in triangles.SelectMany(TriangleVertices))
        {
            if (!unique.Any(existing => SamePoint(existing, point)))
                unique.Add(point);
        }
        return unique;
    }

    private static bool IsRectanglePair(Triangle a, Triangle b)
    {
        if (CountSharedVertices(a, b) != 2)
            return false;

        if (Math.Abs(Math.Abs(a.Normal.Dot(b.Normal)) - 1.0) > 1e-5)
            return false;

        List<Vec3> unique = UniqueVertices(new[] { a, b });
        if (unique.Count != 4)
            return false;

        double[] squaredDistances = new double[6];
        int k = 0;
        for (int i = 0; i < unique.Count; i++)
        {
            for (int j = i + 1; j < unique.Count; j++)
            {
                Vec3 delta = unique[i] - unique[j];
                squaredDistances[k++] = delta.Dot(delta);
            }
        }
        Array.Sort(squaredDistances);

        return squaredDistances[0] > 1e-10 &&
               NearlyEqual(squaredDistances[0], squaredDistances[1]) &&
               NearlyEqual(squaredDistances[2], squaredDistances[3]) &&
               NearlyEqual(squaredDistances[4], squaredDistances[5]) &&
               NearlyEqual(squaredDistances[4], squaredDistances[0] + squaredDistances[2]) &&
               SameMaterial(a.Material, b.Material);
    }

    private static int CountSharedVertices(Triangle a, Triangle b)
    {
        int count = 0;
        foreach (Vec3 pa in TriangleVertices(a))
        {
            foreach (Vec3 pb in TriangleVertices(b))
            {
                if (SamePoint(pa, pb))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    private static IEnumerable<Vec3> TriangleVertices(Triangle triangle)
    {
        yield return triangle.A;
        yield return triangle.B;
        yield return triangle.C;
    }

    private static Material RepresentativeMaterial(SceneObjectGroup group) =>
        group.ColorOverride ?? RepresentativeMaterial(group.LocalTriangles);

    private static Material RepresentativeMaterial(IReadOnlyList<Triangle> triangles) =>
        triangles.Count == 0 ? new Material(new Vec3(0.78, 0.76, 0.72)) : triangles[0].Material;

    private static bool SameMaterial(Material a, Material b) =>
        SameVec(a.Color, b.Color) &&
        NearlyEqual(a.Emission, b.Emission) &&
        SameVec(a.EmissionColor, b.EmissionColor) &&
        NearlyEqual(a.Alpha, b.Alpha) &&
        a.AlphaMode == b.AlphaMode &&
        NearlyEqual(a.AlphaCutoff, b.AlphaCutoff) &&
        a.DoubleSided == b.DoubleSided &&
        NearlyEqual(a.Metallic, b.Metallic) &&
        NearlyEqual(a.Roughness, b.Roughness) &&
        NearlyEqual(a.Transmission, b.Transmission) &&
        NearlyEqual(a.NormalScale, b.NormalScale) &&
        NearlyEqual(a.OcclusionStrength, b.OcclusionStrength) &&
        string.Equals(a.LightId ?? string.Empty, b.LightId ?? string.Empty, StringComparison.Ordinal) &&
        SameTexture(a.Texture, b.Texture) &&
        SameTexture(a.EmissiveTexture, b.EmissiveTexture) &&
        SameTexture(a.MetallicRoughnessTexture, b.MetallicRoughnessTexture) &&
        SameTexture(a.NormalTexture, b.NormalTexture) &&
        SameTexture(a.OcclusionTexture, b.OcclusionTexture);

    private static bool SameTexture(TextureMap? a, TextureMap? b) =>
        string.Equals(a?.Name ?? string.Empty, b?.Name ?? string.Empty, StringComparison.Ordinal) &&
        string.Equals(a?.SourcePath ?? string.Empty, b?.SourcePath ?? string.Empty, StringComparison.Ordinal) &&
        (a?.WrapU ?? TextureAddressMode.Repeat) == (b?.WrapU ?? TextureAddressMode.Repeat) &&
        (a?.WrapV ?? TextureAddressMode.Repeat) == (b?.WrapV ?? TextureAddressMode.Repeat) &&
        NearlyEqual(a?.OffsetU ?? 0.0, b?.OffsetU ?? 0.0) &&
        NearlyEqual(a?.OffsetV ?? 0.0, b?.OffsetV ?? 0.0) &&
        NearlyEqual(a?.ScaleU ?? 1.0, b?.ScaleU ?? 1.0) &&
        NearlyEqual(a?.ScaleV ?? 1.0, b?.ScaleV ?? 1.0) &&
        NearlyEqual(a?.Rotation ?? 0.0, b?.Rotation ?? 0.0);

    private static bool SameVec(Vec3 a, Vec3 b) => SamePoint(a, b);
    private static bool SamePoint(Vec3 a, Vec3 b) => NearlyEqual(a.X, b.X) && NearlyEqual(a.Y, b.Y) && NearlyEqual(a.Z, b.Z);
    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-6;
    private static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    private static void AddBox(SceneObjectGroup group, Vec3 min, Vec3 max, Material material)
    {
        double x0 = min.X, y0 = min.Y, z0 = min.Z, x1 = max.X, y1 = max.Y, z1 = max.Z;
        Vec3 p000 = new(x0, y0, z0), p001 = new(x0, y0, z1), p010 = new(x0, y1, z0), p011 = new(x0, y1, z1);
        Vec3 p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1);

        AddQuad(group, p001, p101, p111, p011, material);
        AddQuad(group, p100, p000, p010, p110, material);
        AddQuad(group, p000, p001, p011, p010, material);
        AddQuad(group, p101, p100, p110, p111, material);
        AddQuad(group, p010, p011, p111, p110, material);
        AddQuad(group, p000, p100, p101, p001, material);
    }

    private static void AddQuad(SceneObjectGroup group, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Material material)
    {
        group.AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), material);
        group.AddTriangle(a, c, d, new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), material);
    }

    private static void WriteVec3Single(BinaryWriter writer, Vec3 value)
    {
        writer.Write((float)value.X);
        writer.Write((float)value.Y);
        writer.Write((float)value.Z);
    }

    private static Vec3 ReadVec3Single(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static void WriteVec2Single(BinaryWriter writer, Vec2 value)
    {
        writer.Write((float)value.U);
        writer.Write((float)value.V);
    }

    private static Vec2 ReadVec2Single(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle());

    private static void WriteVec3(BinaryWriter writer, Vec3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static Vec3 ReadVec3(BinaryReader reader) => new(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());

    private static void WriteVec2(BinaryWriter writer, Vec2 value)
    {
        writer.Write(value.U);
        writer.Write(value.V);
    }

    private static Vec2 ReadVec2(BinaryReader reader) => new(reader.ReadDouble(), reader.ReadDouble());

    private readonly struct GeometryRecord
    {
        public GeometryKind Kind { get; }
        public string? Name { get; }
        public Vec3 Min { get; }
        public Vec3 Max { get; }
        public Vec3 P0 { get; }
        public Vec3 P1 { get; }
        public Vec3 P2 { get; }
        public Vec3 P3 { get; }
        public string? PrimitiveKind { get; }
        public string? PrimitiveSourceName { get; }
        public Material? Material { get; }
        public IReadOnlyList<Triangle> Triangles { get; }

        private GeometryRecord(GeometryKind kind, string? name, Vec3 min, Vec3 max, Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, string? primitiveKind, string? primitiveSourceName, Material? material, IReadOnlyList<Triangle>? triangles)
        {
            Kind = kind;
            Name = name;
            Min = min;
            Max = max;
            P0 = p0;
            P1 = p1;
            P2 = p2;
            P3 = p3;
            PrimitiveKind = primitiveKind;
            PrimitiveSourceName = primitiveSourceName;
            Material = material;
            Triangles = triangles ?? Array.Empty<Triangle>();
        }

        public static GeometryRecord Cuboid(Vec3 min, Vec3 max, Material material) =>
            new(GeometryKind.Cuboid, null, min, max, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, null, null, material, null);

        public static GeometryRecord Rectangle(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, Material material) =>
            new(GeometryKind.Rectangle, null, Vec3.Zero, Vec3.Zero, p0, p1, p2, p3, null, null, material, null);

        public static GeometryRecord ReadyMade(string kind, string? sourceName, Material material) =>
            new(GeometryKind.ReadyMadePrimitive, null, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, kind, sourceName, material, null);

        public static GeometryRecord Mesh(IReadOnlyList<Triangle> triangles) =>
            new(GeometryKind.Mesh, null, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, null, null, null, triangles);
    }
}
