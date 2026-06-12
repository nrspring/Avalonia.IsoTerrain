using System.Collections.Generic;
using System.ComponentModel;

namespace IsoViewport.Controls.Contracts;

public interface IMapPiece : INotifyPropertyChanged
{
    string Id { get; }

    string TypeId { get; }

    TileCoordinate Tile { get; }

    int? ZLayerOverride { get; }

    bool IsVisible { get; }

    PieceOrientation Orientation { get; }

    IReadOnlyDictionary<string, string>? Metadata { get; }
}
