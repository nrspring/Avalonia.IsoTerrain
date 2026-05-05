using System.Drawing;
using System.Numerics;

namespace IsoViewport.Controls.Rendering;

public readonly record struct TileBatch(float[] Vertices, int VisibleTileCount);

public readonly record struct TileChunkBatch(float[] Vertices, int TileCount, RectangleF Bounds);

public static class TileBatcher
{
    private readonly record struct LodBlockSummary(
        int StartRow,
        int EndRow,
        int StartCol,
        int EndCol,
        int TileCount,
        int CentreRow,
        int CentreCol,
        byte RepresentativeElevation,
        byte DominantTileType);

    private readonly struct ChunkGeometryCache
    {
        private const int CornersPerTile = 4;

        private readonly int _tileCols;
        private readonly Vector2[] _topCorners;
        private readonly Vector2[] _baseCorners;

        private ChunkGeometryCache(int tileCols, Vector2[] topCorners, Vector2[] baseCorners)
        {
            _tileCols = tileCols;
            _topCorners = topCorners;
            _baseCorners = baseCorners;
        }

        public static ChunkGeometryCache Build(
            TileMap map,
            int startRow,
            int startCol,
            int endRow,
            int endCol,
            float zoom,
            float rotationDegrees,
            ViewProjectionMode projectionMode)
        {
            var tileRows = endRow - startRow;
            var tileCols = endCol - startCol;

            if (tileRows <= 0 || tileCols <= 0)
            {
                return new ChunkGeometryCache(0, Array.Empty<Vector2>(), Array.Empty<Vector2>());
            }

            var topCorners = new Vector2[tileRows * tileCols * CornersPerTile];
            var baseCorners = new Vector2[topCorners.Length];
            var cornerOffsets = IsoMath.TopFaceCorners(0, 0, 0, zoom, rotationDegrees, projectionMode);
            var origin = IsoMath.TileToScreen(startCol, startRow, 0f, rotationDegrees, projectionMode) * zoom;
            var colAxis = IsoMath.TileToScreen(1f, 0f, 0f, rotationDegrees, projectionMode) * zoom;
            var rowAxis = IsoMath.TileToScreen(0f, 1f, 0f, rotationDegrees, projectionMode) * zoom;
            var elevationScale = IsoMath.ElevStep * zoom;
            var gridVertexElevations = new float[(tileRows + 1) * (tileCols + 1)];

            for (var localRow = 0; localRow <= tileRows; localRow++)
            {
                for (var localCol = 0; localCol <= tileCols; localCol++)
                {
                    gridVertexElevations[(localRow * (tileCols + 1)) + localCol] =
                        IsoMath.GridVertexElevation(map, startRow + localRow, startCol + localCol);
                }
            }

            for (var localRow = 0; localRow < tileRows; localRow++)
            {
                var rowStart = origin + (rowAxis * localRow);

                for (var localCol = 0; localCol < tileCols; localCol++)
                {
                    var centre = rowStart + (colAxis * localCol);
                    var cornerIndex = ((localRow * tileCols) + localCol) * CornersPerTile;
                    var baseTop = centre + cornerOffsets[0];
                    var baseRight = centre + cornerOffsets[1];
                    var baseBottom = centre + cornerOffsets[2];
                    var baseLeft = centre + cornerOffsets[3];

                    baseCorners[cornerIndex] = baseTop;
                    baseCorners[cornerIndex + 1] = baseRight;
                    baseCorners[cornerIndex + 2] = baseBottom;
                    baseCorners[cornerIndex + 3] = baseLeft;

                    if (projectionMode == ViewProjectionMode.TopDown)
                    {
                        topCorners[cornerIndex] = baseTop;
                        topCorners[cornerIndex + 1] = baseRight;
                        topCorners[cornerIndex + 2] = baseBottom;
                        topCorners[cornerIndex + 3] = baseLeft;
                        continue;
                    }

                    var gridIndex = (localRow * (tileCols + 1)) + localCol;
                    topCorners[cornerIndex] = baseTop - new Vector2(0f, gridVertexElevations[gridIndex] * elevationScale);
                    topCorners[cornerIndex + 1] = baseRight - new Vector2(0f, gridVertexElevations[gridIndex + 1] * elevationScale);
                    topCorners[cornerIndex + 2] = baseBottom - new Vector2(0f, gridVertexElevations[gridIndex + tileCols + 2] * elevationScale);
                    topCorners[cornerIndex + 3] = baseLeft - new Vector2(0f, gridVertexElevations[gridIndex + tileCols + 1] * elevationScale);
                }
            }

            return new ChunkGeometryCache(tileCols, topCorners, baseCorners);
        }

        public ReadOnlySpan<Vector2> GetTopCorners(int localRow, int localCol)
        {
            var cornerIndex = ((localRow * _tileCols) + localCol) * CornersPerTile;
            return _topCorners.AsSpan(cornerIndex, CornersPerTile);
        }

        public ReadOnlySpan<Vector2> GetBaseCorners(int localRow, int localCol)
        {
            var cornerIndex = ((localRow * _tileCols) + localCol) * CornersPerTile;
            return _baseCorners.AsSpan(cornerIndex, CornersPerTile);
        }
    }

    private enum AnimatedWaterBand
    {
        Fill,
        Inner,
        Outer,
    }

    public const int ChunkSize = 32;

    private const int FloatsPerVertex = 6;
    private const int VerticesPerQuad = 6;
    private const int MaxQuadsPerTile = 5;
    public const int FarZoomLodBlockSize = 4;
    private const float TopInsetFactor = 0.12f;
    private const float WaterOuterInsetFactor = 0.07f;
    private const float WaterInnerInsetFactor = 0.18f;
    private const float TopInsetDepthBias = 0.0005f;
    private const float AnimatedWaterCullPadding = 8f;
    private const int TopographicalMinorInterval = 10;
    private const int TopographicalMajorInterval = 20;
    private const float CardinalShoreWeight = 1f;
    private const float DiagonalShoreWeight = 0.65f;
    private const float MaxShoreWeight = CardinalShoreWeight * 4f + DiagonalShoreWeight * 4f;

    public static float[] BuildVertexData(
        TileMap map,
        float zoom,
        RectangleF viewport,
        float panX,
        float panY,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric,
        bool showTerrainTileBorders = true)
    {
        return BuildTileBatch(map, zoom, viewport, panX, panY, rotationDegrees, projectionMode, showTerrainTileBorders).Vertices;
    }

    public static float[] BuildChunkVertexData(TileMap map, int chunkRow, int chunkCol, float zoom, float rotationDegrees = 0f, ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        return BuildChunkBatch(map, chunkRow, chunkCol, zoom, rotationDegrees, TerrainRenderMode.Terrain, projectionMode, false).Vertices;
    }

    public static float[] BuildChunkVertexData(TileMap map, int chunkRow, int chunkCol, float zoom, bool animPass)
    {
        return BuildChunkBatch(map, chunkRow, chunkCol, zoom, 0f, TerrainRenderMode.Terrain, ViewProjectionMode.Isometric, animPass).Vertices;
    }

    public static float[] BuildChunkVertexData(
        TileMap map,
        int chunkRow,
        int chunkCol,
        float zoom,
        float rotationDegrees,
        bool animPass,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        return BuildChunkBatch(map, chunkRow, chunkCol, zoom, rotationDegrees, TerrainRenderMode.Terrain, projectionMode, animPass).Vertices;
    }

    public static TileChunkBatch BuildChunkBatch(
        TileMap map,
        int chunkRow,
        int chunkCol,
        float zoom,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        return BuildChunkBatch(map, chunkRow, chunkCol, zoom, rotationDegrees, TerrainRenderMode.Terrain, projectionMode, false);
    }

    public static TileChunkBatch BuildChunkBatch(
        TileMap map,
        int chunkRow,
        int chunkCol,
        float zoom,
        float rotationDegrees,
        bool animPass,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        return BuildChunkBatch(map, chunkRow, chunkCol, zoom, rotationDegrees, TerrainRenderMode.Terrain, projectionMode, animPass);
    }

    public static TileChunkBatch BuildChunkBatch(
        TileMap map,
        int chunkRow,
        int chunkCol,
        float zoom,
        float rotationDegrees,
        TerrainRenderMode renderMode,
        ViewProjectionMode projectionMode,
        bool animPass,
        bool showTerrainTileBorders = true,
        int lodBlockSize = 1)
    {
        if (zoom <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be greater than zero.");
        }

        var startRow = chunkRow * ChunkSize;
        var startCol = chunkCol * ChunkSize;
        var endRow = Math.Min(startRow + ChunkSize, map.Rows);
        var endCol = Math.Min(startCol + ChunkSize, map.Cols);

        if (startRow >= map.Rows || startCol >= map.Cols || chunkRow < 0 || chunkCol < 0)
        {
            return new TileChunkBatch([], 0, RectangleF.Empty);
        }

        if (lodBlockSize > 1)
        {
            return BuildLodChunkBatch(
                map,
                startRow,
                startCol,
                endRow,
                endCol,
                zoom,
                rotationDegrees,
                renderMode,
                projectionMode,
                animPass,
                lodBlockSize);
        }

        var vertices = new List<float>((endRow - startRow) * (endCol - startCol) * MaxQuadsPerTile * VerticesPerQuad * FloatsPerVertex);
        var mapSize = Math.Max(map.Rows, map.Cols);
        var bounds = default(RectangleF);
        var hasBounds = false;
        var tileCount = 0;
        var tileRows = endRow - startRow;
        var tileCols = endCol - startCol;
        var geometryCache = ChunkGeometryCache.Build(map, startRow, startCol, endRow, endCol, zoom, rotationDegrees, projectionMode);

        for (var localRow = 0; localRow < tileRows; localRow++)
        {
            var row = startRow + localRow;

            for (var localCol = 0; localCol < tileCols; localCol++)
            {
                var col = startCol + localCol;
                var tileType = map.TileType[row, col];
                var elev = map.Elevation[row, col];
                var isAnimated = IsAnimatedTile(elev, renderMode);

                if (animPass != isAnimated)
                {
                    continue;
                }

                tileCount++;
                var depth = IsoMath.TileDepth(col, row, elev, mapSize);
                var topCorners = geometryCache.GetTopCorners(localRow, localCol);
                var baseCorners = geometryCache.GetBaseCorners(localRow, localCol);
                var tileBounds = GetTileBounds(topCorners, baseCorners);
                bounds = hasBounds ? RectangleF.Union(bounds, tileBounds) : tileBounds;
                hasBounds = true;

                if (animPass)
                {
                    var waterMeta = GetAnimatedWaterMetadata(map, row, col);
                    EmitAnimatedWaterTopFace(vertices, topCorners, depth, waterMeta, IsDeepWater(elev));
                    if (projectionMode == ViewProjectionMode.Isometric)
                    {
                        if (localRow + 1 < tileRows)
                        {
                            var southCorners = geometryCache.GetTopCorners(localRow + 1, localCol);
                            EmitLeftFace(vertices, topCorners, southCorners[0], southCorners[1], depth, waterMeta);
                        }
                        else
                        {
                            EmitLeftFace(vertices, map, col, row, zoom, rotationDegrees, projectionMode, topCorners, depth, waterMeta);
                        }

                        if (localCol + 1 < tileCols)
                        {
                            var eastCorners = geometryCache.GetTopCorners(localRow, localCol + 1);
                            EmitRightFace(vertices, topCorners, eastCorners[0], eastCorners[3], depth, waterMeta);
                        }
                        else
                        {
                            EmitRightFace(vertices, map, col, row, zoom, rotationDegrees, projectionMode, topCorners, depth, waterMeta);
                        }
                    }
                }
                else
                {
                    var colours = TileColours.GetFaceColours(tileType, elev, renderMode, row, col);
                    var borderColour = renderMode switch
                    {
                        TerrainRenderMode.Topographical => GetContourBorderColour(map, row, col, colours.top, renderMode),
                        TerrainRenderMode.Heat => GetContourBorderColour(map, row, col, colours.top, renderMode),
                        _ => showTerrainTileBorders
                            ? EncodeSuppressibleTerrainBorderColour(colours.top)
                            : colours.top,
                    };
                    EmitTopFace(vertices, topCorners, depth, colours.top, borderColour);
                    if (projectionMode == ViewProjectionMode.Isometric)
                    {
                        EmitLeftFace(vertices, map, col, row, zoom, rotationDegrees, projectionMode, topCorners, depth, colours.left);
                        EmitRightFace(vertices, map, col, row, zoom, rotationDegrees, projectionMode, topCorners, depth, colours.right);
                    }
                }
            }
        }

        if (animPass && hasBounds)
        {
            bounds.Inflate(0f, AnimatedWaterCullPadding);
        }

        return new TileChunkBatch(vertices.ToArray(), tileCount, bounds);
    }

    private static TileChunkBatch BuildLodChunkBatch(
        TileMap map,
        int startRow,
        int startCol,
        int endRow,
        int endCol,
        float zoom,
        float rotationDegrees,
        TerrainRenderMode renderMode,
        ViewProjectionMode projectionMode,
        bool animPass,
        int lodBlockSize)
    {
        if (animPass)
        {
            return new TileChunkBatch([], 0, RectangleF.Empty);
        }

        var tileRows = endRow - startRow;
        var tileCols = endCol - startCol;
        var blockRows = (tileRows + lodBlockSize - 1) / lodBlockSize;
        var blockCols = (tileCols + lodBlockSize - 1) / lodBlockSize;
        var vertices = new List<float>(blockRows * blockCols * 2 * VerticesPerQuad * FloatsPerVertex);
        var mapSize = Math.Max(map.Rows, map.Cols);
        var summaries = new LodBlockSummary[blockRows * blockCols];
        var bounds = default(RectangleF);
        var hasBounds = false;
        var tileCount = 0;

        for (var blockRow = 0; blockRow < blockRows; blockRow++)
        {
            var blockStartRow = startRow + (blockRow * lodBlockSize);
            var blockEndRow = Math.Min(blockStartRow + lodBlockSize, endRow);

            for (var blockCol = 0; blockCol < blockCols; blockCol++)
            {
                var blockStartCol = startCol + (blockCol * lodBlockSize);
                var blockEndCol = Math.Min(blockStartCol + lodBlockSize, endCol);
                summaries[(blockRow * blockCols) + blockCol] = SummarizeLodBlock(
                    map,
                    blockStartRow,
                    blockStartCol,
                    blockEndRow,
                    blockEndCol,
                    renderMode);
            }
        }

        for (var blockRow = 0; blockRow < blockRows; blockRow++)
        {
            for (var blockCol = 0; blockCol < blockCols; blockCol++)
            {
                var summary = summaries[(blockRow * blockCols) + blockCol];
                var topCorners = GetLodBlockCorners(
                    map,
                    summary.StartRow,
                    summary.StartCol,
                    summary.EndRow,
                    summary.EndCol,
                    zoom,
                    rotationDegrees,
                    projectionMode,
                    useElevation: true);
                var baseCorners = GetLodBlockCorners(
                    map,
                    summary.StartRow,
                    summary.StartCol,
                    summary.EndRow,
                    summary.EndCol,
                    zoom,
                    rotationDegrees,
                    projectionMode,
                    useElevation: false);
                var tileBounds = GetTileBounds(topCorners, baseCorners);
                var depth = IsoMath.TileDepth(
                    summary.CentreCol,
                    summary.CentreRow,
                    summary.RepresentativeElevation,
                    mapSize);

                bounds = hasBounds ? RectangleF.Union(bounds, tileBounds) : tileBounds;
                hasBounds = true;
                tileCount += summary.TileCount;

                var colours = TileColours.GetFaceColours(
                    summary.DominantTileType,
                    summary.RepresentativeElevation,
                    renderMode,
                    summary.CentreRow,
                    summary.CentreCol);

                if (renderMode == TerrainRenderMode.Terrain)
                {
                    EmitFilledQuad(vertices, topCorners, depth, colours.top);
                    continue;
                }

                var borderColour = GetLodContourBorderColour(
                    summaries,
                    blockRows,
                    blockCols,
                    blockRow,
                    blockCol,
                    colours.top,
                    renderMode);

                if (borderColour == colours.top)
                {
                    EmitFilledQuad(vertices, topCorners, depth, colours.top);
                }
                else
                {
                    EmitTopFace(vertices, topCorners, depth, colours.top, borderColour);
                }
            }
        }

        return new TileChunkBatch(vertices.ToArray(), tileCount, bounds);
    }

    private static LodBlockSummary SummarizeLodBlock(
        TileMap map,
        int startRow,
        int startCol,
        int endRow,
        int endCol,
        TerrainRenderMode renderMode)
    {
        var tileCount = 0;
        var waterCount = 0;
        var deepWaterCount = 0;
        var landCount = 0;
        var totalElevation = 0;
        var totalLandElevation = 0;
        var tileTypeCounts = new int[Enum.GetValues<TileType>().Length];

        for (var row = startRow; row < endRow; row++)
        {
            for (var col = startCol; col < endCol; col++)
            {
                var elevation = map.Elevation[row, col];
                tileCount++;
                totalElevation += elevation;

                if (TileMap.IsWaterElevation(elevation))
                {
                    waterCount++;

                    if (elevation <= TileMap.DeepWaterElevation)
                    {
                        deepWaterCount++;
                    }

                    continue;
                }

                landCount++;
                totalLandElevation += elevation;
                tileTypeCounts[map.TileType[row, col]]++;
            }
        }

        var dominantTileType = (byte)TileType.Sand;

        if (landCount > 0)
        {
            var highestCount = -1;

            for (var tileType = 0; tileType < tileTypeCounts.Length; tileType++)
            {
                if (tileTypeCounts[tileType] <= highestCount)
                {
                    continue;
                }

                highestCount = tileTypeCounts[tileType];
                dominantTileType = (byte)tileType;
            }
        }

        byte representativeElevation;

        if (renderMode == TerrainRenderMode.Terrain)
        {
            if (waterCount > 0 && waterCount >= landCount)
            {
                representativeElevation = deepWaterCount * 2 >= waterCount
                    ? TileMap.DeepWaterElevation
                    : TileMap.ShallowWaterElevation;
            }
            else
            {
                var averageLandElevation = landCount == 0
                    ? TileMap.LandMinElevation
                    : (float)totalLandElevation / landCount;
                representativeElevation = (byte)Math.Clamp(
                    (int)MathF.Round(averageLandElevation),
                    TileMap.LandMinElevation,
                    TileMap.MaxElevation);
            }
        }
        else
        {
            var averageElevation = tileCount == 0 ? 0f : (float)totalElevation / tileCount;
            representativeElevation = (byte)Math.Clamp(
                (int)MathF.Round(averageElevation),
                TileMap.DeepWaterElevation,
                TileMap.MaxElevation);
        }

        return new LodBlockSummary(
            startRow,
            endRow,
            startCol,
            endCol,
            tileCount,
            startRow + ((endRow - startRow - 1) / 2),
            startCol + ((endCol - startCol - 1) / 2),
            representativeElevation,
            dominantTileType);
    }

    private static Vector2[] GetLodBlockCorners(
        TileMap map,
        int startRow,
        int startCol,
        int endRow,
        int endCol,
        float zoom,
        float rotationDegrees,
        ViewProjectionMode projectionMode,
        bool useElevation)
    {
        var topLeftElevation = useElevation && projectionMode == ViewProjectionMode.Isometric
            ? IsoMath.GridVertexElevation(map, startRow, startCol)
            : 0f;
        var topRightElevation = useElevation && projectionMode == ViewProjectionMode.Isometric
            ? IsoMath.GridVertexElevation(map, startRow, endCol)
            : 0f;
        var bottomRightElevation = useElevation && projectionMode == ViewProjectionMode.Isometric
            ? IsoMath.GridVertexElevation(map, endRow, endCol)
            : 0f;
        var bottomLeftElevation = useElevation && projectionMode == ViewProjectionMode.Isometric
            ? IsoMath.GridVertexElevation(map, endRow, startCol)
            : 0f;

        return
        [
            IsoMath.GridVertexToScreen(startCol, startRow, topLeftElevation, rotationDegrees, projectionMode) * zoom,
            IsoMath.GridVertexToScreen(endCol, startRow, topRightElevation, rotationDegrees, projectionMode) * zoom,
            IsoMath.GridVertexToScreen(endCol, endRow, bottomRightElevation, rotationDegrees, projectionMode) * zoom,
            IsoMath.GridVertexToScreen(startCol, endRow, bottomLeftElevation, rotationDegrees, projectionMode) * zoom,
        ];
    }

    private static Vector3 GetLodContourBorderColour(
        ReadOnlySpan<LodBlockSummary> summaries,
        int blockRows,
        int blockCols,
        int blockRow,
        int blockCol,
        Vector3 fillColour,
        TerrainRenderMode renderMode)
    {
        var summary = summaries[(blockRow * blockCols) + blockCol];
        var elevation = summary.RepresentativeElevation;
        var minorBand = elevation / TopographicalMinorInterval;
        var majorBand = elevation / TopographicalMajorInterval;
        var hasMinorContour = false;
        var hasMajorContour = false;
        var hasCoastline = false;

        for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
        {
            for (var colOffset = -1; colOffset <= 1; colOffset++)
            {
                if (rowOffset == 0 && colOffset == 0)
                {
                    continue;
                }

                var neighborRow = blockRow + rowOffset;
                var neighborCol = blockCol + colOffset;

                if ((uint)neighborRow >= (uint)blockRows || (uint)neighborCol >= (uint)blockCols)
                {
                    continue;
                }

                var neighbor = summaries[(neighborRow * blockCols) + neighborCol];
                var neighborElevation = neighbor.RepresentativeElevation;

                if (TileMap.IsWaterElevation(elevation) != TileMap.IsWaterElevation(neighborElevation))
                {
                    hasCoastline = true;
                }

                if (neighborElevation / TopographicalMajorInterval != majorBand &&
                    Math.Max(elevation, neighborElevation) >= TopographicalMajorInterval)
                {
                    hasMajorContour = true;
                }

                if (neighborElevation / TopographicalMinorInterval != minorBand &&
                    Math.Max(elevation, neighborElevation) >= TopographicalMinorInterval)
                {
                    hasMinorContour = true;
                }
            }
        }

        if (hasMajorContour)
        {
            return renderMode == TerrainRenderMode.Heat
                ? GetHeatContourBorderColour(fillColour, major: true)
                : new Vector3(0.23f, 0.23f, 0.23f);
        }

        if (hasMinorContour)
        {
            return renderMode == TerrainRenderMode.Heat
                ? GetHeatContourBorderColour(fillColour, major: false)
                : new Vector3(0.42f, 0.42f, 0.42f);
        }

        if (hasCoastline)
        {
            return renderMode == TerrainRenderMode.Heat
                ? GetHeatCoastlineBorderColour(fillColour)
                : new Vector3(0.52f, 0.52f, 0.52f);
        }

        return fillColour;
    }

    public static TileBatch BuildTileBatch(
        TileMap map,
        float zoom,
        RectangleF viewport,
        float panX,
        float panY,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric,
        bool showTerrainTileBorders = true)
    {
        if (zoom <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be greater than zero.");
        }

        var capacity = Math.Min(
            (long)map.Rows * map.Cols * MaxQuadsPerTile * VerticesPerQuad * FloatsPerVertex,
            int.MaxValue);
        var vertices = new List<float>((int)capacity);
        var mapSize = Math.Max(map.Rows, map.Cols);
        var visibleTiles = 0;

        for (var row = 0; row < map.Rows; row++)
        {
            for (var col = 0; col < map.Cols; col++)
            {
                var elev = map.Elevation[row, col];

                if (IsCulled(GetTileBounds(col, row, elev, zoom, rotationDegrees, projectionMode), viewport, panX, panY))
                {
                    continue;
                }

                visibleTiles++;
                var tileType = map.TileType[row, col];
                var depth = IsoMath.TileDepth(col, row, elev, mapSize);
                var topCorners = IsoMath.SmoothedTopFaceCorners(map, col, row, zoom, rotationDegrees, projectionMode);
                var colours = TileColours.GetFaceColours(tileType, elev, TerrainRenderMode.Terrain, row, col);
                var borderColour = showTerrainTileBorders
                    ? TileColours.GetTopBorderColour(colours.top, TileMap.IsWaterElevation(elev))
                    : colours.top;

                EmitTopFace(vertices, topCorners, depth, colours.top, borderColour);
                if (projectionMode == ViewProjectionMode.Isometric)
                {
                    EmitLeftFace(vertices, map, col, row, zoom, rotationDegrees, projectionMode, topCorners, depth, colours.left);
                    EmitRightFace(vertices, map, col, row, zoom, rotationDegrees, projectionMode, topCorners, depth, colours.right);
                }
            }
        }

        return new TileBatch(vertices.ToArray(), visibleTiles);
    }

    public static RectangleF GetTileBounds(
        int col,
        int row,
        int elev,
        float zoom,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        return IsoMath.GetTileBounds(col, row, elev, zoom, rotationDegrees, projectionMode);
    }

    private static bool IsCulled(RectangleF bounds, RectangleF viewport, float panX, float panY)
    {
        var worldViewport = new RectangleF(viewport.Left - panX, viewport.Top - panY, viewport.Width, viewport.Height);
        return !IsoMath.BoundsIntersect(bounds, worldViewport);
    }

    private static RectangleF GetTileBounds(ReadOnlySpan<Vector2> topCorners, ReadOnlySpan<Vector2> baseCorners)
    {
        var left = float.MaxValue;
        var top = float.MaxValue;
        var right = float.MinValue;
        var bottom = float.MinValue;

        Include(topCorners);
        Include(baseCorners);

        return new RectangleF(left, top, right - left, bottom - top);

        void Include(ReadOnlySpan<Vector2> corners)
        {
            for (var i = 0; i < corners.Length; i++)
            {
                var corner = corners[i];
                left = Math.Min(left, corner.X);
                top = Math.Min(top, corner.Y);
                right = Math.Max(right, corner.X);
                bottom = Math.Max(bottom, corner.Y);
            }
        }
    }

    private static bool IsAnimatedTile(byte elevation, TerrainRenderMode renderMode)
    {
        return renderMode == TerrainRenderMode.Terrain && TileMap.IsWaterElevation(elevation);
    }

    private static Vector3 GetContourBorderColour(
        TileMap map,
        int row,
        int col,
        Vector3 fillColour,
        TerrainRenderMode renderMode)
    {
        var elevation = map.Elevation[row, col];
        var minorBand = elevation / TopographicalMinorInterval;
        var majorBand = elevation / TopographicalMajorInterval;
        var hasMinorContour = false;
        var hasMajorContour = false;
        var hasCoastline = false;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var sampleRow = row + dy;
                var sampleCol = col + dx;

                if ((uint)sampleRow >= (uint)map.Rows || (uint)sampleCol >= (uint)map.Cols)
                {
                    continue;
                }

                var neighborElevation = map.Elevation[sampleRow, sampleCol];

                if (TileMap.IsWaterElevation(elevation) != TileMap.IsWaterElevation(neighborElevation))
                {
                    hasCoastline = true;
                }

                if (neighborElevation / TopographicalMajorInterval != majorBand &&
                    Math.Max(elevation, neighborElevation) >= TopographicalMajorInterval)
                {
                    hasMajorContour = true;
                }

                if (neighborElevation / TopographicalMinorInterval != minorBand &&
                    Math.Max(elevation, neighborElevation) >= TopographicalMinorInterval)
                {
                    hasMinorContour = true;
                }
            }
        }

        if (hasMajorContour)
        {
            return renderMode == TerrainRenderMode.Heat
                ? GetHeatContourBorderColour(fillColour, major: true)
                : new Vector3(0.23f, 0.23f, 0.23f);
        }

        if (hasMinorContour)
        {
            return renderMode == TerrainRenderMode.Heat
                ? GetHeatContourBorderColour(fillColour, major: false)
                : new Vector3(0.42f, 0.42f, 0.42f);
        }

        if (hasCoastline)
        {
            return renderMode == TerrainRenderMode.Heat
                ? GetHeatCoastlineBorderColour(fillColour)
                : new Vector3(0.52f, 0.52f, 0.52f);
        }

        return fillColour;
    }

    private static Vector3 GetHeatContourBorderColour(Vector3 fillColour, bool major)
    {
        var luminance = GetLuminance(fillColour);

        if (major)
        {
            return luminance > 0.52f
                ? new Vector3(0.12f, 0.12f, 0.12f)
                : new Vector3(0.90f, 0.90f, 0.90f);
        }

        return luminance > 0.52f
            ? new Vector3(0.28f, 0.28f, 0.28f)
            : new Vector3(0.74f, 0.74f, 0.74f);
    }

    private static Vector3 GetHeatCoastlineBorderColour(Vector3 fillColour)
    {
        var luminance = GetLuminance(fillColour);

        return luminance > 0.52f
            ? new Vector3(0.40f, 0.40f, 0.40f)
            : new Vector3(0.62f, 0.62f, 0.62f);
    }

    private static float GetLuminance(Vector3 colour)
    {
        return colour.X * 0.2126f + colour.Y * 0.7152f + colour.Z * 0.0722f;
    }

    private static bool IsDeepWater(int elevation)
    {
        return elevation <= TileMap.DeepWaterElevation;
    }

    private static Vector3 GetAnimatedWaterMetadata(TileMap map, int row, int col)
    {
        var shoreVector = Vector2.Zero;
        var shoreWeight = 0f;

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var sampleRow = row + dy;
                var sampleCol = col + dx;

                if ((uint)sampleRow >= (uint)map.Rows || (uint)sampleCol >= (uint)map.Cols)
                {
                    continue;
                }

                if (TileMap.IsWaterElevation(map.Elevation[sampleRow, sampleCol]))
                {
                    continue;
                }

                var weight = dx == 0 || dy == 0 ? CardinalShoreWeight : DiagonalShoreWeight;
                shoreWeight += weight;
                var direction = Vector2.Normalize(new Vector2(dx, dy));
                shoreVector += direction * weight;
            }
        }

        if (shoreWeight <= 0f || shoreVector.LengthSquared() < 0.0001f)
        {
            return new Vector3(0f, 0f, 0f);
        }

        var normalized = Vector2.Normalize(shoreVector);
        var shoreStrength = Math.Clamp(shoreWeight / MaxShoreWeight, 0f, 1f);
        return new Vector3(normalized.X, normalized.Y, shoreStrength);
    }

    private static void EmitTopFace(
        List<float> vertices,
        ReadOnlySpan<Vector2> topCorners,
        float depth,
        Vector3 fillColour,
        Vector3 borderColour)
    {
        EmitQuad(vertices, topCorners[0], topCorners[1], topCorners[2], topCorners[3], depth, borderColour);

        var centre = (topCorners[0] + topCorners[1] + topCorners[2] + topCorners[3]) * 0.25f;
        var innerDepth = Math.Max(0f, depth - TopInsetDepthBias);
        var innerTop = Vector2.Lerp(topCorners[0], centre, TopInsetFactor);
        var innerRight = Vector2.Lerp(topCorners[1], centre, TopInsetFactor);
        var innerBottom = Vector2.Lerp(topCorners[2], centre, TopInsetFactor);
        var innerLeft = Vector2.Lerp(topCorners[3], centre, TopInsetFactor);

        EmitQuad(vertices, innerTop, innerRight, innerBottom, innerLeft, innerDepth, fillColour);
    }

    private static void EmitFilledQuad(
        List<float> vertices,
        ReadOnlySpan<Vector2> corners,
        float depth,
        Vector3 colour)
    {
        EmitQuad(vertices, corners[0], corners[1], corners[2], corners[3], depth, colour);
    }

    private static void EmitAnimatedWaterTopFace(
        List<float> vertices,
        ReadOnlySpan<Vector2> topCorners,
        float depth,
        Vector3 waterMeta,
        bool isDeepWater)
    {
        var centre = (topCorners[0] + topCorners[1] + topCorners[2] + topCorners[3]) * 0.25f;
        var innerDepth = Math.Max(0f, depth - TopInsetDepthBias);
        var midTop = Vector2.Lerp(topCorners[0], centre, WaterOuterInsetFactor);
        var midRight = Vector2.Lerp(topCorners[1], centre, WaterOuterInsetFactor);
        var midBottom = Vector2.Lerp(topCorners[2], centre, WaterOuterInsetFactor);
        var midLeft = Vector2.Lerp(topCorners[3], centre, WaterOuterInsetFactor);
        var innerTop = Vector2.Lerp(topCorners[0], centre, WaterInnerInsetFactor);
        var innerRight = Vector2.Lerp(topCorners[1], centre, WaterInnerInsetFactor);
        var innerBottom = Vector2.Lerp(topCorners[2], centre, WaterInnerInsetFactor);
        var innerLeft = Vector2.Lerp(topCorners[3], centre, WaterInnerInsetFactor);
        var outerBandMeta = new Vector3(waterMeta.X, waterMeta.Y, EncodeAnimatedWaterBand(waterMeta.Z, isDeepWater, AnimatedWaterBand.Outer));
        var innerBandMeta = new Vector3(waterMeta.X, waterMeta.Y, EncodeAnimatedWaterBand(waterMeta.Z, isDeepWater, AnimatedWaterBand.Inner));
        var fillMeta = new Vector3(waterMeta.X, waterMeta.Y, EncodeAnimatedWaterBand(waterMeta.Z, isDeepWater, AnimatedWaterBand.Fill));

        EmitQuad(vertices, topCorners[0], topCorners[1], topCorners[2], topCorners[3], depth, outerBandMeta);
        EmitQuad(vertices, midTop, midRight, midBottom, midLeft, innerDepth, innerBandMeta);
        EmitQuad(vertices, innerTop, innerRight, innerBottom, innerLeft, innerDepth, fillMeta);
    }

    private static float EncodeAnimatedWaterBand(float shoreStrength, bool isDeepWater, AnimatedWaterBand band)
    {
        var baseOffset = band switch
        {
            AnimatedWaterBand.Fill => isDeepWater ? 2f : 0f,
            AnimatedWaterBand.Inner => isDeepWater ? 6f : 4f,
            AnimatedWaterBand.Outer => isDeepWater ? 10f : 8f,
            _ => 0f,
        };

        var encoded = baseOffset + shoreStrength;
        return band == AnimatedWaterBand.Fill ? encoded : -encoded;
    }

    private static Vector3 EncodeSuppressibleTerrainBorderColour(Vector3 fillColour)
    {
        return new Vector3(-fillColour.X, fillColour.Y, fillColour.Z);
    }

    private static void EmitLeftFace(
        List<float> vertices,
        TileMap map,
        int col,
        int row,
        float zoom,
        float rotationDegrees,
        ViewProjectionMode projectionMode,
        ReadOnlySpan<Vector2> topCorners,
        float depth,
        Vector3 colour)
    {
        Vector2 lowerLeft;
        Vector2 lowerBottom;

        if (row + 1 < map.Rows)
        {
            var southCorners = IsoMath.SmoothedTopFaceCorners(map, col, row + 1, zoom, rotationDegrees, projectionMode);
            lowerLeft = southCorners[0];
            lowerBottom = southCorners[1];
        }
        else
        {
            lowerLeft = IsoMath.GridVertexToScreen(col, row + 1, 0f, rotationDegrees, projectionMode) * zoom;
            lowerBottom = IsoMath.GridVertexToScreen(col + 1, row + 1, 0f, rotationDegrees, projectionMode) * zoom;
        }

        EmitLeftFace(vertices, topCorners, lowerLeft, lowerBottom, depth, colour);
    }

    private static void EmitRightFace(
        List<float> vertices,
        TileMap map,
        int col,
        int row,
        float zoom,
        float rotationDegrees,
        ViewProjectionMode projectionMode,
        ReadOnlySpan<Vector2> topCorners,
        float depth,
        Vector3 colour)
    {
        Vector2 lowerRight;
        Vector2 lowerBottom;

        if (col + 1 < map.Cols)
        {
            var eastCorners = IsoMath.SmoothedTopFaceCorners(map, col + 1, row, zoom, rotationDegrees, projectionMode);
            lowerRight = eastCorners[0];
            lowerBottom = eastCorners[3];
        }
        else
        {
            lowerRight = IsoMath.GridVertexToScreen(col + 1, row, 0f, rotationDegrees, projectionMode) * zoom;
            lowerBottom = IsoMath.GridVertexToScreen(col + 1, row + 1, 0f, rotationDegrees, projectionMode) * zoom;
        }

        EmitRightFace(vertices, topCorners, lowerRight, lowerBottom, depth, colour);
    }

    private static void EmitLeftFace(
        List<float> vertices,
        ReadOnlySpan<Vector2> topCorners,
        Vector2 lowerLeft,
        Vector2 lowerBottom,
        float depth,
        Vector3 colour)
    {
        EmitQuadIfVisible(vertices, topCorners[3], topCorners[2], lowerBottom, lowerLeft, depth, colour);
    }

    private static void EmitRightFace(
        List<float> vertices,
        ReadOnlySpan<Vector2> topCorners,
        Vector2 lowerRight,
        Vector2 lowerBottom,
        float depth,
        Vector3 colour)
    {
        if (Math.Abs(topCorners[1].Y - lowerRight.Y) < 0.01f &&
            Math.Abs(topCorners[2].Y - lowerBottom.Y) < 0.01f)
        {
            return;
        }

        EmitQuad(vertices, topCorners[1], lowerRight, lowerBottom, topCorners[2], depth, colour);
    }

    private static void EmitQuadIfVisible(
        List<float> vertices,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        float depth,
        Vector3 colour)
    {
        if (Math.Abs(a.Y - d.Y) < 0.01f &&
            Math.Abs(b.Y - c.Y) < 0.01f)
        {
            return;
        }

        EmitQuad(vertices, a, b, c, d, depth, colour);
    }

    private static void EmitQuad(List<float> vertices, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float depth, Vector3 colour)
    {
        EmitVertex(vertices, a, depth, colour);
        EmitVertex(vertices, b, depth, colour);
        EmitVertex(vertices, c, depth, colour);
        EmitVertex(vertices, a, depth, colour);
        EmitVertex(vertices, c, depth, colour);
        EmitVertex(vertices, d, depth, colour);
    }

    private static void EmitVertex(List<float> vertices, Vector2 point, float depth, Vector3 colour)
    {
        vertices.Add(point.X);
        vertices.Add(point.Y);
        vertices.Add(depth);
        vertices.Add(colour.X);
        vertices.Add(colour.Y);
        vertices.Add(colour.Z);
    }
}
