// -----------------------------------------------------------------------------
// File: Scene/ReadyMadeObjectLibrary.cs
// Purpose: Built-in asset catalog.
//
// Provides named factory entries for insertable objects shown in the UI.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Catalog of named ready-made objects shown in the UI.</summary>
public static class ReadyMadeObjectLibrary
{
    public static readonly string[] Names =
    {
        // Basic triangle-mesh primitives
        "Plane",
        "Cube",
        "Sphere",
        "Low-poly Sphere",
        "Hemisphere",
        "Cylinder",
        "Cone",
        "Torus",
        "Tube",
        "Capsule",
        "Pyramid",
        "Triangular Prism",
        "Wedge",

        // Simple room/furniture props
        "Dining Table",
        "Coffee Table",
        "Chair",
        "Sofa",
        "Bed",
        "Bookshelf",
        "Storage Cabinet",
        "TV / Screen",
        "Floor Lamp",
        "Potted Plant",
        "Rug",
        "Wall Panel",
        "Pedestal"
    };

    /// <summary>Implements the contains operation for this file's subsystem.</summary>
    public static bool Contains(string name) => Names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));


    /// <summary>
    /// Converts the compact primitive kind stored in binary scene files back to a
    /// ready-made catalog name. Older saves may contain either a compact kind
    /// (for example, "sphere") or the original ready-made source name. Prefer a
    /// valid source name when present so round-tripping preserves furniture and
    /// variant names such as "Low-poly Sphere" and "TV / Screen".
    /// </summary>
    public static string ReadyMadeNameForPrimitiveKind(string? primitiveKind, string? sourceName)
    {
        if (!string.IsNullOrWhiteSpace(sourceName) && Contains(sourceName.Trim()))
            return Names.First(n => string.Equals(n, sourceName.Trim(), StringComparison.OrdinalIgnoreCase));

        string key = NormalizePrimitiveKey(primitiveKind);
        if (string.IsNullOrWhiteSpace(key))
            return Names[0];

        return key switch
        {
            "plane" => "Plane",
            "cube" => "Cube",
            "box" => "Cube",
            "sphere" => "Sphere",
            "lowpolysphere" => "Low-poly Sphere",
            "low-poly sphere" => "Low-poly Sphere",
            "hemisphere" => "Hemisphere",
            "cylinder" => "Cylinder",
            "cone" => "Cone",
            "torus" => "Torus",
            "tube" => "Tube",
            "capsule" => "Capsule",
            "pyramid" => "Pyramid",
            "triangularprism" => "Triangular Prism",
            "triangular prism" => "Triangular Prism",
            "wedge" => "Wedge",
            "diningtable" => "Dining Table",
            "dining table" => "Dining Table",
            "coffeetable" => "Coffee Table",
            "coffee table" => "Coffee Table",
            "chair" => "Chair",
            "sofa" => "Sofa",
            "bed" => "Bed",
            "bookshelf" => "Bookshelf",
            "storagecabinet" => "Storage Cabinet",
            "storage cabinet" => "Storage Cabinet",
            "cabinet" => "Storage Cabinet",
            "tvscreen" => "TV / Screen",
            "tv / screen" => "TV / Screen",
            "screen" => "TV / Screen",
            "floorlamp" => "Floor Lamp",
            "floor lamp" => "Floor Lamp",
            "pottedplant" => "Potted Plant",
            "potted plant" => "Potted Plant",
            "plant" => "Potted Plant",
            "rug" => "Rug",
            "wallpanel" => "Wall Panel",
            "wall panel" => "Wall Panel",
            "pedestal" => "Pedestal",
            _ when Contains(primitiveKind ?? string.Empty) => Names.First(n => string.Equals(n, primitiveKind!.Trim(), StringComparison.OrdinalIgnoreCase)),
            _ => Names[0]
        };
    }

    /// <summary>
    /// Converts a ready-made catalog name to the compact primitive kind persisted
    /// in binary scene files. The returned value is intentionally stable and
    /// lower-case so future readers do not depend on UI display text.
    /// </summary>
    public static string PrimitiveKindForReadyMade(string? readyMadeName)
    {
        string name = string.IsNullOrWhiteSpace(readyMadeName) ? Names[0] : readyMadeName.Trim();
        if (!Contains(name))
            name = ReadyMadeNameForPrimitiveKind(name, name);

        return name.ToLowerInvariant() switch
        {
            "low-poly sphere" => "lowPolySphere",
            "triangular prism" => "triangularPrism",
            "dining table" => "diningTable",
            "coffee table" => "coffeeTable",
            "storage cabinet" => "storageCabinet",
            "tv / screen" => "tvScreen",
            "floor lamp" => "floorLamp",
            "potted plant" => "pottedPlant",
            "wall panel" => "wallPanel",
            _ => NormalizePrimitiveKey(name)
        };
    }

    private static string NormalizePrimitiveKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string trimmed = value.Trim();
        if (trimmed.Contains(' '))
            return trimmed.ToLowerInvariant();

        return trimmed.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    public static SceneObjectGroup Insert(Scene scene, SceneMaterials materials, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = Names[0];

        scene.BeginGroup(name);
        switch (name.Trim().ToLowerInvariant())
        {
            case "plane": BuildPlane(scene, materials); break;
            case "sphere": BuildSphere(scene, materials, 32, 16); break;
            case "low-poly sphere": BuildSphere(scene, materials, 12, 8); break;
            case "hemisphere": BuildHemisphere(scene, materials); break;
            case "cylinder": BuildCylinder(scene, materials); break;
            case "cone": BuildCone(scene, materials); break;
            case "torus": BuildTorus(scene, materials); break;
            case "tube": BuildTube(scene, materials); break;
            case "capsule": BuildCapsule(scene, materials); break;
            case "pyramid": BuildPyramid(scene, materials); break;
            case "triangular prism": BuildTriangularPrism(scene, materials); break;
            case "wedge": BuildWedge(scene, materials); break;
            case "dining table": BuildDiningTable(scene, materials); break;
            case "coffee table": BuildCoffeeTable(scene, materials); break;
            case "chair": BuildChair(scene, materials); break;
            case "sofa": BuildSofa(scene, materials); break;
            case "bed": BuildBed(scene, materials); break;
            case "bookshelf": BuildBookshelf(scene, materials); break;
            case "storage cabinet": BuildStorageCabinet(scene, materials); break;
            case "tv / screen": BuildScreen(scene, materials); break;
            case "floor lamp": BuildFloorLamp(scene, materials); break;
            case "potted plant": BuildPlant(scene, materials); break;
            case "rug": BuildRug(scene, materials); break;
            case "wall panel": BuildWallPanel(scene, materials); break;
            case "pedestal": BuildPedestal(scene, materials); break;
            default: BuildCube(scene, materials); break;
        }

        SceneObjectGroup group = scene.EndGroup();
        group.PrimitiveKind = PrimitiveKindForReadyMade(name);
        group.PrimitiveSourceName = ReadyMadeNameForPrimitiveKind(group.PrimitiveKind, name);
        StoreDefaultPrimitiveParametersFromShadow(group);
        return group;
    }

    /// <summary>Updates the authored parameter dictionary from the current triangle-shadow bounds.</summary>
    public static void StoreDefaultPrimitiveParametersFromShadow(SceneObjectGroup group)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        Aabb bounds = group.GetWorldBounds(includeHidden: true);
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        group.PrimitiveParameters.Clear();
        group.PrimitiveParameters["originX"] = center.X;
        group.PrimitiveParameters["originY"] = center.Y;
        group.PrimitiveParameters["originZ"] = center.Z;

        string kind = NormalizePrimitiveKey(group.PrimitiveKind ?? group.PrimitiveSourceName);
        double width = Math.Max(1e-6, size.X);
        double height = Math.Max(1e-6, size.Y);
        double depth = Math.Max(1e-6, size.Z);
        double radius = Math.Max(width, depth) * 0.5;

        switch (kind)
        {
            case "sphere":
            case "lowpolysphere":
                group.PrimitiveParameters["radius"] = Math.Max(Math.Max(width, height), depth) * 0.5;
                break;
            case "hemisphere":
                group.PrimitiveParameters["radius"] = radius;
                group.PrimitiveParameters["height"] = height;
                break;
            case "cylinder":
            case "cone":
                group.PrimitiveParameters["radius"] = radius;
                group.PrimitiveParameters["height"] = height;
                break;
            case "tube":
                group.PrimitiveParameters["outerRadius"] = radius;
                group.PrimitiveParameters["innerRadius"] = Math.Max(1e-6, radius * 0.58);
                group.PrimitiveParameters["height"] = height;
                break;
            case "torus":
                double minor = Math.Max(1e-6, height * 0.5);
                group.PrimitiveParameters["majorRadius"] = Math.Max(1e-6, radius - minor);
                group.PrimitiveParameters["minorRadius"] = minor;
                break;
            case "capsule":
                group.PrimitiveParameters["radius"] = radius;
                group.PrimitiveParameters["totalHeight"] = height;
                break;
            case "plane":
            case "rug":
            case "wallpanel":
                group.PrimitiveParameters["width"] = width;
                group.PrimitiveParameters["depth"] = depth;
                group.PrimitiveParameters["thickness"] = height;
                break;
            default:
                group.PrimitiveParameters["width"] = width;
                group.PrimitiveParameters["height"] = height;
                group.PrimitiveParameters["depth"] = depth;
                break;
        }
    }

    /// <summary>Regenerates the triangle-shadow mesh from authored primitive parameters.</summary>
    public static bool RebuildPrimitiveShadowGeometry(SceneObjectGroup group, SceneMaterials materials)
    {
        if (group == null) throw new ArgumentNullException(nameof(group));
        if (materials == null) throw new ArgumentNullException(nameof(materials));
        if (string.IsNullOrWhiteSpace(group.PrimitiveKind) || group.PrimitiveParameters.Count == 0 || group.Children.Count > 0)
            return false;

        string name = ReadyMadeNameForPrimitiveKind(group.PrimitiveKind, group.PrimitiveSourceName);
        Material material = group.FirstMaterialOrDefault() ?? materials.WhiteWall;
        Scene temporary = new();
        temporary.BeginGroup(group.Name);
        BuildParameterizedPrimitive(temporary, materials, name, group.PrimitiveParameters, material);
        SceneObjectGroup shadow = temporary.EndGroup();

        group.LocalTriangles.Clear();
        foreach (Triangle triangle in shadow.LocalTriangles)
            group.LocalTriangles.Add(new Triangle(triangle.A, triangle.B, triangle.C, triangle.UvA, triangle.UvB, triangle.UvC, triangle.Material, group.Id));
        group.RecalculatePivot();
        return true;
    }

    private static void BuildParameterizedPrimitive(Scene scene, SceneMaterials materials, string name, IReadOnlyDictionary<string, double> parameters, Material material)
    {
        Vec3 origin = new(Read(parameters, "originX", 0.0), Read(parameters, "originY", 0.0), Read(parameters, "originZ", 3.55));
        string key = name.Trim().ToLowerInvariant();
        double Radius(string parameter = "radius", double fallback = 0.5) => Math.Max(1e-6, Read(parameters, parameter, fallback));
        double Size(string parameter, double fallback) => Math.Max(1e-6, Read(parameters, parameter, fallback));

        switch (key)
        {
            case "sphere":
                AddSphere(scene, origin, Radius(), 32, 16, material);
                break;
            case "low-poly sphere":
                AddSphere(scene, origin, Radius(), 12, 8, material);
                break;
            case "hemisphere":
                AddHemisphere(scene, origin - new Vec3(0, Size("height", Radius()) * 0.5, 0), Radius(), 32, 8, upper: true, material);
                AddDisk(scene, origin - new Vec3(0, Size("height", Radius()) * 0.5, 0), Radius(), 32, normalUp: false, material);
                break;
            case "cylinder":
                AddCylinder(scene, origin - new Vec3(0, Size("height", 1.0) * 0.5, 0), Radius(), Size("height", 1.0), 32, material);
                break;
            case "cone":
                AddCone(scene, origin - new Vec3(0, Size("height", 1.0) * 0.5, 0), Radius(), Size("height", 1.0), 32, material);
                break;
            case "torus":
                AddTorus(scene, origin, Radius("majorRadius", 0.45), Radius("minorRadius", 0.12), 40, 16, material);
                break;
            case "tube":
            {
                double outer = Radius("outerRadius", 0.5);
                double inner = Math.Min(outer * 0.95, Radius("innerRadius", outer * 0.58));
                double height = Size("height", 1.0);
                AddTube(scene, origin - new Vec3(0, height * 0.5, 0), outer, inner, height, 36, material);
                break;
            }
            case "capsule":
            {
                double radius = Radius();
                double totalHeight = Math.Max(radius * 2.0, Size("totalHeight", 1.0));
                double cylinderHeight = Math.Max(1e-6, totalHeight - radius * 2.0);
                Vec3 lowerCenter = origin - new Vec3(0, cylinderHeight * 0.5, 0);
                Vec3 upperCenter = origin + new Vec3(0, cylinderHeight * 0.5, 0);
                AddCylinderSides(scene, lowerCenter, radius, cylinderHeight, 28, material);
                AddHemisphere(scene, upperCenter, radius, 28, 8, upper: true, material);
                AddHemisphere(scene, lowerCenter, radius, 28, 8, upper: false, material);
                break;
            }
            case "plane":
            case "rug":
            case "wall panel":
            case "wallpanel":
            {
                double width = Size("width", 1.0);
                double depth = Size("depth", 1.0);
                double thickness = Size("thickness", 0.02);
                scene.Box(origin - new Vec3(width * 0.5, thickness * 0.5, depth * 0.5), origin + new Vec3(width * 0.5, thickness * 0.5, depth * 0.5), material);
                break;
            }
            default:
            {
                double width = Size("width", 1.0);
                double height = Size("height", 1.0);
                double depth = Size("depth", 1.0);
                scene.Box(origin - new Vec3(width * 0.5, height * 0.5, depth * 0.5), origin + new Vec3(width * 0.5, height * 0.5, depth * 0.5), material);
                break;
            }
        }
    }

    private static double Read(IReadOnlyDictionary<string, double> parameters, string key, double fallback) =>
        parameters.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;

    // Primitive mesh builders. Every curved primitive is approximated by composite triangles.
    private static void BuildPlane(Scene scene, SceneMaterials m)
    {
        AddQuad(scene, new Vec3(-0.85, -0.90, 3.10), new Vec3(0.85, -0.90, 3.10), new Vec3(0.85, -0.90, 4.10), new Vec3(-0.85, -0.90, 4.10), m.WhiteWall);
    }

    /// <summary>Implements the build cube operation for this file's subsystem.</summary>
    private static void BuildCube(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-0.45, -0.95, 3.05), new Vec3(0.45, -0.05, 3.95), m.WhiteWall);
    }

    /// <summary>Implements the build sphere operation for this file's subsystem.</summary>
    private static void BuildSphere(Scene scene, SceneMaterials m, int longitudeSegments, int latitudeSegments)
    {
        AddSphere(scene, new Vec3(0.0, -0.52, 3.55), 0.52, longitudeSegments, latitudeSegments, m.Cushion);
    }

    /// <summary>Implements the build hemisphere operation for this file's subsystem.</summary>
    private static void BuildHemisphere(Scene scene, SceneMaterials m)
    {
        Vec3 center = new(0.0, -0.92, 3.55);
        double radius = 0.62;
        AddHemisphere(scene, center, radius, 32, 8, upper: true, m.Cushion);
        AddDisk(scene, center, radius, 32, normalUp: false, m.Cushion);
    }

    /// <summary>Implements the build cylinder operation for this file's subsystem.</summary>
    private static void BuildCylinder(Scene scene, SceneMaterials m)
    {
        AddCylinder(scene, new Vec3(0.0, -1.10, 3.55), 0.45, 1.05, 32, m.Wood);
    }

    /// <summary>Implements the build cone operation for this file's subsystem.</summary>
    private static void BuildCone(Scene scene, SceneMaterials m)
    {
        AddCone(scene, new Vec3(0.0, -1.10, 3.55), 0.55, 1.15, 32, m.RedWall);
    }

    /// <summary>Implements the build torus operation for this file's subsystem.</summary>
    private static void BuildTorus(Scene scene, SceneMaterials m)
    {
        AddTorus(scene, new Vec3(0.0, -0.58, 3.55), 0.48, 0.16, 40, 16, m.BlueWall);
    }

    /// <summary>Implements the build tube operation for this file's subsystem.</summary>
    private static void BuildTube(Scene scene, SceneMaterials m)
    {
        AddTube(scene, new Vec3(0.0, -1.05, 3.55), outerRadius: 0.55, innerRadius: 0.32, height: 1.05, sides: 36, m.Wood);
    }

    /// <summary>Implements the build capsule operation for this file's subsystem.</summary>
    private static void BuildCapsule(Scene scene, SceneMaterials m)
    {
        Vec3 lowerCenter = new(0.0, -0.95, 3.55);
        Vec3 upperCenter = new(0.0, -0.28, 3.55);
        double radius = 0.34;
        AddCylinderSides(scene, lowerCenter, radius, upperCenter.Y - lowerCenter.Y, 28, m.Cushion);
        AddHemisphere(scene, upperCenter, radius, 28, 8, upper: true, m.Cushion);
        AddHemisphere(scene, lowerCenter, radius, 28, 8, upper: false, m.Cushion);
    }

    /// <summary>Implements the build pyramid operation for this file's subsystem.</summary>
    private static void BuildPyramid(Scene scene, SceneMaterials m)
    {
        Vec3 a = new(-0.55, -1.05, 3.05);
        Vec3 b = new(0.55, -1.05, 3.05);
        Vec3 c = new(0.55, -1.05, 4.05);
        Vec3 d = new(-0.55, -1.05, 4.05);
        Vec3 apex = new(0.0, 0.05, 3.55);
        AddQuad(scene, a, b, c, d, m.RedWall);
        scene.AddTriangle(a, apex, b, m.RedWall);
        scene.AddTriangle(b, apex, c, m.RedWall);
        scene.AddTriangle(c, apex, d, m.RedWall);
        scene.AddTriangle(d, apex, a, m.RedWall);
    }

    /// <summary>Implements the build triangular prism operation for this file's subsystem.</summary>
    private static void BuildTriangularPrism(Scene scene, SceneMaterials m)
    {
        Vec3 a0 = new(-0.55, -1.05, 3.15);
        Vec3 b0 = new(0.55, -1.05, 3.15);
        Vec3 c0 = new(0.0, -0.15, 3.15);
        Vec3 a1 = new(-0.55, -1.05, 4.05);
        Vec3 b1 = new(0.55, -1.05, 4.05);
        Vec3 c1 = new(0.0, -0.15, 4.05);
        scene.AddTriangle(a0, c0, b0, m.BlueWall);
        scene.AddTriangle(a1, b1, c1, m.BlueWall);
        AddQuad(scene, a0, a1, c1, c0, m.BlueWall);
        AddQuad(scene, b1, b0, c0, c1, m.BlueWall);
        AddQuad(scene, a0, b0, b1, a1, m.BlueWall);
    }

    /// <summary>Implements the build wedge operation for this file's subsystem.</summary>
    private static void BuildWedge(Scene scene, SceneMaterials m)
    {
        Vec3 a = new(-0.60, -1.05, 3.10);
        Vec3 b = new(0.60, -1.05, 3.10);
        Vec3 c = new(-0.60, -1.05, 4.10);
        Vec3 d = new(0.60, -1.05, 4.10);
        Vec3 e = new(-0.60, -0.10, 4.10);
        Vec3 f = new(0.60, -0.10, 4.10);
        AddQuad(scene, a, b, d, c, m.Wood);      // bottom
        AddQuad(scene, c, d, f, e, m.Wood);      // tall back
        scene.AddTriangle(a, e, c, m.Wood);      // left triangular side
        scene.AddTriangle(b, d, f, m.Wood);
        AddQuad(scene, a, e, f, b, m.Wood);      // ramp
    }

    // Furniture builders retained from the previous ready-made object library.
    private static void BuildDiningTable(Scene scene, SceneMaterials m)
    {
        scene.BeginGroup("Table top");
        scene.Box(new Vec3(-1.05, -0.05, 3.15), new Vec3(1.05, 0.12, 4.05), m.Wood);
        scene.EndGroup();

        BuildLegGroup(scene, m, "Front left leg", -0.90, 3.28);
        BuildLegGroup(scene, m, "Front right leg", 0.90, 3.28);
        BuildLegGroup(scene, m, "Back left leg", -0.90, 3.92);
        BuildLegGroup(scene, m, "Back right leg", 0.90, 3.92);
    }

    /// <summary>Implements the build coffee table operation for this file's subsystem.</summary>
    private static void BuildCoffeeTable(Scene scene, SceneMaterials m)
    {
        scene.BeginGroup("Coffee table top");
        scene.Box(new Vec3(-0.75, -0.80, 3.20), new Vec3(0.75, -0.62, 3.85), m.Wood);
        scene.EndGroup();

        BuildLegGroup(scene, m, "Front left leg", -0.62, 3.30, -1.25, -0.80);
        BuildLegGroup(scene, m, "Front right leg", 0.62, 3.30, -1.25, -0.80);
        BuildLegGroup(scene, m, "Back left leg", -0.62, 3.75, -1.25, -0.80);
        BuildLegGroup(scene, m, "Back right leg", 0.62, 3.75, -1.25, -0.80);
    }

    /// <summary>Implements the build chair operation for this file's subsystem.</summary>
    private static void BuildChair(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-0.42, -0.72, 3.20), new Vec3(0.42, -0.52, 3.72), m.Wood);
        scene.Box(new Vec3(-0.42, -0.52, 3.62), new Vec3(0.42, 0.26, 3.78), m.Wood);
        Leg(scene, m, -0.32, 3.28, -1.25, -0.72); Leg(scene, m, 0.32, 3.28, -1.25, -0.72);
        Leg(scene, m, -0.32, 3.62, -1.25, -0.72); Leg(scene, m, 0.32, 3.62, -1.25, -0.72);
    }

    /// <summary>Implements the build sofa operation for this file's subsystem.</summary>
    private static void BuildSofa(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-1.35, -1.05, 3.65), new Vec3(1.35, -0.62, 4.35), m.Sofa);
        scene.Box(new Vec3(-1.35, -0.62, 4.12), new Vec3(1.35, 0.20, 4.35), m.Sofa);
        scene.Box(new Vec3(-1.55, -1.05, 3.65), new Vec3(-1.30, -0.42, 4.35), m.Sofa);
        scene.Box(new Vec3(1.30, -1.05, 3.65), new Vec3(1.55, -0.42, 4.35), m.Sofa);
        scene.Box(new Vec3(-0.95, -0.58, 3.45), new Vec3(-0.15, -0.18, 3.65), m.Cushion);
        scene.Box(new Vec3(0.15, -0.58, 3.45), new Vec3(0.95, -0.18, 3.65), m.Cushion);
    }

    /// <summary>Implements the build bed operation for this file's subsystem.</summary>
    private static void BuildBed(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-1.25, -1.10, 3.00), new Vec3(1.25, -0.78, 4.85), m.Wood);
        scene.Box(new Vec3(-1.15, -0.78, 3.15), new Vec3(1.15, -0.46, 4.72), m.Cushion);
        scene.Box(new Vec3(-1.25, -0.46, 4.62), new Vec3(1.25, 0.25, 4.85), m.Sofa);
        scene.Box(new Vec3(-0.95, -0.42, 3.18), new Vec3(-0.10, -0.20, 3.70), m.WhiteWall);
        scene.Box(new Vec3(0.10, -0.42, 3.18), new Vec3(0.95, -0.20, 3.70), m.WhiteWall);
    }

    /// <summary>Implements the build bookshelf operation for this file's subsystem.</summary>
    private static void BuildBookshelf(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-0.90, -1.30, 3.15), new Vec3(0.90, 0.65, 3.35), m.Wood);
        scene.Box(new Vec3(-0.82, -1.18, 3.05), new Vec3(0.82, 0.53, 3.18), m.DarkWood);
        for (int i = 0; i < 4; i++)
        {
            double y = -1.02 + i * 0.42;
            scene.Box(new Vec3(-0.78, y, 3.00), new Vec3(0.78, y + 0.07, 3.22), m.Wood);
            scene.Box(new Vec3(-0.72, y + 0.09, 3.01), new Vec3(-0.36, y + 0.35, 3.20), m.RedWall);
            scene.Box(new Vec3(-0.30, y + 0.09, 3.01), new Vec3(0.05, y + 0.35, 3.20), m.BlueWall);
            scene.Box(new Vec3(0.12, y + 0.09, 3.01), new Vec3(0.52, y + 0.35, 3.20), m.Cushion);
        }
    }

    /// <summary>Implements the build storage cabinet operation for this file's subsystem.</summary>
    private static void BuildStorageCabinet(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-0.95, -1.20, 3.15), new Vec3(0.95, -0.15, 3.75), m.Wood);
        scene.Box(new Vec3(-0.88, -1.12, 3.10), new Vec3(-0.03, -0.22, 3.12), m.DarkWood);
        scene.Box(new Vec3(0.03, -1.12, 3.10), new Vec3(0.88, -0.22, 3.12), m.DarkWood);
    }

    /// <summary>Implements the build screen operation for this file's subsystem.</summary>
    private static void BuildScreen(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-1.05, -0.15, 3.45), new Vec3(1.05, 1.05, 3.52), m.ScreenFrame);
        scene.Box(new Vec3(-0.92, -0.02, 3.40), new Vec3(0.92, 0.92, 3.46), m.Screen);
        scene.Box(new Vec3(-0.20, -0.85, 3.72), new Vec3(0.20, -0.15, 3.92), m.DarkWood);
        scene.Box(new Vec3(-0.70, -0.90, 3.55), new Vec3(0.70, -0.75, 4.10), m.DarkWood);
    }

    /// <summary>Implements the build floor lamp operation for this file's subsystem.</summary>
    private static void BuildFloorLamp(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-0.10, -1.35, 3.45), new Vec3(0.10, 0.62, 3.65), m.DarkWood);
        scene.Box(new Vec3(-0.42, 0.62, 3.18), new Vec3(0.42, 1.05, 3.92), m.LampGlow);
        scene.Box(new Vec3(-0.42, -1.45, 3.18), new Vec3(0.42, -1.35, 3.92), m.DarkWood);
    }

    /// <summary>Implements the build plant operation for this file's subsystem.</summary>
    private static void BuildPlant(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-0.32, -1.35, 3.20), new Vec3(0.32, -0.55, 3.82), m.Pot);
        scene.Box(new Vec3(-0.45, -0.55, 3.08), new Vec3(0.45, -0.05, 3.94), m.Plant);
        scene.Box(new Vec3(-0.22, -0.05, 3.28), new Vec3(0.22, 0.55, 3.75), m.Plant);
    }

    /// <summary>Implements the build rug operation for this file's subsystem.</summary>
    private static void BuildRug(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-1.35, -1.48, 3.05), new Vec3(1.35, -1.40, 4.65), m.Rug);
    }

    /// <summary>Implements the build wall panel operation for this file's subsystem.</summary>
    private static void BuildWallPanel(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-1.05, -0.55, 3.90), new Vec3(1.05, 0.90, 3.98), m.WhiteWall);
        scene.Box(new Vec3(-0.95, -0.45, 3.86), new Vec3(0.95, 0.80, 3.91), m.ScreenFrame);
    }

    /// <summary>Implements the build pedestal operation for this file's subsystem.</summary>
    private static void BuildPedestal(Scene scene, SceneMaterials m)
    {
        scene.Box(new Vec3(-0.42, -1.35, 3.20), new Vec3(0.42, -0.10, 4.02), m.Ceiling);
        scene.Box(new Vec3(-0.55, -0.10, 3.08), new Vec3(0.55, 0.08, 4.14), m.WhiteWall);
    }

    /// <summary>Implements the leg operation for this file's subsystem.</summary>
    private static void Leg(Scene scene, SceneMaterials m, double x, double z, double y0 = -1.25, double y1 = -0.05)
    {
        scene.Box(new Vec3(x - 0.06, y0, z - 0.06), new Vec3(x + 0.06, y1, z + 0.06), m.DarkWood);
    }

    private static void BuildLegGroup(Scene scene, SceneMaterials m, string name, double x, double z, double y0 = -1.25, double y1 = -0.05)
    {
        scene.BeginGroup(name);
        Leg(scene, m, x, z, y0, y1);
        scene.EndGroup();
    }

    /// <summary>Adds or creates sphere for this subsystem.</summary>
    private static void AddSphere(Scene scene, Vec3 center, double radius, int longitudeSegments, int latitudeSegments, Material material)
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
                    scene.AddTriangle(p00, p11, p10, material);
                else if (lat == latitudeSegments - 1)
                    scene.AddTriangle(p00, p01, p10, material);
                else
                    AddQuad(scene, p00, p01, p11, p10, material);
            }
        }
    }

    /// <summary>Adds or creates hemisphere for this subsystem.</summary>
    private static void AddHemisphere(Scene scene, Vec3 center, double radius, int longitudeSegments, int latitudeSegments, bool upper, Material material)
    {
        double startTheta = upper ? 0.0 : Math.PI / 2.0;
        double endTheta = upper ? Math.PI / 2.0 : Math.PI;
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            double theta0 = startTheta + (endTheta - startTheta) * lat / latitudeSegments;
            double theta1 = startTheta + (endTheta - startTheta) * (lat + 1) / latitudeSegments;
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                double phi0 = 2.0 * Math.PI * lon / longitudeSegments;
                double phi1 = 2.0 * Math.PI * (lon + 1) / longitudeSegments;
                Vec3 p00 = SpherePoint(center, radius, theta0, phi0);
                Vec3 p01 = SpherePoint(center, radius, theta0, phi1);
                Vec3 p10 = SpherePoint(center, radius, theta1, phi0);
                Vec3 p11 = SpherePoint(center, radius, theta1, phi1);
                if (lat == 0 && upper)
                    scene.AddTriangle(p00, p11, p10, material);
                else if (lat == latitudeSegments - 1 && !upper)
                    scene.AddTriangle(p00, p01, p10, material);
                else
                    AddQuad(scene, p00, p01, p11, p10, material);
            }
        }
    }

    /// <summary>Implements the sphere point operation for this file's subsystem.</summary>
    private static Vec3 SpherePoint(Vec3 center, double radius, double theta, double phi)
    {
        double sinTheta = Math.Sin(theta);
        return center + new Vec3(radius * sinTheta * Math.Cos(phi), radius * Math.Cos(theta), radius * sinTheta * Math.Sin(phi));
    }

    /// <summary>Adds or creates cylinder for this subsystem.</summary>
    private static void AddCylinder(Scene scene, Vec3 baseCenter, double radius, double height, int sides, Material material)
    {
        AddCylinderSides(scene, baseCenter, radius, height, sides, material);
        AddDisk(scene, baseCenter, radius, sides, normalUp: false, material);
        AddDisk(scene, baseCenter + new Vec3(0, height, 0), radius, sides, normalUp: true, material);
    }

    /// <summary>Adds or creates cylinder sides for this subsystem.</summary>
    private static void AddCylinderSides(Scene scene, Vec3 baseCenter, double radius, double height, int sides, Material material)
    {
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 b0 = baseCenter + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 b1 = baseCenter + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            Vec3 t0 = b0 + new Vec3(0, height, 0);
            Vec3 t1 = b1 + new Vec3(0, height, 0);
            AddQuad(scene, b0, b1, t1, t0, material);
        }
    }

    /// <summary>Adds or creates cone for this subsystem.</summary>
    private static void AddCone(Scene scene, Vec3 baseCenter, double radius, double height, int sides, Material material)
    {
        Vec3 apex = baseCenter + new Vec3(0, height, 0);
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 b0 = baseCenter + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 b1 = baseCenter + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            scene.AddTriangle(b0, apex, b1, material);
            scene.AddTriangle(baseCenter, b1, b0, material);
        }
    }

    /// <summary>Adds or creates torus for this subsystem.</summary>
    private static void AddTorus(Scene scene, Vec3 center, double majorRadius, double tubeRadius, int majorSegments, int tubeSegments, Material material)
    {
        for (int i = 0; i < majorSegments; i++)
        {
            double u0 = 2.0 * Math.PI * i / majorSegments;
            double u1 = 2.0 * Math.PI * (i + 1) / majorSegments;
            for (int j = 0; j < tubeSegments; j++)
            {
                double v0 = 2.0 * Math.PI * j / tubeSegments;
                double v1 = 2.0 * Math.PI * (j + 1) / tubeSegments;
                Vec3 p00 = TorusPoint(center, majorRadius, tubeRadius, u0, v0);
                Vec3 p01 = TorusPoint(center, majorRadius, tubeRadius, u0, v1);
                Vec3 p10 = TorusPoint(center, majorRadius, tubeRadius, u1, v0);
                Vec3 p11 = TorusPoint(center, majorRadius, tubeRadius, u1, v1);
                AddQuad(scene, p00, p10, p11, p01, material);
            }
        }
    }

    /// <summary>Implements the torus point operation for this file's subsystem.</summary>
    private static Vec3 TorusPoint(Vec3 center, double majorRadius, double tubeRadius, double u, double v)
    {
        double radial = majorRadius + tubeRadius * Math.Cos(v);
        return center + new Vec3(radial * Math.Cos(u), tubeRadius * Math.Sin(v), radial * Math.Sin(u));
    }

    /// <summary>Adds or creates tube for this subsystem.</summary>
    private static void AddTube(Scene scene, Vec3 baseCenter, double outerRadius, double innerRadius, double height, int sides, Material material)
    {
        Vec3 topCenter = baseCenter + new Vec3(0, height, 0);
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 ob0 = baseCenter + new Vec3(Math.Cos(a0) * outerRadius, 0, Math.Sin(a0) * outerRadius);
            Vec3 ob1 = baseCenter + new Vec3(Math.Cos(a1) * outerRadius, 0, Math.Sin(a1) * outerRadius);
            Vec3 ot0 = topCenter + new Vec3(Math.Cos(a0) * outerRadius, 0, Math.Sin(a0) * outerRadius);
            Vec3 ot1 = topCenter + new Vec3(Math.Cos(a1) * outerRadius, 0, Math.Sin(a1) * outerRadius);
            Vec3 ib0 = baseCenter + new Vec3(Math.Cos(a0) * innerRadius, 0, Math.Sin(a0) * innerRadius);
            Vec3 ib1 = baseCenter + new Vec3(Math.Cos(a1) * innerRadius, 0, Math.Sin(a1) * innerRadius);
            Vec3 it0 = topCenter + new Vec3(Math.Cos(a0) * innerRadius, 0, Math.Sin(a0) * innerRadius);
            Vec3 it1 = topCenter + new Vec3(Math.Cos(a1) * innerRadius, 0, Math.Sin(a1) * innerRadius);
            AddQuad(scene, ob0, ob1, ot1, ot0, material);     // outer wall
            AddQuad(scene, ib1, ib0, it0, it1, material);     // inner wall
            AddQuad(scene, ot0, ot1, it1, it0, material);     // top ring
            AddQuad(scene, ob1, ob0, ib0, ib1, material);     // bottom ring
        }
    }

    /// <summary>Adds or creates disk for this subsystem.</summary>
    private static void AddDisk(Scene scene, Vec3 center, double radius, int sides, bool normalUp, Material material)
    {
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 p0 = center + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 p1 = center + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            if (normalUp)
                scene.AddTriangle(center, p0, p1, material);
            else
                scene.AddTriangle(center, p1, p0, material);
        }
    }

    /// <summary>Adds or creates quad for this subsystem.</summary>
    private static void AddQuad(Scene scene, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Material material)
    {
        scene.AddTriangle(a, b, c, material);
        scene.AddTriangle(a, c, d, material);
    }
}
