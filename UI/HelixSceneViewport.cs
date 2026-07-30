// -----------------------------------------------------------------------------
// File: UI/HelixSceneViewport.cs
// Purpose: Interactive 3D viewport.
//
// Wraps HelixToolkit/WPF inside WinForms, displays editable meshes, selection bounds, and transform gizmos.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using FormsControl = System.Windows.Forms.UserControl;
using FormsDockStyle = System.Windows.Forms.DockStyle;
using WpfGrid = System.Windows.Controls.Grid;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfMaterial = System.Windows.Media.Media3D.Material;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfPolyline = System.Windows.Shapes.Polyline;
using SceneTriangle = LightingShowcase.SceneGraph.Triangle;

namespace LightingShowcase.UI;

/// <summary>Transform operation currently represented by the viewport gizmo.</summary>
public enum GizmoOperation
{
    Move,
    Rotate,
    Scale
}

public enum GizmoAxis
{
    X,
    Y,
    Z
}

/// <summary>Navigation behavior profile for the Helix edit viewport.</summary>
public enum ViewportNavigationMode
{
    /// <summary>Desktop mouse mode: Helix keeps its normal right/middle/wheel navigation gestures.</summary>
    Mouse,

    /// <summary>Trackpad mode: left-drag orbits, Shift-drag pans, Ctrl-drag dollies, and two-finger scroll zooms gently.</summary>
    Trackpad
}

/// <summary>Explicit left-drag navigation tool selected from the WinForms control panel.</summary>
public enum ViewportNavigationTool
{
    /// <summary>Normal editor behavior: click objects to select them and drag gizmo handles to transform them.</summary>
    SelectEdit,

    /// <summary>Left-drag anywhere in the Helix viewport to orbit around the selected object or scene.</summary>
    Orbit,

    /// <summary>Left-drag anywhere in the Helix viewport to pan the camera.</summary>
    Pan,

    /// <summary>Left-drag a rectangular marquee; left-to-right contains, right-to-left crossing.</summary>
    RectangleSelect,

    /// <summary>Left-drag a freehand lasso to select objects by their projected bounds.</summary>
    LassoSelect
}

public readonly record struct GizmoDelta(int GroupId, GizmoOperation Operation, GizmoAxis Axis, double Amount);

/// <summary>
/// Hosts a Helix Toolkit realtime viewport inside the WinForms application.
/// The application's own Scene remains the source of truth; this class only mirrors
/// the current triangle groups for fast rasterized editing/inspection.
/// </summary>
/// <summary>WinForms-hosted WPF/Helix viewport for interactive editing and transform gizmos.</summary>
public sealed partial class HelixSceneViewport : FormsControl
{
    private readonly ElementHost host = new();
    private readonly WpfGrid viewportHost = new();
    private readonly HelixViewport3D viewport = new();
    private readonly HelixViewport3D gizmoViewport = new();
    private readonly WpfCanvas selectionOverlay = new();
    private readonly WpfRectangle marqueeRectangle = new();
    private readonly WpfPolyline lassoPolyline = new();
    private readonly Model3DGroup root = new();
    private readonly Model3DGroup rasterLightRoot = new();
    private readonly Model3DGroup lightRoot = new();
    private readonly Model3DGroup gizmoRoot = new();
    private readonly ModelVisual3D modelVisual;
    private readonly ModelVisual3D rasterLightVisual;
    private readonly ModelVisual3D lightVisual;
    private readonly ModelVisual3D gizmoVisual;
    private readonly Dictionary<GeometryModel3D, int> modelToGroupId = new();
    private readonly Dictionary<int, List<GeometryModel3D>> groupToModels = new();
    private readonly Dictionary<GeometryModel3D, WpfMaterial> modelBaseMaterials = new();
    private readonly Dictionary<GeometryModel3D, WpfMaterial> modelSelectedMaterials = new();
    private readonly Dictionary<GeometryModel3D, GizmoHandle> modelToGizmoHandle = new();
    private readonly Dictionary<GeometryModel3D, string> modelToLightId = new();
    private int selectedGroupId = -1;
    private readonly HashSet<int> selectedGroupIds = new();
    private Aabb? selectedBounds;
    private Point3D selectedCenter;
    private double gizmoWorldSize = 1.0;
    private DragState? drag;
    private TrackpadNavigationDrag? trackpadDrag;
    private MarqueeSelectionDrag? marqueeDrag;
    private Scene? syncedScene;
    private string? selectedLightId;
    private ViewportNavigationMode navigationMode = ViewportNavigationMode.Mouse;
    private ViewportNavigationTool navigationTool = ViewportNavigationTool.SelectEdit;

    // Navigation tuning for editor-like movement in the realtime viewport.
    // These values scale against the current selection/scene size so movement
    // feels reasonable for both small props and large imported rooms.
    private const double KeyboardMoveFraction = 0.08;
    private const double FastMoveMultiplier = 4.0;
    private const double SlowMoveMultiplier = 0.25;
    private const double TrackpadOrbitRadiansPerPixel = 0.008;
    private const double TrackpadPanFractionPerPixel = 0.0048;
    private const double TrackpadZoomFractionPerPixel = 0.018;
    private const double TrackpadWheelZoomFraction = 0.0048;
    private double orbitSensitivityMultiplier = 1.25;
    private double panSensitivityMultiplier = 1.25;
    private double zoomSensitivityMultiplier = 1.50;

    public event Action<int>? GroupPicked;
    public event Action<IReadOnlyCollection<int>, ViewportSelectionCombineMode>? GroupsMarqueeSelected;
    public event Action? EmptySpacePicked;
    public event Action<GizmoDelta>? GizmoDragged;
    public event Action? GizmoDragCompleted;
    public event Action<string>? LightPicked;

    // Raised by the viewport's right-click context menu. The WinForms shell owns
    // scene mutations, so the WPF viewport only reports the requested edit.
    public event Action<int>? ContextDeleteRequested;
    public event Action<int>? ContextDuplicateRequested;
    public event Action<int>? ContextConvertToLightRequested;
    public event Action<int>? ContextFrameRequested;
    public event Action<ViewportNavigationTool>? ContextToolRequested;

    /// <summary>Constructs and initializes this component.</summary>
    public HelixSceneViewport()
    {
        Dock = FormsDockStyle.Fill;
        host.Dock = FormsDockStyle.Fill;
        Controls.Add(host);

        PerspectiveCamera sharedCamera = new()
        {
            Position = new Point3D(0, 0, -6),
            LookDirection = new Vector3D(0, 0, 1),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 48
        };

        viewport.ShowFrameRate = false;
        viewport.ShowCoordinateSystem = true;
        viewport.ShowViewCube = true;
        viewport.Background = WpfBrushes.White;
        viewport.Camera = sharedCamera;

        // WPF 3D does not provide a simple per-object Z-order inside one
        // Viewport3D. Depth testing will still hide later-added objects when
        // scene geometry is closer to the camera. The gizmo is therefore drawn
        // in a transparent overlay viewport that shares the same camera.
        // The overlay receives mouse input first; this class therefore handles
        // selection, gizmo dragging, orbit, pan, and wheel zoom explicitly instead
        // of relying on Helix's built-in mouse gesture routing.
        gizmoViewport.ShowFrameRate = false;
        gizmoViewport.ShowCoordinateSystem = false;
        gizmoViewport.ShowViewCube = false;
        gizmoViewport.Background = WpfBrushes.Transparent;
        gizmoViewport.Camera = sharedCamera;
        gizmoViewport.IsHitTestVisible = true;
        gizmoViewport.Focusable = true;
        gizmoViewport.IsRotationEnabled = false;
        gizmoViewport.IsPanEnabled = false;
        gizmoViewport.IsZoomEnabled = false;

        gizmoViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(WpfColors.White) });
        modelVisual = new ModelVisual3D { Content = root };
        rasterLightVisual = new ModelVisual3D { Content = rasterLightRoot };
        lightVisual = new ModelVisual3D { Content = lightRoot };
        gizmoVisual = new ModelVisual3D { Content = gizmoRoot };
        viewport.Children.Add(rasterLightVisual);
        viewport.Children.Add(modelVisual);
        gizmoViewport.Children.Add(lightVisual);
        gizmoViewport.Children.Add(gizmoVisual);

        ConfigureSelectionOverlay();

        viewportHost.Children.Add(viewport);
        viewportHost.Children.Add(gizmoViewport);
        viewportHost.Children.Add(selectionOverlay);

        viewport.Focusable = true;
        gizmoViewport.MouseDown += OnViewportMouseDown;
        gizmoViewport.MouseMove += OnViewportMouseMove;
        gizmoViewport.MouseUp += OnViewportMouseUp;
        gizmoViewport.MouseWheel += OnViewportMouseWheel;
        gizmoViewport.MouseDoubleClick += OnViewportMouseDoubleClick;
        gizmoViewport.KeyDown += OnViewportKeyDown;
        host.Child = viewportHost;
    }

    /// <summary>Rebuilds the Helix viewport meshes from the current scene graph.</summary>
    public void SyncFromScene(Scene scene)
    {
        syncedScene = scene;
        root.Children.Clear();
        modelToGroupId.Clear();
        groupToModels.Clear();
        modelBaseMaterials.Clear();
        modelSelectedMaterials.Clear();
        selectedBounds = null;

        foreach (SceneObjectGroup group in scene.ObjectGroups)
        {
            List<GeometryModel3D> models = new();
            foreach (GeometryModel3D model in BuildModels(group, selected: false))
            {
                modelToGroupId[model] = group.Id;
                modelBaseMaterials[model] = model.Material;
                modelSelectedMaterials[model] = CreateSelectedViewportMaterial(model.Material);
                models.Add(model);
                root.Children.Add(model);
            }
            if (models.Count > 0)
                groupToModels[group.Id] = models;
        }

        ApplySelectionHighlightAndBounds();
        RebuildLightVisuals();
        RebuildGizmo();
        viewport.InvalidateVisual();
        gizmoViewport.InvalidateVisual();
    }

    /// <summary>Updates camera from the current application state.</summary>
    public void UpdateCamera(Vec3 position, CameraBasis basis)
    {
        if (viewport.Camera is not PerspectiveCamera camera)
            return;

        camera.Position = ToPoint(position);
        camera.LookDirection = ToVector(basis.Forward);
        camera.UpDirection = ToVector(basis.Up);
        UpdateGizmoScale();
        gizmoViewport.InvalidateVisual();
    }

    /// <summary>Implements the zoom extents operation for this file's subsystem.</summary>
    public void ZoomExtents()
    {
        viewport.ZoomExtents(250);
    }

    /// <summary>Switches between desktop mouse navigation and trackpad-friendly navigation.</summary>
    public void SetNavigationMode(ViewportNavigationMode mode)
    {
        navigationMode = mode;
        trackpadDrag = null;

        // Mouse mode deliberately lets HelixToolkit keep its default gesture model.
        // Trackpad mode uses custom left-drag handling because many trackpads do not
        // have comfortable middle-click or right-drag gestures.
        bool useHelixDefaults = navigationTool == ViewportNavigationTool.SelectEdit && mode == ViewportNavigationMode.Mouse;
        viewport.IsRotationEnabled = useHelixDefaults;
        viewport.IsPanEnabled = useHelixDefaults;
        viewport.IsZoomEnabled = useHelixDefaults || mode == ViewportNavigationMode.Mouse;
    }

    /// <summary>Sets the explicit left-drag navigation tool used by the Orbit/Pan/Select buttons.</summary>
    public void SetNavigationTool(ViewportNavigationTool tool)
    {
        navigationTool = tool;
        trackpadDrag = null;
        if (drag == null)
            gizmoViewport.ReleaseMouseCapture();

        // When an explicit navigation tool is active, Helix's own mouse navigation is
        // disabled so left-drag behavior is deterministic on mice and trackpads.
        bool useHelixDefaults = navigationTool == ViewportNavigationTool.SelectEdit && navigationMode == ViewportNavigationMode.Mouse;
        viewport.IsRotationEnabled = useHelixDefaults;
        viewport.IsPanEnabled = useHelixDefaults;
        viewport.IsZoomEnabled = useHelixDefaults || navigationMode == ViewportNavigationMode.Mouse;
    }


    /// <summary>Adjusts editor navigation sensitivity. A value of 1.0 means the built-in baseline speed.</summary>
    public void SetNavigationSensitivity(double orbitMultiplier, double panMultiplier, double zoomMultiplier)
    {
        orbitSensitivityMultiplier = ClampNavigationMultiplier(orbitMultiplier);
        panSensitivityMultiplier = ClampNavigationMultiplier(panMultiplier);
        zoomSensitivityMultiplier = ClampNavigationMultiplier(zoomMultiplier);
    }

    private static double ClampNavigationMultiplier(double value)
    {
        if (!double.IsFinite(value))
            return 1.0;
        return Math.Clamp(value, 0.25, 3.0);
    }

    /// <summary>Frames the selected object if one exists; otherwise frames the whole scene.</summary>
    public void FrameSelectionOrScene(Scene scene)
    {
        syncedScene = scene;
        Aabb? bounds = selectedGroupId > 0
            ? scene.GroupById(selectedGroupId)?.GetWorldBounds()
            : scene.GetSceneBounds();

        if (bounds == null)
        {
            ResetToComfortableView(scene);
            return;
        }

        LookAtBounds(bounds.Value);
    }

    /// <summary>Moves the editor camera to a predictable three-quarter view of the scene.</summary>
    public void ResetToComfortableView(Scene scene)
    {
        syncedScene = scene;
        Aabb? bounds = scene.GetSceneBounds();
        if (bounds == null)
        {
            SetCameraLookAt(new Point3D(0, 0.55, -2.25), new Point3D(0, 0.55, 0.0));
            return;
        }

        Vec3 center = (bounds.Value.Min + bounds.Value.Max) * 0.5;
        double radius = BoundsRadius(bounds.Value);
        Point3D target = ToPoint(center);
        Point3D position = new(target.X + radius * 0.75, target.Y + radius * 0.45, target.Z - radius * 2.25);
        SetCameraLookAt(position, target);
    }

    /// <summary>Sets a common orthogonal-ish view while keeping the current scene centered.</summary>
    public void SetStandardView(Scene scene, string viewName)
    {
        syncedScene = scene;
        Aabb? bounds = selectedGroupId > 0
            ? scene.GroupById(selectedGroupId)?.GetWorldBounds()
            : scene.GetSceneBounds();
        if (bounds == null)
            return;

        Vec3 center = (bounds.Value.Min + bounds.Value.Max) * 0.5;
        double radius = BoundsRadius(bounds.Value);
        Point3D target = ToPoint(center);
        Vector3D offset = viewName.ToLowerInvariant() switch
        {
            "front" => new Vector3D(0, 0, -radius * 2.25),
            "back" => new Vector3D(0, 0, radius * 2.25),
            "left" => new Vector3D(-radius * 2.25, 0, 0),
            "right" => new Vector3D(radius * 2.25, 0, 0),
            "top" => new Vector3D(0, radius * 2.25, 0.001),
            _ => new Vector3D(radius * 0.75, radius * 0.45, -radius * 2.25)
        };

        SetCameraLookAt(target + offset, target);
    }

    /// <summary>Attempts to get camera and reports failure without crashing the UI.</summary>
    public bool TryGetCamera(out Vec3 position, out Vec3 lookDirection, out Vec3 upDirection)
    {
        position = Vec3.Zero;
        lookDirection = new Vec3(0, 0, 1);
        upDirection = new Vec3(0, 1, 0);

        if (viewport.Camera is not PerspectiveCamera camera)
            return false;

        position = new Vec3(camera.Position.X, camera.Position.Y, camera.Position.Z);
        lookDirection = new Vec3(camera.LookDirection.X, camera.LookDirection.Y, camera.LookDirection.Z);
        upDirection = new Vec3(camera.UpDirection.X, camera.UpDirection.Y, camera.UpDirection.Z);
        return lookDirection.Length() > 0.000001;
    }

    /// <summary>Implements the select group operation for this file's subsystem.</summary>
    public void SelectGroup(int groupId, Scene scene)
    {
        selectedGroupIds.Clear();
        if (groupId > 0) selectedGroupIds.Add(groupId);
        selectedGroupId = groupId;
        UpdateSelectionOnly(scene);
    }

    /// <summary>Updates viewport highlighting for a multi-selection set without rebuilding large mesh buffers.</summary>
    public void SelectGroups(IEnumerable<int> groupIds, Scene scene)
    {
        selectedGroupIds.Clear();
        foreach (int groupId in groupIds.Where(id => id > 0).Distinct())
            selectedGroupIds.Add(groupId);
        selectedGroupId = selectedGroupIds.Count == 0 ? -1 : selectedGroupIds.Last();
        UpdateSelectionOnly(scene);
    }

    private void UpdateSelectionOnly(Scene scene)
    {
        syncedScene = scene;
        // If the viewport has not been populated yet, fall back to a complete sync.
        if (root.Children.Count == 0 && scene.ObjectGroups.Count > 0)
        {
            SyncFromScene(scene);
            return;
        }

        ApplySelectionHighlightAndBounds();
        RebuildLightVisuals();
        RebuildGizmo();
        viewport.InvalidateVisual();
        gizmoViewport.InvalidateVisual();
    }

    /// <summary>Selects one light marker in the Helix overlay without rebuilding scene mesh buffers.</summary>
    public void SelectLight(string? lightId, Scene scene)
    {
        syncedScene = scene;
        selectedLightId = string.IsNullOrWhiteSpace(lightId) ? null : lightId;
        RebuildLightVisuals();
        gizmoViewport.InvalidateVisual();
    }

    private void ApplySelectionHighlightAndBounds()
    {
        selectedBounds = null;

        foreach (KeyValuePair<GeometryModel3D, int> entry in modelToGroupId)
        {
            GeometryModel3D model = entry.Key;
            int groupId = entry.Value;
            bool selected = selectedGroupIds.Contains(groupId);
            WpfMaterial material = selected
                ? modelSelectedMaterials.GetValueOrDefault(model, model.Material)
                : modelBaseMaterials.GetValueOrDefault(model, model.Material);
            model.Material = material;
            model.BackMaterial = material;
        }

        if (syncedScene == null)
            return;

        foreach (int groupId in selectedGroupIds)
        {
            SceneObjectGroup? group = syncedScene.GroupById(groupId);
            if (group == null)
                continue;
            Aabb bounds = group.GetWorldBounds();
            selectedBounds = selectedBounds == null ? bounds : Aabb.Surrounding(selectedBounds.Value, bounds);
        }
    }

    private readonly record struct GizmoHandle(GizmoOperation Operation, GizmoAxis Axis, Vector3D Direction);
    private readonly record struct DragState(GizmoHandle Handle, WpfPoint LastMouse, Vector ScreenAxis);
    private readonly record struct TrackpadNavigationDrag(WpfPoint LastMouse, TrackpadNavigationOperation Operation, bool DragStarted);
    private sealed class MarqueeSelectionDrag
    {
        public required WpfPoint Start { get; init; }
        public WpfPoint Current { get; set; }
        public bool DragStarted { get; set; }
        public bool IsLasso { get; init; }
        public List<WpfPoint> LassoPoints { get; } = new();
    }
    private enum TrackpadNavigationOperation { Orbit, Pan, Zoom }
}

/// <summary>How a viewport marquee/lasso result should be merged with the current editor selection.</summary>
public enum ViewportSelectionCombineMode
{
    Replace,
    Add,
    Subtract,
    Toggle

}

