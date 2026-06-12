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

    float CameraRotationDegrees { get; }

    Point TileTopCenter { get; }

    Rect TileBounds { get; }

    Point ProjectTilePoint(float column, float row, float elevation);
}
