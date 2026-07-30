// -----------------------------------------------------------------------------
// File: Camera/CameraKey.cs
// Purpose: Camera keyframe.
//
// Immutable value describing one point on the demo camera path: time, position, target, and field of view.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.CameraSystem;

/// <summary>One editable keyframe in the demo camera path.</summary>
public readonly struct CameraKey
{
    public readonly double Time;
    public readonly Vec3 Position;
    public readonly Vec3 Target;

    /// <summary>Constructs and initializes this component.</summary>
    public CameraKey(double time, Vec3 position, Vec3 target)
    {
        Time = time;
        Position = position;
        Target = target;
    }
}
