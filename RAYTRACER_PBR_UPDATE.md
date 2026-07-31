# Linux ray-tracer glTF PBR update

This source applies the Vulkan raster glTF material corrections to both ray-tracing paths:

- Vulkan compute ray/path tracer
- CPU ray/path tracer used by the Linux preview and the desktop frame renderer

Implemented behavior:

- sRGB-to-linear decoding for base-color and emissive textures
- glTF metallic-roughness channel handling (G = roughness, B = metallic)
- interpolated authored vertex normals
- tangent-space normal mapping generated from triangle positions and UVs
- normal scale and occlusion strength
- GGX distribution, correlated Smith visibility, and Schlick Fresnel
- procedural neutral studio indirect lighting for metallic reflections
- occlusion applied to indirect lighting
- PBR Neutral tone mapping and exact linear-to-sRGB output conversion
- alpha-mask testing in the Vulkan compute tracer

The Vulkan compute `GpuTriangle` remains 11 `vec4` values (176 bytes). Vertex normals are packed into previously unused `w`, `z`, and `w` components of position and UV records, avoiding a second scene-sized normal buffer.
