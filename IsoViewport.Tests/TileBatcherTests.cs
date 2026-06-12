using System.Drawing;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class TileBatcherTests
{
    [Fact]
    public void FlatLandTileEmitsTopAndVisibleSideFaces()
    {
        var map = TileMapPresets.Flat(1, 1);
        var batch = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-100, -100, 200, 200), 0f, 0f);

        Assert.Equal(1, batch.VisibleTileCount);
        Assert.Equal(4 * 6 * 6, batch.Vertices.Length);
    }

    [Fact]
    public void ElevatedTileEmitsTopAndVisibleSideFaces()
    {
        var map = TileMapPresets.Flat(1, 1);
        map.SetTile(0, 0, 1, 3);

        var batch = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-100, -100, 200, 200), 0f, 0f);

        Assert.Equal(1, batch.VisibleTileCount);
        Assert.Equal(4 * 6 * 6, batch.Vertices.Length);
    }

    [Fact]
    public void TilesOutsideViewportAreCulled()
    {
        var map = TileMapPresets.Flat(20, 20);
        var full = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-1000, -1000, 2000, 2000), 0f, 0f);
        var culled = TileBatcher.BuildTileBatch(map, 4f, new RectangleF(0, 0, 64, 64), 0f, 0f);

        Assert.True(culled.VisibleTileCount < full.VisibleTileCount);
    }

    [Fact]
    public void TilesOutsidePannedViewportAreCulled()
    {
        var map = TileMapPresets.Flat(20, 20);
        var full = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(0, 0, 128, 128), 32f, 16f);
        var culled = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(0, 0, 128, 128), 4000f, 4000f);

        Assert.True(culled.VisibleTileCount < full.VisibleTileCount);
        Assert.Equal(0, culled.VisibleTileCount);
    }

    [Fact]
    public void BuildChunkVertexDataDoesNotViewportCull()
    {
        var map = TileMapPresets.Flat(40, 40);
        var vertices = TileBatcher.BuildChunkVertexData(map, 0, 0, 1f, false);

        Assert.Equal(32 * 32 * 2 * 6 * 6, vertices.Length);
    }

    [Fact]
    public void BuildChunkVertexDataSplitsAnimatedWaterTiles()
    {
        var map = TileMapPresets.Ocean(1, 2);
        map.SetTile(0, 1, (byte)TileType.Grass, TileMap.LandMinElevation);

        var staticVertices = TileBatcher.BuildChunkVertexData(map, 0, 0, 1f, false);
        var animVertices = TileBatcher.BuildChunkVertexData(map, 0, 0, 1f, true);

        Assert.Equal(4 * 6 * 6, staticVertices.Length);
        Assert.Equal(4 * 6 * 6, animVertices.Length);
    }

    [Fact]
    public void OceanInteriorWaterHasNoShoreMetadata()
    {
        var map = TileMapPresets.Ocean(4, 4);
        var vertices = TileBatcher.BuildChunkVertexData(map, 0, 0, 1f, true);

        AssertVertexColour(vertices, 12, 0f, 0f, 2f);
    }

    [Fact]
    public void ShorelineWaterCarriesDirectionalFoamMetadata()
    {
        var map = TileMapPresets.Ocean(1, 2);
        map.SetTile(0, 1, (byte)TileType.Grass, TileMap.LandMinElevation);

        var vertices = TileBatcher.BuildChunkVertexData(map, 0, 0, 1f, true);

        Assert.True(vertices.Length >= 18 * 3);
        var outerOffset = 3;
        AssertClose(1f, vertices[outerOffset]);
        AssertClose(0f, vertices[outerOffset + 1]);
        AssertClose(-10.1515f, vertices[outerOffset + 2], 0.001f);

        var middleOffset = 6 * 6 + 3;
        AssertClose(1f, vertices[middleOffset]);
        AssertClose(0f, vertices[middleOffset + 1]);
        AssertClose(-6.1515f, vertices[middleOffset + 2], 0.001f);

        var innerOffset = 12 * 6 + 3;
        AssertClose(1f, vertices[innerOffset]);
        AssertClose(0f, vertices[innerOffset + 1]);
        AssertClose(2.1515f, vertices[innerOffset + 2], 0.001f);
    }

    [Fact]
    public void TopFaceUsesTileTypeColour()
    {
        var map = TileMapPresets.Ocean(1, 1);
        var batch = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-100, -100, 200, 200), 0f, 0f);

        AssertVertexColour(batch.Vertices, 6, 0.08f, 0.24f, 0.45f);
    }

    [Fact]
    public void TopFaceBorderIsSlightlyDarkerThanFill()
    {
        var map = TileMapPresets.Flat(1, 1, (byte)TileType.Grass);
        var batch = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-100, -100, 200, 200), 0f, 0f);
        var borderOffset = 3;
        var fillOffset = 6 * 6 + 3;

        Assert.True(verticesGreater(batch.Vertices, fillOffset, borderOffset));
    }

    [Fact]
    public void LandTilesReceiveDeterministicNoiseVariation()
    {
        var map = TileMapPresets.Flat(1, 2, (byte)TileType.Grass);
        var batch = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-200, -100, 400, 200), 0f, 0f);

        var firstTileFillOffset = 6 * 6 + 3;
        var secondTileFillOffset = (2 * 6 * 6) + (6 * 6) + 3;

        Assert.False(
            NearlyEqual(batch.Vertices[firstTileFillOffset], batch.Vertices[secondTileFillOffset]) &&
            NearlyEqual(batch.Vertices[firstTileFillOffset + 1], batch.Vertices[secondTileFillOffset + 1]) &&
            NearlyEqual(batch.Vertices[firstTileFillOffset + 2], batch.Vertices[secondTileFillOffset + 2]));
    }

    [Fact]
    public void ElevatedTileUsesSideFaceShadows()
    {
        var map = TileMapPresets.Flat(1, 1, (byte)TileType.Stone);
        map.SetTile(0, 0, (byte)TileType.Stone, 2);
        var batch = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-100, -100, 200, 200), 0f, 0f);

        var topOffset = 6 * 6 + 3;
        var leftOffset = 12 * 6 + 3;
        var rightOffset = 18 * 6 + 3;

        Assert.True(batch.Vertices[leftOffset] < batch.Vertices[rightOffset]);
        Assert.True(batch.Vertices[rightOffset] < batch.Vertices[topOffset]);
        Assert.True(batch.Vertices[leftOffset + 1] < batch.Vertices[rightOffset + 1]);
        Assert.True(batch.Vertices[rightOffset + 1] < batch.Vertices[topOffset + 1]);
        Assert.True(batch.Vertices[leftOffset + 2] < batch.Vertices[rightOffset + 2]);
        Assert.True(batch.Vertices[rightOffset + 2] < batch.Vertices[topOffset + 2]);
    }

    [Fact]
    public void AdjacentElevatedTilesShareInteriorWall()
    {
        var map = TileMapPresets.Flat(1, 2, (byte)TileType.Grass);
        map.SetTile(0, 0, (byte)TileType.Grass, 3);
        map.SetTile(0, 1, (byte)TileType.Grass, 3);

        var batch = TileBatcher.BuildTileBatch(map, 1f, new RectangleF(-200, -200, 400, 400), 0f, 0f);

        Assert.Equal(2, batch.VisibleTileCount);
        Assert.Equal(7 * 6 * 6, batch.Vertices.Length);
    }

    [Fact]
    public void HeatModeContourBoundaryUsesDistinctBorderColour()
    {
        var map = TileMapPresets.Flat(1, 2, (byte)TileType.Grass);
        map.Elevation[0, 0] = 20;
        map.Elevation[0, 1] = 40;

        var batch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            TerrainRenderMode.Heat,
            ViewProjectionMode.ThreeD,
            false);

        var borderOffset = 3;
        var fillOffset = 6 * 6 + 3;
        Assert.False(NearlyEqual(batch.Vertices[borderOffset], batch.Vertices[fillOffset]));
        Assert.False(NearlyEqual(batch.Vertices[borderOffset + 1], batch.Vertices[fillOffset + 1]));
        Assert.False(NearlyEqual(batch.Vertices[borderOffset + 2], batch.Vertices[fillOffset + 2]));
    }

    [Fact]
    public void HeatModeInteriorTileKeepsFillBorderColour()
    {
        var map = TileMapPresets.Flat(1, 1, (byte)TileType.Grass);
        map.Elevation[0, 0] = 40;

        var batch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            TerrainRenderMode.Heat,
            ViewProjectionMode.ThreeD,
            false);

        var borderOffset = 3;
        var fillOffset = 6 * 6 + 3;
        AssertClose(batch.Vertices[fillOffset], batch.Vertices[borderOffset]);
        AssertClose(batch.Vertices[fillOffset + 1], batch.Vertices[borderOffset + 1]);
        AssertClose(batch.Vertices[fillOffset + 2], batch.Vertices[borderOffset + 2]);
    }

    [Fact]
    public void TerrainModeCanSuppressTopBorderColourWhenZoomedOut()
    {
        var map = TileMapPresets.Flat(1, 1, (byte)TileType.Grass);

        var batch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            TerrainRenderMode.ShadedRelief,
            ViewProjectionMode.ThreeD,
            false,
            false);

        var borderOffset = 3;
        var fillOffset = 6 * 6 + 3;
        AssertClose(batch.Vertices[fillOffset], batch.Vertices[borderOffset]);
        AssertClose(batch.Vertices[fillOffset + 1], batch.Vertices[borderOffset + 1]);
        AssertClose(batch.Vertices[fillOffset + 2], batch.Vertices[borderOffset + 2]);
    }

    [Fact]
    public void TerrainChunkBatchEncodesTopBorderForShaderFade()
    {
        var map = TileMapPresets.Flat(1, 1, (byte)TileType.Grass);

        var batch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            TerrainRenderMode.ShadedRelief,
            ViewProjectionMode.ThreeD,
            false);

        var borderOffset = 3;
        var fillOffset = 6 * 6 + 3;
        AssertClose(-batch.Vertices[fillOffset], batch.Vertices[borderOffset]);
        AssertClose(batch.Vertices[fillOffset + 1], batch.Vertices[borderOffset + 1]);
        AssertClose(batch.Vertices[fillOffset + 2], batch.Vertices[borderOffset + 2]);
    }

    [Fact]
    public void VoxelModeUsesPerTileElevationForTopFaceCorners()
    {
        var map = TileMapPresets.Flat(1, 2, (byte)TileType.Grass);
        map.SetTile(0, 0, (byte)TileType.Grass, 8);
        map.SetTile(0, 1, (byte)TileType.Grass, 2);

        var batch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            TerrainRenderMode.Voxel,
            ViewProjectionMode.ThreeD,
            false);
        var voxelCorners = IsoMath.TopFaceCorners(0, 0, 8, 1f);
        var smoothedCorners = IsoMath.SmoothedTopFaceCorners(map, 0, 0, 1f);

        Assert.Equal(2, batch.TileCount);
        AssertClose(voxelCorners[1].Y, batch.Vertices[7]);
        Assert.False(NearlyEqual(smoothedCorners[1].Y, batch.Vertices[7]));
    }

    [Theory]
    [InlineData(TerrainRenderMode.ShadedRelief)]
    [InlineData(TerrainRenderMode.Heat)]
    [InlineData(TerrainRenderMode.Topographical)]
    [InlineData(TerrainRenderMode.Voxel)]
    public void TopDownRenderModesUseFlatTopFaces(TerrainRenderMode renderMode)
    {
        var map = TileMapPresets.Flat(1, 1, (byte)TileType.Grass);
        map.SetTile(0, 0, (byte)TileType.Grass, 6);

        var batch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            renderMode,
            ViewProjectionMode.TopDown,
            false);

        Assert.Equal(1, batch.TileCount);
        Assert.Equal(2 * 6 * 6, batch.Vertices.Length);
    }

    [Fact]
    public void FarZoomLodChunkBatchUsesMuchLessGeometry()
    {
        var map = TileMapPresets.Flat(TileBatcher.ChunkSize, TileBatcher.ChunkSize, (byte)TileType.Grass);
        var fullDetailBatch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            TerrainRenderMode.ShadedRelief,
            ViewProjectionMode.ThreeD,
            false);
        var lodBatch = TileBatcher.BuildChunkBatch(
            map,
            0,
            0,
            1f,
            0f,
            TerrainRenderMode.ShadedRelief,
            ViewProjectionMode.ThreeD,
            false,
            lodBlockSize: TileBatcher.FarZoomLodBlockSize);

        Assert.Equal(TileBatcher.ChunkSize * TileBatcher.ChunkSize, lodBatch.TileCount);
        Assert.True(lodBatch.Vertices.Length < fullDetailBatch.Vertices.Length / 8);
    }

    private static void AssertVertexColour(float[] vertices, int vertexIndex, float r, float g, float b)
    {
        var offset = vertexIndex * 6 + 3;
        AssertClose(r, vertices[offset]);
        AssertClose(g, vertices[offset + 1]);
        AssertClose(b, vertices[offset + 2]);
    }

    private static bool verticesGreater(float[] vertices, int leftOffset, int rightOffset)
    {
        return vertices[leftOffset] > vertices[rightOffset] &&
               vertices[leftOffset + 1] > vertices[rightOffset + 1] &&
               vertices[leftOffset + 2] > vertices[rightOffset + 2];
    }

    private static void AssertClose(float expected, float actual, float tolerance = 0.0001f)
    {
        Assert.True(Math.Abs(expected - actual) < tolerance, $"Expected {expected}, actual {actual}");
    }

    private static bool NearlyEqual(float a, float b, float tolerance = 0.0001f)
    {
        return Math.Abs(a - b) < tolerance;
    }
}
