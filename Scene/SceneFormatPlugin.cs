// -----------------------------------------------------------------------------
// File: Scene/SceneFormatPlugin.cs
// Purpose: Scene import/export plugin contracts.
//
// All model importers and exporters are reached through this interface so file
// formats can live in separate DLLs instead of being hard-wired into the editor UI.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

public sealed class SceneLoadOptions
{
    public Material FallbackMaterial { get; init; } = new(new Vec3(0.82, 0.82, 0.78));
    public double TargetSize { get; init; } = 2.15;
    public Vec3? TargetCenter { get; init; }
    public double FloorY { get; init; } = -1.48;
    public bool ReplaceScene { get; init; }
    public double? SimplifyKeepFraction { get; init; }
    public Action<ObjLoadProgress>? Progress { get; init; }
}

public sealed class SceneSaveOptions
{
    public string? Variant { get; init; }
}

public interface ISceneFormatPlugin
{
    string FormatId { get; }
    string DisplayName { get; }
    IReadOnlyList<string> Extensions { get; }
    bool CanImport { get; }
    bool CanExport { get; }
    bool CarriesLights { get; }
    IReadOnlyList<string> ExportVariants { get; }

    ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options);
    void Export(Scene scene, string filePath, SceneSaveOptions options);
}
