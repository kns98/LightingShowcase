using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Preview;

internal sealed class PreviewCamera
{
    private Vec3 target;
    private double radius;
    private double yaw;
    private double pitch;

    public double FieldOfViewDegrees { get; set; } = 72.0;

    public CameraDefinition Snapshot()
    {
        double horizontal = radius * Math.Cos(pitch);
        Vec3 position = target + new Vec3(
            horizontal * Math.Sin(yaw),
            radius * Math.Sin(pitch),
            -horizontal * Math.Cos(yaw));

        return new CameraDefinition
        {
            Position = position,
            Target = target,
            Up = new Vec3(0, 1, 0),
            FieldOfViewDegrees = FieldOfViewDegrees,
            NearPlane = Math.Max(0.01, radius / 500.0),
            FarPlane = Math.Max(5000.0, radius * 40.0)
        };
    }

    public void Reset(Scene scene)
    {
        Aabb? bounds = ComputeBounds(scene.Triangles);
        target = bounds.HasValue ? (bounds.Value.Min + bounds.Value.Max) * 0.5 : new Vec3(0, 0.55, 0);
        Vec3 extent = bounds.HasValue ? bounds.Value.Max - bounds.Value.Min : new Vec3(2, 2, 2);
        radius = Math.Max(0.5, extent.Length() * 1.25);
        yaw = 0.32;
        pitch = 0.18;
    }

    public void Orbit(double deltaX, double deltaY)
    {
        yaw -= deltaX * 0.008;
        pitch = Math.Clamp(pitch + deltaY * 0.008, -1.45, 1.45);
    }

    public void Zoom(double wheelDelta)
    {
        radius *= Math.Exp(-wheelDelta * 0.12);
        radius = Math.Clamp(radius, 0.05, 100000.0);
    }

    private static Aabb? ComputeBounds(IReadOnlyList<Triangle> triangles)
    {
        if (triangles.Count == 0)
            return null;

        Vec3 first = triangles[0].A;
        double minX = first.X, minY = first.Y, minZ = first.Z;
        double maxX = first.X, maxY = first.Y, maxZ = first.Z;

        foreach (Triangle triangle in triangles)
        {
            Expand(triangle.A);
            Expand(triangle.B);
            Expand(triangle.C);
        }

        return new Aabb(new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ));

        void Expand(Vec3 value)
        {
            minX = Math.Min(minX, value.X);
            minY = Math.Min(minY, value.Y);
            minZ = Math.Min(minZ, value.Z);
            maxX = Math.Max(maxX, value.X);
            maxY = Math.Max(maxY, value.Y);
            maxZ = Math.Max(maxZ, value.Z);
        }
    }
}
