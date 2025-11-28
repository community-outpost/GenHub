using System.Globalization;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Converters;

/// <summary>
/// Tests for FileSizeConverter.
/// </summary>
public class FileSizeConverterTests
{
    private readonly FileSizeConverter _converter;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSizeConverterTests"/> class.
    /// </summary>
    public FileSizeConverterTests()
    {
        _converter = new FileSizeConverter();
    }

    /// <summary>
    /// Verifies that Convert formats bytes correctly.
    /// </summary>
    [Fact]
    public void Convert_FormatsBytesCorrectly()
    {
        // Act
        var result = _converter.Convert(512L, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("512 B", result);
    }

    /// <summary>
    /// Verifies that Convert formats kilobytes correctly.
    /// </summary>
    [Fact]
    public void Convert_FormatsKilobytesCorrectly()
    {
        // Act
        var result = _converter.Convert(1536L, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("1.50 KB", result);
    }

    /// <summary>
    /// Verifies that Convert formats megabytes correctly.
    /// </summary>
    [Fact]
    public void Convert_FormatsMegabytesCorrectly()
    {
        // Act
        var result = _converter.Convert(1048576L, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("1.00 MB", result);
    }

    /// <summary>
    /// Verifies that Convert formats gigabytes correctly.
    /// </summary>
    [Fact]
    public void Convert_FormatsGigabytesCorrectly()
    {
        // Act
        var result = _converter.Convert(1073741824L, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("1.00 GB", result);
    }

    /// <summary>
    /// Verifies that Convert handles zero bytes correctly.
    /// </summary>
    [Fact]
    public void Convert_HandlesZeroBytesCorrectly()
    {
        // Act
        var result = _converter.Convert(0L, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("0 B", result);
    }

    /// <summary>
    /// Verifies that Convert handles negative values correctly.
    /// </summary>
    [Fact]
    public void Convert_HandlesNegativeValuesCorrectly()
    {
        // Act
        var result = _converter.Convert(-1024L, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("-1024 B", result);
    }

    /// <summary>
    /// Verifies that Convert handles non-long values by returning empty string.
    /// </summary>
    [Fact]
    public void Convert_HandlesNonLongValues()
    {
        // Act
        var result = _converter.Convert("not a number", typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal("not a number", result);
    }

    /// <summary>
    /// Verifies that ConvertBack is not implemented and throws exception.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("1 KB", typeof(long), null, CultureInfo.InvariantCulture));
    }
}
