using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace GenHub.Tests.Windows.Gameinstallations;

/// <summary>
/// Unit tests for <see cref="WindowsInstallationDetector"/>.
/// </summary>
public class WindowsInstallationDetectorTests
{
    /// <summary>
    /// Verifies the detector name is correct.
    /// </summary>
    [Fact]
    public void DetectorName_IsCorrect()
    {
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        Assert.Equal("Windows Installation Detector", detector.DetectorName);
    }

    /// <summary>
    /// Verifies CanDetectOnCurrentPlatform returns a bool.
    /// </summary>
    [Fact]
    public void CanDetectOnCurrentPlatform_IsBool()
    {
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        Assert.IsType<bool>(detector.CanDetectOnCurrentPlatform);
    }

    /// <summary>
    /// Verifies DetectInstallationsAsync returns a detection result.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DetectInstallationsAsync_ReturnsDetectionResultAsync()
    {
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        var result = await detector.DetectInstallationsAsync();
        Assert.NotNull(result);
        Assert.True(result.Success || !result.Success); // Always true, just checks method runs
    }

    /// <summary>Retail discovery keeps parent grouping and accepts archives without executables.</summary>
    /// <param name="underEaParent">Whether both games live under the EA Games parent.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DetectRetailInstallations_WithArchivesOnly_DetectsBothGames(bool underEaParent)
    {
        var root = Directory.CreateTempSubdirectory("GenHub.RetailDiscovery.").FullName;
        try
        {
            var parent = underEaParent ? Path.Combine(root, GameClientConstants.EaGamesParentDirectoryName) : root;
            var generals = Directory.CreateDirectory(Path.Combine(parent, GameClientConstants.GeneralsRetailDirectoryName)).FullName;
            var zeroHour = Directory.CreateDirectory(Path.Combine(parent, GameClientConstants.ZeroHourRetailDirectoryName)).FullName;
            File.WriteAllText(Path.Combine(generals, GameClientConstants.GeneralsIniBig), "archive");
            File.WriteAllText(Path.Combine(zeroHour, GameClientConstants.ZeroHourIniBig), "archive");
            var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
            var method = typeof(WindowsInstallationDetector).GetMethod("DetectRetailInstallations", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var result = (List<GameInstallation>)method.Invoke(detector, [root, Path.Combine(root, "unused")])!;
            Assert.Equal(underEaParent ? 1 : 2, result.Count);
            Assert.Equal(generals, Assert.Single(result, i => i.HasGenerals).GeneralsPath);
            Assert.Equal(zeroHour, Assert.Single(result, i => i.HasZeroHour).ZeroHourPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A partial higher-priority detection cannot hide the other game in a combined root.</summary>
    /// <param name="generalsClaimed">Whether the earlier detection claimed Generals rather than Zero Hour.</param>
    /// <param name="trailingSeparator">Whether the combined source includes a trailing separator.</param>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void DeduplicateInstallations_PartiallyClaimedCombinedRoot_IsRetained(bool generalsClaimed, bool trailingSeparator)
    {
        var directory = Path.GetFullPath("combined-installation");
        var partial = new GameInstallation(directory, GameInstallationType.Steam)
        {
            HasGenerals = generalsClaimed, HasZeroHour = !generalsClaimed,
            GeneralsPath = generalsClaimed ? directory : string.Empty,
            ZeroHourPath = generalsClaimed ? string.Empty : directory,
        };
        var combined = new GameInstallation(directory, GameInstallationType.Retail)
        {
            HasGenerals = true, HasZeroHour = true, GeneralsPath = trailingSeparator ? directory + Path.DirectorySeparatorChar : directory,
            ZeroHourPath = directory,
        };
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        var method = typeof(WindowsInstallationDetector).GetMethod("DeduplicateInstallations", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (List<GameInstallation>)method.Invoke(detector, [new List<GameInstallation> { partial, combined }])!;
        Assert.Same(partial, Assert.Single(result));
        Assert.True(partial.HasGenerals);
        Assert.True(partial.HasZeroHour);
    }

    /// <summary>Retaining a combined root must not discard a distinct game directory.</summary>
    [Fact]
    public void DeduplicateInstallations_SharedRootWithDistinctSibling_PreservesSibling()
    {
        var directory = Path.GetFullPath("combined-installation");
        var siblingDirectory = Path.GetFullPath("other-zero-hour");
        var split = new GameInstallation(directory, GameInstallationType.Steam)
        {
            HasGenerals = true, HasZeroHour = true,
            GeneralsPath = directory, ZeroHourPath = siblingDirectory,
        };
        var combined = new GameInstallation(directory, GameInstallationType.Retail)
        {
            HasGenerals = true, HasZeroHour = true, GeneralsPath = directory, ZeroHourPath = directory,
        };
        var detector = new WindowsInstallationDetector(NullLogger<WindowsInstallationDetector>.Instance);
        var method = typeof(WindowsInstallationDetector).GetMethod("DeduplicateInstallations", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = (List<GameInstallation>)method.Invoke(detector, [new List<GameInstallation> { split, combined }])!;
        Assert.Equal(2, result.Count);
        Assert.Same(combined, Assert.Single(result, i => i.HasGenerals));
        Assert.False(split.HasGenerals);
        Assert.Equal(siblingDirectory, split.ZeroHourPath);
    }
}
