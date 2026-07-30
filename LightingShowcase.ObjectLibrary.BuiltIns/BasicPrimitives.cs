using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

public sealed class PlanePrimitive : PrimitiveBase
{
    public override string Kind => "plane";
    public override string DisplayName => "Plane";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Plane", true, "X scale updates width; Y scale updates thickness; Z scale updates depth", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.90), ("originZ", 3.60), ("width", 1.70), ("depth", 1.00), ("thickness", 0.02));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds) => FlatParameters(bounds);
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => Box(addTriangle, Origin(p, 0, -0.90, 3.60), Size(p, "width", 1.70), Size(p, "thickness", 0.02), Size(p, "depth", 1.00), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch { 'X' => Multiply(p, "width", factor), 'Y' => Multiply(p, "thickness", factor), _ => Multiply(p, "depth", factor) };
}

public sealed class CubePrimitive : PrimitiveBase
{
    public override string Kind => "cube";
    public override string DisplayName => "Cube";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Cuboid", true, "X/Y/Z scale updates width/height/depth", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.50), ("originZ", 3.50), ("width", 0.90), ("height", 0.90), ("depth", 0.90));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return BoxParameters(bounds, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => Box(addTriangle, Origin(p, 0, -0.50, 3.50), Size(p, "width", 0.90), Size(p, "height", 0.90), Size(p, "depth", 0.90), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) switch { 'X' => Multiply(p, "width", factor), 'Y' => Multiply(p, "height", factor), _ => Multiply(p, "depth", factor) };
}

public sealed class SpherePrimitive : PrimitiveBase
{
    public override string Kind => "sphere";
    public override string DisplayName => "Sphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Sphere", true, "uniform gizmo scale updates radius", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("radius", 0.52), ("longitudeSegments", 32), ("latitudeSegments", 16));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5), ("longitudeSegments", 32), ("latitudeSegments", 16));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddSphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 0.52), ReadInt(p, "longitudeSegments", 32, 3, 256), ReadInt(p, "latitudeSegments", 16, 2, 128), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Y)), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

public sealed class LowPolySpherePrimitive : PrimitiveBase
{
    public override string Kind => "lowPolySphere";
    public override string DisplayName => "Low-poly Sphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Low-poly Sphere", true, "uniform gizmo scale updates radius", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.52), ("originZ", 3.55), ("radius", 0.52), ("longitudeSegments", 12), ("latitudeSegments", 8));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("radius", Math.Max(Math.Max(size.X, size.Y), size.Z) * 0.5), ("longitudeSegments", 12), ("latitudeSegments", 8));
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddSphere(addTriangle, Origin(p, 0, -0.52, 3.55), Size(p, "radius", 0.52), ReadInt(p, "longitudeSegments", 12, 3, 256), ReadInt(p, "latitudeSegments", 8, 2, 128), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => Multiply(p, "radius", factor);
    public override bool ApplyPendingTransform(IDictionary<string, double> p, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(p, position);
        double uniform = Math.Max(Math.Max(SanitizeScale(scale.X), SanitizeScale(scale.Y)), SanitizeScale(scale.Z));
        changed |= Multiply(p, "radius", uniform);
        return changed;
    }
}

public sealed class HemispherePrimitive : PrimitiveBase
{
    public override string Kind => "hemisphere";
    public override string DisplayName => "Hemisphere";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Hemisphere", true, "X/Z scale updates radius; Y scale updates height", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.61), ("originZ", 3.55), ("radius", 0.62), ("height", 0.62), ("longitudeSegments", 32), ("latitudeSegments", 8));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds);
        p["longitudeSegments"] = 32;
        p["latitudeSegments"] = 8;
        return p;
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddHemisphere(addTriangle, Origin(p, 0, -0.61, 3.55), Size(p, "radius", 0.62), Size(p, "height", 0.62), ReadInt(p, "longitudeSegments", 32, 3, 256), ReadInt(p, "latitudeSegments", 8, 1, 128), upper: true, material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : Multiply(p, "radius", factor);
}

public sealed class CylinderPrimitive : PrimitiveBase
{
    public override string Kind => "cylinder";
    public override string DisplayName => "Cylinder";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Cylinder", true, "X/Z scale updates radius; Y scale updates height", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.575), ("originZ", 3.55), ("radius", 0.45), ("height", 1.05), ("sides", 32));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds);
        p["sides"] = 32;
        return p;
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddCylinder(addTriangle, Origin(p, 0, -0.575, 3.55), Size(p, "radius", 0.45), Size(p, "height", 1.05), ReadInt(p, "sides", 32, 3, 256), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : Multiply(p, "radius", factor);
}

public sealed class ConePrimitive : PrimitiveBase
{
    public override string Kind => "cone";
    public override string DisplayName => "Cone";
    public override PrimitiveGizmoEditMetadata GizmoMetadata { get; } = new("Cone", true, "X/Z scale updates radius; Y scale updates height", "stored as object rotation");
    public override Dictionary<string, double> CreateDefaultParameters() => Parameters(("originX", 0.0), ("originY", -0.525), ("originZ", 3.55), ("radius", 0.55), ("height", 1.15), ("sides", 32));
    public override Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Dictionary<string, double> p = RadialHeightParameters(bounds);
        p["sides"] = 32;
        return p;
    }
    public override void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> p, Material material, AddTriangleCallback addTriangle) => AddCone(addTriangle, Origin(p, 0, -0.525, 3.55), Size(p, "radius", 0.55), Size(p, "height", 1.15), ReadInt(p, "sides", 32, 3, 256), material);
    public override bool ApplyScaleDelta(IDictionary<string, double> p, char axis, double factor) => char.ToUpperInvariant(axis) == 'Y' ? Multiply(p, "height", factor) : Multiply(p, "radius", factor);
}
