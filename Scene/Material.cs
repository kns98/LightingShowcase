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
        TextureMap? normalTexture = null)
    {
        Color = color;
        Emission = emission;
        LightId = lightId;
        Texture = texture;
        EmissionColor = emissionColor ?? new Vec3(1.0, 1.0, 1.0);
        EmissiveTexture = emissiveTexture;
        Alpha = Math.Clamp(alpha, 0.0, 1.0);
        AlphaBlend = alphaBlend || Alpha < 0.999 || transmission > 0.0;
        Metallic = Math.Clamp(metallic, 0.0, 1.0);
        Roughness = Math.Clamp(roughness, 0.02, 1.0);
        Transmission = Math.Clamp(transmission, 0.0, 1.0);
        MetallicRoughnessTexture = metallicRoughnessTexture;
        NormalTexture = normalTexture;
    }

    /// <summary>Samples the base color/albedo texture.</summary>
    public Vec3 Sample(double u, double v)
    {
        // glTF baseColor is texture * baseColorFactor.  Keep texture-only
        // materials bright by importing their color factor as white, but still
        // allow explicit tint factors and vertex colors to affect ray rendering.
        return Texture == null ? Color : Texture.Sample(u, v).Multiply(Color);
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

    /// <summary>Samples the self-illumination term used by glTF emissive materials.</summary>
    public Vec3 SampleEmission(double u, double v)
    {
        if (Emission <= 0.0)
            return Vec3.Zero;

        // For legacy/editor emissive materials, the visible surface color is the
        // emitter color.  For glTF emissiveTexture, the separate emissive atlas is
        // the mask/color and emissiveFactor is the multiplier.
        Vec3 emissionSource = EmissiveTexture == null ? Sample(u, v) : EmissiveTexture.Sample(u, v);
        return emissionSource.Multiply(EmissionColor) * Emission;
    }

    /// <summary>Returns a copy with a different base texture while preserving emission data.</summary>
    public Material WithTexture(TextureMap? texture)
    {
        return new Material(Color, Emission, LightId, texture, EmissionColor, EmissiveTexture, Alpha, AlphaBlend, Metallic, Roughness, Transmission, MetallicRoughnessTexture, NormalTexture);
    }
}
