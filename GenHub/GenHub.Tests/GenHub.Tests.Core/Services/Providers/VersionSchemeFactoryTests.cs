using GenHub.Core.Constants;
using GenHub.Core.Services.Providers;
using GenHub.Core.Services.Providers.VersionSchemes;
using GenHub.Tests.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Services.Providers;

/// <summary>
/// Tests for <see cref="VersionSchemeFactory"/>.
/// </summary>
public class VersionSchemeFactoryTests
{
    /// <summary>
    /// Verifies that every registered scheme resolves by its identifier.
    /// </summary>
    /// <param name="schemeId">The scheme identifier.</param>
    [Theory]
    [InlineData(VersionSchemeConstants.Numeric)]
    [InlineData(VersionSchemeConstants.IsoDate)]
    [InlineData(VersionSchemeConstants.MmddyyQfe)]
    public void GetScheme_ResolvesRegisteredSchemes(string schemeId)
    {
        var factory = TestVersionComparer.CreateSchemeFactory();

        Assert.Equal(schemeId, factory.GetScheme(schemeId).SchemeId);
    }

    /// <summary>
    /// Verifies that scheme identifiers are matched without regard to case.
    /// </summary>
    [Fact]
    public void GetScheme_IgnoresCase()
    {
        var factory = TestVersionComparer.CreateSchemeFactory();

        Assert.Equal(VersionSchemeConstants.MmddyyQfe, factory.GetScheme("MMDDYY-QFE").SchemeId);
    }

    /// <summary>
    /// Verifies that an unknown or absent identifier falls back to the default scheme,
    /// so a third-party provider definition cannot break version comparison.
    /// </summary>
    /// <param name="schemeId">The scheme identifier.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("does-not-exist")]
    public void GetScheme_FallsBackToDefault(string? schemeId)
    {
        var factory = TestVersionComparer.CreateSchemeFactory();

        Assert.Equal(VersionSchemeConstants.Default, factory.GetScheme(schemeId).SchemeId);
    }

    /// <summary>
    /// Verifies that the factory refuses to start without the default scheme, rather than
    /// failing later at the first comparison.
    /// </summary>
    [Fact]
    public void Constructor_ThrowsWhenDefaultSchemeIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() => new VersionSchemeFactory(
            [new MmddyyQfeVersionScheme()],
            NullLogger<VersionSchemeFactory>.Instance));
    }
}
