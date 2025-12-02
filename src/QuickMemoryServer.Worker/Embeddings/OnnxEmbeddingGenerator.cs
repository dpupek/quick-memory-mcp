using Microsoft.ML.OnnxRuntime;
using Microsoft.Extensions.Logging;

namespace QuickMemoryServer.Worker.Embeddings;

public sealed class OnnxEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly InferenceSession _session;
    private readonly ILogger<OnnxEmbeddingGenerator> _logger;
    private readonly int _dimension;
    private readonly HashEmbeddingGenerator _fallback;

    public OnnxEmbeddingGenerator(string modelPath, int dimension, ILogger<OnnxEmbeddingGenerator> logger)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Embedding model not found.", modelPath);
        }

        _logger = logger;

        var options = new Microsoft.ML.OnnxRuntime.SessionOptions();
        _session = new InferenceSession(modelPath, options);
        _dimension = dimension > 0 ? dimension : 384;
        _fallback = new HashEmbeddingGenerator(_dimension);
    }

    public int Dimension => _dimension;

    public Task<IReadOnlyList<double>> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        // Placeholder: actual tokenization/inference is model-specific. For now, fallback to deterministic hash embeddings,
        // while keeping the ONNX session open for future enhancement.
        _logger.LogTrace("Using fallback embedding path for text length {Length}", text?.Length ?? 0);
        return _fallback.GenerateAsync(text ?? string.Empty, cancellationToken);
    }

    public void Dispose()
    {
        _session.Dispose();
        _fallback.Dispose();
    }
}
