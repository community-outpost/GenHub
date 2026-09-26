using FluentAssertions;
using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Unit tests for <see cref="GenLauncherConstants"/>.
/// </summary>
public class GenLauncherConstantsTests
{
    /// <summary>
    /// Verifies that <see cref="GenLauncherConstants.IsUsableArchiveFileName"/> accepts names with a real extension.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    [Theory]
    [InlineData("mod.zip")]
    [InlineData("Hanpatch v32.2.zip")]
    [InlineData("AntiThesisPatch-ROTR-V0.7.zip")]
    [InlineData("shockwave.big")]
    [InlineData("patch.RAR")]
    public void IsUsableArchiveFileName_WithExtension_ShouldReturnTrue(string fileName)
    {
        GenLauncherConstants.IsUsableArchiveFileName(fileName).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="GenLauncherConstants.IsUsableArchiveFileName"/> rejects
    /// extensionless endpoint segments, descriptors, and blank names.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("embed")]
    [InlineData("download")]
    [InlineData("manifest.yaml")]
    [InlineData("manifest.yml")]
    public void IsUsableArchiveFileName_WithoutUsableName_ShouldReturnFalse(string? fileName)
    {
        GenLauncherConstants.IsUsableArchiveFileName(fileName).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="GenLauncherConstants.ResolveEffectiveSourceUrl"/> selects the first valid HTTP/HTTPS URL
    /// that does not point to a YAML manifest descriptor.
    /// </summary>
    [Fact]
    public void ResolveEffectiveSourceUrl_ValidWebsiteCandidates_ShouldReturnFirstValidUrl()
    {
        // Arrange
        var candidate1 = "https://www.moddb.com/mods/cool-mod";
        var candidate2 = "https://discord.gg/invite";

        // Act
        var result = GenLauncherConstants.ResolveEffectiveSourceUrl(candidate1, candidate2);

        // Assert
        result.Should().Be("https://www.moddb.com/mods/cool-mod");
    }

    /// <summary>
    /// Verifies that <see cref="GenLauncherConstants.ResolveEffectiveSourceUrl"/> skips YAML descriptors and invalid URLs.
    /// </summary>
    [Fact]
    public void ResolveEffectiveSourceUrl_WithYamlDescriptorsAndInvalidUrls_ShouldSkipAndSelectValid()
    {
        // Arrange
        var descriptor = "https://example.com/repos/mod/manifest.yaml";
        var ymlDescriptor = "https://example.com/repos/mod/versions.yml";
        var invalidUrl = "not-a-valid-url";
        var validUrl = "https://discord.gg/generals";

        // Act
        var result = GenLauncherConstants.ResolveEffectiveSourceUrl(null, string.Empty, descriptor, invalidUrl, ymlDescriptor, validUrl);

        // Assert
        result.Should().Be("https://discord.gg/generals");
    }

    /// <summary>
    /// Verifies that <see cref="GenLauncherConstants.ResolveEffectiveSourceUrl"/> returns an empty string when all candidates are invalid.
    /// </summary>
    [Fact]
    public void ResolveEffectiveSourceUrl_AllInvalidCandidates_ShouldReturnEmptyString()
    {
        // Act & Assert
        GenLauncherConstants.ResolveEffectiveSourceUrl(null, string.Empty, "   ", "ftp://example.com", "manifest.yaml").Should().BeEmpty();
        GenLauncherConstants.ResolveEffectiveSourceUrl().Should().BeEmpty();
    }
}
