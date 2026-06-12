namespace IsoViewport.Controls.Contracts;

public sealed class IsoViewportValidationException : IsoViewportException
{
    public IsoViewportValidationException(string message)
        : base(message)
    {
    }

    public IsoViewportValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
