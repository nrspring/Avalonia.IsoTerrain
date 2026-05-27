using System.Numerics;
using Silk.NET.OpenGL;

namespace IsoViewport.Controls.Rendering;

public sealed class ObjectLayer
{
    private const uint VertexStrideBytes = 24;
    private const float ObjectDepthBias = 0.001f;
    private const float ObjectLayerDepthStep = 0.00005f;
    private static readonly Vector3 TreeCanopyColour = new(0.03f, 0.44f, 0.26f);
    private static readonly Vector3 TreeCanopyShadeColour = new(0.02f, 0.30f, 0.18f);
    private static readonly Vector3 TreeTrunkColour = new(0.34f, 0.20f, 0.10f);
    private static readonly Vector3 ShadowColour = new(0.04f, 0.05f, 0.05f);
    private static readonly Vector3 StoneLightColour = new(0.64f, 0.63f, 0.60f);
    private static readonly Vector3 StoneMidColour = new(0.47f, 0.47f, 0.45f);
    private static readonly Vector3 StoneDarkColour = new(0.31f, 0.32f, 0.32f);
    private static readonly Vector3 IronLightColour = new(0.78f, 0.40f, 0.22f);
    private static readonly Vector3 IronMidColour = new(0.55f, 0.24f, 0.16f);
    private static readonly Vector3 IronDarkColour = new(0.24f, 0.16f, 0.13f);
    private static readonly Vector3 OilColour = new(0.03f, 0.03f, 0.04f);
    private static readonly Vector3 OilHighlightColour = new(0.34f, 0.36f, 0.38f);
    private static readonly Vector3 RareMetalsLightColour = new(0.62f, 0.93f, 0.98f);
    private static readonly Vector3 RareMetalsMidColour = new(0.25f, 0.66f, 0.74f);
    private static readonly Vector3 RareMetalsDarkColour = new(0.11f, 0.36f, 0.43f);
    private static readonly Vector3 ReedLightColour = new(0.74f, 0.74f, 0.30f);
    private static readonly Vector3 ReedDarkColour = new(0.36f, 0.42f, 0.16f);
    private static readonly Vector3 CattailColour = new(0.24f, 0.14f, 0.06f);

    private readonly List<TileObject> _objects = [];
    private uint _vbo;
    private int _vertCount;
    private bool _dirty = true;

    public event Action? Changed;

    public IReadOnlyList<TileObject> Objects => _objects;

    public int Count => _objects.Count;

    public bool Dirty => _dirty;

    public void Add(TileObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        _objects.Add(obj);
        obj.Dirty = true;
        MarkDirty();
    }

    public void Remove(int col, int row, byte? type = null)
    {
        var removed = _objects.RemoveAll(obj =>
            obj.Col == col &&
            obj.Row == row &&
            (type is null || obj.Type == type.Value));

        if (removed > 0)
        {
            MarkDirty();
        }
    }

    public void Move(TileObject obj, int newCol, int newRow)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (!_objects.Contains(obj))
        {
            return;
        }

        obj.Col = newCol;
        obj.Row = newRow;
        obj.Dirty = true;
        MarkDirty();
    }

    public bool Contains(int col, int row, byte? type = null)
    {
        return _objects.Any(obj =>
            obj.Col == col &&
            obj.Row == row &&
            (type is null || obj.Type == type.Value));
    }

    public unsafe void RebuildVbo(
        GL gl,
        TileMap map,
        float rotationDegrees = 0f,
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric,
        TerrainRenderMode renderMode = TerrainRenderMode.Terrain)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(map);

        if (_vbo == 0)
        {
            _vbo = gl.GenBuffer();
        }

        var mapSize = Math.Max(map.Rows, map.Cols);
        var vertices = new List<float>(_objects.Count * 6 * 6);

        foreach (var obj in _objects)
        {
            if ((uint)obj.Row >= (uint)map.Rows || (uint)obj.Col >= (uint)map.Cols)
            {
                continue;
            }

            var elevation = map.Elevation[obj.Row, obj.Col];

            if (renderMode == TerrainRenderMode.Voxel &&
                projectionMode == ViewProjectionMode.Isometric &&
                IsOccludedByForegroundVoxel(map, obj.Col, obj.Row, elevation, rotationDegrees))
            {
                continue;
            }

            var objectElevation = ObjectSitsOnTileSurface(obj.Type) ? elevation : elevation + 1;
            var corners = IsoMath.TopFaceCorners(obj.Col, obj.Row, objectElevation, 1f, rotationDegrees, projectionMode);
            var centre = GetQuadCentre(corners);
            var halfWidth = Vector2.Distance(corners[1], corners[3]) * 0.25f;
            var halfHeight = Vector2.Distance(corners[0], corners[2]) * 0.25f;
            var depth = Math.Clamp(IsoMath.TileDepth(obj.Col, obj.Row, elevation, mapSize, rotationDegrees) - ObjectDepthBias, 0f, 1f);

            switch ((ObjectType)obj.Type)
            {
                case ObjectType.Tree:
                    EmitTree(vertices, centre, halfWidth, halfHeight, depth, projectionMode);
                    break;
                case ObjectType.StoneDeposit:
                    EmitStoneDeposit(vertices, centre, halfWidth, halfHeight, depth);
                    break;
                case ObjectType.IronDeposit:
                    EmitIronDeposit(vertices, centre, halfWidth, halfHeight, depth);
                    break;
                case ObjectType.OilSeep:
                    EmitOilSeep(vertices, centre, halfWidth, halfHeight, depth);
                    break;
                case ObjectType.RareMetalsDeposit:
                    EmitRareMetalsDeposit(vertices, centre, halfWidth, halfHeight, depth, projectionMode);
                    break;
                case ObjectType.SwampReeds:
                    EmitSwampReeds(vertices, centre, halfWidth, halfHeight, depth, projectionMode);
                    break;
                default:
                    var colour = ObjectColours.GetColour(obj.Type);

                    EmitQuad(
                        vertices,
                        centre + Vector2.Normalize(corners[0] - centre) * halfHeight,
                        centre + Vector2.Normalize(corners[1] - centre) * halfWidth,
                        centre + Vector2.Normalize(corners[2] - centre) * halfHeight,
                        centre + Vector2.Normalize(corners[3] - centre) * halfWidth,
                        depth,
                        colour);
                    break;
            }

            obj.Dirty = false;
        }

        _vertCount = vertices.Count / 6;
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        if (vertices.Count == 0)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, 0, null, BufferUsageARB.DynamicDraw);
        }
        else
        {
            var data = vertices.ToArray();

            fixed (float* dataPtr = data)
            {
                gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    (nuint)(data.Length * sizeof(float)),
                    dataPtr,
                    BufferUsageARB.DynamicDraw);
            }
        }

        _dirty = false;
    }

    public void Draw(
        GL gl,
        uint program,
        int locViewport,
        int locPan,
        int locZoom,
        float width,
        float height,
        float zoom,
        float panX,
        float panY)
    {
        if (_vertCount == 0 || _vbo == 0)
        {
            return;
        }

        gl.UseProgram(program);
        gl.Uniform2(locViewport, width, height);
        gl.Uniform2(locPan, panX, panY);
        gl.Uniform1(locZoom, zoom);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        SetAttribPointers(gl);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertCount);
    }

    public void Delete(GL gl)
    {
        if (_vbo != 0)
        {
            gl.DeleteBuffer(_vbo);
            _vbo = 0;
        }

        _vertCount = 0;
        _dirty = true;
    }

    private void MarkDirty()
    {
        _dirty = true;
        Changed?.Invoke();
    }

    private static void SetAttribPointers(GL gl)
    {
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VertexStrideBytes, IntPtr.Zero);

        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, VertexStrideBytes, (IntPtr)8);

        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, VertexStrideBytes, (IntPtr)12);
    }

    private static Vector2 GetQuadCentre(ReadOnlySpan<Vector2> corners)
    {
        return (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
    }

    private static bool ObjectSitsOnTileSurface(byte type)
    {
        return (ObjectType)type is
            ObjectType.Tree or
            ObjectType.StoneDeposit or
            ObjectType.IronDeposit or
            ObjectType.OilSeep or
            ObjectType.RareMetalsDeposit or
            ObjectType.SwampReeds;
    }

    internal static bool IsOccludedByForegroundVoxel(
        TileMap map,
        int col,
        int row,
        int elevation,
        float rotationDegrees)
    {
        ArgumentNullException.ThrowIfNull(map);

        var mapSize = Math.Max(map.Rows, map.Cols);
        var objectGroundDepth = IsoMath.TileDepth(col, row, 0, mapSize, rotationDegrees);

        return IsOccludedByForegroundNeighbor(row - 1, col) ||
               IsOccludedByForegroundNeighbor(row + 1, col) ||
               IsOccludedByForegroundNeighbor(row, col - 1) ||
               IsOccludedByForegroundNeighbor(row, col + 1);

        bool IsOccludedByForegroundNeighbor(int neighborRow, int neighborCol)
        {
            if ((uint)neighborRow >= (uint)map.Rows || (uint)neighborCol >= (uint)map.Cols)
            {
                return false;
            }

            if (map.Elevation[neighborRow, neighborCol] <= elevation)
            {
                return false;
            }

            var neighborGroundDepth = IsoMath.TileDepth(neighborCol, neighborRow, 0, mapSize, rotationDegrees);
            return neighborGroundDepth < objectGroundDepth;
        }
    }

    private static void EmitTree(
        List<float> vertices,
        Vector2 centre,
        float halfWidth,
        float halfHeight,
        float depth,
        ViewProjectionMode projectionMode)
    {
        var treeHeight = projectionMode == ViewProjectionMode.TopDown
            ? halfHeight * 1.05f
            : IsoMath.ElevStep * 1.20f;
        var trunkHalfWidth = Math.Max(2f, halfWidth * 0.14f);
        var trunkBottom = centre + new Vector2(0f, halfHeight * 0.26f);
        var trunkTop = centre - new Vector2(0f, treeHeight * 0.62f);
        var trunkDepth = Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f);

        EmitQuad(
            vertices,
            trunkTop - new Vector2(trunkHalfWidth, 0f),
            trunkTop + new Vector2(trunkHalfWidth, 0f),
            trunkBottom + new Vector2(trunkHalfWidth * 0.82f, 0f),
            trunkBottom - new Vector2(trunkHalfWidth * 0.82f, 0f),
            trunkDepth,
            TreeTrunkColour);

        var canopyCentre = centre - new Vector2(0f, treeHeight * 0.78f);
        var canopyHalfWidth = halfWidth * 0.88f;
        var canopyHalfHeight = halfHeight * 0.95f;
        var canopyDepth = Math.Clamp(depth - ObjectLayerDepthStep * 3f, 0f, 1f);

        EmitQuad(
            vertices,
            canopyCentre - new Vector2(0f, canopyHalfHeight),
            canopyCentre + new Vector2(canopyHalfWidth, 0f),
            canopyCentre + new Vector2(0f, canopyHalfHeight),
            canopyCentre - new Vector2(canopyHalfWidth, 0f),
            canopyDepth,
            TreeCanopyShadeColour);

        EmitQuad(
            vertices,
            canopyCentre - new Vector2(0f, canopyHalfHeight * 0.78f),
            canopyCentre + new Vector2(canopyHalfWidth * 0.72f, 0f),
            canopyCentre + new Vector2(0f, canopyHalfHeight * 0.58f),
            canopyCentre - new Vector2(canopyHalfWidth * 0.72f, 0f),
            Math.Clamp(canopyDepth - ObjectLayerDepthStep, 0f, 1f),
            TreeCanopyColour);
    }

    private static void EmitStoneDeposit(List<float> vertices, Vector2 centre, float halfWidth, float halfHeight, float depth)
    {
        EmitRock(
            vertices,
            centre + new Vector2(-halfWidth * 0.30f, halfHeight * 0.04f),
            halfWidth * 0.36f,
            halfHeight * 0.52f,
            Math.Clamp(depth - ObjectLayerDepthStep * 2f, 0f, 1f),
            StoneLightColour,
            StoneMidColour,
            StoneDarkColour);

        EmitRock(
            vertices,
            centre + new Vector2(halfWidth * 0.16f, -halfHeight * 0.08f),
            halfWidth * 0.44f,
            halfHeight * 0.62f,
            Math.Clamp(depth - ObjectLayerDepthStep * 3f, 0f, 1f),
            StoneLightColour,
            StoneMidColour,
            StoneDarkColour);

        EmitRock(
            vertices,
            centre + new Vector2(halfWidth * 0.44f, halfHeight * 0.14f),
            halfWidth * 0.26f,
            halfHeight * 0.40f,
            Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f),
            StoneLightColour,
            StoneMidColour,
            StoneDarkColour);
    }

    private static void EmitIronDeposit(List<float> vertices, Vector2 centre, float halfWidth, float halfHeight, float depth)
    {
        EmitShard(
            vertices,
            centre + new Vector2(-halfWidth * 0.28f, halfHeight * 0.14f),
            halfWidth * 0.24f,
            halfHeight * 0.76f,
            Math.Clamp(depth - ObjectLayerDepthStep * 2f, 0f, 1f),
            IronLightColour,
            IronMidColour,
            IronDarkColour);

        EmitShard(
            vertices,
            centre + new Vector2(halfWidth * 0.08f, halfHeight * 0.16f),
            halfWidth * 0.34f,
            halfHeight * 0.92f,
            Math.Clamp(depth - ObjectLayerDepthStep * 3f, 0f, 1f),
            IronLightColour,
            IronMidColour,
            IronDarkColour);

        EmitShard(
            vertices,
            centre + new Vector2(halfWidth * 0.36f, halfHeight * 0.20f),
            halfWidth * 0.22f,
            halfHeight * 0.62f,
            Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f),
            IronLightColour,
            IronMidColour,
            IronDarkColour);
    }

    private static void EmitOilSeep(List<float> vertices, Vector2 centre, float halfWidth, float halfHeight, float depth)
    {
        var poolDepth = Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f);
        var sheenDepth = Math.Clamp(poolDepth - ObjectLayerDepthStep, 0f, 1f);
        var edgeColour = OilColour * 0.74f;

        EmitQuad(
            vertices,
            centre + new Vector2(-halfWidth * 0.78f, -halfHeight * 0.08f),
            centre + new Vector2(-halfWidth * 0.14f, -halfHeight * 0.40f),
            centre + new Vector2(halfWidth * 0.58f, -halfHeight * 0.30f),
            centre + new Vector2(halfWidth * 0.82f, halfHeight * 0.08f),
            poolDepth,
            edgeColour);

        EmitQuad(
            vertices,
            centre + new Vector2(-halfWidth * 0.78f, -halfHeight * 0.08f),
            centre + new Vector2(halfWidth * 0.82f, halfHeight * 0.08f),
            centre + new Vector2(halfWidth * 0.40f, halfHeight * 0.42f),
            centre + new Vector2(-halfWidth * 0.54f, halfHeight * 0.36f),
            poolDepth,
            edgeColour);

        EmitQuad(
            vertices,
            centre + new Vector2(-halfWidth * 0.52f, -halfHeight * 0.06f),
            centre + new Vector2(-halfWidth * 0.04f, -halfHeight * 0.26f),
            centre + new Vector2(halfWidth * 0.44f, -halfHeight * 0.16f),
            centre + new Vector2(halfWidth * 0.54f, halfHeight * 0.08f),
            Math.Clamp(poolDepth - ObjectLayerDepthStep * 0.5f, 0f, 1f),
            OilColour);

        EmitQuad(
            vertices,
            centre + new Vector2(-halfWidth * 0.52f, -halfHeight * 0.06f),
            centre + new Vector2(halfWidth * 0.54f, halfHeight * 0.08f),
            centre + new Vector2(halfWidth * 0.28f, halfHeight * 0.26f),
            centre + new Vector2(-halfWidth * 0.36f, halfHeight * 0.22f),
            poolDepth,
            OilColour);

        EmitQuad(
            vertices,
            centre + new Vector2(-halfWidth * 0.16f, -halfHeight * 0.20f),
            centre + new Vector2(halfWidth * 0.34f, -halfHeight * 0.13f),
            centre + new Vector2(halfWidth * 0.22f, -halfHeight * 0.03f),
            centre + new Vector2(-halfWidth * 0.26f, -halfHeight * 0.08f),
            sheenDepth,
            OilHighlightColour);

        EmitQuad(
            vertices,
            centre + new Vector2(-halfWidth * 0.52f, halfHeight * 0.08f),
            centre + new Vector2(-halfWidth * 0.20f, halfHeight * 0.12f),
            centre + new Vector2(-halfWidth * 0.26f, halfHeight * 0.20f),
            centre + new Vector2(-halfWidth * 0.56f, halfHeight * 0.18f),
            Math.Clamp(sheenDepth - ObjectLayerDepthStep, 0f, 1f),
            OilHighlightColour * 0.58f);
    }

    private static void EmitRareMetalsDeposit(
        List<float> vertices,
        Vector2 centre,
        float halfWidth,
        float halfHeight,
        float depth,
        ViewProjectionMode projectionMode)
    {
        var crystalHeight = projectionMode == ViewProjectionMode.TopDown
            ? halfHeight * 0.84f
            : IsoMath.ElevStep * 0.72f;

        EmitCrystal(
            vertices,
            centre + new Vector2(-halfWidth * 0.28f, halfHeight * 0.14f),
            halfWidth * 0.20f,
            crystalHeight * 0.72f,
            Math.Clamp(depth - ObjectLayerDepthStep * 2f, 0f, 1f));

        EmitCrystal(
            vertices,
            centre + new Vector2(halfWidth * 0.06f, halfHeight * 0.18f),
            halfWidth * 0.28f,
            crystalHeight,
            Math.Clamp(depth - ObjectLayerDepthStep * 4f, 0f, 1f));

        EmitCrystal(
            vertices,
            centre + new Vector2(halfWidth * 0.32f, halfHeight * 0.19f),
            halfWidth * 0.18f,
            crystalHeight * 0.58f,
            Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f));
    }

    private static void EmitSwampReeds(
        List<float> vertices,
        Vector2 centre,
        float halfWidth,
        float halfHeight,
        float depth,
        ViewProjectionMode projectionMode)
    {
        var reedHeight = projectionMode == ViewProjectionMode.TopDown
            ? halfHeight * 1.05f
            : IsoMath.ElevStep * 0.86f;
        var reedWidth = Math.Max(1.5f, halfWidth * 0.045f);
        var baseCentre = centre + new Vector2(0f, halfHeight * 0.16f);
        var reedDepth = Math.Clamp(depth - ObjectLayerDepthStep * 2f, 0f, 1f);

        EmitScreenStroke(vertices, baseCentre, baseCentre + new Vector2(-halfWidth * 0.04f, -reedHeight), reedWidth, reedDepth, ReedLightColour);
        EmitScreenStroke(vertices, baseCentre + new Vector2(halfWidth * 0.13f, halfHeight * 0.04f), baseCentre + new Vector2(halfWidth * 0.22f, -reedHeight * 0.82f), reedWidth * 0.9f, reedDepth, ReedLightColour * 0.92f);
        EmitScreenStroke(vertices, baseCentre + new Vector2(-halfWidth * 0.12f, halfHeight * 0.05f), baseCentre + new Vector2(-halfWidth * 0.24f, -reedHeight * 0.74f), reedWidth * 0.9f, reedDepth, ReedDarkColour);
        EmitScreenStroke(vertices, baseCentre + new Vector2(halfWidth * 0.28f, halfHeight * 0.08f), baseCentre + new Vector2(halfWidth * 0.20f, -reedHeight * 0.55f), reedWidth * 0.76f, reedDepth, ReedDarkColour * 0.92f);

        EmitQuad(
            vertices,
            baseCentre + new Vector2(halfWidth * 0.18f, -reedHeight * 0.80f),
            baseCentre + new Vector2(halfWidth * 0.30f, -reedHeight * 0.74f),
            baseCentre + new Vector2(halfWidth * 0.28f, -reedHeight * 0.48f),
            baseCentre + new Vector2(halfWidth * 0.16f, -reedHeight * 0.54f),
            Math.Clamp(reedDepth - ObjectLayerDepthStep, 0f, 1f),
            CattailColour);
    }

    private static void EmitRock(
        List<float> vertices,
        Vector2 baseCentre,
        float halfWidth,
        float height,
        float depth,
        Vector3 lightColour,
        Vector3 midColour,
        Vector3 darkColour)
    {
        var top = baseCentre - new Vector2(0f, height);
        var left = baseCentre - new Vector2(halfWidth, height * 0.22f);
        var right = baseCentre + new Vector2(halfWidth, -height * 0.18f);
        var bottom = baseCentre + new Vector2(0f, height * 0.20f);
        var mid = baseCentre - new Vector2(0f, height * 0.26f);

        EmitTriangle(vertices, top, left, mid, Math.Clamp(depth - ObjectLayerDepthStep * 2f, 0f, 1f), lightColour);
        EmitTriangle(vertices, top, mid, right, Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f), midColour);
        EmitQuad(vertices, left, mid, right, bottom, depth, darkColour);
    }

    private static void EmitShard(
        List<float> vertices,
        Vector2 baseCentre,
        float halfWidth,
        float height,
        float depth,
        Vector3 lightColour,
        Vector3 midColour,
        Vector3 darkColour)
    {
        var tip = baseCentre - new Vector2(halfWidth * 0.10f, height);
        var left = baseCentre - new Vector2(halfWidth, 0f);
        var right = baseCentre + new Vector2(halfWidth, -height * 0.04f);
        var centreLine = baseCentre - new Vector2(halfWidth * 0.04f, height * 0.22f);

        EmitTriangle(vertices, tip, left, centreLine, Math.Clamp(depth - ObjectLayerDepthStep * 2f, 0f, 1f), lightColour);
        EmitTriangle(vertices, tip, centreLine, right, Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f), midColour);
        EmitTriangle(vertices, left, right, baseCentre + new Vector2(0f, height * 0.10f), depth, darkColour);
    }

    private static void EmitCrystal(List<float> vertices, Vector2 baseCentre, float halfWidth, float height, float depth)
    {
        var tip = baseCentre - new Vector2(0f, height);
        var left = baseCentre - new Vector2(halfWidth, 0f);
        var right = baseCentre + new Vector2(halfWidth, 0f);
        var ridge = baseCentre - new Vector2(halfWidth * 0.18f, height * 0.22f);

        EmitTriangle(vertices, tip, left, ridge, Math.Clamp(depth - ObjectLayerDepthStep * 2f, 0f, 1f), RareMetalsLightColour);
        EmitTriangle(vertices, tip, ridge, right, Math.Clamp(depth - ObjectLayerDepthStep, 0f, 1f), RareMetalsMidColour);
        EmitTriangle(vertices, left, right, baseCentre + new Vector2(0f, halfWidth * 0.22f), depth, RareMetalsDarkColour);
    }

    private static void EmitFlatDiamond(
        List<float> vertices,
        Vector2 centre,
        float halfWidth,
        float halfHeight,
        float depth,
        Vector3 colour)
    {
        EmitQuad(
            vertices,
            centre - new Vector2(0f, halfHeight),
            centre + new Vector2(halfWidth, 0f),
            centre + new Vector2(0f, halfHeight),
            centre - new Vector2(halfWidth, 0f),
            depth,
            colour);
    }

    private static void EmitScreenStroke(
        List<float> vertices,
        Vector2 start,
        Vector2 end,
        float halfWidth,
        float depth,
        Vector3 colour)
    {
        var direction = end - start;

        if (direction.LengthSquared() < 0.0001f)
        {
            return;
        }

        var normal = Vector2.Normalize(new Vector2(-direction.Y, direction.X)) * halfWidth;
        EmitQuad(vertices, start - normal, start + normal, end + normal, end - normal, depth, colour);
    }

    private static void EmitQuad(
        List<float> vertices,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        float depth,
        Vector3 colour)
    {
        EmitVertex(vertices, a, depth, colour);
        EmitVertex(vertices, b, depth, colour);
        EmitVertex(vertices, c, depth, colour);
        EmitVertex(vertices, a, depth, colour);
        EmitVertex(vertices, c, depth, colour);
        EmitVertex(vertices, d, depth, colour);
    }

    private static void EmitTriangle(List<float> vertices, Vector2 a, Vector2 b, Vector2 c, float depth, Vector3 colour)
    {
        EmitVertex(vertices, a, depth, colour);
        EmitVertex(vertices, b, depth, colour);
        EmitVertex(vertices, c, depth, colour);
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
