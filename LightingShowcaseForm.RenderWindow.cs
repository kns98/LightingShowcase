// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.RenderWindow.cs
// Purpose: Embedded render tab and render-image export helpers.
// -----------------------------------------------------------------------------

using System.Drawing.Imaging;
using LightingShowcase.SceneGraph;
using LightingShowcase.UI;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Creates the embedded render tab that sits beside the Helix tab.</summary>
    private void ConfigureFloatingRenderWindow()
    {
        renderFloatingToolbar.Dock = DockStyle.Top;
        renderFloatingToolbar.Height = 36;
        renderFloatingToolbar.BackColor = Color.FromArgb(245, 247, 250);
        renderFloatingToolbar.Padding = new Padding(8, 5, 8, 4);
        renderViewportTab.Controls.Add(renderFloatingToolbar);

        raytraceViewLabel.Text = "RENDER TAB - select a backend and render";
        raytraceViewLabel.Dock = DockStyle.Fill;
        raytraceViewLabel.TextAlign = ContentAlignment.MiddleLeft;
        raytraceViewLabel.ForeColor = Color.FromArgb(35, 45, 55);
        raytraceViewLabel.BackColor = Color.Transparent;
        raytraceViewLabel.Padding = new Padding(4, 0, 0, 0);
        renderFloatingToolbar.Controls.Add(raytraceViewLabel);

        renderCloseButton.Text = "Helix";
        renderCloseButton.Dock = DockStyle.Right;
        renderCloseButton.Width = 64;
        renderCloseButton.FlatStyle = FlatStyle.Flat;
        renderCloseButton.BackColor = Color.White;
        renderCloseButton.FlatAppearance.BorderColor = Color.FromArgb(210, 218, 226);
        renderCloseButton.Click += (_, _) => viewportTabs.SelectedTab = helixViewportTab;
        renderFloatingToolbar.Controls.Add(renderCloseButton);

        renderSaveButton.Text = "Save Image";
        renderSaveButton.Dock = DockStyle.Right;
        renderSaveButton.Width = 96;
        renderSaveButton.FlatStyle = FlatStyle.Flat;
        renderSaveButton.BackColor = Color.White;
        renderSaveButton.FlatAppearance.BorderColor = Color.FromArgb(210, 218, 226);
        renderSaveButton.Click += (_, _) => SaveCurrentRenderImage();
        renderFloatingToolbar.Controls.Add(renderSaveButton);

        raytraceScrollPanel.Dock = DockStyle.Fill;
        raytraceScrollPanel.AutoScroll = true;
        raytraceScrollPanel.BackColor = Color.Black;
        raytraceScrollPanel.Controls.Add(raytracePicture);
        raytracePicture.MouseDown += OnRasterPreviewMouseDown;
        raytracePicture.MouseMove += OnRasterPreviewMouseMove;
        raytracePicture.MouseUp += OnRasterPreviewMouseUp;
        raytracePicture.MouseWheel += OnRasterPreviewMouseWheel;
        raytraceScrollPanel.MouseWheel += OnRasterPreviewMouseWheel;
        renderViewportTab.Controls.Add(raytraceScrollPanel);
        raytraceScrollPanel.BringToFront();
        renderFloatingToolbar.BringToFront();
    }

    /// <summary>Shows the selected render backend in the Render tab beside Helix.</summary>
    private void ShowRenderWindowAndRender()
    {
        // The shadow raster preview uses the shared CameraController, not the
        // Helix WPF camera directly. Pull the current Helix camera before
        // switching tabs so the Render tab starts from the same framing the user
        // was just editing.
        if (!useDemoCamera)
            PullCameraFromHelix(force: true);

        ShowRenderWindow();
        if (IsOrbitableRasterBackend(renderBackend))
        {
            raytraceViewLabel.Text = renderBackend == RenderBackend.VulkanRasterPreview
                ? "VULKAN RASTER PREVIEW - drag to orbit, right/middle drag to pan, wheel to zoom"
                : "SHADOW RASTER PREVIEW - drag to orbit, right/middle drag to pan, wheel to zoom";
            ResizeRenderTarget(forceShrinkToViewport: true);
        }
        else
        {
            raytraceViewLabel.Text = "RAYTRACED VIEW - follows Helix camera, renders in background";
        }

        QueueBackgroundRaytrace(force: true);
    }

    /// <summary>Begins direct orbit/pan interaction inside the custom raster preview tab.</summary>
    private void OnRasterPreviewMouseDown(object? sender, MouseEventArgs e)
    {
        if (!IsOrbitableRasterBackend(renderBackend))
            return;

        rasterPreviewMouseDragging = true;
        rasterPreviewMouseButton = e.Button;
        rasterPreviewLastMouseX = e.X;
        rasterPreviewLastMouseY = e.Y;
        raytracePicture.Focus();
    }

    /// <summary>Applies orbit/pan deltas to the shared editor camera while the shadow raster preview is active.</summary>
    private void OnRasterPreviewMouseMove(object? sender, MouseEventArgs e)
    {
        if (!IsOrbitableRasterBackend(renderBackend) || !rasterPreviewMouseDragging)
            return;

        int dx = e.X - rasterPreviewLastMouseX;
        int dy = e.Y - rasterPreviewLastMouseY;
        rasterPreviewLastMouseX = e.X;
        rasterPreviewLastMouseY = e.Y;
        if (dx == 0 && dy == 0)
            return;

        EnterManualCameraMode();
        if (rasterPreviewMouseButton == MouseButtons.Right || rasterPreviewMouseButton == MouseButtons.Middle)
            camera.Pan(dx, dy, speed: 1.5);
        else
            camera.Orbit(-dx * 0.008, dy * 0.008);

        helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
        MarkRaytraceDirty();
        QueueBackgroundRaytrace(force: false);
        UpdateCameraUi();
    }

    /// <summary>Ends direct raster preview mouse interaction.</summary>
    private void OnRasterPreviewMouseUp(object? sender, MouseEventArgs e)
    {
        rasterPreviewMouseDragging = false;
        rasterPreviewMouseButton = MouseButtons.None;

        if (IsOrbitableRasterBackend(renderBackend))
        {
            MarkRaytraceDirty();
            QueueBackgroundRaytrace(force: true);
        }
    }

    /// <summary>Dollies the shared camera from the custom raster preview tab.</summary>
    private void OnRasterPreviewMouseWheel(object? sender, MouseEventArgs e)
    {
        if (!IsOrbitableRasterBackend(renderBackend))
            return;

        EnterManualCameraMode();
        camera.Zoom(e.Delta, sensitivity: 0.0018);
        helixViewport.UpdateCamera(camera.Position, camera.GetBasis());
        MarkRaytraceDirty();
        QueueBackgroundRaytrace(force: false);
        UpdateCameraUi();
    }

    /// <summary>Selects the embedded Render tab and prepares its render surface.</summary>
    private void ShowRenderWindow()
    {
        if (viewportTabs.SelectedTab != renderViewportTab)
            viewportTabs.SelectedTab = renderViewportTab;

        QueueRenderTargetResize();
        raytracePicture.Focus();
    }

    /// <summary>Saves the currently displayed raytraced image to a common raster image format.</summary>
    private void SaveCurrentRenderImage()
    {
        Image? image = raytracePicture.Image ?? frame;
        if (image == null)
        {
            MessageBox.Show(this, "Render an image before saving.", "No render image", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using SaveFileDialog dialog = new()
        {
            Title = "Save render image",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap image (*.bmp)|*.bmp|All files (*.*)|*.*",
            FileName = "render.png",
            DefaultExt = "png",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string path = NormalizeRenderImageFileName(dialog.FileName, dialog.FilterIndex);
        ImageFormat format = dialog.FilterIndex switch
        {
            2 => ImageFormat.Jpeg,
            3 => ImageFormat.Bmp,
            _ => ImageFormat.Png
        };

        try
        {
            using Bitmap copy = new(image);
            copy.Save(path, format);
            lastLoadMessage = $"Saved render image: {Path.GetFileName(path)}";
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save render image failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Normalizes the file extension for render-image export.</summary>
    private static string NormalizeRenderImageFileName(string fileName, int filterIndex)
    {
        string wanted = filterIndex switch
        {
            2 => ".jpg",
            3 => ".bmp",
            _ => ".png"
        };

        string extension = Path.GetExtension(fileName);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return fileName;

        return Path.ChangeExtension(fileName, wanted);
    }
}
