using System.Numerics;
using System.Drawing;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class IsoMathTests
{
    [Theory]
    [InlineData(0, 0, 0, 0f, 0f)]
    [InlineData(1, 0, 0, 32f, 16f)]
    [InlineData(0, 1, 0, -32f, 16f)]
    [InlineData(1, 1, 1, 0f, 16f)]
    public void TileToScreenReturnsExpectedCoordinates(int col, int row, int elev, float expectedX, float expectedY)
    {
        var screen = IsoMath.TileToScreen(col, row, elev);

        Assert.Equal(expectedX, screen.X);
        Assert.Equal(expectedY, screen.Y);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(12, 7)]
    [InlineData(999, 999)]
    public void ScreenToTileRoundTripsTileToScreenAtZoomOne(int col, int row)
    {
        var screen = IsoMath.TileToScreen(col, row, 0);
        var tile = IsoMath.ScreenToTile(screen, 1f);

        AssertClose(col, tile.X);
        AssertClose(row, tile.Y);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(1f)]
    [InlineData(4f)]
    public void ScreenToTileRoundTripsTileToScreenAtZoom(float zoom)
    {
        const int col = 17;
        const int row = 23;

        var screen = IsoMath.TileToScreen(col, row, 0) * zoom;
        var tile = IsoMath.ScreenToTile(screen, zoom);

        AssertClose(col, tile.X);
        AssertClose(row, tile.Y);
    }

    [Theory]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    public void ScreenToTileRoundTripsTileToScreenAtQuarterTurns(float rotationDegrees)
    {
        const int col = 11;
        const int row = 7;

        var screen = IsoMath.TileToScreen(col, row, 0, rotationDegrees);
        var tile = IsoMath.ScreenToTile(screen, 1f, rotationDegrees);

        AssertClose(col, tile.X);
        AssertClose(row, tile.Y);
    }

    [Fact]
    public void TopDownProjectionUsesSquareGridCoordinates()
    {
        var screen = IsoMath.TileToScreen(2, 3, 7, projectionMode: ViewProjectionMode.TopDown);

        Assert.Equal(64f, screen.X);
        Assert.Equal(96f, screen.Y);
    }

    [Fact]
    public void TopDownScreenToTileRoundTripsAtQuarterTurns()
    {
        const int col = 8;
        const int row = 5;
        const float rotationDegrees = 90f;

        var screen = IsoMath.TileToScreen(col, row, 4, rotationDegrees, ViewProjectionMode.TopDown);
        var tile = IsoMath.ScreenToTile(screen, 1f, rotationDegrees, ViewProjectionMode.TopDown);

        AssertClose(col, tile.X);
        AssertClose(row, tile.Y);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(135f)]
    [InlineData(180f)]
    [InlineData(225f)]
    [InlineData(270f)]
    [InlineData(315f)]
    public void TileDepthStaysInsideClipRangeForRotatedMaps(float rotationDegrees)
    {
        const int mapSize = 1000;

        foreach (var (col, row) in new[] { (0, 0), (999, 0), (0, 999), (999, 999), (500, 500) })
        {
            var depth = IsoMath.TileDepth(col, row, TileMap.MaxElevation, mapSize, rotationDegrees);

            Assert.InRange(depth, -1f, 1f);
        }
    }

    [Fact]
    public void FitMapToViewportCentersAndExpandsFlatMap()
    {
        var map = TileMapPresets.Flat(20, 20);
        var fitted = IsoMath.FitMapToViewport(map, 1900f, 1100f);

        Assert.InRange(fitted.Zoom, 1.47f, 1.49f);
        Assert.InRange(fitted.PanX, 950f, 950f);
        Assert.InRange(fitted.PanY, 122f, 123f);
    }

    [Fact]
    public void GridVertexElevationAveragesAdjacentTiles()
    {
        var map = TileMapPresets.Flat(2, 2);
        map.SetTile(0, 0, (byte)TileType.Grass, 0);
        map.SetTile(0, 1, (byte)TileType.Grass, 4);
        map.SetTile(1, 0, (byte)TileType.Grass, 8);
        map.SetTile(1, 1, (byte)TileType.Grass, 12);

        AssertClose(6f, IsoMath.GridVertexElevation(map, 1, 1));
    }

    [Fact]
    public void SmoothedTopFaceCornersShareEdgesBetweenAdjacentTiles()
    {
        var map = TileMapPresets.Flat(2, 2);
        map.SetTile(0, 0, (byte)TileType.Grass, 0);
        map.SetTile(0, 1, (byte)TileType.Grass, 5);
        map.SetTile(1, 0, (byte)TileType.Grass, 2);
        map.SetTile(1, 1, (byte)TileType.Grass, 8);

        var leftTile = IsoMath.SmoothedTopFaceCorners(map, 0, 0, 1f);
        var rightTile = IsoMath.SmoothedTopFaceCorners(map, 1, 0, 1f);

        AssertClose(leftTile[1].X, rightTile[0].X);
        AssertClose(leftTile[1].Y, rightTile[0].Y);
        AssertClose(leftTile[2].X, rightTile[3].X);
        AssertClose(leftTile[2].Y, rightTile[3].Y);
    }

    [Fact]
    public void TryPickTileFindsFlatTileAtItsCentre()
    {
        var map = TileMapPresets.Flat(3, 3);
        var centre = IsoMath.TileToScreen(1, 1, TileMap.LandMinElevation);

        var found = IsoMath.TryPickTile(map, centre, 1f, out var col, out var row);

        Assert.True(found);
        Assert.Equal(1, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void TryPickTileFindsElevatedSmoothedTileAtItsCentre()
    {
        var map = TileMapPresets.Flat(3, 3);
        map.SetTile(1, 1, (byte)TileType.Grass, 8);
        map.SetTile(0, 1, (byte)TileType.Grass, 6);
        map.SetTile(1, 0, (byte)TileType.Grass, 6);
        var corners = IsoMath.SmoothedTopFaceCorners(map, 1, 1, 1f);
        var centre = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;

        var found = IsoMath.TryPickTile(map, centre, 1f, out var col, out var row);

        Assert.True(found);
        Assert.Equal(1, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void TryPickTileFindsVeryHighTileAtItsCentre()
    {
        var map = TileMapPresets.Flat(3, 3);
        map.SetTile(1, 1, (byte)TileType.Grass, 80);
        map.SetTile(0, 1, (byte)TileType.Grass, 60);
        map.SetTile(1, 0, (byte)TileType.Grass, 60);
        var corners = IsoMath.SmoothedTopFaceCorners(map, 1, 1, 1f);
        var centre = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;

        var found = IsoMath.TryPickTile(map, centre, 1f, out var col, out var row);

        Assert.True(found);
        Assert.Equal(1, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void TryPickTileFindsRotatedTileAtItsCentre()
    {
        var map = TileMapPresets.Flat(3, 3);
        var centre = IsoMath.TileToScreen(1, 1, TileMap.LandMinElevation, 90f);

        var found = IsoMath.TryPickTile(map, centre, 1f, out var col, out var row, 90f);

        Assert.True(found);
        Assert.Equal(1, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void TryPickTileFindsTopDownTileAtItsCentre()
    {
        var map = TileMapPresets.Flat(3, 3);
        var centre = IsoMath.TileToScreen(1, 1, 9, projectionMode: ViewProjectionMode.TopDown);

        var found = IsoMath.TryPickTile(map, centre, 1f, out var col, out var row, projectionMode: ViewProjectionMode.TopDown);

        Assert.True(found);
        Assert.Equal(1, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void GetVisibleTileBoundsReturnsPositiveArea()
    {
        var visible = IsoMath.GetVisibleTileBounds(
            panX: 400f,
            panY: 280f,
            zoom: 2f,
            rotationDegrees: 0f,
            viewportWidth: 800f,
            viewportHeight: 600f);

        Assert.True(visible.Width > 0f);
        Assert.True(visible.Height > 0f);
    }

    [Fact]
    public void TopDownVisibleTileBoundsReturnsPositiveArea()
    {
        var visible = IsoMath.GetVisibleTileBounds(
            panX: 240f,
            panY: 160f,
            zoom: 2f,
            rotationDegrees: 90f,
            viewportWidth: 800f,
            viewportHeight: 600f,
            projectionMode: ViewProjectionMode.TopDown);

        Assert.True(visible.Width > 0f);
        Assert.True(visible.Height > 0f);
    }

    [Fact]
    public void FitMapToViewportAllowsLargeMapsToZoomBelowQuarterScale()
    {
        var map = TileMapPresets.RealisticWorld(600, 1200);
        var fitted = IsoMath.FitMapToViewport(map, 1600f, 900f);

        Assert.True(fitted.Zoom < 0.25f);
        Assert.True(fitted.Zoom >= IsoCamera.MinZoom);
    }

    [Fact]
    public void BoundsIntersectReturnsTrueForOverlappingRectangles()
    {
        var left = new RectangleF(-10f, -10f, 30f, 30f);
        var right = new RectangleF(15f, 5f, 20f, 20f);

        Assert.True(IsoMath.BoundsIntersect(left, right));
    }

    [Fact]
    public void BoundsIntersectReturnsFalseForSeparatedRectangles()
    {
        var left = new RectangleF(-10f, -10f, 10f, 10f);
        var right = new RectangleF(5f, 5f, 10f, 10f);

        Assert.False(IsoMath.BoundsIntersect(left, right));
    }

    private static void AssertClose(float expected, float actual)
    {
        Assert.True(Math.Abs(expected - actual) < 0.0001f, $"Expected {expected}, actual {actual}");
    }
}
