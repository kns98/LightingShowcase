// -----------------------------------------------------------------------------
// File: UI/HelixSceneViewport.SelectionTools.cs
// Purpose: Viewport marquee and lasso selection overlay and hit testing.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using WpfPoint = System.Windows.Point;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfKeyboard = System.Windows.Input.Keyboard;

namespace LightingShowcase.UI;

public sealed partial class HelixSceneViewport
{
    private const double MarqueeDragThresholdSquared = 16.0;
    private const double LassoPointMinimumSpacingSquared = 9.0;

    /// <summary>Creates the transparent 2D overlay used by rectangle and lasso selection.</summary>
    private void ConfigureSelectionOverlay()
    {
        selectionOverlay.Background = WpfBrushes.Transparent;
        selectionOverlay.IsHitTestVisible = false;

        marqueeRectangle.Visibility = Visibility.Collapsed;
        marqueeRectangle.StrokeThickness = 1.0;
        marqueeRectangle.Stroke = new WpfSolidColorBrush(WpfColor.FromArgb(230, 0, 122, 204));
        marqueeRectangle.Fill = new WpfSolidColorBrush(WpfColor.FromArgb(40, 0, 122, 204));
        selectionOverlay.Children.Add(marqueeRectangle);

        lassoPolyline.Visibility = Visibility.Collapsed;
        lassoPolyline.StrokeThickness = 1.5;
        lassoPolyline.Stroke = new WpfSolidColorBrush(WpfColor.FromArgb(235, 0, 122, 204));
        lassoPolyline.Fill = new WpfSolidColorBrush(WpfColor.FromArgb(30, 0, 122, 204));
        selectionOverlay.Children.Add(lassoPolyline);
    }

    private void BeginMarqueeSelection(WpfPoint point, bool isLasso)
    {
        marqueeDrag = new MarqueeSelectionDrag
        {
            Start = point,
            Current = point,
            IsLasso = isLasso
        };

        if (isLasso)
        {
            lassoPolyline.Points.Clear();
            lassoPolyline.Points.Add(point);
            lassoPolyline.Visibility = Visibility.Visible;
            marqueeRectangle.Visibility = Visibility.Collapsed;
        }
        else
        {
            UpdateMarqueeRectangle(point, point, crossing: false);
            marqueeRectangle.Visibility = Visibility.Visible;
            lassoPolyline.Visibility = Visibility.Collapsed;
        }

        gizmoViewport.CaptureMouse();
    }

    private void UpdateMarqueeSelection(WpfPoint point)
    {
        if (marqueeDrag == null)
            return;

        marqueeDrag.Current = point;
        Vector delta = point - marqueeDrag.Start;
        if (delta.LengthSquared >= MarqueeDragThresholdSquared)
            marqueeDrag.DragStarted = true;

        if (marqueeDrag.IsLasso)
        {
            if (lassoPolyline.Points.Count == 0 || SquaredDistance(lassoPolyline.Points[^1], point) >= LassoPointMinimumSpacingSquared)
                lassoPolyline.Points.Add(point);
        }
        else
        {
            UpdateMarqueeRectangle(marqueeDrag.Start, point, crossing: point.X < marqueeDrag.Start.X);
        }
    }

    private void CompleteMarqueeSelection()
    {
        if (marqueeDrag == null)
            return;

        MarqueeSelectionDrag selection = marqueeDrag;
        marqueeDrag = null;
        gizmoViewport.ReleaseMouseCapture();
        marqueeRectangle.Visibility = Visibility.Collapsed;
        lassoPolyline.Visibility = Visibility.Collapsed;

        if (!selection.DragStarted || syncedScene == null)
        {
            if (!selection.IsLasso)
                EmptySpacePicked?.Invoke();
            return;
        }

        IReadOnlyCollection<int> picked = selection.IsLasso
            ? PickGroupsByLasso(selection.LassoPoints.Count > 0 ? selection.LassoPoints : lassoPolyline.Points.ToList())
            : PickGroupsByRectangle(GetSelectionRect(selection.Start, selection.Current), crossing: selection.Current.X < selection.Start.X);

        GroupsMarqueeSelected?.Invoke(picked, GetSelectionCombineMode());
    }

    private void CancelMarqueeSelection()
    {
        marqueeDrag = null;
        marqueeRectangle.Visibility = Visibility.Collapsed;
        lassoPolyline.Visibility = Visibility.Collapsed;
        gizmoViewport.ReleaseMouseCapture();
    }

    private void UpdateMarqueeRectangle(WpfPoint start, WpfPoint current, bool crossing)
    {
        Rect rect = GetSelectionRect(start, current);
        Canvas.SetLeft(marqueeRectangle, rect.Left);
        Canvas.SetTop(marqueeRectangle, rect.Top);
        marqueeRectangle.Width = Math.Max(1.0, rect.Width);
        marqueeRectangle.Height = Math.Max(1.0, rect.Height);

        // Blue = full containment; green = crossing/intersection selection.
        if (crossing)
        {
            marqueeRectangle.Stroke = new WpfSolidColorBrush(WpfColor.FromArgb(235, 30, 160, 95));
            marqueeRectangle.Fill = new WpfSolidColorBrush(WpfColor.FromArgb(35, 30, 160, 95));
        }
        else
        {
            marqueeRectangle.Stroke = new WpfSolidColorBrush(WpfColor.FromArgb(230, 0, 122, 204));
            marqueeRectangle.Fill = new WpfSolidColorBrush(WpfColor.FromArgb(40, 0, 122, 204));
        }
    }

    private static Rect GetSelectionRect(WpfPoint a, WpfPoint b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private IReadOnlyCollection<int> PickGroupsByRectangle(Rect selectionRect, bool crossing)
    {
        if (syncedScene == null || selectionRect.Width < 1.0 || selectionRect.Height < 1.0)
            return Array.Empty<int>();

        List<int> ids = new();
        foreach (SceneObjectGroup group in syncedScene.ObjectGroups)
        {
            if (!group.IsSelectable || !group.Visible)
                continue;

            Rect? screenBounds = ProjectBoundsToScreen(group.GetWorldBounds());
            if (screenBounds == null)
                continue;

            bool selected = crossing
                ? selectionRect.IntersectsWith(screenBounds.Value)
                : selectionRect.Contains(screenBounds.Value);

            if (selected)
                ids.Add(group.Id);
        }

        return ids;
    }

    private IReadOnlyCollection<int> PickGroupsByLasso(IList<WpfPoint> polygon)
    {
        if (syncedScene == null || polygon.Count < 3)
            return Array.Empty<int>();

        List<int> ids = new();
        foreach (SceneObjectGroup group in syncedScene.ObjectGroups)
        {
            if (!group.IsSelectable || !group.Visible)
                continue;

            Rect? bounds = ProjectBoundsToScreen(group.GetWorldBounds());
            if (bounds == null)
                continue;

            WpfPoint center = new(bounds.Value.Left + bounds.Value.Width * 0.5, bounds.Value.Top + bounds.Value.Height * 0.5);
            WpfPoint[] testPoints =
            {
                center,
                new(bounds.Value.Left, bounds.Value.Top),
                new(bounds.Value.Right, bounds.Value.Top),
                new(bounds.Value.Right, bounds.Value.Bottom),
                new(bounds.Value.Left, bounds.Value.Bottom)
            };

            if (testPoints.Any(p => PointInPolygon(p, polygon)))
                ids.Add(group.Id);
        }

        return ids;
    }

    private Rect? ProjectBoundsToScreen(Aabb bounds)
    {
        Vec3 min = bounds.Min;
        Vec3 max = bounds.Max;
        Vec3[] corners =
        {
            new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)
        };

        List<WpfPoint> projected = new();
        foreach (Vec3 corner in corners)
        {
            WpfPoint? point = ProjectWorldToViewport(corner);
            if (point.HasValue)
                projected.Add(point.Value);
        }

        if (projected.Count == 0)
            return null;

        double minX = projected.Min(p => p.X);
        double minY = projected.Min(p => p.Y);
        double maxX = projected.Max(p => p.X);
        double maxY = projected.Max(p => p.Y);
        return new Rect(minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY));
    }

    private WpfPoint? ProjectWorldToViewport(Vec3 worldPoint)
    {
        if (viewport.Camera is not PerspectiveCamera camera)
            return null;

        double width = Math.Max(1.0, viewport.ActualWidth);
        double height = Math.Max(1.0, viewport.ActualHeight);

        Vec3 position = new(camera.Position.X, camera.Position.Y, camera.Position.Z);
        Vec3 forward = new(camera.LookDirection.X, camera.LookDirection.Y, camera.LookDirection.Z);
        Vec3 up = new(camera.UpDirection.X, camera.UpDirection.Y, camera.UpDirection.Z);
        if (forward.Length() <= 1e-9)
            return null;
        forward = forward.Normalize();
        up = up.Length() <= 1e-9 ? new Vec3(0, 1, 0) : up.Normalize();
        Vec3 right = forward.Cross(up).Normalize();
        if (right.Length() <= 1e-9)
            right = new Vec3(1, 0, 0);
        up = right.Cross(forward).Normalize();

        Vec3 rel = worldPoint - position;
        double z = rel.Dot(forward);
        if (z <= Math.Max(0.001, camera.NearPlaneDistance))
            return null;

        double x = rel.Dot(right);
        double y = rel.Dot(up);
        double fovRadians = Math.Clamp(camera.FieldOfView, 1.0, 160.0) * Math.PI / 180.0;
        double tanHalfFov = Math.Tan(fovRadians * 0.5);
        double aspect = width / height;
        double ndcX = x / (z * tanHalfFov * aspect);
        double ndcY = y / (z * tanHalfFov);

        if (!double.IsFinite(ndcX) || !double.IsFinite(ndcY))
            return null;

        return new WpfPoint((ndcX + 1.0) * 0.5 * width, (1.0 - ndcY) * 0.5 * height);
    }

    private static bool PointInPolygon(WpfPoint point, IList<WpfPoint> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            WpfPoint pi = polygon[i];
            WpfPoint pj = polygon[j];
            bool intersects = ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                point.X < (pj.X - pi.X) * (point.Y - pi.Y) / Math.Max(1e-9, pj.Y - pi.Y) + pi.X;
            if (intersects)
                inside = !inside;
        }
        return inside;
    }

    private static double SquaredDistance(WpfPoint a, WpfPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static ViewportSelectionCombineMode GetSelectionCombineMode()
    {
        WpfModifierKeys modifiers = WpfKeyboard.Modifiers;
        if ((modifiers & WpfModifierKeys.Control) != 0)
            return ViewportSelectionCombineMode.Toggle;
        if ((modifiers & WpfModifierKeys.Alt) != 0)
            return ViewportSelectionCombineMode.Subtract;
        if ((modifiers & WpfModifierKeys.Shift) != 0)
            return ViewportSelectionCombineMode.Add;
        return ViewportSelectionCombineMode.Replace;
    }
}
