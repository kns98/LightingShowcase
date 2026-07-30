using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

public sealed class BuiltInObjectLibraryPlugin : IObjectLibraryPlugin
{
    public string LibraryId => "builtin-objects";
    public string DisplayName => "Built-in Objects";
    public IReadOnlyList<string> ObjectNames => ReadyMadeObjectLibrary.Names;
    public bool Contains(string objectName) => ReadyMadeObjectLibrary.Contains(objectName);
    public SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName) => ReadyMadeObjectLibrary.Insert(scene, materials, objectName);
}
