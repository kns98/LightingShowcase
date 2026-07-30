// -----------------------------------------------------------------------------
// File: UI/HelixSceneViewport.Lights.cs
// Purpose: Displays editable scene lights directly in the Helix viewport.
// -----------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfPoint = System.Windows.Point;

namespace LightingShowcase.UI;

public sealed partial class HelixSceneViewport
{
    private const double LightMarkerSceneFraction = 0.035;
    private const double SelectedLightMarkerMultiplier = 1.45;

    /// <summary>Rebuilds raster lights plus editor-only light markers and direction arrows.</summary>
    private void RebuildLightVisuals()
    {
        RebuildRasterLights();

        lightRoot.Children.Clear();
        modelToLightId.Clear();
        if (syncedScene == null || syncedScene.Lights.Count == 0)
            return;

        double sceneRadius = syncedScene.GetSceneBounds() is Aabb bounds ? BoundsRadius(bounds) : 2.0;
        double markerSize = Math.Clamp(sceneRadius * LightMarkerSceneFraction, 0.055, 0.42);
        double arrowLength = Math.Clamp(sceneRadius * 0.22, markerSize * 3.5, markerSize * 12.0);

        foreach (SceneLight light in syncedScene.Lights)
            AddLightVisual(light, markerSize, arrowLength);
    }

    /// <summary>Mirrors SceneLight objects into real WPF 3D lights for the fast raster preview.</summary>
    private void RebuildRasterLights()
    {
        rasterLightRoot.Children.Clear();
        rasterLightRoot.Children.Add(new AmbientLight(WpfColor.FromRgb(42, 45, 52)));

        if (syncedScene == null || syncedScene.Lights.Count == 0)
        {
            rasterLightRoot.Children.Add(new DirectionalLight(WpfColor.FromRgb(210, 220, 235), new Vector3D(-0.45, -0.65, -0.55)));
            return;
        }

        foreach (SceneLight light in syncedScene.Lights)
        {
            if (!light.Enabled)
                continue;

            WpfColor color = ToRasterLightColor(light);
            switch (light.Kind)
            {
                case SceneLightKind.Directional:
                    rasterLightRoot.Children.Add(new DirectionalLight(color, NormalizedLightVector(light.Direction)));
                    break;
                case SceneLightKind.Spot:
                    rasterLightRoot.Children.Add(CreateRasterSpotLight(light, color));
                    break;
                default:
                    PointLight point = new(color, ToPoint(light.Position));
                    if (light.Range > 0.0)
                        point.Range = light.Range;
                    rasterLightRoot.Children.Add(point);
                    break;
            }
        }
    }

    private static SpotLight CreateRasterSpotLight(SceneLight light, WpfColor color)
    {
        SpotLight spot = new()
        {
            Color = color,
            Position = ToPoint(light.Position),
            Direction = NormalizedLightVector(light.Direction),
            InnerConeAngle = RadiansToDegrees(Math.Clamp(light.InnerConeAngle, 0.0, Math.PI)),
            OuterConeAngle = RadiansToDegrees(Math.Clamp(light.OuterConeAngle, light.InnerConeAngle, Math.PI))
        };

        if (light.Range > 0.0)
            spot.Range = light.Range;

        return spot;
    }

    private static WpfColor ToRasterLightColor(SceneLight light)
    {
        Vec3 normalized = NormalizeLightColor(light.Color);
        double intensityScale = Math.Clamp(light.Intensity / 5.0, 0.08, 2.0);
        return ToColor(new Vec3(
            Math.Clamp(normalized.X * intensityScale, 0.0, 1.0),
            Math.Clamp(normalized.Y * intensityScale, 0.0, 1.0),
            Math.Clamp(normalized.Z * intensityScale, 0.0, 1.0)));
    }

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    private void AddLightVisual(SceneLight light, double markerSize, double arrowLength)
    {
        bool selected = string.Equals(light.Id, selectedLightId, StringComparison.OrdinalIgnoreCase);
        bool directional = light.Kind == SceneLightKind.Directional || light.Kind == SceneLightKind.Spot || selected;
        double size = markerSize * (selected ? SelectedLightMarkerMultiplier : 1.0);
        Point3D center = ToPoint(light.Position);
        WpfColor lightColor = light.Enabled ? ToColor(NormalizeLightColor(light.Color), selected ? 1.0 : 0.88) : WpfColors.DimGray;
        WpfColor markerColor = selected ? WpfColors.Gold : lightColor;

        GeometryModel3D marker = light.Kind switch
        {
            SceneLightKind.Directional => CreateLightDiamond(center, size, markerColor),
            SceneLightKind.Spot => CreateCone(center, center + NormalizedLightVector(light.Direction) * size * 1.8, size * 0.72, markerColor, 18),
            _ => CreateCube(center, size, markerColor)
        };
        RegisterLightModel(marker, light.Id);
        lightRoot.Children.Add(marker);

        if (directional)
        {
            Vector3D direction = NormalizedLightVector(light.Direction);
            Point3D arrowStart = center;
            Point3D arrowEnd = center + direction * (selected ? arrowLength * 1.25 : arrowLength);
            GeometryModel3D shaft = CreateCylinder(arrowStart, arrowEnd, size * 0.11, lightColor, 18);
            RegisterLightModel(shaft, light.Id);
            lightRoot.Children.Add(shaft);

            GeometryModel3D tip = CreateCone(arrowEnd, arrowEnd + direction * size * 0.85, size * 0.35, lightColor, 18);
            RegisterLightModel(tip, light.Id);
            lightRoot.Children.Add(tip);
        }

        if (light.Kind == SceneLightKind.Spot)
            AddSpotConeGuide(light, center, size, arrowLength, selected, lightColor);
    }

    private void AddSpotConeGuide(SceneLight light, Point3D center, double markerSize, double arrowLength, bool selected, WpfColor color)
    {
        Vector3D direction = NormalizedLightVector(light.Direction);
        double guideLength = arrowLength * (selected ? 1.0 : 0.68);
        double coneRadius = Math.Tan(Math.Clamp(light.OuterConeAngle, 0.02, Math.PI * 0.49)) * guideLength;
        if (!double.IsFinite(coneRadius) || coneRadius <= 0.0)
            return;

        Vector3D sideA = Vector3D.CrossProduct(direction, Math.Abs(direction.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0));
        if (sideA.LengthSquared < 0.000001)
            sideA = new Vector3D(1, 0, 0);
        sideA.Normalize();
        Vector3D sideB = Vector3D.CrossProduct(direction, sideA);
        sideB.Normalize();
        Point3D capCenter = center + direction * guideLength;
        double lineRadius = Math.Max(markerSize * 0.035, 0.01);

        Vector3D[] offsets =
        {
            sideA * coneRadius,
            sideA * -coneRadius,
            sideB * coneRadius,
            sideB * -coneRadius
        };

        foreach (Vector3D offset in offsets)
        {
            GeometryModel3D guide = CreateCylinder(center, capCenter + offset, lineRadius, color, 10);
            RegisterLightModel(guide, light.Id);
            lightRoot.Children.Add(guide);
        }
    }

    private void RegisterLightModel(GeometryModel3D model, string lightId)
    {
        modelToLightId[model] = lightId;
    }

    private bool TryHitLight(WpfPoint point, out string lightId)
    {
        lightId = string.Empty;
        double bestDistance = double.PositiveInfinity;
        string? bestLightId = null;

        VisualTreeHelper.HitTest(
            gizmoViewport.Viewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult meshHit &&
                    meshHit.ModelHit is GeometryModel3D model &&
                    modelToLightId.TryGetValue(model, out string candidate) &&
                    meshHit.DistanceToRayOrigin < bestDistance)
                {
                    bestDistance = meshHit.DistanceToRayOrigin;
                    bestLightId = candidate;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (string.IsNullOrWhiteSpace(bestLightId))
            return false;

        lightId = bestLightId;
        return true;
    }

    private void FrameLight(string lightId)
    {
        if (syncedScene == null)
            return;

        SceneLight? light = syncedScene.Lights.FirstOrDefault(l => string.Equals(l.Id, lightId, StringComparison.OrdinalIgnoreCase));
        if (light == null)
            return;

        double radius = syncedScene.GetSceneBounds() is Aabb bounds ? BoundsRadius(bounds) * 0.08 : 0.25;
        Vec3 min = light.Position - new Vec3(radius, radius, radius);
        Vec3 max = light.Position + new Vec3(radius, radius, radius);
        LookAtBounds(new Aabb(min, max));
    }

    private static Vector3D NormalizedLightVector(Vec3 direction)
    {
        Vector3D vector = new(direction.X, direction.Y, direction.Z);
        if (vector.LengthSquared < 0.000001)
            vector = new Vector3D(0, 0, -1);
        vector.Normalize();
        return vector;
    }

    private static Vec3 NormalizeLightColor(Vec3 color)
    {
        double max = Math.Max(color.X, Math.Max(color.Y, color.Z));
        if (!double.IsFinite(max) || max < 0.05)
            return new Vec3(1.0, 0.92, 0.76);
        return new Vec3(
            Math.Clamp(color.X / max, 0.0, 1.0),
            Math.Clamp(color.Y / max, 0.0, 1.0),
            Math.Clamp(color.Z / max, 0.0, 1.0));
    }

    private static GeometryModel3D CreateLightDiamond(Point3D center, double size, WpfColor color)
    {
        MeshGeometry3D mesh = new();
        double h = size * 0.9;
        Point3D top = new(center.X, center.Y + h, center.Z);
        Point3D bottom = new(center.X, center.Y - h, center.Z);
        Point3D left = new(center.X - h, center.Y, center.Z);
        Point3D right = new(center.X + h, center.Y, center.Z);
        Point3D front = new(center.X, center.Y, center.Z - h);
        Point3D back = new(center.X, center.Y, center.Z + h);

        AddTriangle(mesh, top, front, right);
        AddTriangle(mesh, top, right, back);
        AddTriangle(mesh, top, back, left);
        AddTriangle(mesh, top, left, front);
        AddTriangle(mesh, bottom, right, front);
        AddTriangle(mesh, bottom, back, right);
        AddTriangle(mesh, bottom, left, back);
        AddTriangle(mesh, bottom, front, left);
        return CreateModel(mesh, color);
    }

    private static GeometryModel3D CreateCone(Point3D baseCenter, Point3D tip, double baseRadius, WpfColor color, int segments)
    {
        MeshGeometry3D mesh = new();
        Vector3D axis = tip - baseCenter;
        if (axis.LengthSquared < 0.000001)
            axis = new Vector3D(0, 0, -1);
        axis.Normalize();

        Vector3D n1 = Vector3D.CrossProduct(axis, Math.Abs(axis.Y) < 0.9 ? new Vector3D(0, 1, 0) : new Vector3D(1, 0, 0));
        if (n1.LengthSquared < 0.000001)
            n1 = new Vector3D(1, 0, 0);
        n1.Normalize();
        Vector3D n2 = Vector3D.CrossProduct(axis, n1);
        n2.Normalize();

        for (int i = 0; i < segments; i++)
        {
            double t0 = i * Math.PI * 2.0 / segments;
            double t1 = (i + 1) * Math.PI * 2.0 / segments;
            Point3D p0 = baseCenter + n1 * Math.Cos(t0) * baseRadius + n2 * Math.Sin(t0) * baseRadius;
            Point3D p1 = baseCenter + n1 * Math.Cos(t1) * baseRadius + n2 * Math.Sin(t1) * baseRadius;
            AddTriangle(mesh, p0, p1, tip);
            AddTriangle(mesh, baseCenter, p1, p0);
        }
        return CreateModel(mesh, color);
    }

    private static void AddTriangle(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
    {
        int i = mesh.Positions.Count;
        mesh.Positions.Add(a);
        mesh.Positions.Add(b);
        mesh.Positions.Add(c);
        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 1);
        mesh.TriangleIndices.Add(i + 2);
    }
}
