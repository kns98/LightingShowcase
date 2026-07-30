using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Ply;

public sealed class PlySceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "ply";
    public string DisplayName => "PLY";
    public IReadOnlyList<string> Extensions => new[] { ".ply" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => new[] { "binary", "ascii" };

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        PlySceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        PlySceneSaver.Save(scene, filePath, binary: !string.Equals(options.Variant, "ascii", StringComparison.OrdinalIgnoreCase));
}
