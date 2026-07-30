using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Fbx;

public sealed class FbxSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "fbx";
    public string DisplayName => "FBX";
    public IReadOnlyList<string> Extensions => new[] { ".fbx" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => new[] { "binary", "ascii" };

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        FbxSceneIO.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) => FbxSceneIO.Save(scene, filePath, options.Variant);
}
