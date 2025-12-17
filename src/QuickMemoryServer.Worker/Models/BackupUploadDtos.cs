using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace QuickMemoryServer.Worker.Models;

public sealed record BackupUploadSettingsRequest(
    bool Enabled,
    string Provider,
    string? AccountUrl,
    string? Container,
    string? Prefix,
    string AuthMode,
    string? CertificateThumbprint);

public sealed record BackupUploadSasRequest([Required] string SasToken);

public sealed record BackupUploadSettingsValidation(bool IsValid, IReadOnlyList<string> Errors);

public static class BackupUploadSettingsValidator
{
    private static readonly Regex ContainerRegex = new("^[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?$", RegexOptions.Compiled);

    public static BackupUploadSettingsValidation Validate(BackupUploadSettingsRequest request, bool hasExistingSasToken)
    {
        var errors = new List<string>();

        var provider = (request.Provider ?? string.Empty).Trim();
        if (!string.Equals(provider, "azureBlob", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("upload-provider-unsupported");
        }

        var authMode = (request.AuthMode ?? string.Empty).Trim();
        if (!string.Equals(authMode, "sas", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authMode, "certificate", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("upload-authmode-unsupported");
        }

        var accountUrl = (request.AccountUrl ?? string.Empty).Trim().TrimEnd('/');
        var container = (request.Container ?? string.Empty).Trim();
        var prefix = (request.Prefix ?? string.Empty).Trim().Trim('/');

        if (request.Enabled)
        {
            if (string.IsNullOrWhiteSpace(accountUrl) || !Uri.TryCreate(accountUrl, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add("upload-accountUrl-invalid");
            }

            if (string.IsNullOrWhiteSpace(container) || !ContainerRegex.IsMatch(container))
            {
                errors.Add("upload-container-invalid");
            }

            if (string.Equals(authMode, "sas", StringComparison.OrdinalIgnoreCase) && !hasExistingSasToken)
            {
                errors.Add("upload-sas-missing");
            }

            if (string.Equals(authMode, "certificate", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(request.CertificateThumbprint))
            {
                errors.Add("upload-certificate-thumbprint-missing");
            }
        }

        return new BackupUploadSettingsValidation(errors.Count == 0, errors);
    }

    public static (string Provider, string? AccountUrl, string? Container, string? Prefix, string AuthMode, string? CertificateThumbprint) Normalize(BackupUploadSettingsRequest request)
    {
        var provider = (request.Provider ?? "azureBlob").Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = "azureBlob";
        }

        var authMode = (request.AuthMode ?? "sas").Trim();
        if (string.IsNullOrWhiteSpace(authMode))
        {
            authMode = "sas";
        }

        var accountUrl = string.IsNullOrWhiteSpace(request.AccountUrl) ? null : request.AccountUrl.Trim().TrimEnd('/');
        var container = string.IsNullOrWhiteSpace(request.Container) ? null : request.Container.Trim();
        var prefix = string.IsNullOrWhiteSpace(request.Prefix) ? null : request.Prefix.Trim().Trim('/');
        var thumbprint = string.IsNullOrWhiteSpace(request.CertificateThumbprint) ? null : request.CertificateThumbprint.Trim();

        return (provider, accountUrl, container, prefix, authMode, thumbprint);
    }
}

