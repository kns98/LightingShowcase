// -----------------------------------------------------------------------------
// File: Scene/SceneRenderData.cs
// Purpose: Shared renderer input built from the canonical scene.
// -----------------------------------------------------------------------------

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

public sealed record RenderMeshInstance(int ObjectId, string ObjectName, IReadOnlyList<Triangle> Triangles, bool Visible, Aabb Bounds);
public sealed record RenderLight(string Id, SceneLightKind Kind, Vec3 Position, Vec3 Direction, Vec3 Color, double Intensity, double Range, double InnerConeAngle, double OuterConeAngle, bool Enabled, bool CastsShadow, bool IsImported, bool IsDefault);
public sealed record RenderMaterial(string Id, MaterialDefinition Definition);

/// <summary>Flattened, renderer-independent snapshot consumed by both preview and final render adapters.</summary>
public sealed class SceneRenderData
{
    public IReadOnlyList<RenderMeshInstance> Meshes { get; }
    public IReadOnlyList<RenderLight> Lights { get; }
    public IReadOnlyList<RenderMaterial> Materials { get; }
    public AssetRegistry Assets { get; }
    public RenderSettings Settings { get; }
    public Aabb? Bounds { get; }

    public SceneRenderData(IReadOnlyList<RenderMeshInstance> meshes, IReadOnlyList<RenderLight> lights, IReadOnlyList<RenderMaterial> materials, AssetRegistry assets, RenderSettings settings, Aabb? bounds)
    {
        Meshes = meshes;
        Lights = lights;
        Materials = materials;
        Assets = assets;
        Settings = settings;
        Bounds = bounds;
    }
}

/// <summary>Builds shared render data from the editable scene so renderers do not reinterpret the scene differently.</summary>
public static class SceneRenderDataBuilder
{
    public static SceneRenderData Build(SceneDocument document, RenderSettings? settings = null)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        Scene scene = document.Scene;
        RenderSettings renderSettings = settings?.Clone() ?? document.RenderSettings.Clone();
        List<RenderMeshInstance> meshes = new();

        foreach (SceneObjectGroup group in scene.ObjectGroups.SelectMany(g => g.SelfAndDescendants()))
        {
            List<Triangle> triangles = group.BuildWorldTriangles(includeHidden: true).ToList();
            if (triangles.Count == 0)
                continue;
            meshes.Add(new RenderMeshInstance(group.Id, group.Name, triangles, group.Visible, group.GetWorldBounds(includeHidden: true)));
        }

        List<RenderLight> lights = scene.Lights.Select(l => new RenderLight(l.Id, l.Kind, l.Position, l.Direction, l.Color, l.Intensity, l.Range, l.InnerConeAngle, l.OuterConeAngle, l.Enabled, l.CastsShadow, l.IsImported, l.IsDefault)).ToList();
        AssetRegistry assets = AssetRegistry.FromScene(scene);
        List<RenderMaterial> materials = assets.Materials.Select(m => new RenderMaterial(m.Id, m)).ToList();
        return new SceneRenderData(meshes, lights, materials, assets, renderSettings, scene.GetSceneBounds());
    }
}
