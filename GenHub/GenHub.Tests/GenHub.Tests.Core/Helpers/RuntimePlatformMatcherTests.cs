using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="RuntimePlatformMatcher"/>.
/// </summary>
public sealed class RuntimePlatformMatcherTests
{
    /// <summary>
    /// Verifies OS families, runtime identifiers and unconstrained platforms against each host.
    /// </summary>
    /// <param name="platform">The declared platform.</param>
    /// <param name="host">The host runtime identifier.</param>
    /// <param name="expected">Whether the platform applies to the host.</param>
    [Theory]
    [InlineData(null, "win-x64", true)]
    [InlineData("", "osx-arm64", true)]
    [InlineData("   ", "linux-x64", true)]
    [InlineData("windows", "win-x64", true)]
    [InlineData("Windows", "win-arm64", true)]
    [InlineData("windows", "osx-arm64", false)]
    [InlineData("windows", "linux-x64", false)]
    [InlineData("macos", "osx-arm64", true)]
    [InlineData("macOS", "osx-x64", true)]
    [InlineData("macos", "win-x64", false)]
    [InlineData("macos", "linux-x64", false)]
    [InlineData("linux", "linux-x64", true)]
    [InlineData("linux", "linux-musl-arm64", true)]
    [InlineData("linux", "win-x64", false)]
    [InlineData("linux", "osx-arm64", false)]
    [InlineData("win-x64", "win-x64", true)]
    [InlineData("WIN-X64", "win-x64", true)]
    [InlineData("win-x64", "win-arm64", false)]
    [InlineData("win-x64", "linux-x64", false)]
    [InlineData("osx-arm64", "osx-arm64", true)]
    [InlineData("osx-arm64", "osx-x64", false)]
    [InlineData("macos-arm64", "osx-arm64", true)]
    [InlineData("linux-x64", "linux-x64", true)]
    [InlineData("linux-x64", "linux-arm64", false)]
    [InlineData("linux-x64", "osx-x64", false)]
    [InlineData("freebsd", "linux-x64", false)]
    [InlineData("win-", "win-x64", false)]
    [InlineData("windows", "", false)]
    [InlineData("windows", null, false)]
    [InlineData("windows", "freebsd-x64", false)]
    public void Matches_ReturnsExpected(string? platform, string? host, bool expected)
    {
        Assert.Equal(expected, RuntimePlatformMatcher.Matches(platform, host));
    }
}
