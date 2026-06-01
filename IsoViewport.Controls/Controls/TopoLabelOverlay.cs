using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Controls.Controls;

public sealed class TopoLabelOverlay : Control
{
    private readonly record struct LabelCandidate(Point Anchor, string Text, double Priority);

    public static readonly StyledProperty<TileMap?> TileMapProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, TileMap?>(nameof(TileMap));

    public static readonly StyledProperty<float> CameraZoomProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, float>(nameof(CameraZoom), 1.0f);

    public static readonly StyledProperty<float> CameraPanXProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, float>(nameof(CameraPanX), 0f);

    public static readonly StyledProperty<float> CameraPanYProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, float>(nameof(CameraPanY), 0f);

    public static readonly StyledProperty<float> CameraRotationDegreesProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, float>(nameof(CameraRotationDegrees), 0f);

    public static readonly StyledProperty<ViewProjectionMode> ViewProjectionModeProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, ViewProjectionMode>(nameof(ViewProjectionMode), ViewProjectionMode.ThreeD);

    public static readonly StyledProperty<TerrainRenderMode> RenderModeProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, TerrainRenderMode>(nameof(RenderMode), TerrainRenderMode.Voxel);

    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, double>(nameof(ViewportWidth), 0d);

    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<TopoLabelOverlay, double>(nameof(ViewportHeight), 0d);

    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(66, 66, 66));
    private static readonly IBrush LabelHaloBrush = new SolidColorBrush(Color.FromArgb(220, 250, 250, 247));
    private static readonly string[] ElevationLabels = BuildElevationLabels();
    private const int LabelInterval = 2;
    private const int MajorLabelInterval = 5;
    private const int MinimumLabelSpacingPixels = 64;
    private readonly Dictionary<string, FormattedText> _labelTextCache = [];
    private readonly Dictionary<string, FormattedText> _haloTextCache = [];
    private double _cachedFontSize = -1d;

    public TileMap? TileMap
    {
        get => GetValue(TileMapProperty);
        set => SetValue(TileMapProperty, value);
    }

    public float CameraZoom
    {
        get => GetValue(CameraZoomProperty);
        set => SetValue(CameraZoomProperty, IsoCamera.ClampZoom(value));
    }

    public float CameraPanX
    {
        get => GetValue(CameraPanXProperty);
        set => SetValue(CameraPanXProperty, value);
    }

    public float CameraPanY
    {
        get => GetValue(CameraPanYProperty);
        set => SetValue(CameraPanYProperty, value);
    }

    public float CameraRotationDegrees
    {
        get => GetValue(CameraRotationDegreesProperty);
        set => SetValue(CameraRotationDegreesProperty, IsoMath.NormalizeRotationDegrees(value));
    }

    public ViewProjectionMode ViewProjectionMode
    {
        get => GetValue(ViewProjectionModeProperty);
        set => SetValue(ViewProjectionModeProperty, value);
    }

    public TerrainRenderMode RenderMode
    {
        get => GetValue(RenderModeProperty);
        set => SetValue(RenderModeProperty, value);
    }

    public double ViewportWidth
    {
        get => GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public double ViewportHeight
    {
        get => GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TileMapProperty ||
            change.Property == CameraZoomProperty ||
            change.Property == CameraPanXProperty ||
            change.Property == CameraPanYProperty ||
            change.Property == CameraRotationDegreesProperty ||
            change.Property == ViewProjectionModeProperty ||
            change.Property == RenderModeProperty ||
            change.Property == ViewportWidthProperty ||
            change.Property == ViewportHeightProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (RenderMode != TerrainRenderMode.Topographical || TileMap is not { } map)
        {
            return;
        }

        var viewportWidth = ViewportWidth > 0d ? (float)ViewportWidth : (float)Bounds.Width;
        var viewportHeight = ViewportHeight > 0d ? (float)ViewportHeight : (float)Bounds.Height;

        if (viewportWidth <= 0f || viewportHeight <= 0f)
        {
            return;
        }

        var visibleGround = IsoMath.GetVisibleTileBounds(
            CameraPanX,
            CameraPanY,
            CameraZoom,
            CameraRotationDegrees,
            viewportWidth,
            viewportHeight,
            ViewProjectionMode);

        var minCol = Math.Max(0, (int)MathF.Floor(visibleGround.Left) - 2);
        var maxCol = Math.Min(map.Cols - 1, (int)MathF.Ceiling(visibleGround.Right) + 2);
        var minRow = Math.Max(0, (int)MathF.Floor(visibleGround.Top) - 2);
        var maxRow = Math.Min(map.Rows - 1, (int)MathF.Ceiling(visibleGround.Bottom) + 2);
        var viewportCentre = new Point(viewportWidth * 0.5f, viewportHeight * 0.5f);
        var candidates = new List<LabelCandidate>();
        var fontSize = Math.Clamp(15d + (CameraZoom * 5.5d), 15d, 26d) * 3d;
        var minimumLabelSpacing = Math.Max(MinimumLabelSpacingPixels, fontSize * 2.8d);
        var minimumLabelSpacingSquared = minimumLabelSpacing * minimumLabelSpacing;
        var placedLabels = new Dictionary<(int X, int Y), List<Point>>();

        EnsureFormattedTextCache(fontSize);

        for (var row = minRow; row <= maxRow; row++)
        {
            for (var col = minCol; col <= maxCol; col++)
            {
                if (!IsContourLabelCandidate(map, row, col))
                {
                    continue;
                }

                var screen = IsoMath.TileToScreen(col, row, map.Elevation[row, col], CameraRotationDegrees, ViewProjectionMode) * CameraZoom;
                var labelYOffset = ViewProjectionMode == IsoViewport.Controls.Rendering.ViewProjectionMode.TopDown
                    ? 0f
                    : IsoMath.TileHalfH * CameraZoom * 0.4f;
                screen += new Vector2(CameraPanX, CameraPanY - labelYOffset);

                if (screen.X < -32f || screen.X > viewportWidth + 32f || screen.Y < -24f || screen.Y > viewportHeight + 24f)
                {
                    continue;
                }

                var anchor = new Point(screen.X, screen.Y);
                var elevation = map.Elevation[row, col];
                var priority = DistanceSquared(anchor, viewportCentre);

                if (elevation % MajorLabelInterval == 0)
                {
                    priority -= 1500d;
                }

                candidates.Add(new LabelCandidate(
                    anchor,
                    GetElevationLabel(elevation),
                    priority));
            }
        }

        candidates.Sort(static (left, right) => left.Priority.CompareTo(right.Priority));

        foreach (var candidate in candidates)
        {
            if (!CanPlaceLabel(placedLabels, candidate.Anchor, minimumLabelSpacing, minimumLabelSpacingSquared))
            {
                continue;
            }

            var text = GetOrCreateFormattedText(candidate.Text, fontSize, halo: false);
            var drawPoint = new Point(candidate.Anchor.X - text.Width / 2d, candidate.Anchor.Y - text.Height / 2d);
            DrawTextWithHalo(context, candidate.Text, fontSize, text, drawPoint);
            StorePlacedLabel(placedLabels, candidate.Anchor, minimumLabelSpacing);
        }
    }

    internal static bool IsContourLabelCandidate(TileMap map, int row, int col)
    {
        var elevation = map.Elevation[row, col];

        if (elevation < LabelInterval || elevation % LabelInterval != 0)
        {
            return false;
        }

        var contourBand = elevation / LabelInterval;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var sampleRow = row + dy;
                var sampleCol = col + dx;

                if ((uint)sampleRow >= (uint)map.Rows || (uint)sampleCol >= (uint)map.Cols)
                {
                    continue;
                }

                var sampleElevation = map.Elevation[sampleRow, sampleCol];

                if (sampleElevation / LabelInterval < contourBand)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void DrawTextWithHalo(DrawingContext context, string label, double fontSize, FormattedText text, Point origin)
    {
        var haloOffset = Math.Max(2d, Math.Round(fontSize * 0.08d));
        var halo = GetOrCreateFormattedText(label, fontSize, halo: true);
        var offsets = new[]
        {
            new Point(-haloOffset, 0),
            new Point(haloOffset, 0),
            new Point(0, -haloOffset),
            new Point(0, haloOffset),
        };

        foreach (var offset in offsets)
        {
            context.DrawText(halo, new Point(origin.X + offset.X, origin.Y + offset.Y));
        }

        context.DrawText(text, origin);
    }

    private void EnsureFormattedTextCache(double fontSize)
    {
        if (Math.Abs(_cachedFontSize - fontSize) < 0.01d)
        {
            return;
        }

        _cachedFontSize = fontSize;
        _labelTextCache.Clear();
        _haloTextCache.Clear();
    }

    private FormattedText GetOrCreateFormattedText(string label, double fontSize, bool halo)
    {
        var cache = halo ? _haloTextCache : _labelTextCache;

        if (cache.TryGetValue(label, out var text))
        {
            return text;
        }

        text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            LabelTypeface,
            fontSize,
            halo ? LabelHaloBrush : LabelBrush);
        cache[label] = text;
        return text;
    }

    private static bool CanPlaceLabel(
        Dictionary<(int X, int Y), List<Point>> placedLabels,
        Point anchor,
        double cellSize,
        double minimumLabelSpacingSquared)
    {
        var originCell = GetPlacementCell(anchor, cellSize);

        for (var cellY = originCell.Y - 1; cellY <= originCell.Y + 1; cellY++)
        {
            for (var cellX = originCell.X - 1; cellX <= originCell.X + 1; cellX++)
            {
                if (!placedLabels.TryGetValue((cellX, cellY), out var points))
                {
                    continue;
                }

                for (var i = 0; i < points.Count; i++)
                {
                    if (DistanceSquared(points[i], anchor) < minimumLabelSpacingSquared)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static void StorePlacedLabel(Dictionary<(int X, int Y), List<Point>> placedLabels, Point anchor, double cellSize)
    {
        var cell = GetPlacementCell(anchor, cellSize);

        if (!placedLabels.TryGetValue(cell, out var points))
        {
            points = [];
            placedLabels[cell] = points;
        }

        points.Add(anchor);
    }

    private static (int X, int Y) GetPlacementCell(Point anchor, double cellSize)
    {
        return ((int)Math.Floor(anchor.X / cellSize), (int)Math.Floor(anchor.Y / cellSize));
    }

    private static string GetElevationLabel(int elevation)
    {
        return (uint)elevation < (uint)ElevationLabels.Length
            ? ElevationLabels[elevation]
            : elevation.ToString(CultureInfo.InvariantCulture);
    }

    private static string[] BuildElevationLabels()
    {
        var labels = new string[TileMap.MaxElevation + 1];

        for (var elevation = 0; elevation < labels.Length; elevation++)
        {
            labels[elevation] = elevation.ToString(CultureInfo.InvariantCulture);
        }

        return labels;
    }

    private static double DistanceSquared(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }
}
