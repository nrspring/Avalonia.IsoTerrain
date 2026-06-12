namespace IsoViewport.Controls.Contracts;

public sealed class IsoViewportRendererException : IsoViewportException
{
    public IsoViewportRendererException(
        string message,
        string? pieceId = null,
        string? pieceTypeId = null,
        string? pieceTypeDisplayName = null)
        : base(message)
    {
        PieceId = pieceId;
        PieceTypeId = pieceTypeId;
        PieceTypeDisplayName = pieceTypeDisplayName;
    }

    public IsoViewportRendererException(
        string message,
        Exception innerException,
        string? pieceId = null,
        string? pieceTypeId = null,
        string? pieceTypeDisplayName = null)
        : base(message, innerException)
    {
        PieceId = pieceId;
        PieceTypeId = pieceTypeId;
        PieceTypeDisplayName = pieceTypeDisplayName;
    }

    public string? PieceId { get; }

    public string? PieceTypeId { get; }

    public string? PieceTypeDisplayName { get; }
}
