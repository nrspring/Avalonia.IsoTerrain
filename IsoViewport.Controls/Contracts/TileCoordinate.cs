namespace IsoViewport.Controls.Contracts;

public readonly record struct TileCoordinate
{
    public TileCoordinate(int row, int column)
    {
        if (row < 0)
        {
            throw new IsoViewportValidationException("Tile row must be non-negative.");
        }

        if (column < 0)
        {
            throw new IsoViewportValidationException("Tile column must be non-negative.");
        }

        Row = row;
        Column = column;
    }

    public int Row { get; }

    public int Column { get; }

    public void Deconstruct(out int row, out int column)
    {
        row = Row;
        column = Column;
    }

    public override string ToString()
    {
        return $"{Row},{Column}";
    }
}
