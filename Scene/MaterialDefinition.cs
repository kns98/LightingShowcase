// -----------------------------------------------------------------------------
// File: Scene/MaterialDefinition.cs
// Purpose: Renderer-independent PBR material record.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Canonical PBR-style material definition used before adapting to a concrete renderer.</summary>
public sealed class MaterialDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Material";
    public Vec3 BaseColor { get; set; } = new(1, 1, 1);
    public string? BaseColorTextureId { get; set; }
    public double Metallic { get; set; }
    public double Roughness { get; set; } = 0.72;
    public string? MetallicRoughnessTextureId { get; set; }
    public string? NormalTextureId { get; set; }
    public double NormalScale { get; set; } = 1.0;
    public string? OcclusionTextureId { get; set; }
    public double OcclusionStrength { get; set; } = 1.0;
    public string? TransmissionTextureId { get; set; }
    public Vec3 EmissiveColor { get; set; } = new(1, 1, 1);
    public string? EmissiveTextureId { get; set; }
    public double EmissiveStrength { get; set; }
    public double Opacity { get; set; } = 1.0;
    public bool AlphaBlend { get; set; }
    public MaterialAlphaMode AlphaMode { get; set; } = MaterialAlphaMode.Opaque;
    public double AlphaCutoff { get; set; } = 0.5;
    public double Transmission { get; set; }
    public bool DoubleSided { get; set; }

    public static MaterialDefinition FromMaterial(Material material, string id, string name)
    {
        if (material == null) throw new ArgumentNullException(nameof(material));
        return new MaterialDefinition
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Name = string.IsNullOrWhiteSpace(name) ? "Material" : name,
            BaseColor = material.Color,
            BaseColorTextureId = material.Texture?.Name,
            Metallic = material.Metallic,
            Roughness = material.Roughness,
            MetallicRoughnessTextureId = material.MetallicRoughnessTexture?.Name,
            NormalTextureId = material.NormalTexture?.Name,
            NormalScale = material.NormalScale,
            OcclusionTextureId = material.OcclusionTexture?.Name,
            OcclusionStrength = material.OcclusionStrength,
            TransmissionTextureId = material.TransmissionTexture?.Name,
            EmissiveColor = material.EmissionColor,
            EmissiveTextureId = material.EmissiveTexture?.Name,
            EmissiveStrength = material.Emission,
            Opacity = material.Alpha,
            AlphaBlend = material.AlphaBlend,
            AlphaMode = material.AlphaMode,
            AlphaCutoff = material.AlphaCutoff,
            Transmission = material.Transmission,
            DoubleSided = material.DoubleSided
        };
    }

    public Material ToMaterial(
        TextureMap? baseColorTexture = null,
        TextureMap? emissiveTexture = null,
        TextureMap? metallicRoughnessTexture = null,
        TextureMap? normalTexture = null,
        TextureMap? occlusionTexture = null,
        TextureMap? transmissionTexture = null)
    {
        return new Material(
            BaseColor, EmissiveStrength, null, baseColorTexture, EmissiveColor, emissiveTexture,
            Opacity, AlphaBlend, Metallic, Roughness, Transmission, metallicRoughnessTexture,
            normalTexture, occlusionTexture, NormalScale, OcclusionStrength, AlphaMode, AlphaCutoff, DoubleSided,
            transmissionTexture);
    }
}
