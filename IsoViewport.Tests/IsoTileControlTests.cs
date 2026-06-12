using System.Numerics;
using Avalonia.Media;
using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class IsoTileControlTests
{
    [Theory]
    [InlineData(TerrainRenderMode.Voxel, 0.20f, 0f)]
    [InlineData(TerrainRenderMode.Voxel, 0.30f, 0.50f)]
    [InlineData(TerrainRenderMode.Voxel, 0.40f, 1f)]
    [InlineData(TerrainRenderMode.Heat, 0.20f, 1f)]
    [InlineData(TerrainRenderMode.Topographical, 0.20f, 1f)]
    [InlineData(TerrainRenderMode.ShadedRelief, 0.20f, 1f)]
    public void TerrainBorderVisibilityFadesWithoutRequiringMeshSwap(
        TerrainRenderMode renderMode,
        float zoom,
        float expected)
    {
        var visibility = IsoTileControl.GetTerrainBorderVisibility(zoom, renderMode);

        Assert.True(Math.Abs(expected - visibility) < 0.0001f, $"Expected {expected}, actual {visibility}");
    }

    [Theory]
    [InlineData(0.20f, 0.42f)]
    [InlineData(0.45f, 0.71f)]
    [InlineData(0.90f, 1f)]
    public void WaterAnimationStrengthSoftensWhenZoomedOut(float zoom, float expected)
    {
        var strength = IsoTileControl.GetWaterAnimationStrength(zoom);

        Assert.True(Math.Abs(expected - strength) < 0.0001f, $"Expected {expected}, actual {strength}");
    }

    [Theory]
    [InlineData(0.20f, 0f)]
    [InlineData(0.46f, 0.50f)]
    [InlineData(0.80f, 1f)]
    public void WaterGridVisibilityFallsAwayWhenZoomedOut(float zoom, float expected)
    {
        var visibility = IsoTileControl.GetWaterGridVisibility(zoom);

        Assert.True(Math.Abs(expected - visibility) < 0.0001f, $"Expected {expected}, actual {visibility}");
    }

    [Fact]
    public void FarZoomLodUsesHysteresis()
    {
        Assert.Equal(1, IsoTileControl.GetFarZoomLodBlockSize(0.20f));
        Assert.Equal(TileBatcher.FarZoomLodBlockSize, IsoTileControl.GetFarZoomLodBlockSize(0.10f));
        Assert.Equal(TileBatcher.FarZoomLodBlockSize, IsoTileControl.GetFarZoomLodBlockSize(0.14f, TileBatcher.FarZoomLodBlockSize));
        Assert.Equal(1, IsoTileControl.GetFarZoomLodBlockSize(0.18f, TileBatcher.FarZoomLodBlockSize));
    }

    [Theory]
    [InlineData(TerrainRenderMode.Voxel)]
    [InlineData(TerrainRenderMode.ShadedRelief)]
    [InlineData(TerrainRenderMode.Heat)]
    [InlineData(TerrainRenderMode.Topographical)]
    public void TileHighlightVerticesAreBuiltForEveryRenderMode(TerrainRenderMode renderMode)
    {
        var map = TileMapPresets.Flat(3, 3);
        var highlights = new[]
        {
            new ObservableTileHighlight(new TileCoordinate(1, 1), Colors.Gold),
        };

        var vertices = IsoTileControl.BuildTileHighlightVertices(
            map,
            highlights,
            renderMode,
            0f,
            ViewProjectionMode.ThreeD);

        Assert.Equal(5 * 6 * 6, vertices.Length);
    }

    [Fact]
    public void TileHighlightVerticesSkipOutOfBoundsEntries()
    {
        var map = TileMapPresets.Flat(2, 2);
        var highlights = new[]
        {
            new ObservableTileHighlight(new TileCoordinate(4, 1), Colors.Gold),
        };

        var vertices = IsoTileControl.BuildTileHighlightVertices(
            map,
            highlights,
            TerrainRenderMode.Voxel,
            0f,
            ViewProjectionMode.ThreeD);

        Assert.Empty(vertices);
    }

    [Fact]
    public void TileHighlightColoursAreDerivedFromHighlightAndTerrainColour()
    {
        var (ring, fill) = IsoTileControl.GetTileHighlightColours(Colors.Red, new Vector3(0.2f, 0.4f, 0.6f));

        Assert.True(ring.X > fill.X);
        Assert.True(fill.Z > 0f);
        Assert.True(ring.X <= 1f);
    }
}
