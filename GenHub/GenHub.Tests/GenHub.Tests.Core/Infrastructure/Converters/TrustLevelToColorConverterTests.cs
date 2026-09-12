using System;
using System.Globalization;
using Avalonia;
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
    /// Tests that Convert returns representative brush for Trusted trust level.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithTrustedLevel_ReturnsSuccessBrush()
    {
        var result = _converter.Convert(TrustLevel.Trusted, typeof(IBrush), null, _culture) as ISolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Color.Parse("#10B981"), result.Color);
    }

    /// <summary>
    /// Tests that Convert returns representative brush for Verified trust level.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithVerifiedLevel_ReturnsAccentBrush()
    {
        var result = _converter.Convert(TrustLevel.Verified, typeof(IBrush), null, _culture) as ISolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Color.Parse("#06B6D4"), result.Color);
    }

    /// <summary>
    /// Tests that Convert returns representative brush for Untrusted trust level.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithUntrustedLevel_ReturnsSecondaryBrush()
    {
        var result = _converter.Convert(TrustLevel.Untrusted, typeof(IBrush), null, _culture) as ISolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Color.Parse("#9A9AB0"), result.Color);
    }

    /// <summary>
    /// Tests that Convert returns secondary brush for null or non-enum value.
    /// </summary>
    [AvaloniaFact]
    public void Convert_WithNullOrInvalidValue_ReturnsSecondaryBrush()
    {
        var resultNull = _converter.Convert(null, typeof(IBrush), null, _culture) as ISolidColorBrush;
        var resultInvalid = _converter.Convert("not-a-trust-level", typeof(IBrush), null, _culture) as ISolidColorBrush;

        Assert.NotNull(resultNull);
        Assert.Equal(Color.Parse("#9A9AB0"), resultNull.Color);
        Assert.NotNull(resultInvalid);
        Assert.Equal(Color.Parse("#9A9AB0"), resultInvalid.Color);
    }

    /// <summary>
    /// Tests that ConvertBack returns AvaloniaProperty.UnsetValue.
    /// </summary>
    [Fact]
    public void ConvertBack_ReturnsUnsetValue()
    {
        var result = _converter.ConvertBack(Brushes.Green, typeof(TrustLevel), null, _culture);
        Assert.Equal(AvaloniaProperty.UnsetValue, result);
    }
}
