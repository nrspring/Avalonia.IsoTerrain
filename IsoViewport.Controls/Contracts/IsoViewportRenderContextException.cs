namespace IsoViewport.Controls.Contracts;

public sealed class IsoViewportRenderContextException : IsoViewportException
{
    public IsoViewportRenderContextException(string message)
        : base(message)
    {
    }

    public IsoViewportRenderContextException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
