// -----------------------------------------------------------------------------
// File: Lighting/SceneLight.cs
// Purpose: Editable light data.
//
// Represents a scene light with id, shape, position, direction, color,
// intensity, range, spot cone, and enabled state. The properties are mutable
// because the WinForms lighting editor edits existing lights in place and the
// ray tracer reads the latest scene snapshot.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.Lighting;

/// <summary>Single editable scene light consumed by the ray tracer.</summary>
public sealed class SceneLight
{
    public string Id { get; set; }
    public SceneLightKind Kind { get; set; }
    public Vec3 Position { get; set; }
    public Vec3 Direction { get; set; }
    public Vec3 Color { get; set; }
    public double Intensity { get; set; }
    public double Range { get; set; }
    public double InnerConeAngle { get; set; }
    public double OuterConeAngle { get; set; }
    public bool Enabled { get; set; }
    public bool CastsShadow { get; set; }
    public bool IsImported { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>Constructs and initializes this component.</summary>
    public SceneLight(
        string id,
        Vec3 position,
        Vec3 color,
        double intensity,
        bool enabled = true,
        SceneLightKind kind = SceneLightKind.Point,
        Vec3? direction = null,
        double range = 0.0,
        double innerConeAngle = 0.0,
        double outerConeAngle = Math.PI / 4.0,
        bool castsShadow = true,
        bool isImported = false,
        bool isDefault = false)
    {
        Id = string.IsNullOrWhiteSpace(id) ? "light" : id.Trim();
        Kind = kind;
        Position = position;
        Direction = NormalizeDirection(direction ?? new Vec3(0.0, 0.0, -1.0));
        Color = color;
        Intensity = intensity;
        Range = Math.Max(0.0, range);
        InnerConeAngle = Math.Max(0.0, innerConeAngle);
        OuterConeAngle = Math.Max(InnerConeAngle, outerConeAngle);
        Enabled = enabled;
        CastsShadow = castsShadow;
        IsImported = isImported;
        IsDefault = isDefault;
    }

    private static Vec3 NormalizeDirection(Vec3 direction)
    {
        Vec3 normalized = direction.Normalize();
        return normalized.Length() < 1e-8 ? new Vec3(0.0, 0.0, -1.0) : normalized;
    }
}
