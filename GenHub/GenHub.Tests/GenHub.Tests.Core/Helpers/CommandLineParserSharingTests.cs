using System;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for command line parsing of profile sharing URIs and arguments.
/// </summary>
public class CommandLineParserSharingTests
{
    /// <summary>
    /// Verifies extraction of genhub://profile/import URI from arguments.
    /// </summary>
    [Fact]
    public void ExtractProfileShareUri_Should_ExtractDirectGenHubUri()
    {
        // Arrange
        var uri = "genhub://profile/import?data=H4sICAAAAAAA_w==";
        var args = new[] { "--debug", uri };

        // Act
        var result = CommandLineParser.ExtractProfileShareUri(args);

        // Assert
        Assert.Equal(uri, result);
    }

    /// <summary>
    /// Verifies extraction of --import-profile with inline equals.
    /// </summary>
    [Fact]
    public void ExtractProfileShareUri_Should_ExtractInlineImportProfileArg()
    {
        // Arrange
        var payload = "genhub://profile/import?data=test";
        var args = new[] { $"--import-profile={payload}" };

        // Act
        var result = CommandLineParser.ExtractProfileShareUri(args);

        // Assert
        Assert.Equal(payload, result);
    }

    /// <summary>
    /// Verifies extraction of --import-profile followed by next argument.
    /// </summary>
    [Fact]
    public void ExtractProfileShareUri_Should_ExtractSpacedImportProfileArg()
    {
        // Arrange
        var payload = "genhub://profile/import?data=test";
        var args = new[] { "--import-profile", payload };

        // Act
        var result = CommandLineParser.ExtractProfileShareUri(args);

        // Assert
        Assert.Equal(payload, result);
    }

    /// <summary>
    /// Verifies extraction of .ghprofile file path from args.
    /// </summary>
    [Fact]
    public void ExtractProfileShareUri_Should_ExtractGhProfileFilePath()
    {
        // Arrange
        var filePath = "/path/to/mycustom.ghprofile";
        var args = new[] { filePath };

        // Act
        var result = CommandLineParser.ExtractProfileShareUri(args);

        // Assert
        Assert.Equal(filePath, result);
    }

    /// <summary>
    /// Verifies that null or empty args return null.
    /// </summary>
    [Fact]
    public void ExtractProfileShareUri_Should_ReturnNull_WhenNoMatchingArgs()
    {
        // Arrange
        var args = new[] { "--launch-profile", "123", "--multi-instance" };

        // Act
        var result = CommandLineParser.ExtractProfileShareUri(args);

        // Assert
        Assert.Null(result);
    }
}
