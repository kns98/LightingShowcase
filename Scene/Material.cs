// -----------------------------------------------------------------------------
// File: Scene/Material.cs
// Purpose: Surface material.
//
// Stores color, emission, optional texture, and material values consumed by the ray tracer.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

public enum MaterialAlphaMode
{
    Opaque = 0,
    Mask = 1,
    Blend = 2
}

/// <summary>Surface material definition used by shading and texture lookup.</summary>
public sealed class Material
{
    public Vec3 Color { get; }
    public double Emission { get; }
    public string? LightId { get; }
    public TextureMap? Texture { get; }
    public Vec3 EmissionColor { get; }
    public TextureMap? EmissiveTexture { get; }
    public double Alpha { get; }
    public bool AlphaBlend { get; }
    public double Metallic { get; }
    public double Roughness { get; }
    public double Transmission { get; }
    public TextureMap? MetallicRoughnessTexture { get; }
    public TextureMap? NormalTexture { get; }
    public TextureMap? OcclusionTexture { get; }
    public TextureMap? TransmissionTexture { get; }
    public double NormalScale { get; }
    public double OcclusionStrength { get; }
    public MaterialAlphaMode AlphaMode { get; }
    public double AlphaCutoff { get; }
    public bool DoubleSided { get; }

    /// <summary>Constructs and initializes this component.</summary>
    public Material(
        Vec3 color,
        double emission = 0.0,
        string? lightId = null,
        TextureMap? texture = null,
        Vec3? emissionColor = null,
        TextureMap? emissiveTexture = null,
        double alpha = 1.0,
        bool alphaBlend = false,
        double metallic = 0.0,
        double roughness = 0.72,
        double transmission = 0.0,
        TextureMap? metallicRoughnessTexture = null,
        TextureMap? normalTexture = null,
        TextureMap? occlusionTexture = null,
        double normalScale = 1.0,
        double occlusionStrength = 1.0,
        MaterialAlphaMode alphaMode = MaterialAlphaMode.Opaque,
        double alphaCutoff = 0.5,
        bool doubleSided = false,
        TextureMap? transmissionTexture = null)
    {
        Color = color;
        Emission = emission;
        LightId = lightId;
        Texture = texture;
        EmissionColor = emissionColor ?? new Vec3(1.0, 1.0, 1.0);
        EmissiveTexture = emissiveTexture;
        Alpha = Math.Clamp(alpha, 0.0, 1.0);
        AlphaMode = alphaMode == MaterialAlphaMode.Opaque && (alphaBlend || Alpha < 0.999)
            ? MaterialAlphaMode.Blend
            : alphaMode;
        AlphaBlend = AlphaMode == MaterialAlphaMode.Blend || transmission > 0.0;
        AlphaCutoff = Math.Clamp(alphaCutoff, 0.0, 1.0);
        DoubleSided = doubleSided;
        Metallic = Math.Clamp(metallic, 0.0, 1.0);
        Roughness = Math.Clamp(roughness, 0.02, 1.0);
        Transmission = Math.Clamp(transmission, 0.0, 1.0);
        MetallicRoughnessTexture = metallicRoughnessTexture;
        NormalTexture = normalTexture;
        OcclusionTexture = occlusionTexture;
        TransmissionTexture = transmissionTexture;
        NormalScale = double.IsFinite(normalScale) ? Math.Clamp(normalScale, -8.0, 8.0) : 1.0;
        OcclusionStrength = double.IsFinite(occlusionStrength) ? Math.Clamp(occlusionStrength, 0.0, 1.0) : 1.0;
    }

    /// <summary>Samples the base color/albedo texture in its stored color space.</summary>
    public Vec3 Sample(double u, double v)
    {
        // Preserve the historical sampling contract for editor/raster callers.
        // glTF-aware ray paths should use SampleLinear(), because base-color
        // textures are encoded as sRGB while the factor is already linear.
        return Texture == null ? Color : Texture.Sample(u, v).Multiply(Color);
    }

    /// <summary>Samples glTF base color in linear-light space for physically based shading.</summary>
    public Vec3 SampleLinear(double u, double v)
    {
        if (Texture == null)
            return Color;

        return SrgbToLinear(Texture.Sample(u, v)).Multiply(Color);
    }

    /// <summary>Samples opacity from baseColorFactor alpha and texture alpha.</summary>
    public double SampleAlpha(double u, double v)
    {
        double textureAlpha = Texture?.SampleAlpha(u, v) ?? 1.0;
        return Math.Clamp(Alpha * textureAlpha, 0.0, 1.0);
    }

    /// <summary>Samples metallic and roughness values. glTF stores roughness in G and metallic in B.</summary>
    public (double Metallic, double Roughness) SampleMetallicRoughness(double u, double v)
    {
        double metallic = Metallic;
        double roughness = Roughness;
        if (MetallicRoughnessTexture != null)
        {
            Vec3 mr = MetallicRoughnessTexture.Sample(u, v);
            roughness *= mr.Y;
            metallic *= mr.Z;
        }
        return (Math.Clamp(metallic, 0.0, 1.0), Math.Clamp(roughness, 0.02, 1.0));
    }

    /// <summary>Samples a small normal-map perturbation strength for the current simple ray tracer.</summary>
    public Vec3 SampleNormalMap(double u, double v)
    {
        if (NormalTexture == null)
            return new Vec3(0.5, 0.5, 1.0);
        return NormalTexture.Sample(u, v);
    }

    /// <summary>Samples the self-illumination term using the historical stored-color-space behavior.</summary>
    public Vec3 SampleEmission(double u, double v)
    {
        if (Emission <= 0.0)
            return Vec3.Zero;

        Vec3 emissionSource = EmissiveTexture == null ? Sample(u, v) : EmissiveTexture.Sample(u, v);
        return emissionSource.Multiply(EmissionColor) * Emission;
    }

    /// <summary>Samples glTF emissive data in linear-light space.</summary>
    public Vec3 SampleEmissionLinear(double u, double v)
    {
        if (Emission <= 0.0)
            return Vec3.Zero;

        Vec3 emissionSource = EmissiveTexture == null
            ? SampleLinear(u, v)
            : SrgbToLinear(EmissiveTexture.Sample(u, v));
        return emissionSource.Multiply(EmissionColor) * Emission;
    }

    /// <summary>Samples glTF occlusion, where the red channel attenuates indirect lighting.</summary>
    public double SampleOcclusion(double u, double v)
    {
        if (OcclusionTexture == null)
            return 1.0;

        double sampled = Math.Clamp(OcclusionTexture.Sample(u, v).X, 0.0, 1.0);
        return 1.0 + (sampled - 1.0) * OcclusionStrength;
    }

    private static Vec3 SrgbToLinear(Vec3 value) => new(
        SrgbChannelToLinear(value.X),
        SrgbChannelToLinear(value.Y),
        SrgbChannelToLinear(value.Z));

    private static double SrgbChannelToLinear(double value)
    {
        value = Math.Clamp(value, 0.0, 1.0);
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>Returns a copy with a different base texture while preserving emission data.</summary>
    public Material WithTexture(TextureMap? texture)
    {
        return new Material(
            Color, Emission, LightId, texture, EmissionColor, EmissiveTexture,
            Alpha, AlphaBlend, Metallic, Roughness, Transmission,
            MetallicRoughnessTexture, NormalTexture, OcclusionTexture,
            NormalScale, OcclusionStrength, AlphaMode, AlphaCutoff, DoubleSided,
            TransmissionTexture);
    }
}
