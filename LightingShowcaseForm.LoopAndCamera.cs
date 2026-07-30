// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.LoopAndCamera.cs
// Purpose: Frame loop and camera synchronization.
//
// Runs the timer tick, advances demo/manual camera state, detects view changes, and keeps Helix/raytrace cameras aligned.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Main frame loop; advances time, updates cameras, and schedules renders.</summary>
    private void OnTick(object? sender, EventArgs e)
    {
        if (loadingScene)
            return;

        DateTime now = DateTime.UtcNow;
        double dt = System.Math.Min(0.05, (now - previousTime).TotalSeconds);
        previousTime = now;

        bool animatedFrame = false;
        if (demoPlaying)
        {
            UpdateDemo(dt);
            animatedFrame = true;
        }

        bool cameraMoved = UpdateManualCamera(dt);

        if (helixSceneDirty)
        {
            helixViewport.SyncFromScene(scene);
            RefreshEditorMeshList();
            helixSceneDirty = false;
        }

        bool helixCameraMoved = false;
        if (animatedFrame || cameraMoved)
            helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
        else
            helixCameraMoved = PullCameraFromHelix();

        if (animatedFrame || cameraMoved || helixCameraMoved)
            MarkRaytraceDirty();

        QueueBackgroundRaytrace(force: false);
        UpdateCameraUi();
        UpdateStatus();
    }

    /// <summary>Updates demo from the current application state.</summary>
    private void UpdateDemo(double dt)
    {
        demoTime = (demoTime + dt) % DemoDuration;
        // Playback animates only the camera timeline. Lighting is manual.
        if (useDemoCamera) SetDemoCamera();
    }

    /// <summary>Sets demo camera while preserving related state invariants.</summary>
    private void SetDemoCamera()
    {
        CameraSample sample = demoPath.Sample(demoTime / DemoDuration);
        camera.SetLookAt(sample.Position, sample.Target);
    }

    /// <summary>Implements the on paint operation for this file's subsystem.</summary>
    private void OnPaint(object? sender, PaintEventArgs e)
    {
        // Both viewports are child controls now; the form no longer paints the raytraced image directly.
    }

    /// <summary>Updates manual camera from the current application state.</summary>
    private bool UpdateManualCamera(double dt)
    {
        if (useDemoCamera || keys.Count == 0) return false;
        CameraBasis basis = camera.GetBasis();
        Vec3 move = Vec3.Zero;
        if (keys.Contains(Keys.W)) move += basis.Forward;
        if (keys.Contains(Keys.S)) move -= basis.Forward;
        if (keys.Contains(Keys.D)) move += basis.Right;
        if (keys.Contains(Keys.A)) move -= basis.Right;
        if (keys.Contains(Keys.E)) move += new Vec3(0, 1, 0);
        if (keys.Contains(Keys.Q)) move -= new Vec3(0, 1, 0);
        if (move.Length() <= 0) return false;
        camera.Move(move.Normalize(), 2.6 * dt);
        UpdateCameraUi();
        helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
        return true;
    }

    /// <summary>Implements the mark render dirty operation for this file's subsystem.</summary>
    private void MarkRenderDirty()
    {
        shadowRasterContentRevision++;
        shadowRasterPreviewCache = null;
        shadowRasterPreviewCacheContentRevision = -1;
        CancelActiveRender();
        helixSceneDirty = true;
        MarkRaytraceDirty();
        helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
        Invalidate();
    }

    /// <summary>Implements the mark raytrace dirty operation for this file's subsystem.</summary>
    private void MarkRaytraceDirty()
    {
        renderDirty = true;
        renderRevision++;
        lastRenderDirtyUtc = DateTime.UtcNow;

        // The custom shadow raster preview is meant to orbit in real time.
        // Camera changes should not cancel the frame already being rasterized;
        // let it publish, then immediately queue the newer camera frame.
        if (!IsOrbitableRasterBackend(renderBackend))
            CancelActiveRender();
    }

    /// <summary>Implements the focus edit view operation for this file's subsystem.</summary>
    private void FocusEditView()
    {
        EnterManualCameraMode();
        if (viewportTabs.SelectedTab != helixViewportTab)
            viewportTabs.SelectedTab = helixViewportTab;
        helixViewport.Focus();
        helixViewport.FrameSelectionOrScene(scene);
        MarkRaytraceDirty();
        UpdateStatus();
    }

    /// <summary>Implements the pull camera from helix operation for this file's subsystem.</summary>
    private bool PullCameraFromHelix(bool force = false)
    {
        // Poll at a modest cadence so Helix can remain fully interactive while the
        // raytraced viewport follows the latest realtime editing camera.
        DateTime now = DateTime.UtcNow;
        if (!force && (now - lastHelixCameraSampleUtc).TotalMilliseconds < 60)
            return false;
        lastHelixCameraSampleUtc = now;

        if (!helixViewport.TryGetCamera(out Vec3 helixPosition, out Vec3 helixLookDirection, out Vec3 _))
            return false;

        CameraBasis basis = camera.GetBasis();
        bool moved = DistanceSquared(camera.Position, helixPosition) > 0.000001 ||
                     DistanceSquared(basis.Forward, helixLookDirection.Normalize()) > 0.000001;
        if (!moved)
            return false;

        EnterManualCameraMode();
        camera.SetLookAt(helixPosition, helixPosition + helixLookDirection);
        return true;
    }

    /// <summary>Implements the distance squared operation for this file's subsystem.</summary>
    private static double DistanceSquared(Vec3 a, Vec3 b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

}
