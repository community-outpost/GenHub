using GenHub.Core.Constants;
using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="LaunchEntryPointResolver"/>.
/// </summary>
public class LaunchEntryPointResolverTests
{
    /// <summary>
    /// The Easy Anti-Cheat bootstrapper starts the 60Hz client and keeps running, so tracking has
    /// to be told which process the session actually moves to.
    /// </summary>
    [Fact]
    public void ResolveExpectedChildProcessName_ForTheAntiCheatBootstrapper_ReturnsTheSixtyHertzClient()
    {
        var path = Path.Combine("/workspace", GameClientConstants.GeneralsOnlineEacLauncherExecutable);

        var child = LaunchEntryPointResolver.ResolveExpectedChildProcessName(path);

        Assert.Equal(
            Path.GetFileNameWithoutExtension(GameClientConstants.GeneralsOnline60HzExecutable),
            child);
    }

    /// <summary>
    /// The bootstrapper ships with mixed-case naming; matching must not depend on it.
    /// </summary>
    [Fact]
    public void ResolveExpectedChildProcessName_MatchesTheBootstrapperCaseInsensitively()
    {
        var path = Path.Combine("/workspace", GameClientConstants.GeneralsOnlineEacLauncherExecutable.ToUpperInvariant());

        var child = LaunchEntryPointResolver.ResolveExpectedChildProcessName(path);

        Assert.NotNull(child);
    }

    /// <summary>
    /// A pre-EAC portable launches the game directly — there is no child to wait for, and claiming
    /// one would make every legacy launch fail.
    /// </summary>
    [Fact]
    public void ResolveExpectedChildProcessName_ForTheSixtyHertzClientItself_ReturnsNull()
    {
        var path = Path.Combine("/workspace", GameClientConstants.GeneralsOnline60HzExecutable);

        Assert.Null(LaunchEntryPointResolver.ResolveExpectedChildProcessName(path));
    }

    /// <summary>
    /// Every other client launches its own executable and is unaffected.
    /// </summary>
    [Fact]
    public void ResolveExpectedChildProcessName_ForAnOrdinaryExecutable_ReturnsNull()
    {
        var path = Path.Combine("/workspace", GameClientConstants.GeneralsExecutable);

        Assert.Null(LaunchEntryPointResolver.ResolveExpectedChildProcessName(path));
    }

    /// <summary>
    /// A missing path resolves to no expectation rather than throwing.
    /// </summary>
    /// <param name="path">The path under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveExpectedChildProcessName_WithoutAPath_ReturnsNull(string? path)
    {
        Assert.Null(LaunchEntryPointResolver.ResolveExpectedChildProcessName(path));
    }
}
