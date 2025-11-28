using System.Globalization;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Converters;

/// <summary>
/// Tests for IconKeyToPathDataConverter.
/// </summary>
public class IconKeyToPathDataConverterTests
{
    private readonly IconKeyToPathDataConverter _converter;

    /// <summary>
    /// Initializes a new instance of the <see cref="IconKeyToPathDataConverterTests"/> class.
    /// </summary>
    public IconKeyToPathDataConverterTests()
    {
        _converter = new IconKeyToPathDataConverter();
    }

    /// <summary>
    /// Verifies that Convert returns release icon path data for "release" key.
    /// </summary>
    [Fact]
    public void Convert_ReturnsReleaseIcon_ForReleaseKey()
    {
        // Act
        var result = _converter.Convert("release", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result);
        Assert.Contains("M", (string)result); // SVG path data typically starts with M
    }

    /// <summary>
    /// Verifies that Convert returns workflow icon path data for "workflow" key.
    /// </summary>
    [Fact]
    public void Convert_ReturnsWorkflowIcon_ForWorkflowKey()
    {
        // Act
        var result = _converter.Convert("workflow", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result);
        Assert.Contains("M", (string)result);
    }

    /// <summary>
    /// Verifies that Convert returns artifact icon path data for "artifact" key.
    /// </summary>
    [Fact]
    public void Convert_ReturnsArtifactIcon_ForArtifactKey()
    {
        // Act
        var result = _converter.Convert("artifact", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result);
        Assert.Contains("M", (string)result);
    }

    /// <summary>
    /// Verifies that Convert returns default icon for unknown key.
    /// </summary>
    [Fact]
    public void Convert_ReturnsDefaultIcon_ForUnknownKey()
    {
        // Act
        var result = _converter.Convert("unknown", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result);
        Assert.Contains("M", (string)result);
    }

    /// <summary>
    /// Verifies that Convert handles null value by returning default icon.
    /// </summary>
    [Fact]
    public void Convert_HandlesNullValue()
    {
        // Act
        var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result);
    }

    /// <summary>
    /// Verifies that Convert handles non-string values by returning default icon.
    /// </summary>
    [Fact]
    public void Convert_HandlesNonStringValues()
    {
        // Act
        var result = _converter.Convert(123, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result);
    }

    /// <summary>
    /// Verifies that ConvertBack is not implemented and throws exception.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("M0 0", typeof(string), null, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Verifies that different icon keys return different path data.
    /// </summary>
    [Fact]
    public void Convert_ReturnsDifferentPaths_ForDifferentKeys()
    {
        // Act
        var releasePath = _converter.Convert("release", typeof(string), null, CultureInfo.InvariantCulture);
        var workflowPath = _converter.Convert("workflow", typeof(string), null, CultureInfo.InvariantCulture);
        var artifactPath = _converter.Convert("artifact", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.NotEqual(releasePath, workflowPath);
        Assert.NotEqual(workflowPath, artifactPath);
        Assert.NotEqual(releasePath, artifactPath);
    }
}
