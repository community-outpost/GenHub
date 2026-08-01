using System;
using System.IO;
using GenHub.Core.Models.Enums;
using GenHub.Features.GameSettings;
using Xunit;

namespace GenHub.Tests.Core.Features.GameSettings;

/// <summary>
/// Pins the Options.ini directory each platform provider produces.
/// <para>
/// These paths are dictated by the game engine, not chosen by GenHub. They mirror
/// <c>GlobalData::BuildUserDataPathFromRegistry</c> in the GeneralsGameCode tree. If
/// GenHub writes Options.ini anywhere else, the engine simply never reads it: the
/// launch still succeeds and every profile setting is silently discarded, with no
/// error anywhere. That failure is invisible in manual testing, so it is pinned here.
/// </para>
/// </summary>
public class GamePathProviderTests
{
    /// <summary>
    /// macOS resolves under Application Support with no vendor subdirectory. Notably
    /// this is NOT the SDL_GetPrefPath convention of
    /// <c>~/Library/Application Support/&lt;org&gt;/&lt;app&gt;/</c>, which an
    /// SDL3-based port would otherwise be expected to use.
    /// </summary>
    /// <param name="gameType">The game being resolved.</param>
    /// <param name="expectedLeaf">The directory name the engine expects.</param>
    [Theory]
    [InlineData(GameType.ZeroHour, "Command and Conquer Generals Zero Hour Data")]
    [InlineData(GameType.Generals, "Command and Conquer Generals Data")]
    public void MacOSProvider_ResolvesUnderApplicationSupport(GameType gameType, string expectedLeaf)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expected = Path.Combine(home, "Library", "Application Support", expectedLeaf);

        var actual = new MacOSGamePathProvider().GetOptionsDirectory(gameType);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Linux honours XDG_DATA_HOME when it is set.
    /// </summary>
    [Fact]
    public void LinuxProvider_HonoursXdgDataHome()
    {
        var original = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            var custom = Path.Combine(Path.GetTempPath(), "genhub-xdg-probe");
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", custom);

            var actual = new LinuxGamePathProvider().GetOptionsDirectory(GameType.ZeroHour);

            Assert.Equal(
                Path.Combine(custom, "Command and Conquer Generals Zero Hour Data"),
                actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", original);
        }
    }

    /// <summary>
    /// With XDG_DATA_HOME unset, Linux falls back to ~/.local/share, matching the
    /// engine rather than defaulting to the home directory root.
    /// </summary>
    [Fact]
    public void LinuxProvider_FallsBackToLocalShare()
    {
        var original = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var actual = new LinuxGamePathProvider().GetOptionsDirectory(GameType.ZeroHour);

            Assert.Equal(
                Path.Combine(home, ".local", "share", "Command and Conquer Generals Zero Hour Data"),
                actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", original);
        }
    }

    /// <summary>
    /// The Unix providers must not resolve to the home directory root. That is what
    /// <c>SpecialFolder.MyDocuments</c> returns on Unix, and it is what every platform
    /// silently used before <c>IGamePathProvider</c> was registered anywhere.
    /// </summary>
    [Fact]
    public void UnixProviders_DoNotResolveDirectlyUnderHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var badPath = Path.Combine(home, "Command and Conquer Generals Zero Hour Data");

        Assert.NotEqual(badPath, new MacOSGamePathProvider().GetOptionsDirectory(GameType.ZeroHour));
        Assert.NotEqual(badPath, new LinuxGamePathProvider().GetOptionsDirectory(GameType.ZeroHour));
    }
}
