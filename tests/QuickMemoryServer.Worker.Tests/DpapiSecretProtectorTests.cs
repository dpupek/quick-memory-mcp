using QuickMemoryServer.Worker.Services;

namespace QuickMemoryServer.Worker.Tests;

public sealed class DpapiSecretProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsOnWindows()
    {
#region Arrange
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string secret = "sv=2022-11-02&ss=b&srt=sco&sp=rw&se=2099-01-01T00:00:00Z&st=2025-01-01T00:00:00Z&spr=https&sig=abc";
#endregion

#region Act
        var protectedBase64 = DpapiSecretProtector.ProtectToBase64(secret);
        var roundTripped = DpapiSecretProtector.UnprotectFromBase64(protectedBase64);
#endregion

#region Assert
        Assert.Equal(secret, roundTripped);
#endregion
    }
}

