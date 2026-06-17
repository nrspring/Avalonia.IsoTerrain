using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IsoViewport.Controls.Contracts;
using IsoViewport.Controls.Rendering;

namespace IsoViewport.Controls.Controls;

internal sealed class MapPieceOverlay : Control
{
    private IReadOnlyDictionary<string, IMapPieceTypeDefinition> _pieceTypeDefinitions =
        new Dictionary<string, IMapPieceTypeDefinition>(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, IMapPiece> _pieces =
        new Dictionary<string, IMapPiece>(StringComparer.Ordinal);

    public MapPieceOverlay()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    public TileMap? TileMap { get; set; }

    public float CameraZoom { get; set; } = 1f;

    public float CameraPanX { get; set; }

    public float CameraPanY { get; set; }

    public float CameraRotationDegrees { get; set; }

    public ViewProjectionMode ViewProjectionMode { get; set; } = ViewProjectionMode.ThreeD;

    public TerrainRenderMode RenderMode { get; set; } = TerrainRenderMode.Voxel;

    internal IReadOnlyDictionary<string, IMapPieceTypeDefinition> PieceTypeDefinitions
    {
        get => _pieceTypeDefinitions;
        set
        {
            _pieceTypeDefinitions = value;
            InvalidateVisual();
        }
    }

    internal IReadOnlyDictionary<string, IMapPiece> Pieces
    {
        get => _pieces;
        set
        {
            _pieces = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        RenderPieces(context);
    }

    internal void RenderPieces(DrawingContext? drawingContext)
    {
        if (TileMap is not { } map || !ShouldRenderPieces(RenderMode))
        {
            return;
        }

        foreach (var renderable in BuildRenderablePieces(map, PieceTypeDefinitions, Pieces.Values, CameraRotationDegrees))
        {
            var renderContext = new MapPieceRenderContext(
                map,
                drawingContext,
                renderable.Piece.Tile,
                RenderMode,
                ViewProjectionMode,
                CameraZoom,
                CameraPanX,
                CameraPanY,
                CameraRotationDegrees);

            try
            {
                renderable.TypeDefinition.Renderer.Render(renderContext, renderable.Piece);
            }
            catch (Exception ex)
            {
                throw CreateRendererException(renderable, ex);
            }
        }
    }

    internal static IReadOnlyList<RenderablePiece> BuildRenderablePieces(
        TileMap map,
        IReadOnlyDictionary<string, IMapPieceTypeDefinition> pieceTypeDefinitions,
        IEnumerable<IMapPiece> pieces,
        float cameraRotationDegrees)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(pieceTypeDefinitions);
        ArgumentNullException.ThrowIfNull(pieces);

        var renderables = new List<RenderablePiece>();

        foreach (var piece in pieces)
        {
            if (!piece.IsVisible)
            {
                continue;
            }

            if (!pieceTypeDefinitions.TryGetValue(piece.TypeId, out var typeDefinition))
            {
                throw new IsoViewportValidationException($"Pieces entry '{piece.Id}' references unknown piece type '{piece.TypeId}'.");
            }

            var tile = piece.Tile;

            if ((uint)tile.Row >= (uint)map.Rows || (uint)tile.Column >= (uint)map.Cols)
            {
                throw new IsoViewportValidationException($"Pieces entry '{piece.Id}' tile coordinate '{tile}' is outside the loaded map.");
            }

            var elevation = map.Elevation[tile.Row, tile.Column];
            var depth = IsoMath.TileDepth(tile.Column, tile.Row, elevation, Math.Max(map.Rows, map.Cols), cameraRotationDegrees);
            var effectiveZLayer = piece.ZLayerOverride ?? typeDefinition.DefaultZLayer;
            renderables.Add(new RenderablePiece(piece, typeDefinition, effectiveZLayer, depth));
        }

        return renderables
            .OrderByDescending(renderable => renderable.TileDepth)
            .ThenBy(renderable => renderable.EffectiveZLayer)
            .ThenBy(renderable => renderable.Piece.Id, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool ShouldRenderPieces(TerrainRenderMode renderMode)
    {
        return renderMode is TerrainRenderMode.Voxel or TerrainRenderMode.ShadedRelief;
    }

    private static IsoViewportRendererException CreateRendererException(RenderablePiece renderable, Exception exception)
    {
        var typeDefinition = renderable.TypeDefinition;
        var message = $"Renderer for piece '{renderable.Piece.Id}' of type '{renderable.Piece.TypeId}' failed.";

        if (!string.IsNullOrWhiteSpace(typeDefinition.DisplayName))
        {
            message += $" Display name: '{typeDefinition.DisplayName}'.";
        }

        return new IsoViewportRendererException(
            message,
            exception,
            renderable.Piece.Id,
            renderable.Piece.TypeId,
            typeDefinition.DisplayName);
    }

    internal readonly record struct RenderablePiece(
        IMapPiece Piece,
        IMapPieceTypeDefinition TypeDefinition,
        int EffectiveZLayer,
        float TileDepth);
}
