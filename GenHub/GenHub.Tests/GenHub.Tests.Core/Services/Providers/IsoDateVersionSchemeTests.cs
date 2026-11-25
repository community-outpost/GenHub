using GenHub.Core.Services.Providers.VersionSchemes;

namespace GenHub.Tests.Core.Services.Providers;

/// <summary>
/// Tests for <see cref="IsoDateVersionScheme"/>.
/// </summary>
public class IsoDateVersionSchemeTests
{
    private readonly IsoDateVersionScheme _scheme = new();

    /// <summary>
    /// Verifies each declared ISO-date representation.
    /// </summary>
    /// <param name="version">The version string.</param>
    [Theory]
    [InlineData("2025-11-07")]
    [InlineData("2025/11/07")]
    [InlineData("2025.11.07")]
    [InlineData("20251107")]
    public void TryParse_AcceptsDeclaredFormats(string version)
    {
        Assert.True(_scheme.TryParse(version, out var result));
        Assert.Equal(new long[] { 2025, 11, 7 }, result.Components);
    }

    /// <summary>
    /// Verifies malformed separators are not removed to manufacture a valid date.
    /// </summary>
    /// <param name="version">The malformed version string.</param>
    [Theory]
    [InlineData("2025--11-07")]
    [InlineData("2025/11-07")]
    [InlineData("/2025/11/07")]
    [InlineData("2025.11.07.")]
    [InlineData("2025117")]
    public void TryParse_RejectsMalformedFormats(string version)
    {
        Assert.False(_scheme.TryParse(version, out var result));
        Assert.True(result.IsEmpty);
    }
}
