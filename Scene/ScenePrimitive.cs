// -----------------------------------------------------------------------------
// File: Scene/ScenePrimitive.cs
// Purpose: Self-discoverable editable object contract.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>
/// Callback supplied by the core scene layer to external object DLLs. Object
/// definitions emit triangle geometry through this callback instead of receiving
/// Scene or SceneObjectGroup references.
/// </summary>
public delegate void AddTriangleCallback(Vec3 a, Vec3 b, Vec3 c, Vec2 uvA, Vec2 uvB, Vec2 uvC, Material material);

/// <summary>
/// Contract for insertable objects that can emit their own triangle shadow mesh and own
/// the gizmo-to-parameter rules used by the editor. Implement this in an external
/// LightingShowcase.ObjectLibrary.* DLL; the registry discovers it automatically.
/// </summary>
public interface ISceneObjectDefinition
{
    /// <summary>Stable serializer/editor kind, for example "sphere" or "diningTable".</summary>
    string Kind { get; }

    /// <summary>User-facing insert menu name, for example "Sphere".</summary>
    string DisplayName { get; }

    /// <summary>Metadata shown by the editor and used to describe gizmo behavior.</summary>
    PrimitiveGizmoEditMetadata GizmoMetadata { get; }

    /// <summary>Creates the default authored parameters for a newly inserted object.</summary>
    Dictionary<string, double> CreateDefaultParameters();

    /// <summary>Creates authored parameters that fit this object definition into an existing mesh/shadow bounding box.</summary>
    Dictionary<string, double> CreateParametersFromBounds(Aabb bounds);

    /// <summary>Emits the render/pick shadow mesh from authored parameters through the supplied triangle callback.</summary>
    void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> parameters, Material material, AddTriangleCallback addTriangle);

    /// <summary>Applies a live gizmo move to authored parameters.</summary>
    bool ApplyMoveDelta(IDictionary<string, double> parameters, Vec3 delta);

    /// <summary>Applies a live gizmo scale to authored parameters.</summary>
    bool ApplyScaleDelta(IDictionary<string, double> parameters, char axis, double factor);

    /// <summary>Commits accumulated object transform preview values back into authored parameters.</summary>
    bool ApplyPendingTransform(IDictionary<string, double> parameters, Vec3 position, Vec3 scale);
}


/// <summary>Backward-compatible alias for older primitive plugin classes.</summary>
public interface IScenePrimitive : ISceneObjectDefinition
{
}
