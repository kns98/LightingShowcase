// -----------------------------------------------------------------------------
// File: Scene/SceneBuilder.cs
// Purpose: Default advanced material showcase scene.
//
// The startup scene is intentionally authored as a compact material/lighting lab
// for the CPU and Vulkan-compute ray tracers: emissive panels, point lights,
// metallic/roughness variation, glass/transmission, alpha-blended diffusers,
// shadows, indirect bounce targets, and mixed primitive geometry.
// -----------------------------------------------------------------------------

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Builds the default startup material showcase.</summary>
public sealed class SceneBuilder
{
    private readonly Scene scene;
    private readonly SceneMaterials m;

    private readonly Material matteWall = new(new Vec3(0.72, 0.72, 0.68), roughness: 0.88);
    private readonly Material darkWall = new(new Vec3(0.055, 0.060, 0.070), roughness: 0.72);
    private readonly Material warmFloor = new(new Vec3(0.52, 0.44, 0.34), metallic: 0.0, roughness: 0.42);
    private readonly Material blackRubber = new(new Vec3(0.010, 0.011, 0.012), metallic: 0.0, roughness: 0.86);
    private readonly Material whiteCeramic = new(new Vec3(0.86, 0.84, 0.78), metallic: 0.0, roughness: 0.22);
    private readonly Material brushedSteel = new(new Vec3(0.66, 0.67, 0.66), metallic: 1.0, roughness: 0.34);
    private readonly Material chrome = new(new Vec3(0.96, 0.96, 0.94), metallic: 1.0, roughness: 0.05);
    private readonly Material roughIron = new(new Vec3(0.30, 0.29, 0.27), metallic: 1.0, roughness: 0.74);
    private readonly Material copper = new(new Vec3(0.96, 0.54, 0.30), metallic: 1.0, roughness: 0.20);
    private readonly Material gold = new(new Vec3(1.00, 0.76, 0.34), metallic: 1.0, roughness: 0.16);
    private readonly Material clearGlass = new(new Vec3(0.86, 0.96, 1.00), alpha: 0.28, alphaBlend: true, metallic: 0.0, roughness: 0.03, transmission: 0.82);
    private readonly Material frostedGlass = new(new Vec3(0.76, 0.90, 1.00), alpha: 0.46, alphaBlend: true, metallic: 0.0, roughness: 0.52, transmission: 0.62);
    private readonly Material amberGlass = new(new Vec3(0.96, 0.50, 0.18), alpha: 0.36, alphaBlend: true, metallic: 0.0, roughness: 0.08, transmission: 0.70);
    private readonly Material water = new(new Vec3(0.42, 0.76, 1.00), alpha: 0.34, alphaBlend: true, metallic: 0.0, roughness: 0.02, transmission: 0.86);
    private readonly Material redNeon = new(new Vec3(1.00, 0.04, 0.025), emission: 4.5, emissionColor: new Vec3(1.0, 0.08, 0.04), roughness: 0.18);
    private readonly Material blueNeon = new(new Vec3(0.06, 0.24, 1.00), emission: 4.0, emissionColor: new Vec3(0.10, 0.32, 1.00), roughness: 0.18);
    private readonly Material warmLed = new(new Vec3(1.00, 0.78, 0.42), emission: 3.4, emissionColor: new Vec3(1.0, 0.70, 0.34), roughness: 0.24);
    private readonly Material coolLed = new(new Vec3(0.58, 0.78, 1.00), emission: 2.7, emissionColor: new Vec3(0.65, 0.84, 1.00), roughness: 0.22);
    private readonly Material redDiffuse = new(new Vec3(0.75, 0.08, 0.05), roughness: 0.78);
    private readonly Material blueDiffuse = new(new Vec3(0.05, 0.12, 0.75), roughness: 0.78);
    private readonly Material greenDiffuse = new(new Vec3(0.08, 0.48, 0.16), roughness: 0.72);
    private readonly Material wood = new(new Vec3(0.60, 0.36, 0.16), roughness: 0.50);

    public SceneBuilder(Scene scene, SceneMaterials materials)
    {
        this.scene = scene;
        m = materials;
    }

    /// <summary>Builds default scene content from editable grouped geometry.</summary>
    public void Build()
    {
        BuildShowcaseRoom();
        BuildNeonAndEmissionWall();
        BuildMetalRoughnessPedestals();
        BuildGlassTransmissionArea();
        BuildTranslucentLampFeature();
        BuildBounceAndShadowTestObjects();
        BuildCameraFramingProps();
        BuildLights();
    }

    private void BuildShowcaseRoom()
    {
        double x0 = -3.4, x1 = 3.4, y0 = -1.45, y1 = 2.35, z0 = 0.75, z1 = 7.10;
        const double t = 0.04;

        AddBoxPrimitive("Feature floor - semi glossy warm material", new Vec3(x0, y0 - t, z0), new Vec3(x1, y0, z1), warmFloor);
        AddBoxPrimitive("Matte ceiling", new Vec3(x0, y1, z0), new Vec3(x1, y1 + t, z1), matteWall);
        AddBoxPrimitive("Back wall - dark bounce background", new Vec3(x0, y0, z1), new Vec3(x1, y1, z1 + t), darkWall);
        AddBoxPrimitive("Left red diffuse bounce wall", new Vec3(x0 - t, y0, z0), new Vec3(x0, y1, z1), redDiffuse);
        AddBoxPrimitive("Right blue diffuse bounce wall", new Vec3(x1, y0, z0), new Vec3(x1 + t, y1, z1), blueDiffuse);
        AddBoxPrimitive("Front low threshold", new Vec3(x0, y0, z0 - t), new Vec3(x1, -1.05, z0), matteWall);

        AddBoxPrimitive("Glossy ceramic runway", new Vec3(-2.55, -1.43, 2.00), new Vec3(2.55, -1.38, 6.55), whiteCeramic);
        AddBoxPrimitive("Black rough rubber strip", new Vec3(-2.70, -1.37, 3.18), new Vec3(2.70, -1.31, 3.30), blackRubber);
        AddBoxPrimitive("Black rough rubber strip 2", new Vec3(-2.70, -1.37, 5.28), new Vec3(2.70, -1.31, 5.40), blackRubber);
    }

    private void BuildNeonAndEmissionWall()
    {
        scene.BeginGroup("Emissive wall signs and area panels");
        AddBoxPrimitive("Warm rectangular area emitter", new Vec3(-2.65, 0.90, 7.04), new Vec3(-1.55, 1.78, 7.08), warmLed);
        AddBoxPrimitive("Cool rectangular area emitter", new Vec3(1.55, 0.90, 7.04), new Vec3(2.65, 1.78, 7.08), coolLed);
        AddBoxPrimitive("Neon red horizontal tube", new Vec3(-1.15, 1.42, 7.02), new Vec3(1.15, 1.53, 7.10), redNeon);
        AddBoxPrimitive("Neon blue vertical tube", new Vec3(-0.06, 0.58, 7.01), new Vec3(0.06, 1.82, 7.11), blueNeon);
        AddBoxPrimitive("Dark wall inset behind neon", new Vec3(-1.35, 0.42, 7.00), new Vec3(1.35, 1.94, 7.03), blackRubber);
        scene.EndGroup();
    }

    private void BuildMetalRoughnessPedestals()
    {
        scene.BeginGroup("Metallic roughness comparison row");
        AddPedestal(-2.35, 3.05, "Chrome mirror", chrome);
        AddPedestal(-1.15, 3.05, "Brushed steel", brushedSteel);
        AddPedestal(0.05, 3.05, "Rough iron", roughIron);
        AddPedestal(1.25, 3.05, "Copper", copper);
        AddPedestal(2.45, 3.05, "Gold", gold);
        scene.EndGroup();
    }

    private void AddPedestal(double x, double z, string name, Material material)
    {
        AddBoxPrimitive(name + " pedestal", new Vec3(x - 0.36, -1.45, z - 0.36), new Vec3(x + 0.36, -0.94, z + 0.36), whiteCeramic);
        AddSpherePrimitive(name + " sphere", new Vec3(x, -0.55, z), 0.36, 28, 16, material);
    }

    private void BuildGlassTransmissionArea()
    {
        scene.BeginGroup("Glass transmission display");
        AddBoxPrimitive("Clear glass panel", new Vec3(-2.75, -1.20, 4.25), new Vec3(-2.62, 0.85, 5.15), clearGlass);
        AddBoxPrimitive("Frosted glass panel", new Vec3(-2.35, -1.20, 4.25), new Vec3(-2.22, 0.85, 5.15), frostedGlass);
        AddBoxPrimitive("Amber glass panel", new Vec3(-1.95, -1.20, 4.25), new Vec3(-1.82, 0.85, 5.15), amberGlass);

        AddBoxPrimitive("Water tank glass front", new Vec3(-1.45, -1.23, 4.20), new Vec3(-0.45, -0.20, 4.30), clearGlass);
        AddBoxPrimitive("Water tank glass back", new Vec3(-1.45, -1.23, 5.10), new Vec3(-0.45, -0.20, 5.20), clearGlass);
        AddBoxPrimitive("Water volume", new Vec3(-1.40, -1.12, 4.32), new Vec3(-0.50, -0.38, 5.08), water);
        AddSpherePrimitive("Object behind glass - red ball", new Vec3(-0.95, -0.78, 5.42), 0.22, 20, 12, redDiffuse);
        scene.EndGroup();
    }

    private void BuildTranslucentLampFeature()
    {
        scene.BeginGroup("Translucent lamp feature");
        AddCylinderPrimitive("Lamp black base", new Vec3(1.70, -1.45, 4.70), 0.34, 0.10, 32, blackRubber);
        AddCylinderPrimitive("Lamp copper pole", new Vec3(1.70, -1.35, 4.70), 0.055, 1.15, 20, copper);
        AddSpherePrimitive("Warm bulb visible through shade", new Vec3(1.70, -0.10, 4.70), 0.18, 24, 14, warmLed);
        AddCylinderPrimitive("Frosted translucent shade", new Vec3(1.70, -0.38, 4.70), 0.47, 0.52, 36, frostedGlass);
        AddBoxPrimitive("Lamp light catcher floor patch", new Vec3(1.05, -1.36, 4.05), new Vec3(2.35, -1.30, 5.35), new Material(new Vec3(0.78, 0.70, 0.58), roughness: 0.36));
        scene.EndGroup();
    }

    private void BuildBounceAndShadowTestObjects()
    {
        scene.BeginGroup("Bounce color and shadow tests");
        AddBoxPrimitive("Tall white bounce card", new Vec3(2.55, -1.45, 5.55), new Vec3(2.70, 0.80, 6.35), new Material(new Vec3(0.92, 0.90, 0.84), roughness: 0.84));
        AddBoxPrimitive("Small red bounce card", new Vec3(2.05, -1.45, 5.60), new Vec3(2.20, -0.25, 6.25), redDiffuse);
        AddBoxPrimitive("Small blue bounce card", new Vec3(2.87, -1.45, 5.60), new Vec3(3.02, -0.25, 6.25), blueDiffuse);
        AddSpherePrimitive("Matte shadow sphere", new Vec3(2.48, -0.98, 4.15), 0.34, 26, 15, new Material(new Vec3(0.72, 0.68, 0.62), roughness: 0.92));
        AddBoxPrimitive("Sharp shadow block", new Vec3(2.15, -1.45, 3.75), new Vec3(2.72, -0.82, 4.32), blackRubber);
        scene.EndGroup();
    }

    private void BuildCameraFramingProps()
    {
        scene.BeginGroup("Material labels as colored blocks");
        AddBoxPrimitive("Diffuse label block", new Vec3(-3.05, -1.45, 1.35), new Vec3(-2.55, -0.95, 1.85), greenDiffuse);
        AddBoxPrimitive("Metal label block", new Vec3(-2.35, -1.45, 1.35), new Vec3(-1.85, -0.95, 1.85), brushedSteel);
        AddBoxPrimitive("Glass label block", new Vec3(-1.65, -1.45, 1.35), new Vec3(-1.15, -0.95, 1.85), clearGlass);
        AddBoxPrimitive("Emission label block", new Vec3(-0.95, -1.45, 1.35), new Vec3(-0.45, -0.95, 1.85), blueNeon);
        AddCylinderPrimitive("Wood display cylinder", new Vec3(0.45, -1.45, 1.60), 0.30, 0.70, 28, wood);
        AddSpherePrimitive("Rough rubber ball", new Vec3(1.25, -1.02, 1.60), 0.30, 24, 14, blackRubber);
        scene.EndGroup();
    }

    private void BuildLights()
    {
        scene.Lights.Add(new SceneLight("large warm softbox", new Vec3(-2.10, 1.72, 5.40), new Vec3(1.0, 0.78, 0.48), 5.8, isDefault: true));
        scene.Lights.Add(new SceneLight("cool rim light", new Vec3(2.35, 1.55, 2.60), new Vec3(0.55, 0.72, 1.0), 4.2, isDefault: true));
        scene.Lights.Add(new SceneLight("lamp bulb", new Vec3(1.70, -0.05, 4.70), new Vec3(1.0, 0.68, 0.36), 5.2, isDefault: true));
        scene.Lights.Add(new SceneLight("small white key", new Vec3(0.0, 2.05, 3.50), new Vec3(1.0, 0.96, 0.88), 3.6, isDefault: true));
    }

    private SceneObjectGroup AddBoxPrimitive(string name, Vec3 min, Vec3 max, Material material)
    {
        scene.BeginGroup(name);
        scene.Box(min, max, material);
        SceneObjectGroup group = scene.EndGroup();
        StoreBoxPrimitiveParameters(group, min, max);
        return group;
    }

    private SceneObjectGroup AddSpherePrimitive(string name, Vec3 center, double radius, int segments, int rings, Material material)
    {
        scene.BeginGroup(name);
        for (int y = 0; y < rings; y++)
        {
            double v0 = (double)y / rings;
            double v1 = (double)(y + 1) / rings;
            double phi0 = -Math.PI * 0.5 + Math.PI * v0;
            double phi1 = -Math.PI * 0.5 + Math.PI * v1;

            for (int x = 0; x < segments; x++)
            {
                double u0 = (double)x / segments;
                double u1 = (double)(x + 1) / segments;
                Vec3 p00 = SpherePoint(center, radius, u0, phi0);
                Vec3 p10 = SpherePoint(center, radius, u1, phi0);
                Vec3 p01 = SpherePoint(center, radius, u0, phi1);
                Vec3 p11 = SpherePoint(center, radius, u1, phi1);
                Vec2 uv00 = new(u0, v0);
                Vec2 uv10 = new(u1, v0);
                Vec2 uv01 = new(u0, v1);
                Vec2 uv11 = new(u1, v1);

                if (y > 0)
                    scene.AddTriangle(p00, p10, p11, uv00, uv10, uv11, material);
                if (y < rings - 1)
                    scene.AddTriangle(p00, p11, p01, uv00, uv11, uv01, material);
            }
        }

        SceneObjectGroup group = scene.EndGroup();
        group.PrimitiveKind = "sphere";
        group.PrimitiveSourceName = "Sphere";
        group.PrimitiveParameters["originX"] = center.X;
        group.PrimitiveParameters["originY"] = center.Y;
        group.PrimitiveParameters["originZ"] = center.Z;
        group.PrimitiveParameters["radius"] = radius;
        return group;
    }

    private static Vec3 SpherePoint(Vec3 center, double radius, double u, double phi)
    {
        double theta = u * Math.PI * 2.0;
        double cp = Math.Cos(phi);
        return center + new Vec3(Math.Cos(theta) * cp * radius, Math.Sin(phi) * radius, Math.Sin(theta) * cp * radius);
    }

    private SceneObjectGroup AddCylinderPrimitive(string name, Vec3 baseCenter, double radius, double height, int sides, Material material)
    {
        scene.BeginGroup(name);
        Vec3 topCenter = baseCenter + new Vec3(0, height, 0);
        for (int i = 0; i < sides; i++)
        {
            double a0 = i * Math.PI * 2.0 / sides;
            double a1 = (i + 1) * Math.PI * 2.0 / sides;
            Vec3 b0 = baseCenter + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 b1 = baseCenter + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            Vec3 t0 = b0 + new Vec3(0, height, 0);
            Vec3 t1 = b1 + new Vec3(0, height, 0);
            double u0 = (double)i / sides;
            double u1 = (double)(i + 1) / sides;

            scene.AddTriangle(b0, b1, t1, new Vec2(u0, 1), new Vec2(u1, 1), new Vec2(u1, 0), material);
            scene.AddTriangle(b0, t1, t0, new Vec2(u0, 1), new Vec2(u1, 0), new Vec2(u0, 0), material);
            scene.AddTriangle(baseCenter, b1, b0, material);
            scene.AddTriangle(topCenter, t0, t1, material);
        }

        SceneObjectGroup group = scene.EndGroup();
        group.PrimitiveKind = "cylinder";
        group.PrimitiveSourceName = "Cylinder";
        group.PrimitiveParameters["originX"] = baseCenter.X;
        group.PrimitiveParameters["originY"] = baseCenter.Y + height * 0.5;
        group.PrimitiveParameters["originZ"] = baseCenter.Z;
        group.PrimitiveParameters["radius"] = radius;
        group.PrimitiveParameters["height"] = height;
        return group;
    }

    private static void StoreBoxPrimitiveParameters(SceneObjectGroup group, Vec3 a, Vec3 b)
    {
        Vec3 min = new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        Vec3 max = new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        Vec3 center = (min + max) * 0.5;
        Vec3 size = max - min;

        group.PrimitiveKind = "box";
        group.PrimitiveSourceName = "Cube";
        group.PrimitiveParameters.Clear();
        group.PrimitiveParameters["originX"] = center.X;
        group.PrimitiveParameters["originY"] = center.Y;
        group.PrimitiveParameters["originZ"] = center.Z;
        group.PrimitiveParameters["width"] = Math.Max(1e-6, size.X);
        group.PrimitiveParameters["height"] = Math.Max(1e-6, size.Y);
        group.PrimitiveParameters["depth"] = Math.Max(1e-6, size.Z);
    }
}
