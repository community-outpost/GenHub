using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;

namespace GenHub.Tests.Core.Models.GameInstallations;

/// <summary>
/// Unit tests for <see cref="GameInstallation"/>.
/// </summary>
public class GameInstallationTests
{
    /// <summary>Combined named children supply both games while dedicated paths retain precedence.</summary>
    /// <param name="generalsNamed">Whether the combined child uses the Generals name.</param>
    /// <param name="separateSibling">Whether the other game has its own named directory.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Fetch_CombinedNamedChild_PreservesBothGames(bool generalsNamed, bool separateSibling)
    {
        var root = Directory.CreateTempSubdirectory("GenHub.CombinedChild.").FullName;
        try
        {
            var combinedName = generalsNamed ? GameClientConstants.GeneralsDirectoryName : GameClientConstants.ZeroHourDirectoryName;
            var combined = Directory.CreateDirectory(Path.Combine(root, combinedName)).FullName;
            File.WriteAllText(Path.Combine(combined, "INI.big"), "archive");
            File.WriteAllText(Path.Combine(combined, "INIZH.big"), "archive");
            var sibling = combined;
            if (separateSibling)
            {
                var siblingName = generalsNamed ? GameClientConstants.ZeroHourDirectoryName : GameClientConstants.GeneralsDirectoryName;
                sibling = Directory.CreateDirectory(Path.Combine(root, siblingName)).FullName;
                File.WriteAllText(Path.Combine(sibling, generalsNamed ? "INIZH.big" : "INI.big"), "archive");
            }

            var installation = new GameInstallation(root, GameInstallationType.Retail);
            installation.Fetch();
            Assert.True(installation.HasGenerals);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(generalsNamed ? combined : sibling, installation.GeneralsPath);
            Assert.Equal(generalsNamed ? sibling : combined, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>Combined directories follow platform path case rules.</summary>
    [Fact]
    public void IsCombinedDirectory_UsesPlatformPathCasePolicy()
    {
        var root = Path.GetTempPath();
        var installation = new GameInstallation(root, GameInstallationType.Retail)
        {
            HasGenerals = true,
            HasZeroHour = true,
            GeneralsPath = Path.Combine(root, "CaseTest"),
            ZeroHourPath = Path.Combine(root, "casetest"),
        };
        Assert.Equal(OperatingSystem.IsWindows(), installation.IsCombinedDirectory);
        installation.ZeroHourPath = installation.GeneralsPath + Path.DirectorySeparatorChar;
        Assert.True(installation.IsCombinedDirectory);
    }

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
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.ZeroHourIniBig), string.Empty);

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
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsIniBig), string.Empty);

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
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.ZeroHourIniBig), string.Empty);

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
    /// Verifies that a folder name alone does not identify retail data.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DoesNotDetectRetailData_WhenDirectoryNamedZeroHour()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Command and Conquer Generals Zero Hour_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.False(installation.HasZeroHour);
            Assert.False(installation.HasGenerals);
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
            File.WriteAllText(Path.Combine(generalsDir, GameClientConstants.GeneralsIniBig), string.Empty);

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
    /// Verifies that ZH directory-name tokens do not substitute for retail archives.
    /// </summary>
    /// <param name="dirName">The directory name matching the anchored Zero Hour token.</param>
    [Theory]
    [InlineData("ZH")]
    [InlineData("ZH_Mod")]
    [InlineData("Mod_ZH")]
    [InlineData("ZH-Mod")]
    [InlineData("Mod-ZH")]
    public void GameInstallation_Fetch_DoesNotDetectRetailData_WhenDirectoryMatchesAnchoredZhToken(string dirName)
    {
        var parentDir = Path.Combine(Path.GetTempPath(), "ZhTestParent_" + Guid.NewGuid().ToString("N"));
        var tempDir = Path.Combine(parentDir, dirName);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.False(installation.HasZeroHour);
            Assert.False(installation.HasGenerals);
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
            File.WriteAllText(Path.Combine(zhDir, GameClientConstants.ZeroHourIniBig), string.Empty);

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
    /// Verifies that a client executable alone does not identify retail data.
    /// </summary>
    /// <param name="exeName">The client executable name.</param>
    [Theory]
    [InlineData(GameClientConstants.SuperHackersZeroHourExecutable)]
    [InlineData(GameClientConstants.GeneralsOnlineDefaultExecutable)]
    [InlineData(GameClientConstants.GeneralsOnline60HzExecutable)]
    [InlineData(GameClientConstants.GeneralsOnlineEacLauncherExecutable)]
    [InlineData(GameClientConstants.ContraExecutable)]
    public void GameInstallation_Fetch_DoesNotDetectRetailData_WhenClientExecutablePresent(string exeName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenericRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, exeName), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.False(installation.HasZeroHour);
            Assert.False(installation.HasGenerals);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that archive identity takes precedence over the directory name.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsGenerals_WhenNamedZeroHourAndIniBigPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Command and Conquer Generals Zero Hour_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsIniBig), string.Empty);

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
    /// Verifies that Fetch identifies a generic directory containing both generic INI.big and a Zero Hour archive signature as a combined installation.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsBothGames_WhenGenericRootContainsIniBigAndZhArchive()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenericRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsIniBig), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, "RussianZH.BIG"), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.HasZeroHour);
            Assert.Equal(tempDir, installation.ZeroHourPath);
            Assert.True(installation.HasGenerals);
            Assert.Equal(tempDir, installation.GeneralsPath);
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
            File.WriteAllText(Path.Combine(zhDir, GameClientConstants.ZeroHourIniBig), string.Empty);

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
            File.WriteAllText(Path.Combine(customZhDir, GameClientConstants.ZeroHourIniBig), string.Empty);
            File.WriteAllText(Path.Combine(standardZhDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(standardZhDir, GameClientConstants.ZeroHourIniBig), string.Empty);

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
    /// Verifies that Fetch does not detect Zero Hour when files with ZH suffixes are not .big archives (e.g. .txt, .bak).
    /// </summary>
    /// <param name="nonArchiveName">The non-big filename containing the ZH.big suffix.</param>
    [Theory]
    [InlineData("SpeechEnglishZH.big.txt")]
    [InlineData("RussianZH.big.bak")]
    [InlineData("GermanZH.big.log")]
    [InlineData("ZH.big.zip")]
    public void GameInstallation_Fetch_DoesNotDetectZeroHour_WhenNonBigFileHasZhSuffix(string nonArchiveName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NonArchiveZhTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsExecutable), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, nonArchiveName), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsIniBig), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.False(installation.HasZeroHour);
            Assert.True(installation.HasGenerals);
            Assert.Equal(tempDir, installation.GeneralsPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch does not detect Zero Hour when .big archives do not end with the Zero Hour suffix.
    /// </summary>
    /// <param name="bigArchiveName">The standard Generals big archive filename.</param>
    [Theory]
    [InlineData("Speech.big")]
    [InlineData("Music.big")]
    [InlineData("Textures.big")]
    [InlineData("Generals.big")]
    public void GameInstallation_Fetch_DoesNotDetectZeroHour_WhenNonZhBigArchivesPresent(string bigArchiveName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "NonZhBigTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsExecutable), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, bigArchiveName), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsIniBig), string.Empty);

            var installation = new GameInstallation(tempDir, GameInstallationType.Retail, NullLogger<GameInstallation>.Instance);
            installation.Fetch();

            Assert.False(installation.HasZeroHour);
            Assert.True(installation.HasGenerals);
            Assert.Equal(tempDir, installation.GeneralsPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that Fetch detects Zero Hour regardless of casing in the ZH.big archive filename.
    /// </summary>
    /// <param name="casedArchiveName">The localized Zero Hour archive filename with varying casing.</param>
    [Theory]
    [InlineData("speechenglishzh.big")]
    [InlineData("SPEECHENGLISHZH.BIG")]
    [InlineData("russian_ZH.BiG")]
    [InlineData("GermanZH.BIG")]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenCasingVariesInZhArchive(string casedArchiveName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CaseVaryingZhTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsExecutable), string.Empty);
            File.WriteAllText(Path.Combine(tempDir, casedArchiveName), string.Empty);

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
    /// Verifies that Fetch detects Zero Hour correctly when a valid ZH.big archive is present alongside numerous unrelated files.
    /// </summary>
    [Fact]
    public void GameInstallation_Fetch_DetectsZeroHour_WhenZhArchivePresentAmongManyFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MixedFilesZhTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsExecutable), string.Empty);
            for (var i = 0; i < 20; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"data_{i}.dat"), string.Empty);
                File.WriteAllText(Path.Combine(tempDir, $"texture_{i}.tga"), string.Empty);
                File.WriteAllText(Path.Combine(tempDir, $"config_{i}.ini"), string.Empty);
            }

            File.WriteAllText(Path.Combine(tempDir, "SpeechEnglishZH.big"), string.Empty);

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
            File.WriteAllText(Path.Combine(customGenDir, GameClientConstants.GeneralsIniBig), string.Empty);
            File.WriteAllText(Path.Combine(standardGenDir, "generals.exe"), string.Empty);
            File.WriteAllText(Path.Combine(standardGenDir, GameClientConstants.GeneralsIniBig), string.Empty);

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
            File.WriteAllText(Path.Combine(generalsPath, GameClientConstants.GeneralsIniBig), "archive");
            var zeroHourPath = Directory.CreateDirectory(Path.Combine(tempDir, "zerohour")).FullName;
            File.WriteAllText(Path.Combine(zeroHourPath, GameClientConstants.ZeroHourIniBig), "archive");

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
            File.WriteAllText(Path.Combine(zeroHourOnly, GameClientConstants.ZeroHourIniBig), "archive");
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
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsIniBig), "archive");
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.ZeroHourIniBig), "archive");

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
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.GeneralsIniBig), "archive");
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.ZeroHourIniBig), "archive");

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
            File.WriteAllText(Path.Combine(tempDir, GameClientConstants.ZeroHourIniBig), "archive");
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
            File.WriteAllText(Path.Combine(generalsDir, GameClientConstants.GeneralsIniBig), "archive");
            var zeroHourDir = Directory.CreateDirectory(Path.Combine(tempDir, "Command and Conquer Generals Zero Hour")).FullName;
            File.WriteAllText(Path.Combine(zeroHourDir, GameClientConstants.ZeroHourIniBig), "archive");

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

    /// <summary>
    /// Verifies that BundledGeneralsPath returns the ZH_Generals directory path when it exists and contains archives.
    /// </summary>
    [Fact]
    public void GameInstallation_BundledGeneralsPath_ReturnsPath_WhenZhGeneralsDirectoryExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubBundledZhTest_" + Guid.NewGuid().ToString("N"));
        var zhGeneralsDir = Path.Combine(tempDir, GameClientConstants.ZhGeneralsDirectory);
        Directory.CreateDirectory(zhGeneralsDir);
        try
        {
            File.WriteAllText(Path.Combine(zhGeneralsDir, "Textures.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, tempDir);

            Assert.Equal(zhGeneralsDir, installation.BundledGeneralsPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that BundledGeneralsPath returns null when the ZH_Generals directory exists but contains no archives.
    /// </summary>
    [Fact]
    public void GameInstallation_BundledGeneralsPath_ReturnsNull_WhenZhGeneralsDirectoryEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubEmptyBundledZhTest_" + Guid.NewGuid().ToString("N"));
        var zhGeneralsDir = Path.Combine(tempDir, GameClientConstants.ZhGeneralsDirectory);
        Directory.CreateDirectory(zhGeneralsDir);
        try
        {
            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, tempDir);

            Assert.Null(installation.BundledGeneralsPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that BundledGeneralsPath returns null when the ZH_Generals directory does not exist.
    /// </summary>
    [Fact]
    public void GameInstallation_BundledGeneralsPath_ReturnsNull_WhenZhGeneralsDirectoryDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubNoBundledZhTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, tempDir);

            Assert.Null(installation.BundledGeneralsPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that BundledGeneralsPath returns the ZH_Generals directory path when it exists but
    /// cannot be read, so launch validation reports the permission problem instead of treating the
    /// bundled archives as absent and starting Zero Hour without its base content.
    /// </summary>
    [Fact]
    public void GameInstallation_BundledGeneralsPath_ReturnsPath_WhenZhGeneralsDirectoryUnreadable()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubUnreadableBundledZhTest_" + Guid.NewGuid().ToString("N"));
        var zhGeneralsDir = Path.Combine(tempDir, GameClientConstants.ZhGeneralsDirectory);
        Directory.CreateDirectory(zhGeneralsDir);
        File.WriteAllText(Path.Combine(zhGeneralsDir, "Textures.big"), "archive");

        // Revoking traversal on the parent makes the child unstatable as well as unreadable,
        // which is exactly the case Directory.Exists misreports as missing.
        File.SetUnixFileMode(tempDir, UnixFileMode.UserWrite);
        try
        {
            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, tempDir);

            Assert.Equal(zhGeneralsDir, installation.BundledGeneralsPath);
        }
        finally
        {
            File.SetUnixFileMode(
                tempDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that EffectiveGeneralsArchivePath prefers GeneralsPath over BundledGeneralsPath when both exist.
    /// </summary>
    [Fact]
    public void GameInstallation_EffectiveGeneralsArchivePath_PrefersGeneralsPath_WhenBothPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubEffectivePrefersGenTest_" + Guid.NewGuid().ToString("N"));
        var generalsDir = Path.Combine(tempDir, "Generals");
        var zhDir = Path.Combine(tempDir, "ZeroHour");
        var zhGeneralsDir = Path.Combine(zhDir, GameClientConstants.ZhGeneralsDirectory);
        Directory.CreateDirectory(generalsDir);
        Directory.CreateDirectory(zhGeneralsDir);
        File.WriteAllText(Path.Combine(zhGeneralsDir, "Textures.big"), "archive");
        try
        {
            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(generalsDir, zhDir);

            Assert.NotNull(installation.BundledGeneralsPath);
            Assert.Equal(generalsDir, installation.EffectiveGeneralsArchivePath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that EffectiveGeneralsArchivePath falls back to BundledGeneralsPath when GeneralsPath is null or empty.
    /// </summary>
    [Fact]
    public void GameInstallation_EffectiveGeneralsArchivePath_FallsBackToBundledGeneralsPath_WhenGeneralsPathNullOrEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubEffectiveFallbackTest_" + Guid.NewGuid().ToString("N"));
        var zhGeneralsDir = Path.Combine(tempDir, GameClientConstants.ZhGeneralsDirectory);
        Directory.CreateDirectory(zhGeneralsDir);
        try
        {
            File.WriteAllText(Path.Combine(zhGeneralsDir, "Textures.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, tempDir);

            Assert.Equal(zhGeneralsDir, installation.EffectiveGeneralsArchivePath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that BundledGeneralsPath resolves the ZH_Generals directory even when its
    /// on-disk casing differs, which case-sensitive filesystems would otherwise miss.
    /// </summary>
    [Fact]
    public void GameInstallation_BundledGeneralsPath_ReturnsPath_WhenZhGeneralsDirectoryCaseDiffers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubCaseVariantBundledZhTest_" + Guid.NewGuid().ToString("N"));
        var zhGeneralsDir = Path.Combine(tempDir, GameClientConstants.ZhGeneralsDirectory.ToLowerInvariant());
        Directory.CreateDirectory(zhGeneralsDir);
        try
        {
            File.WriteAllText(Path.Combine(zhGeneralsDir, "Textures.big"), "archive");

            var installation = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            installation.SetPaths(null, tempDir);

            Assert.True(
                string.Equals(zhGeneralsDir, installation.BundledGeneralsPath, StringComparison.OrdinalIgnoreCase),
                $"Expected {zhGeneralsDir} but got {installation.BundledGeneralsPath ?? "(null)"}.");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that the IGameInstallation default implementations resolve the bundled and
    /// effective Generals paths through the shared helpers. A bare implementer is used on
    /// purpose: casting a GameInstallation would dispatch to its own overrides and never
    /// exercise the defaults.
    /// </summary>
    [Fact]
    public void IGameInstallation_DefaultImplementations_ResolvePathsThroughSharedHelpers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GenHubInterfaceParityTest_" + Guid.NewGuid().ToString("N"));
        var zhGeneralsDir = Path.Combine(tempDir, GameClientConstants.ZhGeneralsDirectory);
        Directory.CreateDirectory(zhGeneralsDir);
        try
        {
            File.WriteAllText(Path.Combine(zhGeneralsDir, "Textures.big"), "archive");

            IGameInstallation installation = new BareInstallation(tempDir);
            var concrete = new GameInstallation(tempDir, GameInstallationType.Steam, NullLogger<GameInstallation>.Instance);
            concrete.SetPaths(null, tempDir);

            Assert.Equal(zhGeneralsDir, installation.BundledGeneralsPath);
            Assert.Equal(zhGeneralsDir, installation.EffectiveGeneralsArchivePath);
            Assert.Equal(concrete.BundledGeneralsPath, installation.BundledGeneralsPath);
            Assert.Equal(concrete.EffectiveGeneralsArchivePath, installation.EffectiveGeneralsArchivePath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class BareInstallation(string zeroHourPath) : IGameInstallation
    {
        /// <inheritdoc />
        public string Id => "bare";

        /// <inheritdoc />
        public GameInstallationType InstallationType => GameInstallationType.Steam;

        /// <inheritdoc />
        public string InstallationPath => string.Empty;

        /// <inheritdoc />
        public bool HasGenerals => false;

        /// <inheritdoc />
        public string GeneralsPath => string.Empty;

        /// <inheritdoc />
        public bool HasZeroHour => true;

        /// <inheritdoc />
        public string ZeroHourPath => zeroHourPath;

        /// <inheritdoc />
        public List<GameClient> AvailableGameClients => [];

        /// <inheritdoc />
        public void Fetch()
        {
        }

        /// <inheritdoc />
        public void SetPaths(string? generalsPath, string? zeroHourPath)
        {
        }

        /// <inheritdoc />
        public void PopulateGameClients(IEnumerable<GameClient> clients)
        {
        }
    }
}
