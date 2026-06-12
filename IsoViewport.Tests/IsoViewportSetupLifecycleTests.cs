using System.Collections.ObjectModel;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;
using Xunit;
using ViewerControl = IsoViewport.Controls.Controls.IsoViewport;

namespace IsoViewport.Tests;

public sealed class IsoViewportSetupLifecycleTests
{
    [Fact]
    public void AssigningTileMapBeforePieceTypeDefinitionsThrows()
    {
        var viewport = new ViewerControl();

        Assert.Throws<IsoViewportSetupException>(() => viewport.TileMap = TileMapPresets.Flat(2, 2));
        Assert.False(viewport.IsSetupLocked);
    }

    [Fact]
    public void EmptyPieceTypeDefinitionsCanLockSetup()
    {
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = Array.Empty<IMapPieceTypeDefinition>(),
        };

        viewport.TileMap = TileMapPresets.Flat(2, 2);

        Assert.True(viewport.IsSetupLocked);
    }

    [Fact]
    public void AssigningDynamicCollectionsBeforeSetupLockThrows()
    {
        var viewport = new ViewerControl();

        Assert.Throws<IsoViewportSetupException>(() => viewport.Pieces = Array.Empty<IMapPiece>());
        Assert.Throws<IsoViewportSetupException>(() => viewport.TileHighlights = Array.Empty<ITileHighlight>());
    }

    [Fact]
    public void NullDynamicCollectionsBeforeSetupLockAreAllowed()
    {
        var viewport = new ViewerControl
        {
            Pieces = null,
            TileHighlights = null,
        };

        Assert.Null(viewport.Pieces);
        Assert.Null(viewport.TileHighlights);
    }

    [Fact]
    public void ReplacingPieceTypeDefinitionsAfterSetupLockThrows()
    {
        var viewport = LockWithEmptyTypeDefinitions();

        Assert.Throws<IsoViewportSetupException>(
            () => viewport.PieceTypeDefinitions = new List<IMapPieceTypeDefinition>());
    }

    [Fact]
    public void MutatingPieceTypeDefinitionsAfterSetupLockThrows()
    {
        var definitions = new ObservableCollection<IMapPieceTypeDefinition>
        {
            new ObservableMapPieceTypeDefinition("unit", "Unit", 10, NullMapPieceRenderer.Instance),
        };
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = definitions,
        };

        viewport.TileMap = TileMapPresets.Flat(2, 2);

        Assert.Throws<IsoViewportSetupException>(
            () => definitions.Add(new ObservableMapPieceTypeDefinition("bridge", "Bridge", 5, NullMapPieceRenderer.Instance)));
    }

    [Fact]
    public void MutatingPieceTypeDefinitionItemAfterSetupLockThrows()
    {
        var definition = new ObservableMapPieceTypeDefinition("unit", "Unit", 10, NullMapPieceRenderer.Instance);
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = new[] { definition },
        };

        viewport.TileMap = TileMapPresets.Flat(2, 2);

        Assert.Throws<IsoViewportSetupException>(() => definition.DisplayName = "Changed");
    }

    [Fact]
    public void MutatingTileMapAfterSetupLockThrows()
    {
        var map = TileMapPresets.Flat(2, 2);
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = Array.Empty<IMapPieceTypeDefinition>(),
            TileMap = map,
        };

        Assert.True(viewport.IsSetupLocked);
        Assert.Throws<IsoViewportSetupException>(() => map.SetTile(0, 0, (byte)TileType.Water, 0));
    }

    [Fact]
    public void ReplacingTileMapAfterSetupLockThrows()
    {
        var viewport = LockWithEmptyTypeDefinitions();

        Assert.Throws<IsoViewportSetupException>(() => viewport.TileMap = TileMapPresets.Flat(3, 3));
    }

    [Fact]
    public void InvalidPieceTypeDefinitionThrowsWhenTileMapLocksSetup()
    {
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = new[]
            {
                new ObservableMapPieceTypeDefinition(" ", "Invalid", 0, NullMapPieceRenderer.Instance),
            },
        };

        Assert.Throws<IsoViewportValidationException>(() => viewport.TileMap = TileMapPresets.Flat(2, 2));
        Assert.False(viewport.IsSetupLocked);
    }

    [Fact]
    public void MissingRendererThrowsWhenTileMapLocksSetup()
    {
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = new[]
            {
                new ObservableMapPieceTypeDefinition("unit", "Unit", 0, null!),
            },
        };

        Assert.Throws<IsoViewportValidationException>(() => viewport.TileMap = TileMapPresets.Flat(2, 2));
        Assert.False(viewport.IsSetupLocked);
    }

    [Fact]
    public void DuplicatePieceTypeIdsUseLatestDefinition()
    {
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = new[]
            {
                new ObservableMapPieceTypeDefinition("unit", "Old Unit", 1, NullMapPieceRenderer.Instance),
                new ObservableMapPieceTypeDefinition("unit", "New Unit", 2, NullMapPieceRenderer.Instance),
            },
        };

        viewport.TileMap = TileMapPresets.Flat(2, 2);

        Assert.True(viewport.IsSetupLocked);
    }

    private static ViewerControl LockWithEmptyTypeDefinitions()
    {
        var viewport = new ViewerControl
        {
            PieceTypeDefinitions = Array.Empty<IMapPieceTypeDefinition>(),
        };

        viewport.TileMap = TileMapPresets.Flat(2, 2);
        return viewport;
    }
}
