// -----------------------------------------------------------------------------
// File: UndoRedo/SceneFingerprint.cs
// Purpose: Stable scene CRC/fingerprint support for undo/redo de-duplication.
//
// The undo/redo service uses this fingerprint to commit a pending user edit only
// when editable scene content actually changed. This keeps internal rebuilds,
// failed operations, selection-only changes, and identical repeated states out of
// history while still allowing full snapshot-based restoration.
// -----------------------------------------------------------------------------

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.UndoRedo;

/// <summary>Computes a stable 64-bit fingerprint over editable scene content.</summary>
internal static class SceneFingerprint
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Compute(Scene scene)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));

        ulong hash = OffsetBasis;
        AddString(ref hash, scene.Description);
        AddInt(ref hash, scene.ObjectGroups.Count);
        foreach (SceneObjectGroup group in scene.ObjectGroups.OrderBy(g => g.Id))
            AddGroup(ref hash, group);

        AddInt(ref hash, scene.Lights.Count);
        foreach (SceneLight light in scene.Lights.OrderBy(l => l.Id, StringComparer.Ordinal))
            AddLight(ref hash, light);

        return hash;
    }

    private static void AddGroup(ref ulong hash, SceneObjectGroup group)
    {
        AddString(ref hash, "group");
        AddInt(ref hash, group.Id);
        AddString(ref hash, group.Name);
        AddBool(ref hash, group.Visible);
        AddBool(ref hash, group.IsSelectable);
        AddVec3(ref hash, group.Position);
        AddVec3(ref hash, group.Rotation);
        AddVec3(ref hash, group.Scale);
        AddVec3(ref hash, group.Pivot);
        AddString(ref hash, group.PrimitiveKind);
        AddString(ref hash, group.PrimitiveSourceName);
        AddMaterial(ref hash, group.ColorOverride);

        AddInt(ref hash, group.LocalTriangles.Count);
        foreach (Triangle triangle in group.LocalTriangles)
            AddTriangle(ref hash, triangle);

        AddInt(ref hash, group.Children.Count);
        foreach (SceneObjectGroup child in group.Children.OrderBy(g => g.Id))
            AddGroup(ref hash, child);
    }

    private static void AddTriangle(ref ulong hash, Triangle triangle)
    {
        AddVec3(ref hash, triangle.A);
        AddVec3(ref hash, triangle.B);
        AddVec3(ref hash, triangle.C);
        AddVec2(ref hash, triangle.UvA);
        AddVec2(ref hash, triangle.UvB);
        AddVec2(ref hash, triangle.UvC);
        AddInt(ref hash, triangle.GroupId);
        AddMaterial(ref hash, triangle.Material);
    }

    private static void AddMaterial(ref ulong hash, Material? material)
    {
        AddBool(ref hash, material != null);
        if (material == null)
            return;

        AddVec3(ref hash, material.Color);
        AddDouble(ref hash, material.Emission);
        AddString(ref hash, material.LightId);
        AddTexture(ref hash, material.Texture);
        AddVec3(ref hash, material.EmissionColor);
        AddTexture(ref hash, material.EmissiveTexture);
        AddDouble(ref hash, material.Alpha);
        AddBool(ref hash, material.AlphaBlend);
        AddDouble(ref hash, material.Metallic);
        AddDouble(ref hash, material.Roughness);
        AddDouble(ref hash, material.Transmission);
        AddTexture(ref hash, material.MetallicRoughnessTexture);
        AddTexture(ref hash, material.NormalTexture);
    }

    private static void AddTexture(ref ulong hash, TextureMap? texture)
    {
        AddBool(ref hash, texture != null);
        if (texture == null)
            return;

        AddString(ref hash, texture.Name);
        AddInt(ref hash, texture.Width);
        AddInt(ref hash, texture.Height);
        AddString(ref hash, texture.SourcePath);
        AddBool(ref hash, texture.IsBuiltInChecker);
    }

    private static void AddLight(ref ulong hash, SceneLight light)
    {
        AddString(ref hash, "light");
        AddString(ref hash, light.Id);
        AddInt(ref hash, (int)light.Kind);
        AddVec3(ref hash, light.Position);
        AddVec3(ref hash, light.Direction);
        AddVec3(ref hash, light.Color);
        AddDouble(ref hash, light.Intensity);
        AddDouble(ref hash, light.Range);
        AddDouble(ref hash, light.InnerConeAngle);
        AddDouble(ref hash, light.OuterConeAngle);
        AddBool(ref hash, light.Enabled);
        AddBool(ref hash, light.CastsShadow);
        AddBool(ref hash, light.IsImported);
        AddBool(ref hash, light.IsDefault);
    }

    private static void AddVec3(ref ulong hash, Vec3 value)
    {
        AddDouble(ref hash, value.X);
        AddDouble(ref hash, value.Y);
        AddDouble(ref hash, value.Z);
    }

    private static void AddVec2(ref ulong hash, Vec2 value)
    {
        AddDouble(ref hash, value.U);
        AddDouble(ref hash, value.V);
    }

    private static void AddString(ref ulong hash, string? value)
    {
        if (value == null)
        {
            AddInt(ref hash, -1);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AddInt(ref hash, bytes.Length);
        foreach (byte b in bytes)
            AddByte(ref hash, b);
    }

    private static void AddBool(ref ulong hash, bool value) => AddByte(ref hash, value ? (byte)1 : (byte)0);

    private static void AddInt(ref ulong hash, int value)
    {
        unchecked
        {
            uint v = (uint)value;
            AddByte(ref hash, (byte)v);
            AddByte(ref hash, (byte)(v >> 8));
            AddByte(ref hash, (byte)(v >> 16));
            AddByte(ref hash, (byte)(v >> 24));
        }
    }

    private static void AddDouble(ref ulong hash, double value)
    {
        long bits = double.IsNaN(value) ? long.MinValue : BitConverter.DoubleToInt64Bits(value == 0.0 ? 0.0 : value);
        unchecked
        {
            ulong v = (ulong)bits;
            for (int i = 0; i < 8; i++)
                AddByte(ref hash, (byte)(v >> (i * 8)));
        }
    }

    private static void AddByte(ref ulong hash, byte value)
    {
        unchecked
        {
            hash ^= value;
            hash *= Prime;
        }
    }
}
