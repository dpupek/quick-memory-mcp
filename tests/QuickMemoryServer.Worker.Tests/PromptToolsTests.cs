using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Embeddings;
using QuickMemoryServer.Worker.Memory;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Persistence;
using QuickMemoryServer.Worker.Search;
using QuickMemoryServer.Worker.Services;
using QuickMemoryServer.Worker.Validation;
using Xunit;

namespace QuickMemoryServer.Worker.Tests;

public sealed class PromptToolsTests : IDisposable
{
    private readonly PromptTestContext _ctx;

    public PromptToolsTests()
    {
        _ctx = new PromptTestContext();
    }

    [Fact]
    public async Task UpsertEntry_InvalidPromptArgsBlock_ReturnsError()
    {
        #region Arrange
        var entry = new MemoryEntry
        {
            SchemaVersion = 1,
            Id = "prompt-1",
            Project = "prompts-repository",
            Kind = "prompt",
            Title = "Broken prompt",
            Tags = new[] { "prompt-template", "category:test" },
            Body = new JsonObject { ["text"] = "```prompt-args\nnot-json\n```\nBody" },
            CurationTier = "curated"
        };

        var context = PromptTestContext.CreateRequestContext(PermissionTier.Editor);
        var optionsMonitor = _ctx.OptionsMonitor;
        #endregion

        #region Act
        var result = await MemoryMcpTools.UpsertEntry(
            endpoint: "prompts-repository",
            entry: entry,
            router: null!,
            optionsMonitor: optionsMonitor,
            context: context,
            cancellationToken: CancellationToken.None);
        #endregion

        #region Assert
        var error = Assert.IsType<CallToolResult>(result);
        Assert.True(error.IsError);
        Assert.Contains("invalid prompt-args block", error.Content.OfType<TextContentBlock>().First().Text);
        #endregion
    }

    [Fact]
    public void GetPromptTemplate_MissingRequiredArguments_ReturnsError()
    {
        #region Arrange
        _ctx.WritePromptEntry(
            id: "prompt-required",
            body: "```prompt-args\n[ { \"name\": \"projectKey\", \"description\": \"endpoint\", \"required\": true } ]\n```\nHello {{projectKey}}",
            tags: new[] { "prompt-template", "category:test" });
        _ctx.InitializeStores();
        #endregion

        #region Act
        var result = MemoryMcpTools.GetPromptTemplate(
            name: "prompt-required",
            arguments: new Dictionary<string, string>(),
            router: _ctx.Router);
        #endregion

        #region Assert
        var error = Assert.IsType<CallToolResult>(result);
        Assert.True(error.IsError);
        Assert.Contains("missing-arguments", error.Content.OfType<TextContentBlock>().First().Text);
        #endregion
    }

    [Fact]
    public async Task UpsertEntry_PermanentPromptRequiresAdminTier()
    {
        #region Arrange
        var entry = new MemoryEntry
        {
            SchemaVersion = 1,
            Id = "prompt-permanent",
            Project = "prompts-repository",
            Kind = "prompt",
            Title = "Permanent prompt",
            Tags = new[] { "prompt-template", "category:test" },
            Body = new JsonObject { ["text"] = "Body" },
            CurationTier = "canonical",
            IsPermanent = true
        };

        var context = PromptTestContext.CreateRequestContext(PermissionTier.Reader);
        var optionsMonitor = _ctx.OptionsMonitor;
        #endregion

        #region Act
        var result = await MemoryMcpTools.UpsertEntry(
            endpoint: "prompts-repository",
            entry: entry,
            router: null!,
            optionsMonitor: optionsMonitor,
            context: context,
            cancellationToken: CancellationToken.None);
        #endregion

        #region Assert
        var error = Assert.IsType<CallToolResult>(result);
        Assert.True(error.IsError);
        Assert.Contains("admin tier", error.Content.OfType<TextContentBlock>().First().Text);
        #endregion
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }
}

internal sealed class PromptTestContext : IDisposable
{
    private readonly string _tempDir;
    private readonly ServerOptions _options;
    private readonly MemoryStoreFactory _storeFactory;
    private readonly MemoryRouter _router;
    private readonly JsonlRepository _repository;
    private readonly EmbeddingService _embeddingService;
    private readonly IOptionsMonitor<ServerOptions> _optionsMonitor;

    public MemoryRouter Router => _router;
    public IOptionsMonitor<ServerOptions> OptionsMonitor => _optionsMonitor;

    public PromptTestContext()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "prompt-tests-" + Guid.NewGuid().ToString("N"));

        _options = new ServerOptions
        {
            Global = new GlobalOptions
            {
                ServiceName = "test-server",
                EmbeddingDims = 3
            },
            Endpoints =
            {
                ["prompts-repository"] = new EndpointOptions
                {
                    Name = "Prompts",
                    Slug = "prompts-repository",
                    StoragePath = Path.Combine(_tempDir, "prompts-repository"),
                    IncludeInSearchByDefault = false,
                    InheritShared = false
                }
            }
        };

        _optionsMonitor = new TestOptionsMonitor(_options);
        var validator = new MemoryEntryValidator();
        _repository = new JsonlRepository(validator, NullLogger<JsonlRepository>.Instance);
        var embeddingGenerator = new FakeEmbeddingGenerator(_options.Global.EmbeddingDims);
        _embeddingService = new EmbeddingService(embeddingGenerator, NullLogger<EmbeddingService>.Instance);
        var searchEngine = new SearchEngine(NullLoggerFactory.Instance);

        _storeFactory = new MemoryStoreFactory(
            _optionsMonitor,
            _repository,
            validator,
            _embeddingService,
            searchEngine,
            NullLoggerFactory.Instance);

        _router = new MemoryRouter(_storeFactory, _optionsMonitor, NullLogger<MemoryRouter>.Instance);
    }

    public void WritePromptEntry(string id, string body, string[] tags)
    {
        var dir = Path.Combine(_tempDir, "prompts-repository");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "entries.jsonl");
        var entry = new MemoryEntry
        {
            SchemaVersion = 1,
            Id = id,
            Project = "prompts-repository",
            Kind = "prompt",
            Title = id,
            Body = new JsonObject { ["text"] = body },
            Tags = tags,
            CurationTier = "curated",
            IsPermanent = true,
            Confidence = 0.9,
            Timestamps = new MemoryTimestamps
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(entry, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        File.AppendAllText(path, json + "\n");
    }

    public void InitializeStores()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _storeFactory.InitializeAllAsync(cts.Token).GetAwaiter().GetResult();
    }

    public static RequestContext<CallToolRequestParams> CreateRequestContext(PermissionTier tier)
    {
        var context = new RequestContext<CallToolRequestParams>(
            new TestMcpServer(),
            new JsonRpcRequest { Method = "tools/call" });
        context.Items[McpAuthorizationContext.TierItem] = tier;
        return context;
    }

    private sealed class TestMcpServer : McpServer
    {
        private static readonly IServiceProvider EmptyServices = new EmptyServiceProvider();
        private static readonly ClientCapabilities EmptyCapabilities = new();
        private static readonly Implementation EmptyClientInfo = new()
        {
            Name = "test-client",
            Version = "0.0"
        };
        private static readonly McpServerOptions EmptyOptions = new();

        public override IServiceProvider Services => EmptyServices;
        public override McpServerOptions ServerOptions => EmptyOptions;
        public override ClientCapabilities ClientCapabilities => EmptyCapabilities;
        public override Implementation ClientInfo => EmptyClientInfo;
        public override string SessionId => "test-session";
        public override string NegotiatedProtocolVersion => "2025-06-18";
        public override LoggingLevel? LoggingLevel => null;

        public override Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonValue.Create("ok")
            });
        }

        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
        {
            return NullAsyncDisposable.Instance;
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class NullAsyncDisposable : IAsyncDisposable
        {
            public static readonly NullAsyncDisposable Instance = new();
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class EmptyServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup errors in tests
        }

        _embeddingService.Dispose();
    }
}

file sealed class FakeEmbeddingGenerator : IEmbeddingGenerator
{
    public FakeEmbeddingGenerator(int dimension)
    {
        Dimension = dimension;
    }

    public int Dimension { get; }

    public Task<IReadOnlyList<double>> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        IReadOnlyList<double> vec = Enumerable.Repeat(0.0, Dimension).ToArray();
        return Task.FromResult(vec);
    }

    public void Dispose()
    {
    }
}

file sealed class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
{
    private readonly ServerOptions _options;

    public TestOptionsMonitor(ServerOptions options)
    {
        _options = options;
    }

    public ServerOptions CurrentValue => _options;

    public ServerOptions Get(string? name) => _options;

    public IDisposable OnChange(Action<ServerOptions, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
