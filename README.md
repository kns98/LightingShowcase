# LightingShowcase

LightingShowcase is a 3D scene editor, local command-line renderer, and read-only Linux visualization frontend written in C# and .NET 8. The command-line tool can render supported scene and model files with four rendering backends:

- **Raster** — software/CPU z-buffer rasterization
- **Raster Vulkan** — Vulkan graphics-pipeline rasterization
- **Vulkan** — Vulkan compute ray/path tracing
- **CPU** — CPU ray/path tracing

The CLI reads scenes directly from disk. Textures, buffers, and other referenced assets are resolved from the scene file's directory tree, so no archive or bundle is required.

## Features

- Windows desktop scene editor
- Windows, Linux, and macOS command-line builds
- Read-only Linux preview window with orbit and zoom controls
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
| `raster` | Software/CPU z-buffer rasterizer with shadow maps | Yes | Yes |
| `raster-vulkan` | Vulkan graphics-pipeline hardware rasterizer | Yes | Yes |
| `vulkan` | Vulkan compute BVH ray/path tracer | Yes | Yes |
| `cpu` | CPU ray/path tracer | Yes | Yes |

`vulkan` is the default. Vulkan modes require a working Vulkan runtime, graphics driver, and compatible device. Use `cpu` or `raster` when Vulkan is unavailable.

Both rasterizers use the shared cross-platform `RenderImage` RGBA buffer. `System.Drawing.Bitmap` conversion is confined to the Windows desktop UI, so the command-line raster paths do not require WinForms or `System.Drawing.Common`.

## Requirements

### Windows

- Windows 10 or later
- .NET 8 SDK to build, or .NET 8 Desktop Runtime to run a framework-dependent release
- A current Vulkan-capable graphics driver for `raster-vulkan` and `vulkan`

### Linux

- .NET 8 SDK to build, or .NET 8 Runtime to run a framework-dependent release
- Vulkan loader and compatible driver for the `raster-vulkan` and `vulkan` renderers
- X11 or XWayland for the Linux preview window

The included setup script installs the .NET SDK and common Vulkan packages on apt-based Linux distributions:

```bash
./setup-linux.sh
```

#### Linux Vulkan preflight: missing `libdl`

On some Ubuntu installations, either Vulkan renderer (`raster-vulkan` or `vulkan`) can fail during the isolated Veldrid preflight with an error similar to:

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
- A working Vulkan implementation for the `raster-vulkan` and `vulkan` renderers

The Windows desktop editor is not built for Linux or macOS. Linux also includes the read-only preview frontend described below.

## Linux preview window

`LightingShowcase.Preview` is a visualization-only Linux frontend. It does not expose object selection, transforms, materials, lighting edits, scene saving, or any other editor operation.

Launch a published build with a scene path:

```bash
./LightingShowcase.Preview /path/to/scene.gltf
```

You can also start it without a scene and use **Open…** to select a local scene/model:

```bash
./LightingShowcase.Preview
```

Controls:

- Drag with the left mouse button to orbit the camera.
- Use the mouse wheel or `+`/`-` to zoom.
- Use the arrow keys for stepped rotation.
- Use **Reset view** to restore the fitted scene camera.
- Select **Raster**, **Vulkan raster**, **Vulkan**, or **CPU** from the renderer menu.

Interaction policy:

- **Raster** continuously redraws while dragging and uses the reusable software shadow-map cache.
- **Vulkan raster** and **Vulkan** first measure an actual frame. Continuous drag rendering is enabled only when the measured frame time is at or below the frontend threshold; otherwise the new angle renders when the mouse button is released.
- **CPU** uses a reduced-resolution one-sample path preview and always renders after the mouse is released. This avoids locking the UI into a queue of slow CPU frames.

The preview keeps assets external. Textures and buffers are resolved from the same directory tree as the selected scene; nothing is bundled into the scene or application package.

Large-scene Vulkan memory behavior:

- Vulkan compute and Vulkan raster always attempt the complete scene; there is no configurable safety budget or triangle sampling threshold.
- Scene-sized GPU buffers are cached only for the active Vulkan renderer. Switching backends or loading another model releases the inactive scene first.
- Geometry is packed and uploaded in small fixed-size chunks, so CPU upload memory does not scale with the full vertex or triangle buffer. Vulkan raster also stores one material record per shared material rather than one record per triangle.
- Vulkan compute stores only an integer triangle-order array and compact CPU BVH nodes, then streams GPU triangles and GPU nodes in small chunks instead of allocating complete upload arrays.
- Texture pixels are uploaded one texture at a time. Vulkan raster does not build a full CPU-side atlas, and Vulkan compute does not concatenate every texture into one managed array.
- Vulkan raster keeps only one framebuffer target size cached to minimize color, depth, and readback allocations.
- Hardware/API limits still apply, including Vulkan's per-buffer size and maximum texture-dimension constraints.

On minimal Ubuntu or WSL installations, install the common Avalonia/X11 libraries:

```bash
sudo apt install libice6 libsm6 libfontconfig1
```

The existing `libdl.so` Vulkan preflight fix applies to the preview executable too. Create the compatibility link inside the preview publish directory and run the application from that directory.

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
# Software rasterizer; no Vulkan device required
sh ./LightingShowcase.CommandLine/render.sh ./scenes/room.gltf --renderer raster --output ./room-raster.png

# Vulkan graphics-pipeline rasterizer
sh ./LightingShowcase.CommandLine/render.sh ./scenes/room.gltf --renderer raster-vulkan --output ./room-raster-vulkan.png
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

### Linux command line and preview

```bash
dotnet restore ./LightingShowcase.Linux.sln
dotnet build ./LightingShowcase.Linux.sln --configuration Release --no-restore
```

Or use:

```bash
./build.sh
```

The Linux solution includes both:

```text
LightingShowcase.CommandLine/LightingShowcase.CommandLine.Linux.csproj
LightingShowcase.Preview.Linux/LightingShowcase.Preview.Linux.csproj
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

Publish the Linux visualization frontend separately:

```bash
./publish-linux-preview.sh
```

Default output:

```text
publish/preview-linux-x64/
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
LightingShowcase.Preview.Linux/         Read-only Linux orbit/zoom visualization frontend
LightingShowcase.Core/                 Cross-platform engine code
LightingShowcase.ImportExport.*/       Scene and model format plug-ins
LightingShowcase.ObjectLibrary.*/      Built-in and ready-made objects
Camera/ Lighting/ Rendering/ Scene/    Shared rendering and scene implementation
UI/ LightingShowcaseForm*.cs           Windows desktop editor
Shaders/                               Vulkan compute shaders
.github/workflows/                     Build and release automation
```

## Validation

Platform-reference and renderer-portability checks verify the solution split, confirm both rasterizers are compiled into the shared core, and reject any reintroduction of `System.Drawing` into the portable raster paths:

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
