using GenHub.Core.Constants;
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
    /// Verifies that Fetch correctly identifies a standalone Zero Hour installation by its INIZH.big archive.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsStandaloneZeroHour_WhenZeroHourBigsPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubZHTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "INIZH.big"), string.Empty);

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
    /// Verifies that Fetch correctly identifies a standalone Generals installation by its INI.big archive.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsStandaloneGenerals_WhenGeneralsBigsPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubGenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "INI.big"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasGenerals);
            Assert.Equal(tempDir, installation.GeneralsPath);
            Assert.False(installation.HasZeroHour);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch correctly identifies a merged installation containing both Generals and Zero Hour archives.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsMergedInstall_WhenBothBigsPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubMergedTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "gensec.big"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "INIZH.big"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasGenerals);
            Assert.Equal(tempDir, installation.GeneralsPath);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch correctly identifies Zero Hour based on folder name when specific archives are absent.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenDirectoryNamedZeroHour()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Command and Conquer Generals Zero Hour_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch does not misclassify a vanilla Generals installation when parent path contains ZH text.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DoesNotMisclassifyGenerals_WhenParentPathContainsZh()
    {
        var parentDir = Path.Combine(Path.GetTempPath(), "ZH_Tools_" + Guid.NewGuid().ToString("N"));
        var generalsDir = Path.Combine(parentDir, "Generals");
        Directory.CreateDirectory(generalsDir);
        try
        {
            File.WriteAllText(Path.Combine(generalsDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(generalsDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasGenerals);
            Assert.Equal(generalsDir, installation.GeneralsPath);
            Assert.False(installation.HasZeroHour);
        }
        finally
        {
            Directory.Delete(parentDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch identifies Zero Hour for leaf directories matching anchored ZH tokens.
    /// </summary>
    /// <param name="dirName">The directory name matching the anchored Zero Hour token.</param>
    [Theory]
    [InlineData("ZH")]
    [InlineData("ZH_Mod")]
    [InlineData("Mod_ZH")]
    [InlineData("ZH-Mod")]
    [InlineData("Mod-ZH")]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenDirectoryMatchesAnchoredZhToken(string dirName)
    {
        var parentDir = Path.Combine(Path.GetTempPath(), "ZhTestParent_" + Guid.NewGuid().ToString("N"));
        var tempDir = Path.Combine(parentDir, dirName);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(parentDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch detects Zero Hour from supported subdirectories under a parent installation path.
    /// </summary>
    /// <param name="subDirName">The subdirectory name under the installation root.</param>
    [Theory]
    [InlineData(GameClientConstants.ZeroHourDirectoryName)]
    [InlineData(GameClientConstants.ZeroHourDirectoryNameAmpersandHyphen)]
    [InlineData(GameClientConstants.ZeroHourRetailDirectoryName)]
    [InlineData(GameClientConstants.ZeroHourDirectoryNameAbbreviated)]
    public void GameInstallation_Fetch_DetectsZeroHour_FromSupportedSubdirectory(string subDirName)
    {
        var parentDir = Path.Combine(Path.GetTempPath(), "GamesParent_" + Guid.NewGuid().ToString("N"));
        var zhDir = Path.Combine(parentDir, subDirName);
        Directory.CreateDirectory(zhDir);
        try
        {
            File.WriteAllText(Path.Combine(zhDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(parentDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(zhDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(parentDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch detects Zero Hour based on archive signatures like PatchZH.big.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenPatchZhArchivePresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenericRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.ZeroHourPatchBig), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch detects Generals Vanilla based on Patch.big archive signature.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsGenerals_WhenPatchArchivePresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenericRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsPatchBig), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasGenerals);
            Assert.Equal(tempDir, installation.GeneralsPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch detects Zero Hour when client-specific executables like generalszh.exe or generalsonlinezh_60.exe are present.
    /// </summary>
    /// <param name="exeName">The client executable name.</param>
    [Theory]
    [InlineData(GameClientConstants.SuperHackersZeroHourExecutable)]
    [InlineData(GameClientConstants.GeneralsOnlineDefaultExecutable)]
    [InlineData(GameClientConstants.GeneralsOnline60HzExecutable)]
    [InlineData(GameClientConstants.GeneralsOnlineEacLauncherExecutable)]
    [InlineData(GameClientConstants.ContraExecutable)]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenClientExecutablePresent(string exeName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenericRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, exeName), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch identifies a directory named Zero Hour as Zero Hour even if generic INI.big is present (repack scenario).
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenNamedZeroHourAndIniBigPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Command and Conquer Generals Zero Hour_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "INI.big"), string.Empty);

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
    /// Verifies that Fetch detects Zero Hour when non-English localized archives like RussianZH.big or GermanZH.big are present.
    /// </summary>
    /// <param name="archiveName">The localized Zero Hour archive filename.</param>
    [Theory]
    [InlineData("RussianZH.big")]
    [InlineData("RussianZH.BIG")]
    [InlineData("GermanZH.big")]
    [InlineData("GermanZH.Big")]
    [InlineData("FrenchZH.big")]
    [InlineData("AudioZH.big")]
    [InlineData("MapsZH.BIG")]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenLocalizedZhBigArchivePresent(string archiveName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenericRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, archiveName), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch identifies a generic directory containing both generic INI.big and a Zero Hour archive signature as Zero Hour only.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsOnlyZeroHour_WhenGenericRootContainsIniBigAndZhArchive()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenericRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "INI.big"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "RussianZH.BIG"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
            Assert.False(installation.HasGenerals);
            Assert.True(string.IsNullOrEmpty(installation.GeneralsPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch preserves explicitly configured paths when those paths exist on disk.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_PreservesExplicitlyConfiguredPaths()
    {
        var tempParent = Path.Combine(Path.GetTempPath(), "ExplicitTest_" + Guid.NewGuid().ToString("N"));
        var zhDir = Path.Combine(tempParent, "ZH_Custom");
        Directory.CreateDirectory(zhDir);
        try
        {
            File.WriteAllText(Path.Combine(zhDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(tempParent, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, zhDir);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(zhDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempParent, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch preserves explicitly configured paths even when a standard supported subdirectory also exists.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_PreservesExplicitlyConfiguredPaths_EvenWhenStandardSubdirectoriesExist()
    {
        var tempParent = Path.Combine(Path.GetTempPath(), "ExplicitSubdirTest_" + Guid.NewGuid().ToString("N"));
        var customZhDir = Path.Combine(tempParent, "ZH_Custom");
        var standardZhDir = Path.Combine(tempParent, GameClientConstants.ZeroHourDirectoryName);
        Directory.CreateDirectory(customZhDir);
        Directory.CreateDirectory(standardZhDir);
        try
        {
            File.WriteAllText(Path.Combine(customZhDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(standardZhDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(tempParent, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, customZhDir);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(customZhDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(tempParent, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch preserves explicitly configured Generals paths even when a standard supported subdirectory also exists.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_PreservesExplicitlyConfiguredGeneralsPath_EvenWhenStandardSubdirectoriesExist()
    {
        var tempParent = Path.Combine(Path.GetTempPath(), "ExplicitGenSubdirTest_" + Guid.NewGuid().ToString("N"));
        var customGenDir = Path.Combine(tempParent, "Generals_Custom");
        var standardGenDir = Path.Combine(tempParent, GameClientConstants.GeneralsDirectoryName);
        Directory.CreateDirectory(customGenDir);
        Directory.CreateDirectory(standardGenDir);
        try
        {
            File.WriteAllText(Path.Combine(customGenDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(standardGenDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(tempParent, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(customGenDir, null);
            installation.Fetch();

            Assert.True(installation.HasGenerals);
            Assert.Equal(customGenDir, installation.GeneralsPath);
        }
        finally
        {
            Directory.Delete(tempParent, true);
        }
    }
}
