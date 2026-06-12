using Avalonia.Input;

namespace IsoViewport.Controls.Controls;

public sealed class TileClickedEventArgs : EventArgs
{
    public TileClickedEventArgs(int col, int row, MouseButton button, KeyModifiers keyModifiers = KeyModifiers.None)
    {
        Col = col;
        Row = row;
        Button = button;
        KeyModifiers = keyModifiers;
    }

    public int Col { get; }

    public int Row { get; }

    public MouseButton Button { get; }

    public KeyModifiers KeyModifiers { get; }
}
