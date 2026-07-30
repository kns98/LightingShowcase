// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.SceneFiles.cs
// Purpose: Scene file operations and history.
//
// Opens, inserts, saves, drag-drops scenes, tracks load progress, fits imported objects, and manages undo/redo snapshots.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;
using LightingShowcase.UI;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Implements the insert obj from dialog operation for this file's subsystem.</summary>
    private void InsertModelFromDialog()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Insert 3D model",
            Filter = sceneFiles.InsertDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            TryInsertModel(dialog.FileName);
    }

    /// <summary>Implements the on drag enter operation for this file's subsystem.</summary>
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files?.Any(sceneFiles.IsSupportedDropFile) == true)
                e.Effect = DragDropEffects.Copy;
        }
    }

    /// <summary>Implements the on drag drop operation for this file's subsystem.</summary>
    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        string[]? files = e.Data?.GetData(DataFormats.FileDrop) as string[];
        string? path = files?.FirstOrDefault(sceneFiles.IsSupportedDropFile);
        if (path == null) return;

        if (sceneFiles.IsSupportedModelFile(path))
            TryOpenModel(path);
        else
            TryOpenPropXml(path);
    }

    /// <summary>Begins a user-initiated undo transaction before a mutating edit.</summary>
    private void CaptureUndoState()
    {
        sceneHistory.BeginUserAction(scene);
        UpdateHistoryUi();
    }

    /// <summary>Restores the previous scene snapshot.</summary>
    private void UndoSceneEdit()
    {
        if (!sceneHistory.Undo(scene))
            return;

        selectedGroupId = -1;
        selectedGroupIds.Clear();
        lastLoadMessage = "Undo";
        AfterSceneRestored();
    }

    /// <summary>Reapplies the most recently undone scene snapshot.</summary>
    private void RedoSceneEdit()
    {
        if (!sceneHistory.Redo(scene))
            return;

        selectedGroupId = -1;
        selectedGroupIds.Clear();
        lastLoadMessage = "Redo";
        AfterSceneRestored();
    }

    /// <summary>Implements the after scene restored operation for this file's subsystem.</summary>
    private void AfterSceneRestored()
    {
        UpdateHistoryUi();
        RefreshLightList();
        helixViewport.SelectGroups(selectedGroupIds, scene);
        UpdateSelectionUi();
        MarkRenderDirty();
        UpdateStatus();
    }

    /// <summary>Updates history ui from the current application state.</summary>
    private void UpdateHistoryUi()
    {
        panel.UndoButton.Enabled = sceneHistory.CanUndo;
        panel.RedoButton.Enabled = sceneHistory.CanRedo;
    }

    /// <summary>Loads an OBJ file as a replacement scene.</summary>
    private void TryOpenModel(string filePath, bool recordUndo = true, double? simplifyKeepFraction = null)
    {
        string loadingVerb = simplifyKeepFraction.HasValue ? "Opening simplified" : "Opening";
        BeginLoading($"{loadingVerb} {Path.GetFileName(filePath)}");
        try
        {
            if (recordUndo)
                CaptureUndoState();
            ObjLoadResult result = simplifyKeepFraction.HasValue
                ? sceneFiles.OpenModelSimplified(filePath, simplifyKeepFraction.Value, ReportObjLoadProgress)
                : sceneFiles.OpenModel(filePath, ReportObjLoadProgress);
            selectedGroupId = -1;
            selectedGroupIds.Clear();
            demoPlaying = false;
            useDemoCamera = false;
            FitCameraToSceneBounds();
            AutoLowerRenderScaleForLargeScene(result.TriangleCount);
            lastLoadMessage = simplifyKeepFraction.HasValue
                ? $"Opened simplified {Path.GetFileName(filePath)}: {result.TriangleCount:N0} tris kept at {simplifyKeepFraction.Value:P0}"
                : $"Opened {Path.GetFileName(filePath)}: {result.TriangleCount:N0} tris";
            MarkRenderDirty();
            RefreshLightList();
            UpdateSelectionUi();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            lastLoadMessage = $"Model open failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Model open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus();
        }
        finally
        {
            EndLoading();
        }
    }

    /// <summary>Implements the begin loading operation for this file's subsystem.</summary>
    private void BeginLoading(string message)
    {
        loadingScene = true;
        Cursor = Cursors.WaitCursor;
        panel.LoadingLabel.Text = message;
        panel.LoadingLabel.Visible = true;
        panel.LoadingProgressBar.Value = 0;
        panel.LoadingProgressBar.Visible = true;
        SetLoadButtonsEnabled(false);
        panel.Refresh();
        Application.DoEvents();
    }

    /// <summary>Implements the end loading operation for this file's subsystem.</summary>
    private void EndLoading()
    {
        panel.LoadingProgressBar.Value = 100;
        panel.LoadingLabel.Text = "Done";
        panel.Refresh();
        Application.DoEvents();
        panel.LoadingLabel.Visible = false;
        panel.LoadingProgressBar.Visible = false;
        SetLoadButtonsEnabled(true);
        Cursor = Cursors.Default;
        loadingScene = false;
    }

    /// <summary>Implements the report obj load progress operation for this file's subsystem.</summary>
    private void ReportObjLoadProgress(ObjLoadProgress progress)
    {
        int value = Math.Max(panel.LoadingProgressBar.Minimum, Math.Min(panel.LoadingProgressBar.Maximum, progress.Percent));
        panel.LoadingProgressBar.Value = value;
        panel.LoadingLabel.Text = $"{progress.Stage} {value}%  V:{progress.VertexCount:N0}  F:{progress.FaceCount:N0}  T:{progress.TriangleCount:N0}";
        panel.LoadingLabel.Visible = true;
        panel.LoadingProgressBar.Visible = true;
        panel.LoadingProgressBar.Refresh();
        panel.LoadingLabel.Refresh();
        Application.DoEvents();
    }

    /// <summary>Sets load buttons enabled while preserving related state invariants.</summary>
    private void SetLoadButtonsEnabled(bool enabled)
    {
        panel.InsertObjButton.Enabled = enabled;
        panel.OpenFileButton.Enabled = enabled;
        panel.OpenSimplifiedButton.Enabled = enabled;
        panel.OpenSimplifiedKeepPercentBox.Enabled = enabled;
        panel.ClearSceneButton.Enabled = enabled;
        panel.SaveSceneButton.Enabled = enabled;
        panel.RealtimeViewButton.Enabled = enabled;
        panel.RaytraceViewButton.Enabled = enabled;
        panel.InsertReadyMadeButton.Enabled = enabled;
        panel.ReadyMadeObjectComboBox.Enabled = enabled;
    }

    /// <summary>Implements the fit camera to scene bounds operation for this file's subsystem.</summary>
    private void FitCameraToSceneBounds()
    {
        Aabb? maybeBounds = scene.GetSceneBounds();
        if (maybeBounds == null)
            return;

        Aabb bounds = maybeBounds.Value;
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        Vec3 size = bounds.Max - bounds.Min;
        double radius = Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5;
        radius = Math.Max(radius, 0.25);

        double verticalFovRadians = 72.0 * Math.PI / 180.0;
        double aspect = ClientSize.Height <= 0 ? 1.0 : ClientSize.Width / (double)ClientSize.Height;
        double horizontalFovRadians = 2.0 * Math.Atan(Math.Tan(verticalFovRadians / 2.0) * aspect);
        double limitingFov = Math.Min(verticalFovRadians, horizontalFovRadians);
        double distance = radius / Math.Tan(limitingFov / 2.0) * 1.18;

        Vec3 cameraPosition = new(center.X, center.Y + radius * 0.10, center.Z - distance);
        camera.SetLookAt(cameraPosition, center);
        UpdateCameraUi();
    }

    /// <summary>Implements the auto lower render scale for large scene operation for this file's subsystem.</summary>
    private void AutoLowerRenderScaleForLargeScene(int triangleCount)
    {
        if (triangleCount < 150_000 || renderScale <= 0.25)
            return;

        renderScale = 0.25;
        panel.ScaleComboBox.SelectedIndex = RenderScale.IndexOf(renderScale);
        ResizeRenderTarget();
    }

    /// <summary>Imports an OBJ file into the current scene.</summary>
    private void TryInsertModel(string filePath)
    {
        BeginLoading($"Inserting {Path.GetFileName(filePath)}");
        try
        {
            CaptureUndoState();
            ObjLoadResult result = sceneFiles.InsertModel(filePath, ReportObjLoadProgress);
            AutoLowerRenderScaleForLargeScene(result.TriangleCount);
            lastLoadMessage = $"Inserted {Path.GetFileName(filePath)}: {result.TriangleCount} tris";
            MarkRenderDirty();
            UpdateSelectionUi();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            lastLoadMessage = $"Model insert failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Model insert failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus();
        }
        finally
        {
            EndLoading();
        }
    }


    /// <summary>Implements the insert selected ready made object operation for this file's subsystem.</summary>
    private void InsertSelectedReadyMadeObject()
    {
        string objectName = panel.ReadyMadeObjectComboBox.SelectedItem?.ToString() ?? ObjectLibraryRegistry.Names.FirstOrDefault() ?? "Cube";
        try
        {
            CaptureUndoState();
            SceneObjectGroup group = scene.InsertReadyMadeObject(objectName);
            selectedGroupId = group.Id;
            selectedGroupIds.Clear();
            selectedGroupIds.Add(group.Id);
            helixViewport.SelectGroups(selectedGroupIds, scene);
            demoPlaying = false;
            useDemoCamera = false;
            lastLoadMessage = $"Inserted ready-made object: {group.Name}";
            MarkRenderDirty();
            UpdateSelectionUi();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            lastLoadMessage = $"Ready-made insert failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Ready-made insert failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus();
        }
    }

    /// <summary>Clears scene and updates dependent UI/render state.</summary>
    private void ClearScene()
    {
        CaptureUndoState();
        scene.Clear();
        selectedGroupId = -1;
        selectedGroupIds.Clear();
        lastLoadMessage = "Scene cleared";
        UpdateSelectionUi();
        RefreshLightList();
        MarkRenderDirty();
        UpdateStatus();
    }

    /// <summary>Implements the open file from dialog operation for this file's subsystem.</summary>
    private void OpenFileFromDialog()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Open model or Prop XML scene",
            Filter = sceneFiles.OpenDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (sceneFiles.IsSupportedModelFile(dialog.FileName))
            TryOpenModel(dialog.FileName);
        else
            TryOpenPropXml(dialog.FileName);
    }


    /// <summary>Opens a model using import-time mesh simplification to keep large assets responsive.</summary>
    private void OpenSimplifiedFileFromDialog()
    {
        if (!TryReadOpenSimplifiedKeepFraction(out double keepFraction))
            return;

        using OpenFileDialog dialog = new()
        {
            Title = "Open simplified model",
            Filter = sceneFiles.InsertDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (sceneFiles.IsSupportedModelFile(dialog.FileName))
            TryOpenModel(dialog.FileName, recordUndo: true, simplifyKeepFraction: keepFraction);
        else
            MessageBox.Show(this, "Open Simplified is for 3D model files only, not Prop XML scenes.", "Open Simplified", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Reads and validates the Simplified Open percentage from the Scene tab.</summary>
    private bool TryReadOpenSimplifiedKeepFraction(out double keepFraction)
    {
        keepFraction = 0.35;
        if (!double.TryParse(panel.OpenSimplifiedKeepPercentBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double keepPercent) ||
            keepPercent < 2.0 || keepPercent > 100.0)
        {
            MessageBox.Show(this, "Enter a simplified-open keep percentage between 2 and 100.", "Invalid simplify setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        keepFraction = keepPercent / 100.0;
        return true;
    }

    /// <summary>Attempts to open prop xml and reports failure without crashing the UI.</summary>
    private void TryOpenPropXml(string filePath)
    {
        try
        {
            CaptureUndoState();
            sceneFiles.OpenPropXml(filePath);
            selectedGroupId = -1;
            selectedGroupIds.Clear();
            demoTime = 0.0;
            useDemoCamera = true;
            SetDemoCamera();
            lastLoadMessage = $"Opened {Path.GetFileName(filePath)}";
            MarkRenderDirty();
            RefreshLightList();
            UpdateSelectionUi();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            lastLoadMessage = $"XML open failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "XML open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus();
        }
    }

    /// <summary>Prompts for a file name and exports the current scene in any supported import format.</summary>
    private void SaveSceneFromDialog()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Export scene",
            Filter = sceneFiles.SaveDialogFilter,
            FileName = "scene.prop.xml",
            AddExtension = true,
            DefaultExt = "xml",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string fileName = sceneFiles.NormalizeExportFileName(dialog.FileName, dialog.FilterIndex);

        try
        {
            SaveSceneToFile(fileName, dialog.FilterIndex);
            lastLoadMessage = $"Saved {Path.GetFileName(fileName)}";
            UpdateStatus();
        }
        catch (Exception ex)
        {
            lastLoadMessage = $"Save failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateStatus();
        }
    }

    /// <summary>Saves the scene through the central import/export service.</summary>
    private void SaveSceneToFile(string fileName, int filterIndex)
    {
        sceneFiles.Save(fileName, filterIndex);
    }
}
