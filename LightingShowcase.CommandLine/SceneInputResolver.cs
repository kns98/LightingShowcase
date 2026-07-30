namespace LightingShowcase.CommandLine;

internal sealed class ResolvedSceneInput
{
    public required string ScenePath { get; init; }
    public required string AssetDirectory { get; init; }
}

internal static class SceneInputResolver
{
    public static ResolvedSceneInput Resolve(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("A local scene/model file path is required.", nameof(input));

        if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
            throw new NotSupportedException("Remote scene URLs are not supported. Place the scene and its assets in a local directory.");

        string scenePath = Path.GetFullPath(input);
        if (!File.Exists(scenePath))
            throw new FileNotFoundException("Scene input was not found.", scenePath);
        if (string.Equals(Path.GetExtension(scenePath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("ZIP scene packages are not supported. Extract the files and pass the local scene path directly.");
        if (!SupportedSceneFormats.IsSupportedPath(scenePath))
            throw new NotSupportedException($"Unsupported scene/model format. Supported inputs: {SupportedSceneFormats.Describe()}.");

        string assetDirectory = Path.GetDirectoryName(scenePath)
            ?? throw new InvalidOperationException("The scene path does not have a parent directory.");

        return new ResolvedSceneInput
        {
            ScenePath = scenePath,
            AssetDirectory = assetDirectory
        };
    }
}
