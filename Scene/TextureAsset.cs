// -----------------------------------------------------------------------------
// File: Scene/TextureAsset.cs
// Purpose: Canonical texture asset metadata for save/load and render adapters.
// -----------------------------------------------------------------------------

namespace LightingShowcase.SceneGraph;

/// <summary>Texture metadata independent of any renderer-specific bitmap/cache object.</summary>
public sealed class TextureAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Texture";
    public string? SourcePath { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsGenerated { get; set; }

    public static TextureAsset FromTextureMap(TextureMap texture)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        return new TextureAsset
        {
            Id = string.IsNullOrWhiteSpace(texture.Name) ? Guid.NewGuid().ToString("N") : texture.Name,
            Name = string.IsNullOrWhiteSpace(texture.Name) ? "Texture" : texture.Name,
            SourcePath = texture.SourcePath,
            Width = texture.Width,
            Height = texture.Height,
            IsGenerated = texture.IsBuiltInChecker
        };
    }
}
