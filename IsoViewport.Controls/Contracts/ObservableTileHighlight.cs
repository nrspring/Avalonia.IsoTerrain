using Avalonia.Media;

namespace IsoViewport.Controls.Contracts;

public class ObservableTileHighlight : ObservableViewportObject, ITileHighlight
{
    private TileCoordinate _tile;
    private Color _color;

    public ObservableTileHighlight()
    {
    }

    public ObservableTileHighlight(TileCoordinate tile, Color color)
    {
        _tile = tile;
        _color = color;
    }

    public TileCoordinate Tile
    {
        get => _tile;
        set => SetProperty(ref _tile, value);
    }

    public Color Color
    {
        get => _color;
        set => SetProperty(ref _color, value);
    }
}
