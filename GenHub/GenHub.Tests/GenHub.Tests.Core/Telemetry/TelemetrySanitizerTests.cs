using System.Collections.Generic;
using GenHub.Core.Constants;
using GenHub.Core.Utilities;
using Xunit;

namespace GenHub.Tests.Core.Telemetry;

/// <summary>
/// Unit tests for <see cref="TelemetrySanitizer"/>.
/// </summary>
public class TelemetrySanitizerTests
{
    private readonly TelemetrySanitizer _sanitizer = new();

    /// <summary>
    /// Verifies that null and empty strings are handled gracefully.
    /// </summary>
    [Fact]
    public void SanitizeString_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, _sanitizer.SanitizeString(null));
        Assert.Equal(string.Empty, _sanitizer.SanitizeString(string.Empty));
    }

    /// <summary>
    /// Verifies that Windows user paths are sanitized.
    /// </summary>
    [Fact]
    public void SanitizeString_WindowsUserPath_ReplacesWithUserDirMask()
    {
        var input = @"C:\Users\JohnDoe\AppData\Local\GenHub\game.dat";
        var result = _sanitizer.SanitizeString(input);

        Assert.Contains(TelemetryConstants.UserDirectoryMask, result);
        Assert.DoesNotContain("JohnDoe", result);
    }

    /// <summary>
    /// Verifies that Unix user paths are sanitized.
    /// </summary>
    [Fact]
    public void SanitizeString_UnixUserPath_ReplacesWithUserDirMask()
    {
        var input = "/home/alice/games/cnc/generals.exe";
        var result = _sanitizer.SanitizeString(input);

        Assert.Contains(TelemetryConstants.UserDirectoryMask, result);
        Assert.DoesNotContain("alice", result);
    }

    /// <summary>
    /// Verifies that Wine prefix paths are sanitized.
    /// </summary>
    [Fact]
    public void SanitizeString_WinePrefixPath_ReplacesWithWinePrefixMask()
    {
        var input = "/home/gamer/.wine/drive_c/Program Files/EA Games/Command and Conquer Generals";
        var result = _sanitizer.SanitizeString(input);

        Assert.Contains(TelemetryConstants.WinePrefixMask, result);
    }

    /// <summary>
    /// Verifies that IPv4 and IPv6 addresses are masked.
    /// </summary>
    [Fact]
    public void SanitizeString_IpAddresses_ReplacesWithIpMask()
    {
        var input = "Connection from 192.168.1.50 and 2001:0db8:85a3:0000:0000:8a2e:0370:7334 failed.";
        var result = _sanitizer.SanitizeString(input);

        Assert.Contains(TelemetryConstants.IpAddressMask, result);
        Assert.DoesNotContain("192.168.1.50", result);
        Assert.DoesNotContain("2001:0db8:85a3:0000:0000:8a2e:0370:7334", result);
    }

    /// <summary>
    /// Verifies that GitHub tokens and Bearer tokens are masked.
    /// </summary>
    [Fact]
    public void SanitizeString_Tokens_ReplacesWithTokenMask()
    {
        var input = "Authorization: Bearer secret_token_1234567890abcdef123456 and token ghp_123456789012345678901234567890123456";
        var result = _sanitizer.SanitizeString(input);

        Assert.Contains(TelemetryConstants.SecretTokenMask, result);
        Assert.DoesNotContain("secret_token_1234567890abcdef123456", result);
        Assert.DoesNotContain("ghp_123456789012345678901234567890123456", result);
    }

    /// <summary>
    /// Verifies that dictionary properties are recursively sanitized.
    /// </summary>
    [Fact]
    public void SanitizeProperties_NestedDictionary_SanitizesAllValues()
    {
        var props = new Dictionary<string, object?>
        {
            ["path"] = @"C:\Users\SecretUser\game.exe",
            ["ip"] = "10.0.0.1",
            ["count"] = 42,
            ["nested"] = new Dictionary<string, object?>
            {
                ["user_folder"] = "/home/secretuser/workspace",
            },
        };

        var sanitized = _sanitizer.SanitizeProperties(props);

        Assert.Equal(42, sanitized["count"]);
        Assert.Contains(TelemetryConstants.UserDirectoryMask, sanitized["path"]?.ToString());
        Assert.Contains(TelemetryConstants.IpAddressMask, sanitized["ip"]?.ToString());

        var nested = sanitized["nested"] as IReadOnlyDictionary<string, object?>;
        Assert.NotNull(nested);
        Assert.Contains(TelemetryConstants.UserDirectoryMask, nested["user_folder"]?.ToString());
        Assert.DoesNotContain("secretuser", nested["user_folder"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that stack trace sanitization strips sensitive directory information.
    /// </summary>
    [Fact]
    public void SanitizeStackTrace_StripsPersonalPaths()
    {
        var stackTrace = @"at GenHub.Program.Main() in C:\Users\Tester\source\repos\GenHub\Program.cs:line 45";
        var result = _sanitizer.SanitizeStackTrace(stackTrace);

        Assert.Contains(TelemetryConstants.UserDirectoryMask, result);
        Assert.DoesNotContain("Tester", result);
    }
}
