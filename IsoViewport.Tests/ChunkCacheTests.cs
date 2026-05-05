using System.Drawing;
using IsoViewport.Controls.Rendering;
using Xunit;

namespace IsoViewport.Tests;

public sealed class ChunkCacheTests
{
    [Fact]
    public void CandidateChunkWindowNarrowsIterationToVisibleRegion()
    {
        var window = ChunkCache.GetCandidateChunkWindow(
            chunkRows: 79,
            chunkCols: 79,
            visibleTileBounds: new RectangleF(1200f, 1180f, 40f, 35f));

        Assert.InRange(window.MinChunkRow, 34, 36);
        Assert.InRange(window.MaxChunkRow, 38, 40);
        Assert.InRange(window.MinChunkCol, 35, 37);
        Assert.InRange(window.MaxChunkCol, 39, 41);
    }

    [Fact]
    public void CandidateChunkWindowClampsAtMapEdges()
    {
        var window = ChunkCache.GetCandidateChunkWindow(
            chunkRows: 5,
            chunkCols: 7,
            visibleTileBounds: new RectangleF(-100f, -140f, 30f, 20f));

        Assert.Equal(0, window.MinChunkRow);
        Assert.Equal(0, window.MinChunkCol);
        Assert.InRange(window.MaxChunkRow, 0, 1);
        Assert.InRange(window.MaxChunkCol, 0, 1);
    }
}
