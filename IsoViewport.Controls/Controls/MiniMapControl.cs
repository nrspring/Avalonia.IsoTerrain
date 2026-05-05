using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Controls.Controls;

public sealed class MiniMapControl : Control
{
    public static readonly StyledProperty<TileMap?> TileMapProperty =
        AvaloniaProperty.Register<MiniMapControl, TileMap?>(nameof(TileMap));

    public static readonly StyledProperty<float> CameraZoomProperty =
        AvaloniaProperty.Register<MiniMapControl, float>(nameof(CameraZoom), 1f);

    public static readonly StyledProperty<float> CameraPanXProperty =
        AvaloniaProperty.Register<MiniMapControl, float>(nameof(CameraPanX), 0f);

    public static readonly StyledProperty<float> CameraPanYProperty =
        AvaloniaProperty.Register<MiniMapControl, float>(nameof(CameraPanY), 0f);

    public static readonly StyledProperty<float> CameraRotationDegreesProperty =
        AvaloniaProperty.Register<MiniMapControl, float>(nameof(CameraRotationDegrees), 0f);

    public static readonly StyledProperty<ViewProjectionMode> ViewProjectionModeProperty =
        AvaloniaProperty.Register<MiniMapControl, ViewProjectionMode>(nameof(ViewProjectionMode), ViewProjectionMode.Isometric);

    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<MiniMapControl, double>(nameof(ViewportWidth), 0d);

    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<MiniMapControl, double>(nameof(ViewportHeight), 0d);

    public static readonly StyledProperty<MiniMapLocation> LocationProperty =
        AvaloniaProperty.Register<MiniMapControl, MiniMapLocation>(nameof(Location), MiniMapLocation.BottomRight);

    private static readonly SolidColorBrush FrameBrush = new(Color.Parse("#7A1B232D"));
    private static readonly SolidColorBrush PanelBrush = new(Color.Parse("#B0121820"));
    private static readonly SolidColorBrush ViewFillBrush = new(Color.Parse("#22FF3B30"));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.Parse("#D04A5563")), 1d);
    private static readonly Pen ViewPen = new(Brushes.Red, 2d);

    private TileMap? _observedTileMap;
    private WriteableBitmap? _bitmap;
    private bool _bitmapDirty = true;
    private bool _dragging;

    public MiniMapControl()
    {
        Width = 180;
        Height = 180;
        ApplyLocation(Location);
    }

    public TileMap? TileMap
    {
        get => GetValue(TileMapProperty);
        set => SetValue(TileMapProperty, value);
    }

    public float CameraZoom
    {
        get => GetValue(CameraZoomProperty);
        set => SetValue(CameraZoomProperty, value);
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
        set => SetValue(CameraRotationDegreesProperty, value);
    }

    public ViewProjectionMode ViewProjectionMode
    {
        get => GetValue(ViewProjectionModeProperty);
        set => SetValue(ViewProjectionModeProperty, value);
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

    public MiniMapLocation Location
    {
        get => GetValue(LocationProperty);
        set => SetValue(LocationProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TileMapProperty)
        {
            ObserveTileMap(TileMap);
            _bitmapDirty = true;
            InvalidateVisual();
        }
        else if (change.Property == LocationProperty)
        {
            ApplyLocation(Location);
        }
        else if (change.Property == CameraZoomProperty ||
                 change.Property == CameraPanXProperty ||
                 change.Property == CameraPanYProperty ||
                 change.Property == CameraRotationDegreesProperty ||
                 change.Property == ViewProjectionModeProperty ||
                 change.Property == ViewportWidthProperty ||
                 change.Property == ViewportHeightProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var frameRect = new Rect(Bounds.Size);

        if (frameRect.Width <= 0d || frameRect.Height <= 0d)
        {
            return;
        }

        context.DrawRectangle(PanelBrush, null, frameRect);

        if (TileMap is not { } map)
        {
            context.DrawRectangle(null, BorderPen, frameRect.Deflate(1d));
            return;
        }

        EnsureBitmap(map);

        if (_bitmap is null)
        {
            return;
        }

        var mapRect = GetMapRect(map, frameRect.Deflate(6d));

        context.DrawRectangle(FrameBrush, null, mapRect.Inflate(1d));
        context.DrawImage(
            _bitmap,
            new Rect(0d, 0d, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
            mapRect);
        context.DrawRectangle(null, BorderPen, mapRect);

        DrawVisibleArea(context, map, mapRect);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ObserveTileMap(null);
        _bitmap?.Dispose();
        _bitmap = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (TileMap is not { } map)
        {
            return;
        }

        var mapRect = GetMapRect(map, new Rect(Bounds.Size).Deflate(6d));
        var position = e.GetPosition(this);

        if (!mapRect.Contains(position))
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragging = true;
        e.Pointer.Capture(this);
        RecenterFromPosition(position, map, mapRect);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_dragging || TileMap is not { } map)
        {
            return;
        }

        var mapRect = GetMapRect(map, new Rect(Bounds.Size).Deflate(6d));
        var position = e.GetPosition(this);
        var clamped = new Point(
            Math.Clamp(position.X, mapRect.X, mapRect.Right),
            Math.Clamp(position.Y, mapRect.Y, mapRect.Bottom));

        RecenterFromPosition(clamped, map, mapRect);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void DrawVisibleArea(DrawingContext context, TileMap map, Rect mapRect)
    {
        var viewportWidth = Math.Max(1f, (float)ViewportWidth);
        var viewportHeight = Math.Max(1f, (float)ViewportHeight);

        if (CameraZoom <= 0f || viewportWidth <= 0f || viewportHeight <= 0f)
        {
            return;
        }

        var visible = IsoMath.GetVisibleTileBounds(
            CameraPanX,
            CameraPanY,
            CameraZoom,
            CameraRotationDegrees,
            viewportWidth,
            viewportHeight,
            ViewProjectionMode);

        var left = Math.Clamp(visible.Left, 0f, map.Cols);
        var right = Math.Clamp(visible.Right, 0f, map.Cols);
        var top = Math.Clamp(visible.Top, 0f, map.Rows);
        var bottom = Math.Clamp(visible.Bottom, 0f, map.Rows);

        if (right <= left || bottom <= top)
        {
            return;
        }

        var viewRect = new Rect(
            mapRect.X + ((left / map.Cols) * mapRect.Width),
            mapRect.Y + ((top / map.Rows) * mapRect.Height),
            Math.Max(2d, ((right - left) / map.Cols) * mapRect.Width),
            Math.Max(2d, ((bottom - top) / map.Rows) * mapRect.Height));

        context.DrawRectangle(ViewFillBrush, ViewPen, viewRect);
    }

    private Rect GetMapRect(TileMap map, Rect availableRect)
    {
        var scale = Math.Min(availableRect.Width / map.Cols, availableRect.Height / map.Rows);
        var width = map.Cols * scale;
        var height = map.Rows * scale;
        var x = availableRect.X + ((availableRect.Width - width) * 0.5d);
        var y = availableRect.Y + ((availableRect.Height - height) * 0.5d);
        return new Rect(x, y, width, height);
    }

    private unsafe void EnsureBitmap(TileMap map)
    {
        if (_bitmap is not null &&
            _bitmap.PixelSize.Width == map.Cols &&
            _bitmap.PixelSize.Height == map.Rows &&
            !_bitmapDirty)
        {
            return;
        }

        if (_bitmap is null ||
            _bitmap.PixelSize.Width != map.Cols ||
            _bitmap.PixelSize.Height != map.Rows)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(map.Cols, map.Rows),
                new Vector(96d, 96d),
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);
        }

        using var locked = _bitmap.Lock();
        var buffer = new Span<byte>((void*)locked.Address, locked.RowBytes * locked.Size.Height);

        for (var row = 0; row < map.Rows; row++)
        {
            for (var col = 0; col < map.Cols; col++)
            {
                WriteTilePixel(buffer, locked.RowBytes, map, row, col);
            }
        }

        _bitmapDirty = false;
    }

    private void ObserveTileMap(TileMap? map)
    {
        if (ReferenceEquals(_observedTileMap, map))
        {
            return;
        }

        if (_observedTileMap is not null)
        {
            _observedTileMap.TileChanged -= OnTileChanged;
        }

        _observedTileMap = map;

        if (_observedTileMap is not null)
        {
            _observedTileMap.TileChanged += OnTileChanged;
        }
    }

    private void OnTileChanged(int row, int col)
    {
        if (TileMap is { } map &&
            _bitmap is not null &&
            _bitmap.PixelSize.Width == map.Cols &&
            _bitmap.PixelSize.Height == map.Rows)
        {
            UpdateBitmapTile(map, row, col);
        }
        else
        {
            _bitmapDirty = true;
        }

        InvalidateVisual();
    }

    private unsafe void UpdateBitmapTile(TileMap map, int row, int col)
    {
        if ((uint)row >= (uint)map.Rows || (uint)col >= (uint)map.Cols || _bitmap is null)
        {
            _bitmapDirty = true;
            return;
        }

        using var locked = _bitmap.Lock();
        var buffer = new Span<byte>((void*)locked.Address, locked.RowBytes * locked.Size.Height);
        WriteTilePixel(buffer, locked.RowBytes, map, row, col);
        _bitmapDirty = false;
    }

    private void ApplyLocation(MiniMapLocation location)
    {
        const double overlayMargin = 14d;
        Margin = new Thickness(overlayMargin);

        switch (location)
        {
            case MiniMapLocation.TopLeft:
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                break;
            case MiniMapLocation.TopRight:
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                break;
            case MiniMapLocation.BottomLeft:
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                break;
            default:
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
                break;
        }
    }

    private void RecenterFromPosition(Point position, TileMap map, Rect mapRect)
    {
        var viewportWidth = Math.Max(1f, (float)ViewportWidth);
        var viewportHeight = Math.Max(1f, (float)ViewportHeight);

        if (CameraZoom <= 0f || viewportWidth <= 0f || viewportHeight <= 0f)
        {
            return;
        }

        var tileX = (float)(((position.X - mapRect.X) / mapRect.Width) * map.Cols);
        var tileY = (float)(((position.Y - mapRect.Y) / mapRect.Height) * map.Rows);
        var target = IsoMath.TileToScreen(tileX, tileY, 0f, CameraRotationDegrees, ViewProjectionMode) * CameraZoom;
        var viewportCentre = new Point(viewportWidth * 0.5f, viewportHeight * 0.5f);

        SetCurrentValue(CameraPanXProperty, (float)viewportCentre.X - target.X);
        SetCurrentValue(CameraPanYProperty, (float)viewportCentre.Y - target.Y);
    }

    private static byte ToByte(float channel)
    {
        return (byte)(Math.Clamp(channel, 0f, 1f) * byte.MaxValue);
    }

    internal static void WriteTilePixel(Span<byte> buffer, int rowBytes, TileMap map, int row, int col)
    {
        var colour = TileColours.GetFaceColours(map.TileType[row, col], map.Elevation[row, col]).top;
        var offset = (row * rowBytes) + (col * 4);
        buffer[offset] = ToByte(colour.X);
        buffer[offset + 1] = ToByte(colour.Y);
        buffer[offset + 2] = ToByte(colour.Z);
        buffer[offset + 3] = 255;
    }
}
