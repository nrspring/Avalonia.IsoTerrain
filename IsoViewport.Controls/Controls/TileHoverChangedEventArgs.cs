using Avalonia.Input;

namespace IsoViewport.Controls.Controls;

internal sealed class TileHoverChangedEventArgs : EventArgs
{
    public TileHoverChangedEventArgs((int Col, int Row)? tile, KeyModifiers keyModifiers)
    {
        Tile = tile;
        KeyModifiers = keyModifiers;
    }

    public (int Col, int Row)? Tile { get; }

    public KeyModifiers KeyModifiers { get; }
}
