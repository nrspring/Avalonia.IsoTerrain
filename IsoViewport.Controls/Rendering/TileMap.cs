namespace IsoViewport.Controls.Rendering;

public sealed class TileMap
{
    public const byte DeepWaterElevation = 0;
    public const byte ShallowWaterElevation = 1;
    public const byte LandMinElevation = 2;
    public const byte MaxElevation = 100;

    public TileMap(int rows, int cols)
    {
        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "Rows must be greater than zero.");
        }

        if (cols <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cols), cols, "Columns must be greater than zero.");
        }

        Rows = rows;
        Cols = cols;
        TileType = new byte[rows, cols];
        Elevation = new byte[rows, cols];

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                TileType[row, col] = (byte)global::IsoViewport.Controls.Rendering.TileType.Grass;
                Elevation[row, col] = LandMinElevation;
            }
        }
    }

    public int Rows { get; }

    public int Cols { get; }

    public byte[,] TileType { get; }

    public byte[,] Elevation { get; }

    public event Action<int, int>? TileChanged;

    public static bool IsWaterElevation(int elevation)
    {
        return elevation <= ShallowWaterElevation;
    }

    public void SetTile(int row, int col, byte type, byte elev)
    {
        if ((uint)row >= (uint)Rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "Row is outside the tile map.");
        }

        if ((uint)col >= (uint)Cols)
        {
            throw new ArgumentOutOfRangeException(nameof(col), col, "Column is outside the tile map.");
        }

        var clampedElevation = elev > MaxElevation ? MaxElevation : elev;

        if (TileType[row, col] == type && Elevation[row, col] == clampedElevation)
        {
            return;
        }

        TileType[row, col] = type;
        Elevation[row, col] = clampedElevation;
        TileChanged?.Invoke(row, col);
    }
}
