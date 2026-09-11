using System;
using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using GenHub.Core.Models.Enums;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="TrustLevelToColorConverter"/>.
/// </summary>
public class TrustLevelToColorConverterTests
{
    private readonly TrustLevelToColorConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that Convert returns Green brush for Trusted trust level.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithTrustedLevel_ReturnsGreenBrush()
    {
        var result = _converter.Convert(TrustLevel.Trusted, typeof(IBrush), null, _culture) as ISolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Colors.Green, result.Color);
    }

    /// <summary>
    /// Tests that Convert returns SkyBlue brush for Verified trust level.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithVerifiedLevel_ReturnsSkyBlueBrush()
    {
        var result = _converter.Convert(TrustLevel.Verified, typeof(IBrush), null, _culture) as ISolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Colors.SkyBlue, result.Color);
    }

    /// <summary>
    /// Tests that Convert returns Gray brush for Untrusted trust level.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithUntrustedLevel_ReturnsGrayBrush()
    {
        var result = _converter.Convert(TrustLevel.Untrusted, typeof(IBrush), null, _culture) as ISolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Colors.Gray, result.Color);
    }

    /// <summary>
    /// Tests that Convert returns Gray brush for null or non-enum value.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithNullOrInvalidValue_ReturnsGrayBrush()
    {
        var resultNull = _converter.Convert(null, typeof(IBrush), null, _culture) as ISolidColorBrush;
        var resultInvalid = _converter.Convert("not-a-trust-level", typeof(IBrush), null, _culture) as ISolidColorBrush;

        Assert.NotNull(resultNull);
        Assert.Equal(Colors.Gray, resultNull.Color);
        Assert.NotNull(resultInvalid);
        Assert.Equal(Colors.Gray, resultInvalid.Color);
    }

    /// <summary>
    /// Tests that ConvertBack throws NotSupportedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(Brushes.Green, typeof(TrustLevel), null, _culture));
    }
}
