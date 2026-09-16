using GenHub.Features.Content.Services.GenLauncher;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherChecksumValidator"/>.
/// </summary>
public sealed class GenLauncherChecksumValidatorTests
{
    /// <summary>
    /// Tests that RequiresValidation identifies engine file extensions requiring MD5 validation.
    /// </summary>
    /// <param name="fileName">The file name to inspect.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData("mod.big", true)]
    [InlineData("data.ini", true)]
    [InlineData("model.w3d", true)]
    [InlineData("texture.dds", true)]
    [InlineData("text.csf", true)]
    [InlineData("readme.txt", false)]
    [InlineData("installer.exe", false)]
    [InlineData("document.pdf", false)]
    public void RequiresValidation_IdentifiesEngineExtensionsCorrectly(string fileName, bool expected)
    {
        var result = GenLauncherChecksumValidator.RequiresValidation(fileName);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that CleanETag strips quotes and HTML entities from ETags.
    /// </summary>
    [Fact]
    public void CleanETag_StripsQuotesAndEntities()
    {
        Assert.Equal("abcdef123456", GenLauncherChecksumValidator.CleanETag("\"abcdef123456\""));
        Assert.Equal("abcdef123456", GenLauncherChecksumValidator.CleanETag("&quot;abcdef123456&quot;"));
        Assert.Equal("abcdef123456", GenLauncherChecksumValidator.CleanETag("  \"abcdef123456\"  "));
    }

    /// <summary>
    /// Tests that ValidateFile returns true when MD5 hashes match.
    /// </summary>
    [Fact]
    public void ValidateFile_MatchingMd5_ReturnsTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".big");
        try
        {
            var content = "Generals Mod Data Content";
            File.WriteAllText(tempFile, content, Encoding.UTF8);

            var expectedMd5 = GenLauncherChecksumValidator.ComputeMd5Hex(tempFile);
            var isValid = GenLauncherChecksumValidator.ValidateFile(tempFile, $"\"{expectedMd5}\"");

            Assert.True(isValid);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Tests that ValidateFile returns false when the MD5 hash does not match.
    /// </summary>
    [Fact]
    public void ValidateFile_MismatchedMd5_ReturnsFalse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".big");
        try
        {
            File.WriteAllText(tempFile, "Real Content", Encoding.UTF8);

            var isValid = GenLauncherChecksumValidator.ValidateFile(tempFile, "0123456789abcdef0123456789abcdef");
            Assert.False(isValid);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
