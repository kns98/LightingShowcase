// -----------------------------------------------------------------------------
// File: LightingShowcaseForm.Rendering.cs
// Purpose: Raytrace orchestration.
//
// Manages bitmap lifetime, render sizing, cancellation, progressive preview passes, and publishing finished frames to the PictureBox.
// This comment is intentionally kept in source code so future maintainers can
// understand the role of this file without opening external documentation.
// -----------------------------------------------------------------------------

using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.Rendering;
using LightingShowcase.SceneGraph;
using LightingShowcase.UI;

namespace LightingShowcase;

public sealed partial class LightingShowcaseForm
{
    /// <summary>Implements the queue render target resize operation for this file's subsystem.</summary>
    private void QueueRenderTargetResize()
    {
        if (renderTabResizing || renderTargetResizeQueued || IsDisposed)
            return;

        renderTargetResizeQueued = true;
        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                renderTargetResizeQueued = false;
                if (!renderTabResizing && !IsDisposed)
                    ResizeRenderTarget();
            }));
        }
        catch (InvalidOperationException)
        {
            renderTargetResizeQueued = false;
        }
    }

    /// <summary>Implements the cancel active render operation for this file's subsystem.</summary>
    private void CancelActiveRender()
    {
        try
        {
            renderCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>Implements the resize render target operation for this file's subsystem.</summary>
    private void ResizeRenderTarget(bool forceShrinkToViewport = false)
    {
        Size viewportSize = GetRaytraceViewportSize();

        // Keep a stable logical render canvas.  Splitter movement may make the
        // visible viewport smaller, but it should not shrink the bitmap to match;
        // otherwise the child PictureBox never becomes larger than the scroll panel
        // and WinForms has no reason to show scroll bars.
        if (forceShrinkToViewport || raytraceRenderBaseSize.IsEmpty)
        {
            raytraceRenderBaseSize = viewportSize;
        }
        else
        {
            raytraceRenderBaseSize = new Size(
                System.Math.Max(raytraceRenderBaseSize.Width, viewportSize.Width),
                System.Math.Max(raytraceRenderBaseSize.Height, viewportSize.Height));
        }

        int w = System.Math.Max(1, (int)System.Math.Round(raytraceRenderBaseSize.Width * renderScale));
        int h = System.Math.Max(1, (int)System.Math.Round(raytraceRenderBaseSize.Height * renderScale));

        if (TryGetFrameSize(out int currentWidth, out int currentHeight) && currentWidth == w && currentHeight == h)
        {
            SizeRaytracePictureToFrame(w, h);
            UpdateStatus();
            return;
        }

        CancelActiveRender();

        Image? oldFrame = frame;
        frame = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        SizeRaytracePictureToFrame(w, h);

        if (ReferenceEquals(raytracePicture.Image, oldFrame))
            raytracePicture.Image = null;

        DisposeImageIfUnreferenced(oldFrame);
        MarkRaytraceDirty();
        UpdateStatus();
    }

    /// <summary>Returns raytrace viewport size derived from the current state.</summary>
    private Size GetRaytraceViewportSize()
    {
        int sourceWidth = raytraceScrollPanel.ClientSize.Width > 0
            ? raytraceScrollPanel.ClientSize.Width
            : renderViewportTab.ClientSize.Width > 0
                ? renderViewportTab.ClientSize.Width
                : System.Math.Max(1, viewportPanel.ClientSize.Width);

        int sourceHeight = raytraceScrollPanel.ClientSize.Height > 0
            ? raytraceScrollPanel.ClientSize.Height
            : renderViewportTab.ClientSize.Height > 0
                ? renderViewportTab.ClientSize.Height
                : System.Math.Max(1, viewportPanel.ClientSize.Height);

        return new Size(System.Math.Max(1, sourceWidth), System.Math.Max(1, sourceHeight));
    }

    /// <summary>Attempts to get frame size and reports failure without crashing the UI.</summary>
    private bool TryGetFrameSize(out int width, out int height)
    {
        width = 0;
        height = 0;

        Bitmap? currentFrame = frame;
        if (currentFrame == null)
            return false;

        try
        {
            width = currentFrame.Width;
            height = currentFrame.Height;
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is ObjectDisposedException || ex is InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Sets raytrace image while preserving related state invariants.</summary>
    private void SetRaytraceImage(Image? image)
    {
        Image? oldImage = raytracePicture.Image;
        if (ReferenceEquals(oldImage, image))
            return;

        raytracePicture.Image = image;
        SizeRaytracePictureToImage(image);
        DisposeImageIfUnreferenced(oldImage);
    }


    /// <summary>Implements the size raytrace picture to image operation for this file's subsystem.</summary>
    private void SizeRaytracePictureToImage(Image? image)
    {
        if (image == null)
        {
            raytracePicture.Size = Size.Empty;
            raytraceScrollPanel.AutoScrollMinSize = Size.Empty;
            return;
        }

        SizeRaytracePictureToFrame(image.Width, image.Height);
    }

    /// <summary>Implements the size raytrace picture to frame operation for this file's subsystem.</summary>
    private void SizeRaytracePictureToFrame(int width, int height)
    {
        Size size = new(
            System.Math.Max(1, width),
            System.Math.Max(1, height));

        raytracePicture.Location = Point.Empty;
        raytracePicture.Size = size;
        raytraceScrollPanel.AutoScrollMinSize = size;
    }

    /// <summary>Implements the dispose image if unreferenced operation for this file's subsystem.</summary>
    private void DisposeImageIfUnreferenced(Image? image)
    {
        if (image == null)
            return;
        if (ReferenceEquals(image, frame))
            return;
        if (ReferenceEquals(image, raytracePicture.Image))
            return;

        SafeDisposeImage(image);
    }

    /// <summary>Implements the safe dispose image operation for this file's subsystem.</summary>
    private static void SafeDisposeImage(Image? image)
    {
        if (image == null)
            return;

        try
        {
            image.Dispose();
        }
        catch (Exception ex) when (ex is ArgumentException || ex is ObjectDisposedException || ex is InvalidOperationException)
        {
            // GDI+ can throw while a PictureBox paint or another queued UI callback is
            // releasing the same image. Disposal is best-effort; do not crash editing.
        }
    }

    /// <summary>Converts the bounce dropdown label into a path-tracing bounce count.</summary>
    private static int ParseBounceSelection(string? text)
    {
        return int.TryParse(text, out int value) ? Math.Clamp(value, 0, 16) : 0;
    }

    /// <summary>Converts the renderer dropdown label into a backend preference.</summary>
    private static RenderBackend ParseRenderBackendSelection(string? text)
    {
        if (string.Equals(text, "Shadow Raster Preview", StringComparison.OrdinalIgnoreCase))
            return RenderBackend.ShadowRasterPreview;
        if (string.Equals(text, "Vulkan Raster Preview", StringComparison.OrdinalIgnoreCase))
            return RenderBackend.VulkanRasterPreview;
        if (string.Equals(text, "CPU", StringComparison.OrdinalIgnoreCase))
            return RenderBackend.Cpu;
        if (string.Equals(text, "Vulkan GPU", StringComparison.OrdinalIgnoreCase))
            return RenderBackend.VulkanGpu;
        if (string.Equals(text, "Vulkan Diagnostic", StringComparison.OrdinalIgnoreCase))
            return RenderBackend.VulkanDiagnostic;
        return RenderBackend.ShadowRasterPreview;
    }

    /// <summary>Converts the max-samples dropdown label into an accumulation limit.</summary>
    private static int ParseMaxSamplesSelection(string? text)
    {
        if (string.Equals(text, "Unlimited", StringComparison.OrdinalIgnoreCase))
            return -1;
        return int.TryParse(text, out int value) ? Math.Clamp(value, 1, 4096) : 0;
    }

    /// <summary>Starts/cancels asynchronous raytrace work for the current scene and camera.</summary>
    private void QueueBackgroundRaytrace(bool force)
    {
        if (renderBackend == RenderBackend.ShadowRasterPreview)
        {
            QueueShadowRasterPreview(force);
            return;
        }

        if (renderBackend == RenderBackend.VulkanRasterPreview)
        {
            QueueVulkanRasterPreview(force);
            return;
        }

        if (frame == null || raytraceInProgress)
            return;

        // Debounce non-forced renders so mouse orbiting and transform edits do not
        // spawn expensive renders every timer tick.
        if (!force && (!renderDirty || (DateTime.UtcNow - lastRenderDirtyUtc).TotalMilliseconds < 250))
            return;

        CameraBasis basis = camera.GetBasis();
        bool cameraChangedSinceLastRender = DistanceSquared(lastRenderedCameraPosition, camera.Position) > 0.000001 ||
                                            DistanceSquared(lastRenderedCameraForward, basis.Forward) > 0.000001;
        if (!force && !renderDirty && !cameraChangedSinceLastRender)
            return;

        if (!TryGetFrameSize(out int width, out int height))
        {
            ResizeRenderTarget();
            return;
        }
        Vec3 renderPosition = camera.Position;
        CameraBasis renderBasis = basis;
        int requestedRevision = renderRevision;
        RenderSettings renderSettings = new() { PathBounceCount = pathBounceCount, Backend = renderBackend };
        string completedRenderDetails = string.Empty;

        Scene sceneSnapshot = CreateSceneSnapshot();
        LightingState lightingSnapshot = lighting.Clone();

        renderCancellation?.Cancel();
        renderCancellation?.Dispose();
        renderCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = renderCancellation.Token;

        raytraceInProgress = true;
        raytraceViewLabel.Text = $"RAYTRACED VIEW - progressive render {width}x{height}...";

        Task.Run<Bitmap?>(() =>
        {
            Bitmap output = new(width, height, PixelFormat.Format32bppArgb);
            try
            {
                if (renderSettings.Backend == RenderBackend.VulkanGpu || renderSettings.Backend == RenderBackend.VulkanDiagnostic)
                {
                    try
                    {
                        int gpuSamples = GetSelectedAccumulationSampleCount(width, height, renderSettings.PathBounceCount);
                        int gpuBatchSize = renderSettings.Backend == RenderBackend.VulkanDiagnostic
                            ? 1
                            : GetGpuSampleBatchSize(width, height, renderSettings.PathBounceCount);
                        System.Numerics.Vector3[] gpuAccumulation = new System.Numerics.Vector3[width * height];
                        string gpuRunDetails = string.Empty;
                        int completedGpuSamples = 0;

                        while (gpuSamples < 0 || completedGpuSamples < gpuSamples)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                SafeDisposeImage(output);
                                return null;
                            }

                            int remainingSamples = gpuSamples < 0 ? gpuBatchSize : gpuSamples - completedGpuSamples;
                            int batchSamples = Math.Clamp(Math.Min(gpuBatchSize, remainingSamples), 1, 4096);

                            Action<RenderImage, string>? tileProgress = completedGpuSamples == 0 && ShouldUseVulkanTilePreview(width, height, sceneSnapshot.Triangles.Count, renderSettings.PathBounceCount)
                                ? (partial, label) =>
                                {
                                    using Bitmap partialBitmap = partial.ToBitmap();
                                    PublishVulkanTilePreview(partialBitmap, label, requestedRevision, cancellationToken);
                                }
                                : null;

                            RenderImage gpuRenderedBatch = VulkanSceneComputeRenderer.Render(
                                sceneSnapshot,
                                renderPosition,
                                renderBasis,
                                width,
                                height,
                                renderSettings.PathBounceCount,
                                completedGpuSamples,
                                batchSamples,
                                cancellationToken,
                                out string gpuDetails,
                                tileProgress,
                                settings: renderSettings);
                            using Bitmap gpuSampleBatch = gpuRenderedBatch.ToBitmap();

                            gpuRunDetails = gpuDetails;
                            RenderImageDiagnostics batchImage = AnalyzeBitmap(gpuSampleBatch);
                            if (batchImage.LooksInvalid)
                            {
                                // Do not count a broken GPU readback as completed work.
                                // The symptom is: Vulkan appears to speed up after a few
                                // samples because later all-black batches finish quickly and
                                // were previously accumulated as if they were valid.
                                VulkanSceneComputeRenderer.DisposeSharedDevice();

                                if (batchSamples <= 1)
                                    throw new InvalidOperationException($"Vulkan GPU sample batch appears invalid/black: {batchImage}. {gpuDetails}");

                                completedRenderDetails = $"Vulkan GPU retrying smaller batch after invalid output - {gpuDetails}, {batchImage}";

                                RenderImage retryRendered = VulkanSceneComputeRenderer.Render(
                                    sceneSnapshot,
                                    renderPosition,
                                    renderBasis,
                                    width,
                                    height,
                                    renderSettings.PathBounceCount,
                                    completedGpuSamples,
                                    1,
                                    cancellationToken,
                                    out string retryDetails,
                                    progressCallback: null,
                                    settings: renderSettings);
                                using Bitmap retrySample = retryRendered.ToBitmap();

                                gpuRunDetails = retryDetails;
                                RenderImageDiagnostics retryImage = AnalyzeBitmap(retrySample);
                                if (retryImage.LooksInvalid)
                                    throw new InvalidOperationException($"Vulkan GPU retry sample appears invalid/black: {retryImage}. {retryDetails}");

                                AccumulateBitmapBatch(retrySample, output, gpuAccumulation, completedGpuSamples, 1);
                                completedGpuSamples += 1;
                                completedRenderDetails = $"Vulkan GPU active - {retryDetails}, {retryImage}, samples={completedGpuSamples}/{FormatSampleLimit(gpuSamples)}";
                                PublishAccumulationPreview(output, completedGpuSamples, gpuSamples, width, height, requestedRevision, cancellationToken);
                            }
                            else
                            {
                                AccumulateBitmapBatch(gpuSampleBatch, output, gpuAccumulation, completedGpuSamples, batchSamples);
                                completedGpuSamples += batchSamples;
                                completedRenderDetails = $"Vulkan GPU active - {gpuDetails}, {batchImage}, samples={completedGpuSamples}/{FormatSampleLimit(gpuSamples)}";
                                PublishAccumulationPreview(output, completedGpuSamples, gpuSamples, width, height, requestedRevision, cancellationToken);
                            }

                            if (gpuSamples < 0)
                                Thread.Sleep(1);
                        }

                        RenderImageDiagnostics gpuImage = AnalyzeBitmap(output);
                        if (gpuImage.LooksInvalid)
                            throw new InvalidOperationException($"Vulkan GPU output appears invalid/black: {gpuImage}. {gpuRunDetails}");

                        completedRenderDetails = $"Vulkan GPU OK - {gpuRunDetails}, {gpuImage}, samples={FormatSampleLimit(gpuSamples)}";
                        return output;
                    }
                    catch (Exception gpuError) when (gpuError is not OperationCanceledException)
                    {
                        VulkanSceneComputeRenderer.DisposeSharedDevice();
                        string gpuLogPath = WriteRenderDiagnosticLog(gpuError, "vulkan gpu explicit", sceneSnapshot, width, height, renderSettings);
                        throw new InvalidOperationException(
                            $"Vulkan GPU renderer failed. No CPU fallback was used. Diagnostic log: {gpuLogPath}; stage log: {VulkanSceneComputeRenderer.StageLogPath}",
                            gpuError);
                    }
                }

                completedRenderDetails = "CPU renderer selected; Vulkan path not used.";

                FrameRenderer snapshotRenderer = new(new RayTracer(sceneSnapshot, lightingSnapshot));
                foreach (int step in GetProgressivePasses(width, height))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        SafeDisposeImage(output);
                        return null;
                    }

                    snapshotRenderer.RenderProgressivePass(output, renderPosition, renderBasis, step, renderSettings, cancellationToken);
                    PublishProgressivePreview(output, step, width, height, requestedRevision, cancellationToken);
                }

                int accumulationSamples = GetSelectedAccumulationSampleCount(width, height, renderSettings.PathBounceCount);
                Vec3[] accumulation = new Vec3[width * height];
                for (int sampleIndex = 0; accumulationSamples < 0 || sampleIndex < accumulationSamples; sampleIndex++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        SafeDisposeImage(output);
                        return null;
                    }

                    snapshotRenderer.RenderAccumulationPass(output, renderPosition, renderBasis, accumulation, sampleIndex, renderSettings, cancellationToken);
                    PublishAccumulationPreview(output, sampleIndex + 1, accumulationSamples, width, height, requestedRevision, cancellationToken);

                    // In Unlimited mode, yield occasionally so queued UI cancellation
                    // callbacks can run promptly while the image keeps refining.
                    if (accumulationSamples < 0 && (sampleIndex + 1) % 8 == 0)
                        Thread.Sleep(1);
                }

                return output;
            }
            catch (OperationCanceledException)
            {
                SafeDisposeImage(output);
                return null;
            }
            catch
            {
                SafeDisposeImage(output);
                throw;
            }
        }, CancellationToken.None).ContinueWith(task =>
        {
            raytraceInProgress = false;

            if (task.IsCanceled)
            {
                raytraceViewLabel.Text = "RAYTRACED VIEW - update queued";
                renderDirty = true;
                return;
            }

            if (task.IsFaulted)
            {
                Exception? error = task.Exception?.GetBaseException();
                if (error is OperationCanceledException)
                {
                    raytraceViewLabel.Text = "RAYTRACED VIEW - update queued";
                    renderDirty = true;
                    return;
                }

                if (error != null)
                    ReportRenderException(error, "render task");
                else
                    raytraceViewLabel.Text = "RAYTRACED VIEW - render failed: unknown error";
                return;
            }

            Bitmap? output = task.Result;
            if (output == null || cancellationToken.IsCancellationRequested)
            {
                SafeDisposeImage(output);
                raytraceViewLabel.Text = "RAYTRACED VIEW - update queued";
                renderDirty = true;
                QueueBackgroundRaytrace(force: false);
                return;
            }
            if (requestedRevision != renderRevision || cancellationToken.IsCancellationRequested)
            {
                SafeDisposeImage(output);
                raytraceViewLabel.Text = "RAYTRACED VIEW - update queued";
                renderDirty = true;
                QueueBackgroundRaytrace(force: false);
                return;
            }

            Image? previousFrame = frame;
            frame = output;
            SetRaytraceImage(output);
            DisposeImageIfUnreferenced(previousFrame);

            lastRenderedCameraPosition = renderPosition;
            lastRenderedCameraForward = renderBasis.Forward;
            renderDirty = requestedRevision != renderRevision;
            raytraceViewLabel.Text = renderDirty
                ? "RAYTRACED VIEW - update queued"
                : !string.IsNullOrWhiteSpace(completedRenderDetails)
                    ? completedRenderDetails
                    : $"RAYTRACED VIEW - follows Helix camera ({width}x{height}, {FormatSampleLimit(GetSelectedAccumulationSampleCount(width, height, pathBounceCount))} spp)";
            UpdateStatus();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }


    private void QueueShadowRasterPreview(bool force)
    {
        if (frame == null || raytraceInProgress)
            return;

        if (viewportTabs.SelectedTab != renderViewportTab)
            return;

        // Keep the preview cadence close to a realtime viewer. The render itself
        // still runs one frame at a time so the UI thread remains responsive.
        // 20 FPS means one frame every ~50 ms; do not overschedule work the CPU
        // cannot display. This cap is bypassed for the explicit first/final frame.
        DateTime now = DateTime.UtcNow;
        if (!force && (!renderDirty || (now - lastRenderDirtyUtc).TotalMilliseconds < 8))
            return;
        if (!force && (now - lastShadowRasterPreviewStartUtc).TotalMilliseconds < 50)
            return;

        CameraBasis basis = camera.GetBasis();
        bool cameraChangedSinceLastRender = DistanceSquared(lastRenderedCameraPosition, camera.Position) > 0.000001 ||
                                            DistanceSquared(lastRenderedCameraForward, basis.Forward) > 0.000001;
        if (!force && !renderDirty && !cameraChangedSinceLastRender)
            return;

        if (!TryGetFrameSize(out int width, out int height))
        {
            ResizeRenderTarget(forceShrinkToViewport: true);
            return;
        }

        Size displaySize = GetRaytraceViewportSize();
        int displayWidth = Math.Max(1, displaySize.Width);
        int displayHeight = Math.Max(1, displaySize.Height);

        bool interactiveOrbit = rasterPreviewMouseDragging || keys.Count > 0 || demoPlaying || (!force && renderDirty);
        bool interactiveFast = interactiveOrbit;
        if (interactiveOrbit)
            LimitInteractiveShadowRasterSize(ref width, ref height);

        Vec3 renderPosition = camera.Position;
        CameraBasis renderBasis = basis;
        int requestedRevision = renderRevision;
        int contentRevisionAtStart = shadowRasterContentRevision;
        bool rebuildCache = shadowRasterPreviewCache == null || shadowRasterPreviewCacheContentRevision != contentRevisionAtStart;
        ShadowRasterRenderer.PreviewCache? cachedPreview = rebuildCache ? null : shadowRasterPreviewCache;
        Scene? sceneSnapshotForCache = rebuildCache ? CreateSceneSnapshot() : null;

        lastShadowRasterPreviewStartUtc = DateTime.UtcNow;
        lastShadowRasterPreviewWasInteractive = interactiveFast;

        renderCancellation?.Cancel();
        renderCancellation?.Dispose();
        renderCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = renderCancellation.Token;

        raytraceInProgress = true;
        raytraceViewLabel.Text = rebuildCache
            ? $"SHADOW RASTER PREVIEW - building shadow cache then rasterizing {width}x{height}..."
            : interactiveFast
                ? $"SHADOW RASTER PREVIEW - 20 FPS target mode {width}x{height}..."
                : $"SHADOW RASTER PREVIEW - quality frame {width}x{height}...";

        Task.Run<ShadowRasterPreviewTaskResult?>(() =>
        {
            try
            {
                ShadowRasterRenderer.PreviewCache previewCache = cachedPreview ?? ShadowRasterRenderer.BuildCache(sceneSnapshotForCache!, cancellationToken);
                Bitmap output = ShadowRasterRenderer.Render(
                    previewCache,
                    renderPosition,
                    renderBasis,
                    width,
                    height,
                    cancellationToken,
                    out string completedRenderDetails,
                    interactiveFast).ToBitmap();

                return new ShadowRasterPreviewTaskResult(output, completedRenderDetails, previewCache, contentRevisionAtStart, requestedRevision, displayWidth, displayHeight, interactiveFast);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }, CancellationToken.None).ContinueWith(task =>
        {
            raytraceInProgress = false;

            if (task.IsFaulted)
            {
                Exception? error = task.Exception?.GetBaseException();
                if (error != null)
                    ReportRenderException(error, "shadow raster preview");
                else
                    raytraceViewLabel.Text = "SHADOW RASTER PREVIEW - render failed: unknown error";
                return;
            }

            ShadowRasterPreviewTaskResult? result = task.Result;
            Bitmap? output = result?.Bitmap;
            if (result == null || output == null || cancellationToken.IsCancellationRequested)
            {
                SafeDisposeImage(output);
                raytraceViewLabel.Text = "SHADOW RASTER PREVIEW - update queued";
                renderDirty = true;
                QueueBackgroundRaytrace(force: false);
                return;
            }

            if (result.ContentRevision == shadowRasterContentRevision)
            {
                shadowRasterPreviewCache = result.Cache;
                shadowRasterPreviewCacheContentRevision = result.ContentRevision;
            }

            Bitmap displayOutput = PrepareRasterDisplayBitmap(output, result.DisplayWidth, result.DisplayHeight);
            Image? previousFrame = frame;
            frame = displayOutput;
            SetRaytraceImage(displayOutput);
            DisposeImageIfUnreferenced(previousFrame);

            lastRenderedCameraPosition = renderPosition;
            lastRenderedCameraForward = renderBasis.Forward;
            bool needsSettledQualityFrame = result.InteractivePreview && !IsRasterPreviewInteractionActive();
            renderDirty = result.RequestedRevision != renderRevision || result.ContentRevision != shadowRasterContentRevision || needsSettledQualityFrame;
            raytraceViewLabel.Text = renderDirty
                ? $"{result.Details} - updating..."
                : result.Details;
            UpdateStatus();

            if (renderDirty)
                QueueBackgroundRaytrace(force: needsSettledQualityFrame);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }


    private static Bitmap PrepareRasterDisplayBitmap(Bitmap source, int displayWidth, int displayHeight)
    {
        displayWidth = Math.Max(1, displayWidth);
        displayHeight = Math.Max(1, displayHeight);
        if (source.Width == displayWidth && source.Height == displayHeight)
            return source;

        Bitmap display = new(displayWidth, displayHeight, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(display))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingMode = CompositingMode.SourceCopy;

            double scale = Math.Min(displayWidth / (double)Math.Max(1, source.Width), displayHeight / (double)Math.Max(1, source.Height));
            int drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            int left = (displayWidth - drawWidth) / 2;
            int top = (displayHeight - drawHeight) / 2;
            graphics.DrawImage(source, new Rectangle(left, top, drawWidth, drawHeight));
        }

        SafeDisposeImage(source);
        return display;
    }

    private static void LimitInteractiveShadowRasterSize(ref int width, ref int height)
    {
        const int maxInteractiveSide = 480;
        int longestSide = Math.Max(width, height);
        if (longestSide <= maxInteractiveSide)
            return;

        double scale = maxInteractiveSide / (double)longestSide;
        width = Math.Max(1, (int)Math.Round(width * scale));
        height = Math.Max(1, (int)Math.Round(height * scale));
    }

    private sealed record ShadowRasterPreviewTaskResult(
        Bitmap Bitmap,
        string Details,
        ShadowRasterRenderer.PreviewCache Cache,
        int ContentRevision,
        int RequestedRevision,
        int DisplayWidth,
        int DisplayHeight,
        bool InteractivePreview);

    private void QueueVulkanRasterPreview(bool force)
    {
        if (frame == null || raytraceInProgress)
            return;

        if (viewportTabs.SelectedTab != renderViewportTab)
            return;

        DateTime now = DateTime.UtcNow;
        if (!force && (!renderDirty || (now - lastRenderDirtyUtc).TotalMilliseconds < 8))
            return;
        if (!force && (now - lastVulkanRasterPreviewStartUtc).TotalMilliseconds < 50)
            return;

        CameraBasis basis = camera.GetBasis();
        bool cameraChangedSinceLastRender = DistanceSquared(lastRenderedCameraPosition, camera.Position) > 0.000001 ||
                                            DistanceSquared(lastRenderedCameraForward, basis.Forward) > 0.000001;
        if (!force && !renderDirty && !cameraChangedSinceLastRender)
            return;

        if (!TryGetFrameSize(out int width, out int height))
        {
            ResizeRenderTarget(forceShrinkToViewport: true);
            return;
        }

        Size displaySize = GetRaytraceViewportSize();
        int displayWidth = Math.Max(1, displaySize.Width);
        int displayHeight = Math.Max(1, displaySize.Height);

        bool interactiveOrbit = rasterPreviewMouseDragging || keys.Count > 0 || demoPlaying || (!force && renderDirty);
        if (interactiveOrbit)
            LimitInteractiveShadowRasterSize(ref width, ref height);

        Vec3 renderPosition = camera.Position;
        CameraBasis renderBasis = basis;
        int requestedRevision = renderRevision;
        Scene sceneSnapshot = CreateSceneSnapshot();

        lastVulkanRasterPreviewStartUtc = DateTime.UtcNow;

        renderCancellation?.Cancel();
        renderCancellation?.Dispose();
        renderCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = renderCancellation.Token;

        raytraceInProgress = true;
        raytraceViewLabel.Text = interactiveOrbit
            ? $"VULKAN RASTER PREVIEW - hardware rasterizing interactive frame {width}x{height}..."
            : $"VULKAN RASTER PREVIEW - hardware rasterizing quality frame {width}x{height}...";

        Task.Run<VulkanRasterPreviewTaskResult?>(() =>
        {
            try
            {
                Bitmap output = VulkanRasterRenderer.Render(
                    sceneSnapshot,
                    renderPosition,
                    renderBasis,
                    width,
                    height,
                    cancellationToken,
                    out string completedRenderDetails).ToBitmap();

                return new VulkanRasterPreviewTaskResult(output, completedRenderDetails, requestedRevision, displayWidth, displayHeight, interactiveOrbit);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }, CancellationToken.None).ContinueWith(task =>
        {
            raytraceInProgress = false;

            if (task.IsFaulted)
            {
                VulkanRasterRenderer.DisposeSharedDevice();
                Exception? error = task.Exception?.GetBaseException();
                if (error != null)
                    ReportRenderException(error, "vulkan raster preview");
                else
                    raytraceViewLabel.Text = "VULKAN RASTER PREVIEW - render failed: unknown error";
                return;
            }

            VulkanRasterPreviewTaskResult? result = task.Result;
            Bitmap? output = result?.Bitmap;
            if (result == null || output == null || cancellationToken.IsCancellationRequested)
            {
                SafeDisposeImage(output);
                raytraceViewLabel.Text = "VULKAN RASTER PREVIEW - update queued";
                renderDirty = true;
                QueueBackgroundRaytrace(force: false);
                return;
            }

            Bitmap displayOutput = PrepareRasterDisplayBitmap(output, result.DisplayWidth, result.DisplayHeight);
            Image? previousFrame = frame;
            frame = displayOutput;
            SetRaytraceImage(displayOutput);
            DisposeImageIfUnreferenced(previousFrame);

            lastRenderedCameraPosition = renderPosition;
            lastRenderedCameraForward = renderBasis.Forward;
            bool needsSettledQualityFrame = result.InteractivePreview && !IsRasterPreviewInteractionActive();
            renderDirty = result.RequestedRevision != renderRevision || needsSettledQualityFrame;
            raytraceViewLabel.Text = renderDirty
                ? $"{result.Details} - updating..."
                : result.Details;
            UpdateStatus();

            if (renderDirty)
                QueueBackgroundRaytrace(force: needsSettledQualityFrame);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private sealed record VulkanRasterPreviewTaskResult(
        Bitmap Bitmap,
        string Details,
        int RequestedRevision,
        int DisplayWidth,
        int DisplayHeight,
        bool InteractivePreview);

    private static bool IsOrbitableRasterBackend(RenderBackend backend) =>
        backend == RenderBackend.ShadowRasterPreview || backend == RenderBackend.VulkanRasterPreview;

    private bool IsRasterPreviewInteractionActive() =>
        rasterPreviewMouseDragging || keys.Count > 0 || demoPlaying;

    private static long EstimateGpuRayTriangleWork(int width, int height, Scene scene, int bounceCount)
    {
        long pixels = Math.Max(1L, width) * Math.Max(1L, height);
        long triangles = Math.Max(1L, scene.Triangles.Count);
        long passes = Math.Max(1L, bounceCount + 1L);
        return pixels * triangles * passes;
    }

    private static bool IsGpuWorkRisky(long estimatedRayTriangleTests)
    {
        // This Vulkan path is a brute-force compute shader, not a BLAS/TLAS Vulkan-RT path.
        // Large scenes at 100% scale can hit the Windows GPU watchdog or return black readback.
        return estimatedRayTriangleTests > 2_000_000_000L;
    }

    private readonly record struct RenderImageDiagnostics(double BlackRatio, double NonFiniteRatio, double AverageLuma)
    {
        // Treat a truly empty Vulkan readback as invalid, but do not reject a
        // deliberately dark scene merely because its average luminance is low.
        public bool LooksInvalid => NonFiniteRatio > 0.0 || (BlackRatio > 0.999 && AverageLuma < 0.0001);
        public override string ToString() => $"black={BlackRatio:P1}, bad={NonFiniteRatio:P1}, avgLuma={AverageLuma:0.0000}";
    }

    /// <summary>Detects the common GPU failure mode where complex scenes produce an all-black readback.</summary>
    private static RenderImageDiagnostics AnalyzeBitmap(Bitmap bitmap)
    {
        Rectangle rect = new(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int total = checked(bitmap.Width * bitmap.Height);
            int black = 0;
            double lumaSum = 0.0;
            byte[] row = new byte[bitmap.Width * 4];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int i = x * 4;
                    double b = row[i + 0] / 255.0;
                    double g = row[i + 1] / 255.0;
                    double r = row[i + 2] / 255.0;
                    double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    lumaSum += luma;
                    if (r < 0.003 && g < 0.003 && b < 0.003)
                        black++;
                }
            }

            return new RenderImageDiagnostics(black / (double)Math.Max(1, total), 0.0, lumaSum / Math.Max(1, total));
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static string WriteRenderDiagnosticLog(Exception ex, string stage, Scene scene, int width, int height, RenderSettings settings)
    {
        string path = Path.Combine(Path.GetTempPath(), "LightingShowcase-gpu-render-diagnostic.txt");
        try
        {
            File.WriteAllText(path,
                $"Stage: {stage}{Environment.NewLine}" +
                $"Backend: {settings.Backend}{Environment.NewLine}" +
                $"Resolution: {width}x{height}{Environment.NewLine}" +
                $"Bounces: {settings.PathBounceCount}{Environment.NewLine}" +
                $"Triangles: {scene.Triangles.Count}{Environment.NewLine}" +
                $"Lights: {scene.Lights.Count}{Environment.NewLine}" +
                $"Estimated work: {(long)width * height * Math.Max(1, scene.Triangles.Count) * Math.Max(1, settings.PathBounceCount + 1):N0} ray-triangle tests before shadows{Environment.NewLine}" +
                $"Exception:{Environment.NewLine}{ex}");
        }
        catch
        {
            return "log write failed";
        }

        return path;
    }

    /// <summary>Clears a bitmap before falling back to another renderer.</summary>
    private static void ClearBitmap(Bitmap bitmap)
    {
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
    }


    /// <summary>Shows and records a full render exception without letting a background/UI callback terminate the app.</summary>
    private void ReportRenderException(Exception ex, string stage)
    {
        string message = ex.ToString();
        string shortMessage = ex.GetBaseException().Message;
        string logPath = Path.Combine(Path.GetTempPath(), "LightingShowcase-render-error.txt");

        try
        {
            File.WriteAllText(logPath, $"Stage: {stage}{Environment.NewLine}{message}");
        }
        catch
        {
            logPath = string.Empty;
        }

        string label = string.IsNullOrWhiteSpace(logPath)
            ? $"RAYTRACED VIEW - {stage} failed: {shortMessage}"
            : $"RAYTRACED VIEW - {stage} failed: {shortMessage} (log: {logPath})";

        try
        {
            if (InvokeRequired && !IsDisposed)
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    if (!IsDisposed)
                        raytraceViewLabel.Text = label;
                }));
            }
            else if (!IsDisposed)
            {
                raytraceViewLabel.Text = label;
            }
        }
        catch
        {
            // Last-resort diagnostics only; never crash while reporting a render failure.
        }
    }

    /// <summary>
    /// Avoids publishing full-frame tile previews for large scenes.  Each tile
    /// preview requires a full GPU readback and a cloned Bitmap on the UI side;
    /// on 150-200% renders this makes memory usage look much worse than the
    /// actual final frame.  Large scenes still publish progressive previews per
    /// completed sample.
    /// </summary>
    private static bool ShouldUseVulkanTilePreview(int width, int height, int triangleCount, int bounceCount)
    {
        long pixels = (long)width * height;
        if (pixels > 600_000)
            return false;
        if (triangleCount > 75_000)
            return false;
        if (bounceCount > 1 && pixels > 300_000)
            return false;
        return true;
    }

    /// <summary>Returns how many GPU samples to trace per Vulkan setup/upload batch.</summary>
    private static int GetGpuSampleBatchSize(int width, int height, int bounceCount)
    {
        long pixels = (long)Math.Max(1, width) * Math.Max(1, height);

        // Deep path tracing multiplies the shader cost per pixel.  Keep those
        // batches small enough that one Vulkan submit does not monopolize the
        // GPU and return an all-black buffer on watchdog-sensitive drivers.
        if (bounceCount >= 6)
            return 1;
        if (bounceCount >= 3)
            return pixels >= 900_000 ? 2 : 4;
        if (bounceCount > 0)
            return pixels >= 900_000 ? 4 : 8;
        return pixels >= 1_200_000 ? 16 : 32;
    }

    /// <summary>Accumulates a GPU-rendered average sample batch into the visible output bitmap.</summary>
    private static void AccumulateBitmapBatch(Bitmap sampleBatch, Bitmap output, System.Numerics.Vector3[] accumulation, int completedSamples, int batchSamples)
    {
        if (sampleBatch.Width != output.Width || sampleBatch.Height != output.Height)
            throw new ArgumentException("GPU sample batch and output bitmap dimensions must match.", nameof(sampleBatch));
        if (accumulation.Length != output.Width * output.Height)
            throw new ArgumentException("Accumulation buffer size does not match the output bitmap.", nameof(accumulation));
        if (completedSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(completedSamples));
        if (batchSamples <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSamples));

        Rectangle rect = new(0, 0, output.Width, output.Height);
        BitmapData sampleData = sampleBatch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData outputData = output.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int width = output.Width;
            int height = output.Height;
            byte[] sampleRow = new byte[width * 4];
            byte[] outputRow = new byte[width * 4];
            double previousWeight = completedSamples;
            double batchWeight = batchSamples;
            double totalWeight = Math.Max(1.0, previousWeight + batchWeight);

            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(sampleData.Scan0 + y * sampleData.Stride, sampleRow, 0, sampleRow.Length);

                for (int x = 0; x < width; x++)
                {
                    int byteIndex = x * 4;
                    double b = SanitizeChannel(sampleRow[byteIndex + 0] / 255.0);
                    double g = SanitizeChannel(sampleRow[byteIndex + 1] / 255.0);
                    double r = SanitizeChannel(sampleRow[byteIndex + 2] / 255.0);

                    int pixelIndex = y * width + x;
                    System.Numerics.Vector3 previous = SanitizeColor(accumulation[pixelIndex]);
                    System.Numerics.Vector3 next = SanitizeColor(new System.Numerics.Vector3(
                        (float)((previous.X * previousWeight + r * batchWeight) / totalWeight),
                        (float)((previous.Y * previousWeight + g * batchWeight) / totalWeight),
                        (float)((previous.Z * previousWeight + b * batchWeight) / totalWeight)));
                    accumulation[pixelIndex] = next;

                    outputRow[byteIndex + 0] = ToByte(next.Z);
                    outputRow[byteIndex + 1] = ToByte(next.Y);
                    outputRow[byteIndex + 2] = ToByte(next.X);
                    outputRow[byteIndex + 3] = 255;
                }

                Marshal.Copy(outputRow, 0, outputData.Scan0 + y * outputData.Stride, outputRow.Length);
            }
        }
        finally
        {
            output.UnlockBits(outputData);
            sampleBatch.UnlockBits(sampleData);
        }
    }

    /// <summary>Accumulates one GPU-rendered sample into the visible output bitmap.</summary>
    private static void AccumulateBitmapSample(Bitmap sample, Bitmap output, System.Numerics.Vector3[] accumulation, int sampleIndex)
    {
        if (sample.Width != output.Width || sample.Height != output.Height)
            throw new ArgumentException("GPU sample and output bitmap dimensions must match.", nameof(sample));
        if (accumulation.Length != output.Width * output.Height)
            throw new ArgumentException("Accumulation buffer size does not match the output bitmap.", nameof(accumulation));

        Rectangle rect = new(0, 0, output.Width, output.Height);
        BitmapData sampleData = sample.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData outputData = output.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            int width = output.Width;
            int height = output.Height;
            byte[] sampleRow = new byte[width * 4];
            byte[] outputRow = new byte[width * 4];
            double sampleWeight = 1.0 / (sampleIndex + 1.0);

            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(sampleData.Scan0 + y * sampleData.Stride, sampleRow, 0, sampleRow.Length);

                for (int x = 0; x < width; x++)
                {
                    int byteIndex = x * 4;
                    double b = SanitizeChannel(sampleRow[byteIndex + 0] / 255.0);
                    double g = SanitizeChannel(sampleRow[byteIndex + 1] / 255.0);
                    double r = SanitizeChannel(sampleRow[byteIndex + 2] / 255.0);

                    int pixelIndex = y * width + x;
                    System.Numerics.Vector3 previous = SanitizeColor(accumulation[pixelIndex]);
                    System.Numerics.Vector3 next = SanitizeColor(new System.Numerics.Vector3(
                        (float)(previous.X + (r - previous.X) * sampleWeight),
                        (float)(previous.Y + (g - previous.Y) * sampleWeight),
                        (float)(previous.Z + (b - previous.Z) * sampleWeight)));
                    accumulation[pixelIndex] = next;

                    outputRow[byteIndex + 0] = ToByte(next.Z);
                    outputRow[byteIndex + 1] = ToByte(next.Y);
                    outputRow[byteIndex + 2] = ToByte(next.X);
                    outputRow[byteIndex + 3] = 255;
                }

                Marshal.Copy(outputRow, 0, outputData.Scan0 + y * outputData.Stride, outputRow.Length);
            }
        }
        finally
        {
            output.UnlockBits(outputData);
            sample.UnlockBits(sampleData);
        }
    }

    private static byte ToByte(double value)
    {
        if (!double.IsFinite(value))
            return 0;
        return (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
    }

    private static System.Numerics.Vector3 SanitizeColor(System.Numerics.Vector3 color) => new(
        (float)SanitizeChannel(color.X),
        (float)SanitizeChannel(color.Y),
        (float)SanitizeChannel(color.Z));

    private static Vec3 SanitizeColor(Vec3 color) => new(
        SanitizeChannel(color.X),
        SanitizeChannel(color.Y),
        SanitizeChannel(color.Z));

    private static double SanitizeChannel(double value)
    {
        if (!double.IsFinite(value) || value < 0.0)
            return 0.0;
        return Math.Min(value, 64.0);
    }

    /// <summary>Publishes a partial Vulkan tile image so large GPU renders visibly progress before the full sample completes.</summary>
    private void PublishVulkanTilePreview(Bitmap partial, string label, int requestedRevision, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || IsDisposed)
            return;

        Bitmap preview = (Bitmap)partial.Clone();

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || IsDisposed || requestedRevision != renderRevision)
                    {
                        SafeDisposeImage(preview);
                        return;
                    }

                    SetRaytraceImage(preview);
                    raytraceViewLabel.Text = label;
                }
                catch (Exception ex)
                {
                    SafeDisposeImage(preview);
                    ReportRenderException(ex, "vulkan tile preview publish");
                }
            }));
        }
        catch (InvalidOperationException)
        {
            SafeDisposeImage(preview);
        }
    }

    /// <summary>Implements the publish progressive preview operation for this file's subsystem.</summary>
    private void PublishProgressivePreview(Bitmap output, int step, int width, int height, int requestedRevision, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || IsDisposed)
            return;

        Bitmap preview = (Bitmap)output.Clone();

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || IsDisposed || requestedRevision != renderRevision)
                    {
                        SafeDisposeImage(preview);
                        return;
                    }

                    SetRaytraceImage(preview);
                    raytraceViewLabel.Text = $"RAYTRACED VIEW - refining {width}x{height}, {step}px pass";
                }
                catch (Exception ex)
                {
                    SafeDisposeImage(preview);
                    ReportRenderException(ex, "preview publish");
                }
            }));
        }
        catch (InvalidOperationException)
        {
            SafeDisposeImage(preview);
        }
    }

    /// <summary>Implements the publish accumulation preview operation for this file's subsystem.</summary>
    private void PublishAccumulationPreview(Bitmap output, int sampleNumber, int totalSamples, int width, int height, int requestedRevision, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || IsDisposed)
            return;

        Bitmap preview = (Bitmap)output.Clone();

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || IsDisposed || requestedRevision != renderRevision)
                    {
                        SafeDisposeImage(preview);
                        return;
                    }

                    SetRaytraceImage(preview);
                    raytraceViewLabel.Text = $"RAYTRACED VIEW - anti-aliasing {width}x{height}, sample {sampleNumber}/{FormatSampleLimit(totalSamples)}";
                }
                catch (Exception ex)
                {
                    SafeDisposeImage(preview);
                    ReportRenderException(ex, "accumulation publish");
                }
            }));
        }
        catch (InvalidOperationException)
        {
            SafeDisposeImage(preview);
        }
    }

    /// <summary>Returns progressive passes derived from the current state.</summary>
    private static int[] GetProgressivePasses(int width, int height)
    {
        int longestSide = Math.Max(width, height);
        if (longestSide >= 900) return new[] { 32, 16, 8, 4, 2 };
        if (longestSide >= 450) return new[] { 16, 8, 4, 2 };
        return new[] { 8, 4, 2 };
    }

    /// <summary>Returns the currently selected accumulation limit, or the adaptive default when Auto is selected.</summary>
    private int GetSelectedAccumulationSampleCount(int width, int height, int bounceCount = 0)
    {
        if (maxAccumulationSamples != 0)
            return maxAccumulationSamples;

        int autoSamples = GetAccumulationSampleCount(width, height);
        if (bounceCount > 0)
        {
            int longestSide = Math.Max(width, height);
            int bouncedMinimum = longestSide >= 900 ? 48 : longestSide >= 450 ? 64 : 96;
            autoSamples = Math.Max(autoSamples, bouncedMinimum);
        }

        return autoSamples;
    }

    /// <summary>Formats the accumulation limit for render status text.</summary>
    private static string FormatSampleLimit(int sampleLimit)
    {
        return sampleLimit < 0 ? "Unlimited" : sampleLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Returns accumulation sample count derived from the current state.</summary>
    private static int GetAccumulationSampleCount(int width, int height)
    {
        int longestSide = Math.Max(width, height);
        if (longestSide >= 900) return 4;
        if (longestSide >= 450) return 6;
        return 8;
    }

    /// <summary>Creates scene snapshot for use by the renderer or editor.</summary>
    private Scene CreateSceneSnapshot()
    {
        Scene snapshot = new();
        snapshot.Clear();
        snapshot.Triangles.AddRange(scene.Triangles);
        snapshot.Lights.AddRange(scene.Lights);
        snapshot.RebuildAccelerationStructure();
        return snapshot;
    }

    /// <summary>Renders current frame using the current camera and scene data.</summary>
    private void RenderCurrentFrame()
    {
        QueueBackgroundRaytrace(force: true);
    }

}
