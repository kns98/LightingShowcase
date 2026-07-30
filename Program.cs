// -----------------------------------------------------------------------------
// File: Program.cs
// Purpose: Application entry point.
//
// Configures WinForms application defaults, reads the optional startup file path, and opens the main editor window.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Rendering;

namespace LightingShowcase;

/// <summary>Application startup class.</summary>
internal static class Program
{
    [STAThread]
    /// <summary>Implements the main operation for this file's subsystem.</summary>
    private static void Main(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, VeldridVulkanDevicePreflight.ChildArgument, StringComparison.OrdinalIgnoreCase)))
        {
            Environment.Exit(VeldridVulkanDevicePreflight.RunChildDeviceCreationTest());
            return;
        }

        ApplicationConfiguration.Initialize();
        string? initialObjPath = args.FirstOrDefault(path =>
            string.Equals(Path.GetExtension(path), ".obj", StringComparison.OrdinalIgnoreCase));
        Application.Run(new LightingShowcaseForm(initialObjPath));
    }
}
