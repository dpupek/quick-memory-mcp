namespace QuickMemoryServer.Worker.Validation;

public sealed class MemoryValidationException : Exception
{
    public MemoryValidationException(string message)
        : base(message)
    {
    }
}
