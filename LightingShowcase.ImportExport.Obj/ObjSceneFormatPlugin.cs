using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Obj;

public sealed class ObjSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "obj";
    public string DisplayName => "Wavefront OBJ";
    public IReadOnlyList<string> Extensions => new[] { ".obj" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => false;
    public IReadOnlyList<string> ExportVariants => Array.Empty<string>();

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        ObjSceneLoader.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress, options.SimplifyKeepFraction);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        ObjSceneSaver.Save(scene, filePath);
}
