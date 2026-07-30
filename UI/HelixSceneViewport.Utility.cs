// -----------------------------------------------------------------------------
// File: UI/HelixSceneViewport.Utility.cs
// Purpose: Shared conversion and mesh helper methods used by Helix viewport partials.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using WpfColor = System.Windows.Media.Color;

namespace LightingShowcase.UI;

public sealed partial class HelixSceneViewport
{
    /// <summary>Adds or creates quad for this subsystem.</summary>
    private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        int i = mesh.Positions.Count;
        mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(d);
        mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
    }


    /// <summary>Creates a brightly lit WPF model for editor-only helpers such as gizmo handles.</summary>
    private static GeometryModel3D CreateModel(MeshGeometry3D mesh, WpfColor color)
    {
        MaterialGroup material = new();
        SolidColorBrush brush = new(color);
        material.Children.Add(new DiffuseMaterial(brush));
        material.Children.Add(new EmissiveMaterial(brush));
        return new GeometryModel3D { Geometry = mesh, Material = material, BackMaterial = material };
    }


    /// <summary>Applies a pending scene transform to cached Helix models without rebuilding their large mesh buffers.</summary>
    public void PreviewGroupTransform(int groupId, Scene scene)
    {
        syncedScene = scene;
        SceneObjectGroup? group = scene.GroupById(groupId);
        if (group == null || !groupToModels.TryGetValue(groupId, out List<GeometryModel3D>? models))
            return;

        Transform3D transform = CreateGroupTransform(group);
        foreach (GeometryModel3D model in models)
            model.Transform = transform;

        Aabb bounds = group.GetWorldBounds();
        selectedBounds = bounds;
        RebuildGizmo();
        viewport.InvalidateVisual();
        gizmoViewport.InvalidateVisual();
    }


    /// <summary>Rebuilds only one group's Helix meshes from regenerated scene geometry.</summary>
    public void RebuildGroupGeometry(int groupId, Scene scene)
    {
        syncedScene = scene;
        if (!groupToModels.TryGetValue(groupId, out List<GeometryModel3D>? oldModels))
        {
            SyncFromScene(scene);
            return;
        }

        foreach (GeometryModel3D model in oldModels)
        {
            root.Children.Remove(model);
            modelToGroupId.Remove(model);
            modelBaseMaterials.Remove(model);
            modelSelectedMaterials.Remove(model);
        }
        groupToModels.Remove(groupId);

        SceneObjectGroup? group = scene.GroupById(groupId);
        if (group != null)
        {
            List<GeometryModel3D> newModels = new();
            foreach (GeometryModel3D model in BuildModels(group, selected: false))
            {
                modelToGroupId[model] = group.Id;
                modelBaseMaterials[model] = model.Material;
                modelSelectedMaterials[model] = CreateSelectedViewportMaterial(model.Material);
                newModels.Add(model);
                root.Children.Add(model);
            }
            if (newModels.Count > 0)
                groupToModels[group.Id] = newModels;
        }

        ApplySelectionHighlightAndBounds();
        RebuildGizmo();
        viewport.InvalidateVisual();
        gizmoViewport.InvalidateVisual();
    }

    private static Transform3D CreateGroupTransform(SceneObjectGroup group)
    {
        if (group.Position.Length() <= 1e-12 && group.Rotation.Length() <= 1e-12 &&
            Math.Abs(group.Scale.X - 1.0) <= 1e-12 &&
            Math.Abs(group.Scale.Y - 1.0) <= 1e-12 &&
            Math.Abs(group.Scale.Z - 1.0) <= 1e-12)
            return Transform3D.Identity;

        // Keep Helix live-preview transforms numerically identical to the scene
        // graph/raytracer transform path.  A Transform3DGroup made from separate
        // WPF RotateTransform3D children can compose Euler rotations differently
        // from SceneObjectGroup.TransformPoint, which is most visible on
        // asymmetric ready-made objects such as the Bed.
        Point3D origin = ToPoint(TransformLikeScene(group, Vec3.Zero));
        Point3D xBasis = ToPoint(TransformLikeScene(group, new Vec3(1, 0, 0)));
        Point3D yBasis = ToPoint(TransformLikeScene(group, new Vec3(0, 1, 0)));
        Point3D zBasis = ToPoint(TransformLikeScene(group, new Vec3(0, 0, 1)));

        Matrix3D matrix = new(
            xBasis.X - origin.X, xBasis.Y - origin.Y, xBasis.Z - origin.Z, 0,
            yBasis.X - origin.X, yBasis.Y - origin.Y, yBasis.Z - origin.Z, 0,
            zBasis.X - origin.X, zBasis.Y - origin.Y, zBasis.Z - origin.Z, 0,
            origin.X, origin.Y, origin.Z, 1);

        return new MatrixTransform3D(matrix);
    }

    private static Vec3 TransformLikeScene(SceneObjectGroup group, Vec3 point)
    {
        Vec3 q = point - group.Pivot;
        q = new Vec3(q.X * group.Scale.X, q.Y * group.Scale.Y, q.Z * group.Scale.Z);
        q = RotateLikeScene(q, group.Rotation);
        return group.Pivot + group.Position + q;
    }

    private static Vec3 RotateLikeScene(Vec3 point, Vec3 rotation)
    {
        double cx = Math.Cos(rotation.X), sx = Math.Sin(rotation.X);
        double cy = Math.Cos(rotation.Y), sy = Math.Sin(rotation.Y);
        double cz = Math.Cos(rotation.Z), sz = Math.Sin(rotation.Z);

        Vec3 x = new(point.X, point.Y * cx - point.Z * sx, point.Y * sx + point.Z * cx);
        Vec3 y = new(x.X * cy + x.Z * sy, x.Y, -x.X * sy + x.Z * cy);
        return new Vec3(y.X * cz - y.Y * sz, y.X * sz + y.Y * cz, y.Z);
    }

    /// <summary>Implements the to point operation for this file's subsystem.</summary>
    private static Point3D ToPoint(Vec3 v) => new(v.X, v.Y, v.Z);
    private static Vector3D ToVector(Vec3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Implements the to color operation for this file's subsystem.</summary>
    private static WpfColor ToColor(Vec3 color) => ToColor(color, 1.0);

    private static WpfColor ToColor(Vec3 color, double alpha)
    {
        byte a = ClampByte(alpha * 255.0);
        byte r = ClampByte(color.X * 255.0);
        byte g = ClampByte(color.Y * 255.0);
        byte b = ClampByte(color.Z * 255.0);
        return WpfColor.FromArgb(a, r, g, b);
    }

    /// <summary>Implements the clamp byte operation for this file's subsystem.</summary>
    private static byte ClampByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
