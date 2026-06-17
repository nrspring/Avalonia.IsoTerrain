using Avalonia;
using Avalonia.Media;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Controls.Contracts;

public interface IMapPieceRenderContext
{
    DrawingContext DrawingContext { get; }

    TileCoordinate Tile { get; }

    byte TileType { get; }

    byte TileElevation { get; }

    TerrainRenderMode RenderMode { get; }

    ViewProjectionMode ProjectionMode { get; }

    float CameraZoom { get; }

    float CameraPanX { get; }

    float CameraPanY { get; }

    float CameraRotationDegrees { get; }

    IReadOnlyList<Point> TileTopCorners { get; }

    Point TileTopCenter { get; }

    Rect TileBounds { get; }

    Point ProjectTilePoint(float column, float row, float elevation);

    IReadOnlyList<Point> GetTileTopCorners(TileCoordinate tile);

    Point GetTileTopCenter(TileCoordinate tile);

    Rect GetTileBounds(TileCoordinate tile);
}
