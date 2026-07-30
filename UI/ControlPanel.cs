// -----------------------------------------------------------------------------
// File: UI/ControlPanel.cs
// Purpose: Tabbed editor control panel.
//
// Builds the left-side control panel and exposes strongly named controls to the
// main form. The panel uses tabs so file commands, navigation, selection,
// render/light controls, timeline editing, and status/progress feedback are not
// crowded into one very tall scrolling page.
// -----------------------------------------------------------------------------

namespace LightingShowcase.UI;

/// <summary>
/// Left-side WinForms control panel containing editor, render, camera, timeline,
/// and status controls. Public controls are exposed so the main form can wire
/// events while this class remains responsible for layout only.
/// </summary>
public sealed partial class ControlPanel : Panel
{
    private static readonly Font ButtonFont = new(SystemFonts.MessageBoxFont.FontFamily, 10.0f, FontStyle.Bold);
    private static readonly Font LabelFont = new(SystemFonts.MessageBoxFont.FontFamily, 9.0f, FontStyle.Regular);
    private static readonly Font HeadingFont = new(SystemFonts.MessageBoxFont.FontFamily, 9.0f, FontStyle.Bold);
    private const int ButtonHeight = 34;
    private const int TextBoxHeight = 26;

    // The tab host is public for future UI code that may want to switch to a
    // specific area after an operation, for example opening the Selection tab
    // after an object is picked.
    public TabControl Tabs { get; } = new();

    // Playback and camera mode controls. These switch between the automated
    // camera timeline and manual navigation.
    public Button PlayPauseButton { get; } = new();
    public Button RestartButton { get; } = new();
    public Button ManualButton { get; } = new();
    public Button DemoButton { get; } = new();

    // File/object commands. The main form wires these to OBJ/XML load/save,
    // built-in object insertion, scene clear, undo/redo, and view switching.
    public Button InsertObjButton { get; } = new();
    public Button OpenFileButton { get; } = new();
    public Button OpenSimplifiedButton { get; } = new();
    public TextBox OpenSimplifiedKeepPercentBox { get; } = new();
    public Button ClearSceneButton { get; } = new();
    public Button SaveSceneButton { get; } = new();
    public Button UndoButton { get; } = new();
    public Button RedoButton { get; } = new();
    public Button RealtimeViewButton { get; } = new();
    public Button RaytraceViewButton { get; } = new();
    public CheckBox TrackpadNavigationBox { get; } = new();
    public Button SelectNavigationButton { get; } = new();
    public Button OrbitNavigationButton { get; } = new();
    public Button PanNavigationButton { get; } = new();
    public TrackBar OrbitSensitivityTrackBar { get; } = new();
    public TrackBar PanSensitivityTrackBar { get; } = new();
    public TrackBar ZoomSensitivityTrackBar { get; } = new();
    public Label OrbitSensitivityLabel { get; } = new();
    public Label PanSensitivityLabel { get; } = new();
    public Label ZoomSensitivityLabel { get; } = new();
    public Button FrontViewButton { get; } = new();
    public Button RightViewButton { get; } = new();
    public Button TopViewButton { get; } = new();
    public Button ResetViewButton { get; } = new();
    public ComboBox ReadyMadeObjectComboBox { get; } = new();
    public Button InsertReadyMadeButton { get; } = new();
    public ListView ObjectListView { get; } = new();
    public Button ShowSelectedObjectsButton { get; } = new();
    public Button HideSelectedObjectsButton { get; } = new();
    public Button ShowAllObjectsButton { get; } = new();

    // Render and status controls. ScaleComboBox controls the raytrace preview
    // resolution; status/progress controls provide feedback for long imports.
    public ComboBox ScaleComboBox { get; } = new();
    public ComboBox RenderBackendComboBox { get; } = new();
    public ComboBox BounceComboBox { get; } = new();
    public ComboBox MaxSamplesComboBox { get; } = new();
    public Label StatusLabel { get; } = new();
    public Label LoadingLabel { get; } = new();
    public ProgressBar LoadingProgressBar { get; } = new();

    // Selection and material controls. These reflect the currently selected
    // SceneObjectGroup and let the user edit transforms, color, and texture.
    public Label SelectionLabel { get; } = new();
    public Label TextureInfoLabel { get; } = new();
    public PictureBox TexturePreviewBox { get; } = new();
    public TextBox MoveXBox { get; } = new();
    public TextBox MoveYBox { get; } = new();
    public TextBox MoveZBox { get; } = new();
    public TextBox RotXBox { get; } = new();
    public TextBox RotYBox { get; } = new();
    public TextBox RotZBox { get; } = new();
    public TextBox ScaleXBox { get; } = new();
    public TextBox ScaleYBox { get; } = new();
    public TextBox ScaleZBox { get; } = new();
    public Button ApplyMoveButton { get; } = new();
    public Button ApplyRotateButton { get; } = new();
    public Button ApplyScaleButton { get; } = new();
    public Button DeleteSelectionButton { get; } = new();
    public Button DuplicateSelectionButton { get; } = new();
    public Button GroupSelectionButton { get; } = new();
    public Button UngroupSelectionButton { get; } = new();
    public Button ColorSelectionButton { get; } = new();
    public Button TextureSelectionButton { get; } = new();
    public Button SampleTextureSelectionButton { get; } = new();
    public Button ClearTextureSelectionButton { get; } = new();
    public TextBox TextureTileSizeBox { get; } = new();
    public Button RetileTextureButton { get; } = new();
    public TextBox SimplifyKeepPercentBox { get; } = new();
    public Button SimplifySelectionButton { get; } = new();
    public CheckBox ReferenceCameraBox { get; } = new();

    // glTF/PBR material controls. These edit the material properties imported
    // from glTF files and consumed by the raytracer/material preview.
    public Label MaterialInfoLabel { get; } = new();
    public ComboBox MaterialLibraryComboBox { get; } = new();
    public Button ApplyMaterialPresetButton { get; } = new();
    public Label MaterialPresetInfoLabel { get; } = new();
    public TextBox MaterialAlphaBox { get; } = new();
    public TextBox MaterialTransmissionBox { get; } = new();
    public TextBox MaterialMetallicBox { get; } = new();
    public TextBox MaterialRoughnessBox { get; } = new();
    public TextBox MaterialEmissionBox { get; } = new();
    public TextBox MaterialEmissionRBox { get; } = new();
    public TextBox MaterialEmissionGBox { get; } = new();
    public TextBox MaterialEmissionBBox { get; } = new();
    public CheckBox MaterialAlphaBlendBox { get; } = new();
    public CheckBox MaterialUseBaseTextureBox { get; } = new();
    public CheckBox MaterialUseEmissiveTextureBox { get; } = new();
    public CheckBox MaterialUseMetallicRoughnessTextureBox { get; } = new();
    public CheckBox MaterialUseNormalTextureBox { get; } = new();
    public Button ApplyMaterialPropertiesButton { get; } = new();

    // Lighting controls. The editor lists every SceneLight and exposes the
    // actual light properties used by both imported glTF lights and default lights.
    public ListBox LightListBox { get; } = new();
    public TextBox LightIdBox { get; } = new();
    public TextBox LightPosXBox { get; } = new();
    public TextBox LightPosYBox { get; } = new();
    public TextBox LightPosZBox { get; } = new();
    public TextBox LightColorRBox { get; } = new();
    public TextBox LightColorGBox { get; } = new();
    public TextBox LightColorBBox { get; } = new();
    public TextBox LightIntensityBox { get; } = new();
    public ComboBox LightKindComboBox { get; } = new();
    public TextBox LightDirXBox { get; } = new();
    public TextBox LightDirYBox { get; } = new();
    public TextBox LightDirZBox { get; } = new();
    public TextBox LightRangeBox { get; } = new();
    public TextBox LightInnerConeBox { get; } = new();
    public TextBox LightOuterConeBox { get; } = new();
    public CheckBox LightEnabledBox { get; } = new();
    public Button AddLightButton { get; } = new();
    public Button ApplyLightButton { get; } = new();
    public Button RemoveLightButton { get; } = new();
    public Button ConvertSelectionToLightButton { get; } = new();

    // Camera timeline controls. These edit DemoCameraPath keyframes without
    // directly touching the raytracer. The form marks rendering dirty afterward.
    public ListBox TimelineListBox { get; } = new();
    public TextBox TimelineTimeBox { get; } = new();
    public TextBox TimelinePosXBox { get; } = new();
    public TextBox TimelinePosYBox { get; } = new();
    public TextBox TimelinePosZBox { get; } = new();
    public TextBox TimelineTargetXBox { get; } = new();
    public TextBox TimelineTargetYBox { get; } = new();
    public TextBox TimelineTargetZBox { get; } = new();
    public Button TimelineApplyButton { get; } = new();
    public Button TimelineAddCurrentButton { get; } = new();
    public Button TimelineDeleteButton { get; } = new();
    public Button TimelinePreviewButton { get; } = new();

    /// <summary>Constructs the tabbed control panel and all child controls.</summary>
    public ControlPanel()
    {
        Left = 14; Top = 14; Width = 520; Height = 820;
        BackColor = Color.FromArgb(185, 0, 0, 0);
        Font = LabelFont;
        AutoScroll = false;

        AddTitle();
        AddHelp();
        AddTabs();
    }

}
