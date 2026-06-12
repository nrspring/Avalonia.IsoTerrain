namespace IsoViewport.Controls.Contracts;

public interface IMapPieceRenderer
{
    void Render(IMapPieceRenderContext context, IMapPiece piece);
}
