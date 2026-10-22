using GenHub.Core.Models.Providers;
using GenHub.Infrastructure.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="ReleaseOrderingConverter"/>.
/// </summary>
public class ReleaseOrderingConverterTests
{
    private readonly ReleaseOrderingConverter _converter = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Tests that releases are ordered latest-first: the latest release on top,
    /// then by release date descending.
    /// </summary>
    [Fact]
    public void Convert_WithUnorderedReleases_OrdersLatestFirst()
    {
        var releases = new List<ContentRelease>
        {
            new() { Version = "1.0.0", ReleaseDate = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc) },
            new() { Version = "1.0.1", ReleaseDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc), IsLatest = true },
            new() { Version = "0.9.0", ReleaseDate = new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc) },
        };

        var result = _converter.Convert(releases, typeof(object), null, _culture);

        var ordered = Assert.IsAssignableFrom<IReadOnlyList<ContentRelease>>(result);
        Assert.Equal(["1.0.1", "1.0.0", "0.9.0"], ordered.Select(r => r.Version));
    }

    /// <summary>
    /// Tests that non-release values pass through untouched.
    /// </summary>
    [Fact]
    public void Convert_WithNonReleaseValue_ReturnsValue()
    {
        Assert.Null(_converter.Convert(null, typeof(object), null, _culture));
        Assert.Equal("x", _converter.Convert("x", typeof(object), null, _culture));
    }

    /// <summary>
    /// Tests that <see cref="ReleaseOrderingConverter.ConvertBack"/> throws <see cref="NotImplementedException"/>.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack(null, typeof(object), null, _culture));
    }
}
