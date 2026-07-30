// -----------------------------------------------------------------------------
// File: Scene/ReadyMadeObjectFactory.cs
// Purpose: Built-in geometry creation.
//
// Creates procedural objects such as room parts and simple props used by the ready-made asset library.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Creates procedural built-in objects for quick scene composition.</summary>
public static class ReadyMadeObjectFactory
{
    public static readonly string[] Names =
    {
        "Cube",
        "Tall Box",
        "Sphere",
        "Cylinder",
        "Pyramid",
        "Coffee Table",
        "Chair",
        "Sofa",
        "Side Cabinet",
        "Floor Lamp",
        "Plant",
        "Wall Screen",
        "Rug",
        "Pedestal",
        "Textured Cube"
    };

    /// <summary>Creates  for use by the renderer or editor.</summary>
    public static SceneObjectGroup Create(Scene scene, SceneMaterials materials, string name, Vec3 placement)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (materials == null) throw new ArgumentNullException(nameof(materials));

        string objectName = string.IsNullOrWhiteSpace(name) ? Names[0] : name.Trim();
        SceneObjectGroup group = scene.AddImportedGroup(objectName, selectable: true);

        switch (objectName)
        {
            case "Cube":
                AddBox(group, new Vec3(-0.35, 0.0, -0.35), new Vec3(0.35, 0.70, 0.35), materials.WhiteWall);
                break;
            case "Tall Box":
                AddBox(group, new Vec3(-0.28, 0.0, -0.28), new Vec3(0.28, 1.40, 0.28), materials.BlueWall);
                break;
            case "Sphere":
                AddSphere(group, new Vec3(0, 0.42, 0), 0.42, 18, 12, materials.Cushion);
                break;
            case "Cylinder":
                AddCylinder(group, new Vec3(0, 0.0, 0), 0.38, 0.85, 24, materials.Wood);
                break;
            case "Pyramid":
                AddPyramid(group, new Vec3(-0.45, 0.0, -0.45), new Vec3(0.45, 0.0, 0.45), new Vec3(0.0, 0.82, 0.0), materials.RedWall);
                break;
            case "Coffee Table":
                AddBox(group, new Vec3(-0.75, 0.34, -0.40), new Vec3(0.75, 0.52, 0.40), materials.Wood);
                AddBox(group, new Vec3(-0.66, 0.0, -0.32), new Vec3(-0.52, 0.34, -0.18), materials.DarkWood);
                AddBox(group, new Vec3(0.52, 0.0, -0.32), new Vec3(0.66, 0.34, -0.18), materials.DarkWood);
                AddBox(group, new Vec3(-0.66, 0.0, 0.18), new Vec3(-0.52, 0.34, 0.32), materials.DarkWood);
                AddBox(group, new Vec3(0.52, 0.0, 0.18), new Vec3(0.66, 0.34, 0.32), materials.DarkWood);
                break;
            case "Chair":
                AddBox(group, new Vec3(-0.42, 0.42, -0.42), new Vec3(0.42, 0.58, 0.42), materials.Wood);
                AddBox(group, new Vec3(-0.42, 0.58, 0.30), new Vec3(0.42, 1.22, 0.42), materials.Wood);
                AddBox(group, new Vec3(-0.38, 0.0, -0.36), new Vec3(-0.24, 0.42, -0.22), materials.DarkWood);
                AddBox(group, new Vec3(0.24, 0.0, -0.36), new Vec3(0.38, 0.42, -0.22), materials.DarkWood);
                AddBox(group, new Vec3(-0.38, 0.0, 0.22), new Vec3(-0.24, 0.42, 0.36), materials.DarkWood);
                AddBox(group, new Vec3(0.24, 0.0, 0.22), new Vec3(0.38, 0.42, 0.36), materials.DarkWood);
                break;
            case "Sofa":
                AddBox(group, new Vec3(-1.35, 0.00, -0.38), new Vec3(1.35, 0.45, 0.38), materials.Sofa);
                AddBox(group, new Vec3(-1.35, 0.45, 0.18), new Vec3(1.35, 1.05, 0.38), materials.Sofa);
                AddBox(group, new Vec3(-1.55, 0.00, -0.38), new Vec3(-1.30, 0.72, 0.38), materials.Sofa);
                AddBox(group, new Vec3(1.30, 0.00, -0.38), new Vec3(1.55, 0.72, 0.38), materials.Sofa);
                AddBox(group, new Vec3(-1.05, 0.48, -0.58), new Vec3(-0.18, 0.84, -0.36), materials.Cushion);
                AddBox(group, new Vec3(0.18, 0.48, -0.58), new Vec3(1.05, 0.84, -0.36), materials.Cushion);
                break;
            case "Side Cabinet":
                AddBox(group, new Vec3(-0.50, 0.00, -0.26), new Vec3(0.50, 0.48, 0.26), materials.Wood);
                AddBox(group, new Vec3(-0.45, 0.48, -0.22), new Vec3(0.45, 1.02, 0.22), materials.Wood);
                AddBox(group, new Vec3(-0.08, 0.56, -0.255), new Vec3(0.08, 0.66, -0.235), materials.DarkWood);
                break;
            case "Floor Lamp":
                AddBox(group, new Vec3(-0.08, 0.0, -0.08), new Vec3(0.08, 1.65, 0.08), materials.DarkWood);
                AddBox(group, new Vec3(-0.34, 1.65, -0.34), new Vec3(0.34, 2.05, 0.34), materials.LampGlow);
                break;
            case "Plant":
                AddBox(group, new Vec3(-0.25, 0.0, -0.25), new Vec3(0.25, 0.38, 0.25), materials.Pot);
                AddBox(group, new Vec3(-0.34, 0.38, -0.34), new Vec3(0.34, 0.78, 0.34), materials.Plant);
                AddBox(group, new Vec3(-0.14, 0.75, -0.14), new Vec3(0.14, 1.25, 0.14), materials.Plant);
                break;
            case "Wall Screen":
                AddBox(group, new Vec3(-0.95, 0.12, -0.04), new Vec3(0.95, 0.92, 0.04), materials.ScreenFrame);
                AddBox(group, new Vec3(-0.82, 0.22, -0.055), new Vec3(0.82, 0.82, -0.035), materials.Screen);
                break;
            case "Rug":
                AddBox(group, new Vec3(-1.20, 0.0, -0.75), new Vec3(1.20, 0.04, 0.75), materials.Rug);
                break;
            case "Pedestal":
                AddBox(group, new Vec3(-0.40, 0.0, -0.40), new Vec3(0.40, 0.16, 0.40), materials.DarkWood);
                AddBox(group, new Vec3(-0.28, 0.16, -0.28), new Vec3(0.28, 0.90, 0.28), materials.WhiteWall);
                AddBox(group, new Vec3(-0.45, 0.90, -0.45), new Vec3(0.45, 1.06, 0.45), materials.DarkWood);
                break;
            case "Textured Cube":
                AddBox(group, new Vec3(-0.45, 0.0, -0.45), new Vec3(0.45, 0.90, 0.45), new Material(new Vec3(1, 1, 1), texture: TextureMap.CreateChecker()));
                break;
            default:
                AddBox(group, new Vec3(-0.35, 0.0, -0.35), new Vec3(0.35, 0.70, 0.35), materials.WhiteWall);
                break;
        }

        group.RecalculatePivot();
        group.Position = placement;
        group.BakeCurrentTransform();
        return group;
    }

    /// <summary>Adds or creates box for this subsystem.</summary>
    private static void AddBox(SceneObjectGroup group, Vec3 min, Vec3 max, Material material)
    {
        double x0 = min.X, y0 = min.Y, z0 = min.Z, x1 = max.X, y1 = max.Y, z1 = max.Z;
        Vec3 p000 = new(x0, y0, z0), p001 = new(x0, y0, z1), p010 = new(x0, y1, z0), p011 = new(x0, y1, z1);
        Vec3 p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1);
        AddQuad(group, p001, p101, p111, p011, material);
        AddQuad(group, p100, p000, p010, p110, material);
        AddQuad(group, p000, p001, p011, p010, material);
        AddQuad(group, p101, p100, p110, p111, material);
        AddQuad(group, p010, p011, p111, p110, material);
        AddQuad(group, p000, p100, p101, p001, material);
    }

    /// <summary>Adds or creates pyramid for this subsystem.</summary>
    private static void AddPyramid(SceneObjectGroup group, Vec3 min, Vec3 max, Vec3 apex, Material material)
    {
        Vec3 a = new(min.X, min.Y, min.Z);
        Vec3 b = new(max.X, min.Y, min.Z);
        Vec3 c = new(max.X, min.Y, max.Z);
        Vec3 d = new(min.X, min.Y, max.Z);
        AddQuad(group, a, b, c, d, material);
        group.AddTriangle(a, apex, b, material);
        group.AddTriangle(b, apex, c, material);
        group.AddTriangle(c, apex, d, material);
        group.AddTriangle(d, apex, a, material);
    }

    /// <summary>Adds or creates cylinder for this subsystem.</summary>
    private static void AddCylinder(SceneObjectGroup group, Vec3 baseCenter, double radius, double height, int sides, Material material)
    {
        Vec3 topCenter = baseCenter + new Vec3(0, height, 0);
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 b0 = baseCenter + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 b1 = baseCenter + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            Vec3 t0 = b0 + new Vec3(0, height, 0);
            Vec3 t1 = b1 + new Vec3(0, height, 0);
            AddQuad(group, b0, b1, t1, t0, material);
            group.AddTriangle(baseCenter, b1, b0, material);
            group.AddTriangle(topCenter, t0, t1, material);
        }
    }

    /// <summary>Adds or creates sphere for this subsystem.</summary>
    private static void AddSphere(SceneObjectGroup group, Vec3 center, double radius, int longitudeSegments, int latitudeSegments, Material material)
    {
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            double theta0 = Math.PI * lat / latitudeSegments;
            double theta1 = Math.PI * (lat + 1) / latitudeSegments;
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                double phi0 = 2.0 * Math.PI * lon / longitudeSegments;
                double phi1 = 2.0 * Math.PI * (lon + 1) / longitudeSegments;
                Vec3 p00 = SpherePoint(center, radius, theta0, phi0);
                Vec3 p01 = SpherePoint(center, radius, theta0, phi1);
                Vec3 p10 = SpherePoint(center, radius, theta1, phi0);
                Vec3 p11 = SpherePoint(center, radius, theta1, phi1);
                if (lat == 0)
                    group.AddTriangle(p00, p11, p10, material);
                else if (lat == latitudeSegments - 1)
                    group.AddTriangle(p00, p01, p10, material);
                else
                    AddQuad(group, p00, p01, p11, p10, material);
            }
        }
    }

    /// <summary>Implements the sphere point operation for this file's subsystem.</summary>
    private static Vec3 SpherePoint(Vec3 center, double radius, double theta, double phi)
    {
        double sinTheta = Math.Sin(theta);
        return center + new Vec3(
            radius * sinTheta * Math.Cos(phi),
            radius * Math.Cos(theta),
            radius * sinTheta * Math.Sin(phi));
    }

    /// <summary>Adds or creates quad for this subsystem.</summary>
    private static void AddQuad(SceneObjectGroup group, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Material material)
    {
        group.AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), material);
        group.AddTriangle(a, c, d, new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), material);
    }
}
