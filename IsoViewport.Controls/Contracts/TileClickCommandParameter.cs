using Avalonia.Input;

namespace IsoViewport.Controls.Contracts;

public sealed record TileClickCommandParameter(
    TileCoordinate Tile,
    MouseButton Button,
    KeyModifiers KeyModifiers);
