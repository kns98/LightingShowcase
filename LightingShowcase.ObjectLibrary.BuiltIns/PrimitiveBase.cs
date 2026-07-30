using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.ObjectLibrary.BuiltIns;

public abstract class PrimitiveBase : IScenePrimitive
{
    public abstract string Kind { get; }
    public abstract string DisplayName { get; }
    public abstract PrimitiveGizmoEditMetadata GizmoMetadata { get; }
    public abstract Dictionary<string, double> CreateDefaultParameters();

    public virtual Dictionary<string, double> CreateParametersFromBounds(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return BoxParameters(bounds, Math.Max(1e-6, size.X), Math.Max(1e-6, size.Y), Math.Max(1e-6, size.Z));
    }

    public abstract void Build(SceneMaterials materials, IReadOnlyDictionary<string, double> parameters, Material material, AddTriangleCallback addTriangle);
    public abstract bool ApplyScaleDelta(IDictionary<string, double> parameters, char axis, double factor);

    public virtual bool ApplyMoveDelta(IDictionary<string, double> parameters, Vec3 delta)
    {
        bool changed = false;
        changed |= Add(parameters, "originX", delta.X);
        changed |= Add(parameters, "originY", delta.Y);
        changed |= Add(parameters, "originZ", delta.Z);
        return changed;
    }

    public virtual bool ApplyPendingTransform(IDictionary<string, double> parameters, Vec3 position, Vec3 scale)
    {
        bool changed = ApplyMoveDelta(parameters, position);
        changed |= ApplyScaleDelta(parameters, 'X', SanitizeScale(scale.X));
        changed |= ApplyScaleDelta(parameters, 'Y', SanitizeScale(scale.Y));
        changed |= ApplyScaleDelta(parameters, 'Z', SanitizeScale(scale.Z));
        return changed;
    }

    protected static Dictionary<string, double> Parameters(params (string Key, double Value)[] values)
    {
        Dictionary<string, double> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, double value) in values)
            result[key] = value;
        return result;
    }

    protected static Dictionary<string, double> BoxParameters(Aabb bounds, double width, double height, double depth)
    {
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("width", width), ("height", height), ("depth", depth));
    }

    protected static Dictionary<string, double> FlatParameters(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), ("width", Math.Max(1e-6, size.X)), ("depth", Math.Max(1e-6, size.Z)), ("thickness", Math.Max(1e-6, size.Y)));
    }

    protected static Dictionary<string, double> RadialHeightParameters(Aabb bounds, string radialKey = "radius", string heightKey = "height")
    {
        Vec3 size = bounds.Max - bounds.Min;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        double radius = Math.Max(Math.Max(1e-6, size.X), Math.Max(1e-6, size.Z)) * 0.5;
        return Parameters(("originX", center.X), ("originY", center.Y), ("originZ", center.Z), (radialKey, radius), (heightKey, Math.Max(1e-6, size.Y)));
    }

    protected static Vec3 Origin(IReadOnlyDictionary<string, double> p, double x = 0.0, double y = 0.0, double z = 3.55) =>
        new(Read(p, "originX", x), Read(p, "originY", y), Read(p, "originZ", z));

    protected static double Read(IReadOnlyDictionary<string, double> parameters, string key, double fallback) =>
        parameters.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;

    protected static double Read(IDictionary<string, double> parameters, string key, double fallback) =>
        parameters.TryGetValue(key, out double value) && double.IsFinite(value) ? value : fallback;

    protected static int ReadInt(IReadOnlyDictionary<string, double> parameters, string key, int fallback, int min, int max) =>
        Math.Clamp((int)Math.Round(Read(parameters, key, fallback)), min, max);

    protected static double Size(IReadOnlyDictionary<string, double> p, string key, double fallback) => Math.Max(1e-6, Read(p, key, fallback));

    protected static bool Add(IDictionary<string, double> parameters, string key, double delta)
    {
        if (!double.IsFinite(delta) || Math.Abs(delta) <= 1e-12) return false;
        parameters[key] = Read(parameters, key, 0.0) + delta;
        return true;
    }

    protected static bool Multiply(IDictionary<string, double> parameters, string key, double factor)
    {
        factor = SanitizeScale(factor);
        if (!parameters.ContainsKey(key) || Math.Abs(factor - 1.0) <= 1e-12) return false;
        parameters[key] = Math.Max(1e-6, Read(parameters, key, 1.0) * factor);
        return true;
    }

    protected static bool MultiplyAny(IDictionary<string, double> p, double factor, params string[] keys)
    {
        bool changed = false;
        foreach (string key in keys)
            changed |= Multiply(p, key, factor);
        return changed;
    }

    protected static double SanitizeScale(double value) => double.IsFinite(value) && value > 1e-6 ? value : 1.0;

    protected static void Box(AddTriangleCallback addTriangle, Vec3 origin, double width, double height, double depth, Material material)
    {
        Vec3 min = origin - new Vec3(width * 0.5, height * 0.5, depth * 0.5);
        Vec3 max = origin + new Vec3(width * 0.5, height * 0.5, depth * 0.5);

        Vec3 v000 = new(min.X, min.Y, min.Z);
        Vec3 v001 = new(min.X, min.Y, max.Z);
        Vec3 v010 = new(min.X, max.Y, min.Z);
        Vec3 v011 = new(min.X, max.Y, max.Z);
        Vec3 v100 = new(max.X, min.Y, min.Z);
        Vec3 v101 = new(max.X, min.Y, max.Z);
        Vec3 v110 = new(max.X, max.Y, min.Z);
        Vec3 v111 = new(max.X, max.Y, max.Z);

        AddQuad(addTriangle, v000, v100, v110, v010, material);
        AddQuad(addTriangle, v101, v001, v011, v111, material);
        AddQuad(addTriangle, v001, v000, v010, v011, material);
        AddQuad(addTriangle, v100, v101, v111, v110, material);
        AddQuad(addTriangle, v010, v110, v111, v011, material);
        AddQuad(addTriangle, v001, v101, v100, v000, material);
    }

    protected static void EmitTriangle(AddTriangleCallback addTriangle, Vec3 a, Vec3 b, Vec3 c, Material material) =>
        addTriangle(a, b, c, Vec2.Zero, Vec2.Zero, Vec2.Zero, material);

    protected static void AddQuad(AddTriangleCallback addTriangle, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Material material)
    {
        addTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), material);
        addTriangle(a, c, d, new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1), material);
    }

    protected static void AddSphere(AddTriangleCallback addTriangle, Vec3 center, double radius, int longitudeSegments, int latitudeSegments, Material material)
    {
        radius = Math.Max(1e-6, radius);
        longitudeSegments = Math.Clamp(longitudeSegments, 3, 256);
        latitudeSegments = Math.Clamp(latitudeSegments, 2, 128);
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
                if (lat == 0) EmitTriangle(addTriangle, p00, p11, p10, material);
                else if (lat == latitudeSegments - 1) EmitTriangle(addTriangle, p00, p01, p10, material);
                else AddQuad(addTriangle, p00, p01, p11, p10, material);
            }
        }
    }

    protected static Vec3 SpherePoint(Vec3 center, double radius, double theta, double phi) => new(
        center.X + radius * Math.Sin(theta) * Math.Cos(phi),
        center.Y + radius * Math.Cos(theta),
        center.Z + radius * Math.Sin(theta) * Math.Sin(phi));

    protected static void AddHemisphere(AddTriangleCallback addTriangle, Vec3 origin, double radius, double height, int longitudeSegments, int latitudeSegments, bool upper, Material material)
    {
        radius = Math.Max(1e-6, radius);
        height = Math.Max(1e-6, height);
        Vec3 baseCenter = origin - new Vec3(0, height * 0.5, 0);
        for (int lat = 0; lat < latitudeSegments; lat++)
        {
            double t0 = (Math.PI * 0.5) * lat / latitudeSegments;
            double t1 = (Math.PI * 0.5) * (lat + 1) / latitudeSegments;
            if (!upper) { t0 = Math.PI - t0; t1 = Math.PI - t1; }
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                double p0 = 2.0 * Math.PI * lon / longitudeSegments;
                double p1 = 2.0 * Math.PI * (lon + 1) / longitudeSegments;
                Vec3 a = HemispherePoint(baseCenter, radius, height, t0, p0, upper);
                Vec3 b = HemispherePoint(baseCenter, radius, height, t0, p1, upper);
                Vec3 c = HemispherePoint(baseCenter, radius, height, t1, p1, upper);
                Vec3 d = HemispherePoint(baseCenter, radius, height, t1, p0, upper);
                AddQuad(addTriangle, a, b, c, d, material);
            }
        }
        AddDisk(addTriangle, baseCenter, radius, longitudeSegments, normalUp: !upper, material);
    }

    private static Vec3 HemispherePoint(Vec3 baseCenter, double radius, double height, double theta, double phi, bool upper)
    {
        double radial = radius * Math.Sin(theta);
        double y = upper ? height * Math.Cos(theta) : -height * Math.Cos(theta);
        return new Vec3(baseCenter.X + radial * Math.Cos(phi), baseCenter.Y + y, baseCenter.Z + radial * Math.Sin(phi));
    }

    protected static void AddCylinder(AddTriangleCallback addTriangle, Vec3 origin, double radius, double height, int sides, Material material)
    {
        Vec3 baseCenter = origin - new Vec3(0, height * 0.5, 0);
        AddCylinderSides(addTriangle, baseCenter, radius, height, sides, material);
        AddDisk(addTriangle, baseCenter, radius, sides, normalUp: false, material);
        AddDisk(addTriangle, baseCenter + new Vec3(0, height, 0), radius, sides, normalUp: true, material);
    }

    protected static void AddCylinderSides(AddTriangleCallback addTriangle, Vec3 baseCenter, double radius, double height, int sides, Material material)
    {
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 b0 = baseCenter + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 b1 = baseCenter + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            AddQuad(addTriangle, b0, b1, b1 + new Vec3(0, height, 0), b0 + new Vec3(0, height, 0), material);
        }
    }

    protected static void AddCone(AddTriangleCallback addTriangle, Vec3 origin, double radius, double height, int sides, Material material)
    {
        Vec3 baseCenter = origin - new Vec3(0, height * 0.5, 0);
        Vec3 apex = baseCenter + new Vec3(0, height, 0);
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 b0 = baseCenter + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 b1 = baseCenter + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            EmitTriangle(addTriangle, b0, apex, b1, material);
        }
        AddDisk(addTriangle, baseCenter, radius, sides, normalUp: false, material);
    }

    protected static void AddTorus(AddTriangleCallback addTriangle, Vec3 center, double majorRadius, double tubeRadius, int majorSegments, int tubeSegments, Material material)
    {
        majorRadius = Math.Max(1e-6, majorRadius);
        tubeRadius = Math.Max(1e-6, tubeRadius);
        for (int i = 0; i < majorSegments; i++)
        {
            double u0 = 2.0 * Math.PI * i / majorSegments;
            double u1 = 2.0 * Math.PI * (i + 1) / majorSegments;
            for (int j = 0; j < tubeSegments; j++)
            {
                double v0 = 2.0 * Math.PI * j / tubeSegments;
                double v1 = 2.0 * Math.PI * (j + 1) / tubeSegments;
                AddQuad(addTriangle, TorusPoint(center, majorRadius, tubeRadius, u0, v0), TorusPoint(center, majorRadius, tubeRadius, u1, v0), TorusPoint(center, majorRadius, tubeRadius, u1, v1), TorusPoint(center, majorRadius, tubeRadius, u0, v1), material);
            }
        }
    }

    private static Vec3 TorusPoint(Vec3 c, double major, double minor, double u, double v)
    {
        double ring = major + minor * Math.Cos(v);
        return new Vec3(c.X + ring * Math.Cos(u), c.Y + minor * Math.Sin(v), c.Z + ring * Math.Sin(u));
    }

    protected static void AddTube(AddTriangleCallback addTriangle, Vec3 origin, double outerRadius, double innerRadius, double height, int sides, Material material)
    {
        outerRadius = Math.Max(1e-6, outerRadius);
        innerRadius = Math.Clamp(innerRadius, 1e-6, outerRadius * 0.95);
        Vec3 baseCenter = origin - new Vec3(0, height * 0.5, 0);
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 ob0 = baseCenter + new Vec3(Math.Cos(a0) * outerRadius, 0, Math.Sin(a0) * outerRadius);
            Vec3 ob1 = baseCenter + new Vec3(Math.Cos(a1) * outerRadius, 0, Math.Sin(a1) * outerRadius);
            Vec3 ib0 = baseCenter + new Vec3(Math.Cos(a0) * innerRadius, 0, Math.Sin(a0) * innerRadius);
            Vec3 ib1 = baseCenter + new Vec3(Math.Cos(a1) * innerRadius, 0, Math.Sin(a1) * innerRadius);
            Vec3 ot0 = ob0 + new Vec3(0, height, 0), ot1 = ob1 + new Vec3(0, height, 0);
            Vec3 it0 = ib0 + new Vec3(0, height, 0), it1 = ib1 + new Vec3(0, height, 0);
            AddQuad(addTriangle, ob0, ob1, ot1, ot0, material);
            AddQuad(addTriangle, ib1, ib0, it0, it1, material);
            AddQuad(addTriangle, it0, it1, ot1, ot0, material);
            AddQuad(addTriangle, ib0, ob0, ob1, ib1, material);
        }
    }

    protected static void AddDisk(AddTriangleCallback addTriangle, Vec3 center, double radius, int sides, bool normalUp, Material material)
    {
        sides = Math.Clamp(sides, 3, 256);
        for (int i = 0; i < sides; i++)
        {
            double a0 = 2.0 * Math.PI * i / sides;
            double a1 = 2.0 * Math.PI * (i + 1) / sides;
            Vec3 p0 = center + new Vec3(Math.Cos(a0) * radius, 0, Math.Sin(a0) * radius);
            Vec3 p1 = center + new Vec3(Math.Cos(a1) * radius, 0, Math.Sin(a1) * radius);
            if (normalUp) EmitTriangle(addTriangle, center, p0, p1, material);
            else EmitTriangle(addTriangle, center, p1, p0, material);
        }
    }
}
