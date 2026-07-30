# LightingShowcase

LightingShowcase is a 3D scene editor and local command-line renderer written in C# and .NET 8. The command-line tool can render supported scene and model files with four rendering backends:

- **Raster** — software/CPU z-buffer rasterization
- **Raster Vulkan** — Vulkan graphics-pipeline rasterization
- **Vulkan** — Vulkan compute ray/path tracing
- **CPU** — CPU ray/path tracing

The CLI reads scenes directly from disk. Textures, buffers, and other referenced assets are resolved from the scene file's directory tree, so no archive or bundle is required.

## Features

- Windows desktop scene editor
- Windows, Linux, and macOS command-line builds
- Four command-line renderer selections
- Local scene and asset loading
- Configurable image size, camera, quality, lighting, exposure, background, and shadows
- Native LightingShowcase scene formats and common 3D interchange formats
- PNG rendering output with a JSON result summary

## Supported input formats

| Format | Extensions |
|---|---|
| LightingShowcase binary scene | `.lscene`, `.lsb` |
| Prop XML | `.prop.xml`, `.xml` |
| glTF | `.glb`, `.gltf` |
| Autodesk FBX | `.fbx` |
| Wavefront OBJ | `.obj` |
| 3D Studio | `.3ds` |
| Polygon File Format | `.ply` |
| STL | `.stl` |

Run the following command to print the formats supported by the installed CLI:

```text
LightingShowcase.CommandLine formats
```

## Renderer choices

Use `--renderer` to select one of the four command-line rendering modes.

| CLI value | Renderer | Windows | Linux/macOS |
|---|---|:---:|:---:|
| `raster` | Software/CPU z-buffer rasterizer with shadow maps | Yes | No |
| `raster-vulkan` | Vulkan graphics-pipeline hardware rasterizer | Yes | No |
| `vulkan` | Vulkan compute BVH ray/path tracer | Yes | Yes |
| `cpu` | CPU ray/path tracer | Yes | Yes |

`vulkan` is the default. Vulkan modes require a working Vulkan runtime, graphics driver, and compatible device. Use `cpu` when Vulkan is unavailable.

## Requirements

### Windows

- Windows 10 or later
- .NET 8 SDK to build, or .NET 8 Desktop Runtime to run a framework-dependent release
- A current Vulkan-capable graphics driver for `raster-vulkan` and `vulkan`

### Linux

- .NET 8 SDK to build, or .NET 8 Runtime to run a framework-dependent release
- Vulkan loader and compatible driver for the `vulkan` renderer

The included setup script installs the .NET SDK and common Vulkan packages on apt-based Linux distributions:

```bash
./setup-linux.sh
```

#### Linux Vulkan preflight: missing `libdl`

On some Ubuntu installations, the Vulkan renderer can fail during the isolated Veldrid preflight with an error similar to:

```text
System.DllNotFoundException: Unable to load shared library 'libdl'
```

First confirm that the Vulkan loader and a Vulkan device are available:

```bash
vulkaninfo --summary
```

Ubuntu normally supplies the runtime library as `libdl.so.2`, but the Vulkan binding used by Veldrid may request the unversioned name `libdl.so`. Create a project-local compatibility link beside the Linux command-line executable. This does not modify `/usr/lib` or other system library directories.

For a Release build from source:

```bash
cd ./LightingShowcase.CommandLine/bin/Release/net8.0

LIBDL="$(ldconfig -p | awk '/libdl\.so\.2/ {print $NF; exit}')"
test -n "$LIBDL" || {
    echo "libdl.so.2 was not found"
    exit 1
}

ln -sfn "$LIBDL" ./libdl.so
ls -l ./libdl.so
```

For a Debug build, use `bin/Debug/net8.0`. For a published build, run the same commands inside the publish directory, such as `publish/commandline-linux-x64`. Recreate the link after cleaning `bin`, deleting the publish directory, or publishing to a new location.

Run the renderer from the directory containing the executable and compatibility link:

```bash
./LightingShowcase.CommandLine \
  --input "$HOME/Downloads/scene.glb" \
  --renderer vulkan \
  --output "$HOME/Downloads/out.png"
```

If the preflight reports only exit code `0x0000000A`, bypass the isolated preflight for one diagnostic run to expose the underlying Vulkan exception:

```bash
LIGHTINGSHOWCASE_SKIP_VULKAN_PREFLIGHT=1 \
LIGHTINGSHOWCASE_VERBOSE_ERRORS=1 \
./LightingShowcase.CommandLine \
  --input "$HOME/Downloads/scene.glb" \
  --renderer vulkan \
  --output "$HOME/Downloads/out.png" \
  2>&1 | tee "$HOME/Downloads/lighting-vulkan-error.txt"
```

`LIGHTINGSHOWCASE_SKIP_VULKAN_PREFLIGHT=1` is intended only for diagnosis. It does not fix a missing native library and should not be used as the permanent solution.

### macOS

- .NET 8 SDK to build, or .NET 8 Runtime to run a framework-dependent release
- A working Vulkan implementation for the `vulkan` renderer

The Windows desktop editor is not built for Linux or macOS. Those platforms use the command-line project.

## Command-line quick start

### Use a published Windows build

```powershell
.\LightingShowcase.CommandLine.exe .\scenes\room.gltf --renderer raster --output .\room.png
```

### Run from source on Windows

```powershell
.\LightingShowcase.CommandLine\render.cmd .\scenes\room.gltf --renderer raster-vulkan --output .\room.png
```

### Run from source on Linux or macOS

```bash
sh ./LightingShowcase.CommandLine/render.sh ./scenes/room.gltf --renderer cpu --output ./room.png
```

The explicit `render` command is optional. These forms are equivalent:

```text
LightingShowcase.CommandLine render scene.gltf --renderer vulkan
LightingShowcase.CommandLine scene.gltf --renderer vulkan
```

## Examples for all four renderers

```powershell
# Software rasterizer
LightingShowcase.CommandLine.exe scene.gltf --renderer raster --output raster.png

# Vulkan rasterizer
LightingShowcase.CommandLine.exe scene.gltf --renderer raster-vulkan --output raster-vulkan.png

# Vulkan compute ray/path tracer
LightingShowcase.CommandLine.exe scene.gltf --renderer vulkan --samples 64 --bounces 4 --output vulkan.png

# CPU ray/path tracer
LightingShowcase.CommandLine.exe scene.gltf --renderer cpu --samples 8 --bounces 3 --output cpu.png
```

## Command-line options

### Input and output

```text
--input <path>                 Local scene/model path. A positional path is also accepted.
--output <path>                PNG output path.
                               Default: <scene-name>-render.png beside the scene.
```

Do not provide the scene both positionally and with `--input`.

### Renderer

```text
--renderer <name>              raster | raster-vulkan | vulkan | cpu
                               Default: vulkan
```

### Image quality

```text
--width <1-32768>              Output width. Default: 1920.
--height <1-32768>             Output height. Default: 1080.
--samples <1-4096>             Path-tracing samples. Default: 1.
--bounces <0-8>                Path-tracing bounce count. Default: 2.
```

`--samples` and `--bounces` apply to the `vulkan` and `cpu` renderers and are ignored by the raster renderers.

### Camera

```text
--camera-position <x,y,z>      Camera position. Default: automatically framed.
--camera-target <x,y,z>        Look-at target. Default: scene center.
--camera-up <x,y,z>            Up vector. Default: 0,1,0.
--fov <1-179>                  Vertical field of view in degrees. Default: 72.
```

### Lighting and tone

```text
--exposure <0.01-100>          Exposure before tone mapping. Default: 1.
--ambient <0-100>              Ambient-light multiplier. Default: 1.
--background-top <r,g,b>       Top linear RGB color. Default: 0.055,0.060,0.072.
--background-bottom <r,g,b>    Bottom linear RGB color. Default: 0.010,0.012,0.016.
--shadows <true|false>         Enable or disable cast shadows. Default: true.
--no-shadows                   Equivalent to --shadows false.
```

Print the complete built-in help:

```text
LightingShowcase.CommandLine --help
```

## Local asset loading

Keep the model and its dependencies together in a normal directory structure. Relative paths are resolved from the scene's directory, and the loader can search beneath that directory for matching asset filenames.

```text
project/
  room.gltf
  room.bin
  textures/
    wall.png
    floor.jpg
```

Render the scene directly:

```bash
LightingShowcase.CommandLine project/room.gltf --renderer vulkan
```

The default output is written as `project/room-render.png`.

## Build from source

Clone or extract the repository, then run the build command for the target platform.

### Windows desktop and command line

```powershell
dotnet restore .\LightingShowcase.Windows.sln
dotnet build .\LightingShowcase.Windows.sln --configuration Release --no-restore
```

Or use:

```powershell
.\build.ps1
```

The Windows solution references the Windows command-line project:

```text
LightingShowcase.CommandLine/LightingShowcase.CommandLine.Windows.csproj
```

### Linux command line

```bash
dotnet restore ./LightingShowcase.Linux.sln
dotnet build ./LightingShowcase.Linux.sln --configuration Release --no-restore
```

Or use:

```bash
./build.sh
```

### macOS command line

```bash
dotnet restore ./LightingShowcase.CommandLine/LightingShowcase.CommandLine.Linux.csproj --runtime osx-x64
dotnet publish ./LightingShowcase.CommandLine/LightingShowcase.CommandLine.Linux.csproj \
  --configuration Release \
  --runtime osx-x64 \
  --self-contained false \
  --output ./publish/commandline-macos-x64 \
  --no-restore
```

## Publish

Published packages are framework-dependent and do not bundle the .NET runtime.

### Windows

```powershell
.\publish-windows.ps1
```

Default outputs:

```text
publish/desktop-win-x64/
publish/commandline-win-x64/
```

### Linux x64

```bash
./publish-linux.sh
```

Default output:

```text
publish/commandline-linux-x64/
```

## CLI output

After a successful render, the command-line application writes a JSON summary to standard output. It includes the selected backend, scene path, asset directory, dimensions, sample and bounce settings, scene statistics, elapsed time, and output image path.

Example shape:

```json
{
  "backend": "cpu",
  "scene": "/path/to/scene.gltf",
  "assetDirectory": "/path/to",
  "width": 1920,
  "height": 1080,
  "samples": 8,
  "bounces": 3,
  "output": "/path/to/scene-render.png"
}
```

## Project structure

```text
LightingShowcase.CommandLine/          Command-line parsing and render orchestration
LightingShowcase.Core/                 Cross-platform engine code
LightingShowcase.ImportExport.*/       Scene and model format plug-ins
LightingShowcase.ObjectLibrary.*/      Built-in and ready-made objects
Camera/ Lighting/ Rendering/ Scene/    Shared rendering and scene implementation
UI/ LightingShowcaseForm*.cs           Windows desktop editor
Shaders/                               Vulkan compute shaders
.github/workflows/                     Build and release automation
```

## Validation

Platform-reference checks are included to prevent the Windows solution or Windows scripts from accidentally referencing the Linux command-line project:

```powershell
.\validate-platform-split.ps1
```

```bash
./validate-platform-split.sh
```

## Additional documentation

See [`v1.md`](v1.md) for the detailed v1 technical reference and consolidated implementation history.

## License notices

See [`LICENSE-NOTICE.txt`](LICENSE-NOTICE.txt) for the repository's license and third-party notices.
