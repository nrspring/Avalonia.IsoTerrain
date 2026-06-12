namespace IsoViewport.Controls.Contracts;

public sealed class NullMapPieceRenderer : IMapPieceRenderer
{
    public static NullMapPieceRenderer Instance { get; } = new();

    private NullMapPieceRenderer()
    {
    }

    public void Render(IMapPieceRenderContext context, IMapPiece piece)
    {
    }
}
