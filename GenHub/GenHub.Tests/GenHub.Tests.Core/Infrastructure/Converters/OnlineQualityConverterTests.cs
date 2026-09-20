using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Online;
using GenHub.Infrastructure.Converters;
using System.Globalization;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for the online quality converters.
/// </summary>
public class OnlineQualityConverterTests
{
    /// <summary>
    /// Tests that quality maps to its resource key without a localization service.
    /// </summary>
    /// <param name="quality">The connection quality.</param>
    /// <param name="expected">The expected resource key.</param>
    [Theory]
    [InlineData(OnlineConnectionQuality.Direct, "Online.Quality.Direct")]
    [InlineData(OnlineConnectionQuality.Relay, "Online.Quality.Relay")]
    [InlineData(OnlineConnectionQuality.Connecting, "Online.Quality.Connecting")]
    [InlineData(OnlineConnectionQuality.Unknown, "Online.Quality.Unknown")]
    public void QualityText_ShouldMapToKey(OnlineConnectionQuality quality, string expected)
    {
        // Arrange
        var converter = new OnlineQualityTextConverter();

        // Act
        var result = converter.Convert(quality, typeof(string), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that quality maps to its status brush.
    /// </summary>
    /// <param name="quality">The connection quality.</param>
    /// <param name="expected">The expected color constant.</param>
    [Theory]
    [InlineData(OnlineConnectionQuality.Direct, UiConstants.StatusSuccessColor)]
    [InlineData(OnlineConnectionQuality.Relay, UiConstants.StatusUpdateAvailableColor)]
    [InlineData(OnlineConnectionQuality.Unknown, UiConstants.StatusInactiveColor)]
    [InlineData(OnlineConnectionQuality.Connecting, UiConstants.StatusInactiveColor)]
    public void QualityBrush_ShouldMapToStatusColor(OnlineConnectionQuality quality, string expected)
    {
        // Arrange
        var converter = new OnlineQualityBrushConverter();

        // Act
        var result = converter.Convert(quality, typeof(IBrush), null, CultureInfo.InvariantCulture);

        // Assert
        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse(expected), brush.Color);
    }
}
