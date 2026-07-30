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

            Vec3 surfaceColor = hit.Material.Sample(hit.TextureU, hit.TextureV);
            Vec3 emission = hit.Material.SampleEmission(hit.TextureU, hit.TextureV);
            if (emission.X > 0.0 || emission.Y > 0.0 || emission.Z > 0.0)
                radiance += throughput.Multiply(emission);

            // Preserve the existing local/direct lighting look at every visible hit,
            // then add stochastic indirect transport through the selected bounces.
            Vec3 direct = ShadeHit(hit, surfaceColor, currentRay.Direction) - emission;
            radiance += throughput.Multiply(direct);

            if (bounce == bounceCount)
                break;

            Vec3 normal = ApplyNormalMapApproximation(hit).Normalize();
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

        Vec3 surfaceColor = hit.Material.Sample(hit.TextureU, hit.TextureV);
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
            RenderMode.Unlit => hit.Material.Sample(hit.TextureU, hit.TextureV),
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
        Vec3 normal = ApplyNormalMapApproximation(hit);
        (double metallic, double roughness) = hit.Material.SampleMetallicRoughness(hit.TextureU, hit.TextureV);
        Vec3 color = surfaceColor * (0.035 + 0.025 * (1.0 - metallic));

        foreach (SceneLight light in scene.Lights)
        {
            if (light.Enabled)
                color += DirectLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness);
        }

        color += hit.Material.SampleEmission(hit.TextureU, hit.TextureV);
        return color;
    }

    private Vec3 ApplyNormalMapApproximation(Hit hit)
    {
        if (hit.Material.NormalTexture == null)
            return hit.Normal;

        // A full tangent-space normal map needs tangents.  Older scene triangles
        // do not store tangents, so use a safe small perturbation in a generated
        // local basis.  This preserves lighting texture/detail cues without
        // destabilizing intersections or requiring a scene format migration.
        Vec3 sample = hit.Material.SampleNormalMap(hit.TextureU, hit.TextureV);
        Vec3 n = hit.Normal.Normalize();
        Vec3 tangent = Math.Abs(n.Y) < 0.9 ? new Vec3(0, 1, 0).Cross(n).Normalize() : new Vec3(1, 0, 0).Cross(n).Normalize();
        Vec3 bitangent = n.Cross(tangent).Normalize();
        Vec3 mapped = (tangent * (sample.X * 2.0 - 1.0) + bitangent * (sample.Y * 2.0 - 1.0) + n * Math.Max(0.0, sample.Z * 2.0 - 1.0)).Normalize();
        return Vec3.Lerp(n, mapped, 0.45).Normalize();
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

    private static Vec3 LightContribution(Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, Vec3 lightDir, Vec3 lightColor, double strength, double metallic, double roughness)
    {
        double ndotl = Math.Max(0.0, normal.Dot(lightDir));
        if (ndotl <= 0.0 || strength <= 0.0)
            return Vec3.Zero;

        Vec3 diffuse = surfaceColor.Multiply(lightColor) * ((1.0 - metallic) * ndotl * strength);

        Vec3 view = (viewDirection * -1.0).Normalize();
        Vec3 halfVector = (lightDir + view).Normalize();
        double ndoth = Math.Max(0.0, normal.Dot(halfVector));
        double shininess = Math.Clamp(2.0 / (roughness * roughness) - 2.0, 2.0, 256.0);
        double specularTerm = Math.Pow(ndoth, shininess) * ndotl * strength;
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
        double roughnessDamping = 1.0 - roughness * 0.72;
        Vec3 specular = f0.Multiply(lightColor) * (specularTerm * roughnessDamping * (1.0 + metallic * 1.8));

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
