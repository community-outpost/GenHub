using Avalonia;
using Avalonia.Media;
using GenHub.Infrastructure.Converters;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="FeaturedColorToBrushConverter"/> and
/// <see cref="FeaturedColorToBoxShadowConverter"/>.
/// </summary>
public class FeaturedColorConverterTests
{
    private readonly FeaturedColorToBrushConverter _brushConverter = new();
    private readonly FeaturedColorToBoxShadowConverter _shadowConverter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies a valid featured hex resolves to a brush of that color.
    /// </summary>
    [Fact]
    public void BrushConverter_ValidHex_ReturnsBrushOfThatColor()
    {
        var result = _brushConverter.Convert("#76F525", typeof(IBrush), null, _culture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Color.Parse("#76F525"), brush.Color);
    }

    /// <summary>
    /// Verifies an alpha-bearing accent normalizes to fully opaque so borders, badges,
    /// and glows agree on the same color.
    /// </summary>
    [Fact]
    public void BrushConverter_AlphaBearingHex_ReturnsFullyOpaqueBrush()
    {
        var result = _brushConverter.Convert("#33F59E0B", typeof(IBrush), null, _culture);

        var brush = Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(new Color(0xFF, 0xF5, 0x9E, 0x0B), brush.Color);
    }

    /// <summary>
    /// Verifies repeated evaluations return the shared cached brush instance.
    /// </summary>
    [Fact]
    public void BrushConverter_SameHexTwice_ReturnsSameInstance()
    {
        var first = _brushConverter.Convert("#76F525", typeof(IBrush), null, _culture);
        var second = _brushConverter.Convert("#76F525", typeof(IBrush), null, _culture);

        Assert.Same(first, second);
    }

    /// <summary>
    /// Verifies null or invalid values return UnsetValue so styled fallback brushes apply.
    /// </summary>
    /// <param name="value">The candidate featured color value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-color")]
    public void BrushConverter_InvalidValue_ReturnsUnsetValue(string? value)
    {
        var result = _brushConverter.Convert(value, typeof(IBrush), null, _culture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }

    /// <summary>
    /// Verifies a valid featured hex resolves to a glow tinted with that color.
    /// </summary>
    [Fact]
    public void ShadowConverter_ValidHex_ReturnsTintedGlow()
    {
        var result = _shadowConverter.Convert("#76F525", typeof(BoxShadows), null, _culture);

        var shadows = Assert.IsType<BoxShadows>(result);
        Assert.Equal(1, shadows.Count);
        var shadow = shadows[0];
        Assert.Equal(new Color(0x59, 0x76, 0xF5, 0x25), shadow.Color);
        Assert.Equal(0, shadow.OffsetX);
        Assert.Equal(10, shadow.OffsetY);
        Assert.Equal(35, shadow.Blur);
    }

    /// <summary>
    /// Verifies the hover parameter resolves to a brighter glow in the featured hue
    /// instead of the legacy fixed gold hover glow.
    /// </summary>
    [Fact]
    public void ShadowConverter_HoverParameter_ReturnsBrighterTintedGlow()
    {
        var result = _shadowConverter.Convert(
            "#0F8E45",
            typeof(BoxShadows),
            FeaturedColorToBoxShadowConverter.HoverParameter,
            _culture);

        var shadows = Assert.IsType<BoxShadows>(result);
        Assert.Equal(1, shadows.Count);
        var shadow = shadows[0];
        Assert.Equal(new Color(0x80, 0x0F, 0x8E, 0x45), shadow.Color);
        Assert.Equal(0, shadow.OffsetX);
        Assert.Equal(14, shadow.OffsetY);
        Assert.Equal(42, shadow.Blur);
    }

    /// <summary>
    /// Verifies null or invalid values return UnsetValue so styled fallback shadows apply.
    /// </summary>
    /// <param name="value">The candidate featured color value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-color")]
    public void ShadowConverter_InvalidValue_ReturnsUnsetValue(string? value)
    {
        var result = _shadowConverter.Convert(value, typeof(BoxShadow), null, _culture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }
}
