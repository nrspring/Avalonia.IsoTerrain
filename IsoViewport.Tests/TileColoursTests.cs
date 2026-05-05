using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class TileColoursTests
{
    [Fact]
    public void ElevationDarkensTopFace()
    {
        var low = TileColours.GetFaceColours((byte)TileType.RareMetals, TileMap.LandMinElevation).top;
        var high = TileColours.GetFaceColours((byte)TileType.RareMetals, 10).top;

        Assert.True(high.X < low.X);
        Assert.True(high.Y < low.Y);
        Assert.True(high.Z < low.Z);
    }

    [Fact]
    public void SideFacesAreDarkerThanTopFace()
    {
        var colours = TileColours.GetFaceColours((byte)TileType.Grass, 4);

        Assert.True(colours.left.X < colours.right.X);
        Assert.True(colours.right.X < colours.top.X);
    }

    [Fact]
    public void WaterElevationSelectsDeepAndShallowWaterColours()
    {
        var deep = TileColours.GetFaceColours((byte)TileType.Grass, TileMap.DeepWaterElevation).top;
        var shallow = TileColours.GetFaceColours((byte)TileType.Grass, TileMap.ShallowWaterElevation).top;

        Assert.True(shallow.X > deep.X);
        Assert.True(shallow.Y > deep.Y);
        Assert.True(shallow.Z > deep.Z);
    }

    [Fact]
    public void LandBordersAreDarkerThanWaterBorders()
    {
        var top = new System.Numerics.Vector3(0.4f, 0.6f, 0.3f);
        var land = TileColours.GetTopBorderColour(top, false);
        var water = TileColours.GetTopBorderColour(top, true);

        Assert.True(land.X < water.X);
        Assert.True(land.Y < water.Y);
        Assert.True(land.Z < water.Z);
    }

    [Fact]
    public void IslandPresetIncludesForestTiles()
    {
        var map = TileMapPresets.Island(60, 60);
        var hasForest = false;

        for (var row = 0; row < map.Rows && !hasForest; row++)
        {
            for (var col = 0; col < map.Cols; col++)
            {
                if (map.TileType[row, col] == (byte)TileType.Forest)
                {
                    hasForest = true;
                    break;
                }
            }
        }

        Assert.True(hasForest);
    }

    [Fact]
    public void RealisticWorldIncludesEveryLandSurfaceType()
    {
        var map = TileMapPresets.RealisticWorld(180, 360);
        var seen = new HashSet<TileType>();

        for (var row = 0; row < map.Rows; row++)
        {
            for (var col = 0; col < map.Cols; col++)
            {
                var elevation = map.Elevation[row, col];

                if (TileMap.IsWaterElevation(elevation))
                {
                    continue;
                }

                seen.Add((TileType)map.TileType[row, col]);
            }
        }

        foreach (var landType in Enum.GetValues<TileType>())
        {
            Assert.Contains(landType, seen);
        }
    }

    [Fact]
    public void RealisticWorldUsesOnlySupportedWaterAndLandElevations()
    {
        var map = TileMapPresets.RealisticWorld(120, 240);
        var sawDeepWater = false;
        var sawShallowWater = false;
        var sawLand = false;

        for (var row = 0; row < map.Rows; row++)
        {
            for (var col = 0; col < map.Cols; col++)
            {
                var elevation = map.Elevation[row, col];
                var type = map.TileType[row, col];

                if (TileMap.IsWaterElevation(elevation))
                {
                    Assert.InRange(elevation, TileMap.DeepWaterElevation, TileMap.ShallowWaterElevation);
                    Assert.True(Enum.IsDefined(typeof(TileType), type));
                    sawDeepWater |= elevation == TileMap.DeepWaterElevation;
                    sawShallowWater |= elevation == TileMap.ShallowWaterElevation;
                }
                else
                {
                    Assert.InRange(elevation, TileMap.LandMinElevation, TileMap.MaxElevation);
                    Assert.True(Enum.IsDefined(typeof(TileType), type));
                    sawLand = true;
                }
            }
        }

        Assert.True(sawDeepWater);
        Assert.True(sawShallowWater);
        Assert.True(sawLand);
    }
}
