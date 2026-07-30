// -----------------------------------------------------------------------------
// File: Camera/CameraController.cs
// Purpose: Interactive camera model.
//
// Stores canonical orbit/look-at camera state and applies keyboard/mouse movement
// for manual navigation. The controller owns user interaction only; import/render
// coordinate fixes belong in TransformConverter or renderer adapters.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CameraSystem;

/// <summary>Manual camera state and movement/orbit controls.</summary>
public sealed class CameraController
{
    private const double MinDistance = 0.05;
    private const double MaxDistance = 10000.0;

    public CameraDefinition Camera { get; } = new();
    public Vec3 Position { get => Camera.Position; private set => Camera.Position = value; }
    public Vec3 Target { get => Camera.Target; private set => Camera.Target = value; }
    public double Yaw { get; private set; }
    public double Pitch { get; private set; } = -0.09;
    public double Distance => Math.Max(MinDistance, (Position - Target).Length());

    public CameraController()
    {
        Target = Position + ForwardFromAngles() * 2.25;
    }

    /// <summary>Resets to a safe default value.</summary>
    public void Reset()
    {
        Camera.Position = new Vec3(0.0, 0.55, -2.25);
        Camera.Up = TransformConverter.WorldUp;
        Camera.FieldOfViewDegrees = 72.0;
        Yaw = 0.0;
        Pitch = -0.09;
        Camera.Target = Camera.Position + ForwardFromAngles() * 2.25;
    }

    /// <summary>Moves the camera and target together in a world-space direction.</summary>
    public void Move(Vec3 direction, double amount)
    {
        Vec3 delta = direction * amount;
        Position += delta;
        Target += delta;
    }

    public void SetPosition(Vec3 position)
    {
        Position = position;
        UpdateAnglesFromLookAt();
    }

    /// <summary>Free-look rotation around the current camera position.</summary>
    public void Rotate(double yawDelta, double pitchDelta)
    {
        Yaw += yawDelta;
        Pitch = Clamp(Pitch + pitchDelta, -1.35, 1.35);
        Target = Position + ForwardFromAngles() * Distance;
    }

    /// <summary>Orbit camera position around the current target.</summary>
    public void Orbit(double yawDelta, double pitchDelta)
    {
        Yaw += yawDelta;
        Pitch = Clamp(Pitch + pitchDelta, -1.35, 1.35);
        RebuildPositionFromOrbit(Distance);
    }

    /// <summary>Pan camera and target in camera-right/camera-up space.</summary>
    public void Pan(double deltaX, double deltaY, double speed = 1.0)
    {
        CameraBasis basis = GetBasis();
        double scale = Math.Max(0.001, Distance * 0.001 * speed);
        Vec3 offset = basis.Right * (-deltaX * scale) + basis.Up * (deltaY * scale);
        Position += offset;
        Target += offset;
    }

    /// <summary>Dolly zooms toward/away from the target while preserving orbit angle.</summary>
    public void Zoom(double wheelDelta, double sensitivity = 0.0015)
    {
        double factor = Math.Exp(-wheelDelta * sensitivity);
        RebuildPositionFromOrbit(Clamp(Distance * factor, MinDistance, MaxDistance));
    }

    /// <summary>Sets look at while preserving related state invariants.</summary>
    public void SetLookAt(Vec3 position, Vec3 target)
    {
        Position = position;
        Target = target;
        UpdateAnglesFromLookAt();
    }

    /// <summary>Frames a bounding box in the current view direction.</summary>
    public void Frame(Aabb bounds, double padding = 1.25)
    {
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        Vec3 diagonal = bounds.Max - bounds.Min;
        double radius = Math.Max(0.05, diagonal.Length() * 0.5);
        double fov = Math.Max(5.0, Math.Min(140.0, Camera.FieldOfViewDegrees)) * Math.PI / 180.0;
        double distance = radius / Math.Tan(fov * 0.5) * Math.Max(1.0, padding);
        Target = center;
        RebuildPositionFromOrbit(distance);
    }

    /// <summary>Returns basis derived from the current state.</summary>
    public CameraBasis GetBasis() => Camera.ToBasis();

    public CameraDefinition ToDefinition() => Camera.Clone();

    private void RebuildPositionFromOrbit(double distance)
    {
        Vec3 forward = ForwardFromAngles();
        Position = Target - forward * Clamp(distance, MinDistance, MaxDistance);
    }

    private Vec3 ForwardFromAngles()
    {
        Vec3 forward = new(
            Math.Sin(Yaw) * Math.Cos(Pitch),
            Math.Sin(Pitch),
            Math.Cos(Yaw) * Math.Cos(Pitch)
        );
        Vec3 normalized = forward.Normalize();
        return normalized.Length() < 1e-8 ? TransformConverter.WorldForward : normalized;
    }

    private void UpdateAnglesFromLookAt()
    {
        Vec3 dir = (Target - Position).Normalize();
        if (dir.Length() < 1e-8)
            dir = TransformConverter.WorldForward;
        Yaw = Math.Atan2(dir.X, dir.Z);
        Pitch = Math.Asin(Clamp(dir.Y, -1.0, 1.0));
    }

    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
}
