using System.Collections.ObjectModel;
using Avalonia.Media;
using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;
using Xunit;
using ViewerControl = IsoViewport.Controls.Controls.IsoViewport;

namespace IsoViewport.Tests;

public sealed class IsoViewportRuntimeCatalogTests
{
    [Fact]
    public void NullRuntimeCollectionsAfterSetupLockRenderAsEmptyCatalogs()
    {
        var viewport = LockWithUnitType();

        viewport.Pieces = null;
        viewport.TileHighlights = null;

        Assert.Empty(viewport.RuntimePieceCatalog);
        Assert.Empty(viewport.RuntimeHighlightCatalog);
    }

    [Fact]
    public void RuntimePieceCatalogUsesNewestDuplicateId()
    {
        var viewport = LockWithUnitType();
        var older = new ObservableMapPiece("unit-1", "unit", new TileCoordinate(0, 0));
        var newer = new ObservableMapPiece("unit-1", "unit", new TileCoordinate(1, 1));

        viewport.Pieces = new[] { older, newer };

        Assert.Single(viewport.RuntimePieceCatalog);
        Assert.Same(newer, viewport.RuntimePieceCatalog["unit-1"]);
    }

    [Fact]
    public void RuntimeHighlightCatalogUsesNewestDuplicateTile()
    {
        var viewport = LockWithUnitType();
        var older = new ObservableTileHighlight(new TileCoordinate(0, 0), Colors.Red);
        var newer = new ObservableTileHighlight(new TileCoordinate(0, 0), Colors.Blue);

        viewport.TileHighlights = new[] { older, newer };

        Assert.Single(viewport.RuntimeHighlightCatalog);
        Assert.Same(newer, viewport.RuntimeHighlightCatalog[new TileCoordinate(0, 0)]);
    }

    [Fact]
    public void RuntimeHighlightCatalogIsProjectedIntoMainTerrainOnly()
    {
        var viewport = LockWithUnitType();
        var older = new ObservableTileHighlight(new TileCoordinate(0, 0), Colors.Red);
        var newer = new ObservableTileHighlight(new TileCoordinate(0, 0), Colors.Blue);

        viewport.TileHighlights = new[] { older, newer };

        var terrain = viewport.Children.OfType<IsoTileControl>().Single();
        var miniMap = viewport.Children.OfType<MiniMapControl>().Single();
        var projected = Assert.Single(terrain.TileHighlights!);
        Assert.Same(newer, projected);
        Assert.DoesNotContain(
            miniMap.GetType().GetProperties(),
            property => property.Name == nameof(viewport.TileHighlights));
    }

    [Fact]
    public void ObservablePieceCollectionChangesUpdateCatalog()
    {
        var viewport = LockWithUnitType();
        var pieces = new ObservableCollection<IMapPiece>();
        var piece = new ObservableMapPiece("unit-1", "unit", new TileCoordinate(0, 0));

        viewport.Pieces = pieces;
        pieces.Add(piece);

        Assert.Same(piece, viewport.RuntimePieceCatalog["unit-1"]);

        pieces.Remove(piece);

        Assert.Empty(viewport.RuntimePieceCatalog);
    }

    [Fact]
    public void ObservableHighlightCollectionChangesUpdateCatalog()
    {
        var viewport = LockWithUnitType();
        var highlights = new ObservableCollection<ITileHighlight>();
        var highlight = new ObservableTileHighlight(new TileCoordinate(0, 0), Colors.Yellow);

        viewport.TileHighlights = highlights;
        highlights.Add(highlight);

        Assert.Same(highlight, viewport.RuntimeHighlightCatalog[new TileCoordinate(0, 0)]);

        highlights.Remove(highlight);

        Assert.Empty(viewport.RuntimeHighlightCatalog);
    }

    [Fact]
    public void PieceItemChangesUpdateCatalog()
    {
        var viewport = LockWithUnitType();
        var piece = new ObservableMapPiece("unit-1", "unit", new TileCoordinate(0, 0));

        viewport.Pieces = new[] { piece };
        piece.Id = "unit-2";
        piece.Tile = new TileCoordinate(1, 1);

        Assert.False(viewport.RuntimePieceCatalog.ContainsKey("unit-1"));
        Assert.Same(piece, viewport.RuntimePieceCatalog["unit-2"]);
        Assert.Equal(new TileCoordinate(1, 1), viewport.RuntimePieceCatalog["unit-2"].Tile);
    }

    [Fact]
    public void HighlightItemChangesUpdateCatalog()
    {
        var viewport = LockWithUnitType();
        var highlight = new ObservableTileHighlight(new TileCoordinate(0, 0), Colors.Yellow);

        viewport.TileHighlights = new[] { highlight };
        highlight.Tile = new TileCoordinate(1, 1);

        Assert.False(viewport.RuntimeHighlightCatalog.ContainsKey(new TileCoordinate(0, 0)));
        Assert.Same(highlight, viewport.RuntimeHighlightCatalog[new TileCoordinate(1, 1)]);
    }

    [Fact]
    public void ReplacingRuntimeCollectionsDetachesOldCollectionAndItems()
    {
        var viewport = LockWithUnitType();
        var oldPiece = new ObservableMapPiece("unit-1", "unit", new TileCoordinate(0, 0));
        var oldPieces = new ObservableCollection<IMapPiece> { oldPiece };
        var newPiece = new ObservableMapPiece("unit-2", "unit", new TileCoordinate(1, 1));

        viewport.Pieces = oldPieces;
        viewport.Pieces = new[] { newPiece };
        oldPieces.Add(new ObservableMapPiece("unit-3", "unit", new TileCoordinate(2, 2)));
        oldPiece.Id = "changed";

        Assert.Single(viewport.RuntimePieceCatalog);
        Assert.Same(newPiece, viewport.RuntimePieceCatalog["unit-2"]);
    }

    [Fact]
    public void RuntimePieceValidationRejectsInvalidValues()
    {
        var viewport = LockWithUnitType();

        Assert.Throws<IsoViewportValidationException>(
            () => viewport.Pieces = new[] { new ObservableMapPiece(" ", "unit", new TileCoordinate(0, 0)) });
        Assert.Throws<IsoViewportValidationException>(
            () => viewport.Pieces = new[] { new ObservableMapPiece("unit-1", " ", new TileCoordinate(0, 0)) });
        Assert.Throws<IsoViewportValidationException>(
            () => viewport.Pieces = new[] { new ObservableMapPiece("unit-1", "missing", new TileCoordinate(0, 0)) });
        Assert.Throws<IsoViewportValidationException>(
            () => viewport.Pieces = new[] { new ObservableMapPiece("unit-1", "unit", new TileCoordinate(3, 0)) });
    }

    [Fact]
    public void InvisibleRuntimePiecesStillValidate()
    {
        var viewport = LockWithUnitType();
        var piece = new ObservableMapPiece("unit-1", "missing", new TileCoordinate(0, 0))
        {
            IsVisible = false,
        };

        Assert.Throws<IsoViewportValidationException>(() => viewport.Pieces = new[] { piece });
    }

    [Fact]
    public void RuntimeHighlightValidationRejectsOutOfBoundsCoordinates()
    {
        var viewport = LockWithUnitType();

        Assert.Throws<IsoViewportValidationException>(
            () => viewport.TileHighlights = new[]
            {
                new ObservableTileHighlight(new TileCoordinate(0, 3), Colors.Yellow),
            });
    }

    private static ViewerControl LockWithUnitType()
    {
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = new[]
            {
                new ObservableMapPieceTypeDefinition("unit", "Unit", 10, NullMapPieceRenderer.Instance),
            },
        };

        viewport.TileMap = TileMapPresets.Flat(3, 3);
        return viewport;
    }
}
