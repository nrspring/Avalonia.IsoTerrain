using System.Numerics;
using RectangleF = System.Drawing.RectangleF;

namespace IsoViewport.Controls.Rendering;

public static class IsoMath
{
    public const float TileW = 64f;
    public const float TileH = 32f;
    public const float TileHalfW = 32f;
    public const float TileHalfH = 16f;
    public const float ElevStep = 16f;
    public const float TopDownTileSize = TileHalfW;

    public static float NormalizeRotationDegrees(float rotationDegrees)
    {
        var normalized = rotationDegrees % 360f;

        if (normalized < 0f)
        {
            normalized += 360f;
        }

        return normalized;
    }

    public static Vector2 TileToScreen(
        int col,
        int row,
        int elev,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        return TileToScreen((float)col, row, elev, rotationDegrees, projectionMode);
    }

    public static Vector2 TileToScreen(
        float col,
        float row,
        float elev,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        var rotatedGround = RotateGround(new Vector2(col, row), rotationDegrees);
        return ProjectTileCentre(rotatedGround.X, rotatedGround.Y, elev, projectionMode);
    }

    public static Vector2 GridVertexToScreen(
        int col,
        int row,
        float elev,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        return GridVertexToScreen((float)col, row, elev, rotationDegrees, projectionMode);
    }

    public static Vector2 GridVertexToScreen(
        float col,
        float row,
        float elev,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        var groundCorner = TileToScreen(col, row, 0f, rotationDegrees, projectionMode) +
                           GetTopFaceCornerOffsets(rotationDegrees, projectionMode).top;
        return projectionMode == ViewProjectionMode.TopDown
            ? groundCorner
            : groundCorner - new Vector2(0f, elev * ElevStep);
    }

    public static Vector2 ScreenToTile(
        Vector2 screenPos,
        float zoom,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        if (zoom <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be greater than zero.");
        }

        var rotatedGround = UnprojectGround(screenPos, zoom, projectionMode);
        return RotateGround(rotatedGround, -rotationDegrees);
    }

    public static Vector2[] TopFaceCorners(
        int col,
        int row,
        int elev,
        float zoom,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        var projectedElevation = projectionMode == ViewProjectionMode.TopDown ? 0 : elev;
        var centre = TileToScreen(col, row, projectedElevation, rotationDegrees, projectionMode) * zoom;
        var (top, right, bottom, left) = GetTopFaceCornerOffsets(rotationDegrees, projectionMode);

        return
        [
            centre + (top * zoom),
            centre + (right * zoom),
            centre + (bottom * zoom),
            centre + (left * zoom),
        ];
    }

    public static float GridVertexElevation(TileMap map, int row, int col)
    {
        ArgumentNullException.ThrowIfNull(map);

        float total = 0f;
        var count = 0;

        AddCell(row - 1, col - 1);
        AddCell(row - 1, col);
        AddCell(row, col - 1);
        AddCell(row, col);

        return count == 0 ? 0f : total / count;

        void AddCell(int sampleRow, int sampleCol)
        {
            if ((uint)sampleRow >= (uint)map.Rows || (uint)sampleCol >= (uint)map.Cols)
            {
                return;
            }

            total += map.Elevation[sampleRow, sampleCol];
            count++;
        }
    }

    public static Vector2[] SmoothedTopFaceCorners(
        TileMap map,
        int col,
        int row,
        float zoom,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (projectionMode == ViewProjectionMode.TopDown)
        {
            return TopFaceCorners(col, row, 0, zoom, rotationDegrees, projectionMode);
        }

        var centre = TileToScreen(col, row, 0f, rotationDegrees, projectionMode) * zoom;
        var (top, right, bottom, left) = GetTopFaceCornerOffsets(rotationDegrees, projectionMode);

        return
        [
            centre + (top * zoom) - new Vector2(0f, GridVertexElevation(map, row, col) * ElevStep * zoom),
            centre + (right * zoom) - new Vector2(0f, GridVertexElevation(map, row, col + 1) * ElevStep * zoom),
            centre + (bottom * zoom) - new Vector2(0f, GridVertexElevation(map, row + 1, col + 1) * ElevStep * zoom),
            centre + (left * zoom) - new Vector2(0f, GridVertexElevation(map, row + 1, col) * ElevStep * zoom),
        ];
    }

    public static bool TryPickTile(
        TileMap map,
        Vector2 screenPos,
        float zoom,
        out int col,
        out int row,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        ArgumentNullException.ThrowIfNull(map);

        col = -1;
        row = -1;

        if (zoom <= 0f)
        {
            return false;
        }

        var approx = ScreenToTile(screenPos, zoom, rotationDegrees, projectionMode);
        var approxCol = (int)MathF.Floor(approx.X);
        var approxRow = (int)MathF.Floor(approx.Y);
        var maxElevationOffset = (TileMap.MaxElevation / 2) + 2;
        var bestDepth = float.MaxValue;
        var found = false;

        for (var candidateRow = approxRow - maxElevationOffset; candidateRow <= approxRow + maxElevationOffset; candidateRow++)
        {
            if ((uint)candidateRow >= (uint)map.Rows)
            {
                continue;
            }

            for (var candidateCol = approxCol - maxElevationOffset; candidateCol <= approxCol + maxElevationOffset; candidateCol++)
            {
                if ((uint)candidateCol >= (uint)map.Cols)
                {
                    continue;
                }

                var corners = SmoothedTopFaceCorners(map, candidateCol, candidateRow, zoom, rotationDegrees, projectionMode);

                if (!PointInConvexQuad(screenPos, corners))
                {
                    continue;
                }

                var depth = TileDepth(
                    candidateCol,
                    candidateRow,
                    map.Elevation[candidateRow, candidateCol],
                    Math.Max(map.Rows, map.Cols));

                if (!found || depth < bestDepth)
                {
                    bestDepth = depth;
                    col = candidateCol;
                    row = candidateRow;
                    found = true;
                }
            }
        }

        return found;
    }

    public static float TileDepth(int col, int row, int elev, int mapSize)
    {
        if (mapSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapSize), mapSize, "Map size must be greater than zero.");
        }

        var raw = row + col + elev * 0.5f;
        var max = (mapSize - 1) * 2f + TileMap.MaxElevation * 0.5f;
        return max <= 0f ? 1f : 1f - (raw / max);
    }

    public static RectangleF GetTileBounds(
        int col,
        int row,
        int elev,
        float zoom,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        var projectedElevation = projectionMode == ViewProjectionMode.TopDown ? 0 : elev;
        var topCorners = TopFaceCorners(col, row, projectedElevation, zoom, rotationDegrees, projectionMode);
        var baseCorners = TopFaceCorners(col, row, 0, zoom, rotationDegrees, projectionMode);
        var left = float.MaxValue;
        var top = float.MaxValue;
        var right = float.MinValue;
        var bottom = float.MinValue;

        Include(topCorners);
        Include(baseCorners);

        return new RectangleF(left, top, right - left, bottom - top);

        void Include(IEnumerable<Vector2> corners)
        {
            foreach (var corner in corners)
            {
                left = Math.Min(left, corner.X);
                top = Math.Min(top, corner.Y);
                right = Math.Max(right, corner.X);
                bottom = Math.Max(bottom, corner.Y);
            }
        }
    }

    public static RectangleF GetMapBounds(
        TileMap map,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        ArgumentNullException.ThrowIfNull(map);

        var bounds = RectangleF.Empty;
        var hasBounds = false;

        for (var row = 0; row < map.Rows; row++)
        {
            for (var col = 0; col < map.Cols; col++)
            {
                var tileBounds = GetTileBounds(col, row, map.Elevation[row, col], 1f, rotationDegrees, projectionMode);
                bounds = hasBounds ? RectangleF.Union(bounds, tileBounds) : tileBounds;
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            return projectionMode == ViewProjectionMode.TopDown
                ? new RectangleF(-TopDownTileSize * 0.5f, -TopDownTileSize * 0.5f, TopDownTileSize, TopDownTileSize)
                : new RectangleF(0f, -TileHalfH, TileW, TileH);
        }

        return bounds;
    }

    public static (float Zoom, float PanX, float PanY) FitMapToViewport(
        TileMap map,
        float viewportWidth,
        float viewportHeight,
        float padding = 0f,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        ArgumentNullException.ThrowIfNull(map);

        var mapBounds = GetMapBounds(map, rotationDegrees, projectionMode);
        var availableWidth = Math.Max(1f, viewportWidth - (padding * 2f));
        var availableHeight = Math.Max(1f, viewportHeight - (padding * 2f));
        var fitZoom = Math.Clamp(
            Math.Min(
                availableWidth / Math.Max(1f, mapBounds.Width),
                availableHeight / Math.Max(1f, mapBounds.Height)),
            IsoCamera.MinZoom,
            IsoCamera.MaxZoom);
        var centre = new Vector2(
            mapBounds.Left + (mapBounds.Width * 0.5f),
            mapBounds.Top + (mapBounds.Height * 0.5f)) * fitZoom;

        return (
            fitZoom,
            (viewportWidth * 0.5f) - centre.X,
            (viewportHeight * 0.5f) - centre.Y);
    }

    public static RectangleF GetVisibleTileBounds(
        float panX,
        float panY,
        float zoom,
        float rotationDegrees,
        float viewportWidth,
        float viewportHeight,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
    {
        if (zoom <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be greater than zero.");
        }

        var topLeft = ScreenToTile(new Vector2(-panX, -panY), zoom, rotationDegrees, projectionMode);
        var topRight = ScreenToTile(new Vector2(viewportWidth - panX, -panY), zoom, rotationDegrees, projectionMode);
        var bottomRight = ScreenToTile(new Vector2(viewportWidth - panX, viewportHeight - panY), zoom, rotationDegrees, projectionMode);
        var bottomLeft = ScreenToTile(new Vector2(-panX, viewportHeight - panY), zoom, rotationDegrees, projectionMode);

        var left = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X));
        var right = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X));
        var top = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y));
        var bottom = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y));

        return new RectangleF(left, top, right - left, bottom - top);
    }

    public static bool BoundsIntersect(RectangleF a, RectangleF b)
    {
        if (a.Width <= 0f || a.Height <= 0f || b.Width <= 0f || b.Height <= 0f)
        {
            return false;
        }

        return a.Left <= b.Right &&
               a.Right >= b.Left &&
               a.Top <= b.Bottom &&
               a.Bottom >= b.Top;
    }

    private static bool PointInConvexQuad(Vector2 point, Vector2[] quad)
    {
        var hasPositive = false;
        var hasNegative = false;

        for (var i = 0; i < quad.Length; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % quad.Length];
            var cross = Cross(b - a, point - a);

            if (cross > 0.001f)
            {
                hasPositive = true;
            }
            else if (cross < -0.001f)
            {
                hasNegative = true;
            }

            if (hasPositive && hasNegative)
            {
                return false;
            }
        }

        return true;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    private static Vector2 ProjectTileCentre(float col, float row, float elev, ViewProjectionMode projectionMode)
    {
        return projectionMode switch
        {
            ViewProjectionMode.TopDown => new Vector2(col * TopDownTileSize, row * TopDownTileSize),
            _ => new Vector2(
                (col - row) * TileHalfW,
                (col + row) * TileHalfH - elev * ElevStep),
        };
    }

    private static Vector2 UnprojectGround(Vector2 screenPos, float zoom, ViewProjectionMode projectionMode)
    {
        var x = screenPos.X / zoom;
        var y = screenPos.Y / zoom;

        return projectionMode switch
        {
            ViewProjectionMode.TopDown => new Vector2(
                x / TopDownTileSize,
                y / TopDownTileSize),
            _ => new Vector2(
                (x / TileHalfW + y / TileHalfH) * 0.5f,
                (y / TileHalfH - x / TileHalfW) * 0.5f),
        };
    }

    private static Vector2 RotateGround(Vector2 ground, float rotationDegrees)
    {
        var radians = NormalizeRotationDegrees(rotationDegrees) * (MathF.PI / 180f);

        if (Math.Abs(radians) < 0.0001f)
        {
            return ground;
        }

        var sin = MathF.Sin(radians);
        var cos = MathF.Cos(radians);
        return new Vector2(
            ground.X * cos - ground.Y * sin,
            ground.X * sin + ground.Y * cos);
    }

    private static (Vector2 top, Vector2 right, Vector2 bottom, Vector2 left) GetTopFaceCornerOffsets(
        float rotationDegrees,
        ViewProjectionMode projectionMode)
    {
        var origin = TileToScreen(0f, 0f, 0f, rotationDegrees, projectionMode);
        var colAxis = TileToScreen(1f, 0f, 0f, rotationDegrees, projectionMode) - origin;
        var rowAxis = TileToScreen(0f, 1f, 0f, rotationDegrees, projectionMode) - origin;
        var top = -(colAxis + rowAxis) * 0.5f;
        var right = (colAxis - rowAxis) * 0.5f;
        var bottom = (colAxis + rowAxis) * 0.5f;
        var left = (-colAxis + rowAxis) * 0.5f;
        return (top, right, bottom, left);
    }
}
