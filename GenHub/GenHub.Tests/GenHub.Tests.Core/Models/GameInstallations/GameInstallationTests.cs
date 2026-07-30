using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Models.GameInstallations;

/// <summary>
/// Unit tests for <see cref="GameInstallation"/>.
/// </summary>
public class GameInstallationTests
{
    /// <summary>
    /// Verifies that default values are set correctly.
    /// </summary>
    [Fact]
    public void GameInstallation_Defaults_AreSet()
    {
        var tempPath = Path.GetTempPath();
        var installation = new GameInstallation(tempPath, GameInstallationType.Unknown, NullLogger<GameInstallation>.Instance);

        Assert.False(string.IsNullOrEmpty(installation.Id));
        Assert.Equal(GameInstallationType.Unknown, installation.InstallationType);
        Assert.Equal(tempPath, installation.InstallationPath);
        Assert.False(installation.HasGenerals);
        Assert.Equal(string.Empty, installation.GeneralsPath);
        Assert.False(installation.HasZeroHour);
        Assert.Equal(string.Empty, installation.ZeroHourPath);
        Assert.True((DateTime.UtcNow - installation.DetectedAt).TotalSeconds < 5);
    }

    /// <summary>
    /// Verifies IsValid returns true when no games are installed.
    /// </summary>
    [Fact]
    public void GameInstallation_IsValid_ReturnsTrue_WhenNoGamesInstalled()
    {
        var installation = new GameInstallation(string.Empty, GameInstallationType.Unknown, NullLogger<GameInstallation>.Instance);

        Assert.True(installation.IsValid);
    }

    /// <summary>
    /// Verifies IsValid returns false when Generals path is missing/non-existent.
    /// </summary>
    [Fact]
    public void GameInstallation_IsValid_ReturnsFalse_WhenGeneralsPathMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); // Non-existent path
        var installation = new GameInstallation(string.Empty, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
        installation.SetPaths(missingPath, null);
        installation.HasGenerals = true; // Force HasGenerals to true to test path existence

        Assert.False(installation.IsValid);
    }

    /// <summary>
    /// Verifies IsValid returns true when the Generals installation path exists.
    /// </summary>
    [Fact]
    public void GameInstallation_IsValid_ReturnsTrue_WhenGeneralsPathExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var generalsPath = Path.Combine(tempDir, "Command and Conquer Generals");
            Directory.CreateDirectory(generalsPath);

            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(generalsPath, null);
            Assert.True(installation.IsValid);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// SetPaths flags each game from its retail archives: a directory holding the
    /// canonical Generals set is Generals, one holding *zh.big archives is Zero Hour.
    /// </summary>
    [Fact]
    public void SetPaths_FlagsGamesFromArchivePresence()
    {
        var tempDir = Directory.CreateTempSubdirectory("GenHub.SetPathsTests.").FullName;
        try
        {
            var generalsPath = Directory.CreateDirectory(Path.Combine(tempDir, "generals")).FullName;
            File.WriteAllText(Path.Combine(generalsPath, "INI.big"), "archive");
            var zeroHourPath = Directory.CreateDirectory(Path.Combine(tempDir, "zerohour")).FullName;
            File.WriteAllText(Path.Combine(zeroHourPath, "INIZH.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(generalsPath, zeroHourPath);

            Assert.True(installation.HasGenerals);
            Assert.True(installation.HasZeroHour);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// A directory holding only the other game's archives must not flag: a Zero Hour
    /// directory passed as the Generals path is not a Generals installation, and an
    /// executable name proves nothing either way.
    /// </summary>
    [Fact]
    public void SetPaths_DirectoryWithWrongGamesArchives_DoesNotFlag()
    {
        var tempDir = Directory.CreateTempSubdirectory("GenHub.SetPathsTests.").FullName;
        try
        {
            var zeroHourOnly = Directory.CreateDirectory(Path.Combine(tempDir, "zh")).FullName;
            File.WriteAllText(Path.Combine(zeroHourOnly, "INIZH.big"), "archive");
            File.WriteAllText(Path.Combine(zeroHourOnly, "generals.exe"), "binary");

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(zeroHourOnly, null);

            Assert.False(installation.HasGenerals);
            Assert.Equal(zeroHourOnly, installation.GeneralsPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// A combined directory passed as both paths sets both flags to the same directory —
    /// one installation, both games, per the issue's acceptance criteria.
    /// </summary>
    [Fact]
    public void SetPaths_CombinedDirectory_FlagsBothGames()
    {
        var tempDir = Directory.CreateTempSubdirectory("GenHub.SetPathsTests.").FullName;
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "INI.big"), "archive");
            File.WriteAllText(Path.Combine(tempDir, "INIZH.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(tempDir, tempDir);

            Assert.True(installation.HasGenerals);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(installation.GeneralsPath, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Fetch on a flat root holding both games' archives yields both paths set to the
    /// root. The earlier executable-based scan had to guess in this layout because both
    /// games ship the same executable name.
    /// </summary>
    [Fact]
    public void Fetch_FlatCombinedRoot_FlagsBothGamesAtRoot()
    {
        var tempDir = Directory.CreateTempSubdirectory("GenHub.FetchTests.").FullName;
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "INI.big"), "archive");
            File.WriteAllText(Path.Combine(tempDir, "INIZH.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasGenerals);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.GeneralsPath);
            Assert.Equal(tempDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Fetch prefers the standard subdirectories when they hold archives, and a flat
    /// Zero Hour-only root no longer reads as Generals too.
    /// </summary>
    [Fact]
    public void Fetch_ZeroHourOnlyRoot_DoesNotFlagGenerals()
    {
        var tempDir = Directory.CreateTempSubdirectory("GenHub.FetchTests.").FullName;
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "INIZH.big"), "archive");
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), "binary");

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
            Assert.False(installation.HasGenerals);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Fetch finds each game in its standard subdirectory by that game's archives.
    /// </summary>
    [Fact]
    public void Fetch_StandardSubdirectories_FlagsEachGameInItsDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory("GenHub.FetchTests.").FullName;
        try
        {
            var generalsDir = Directory.CreateDirectory(Path.Combine(tempDir, "Command and Conquer Generals")).FullName;
            File.WriteAllText(Path.Combine(generalsDir, "INI.big"), "archive");
            var zeroHourDir = Directory.CreateDirectory(Path.Combine(tempDir, "Command and Conquer Generals Zero Hour")).FullName;
            File.WriteAllText(Path.Combine(zeroHourDir, "INIZH.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasGenerals);
            Assert.Equal(generalsDir, installation.GeneralsPath);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(zeroHourDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// A root holding only unrecognised archives — mod content — must not read as a game.
    /// </summary>
    [Fact]
    public void Fetch_ModArchivesOnlyRoot_FlagsNothing()
    {
        var tempDir = Directory.CreateTempSubdirectory("GenHub.FetchTests.").FullName;
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "somemod.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.False(installation.HasGenerals);
            Assert.False(installation.HasZeroHour);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}