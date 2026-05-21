using System.Numerics;
using Silk.NET.OpenGL;

namespace IsoViewport.Controls.Rendering;

public sealed class ObjectLayer
{
    private const uint VertexStrideBytes = 24;
    private static readonly Vector3 TreeCanopyColour = new(0.03f, 0.44f, 0.26f);
    private static readonly Vector3 TreeCanopyShadeColour = new(0.02f, 0.30f, 0.18f);
    private static readonly Vector3 TreeTrunkColour = new(0.34f, 0.20f, 0.10f);

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
        ViewProjectionMode projectionMode = ViewProjectionMode.Isometric)
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
            var objectElevation = obj.Type == (byte)ObjectType.Tree ? elevation : elevation + 1;
            var corners = IsoMath.TopFaceCorners(obj.Col, obj.Row, objectElevation, 1f, rotationDegrees, projectionMode);
            var centre = GetQuadCentre(corners);
            var halfWidth = Vector2.Distance(corners[1], corners[3]) * 0.25f;
            var halfHeight = Vector2.Distance(corners[0], corners[2]) * 0.25f;
            var depth = Math.Clamp(IsoMath.TileDepth(obj.Col, obj.Row, elevation, mapSize) - 0.002f, 0f, 1f);

            if (obj.Type == (byte)ObjectType.Tree)
            {
                EmitTree(vertices, centre, halfWidth, halfHeight, depth, projectionMode);
            }
            else
            {
                var colour = ObjectColours.GetColour(obj.Type);

                EmitQuad(
                    vertices,
                    centre + Vector2.Normalize(corners[0] - centre) * halfHeight,
                    centre + Vector2.Normalize(corners[1] - centre) * halfWidth,
                    centre + Vector2.Normalize(corners[2] - centre) * halfHeight,
                    centre + Vector2.Normalize(corners[3] - centre) * halfWidth,
                    depth,
                    colour);
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
        var trunkDepth = Math.Clamp(depth - 0.001f, 0f, 1f);

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
        var canopyDepth = Math.Clamp(depth - 0.003f, 0f, 1f);

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
            Math.Clamp(canopyDepth - 0.001f, 0f, 1f),
            TreeCanopyColour);
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
