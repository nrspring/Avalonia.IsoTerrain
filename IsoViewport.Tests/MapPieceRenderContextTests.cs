using Avalonia;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class MapPieceRenderContextTests
{
    [Fact]
    public void ContextCapturesTileCameraAndRenderValues()
    {
        var map = CreateMap();
        var context = CreateContext(
            map,
            new TileCoordinate(1, 2),
            TerrainRenderMode.ShadedRelief,
            ViewProjectionMode.TopDown,
            cameraZoom: 1.75f,
            cameraPanX: 20f,
            cameraPanY: -8f,
            cameraRotationDegrees: 450f);

        Assert.Equal(new TileCoordinate(1, 2), context.Tile);
        Assert.Equal(map.TileType[1, 2], context.TileType);
        Assert.Equal(map.Elevation[1, 2], context.TileElevation);
        Assert.Equal(TerrainRenderMode.ShadedRelief, context.RenderMode);
        Assert.Equal(ViewProjectionMode.TopDown, context.ProjectionMode);
        Assert.Equal(1.75f, context.CameraZoom);
        Assert.Equal(20f, context.CameraPanX);
        Assert.Equal(-8f, context.CameraPanY);
        Assert.Equal(90f, context.CameraRotationDegrees);
    }

    [Fact]
    public void CurrentTileGeometryMatchesHelperGeometry()
    {
        var context = CreateContext(CreateMap(), new TileCoordinate(1, 1));

        Assert.Equal(4, context.TileTopCorners.Count);
        Assert.Equal(context.GetTileTopCorners(context.Tile), context.TileTopCorners);
        Assert.Equal(context.GetTileTopCenter(context.Tile), context.TileTopCenter);
        Assert.Equal(context.GetTileBounds(context.Tile), context.TileBounds);

        foreach (var point in context.TileTopCorners)
        {
            Assert.True(context.TileBounds.Contains(point));
        }
    }

    [Fact]
    public void ProjectTilePointAppliesZoomPanAndRotation()
    {
        var map = CreateMap();
        var unrotated = CreateContext(
            map,
            new TileCoordinate(1, 1),
            cameraZoom: 1f,
            cameraPanX: 0f,
            cameraPanY: 0f,
            cameraRotationDegrees: 0f);
        var transformed = CreateContext(
            map,
            new TileCoordinate(1, 1),
            cameraZoom: 2f,
            cameraPanX: 10f,
            cameraPanY: 20f,
            cameraRotationDegrees: 90f);

        var unrotatedPoint = unrotated.ProjectTilePoint(1f, 1f, 3f);
        var transformedPoint = transformed.ProjectTilePoint(1f, 1f, 3f);

        Assert.NotEqual(unrotatedPoint, transformedPoint);
        Assert.Equal(
            new Point(unrotatedPoint.X * 2d + 10d, unrotatedPoint.Y * 2d + 20d),
            CreateContext(map, new TileCoordinate(1, 1), cameraZoom: 2f, cameraPanX: 10f, cameraPanY: 20f)
                .ProjectTilePoint(1f, 1f, 3f));
    }

    [Fact]
    public void ProjectionModeChangesTileGeometry()
    {
        var map = CreateMap();
        var threeD = CreateContext(map, new TileCoordinate(1, 1), projectionMode: ViewProjectionMode.ThreeD);
        var topDown = CreateContext(map, new TileCoordinate(1, 1), projectionMode: ViewProjectionMode.TopDown);

        Assert.NotEqual(threeD.TileTopCorners, topDown.TileTopCorners);
        Assert.NotEqual(threeD.TileBounds, topDown.TileBounds);
    }

    [Fact]
    public void InvalidContextInputsThrowRenderContextException()
    {
        var map = CreateMap();

        Assert.Throws<IsoViewportRenderContextException>(
            () => CreateContext(map, new TileCoordinate(4, 0)));
        Assert.Throws<IsoViewportRenderContextException>(
            () => CreateContext(map, new TileCoordinate(0, 0), cameraZoom: 0f));
        Assert.Throws<IsoViewportRenderContextException>(
            () => CreateContext(map, new TileCoordinate(0, 0), cameraZoom: float.NaN));
    }

    [Fact]
    public void InvalidHelperInputsThrowRenderContextException()
    {
        var context = CreateContext(CreateMap(), new TileCoordinate(1, 1));

        Assert.Throws<IsoViewportRenderContextException>(
            () => context.GetTileTopCorners(new TileCoordinate(0, 4)));
        Assert.Throws<IsoViewportRenderContextException>(
            () => context.ProjectTilePoint(float.PositiveInfinity, 0f, 0f));
    }

    [Fact]
    public void DrawingContextThrowsWhenContextWasCreatedForGeometryOnly()
    {
        var context = CreateContext(CreateMap(), new TileCoordinate(1, 1));

        Assert.Throws<IsoViewportRenderContextException>(() => context.DrawingContext);
    }

    [Fact]
    public void TrivialRendererCanUseContextWithoutTileMap()
    {
        var context = CreateContext(CreateMap(), new TileCoordinate(1, 1));
        var piece = new ObservableMapPiece("piece-1", "marker", new TileCoordinate(1, 1));
        var renderer = new ProbeRenderer();

        renderer.Render(context, piece);

        Assert.Equal(context.TileTopCenter, renderer.Center);
        Assert.Equal(context.TileBounds, renderer.Bounds);
    }

    private static MapPieceRenderContext CreateContext(
        TileMap map,
        TileCoordinate tile,
        TerrainRenderMode renderMode = TerrainRenderMode.Voxel,
        ViewProjectionMode projectionMode = ViewProjectionMode.ThreeD,
        float cameraZoom = 1f,
        float cameraPanX = 0f,
        float cameraPanY = 0f,
        float cameraRotationDegrees = 0f)
    {
        return new MapPieceRenderContext(
            map,
            drawingContext: null,
            tile,
            renderMode,
            projectionMode,
            cameraZoom,
            cameraPanX,
            cameraPanY,
            cameraRotationDegrees);
    }

    private static TileMap CreateMap()
    {
        var map = TileMapPresets.Flat(4, 4);
        map.SetTile(1, 2, (byte)TileType.Stone, 12);
        map.SetTile(1, 1, (byte)TileType.Grass, 8);
        return map;
    }

    private sealed class ProbeRenderer : IMapPieceRenderer
    {
        public Point Center { get; private set; }

        public Rect Bounds { get; private set; }

        public void Render(IMapPieceRenderContext context, IMapPiece piece)
        {
            Center = context.TileTopCenter;
            Bounds = context.TileBounds;
        }
    }
}
