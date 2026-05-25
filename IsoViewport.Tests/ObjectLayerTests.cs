using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class ObjectLayerTests
{
    [Fact]
    public void VoxelObjectIsOccludedByHigherForegroundNeighbor()
    {
        var map = TileMapPresets.Flat(3, 3, (byte)TileType.Grass);
        map.Elevation[1, 1] = 4;
        map.Elevation[2, 1] = 9;

        var occluded = ObjectLayer.IsOccludedByForegroundVoxel(map, 1, 1, map.Elevation[1, 1], 0f);

        Assert.True(occluded);
    }

    [Fact]
    public void VoxelObjectIsNotOccludedByHigherBackgroundNeighbor()
    {
        var map = TileMapPresets.Flat(3, 3, (byte)TileType.Grass);
        map.Elevation[1, 1] = 4;
        map.Elevation[0, 1] = 9;

        var occluded = ObjectLayer.IsOccludedByForegroundVoxel(map, 1, 1, map.Elevation[1, 1], 0f);

        Assert.False(occluded);
    }

    [Fact]
    public void VoxelObjectOcclusionTracksCameraRotation()
    {
        var map = TileMapPresets.Flat(3, 3, (byte)TileType.Grass);
        map.Elevation[1, 1] = 4;
        map.Elevation[0, 1] = 9;

        var occluded = ObjectLayer.IsOccludedByForegroundVoxel(map, 1, 1, map.Elevation[1, 1], 180f);

        Assert.True(occluded);
    }
}
