// -----------------------------------------------------------------------------
// File: Scene/SceneProjectFile.cs
// Purpose: Portable project manifest save/load helpers.
// -----------------------------------------------------------------------------

using System.Text.Json;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>
/// Lightweight project manifest for the editor. Native geometry is still saved by
/// existing scene format plugins; this manifest captures renderer-neutral metadata
/// such as assets, lights, render settings, and object transform/visibility state.
/// </summary>
public static class SceneProjectFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IncludeFields = true };

    public static void SaveManifest(SceneDocument document, string filePath)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A project path is required.", nameof(filePath));

        SceneProjectManifest manifest = SceneProjectManifest.FromDocument(document);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");
        File.WriteAllText(filePath, JsonSerializer.Serialize(manifest, Options));
    }

    public static SceneProjectManifest LoadManifest(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A project path is required.", nameof(filePath));
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<SceneProjectManifest>(json, Options) ?? new SceneProjectManifest();
    }
}

public sealed class SceneProjectManifest
{
    public string Format { get; set; } = "LightingShowcase.ProjectManifest.v1";
    public string Title { get; set; } = "scene";
    public RenderSettings RenderSettings { get; set; } = new();
    public List<ProjectObjectRecord> Objects { get; set; } = new();
    public List<ProjectLightRecord> Lights { get; set; } = new();
    public List<MaterialDefinition> Materials { get; set; } = new();
    public List<TextureAsset> Textures { get; set; } = new();

    public static SceneProjectManifest FromDocument(SceneDocument document)
    {
        AssetRegistry assets = document.Assets;
        return new SceneProjectManifest
        {
            Title = document.Title,
            RenderSettings = document.RenderSettings.Clone(),
            Objects = document.Scene.ObjectGroups.SelectMany(g => g.SelfAndDescendants()).Select(ProjectObjectRecord.FromGroup).ToList(),
            Lights = document.Scene.Lights.Select(ProjectLightRecord.FromLight).ToList(),
            Materials = assets.Materials.ToList(),
            Textures = assets.Textures.ToList()
        };
    }
}

public sealed class ProjectObjectRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "Object";
    public bool Visible { get; set; } = true;
    public bool Selectable { get; set; } = true;
    public string? PrimitiveKind { get; set; }
    public string? PrimitiveSourceName { get; set; }
    public Vec3 Position { get; set; }
    public Vec3 Rotation { get; set; }
    public Vec3 Scale { get; set; } = new(1, 1, 1);
    public int TriangleCount { get; set; }

    public static ProjectObjectRecord FromGroup(SceneObjectGroup group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        Visible = group.Visible,
        Selectable = group.IsSelectable,
        PrimitiveKind = group.PrimitiveKind,
        PrimitiveSourceName = group.PrimitiveSourceName,
        Position = group.Position,
        Rotation = group.Rotation,
        Scale = group.Scale,
        TriangleCount = group.CountLocalTrianglesRecursively()
    };
}

public sealed class ProjectLightRecord
{
    public string Id { get; set; } = "light";
    public SceneLightKind Kind { get; set; } = SceneLightKind.Point;
    public Vec3 Position { get; set; }
    public Vec3 Direction { get; set; } = new(0, 0, -1);
    public Vec3 Color { get; set; } = new(1, 1, 1);
    public double Intensity { get; set; } = 1.0;
    public double Range { get; set; }
    public double InnerConeAngle { get; set; }
    public double OuterConeAngle { get; set; }
    public bool Enabled { get; set; } = true;
    public bool CastsShadow { get; set; } = true;
    public bool IsImported { get; set; }
    public bool IsDefault { get; set; }

    public static ProjectLightRecord FromLight(SceneLight light) => new()
    {
        Id = light.Id,
        Kind = light.Kind,
        Position = light.Position,
        Direction = light.Direction,
        Color = light.Color,
        Intensity = light.Intensity,
        Range = light.Range,
        InnerConeAngle = light.InnerConeAngle,
        OuterConeAngle = light.OuterConeAngle,
        Enabled = light.Enabled,
        CastsShadow = light.CastsShadow,
        IsImported = light.IsImported,
        IsDefault = light.IsDefault
    };
}
