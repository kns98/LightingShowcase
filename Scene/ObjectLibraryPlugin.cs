// -----------------------------------------------------------------------------
// File: Scene/ObjectLibraryPlugin.cs
// Purpose: Discoverable object-library plugin contract.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Provides insertable authored objects from a separately built object-library DLL.</summary>
public interface IObjectLibraryPlugin
{
    string LibraryId { get; }
    string DisplayName { get; }
    IReadOnlyList<string> ObjectNames { get; }
    bool Contains(string objectName);
    SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName);
}
