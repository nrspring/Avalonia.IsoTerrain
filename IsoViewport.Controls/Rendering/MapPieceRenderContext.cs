using System.Numerics;
using Avalonia;
using Avalonia.Media;
using IsoViewport.Controls.Contracts;

namespace IsoViewport.Controls.Rendering;

internal sealed class MapPieceRenderContext : IMapPieceRenderContext
{
    private readonly TileMap _map;
    private readonly DrawingContext? _drawingContext;

    public MapPieceRenderContext(
        TileMap map,
        DrawingContext? drawingContext,
        TileCoordinate tile,
        TerrainRenderMode renderMode,
        ViewProjectionMode projectionMode,
        float cameraZoom,
        float cameraPanX,
        float cameraPanY,
        float cameraRotationDegrees)
    {
        _map = map ?? throw new IsoViewportRenderContextException("A tile map is required to create a piece render context.");
        ValidateTile(tile);
        ValidateFinite(cameraZoom, nameof(cameraZoom));
        ValidateFinite(cameraPanX, nameof(cameraPanX));
        ValidateFinite(cameraPanY, nameof(cameraPanY));
        ValidateFinite(cameraRotationDegrees, nameof(cameraRotationDegrees));

        if (cameraZoom <= 0f)
        {
            throw new IsoViewportRenderContextException("Camera zoom must be greater than zero.");
        }

        _drawingContext = drawingContext;
        Tile = tile;
        TileType = _map.TileType[tile.Row, tile.Column];
        TileElevation = _map.Elevation[tile.Row, tile.Column];
        RenderMode = renderMode;
        ProjectionMode = projectionMode;
        CameraZoom = cameraZoom;
        CameraPanX = cameraPanX;
        CameraPanY = cameraPanY;
        CameraRotationDegrees = IsoMath.NormalizeRotationDegrees(cameraRotationDegrees);
        TileTopCorners = BuildTileTopCorners(tile);
        TileTopCenter = Average(TileTopCorners);
        TileBounds = BoundsFrom(TileTopCorners);
    }

    public DrawingContext DrawingContext =>
        _drawingContext ?? throw new IsoViewportRenderContextException("DrawingContext is only available while a piece renderer is executing.");

    public TileCoordinate Tile { get; }

    public byte TileType { get; }

    public byte TileElevation { get; }

    public TerrainRenderMode RenderMode { get; }

    public ViewProjectionMode ProjectionMode { get; }

    public float CameraZoom { get; }

    public float CameraPanX { get; }

    public float CameraPanY { get; }

    public float CameraRotationDegrees { get; }

    public IReadOnlyList<Point> TileTopCorners { get; }

    public Point TileTopCenter { get; }

    public Rect TileBounds { get; }

    public Point ProjectTilePoint(float column, float row, float elevation)
    {
        ValidateFinite(column, nameof(column));
        ValidateFinite(row, nameof(row));
        ValidateFinite(elevation, nameof(elevation));
        return ToScreenPoint(IsoMath.TileToScreen(column, row, elevation, CameraRotationDegrees, ProjectionMode));
    }

    public IReadOnlyList<Point> GetTileTopCorners(TileCoordinate tile)
    {
        ValidateTile(tile);
        return BuildTileTopCorners(tile);
    }

    public Point GetTileTopCenter(TileCoordinate tile)
    {
        return Average(GetTileTopCorners(tile));
    }

    public Rect GetTileBounds(TileCoordinate tile)
    {
        return BoundsFrom(GetTileTopCorners(tile));
    }

    private Point[] BuildTileTopCorners(TileCoordinate tile)
    {
        var corners = RenderMode == TerrainRenderMode.Voxel
            ? IsoMath.TopFaceCorners(
                tile.Column,
                tile.Row,
                _map.Elevation[tile.Row, tile.Column],
                CameraZoom,
                CameraRotationDegrees,
                ProjectionMode)
            : IsoMath.SmoothedTopFaceCorners(
                _map,
                tile.Column,
                tile.Row,
                CameraZoom,
                CameraRotationDegrees,
                ProjectionMode);

        return
        [
            AddPan(corners[0]),
            AddPan(corners[1]),
            AddPan(corners[2]),
            AddPan(corners[3]),
        ];
    }

    private Point ToScreenPoint(Vector2 worldPoint)
    {
        return AddPan(worldPoint * CameraZoom);
    }

    private Point AddPan(Vector2 point)
    {
        return new Point(point.X + CameraPanX, point.Y + CameraPanY);
    }

    private void ValidateTile(TileCoordinate tile)
    {
        if ((uint)tile.Row >= (uint)_map.Rows || (uint)tile.Column >= (uint)_map.Cols)
        {
            throw new IsoViewportRenderContextException($"Tile coordinate '{tile}' is outside the loaded map.");
        }
    }

    private static void ValidateFinite(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new IsoViewportRenderContextException($"{name} must be a finite number.");
        }
    }

    private static Point Average(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            throw new IsoViewportRenderContextException("At least one projected point is required.");
        }

        var x = 0d;
        var y = 0d;

        foreach (var point in points)
        {
            x += point.X;
            y += point.Y;
        }

        return new Point(x / points.Count, y / points.Count);
    }

    private static Rect BoundsFrom(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            throw new IsoViewportRenderContextException("At least one projected point is required.");
        }

        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;

        foreach (var point in points)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
