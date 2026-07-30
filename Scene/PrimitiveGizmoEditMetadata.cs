// -----------------------------------------------------------------------------
// File: Scene/PrimitiveGizmoEditMetadata.cs
// Purpose: Describes how gizmo edits map onto authored primitive parameters.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>
/// Editor metadata for a parametric primitive.  This keeps gizmo behavior data-driven:
/// moving changes the authored origin, scaling changes dimensions/radii, and the
/// triangle mesh remains only a regenerated shadow representation.
/// </summary>
public sealed class PrimitiveGizmoEditMetadata
{
    public static readonly PrimitiveGizmoEditMetadata MeshFallback = new(
        "Mesh fallback",
        moveUpdatesOrigin: false,
        scaleRule: "Scale transform is baked into mesh geometry",
        rotationRule: "Rotation transform is baked into mesh geometry");

    public PrimitiveGizmoEditMetadata(string displayName, bool moveUpdatesOrigin, string scaleRule, string rotationRule)
    {
        DisplayName = displayName;
        MoveUpdatesOrigin = moveUpdatesOrigin;
        ScaleRule = scaleRule;
        RotationRule = rotationRule;
    }

    public string DisplayName { get; }
    public bool MoveUpdatesOrigin { get; }
    public string ScaleRule { get; }
    public string RotationRule { get; }

    public override string ToString() => $"{DisplayName}; move: {(MoveUpdatesOrigin ? "origin" : "transform")}; scale: {ScaleRule}; rotate: {RotationRule}";
}
