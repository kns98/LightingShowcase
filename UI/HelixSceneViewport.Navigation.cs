// -----------------------------------------------------------------------------
// File: UI/HelixSceneViewport.Navigation.cs
// Purpose: Handles mouse, keyboard, trackpad, camera, and right-click editing behavior.
// -----------------------------------------------------------------------------

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace LightingShowcase.UI;

public sealed partial class HelixSceneViewport
{
    /// <summary>Frames the clicked object/scene on double-click, matching common 3D editor behavior.</summary>
    private void OnViewportMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (syncedScene == null)
            return;

        WpfPoint point = e.GetPosition(viewport.Viewport);
        if (TryHitLight(point, out string lightId))
        {
            LightPicked?.Invoke(lightId);
            FrameLight(lightId);
            e.Handled = true;
            return;
        }

        HitTestResult? result = VisualTreeHelper.HitTest(viewport.Viewport, point);
        if (result is RayMeshGeometry3DHitTestResult meshHit && meshHit.ModelHit is GeometryModel3D model &&
            modelToGroupId.TryGetValue(model, out int groupId))
        {
            GroupPicked?.Invoke(groupId);
            selectedGroupId = groupId;
            Aabb? bounds = syncedScene.GroupById(groupId)?.GetWorldBounds();
            if (bounds != null)
                LookAtBounds(bounds.Value);
            e.Handled = true;
            return;
        }

        FrameSelectionOrScene(syncedScene);
        e.Handled = true;
    }

    /// <summary>Returns the scene object under the pointer, if any.</summary>
    private bool TryHitScene(WpfPoint point, out int groupId)
    {
        groupId = -1;
        HitTestResult? result = VisualTreeHelper.HitTest(viewport.Viewport, point);
        if (result is RayMeshGeometry3DHitTestResult meshHit &&
            meshHit.ModelHit is GeometryModel3D model &&
            modelToGroupId.TryGetValue(model, out int foundGroupId))
        {
            groupId = foundGroupId;
            return true;
        }

        return false;
    }

    /// <summary>Shows a right-click editor menu without stealing the right-drag orbit gesture.</summary>
    private void ShowViewportContextMenu(WpfPoint point)
    {
        if (TryHitLight(point, out string lightId))
        {
            LightPicked?.Invoke(lightId);
            ContextMenu lightMenu = new();
            AddContextItem(lightMenu, "Frame Light", () => FrameLight(lightId));
            AddContextItem(lightMenu, "Select/Edit Tool", () => ContextToolRequested?.Invoke(ViewportNavigationTool.SelectEdit));
            gizmoViewport.ContextMenu = lightMenu;
            lightMenu.PlacementTarget = gizmoViewport;
            lightMenu.IsOpen = true;
            return;
        }

        bool hasGroup = TryHitScene(point, out int groupId);
        if (hasGroup)
            GroupPicked?.Invoke(groupId);

        ContextMenu menu = new();
        if (hasGroup)
        {
            AddContextItem(menu, "Frame Selection", () => ContextFrameRequested?.Invoke(groupId));
            AddContextItem(menu, "Convert to Light", () => ContextConvertToLightRequested?.Invoke(groupId));
            AddContextItem(menu, "Duplicate", () => ContextDuplicateRequested?.Invoke(groupId));
            AddContextItem(menu, "Delete", () => ContextDeleteRequested?.Invoke(groupId));
            menu.Items.Add(new Separator());
        }
        else
        {
            AddContextItem(menu, "Frame Scene", () =>
            {
                if (syncedScene != null)
                    FrameSelectionOrScene(syncedScene);
            });
            menu.Items.Add(new Separator());
        }

        AddContextItem(menu, "Select/Edit Tool", () => ContextToolRequested?.Invoke(ViewportNavigationTool.SelectEdit));
        AddContextItem(menu, "Rectangle Select Tool", () => ContextToolRequested?.Invoke(ViewportNavigationTool.RectangleSelect));
        AddContextItem(menu, "Lasso Select Tool", () => ContextToolRequested?.Invoke(ViewportNavigationTool.LassoSelect));
        AddContextItem(menu, "Orbit Tool", () => ContextToolRequested?.Invoke(ViewportNavigationTool.Orbit));
        AddContextItem(menu, "Pan Tool", () => ContextToolRequested?.Invoke(ViewportNavigationTool.Pan));

        gizmoViewport.ContextMenu = menu;
        menu.PlacementTarget = gizmoViewport;
        menu.IsOpen = true;
    }

    private static void AddContextItem(ContextMenu menu, string header, Action action)
    {
        MenuItem item = new() { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    /// <summary>Provides game/editor-style keyboard navigation while the Helix viewport has focus.</summary>
    private void OnViewportKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (viewport.Camera is not PerspectiveCamera camera)
            return;

        double step = GetKeyboardMoveStep();
        // Because this class derives from WinForms UserControl, the unqualified name
        // "ModifierKeys" resolves to Control.ModifierKeys instead of the WPF enum.
        // Fully qualify both WPF types so this code compiles reliably.
        System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.Keyboard.Modifiers;
        if ((modifiers & System.Windows.Input.ModifierKeys.Shift) != 0) step *= FastMoveMultiplier;
        if ((modifiers & System.Windows.Input.ModifierKeys.Control) != 0) step *= SlowMoveMultiplier;

        bool handled = true;
        switch (e.Key)
        {
            case Key.W: MoveCameraLocal(camera, 0, 0, step); break;
            case Key.S: MoveCameraLocal(camera, 0, 0, -step); break;
            case Key.A: MoveCameraLocal(camera, -step, 0, 0); break;
            case Key.D: MoveCameraLocal(camera, step, 0, 0); break;
            case Key.E: MoveCameraLocal(camera, 0, step, 0); break;
            case Key.Q: MoveCameraLocal(camera, 0, -step, 0); break;
            case Key.F:
                if (syncedScene != null) FrameSelectionOrScene(syncedScene);
                break;
            case Key.R:
                if (syncedScene != null) ResetToComfortableView(syncedScene);
                break;
            case Key.D1:
                if (syncedScene != null) SetStandardView(syncedScene, "front");
                break;
            case Key.D2:
                if (syncedScene != null) SetStandardView(syncedScene, "right");
                break;
            case Key.D3:
                if (syncedScene != null) SetStandardView(syncedScene, "top");
                break;
            case Key.Escape:
                if (marqueeDrag != null)
                    CancelMarqueeSelection();
                else
                    EmptySpacePicked?.Invoke();
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            UpdateGizmoScale();
            e.Handled = true;
        }
    }

    /// <summary>Handles selection, gizmo capture, and explicit Orbit/Pan tool capture on mouse down.</summary>
    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        gizmoViewport.Focus();
        WpfPoint point = e.GetPosition(viewport.Viewport);

        if (e.ChangedButton == MouseButton.Right)
        {
            // A still right-click opens the editing menu; a right-drag still orbits.
            StartNavigationDrag(point, TrackpadNavigationOperation.Orbit, waitForDragThreshold: true);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            StartNavigationDrag(point, TrackpadNavigationOperation.Pan);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && (navigationTool == ViewportNavigationTool.RectangleSelect || navigationTool == ViewportNavigationTool.LassoSelect))
        {
            BeginMarqueeSelection(point, isLasso: navigationTool == ViewportNavigationTool.LassoSelect);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && navigationTool != ViewportNavigationTool.SelectEdit)
        {
            StartNavigationDrag(point, navigationTool == ViewportNavigationTool.Pan
                ? TrackpadNavigationOperation.Pan
                : TrackpadNavigationOperation.Orbit);
            e.Handled = true;
            return;
        }

        if (TryHitGizmo(point, out GizmoHandle handle))
        {
            Vector screenAxis = ProjectAxisToScreen(handle.Direction, point);
            drag = new DragState(handle, point, screenAxis);
            gizmoViewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (TryHitLight(point, out string lightId))
        {
            LightPicked?.Invoke(lightId);
            e.Handled = true;
            return;
        }

        if (TryHitScene(point, out int groupId))
        {
            GroupPicked?.Invoke(groupId);
            e.Handled = true;
            return;
        }
        else if (navigationMode == ViewportNavigationMode.Mouse)
        {
            BeginMarqueeSelection(point, isLasso: false);
            e.Handled = true;
            return;
        }

        if (navigationMode == ViewportNavigationMode.Trackpad && e.ChangedButton == MouseButton.Left)
        {
            StartNavigationDrag(point, GetTrackpadOperation());
            e.Handled = true;
            return;
        }
    }

    /// <summary>Implements the on viewport mouse move operation for this file's subsystem.</summary>
    private void OnViewportMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (marqueeDrag != null)
        {
            UpdateMarqueeSelection(e.GetPosition(viewport.Viewport));
            e.Handled = true;
            return;
        }

        if (drag != null && selectedGroupId > 0)
        {
            DragState currentDrag = drag.Value;
            WpfPoint point = e.GetPosition(viewport.Viewport);
            Vector delta = point - currentDrag.LastMouse;
            if (delta.LengthSquared < 0.1)
                return;

            double amount;
            if (currentDrag.Handle.Operation == GizmoOperation.Rotate)
            {
                Vector tangent = new(-currentDrag.ScreenAxis.Y, currentDrag.ScreenAxis.X);
                if (tangent.LengthSquared < 0.0001)
                    tangent = new(1, 0);
                tangent.Normalize();
                amount = Vector.Multiply(delta, tangent) * 0.012;
            }
            else if (currentDrag.Handle.Operation == GizmoOperation.Scale)
            {
                amount = Vector.Multiply(delta, currentDrag.ScreenAxis) * 0.01;
            }
            else
            {
                amount = Vector.Multiply(delta, currentDrag.ScreenAxis) * gizmoWorldSize * 0.008;
            }

            if (Math.Abs(amount) > 0.0000001 && double.IsFinite(amount))
                GizmoDragged?.Invoke(new GizmoDelta(selectedGroupId, currentDrag.Handle.Operation, currentDrag.Handle.Axis, amount));

            drag = currentDrag with { LastMouse = point };
            e.Handled = true;
            return;
        }

        if (trackpadDrag != null)
        {
            TrackpadNavigationDrag currentDrag = trackpadDrag.Value;
            WpfPoint point = e.GetPosition(viewport.Viewport);
            Vector delta = point - currentDrag.LastMouse;
            if (!currentDrag.DragStarted && delta.LengthSquared < 16.0)
            {
                e.Handled = true;
                return;
            }

            if (delta.LengthSquared >= 0.1)
            {
                ApplyTrackpadNavigation(currentDrag.Operation, delta);
                trackpadDrag = currentDrag with
                {
                    LastMouse = point,
                    DragStarted = true,
                    Operation = navigationTool == ViewportNavigationTool.SelectEdit ? GetTrackpadOperation() : currentDrag.Operation
                };
            }
            e.Handled = true;
        }
    }

    /// <summary>Implements the on viewport mouse up operation for this file's subsystem.</summary>
    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (marqueeDrag != null)
        {
            CompleteMarqueeSelection();
            e.Handled = true;
            return;
        }

        if (drag != null)
        {
            drag = null;
            gizmoViewport.ReleaseMouseCapture();
            GizmoDragCompleted?.Invoke();
            e.Handled = true;
            return;
        }

        if (trackpadDrag != null)
        {
            TrackpadNavigationDrag currentDrag = trackpadDrag.Value;
            WpfPoint point = e.GetPosition(viewport.Viewport);
            trackpadDrag = null;
            gizmoViewport.ReleaseMouseCapture();

            if (e.ChangedButton == MouseButton.Right && !currentDrag.DragStarted)
                ShowViewportContextMenu(point);

            e.Handled = true;
        }
    }

    /// <summary>Handles gentle two-finger trackpad scroll zooming in trackpad mode.</summary>
    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (viewport.Camera is not PerspectiveCamera camera)
            return;

        double step = GetKeyboardMoveStep() * e.Delta * TrackpadWheelZoomFraction * zoomSensitivityMultiplier;
        MoveCameraLocal(camera, 0, 0, step);
        UpdateGizmoScale();
        e.Handled = true;
    }

    /// <summary>Begins a custom left-drag navigation operation and captures the mouse until button release.</summary>
    private void StartNavigationDrag(WpfPoint point, TrackpadNavigationOperation operation, bool waitForDragThreshold = false)
    {
        trackpadDrag = new TrackpadNavigationDrag(point, operation, !waitForDragThreshold);
        gizmoViewport.CaptureMouse();
    }

    /// <summary>Returns the current trackpad drag operation based on modifier keys.</summary>
    private static TrackpadNavigationOperation GetTrackpadOperation()
    {
        System.Windows.Input.ModifierKeys modifiers = System.Windows.Input.Keyboard.Modifiers;
        if ((modifiers & System.Windows.Input.ModifierKeys.Shift) != 0)
            return TrackpadNavigationOperation.Pan;
        if ((modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            return TrackpadNavigationOperation.Zoom;
        return TrackpadNavigationOperation.Orbit;
    }

    /// <summary>Applies one trackpad navigation delta to the Helix perspective camera.</summary>
    private void ApplyTrackpadNavigation(TrackpadNavigationOperation operation, Vector delta)
    {
        if (viewport.Camera is not PerspectiveCamera camera)
            return;

        double step = GetKeyboardMoveStep();
        switch (operation)
        {
            case TrackpadNavigationOperation.Pan:
                MoveCameraLocal(camera, -delta.X * step * TrackpadPanFractionPerPixel * panSensitivityMultiplier, delta.Y * step * TrackpadPanFractionPerPixel * panSensitivityMultiplier, 0);
                break;
            case TrackpadNavigationOperation.Zoom:
                MoveCameraLocal(camera, 0, 0, -delta.Y * step * TrackpadZoomFractionPerPixel * zoomSensitivityMultiplier);
                break;
            default:
                OrbitCameraAroundNavigationTarget(camera, delta);
                break;
        }

        UpdateGizmoScale();
    }

    /// <summary>Orbits the camera around the selected object or scene center for trackpad left-drag navigation.</summary>
    private void OrbitCameraAroundNavigationTarget(PerspectiveCamera camera, Vector delta)
    {
        Point3D target = GetNavigationTarget(camera);
        Vector3D fromTarget = camera.Position - target;
        if (fromTarget.LengthSquared < 0.000001)
            fromTarget = new Vector3D(0, 0, -GetKeyboardMoveStep() * 10.0);

        Vector3D up = camera.UpDirection;
        if (up.LengthSquared < 0.000001)
            up = new Vector3D(0, 1, 0);
        up.Normalize();

        Vector3D look = camera.LookDirection;
        if (look.LengthSquared < 0.000001)
            look = target - camera.Position;
        if (look.LengthSquared < 0.000001)
            look = new Vector3D(0, 0, 1);
        look.Normalize();

        Vector3D right = Vector3D.CrossProduct(look, up);
        if (right.LengthSquared < 0.000001)
            right = new Vector3D(1, 0, 0);
        right.Normalize();

        fromTarget = RotateVector(fromTarget, up, -delta.X * TrackpadOrbitRadiansPerPixel * orbitSensitivityMultiplier);
        fromTarget = RotateVector(fromTarget, right, -delta.Y * TrackpadOrbitRadiansPerPixel * orbitSensitivityMultiplier);
        Vector3D newUp = RotateVector(up, right, -delta.Y * TrackpadOrbitRadiansPerPixel * orbitSensitivityMultiplier);
        if (newUp.LengthSquared < 0.000001)
            newUp = new Vector3D(0, 1, 0);
        newUp.Normalize();

        camera.Position = target + fromTarget;
        camera.LookDirection = target - camera.Position;
        camera.UpDirection = newUp;
    }

    /// <summary>Chooses an orbit target from the selected object, whole scene, or current camera center.</summary>
    private Point3D GetNavigationTarget(PerspectiveCamera camera)
    {
        Aabb? bounds = null;
        if (syncedScene != null && selectedGroupId > 0)
            bounds = syncedScene.GroupById(selectedGroupId)?.GetWorldBounds();
        bounds ??= syncedScene?.GetSceneBounds();
        if (bounds != null)
            return ToPoint((bounds.Value.Min + bounds.Value.Max) * 0.5);

        Vector3D look = camera.LookDirection;
        if (look.LengthSquared < 0.000001)
            look = new Vector3D(0, 0, 1);
        look.Normalize();
        return camera.Position + look * 3.0;
    }

    /// <summary>Rotates a vector around an arbitrary axis using Rodrigues' rotation formula.</summary>
    private static Vector3D RotateVector(Vector3D vector, Vector3D axis, double radians)
    {
        if (axis.LengthSquared < 0.000001 || Math.Abs(radians) < 0.0000001)
            return vector;
        axis.Normalize();
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return vector * cos + Vector3D.CrossProduct(axis, vector) * sin + axis * Vector3D.DotProduct(axis, vector) * (1.0 - cos);
    }

    /// <summary>Moves the perspective camera in local editor-space coordinates.</summary>
    private static void MoveCameraLocal(PerspectiveCamera camera, double rightAmount, double upAmount, double forwardAmount)
    {
        Vector3D forward = camera.LookDirection;
        if (forward.LengthSquared < 0.000001)
            forward = new Vector3D(0, 0, 1);
        forward.Normalize();

        Vector3D up = camera.UpDirection;
        if (up.LengthSquared < 0.000001)
            up = new Vector3D(0, 1, 0);
        up.Normalize();

        Vector3D right = Vector3D.CrossProduct(forward, up);
        if (right.LengthSquared < 0.000001)
            right = new Vector3D(1, 0, 0);
        right.Normalize();

        Vector3D delta = right * rightAmount + up * upAmount + forward * forwardAmount;
        camera.Position += delta;
    }

    /// <summary>Calculates a keyboard navigation step from the current selection or scene size.</summary>
    private double GetKeyboardMoveStep()
    {
        Aabb? bounds = null;
        if (syncedScene != null && selectedGroupId > 0)
            bounds = syncedScene.GroupById(selectedGroupId)?.GetWorldBounds();
        bounds ??= syncedScene?.GetSceneBounds();

        double radius = bounds != null ? BoundsRadius(bounds.Value) : 2.0;
        return Math.Max(0.03, radius * KeyboardMoveFraction) * zoomSensitivityMultiplier;
    }

    /// <summary>Frames a bounding box using the current camera direction when possible.</summary>
    private void LookAtBounds(Aabb bounds)
    {
        if (viewport.Camera is not PerspectiveCamera camera)
            return;

        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        double radius = BoundsRadius(bounds);
        double fovRadians = Math.Clamp(camera.FieldOfView, 15.0, 90.0) * Math.PI / 180.0;
        double distance = Math.Max(radius * 1.35, radius / Math.Tan(fovRadians * 0.5) * 1.15);

        Vector3D viewDirection = camera.LookDirection;
        if (viewDirection.LengthSquared < 0.000001)
            viewDirection = new Vector3D(0.35, -0.18, 1.0);
        viewDirection.Normalize();

        Point3D target = ToPoint(center);
        Point3D position = target - viewDirection * distance;
        SetCameraLookAt(position, target);
    }

    /// <summary>Sets the Helix perspective camera to look at a target from a world position.</summary>
    private void SetCameraLookAt(Point3D position, Point3D target)
    {
        if (viewport.Camera is not PerspectiveCamera camera)
            return;

        Vector3D look = target - position;
        if (look.LengthSquared < 0.000001)
            look = new Vector3D(0, 0, 1);

        camera.Position = position;
        camera.LookDirection = look;
        camera.UpDirection = Math.Abs(Vector3D.DotProduct(Normalized(look), new Vector3D(0, 1, 0))) > 0.98
            ? new Vector3D(0, 0, -1)
            : new Vector3D(0, 1, 0);
        UpdateGizmoScale();
    }

    /// <summary>Returns a normalized vector without mutating the caller's local variable.</summary>
    private static Vector3D Normalized(Vector3D vector)
    {
        if (vector.LengthSquared < 0.000001)
            return new Vector3D(0, 0, 1);
        vector.Normalize();
        return vector;
    }

    /// <summary>Returns a conservative bounding sphere radius for framing and navigation.</summary>
    private static double BoundsRadius(Aabb bounds)
    {
        Vec3 size = bounds.Max - bounds.Min;
        return Math.Max(0.25, size.Length() * 0.5);
    }
}
