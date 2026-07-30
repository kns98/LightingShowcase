using Avalonia;
using LightingShowcase.Rendering;

namespace LightingShowcase.Preview;

internal static class Program
{
    internal static string[] StartupArguments { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], VeldridVulkanDevicePreflight.ChildArgument, StringComparison.Ordinal))
            return VeldridVulkanDevicePreflight.RunChildDeviceCreationTest();

        if (args.Any(argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Usage: LightingShowcase.Preview [scene-file]");
            Console.WriteLine("Read-only Linux visualization frontend. Drag to orbit and use the mouse wheel to zoom.");
            return 0;
        }

        StartupArguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
