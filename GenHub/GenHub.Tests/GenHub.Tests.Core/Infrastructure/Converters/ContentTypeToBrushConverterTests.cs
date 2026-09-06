using System;
using System.Globalization;
using Avalonia.Media;
using GenHub.Infrastructure.Converters;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="ContentTypeToBrushConverter"/>.
/// </summary>
public class ContentTypeToBrushConverterTests
{
    private readonly ContentTypeToBrushConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that Convert returns the expected brush for each content type.
    /// </summary>
    /// <param name="contentType">The content type to convert.</param>
    /// <param name="expectedHex">The expected hex color string.</param>
    [Theory]
    [InlineData(ContentType.GameClient, "#06B6D4")]
    [InlineData(ContentType.Mod, "#A855F7")]
    [InlineData(ContentType.Patch, "#F59E0B")]
    [InlineData(ContentType.Map, "#10B981")]
    [InlineData(ContentType.MapPack, "#10B981")]
    [InlineData(ContentType.Addon, "#EC4899")]
    [InlineData(ContentType.ModdingTool, "#38BDF8")]
    [InlineData(ContentType.Executable, "#38BDF8")]
    [InlineData(ContentType.ContentBundle, "#6366F1")]
    [InlineData(ContentType.Mission, "#F97316")]
    [InlineData(ContentType.Skin, "#8B5CF6")]
    [InlineData(ContentType.LanguagePack, "#8B5CF6")]
    [InlineData(ContentType.UnknownContentType, "#A855F7")]
    public void Convert_WithContentType_ReturnsExpectedBrush(ContentType contentType, string expectedHex)
    {
        var result = _converter.Convert(contentType, typeof(IBrush), null, _culture) as SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Color.Parse(expectedHex), result.Color);
    }

    /// <summary>
    /// Verifies that Convert returns the default accent brush when an invalid value is provided.
    /// </summary>
    [Fact]
    public void Convert_WithInvalidValue_ReturnsDefaultAccentBrush()
    {
        var result = _converter.Convert("not a content type", typeof(IBrush), null, _culture) as SolidColorBrush;
        Assert.NotNull(result);
        Assert.Equal(Color.Parse("#A855F7"), result.Color);
    }

    /// <summary>
    /// Verifies that ConvertBack returns AvaloniaProperty.UnsetValue.
    /// </summary>
    [Fact]
    public void ConvertBack_ReturnsUnsetValue()
    {
        var result = _converter.ConvertBack(null, typeof(ContentType), null, _culture);
        Assert.Equal(Avalonia.AvaloniaProperty.UnsetValue, result);
    }
}
