// -----------------------------------------------------------------------------
// File: Scene/SceneStats.cs
// Purpose: Scene statistics.
//
// Small value type summarizing object, triangle, and light counts for status display.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Compact summary of scene size for status display.</summary>
public readonly record struct SceneStats(int ObjectCount, int TriangleCount, int LightCount)
{
    public override string ToString()
        => $"{ObjectCount:N0} objects, {TriangleCount:N0} triangles, {LightCount:N0} lights";
}
