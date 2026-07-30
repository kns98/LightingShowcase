using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

public sealed class TorusPrimitive : PrimitiveBase
{
    public override string Kind => "torus";
    public override string DisplayName => "Torus";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Torus", true, "X/Z scale updates major radius; Y scale updates minor radius", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.58), ("originZ", 3.55), ("majorRadius", 0.48), ("minorRadius", 0.16), ("majorSegments", 40), ("tubeSegments", 16));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        double outer = Math.Max(Math.Max(1e-6, size.X), Math.Max(1e-6, size.Z)) * 0.5;
        double minor = Math.Max(1e-6, size.Y) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("majorRadius", Math.Max(1e-6, outer - minor)), ("minorRadius", minor), ("majorSegments", 40), ("tubeSegments", 16));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddTorus(addTriangle, Origin(p, 0, -0.58, 3.55), Size(p, "majorRadius", 0.48), Size(p, "minorRadius", 0.16), ReadInt(p, "majorSegments", 40, 3, 256), ReadInt(p, "tubeSegments", 16, 3, 128), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "minorRadius", factor) : Multiply(p, "majorRadius", factor);
}

public sealed class TubePrimitive : PrimitiveBase
{
    public override string Kind => "tube";
    public override string DisplayName => "Tube";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Tube", true, "X/Z scale updates inner/outer radius; Y scale updates height", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.525), ("originZ", 3.55), ("outerRadius", 0.55), ("innerRadius", 0.32), ("height", 1.05), ("sides", 36));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds, "outerRadius", "height");
        p["innerRadius"] = Math.Max(1e-6, p["outerRadius"] * 0.58);
        p["sides"] = 36;
        return p;
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddTube(addTriangle, Origin(p, 0, -0.525, 3.55), Size(p, "outerRadius", 0.55), Size(p, "innerRadius", 0.32), Size(p, "height", 1.05), ReadInt(p, "sides", 36, 3, 256), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : MultiplyAny(p, factor, "outerRadius", "innerRadius");
}

public sealed class CapsulePrimitive : PrimitiveBase
{
    public override string Kind => "capsule";
    public override string DisplayName => "Capsule";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Capsule", true, "X/Z scale updates radius; Y scale updates total height", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.615), ("originZ", 3.55), ("radius", 0.34), ("totalHeight", 1.35), ("longitudeSegments", 28), ("latitudeSegments", 8));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds, "radius", "totalHeight");
        p["longitudeSegments"] = 28;
        p["latitudeSegments"] = 8;
        return p;
    }

    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle)
    {
        Vec3 origin = Origin(p, 0, -0.615, 3.55);
        double radius = Size(p, "radius", 0.34);
        double totalHeight = Math.Max(radius * 2.0, Size(p, "totalHeight", 1.35));
        double cylinderHeight = Math.Max(1e-6, totalHeight - radius * 2.0);
        int sides = ReadInt(p, "longitudeSegments", 28, 3, 256);
        int lat = ReadInt(p, "latitudeSegments", 8, 1, 128);
        Vec3 lowerCenter = origin - new Vec3(0, cylinderHeight * 0.5, 0);
        Vec3 upperCenter = origin + new Vec3(0, cylinderHeight * 0.5, 0);
        AddCylinderSides(addTriangle, lowerCenter, radius, cylinderHeight, sides, material);
        AddHemisphere(addTriangle, upperCenter + new Vec3(0, radius * 0.5, 0), radius, radius, sides, lat, upper: true, material);
        AddHemisphere(addTriangle, lowerCenter + new Vec3(0, radius * 0.5, 0), radius, radius, sides, lat, upper: false, material);
    }

    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "totalHeight", factor) : Multiply(p, "radius", factor);
}

public sealed class PyramidPrimitive : PrimitiveBase
{
    public override string Kind => "pyramid";
    public override string DisplayName => "Pyramid";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Pyramid", true, "X/Y/Z scale updates width/height/depth", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.50), ("originZ", 3.55), ("width", 1.10), ("height", 1.10), ("depth", 1.00));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return BoxParameters(bounds, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle)
    {
        Vec3 o = Origin(p, 0, -0.50, 3.55);
        double w = Size(p, "width", 1.10), h = Size(p, "height", 1.10), d = Size(p, "depth", 1.00);
        double x0 = o.X - w * 0.5, x1 = o.X + w * 0.5, y0 = o.Y - h * 0.5, z0 = o.Z - d * 0.5, z1 = o.Z + d * 0.5;
        Vec3 a = new(x0, y0, z0), b = new(x1, y0, z0), c = new(x1, y0, z1), dd = new(x0, y0, z1), apex = new(o.X, o.Y + h * 0.5, o.Z);
        AddQuad(addTriangle, a, b, c, dd, material);
        EmitTriangle(addTriangle, a, apex, b, material); EmitTriangle(addTriangle, b, apex, c, material); EmitTriangle(addTriangle, c, apex, dd, material); EmitTriangle(addTriangle, dd, apex, a, material);
    }
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch { 'X' => Multiply(p, "width", factor), 'Y' => Multiply(p, "height", factor), _ => Multiply(p, "depth", factor) };
}

public sealed class TriangularPrismPrimitive : PrimitiveBase
{
    public override string Kind => "triangularPrism";
    public override string DisplayName => "Triangular Prism";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Triangular Prism", true, "X/Y/Z scale updates width/height/depth", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.60), ("originZ", 3.60), ("width", 1.10), ("height", 0.90), ("depth", 0.90));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return BoxParameters(bounds, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle)
    {
        Vec3 o = Origin(p, 0, -0.60, 3.60);
        double w = Size(p, "width", 1.10), h = Size(p, "height", 0.90), d = Size(p, "depth", 0.90);
        double x0 = o.X - w * 0.5, x1 = o.X + w * 0.5, y0 = o.Y - h * 0.5, y1 = o.Y + h * 0.5, z0 = o.Z - d * 0.5, z1 = o.Z + d * 0.5;
        Vec3 a0 = new(x0, y0, z0), b0 = new(x1, y0, z0), c0 = new(o.X, y1, z0);
        Vec3 a1 = new(x0, y0, z1), b1 = new(x1, y0, z1), c1 = new(o.X, y1, z1);
        EmitTriangle(addTriangle, a0, c0, b0, material); EmitTriangle(addTriangle, a1, b1, c1, material);
        AddQuad(addTriangle, a0, a1, c1, c0, material); AddQuad(addTriangle, b1, b0, c0, c1, material); AddQuad(addTriangle, a0, b0, b1, a1, material);
    }
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch { 'X' => Multiply(p, "width", factor), 'Y' => Multiply(p, "height", factor), _ => Multiply(p, "depth", factor) };
}

public sealed class WedgePrimitive : PrimitiveBase
{
    public override string Kind => "wedge";
    public override string DisplayName => "Wedge";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Wedge", true, "X/Y/Z scale updates width/height/depth", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.575), ("originZ", 3.60), ("width", 1.20), ("height", 0.95), ("depth", 1.00));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return BoxParameters(bounds, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle)
    {
        Vec3 o = Origin(p, 0, -0.575, 3.60);
        double w = Size(p, "width", 1.20), h = Size(p, "height", 0.95), d = Size(p, "depth", 1.00);
        double x0 = o.X - w * 0.5, x1 = o.X + w * 0.5, y0 = o.Y - h * 0.5, y1 = o.Y + h * 0.5, z0 = o.Z - d * 0.5, z1 = o.Z + d * 0.5;
        Vec3 a = new(x0, y0, z0), b = new(x1, y0, z0), c = new(x0, y0, z1), d0 = new(x1, y0, z1), e = new(x0, y1, z1), f = new(x1, y1, z1);
        AddQuad(addTriangle, a, b, d0, c, material);
        AddQuad(addTriangle, c, d0, f, e, material);
        EmitTriangle(addTriangle, a, e, c, material); EmitTriangle(addTriangle, b, d0, f, material);
        AddQuad(addTriangle, a, e, f, b, material);
    }
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch { 'X' => Multiply(p, "width", factor), 'Y' => Multiply(p, "height", factor), _ => Multiply(p, "depth", factor) };
}
