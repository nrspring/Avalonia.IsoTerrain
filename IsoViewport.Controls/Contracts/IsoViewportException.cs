namespace IsoViewport.Controls.Contracts;

public abstract class IsoViewportException : Exception
{
    protected IsoViewportException(string message)
        : base(message)
    {
    }

    protected IsoViewportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
