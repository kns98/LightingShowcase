# Fast Vulkan raster transmission correction

This patch corrects glTF transmission in the Linux Vulkan raster preview without adding a scene-color resolve, mip generation, or another render pass.

## Performance constraints retained

- Same opaque and transparent draw passes as before.
- Same render-target and readback path.
- No new texture sample for the Khronos StainedGlassLamp: its clearcoat mask reuses the already sampled transmission texture.
- No framebuffer-sized intermediate textures or mip chains.
- Optical extensions add only branch-gated fragment-shader arithmetic on materials that use transmission or clearcoat.

## Visual corrections

- Transmission no longer lowers output alpha. Alpha remains geometric coverage, as required by glTF.
- `MASK` and `OPAQUE` transmissive surfaces remain visibly present instead of becoming ordinary blended transparency.
- Refraction uses the material IOR against the existing procedural studio environment.
- Roughness broadens the transmitted environment response.
- `KHR_materials_clearcoat` adds a dielectric reflection layer.
- `KHR_materials_volume` attenuation color and distance are applied when supplied.
- The importer preserves IOR, thickness, attenuation, and clearcoat parameters.
- Native `.lscene` serialization advances to version 9 to preserve these optical fields and the transmission texture.

This is deliberately a fast preview approximation. Exact screen-space refraction would require sampling a resolved scene-color texture and would add GPU work and memory traffic.
