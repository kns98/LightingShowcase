using Avalonia.Platform.Storage;

namespace LightingShowcase.Preview;

internal static class PreviewSceneFileTypes
{
    public static readonly IReadOnlyList<string> SupportedExtensions =
    [
        ".lscene", ".lsb", ".prop.xml", ".xml", ".glb", ".gltf",
        ".fbx", ".obj", ".3ds", ".ply", ".stl"
    ];

    public static readonly IReadOnlyList<FilePickerFileType> PickerTypes =
    [
        new("All supported scenes and models")
        {
            Patterns = SupportedExtensions.Select(extension => $"*{extension}").ToArray()
        },
        new("LightingShowcase scenes")
        {
            Patterns = ["*.lscene", "*.lsb"]
        },
        new("Property XML scenes")
        {
            Patterns = ["*.prop.xml", "*.xml"]
        },
        new("glTF models")
        {
            Patterns = ["*.glb", "*.gltf"],
            MimeTypes = ["model/gltf-binary", "model/gltf+json"]
        },
        new("FBX models")
        {
            Patterns = ["*.fbx"]
        },
        new("Wavefront OBJ models")
        {
            Patterns = ["*.obj"],
            MimeTypes = ["model/obj"]
        },
        new("3D Studio models")
        {
            Patterns = ["*.3ds"]
        },
        new("PLY models")
        {
            Patterns = ["*.ply"]
        },
        new("STL models")
        {
            Patterns = ["*.stl"],
            MimeTypes = ["model/stl"]
        }
    ];

    public static bool IsSupportedPath(string path) =>
        SupportedExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
