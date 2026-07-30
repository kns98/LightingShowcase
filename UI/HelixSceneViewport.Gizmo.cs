// -----------------------------------------------------------------------------
// File: UI/HelixSceneViewport.Gizmo.cs
// Purpose: Builds, scales, and hit-tests the transform gizmo.
// -----------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LightingShowcase.SceneGraph;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfPoint = System.Windows.Point;

namespace LightingShowcase.UI;

public sealed partial class HelixSceneViewport
{
    private void RebuildGizmo()
    {
        gizmoRoot.Children.Clear();
        modelToGizmoHandle.Clear();
        if (selectedGroupId <= 0 || selectedGroupIds.Count != 1 || selectedBounds == null)
        {
            gizmoViewport.InvalidateVisual();
            return;
        }

        Aabb bounds = selectedBounds.Value;
        selectedCenter = ToPoint((bounds.Min + bounds.Max) * 0.5);
        UpdateGizmoScale();

        AddMoveAxis(GizmoAxis.X, new Vector3D(1, 0, 0), WpfColors.Red);
        AddMoveAxis(GizmoAxis.Y, new Vector3D(0, 1, 0), WpfColors.LimeGreen);
        AddMoveAxis(GizmoAxis.Z, new Vector3D(0, 0, 1), WpfColors.DodgerBlue);
        AddScaleHandle(GizmoAxis.X, new Vector3D(1, 0, 0), WpfColors.Red);
        AddScaleHandle(GizmoAxis.Y, new Vector3D(0, 1, 0), WpfColors.LimeGreen);
        AddScaleHandle(GizmoAxis.Z, new Vector3D(0, 0, 1), WpfColors.DodgerBlue);
        AddRotationRing(GizmoAxis.X, WpfColors.Red);
        AddRotationRing(GizmoAxis.Y, WpfColors.LimeGreen);
        AddRotationRing(GizmoAxis.Z, WpfColors.DodgerBlue);
        gizmoViewport.InvalidateVisual();
    }

    /// <summary>Updates gizmo scale from the current application state.</summary>
    private void UpdateGizmoScale()
    {
        if (selectedGroupId <= 0 || viewport.Camera is not PerspectiveCamera camera)
            return;

        Vector3D toCamera = camera.Position - selectedCenter;
        double cameraDistance = Math.Max(0.25, toCamera.Length);
        gizmoWorldSize = Math.Clamp(cameraDistance * 0.18, 0.25, 8.0);
        gizmoViewport.InvalidateVisual();
    }

    /// <summary>Adds or creates move axis for this subsystem.</summary>
    private void AddMoveAxis(GizmoAxis axis, Vector3D direction, WpfColor color)
    {
        double length = gizmoWorldSize;
        double radius = gizmoWorldSize * 0.04;
        Point3D p0 = selectedCenter;
        Point3D p1 = selectedCenter + direction * length;
        GeometryModel3D shaft = CreateCylinder(p0, p1, radius, color, 18);
        RegisterGizmoModel(shaft, new GizmoHandle(GizmoOperation.Move, axis, direction));
        gizmoRoot.Children.Add(shaft);
    }

    /// <summary>Adds or creates scale handle for this subsystem.</summary>
    private void AddScaleHandle(GizmoAxis axis, Vector3D direction, WpfColor color)
    {
        double centerDistance = gizmoWorldSize * 1.16;
        double size = gizmoWorldSize * 0.16;
        Point3D center = selectedCenter + direction * centerDistance;
        GeometryModel3D cube = CreateCube(center, size, color);
        RegisterGizmoModel(cube, new GizmoHandle(GizmoOperation.Scale, axis, direction));
        gizmoRoot.Children.Add(cube);
    }

    /// <summary>Adds or creates rotation ring for this subsystem.</summary>
    private void AddRotationRing(GizmoAxis axis, WpfColor color)
    {
        double radius = gizmoWorldSize * 0.72;
        double tubeRadius = gizmoWorldSize * 0.022;
        GeometryModel3D ring = CreateTorus(axis, selectedCenter, radius, tubeRadius, color);
        RegisterGizmoModel(ring, new GizmoHandle(GizmoOperation.Rotate, axis, AxisVector(axis)));
        gizmoRoot.Children.Add(ring);
    }

    /// <summary>Implements the register gizmo model operation for this file's subsystem.</summary>
    private void RegisterGizmoModel(GeometryModel3D model, GizmoHandle handle)
    {
        modelToGizmoHandle[model] = handle;
    }

    /// <summary>
    /// Hit-tests every gizmo model under the pointer and accepts the nearest registered handle.
    /// WPF's default single-result hit test can return whichever gizmo surface is closest to
    /// the camera, which made the visually rearward axis hard to select when rings overlap.
    /// Enumerating all ray hits keeps all three axes usable even when one is partially hidden.
    /// </summary>
    private bool TryHitGizmo(WpfPoint point, out GizmoHandle handle)
    {
        handle = default;
        double bestDistance = double.PositiveInfinity;
        GizmoHandle bestHandle = default;
        bool found = false;

        VisualTreeHelper.HitTest(
            gizmoViewport.Viewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult meshHit &&
                    meshHit.ModelHit is GeometryModel3D model &&
                    modelToGizmoHandle.TryGetValue(model, out GizmoHandle candidate) &&
                    meshHit.DistanceToRayOrigin < bestDistance)
                {
                    bestDistance = meshHit.DistanceToRayOrigin;
                    bestHandle = candidate;
                    found = true;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (found)
            handle = bestHandle;

        return found;
    }

    /// <summary>Implements the project axis to screen operation for this file's subsystem.</summary>
    private Vector ProjectAxisToScreen(Vector3D axis, WpfPoint fallbackPoint)
    {
        if (viewport.Camera is not ProjectionCamera camera)
            return new Vector(1, 0);

        Vector3D look = camera.LookDirection;
        look.Normalize();
        Vector3D up = camera.UpDirection;
        up.Normalize();
        Vector3D right = Vector3D.CrossProduct(look, up);
        if (right.LengthSquared < 0.000001)
            right = new Vector3D(1, 0, 0);
        right.Normalize();

        Vector screen = new(Vector3D.DotProduct(axis, right), -Vector3D.DotProduct(axis, up));
        if (screen.LengthSquared < 0.0001)
            screen = new Vector(fallbackPoint.X >= viewport.ActualWidth * 0.5 ? 1 : -1, 0);
        screen.Normalize();
        return screen;
    }

    /// <summary>Creates cylinder for use by the renderer or editor.</summary>
    private static GeometryModel3D CreateCylinder(Point3D a, Point3D b, double radius, WpfColor color, int segments)
    {
        MeshGeometry3D mesh = new();
        Vector3D axis = b - a;
        axis.Normalize();
        Vector3D n1 = Vector3D.CrossProduct(axis, Math.Abs(axis.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0));
        n1.Normalize();
        Vector3D n2 = Vector3D.CrossProduct(axis, n1);
        n2.Normalize();

        for (int i = 0; i < segments; i++)
        {
            double t0 = i * Math.PI * 2.0 / segments;
            double t1 = (i + 1) * Math.PI * 2.0 / segments;
            Vector3D r0 = n1 * Math.Cos(t0) * radius + n2 * Math.Sin(t0) * radius;
            Vector3D r1 = n1 * Math.Cos(t1) * radius + n2 * Math.Sin(t1) * radius;
            AddQuad(mesh, a + r0, a + r1, b + r1, b + r0);
        }
        return CreateModel(mesh, color);
    }

    /// <summary>Creates cube for use by the renderer or editor.</summary>
    private static GeometryModel3D CreateCube(Point3D center, double size, WpfColor color)
    {
        MeshGeometry3D mesh = new();
        double h = size * 0.5;
        Point3D[] p =
        {
            new(center.X-h, center.Y-h, center.Z-h), new(center.X+h, center.Y-h, center.Z-h),
            new(center.X+h, center.Y+h, center.Z-h), new(center.X-h, center.Y+h, center.Z-h),
            new(center.X-h, center.Y-h, center.Z+h), new(center.X+h, center.Y-h, center.Z+h),
            new(center.X+h, center.Y+h, center.Z+h), new(center.X-h, center.Y+h, center.Z+h)
        };
        AddQuad(mesh, p[0], p[1], p[2], p[3]);
        AddQuad(mesh, p[4], p[7], p[6], p[5]);
        AddQuad(mesh, p[0], p[4], p[5], p[1]);
        AddQuad(mesh, p[1], p[5], p[6], p[2]);
        AddQuad(mesh, p[2], p[6], p[7], p[3]);
        AddQuad(mesh, p[3], p[7], p[4], p[0]);
        return CreateModel(mesh, color);
    }

    /// <summary>Creates torus for use by the renderer or editor.</summary>
    private static GeometryModel3D CreateTorus(GizmoAxis axis, Point3D center, double majorRadius, double minorRadius, WpfColor color)
    {
        MeshGeometry3D mesh = new();
        const int majorSegments = 80;
        const int minorSegments = 8;
        for (int i = 0; i < majorSegments; i++)
        {
            double a0 = i * Math.PI * 2.0 / majorSegments;
            double a1 = (i + 1) * Math.PI * 2.0 / majorSegments;
            for (int j = 0; j < minorSegments; j++)
            {
                double b0 = j * Math.PI * 2.0 / minorSegments;
                double b1 = (j + 1) * Math.PI * 2.0 / minorSegments;
                AddQuad(mesh,
                    TorusPoint(axis, center, majorRadius, minorRadius, a0, b0),
                    TorusPoint(axis, center, majorRadius, minorRadius, a1, b0),
                    TorusPoint(axis, center, majorRadius, minorRadius, a1, b1),
                    TorusPoint(axis, center, majorRadius, minorRadius, a0, b1));
            }
        }
        return CreateModel(mesh, color);
    }

    /// <summary>Implements the torus point operation for this file's subsystem.</summary>
    private static Point3D TorusPoint(GizmoAxis axis, Point3D c, double r, double tube, double a, double b)
    {
        double radial = r + tube * Math.Cos(b);
        double z = tube * Math.Sin(b);
        double x = radial * Math.Cos(a);
        double y = radial * Math.Sin(a);
        return axis switch
        {
            GizmoAxis.X => new Point3D(c.X + z, c.Y + x, c.Z + y),
            GizmoAxis.Y => new Point3D(c.X + x, c.Y + z, c.Z + y),
            _ => new Point3D(c.X + x, c.Y + y, c.Z + z)
        };
    }

    /// <summary>Implements the axis vector operation for this file's subsystem.</summary>
    private static Vector3D AxisVector(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => new Vector3D(1, 0, 0),
        GizmoAxis.Y => new Vector3D(0, 1, 0),
        _ => new Vector3D(0, 0, 1)
    };
}
