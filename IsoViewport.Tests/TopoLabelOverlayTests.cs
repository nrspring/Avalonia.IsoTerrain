using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class TopoLabelOverlayTests
{
    [Fact]
    public void MajorContourBoundaryCellIsLabelCandidate()
    {
        var map = TileMapPresets.Flat(3, 3);
        map.Elevation[1, 1] = 40;
        map.Elevation[1, 2] = 60;
        map.Elevation[2, 1] = 20;

        Assert.True(TopoLabelOverlay.IsContourLabelCandidate(map, 1, 1));
    }

    [Fact]
    public void MinorContourBoundaryCellIsLabelCandidate()
    {
        var map = TileMapPresets.Flat(3, 3);
        map.Elevation[1, 1] = 30;
        map.Elevation[2, 1] = 20;

        Assert.True(TopoLabelOverlay.IsContourLabelCandidate(map, 1, 1));
    }

    [Fact]
    public void NonContourElevationIsNotLabelCandidate()
    {
        var map = TileMapPresets.Flat(3, 3);
        map.Elevation[1, 1] = 35;
        map.Elevation[2, 1] = 20;

        Assert.False(TopoLabelOverlay.IsContourLabelCandidate(map, 1, 1));
    }

    [Fact]
    public void PlateauInteriorWithoutLowerBandNeighborIsNotLabelCandidate()
    {
        var map = TileMapPresets.Flat(3, 3);

        for (var row = 0; row < map.Rows; row++)
        {
            for (var col = 0; col < map.Cols; col++)
            {
                map.Elevation[row, col] = 40;
            }
        }

        Assert.False(TopoLabelOverlay.IsContourLabelCandidate(map, 1, 1));
    }
}
