using System.ComponentModel;
using Avalonia.Media;

namespace IsoViewport.Controls.Contracts;

public interface ITileHighlight : INotifyPropertyChanged
{
    TileCoordinate Tile { get; }

    Color Color { get; }
}
