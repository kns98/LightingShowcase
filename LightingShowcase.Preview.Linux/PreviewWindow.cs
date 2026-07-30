using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    private CancellationTokenSource lifetimeCancellation = new();

    public PreviewWindow(string[] startupArguments)
    {
        Title = "LightingShowcase Linux Preview";
        Width = 1280;
        Height = 800;
        MinWidth = 720;
        MinHeight = 480;

        pathBox = new TextBox
        {
            Watermark = "/path/to/scene.gltf",
            Text = FirstSceneArgument(startupArguments) ?? string.Empty,
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        loadButton = new Button { Content = "Load", MinWidth = 72 };
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
            Text = "Enter a scene path or pass one on the command line.",
            Margin = new Thickness(10, 7),
            TextWrapping = TextWrapping.Wrap
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

        DockPanel root = new();
        DockPanel.SetDock(toolbarGrid, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(toolbarGrid);
        root.Children.Add(status);
        root.Children.Add(viewport);
        Content = root;

        loadButton.Click += async (_, _) => await LoadSceneAsync();
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

        Opened += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(pathBox.Text))
                await LoadSceneAsync();
        };
        Closed += (_, _) =>
        {
            lifetimeCancellation.Cancel();
            bitmap?.Dispose();
            session.Dispose();
            lifetimeCancellation.Dispose();
        };
    }

    private RendererChoice SelectedRenderer =>
        rendererBox.SelectedItem as RendererChoice ?? rendererChoices[0];

    private async Task LoadSceneAsync()
    {
        string path = pathBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            status.Text = "Enter a local scene/model file path.";
            return;
        }

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
                await RenderOneFrameAsync(thisInteractive);
            }
            while (renderAgain && !lifetimeCancellation.IsCancellationRequested);
        }
        finally
        {
            rendering = false;
        }
    }

    private async Task RenderOneFrameAsync(bool interactive)
    {
        RendererChoice renderer = SelectedRenderer;
        (int width, int height) = ChooseRenderSize(renderer.Kind, interactive);
        CameraDefinition camera = session.Camera.Snapshot();
        status.Text = $"Rendering {renderer.Label} at {width}x{height} …";

        try
        {
            CancellationToken token = lifetimeCancellation.Token;
            PreviewFrame frame = await Task.Run(
                () => session.Render(renderer.Kind, camera, width, height, interactive, token),
                token);

            if (lifetimeCancellation.IsCancellationRequested)
                return;

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
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            status.Text = $"{renderer.Label} failed: {ex.Message}";
        }
    }

    private (int Width, int Height) ChooseRenderSize(PreviewRendererKind renderer, bool interactive)
    {
        double viewWidth = Math.Max(320.0, viewport.Bounds.Width);
        double viewHeight = Math.Max(180.0, viewport.Bounds.Height);

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
        int width = Math.Max(160, (int)Math.Round(viewWidth * scale));
        int height = Math.Max(90, (int)Math.Round(viewHeight * scale));
        return (width, height);
    }

    private void ShowImage(RenderImage rendered)
    {
        WriteableBitmap next = new(
            new PixelSize(rendered.Width, rendered.Height),
            new Vector(96, 96),
            PixelFormats.Rgba8888,
            AlphaFormat.Unpremul);

        byte[] source = new byte[checked(rendered.PackedRgba32.Length * sizeof(uint))];
        Buffer.BlockCopy(rendered.PackedRgba32, 0, source, 0, source.Length);

        using (ILockedFramebuffer framebuffer = next.Lock())
        {
            int sourceRowBytes = rendered.Width * 4;
            for (int y = 0; y < rendered.Height; y++)
            {
                IntPtr destination = IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes);
                Marshal.Copy(source, y * sourceRowBytes, destination, sourceRowBytes);
            }
        }

        WriteableBitmap? old = bitmap;
        bitmap = next;
        image.Source = next;
        old?.Dispose();
    }

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
