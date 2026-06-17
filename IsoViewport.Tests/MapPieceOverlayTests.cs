using Avalonia;
using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class MapPieceOverlayTests
{
    [Fact]
    public void OverlayInvokesRenderersOnlyForVoxelAndReliefModes()
    {
        var renderer = new CountingRenderer();
        var overlay = CreateOverlay(renderer);

        overlay.RenderMode = TerrainRenderMode.Heat;
        overlay.RenderPieces(null);
        overlay.RenderMode = TerrainRenderMode.Topographical;
        overlay.RenderPieces(null);

        Assert.Equal(0, renderer.Count);

        overlay.RenderMode = TerrainRenderMode.Voxel;
        overlay.RenderPieces(null);
        overlay.RenderMode = TerrainRenderMode.ShadedRelief;
        overlay.RenderPieces(null);

        Assert.Equal(2, renderer.Count);
    }

    [Fact]
    public void RenderablePiecesSkipInvisiblePieces()
    {
        var map = TileMapPresets.Flat(3, 3);
        var visible = new ObservableMapPiece("visible", "unit", new TileCoordinate(0, 0));
        var hidden = new ObservableMapPiece("hidden", "unit", new TileCoordinate(0, 1))
        {
            IsVisible = false,
        };
        var renderables = MapPieceOverlay.BuildRenderablePieces(
            map,
            TypeDefinitions(("unit", 10, NullMapPieceRenderer.Instance)),
            [visible, hidden],
            0f);

        var renderable = Assert.Single(renderables);
        Assert.Same(visible, renderable.Piece);
    }

    [Fact]
    public void RenderablePiecesSortSameTileByEffectiveZLayer()
    {
        var map = TileMapPresets.Flat(3, 3);
        var bridge = new ObservableMapPiece("bridge", "bridge", new TileCoordinate(1, 1));
        var unit = new ObservableMapPiece("unit", "unit", new TileCoordinate(1, 1));
        var marker = new ObservableMapPiece("marker", "bridge", new TileCoordinate(1, 1))
        {
            ZLayerOverride = 200,
        };

        var renderables = MapPieceOverlay.BuildRenderablePieces(
            map,
            TypeDefinitions(
                ("bridge", 10, NullMapPieceRenderer.Instance),
                ("unit", 100, NullMapPieceRenderer.Instance)),
            [unit, marker, bridge],
            0f);

        Assert.Equal(["bridge", "unit", "marker"], renderables.Select(renderable => renderable.Piece.Id));
    }

    [Fact]
    public void RenderablePiecesSortBackTilesBeforeFrontTiles()
    {
        var map = TileMapPresets.Flat(4, 4);
        var back = new ObservableMapPiece("back", "unit", new TileCoordinate(0, 0));
        var front = new ObservableMapPiece("front", "unit", new TileCoordinate(3, 3));

        var renderables = MapPieceOverlay.BuildRenderablePieces(
            map,
            TypeDefinitions(("unit", 10, NullMapPieceRenderer.Instance)),
            [front, back],
            0f);

        Assert.Equal(["back", "front"], renderables.Select(renderable => renderable.Piece.Id));
        Assert.True(renderables[0].TileDepth > renderables[1].TileDepth);
    }

    [Fact]
    public void RendererExceptionIsWrappedWithPieceDiagnostics()
    {
        var inner = new InvalidOperationException("boom");
        var renderer = new ThrowingRenderer(inner);
        var overlay = CreateOverlay(renderer);

        var exception = Assert.Throws<IsoViewportRendererException>(() => overlay.RenderPieces(null));

        Assert.Same(inner, exception.InnerException);
        Assert.Equal("piece-1", exception.PieceId);
        Assert.Equal("unit", exception.PieceTypeId);
        Assert.Equal("Unit", exception.PieceTypeDisplayName);
        Assert.Contains("piece-1", exception.Message);
        Assert.Contains("unit", exception.Message);
        Assert.Contains("Unit", exception.Message);
    }

    [Fact]
    public void RenderContextPassedToRendererMatchesOverlayState()
    {
        var renderer = new CapturingRenderer();
        var overlay = CreateOverlay(renderer);
        overlay.RenderMode = TerrainRenderMode.ShadedRelief;
        overlay.ViewProjectionMode = ViewProjectionMode.TopDown;
        overlay.CameraZoom = 1.5f;
        overlay.CameraPanX = 12f;
        overlay.CameraPanY = 24f;
        overlay.CameraRotationDegrees = 90f;

        overlay.RenderPieces(null);

        Assert.NotNull(renderer.Context);
        Assert.Equal(TerrainRenderMode.ShadedRelief, renderer.Context.RenderMode);
        Assert.Equal(ViewProjectionMode.TopDown, renderer.Context.ProjectionMode);
        Assert.Equal(1.5f, renderer.Context.CameraZoom);
        Assert.Equal(12f, renderer.Context.CameraPanX);
        Assert.Equal(24f, renderer.Context.CameraPanY);
        Assert.Equal(90f, renderer.Context.CameraRotationDegrees);
        Assert.Equal(new TileCoordinate(1, 1), renderer.Context.Tile);
    }

    private static MapPieceOverlay CreateOverlay(IMapPieceRenderer renderer)
    {
        return new MapPieceOverlay
        {
            TileMap = TileMapPresets.Flat(3, 3),
            RenderMode = TerrainRenderMode.Voxel,
            PieceTypeDefinitions = TypeDefinitions(("unit", 10, renderer)),
            Pieces = new Dictionary<string, IMapPiece>(StringComparer.Ordinal)
            {
                ["piece-1"] = new ObservableMapPiece("piece-1", "unit", new TileCoordinate(1, 1)),
            },
        };
    }

    private static IReadOnlyDictionary<string, IMapPieceTypeDefinition> TypeDefinitions(
        params (string TypeId, int ZLayer, IMapPieceRenderer Renderer)[] definitions)
    {
        return definitions.ToDictionary(
            definition => definition.TypeId,
            definition => (IMapPieceTypeDefinition)new ObservableMapPieceTypeDefinition(
                definition.TypeId,
                ToDisplayName(definition.TypeId),
                definition.ZLayer,
                definition.Renderer),
            StringComparer.Ordinal);
    }

    private static string ToDisplayName(string typeId)
    {
        return typeId switch
        {
            "unit" => "Unit",
            "bridge" => "Bridge",
            _ => typeId,
        };
    }

    private sealed class CountingRenderer : IMapPieceRenderer
    {
        public int Count { get; private set; }

        public void Render(IMapPieceRenderContext context, IMapPiece piece)
        {
            Count++;
        }
    }

    private sealed class CapturingRenderer : IMapPieceRenderer
    {
        public IMapPieceRenderContext? Context { get; private set; }

        public void Render(IMapPieceRenderContext context, IMapPiece piece)
        {
            Context = context;
        }
    }

    private sealed class ThrowingRenderer : IMapPieceRenderer
    {
        private readonly Exception _exception;

        public ThrowingRenderer(Exception exception)
        {
            _exception = exception;
        }

        public void Render(IMapPieceRenderContext context, IMapPiece piece)
        {
            throw _exception;
        }
    }
}
