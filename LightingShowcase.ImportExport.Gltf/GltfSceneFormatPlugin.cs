using System.IO;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ImportExport.Gltf;

public sealed class GltfSceneFormatPlugin : ISceneFormatPlugin
{
    public string FormatId => "gltf-glb";
    public string DisplayName => "glTF/GLB";
    public IReadOnlyList<string> Extensions => new[] { ".gltf", ".glb" };
    public bool CanImport => true;
    public bool CanExport => true;
    public bool CarriesLights => true;
    public IReadOnlyList<string> ExportVariants => new[] { "gltf", "glb" };

    public ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options) =>
        GltfSceneIO.LoadIntoScene(scene, filePath, options.FallbackMaterial, options.TargetSize, options.TargetCenter, options.FloorY, options.Progress, options.SimplifyKeepFraction);

    public void Export(Scene scene, string filePath, SceneSaveOptions options) =>
        GltfSceneIO.Save(scene, filePath, binary: string.Equals(Path.GetExtension(filePath), ".glb", StringComparison.OrdinalIgnoreCase) || string.Equals(options.Variant, "glb", StringComparison.OrdinalIgnoreCase));
}
