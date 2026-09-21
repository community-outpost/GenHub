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
}
