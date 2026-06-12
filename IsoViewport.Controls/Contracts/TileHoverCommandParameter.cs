using Avalonia.Input;

namespace IsoViewport.Controls.Contracts;

public sealed record TileHoverCommandParameter(
    TileCoordinate Tile,
    KeyModifiers KeyModifiers);
