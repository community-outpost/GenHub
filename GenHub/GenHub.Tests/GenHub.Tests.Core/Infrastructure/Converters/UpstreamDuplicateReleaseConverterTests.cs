using GenHub.Infrastructure.Converters;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="UpstreamDuplicateReleaseConverter"/>.
/// </summary>
public class UpstreamDuplicateReleaseConverterTests
{
    private readonly UpstreamDuplicateReleaseConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that a static release duplicating a live upstream version is hidden.
    /// </summary>
    [Fact]
    public void Convert_WithDuplicateUpstreamVersion_ReturnsFalse()
    {
        var result = _converter.Convert(
            ["1.06", new List<string> { "11-09-2026", "1.06" }, true],
            typeof(bool),
            null,
            _culture);

        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that version matching ignores a leading v and casing.
    /// </summary>
    [Fact]
    public void Convert_WithLeadingVPrefix_ReturnsFalse()
    {
        var result = _converter.Convert(
            ["weekly-2026-09-25", new List<string> { "vweekly-2026-09-25" }, true],
            typeof(bool),
            null,
            _culture);

        Assert.False((bool?)result);
    }

    /// <summary>
    /// Tests that a fallback release with a genuinely different version stays visible.
    /// </summary>
    [Fact]
    public void Convert_WithDistinctFallbackVersion_ReturnsTrue()
    {
        var result = _converter.Convert(
            ["1.06", new List<string> { "11-09-2026" }, true],
            typeof(bool),
            null,
            _culture);

        Assert.True((bool?)result);
    }

    /// <summary>
    /// Tests that untracked items and empty previews never hide static releases.
    /// </summary>
    [Fact]
    public void Convert_WhenNotTrackedOrNoPreview_ReturnsTrue()
    {
        Assert.True((bool?)_converter.Convert(["1.06", new List<string> { "1.06" }, false], typeof(bool), null, _culture));
        Assert.True((bool?)_converter.Convert(["1.06", new List<string>(), true], typeof(bool), null, _culture));
        Assert.True((bool?)_converter.Convert(["1.06", null, true], typeof(bool), null, _culture));
    }
}
