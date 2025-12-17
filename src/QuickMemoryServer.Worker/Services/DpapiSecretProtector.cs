using System.Security.Cryptography;
using System.Text;

namespace QuickMemoryServer.Worker.Services;

public static class DpapiSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("QuickMemoryServer.Worker.BackupUpload.SasToken.v1");

    public static string ProtectToBase64(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI protection is only available on Windows.");
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectFromBase64(string protectedBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedBase64);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI protection is only available on Windows.");
        }

        var protectedBytes = Convert.FromBase64String(protectedBase64);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }
}

