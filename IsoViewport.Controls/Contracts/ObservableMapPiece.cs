using System.Collections.Generic;

namespace IsoViewport.Controls.Contracts;

public class ObservableMapPiece : ObservableViewportObject, IMapPiece
{
    private string _id = string.Empty;
    private string _typeId = string.Empty;
    private TileCoordinate _tile;
    private int? _zLayerOverride;
    private bool _isVisible = true;
    private PieceOrientation _orientation;
    private IReadOnlyDictionary<string, string>? _metadata;

    public ObservableMapPiece()
    {
    }

    public ObservableMapPiece(string id, string typeId, TileCoordinate tile)
    {
        _id = id;
        _typeId = typeId;
        _tile = tile;
    }

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string TypeId
    {
        get => _typeId;
        set => SetProperty(ref _typeId, value);
    }

    public TileCoordinate Tile
    {
        get => _tile;
        set => SetProperty(ref _tile, value);
    }

    public int? ZLayerOverride
    {
        get => _zLayerOverride;
        set => SetProperty(ref _zLayerOverride, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public PieceOrientation Orientation
    {
        get => _orientation;
        set => SetProperty(ref _orientation, value);
    }

    public IReadOnlyDictionary<string, string>? Metadata
    {
        get => _metadata;
        set => SetProperty(ref _metadata, value);
    }
}
