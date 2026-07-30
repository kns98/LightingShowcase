// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.Input.cs
// Purpose: Keyboard and mouse input.
//
// Handles movement keys, mouse orbit/pan behavior, deletion shortcuts, and user input that manipulates the editor.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.UI;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Handles keyboard shortcuts and manual camera/editor commands.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // KeyPreview lets the form see keys before focused text boxes.
        // Do not consume number keys, minus/plus, WASD, delete, etc. while the user is typing
        // in any Details-panel text box. ActiveControl can be a parent panel instead of the
        // nested TextBox, so use ContainsFocus on the panel as the reliable guard.
        if (ActiveControl is TextBox || editorDetailsPanel.ContainsFocus)
            return;

        if (e.Control && e.KeyCode == Keys.Z) { UndoSceneEdit(); e.SuppressKeyPress = true; return; }
        if (e.Control && e.KeyCode == Keys.Y) { RedoSceneEdit(); e.SuppressKeyPress = true; return; }
        if (e.Control && e.KeyCode == Keys.D) { DuplicateSelectedGroup(); e.SuppressKeyPress = true; return; }
        if (e.Control && e.KeyCode == Keys.O) { OpenFileFromDialog(); e.SuppressKeyPress = true; return; }
        if (e.Control && e.KeyCode == Keys.S) { SaveSceneFromDialog(); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.Delete) { DeleteSelectedGroup(); e.SuppressKeyPress = true; return; }

        if (e.KeyCode == Keys.Space) { ToggleTimelinePlayback(); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.F) { EnterManualCameraMode(); helixViewport.FrameSelectionOrScene(scene); MarkRaytraceDirty(); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) { SetEditorStandardView("front"); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) { SetEditorStandardView("right"); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.D7 || e.KeyCode == Keys.NumPad7) { SetEditorStandardView("top"); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.R && e.Control) { ResetEditorView(); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.T) { RestartDemo(); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.M) { EnterManualCameraMode(); e.SuppressKeyPress = true; Invalidate(); return; }
        if (e.KeyCode == Keys.P) { PlayTimeline(openRasterPreview: viewportTabs.SelectedTab == renderViewportTab); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.R) { camera.Reset(); EnterManualCameraMode(); MarkRenderDirty(); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract) { StepRenderScale(-1); e.SuppressKeyPress = true; return; }
        if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add) { StepRenderScale(1); e.SuppressKeyPress = true; return; }

        if (e.KeyCode is Keys.W or Keys.A or Keys.S or Keys.D or Keys.Q or Keys.E)
        {
            EnterManualCameraMode();
            keys.Add(e.KeyCode);
            e.SuppressKeyPress = true;
            UpdateStatus();
        }
    }

    /// <summary>Starts mouse-driven camera or selection interaction.</summary>
    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (TryPickSelection(e))
        {
            dragging = false;
            return;
        }

        EnterManualCameraMode();
        dragging = true;
        lastMouseX = e.X;
        lastMouseY = e.Y;
        UpdateStatus();
    }

    /// <summary>Updates mouse-driven camera movement or hover/drag state.</summary>
    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!dragging) return;
        const double sensitivity = 0.004;
        int dx = e.X - lastMouseX;
        int dy = e.Y - lastMouseY;
        lastMouseX = e.X;
        lastMouseY = e.Y;
        camera.Rotate(-dx * sensitivity, dy * sensitivity);
        UpdateCameraUi();
        MarkRenderDirty();
    }

    /// <summary>Implements the step render scale operation for this file's subsystem.</summary>
    private void StepRenderScale(int direction)
    {
        int idx = RenderScale.ClampIndex(RenderScale.IndexOf(renderScale) + direction);
        renderScale = RenderScale.Values[idx];
        panel.ScaleComboBox.SelectedIndex = idx;
        ResizeRenderTarget();
    }
}
