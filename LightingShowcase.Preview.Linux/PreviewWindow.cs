using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using LightingShowcase.CameraSystem;
using LightingShowcase.Rendering;

namespace LightingShowcase.Preview;

internal sealed class PreviewWindow : Window
{
    private sealed record RendererChoice(PreviewRendererKind Kind, string Label, string Description)
    {
        public override string ToString() => Label;
    }

    private readonly RendererChoice[] rendererChoices =
    [
        new(PreviewRendererKind.Raster, "Raster", "Software rasterizer; continuous orbit."),
        new(PreviewRendererKind.VulkanRaster, "Vulkan raster", "Hardware rasterizer; continuous orbit after a fast frame."),
        new(PreviewRendererKind.VulkanCompute, "Vulkan", "Vulkan compute ray preview; continuous orbit only when fast enough."),
        new(PreviewRendererKind.Cpu, "CPU", "Slower CPU ray/path preview; renders after releasing the mouse.")
    ];

    private readonly PreviewSceneSession session = new();
    private readonly TextBox pathBox;
    private readonly Button loadButton;
    private readonly Button resetButton;
    private readonly ComboBox rendererBox;
    private readonly Border viewport;
    private readonly Image image;
    private readonly TextBlock status;
    private readonly TextBlock rendererHint;
    private readonly Dictionary<PreviewRendererKind, double> lastFrameTimes = new();

    private WriteableBitmap? bitmap;
    private bool dragging;
    private Point previousPointer;
    private bool rendering;
    private bool renderAgain;
    private bool pendingInteractive;
    private long renderVersion;
    private CancellationTokenSource? activeRenderCancellation;
    private CancellationTokenSource? resizeDebounceCancellation;
    private readonly CancellationTokenSource lifetimeCancellation = new();

    public PreviewWindow(string[] startupArguments)
    {
        Title = "LightingShowcase Linux Preview";
        Width = 1280;
        Height = 800;
        MinWidth = 720;
        MinHeight = 480;

        pathBox = new TextBox
        {
            Watermark = "No scene selected",
            Text = FirstSceneArgument(startupArguments) ?? string.Empty,
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsReadOnly = true
        };
        loadButton = new Button { Content = "Open…", MinWidth = 82 };
        resetButton = new Button { Content = "Reset view", MinWidth = 95, IsEnabled = false };
        rendererBox = new ComboBox
        {
            ItemsSource = rendererChoices,
            SelectedIndex = 0,
            MinWidth = 150
        };
        rendererHint = new TextBlock
        {
            Text = rendererChoices[0].Description,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        status = new TextBlock
        {
            Text = "Open a scene/model file or pass one on the command line.",
            Margin = new Thickness(10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        viewport = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(15, 17, 22)),
            Child = image,
            Focusable = true,
            ClipToBounds = true
        };

        Grid toolbarGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 8,
            RowSpacing = 5,
            Margin = new Thickness(10, 10, 10, 8)
        };
        toolbarGrid.Children.Add(pathBox);
        Grid.SetColumn(pathBox, 0);
        toolbarGrid.Children.Add(loadButton);
        Grid.SetColumn(loadButton, 1);
        toolbarGrid.Children.Add(rendererBox);
        Grid.SetColumn(rendererBox, 2);
        toolbarGrid.Children.Add(resetButton);
        Grid.SetColumn(resetButton, 3);
        toolbarGrid.Children.Add(rendererHint);
        Grid.SetRow(rendererHint, 1);
        Grid.SetColumnSpan(rendererHint, 4);

        Border statusBar = new()
        {
            Height = 36,
            MinHeight = 36,
            MaxHeight = 36,
            ClipToBounds = true,
            Child = status
        };

        DockPanel root = new();
        DockPanel.SetDock(toolbarGrid, Dock.Top);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(toolbarGrid);
        root.Children.Add(statusBar);
        root.Children.Add(viewport);
        Content = root;

        loadButton.Click += async (_, _) => await BrowseAndLoadSceneAsync();
        resetButton.Click += (_, _) =>
        {
            if (session.TriangleCount == 0)
                return;
            session.ResetCamera();
            _ = RequestRenderAsync(interactive: false);
        };
        rendererBox.SelectionChanged += (_, _) =>
        {
            RendererChoice choice = SelectedRenderer;
            rendererHint.Text = choice.Description;
            _ = RequestRenderAsync(interactive: false);
        };

        viewport.PointerPressed += OnPointerPressed;
        viewport.PointerMoved += OnPointerMoved;
        viewport.PointerReleased += OnPointerReleased;
        viewport.PointerCaptureLost += (_, _) => dragging = false;
        viewport.PointerWheelChanged += OnPointerWheelChanged;
        viewport.KeyDown += OnViewportKeyDown;
        viewport.SizeChanged += (_, _) => ScheduleResizeRender();

        Opened += async (_, _) =>
        {
            string? startupPath = pathBox.Text;
            if (!string.IsNullOrWhiteSpace(startupPath))
                await LoadSceneAsync(startupPath);
        };
        Closed += (_, _) =>
        {
            lifetimeCancellation.Cancel();
            activeRenderCancellation?.Cancel();
            resizeDebounceCancellation?.Cancel();
            bitmap?.Dispose();
            session.Dispose();
            lifetimeCancellation.Dispose();
        };
    }

    private RendererChoice SelectedRenderer =>
        rendererBox.SelectedItem as RendererChoice ?? rendererChoices[0];

    private async Task BrowseAndLoadSceneAsync()
    {
        if (!StorageProvider.CanOpen)
        {
            status.Text = "The desktop file picker is unavailable. Pass a scene file on the command line instead.";
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open scene or model",
                AllowMultiple = false,
                FileTypeFilter = PreviewSceneFileTypes.PickerTypes
            });
        }
        catch (Exception ex)
        {
            status.Text = $"Could not open the file picker: {ex.Message}";
            return;
        }

        if (files.Count == 0)
            return;

        string? path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            status.Text = "The selected item is not a local file.";
            return;
        }

        await LoadSceneAsync(path);
    }

    private async Task LoadSceneAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            status.Text = "Select a local scene/model file.";
            return;
        }

        CancelCurrentRender();
        SetBusy(true, $"Loading {Path.GetFileName(path)} …");
        try
        {
            CancellationToken token = lifetimeCancellation.Token;
            await Task.Run(() => session.Load(path, token), token);
            pathBox.Text = session.ScenePath;
            resetButton.IsEnabled = true;
            status.Text = $"Loaded {Path.GetFileName(session.ScenePath)} — {session.TriangleCount:N0} triangles, {session.LightCount:N0} lights.";
            await RequestRenderAsync(interactive: false);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            status.Text = $"Load failed: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (session.TriangleCount == 0)
            return;

        PointerPoint point = e.GetCurrentPoint(viewport);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        dragging = true;
        previousPointer = e.GetPosition(viewport);
        e.Pointer.Capture(viewport);
        viewport.Focus();
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!dragging)
            return;

        Point current = e.GetPosition(viewport);
        Vector delta = current - previousPointer;
        previousPointer = current;
        session.Camera.Orbit(delta.X, delta.Y);

        if (CanRenderContinuously(SelectedRenderer.Kind))
        {
            _ = RequestRenderAsync(interactive: true);
        }
        else
        {
            status.Text = $"{SelectedRenderer.Label}: release the mouse to render the new angle.";
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!dragging)
            return;

        dragging = false;
        e.Pointer.Capture(null);
        _ = RequestRenderAsync(interactive: false);
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (session.TriangleCount == 0)
            return;

        session.Camera.Zoom(e.Delta.Y);
        _ = RequestRenderAsync(interactive: CanRenderContinuously(SelectedRenderer.Kind));
        e.Handled = true;
    }

    private void OnViewportKeyDown(object? sender, KeyEventArgs e)
    {
        if (session.TriangleCount == 0)
            return;

        const double keyStep = 18.0;
        bool changed = true;
        switch (e.Key)
        {
            case Key.Left: session.Camera.Orbit(-keyStep, 0); break;
            case Key.Right: session.Camera.Orbit(keyStep, 0); break;
            case Key.Up: session.Camera.Orbit(0, -keyStep); break;
            case Key.Down: session.Camera.Orbit(0, keyStep); break;
            case Key.Add:
            case Key.OemPlus: session.Camera.Zoom(1); break;
            case Key.Subtract:
            case Key.OemMinus: session.Camera.Zoom(-1); break;
            default: changed = false; break;
        }

        if (changed)
        {
            _ = RequestRenderAsync(interactive: false);
            e.Handled = true;
        }
    }

    private bool CanRenderContinuously(PreviewRendererKind renderer)
    {
        if (renderer == PreviewRendererKind.Raster)
            return true;
        if (renderer == PreviewRendererKind.Cpu)
            return false;
        if (!lastFrameTimes.TryGetValue(renderer, out double milliseconds))
            return false;

        double threshold = renderer == PreviewRendererKind.VulkanRaster ? 160.0 : 220.0;
        return milliseconds <= threshold;
    }

    private async Task RequestRenderAsync(bool interactive)
    {
        if (session.TriangleCount == 0 || lifetimeCancellation.IsCancellationRequested)
            return;

        pendingInteractive = interactive;
        renderVersion++;

        // Do not cancel an in-flight interactive frame for every pointer move.
        // Pointer events arrive faster than most renderers can finish, and
        // cancelling each frame can starve live orbit so nothing is displayed.
        // A non-interactive request (mouse release, reset, resize, renderer
        // change) supersedes the preview and may cancel it immediately.
        if (!interactive)
            activeRenderCancellation?.Cancel();

        if (rendering)
        {
            renderAgain = true;
            return;
        }

        rendering = true;
        try
        {
            do
            {
                renderAgain = false;
                bool thisInteractive = pendingInteractive;
                pendingInteractive = false;
                long thisVersion = renderVersion;

                using CancellationTokenSource frameCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
                activeRenderCancellation = frameCancellation;
                try
                {
                    await RenderOneFrameAsync(thisInteractive, thisVersion, frameCancellation.Token);
                }
                finally
                {
                    if (ReferenceEquals(activeRenderCancellation, frameCancellation))
                        activeRenderCancellation = null;
                }
            }
            while (renderAgain && !lifetimeCancellation.IsCancellationRequested);
        }
        finally
        {
            rendering = false;
        }
    }

    private async Task RenderOneFrameAsync(bool interactive, long requestVersion, CancellationToken token)
    {
        RendererChoice renderer = SelectedRenderer;
        (int width, int height) = ChooseRenderSize(renderer.Kind, interactive);
        CameraDefinition camera = session.Camera.Snapshot();
        RenderOptions.SetBitmapInterpolationMode(
            image,
            interactive ? BitmapInterpolationMode.LowQuality : BitmapInterpolationMode.HighQuality);
        if (!interactive)
            status.Text = $"Rendering {renderer.Label} at {width}x{height} …";

        try
        {
            PreviewFrame frame = await Task.Run(
                () => session.Render(renderer.Kind, camera, width, height, interactive, token),
                token);

            // While dragging, publishing one slightly older completed frame is
            // preferable to starving the display. The queued render immediately
            // follows with the latest camera snapshot. Final-quality requests
            // still reject superseded results.
            if (token.IsCancellationRequested || (!interactive && requestVersion != renderVersion))
                return;

            if (lastFrameTimes.TryGetValue(renderer.Kind, out double previousMilliseconds))
                lastFrameTimes[renderer.Kind] = previousMilliseconds * 0.70 + frame.ElapsedMilliseconds * 0.30;
            else
                lastFrameTimes[renderer.Kind] = frame.ElapsedMilliseconds;

            ShowImage(frame.Image);

            bool live = CanRenderContinuously(renderer.Kind);
            string interactionMessage = renderer.Kind switch
            {
                PreviewRendererKind.Raster => "drag to orbit",
                PreviewRendererKind.Cpu => "drag, then release to render",
                _ when live => "fast enough for live drag",
                _ => "drag, then release to render"
            };
            status.Text = $"{renderer.Label}: {frame.ElapsedMilliseconds:0} ms — {interactionMessage}. {frame.Details}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (requestVersion == renderVersion && !lifetimeCancellation.IsCancellationRequested)
                status.Text = $"{renderer.Label} failed: {ex.Message}";
        }
    }

    private (int Width, int Height) ChooseRenderSize(PreviewRendererKind renderer, bool interactive)
    {
        double renderScaling = Math.Clamp(RenderScaling, 1.0, 4.0);
        double viewWidth = Math.Max(320.0, viewport.Bounds.Width * renderScaling);
        double viewHeight = Math.Max(180.0, viewport.Bounds.Height * renderScaling);

        int maxWidth = renderer switch
        {
            PreviewRendererKind.Cpu => 640,
            PreviewRendererKind.VulkanCompute when interactive => 640,
            PreviewRendererKind.VulkanCompute => 960,
            _ when interactive => 960,
            _ => 1280
        };
        int maxHeight = renderer switch
        {
            PreviewRendererKind.Cpu => 360,
            PreviewRendererKind.VulkanCompute when interactive => 360,
            PreviewRendererKind.VulkanCompute => 540,
            _ when interactive => 540,
            _ => 720
        };

        double scale = Math.Min(maxWidth / viewWidth, maxHeight / viewHeight);
        scale = Math.Min(1.0, scale);
        int width = AlignToEight(Math.Max(160, (int)Math.Round(viewWidth * scale)));
        int height = AlignToEight(Math.Max(96, (int)Math.Round(viewHeight * scale)));
        return (width, height);
    }

    private unsafe void ShowImage(RenderImage rendered)
    {
        bool sizeChanged = bitmap == null ||
            bitmap.PixelSize.Width != rendered.Width ||
            bitmap.PixelSize.Height != rendered.Height;

        // Reuse the Avalonia bitmap at a stable render size. This avoids one
        // native bitmap allocation and one managed full-frame copy per frame.
        if (sizeChanged)
        {
            WriteableBitmap next = new(
                new PixelSize(rendered.Width, rendered.Height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);

            WriteableBitmap? old = bitmap;
            bitmap = next;
            image.Source = next;
            old?.Dispose();
        }

        WriteableBitmap target = bitmap!;
        using (ILockedFramebuffer framebuffer = target.Lock())
        {
            fixed (uint* sourceBase = rendered.PackedRgba32)
            {
                long sourceRowBytes = checked((long)rendered.Width * sizeof(uint));
                for (int y = 0; y < rendered.Height; y++)
                {
                    byte* source = (byte*)(sourceBase + y * rendered.Width);
                    byte* destination = (byte*)framebuffer.Address + y * framebuffer.RowBytes;
                    Buffer.MemoryCopy(source, destination, framebuffer.RowBytes, sourceRowBytes);
                }
            }
        }

        // The bitmap object is intentionally reused, so explicitly invalidate
        // the Image after the framebuffer unlocks to present the new pixels.
        image.InvalidateVisual();
    }

    private void ScheduleResizeRender()
    {
        if (session.TriangleCount == 0 || lifetimeCancellation.IsCancellationRequested)
            return;

        resizeDebounceCancellation?.Cancel();
        CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
        resizeDebounceCancellation = cancellation;
        _ = RenderAfterResizeDelayAsync(cancellation);
    }

    private async Task RenderAfterResizeDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(140, cancellation.Token);
            await RequestRenderAsync(interactive: false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(resizeDebounceCancellation, cancellation))
                resizeDebounceCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelCurrentRender()
    {
        renderVersion++;
        activeRenderCancellation?.Cancel();
    }

    private static int AlignToEight(int value) => Math.Max(8, (value + 7) & ~7);

    private void SetBusy(bool busy, string? message = null)
    {
        loadButton.IsEnabled = !busy;
        rendererBox.IsEnabled = !busy;
        resetButton.IsEnabled = !busy && session.TriangleCount > 0;
        if (!string.IsNullOrWhiteSpace(message))
            status.Text = message;
    }

    private static string? FirstSceneArgument(IEnumerable<string> arguments) =>
        arguments.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
}
