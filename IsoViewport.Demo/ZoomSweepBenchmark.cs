using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IsoViewport.Controls.Rendering;
using IsoViewport.Demo.ViewModels;
using IsoViewport.Demo.Views;

namespace IsoViewport.Demo;

internal static class ZoomSweepBenchmark
{
    private const string OutputPathEnvVar = "ISOVIEWPORT_AUTOBENCH_OUTPUT";
    private const int WarmupDurationMilliseconds = 2_000;
    private const int SampleDurationMilliseconds = 12_000;
    private const int ZoomCycleMilliseconds = 4_000;
    private const int FrameStepMilliseconds = 16;
    private const float MapPadding = 16f;
    private const float ZoomMultiplier = 12f;
    private const float MinimumBenchmarkZoom = 1.0f;

    public static void TryStart(IClassicDesktopStyleApplicationLifetime desktop, MainWindow mainWindow)
    {
        var outputPath = Environment.GetEnvironmentVariable(OutputPathEnvVar);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        mainWindow.Opened += async (_, _) => await RunAsync(desktop, mainWindow, outputPath);
    }

    private static async Task RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow mainWindow,
        string outputPath)
    {
        BenchmarkResult result;

        try
        {
            result = await MeasureAsync(mainWindow);
        }
        catch (Exception ex)
        {
            result = BenchmarkResult.Failure(ex.Message);
            Environment.ExitCode = 1;
        }

        try
        {
            var directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            desktop.Shutdown();
        }
    }

    private static async Task<BenchmarkResult> MeasureAsync(MainWindow mainWindow)
    {
        if (mainWindow.DataContext is not MainViewModel viewModel)
        {
            throw new InvalidOperationException("Main window does not have a MainViewModel.");
        }

        if (viewModel.TileMap is not { } map)
        {
            throw new InvalidOperationException("The tile map is not loaded.");
        }

        var viewportHost = await WaitForViewportHostAsync(mainWindow);
        var viewportWidth = Math.Max(1f, (float)viewportHost.Bounds.Width);
        var viewportHeight = Math.Max(1f, (float)viewportHost.Bounds.Height);
        var fitted = IsoMath.FitMapToViewport(
            map,
            viewportWidth,
            viewportHeight,
            MapPadding,
            viewModel.CameraRotationDegrees,
            viewModel.ViewProjectionMode);
        var minimumZoom = Math.Max(fitted.Zoom, IsoCamera.MinZoom);
        var maximumZoom = Math.Clamp(
            Math.Max(minimumZoom * ZoomMultiplier, MinimumBenchmarkZoom),
            minimumZoom,
            IsoCamera.MaxZoom);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            viewModel.CameraZoom = fitted.Zoom;
            viewModel.CameraPanX = fitted.PanX;
            viewModel.CameraPanY = fitted.PanY;
        });

        await Task.Delay(WarmupDurationMilliseconds);

        var fpsSamples = new List<double>();

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainViewModel.Fps) && viewModel.Fps > 0d)
            {
                fpsSamples.Add(viewModel.Fps);
            }
        }

        viewModel.PropertyChanged += OnPropertyChanged;

        try
        {
            await SweepZoomAsync(viewModel, viewportWidth, viewportHeight, minimumZoom, maximumZoom);
        }
        finally
        {
            viewModel.PropertyChanged -= OnPropertyChanged;
        }

        if (fpsSamples.Count == 0)
        {
            throw new InvalidOperationException("No FPS samples were captured during the zoom sweep.");
        }

        return new BenchmarkResult(
            Success: true,
            Error: null,
            AverageFps: fpsSamples.Average(),
            MinimumFps: fpsSamples.Min(),
            MaximumFps: fpsSamples.Max(),
            Samples: fpsSamples.Count,
            DurationMilliseconds: SampleDurationMilliseconds,
            ZoomStart: minimumZoom,
            ZoomEnd: maximumZoom,
            ProjectionMode: viewModel.ViewProjectionMode.ToString(),
            RenderMode: viewModel.RenderMode.ToString(),
            MapRows: map.Rows,
            MapCols: map.Cols);
    }

    private static async Task<Control> WaitForViewportHostAsync(MainWindow mainWindow)
    {
        if (mainWindow.Content is not MainView mainView)
        {
            throw new InvalidOperationException("Main window content is not MainView.");
        }

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var viewportHost = mainView.FindControl<Control>("ViewportHost");

            if (viewportHost is not null &&
                viewportHost.Bounds.Width > 0d &&
                viewportHost.Bounds.Height > 0d)
            {
                return viewportHost;
            }

            await Task.Delay(50);
        }

        throw new InvalidOperationException("ViewportHost never became ready for benchmarking.");
    }

    private static async Task SweepZoomAsync(
        MainViewModel viewModel,
        float viewportWidth,
        float viewportHeight,
        float minimumZoom,
        float maximumZoom)
    {
        var stopwatch = Stopwatch.StartNew();
        var viewportCentre = new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f);

        while (stopwatch.ElapsedMilliseconds < SampleDurationMilliseconds)
        {
            var phase = (float)((stopwatch.ElapsedMilliseconds % ZoomCycleMilliseconds) / (double)ZoomCycleMilliseconds);
            var normalized = phase < 0.5f
                ? phase * 2f
                : 2f - (phase * 2f);
            var targetZoom = Lerp(minimumZoom, maximumZoom, normalized);

            await Dispatcher.UIThread.InvokeAsync(() => ApplyZoomAtPoint(viewModel, targetZoom, viewportCentre));
            await Task.Delay(FrameStepMilliseconds);
        }
    }

    private static void ApplyZoomAtPoint(MainViewModel viewModel, float nextZoom, Vector2 screenPoint)
    {
        var currentZoom = Math.Max(viewModel.CameraZoom, IsoCamera.MinZoom);

        if (Math.Abs(nextZoom - currentZoom) < 0.0001f)
        {
            return;
        }

        var pan = new Vector2(viewModel.CameraPanX, viewModel.CameraPanY);
        var worldBefore = (screenPoint - pan) / currentZoom;
        viewModel.CameraZoom = nextZoom;
        viewModel.CameraPanX = screenPoint.X - (worldBefore.X * nextZoom);
        viewModel.CameraPanY = screenPoint.Y - (worldBefore.Y * nextZoom);
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }

    private sealed record BenchmarkResult(
        bool Success,
        string? Error,
        double AverageFps,
        double MinimumFps,
        double MaximumFps,
        int Samples,
        int DurationMilliseconds,
        float ZoomStart,
        float ZoomEnd,
        string ProjectionMode,
        string RenderMode,
        int MapRows,
        int MapCols)
    {
        public static BenchmarkResult Failure(string error)
        {
            return new BenchmarkResult(
                Success: false,
                Error: error,
                AverageFps: 0d,
                MinimumFps: 0d,
                MaximumFps: 0d,
                Samples: 0,
                DurationMilliseconds: 0,
                ZoomStart: 0f,
                ZoomEnd: 0f,
                ProjectionMode: string.Empty,
                RenderMode: string.Empty,
                MapRows: 0,
                MapCols: 0);
        }
    }
}
