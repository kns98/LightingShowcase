// -----------------------------------------------------------------------------
// File: Rendering/RayTracer.cs
// Purpose: Ray tracing engine.
//
// Finds intersections, shades surfaces, samples lights, handles texture lookup,
// shadows, alpha/transmission blending, and camera ray generation.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.SceneGraph;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

/// <summary>Core ray tracer responsible for ray/scene intersection and surface lighting.</summary>
public sealed class RayTracer
{
    private const int MaxTransparencyDepth = 4;
    private const double Pi = Math.PI;
    private readonly Scene scene;

    /// <summary>Constructs and initializes this component.</summary>
    public RayTracer(Scene scene, LightingState lighting)
    {
        this.scene = scene;
    }

    /// <summary>Traces a ray through the scene and returns its shaded color.</summary>
    public Vec3 Trace(Ray ray) => Trace(ray, 0);

    /// <summary>Traces a ray using an explicit render/debug mode.</summary>
    public Vec3 Trace(Ray ray, RenderMode mode)
    {
        return mode == RenderMode.Lit ? Trace(ray, 0) : TraceDebug(ray, mode);
    }


    /// <summary>Traces a ray with optional indirect path bounces. A bounce count of zero is equivalent to the existing direct raytracer.</summary>
    public Vec3 TracePath(Ray ray, int bounceCount, int pixelX, int pixelY, int sampleIndex)
    {
        if (bounceCount <= 0)
            return Trace(ray, 0);

        Vec3 radiance = Vec3.Zero;
        Vec3 throughput = new(1.0, 1.0, 1.0);
        Ray currentRay = ray;
        uint rng = Seed(pixelX, pixelY, sampleIndex);

        for (int bounce = 0; bounce <= bounceCount; bounce++)
        {
            Hit? hit = scene.Intersect(currentRay);
            if (hit == null)
            {
                radiance += throughput.Multiply(Background(currentRay));
                break;
            }

            Vec3 surfaceColor = hit.Material.SampleLinear(hit.TextureU, hit.TextureV);
            Vec3 emission = hit.Material.SampleEmissionLinear(hit.TextureU, hit.TextureV);
            if (emission.X > 0.0 || emission.Y > 0.0 || emission.Z > 0.0)
                radiance += throughput.Multiply(emission);

            // Preserve the existing local/direct lighting look at every visible hit,
            // then add stochastic indirect transport through the selected bounces.
            Vec3 direct = ShadeHit(hit, surfaceColor, currentRay.Direction) - emission;
            radiance += throughput.Multiply(direct);

            if (bounce == bounceCount)
                break;

            Vec3 normal = ApplyNormalMap(hit).Normalize();
            if (normal.Dot(currentRay.Direction) > 0.0)
                normal = normal * -1.0;

            (double metallic, double roughness) = hit.Material.SampleMetallicRoughness(hit.TextureU, hit.TextureV);
            double specularProbability = Math.Clamp(metallic + (1.0 - roughness) * 0.18, 0.05, 0.9);
            double chooseSpecular = Next01(ref rng);

            Vec3 bounceDirection;
            Vec3 bounceWeight;
            if (chooseSpecular < specularProbability)
            {
                Vec3 reflected = Reflect(currentRay.Direction, normal).Normalize();
                Vec3 diffuseAroundReflection = CosineHemisphere(ref rng, reflected);
                bounceDirection = Vec3.Lerp(reflected, diffuseAroundReflection, Math.Clamp(roughness * roughness, 0.0, 1.0)).Normalize();
                Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
                bounceWeight = f0 / specularProbability;
            }
            else
            {
                bounceDirection = CosineHemisphere(ref rng, normal);
                bounceWeight = surfaceColor * ((1.0 - metallic) / Math.Max(1e-6, 1.0 - specularProbability));
            }

            throughput = throughput.Multiply(bounceWeight);
            double maxChannel = Math.Max(throughput.X, Math.Max(throughput.Y, throughput.Z));
            if (maxChannel < 0.002)
                break;

            currentRay = new Ray(hit.Point + bounceDirection * 0.003, bounceDirection);
        }

        return radiance;
    }

    private Vec3 Trace(Ray ray, int depth)
    {
        Hit? hit = scene.Intersect(ray);
        if (hit == null) return Background(ray);

        Vec3 surfaceColor = hit.Material.SampleLinear(hit.TextureU, hit.TextureV);
        double alpha = hit.Material.SampleAlpha(hit.TextureU, hit.TextureV);
        double transmission = hit.Material.Transmission;
        double visibleOpacity = Math.Clamp(alpha * (1.0 - transmission * 0.72), 0.0, 1.0);

        Vec3 shaded = ShadeHit(hit, surfaceColor, ray.Direction);

        // glTF transmission is true refractive transport.  This compact renderer
        // uses a practical approximation: blend the shaded glass/surface with the
        // color seen straight through it.  It makes lamp shades and glass samples
        // read correctly without turning the editor into a full path tracer.
        if ((hit.Material.AlphaBlend || transmission > 0.0 || alpha < 0.999) && visibleOpacity < 0.995 && depth < MaxTransparencyDepth)
        {
            Ray throughRay = new(hit.Point + ray.Direction * 0.004, ray.Direction);
            Vec3 through = Trace(throughRay, depth + 1);
            double tintStrength = Math.Clamp(transmission * 0.55, 0.0, 0.75);
            Vec3 tintedThrough = Vec3.Lerp(through, through.Multiply(surfaceColor), tintStrength);
            return Vec3.Lerp(tintedThrough, shaded, visibleOpacity);
        }

        return shaded;
    }


    private static Vec3 Reflect(Vec3 direction, Vec3 normal)
    {
        return direction - normal * (2.0 * direction.Dot(normal));
    }

    private static Vec3 CosineHemisphere(ref uint state, Vec3 normal)
    {
        double r1 = Next01(ref state);
        double r2 = Next01(ref state);
        double phi = 2.0 * Math.PI * r1;
        double radius = Math.Sqrt(r2);
        double x = Math.Cos(phi) * radius;
        double y = Math.Sin(phi) * radius;
        double z = Math.Sqrt(Math.Max(0.0, 1.0 - r2));

        Vec3 n = normal.Normalize();
        Vec3 tangent = Math.Abs(n.Y) < 0.9
            ? new Vec3(0, 1, 0).Cross(n).Normalize()
            : new Vec3(1, 0, 0).Cross(n).Normalize();
        Vec3 bitangent = n.Cross(tangent).Normalize();
        return (tangent * x + bitangent * y + n * z).Normalize();
    }

    private static uint Seed(int x, int y, int sampleIndex)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)x) * 16777619u;
            hash = (hash ^ (uint)y) * 16777619u;
            hash = (hash ^ (uint)sampleIndex) * 16777619u;
            return hash == 0 ? 1u : hash;
        }
    }

    private static double Next01(ref uint state)
    {
        unchecked
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFFu) / 16777216.0;
        }
    }

    private Vec3 TraceDebug(Ray ray, RenderMode mode)
    {
        Hit? hit = scene.Intersect(ray);
        if (hit == null) return Background(ray);

        return mode switch
        {
            RenderMode.Unlit => hit.Material.SampleLinear(hit.TextureU, hit.TextureV),
            RenderMode.NormalDebug => (hit.Normal.Normalize() + new Vec3(1, 1, 1)) * 0.5,
            RenderMode.UvDebug => new Vec3(Frac(hit.TextureU), Frac(hit.TextureV), 0.25),
            RenderMode.MaterialDebug => new Vec3(hit.Material.Metallic, hit.Material.Roughness, hit.Material.Emission > 0.0 ? 1.0 : 0.0),
            RenderMode.Depth => DepthColor((hit.Point - ray.Origin).Length()),
            RenderMode.LightDebug => LightDebug(hit),
            _ => Trace(ray, 0)
        };
    }

    private static double Frac(double value) => value - Math.Floor(value);

    private static Vec3 DepthColor(double distance)
    {
        double t = Math.Clamp(distance / 25.0, 0.0, 1.0);
        return new Vec3(1.0 - t, 1.0 - t, 1.0 - t);
    }

    private Vec3 LightDebug(Hit hit)
    {
        Vec3 color = Vec3.Zero;
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled) continue;
            Vec3 toLight = light.Kind == SceneLightKind.Directional ? light.Direction * -1.0 : light.Position - hit.Point;
            double influence = Math.Max(0.0, hit.Normal.Normalize().Dot(toLight.Normalize()));
            color += light.Color * Math.Clamp(influence * light.Intensity * 0.12, 0.0, 1.0);
        }
        return color;
    }

    private Vec3 ShadeHit(Hit hit, Vec3 surfaceColor, Vec3 viewDirection)
    {
        Vec3 normal = ApplyNormalMap(hit);
        Vec3 view = (-viewDirection).Normalize();
        if (normal.Dot(view) < 0.0)
            normal = -normal;

        (double metallic, double roughness) = hit.Material.SampleMetallicRoughness(hit.TextureU, hit.TextureV);
        Vec3 direct = Vec3.Zero;
        foreach (SceneLight light in scene.Lights)
        {
            if (light.Enabled)
                direct += DirectLight(hit, light, surfaceColor, normal, view, metallic, roughness);
        }

        double occlusion = hit.Material.SampleOcclusion(hit.TextureU, hit.TextureV);
        Vec3 indirect = EnvironmentLighting(surfaceColor, normal, view, metallic, roughness) * occlusion;
        Vec3 emission = hit.Material.SampleEmissionLinear(hit.TextureU, hit.TextureV);
        return direct + indirect + emission;
    }

    private static Vec3 ApplyNormalMap(Hit hit)
    {
        Vec3 normal = hit.Normal.Normalize();
        if (hit.Material.NormalTexture == null)
            return normal;

        Vec3 tangent = hit.Tangent.Normalize();
        Vec3 bitangent = hit.Bitangent.Normalize();
        if (tangent.Length() < 1e-8 || bitangent.Length() < 1e-8)
        {
            Vec3 axis = Math.Abs(normal.Z) < 0.999
                ? new Vec3(0.0, 0.0, 1.0)
                : new Vec3(0.0, 1.0, 0.0);
            tangent = axis.Cross(normal).Normalize();
            bitangent = normal.Cross(tangent).Normalize();
        }

        Vec3 sample = hit.Material.SampleNormalMap(hit.TextureU, hit.TextureV);
        double scale = hit.Material.NormalScale;
        Vec3 tangentNormal = new(
            (sample.X * 2.0 - 1.0) * scale,
            (sample.Y * 2.0 - 1.0) * scale,
            sample.Z * 2.0 - 1.0);
        tangentNormal = tangentNormal.Normalize();
        return (
            tangent * tangentNormal.X +
            bitangent * tangentNormal.Y +
            normal * tangentNormal.Z).Normalize();
    }

    /// <summary>Implements the direct light operation for this file's subsystem.</summary>
    private Vec3 DirectLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
    {
        return light.Kind switch
        {
            SceneLightKind.Directional => DirectDirectionalLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness),
            SceneLightKind.Spot => DirectSpotLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness),
            _ => DirectPointLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness)
        };
    }

    private Vec3 DirectPointLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
    {
        Vec3 toLight = light.Position - hit.Point;
        double distance = toLight.Length();
        if (distance < 1e-8 || (light.Range > 0.0 && distance > light.Range))
            return Vec3.Zero;

        Vec3 lightDir = toLight / distance;
        double shadow = light.CastsShadow ? ShadowFactor(hit, lightDir, distance) : 1.0;
        double attenuation = DistanceAttenuation(distance, light.Range);
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * attenuation * shadow, metallic, roughness);
    }

    private Vec3 DirectDirectionalLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
    {
        // glTF directional lights shine along local -Z. SceneLight.Direction is
        // the direction light travels, so surfaces are lit from the opposite ray.
        Vec3 lightDir = (light.Direction * -1.0).Normalize();
        double shadow = light.CastsShadow ? ShadowFactor(hit, lightDir, double.PositiveInfinity) : 1.0;
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * shadow, metallic, roughness);
    }

    private Vec3 DirectSpotLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
    {
        Vec3 toLight = light.Position - hit.Point;
        double distance = toLight.Length();
        if (distance < 1e-8 || (light.Range > 0.0 && distance > light.Range))
            return Vec3.Zero;

        Vec3 lightDir = toLight / distance;
        Vec3 lightToSurface = lightDir * -1.0;
        double cone = SpotConeFactor(light, lightToSurface);
        if (cone <= 0.0)
            return Vec3.Zero;

        double shadow = light.CastsShadow ? ShadowFactor(hit, lightDir, distance) : 1.0;
        double attenuation = DistanceAttenuation(distance, light.Range);
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * attenuation * cone * shadow, metallic, roughness);
    }

    private static Vec3 LightContribution(Vec3 surfaceColor, Vec3 normal, Vec3 view, Vec3 lightDir, Vec3 lightColor, double strength, double metallic, double roughness)
    {
        double nDotL = Math.Max(0.0, normal.Dot(lightDir));
        double nDotV = Math.Max(0.0001, normal.Dot(view));
        if (nDotL <= 0.0 || strength <= 0.0)
            return Vec3.Zero;

        Vec3 halfVector = (lightDir + view).Normalize();
        double nDotH = Math.Max(0.0, normal.Dot(halfVector));
        double vDotH = Math.Max(0.0, view.Dot(halfVector));
        double alphaRoughness = roughness * roughness;
        double distribution = DistributionGgx(nDotH, alphaRoughness);
        double visibility = VisibilitySmithGgxCorrelated(nDotV, nDotL, alphaRoughness);
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
        Vec3 fresnel = FresnelSchlick(vDotH, f0);
        Vec3 specular = fresnel * (distribution * visibility);
        Vec3 diffuse = (new Vec3(1.0, 1.0, 1.0) - fresnel).Multiply(surfaceColor) * ((1.0 - metallic) / Pi);
        Vec3 radiance = lightColor * (strength * 0.18);
        return (diffuse + specular).Multiply(radiance) * nDotL;
    }

    private static Vec3 EnvironmentLighting(Vec3 baseColor, Vec3 normal, Vec3 view, double metallic, double roughness)
    {
        double nDotV = Math.Max(0.0001, normal.Dot(view));
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), baseColor, metallic);
        double grazing = Math.Pow(1.0 - nDotV, 5.0);
        Vec3 maximum = new(
            Math.Max(1.0 - roughness, f0.X),
            Math.Max(1.0 - roughness, f0.Y),
            Math.Max(1.0 - roughness, f0.Z));
        Vec3 fresnel = f0 + (maximum - f0) * grazing;
        Vec3 oneMinusFresnel = new Vec3(1.0, 1.0, 1.0) - fresnel;
        Vec3 diffuse = DiffuseEnvironment(normal).Multiply(baseColor).Multiply(oneMinusFresnel) * (1.0 - metallic);
        Vec3 reflection = Reflect(-view, normal).Normalize();
        Vec3 sharpSpecular = StudioEnvironment(reflection);
        Vec3 broadSpecular = DiffuseEnvironment(normal);
        Vec3 specular = Vec3.Lerp(sharpSpecular, broadSpecular, roughness * roughness).Multiply(fresnel);
        return (diffuse + specular) * 0.72;
    }

    private static double DistributionGgx(double nDotH, double alphaRoughness)
    {
        double a2 = alphaRoughness * alphaRoughness;
        double f = nDotH * nDotH * (a2 - 1.0) + 1.0;
        return a2 / Math.Max(Pi * f * f, 1e-8);
    }

    private static double VisibilitySmithGgxCorrelated(double nDotV, double nDotL, double alphaRoughness)
    {
        double a2 = alphaRoughness * alphaRoughness;
        double gv = nDotL * Math.Sqrt(Math.Max(nDotV * nDotV * (1.0 - a2) + a2, 0.0));
        double gl = nDotV * Math.Sqrt(Math.Max(nDotL * nDotL * (1.0 - a2) + a2, 0.0));
        return 0.5 / Math.Max(gv + gl, 1e-8);
    }

    private static Vec3 FresnelSchlick(double vDotH, Vec3 f0)
    {
        double factor = Math.Pow(1.0 - Math.Clamp(vDotH, 0.0, 1.0), 5.0);
        return f0 + (new Vec3(1.0, 1.0, 1.0) - f0) * factor;
    }

    private static Vec3 StudioEnvironment(Vec3 direction)
    {
        direction = direction.Normalize();
        double skyAmount = SmoothStep(-0.25, 0.65, direction.Y);
        Vec3 radiance = Vec3.Lerp(new Vec3(0.035, 0.040, 0.050), new Vec3(0.32, 0.39, 0.49), skyAmount);
        Vec3 keyDirection = new Vec3(-0.55, 0.62, -0.56).Normalize();
        Vec3 rimDirection = new Vec3(0.78, 0.28, 0.56).Normalize();
        double key = Math.Pow(Math.Max(direction.Dot(keyDirection), 0.0), 96.0);
        double rim = Math.Pow(Math.Max(direction.Dot(rimDirection), 0.0), 180.0);
        return radiance + new Vec3(5.2, 4.8, 4.2) * key + new Vec3(2.0, 2.7, 3.8) * rim;
    }

    private static Vec3 DiffuseEnvironment(Vec3 normal)
    {
        double skyAmount = Math.Clamp(normal.Y * 0.5 + 0.5, 0.0, 1.0);
        return Vec3.Lerp(new Vec3(0.055, 0.060, 0.070), new Vec3(0.42, 0.47, 0.54), skyAmount);
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        double t = Math.Clamp((value - edge0) / Math.Max(edge1 - edge0, 1e-8), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
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

    private static double SpotConeFactor(SceneLight light, Vec3 lightToSurface)
    {
        double theta = light.Direction.Normalize().Dot(lightToSurface.Normalize());
        double outer = Math.Cos(light.OuterConeAngle);
        double inner = Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle));
        if (theta <= outer) return 0.0;
        if (theta >= inner) return 1.0;
        return (theta - outer) / Math.Max(1e-8, inner - outer);
    }

    private double ShadowFactor(Hit hit, Vec3 lightDir, double distance)
    {
        Ray shadowRay = new(hit.Point + hit.Normal * 0.002, lightDir);
        double opacity = scene.ShadowOpacity(shadowRay, distance, maxSamples: 8);
        return Math.Clamp(1.0 - opacity * 0.82, 0.18, 1.0);
    }

    private static Vec3 Background(Ray ray)
    {
        double t = Math.Clamp(ray.Direction.Y * 0.5 + 0.5, 0.0, 1.0);
        return Vec3.Lerp(new Vec3(0.01, 0.012, 0.016), new Vec3(0.055, 0.06, 0.072), t);
    }

    /// <summary>Converts linear HDR ray-traced color to display-referred sRGB using Khronos PBR Neutral tone mapping.</summary>
    public static Vec3 ToDisplayColor(Vec3 linearColor, double exposure = 1.0)
    {
        Vec3 color = SanitizeLinear(linearColor * Math.Max(0.0, exposure));
        color = PbrNeutralToneMap(color);
        return new Vec3(
            LinearChannelToSrgb(color.X),
            LinearChannelToSrgb(color.Y),
            LinearChannelToSrgb(color.Z));
    }

    private static Vec3 PbrNeutralToneMap(Vec3 color)
    {
        const double startCompression = 0.76;
        const double desaturation = 0.15;
        double minimum = Math.Min(color.X, Math.Min(color.Y, color.Z));
        double offset = minimum < 0.08 ? minimum - 6.25 * minimum * minimum : 0.04;
        color -= new Vec3(offset, offset, offset);

        double peak = Math.Max(color.X, Math.Max(color.Y, color.Z));
        if (peak < startCompression)
            return SanitizeLinear(color);

        double d = 1.0 - startCompression;
        double newPeak = 1.0 - d * d / (peak + d - startCompression);
        color *= newPeak / Math.Max(peak, 1e-8);
        double grayMix = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
        return SanitizeLinear(Vec3.Lerp(color, new Vec3(newPeak, newPeak, newPeak), grayMix));
    }

    private static Vec3 SanitizeLinear(Vec3 color) => new(
        SanitizeLinearChannel(color.X),
        SanitizeLinearChannel(color.Y),
        SanitizeLinearChannel(color.Z));

    private static double SanitizeLinearChannel(double value)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            return 0.0;
        return Math.Min(value, 65504.0);
    }

    private static double LinearChannelToSrgb(double value)
    {
        value = Math.Max(value, 0.0);
        return value <= 0.0031308
            ? value * 12.92
            : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
    }

    /// <summary>Implements the ray direction operation for this file's subsystem.</summary>
    public static Vec3 RayDirection(int x, int y, int width, int height, CameraBasis basis)
        => RayDirection(x + 0.5, y + 0.5, width, height, basis);

    /// <summary>Implements the ray direction operation for this file's subsystem.</summary>
    public static Vec3 RayDirection(double x, double y, int width, int height, CameraBasis basis)
        => RayDirection(x, y, width, height, basis, 72.0);

    /// <summary>Builds a camera ray using an explicit vertical field of view.</summary>
    public static Vec3 RayDirection(double x, double y, int width, int height, CameraBasis basis, double fieldOfViewDegrees)
    {
        double aspect = width / (double)height;
        double safeFov = Math.Clamp(fieldOfViewDegrees, 1.0, 179.0);
        double fov = Math.Tan((safeFov * Math.PI / 180.0) / 2.0);
        // Match the Helix/WPF viewport's screen handedness.  WPF's camera projects
        // positive screen X opposite the previous software-render path, which made
        // the raytraced render and saved images appear left/right mirrored compared
        // with the editable viewport.  Keep the vertical mapping unchanged and only
        // flip the horizontal screen coordinate at ray generation time so picking,
        // progressive preview, final render, and image export all stay consistent.
        double u = (1.0 - 2.0 * x / width) * aspect * fov;
        double v = (1.0 - 2.0 * y / height) * fov;
        return (basis.Forward + basis.Right * u + basis.Up * v).Normalize();
    }
}
