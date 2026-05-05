using IsoViewport.Controls.Controls;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class MiniMapControlTests
{
    [Fact]
    public void WriteTilePixelUsesElevationDrivenWaterColourAtTargetOffset()
    {
        var map = TileMapPresets.Flat(2, 2, (byte)TileType.RareMetals);
        map.SetTile(1, 0, (byte)TileType.RareMetals, TileMap.DeepWaterElevation);
        Span<byte> buffer = stackalloc byte[2 * 2 * 4];

        MiniMapControl.WriteTilePixel(buffer, 2 * 4, map, 1, 0);

        var expectedColour = TileColours.GetFaceColours(
            map.TileType[1, 0],
            map.Elevation[1, 0]).top;
        var offset = 2 * 4;

        Assert.Equal(ToByte(expectedColour.X), buffer[offset]);
        Assert.Equal(ToByte(expectedColour.Y), buffer[offset + 1]);
        Assert.Equal(ToByte(expectedColour.Z), buffer[offset + 2]);
        Assert.Equal(255, buffer[offset + 3]);
        Assert.Equal(0, buffer[0]);
        Assert.Equal(0, buffer[1]);
        Assert.Equal(0, buffer[2]);
        Assert.Equal(0, buffer[3]);
    }

    private static byte ToByte(float channel)
    {
        return (byte)(Math.Clamp(channel, 0f, 1f) * byte.MaxValue);
    }
}
