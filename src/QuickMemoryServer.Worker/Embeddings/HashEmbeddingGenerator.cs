using System.Security.Cryptography;

namespace QuickMemoryServer.Worker.Embeddings;

/// <summary>
/// Deterministic fallback embedding generator used when the ONNX model is unavailable.
/// Produces a fixed-dimension vector via SHA256 hashing.
/// </summary>
public sealed class HashEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly int _dimension;

    public HashEmbeddingGenerator(int dimension)
    {
        _dimension = dimension;
    }

    public int Dimension => _dimension;

    public Task<IReadOnlyList<double>> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
        var vector = new double[_dimension];
        for (var i = 0; i < _dimension; i++)
        {
            var b = bytes[i % bytes.Length];
            vector[i] = (b - 128) / 128.0;
        }

        return Task.FromResult<IReadOnlyList<double>>(vector);
    }

    public void Dispose()
    {
        // nothing to dispose
    }
}
