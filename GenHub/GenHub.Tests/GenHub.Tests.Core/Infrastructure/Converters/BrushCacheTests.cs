using Avalonia.Media;
using GenHub.Infrastructure.Converters;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="BrushCache"/> and converter brush stability.
/// Converters must return stable brush instances: a fresh brush per evaluation keeps brush
/// transitions perpetually dirty, which overflowed the stack during rapid demo tab switches.
/// </summary>
public class BrushCacheTests
{
    /// <summary>
    /// Verifies that requesting the same color twice returns the same brush instance.
    /// </summary>
    [Fact]
    public void Get_SameColorTwice_ReturnsSameInstance()
    {
        var first = BrushCache.Get(Color.Parse("#9C27B0"));
        var second = BrushCache.Get(Color.Parse("#9C27B0"));

        Assert.Same(first, second);
    }

    /// <summary>
    /// Verifies that different colors resolve to different brushes with the expected colors.
    /// </summary>
    [Fact]
    public void Get_DifferentColors_ReturnsDistinctBrushes()
    {
        var purple = BrushCache.Get(Color.Parse("#9C27B0"));
        var orange = BrushCache.Get(Color.Parse("#BD5A0F"));

        Assert.NotSame(purple, orange);
        Assert.Equal(Color.Parse("#9C27B0"), purple.Color);
        Assert.Equal(Color.Parse("#BD5A0F"), orange.Color);
    }

    /// <summary>
    /// Verifies that transparent resolves to the shared transparent brush.
    /// </summary>
    [Fact]
    public void Get_TransparentColor_ReturnsSharedTransparentBrush()
    {
        Assert.Same(BrushCache.Transparent, BrushCache.Get(Colors.Transparent));
        Assert.Equal(Colors.Transparent, BrushCache.Transparent.Color);
    }

    /// <summary>
    /// Verifies that the color converter returns stable instances for repeated evaluations.
    /// </summary>
    /// <param name="colorHex">The color hex value to convert twice.</param>
    [Theory]
    [InlineData("#9C27B0")]
    [InlineData("#BD5A0F")]
    public void ColorToBrushConverter_SameHexTwice_ReturnsSameInstance(string colorHex)
    {
        var converter = new ColorToBrushConverter();

        var first = converter.Convert(colorHex, typeof(IBrush), null, CultureInfo.InvariantCulture);
        var second = converter.Convert(colorHex, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Same(first, second);
    }

    /// <summary>
    /// Verifies that the color converter maps unparsable values to the shared transparent brush.
    /// </summary>
    /// <param name="value">The unparsable value to convert.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-color")]
    public void ColorToBrushConverter_UnparsableValue_ReturnsSharedTransparentBrush(string? value)
    {
        var converter = new ColorToBrushConverter();

        var result = converter.Convert(value, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Same(BrushCache.Transparent, result);
    }
}
