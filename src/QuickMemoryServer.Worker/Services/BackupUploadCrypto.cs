using System.Security.Cryptography;
using System.Text;

namespace QuickMemoryServer.Worker.Services;

public static class BackupUploadCrypto
{
    public static string ComputeSha256Fingerprint(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = sha.ComputeHash(bytes);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string? FingerprintPrefix(string? fingerprint, int length = 16)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        return fingerprint[..Math.Min(length, fingerprint.Length)];
    }
}

