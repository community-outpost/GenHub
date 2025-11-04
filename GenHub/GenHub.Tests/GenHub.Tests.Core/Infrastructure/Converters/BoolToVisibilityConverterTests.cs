using Avalonia.Controls;
using GenHub.Infrastructure.Converters;
using System.Globalization;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="BoolToVisibilityConverter"/>.
/// </summary>
public class BoolToVisibilityConverterTests
{
    private readonly BoolToVisibilityConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that Convert with true value returns Visible.
    /// </summary>
    [Fact]
    public void Convert_WithTrueValue_ReturnsVisible()
    {
        var result = _converter.Convert(true, typeof(object), null, _culture);
        Assert.Equal("Visible", result!.ToString());
    }

    /// <summary>
    /// Tests that Convert with false value returns Collapsed.
    /// </summary>
    [Fact]
    public void Convert_WithFalseValue_ReturnsCollapsed()
    {
        var result = _converter.Convert(false, typeof(object), null, _culture);
        Assert.Equal("Collapsed", result!.ToString());
    }

    /// <summary>
    /// Tests that Convert with null value returns Collapsed.
    /// </summary>
    [Fact]
    public void Convert_WithNullValue_ReturnsCollapsed()
    {
        var result = _converter.Convert(null, typeof(object), null, _culture);
        Assert.Equal("Collapsed", result!.ToString());
    }

    /// <summary>
    /// Tests that Convert with non boolean value returns Collapsed.
    /// </summary>
    [Fact]
    public void Convert_WithNonBooleanValue_ReturnsCollapsed()
    {
        var result = _converter.Convert("not a boolean", typeof(object), null, _culture);
        Assert.Equal("Collapsed", result!.ToString());
    }

    /// <summary>
    /// Tests that <see cref="BoolToVisibilityConverter.ConvertBack"/> throws NotImplementedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() => _converter.ConvertBack("Visible", typeof(bool), null, _culture));
    }
}
