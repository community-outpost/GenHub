using GenHub.Features.Content.Services.Common;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.Common;

/// <summary>
/// Unit tests for <see cref="ManifestTagHelper"/>.
/// </summary>
public class ManifestTagHelperTests
{
    /// <summary>
    /// Verifies that SanitizeFileName returns the fallback when nothing usable remains.
    /// </summary>
    /// <param name="fileName">The filename to sanitize.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\0")]
    [InlineData("///")]
    public void SanitizeFileName_ReturnsFallbackWhenNoUsableNameRemains(string fileName)
    {
        // Act
        var sanitized = ManifestTagHelper.SanitizeFileName(fileName, "fallback.bin");

        // Assert
        Assert.Equal("fallback.bin", sanitized);
    }

    /// <summary>
    /// Verifies that SanitizeFileName preserves usable names.
    /// </summary>
    [Fact]
    public void SanitizeFileName_PreservesUsableName()
    {
        // Act
        var sanitized = ManifestTagHelper.SanitizeFileName("Generals Mod v1.0.zip", "fallback.bin");

        // Assert
        Assert.Equal("Generals Mod v1.0.zip", sanitized);
    }
}
