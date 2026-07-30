using LightingShowcase.ImportExport.Fbx;
using LightingShowcase.ImportExport.Gltf;
using LightingShowcase.ImportExport.Obj;
using LightingShowcase.ImportExport.Ply;
using LightingShowcase.ImportExport.PropXml;
using LightingShowcase.ImportExport.Stl;
using LightingShowcase.ImportExport.ThreeDs;
using LightingShowcase.ObjectLibrary.BuiltIns;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

internal static class PluginBootstrap
{
    private static int initialized;

    public static void EnsureLoaded()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
            return;

        // Explicit type references ensure every plugin assembly is loaded before
        // the shared reflection registries scan AppDomain assemblies.
        _ = typeof(FbxSceneFormatPlugin).Assembly;
        _ = typeof(GltfSceneFormatPlugin).Assembly;
        _ = typeof(ObjSceneFormatPlugin).Assembly;
        _ = typeof(PlySceneFormatPlugin).Assembly;
        _ = typeof(PropXmlSceneFormatPlugin).Assembly;
        _ = typeof(StlSceneFormatPlugin).Assembly;
        _ = typeof(ThreeDsSceneFormatPlugin).Assembly;
        _ = typeof(BuiltInObjectLibraryPlugin).Assembly;
        _ = typeof(DiningTableObject).Assembly;

        SceneFormatRegistry.EnsureInitialized();
        ObjectLibraryRegistry.EnsureInitialized();
    }
}
