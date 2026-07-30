// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.Layout.cs
// Purpose: Main form layout and UI wiring.
//
// Builds the WinForms layout, places controls, configures split panes, and connects button/menu events to editor operations.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using System.Drawing;
using LightingShowcase.SceneGraph;
using LightingShowcase.UI;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Implements the populate ready made objects operation for this file's subsystem.</summary>
    private void PopulateReadyMadeObjects()
    {
        panel.ReadyMadeObjectComboBox.Items.AddRange(ObjectLibraryRegistry.Names);
        if (panel.ReadyMadeObjectComboBox.Items.Count > 0)
            panel.ReadyMadeObjectComboBox.SelectedIndex = 0;
    }


    /// <summary>Populates the material preset library combo box used by the Selection tab.</summary>
    private void PopulateMaterialLibrary()
    {
        panel.MaterialLibraryComboBox.Items.Clear();
        foreach (MaterialPreset preset in MaterialPresetLibrary.Common)
            panel.MaterialLibraryComboBox.Items.Add(preset);

        if (panel.MaterialLibraryComboBox.Items.Count > 0)
            panel.MaterialLibraryComboBox.SelectedIndex = 0;

        UpdateSelectedMaterialPresetInfo();
    }

    /// <summary>Creates and arranges the form layout and viewport containers.</summary>
    private void ConfigureLayout()
    {
        panel.Left = 14;
        panel.Top = 14;
        panel.Height = Math.Max(820, ClientSize.Height - 28);
        Controls.Add(panel);

        viewportPanel.Left = panel.Right + 14;
        viewportPanel.Top = 0;
        viewportPanel.Width = Math.Max(1, ClientSize.Width - viewportPanel.Left);
        viewportPanel.Height = Math.Max(1, ClientSize.Height);
        viewportPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        viewportPanel.BackColor = Color.Black;
        Controls.Add(viewportPanel);

        viewportTabs.Dock = DockStyle.Fill;
        viewportTabs.Alignment = TabAlignment.Top;
        viewportTabs.Appearance = TabAppearance.Normal;
        viewportTabs.ItemSize = new Size(120, 28);
        viewportTabs.Padding = new Point(14, 4);
        viewportTabs.BackColor = Color.Black;
        viewportTabs.TabPages.Add(helixViewportTab);
        viewportTabs.TabPages.Add(renderViewportTab);
        viewportTabs.SelectedTab = helixViewportTab;
        viewportTabs.SelectedIndexChanged += (_, _) =>
        {
            if (viewportTabs.SelectedTab == renderViewportTab)
            {
                ResizeRenderTarget(forceShrinkToViewport: true);
                QueueBackgroundRaytrace(force: true);
            }
            else
            {
                CancelActiveRender();
                raytraceInProgress = false;
            }
            UpdateStatus();
        };
        viewportPanel.Controls.Add(viewportTabs);

        helixViewportTab.BackColor = Color.Black;
        helixViewportTab.Padding = new Padding(0);
        renderViewportTab.BackColor = Color.Black;
        renderViewportTab.Padding = new Padding(0);

        ConfigureEditorViewerChrome();
        helixViewportTab.Controls.Add(editorViewerShell);

        ConfigureFloatingRenderWindow();

        raytraceScrollPanel.SizeChanged += (_, _) => QueueRenderTargetResize();

        raytracePicture.Dock = DockStyle.None;
        raytracePicture.Location = Point.Empty;
        raytracePicture.BackColor = Color.Black;
        raytracePicture.SizeMode = PictureBoxSizeMode.Normal;
        raytraceViewLabel.BringToFront();

        panel.BringToFront();
    }

    /// <summary>Implements the resize viewport area operation for this file's subsystem.</summary>
    private void ResizeViewportArea()
    {
        panel.Height = Math.Max(820, ClientSize.Height - 28);
        viewportPanel.Left = panel.Right + 14;
        viewportPanel.Top = 0;
        viewportPanel.Width = Math.Max(1, ClientSize.Width - viewportPanel.Left);
        viewportPanel.Height = Math.Max(1, ClientSize.Height);
        ResizeRenderTarget();
        helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
    }

    /// <summary>Activates one explicit Helix navigation tool and updates the button visual state.</summary>
    private void SelectNavigationTool(ViewportNavigationTool tool)
    {
        currentNavigationTool = tool;
        helixViewport.SetNavigationTool(tool);

        SetNavigationButtonState(panel.SelectNavigationButton, tool == ViewportNavigationTool.SelectEdit);
        SetNavigationButtonState(panel.OrbitNavigationButton, tool == ViewportNavigationTool.Orbit);
        SetNavigationButtonState(panel.PanNavigationButton, tool == ViewportNavigationTool.Pan);
    }


    /// <summary>Applies navigation sensitivity sliders to the Helix viewport and updates their live labels.</summary>
    private void ApplyNavigationSensitivity()
    {
        double orbit = panel.OrbitSensitivityTrackBar.Value / 100.0;
        double pan = panel.PanSensitivityTrackBar.Value / 100.0;
        double zoom = panel.ZoomSensitivityTrackBar.Value / 100.0;
        panel.OrbitSensitivityLabel.Text = panel.OrbitSensitivityTrackBar.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        panel.PanSensitivityLabel.Text = panel.PanSensitivityTrackBar.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        panel.ZoomSensitivityLabel.Text = panel.ZoomSensitivityTrackBar.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        helixViewport.SetNavigationSensitivity(orbit, pan, zoom);
    }

    /// <summary>Switches the edit viewport to a named standard view and marks preview rendering stale.</summary>
    private void SetEditorStandardView(string viewName)
    {
        EnterManualCameraMode();
        helixViewport.SetStandardView(scene, viewName);
        MarkRaytraceDirty();
        UpdateStatus();
    }

    /// <summary>Resets the edit viewport to a comfortable three-quarter scene view.</summary>
    private void ResetEditorView()
    {
        EnterManualCameraMode();
        helixViewport.ResetToComfortableView(scene);
        MarkRaytraceDirty();
        UpdateStatus();
    }

    /// <summary>Styles navigation tool buttons like simple toggles without introducing another custom control.</summary>
    private static void SetNavigationButtonState(Button button, bool active)
    {
        button.BackColor = active ? Color.FromArgb(80, 115, 170) : SystemColors.Control;
        button.ForeColor = active ? Color.White : SystemColors.ControlText;
    }

    /// <summary>Connects WinForms controls to their event handlers.</summary>
    private void WireUi()
    {
        panel.PlayPauseButton.Click += (_, _) => ToggleTimelinePlayback();
        panel.RestartButton.Click += (_, _) => RestartDemo();
        panel.ManualButton.Click += (_, _) => EnterManualCameraMode();
        panel.DemoButton.Click += (_, _) => PlayTimeline(openRasterPreview: true);
        panel.InsertObjButton.Click += (_, _) => InsertModelFromDialog();
        panel.ClearSceneButton.Click += (_, _) => ClearScene();
        panel.OpenFileButton.Click += (_, _) => OpenFileFromDialog();
        panel.OpenSimplifiedButton.Click += (_, _) => OpenSimplifiedFileFromDialog();
        panel.SaveSceneButton.Click += (_, _) => SaveSceneFromDialog();
        panel.UndoButton.Click += (_, _) => UndoSceneEdit();
        panel.RedoButton.Click += (_, _) => RedoSceneEdit();
        panel.RealtimeViewButton.Click += (_, _) => FocusEditView();
        panel.RaytraceViewButton.Click += (_, _) => ShowRenderWindowAndRender();
        panel.ObjectListView.SelectedIndexChanged += (_, _) => SelectFromEditorMeshList();
        panel.ObjectListView.ItemChecked += (_, e) => ApplyObjectListVisibilityChange(e.Item);
        panel.ObjectListView.AfterLabelEdit += (_, e) => RenameObjectFromListEdit(e);
        panel.ShowSelectedObjectsButton.Click += (_, _) => SetSelectedObjectVisibility(true);
        panel.HideSelectedObjectsButton.Click += (_, _) => SetSelectedObjectVisibility(false);
        panel.ShowAllObjectsButton.Click += (_, _) => ShowAllObjects();
        panel.TrackpadNavigationBox.CheckedChanged += (_, _) =>
            helixViewport.SetNavigationMode(panel.TrackpadNavigationBox.Checked ? ViewportNavigationMode.Trackpad : ViewportNavigationMode.Mouse);
        panel.SelectNavigationButton.Click += (_, _) => SelectNavigationTool(ViewportNavigationTool.SelectEdit);
        panel.OrbitNavigationButton.Click += (_, _) => SelectNavigationTool(ViewportNavigationTool.Orbit);
        panel.PanNavigationButton.Click += (_, _) => SelectNavigationTool(ViewportNavigationTool.Pan);
        panel.OrbitSensitivityTrackBar.ValueChanged += (_, _) => ApplyNavigationSensitivity();
        panel.PanSensitivityTrackBar.ValueChanged += (_, _) => ApplyNavigationSensitivity();
        panel.ZoomSensitivityTrackBar.ValueChanged += (_, _) => ApplyNavigationSensitivity();
        panel.FrontViewButton.Click += (_, _) => SetEditorStandardView("front");
        panel.RightViewButton.Click += (_, _) => SetEditorStandardView("right");
        panel.TopViewButton.Click += (_, _) => SetEditorStandardView("top");
        panel.ResetViewButton.Click += (_, _) => ResetEditorView();
        panel.LightListBox.SelectedIndexChanged += (_, _) => LoadSelectedLightIntoEditor(panel.LightListBox.SelectedIndex);
        panel.AddLightButton.Click += (_, _) => AddLightFromEditor();
        panel.ApplyLightButton.Click += (_, _) => ApplySelectedLightEdit();
        panel.RemoveLightButton.Click += (_, _) => RemoveSelectedLight();
        panel.ConvertSelectionToLightButton.Click += (_, _) => ConvertSelectedObjectsToLights();
        panel.TimelineListBox.SelectedIndexChanged += (_, _) => SelectTimelineKey(panel.TimelineListBox.SelectedIndex);
        panel.TimelineApplyButton.Click += (_, _) => ApplyTimelineKeyEdit();
        panel.TimelineAddCurrentButton.Click += (_, _) => AddCurrentCameraToTimeline();
        panel.TimelineDeleteButton.Click += (_, _) => DeleteSelectedTimelineKey();
        panel.TimelinePreviewButton.Click += (_, _) => PreviewSelectedTimelineKey();
        helixViewport.GroupPicked += OnHelixGroupPicked;
        helixViewport.GroupsMarqueeSelected += OnHelixGroupsMarqueeSelected;
        helixViewport.EmptySpacePicked += ClearSelection;
        helixViewport.GizmoDragged += ApplyGizmoDelta;
        helixViewport.GizmoDragCompleted += CompleteGizmoDrag;
        helixViewport.LightPicked += OnHelixLightPicked;
        helixViewport.ContextToolRequested += SelectNavigationTool;
        helixViewport.ContextFrameRequested += id =>
        {
            SelectGroup(id);
            helixViewport.FrameSelectionOrScene(scene);
            MarkRenderDirty();
            UpdateStatus();
        };
        helixViewport.ContextDuplicateRequested += id =>
        {
            SelectGroup(id);
            DuplicateSelectedGroup();
        };
        helixViewport.ContextConvertToLightRequested += id =>
        {
            SelectGroup(id);
            ConvertSelectedObjectsToLights();
        };
        helixViewport.ContextDeleteRequested += id =>
        {
            SelectGroup(id);
            DeleteSelectedGroup();
        };
        panel.InsertReadyMadeButton.Click += (_, _) => InsertSelectedReadyMadeObject();
        panel.ApplyMoveButton.Click += (_, _) => ApplySelectedMove();
        panel.ApplyRotateButton.Click += (_, _) => ApplySelectedRotation();
        panel.ApplyScaleButton.Click += (_, _) => ApplySelectedScale();
        panel.DeleteSelectionButton.Click += (_, _) => DeleteSelectedGroup();
        panel.DuplicateSelectionButton.Click += (_, _) => DuplicateSelectedGroup();
        panel.GroupSelectionButton.Click += (_, _) => GroupSelectedGroups();
        panel.UngroupSelectionButton.Click += (_, _) => UngroupSelectedGroup();
        panel.ColorSelectionButton.Click += (_, _) => ChangeSelectedColor();
        panel.TextureSelectionButton.Click += (_, _) => AssignSelectedBitmapTexture();
        panel.SampleTextureSelectionButton.Click += (_, _) => AssignSelectedCheckerTexture();
        panel.ClearTextureSelectionButton.Click += (_, _) => ClearSelectedTexture();
        panel.RetileTextureButton.Click += (_, _) => RetileSelectedTexture();
        panel.SimplifySelectionButton.Click += (_, _) => SimplifySelectedGroup();
        panel.MaterialLibraryComboBox.SelectedIndexChanged += (_, _) => UpdateSelectedMaterialPresetInfo();
        panel.ApplyMaterialPresetButton.Click += (_, _) => ApplySelectedMaterialPreset();
        panel.ApplyMaterialPropertiesButton.Click += (_, _) => ApplySelectedMaterialProperties();
        panel.ScaleComboBox.SelectedIndexChanged += (_, _) =>
        {
            renderScale = RenderScale.Values[panel.ScaleComboBox.SelectedIndex];
            ResizeRenderTarget();
        };
        panel.RenderBackendComboBox.SelectedIndexChanged += (_, _) =>
        {
            renderBackend = ParseRenderBackendSelection(panel.RenderBackendComboBox.SelectedItem?.ToString());
            if (IsOrbitableRasterBackend(renderBackend))
                ResizeRenderTarget(forceShrinkToViewport: true);
            MarkRenderDirty();
            if (viewportTabs.SelectedTab == renderViewportTab)
                ShowRenderWindowAndRender();
        };
        panel.BounceComboBox.SelectedIndexChanged += (_, _) =>
        {
            pathBounceCount = ParseBounceSelection(panel.BounceComboBox.SelectedItem?.ToString());
            MarkRenderDirty();
        };
        panel.MaxSamplesComboBox.SelectedIndexChanged += (_, _) =>
        {
            maxAccumulationSamples = ParseMaxSamplesSelection(panel.MaxSamplesComboBox.SelectedItem?.ToString());
            MarkRenderDirty();
        };
    }
}
