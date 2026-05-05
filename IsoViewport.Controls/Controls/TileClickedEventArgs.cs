using Avalonia.Input;

namespace IsoViewport.Controls.Controls;

public sealed class TileClickedEventArgs : EventArgs
{
    public TileClickedEventArgs(int col, int row, MouseButton button)
    {
        Col = col;
        Row = row;
        Button = button;
    }

    public int Col { get; }

    public int Row { get; }

    public MouseButton Button { get; }
}
