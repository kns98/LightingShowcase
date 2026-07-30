// -----------------------------------------------------------------------------
// File: Scene/SceneFormatRegistry.cs
// Purpose: Discovers and routes scene import/export plugins.
// -----------------------------------------------------------------------------

using System.Reflection;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

public static class SceneFormatRegistry
{
    private static readonly object Gate = new();
    private static bool initialized;
    private static readonly List<ISceneFormatPlugin> plugins = new();

    public static IReadOnlyList<ISceneFormatPlugin> Plugins
    {
        get
        {
            EnsureInitialized();
            return plugins;
        }
    }

    public static IReadOnlyList<ISceneFormatPlugin> Importers => Plugins.Where(p => p.CanImport).ToList();
    public static IReadOnlyList<ISceneFormatPlugin> Exporters => Plugins.Where(p => p.CanExport).ToList();

    public static void EnsureInitialized()
    {
        if (initialized) return;
        lock (Gate)
        {
            if (initialized) return;
            LoadPluginAssemblies();
            DiscoverPlugins();
            initialized = true;
        }
    }

    private static void LoadPluginAssemblies()
    {
        string baseDirectory = AppContext.BaseDirectory;
        if (!Directory.Exists(baseDirectory)) return;

        foreach (string dll in Directory.EnumerateFiles(baseDirectory, "LightingShowcase.ImportExport.*.dll"))
        {
            try
            {
                string fullPath = Path.GetFullPath(dll);
                if (AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.Location, fullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                Assembly.LoadFrom(fullPath);
            }
            catch
            {
                // Ignore malformed or incompatible plugin DLLs so one bad format
                // does not stop the editor from opening.
            }
        }
    }

    private static void DiscoverPlugins()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (Type type in types)
            {
                if (type.IsAbstract || !typeof(ISceneFormatPlugin).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                try
                {
                    if (Activator.CreateInstance(type) is ISceneFormatPlugin plugin && !plugins.Any(p => p.FormatId == plugin.FormatId))
                        plugins.Add(plugin);
                }
                catch { }
            }
        }

        plugins.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsImportExtension(string extension)
    {
        EnsureInitialized();
        extension = NormalizeExtension(extension);
        return plugins.Any(p => p.CanImport && p.Extensions.Any(e => NormalizeExtension(e) == extension));
    }

    public static bool IsExportExtension(string extension)
    {
        EnsureInitialized();
        extension = NormalizeExtension(extension);
        return plugins.Any(p => p.CanExport && p.Extensions.Any(e => NormalizeExtension(e) == extension));
    }

    public static ISceneFormatPlugin FindImporter(string filePath)
    {
        EnsureInitialized();
        string extension = NormalizeExtension(Path.GetExtension(filePath));
        return plugins.FirstOrDefault(p => p.CanImport && p.Extensions.Any(e => NormalizeExtension(e) == extension))
            ?? throw new NotSupportedException($"Unsupported model file type: {extension}");
    }

    public static ISceneFormatPlugin FindExporter(string filePath)
    {
        EnsureInitialized();
        string extension = NormalizeExtension(Path.GetExtension(filePath));
        return plugins.FirstOrDefault(p => p.CanExport && p.Extensions.Any(e => NormalizeExtension(e) == extension))
            ?? throw new NotSupportedException($"Unsupported export file type: {extension}");
    }

    public static ObjLoadResult Import(Scene scene, string filePath, SceneLoadOptions options)
    {
        ISceneFormatPlugin plugin = FindImporter(filePath);
        return plugin.Import(scene, filePath, options);
    }

    public static void Export(Scene scene, string filePath, SceneSaveOptions options)
    {
        ISceneFormatPlugin plugin = FindExporter(filePath);
        plugin.Export(scene, filePath, options);
    }

    private static string NormalizeExtension(string extension) => extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
}
