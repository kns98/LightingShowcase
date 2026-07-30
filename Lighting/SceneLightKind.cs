// -----------------------------------------------------------------------------
// File: Lighting/SceneLightKind.cs
// Purpose: Supported scene light shapes.
// -----------------------------------------------------------------------------

namespace LightingShowcase.Lighting;

/// <summary>Light shape used by imported glTF lights and the ray tracer.</summary>
public enum SceneLightKind
{
    Point,
    Directional,
    Spot
}
