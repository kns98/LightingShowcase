// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.cs
// Purpose: Main form shell.
//
// Owns long-lived application services, shared UI controls, shared editor state, and startup wiring. Behavior is split into partial files.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using LightingShowcase.UI;
using LightingShowcase.UndoRedo;

namespace LightingShowcase;

/// <summary>WinForms editor shell for the lighting showcase; behavior is composed from partial files by subsystem.</summary>
public sealed partial class LightingShowcaseForm : Form
{
    // Core domain model. These objects exist for the lifetime of the form and are
    // shared by all partial files. UI handlers mutate these models, then mark the
    // Helix viewport and/or raytrace preview dirty so they are redrawn safely.
    private readonly Scene scene = new();
    private readonly SceneDocument sceneDocument;
    private readonly SceneImportExportService sceneFiles;
    private readonly LightingState lighting = new();
    private readonly CameraController camera = new();
    private readonly DemoCameraPath demoPath = new();

    // Input and frame-loop state. The timer drives editor updates at roughly
    // 60 FPS; the key set lets movement remain smooth while keys are held.
    private readonly HashSet<Keys> keys = new();
    private readonly System.Windows.Forms.Timer timer = new();

    // Main UI controls. The control panel owns named buttons/sliders/text boxes;
    // the viewport area hosts Helix and rendered output as sibling tabs.
    private readonly ControlPanel panel = new();
    private readonly Panel viewportPanel = new();
    private readonly TabControl viewportTabs = new();
    private readonly TabPage helixViewportTab = new("Helix");
    private readonly TabPage renderViewportTab = new("Render");
    private readonly Panel raytraceScrollPanel = new();
    private readonly PictureBox raytracePicture = new();
    private readonly Label editViewLabel = new();
    private readonly Label raytraceViewLabel = new();
    private readonly HelixSceneViewport helixViewport = new();

    // Embedded render tab controls. The rendered image is no longer hosted in a
    // floating window; it lives beside the Helix tab so orbit/edit/render stays in one workspace.
    private readonly Panel renderFloatingToolbar = new();
    private readonly Button renderSaveButton = new();
    private readonly Button renderCloseButton = new();

    // Mouse state for the embedded Shadow Raster Preview tab. This is
    // intentionally separate from the main form mouse state because the preview
    // tab has its own PictureBox and should orbit/pan while the main editor
    // remains responsive.
    private bool rasterPreviewMouseDragging = false;
    private MouseButtons rasterPreviewMouseButton = MouseButtons.None;
    private int rasterPreviewLastMouseX = 0;
    private int rasterPreviewLastMouseY = 0;
    private ShadowRasterRenderer.PreviewCache? shadowRasterPreviewCache = null;
    private int shadowRasterPreviewCacheContentRevision = -1;
    private int shadowRasterContentRevision = 0;
    private DateTime lastShadowRasterPreviewStartUtc = DateTime.MinValue;
    private DateTime lastVulkanRasterPreviewStartUtc = DateTime.MinValue;
    private bool lastShadowRasterPreviewWasInteractive = false;

    // Online-3D-Viewer-style editor chrome around the Helix viewport. These
    // controls make the realtime editor feel more like a dedicated 3D viewer:
    // compact toolbar above, object list in the control panel, and a floating details overlay.
    private readonly Panel editorViewerShell = new();
    private readonly Panel editorToolbar = new();
    private readonly Panel editorMeshPanel = new();
    private readonly Panel editorDetailsPanel = new();
    private readonly Panel editorCenterPanel = new();
    private readonly FlowLayoutPanel editorToolbarButtons = new();
    private readonly ListView editorMeshList = new();
    private readonly Label editorTitleLabel = new();
    private readonly Label editorMeshHeadingLabel = new();
    private readonly Label editorDetailsHeadingLabel = new();
    private readonly Label editorVerticesLabel = new();
    private readonly Label editorTrianglesLabel = new();
    private readonly TextBox editorSizeXTextBox = new();
    private readonly TextBox editorSizeYTextBox = new();
    private readonly TextBox editorSizeZTextBox = new();
    private readonly TextBox editorPositionXTextBox = new();
    private readonly TextBox editorPositionYTextBox = new();
    private readonly TextBox editorPositionZTextBox = new();
    private readonly Label editorBoundsMinLabel = new();
    private readonly Label editorBoundsMaxLabel = new();
    private readonly Label editorPivotLabel = new();
    private readonly TextBox editorDeltaMoveXTextBox = new();
    private readonly TextBox editorDeltaMoveYTextBox = new();
    private readonly TextBox editorDeltaMoveZTextBox = new();
    private readonly TextBox editorDeltaRotateXTextBox = new();
    private readonly TextBox editorDeltaRotateYTextBox = new();
    private readonly TextBox editorDeltaRotateZTextBox = new();
    private readonly TextBox editorDeltaScaleXTextBox = new();
    private readonly TextBox editorDeltaScaleYTextBox = new();
    private readonly TextBox editorDeltaScaleZTextBox = new();
    private readonly Label editorKindLabel = new();
    private readonly Label editorPrimitiveParametersLabel = new();
    private readonly Panel editorPrimitiveParametersPanel = new();
    private readonly Dictionary<string, TextBox> editorPrimitiveParameterTextBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Label editorSelectionHintLabel = new();
    private readonly Button editorDetailsCloseButton = new();
    private readonly Button editorDetailsToggleButton = new();
    private readonly Button editorDetailsApplyButton = new();
    private bool refreshingEditorMeshList = false;
    private bool refreshingEditorDetailsFields = false;
    private const string InspectorProgrammaticTextTag = "programmatic";
    private const string InspectorDirtyTextTag = "dirty";

    // Raytrace rendering state. The renderer works on background tasks; revision
    // numbers and cancellation tokens prevent obsolete renders from publishing
    // after the scene or camera has already changed.
    private FrameRenderer renderer = null!;
    private Bitmap? frame;
    private double renderScale = 0.50;
    private int pathBounceCount = 0;
    private RenderBackend renderBackend = RenderBackend.ShadowRasterPreview;
    // 0 means Auto: use the renderer's existing adaptive sample count.
    // -1 means Unlimited: continue until the render is cancelled or dirtied.
    private int maxAccumulationSamples = 0;
    private bool renderDirty = true;
    private bool raytraceInProgress = false;
    private CancellationTokenSource? renderCancellation;
    private int renderRevision = 0;
    private bool renderTargetResizeQueued = false;
    private Size raytraceRenderBaseSize = Size.Empty;
    private DateTime lastRenderDirtyUtc = DateTime.UtcNow;
    private Vec3 lastRenderedCameraPosition = new(double.NaN, double.NaN, double.NaN);
    private Vec3 lastRenderedCameraForward = new(double.NaN, double.NaN, double.NaN);

    // Camera/playback state. The form can either play the demo path or let the
    // user control the camera manually. Helix camera polling is throttled to avoid
    // dirtying the raytracer on every tiny viewport event.
    private double demoTime = 0.0;
    private const double DemoDuration = 14.0;
    private bool demoPlaying = true, useDemoCamera = true, dragging;
    private int lastMouseX, lastMouseY;
    private DateTime previousTime = DateTime.UtcNow;
    private DateTime lastHelixCameraSampleUtc = DateTime.MinValue;

    // Scene/editor state. selectedGroupId uses -1 to mean no selection. Dirty flags
    // allow expensive viewport sync and raytracing to be deferred until needed.
    private string lastLoadMessage = "Open .obj/.xml, or insert a .obj into the current scene";
    private int selectedGroupId = -1;
    private readonly HashSet<int> selectedGroupIds = new();
    private bool helixSceneDirty = true;
    private bool renderTabResizing = false;
    private bool loadingScene = false;
    private int selectedTimelineIndex = -1;
    private ViewportNavigationTool currentNavigationTool = ViewportNavigationTool.SelectEdit;

    // Undo/redo history. Snapshots are deep copies of scene objects and lights.
    // The history service keeps stack management out of UI code and caps memory
    // growth when repeatedly editing large imported models.
    private readonly SceneHistoryService sceneHistory = new(maxSnapshots: 40);
    private bool gizmoUndoCaptured = false;

    /// <summary>Constructs and initializes this component.</summary>
    public LightingShowcaseForm(string? initialObjPath = null)
    {
        sceneDocument = new SceneDocument(scene);
        sceneFiles = new SceneImportExportService(scene);

        Text = "Ray-Traced Room Lighting Showcase - C#";
        Width = 1180; Height = 720; DoubleBuffered = true; KeyPreview = true; BackColor = Color.Black;
        scene.Build();
        renderer = new FrameRenderer(new RayTracer(scene, lighting));
        ConfigureLayout();
        PopulateReadyMadeObjects();
        PopulateMaterialLibrary();
        WireUi();
        UpdateSelectionUi();
        UpdateCameraUi();
        RefreshTimelineList(selectFirst: true);
        SyncLightControls();
        RefreshLightList();
        UpdateHistoryUi();
        ApplyNavigationSensitivity();
        SelectNavigationTool(ViewportNavigationTool.SelectEdit);
        ResizeRenderTarget(forceShrinkToViewport: true);

        timer.Interval = 16;
        timer.Tick += OnTick;
        timer.Start();

        Resize += (_, _) => ResizeViewportArea();
        Paint += OnPaint;
        KeyDown += OnKeyDown;
        KeyUp += (_, e) => keys.Remove(e.KeyCode);
        MouseDown += OnMouseDown;
        MouseUp += (_, _) => dragging = false;
        MouseMove += OnMouseMove;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        if (!string.IsNullOrWhiteSpace(initialObjPath))
            TryOpenModel(initialObjPath, recordUndo: false);
    }
}
