// See https://aka.ms/new-console-template for more information
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var appArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (appArgs.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = appArgs[0].ToLowerInvariant();
var parameters = appArgs.Skip(1).ToArray();

var baseUrl = GetOption(parameters, "--url") ?? "http://localhost:5080";
var apiKey = GetOption(parameters, "--api-key") ?? Environment.GetEnvironmentVariable("QMS_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Missing API key. Provide --api-key or set QMS_API_KEY.");
    return 1;
}

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl)
};
httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

switch (command)
{
    case "backup":
        return await RunBackupAsync(httpClient, parameters);
    default:
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 1;
}

static string? GetOption(string[] arguments, string key)
{
    for (var i = 0; i < arguments.Length; i++)
    {
        if (string.Equals(arguments[i], key, StringComparison.OrdinalIgnoreCase) && i + 1 < arguments.Length)
        {
            return arguments[i + 1];
        }
    }

    return null;
}

static void PrintUsage()
{
    Console.WriteLine("QuickMemoryServer CLI");
    Console.WriteLine("Usage:");
    Console.WriteLine("  memoryctl backup <endpoint> [--mode full|differential] [--url http://host:port] [--api-key key]");
}

static async Task<int> RunBackupAsync(HttpClient client, string[] parameters)
{
    if (parameters.Length == 0)
    {
        Console.Error.WriteLine("Missing endpoint argument.");
        PrintUsage();
        return 1;
    }

    var endpoint = parameters[0];
    var mode = GetOption(parameters, "--mode") ?? "differential";

    var payload = new { mode };
    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    var response = await client.PostAsync($"/mcp/{endpoint}/backup", content);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        Console.Error.WriteLine($"Backup request failed: {(int)response.StatusCode} {response.ReasonPhrase}\n{body}");
        return 1;
    }

    Console.WriteLine($"Backup queued for endpoint '{endpoint}' in {mode} mode.");
    return 0;
}
