using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.PropXml;

public sealed class PropXmlSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "prop-xml";
    public string DisplayName => "Prop XML";
    public IReadOnlyList<string> Extensions => new[] { ".xml", ".prop.xml" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => true;
    public IReadOnlyList<string> ExportVariants => Array.Empty<string>();

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options)
    {
        PropXmlSceneLoader.LoadIntoScene(scene, filePath);
        scene.RebuildWorldGeometry();
        int triangles = scene.ObjectGroups.SelectMany(g => g.SelfAndDescendants()).Sum(g => g.LocalTriangles.Count);
        return new ObjLoadResult(filePath, 0, triangles, triangles);
    }

    public void Export(Scene scene, string filePath, SceneSaveOptions options) => PropXmlSceneSaver.Save(scene, filePath);
}
