// -----------------------------------------------------------------------------
// File: Scene/GltfSceneIO.cs
// Purpose: glTF/GLB import and export with scene lights.
//
// Provides a lightweight built-in glTF 2.0 reader/writer focused on the subset
// this editor needs: triangle meshes, node names/transforms, basic materials,
// and KHR_lights_punctual point lights. This avoids adding a large dependency
// while still giving the application a widely supported external format that can
// carry lighting data.
// -----------------------------------------------------------------------------

using System.IO;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Imports and exports glTF/GLB scenes, including KHR_lights_punctual lights.</summary>
public static class GltfSceneIO
{
    private const uint GlbMagic = 0x46546C67; // glTF
    private const uint JsonChunkType = 0x4E4F534A; // JSON
    private const uint BinChunkType = 0x004E4942; // BIN\0
    private static readonly JsonDocument EmptyArrayDocument = JsonDocument.Parse("[]");

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
        progress?.Invoke(new ObjLoadProgress("Reading glTF", 5, 0, 0, 0));
        GltfDocument doc = ReadDocument(filePath);
        using JsonDocument json = JsonDocument.Parse(doc.Json);
        JsonElement root = json.RootElement;

        List<byte[]> buffers = LoadBuffers(root, doc, filePath);
        List<GltfMaterial> materials = ReadMaterials(root, buffers, filePath, fallbackMaterial);
        List<ImportedLight> lights = ReadLights(root);

        int vertexCount = 0;
        int faceCount = 0;
        int triangleCount = 0;
        int sceneIndex = root.TryGetProperty("scene", out JsonElement sceneProp) && sceneProp.ValueKind == JsonValueKind.Number ? sceneProp.GetInt32() : 0;
        JsonElement scenes = GetArray(root, "scenes");
        JsonElement nodes = GetArray(root, "nodes");
        JsonElement meshesArray = GetArray(root, "meshes");

        // Compute bounds before building triangles so glTF imports get the same
        // fit-to-editor transform as OBJ/3DS imports.  Without this, many real
        // glTF samples stay in authoring units far from the default ray-trace
        // lights, making the render look unlit even while Helix's built-in
        // directional lights still show the model.
        Vec3 boundsMin = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vec3 boundsMax = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        bool hasBounds = false;
        TraverseSceneRoots(TraverseBounds);
        if (!hasBounds)
            throw new InvalidDataException("glTF file does not contain any mesh positions.");

        Vec3 boundsSize = boundsMax - boundsMin;
        double largestAxis = Math.Max(boundsSize.X, Math.Max(boundsSize.Y, boundsSize.Z));
        if (largestAxis < 1e-8)
            throw new InvalidDataException("glTF model bounds are degenerate.");

        double importScale = targetSize / largestAxis;
        Vec3 sourceCenter = (boundsMin + boundsMax) * 0.5;
        Vec3 desiredCenter = targetCenter ?? new Vec3(0.0, 0.0, 3.45);
        double scaledMinY = (boundsMin.Y - sourceCenter.Y) * importScale + desiredCenter.Y;
        Vec3 importOffset = new(desiredCenter.X, desiredCenter.Y + (floorY - scaledMinY), desiredCenter.Z);

        Vec3 emissiveCenterSum = Vec3.Zero;
        Vec3 emissiveColorSum = Vec3.Zero;
        int emissiveTriangleCount = 0;
        double strongestEmission = 0.0;

        TraverseSceneRoots(TraverseNode);

        if (lights.Count == 0 && emissiveTriangleCount > 0)
        {
            // Some glTF samples, including common lantern assets, do not contain
            // KHR_lights_punctual lights.  They rely on emissiveTexture instead.
            // The ray tracer is not a global-illumination/path tracer, so a purely
            // emissive surface would look bright but would not illuminate nearby
            // geometry.  Add one editable helper point light at the emissive mesh
            // centroid to give the expected practical-lantern effect.
            scene.Lights.RemoveAll(l => l.Id.StartsWith("gltf_emissive_", StringComparison.OrdinalIgnoreCase));
            Vec3 lightPosition = emissiveCenterSum / emissiveTriangleCount;
            Vec3 lightColor = emissiveColorSum / emissiveTriangleCount;
            double intensity = Math.Max(2.0, strongestEmission * 5.0);
            scene.Lights.Add(new SceneLight("gltf_emissive_light", lightPosition, lightColor, intensity, true, SceneLightKind.Point, range: targetSize * 1.8, isImported: true));
        }

        if (lights.Count > 0)
        {
            scene.Lights.RemoveAll(l => l.Id.Equals("ceiling", StringComparison.OrdinalIgnoreCase) || l.Id.Equals("lamp", StringComparison.OrdinalIgnoreCase));
            foreach (ImportedLight light in lights)
                scene.Lights.Add(new SceneLight(
                    light.Id,
                    light.Position,
                    light.Color,
                    light.Intensity,
                    light.Enabled,
                    light.Kind,
                    light.Direction,
                    light.Range,
                    light.InnerConeAngle,
                    light.OuterConeAngle,
                    isImported: true));
        }
        else if (scene.Lights.Count == 0)
        {
            scene.Lights.Add(new SceneLight("gltf_default_key", new Vec3(2.5, 4.0, -3.0), new Vec3(1.0, 0.96, 0.88), 5.0, isDefault: true));
            scene.Lights.Add(new SceneLight("gltf_default_fill", new Vec3(-3.0, 2.2, 2.0), new Vec3(0.75, 0.85, 1.0), 2.2, isDefault: true));
        }

        if (simplifyKeepFraction.HasValue && simplifyKeepFraction.Value < 0.999)
        {
            progress?.Invoke(new ObjLoadProgress("Simplifying glTF mesh", 90, vertexCount, faceCount, triangleCount));
            foreach (SceneObjectGroup group in scene.ObjectGroups)
                group.SimplifyGeometry(simplifyKeepFraction.Value);
            triangleCount = scene.ObjectGroups.Sum(g => g.CountLocalTrianglesRecursively());
        }

        scene.RebuildWorldGeometry();
        progress?.Invoke(new ObjLoadProgress("Finished glTF", 100, vertexCount, faceCount, triangleCount));
        return new ObjLoadResult(filePath, vertexCount, faceCount, triangleCount);

        void TraverseSceneRoots(Action<int, Matrix4x4> visitor)
        {
            if (scenes.GetArrayLength() > 0 && sceneIndex >= 0 && sceneIndex < scenes.GetArrayLength() && scenes[sceneIndex].TryGetProperty("nodes", out JsonElement rootNodes))
            {
                foreach (JsonElement nodeIndexEl in rootNodes.EnumerateArray())
                    visitor(nodeIndexEl.GetInt32(), Matrix4x4.Identity);
            }
            else
            {
                for (int i = 0; i < nodes.GetArrayLength(); i++)
                    visitor(i, Matrix4x4.Identity);
            }
        }

        void TraverseBounds(int nodeIndex, Matrix4x4 parent)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.GetArrayLength())
                return;

            JsonElement node = nodes[nodeIndex];
            Matrix4x4 world = GetNodeTransform(node) * parent;
            if (node.TryGetProperty("mesh", out JsonElement meshEl))
            {
                int meshIndex = meshEl.GetInt32();
                if (meshIndex >= 0 && meshIndex < meshesArray.GetArrayLength())
                    ExpandMeshBounds(meshesArray[meshIndex], world);
            }

            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    TraverseBounds(child.GetInt32(), world);
            }
        }

        void ExpandMeshBounds(JsonElement mesh, Matrix4x4 world)
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitives))
                return;

            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out JsonElement modeEl) ? modeEl.GetInt32() : 4;
                if (mode != 4 || !primitive.TryGetProperty("attributes", out JsonElement attributes) || !attributes.TryGetProperty("POSITION", out JsonElement posEl))
                    continue;

                foreach (Vec3 position in ReadVec3Accessor(root, buffers, posEl.GetInt32(), world))
                    ExpandBounds(position);
            }
        }

        void ExpandBounds(Vec3 position)
        {
            boundsMin = Min(boundsMin, position);
            boundsMax = Max(boundsMax, position);
            hasBounds = true;
        }

        Vec3 NormalizePosition(Vec3 position) => (position - sourceCenter) * importScale + importOffset;

        void TraverseNode(int nodeIndex, Matrix4x4 parent)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.GetArrayLength())
                return;

            JsonElement node = nodes[nodeIndex];
            Matrix4x4 world = GetNodeTransform(node) * parent;
            string nodeName = node.TryGetProperty("name", out JsonElement nameEl) ? SanitizeName(nameEl.GetString(), $"gltf_node_{nodeIndex}") : $"gltf_node_{nodeIndex}";

            if (node.TryGetProperty("extensions", out JsonElement nodeExt) &&
                nodeExt.TryGetProperty("KHR_lights_punctual", out JsonElement lightRef) &&
                lightRef.TryGetProperty("light", out JsonElement lightIndexEl))
            {
                int lightIndex = lightIndexEl.GetInt32();
                if (lightIndex >= 0 && lightIndex < lights.Count)
                {
                    Vector3 transformed = Vector3.Transform(Vector3.Zero, world);
                    Vector3 direction = Vector3.TransformNormal(new Vector3(0.0f, 0.0f, -1.0f), world);
                    Vec3 normalizedDirection = new Vec3(direction.X, direction.Y, direction.Z).Normalize();
                    lights[lightIndex] = lights[lightIndex] with
                    {
                        Position = NormalizePosition(new Vec3(transformed.X, transformed.Y, transformed.Z)),
                        Direction = normalizedDirection.Length() < 1e-8 ? new Vec3(0.0, 0.0, -1.0) : normalizedDirection,
                        Range = lights[lightIndex].Range > 0.0 ? lights[lightIndex].Range * importScale : 0.0,
                        Id = nodeName
                    };
                }
            }

            if (node.TryGetProperty("mesh", out JsonElement meshEl))
            {
                int meshIndex = meshEl.GetInt32();
                if (meshIndex >= 0 && meshIndex < meshesArray.GetArrayLength())
                    ImportMesh(meshesArray[meshIndex], nodeName, world);
            }

            if (node.TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement child in children.EnumerateArray())
                    TraverseNode(child.GetInt32(), world);
            }
        }

        void ImportMesh(JsonElement mesh, string nodeName, Matrix4x4 world)
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitives))
                return;

            int primitiveIndex = 0;
            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode = primitive.TryGetProperty("mode", out JsonElement modeEl) ? modeEl.GetInt32() : 4;
                if (mode != 4 || !primitive.TryGetProperty("attributes", out JsonElement attributes) || !attributes.TryGetProperty("POSITION", out JsonElement posEl))
                    continue;

                List<Vec3> positions = ReadVec3Accessor(root, buffers, posEl.GetInt32(), world)
                    .Select(NormalizePosition)
                    .ToList();
                List<Vec3> normals = attributes.TryGetProperty("NORMAL", out JsonElement normalAccessorEl)
                    ? ReadNormalAccessor(root, buffers, normalAccessorEl.GetInt32(), world)
                    : new List<Vec3>();
                int materialIndex = primitive.TryGetProperty("material", out JsonElement materialEl) ? materialEl.GetInt32() : -1;
                GltfMaterial? gltfMaterial = materialIndex >= 0 && materialIndex < materials.Count ? materials[materialIndex] : null;
                string uvAttributeName = $"TEXCOORD_{Math.Max(0, gltfMaterial?.BaseColorTexCoord ?? 0)}";
                bool hasUv = attributes.TryGetProperty(uvAttributeName, out JsonElement uvEl) ||
                    attributes.TryGetProperty("TEXCOORD_0", out uvEl);
                List<Vec2> uvs = hasUv
                    ? ReadVec2Accessor(root, buffers, uvEl.GetInt32())
                    : new List<Vec2>();
                List<Vec3> vertexColors = attributes.TryGetProperty("COLOR_0", out JsonElement colorAccessorEl)
                    ? ReadColorAccessor(root, buffers, colorAccessorEl.GetInt32())
                    : new List<Vec3>();
                List<int> indices = primitive.TryGetProperty("indices", out JsonElement indicesEl)
                    ? ReadIndexAccessor(root, buffers, indicesEl.GetInt32())
                    : Enumerable.Range(0, positions.Count).ToList();

                Material material = gltfMaterial?.Material ?? fallbackMaterial;
                string groupName = primitiveIndex == 0 ? nodeName : $"{nodeName}_{primitiveIndex + 1}";
                scene.BeginGroup(groupName);
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    if (!IsValidIndex(ia, positions.Count) || !IsValidIndex(ib, positions.Count) || !IsValidIndex(ic, positions.Count))
                        continue;

                    Vec2 uva = ia < uvs.Count ? uvs[ia] : new Vec2(0, 0);
                    Vec2 uvb = ib < uvs.Count ? uvs[ib] : new Vec2(1, 0);
                    Vec2 uvc = ic < uvs.Count ? uvs[ic] : new Vec2(0, 1);
                    Material triangleMaterial = material;
                    if (ia < vertexColors.Count && ib < vertexColors.Count && ic < vertexColors.Count)
                    {
                        Vec3 averageVertexColor = (vertexColors[ia] + vertexColors[ib] + vertexColors[ic]) / 3.0;
                        triangleMaterial = new Material(
                            material.Color.Multiply(averageVertexColor), material.Emission, material.LightId, material.Texture,
                            material.EmissionColor, material.EmissiveTexture, material.Alpha, material.AlphaBlend,
                            material.Metallic, material.Roughness, material.Transmission, material.MetallicRoughnessTexture,
                            material.NormalTexture, material.OcclusionTexture, material.NormalScale, material.OcclusionStrength,
                            material.AlphaMode, material.AlphaCutoff, material.DoubleSided);
                    }
                    if (ia < normals.Count && ib < normals.Count && ic < normals.Count)
                    {
                        scene.AddTriangle(
                            positions[ia], positions[ib], positions[ic],
                            uva, uvb, uvc,
                            normals[ia], normals[ib], normals[ic],
                            triangleMaterial);
                    }
                    else
                    {
                        scene.AddTriangle(positions[ia], positions[ib], positions[ic], uva, uvb, uvc, triangleMaterial);
                    }
                    if (triangleMaterial.Emission > 0.0 && triangleMaterial.EmissiveTexture != null)
                    {
                        Vec3 centroid = (positions[ia] + positions[ib] + positions[ic]) / 3.0;
                        Vec3 emissiveSample = (triangleMaterial.SampleEmissionLinear(uva.U, uva.V) + triangleMaterial.SampleEmissionLinear(uvb.U, uvb.V) + triangleMaterial.SampleEmissionLinear(uvc.U, uvc.V)) / 3.0;
                        double luminance = 0.2126 * emissiveSample.X + 0.7152 * emissiveSample.Y + 0.0722 * emissiveSample.Z;
                        if (luminance > 0.02)
                        {
                            emissiveCenterSum += centroid;
                            emissiveColorSum += emissiveSample;
                            strongestEmission = Math.Max(strongestEmission, luminance);
                            emissiveTriangleCount++;
                        }
                    }
                    triangleCount++;
                }
                scene.EndGroup();
                vertexCount += positions.Count;
                faceCount += indices.Count / 3;
                primitiveIndex++;
            }
        }
    }

    public static void Save(Scene scene, string filePath, bool binary)
    {
        ExportBuild build = BuildExport(scene);
        if (binary)
            WriteGlb(build, filePath);
        else
            WriteGltf(build, filePath);
    }

    private static ExportBuild BuildExport(Scene scene)
    {
        List<byte> bin = new();
        List<object> bufferViews = new();
        List<object> accessors = new();
        List<object> meshes = new();
        List<object> nodes = new();
        List<int> rootNodes = new();
        List<object> materials = new();
        Dictionary<string, int> materialIds = new(StringComparer.Ordinal);

        foreach (SceneObjectGroup group in scene.ObjectGroups)
        {
            if (!group.Visible)
                continue;

            List<Triangle> tris = group.BuildWorldTriangles().ToList();
            if (tris.Count == 0)
                continue;

            Dictionary<string, List<Triangle>> byMaterial = tris.GroupBy(t => MaterialKey(t.Material)).ToDictionary(g => g.Key, g => g.ToList());
            List<object> primitives = new();
            foreach (KeyValuePair<string, List<Triangle>> entry in byMaterial)
            {
                string key = entry.Key;
                List<Triangle> materialTris = entry.Value;
                Material material = materialTris[0].Material;
                if (!materialIds.TryGetValue(key, out int materialId))
                {
                    materialId = materials.Count;
                    materialIds[key] = materialId;
                    materials.Add(new Dictionary<string, object?>
                    {
                        ["name"] = $"mat_{materialId + 1}",
                        ["pbrMetallicRoughness"] = new Dictionary<string, object?>
                        {
                            ["baseColorFactor"] = new[] { Clamp01(material.Color.X), Clamp01(material.Color.Y), Clamp01(material.Color.Z), 1.0 },
                            ["metallicFactor"] = 0.0,
                            ["roughnessFactor"] = 0.72
                        },
                        ["emissiveFactor"] = material.Emission > 0.0 ? new[] { Clamp01(material.Color.X * material.Emission), Clamp01(material.Color.Y * material.Emission), Clamp01(material.Color.Z * material.Emission) } : new[] { 0.0, 0.0, 0.0 }
                    });
                }

                int vertexCount = materialTris.Count * 3;
                float[] positions = new float[vertexCount * 3];
                float[] texcoords = new float[vertexCount * 2];
                uint[] indices = new uint[vertexCount];
                Vec3 min = materialTris[0].A;
                Vec3 max = materialTris[0].A;
                int v = 0;
                foreach (Triangle tri in materialTris)
                {
                    WriteVertex(tri.A, tri.UvA);
                    WriteVertex(tri.B, tri.UvB);
                    WriteVertex(tri.C, tri.UvC);
                }

                int posAccessor = AddFloatAccessor(bin, bufferViews, accessors, positions, "VEC3", min, max);
                int uvAccessor = AddFloatAccessor(bin, bufferViews, accessors, texcoords, "VEC2", null, null);
                int indexAccessor = AddUIntAccessor(bin, bufferViews, accessors, indices);
                primitives.Add(new Dictionary<string, object?>
                {
                    ["attributes"] = new Dictionary<string, object?> { ["POSITION"] = posAccessor, ["TEXCOORD_0"] = uvAccessor },
                    ["indices"] = indexAccessor,
                    ["material"] = materialId,
                    ["mode"] = 4
                });

                void WriteVertex(Vec3 p, Vec2 uv)
                {
                    positions[v * 3] = (float)p.X;
                    positions[v * 3 + 1] = (float)p.Y;
                    positions[v * 3 + 2] = (float)p.Z;
                    texcoords[v * 2] = (float)uv.U;
                    texcoords[v * 2 + 1] = (float)uv.V;
                    indices[v] = (uint)v;
                    min = Min(min, p);
                    max = Max(max, p);
                    v++;
                }
            }

            int meshIndex = meshes.Count;
            meshes.Add(new Dictionary<string, object?> { ["name"] = group.Name, ["primitives"] = primitives });
            int nodeIndex = nodes.Count;
            nodes.Add(new Dictionary<string, object?> { ["name"] = group.Name, ["mesh"] = meshIndex });
            rootNodes.Add(nodeIndex);
        }

        List<object> lightDefs = new();
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;

            int lightIndex = lightDefs.Count;
            Dictionary<string, object?> lightDef = new()
            {
                ["name"] = light.Id,
                ["type"] = LightTypeName(light.Kind),
                ["color"] = new[] { Clamp01(light.Color.X), Clamp01(light.Color.Y), Clamp01(light.Color.Z) },
                ["intensity"] = Math.Max(0.0, light.Intensity)
            };
            if (light.Range > 0.0 && light.Kind != SceneLightKind.Directional)
                lightDef["range"] = light.Range;
            if (light.Kind == SceneLightKind.Spot)
            {
                lightDef["spot"] = new Dictionary<string, object?>
                {
                    ["innerConeAngle"] = Math.Max(0.0, light.InnerConeAngle),
                    ["outerConeAngle"] = Math.Max(light.InnerConeAngle, light.OuterConeAngle)
                };
            }
            lightDefs.Add(lightDef);

            int nodeIndex = nodes.Count;
            Dictionary<string, object?> node = new()
            {
                ["name"] = light.Id,
                ["extensions"] = new Dictionary<string, object?>
                {
                    ["KHR_lights_punctual"] = new Dictionary<string, object?> { ["light"] = lightIndex }
                }
            };
            if (light.Kind != SceneLightKind.Directional)
                node["translation"] = new[] { light.Position.X, light.Position.Y, light.Position.Z };
            double[]? rotation = RotationFromMinusZ(light.Direction);
            if (rotation != null)
                node["rotation"] = rotation;
            nodes.Add(node);
            rootNodes.Add(nodeIndex);
        }

        Dictionary<string, object?> root = new()
        {
            ["asset"] = new Dictionary<string, object?> { ["version"] = "2.0", ["generator"] = "LightingShowcase" },
            ["scene"] = 0,
            ["scenes"] = new[] { new Dictionary<string, object?> { ["name"] = scene.Description, ["nodes"] = rootNodes } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materials,
            ["buffers"] = new[] { new Dictionary<string, object?> { ["byteLength"] = bin.Count } },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors,
            ["extensionsUsed"] = lightDefs.Count > 0 ? new[] { "KHR_lights_punctual" } : Array.Empty<string>(),
            ["extensions"] = lightDefs.Count > 0
                ? new Dictionary<string, object?> { ["KHR_lights_punctual"] = new Dictionary<string, object?> { ["lights"] = lightDefs } }
                : null
        };
        return new ExportBuild(root, bin.ToArray());
    }

    private static void WriteGltf(ExportBuild build, string filePath)
    {
        string binName = Path.GetFileNameWithoutExtension(filePath) + ".bin";
        byte[] bin = build.Bin;
        Dictionary<string, object?> root = new(build.Root)
        {
            ["buffers"] = new[] { new Dictionary<string, object?> { ["byteLength"] = bin.Length, ["uri"] = binName } }
        };
        string json = JsonSerializer.Serialize(root, JsonOptions());
        File.WriteAllText(filePath, json, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, binName), bin);
    }

    private static void WriteGlb(ExportBuild build, string filePath)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(build.Root, JsonOptions()));
        jsonBytes = Pad(jsonBytes, 0x20);
        byte[] binBytes = Pad(build.Bin, 0x00);
        uint length = (uint)(12 + 8 + jsonBytes.Length + 8 + binBytes.Length);
        using BinaryWriter writer = new(File.Create(filePath), Encoding.UTF8);
        writer.Write(GlbMagic);
        writer.Write((uint)2);
        writer.Write(length);
        writer.Write((uint)jsonBytes.Length);
        writer.Write(JsonChunkType);
        writer.Write(jsonBytes);
        writer.Write((uint)binBytes.Length);
        writer.Write(BinChunkType);
        writer.Write(binBytes);
    }

    private static GltfDocument ReadDocument(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension == ".gltf")
            return new GltfDocument(File.ReadAllText(filePath), null);

        using BinaryReader reader = new(File.OpenRead(filePath), Encoding.UTF8);
        uint magic = reader.ReadUInt32();
        uint version = reader.ReadUInt32();
        uint length = reader.ReadUInt32();
        if (magic != GlbMagic || version != 2 || length > reader.BaseStream.Length)
            throw new InvalidDataException("Invalid GLB header.");

        string? json = null;
        byte[]? bin = null;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int chunkLength = checked((int)reader.ReadUInt32());
            uint chunkType = reader.ReadUInt32();
            byte[] chunk = reader.ReadBytes(chunkLength);
            if (chunkType == JsonChunkType)
                json = Encoding.UTF8.GetString(chunk).TrimEnd('\0', ' ', '\r', '\n', '\t');
            else if (chunkType == BinChunkType)
                bin = chunk;
        }
        return new GltfDocument(json ?? throw new InvalidDataException("GLB JSON chunk missing."), bin);
    }

    private static List<byte[]> LoadBuffers(JsonElement root, GltfDocument doc, string filePath)
    {
        List<byte[]> result = new();
        JsonElement buffers = GetArray(root, "buffers");
        string baseDirectory = Path.GetDirectoryName(filePath) ?? string.Empty;
        for (int i = 0; i < buffers.GetArrayLength(); i++)
        {
            JsonElement buffer = buffers[i];
            if (i == 0 && doc.BinaryChunk != null && !buffer.TryGetProperty("uri", out _))
            {
                result.Add(doc.BinaryChunk);
                continue;
            }

            string? uri = buffer.TryGetProperty("uri", out JsonElement uriEl) ? uriEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(uri))
                throw new InvalidDataException("External glTF buffer URI is missing.");
            result.Add(ReadUriBytes(uri, baseDirectory));
        }
        return result;
    }

    private static byte[] ReadUriBytes(string uri, string baseDirectory)
    {
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = uri.IndexOf(',');
            if (comma < 0) throw new InvalidDataException("Invalid data URI.");
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        return File.ReadAllBytes(Path.Combine(baseDirectory, Uri.UnescapeDataString(uri)));
    }

    private static List<GltfMaterial> ReadMaterials(JsonElement root, List<byte[]> buffers, string sceneFilePath, Material fallback)
    {
        List<GltfMaterial> result = new();
        JsonElement materials = GetArray(root, "materials");
        Dictionary<int, TextureMap?> textureCache = new();
        for (int i = 0; i < materials.GetArrayLength(); i++)
        {
            Vec3 color = fallback.Color;
            double emission = fallback.Emission;
            Vec3 emissionColor = new(1.0, 1.0, 1.0);
            double alpha = 1.0;
            bool alphaBlend = false;
            MaterialAlphaMode alphaMode = MaterialAlphaMode.Opaque;
            double alphaCutoff = 0.5;
            bool doubleSided = false;
            double metallic = 1.0;
            double roughness = 1.0;
            double transmission = 0.0;
            TextureMap? texture = null;
            TextureMap? emissiveTexture = null;
            TextureMap? metallicRoughnessTexture = null;
            TextureMap? normalTexture = null;
            TextureMap? occlusionTexture = null;
            double normalScale = 1.0;
            double occlusionStrength = 1.0;
            int baseColorTexCoord = 0;
            JsonElement mat = materials[i];
            string alphaModeName = mat.TryGetProperty("alphaMode", out JsonElement alphaModeEl) ? alphaModeEl.GetString() ?? "OPAQUE" : "OPAQUE";
            alphaMode = alphaModeName.ToUpperInvariant() switch
            {
                "MASK" => MaterialAlphaMode.Mask,
                "BLEND" => MaterialAlphaMode.Blend,
                _ => MaterialAlphaMode.Opaque
            };
            alphaBlend = alphaMode == MaterialAlphaMode.Blend;
            if (mat.TryGetProperty("alphaCutoff", out JsonElement alphaCutoffEl))
                alphaCutoff = alphaCutoffEl.GetDouble();
            doubleSided = mat.TryGetProperty("doubleSided", out JsonElement doubleSidedEl) && doubleSidedEl.GetBoolean();
            if (mat.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr))
            {
                if (pbr.TryGetProperty("baseColorFactor", out JsonElement baseColor) && baseColor.GetArrayLength() >= 3)
                {
                    color = new Vec3(baseColor[0].GetDouble(), baseColor[1].GetDouble(), baseColor[2].GetDouble());
                    if (baseColor.GetArrayLength() >= 4)
                    {
                        alpha = baseColor[3].GetDouble();
                    }
                }

                metallic = pbr.TryGetProperty("metallicFactor", out JsonElement metallicEl) ? metallicEl.GetDouble() : metallic;
                roughness = pbr.TryGetProperty("roughnessFactor", out JsonElement roughnessEl) ? roughnessEl.GetDouble() : roughness;

                if (pbr.TryGetProperty("metallicRoughnessTexture", out JsonElement mrTexture) &&
                    mrTexture.TryGetProperty("index", out JsonElement mrTextureIndexEl))
                {
                    int textureIndex = mrTextureIndexEl.GetInt32();
                    if (!textureCache.TryGetValue(textureIndex, out metallicRoughnessTexture))
                    {
                        metallicRoughnessTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                        textureCache[textureIndex] = metallicRoughnessTexture;
                    }
                    metallicRoughnessTexture = ApplyTextureTransform(metallicRoughnessTexture, mrTexture);
                }

                // Most real glTF samples, including DamagedHelmet/Sponza-style assets,
                // carry their visible color in baseColorTexture rather than only in
                // baseColorFactor.  Earlier builds ignored this, so those files loaded
                // as flat grey/white geometry even though their UVs were present.
                if (pbr.TryGetProperty("baseColorTexture", out JsonElement baseColorTexture) &&
                    baseColorTexture.TryGetProperty("index", out JsonElement textureIndexEl))
                {
                    baseColorTexCoord = ReadTextureCoordSet(baseColorTexture);
                    int textureIndex = textureIndexEl.GetInt32();
                    if (!textureCache.TryGetValue(textureIndex, out texture))
                    {
                        texture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                        textureCache[textureIndex] = texture;
                    }
                    texture = ApplyTextureTransform(texture, baseColorTexture);

                    // The current renderer treats a texture as the material's visible
                    // color source.  Avoid accidentally tinting a loaded texture by an
                    // arbitrary fallback material color when the file has no explicit
                    // factor.  Keep explicit baseColorFactor above when supplied.
                    if (!pbr.TryGetProperty("baseColorFactor", out _))
                        color = new Vec3(1, 1, 1);
                }
            }
            if (mat.TryGetProperty("emissiveFactor", out JsonElement emissive) && emissive.ValueKind == JsonValueKind.Array && emissive.GetArrayLength() >= 3)
            {
                emissionColor = new Vec3(emissive[0].GetDouble(), emissive[1].GetDouble(), emissive[2].GetDouble());
                emission = Math.Max(emissionColor.X, Math.Max(emissionColor.Y, emissionColor.Z)) > 0.0 ? 1.0 : 0.0;
            }

            if (mat.TryGetProperty("emissiveTexture", out JsonElement emissiveTextureEl) &&
                emissiveTextureEl.TryGetProperty("index", out JsonElement emissiveTextureIndexEl))
            {
                int textureIndex = emissiveTextureIndexEl.GetInt32();
                if (!textureCache.TryGetValue(textureIndex, out emissiveTexture))
                {
                    emissiveTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                    textureCache[textureIndex] = emissiveTexture;
                }
                emissiveTexture = ApplyTextureTransform(emissiveTexture, emissiveTextureEl);

            }

            if (mat.TryGetProperty("normalTexture", out JsonElement normalTextureEl) &&
                normalTextureEl.TryGetProperty("index", out JsonElement normalTextureIndexEl))
            {
                int textureIndex = normalTextureIndexEl.GetInt32();
                if (!textureCache.TryGetValue(textureIndex, out normalTexture))
                {
                    normalTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                    textureCache[textureIndex] = normalTexture;
                }
                normalTexture = ApplyTextureTransform(normalTexture, normalTextureEl);
                if (normalTextureEl.TryGetProperty("scale", out JsonElement normalScaleEl))
                    normalScale = normalScaleEl.GetDouble();
            }

            if (mat.TryGetProperty("occlusionTexture", out JsonElement occlusionTextureEl) &&
                occlusionTextureEl.TryGetProperty("index", out JsonElement occlusionTextureIndexEl))
            {
                int textureIndex = occlusionTextureIndexEl.GetInt32();
                if (!textureCache.TryGetValue(textureIndex, out occlusionTexture))
                {
                    occlusionTexture = TryReadTexture(root, buffers, sceneFilePath, textureIndex);
                    textureCache[textureIndex] = occlusionTexture;
                }
                occlusionTexture = ApplyTextureTransform(occlusionTexture, occlusionTextureEl);
                if (occlusionTextureEl.TryGetProperty("strength", out JsonElement occlusionStrengthEl))
                    occlusionStrength = occlusionStrengthEl.GetDouble();
            }

            if (mat.TryGetProperty("extensions", out JsonElement matExt))
            {
                if (matExt.TryGetProperty("KHR_materials_transmission", out JsonElement transmissionExt) &&
                    transmissionExt.TryGetProperty("transmissionFactor", out JsonElement transmissionEl))
                {
                    transmission = transmissionEl.GetDouble();
                }
                if (matExt.TryGetProperty("KHR_materials_ior", out JsonElement iorExt) &&
                    iorExt.TryGetProperty("ior", out JsonElement _))
                {
                    // The current renderer uses a practical transmission approximation rather than refraction.
                }
            }

            result.Add(new GltfMaterial(new Material(
                color, emission, texture: texture, emissionColor: emissionColor, emissiveTexture: emissiveTexture,
                alpha: alphaMode == MaterialAlphaMode.Opaque ? 1.0 : alpha, alphaBlend: alphaBlend, metallic: metallic, roughness: roughness, transmission: transmission,
                metallicRoughnessTexture: metallicRoughnessTexture, normalTexture: normalTexture, occlusionTexture: occlusionTexture,
                normalScale: normalScale, occlusionStrength: occlusionStrength, alphaMode: alphaMode,
                alphaCutoff: alphaCutoff, doubleSided: doubleSided), baseColorTexCoord));
        }
        return result;
    }

    private static TextureMap? TryReadTexture(JsonElement root, List<byte[]> buffers, string sceneFilePath, int textureIndex)
    {
        try
        {
            JsonElement textures = GetArray(root, "textures");
            JsonElement images = GetArray(root, "images");
            if (textureIndex < 0 || textureIndex >= textures.GetArrayLength())
                return null;

            JsonElement texture = textures[textureIndex];
            if (!texture.TryGetProperty("source", out JsonElement sourceEl))
                return null;
            TextureAddressMode wrapS = TextureAddressMode.Repeat;
            TextureAddressMode wrapT = TextureAddressMode.Repeat;
            if (texture.TryGetProperty("sampler", out JsonElement samplerEl))
            {
                JsonElement samplers = GetArray(root, "samplers");
                int samplerIndex = samplerEl.GetInt32();
                if (samplerIndex >= 0 && samplerIndex < samplers.GetArrayLength())
                {
                    JsonElement sampler = samplers[samplerIndex];
                    wrapS = sampler.TryGetProperty("wrapS", out JsonElement wrapSEl) ? ToTextureAddressMode(wrapSEl.GetInt32()) : TextureAddressMode.Repeat;
                    wrapT = sampler.TryGetProperty("wrapT", out JsonElement wrapTEl) ? ToTextureAddressMode(wrapTEl.GetInt32()) : TextureAddressMode.Repeat;
                }
            }

            int imageIndex = sourceEl.GetInt32();
            if (imageIndex < 0 || imageIndex >= images.GetArrayLength())
                return null;

            JsonElement image = images[imageIndex];
            string baseDirectory = Path.GetDirectoryName(sceneFilePath) ?? string.Empty;
            string name = image.TryGetProperty("name", out JsonElement nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString())
                ? nameEl.GetString()!.Trim()
                : $"gltf_texture_{textureIndex + 1}";

            if (image.TryGetProperty("uri", out JsonElement uriEl))
            {
                string? uri = uriEl.GetString();
                if (string.IsNullOrWhiteSpace(uri))
                    return null;

                if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    return TextureMap.FromBytes(name, ReadDataUriBytes(uri), null).WithAddressing(wrapS, wrapT);

                string imagePath = Path.Combine(baseDirectory, Uri.UnescapeDataString(uri));
                return File.Exists(imagePath) ? TextureMap.FromFile(imagePath).WithAddressing(wrapS, wrapT) : null;
            }

            if (image.TryGetProperty("bufferView", out JsonElement viewEl))
            {
                byte[] bytes = ReadBufferViewBytes(root, buffers, viewEl.GetInt32());
                return TextureMap.FromBytes(name, bytes, null).WithAddressing(wrapS, wrapT);
            }
        }
        catch
        {
            // Keep loading the mesh if one texture is unsupported or corrupt.
        }
        return null;
    }

    private static TextureMap? ApplyTextureTransform(TextureMap? texture, JsonElement textureInfo)
    {
        if (texture == null ||
            !textureInfo.TryGetProperty("extensions", out JsonElement extensions) ||
            !extensions.TryGetProperty("KHR_texture_transform", out JsonElement transform))
            return texture;

        double offsetU = 0.0;
        double offsetV = 0.0;
        double scaleU = 1.0;
        double scaleV = 1.0;
        double rotation = 0.0;

        if (transform.TryGetProperty("offset", out JsonElement offset) && offset.ValueKind == JsonValueKind.Array && offset.GetArrayLength() >= 2)
        {
            offsetU = offset[0].GetDouble();
            offsetV = offset[1].GetDouble();
        }
        if (transform.TryGetProperty("scale", out JsonElement scale) && scale.ValueKind == JsonValueKind.Array && scale.GetArrayLength() >= 2)
        {
            scaleU = scale[0].GetDouble();
            scaleV = scale[1].GetDouble();
        }
        if (transform.TryGetProperty("rotation", out JsonElement rotationEl))
            rotation = rotationEl.GetDouble();

        return texture.WithTextureTransform(offsetU, offsetV, scaleU, scaleV, rotation);
    }

    private static int ReadTextureCoordSet(JsonElement textureInfo)
    {
        int texCoord = textureInfo.TryGetProperty("texCoord", out JsonElement texCoordEl)
            ? Math.Max(0, texCoordEl.GetInt32())
            : 0;

        if (textureInfo.TryGetProperty("extensions", out JsonElement extensions) &&
            extensions.TryGetProperty("KHR_texture_transform", out JsonElement transform) &&
            transform.TryGetProperty("texCoord", out JsonElement transformTexCoordEl))
        {
            texCoord = Math.Max(0, transformTexCoordEl.GetInt32());
        }

        return texCoord;
    }

    private static TextureAddressMode ToTextureAddressMode(int gltfWrap) => gltfWrap switch
    {
        33071 => TextureAddressMode.ClampToEdge,
        33648 => TextureAddressMode.MirroredRepeat,
        _ => TextureAddressMode.Repeat
    };

    private static byte[] ReadDataUriBytes(string uri)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0)
            throw new InvalidDataException("Invalid data URI.");
        return Convert.FromBase64String(uri[(comma + 1)..]);
    }

    private static byte[] ReadBufferViewBytes(JsonElement root, List<byte[]> buffers, int bufferViewIndex)
    {
        JsonElement bufferViews = GetArray(root, "bufferViews");
        if (bufferViewIndex < 0 || bufferViewIndex >= bufferViews.GetArrayLength())
            throw new InvalidDataException("Invalid glTF image bufferView index.");
        JsonElement view = bufferViews[bufferViewIndex];
        int buffer = view.TryGetProperty("buffer", out JsonElement bufferEl) ? bufferEl.GetInt32() : 0;
        int offset = view.TryGetProperty("byteOffset", out JsonElement offsetEl) ? offsetEl.GetInt32() : 0;
        int length = view.GetProperty("byteLength").GetInt32();
        byte[] bytes = new byte[length];
        Buffer.BlockCopy(buffers[buffer], offset, bytes, 0, length);
        return bytes;
    }

    private static List<ImportedLight> ReadLights(JsonElement root)
    {
        List<ImportedLight> result = new();
        if (!root.TryGetProperty("extensions", out JsonElement ext) || !ext.TryGetProperty("KHR_lights_punctual", out JsonElement lightExt) || !lightExt.TryGetProperty("lights", out JsonElement lights))
            return result;

        int index = 0;
        foreach (JsonElement light in lights.EnumerateArray())
        {
            string id = light.TryGetProperty("name", out JsonElement nameEl) ? SanitizeName(nameEl.GetString(), $"gltf_light_{index + 1}") : $"gltf_light_{index + 1}";
            string type = light.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? "point" : "point";
            SceneLightKind kind = type switch
            {
                "directional" => SceneLightKind.Directional,
                "spot" => SceneLightKind.Spot,
                _ => SceneLightKind.Point
            };

            Vec3 color = new(1, 1, 1);
            if (light.TryGetProperty("color", out JsonElement colorEl) && colorEl.ValueKind == JsonValueKind.Array && colorEl.GetArrayLength() >= 3)
                color = new Vec3(colorEl[0].GetDouble(), colorEl[1].GetDouble(), colorEl[2].GetDouble());

            double intensity = light.TryGetProperty("intensity", out JsonElement intensityEl) ? intensityEl.GetDouble() : 1.0;
            double range = light.TryGetProperty("range", out JsonElement rangeEl) ? Math.Max(0.0, rangeEl.GetDouble()) : 0.0;
            double innerConeAngle = 0.0;
            double outerConeAngle = Math.PI / 4.0;
            if (kind == SceneLightKind.Spot && light.TryGetProperty("spot", out JsonElement spotEl))
            {
                if (spotEl.TryGetProperty("innerConeAngle", out JsonElement innerEl))
                    innerConeAngle = Math.Max(0.0, innerEl.GetDouble());
                if (spotEl.TryGetProperty("outerConeAngle", out JsonElement outerEl))
                    outerConeAngle = Math.Max(innerConeAngle, outerEl.GetDouble());
            }

            result.Add(new ImportedLight(id, kind, Vec3.Zero, new Vec3(0.0, 0.0, -1.0), color, intensity, range, innerConeAngle, outerConeAngle, true));
            index++;
        }
        return result;
    }

    private static List<Vec3> ReadVec3Accessor(JsonElement root, List<byte[]> buffers, int accessorIndex, Matrix4x4 transform)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        List<Vec3> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            float x = ReadFloat(buffers[info.Buffer], offset);
            float y = ReadFloat(buffers[info.Buffer], offset + 4);
            float z = ReadFloat(buffers[info.Buffer], offset + 8);
            Vector3 v = Vector3.Transform(new Vector3(x, y, z), transform);
            values.Add(new Vec3(v.X, v.Y, v.Z));
        }
        return values;
    }

    private static List<Vec3> ReadNormalAccessor(JsonElement root, List<byte[]> buffers, int accessorIndex, Matrix4x4 transform)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        if (info.Type != "VEC3")
            throw new NotSupportedException($"Expected VEC3 glTF normals, got {info.Type}.");

        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse))
            inverse = Matrix4x4.Identity;
        Matrix4x4 normalTransform = Matrix4x4.Transpose(inverse);

        int componentSize = ComponentByteSize(info.ComponentType);
        List<Vec3> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            float x = (float)ReadAccessorComponent(buffers[info.Buffer], offset, info.ComponentType, normalized: true);
            float y = (float)ReadAccessorComponent(buffers[info.Buffer], offset + componentSize, info.ComponentType, normalized: true);
            float z = (float)ReadAccessorComponent(buffers[info.Buffer], offset + componentSize * 2, info.ComponentType, normalized: true);
            Vector3 transformed = Vector3.TransformNormal(new Vector3(x, y, z), normalTransform);
            if (transformed.LengthSquared() > 1e-20f)
                transformed = Vector3.Normalize(transformed);
            values.Add(new Vec3(transformed.X, transformed.Y, transformed.Z));
        }
        return values;
    }

    private static List<Vec2> ReadVec2Accessor(JsonElement root, List<byte[]> buffers, int accessorIndex)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        if (info.Type != "VEC2")
            throw new NotSupportedException($"Expected VEC2 glTF texture coordinates, got {info.Type}.");

        int componentSize = ComponentByteSize(info.ComponentType);
        List<Vec2> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            double u = ReadAccessorComponent(buffers[info.Buffer], offset, info.ComponentType, normalized: true);
            double v = ReadAccessorComponent(buffers[info.Buffer], offset + componentSize, info.ComponentType, normalized: true);
            values.Add(new Vec2(u, v));
        }
        return values;
    }

    private static List<Vec3> ReadColorAccessor(JsonElement root, List<byte[]> buffers, int accessorIndex)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        List<Vec3> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            double r = ReadAccessorComponent(buffers[info.Buffer], offset, info.ComponentType, normalized: true);
            double g = ReadAccessorComponent(buffers[info.Buffer], offset + ComponentByteSize(info.ComponentType), info.ComponentType, normalized: true);
            double b = ReadAccessorComponent(buffers[info.Buffer], offset + ComponentByteSize(info.ComponentType) * 2, info.ComponentType, normalized: true);
            values.Add(new Vec3(r, g, b));
        }
        return values;
    }

    private static List<int> ReadIndexAccessor(JsonElement root, List<byte[]> buffers, int accessorIndex)
    {
        AccessorInfo info = GetAccessorInfo(root, accessorIndex);
        List<int> values = new(info.Count);
        for (int i = 0; i < info.Count; i++)
        {
            int offset = info.Offset + i * info.Stride;
            int value = info.ComponentType switch
            {
                5121 => buffers[info.Buffer][offset],
                5123 => BitConverter.ToUInt16(buffers[info.Buffer], offset),
                5125 => checked((int)BitConverter.ToUInt32(buffers[info.Buffer], offset)),
                _ => throw new NotSupportedException($"Unsupported glTF index component type {info.ComponentType}.")
            };
            values.Add(value);
        }
        return values;
    }

    private static AccessorInfo GetAccessorInfo(JsonElement root, int accessorIndex)
    {
        JsonElement accessors = GetArray(root, "accessors");
        JsonElement bufferViews = GetArray(root, "bufferViews");
        JsonElement accessor = accessors[accessorIndex];
        int bufferViewIndex = accessor.GetProperty("bufferView").GetInt32();
        JsonElement view = bufferViews[bufferViewIndex];
        int componentType = accessor.GetProperty("componentType").GetInt32();
        string type = accessor.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() ?? "SCALAR" : "SCALAR";
        int componentCount = type switch { "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => 1 };
        int componentSize = componentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new NotSupportedException($"Unsupported glTF component type {componentType}.") };
        int stride = view.TryGetProperty("byteStride", out JsonElement strideEl) ? strideEl.GetInt32() : componentSize * componentCount;
        int offset = (view.TryGetProperty("byteOffset", out JsonElement viewOffset) ? viewOffset.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out JsonElement accessorOffset) ? accessorOffset.GetInt32() : 0);
        int buffer = view.TryGetProperty("buffer", out JsonElement bufferEl) ? bufferEl.GetInt32() : 0;
        return new AccessorInfo(buffer, offset, stride, accessor.GetProperty("count").GetInt32(), componentType, type);
    }

    private static Matrix4x4 GetNodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out JsonElement matrixEl) && matrixEl.GetArrayLength() == 16)
        {
            float[] m = matrixEl.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        }

        Vector3 translation = Vector3.Zero;
        Vector3 scale = Vector3.One;
        Quaternion rotation = Quaternion.Identity;
        if (node.TryGetProperty("translation", out JsonElement t) && t.GetArrayLength() >= 3)
            translation = new Vector3((float)t[0].GetDouble(), (float)t[1].GetDouble(), (float)t[2].GetDouble());
        if (node.TryGetProperty("scale", out JsonElement s) && s.GetArrayLength() >= 3)
            scale = new Vector3((float)s[0].GetDouble(), (float)s[1].GetDouble(), (float)s[2].GetDouble());
        if (node.TryGetProperty("rotation", out JsonElement r) && r.GetArrayLength() >= 4)
            rotation = new Quaternion((float)r[0].GetDouble(), (float)r[1].GetDouble(), (float)r[2].GetDouble(), (float)r[3].GetDouble());
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }

    private static int AddFloatAccessor(List<byte> bin, List<object> views, List<object> accessors, float[] values, string type, Vec3? min, Vec3? max)
    {
        Align(bin, 4);
        int offset = bin.Count;
        foreach (float value in values)
            bin.AddRange(BitConverter.GetBytes(value));
        int view = views.Count;
        views.Add(new Dictionary<string, object?> { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = values.Length * 4 });
        int accessor = accessors.Count;
        Dictionary<string, object?> acc = new()
        {
            ["bufferView"] = view,
            ["componentType"] = 5126,
            ["count"] = values.Length / (type == "VEC3" ? 3 : type == "VEC2" ? 2 : 1),
            ["type"] = type
        };
        if (type == "VEC3" && min.HasValue && max.HasValue)
        {
            acc["min"] = new[] { min.Value.X, min.Value.Y, min.Value.Z };
            acc["max"] = new[] { max.Value.X, max.Value.Y, max.Value.Z };
        }
        accessors.Add(acc);
        return accessor;
    }

    private static int AddUIntAccessor(List<byte> bin, List<object> views, List<object> accessors, uint[] values)
    {
        Align(bin, 4);
        int offset = bin.Count;
        foreach (uint value in values)
            bin.AddRange(BitConverter.GetBytes(value));
        int view = views.Count;
        views.Add(new Dictionary<string, object?> { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = values.Length * 4 });
        int accessor = accessors.Count;
        accessors.Add(new Dictionary<string, object?> { ["bufferView"] = view, ["componentType"] = 5125, ["count"] = values.Length, ["type"] = "SCALAR" });
        return accessor;
    }

    private static double ReadAccessorComponent(byte[] data, int offset, int componentType, bool normalized)
    {
        return componentType switch
        {
            5120 => normalized ? Math.Max(-1.0, (sbyte)data[offset] / 127.0) : (sbyte)data[offset],
            5121 => normalized ? data[offset] / 255.0 : data[offset],
            5122 => normalized ? Math.Max(-1.0, BitConverter.ToInt16(data, offset) / 32767.0) : BitConverter.ToInt16(data, offset),
            5123 => normalized ? BitConverter.ToUInt16(data, offset) / 65535.0 : BitConverter.ToUInt16(data, offset),
            5125 => normalized ? BitConverter.ToUInt32(data, offset) / 4294967295.0 : BitConverter.ToUInt32(data, offset),
            5126 => BitConverter.ToSingle(data, offset),
            _ => throw new NotSupportedException($"Unsupported glTF accessor component type {componentType}.")
        };
    }

    private static int ComponentByteSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new NotSupportedException($"Unsupported glTF accessor component type {componentType}.")
    };

    private static JsonElement GetArray(JsonElement root, string name) => root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array ? value : EmptyArrayDocument.RootElement;
    private static float ReadFloat(byte[] data, int offset) => BitConverter.ToSingle(data, offset);
    private static bool IsValidIndex(int index, int count) => index >= 0 && index < count;
    private static string SanitizeName(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
    private static string MaterialKey(Material m) => string.Create(CultureInfo.InvariantCulture, $"{m.Color.X:F6},{m.Color.Y:F6},{m.Color.Z:F6},{m.Emission:F6},{m.LightId}");
    private static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
    private static void Align(List<byte> bytes, int boundary) { while (bytes.Count % boundary != 0) bytes.Add(0); }
    private static byte[] Pad(byte[] bytes, byte pad) { int padded = (bytes.Length + 3) & ~3; if (padded == bytes.Length) return bytes; byte[] result = new byte[padded]; Array.Copy(bytes, result, bytes.Length); for (int i = bytes.Length; i < result.Length; i++) result[i] = pad; return result; }
    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static string LightTypeName(SceneLightKind kind) => kind switch
    {
        SceneLightKind.Directional => "directional",
        SceneLightKind.Spot => "spot",
        _ => "point"
    };

    private static double[]? RotationFromMinusZ(Vec3 direction)
    {
        Vec3 target = direction.Normalize();
        if (target.Length() < 1e-8)
            return null;

        Vec3 source = new(0.0, 0.0, -1.0);
        double dot = Math.Clamp(source.Dot(target), -1.0, 1.0);
        if (dot > 0.999999)
            return null;

        Vec3 axis = source.Cross(target);
        if (axis.Length() < 1e-8)
            axis = new Vec3(0.0, 1.0, 0.0);
        axis = axis.Normalize();
        double angle = Math.Acos(dot);
        double s = Math.Sin(angle / 2.0);
        return new[] { axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(angle / 2.0) };
    }

    private sealed record GltfDocument(string Json, byte[]? BinaryChunk);
    private sealed record GltfMaterial(Material Material, int BaseColorTexCoord);
    private sealed record ImportedLight(string Id, SceneLightKind Kind, Vec3 Position, Vec3 Direction, Vec3 Color, double Intensity, double Range, double InnerConeAngle, double OuterConeAngle, bool Enabled);
    private sealed record AccessorInfo(int Buffer, int Offset, int Stride, int Count, int ComponentType, string Type);
    private sealed record ExportBuild(Dictionary<string, object?> Root, byte[] Bin);
}
