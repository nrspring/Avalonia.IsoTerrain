using Avalonia.Input;
using Avalonia.Media;
using IsoViewport.Controls.Contracts;
using Xunit;

namespace IsoViewport.Tests;

public sealed class ViewportContractTests
{
    [Fact]
    public void TileCoordinateStoresRowAndColumn()
    {
        var coordinate = new TileCoordinate(12, 8);

        Assert.Equal(12, coordinate.Row);
        Assert.Equal(8, coordinate.Column);
        Assert.Equal("12,8", coordinate.ToString());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void TileCoordinateRejectsNegativeValues(int row, int column)
    {
        Assert.Throws<IsoViewportValidationException>(() => new TileCoordinate(row, column));
    }

    [Fact]
    public void PieceOrientationValuesMatchDegrees()
    {
        Assert.Equal(0, (int)PieceOrientation.Degrees0);
        Assert.Equal(90, (int)PieceOrientation.Degrees90);
        Assert.Equal(180, (int)PieceOrientation.Degrees180);
        Assert.Equal(270, (int)PieceOrientation.Degrees270);
    }

    [Fact]
    public void TileHoverCommandParameterCarriesTileAndModifiers()
    {
        var tile = new TileCoordinate(3, 4);
        var parameter = new TileHoverCommandParameter(tile, KeyModifiers.Shift | KeyModifiers.Control);

        Assert.Equal(tile, parameter.Tile);
        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Control, parameter.KeyModifiers);
    }

    [Fact]
    public void TileClickCommandParameterCarriesTileButtonAndModifiers()
    {
        var tile = new TileCoordinate(5, 6);
        var parameter = new TileClickCommandParameter(tile, MouseButton.Right, KeyModifiers.Alt);

        Assert.Equal(tile, parameter.Tile);
        Assert.Equal(MouseButton.Right, parameter.Button);
        Assert.Equal(KeyModifiers.Alt, parameter.KeyModifiers);
    }

    [Fact]
    public void RendererExceptionPreservesContext()
    {
        var inner = new InvalidOperationException("Renderer failed.");
        var exception = new IsoViewportRendererException(
            "Could not render piece.",
            inner,
            "piece-1",
            "unit",
            "Unit");

        Assert.Same(inner, exception.InnerException);
        Assert.Equal("piece-1", exception.PieceId);
        Assert.Equal("unit", exception.PieceTypeId);
        Assert.Equal("Unit", exception.PieceTypeDisplayName);
    }

    [Fact]
    public void ObservableMapPieceImplementsPieceContractAndRaisesChanges()
    {
        var piece = new ObservableMapPiece("piece-1", "unit", new TileCoordinate(1, 2));
        var changed = new List<string?>();
        piece.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        piece.Tile = new TileCoordinate(3, 4);
        piece.Orientation = PieceOrientation.Degrees90;
        piece.IsVisible = false;
        piece.Metadata = new Dictionary<string, string>
        {
            ["faction"] = "blue",
        };

        Assert.IsAssignableFrom<IMapPiece>(piece);
        Assert.Equal(new TileCoordinate(3, 4), piece.Tile);
        Assert.Equal(PieceOrientation.Degrees90, piece.Orientation);
        Assert.False(piece.IsVisible);
        Assert.Contains(nameof(ObservableMapPiece.Tile), changed);
        Assert.Contains(nameof(ObservableMapPiece.Orientation), changed);
        Assert.Contains(nameof(ObservableMapPiece.IsVisible), changed);
        Assert.Contains(nameof(ObservableMapPiece.Metadata), changed);
    }

    [Fact]
    public void ObservableMapPieceTypeDefinitionImplementsTypeContractAndRaisesChanges()
    {
        var type = new ObservableMapPieceTypeDefinition("unit", "Unit", 10, NullMapPieceRenderer.Instance);
        var changed = new List<string?>();
        type.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        type.DisplayName = "Army Unit";
        type.DefaultZLayer = 20;

        Assert.IsAssignableFrom<IMapPieceTypeDefinition>(type);
        Assert.Equal("unit", type.TypeId);
        Assert.Equal("Army Unit", type.DisplayName);
        Assert.Equal(20, type.DefaultZLayer);
        Assert.Same(NullMapPieceRenderer.Instance, type.Renderer);
        Assert.Contains(nameof(ObservableMapPieceTypeDefinition.DisplayName), changed);
        Assert.Contains(nameof(ObservableMapPieceTypeDefinition.DefaultZLayer), changed);
    }

    [Fact]
    public void ObservableTileHighlightImplementsHighlightContractAndRaisesChanges()
    {
        var highlight = new ObservableTileHighlight(new TileCoordinate(1, 1), Colors.Gold);
        var changed = new List<string?>();
        highlight.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        highlight.Tile = new TileCoordinate(2, 3);
        highlight.Color = Colors.DeepSkyBlue;

        Assert.IsAssignableFrom<ITileHighlight>(highlight);
        Assert.Equal(new TileCoordinate(2, 3), highlight.Tile);
        Assert.Equal(Colors.DeepSkyBlue, highlight.Color);
        Assert.Contains(nameof(ObservableTileHighlight.Tile), changed);
        Assert.Contains(nameof(ObservableTileHighlight.Color), changed);
    }
}
