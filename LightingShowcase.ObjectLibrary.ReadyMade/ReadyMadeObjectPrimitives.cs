// -----------------------------------------------------------------------------
// File: ReadyMadeObjectPrimitives.cs
// Purpose: One self-discoverable class per ready-made object definition.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>A declarative box component used by ready-made object definitions.</summary>
public readonly record struct BoxPart(Vec3 Min, Vec3 Max, Material Material);

/// <summary>
/// Base class for ready-made object definitions. The object library DLL owns only
/// authored object components, parameters, and gizmo rules; the core scene layer
/// owns group creation and triangle insertion.
/// </summary>
public abstract class ReadyMadeObjectDefinitionBase : ISceneObjectDefinition
{
    private readonly record struct EmittedTriangle(Vec3 A, Vec3 B, Vec3 C, Vec2 UvA, Vec2 UvB, Vec2 UvC, Material Material);

    public abstract string Kind { get; }
    public abstract string DisplayName { get; }

    public virtual PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new(
        "Ready-made object",
        true,
        "X/Y/Z scale updates width/height/depth while preserving the authored multi-part geometry",
        "stored as object rotation");

    public Dictionary<string, double> CreateDefaultParameters()
    {
        List<EmittedTriangle> source = CaptureSource(new SceneMaterials());
        if (source.Count == 0)
            return CreateParameters(new Vec3(0, -0.5, 3.55), 1.0, 1.0, 1.0);

        (Vec3 min, Vec3 max) = Bounds(source);
        Vec3 size = max - min;
        return CreateParameters((min + max) * 0.5, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }

    public Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return CreateParameters((bounds.Min + bounds.Max) * 0.5, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }

    public void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> parameters, Material material, AddTriangleCallback addTriangle)
    {
        if (materials == null) throw new ArgumentNullException(nameof(materials));
        if (parameters == null) throw new ArgumentNullException(nameof(parameters));
        if (addTriangle == null) throw new ArgumentNullException(nameof(addTriangle));

        List<EmittedTriangle> source = CaptureSource(materials);
        if (source.Count == 0)
            return;

        (Vec3 sourceMin, Vec3 sourceMax) = Bounds(source);
        Vec3 sourceSize = sourceMax - sourceMin;
        Vec3 origin = new(Read(parameters, "originX", (sourceMin.X + sourceMax.X) * 0.5), Read(parameters, "originY", (sourceMin.Y + sourceMax.Y) * 0.5), Read(parameters, "originZ", (sourceMin.Z + sourceMax.Z) * 0.5));
        double width = Size(parameters, "width", Math.Max(1e-6, sourceSize.X));
        double height = Size(parameters, YSizeParameter, Math.Max(1e-6, sourceSize.Y));
        double depth = Size(parameters, "depth", Math.Max(1e-6, sourceSize.Z));
        Vec3 targetMin = origin - new Vec3(width * 0.5, height * 0.5, depth * 0.5);
        Vec3 targetSize = new(width, height, depth);

        foreach (EmittedTriangle triangle in source)
        {
            addTriangle(
                Remap(triangle.A, sourceMin, sourceSize, targetMin, targetSize),
                Remap(triangle.B, sourceMin, sourceSize, targetMin, targetSize),
                Remap(triangle.C, sourceMin, sourceSize, targetMin, targetSize),
                triangle.UvA,
                triangle.UvB,
                triangle.UvC,
                triangle.Material);
        }
    }

    public virtual bool ApplyMoveDelta(IDictionary<string, double> parameters, Vec3 delta)
    {
        bool changed = false;
        changed |= Add(parameters, "originX", delta.X);
        changed |= Add(parameters, "originY", delta.Y);
        changed |= Add(parameters, "originZ", delta.Z);
        return changed;
    }

    public virtual bool ApplyScaleDelta(IDictionary<string, double> parameters, char axis, double factor)
    {
        factor = SanitizeScale(factor);
        if (Math.Abs(factor - 1.0) <= 1e-12)
            return false;

        char normalized = char.ToUpperInvariant(axis);
        return normalized switch
        {
            'X' => Multiply(parameters, "width", factor),
            'Y' => Multiply(parameters, YSizeParameter, factor),
            _ => Multiply(parameters, "depth", factor)
        };
    }

    public virtual bool ApplyPendingTransform(IDictionary<string, double> parameters, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(parameters, position);
        changed |= Multiply(parameters, "width", SanitizeScale(scale.X));
        changed |= Multiply(parameters, YSizeParameter, SanitizeScale(scale.Y));
        changed |= Multiply(parameters, "depth", SanitizeScale(scale.Z));
        return changed;
    }

    protected abstract IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials);

    protected virtual string YSizeParameter => "height";

    protected static BoxPart Box(Vec3 min, Vec3 max, Material material) => new(min, max, material);

    private Dictionary<string, double> CreateParameters(Vec3 origin, double width, double height, double depth) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["originX"] = origin.X,
            ["originY"] = origin.Y,
            ["originZ"] = origin.Z,
            ["width"] = width,
            [YSizeParameter] = height,
            ["depth"] = depth
        };

    private List<EmittedTriangle> CaptureSource(SceneMaterials materials)
    {
        List<EmittedTriangle> triangles = new();
        foreach (BoxPart part in CreateParts(materials))
            EmitBox(part, (a, b, c, uvA, uvB, uvC, mat) => triangles.Add(new EmittedTriangle(a, b, c, uvA, uvB, uvC, mat)));
        return triangles;
    }

    private static (Vec3 Min, Vec3 Max) Bounds(IEnumerable<EmittedTriangle> triangles)
    {
        bool any = false;
        double minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;
        foreach (EmittedTriangle triangle in triangles)
        {
            Accumulate(triangle.A);
            Accumulate(triangle.B);
            Accumulate(triangle.C);
        }
        return (new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));

        void Accumulate(Vec3 p)
        {
            if (!any)
            {
                minX = maxX = p.X;
                minY = maxY = p.Y;
                minZ = maxZ = p.Z;
                any = true;
                return;
            }

            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            minZ = Math.Min(minZ, p.Z);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
            maxZ = Math.Max(maxZ, p.Z);
        }
    }

    private static Vec3 Remap(Vec3 point, Vec3 sourceMin, Vec3 sourceSize, Vec3 targetMin, Vec3 targetSize)
    {
        double tx = sourceSize.X <= 1e-9 ? 0.5 : (point.X - sourceMin.X) / sourceSize.X;
        double ty = sourceSize.Y <= 1e-9 ? 0.5 : (point.Y - sourceMin.Y) / sourceSize.Y;
        double tz = sourceSize.Z <= 1e-9 ? 0.5 : (point.Z - sourceMin.Z) / sourceSize.Z;
        return new Vec3(targetMin.X + tx * targetSize.X, targetMin.Y + ty * targetSize.Y, targetMin.Z + tz * targetSize.Z);
    }

    protected static bool Add(IDictionary<string, double> parameters, string key, double delta)
    {
        if (Math.Abs(delta) <= 1e-12)
            return false;
        parameters[key] = Read(parameters, key, 0.0) + delta;
        return true;
    }

    protected static bool Multiply(IDictionary<string, double> parameters, string key, double factor)
    {
        factor = SanitizeScale(factor);
        if (Math.Abs(factor - 1.0) <= 1e-12)
            return false;
        parameters[key] = Math.Max(1e-6, Read(parameters, key, 1.0) * factor);
        return true;
    }

    protected static double Read(IReadOnlyDictionary<string, double> parameters, string key, double fallback) =>
        parameters.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;

    protected static double Read(IDictionary<string, double> parameters, string key, double fallback) =>
        parameters.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;

    protected static double Size(IReadOnlyDictionary<string, double> parameters, string key, double fallback) =>
        Math.Max(1e-6, Read(parameters, key, fallback));

    protected static double SanitizeScale(double value) =>
        double.IsFinite(value) && Math.Abs(value) > 1e-9 ? Math.Abs(value) : 1.0;

    private static void EmitBox(BoxPart part, AddTriangleCallback addTriangle)
    {
        double x0 = part.Min.X, y0 = part.Min.Y, z0 = part.Min.Z, x1 = part.Max.X, y1 = part.Max.Y, z1 = part.Max.Z;
        Vec3 p000 = new(x0, y0, z0), p001 = new(x0, y0, z1), p010 = new(x0, y1, z0), p011 = new(x0, y1, z1);
        Vec3 p100 = new(x1, y0, z0), p101 = new(x1, y0, z1), p110 = new(x1, y1, z0), p111 = new(x1, y1, z1);
        AddQuad(addTriangle, p001, p101, p111, p011, part.Material);
        AddQuad(addTriangle, p100, p000, p010, p110, part.Material);
        AddQuad(addTriangle, p000, p001, p011, p010, part.Material);
        AddQuad(addTriangle, p101, p100, p110, p111, part.Material);
        AddQuad(addTriangle, p010, p011, p111, p110, part.Material);
        AddQuad(addTriangle, p000, p100, p101, p001, part.Material);
    }

    private static void AddQuad(AddTriangleCallback addTriangle, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Material material)
    {
        addTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), material);
        addTriangle(a, c, d, new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), material);
    }
}

public sealed class DiningTableObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "diningTable";
    public override string DisplayName => "Dining Table";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-1.20, -0.45, 3.00), new Vec3(1.20, -0.28, 4.10), materials.Wood),
        Box(new Vec3(-1.08, -1.30, 3.12), new Vec3(-0.92, -0.45, 3.28), materials.DarkWood),
        Box(new Vec3(0.92, -1.30, 3.12), new Vec3(1.08, -0.45, 3.28), materials.DarkWood),
        Box(new Vec3(-1.08, -1.30, 3.82), new Vec3(-0.92, -0.45, 3.98), materials.DarkWood),
        Box(new Vec3(0.92, -1.30, 3.82), new Vec3(1.08, -0.45, 3.98), materials.DarkWood)
    };
}

public sealed class CoffeeTableObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "coffeeTable";
    public override string DisplayName => "Coffee Table";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.75, -0.68, 3.15), new Vec3(0.75, -0.50, 3.95), materials.Wood),
        Box(new Vec3(-0.66, -1.22, 3.22), new Vec3(-0.52, -0.68, 3.36), materials.DarkWood),
        Box(new Vec3(0.52, -1.22, 3.22), new Vec3(0.66, -0.68, 3.36), materials.DarkWood),
        Box(new Vec3(-0.66, -1.22, 3.74), new Vec3(-0.52, -0.68, 3.88), materials.DarkWood),
        Box(new Vec3(0.52, -1.22, 3.74), new Vec3(0.66, -0.68, 3.88), materials.DarkWood)
    };
}

public sealed class ChairObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "chair";
    public override string DisplayName => "Chair";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.42, -0.78, 3.10), new Vec3(0.42, -0.60, 3.88), materials.Wood),
        Box(new Vec3(-0.42, -0.60, 3.76), new Vec3(0.42, 0.10, 3.92), materials.Wood),
        Box(new Vec3(-0.38, -1.28, 3.16), new Vec3(-0.24, -0.78, 3.30), materials.DarkWood),
        Box(new Vec3(0.24, -1.28, 3.16), new Vec3(0.38, -0.78, 3.30), materials.DarkWood),
        Box(new Vec3(-0.38, -1.28, 3.68), new Vec3(-0.24, -0.78, 3.82), materials.DarkWood),
        Box(new Vec3(0.24, -1.28, 3.68), new Vec3(0.38, -0.78, 3.82), materials.DarkWood)
    };
}

public sealed class SofaObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "sofa";
    public override string DisplayName => "Sofa";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-1.35, -1.18, 3.08), new Vec3(1.35, -0.70, 3.86), materials.Sofa),
        Box(new Vec3(-1.35, -0.70, 3.62), new Vec3(1.35, -0.02, 3.90), materials.Sofa),
        Box(new Vec3(-1.58, -1.18, 3.08), new Vec3(-1.30, -0.42, 3.86), materials.Sofa),
        Box(new Vec3(1.30, -1.18, 3.08), new Vec3(1.58, -0.42, 3.86), materials.Sofa),
        Box(new Vec3(-1.05, -0.66, 2.86), new Vec3(-0.18, -0.30, 3.10), materials.Cushion),
        Box(new Vec3(0.18, -0.66, 2.86), new Vec3(1.05, -0.30, 3.10), materials.Cushion)
    };
}

public sealed class BedObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "bed";
    public override string DisplayName => "Bed";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-1.20, -1.10, 2.80), new Vec3(1.20, -0.70, 4.50), materials.Sofa),
        Box(new Vec3(-1.25, -0.70, 4.32), new Vec3(1.25, 0.08, 4.55), materials.Wood),
        Box(new Vec3(-1.05, -0.62, 2.95), new Vec3(-0.08, -0.34, 3.45), materials.Cushion),
        Box(new Vec3(0.08, -0.62, 2.95), new Vec3(1.05, -0.34, 3.45), materials.Cushion)
    };
}

public sealed class BookshelfObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "bookshelf";
    public override string DisplayName => "Bookshelf";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.95, -1.25, 3.85), new Vec3(0.95, 0.75, 4.05), materials.Wood),
        Box(new Vec3(-0.86, -1.05, 3.72), new Vec3(0.86, -0.99, 4.12), materials.DarkWood),
        Box(new Vec3(-0.86, -0.61, 3.72), new Vec3(0.86, -0.55, 4.12), materials.DarkWood),
        Box(new Vec3(-0.86, -0.17, 3.72), new Vec3(0.86, -0.11, 4.12), materials.DarkWood),
        Box(new Vec3(-0.86, 0.27, 3.72), new Vec3(0.86, 0.33, 4.12), materials.DarkWood),
        Box(new Vec3(-0.82, -1.18, 3.66), new Vec3(-0.55, 0.40, 3.82), materials.BlueWall),
        Box(new Vec3(-0.45, -1.18, 3.66), new Vec3(-0.15, 0.25, 3.82), materials.RedWall),
        Box(new Vec3(0.00, -1.18, 3.66), new Vec3(0.30, 0.52, 3.82), materials.WhiteWall),
        Box(new Vec3(0.42, -1.18, 3.66), new Vec3(0.75, 0.18, 3.82), materials.Cushion)
    };
}

public sealed class StorageCabinetObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "storageCabinet";
    public override string DisplayName => "Storage Cabinet";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.70, -1.20, 3.12), new Vec3(0.70, 0.18, 3.92), materials.Wood),
        Box(new Vec3(-0.66, -0.54, 3.08), new Vec3(0.66, -0.48, 3.96), materials.DarkWood),
        Box(new Vec3(-0.04, -1.16, 3.07), new Vec3(0.04, 0.14, 3.97), materials.DarkWood),
        Box(new Vec3(-0.32, -0.58, 3.03), new Vec3(-0.18, -0.44, 3.08), materials.ScreenFrame),
        Box(new Vec3(0.18, -0.58, 3.03), new Vec3(0.32, -0.44, 3.08), materials.ScreenFrame)
    };
}

public sealed class TvScreenObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "tvScreen";
    public override string DisplayName => "TV / Screen";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.95, -0.45, 3.86), new Vec3(0.95, 0.80, 3.91), materials.ScreenFrame),
        Box(new Vec3(-0.82, -0.35, 3.835), new Vec3(0.82, 0.70, 3.855), materials.Screen)
    };
}

public sealed class FloorLampObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "floorLamp";
    public override string DisplayName => "Floor Lamp";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.22, -1.30, 3.33), new Vec3(0.22, -1.24, 3.77), materials.DarkWood),
        Box(new Vec3(-0.05, -1.24, 3.50), new Vec3(0.05, 0.35, 3.60), materials.DarkWood),
        Box(new Vec3(-0.35, 0.30, 3.25), new Vec3(0.35, 0.75, 3.85), materials.LampGlow)
    };
}

public sealed class PottedPlantObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "pottedPlant";
    public override string DisplayName => "Potted Plant";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.28, -1.22, 3.28), new Vec3(0.28, -0.78, 3.82), materials.Pot),
        Box(new Vec3(-0.44, -0.78, 3.20), new Vec3(0.44, -0.36, 3.90), materials.Plant),
        Box(new Vec3(-0.20, -0.36, 3.36), new Vec3(0.20, 0.18, 3.74), materials.Plant)
    };
}

public sealed class RugObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "rug";
    public override string DisplayName => "Rug";
    protected override string YSizeParameter => "thickness";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Rug", true, "X/Z scale updates width/depth; Y scale updates thickness", "stored as object rotation");
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-1.45, -1.36, 2.90), new Vec3(1.45, -1.31, 4.30), materials.Rug)
    };
}

public sealed class WallPanelObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "wallPanel";
    public override string DisplayName => "Wall Panel";
    protected override string YSizeParameter => "thickness";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Wall Panel", true, "X/Z scale updates width/depth; Y scale updates thickness", "stored as object rotation");
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-1.10, -0.95, 4.04), new Vec3(1.10, 0.65, 4.10), materials.BlueWall)
    };
}

public sealed class PedestalObject : ReadyMadeObjectDefinitionBase
{
    public override string Kind => "pedestal";
    public override string DisplayName => "Pedestal";
    protected override IReadOnlyList<BoxPart> CreateParts(SceneMaterials materials) => new[]
    {
        Box(new Vec3(-0.42, -1.35, 3.20), new Vec3(0.42, -0.10, 4.02), materials.Ceiling),
        Box(new Vec3(-0.55, -0.10, 3.08), new Vec3(0.55, 0.08, 4.14), materials.WhiteWall)
    };
}
