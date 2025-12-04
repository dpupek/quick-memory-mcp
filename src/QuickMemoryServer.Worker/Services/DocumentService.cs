using System;
using System.IO;
using Markdig;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuickMemoryServer.Worker.Services;

public sealed class DocumentService
{
    private readonly ILogger<DocumentService> _logger;
    private readonly string _docsRoot;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public DocumentService(IHostEnvironment environment, ILogger<DocumentService> logger)
    {
        _logger = logger;
        var defaultRoot = Path.Combine(environment.ContentRootPath, "docs");
        if (Directory.Exists(defaultRoot))
        {
            _docsRoot = defaultRoot;
        }
        else
        {
            _docsRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "docs"));
        }
    }

    public string RenderMarkdown(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_docsRoot, relativePath));
        if (!File.Exists(path))
        {
            _logger.LogWarning("Documentation file not found: {Path}", path);
            return "<p class=\"text-danger\">Documentation is unavailable.</p>";
        }

        try
        {
            var markdown = File.ReadAllText(path);
            return Markdown.ToHtml(markdown, Pipeline);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render markdown for {Path}", path);
            return "<p class=\"text-danger\">Failed to render documentation.</p>";
        }
    }
}
