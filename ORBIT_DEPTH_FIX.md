# Vulkan raster orbit overlap fix

Transmission is an optical material property, not alpha coverage. The fast
single-pass transmission approximation does not sample previously rendered
scene color, so `OPAQUE` and `MASK` transmission materials can and should use
the normal depth-writing pass.

Previously every material with `Transmission > 0` was sent to the blended pass,
where depth writes are disabled. As the camera orbited, lower portions of the
StainedGlassLamp could therefore draw over a nearer glass panel. The renderer
now reserves the transparent pass for genuine alpha-blended materials only.

This change adds no render pass, buffer, texture lookup, sort, or draw call. It
usually reduces transparent geometry and can be marginally faster.
