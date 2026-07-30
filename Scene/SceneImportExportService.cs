// -----------------------------------------------------------------------------
// File: Scene/SceneImportExportService.cs
// Purpose: Scene import/export application service.
//
// Phase 1 architecture cleanup: file-format discovery, filters, load/save calls,
// and export variants are centralized here instead of being hard-coded in the UI.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Coordinates model/scene import and export through the plugin registry.</summary>
public sealed class SceneImportExportService
{
    private readonly Scene scene;
    private readonly SceneMaterials materials = new();

    public SceneImportExportService(Scene scene)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        SceneFormatRegistry.EnsureInitialized();
    }

    public string OpenDialogFilter => BuildOpenFilter(includePropXml: true);
    public string InsertDialogFilter => BuildOpenFilter(includePropXml: false);
    public string SaveDialogFilter => BuildSaveFilter();

    public bool IsSupportedDropFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return IsSupportedModelExtension(extension) || IsPropXmlFile(filePath);
    }

    public bool IsSupportedModelFile(string filePath) => IsSupportedModelExtension(Path.GetExtension(filePath));

    public ObjLoadResult OpenModel(string filePath, Action<ObjLoadProgress>? progress = null) => OpenModel(filePath, simplifyKeepFraction: null, progress);

    /// <summary>Opens a model as a replacement scene and reduces mesh detail during import when requested.</summary>
    public ObjLoadResult OpenModelSimplified(string filePath, double keepFraction, Action<ObjLoadProgress>? progress = null) => OpenModel(filePath, Math.Clamp(keepFraction, 0.02, 1.0), progress);

    private ObjLoadResult OpenModel(string filePath, double? simplifyKeepFraction, Action<ObjLoadProgress>? progress)
    {
        ISceneFormatPlugin plugin = SceneFormatRegistry.FindImporter(filePath);
        scene.Clear();
        if (!plugin.CarriesLights)
            AddDefaultObjectViewingLights();

        ObjLoadResult result = plugin.Import(scene, filePath, new SceneLoadOptions
        {
            FallbackMaterial = materials.WhiteWall,
            TargetSize = 4.75,
            TargetCenter = new Vec3(0.0, 0.0, 3.55),
            FloorY = -1.45,
            ReplaceScene = true,
            SimplifyKeepFraction = simplifyKeepFraction,
            Progress = progress
        });

        string suffix = simplifyKeepFraction.HasValue ? $"simplified to {result.TriangleCount} triangles" : $"{result.TriangleCount} triangles";
        scene.SetDescription($"{plugin.DisplayName}: {Path.GetFileName(filePath)} ({suffix})");
        return result;
    }

    public ObjLoadResult InsertModel(string filePath, Action<ObjLoadProgress>? progress = null)
    {
        ISceneFormatPlugin plugin = SceneFormatRegistry.FindImporter(filePath);
        ObjLoadResult result = plugin.Import(scene, filePath, new SceneLoadOptions
        {
            FallbackMaterial = materials.WhiteWall,
            Progress = progress
        });

        scene.SetDescription($"Scene with inserted {plugin.DisplayName}: {Path.GetFileName(filePath)} ({result.TriangleCount} triangles)");
        return result;
    }

    public void OpenPropXml(string filePath)
    {
        scene.LoadPropXmlFile(filePath);
    }

    public string NormalizeExportFileName(string fileName, int filterIndex)
    {
        ExportFilterChoice choice = GetExportChoice(filterIndex);
        if (string.IsNullOrWhiteSpace(choice.Extension))
            return fileName;

        if (choice.Extension.Equals(".prop.xml", StringComparison.OrdinalIgnoreCase))
        {
            if (!fileName.EndsWith(".prop.xml", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetExtension(fileName).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                return fileName + ".prop.xml";
            return fileName;
        }

        string typedExtension = Path.GetExtension(fileName);
        return typedExtension.Equals(choice.Extension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : Path.ChangeExtension(fileName, choice.Extension);
    }

    public void Save(string fileName, int filterIndex)
    {
        ExportFilterChoice choice = GetExportChoice(filterIndex);
        SceneFormatRegistry.Export(scene, fileName, new SceneSaveOptions { Variant = choice.Variant });
    }

    private bool IsSupportedModelExtension(string extension) =>
        !string.IsNullOrWhiteSpace(extension) &&
        !extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
        SceneFormatRegistry.IsImportExtension(extension);

    private static bool IsPropXmlFile(string filePath) =>
        filePath.EndsWith(".prop.xml", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(filePath).Equals(".xml", StringComparison.OrdinalIgnoreCase);

    private void AddDefaultObjectViewingLights()
    {
        scene.Lights.Add(new LightingShowcase.Lighting.SceneLight("ceiling", new Vec3(0.0, 3.25, -0.50), new Vec3(1.0, 0.96, 0.88), 5.2));
        scene.Lights.Add(new LightingShowcase.Lighting.SceneLight("lamp", new Vec3(-3.15, 1.30, 1.55), new Vec3(1.0, 0.78, 0.52), 3.8));
    }

    private static string BuildOpenFilter(bool includePropXml)
    {
        SceneFormatRegistry.EnsureInitialized();
        List<string> modelExtensions = SceneFormatRegistry.Importers
            .SelectMany(p => p.Extensions)
            .Select(NormalizeExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string allModelPattern = string.Join(";", modelExtensions.Select(e => "*" + e));
        List<string> parts = new();
        if (includePropXml)
            parts.Add($"Supported scene/model files ({allModelPattern};*.prop.xml;*.xml)|{allModelPattern};*.prop.xml;*.xml");
        else
            parts.Add($"Supported 3D models ({allModelPattern})|{allModelPattern}");

        foreach (ISceneFormatPlugin plugin in SceneFormatRegistry.Importers)
        {
            string pattern = string.Join(";", plugin.Extensions.Select(e => "*" + NormalizeExtension(e)));
            parts.Add($"{plugin.DisplayName} ({pattern})|{pattern}");
        }

        if (includePropXml)
            parts.Add("Prop XML (*.prop.xml)|*.prop.xml|XML files (*.xml)|*.xml");
        parts.Add("All files (*.*)|*.*");
        return string.Join("|", parts);
    }

    private static string BuildSaveFilter()
    {
        List<string> parts = ExportChoices
            .Where(c => c.Extension.Length > 0)
            .Select(c => $"{c.DisplayName} (*{c.Extension})|*{c.Extension}")
            .ToList();
        parts.Add("All files (*.*)|*.*");
        return string.Join("|", parts);
    }

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();

    private static ExportFilterChoice GetExportChoice(int filterIndex)
    {
        int index = filterIndex - 1;
        return index >= 0 && index < ExportChoices.Count
            ? ExportChoices[index]
            : new ExportFilterChoice("Typed extension", string.Empty, null);
    }

    private static readonly IReadOnlyList<ExportFilterChoice> ExportChoices = new[]
    {
        new ExportFilterChoice("Prop XML", ".prop.xml", null),
        new ExportFilterChoice("Wavefront OBJ", ".obj", null),
        new ExportFilterChoice("Binary STL", ".stl", "binary"),
        new ExportFilterChoice("ASCII STL", ".stl", "ascii"),
        new ExportFilterChoice("Binary PLY", ".ply", "binary"),
        new ExportFilterChoice("ASCII PLY", ".ply", "ascii"),
        new ExportFilterChoice("3DS files", ".3ds", null),
        new ExportFilterChoice("FBX Binary", ".fbx", "binary"),
        new ExportFilterChoice("FBX ASCII", ".fbx", "ascii"),
        new ExportFilterChoice("glTF JSON with lights", ".gltf", "gltf"),
        new ExportFilterChoice("GLB binary with lights", ".glb", "glb"),
        new ExportFilterChoice("XML files", ".xml", null)
    };

    private readonly record struct ExportFilterChoice(string DisplayName, string Extension, string? Variant);
}
