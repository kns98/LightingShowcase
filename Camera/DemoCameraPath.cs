// -----------------------------------------------------------------------------
// File: Camera/DemoCameraPath.cs
// Purpose: Demo camera animation.
//
// Stores camera keyframes, interpolates between them, and provides an editable camera path for playback.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.CameraSystem;

/// <summary>Editable camera animation path with time-based interpolation.</summary>
public sealed class DemoCameraPath
{
    private readonly List<CameraKey> keys = new()
    {
        new(0.00, new Vec3( 0.00, 0.60, -2.20), new Vec3( 0.00, -0.15, 3.25)),
        new(0.14, new Vec3(-1.55, 0.70,  1.35), new Vec3(-0.10, -0.95, 3.00)),
        new(0.28, new Vec3(-1.95, 0.95,  2.85), new Vec3( 0.35, -0.90, 3.30)),
        new(0.42, new Vec3(-0.45, 0.30,  2.15), new Vec3( 0.15, -1.05, 3.10)),
        new(0.56, new Vec3( 1.50, 0.75,  2.15), new Vec3( 0.25, -0.90, 4.15)),
        new(0.70, new Vec3( 1.55, 1.25,  4.45), new Vec3(-0.35, -0.35, 3.25)),
        new(0.82, new Vec3(-1.55, 1.00,  4.80), new Vec3(-2.10,  0.70, 4.00)),
        new(0.92, new Vec3(-0.20, 1.15,  5.15), new Vec3( 0.00, -0.45, 3.25)),
        new(1.00, new Vec3( 0.00, 0.60, -2.20), new Vec3( 0.00, -0.15, 3.25))
    };

    public IReadOnlyList<CameraKey> Keys => keys;

    /// <summary>Implements the sample operation for this file's subsystem.</summary>
    public CameraSample Sample(double normalizedTime)
    {
        if (keys.Count == 0)
            return new CameraSample(new Vec3(0.0, 0.55, -2.25), new Vec3(0.0, 0.0, 3.0));
        if (keys.Count == 1)
            return new CameraSample(keys[0].Position, keys[0].Target);

        double t = ((normalizedTime % 1.0) + 1.0) % 1.0;
        SortKeys();
        for (int i = 0; i < keys.Count - 1; i++)
        {
            CameraKey a = keys[i], b = keys[i + 1];
            if (t >= a.Time && t <= b.Time)
            {
                double span = System.Math.Max(0.000001, b.Time - a.Time);
                double local = Smooth((t - a.Time) / span);
                return new CameraSample(Vec3.Lerp(a.Position, b.Position, local), Vec3.Lerp(a.Target, b.Target, local));
            }
        }
        return new CameraSample(keys[0].Position, keys[0].Target);
    }

    /// <summary>Updates key from the current application state.</summary>
    public void UpdateKey(int index, CameraKey key)
    {
        if (index < 0 || index >= keys.Count)
            return;
        keys[index] = ClampKey(key);
        SortKeys();
    }

    /// <summary>Adds or creates key for this subsystem.</summary>
    public int AddKey(CameraKey key)
    {
        keys.Add(ClampKey(key));
        SortKeys();
        return keys.FindIndex(k => NearlyEqual(k.Time, ClampTime(key.Time)) && SameVector(k.Position, key.Position) && SameVector(k.Target, key.Target));
    }

    /// <summary>Implements the remove key operation for this file's subsystem.</summary>
    public void RemoveKey(int index)
    {
        if (index < 0 || index >= keys.Count || keys.Count <= 1)
            return;
        keys.RemoveAt(index);
        SortKeys();
    }

    /// <summary>Implements the sort keys operation for this file's subsystem.</summary>
    private void SortKeys() => keys.Sort((a, b) => a.Time.CompareTo(b.Time));
    private static CameraKey ClampKey(CameraKey key) => new(ClampTime(key.Time), key.Position, key.Target);
    private static double ClampTime(double v) => System.Math.Max(0.0, System.Math.Min(1.0, v));
    /// <summary>Implements the smooth operation for this file's subsystem.</summary>
    private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);
    private static bool NearlyEqual(double a, double b) => System.Math.Abs(a - b) < 0.000001;
    private static bool SameVector(Vec3 a, Vec3 b) => (a - b).Length() < 0.000001;
}
