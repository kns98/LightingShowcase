// -----------------------------------------------------------------------------
// File: Scene/AssetRegistry.cs
// Purpose: Deduplicated asset table derived from the editable scene.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Renderer/save-load friendly registry of materials and textures referenced by scene geometry.</summary>
public sealed class AssetRegistry
{
    public List<MaterialDefinition> Materials { get; } = new();
    public List<TextureAsset> Textures { get; } = new();

    public static AssetRegistry FromScene(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        AssetRegistry registry = new();
        Dictionary<TextureMap, TextureAsset> textureByRef = new();
        Dictionary<string, MaterialDefinition> materialByKey = new(StringComparer.Ordinal);

        foreach (Triangle triangle in scene.ObjectGroups.SelectMany(g => g.BuildWorldTriangles(includeHidden: true)))
        {
            RegisterTexture(registry, textureByRef, triangle.Material.Texture);
            RegisterTexture(registry, textureByRef, triangle.Material.EmissiveTexture);
            RegisterTexture(registry, textureByRef, triangle.Material.MetallicRoughnessTexture);
            RegisterTexture(registry, textureByRef, triangle.Material.NormalTexture);
            RegisterTexture(registry, textureByRef, triangle.Material.OcclusionTexture);
            RegisterTexture(registry, textureByRef, triangle.Material.TransmissionTexture);

            string key = MaterialKey(triangle.Material);
            if (!materialByKey.ContainsKey(key))
            {
                string id = $"mat_{materialByKey.Count + 1}";
                MaterialDefinition definition = MaterialDefinition.FromMaterial(triangle.Material, id, id);
                materialByKey[key] = definition;
                registry.Materials.Add(definition);
            }
        }

        return registry;
    }

    private static void RegisterTexture(AssetRegistry registry, Dictionary<TextureMap, TextureAsset> byRef, TextureMap? texture)
    {
        if (texture == null || byRef.ContainsKey(texture))
            return;
        TextureAsset asset = TextureAsset.FromTextureMap(texture);
        byRef[texture] = asset;
        registry.Textures.Add(asset);
    }

    private static string MaterialKey(Material material)
    {
        return string.Join("|",
            material.Color.X, material.Color.Y, material.Color.Z,
            material.Emission, material.EmissionColor.X, material.EmissionColor.Y, material.EmissionColor.Z,
            material.Alpha, material.AlphaBlend, material.Metallic, material.Roughness, material.Transmission,
            material.Texture?.Name, material.EmissiveTexture?.Name, material.MetallicRoughnessTexture?.Name,
            material.NormalTexture?.Name, material.OcclusionTexture?.Name, material.TransmissionTexture?.Name,
            material.NormalScale, material.OcclusionStrength,
            material.AlphaMode, material.AlphaCutoff, material.DoubleSided);
    }
}
