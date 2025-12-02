namespace QuickMemoryServer.Worker.Persistence;

public sealed class JsonlParseException : Exception
{
    public JsonlParseException(string path, int lineNumber, Exception inner)
        : base($"Failed to parse JSONL entry in '{path}' at line {lineNumber}.", inner)
    {
        Path = path;
        LineNumber = lineNumber;
    }

    public string Path { get; }

    public int LineNumber { get; }
}
