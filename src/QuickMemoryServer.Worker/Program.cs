using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QuickMemoryServer.Worker.Configuration;
using QuickMemoryServer.Worker.Diagnostics;
using QuickMemoryServer.Worker.Embeddings;
using QuickMemoryServer.Worker.Memory;
using QuickMemoryServer.Worker.Models;
using QuickMemoryServer.Worker.Persistence;
using QuickMemoryServer.Worker.Search;
using QuickMemoryServer.Worker.Services;
using QuickMemoryServer.Worker.Validation;
using Prometheus;
using Serilog;
using Serilog.Events;
using Tomlyn.Extensions.Configuration;


var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddIniFile("QuickMemoryServer.ini", optional: true, reloadOnChange: true)
    .AddTomlFile("QuickMemoryServer.toml", optional: true, reloadOnChange: true);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = builder.Configuration["global:serviceName"]
        ?? builder.Configuration["Global:ServiceName"]
        ?? "QuickMemoryServer";
});

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    var baseDir = AppContext.BaseDirectory;
    var logDir = Path.Combine(baseDir, "logs");
    var auditDbPath = Path.Combine(logDir, "quick-memory-audit.db");
    Directory.CreateDirectory(logDir);

    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(logDir, "quick-memory-server-.log"), rollingInterval: RollingInterval.Day,
            restrictedToMinimumLevel: LogEventLevel.Information,
            retainedFileCountLimit: 7);

    loggerConfiguration
        .WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("AuditTrail"))
            .WriteTo.File(Path.Combine(logDir, "quick-memory-audit-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.SQLite(auditDbPath,
                tableName: "AuditLog",
                storeTimestampInUtc: true));

    if (OperatingSystem.IsWindows())
    {
        var serviceName = context.Configuration["global:serviceName"]
            ?? context.Configuration["Global:ServiceName"]
            ?? "QuickMemoryServer";

        loggerConfiguration.WriteTo.EventLog(
            source: serviceName,
            manageEventSource: false,
            restrictedToMinimumLevel: LogEventLevel.Information);
    }
});

var httpUrl = builder.Configuration["global:httpUrl"]
              ?? builder.Configuration["Global:HttpUrl"]
              ?? "http://localhost:5080";

builder.WebHost.UseUrls(httpUrl);

var serviceName = builder.Configuration["global:serviceName"]
    ?? builder.Configuration["Global:ServiceName"]
    ?? "QuickMemoryServer";

var assembly = Assembly.GetEntryAssembly();
var assemblyVersion = assembly?.GetName().Version?.ToString() ?? "0.0.0.0";
var infoVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assemblyVersion;

builder.Services.AddRouting();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddSingleton<IConfigureOptions<ServerOptions>, ServerOptionsConfigurator>();
builder.Services.AddSingleton<IOptionsChangeTokenSource<ServerOptions>>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new ConfigurationChangeTokenSource<ServerOptions>(Options.DefaultName, configuration);
});

builder.Services.AddOptions<ServerOptions>()
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<MemoryEntryValidator>();
builder.Services.AddSingleton<JsonlRepository>();

builder.Services.AddSingleton<IEmbeddingGenerator>(sp =>
{
    var options = sp.GetRequiredService<IOptionsMonitor<ServerOptions>>().CurrentValue;
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger<OnnxEmbeddingGenerator>();

    var modelPath = options.Global.EmbeddingModel;
    var dimension = options.Global.EmbeddingDims > 0 ? options.Global.EmbeddingDims : 384;
    if (!string.IsNullOrWhiteSpace(modelPath))
    {
        var resolved = Path.IsPathRooted(modelPath)
            ? modelPath
            : Path.Combine(AppContext.BaseDirectory, modelPath);

        if (File.Exists(resolved))
        {
            try
            {
                return new OnnxEmbeddingGenerator(resolved, dimension, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falling back to hash embeddings; failed to load ONNX model at {Path}", resolved);
            }
        }
    }

    return new HashEmbeddingGenerator(dimension);
});

builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<SearchEngine>();
builder.Services.AddSingleton<MemoryStoreFactory>();
builder.Services.AddSingleton<MemoryRouter>();
builder.Services.AddSingleton<ApiKeyAuthorizer>();
builder.Services.AddHostedService<MemoryService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackupService>());

builder.Services.AddSingleton<ObservabilityMetrics>();
builder.Services.AddSingleton<IMemoryStoreProvider>(sp => sp.GetRequiredService<MemoryStoreFactory>());
builder.Services.AddSingleton<HealthReporter>();
builder.Services.AddSingleton<AdminConfigService>();
builder.Services.AddSingleton<DocumentService>();
builder.Services.AddSingleton<SchemaService>();
builder.Services.AddControllersWithViews();
builder.Services.AddMcpServer()
    .WithResourcesFromAssembly(typeof(HelpResources).Assembly)
    .WithListResourceTemplatesHandler((_, _) =>
    {
        return ValueTask.FromResult(new ListResourceTemplatesResult
        {
            ResourceTemplates = Array.Empty<ResourceTemplate>()
        });
    })
    .WithHttpTransport(options =>
    {
        options.ConfigureSessionOptions = (httpContext, serverOptions, cancellationToken) =>
        {
            var serviceNameSetting = builder.Configuration["global:serviceName"]
                ?? builder.Configuration["Global:ServiceName"]
                ?? "QuickMemoryServer";

            var version = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "1.0.0";
            serverOptions.ServerInfo = new Implementation
            {
                Name = serviceNameSetting,
                Title = serviceNameSetting,
                Version = version,
                WebsiteUrl = httpUrl
            };

            var schemaService = httpContext.RequestServices.GetRequiredService<SchemaService>();
            var requestHost = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}";
            var schemaUrl = $"{requestHost}/docs/schema";
            var agentHelp = $"{requestHost}/admin/help/agent";
            var userHelp = $"{requestHost}/admin/help/end-user";
            var entryFieldGuide = $"{requestHost}/admin/help/agent#memoryentry-field-reference";
            var codexConfig = $"Codex: `[mcp_servers.quick-memory] url=\"{requestHost}/mcp\" experimental_use_rmcp_client=true bearer_token_env_var=\"QMS_API_KEY\"`.";
            var quickStart = "Next steps: listProjects → pick endpoint → listRecentEntries(endpoint) for a browse; use searchEntries(endpoint, text, includeShared) for focus.";
            serverOptions.ServerInstructions = $"Schema: {schemaUrl}. Agent guide: {agentHelp}. Entry fields: {entryFieldGuide} (or resource://quick-memory/entry-fields). End-user help: {userHelp}. {codexConfig} {quickStart} Schema ETag {schemaService.ETag}.";
            return Task.CompletedTask;
        };
    })
    .AddCallToolFilter(next =>
    {
        return async (context, cancellationToken) =>
        {
            static bool IsGlobalTool(string? toolName) =>
                toolName is not null &&
                (toolName.Equals("listProjects", StringComparison.OrdinalIgnoreCase)
                 || toolName.Equals("health", StringComparison.OrdinalIgnoreCase));

            var httpContext = context.Services.GetService<IHttpContextAccessor>()?.HttpContext;
            var apiKey = ExtractApiKey(httpContext);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return CreateCallToolError("missing-api-key");
            }

            var toolName = context.Params.Name;
            var authorizer = context.Services.GetRequiredService<ApiKeyAuthorizer>();

            if (!IsGlobalTool(toolName) && !TryGetEndpoint(context.Params.Arguments, out var endpoint))
            {
                return CreateCallToolError("missing-endpoint");
            }

            if (IsGlobalTool(toolName))
            {
                var optionsMonitor = context.Services.GetRequiredService<IOptionsMonitor<ServerOptions>>();
                string? authorizedEndpoint = null;
                PermissionTier tier = default;
                string? user = null;

                foreach (var endpointKey in optionsMonitor.CurrentValue.Endpoints.Keys)
                {
                    if (authorizer.TryAuthorize(apiKey, endpointKey, out user, out tier))
                    {
                        authorizedEndpoint = endpointKey;
                        break;
                    }
                }

                if (authorizedEndpoint is null &&
                    authorizer.TryAuthorize(apiKey, "shared", out user, out tier))
                {
                    authorizedEndpoint = "shared";
                }

                if (authorizedEndpoint is null)
                {
                    return CreateCallToolError("unauthorized");
                }

                context.Items[McpAuthorizationContext.ApiKeyItem] = apiKey;
                context.Items[McpAuthorizationContext.EndpointItem] = authorizedEndpoint;
                context.Items[McpAuthorizationContext.TierItem] = tier;
                context.Items[McpAuthorizationContext.UserItem] = user;

                return await next(context, cancellationToken);
            }

            if (!TryGetEndpoint(context.Params.Arguments, out var projectEndpoint))
            {
                return CreateCallToolError("missing-endpoint");
            }

            if (!authorizer.TryAuthorize(apiKey, projectEndpoint, out var userScoped, out var tierScoped))
            {
                return CreateCallToolError("unauthorized");
            }

            context.Items[McpAuthorizationContext.ApiKeyItem] = apiKey;
            context.Items[McpAuthorizationContext.EndpointItem] = projectEndpoint;
            context.Items[McpAuthorizationContext.TierItem] = tierScoped;
            context.Items[McpAuthorizationContext.UserItem] = userScoped;

            return await next(context, cancellationToken);
        };
    })
    .WithToolsFromAssembly(typeof(MemoryMcpTools).Assembly);

var app = builder.Build();
var observabilityMetrics = app.Services.GetRequiredService<ObservabilityMetrics>();

// Startup banner with version info reaches configured Serilog sinks.
Log.Information("Starting {ServiceName} version {Version} (informational {InfoVersion}) listening at {HttpUrl} (BaseDir: {BaseDir})",
    serviceName, assemblyVersion, infoVersion, httpUrl, AppContext.BaseDirectory);

EnsureBuiltInProjectsAsync(app.Services).GetAwaiter().GetResult();

app.UseSerilogRequestLogging();
app.UseSession();
app.Use(async (context, next) =>
{
    var sessionKey = context.Session.GetString("ApiKey");
    if (!string.IsNullOrWhiteSpace(sessionKey))
    {
        if (!context.Request.Headers.ContainsKey("X-Api-Key"))
        {
            context.Request.Headers["X-Api-Key"] = sessionKey;
        }

        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            context.Request.Headers["Authorization"] = $"Bearer {sessionKey}";
        }
    }

    await next();
});
app.Use(async (context, next) =>
    {
        var stopwatch = Stopwatch.StartNew();
        await next();

        if (context.Request.Path.StartsWithSegments("/mcp", out var remaining))
        {
            var (endpoint, command) = ResolveMcpLabels(remaining);
            observabilityMetrics.TrackMcpRequest(endpoint, command, context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
            ObservabilityEventSource.Log.RecordMcpRequest(stopwatch.Elapsed.TotalMilliseconds);
        }
    });
app.UseStaticFiles();
app.UseRouting();
app.UseHttpMetrics();

app.MapGet("/health", (HealthReporter healthReporter) =>
{
    var report = healthReporter.GetReport();
    return Results.Ok(report);
});

app.MapPost("/admin/login", async (HttpContext context) =>
{
    var payload = await context.Request.ReadFromJsonAsync<LoginRequest>();
    if (payload is null || string.IsNullOrWhiteSpace(payload.ApiKey))
    {
        return Results.BadRequest(new { error = "missing-api-key" });
    }

    context.Session.SetString("ApiKey", payload.ApiKey.Trim());
    return Results.Ok(new { stored = true });
});

app.MapGet("/admin/session", (HttpContext context) =>
{
    var apiKey = context.Session.GetString("ApiKey");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.NoContent();
    }

    return Results.Ok(new { apiKey });
});

app.MapPost("/admin/logout", (HttpContext context) =>
{
    context.Session.Clear();
    return Results.Ok();
});

app.MapGet("/admin/config/raw", async (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var content = await adminService.ReadRawAsync();
    return Results.Content(content, "text/plain");
});

app.MapPost("/admin/config/raw/validate", async (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<RawConfigRequest>(cancellationToken);
    var content = request?.Content ?? string.Empty;
    var result = await adminService.ValidateRawAsync(content, cancellationToken);
    if (!result.IsValid)
    {
        return Results.BadRequest(new { errors = result.Errors });
    }

    return Results.Ok(new { valid = true });
});

app.MapPost("/admin/config/raw/apply", async (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<RawConfigRequest>(cancellationToken);
    var content = request?.Content ?? string.Empty;
    var result = await adminService.SaveRawAsync(content, cancellationToken);
    if (!result.IsValid)
    {
        return Results.BadRequest(new { errors = result.Errors });
    }

    return Results.Ok(new { saved = true });
});

app.MapGet("/admin/logs", async (HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var baseDir = AppContext.BaseDirectory;
    var logDir = Path.Combine(baseDir, "logs");
    if (!Directory.Exists(logDir))
    {
        return Results.NotFound(new { error = "log-directory-missing" });
    }

    var files = Directory.GetFiles(logDir, "quick-memory-server-*.log")
        .OrderByDescending(File.GetCreationTimeUtc)
        .Take(5)
        .ToArray();

    if (files.Length == 0)
    {
        return Results.NotFound(new { error = "no-logs" });
    }

    context.Response.ContentType = "application/zip";
    context.Response.Headers["Content-Disposition"] = "attachment; filename=quick-memory-logs.zip";

    using var archive = new System.IO.Compression.ZipArchive(context.Response.Body, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true);
    foreach (var file in files)
    {
        var entry = archive.CreateEntry(Path.GetFileName(file));
        await using var entryStream = entry.Open();
        await using var fileStream = File.OpenRead(file);
        await fileStream.CopyToAsync(entryStream, context.RequestAborted);
    }

    return Results.Empty;
});

app.MapGet("/docs/schema", (HttpContext context, SchemaService schemaService) =>
{
    const string cacheControl = "public,max-age=30";
    context.Response.Headers["Cache-Control"] = cacheControl;

    var etagValue = $"\"{schemaService.ETag}\"";
    var requestEtag = context.Request.Headers["If-None-Match"].FirstOrDefault();
    if (!string.IsNullOrEmpty(requestEtag) && string.Equals(requestEtag.Trim(), etagValue, StringComparison.Ordinal))
    {
        context.Response.Headers["ETag"] = etagValue;
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }

    context.Response.Headers["ETag"] = etagValue;
    return Results.Content(schemaService.Json, "application/json");
});

app.MapPost("/mcp/{endpoint}/ping", (string endpoint, HttpContext context, ApiKeyAuthorizer authorizer, MemoryRouter router) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;

    if (!authorizer.TryAuthorize(apiKey, endpoint, out var user, out var tier))
    {
        return Results.Unauthorized();
    }

    try
    {
        var store = router.ResolveStore(endpoint);
        return Results.Ok(new
        {
            endpoint = store.Project,
            user,
            tier = tier.ToString(),
            message = "pong"
        });
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "endpoint-not-found" });
    }
});

app.MapGet("/admin/endpoints", (IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    var options = optionsMonitor.CurrentValue;
    var endpoints = options.Endpoints.Select(kvp => new
    {
        key = kvp.Key,
        kvp.Value.Name,
        kvp.Value.Slug,
        kvp.Value.Description,
        kvp.Value.StoragePath,
        kvp.Value.InheritShared,
        kvp.Value.IncludeInSearchByDefault
    });

    return Results.Ok(new { endpoints });
});

app.MapPost("/mcp/{endpoint}/searchEntries", async (
    string endpoint,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    MemoryRouter router,
    SearchEngine searchEngine,
    IOptionsMonitor<ServerOptions> optionsMonitor,
    CancellationToken cancellationToken) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out _))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<SearchRequest>(cancellationToken) ?? new SearchRequest();
    var maxResults = request.MaxResults is > 0 and <= 200 ? request.MaxResults.Value : 20;
    var includeSharedDefault = optionsMonitor.CurrentValue.Endpoints.TryGetValue(endpoint, out var endpointOptions)
        ? endpointOptions.IncludeInSearchByDefault
        : true;
    var includeShared = request.IncludeShared ?? includeSharedDefault;

    if (router.ResolveStore(endpoint) is not MemoryStore primaryStore)
    {
        return Results.Problem($"Endpoint '{endpoint}' is not available.", statusCode: StatusCodes.Status404NotFound);
    }

    var stores = new List<MemoryStore> { primaryStore };
    if (includeShared && !string.Equals(endpoint, "shared", StringComparison.OrdinalIgnoreCase))
    {
        if (router.ResolveStore("shared") is MemoryStore sharedStore)
        {
            stores.Add(sharedStore);
        }
    }

    var entryLookup = McpHelpers.BuildEntryLookup(stores);

    // If no query text/tags/embedding provided, return latest entries as a browse view
    var noQuery = string.IsNullOrWhiteSpace(request.Text) && (request.Tags is null || request.Tags.Length == 0) && request.Embedding is null;
    if (noQuery)
    {
        var browse = entryLookup.Values
            .OrderByDescending(e => e.Timestamps?.UpdatedUtc ?? e.Timestamps?.CreatedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(e => e.Id)
            .Take(maxResults)
            .Select(e => new
            {
                score = 1.0,
                snippet = e.Title ?? e.Id,
                entry = e
            })
            .ToArray();

        return Results.Ok(new { results = browse });
    }

    var query = new SearchQuery
    {
        Project = endpoint,
        Text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text,
        Embedding = request.Embedding,
        MaxResults = maxResults,
        IncludeShared = includeShared,
        Tags = request.Tags
    };

    var results = searchEngine
        .Search(query, id => entryLookup.TryGetValue(id, out var entry) ? entry : null)
        .ToArray();

    if (request.Tags is { Length: > 0 })
    {
        var tagSet = new HashSet<string>(request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
        results = results
            .Where(r => entryLookup.TryGetValue(r.EntryId, out var entry) && entry.Tags.Any(tag => tagSet.Contains(tag)))
            .ToArray();
    }

    var response = results.Select(r => new
    {
        score = r.Score,
        snippet = r.Snippet,
        entry = entryLookup[r.EntryId]
    }).ToArray();

    return Results.Ok(new { results = response });
});

app.MapPost("/mcp/{endpoint}/relatedEntries", async (
    string endpoint,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    MemoryRouter router,
    CancellationToken cancellationToken) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out _))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<RelatedRequest>(cancellationToken);
    if (request is null || string.IsNullOrWhiteSpace(request.Id))
    {
        return Results.BadRequest(new { error = "missing-id" });
    }

    var maxHops = request.MaxHops.GetValueOrDefault(2);
    if (maxHops < 1)
    {
        maxHops = 1;
    }

    if (router.ResolveStore(endpoint) is not MemoryStore primaryStore)
    {
        return Results.Problem($"Endpoint '{endpoint}' is not available.", statusCode: StatusCodes.Status404NotFound);
    }

    var includeShared = request.IncludeShared ?? true;
    var stores = new List<MemoryStore> { primaryStore };
    if (includeShared && !string.Equals(endpoint, "shared", StringComparison.OrdinalIgnoreCase))
    {
        if (router.ResolveStore("shared") is MemoryStore sharedStore)
        {
            stores.Add(sharedStore);
        }
    }

    var relatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var store in stores)
    {
        foreach (var id in store.Related(request.Id, maxHops))
        {
            relatedIds.Add(id);
        }
    }

    var relatedEntries = relatedIds
        .Select(id => McpHelpers.ResolveEntry(id, stores, router))
        .Where(entry => entry is not null)
        .Select(entry => new
        {
            id = entry!.Id,
            project = entry.Project,
            title = entry.Title,
            kind = entry.Kind
        })
        .ToArray();

    var edges = relatedIds.Select(id => new { source = request.Id, target = id }).ToArray();

    return Results.Ok(new
    {
        nodes = relatedEntries,
        edges
    });
});

app.MapPost("/mcp/{endpoint}/backup", async (
    string endpoint,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    BackupService backupService,
    CancellationToken cancellationToken) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out var tier) || tier != PermissionTier.Admin)
    {
        return Results.Unauthorized();
    }

    var payload = await context.Request.ReadFromJsonAsync<BackupRequestPayload>(cancellationToken) ?? new BackupRequestPayload();
    var mode = Enum.TryParse<BackupMode>(payload.Mode ?? "Differential", true, out var parsed) ? parsed : BackupMode.Differential;
    await backupService.RequestBackupAsync(endpoint, mode, cancellationToken);
    return Results.Accepted($"/mcp/{endpoint}/backup", new { queued = true, mode = mode.ToString() });
});

app.MapGet("/mcp/{endpoint}/entries/{id}", (
    string endpoint,
    string id,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    MemoryRouter router) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out _))
    {
        return Results.Unauthorized();
    }

    if (router.ResolveStore(endpoint) is not MemoryStore store)
    {
        return Results.NotFound(new { error = "endpoint-not-found" });
    }

    var entry = store.FindEntry(id);
    if (entry is null)
    {
        return Results.NotFound(new { error = "not-found" });
    }

    return Results.Ok(entry);
});

app.MapGet("/mcp/{endpoint}/entries", (
    string endpoint,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    MemoryRouter router) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out _))
    {
        return Results.Unauthorized();
    }

    if (router.ResolveStore(endpoint) is not MemoryStore store)
    {
        return Results.NotFound(new { error = "endpoint-not-found" });
    }

    return Results.Ok(store.Snapshot());
});

app.MapPost("/mcp/{endpoint}/entries", async (
    string endpoint,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    MemoryRouter router,
    CancellationToken cancellationToken) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out var tier))
    {
        return Results.Unauthorized();
    }

    if (router.ResolveStore(endpoint) is not MemoryStore store)
    {
        return Results.NotFound(new { error = "endpoint-not-found" });
    }

    var entry = await context.Request.ReadFromJsonAsync<MemoryEntry>(cancellationToken);
    if (entry is null)
    {
        return Results.BadRequest(new { error = "invalid-entry" });
    }

    if (!MemoryMcpTools.TryPrepareEntry(endpoint, entry, out entry, out var prepError))
    {
        return Results.BadRequest(new { error = prepError ?? "invalid-entry" });
    }

    if (entry.IsPermanent && tier != PermissionTier.Admin)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    await store.UpsertAsync(entry, cancellationToken);
    return Results.Ok(new { updated = true, id = entry.Id });
});

app.MapPatch("/mcp/{endpoint}/entries/{id}", async (
    string endpoint,
    string id,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    MemoryRouter router,
    CancellationToken cancellationToken) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out var tier))
    {
        return Results.Unauthorized();
    }

    if (router.ResolveStore(endpoint) is not MemoryStore store)
    {
        return Results.NotFound(new { error = "endpoint-not-found" });
    }

    var existing = store.FindEntry(id);
    if (existing is null)
    {
        return Results.NotFound(new { error = "not-found" });
    }

    var patch = await context.Request.ReadFromJsonAsync<EntryPatchRequest>(cancellationToken);
    if (patch is null)
    {
        return Results.BadRequest(new { error = "invalid-patch" });
    }

    var updated = existing with
    {
        Title = patch.Title ?? existing.Title,
        Tags = patch.Tags ?? existing.Tags,
        CurationTier = patch.CurationTier ?? existing.CurationTier,
        IsPermanent = patch.IsPermanent ?? existing.IsPermanent,
        Pinned = patch.Pinned ?? existing.Pinned,
        Confidence = patch.Confidence ?? existing.Confidence,
        Body = patch.Body ?? existing.Body,
        EpicSlug = patch.EpicSlug ?? existing.EpicSlug,
        EpicCase = patch.EpicCase ?? existing.EpicCase,
        Relations = patch.Relations is null ? existing.Relations : patch.Relations.Deserialize<MemoryRelation[]>(new System.Text.Json.JsonSerializerOptions()),
        Source = patch.Source is null ? existing.Source : patch.Source.Deserialize<MemorySource>(new System.Text.Json.JsonSerializerOptions())
    };

    if (updated.IsPermanent && tier != PermissionTier.Admin)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    await store.UpsertAsync(updated, cancellationToken);
    return Results.Ok(new { updated = true });
});

app.MapDelete("/mcp/{endpoint}/entries/{id}", async (
    string endpoint,
    string id,
    HttpContext context,
    ApiKeyAuthorizer authorizer,
    MemoryRouter router,
    CancellationToken cancellationToken) =>
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (!authorizer.TryAuthorize(apiKey, endpoint, out _, out var tier))
    {
        return Results.Unauthorized();
    }

    if (router.ResolveStore(endpoint) is not MemoryStore store)
    {
        return Results.NotFound(new { error = "endpoint-not-found" });
    }

    var force = string.Equals(context.Request.Query["force"], "true", StringComparison.OrdinalIgnoreCase) || tier == PermissionTier.Admin;

    try
    {
        var deleted = await store.DeleteAsync(id, force, cancellationToken);
        if (!deleted)
        {
            return Results.NotFound(new { error = "not-found" });
        }

        return Results.Ok(new { deleted = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
    }
});

app.MapGet("/admin/users", (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var payload = adminService.ListUsers().Select(kvp => new
    {
        username = kvp.Key,
        apiKey = kvp.Value.ApiKey,
        defaultTier = kvp.Value.DefaultTier.ToString()
    });

    return Results.Ok(payload);
});

app.MapPost("/admin/users", async (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<AdminUserRequest>(cancellationToken);
    if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.ApiKey))
    {
        return Results.BadRequest(new { error = "invalid-request" });
    }

    if (!Enum.TryParse<PermissionTier>(request.DefaultTier, true, out var tier))
    {
        return Results.BadRequest(new { error = "invalid-tier" });
    }

    await adminService.AddOrUpdateUserAsync(request.Username, new UserOptions
    {
        ApiKey = request.ApiKey,
        DefaultTier = tier
    }, cancellationToken);

    return Results.Ok(new { saved = true });
});

app.MapDelete("/admin/users/{username}", async (string username, HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new { error = "missing-username" });
    }

    await adminService.RemoveUserAsync(username, cancellationToken);
    return Results.Ok(new { deleted = true });
});

app.MapGet("/admin/endpoints/manage", (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var payload = adminService.ListEndpoints().Select(kvp => new
    {
        key = kvp.Key,
        kvp.Value.Name,
        kvp.Value.Slug,
        kvp.Value.Description,
        kvp.Value.StoragePath,
        kvp.Value.IncludeInSearchByDefault,
        kvp.Value.InheritShared
    });

    return Results.Ok(payload);
});


static bool IsSafeKey(string? value)
{
    return !string.IsNullOrWhiteSpace(value) && System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9_-]+$");
}

static (string? Error, string? Path) ValidateAndPrepareStorage(string? path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return ("storage-path-missing", null);
    }

    try
    {
        var full = Path.GetFullPath(path);
        var dir = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return ("storage-path-invalid", null);
        }

        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var testFile = Path.Combine(dir, ".qms_write_test" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(testFile, "test");
        var content = File.ReadAllText(testFile);
        File.Delete(testFile);
        if (content != "test")
        {
            return ("storage-path-unreadable", null);
        }

        return (null, full);
    }
    catch (Exception ex)
    {
        return ($"storage-path-invalid: {ex.Message}", null);
    }
}
app.MapPost("/admin/endpoints/manage", async (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<AdminEndpointRequest>(cancellationToken);
    if (request is null || string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Name) || !IsSafeKey(request.Key) || (!string.IsNullOrWhiteSpace(request.Slug) && !IsSafeKey(request.Slug)))
    {
        return Results.BadRequest(new { error = "invalid-request" });
    }

    var slug = string.IsNullOrWhiteSpace(request.Slug) ? request.Key : request.Slug;
    var storagePath = string.IsNullOrWhiteSpace(request.StoragePath)
        ? Path.Combine(AppContext.BaseDirectory, "MemoryStores", request.Key)
        : request.StoragePath;

    var endpointOptions = new EndpointOptions
    {
        Name = request.Name,
        Slug = slug,
        Description = request.Description,
        StoragePath = storagePath,
        IncludeInSearchByDefault = request.IncludeInSearchByDefault,
        InheritShared = request.InheritShared
    };

    var storageValidation = ValidateAndPrepareStorage(endpointOptions.StoragePath);
    if (storageValidation.Error is not null)
    {
        return Results.BadRequest(new { error = storageValidation.Error });
    }

    endpointOptions.StoragePath = storageValidation.Path!;

    await adminService.AddOrUpdateEndpointAsync(request.Key, endpointOptions, cancellationToken);
    return Results.Ok(new { saved = true });
});

app.MapDelete("/admin/endpoints/manage/{endpointKey}", async (string endpointKey, HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(endpointKey))
    {
        return Results.BadRequest(new { error = "missing-endpoint" });
    }

    await adminService.RemoveEndpointAsync(endpointKey, cancellationToken);
    return Results.Ok(new { deleted = true });
});

app.MapGet("/admin/help/end-user", (HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor, DocumentService documentService) =>
{
    if (!TryAuthorizeAny(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var html = documentService.RenderMarkdown("end-user-help.md");
    return Results.Content(html, "text/html");
});

app.MapGet("/admin/help/agent", (HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor, DocumentService documentService) =>
{
    if (!TryAuthorizeAny(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var html = documentService.RenderMarkdown("agent-usage.md");
    return Results.Content(html, "text/html");
});

app.MapGet("/admin/help/admin-ui", (HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor, DocumentService documentService) =>
{
    if (!TryAuthorizeAny(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var html = documentService.RenderMarkdown("admin-ui-help.md");
    return Results.Content(html, "text/html");
});

app.MapGet("/admin/help/codex-workspace", (HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor, DocumentService documentService) =>
{
    if (!TryAuthorizeAny(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var html = documentService.RenderMarkdown("codex-workspace-guide.md");
    return Results.Content(html, "text/html");
});

app.MapGet("/admin/permissions", (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var payload = adminService.ListPermissions()
        .ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToDictionary(p => p.Key, p => p.Value.ToString()));

    return Results.Ok(payload);
});

app.MapPost("/admin/permissions/{endpointKey}", async (string endpointKey, HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<PermissionUpdateRequest>(cancellationToken);
    if (request is null)
    {
        return Results.BadRequest(new { error = "invalid-request" });
    }

    var payload = request.Assignments ?? new Dictionary<string, string>();
    if (!TryParseTierAssignments(payload, out var assignments, out var error))
    {
        return Results.BadRequest(new { error });
    }

    try
    {
        await adminService.SetPermissionsAsync(endpointKey, assignments, cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    var auditUser = ResolveAdminUser(context, authorizer, optionsMonitor);
    LogProjectPermissionChange(
        "project-permissions.set",
        auditUser,
        new[] { endpointKey },
        assignments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()));

    return Results.Ok(new { saved = true });
});

app.MapPost("/admin/import/{endpoint}", async (string endpoint, HttpContext context, ApiKeyAuthorizer authorizer, MemoryRouter router, IOptionsMonitor<ServerOptions> optionsMonitor, CancellationToken cancellationToken) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var mode = context.Request.Query["mode"].FirstOrDefault() ?? "upsert";
    var dryRunRaw = context.Request.Query["dryRun"].FirstOrDefault();
    var dryRun = string.Equals(dryRunRaw, "true", StringComparison.OrdinalIgnoreCase);

    if (!string.Equals(mode, "upsert", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(mode, "append", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "unsupported-mode", details = "Supported modes are 'upsert' and 'append'." });
    }

    IMemoryStore store;
    try
    {
        store = router.ResolveStore(endpoint);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound(new { error = "endpoint-not-found" });
    }

    string rawContent;
    using (var reader = new StreamReader(context.Request.Body))
    {
        rawContent = await reader.ReadToEndAsync(cancellationToken);
    }

    if (string.IsNullOrWhiteSpace(rawContent))
    {
        return Results.BadRequest(new
        {
            error = "empty-content",
            details = "Provide either a JSON array of MemoryEntry objects or JSONL with one MemoryEntry object per line."
        });
    }

    var processed = 0;
    var imported = 0;
    var skipped = 0;
    var errors = new List<object>();
    var entries = new List<(int Index, MemoryEntry Entry)>();

    var importJsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    var trimmed = rawContent.TrimStart();
    try
    {
        if (trimmed.StartsWith("["))
        {
            var doc = JsonDocument.Parse(rawContent);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Results.BadRequest(new { error = "invalid-format", details = "Expected a JSON array for import content." });
            }

            var index = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                processed++;

                if (element.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new
                    {
                        index,
                        message = "invalid-entry-json: expected JSON object",
                        hint = "Each array element must be a JSON object matching the MemoryEntry shape, not a string or number."
                    });
                    index++;
                    continue;
                }

                try
                {
                    var entry = element.Deserialize<MemoryEntry>(importJsonOptions);
                    if (entry is null)
                    {
                        errors.Add(new { index, message = "null-entry", hint = "The JSON value was null; ensure each element is a populated MemoryEntry object." });
                    }
                    else if (IsEffectivelyEmptyEntry(entry))
                    {
                        errors.Add(new
                        {
                            index,
                            message = "empty-entry",
                            hint = "Entries must include at least one of title, body, or tags to be imported."
                        });
                    }
                    else
                    {
                        entries.Add((index, entry));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new { index, message = $"parse-error: {ex.Message}" });
                }

                index++;
            }
        }
        else
        {
            var lines = rawContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                processed++;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add(new
                        {
                            index = i,
                            message = "invalid-entry-json: expected JSON object",
                            hint = "Each non-empty line must be a JSON object representing a MemoryEntry."
                        });
                        continue;
                    }

                    var entry = doc.RootElement.Deserialize<MemoryEntry>(importJsonOptions);
                    if (entry is null)
                    {
                        errors.Add(new { index = i, message = "null-entry", hint = "The JSON value was null; ensure each line is a populated MemoryEntry object." });
                    }
                    else if (IsEffectivelyEmptyEntry(entry))
                    {
                        errors.Add(new
                        {
                            index = i,
                            message = "empty-entry",
                            hint = "Entries must include at least one of title, body, or tags to be imported."
                        });
                    }
                    else
                    {
                        entries.Add((i, entry));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new { index = i, message = $"parse-error: {ex.Message}" });
                }
            }
        }
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = "invalid-json", details = ex.Message });
    }

    var existingIds = new HashSet<string>(store.Snapshot().Select(e => e.Id), StringComparer.OrdinalIgnoreCase);

    foreach (var (index, entry) in entries)
    {
        if (!MemoryMcpTools.TryPrepareEntry(endpoint, entry, out var prepared, out var prepareError))
        {
            errors.Add(new { index, id = entry.Id, message = prepareError ?? "invalid-entry" });
            continue;
        }

        if (string.Equals(mode, "append", StringComparison.OrdinalIgnoreCase) &&
            existingIds.Contains(prepared.Id))
        {
            skipped++;
            continue;
        }

        if (dryRun)
        {
            imported++;
            continue;
        }

        try
        {
            if (store is MemoryStore concrete)
            {
                await concrete.UpsertAsync(prepared, cancellationToken);
            }
            else
            {
                await store.UpsertAsync(prepared, cancellationToken);
            }

            imported++;
            existingIds.Add(prepared.Id);
        }
        catch (Exception ex)
        {
            errors.Add(new { index, id = prepared.Id, message = $"upsert-error: {ex.Message}" });
        }
    }

    return Results.Ok(new
    {
        endpoint,
        mode,
        dryRun,
        processed,
        imported,
        skipped,
        errorCount = errors.Count,
        errors
    });
});

app.MapGet("/admin/projects/{projectKey}/permissions", (string projectKey, HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(projectKey))
    {
        return Results.BadRequest(new { error = "missing-project" });
    }

    var endpoints = adminService.ListEndpoints();
    if (!endpoints.TryGetValue(projectKey, out var project))
    {
        return Results.NotFound(new { error = "project-not-found" });
    }

    var permissions = adminService.ListPermissions();
    var assignments = permissions.TryGetValue(projectKey, out var map)
        ? map.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString(), StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    return Results.Ok(new
    {
        project = new
        {
            key = projectKey,
            project.Name,
            project.Slug,
            project.Description,
            project.StoragePath,
            project.IncludeInSearchByDefault,
            project.InheritShared
        },
        assignments
    });
});

app.MapPatch("/admin/projects/{projectKey}/permissions", async (string projectKey, HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(projectKey))
    {
        return Results.BadRequest(new { error = "missing-project" });
    }

    var request = await context.Request.ReadFromJsonAsync<PermissionUpdateRequest>(cancellationToken);
    if (request is null)
    {
        return Results.BadRequest(new { error = "invalid-request" });
    }

    var payload = request.Assignments ?? new Dictionary<string, string>();
    if (!TryParseTierAssignments(payload, out var assignments, out var error))
    {
        return Results.BadRequest(new { error });
    }

    try
    {
        await adminService.SetPermissionsAsync(projectKey, assignments, cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    var auditUser = ResolveAdminUser(context, authorizer, optionsMonitor);
    LogProjectPermissionChange(
        "project-permissions.patch",
        auditUser,
        new[] { projectKey },
        assignments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()));

    return Results.Ok(new { saved = true });
});

app.MapPost("/admin/projects/permissions/bulk", async (HttpContext context, ApiKeyAuthorizer authorizer, AdminConfigService adminService, CancellationToken cancellationToken, IOptionsMonitor<ServerOptions> optionsMonitor) =>
{
    if (!TryAuthorizeAdmin(context, authorizer, optionsMonitor))
    {
        return Results.Unauthorized();
    }

    var request = await context.Request.ReadFromJsonAsync<PermissionBulkUpdateRequest>(cancellationToken);
    if (request is null || request.Projects is null || request.Projects.Length == 0)
    {
        return Results.BadRequest(new { error = "invalid-request" });
    }

    var projects = request.Projects
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (projects.Length == 0)
    {
        return Results.BadRequest(new { error = "invalid-projects" });
    }

    var overridesPayload = request.Overrides ?? new Dictionary<string, string?>();
    var overrides = new Dictionary<string, PermissionTier?>(StringComparer.OrdinalIgnoreCase);

    foreach (var (user, tierValue) in overridesPayload)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            continue;
        }

        if (string.IsNullOrWhiteSpace(tierValue) || tierValue.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            overrides[user] = null;
            continue;
        }

        if (!Enum.TryParse<PermissionTier>(tierValue, true, out var tier))
        {
            return Results.BadRequest(new { error = $"invalid-tier:{tierValue}" });
        }

        overrides[user] = tier;
    }

    if (overrides.Count == 0)
    {
        return Results.BadRequest(new { error = "no-overrides" });
    }

    try
    {
        await adminService.ApplyPermissionOverridesAsync(projects, overrides, cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    var auditUser = ResolveAdminUser(context, authorizer, optionsMonitor);
    var overrideSnapshot = overrides.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "default", StringComparer.OrdinalIgnoreCase);
    LogProjectPermissionChange("project-permissions.bulk", auditUser, projects, overrideSnapshot);

    return Results.Ok(new { saved = true });
});

app.MapMcp("/mcp");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Admin}/{action=Index}/{id?}");
app.MapMetrics();
app.Run();

static async Task EnsureBuiltInProjectsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var provider = scope.ServiceProvider;
    var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<ServerOptions>>();
    var adminConfig = provider.GetRequiredService<AdminConfigService>();

    var options = optionsMonitor.CurrentValue;

    if (!options.Endpoints.ContainsKey("prompts-repository"))
    {
        var endpoint = new EndpointOptions
        {
            Slug = "prompts-repository",
            Name = "Prompts Repository",
            Description = "Curated prompt templates for agents",
            StoragePath = "MemoryStores/prompts-repository",
            IncludeInSearchByDefault = false,
            InheritShared = false
        };

        await adminConfig.AddOrUpdateEndpointAsync("prompts-repository", endpoint);
        Log.Information("Created built-in endpoint '{Endpoint}' for curated prompts at {Path}", "prompts-repository", endpoint.StoragePath);
    }
}

static bool TryParseTierAssignments(IDictionary<string, string> source, out Dictionary<string, PermissionTier> assignments, out string? error)
{
    assignments = new Dictionary<string, PermissionTier>(StringComparer.OrdinalIgnoreCase);

    foreach (var (user, tierValue) in source)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            error = "invalid-user";
            return false;
        }

        if (!Enum.TryParse<PermissionTier>(tierValue, true, out var tier))
        {
            error = $"invalid-tier:{tierValue}";
            return false;
        }

        assignments[user] = tier;
    }

    error = null;
    return true;
}

static string? ResolveAdminUser(HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor)
{
    var apiKey = ExtractApiKey(context);
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    foreach (var endpointKey in optionsMonitor.CurrentValue.Endpoints.Keys)
    {
        if (authorizer.TryAuthorize(apiKey, endpointKey, out var user, out var tier) && tier == PermissionTier.Admin)
        {
            return user;
        }
    }

    return authorizer.TryAuthorize(apiKey, "shared", out var sharedUser, out var sharedTier) && sharedTier == PermissionTier.Admin
        ? sharedUser
        : null;
}

static void LogProjectPermissionChange(string action, string? user, IEnumerable<string> projects, object payload)
{
    var projectArray = projects as string[] ?? projects.ToArray();
    Log.ForContext("AuditTrail", true)
        .ForContext("AuditAction", action)
        .ForContext("AuditUser", user ?? "unknown")
        .ForContext("AuditProjects", projectArray)
        .Information("{Action} by {User} on {Projects}: {@Payload}", action, user ?? "unknown", projectArray, payload);
}

static bool TryAuthorizeAdmin(HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor)
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return false;
    }

    foreach (var endpointKey in optionsMonitor.CurrentValue.Endpoints.Keys)
    {
        if (authorizer.TryAuthorize(apiKey, endpointKey, out _, out var tier) && tier == PermissionTier.Admin)
        {
            return true;
        }
    }

    return authorizer.TryAuthorize(apiKey, "shared", out _, out var sharedTier) && sharedTier == PermissionTier.Admin;
}

static bool TryAuthorizeAny(HttpContext context, ApiKeyAuthorizer authorizer, IOptionsMonitor<ServerOptions> optionsMonitor)
{
    var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return false;
    }

    foreach (var endpointKey in optionsMonitor.CurrentValue.Endpoints.Keys)
    {
        if (authorizer.TryAuthorize(apiKey, endpointKey, out _, out _))
        {
            return true;
        }
    }

    return authorizer.TryAuthorize(apiKey, "shared", out _, out _);
}

static bool IsEffectivelyEmptyEntry(MemoryEntry entry)
{
    var hasTitle = !string.IsNullOrWhiteSpace(entry.Title);
    var hasBody = entry.Body is not null;
    var hasTags = entry.Tags is { Count: > 0 };

    return !hasTitle && !hasBody && !hasTags;
}

static (string endpoint, string command) ResolveMcpLabels(PathString remaining)
{
    var segments = remaining.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
    var endpoint = segments.Length > 0 ? segments[0] : "global";
    var command = segments.Length > 1 ? segments[1] : (segments.Length == 1 ? "describe" : "describe");
    return (endpoint, command);
}

static CallToolResult CreateCallToolError(string message)
{
    return new CallToolResult
    {
        IsError = true,
        Content = new List<ContentBlock>
        {
            new TextContentBlock
            {
                Text = message
            }
        }
    };
}

static string? ExtractApiKey(HttpContext? httpContext)
{
    if (httpContext is null)
    {
        return null;
    }

    var sessionKey = httpContext.Session.GetString("ApiKey");
    if (!string.IsNullOrWhiteSpace(sessionKey))
    {
        return sessionKey;
    }

    // Prefer explicit X-Api-Key header over any ambient Authorization bearer to avoid
    // cached OAuth tokens (or empty bearer headers) shadowing a valid API key.
    var apiKeyHeader = httpContext.Request.Headers["X-Api-Key"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(apiKeyHeader))
    {
        return apiKeyHeader.Trim();
    }

    if (httpContext.Request.Headers.TryGetValue("Authorization", out var authorization))
    {
        var bearer = authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(bearer) && bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = bearer["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }
    }

    if (httpContext.Request.Query.TryGetValue("apiKey", out var queryValue))
    {
        var trimmed = queryValue.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }
    }

    return null;
}

static bool TryGetEndpoint(IReadOnlyDictionary<string, JsonElement>? arguments, out string endpoint)
{
    if (arguments is not { Count: > 0 })
    {
        endpoint = string.Empty;
        return false;
    }

    foreach (var (key, value) in arguments)
    {
        if (!string.Equals(key, "endpoint", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(key, "project", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var candidate = value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                endpoint = candidate;
                return true;
            }
        }
    }

    endpoint = string.Empty;
    return false;
}
