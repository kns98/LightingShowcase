using System.Text.Json;

namespace LightingShowcase.CommandLine;

public static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        if (RenderJobRunner.TryHandleRendererProcessArgument(args, out int infrastructureExitCode))
            return infrastructureExitCode;

        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            if (args.Length == 0)
                return PrintHelp();

            string command = args[0].ToLowerInvariant();
            return command switch
            {
                "formats" => PrintFormats(),
                "options" or "help" or "--help" or "-h" => PrintHelp(),
                "render" or "local" => args.Length == 1 || args.Skip(1).Any(IsHelpArgument)
                    ? PrintHelp()
                    : await RunRenderAsync(args.Skip(1).ToArray(), cancellation.Token).ConfigureAwait(false),
                _ when args.Any(IsHelpArgument) => PrintHelp(),
                _ => await RunRenderAsync(args, cancellation.Token).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VERBOSE_ERRORS") == "1")
                Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            RenderJobRunner.DisposeSharedResources();
        }
    }

    private static bool IsHelpArgument(string value) =>
        string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);

    private static async Task<int> RunRenderAsync(string[] args, CancellationToken cancellationToken)
    {
        CommandLine values = CommandLine.Parse(args);
        RenderRequest request = RenderRequest.FromCommandLine(values);
        RenderJobResult result = await new RenderJobRunner().RunAsync(request, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
LightingShowcase command-line renderer (local files)

Usage:
  LightingShowcase.CommandLine render <scene> [render options]
  LightingShowcase.CommandLine <scene> [render options]
  LightingShowcase.CommandLine formats

The scene/model and all referenced assets must be available locally. Relative
asset paths are resolved from the scene's directory. No packaging or upload step
is required.

Input/output:
  --input <path>                 Local scene/model path; positional path is simpler.
  --output <path>                PNG output path. Default: <scene-name>-render.png
                                 beside the input scene.

Renderer:
  --renderer <name>              raster | raster-vulkan | vulkan | cpu
                                 Default: vulkan. Raster modes require Windows.

Image quality:
  --width <1-32768>              Output width. Default: 1920.
  --height <1-32768>             Output height. Default: 1080.
  --samples <1-4096>             CPU/Vulkan path samples. Ignored by raster modes. Default: 1.
  --bounces <0-8>                CPU/Vulkan path bounces. Ignored by raster modes. Default: 2.

Camera:
  --camera-position <x,y,z>      Camera position. Default: automatically framed.
  --camera-target <x,y,z>        Look-at target. Default: scene center.
  --camera-up <x,y,z>            Up vector. Default: 0,1,0.
  --fov <1-179>                  Vertical field of view in degrees. Default: 72.

Lighting and tone:
  --exposure <0.01-100>          Exposure before tone mapping. Default: 1.
  --ambient <0-100>              Ambient-light multiplier. Default: 1.
  --background-top <r,g,b>       Top linear RGB background. Default: 0.055,0.060,0.072.
  --background-bottom <r,g,b>    Bottom linear RGB background. Default: 0.010,0.012,0.016.
  --shadows <true|false>         Enable or disable cast shadows. Default: true.
  --no-shadows                   Convenience switch equivalent to --shadows false.

Examples:
  LightingShowcase.CommandLine room.gltf --renderer raster --output room.png
  LightingShowcase.CommandLine room.gltf --renderer raster-vulkan --output room-vulkan-raster.png
  LightingShowcase.CommandLine render scenes/room.lscene --renderer vulkan --width 3840 --height 2160 --samples 64 --bounces 4
  LightingShowcase.CommandLine model.obj --renderer cpu --samples 8 --fov 50 --exposure 1.25

Renderer meanings:
  raster          Software/CPU z-buffer rasterizer with shadow maps.
  raster-vulkan   Vulkan graphics-pipeline hardware rasterizer.
  vulkan          Vulkan compute BVH ray/path tracer.
  cpu             CPU ray/path tracer.

Run "LightingShowcase.CommandLine formats" to list accepted scene/model formats.
""");
        return 0;
    }

    private static int PrintFormats()
    {
        Console.WriteLine("LightingShowcase renderable local input formats:");
        foreach (string extension in SupportedSceneFormats.Extensions)
            Console.WriteLine($"  {extension}");
        return 0;
    }
}
