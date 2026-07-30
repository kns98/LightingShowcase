// -----------------------------------------------------------------------------
// File: Scene/TransformConverter.cs
// Purpose: One canonical transform/axis conversion layer.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Centralized transform helpers so importers, preview, and final render share one convention.</summary>
public static class TransformConverter
{
    public static readonly Vec3 WorldUp = new(0, 1, 0);
    public static readonly Vec3 WorldForward = new(0, 0, 1);
    public static readonly Vec3 WorldRight = new(1, 0, 0);

    public static Vec3 SanitizeScale(Vec3 scale)
    {
        return new Vec3(
            Math.Abs(scale.X) < 1e-8 ? 1.0 : scale.X,
            Math.Abs(scale.Y) < 1e-8 ? 1.0 : scale.Y,
            Math.Abs(scale.Z) < 1e-8 ? 1.0 : scale.Z);
    }

    public static Vec3 RotateEuler(Vec3 point, Vec3 rotation)
    {
        double cx = Math.Cos(rotation.X), sx = Math.Sin(rotation.X);
        double cy = Math.Cos(rotation.Y), sy = Math.Sin(rotation.Y);
        double cz = Math.Cos(rotation.Z), sz = Math.Sin(rotation.Z);

        Vec3 p = point;
        p = new Vec3(p.X, p.Y * cx - p.Z * sx, p.Y * sx + p.Z * cx);
        p = new Vec3(p.X * cy + p.Z * sy, p.Y, -p.X * sy + p.Z * cy);
        p = new Vec3(p.X * cz - p.Y * sz, p.X * sz + p.Y * cz, p.Z);
        return p;
    }

    public static Vec3 ApplySrt(Vec3 point, Vec3 pivot, Vec3 position, Vec3 rotation, Vec3 scale)
    {
        Vec3 q = point - pivot;
        Vec3 safeScale = SanitizeScale(scale);
        q = new Vec3(q.X * safeScale.X, q.Y * safeScale.Y, q.Z * safeScale.Z);
        q = RotateEuler(q, rotation);
        return pivot + position + q;
    }

    public static Vec3 FromRightHandedZForwardToCanonical(Vec3 value) => new(value.X, value.Y, value.Z);
    public static Vec3 FromZUpToCanonicalYUp(Vec3 value) => new(value.X, value.Z, -value.Y);
    public static Vec3 MirrorX(Vec3 value) => new(-value.X, value.Y, value.Z);
}
