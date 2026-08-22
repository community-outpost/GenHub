using System;
using System.Globalization;
using Avalonia.Media;
using GenHub.Infrastructure.Converters;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="ContentTypeToBadgeBackgroundConverter"/>.
/// </summary>
public class ContentTypeToBadgeBackgroundConverterTests
{
    private readonly ContentTypeToBadgeBackgroundConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that Convert returns the expected tinted brush for each content type.
    /// </summary>
    /// <param name="contentType">The content type to convert.</param>
    /// <param name="expectedHex">The expected hex color string.</param>
    [Theory]
    [InlineData(ContentType.GameClient, "#2506B6D4")]
    [InlineData(ContentType.Mod, "#25A855F7")]
    [InlineData(ContentType.Patch, "#25F59E0B")]
    [InlineData(ContentType.Map, "#2510B981")]
    [InlineData(ContentType.MapPack, "#2510B981")]
    [InlineData(ContentType.Addon, "#25EC4899")]
    [InlineData(ContentType.ModdingTool, "#2538BDF8")]
    [InlineData(ContentType.Executable, "#2538BDF8")]
    [InlineData(ContentType.ContentBundle, "#256366F1")]
    [InlineData(ContentType.Mission, "#25F97316")]
    [InlineData(ContentType.Skin, "#258B5CF6")]
    [InlineData(ContentType.LanguagePack, "#258B5CF6")]
    [InlineData(ContentType.UnknownContentType, "#25A855F7")]
    public void Convert_WithContentType_ReturnsExpectedTintedBrush(ContentType contentType, string expectedHex)
    {
        var result = _converter.Convert(contentType, typeof(IBrush), null, _culture) as SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Color.Parse(expectedHex), result.Color);
    }

    /// <summary>
    /// Verifies that Convert returns the default tinted brush when an invalid value is provided.
    /// </summary>
    [Fact]
    public void Convert_WithInvalidValue_ReturnsDefaultTintedBrush()
    {
        var result = _converter.Convert("not a content type", typeof(IBrush), null, _culture) as SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Color.Parse("#25A855F7"), result.Color);
    }

    /// <summary>
    /// Verifies that ConvertBack throws NotImplementedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(null, typeof(ContentType), null, _culture));
    }
}
