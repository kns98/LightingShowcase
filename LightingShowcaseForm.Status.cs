// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.Status.cs
// Purpose: Status text and editor feedback.
//
// Builds concise status messages for the control panel, including current mode, render state, selection, and scene statistics.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.SceneGraph;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Starts or resumes camera-timeline playback and makes the active raster preview follow it.</summary>
    private void PlayTimeline(bool restart = false, bool openRasterPreview = false)
    {
        if (restart)
            demoTime = 0.0;

        demoPlaying = true;
        useDemoCamera = true;
        SetDemoCamera();
        helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
        MarkRaytraceDirty();

        if (IsOrbitableRasterBackend(renderBackend))
        {
            if (openRasterPreview && viewportTabs.SelectedTab != renderViewportTab)
                ShowRenderWindow();
            if (viewportTabs.SelectedTab == renderViewportTab)
                ResizeRenderTarget(forceShrinkToViewport: true);
            QueueBackgroundRaytrace(force: true);
        }

        UpdateStatus();
    }

    /// <summary>Pauses camera-timeline playback without leaving the timeline camera.</summary>
    private void PauseTimeline()
    {
        demoPlaying = false;
        MarkRaytraceDirty();
        if (IsOrbitableRasterBackend(renderBackend))
            QueueBackgroundRaytrace(force: true);
        UpdateStatus();
    }

    /// <summary>Toggles the camera timeline from buttons and keyboard shortcuts.</summary>
    private void ToggleTimelinePlayback()
    {
        if (demoPlaying)
            PauseTimeline();
        else
            PlayTimeline(openRasterPreview: viewportTabs.SelectedTab == renderViewportTab);
    }

    /// <summary>Leaves timeline-follow mode so manual camera input does not fight playback.</summary>
    private void EnterManualCameraMode(bool pauseTimeline = true)
    {
        useDemoCamera = false;
        if (pauseTimeline)
            demoPlaying = false;
        UpdateStatus();
    }

    /// <summary>Implements the restart demo operation for this file's subsystem.</summary>
    private void RestartDemo()
    {
        PlayTimeline(restart: true, openRasterPreview: viewportTabs.SelectedTab == renderViewportTab);
    }

    /// <summary>Refreshes human-readable status text in the control panel.</summary>
    private void UpdateStatus()
    {
        try
        {
            // Commit a pending undo snapshot only after a user action actually
            // changed editable scene content. This keeps failed/no-op operations
            // and internal scene rebuilds out of history.
            if (!gizmoUndoCaptured)
                sceneHistory.CommitPendingIfChanged(scene);
            UpdateHistoryUi();
            string cameraMode = useDemoCamera ? "Demo camera" : "Manual camera";
            string playState = demoPlaying ? "timeline playing" : "timeline paused";
            panel.PlayPauseButton.Text = demoPlaying ? "Pause timeline" : "Play timeline";
            string size = GetFrameSizeText();
            string viewMode = renderBackend switch
            {
                RenderBackend.ShadowRasterPreview => raytraceInProgress ? "Shadow raster tab rendering" : "Shadow raster tab",
                RenderBackend.VulkanRasterPreview => raytraceInProgress ? "Vulkan raster tab rendering" : "Vulkan raster tab",
                _ => raytraceInProgress ? "Render tab background render" : "Editor view"
            };

            string selection = selectedGroupIds.Count > 1
                ? $"Selected: {selectedGroupIds.Count} objects"
                : SelectedGroup == null ? "No selection" : $"Selected: {SelectedGroup.Name}";
            string history = $"Undo {sceneHistory.UndoCount} / Redo {sceneHistory.RedoCount}";
            string stats = scene.GetStats().ToString();
            panel.StatusLabel.Text =
                $"{cameraMode} | {playState} | {viewMode} | {size}\n" +
                $"{lighting.Label} | {scene.Description}\n" +
                $"{selection} | {stats} | {history} | {lastLoadMessage}";
        }
        catch (Exception ex) when (ex is ArgumentException || ex is ObjectDisposedException || ex is InvalidOperationException)
        {
            // Status text should never break editing/rendering. Rendering and preview
            // callbacks can briefly invalidate UI/image objects while a render is being
            // replaced, so keep the app responsive and let the next status update win.
        }
    }

    /// <summary>Returns frame size text derived from the current state.</summary>
    private string GetFrameSizeText()
    {
        if (renderBackend == RenderBackend.ShadowRasterPreview)
            return frame == null ? "shadow raster" : $"shadow raster {frame.Width}×{frame.Height}";
        if (renderBackend == RenderBackend.VulkanRasterPreview)
            return frame == null ? "vulkan raster" : $"vulkan raster {frame.Width}×{frame.Height}";
        if (frame == null)
            return "not rendered";

        try
        {
            return $"{frame.Width}×{frame.Height}";
        }
        catch (Exception ex) when (ex is ArgumentException || ex is ObjectDisposedException || ex is InvalidOperationException)
        {
            return "not rendered";
        }
    }

    /// <summary>Releases UI, bitmap, timer, and cancellation resources owned by the form.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                renderCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            renderCancellation?.Dispose();
            LightingShowcase.Rendering.VulkanSceneComputeRenderer.DisposeSharedDevice();
            LightingShowcase.Rendering.VulkanRasterRenderer.DisposeSharedDevice();
            timer.Dispose();

            Image? displayImage = raytracePicture.Image;
            raytracePicture.Image = null;

            Image? currentFrame = frame;
            frame = null;

            SafeDisposeImage(displayImage);
            if (!ReferenceEquals(displayImage, currentFrame))
                SafeDisposeImage(currentFrame);
        }

        base.Dispose(disposing);
    }
}
