// -----------------------------------------------------------------------------
// File: Rendering/ShadowRasterRenderer.cs
// Purpose: Independent fast raster preview with depth buffering and shadow maps.
//
// This renderer is intentionally separate from Helix/WPF and from the CPU/Vulkan
// ray tracers. It owns a small AMD-style preview pipeline: build reusable shadow
// maps from scene lights, project triangles, rasterize a depth buffer, and shade
// visible pixels with direct lighting. It is not a path tracer and it does not
// call Scene.Intersect for visibility or shadows.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

/// <summary>Independent realtime-style raster preview renderer with z-buffered triangle drawing and shadow-map shadows.</summary>
public static class ShadowRasterRenderer
{
    private const double CameraNear = 0.035;
    private const double CameraFovDegrees = 72.0;
    private const int ShadowMapSize = 512;
    private const int MaxShadowCastingLights = 4;
    private const int MaxInteractiveLights = 2;


    /// <summary>
    /// Reusable raster cache. Shadow maps are view-independent, so they should be
    /// rebuilt only when the scene or lights change, not on every camera orbit.
    /// </summary>
    public sealed class PreviewCache
    {
        internal PreviewCache(Scene scene, List<ShadowMap> shadowMaps, long buildMilliseconds)
        {
            Scene = scene;
            ShadowMaps = shadowMaps;
            BuildMilliseconds = buildMilliseconds;
            TriangleCount = scene.Triangles.Count;
            LightCount = scene.Lights.Count;
            EnabledShadowMapCount = shadowMaps.Count(m => m.Enabled);
        }

        public Scene Scene { get; }
        public int TriangleCount { get; }
        public int LightCount { get; }
        public int EnabledShadowMapCount { get; }
        public long BuildMilliseconds { get; }
        internal IReadOnlyList<ShadowMap> ShadowMaps { get; }
    }

    /// <summary>Builds reusable shadow-map state for a scene snapshot.</summary>
    public static PreviewCache BuildCache(Scene scene, CancellationToken cancellationToken)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<ShadowMap> shadowMaps = BuildShadowMaps(scene, cancellationToken);
        stopwatch.Stop();
        return new PreviewCache(scene, shadowMaps, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Renders one shaded raster preview frame into a 32-bit ARGB bitmap.</summary>
    public static Bitmap Render(
        Scene scene,
        Vec3 cameraPosition,
        CameraBasis cameraBasis,
        int width,
        int height,
        CancellationToken cancellationToken,
        out string details)
    {
        PreviewCache cache = BuildCache(scene, cancellationToken);
        return Render(cache, cameraPosition, cameraBasis, width, height, cancellationToken, out details);
    }

    /// <summary>Renders one shaded raster preview frame using a reusable shadow-map cache.</summary>
    public static Bitmap Render(
        PreviewCache cache,
        Vec3 cameraPosition,
        CameraBasis cameraBasis,
        int width,
        int height,
        CancellationToken cancellationToken,
        out string details,
        bool interactiveFast = false)
    {
        if (cache == null) throw new ArgumentNullException(nameof(cache));
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        Stopwatch stopwatch = Stopwatch.StartNew();
        Scene scene = cache.Scene;
        PreparedLight[] lights = PrepareLights(scene, cache.ShadowMaps);
        if (interactiveFast && lights.Length > MaxInteractiveLights)
            lights = lights.Take(MaxInteractiveLights).ToArray();

        Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        double[] zBuffer = new double[width * height];
        Array.Fill(zBuffer, double.PositiveInfinity);

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int byteCount = data.Stride * height;
            byte[] bytes = new byte[byteCount];
            ClearBackground(bytes, data.Stride, width, height);

            foreach (Triangle tri in scene.Triangles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RasterizeTriangle(bytes, data.Stride, zBuffer, width, height, tri, cameraPosition, cameraBasis, lights, interactiveFast);
            }

            Marshal.Copy(bytes, 0, data.Scan0, byteCount);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        stopwatch.Stop();
        details = $"Shadow raster {(interactiveFast ? "20fps preview" : "OK")} - {width}x{height}, triangles={cache.TriangleCount}, lights={(interactiveFast ? lights.Length : cache.LightCount)}, shadowMaps={cache.EnabledShadowMapCount}, cache={cache.BuildMilliseconds}ms, frame={stopwatch.ElapsedMilliseconds}ms";
        return bitmap;
    }

    private static PreparedLight[] PrepareLights(Scene scene, IReadOnlyList<ShadowMap> shadowMaps)
    {
        Dictionary<string, ShadowMap> shadowByLightId = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShadowMap map in shadowMaps)
        {
            if (!string.IsNullOrWhiteSpace(map.LightId))
                shadowByLightId[map.LightId] = map;
        }

        List<PreparedLight> prepared = new();
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;

            shadowByLightId.TryGetValue(light.Id, out ShadowMap? shadowMap);
            Vec3 directionalLightDirection = light.Kind == SceneLightKind.Directional
                ? (light.Direction * -1.0).Normalize()
                : Vec3.Zero;
            Vec3 spotForward = light.Kind == SceneLightKind.Spot
                ? light.Direction.Normalize()
                : Vec3.Zero;
            double cosOuter = light.Kind == SceneLightKind.Spot ? Math.Cos(light.OuterConeAngle) : 0.0;
            double cosInner = light.Kind == SceneLightKind.Spot ? Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)) : 1.0;
            prepared.Add(new PreparedLight(light, shadowMap, directionalLightDirection, spotForward, cosOuter, cosInner));
        }

        return prepared.ToArray();
    }

    private static void ClearBackground(byte[] bytes, int stride, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            double t = height <= 1 ? 0.0 : y / (double)(height - 1);
            Vec3 color = Vec3.Lerp(new Vec3(0.055, 0.060, 0.074), new Vec3(0.010, 0.012, 0.016), t);
            int row = y * stride;
            byte r = ToByte(Gamma(color.X));
            byte g = ToByte(Gamma(color.Y));
            byte b = ToByte(Gamma(color.Z));
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                bytes[i + 0] = b;
                bytes[i + 1] = g;
                bytes[i + 2] = r;
                bytes[i + 3] = 255;
            }
        }
    }

    private static void RasterizeTriangle(
        byte[] bytes,
        int stride,
        double[] zBuffer,
        int width,
        int height,
        Triangle tri,
        Vec3 cameraPosition,
        CameraBasis basis,
        IReadOnlyList<PreparedLight> lights,
        bool interactiveFast)
    {
        if (!TryProjectCamera(tri.A, cameraPosition, basis, width, height, out RasterVertex a) ||
            !TryProjectCamera(tri.B, cameraPosition, basis, width, height, out RasterVertex b) ||
            !TryProjectCamera(tri.C, cameraPosition, basis, width, height, out RasterVertex c))
            return;

        double area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 1e-9)
            return;

        int minX = Math.Clamp((int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
        if (maxX < minX || maxY < minY)
            return;

        double invZa = 1.0 / Math.Max(CameraNear, a.Z);
        double invZb = 1.0 / Math.Max(CameraNear, b.Z);
        double invZc = 1.0 / Math.Max(CameraNear, c.Z);
        Vec3 normal = tri.Normal.Normalize();
        Vec3 triangleCenter = (tri.A + tri.B + tri.C) / 3.0;
        if (normal.Dot(cameraPosition - triangleCenter) < 0.0)
            normal = normal * -1.0;

        double fastU = (tri.UvA.U + tri.UvB.U + tri.UvC.U) / 3.0;
        double fastV = (tri.UvA.V + tri.UvB.V + tri.UvC.V) / 3.0;
        Vec3 fastAlbedo = interactiveFast ? tri.Material.Sample(fastU, fastV) : Vec3.Zero;
        double fastAlpha = interactiveFast ? tri.Material.SampleAlpha(fastU, fastV) : 1.0;
        if (interactiveFast && fastAlpha < 0.04)
            return;

        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            int row = y * stride;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double wa = Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                double wb = Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                double wc = 1.0 - wa - wb;
                if (wa < -1e-7 || wb < -1e-7 || wc < -1e-7)
                    continue;

                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                int pixel = y * width + x;
                if (depth >= zBuffer[pixel])
                    continue;

                Vec3 world = tri.A * wa + tri.B * wb + tri.C * wc;
                Vec3 shaded;
                if (interactiveFast)
                {
                    shaded = ShadeFast(fastAlbedo, world, normal, lights);
                }
                else
                {
                    double invZ = wa * invZa + wb * invZb + wc * invZc;
                    double u = ((wa * tri.UvA.U * invZa) + (wb * tri.UvB.U * invZb) + (wc * tri.UvC.U * invZc)) / Math.Max(1e-12, invZ);
                    double v = ((wa * tri.UvA.V * invZa) + (wb * tri.UvB.V * invZb) + (wc * tri.UvC.V * invZc)) / Math.Max(1e-12, invZ);
                    double alpha = tri.Material.SampleAlpha(u, v);
                    if (alpha < 0.04)
                        continue;

                    Vec3 viewDirection = (cameraPosition - world).Normalize();
                    shaded = Shade(tri.Material, u, v, world, normal, viewDirection, lights);
                }
                zBuffer[pixel] = depth;

                int i = row + x * 4;
                bytes[i + 0] = ToByte(Gamma(shaded.Z));
                bytes[i + 1] = ToByte(Gamma(shaded.Y));
                bytes[i + 2] = ToByte(Gamma(shaded.X));
                bytes[i + 3] = 255;
            }
        }
    }

    private static Vec3 ShadeFast(Vec3 albedo, Vec3 point, Vec3 normal, IReadOnlyList<PreparedLight> lights)
    {
        Vec3 color = albedo * 0.085;

        foreach (PreparedLight prepared in lights)
        {
            SceneLight light = prepared.Light;
            Vec3 lightDir;
            double strength = light.Intensity;
            double attenuation = 1.0;
            double cone = 1.0;

            if (light.Kind == SceneLightKind.Directional)
            {
                lightDir = prepared.DirectionalLightDirection;
            }
            else
            {
                Vec3 toLight = light.Position - point;
                double distanceSquared = toLight.Dot(toLight);
                if (distanceSquared < 1e-12)
                    continue;
                double distance = Math.Sqrt(distanceSquared);
                if (light.Range > 0.0 && distance > light.Range)
                    continue;

                lightDir = toLight / distance;
                attenuation = DistanceAttenuation(distance, light.Range);
                if (light.Kind == SceneLightKind.Spot)
                {
                    cone = SpotConeFactor(prepared, lightDir * -1.0);
                    if (cone <= 0.0)
                        continue;
                }
            }

            double ndotl = Math.Max(0.0, normal.Dot(lightDir));
            if (ndotl <= 0.0)
                continue;

            double shadow = light.CastsShadow ? FindShadowFactorFast(prepared.ShadowMap, point, normal) : 1.0;
            color += albedo.Multiply(light.Color) * (ndotl * strength * attenuation * cone * shadow * 0.18);
        }

        return ClampColor(color);
    }

    private static Vec3 Shade(
        Material material,
        double u,
        double v,
        Vec3 point,
        Vec3 normal,
        Vec3 viewDirection,
        IReadOnlyList<PreparedLight> lights)
    {
        Vec3 albedo = material.Sample(u, v);
        (double metallic, double roughness) = material.SampleMetallicRoughness(u, v);
        Vec3 color = albedo * (0.075 + 0.035 * (1.0 - metallic));

        foreach (PreparedLight prepared in lights)
        {
            SceneLight light = prepared.Light;
            Vec3 lightDir;
            double strength = light.Intensity;
            double attenuation = 1.0;
            double cone = 1.0;

            if (light.Kind == SceneLightKind.Directional)
            {
                lightDir = prepared.DirectionalLightDirection;
            }
            else
            {
                Vec3 toLight = light.Position - point;
                double distance = toLight.Length();
                if (distance < 1e-8 || (light.Range > 0.0 && distance > light.Range))
                    continue;

                lightDir = toLight / distance;
                attenuation = DistanceAttenuation(distance, light.Range);
                if (light.Kind == SceneLightKind.Spot)
                {
                    Vec3 lightToSurface = lightDir * -1.0;
                    cone = SpotConeFactor(prepared, lightToSurface);
                    if (cone <= 0.0)
                        continue;
                }
            }

            double shadow = light.CastsShadow ? FindShadowFactor(prepared.ShadowMap, point, normal) : 1.0;
            color += LightContribution(albedo, normal, viewDirection, lightDir, light.Color, strength * attenuation * cone * shadow, metallic, roughness);
        }

        color += material.SampleEmission(u, v);
        return ClampColor(color);
    }

    private static Vec3 LightContribution(Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, Vec3 lightDir, Vec3 lightColor, double strength, double metallic, double roughness)
    {
        double ndotl = Math.Max(0.0, normal.Dot(lightDir));
        if (ndotl <= 0.0 || strength <= 0.0)
            return Vec3.Zero;

        double previewScale = 0.18;
        Vec3 diffuse = surfaceColor.Multiply(lightColor) * ((1.0 - metallic) * ndotl * strength * previewScale);
        Vec3 halfVector = (lightDir + viewDirection).Normalize();
        double ndoth = Math.Max(0.0, normal.Dot(halfVector));
        double shininess = Math.Clamp(2.0 / (roughness * roughness) - 2.0, 2.0, 192.0);
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
        double specularTerm = Math.Pow(ndoth, shininess) * ndotl * strength * previewScale * (1.0 - roughness * 0.65);
        Vec3 specular = f0.Multiply(lightColor) * (specularTerm * (1.0 + metallic));
        return diffuse + specular;
    }

    private static double DistanceAttenuation(double distance, double range)
    {
        double attenuation = 1.0 / (1.0 + 0.11 * distance * distance);
        if (range <= 0.0)
            return attenuation;

        double normalized = Math.Clamp(distance / range, 0.0, 1.0);
        double edgeFade = 1.0 - normalized * normalized;
        return attenuation * edgeFade * edgeFade;
    }

    private static double SpotConeFactor(PreparedLight prepared, Vec3 lightToSurface)
    {
        double theta = prepared.SpotForward.Dot(lightToSurface.Normalize());
        if (theta <= prepared.CosOuter) return 0.0;
        if (theta >= prepared.CosInner) return 1.0;
        return (theta - prepared.CosOuter) / Math.Max(1e-8, prepared.CosInner - prepared.CosOuter);
    }

    private static List<ShadowMap> BuildShadowMaps(Scene scene, CancellationToken cancellationToken)
    {
        List<ShadowMap> maps = new();
        Aabb bounds = scene.GetSceneBounds() ?? new Aabb(new Vec3(-2, -2, -2), new Vec3(2, 2, 2));
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        double radius = Math.Max(0.35, (bounds.Max - bounds.Min).Length() * 0.5);

        foreach (SceneLight light in scene.Lights.Where(l => l.Enabled && l.CastsShadow).Take(MaxShadowCastingLights))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShadowProjection projection = CreateShadowProjection(light, center, radius);
            ShadowMap map = new(light.Id, ShadowMapSize, projection);
            foreach (Triangle tri in scene.Triangles)
                RasterizeShadowTriangle(map, tri);
            maps.Add(map);
        }

        return maps;
    }

    private static ShadowProjection CreateShadowProjection(SceneLight light, Vec3 center, double radius)
    {
        if (light.Kind == SceneLightKind.Directional)
        {
            Vec3 directionalForward = light.Direction.Normalize();
            if (directionalForward.Length() < 1e-8)
                directionalForward = new Vec3(0.45, -0.75, 0.55).Normalize();
            Vec3 origin = center - directionalForward * (radius * 2.5);
            return ShadowProjection.CreateOrthographic(origin, directionalForward, radius * 1.45, radius * 5.0, bias: radius * 0.0025 + 0.0015);
        }

        Vec3 forward = light.Kind == SceneLightKind.Spot
            ? light.Direction.Normalize()
            : (center - light.Position).Normalize();
        if (forward.Length() < 1e-8)
            forward = new Vec3(0, -1, 0);

        double fov = light.Kind == SceneLightKind.Spot
            ? Math.Clamp(light.OuterConeAngle * 2.20, 18.0 * Math.PI / 180.0, 140.0 * Math.PI / 180.0)
            : Math.PI * 0.72;
        double far = light.Range > 0.0 ? Math.Max(light.Range, radius * 2.0) : radius * 5.0;
        return ShadowProjection.CreatePerspective(light.Position, forward, fov, far, bias: radius * 0.0035 + 0.0025);
    }

    private static void RasterizeShadowTriangle(ShadowMap map, Triangle tri)
    {
        if (!map.Projection.TryProject(tri.A, map.Size, map.Size, out RasterVertex a) ||
            !map.Projection.TryProject(tri.B, map.Size, map.Size, out RasterVertex b) ||
            !map.Projection.TryProject(tri.C, map.Size, map.Size, out RasterVertex c))
            return;

        double area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (Math.Abs(area) < 1e-9)
            return;

        int minX = Math.Clamp((int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, map.Size - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, map.Size - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, map.Size - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, map.Size - 1);

        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double wa = Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                double wb = Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                double wc = 1.0 - wa - wb;
                if (wa < -1e-7 || wb < -1e-7 || wc < -1e-7)
                    continue;

                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                int index = y * map.Size + x;
                if (depth < map.Depth[index])
                {
                    map.Depth[index] = depth;
                    map.HasDepth = true;
                }
            }
        }
    }

    private static double FindShadowFactorFast(ShadowMap? map, Vec3 point, Vec3 normal)
    {
        if (map == null || !map.Enabled)
            return 1.0;

        Vec3 biasedPoint = point + normal * map.Projection.Bias;
        if (!map.Projection.TryProject(biasedPoint, map.Size, map.Size, out RasterVertex p))
            return 1.0;

        int sx = (int)Math.Round(p.X);
        int sy = (int)Math.Round(p.Y);
        if (sx < 0 || sx >= map.Size || sy < 0 || sy >= map.Size)
            return 1.0;

        double storedDepth = map.Depth[sy * map.Size + sx];
        double visibility = (!double.IsFinite(storedDepth) || p.Z <= storedDepth + map.Projection.Bias) ? 1.0 : 0.0;
        return 0.30 + visibility * 0.70;
    }

    private static double FindShadowFactor(ShadowMap? map, Vec3 point, Vec3 normal)
    {
        if (map == null || !map.Enabled)
            return 1.0;

        Vec3 biasedPoint = point + normal * map.Projection.Bias;
        if (!map.Projection.TryProject(biasedPoint, map.Size, map.Size, out RasterVertex p))
            return 1.0;

        int centerX = (int)Math.Round(p.X);
        int centerY = (int)Math.Round(p.Y);
        double lit = 0.0;
        double samples = 0.0;
        for (int oy = -1; oy <= 1; oy++)
        {
            int sy = centerY + oy;
            if (sy < 0 || sy >= map.Size)
                continue;
            for (int ox = -1; ox <= 1; ox++)
            {
                int sx = centerX + ox;
                if (sx < 0 || sx >= map.Size)
                    continue;

                double storedDepth = map.Depth[sy * map.Size + sx];
                if (!double.IsFinite(storedDepth) || p.Z <= storedDepth + map.Projection.Bias)
                    lit += 1.0;
                samples += 1.0;
            }
        }

        if (samples <= 0.0)
            return 1.0;

        double visibility = lit / samples;
        return Math.Clamp(0.24 + visibility * 0.76, 0.24, 1.0);
    }

    private static bool TryProjectCamera(Vec3 p, Vec3 cameraPosition, CameraBasis basis, int width, int height, out RasterVertex projected)
    {
        Vec3 rel = p - cameraPosition;
        double z = rel.Dot(basis.Forward);
        if (!double.IsFinite(z) || z <= CameraNear)
        {
            projected = default;
            return false;
        }

        double aspect = width / (double)Math.Max(1, height);
        double fov = Math.Tan((CameraFovDegrees * Math.PI / 180.0) * 0.5);
        double u = rel.Dot(basis.Right) / z;
        double v = rel.Dot(basis.Up) / z;
        double x = (1.0 - u / (aspect * fov)) * 0.5 * width;
        double y = (1.0 - v / fov) * 0.5 * height;
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            projected = default;
            return false;
        }

        projected = new RasterVertex(x, y, z);
        return true;
    }

    private static double Edge(double ax, double ay, double bx, double by, double px, double py) =>
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    private static Vec3 ClampColor(Vec3 color) => new(
        Math.Clamp(color.X, 0.0, 8.0),
        Math.Clamp(color.Y, 0.0, 8.0),
        Math.Clamp(color.Z, 0.0, 8.0));

    private static double Gamma(double value) => Math.Pow(Math.Clamp(value, 0.0, 1.0), 1.0 / 2.2);
    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

    internal readonly record struct RasterVertex(double X, double Y, double Z);

    private readonly record struct PreparedLight(
        SceneLight Light,
        ShadowMap? ShadowMap,
        Vec3 DirectionalLightDirection,
        Vec3 SpotForward,
        double CosOuter,
        double CosInner);

    internal sealed class ShadowMap
    {
        public string LightId { get; }
        public int Size { get; }
        public ShadowProjection Projection { get; }
        public double[] Depth { get; }
        public bool Enabled => HasDepth;
        internal bool HasDepth { get; set; }

        public ShadowMap(string lightId, int size, ShadowProjection projection)
        {
            LightId = lightId;
            Size = size;
            Projection = projection;
            Depth = new double[size * size];
            Array.Fill(Depth, double.PositiveInfinity);
        }
    }

    internal readonly struct ShadowProjection
    {
        private readonly Vec3 origin;
        private readonly Vec3 forward;
        private readonly Vec3 right;
        private readonly Vec3 up;
        private readonly bool perspective;
        private readonly double halfExtent;
        private readonly double tanHalfFov;
        private readonly double farPlane;
        public double Bias { get; }

        private ShadowProjection(Vec3 origin, Vec3 forward, Vec3 right, Vec3 up, bool perspective, double halfExtent, double tanHalfFov, double farPlane, double bias)
        {
            this.origin = origin;
            this.forward = forward;
            this.right = right;
            this.up = up;
            this.perspective = perspective;
            this.halfExtent = Math.Max(1e-5, halfExtent);
            this.tanHalfFov = Math.Max(1e-5, tanHalfFov);
            this.farPlane = Math.Max(1e-5, farPlane);
            Bias = Math.Max(0.0001, bias);
        }

        public static ShadowProjection CreateOrthographic(Vec3 origin, Vec3 forward, double halfExtent, double farPlane, double bias)
        {
            CreateBasis(forward, out Vec3 f, out Vec3 r, out Vec3 u);
            return new ShadowProjection(origin, f, r, u, false, halfExtent, 1.0, farPlane, bias);
        }

        public static ShadowProjection CreatePerspective(Vec3 origin, Vec3 forward, double fovRadians, double farPlane, double bias)
        {
            CreateBasis(forward, out Vec3 f, out Vec3 r, out Vec3 u);
            return new ShadowProjection(origin, f, r, u, true, 1.0, Math.Tan(fovRadians * 0.5), farPlane, bias);
        }

        public bool TryProject(Vec3 point, int width, int height, out RasterVertex projected)
        {
            Vec3 rel = point - origin;
            double z = rel.Dot(forward);
            if (!double.IsFinite(z) || z <= CameraNear || z > farPlane)
            {
                projected = default;
                return false;
            }

            double xComponent = rel.Dot(right);
            double yComponent = rel.Dot(up);
            double normalizedX;
            double normalizedY;
            if (perspective)
            {
                normalizedX = xComponent / (z * tanHalfFov);
                normalizedY = yComponent / (z * tanHalfFov);
                if (normalizedX < -1.12 || normalizedX > 1.12 || normalizedY < -1.12 || normalizedY > 1.12)
                {
                    projected = default;
                    return false;
                }
            }
            else
            {
                normalizedX = xComponent / halfExtent;
                normalizedY = yComponent / halfExtent;
                if (normalizedX < -1.12 || normalizedX > 1.12 || normalizedY < -1.12 || normalizedY > 1.12)
                {
                    projected = default;
                    return false;
                }
            }

            double x = (normalizedX * 0.5 + 0.5) * (width - 1);
            double y = (0.5 - normalizedY * 0.5) * (height - 1);
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                projected = default;
                return false;
            }

            projected = new RasterVertex(x, y, z);
            return true;
        }

        private static void CreateBasis(Vec3 forward, out Vec3 f, out Vec3 r, out Vec3 u)
        {
            f = forward.Normalize();
            if (f.Length() < 1e-8)
                f = new Vec3(0, -1, 0);
            Vec3 helper = Math.Abs(f.Y) < 0.92 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            r = helper.Cross(f).Normalize();
            if (r.Length() < 1e-8)
                r = new Vec3(1, 0, 0);
            u = f.Cross(r).Normalize();
            if (u.Length() < 1e-8)
                u = new Vec3(0, 1, 0);
        }
    }
}
