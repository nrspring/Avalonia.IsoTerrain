namespace IsoViewport.Controls.Contracts;

public sealed class IsoViewportSetupException : IsoViewportException
{
    public IsoViewportSetupException(string message)
        : base(message)
    {
    }

    public IsoViewportSetupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
