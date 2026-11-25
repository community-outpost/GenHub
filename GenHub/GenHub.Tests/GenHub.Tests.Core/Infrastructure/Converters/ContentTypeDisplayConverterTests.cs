using GenHub.Infrastructure.Converters;
using System;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="ContentTypeDisplayConverter"/>.
/// </summary>
public class ContentTypeDisplayConverterTests
{
    private readonly ContentTypeDisplayConverter _converter = ContentTypeDisplayConverter.Instance;
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that converting a ContentType enum returns its user-friendly display name.
    /// </summary>
    /// <param name="type">The content type enum value.</param>
    /// <param name="expected">The expected display string.</param>
    [Theory]
    [InlineData(GenHub.Core.Models.Enums.ContentType.GameClient, "GameClient")]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Mod, "Mods")]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Patch, "Patch")]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Map, "Map")]
    [InlineData(GenHub.Core.Models.Enums.ContentType.Addon, "Addons")]
    [InlineData(GenHub.Core.Models.Enums.ContentType.ContentBundle, "Content Bundle")]
    public void Convert_WithEnum_ReturnsExpectedDisplayName(GenHub.Core.Models.Enums.ContentType type, string expected)
    {
        var result = _converter.Convert(type, typeof(string), null, _culture);
        Assert.NotNull(result);
        Assert.Equal(expected, result.ToString());
    }

    /// <summary>
    /// Verifies that converting a ContentType string representation parses and returns its display name.
    /// </summary>
    /// <param name="typeString">The content type string representation.</param>
    /// <param name="expected">The expected display string.</param>
    [Theory]
    [InlineData("GameClient", "GameClient")]
    [InlineData("gameclient", "GameClient")]
    [InlineData("Mod", "Mods")]
    [InlineData("Patch", "Patch")]
    [InlineData("Map", "Map")]
    [InlineData("Addon", "Addons")]
    [InlineData("addon", "Addons")]
    [InlineData("ContentBundle", "Content Bundle")]
    public void Convert_WithString_ParsesAndReturnsExpectedDisplayName(string typeString, string expected)
    {
        var result = _converter.Convert(typeString, typeof(string), null, _culture);
        Assert.NotNull(result);
        Assert.Equal(expected, result.ToString());
    }

    /// <summary>
    /// Verifies that converting null returns an empty string.
    /// </summary>
    [Fact]
    public void Convert_WithNull_ReturnsEmptyString()
    {
        var result = _converter.Convert(null, typeof(string), null, _culture);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that converting a non-enum string returns the original string value.
    /// </summary>
    [Fact]
    public void Convert_WithUnknownString_ReturnsOriginalString()
    {
        var result = _converter.Convert("NonExistentType", typeof(string), null, _culture);
        Assert.Equal("NonExistentType", result);
    }

    /// <summary>
    /// Verifies that ConvertBack throws NotSupportedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("Mods", typeof(GenHub.Core.Models.Enums.ContentType), null, _culture));
    }
}
