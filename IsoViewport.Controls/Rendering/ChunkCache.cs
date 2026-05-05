using System.Drawing;
using Silk.NET.OpenGL;

namespace IsoViewport.Controls.Rendering;

public readonly record struct ChunkDrawStats(int VisibleChunks, int RenderedTiles, int VertexCount);

public sealed class ChunkCache
{
    private const float VisibleChunkTilePadding = (TileMap.MaxElevation / 2f) + 4f;

    private readonly uint[] _staticVbos;
    private readonly uint[] _animVbos;
    private readonly int[] _staticVertCounts;
    private readonly int[] _animVertCounts;
    private readonly int[] _chunkTileCounts;
    private readonly RectangleF[] _chunkBounds;
    private readonly bool[] _chunkDirty;
    private readonly int[] _visibleChunkIndices;
    private int _visibleChunkCount;

    private ChunkCache(int chunkRows, int chunkCols, uint[] staticVbos, uint[] animVbos)
    {
        ChunkRows = chunkRows;
        ChunkCols = chunkCols;
        _staticVbos = staticVbos;
        _animVbos = animVbos;
        _staticVertCounts = new int[staticVbos.Length];
        _animVertCounts = new int[staticVbos.Length];
        _chunkTileCounts = new int[staticVbos.Length];
        _chunkBounds = new RectangleF[staticVbos.Length];
        _chunkDirty = new bool[staticVbos.Length];
        _visibleChunkIndices = new int[staticVbos.Length];
    }

    public int ChunkRows { get; }

    public int ChunkCols { get; }

    public int ChunkCount => _staticVbos.Length;

    public int TotalVertexCount => _staticVertCounts.Sum() + _animVertCounts.Sum();

    public static ChunkCache Create(GL gl, TileMap map)
    {
        var chunkRows = (map.Rows + TileBatcher.ChunkSize - 1) / TileBatcher.ChunkSize;
        var chunkCols = (map.Cols + TileBatcher.ChunkSize - 1) / TileBatcher.ChunkSize;
        var staticVbos = new uint[chunkRows * chunkCols];
        var animVbos = new uint[chunkRows * chunkCols];

        for (var i = 0; i < staticVbos.Length; i++)
        {
            staticVbos[i] = gl.GenBuffer();
            animVbos[i] = gl.GenBuffer();
        }

        return new ChunkCache(chunkRows, chunkCols, staticVbos, animVbos);
    }

    public void MarkTileDirty(int row, int col)
    {
        var chunkRow = row / TileBatcher.ChunkSize;
        var chunkCol = col / TileBatcher.ChunkSize;

        if ((uint)chunkRow >= (uint)ChunkRows || (uint)chunkCol >= (uint)ChunkCols)
        {
            return;
        }

        for (var dirtyChunkRow = Math.Max(0, chunkRow - 1); dirtyChunkRow <= Math.Min(ChunkRows - 1, chunkRow + 1); dirtyChunkRow++)
        {
            for (var dirtyChunkCol = Math.Max(0, chunkCol - 1); dirtyChunkCol <= Math.Min(ChunkCols - 1, chunkCol + 1); dirtyChunkCol++)
            {
                _chunkDirty[ToIndex(dirtyChunkRow, dirtyChunkCol)] = true;
            }
        }
    }

    public void MarkAllDirty()
    {
        Array.Fill(_chunkDirty, true);
    }

    public void RebuildAll(
        GL gl,
        TileMap map,
        float rotationDegrees = 0f,
        TerrainRenderMode renderMode = TerrainRenderMode.Terrain,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric,
        bool showTerrainTileBorders = true,
        int lodBlockSize = 1)
    {
        for (var chunkRow = 0; chunkRow < ChunkRows; chunkRow++)
        {
            for (var chunkCol = 0; chunkCol < ChunkCols; chunkCol++)
            {
                UploadChunk(gl, map, rotationDegrees, renderMode, projectionMode, showTerrainTileBorders, lodBlockSize, chunkRow, chunkCol);
            }
        }
    }

    public void UploadDirtyChunks(
        GL gl,
        TileMap map,
        float rotationDegrees = 0f,
        TerrainRenderMode renderMode = TerrainRenderMode.Terrain,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric,
        bool showTerrainTileBorders = true,
        int lodBlockSize = 1)
    {
        for (var chunkRow = 0; chunkRow < ChunkRows; chunkRow++)
        {
            for (var chunkCol = 0; chunkCol < ChunkCols; chunkCol++)
            {
                var index = ToIndex(chunkRow, chunkCol);

                if (!_chunkDirty[index])
                {
                    continue;
                }

                UploadChunk(gl, map, rotationDegrees, renderMode, projectionMode, showTerrainTileBorders, lodBlockSize, chunkRow, chunkCol);
            }
        }
    }

    public ChunkDrawStats UpdateVisibleChunks(RectangleF viewport, RectangleF visibleTileBounds)
    {
        _visibleChunkCount = 0;
        var visibleChunks = 0;
        var renderedTiles = 0;
        var vertexCount = 0;
        var window = GetCandidateChunkWindow(ChunkRows, ChunkCols, visibleTileBounds);

        if (window.MinChunkRow < 0 || window.MinChunkCol < 0)
        {
            return default;
        }

        for (var chunkRow = window.MinChunkRow; chunkRow <= window.MaxChunkRow; chunkRow++)
        {
            for (var chunkCol = window.MinChunkCol; chunkCol <= window.MaxChunkCol; chunkCol++)
            {
                var index = ToIndex(chunkRow, chunkCol);

                if ((_staticVertCounts[index] == 0 && _animVertCounts[index] == 0) || !IsChunkVisible(index, viewport))
                {
                    continue;
                }

                _visibleChunkIndices[_visibleChunkCount++] = index;
                visibleChunks++;
                renderedTiles += _chunkTileCounts[index];
                vertexCount += _staticVertCounts[index] + _animVertCounts[index];
            }
        }

        return new ChunkDrawStats(visibleChunks, renderedTiles, vertexCount);
    }

    public ChunkDrawStats DrawVisibleStaticChunks(
        GL gl,
        uint vao,
        Action setAttribPointers)
    {
        var vertexCount = 0;

        gl.BindVertexArray(vao);

        for (var visibleIndex = 0; visibleIndex < _visibleChunkCount; visibleIndex++)
        {
            var index = _visibleChunkIndices[visibleIndex];

            if (_staticVertCounts[index] == 0)
            {
                continue;
            }

            vertexCount += _staticVertCounts[index];
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _staticVbos[index]);
            setAttribPointers();
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_staticVertCounts[index]);
        }

        return new ChunkDrawStats(0, 0, vertexCount);
    }

    public ChunkDrawStats DrawVisibleAnimatedChunks(
        GL gl,
        uint vao,
        Action setAttribPointers)
    {
        var vertexCount = 0;

        gl.BindVertexArray(vao);

        for (var visibleIndex = 0; visibleIndex < _visibleChunkCount; visibleIndex++)
        {
            var index = _visibleChunkIndices[visibleIndex];

            if (_animVertCounts[index] == 0)
            {
                continue;
            }

            vertexCount += _animVertCounts[index];
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _animVbos[index]);
            setAttribPointers();
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_animVertCounts[index]);
        }

        return new ChunkDrawStats(0, 0, vertexCount);
    }

    public void Delete(GL gl)
    {
        foreach (var vbo in _staticVbos)
        {
            if (vbo != 0)
            {
                gl.DeleteBuffer(vbo);
            }
        }

        foreach (var vbo in _animVbos)
        {
            if (vbo != 0)
            {
                gl.DeleteBuffer(vbo);
            }
        }
    }

    private unsafe void UploadChunk(
        GL gl,
        TileMap map,
        float rotationDegrees,
        TerrainRenderMode renderMode,
        ViewProjectionMode projectionMode,
        bool showTerrainTileBorders,
        int lodBlockSize,
        int chunkRow,
        int chunkCol)
    {
        var index = ToIndex(chunkRow, chunkCol);
        var staticBatch = TileBatcher.BuildChunkBatch(map, chunkRow, chunkCol, 1f, rotationDegrees, renderMode, projectionMode, false, showTerrainTileBorders, lodBlockSize);
        var animBatch = TileBatcher.BuildChunkBatch(map, chunkRow, chunkCol, 1f, rotationDegrees, renderMode, projectionMode, true, lodBlockSize: lodBlockSize);
        _staticVertCounts[index] = staticBatch.Vertices.Length / 6;
        _animVertCounts[index] = animBatch.Vertices.Length / 6;
        _chunkTileCounts[index] = staticBatch.TileCount + animBatch.TileCount;
        _chunkBounds[index] = UnionBounds(staticBatch.Bounds, animBatch.Bounds);

        UploadBuffer(gl, _staticVbos[index], staticBatch.Vertices);
        UploadBuffer(gl, _animVbos[index], animBatch.Vertices);

        _chunkDirty[index] = false;
    }

    private unsafe void UploadBuffer(GL gl, uint vbo, float[] data)
    {
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

        if (data.Length == 0)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, 0, null, BufferUsageARB.DynamicDraw);
            return;
        }

        fixed (float* dataPtr = data)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(data.Length * sizeof(float)),
                dataPtr,
                BufferUsageARB.DynamicDraw);
        }
    }

    private int ToIndex(int chunkRow, int chunkCol)
    {
        return chunkRow * ChunkCols + chunkCol;
    }

    private bool IsChunkVisible(int index, RectangleF viewport)
    {
        return IsoMath.BoundsIntersect(_chunkBounds[index], viewport);
    }

    internal static (int MinChunkRow, int MaxChunkRow, int MinChunkCol, int MaxChunkCol) GetCandidateChunkWindow(
        int chunkRows,
        int chunkCols,
        RectangleF visibleTileBounds)
    {
        if (chunkRows <= 0 || chunkCols <= 0)
        {
            return (-1, -1, -1, -1);
        }

        var minChunkRow = Math.Clamp(
            (int)MathF.Floor((visibleTileBounds.Top - VisibleChunkTilePadding) / TileBatcher.ChunkSize),
            0,
            chunkRows - 1);
        var maxChunkRow = Math.Clamp(
            (int)MathF.Ceiling((visibleTileBounds.Bottom + VisibleChunkTilePadding) / TileBatcher.ChunkSize) - 1,
            0,
            chunkRows - 1);
        var minChunkCol = Math.Clamp(
            (int)MathF.Floor((visibleTileBounds.Left - VisibleChunkTilePadding) / TileBatcher.ChunkSize),
            0,
            chunkCols - 1);
        var maxChunkCol = Math.Clamp(
            (int)MathF.Ceiling((visibleTileBounds.Right + VisibleChunkTilePadding) / TileBatcher.ChunkSize) - 1,
            0,
            chunkCols - 1);

        return (minChunkRow, maxChunkRow, minChunkCol, maxChunkCol);
    }

    private static RectangleF UnionBounds(RectangleF a, RectangleF b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        return RectangleF.Union(a, b);
    }
}
