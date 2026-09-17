using GenHub.Infrastructure.Converters;
using System;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="LocalizedReplayTooltipConverter"/>.
/// </summary>
public class LocalizedReplayTooltipConverterTests
{
    private readonly LocalizedReplayTooltipConverter _converter = LocalizedReplayTooltipConverter.Instance;
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that null or empty input returns an empty string or the input representation without crashing.
    /// </summary>
    [Fact]
    public void Convert_WithNullOrEmpty_ReturnsExpected()
    {
        var resultNull = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(string.Empty, resultNull);

        var resultEmpty = _converter.Convert(string.Empty, typeof(string), null, _culture);
        Assert.Equal(string.Empty, resultEmpty);
    }

    /// <summary>
    /// Verifies that when localization is not initialized, fallback strings are preserved.
    /// </summary>
    [Fact]
    public void Convert_WithoutLocalization_ReturnsOriginalString()
    {
        const string text = "Checkpoint recovery and match takeover require a game client with checkpoint capabilities (e.g. MP-Recovery or modern community engine).";
        var result = _converter.Convert(text, typeof(string), null, _culture);
        Assert.Equal(text, result);
    }

    /// <summary>
    /// Verifies that ConvertBack throws NotSupportedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("test", typeof(string), null, _culture));
    }
}
