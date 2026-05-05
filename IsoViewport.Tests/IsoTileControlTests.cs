using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class IsoTileControlTests
{
    [Theory]
    [InlineData(TerrainRenderMode.Terrain, 0.20f, 0f)]
    [InlineData(TerrainRenderMode.Terrain, 0.30f, 0.50f)]
    [InlineData(TerrainRenderMode.Terrain, 0.40f, 1f)]
    [InlineData(TerrainRenderMode.Heat, 0.20f, 1f)]
    [InlineData(TerrainRenderMode.Topographical, 0.20f, 1f)]
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
}
