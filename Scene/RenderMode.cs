// -----------------------------------------------------------------------------
// File: Scene/RenderMode.cs
// Purpose: Explicit viewport/final-render diagnostic modes.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>High-level render/debug mode shared by preview and final render pipelines.</summary>
public enum RenderMode
{
    Lit,
    Unlit,
    NormalDebug,
    UvDebug,
    MaterialDebug,
    LightDebug,
    Wireframe,
    BoundingBox,
    Depth
}
