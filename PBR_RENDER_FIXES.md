# Linux Vulkan glTF PBR correction

## Scope

This change corrects the visual treatment of glTF/GLB assets in the Linux Vulkan raster preview, with the Khronos DamagedHelmet asset as the primary regression case. The Vulkan compute path is not converted by this patch.

## Rendering changes

- Retains imported per-vertex normals and transforms them with an inverse-transpose normal matrix.
- Samples base-color, metallic-roughness, normal, occlusion, and emissive maps in the Vulkan raster shader.
- Decodes base-color and emissive textures from sRGB; keeps data textures linear.
- Implements glTF metallic-roughness GGX direct lighting with Smith correlated visibility and Schlick Fresnel.
- Adds derivative-based normal mapping without increasing the raster vertex format.
- Adds indirect diffuse/specular response from a procedural neutral studio environment.
- Applies occlusion to indirect light and adds emissive output.
- Uses PBR Neutral tone mapping and converts the final linear result to sRGB.
- Supports glTF `OPAQUE` and `MASK` alpha behavior and stores `doubleSided` metadata.
- Adds channel debug modes through `LIGHTINGSHOWCASE_VULKAN_RASTER_DEBUG`.

## Scene and serialization changes

- Adds material fields for occlusion, normal scale, occlusion strength, alpha mode/cutoff, and double-sided state.
- Preserves those fields through editor material operations and asset registration.
- Adds per-vertex normals to scene triangles and object transformations.
- Advances the binary LightingShowcase scene format to version 8 so normals and the additional PBR fields survive save/load; version 8 continues to read older files. Older application builds will reject version 8 files rather than silently discarding the added data.

## Validation performed

- Linux platform-split validation script.
- C# delimiter/string/comment lexical checks across changed files.
- C#/GLSL material-layout consistency check.
- Patch application and repository integrity checks during packaging.

A full `dotnet build` was not available in the packaging environment because the .NET SDK was not installed. Run the following on a development machine before publishing a release:

```bash
dotnet restore ./LightingShowcase.Linux.sln
dotnet build ./LightingShowcase.Linux.sln --configuration Release --no-restore
```
