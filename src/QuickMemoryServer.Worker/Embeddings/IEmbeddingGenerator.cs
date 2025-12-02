namespace QuickMemoryServer.Worker.Embeddings;

public interface IEmbeddingGenerator : IDisposable
{
    int Dimension { get; }

    Task<IReadOnlyList<double>> GenerateAsync(string text, CancellationToken cancellationToken);
}
