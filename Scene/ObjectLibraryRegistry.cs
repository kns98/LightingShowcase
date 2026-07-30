// -----------------------------------------------------------------------------
// File: Scene/ObjectLibraryRegistry.cs
// Purpose: Discovers external object-definition DLLs and owns scene insertion.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

public static class ObjectLibraryRegistry
{
    public static string[] Names => ScenePrimitiveRegistry.DisplayNames;

    public static void EnsureInitialized() => ScenePrimitiveRegistry.EnsureInitialized();

    public static bool Contains(string objectName) => ScenePrimitiveRegistry.Contains(objectName);

    public static SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string objectName)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (materials == null) throw new ArgumentNullException(nameof(materials));

        EnsureInitialized();
        string name = string.IsNullOrWhiteSpace(objectName) ? Names.FirstOrDefault() ?? "Object" : objectName.Trim();
        ISceneObjectDefinition definition = ScenePrimitiveRegistry.Find(name)
            ?? ScenePrimitiveRegistry.Primitives.FirstOrDefault()
            ?? throw new InvalidOperationException("No object definitions are available. Build and deploy a LightingShowcase.ObjectLibrary.*.dll next to the application. Add a public class implementing ISceneObjectDefinition to add a new object.");

        scene.BeginGroup(definition.DisplayName);
        Dictionary<string, double> parameters = definition.CreateDefaultParameters();
        definition.Build(materials, parameters, materials.Cushion, (a, b, c, uvA, uvB, uvC, material) => scene.AddTriangle(a, b, c, uvA, uvB, uvC, material));
        SceneObjectGroup group = scene.EndGroup();
        group.PrimitiveKind = definition.Kind;
        group.PrimitiveSourceName = definition.DisplayName;
        group.PrimitiveParameters.Clear();
        foreach (KeyValuePair<string, double> parameter in parameters)
            group.PrimitiveParameters[parameter.Key] = parameter.Value;
        return group;
    }

    public static string ReadyMadeNameForPrimitiveKind(string? primitiveKind, string? sourceName)
    {
        EnsureInitialized();
        if (ScenePrimitiveRegistry.Find(sourceName) is ISceneObjectDefinition fromSource)
            return fromSource.DisplayName;
        if (ScenePrimitiveRegistry.Find(primitiveKind) is ISceneObjectDefinition fromKind)
            return fromKind.DisplayName;
        return !string.IsNullOrWhiteSpace(sourceName) ? sourceName!.Trim() : primitiveKind?.Trim() ?? Names.FirstOrDefault() ?? "Object";
    }

    public static string PrimitiveKindForReadyMade(string? readyMadeName)
    {
        EnsureInitialized();
        if (ScenePrimitiveRegistry.Find(readyMadeName) is ISceneObjectDefinition definition)
            return definition.Kind;
        return string.IsNullOrWhiteSpace(readyMadeName)
            ? "object"
            : readyMadeName.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("/", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    public static void StoreDefaultPrimitiveParametersFromShadow(SceneObjectGroup group)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        group.PrimitiveParameters.Clear();

        if (ScenePrimitiveRegistry.Find(group.PrimitiveKind ?? group.PrimitiveSourceName) is not ISceneObjectDefinition definition)
            return;

        Dictionary<string, double> parameters = definition.CreateParametersFromBounds(group.GetWorldBounds(includeHidden: true));
        foreach (KeyValuePair<string, double> parameter in parameters)
            group.PrimitiveParameters[parameter.Key] = parameter.Value;
    }

    public static bool RebuildPrimitiveShadowGeometry(SceneObjectGroup group, SceneMaterials materials)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (materials == null) throw new ArgumentNullException(nameof(materials));
        if (string.IsNullOrWhiteSpace(group.PrimitiveKind) || group.PrimitiveParameters.Count == 0 || group.Children.Count > 0)
            return false;

        if (ScenePrimitiveRegistry.Find(group.PrimitiveKind) is not ISceneObjectDefinition definition)
            return false;

        Material material = group.FirstMaterialOrDefault() ?? materials.WhiteWall;
        group.LocalTriangles.Clear();
        definition.Build(materials, group.PrimitiveParameters, material,
            (a, b, c, uvA, uvB, uvC, mat) => group.AddTriangle(a, b, c, uvA, uvB, uvC, mat));
        group.RecalculatePivot();
        return true;
    }

}
