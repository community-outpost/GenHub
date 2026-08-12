using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.MacOS.GameInstallations;

namespace GenHub.Tests.MacOS.GameInstallations;

/// <summary>
/// Tests macOS installation detection result semantics.
/// </summary>
public class MacOSInstallationDetectorTests
{
    /// <summary>
    /// Verifies that finding an installation does not turn an incomplete scan into
    /// a cacheable success.
    /// </summary>
    [Fact]
    public void CreateDetectionResult_WithInstallationAndDeniedRoot_ReturnsFailure()
    {
        var installation = new GameInstallation(
            "/readable",
            GameInstallationType.Retail,
            null);

        var result = MacOSInstallationDetector.CreateDetectionResult(
            [installation],
            ["/denied"],
            TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Empty(result.Items);
        Assert.Contains("installation detection is incomplete", result.Errors.Single());
    }

    /// <summary>
    /// Verifies that a complete scan retains the installations it found.
    /// </summary>
    [Fact]
    public void CreateDetectionResult_WithoutDeniedRoot_ReturnsSuccess()
    {
        var installation = new GameInstallation(
            "/readable",
            GameInstallationType.Retail,
            null);
        var elapsed = TimeSpan.FromSeconds(1);

        var result = MacOSInstallationDetector.CreateDetectionResult(
            [installation],
            [],
            elapsed);

        Assert.True(result.Success);
        Assert.Same(installation, Assert.Single(result.Items));
        Assert.Equal(elapsed, result.Elapsed);
    }

    /// <summary>
    /// A candidate root that is itself a flat retail tree — the native engine's default
    /// deploy layout — is detected directly, without being a name-matched child of
    /// anything. This is the layout the executable-name check made undetectable: its
    /// binary is extensionless, but its archives are unambiguous.
    /// </summary>
    [Fact]
    public void InspectRoot_FlatCombinedRoot_DetectsBothGamesAtRoot()
    {
        var root = Directory.CreateTempSubdirectory("GenHub.MacDetector.").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "INI.big"), "archive");
            File.WriteAllText(Path.Combine(root, "INIZH.big"), "archive");

            var (installation, accessDenied) = MacOSInstallationDetector.InspectRoot(root);

            Assert.False(accessDenied);
            Assert.NotNull(installation);
            Assert.True(installation.HasGenerals);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(root, installation.GeneralsPath);
            Assert.Equal(root, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A flat root holding only Zero Hour archives is a Zero Hour installation alone.
    /// </summary>
    [Fact]
    public void InspectRoot_FlatZeroHourRoot_DetectsZeroHourOnly()
    {
        var root = Directory.CreateTempSubdirectory("GenHub.MacDetector.").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "INIZH.big"), "archive");

            var (installation, _) = MacOSInstallationDetector.InspectRoot(root);

            Assert.NotNull(installation);
            Assert.True(installation.HasZeroHour);
            Assert.False(installation.HasGenerals);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Name-matched children still work for copied retail trees that keep their Windows
    /// directory names, with the games flagged from the archives each child holds.
    /// </summary>
    [Fact]
    public void InspectRoot_NameMatchedChildren_DetectsGamesFromArchives()
    {
        var root = Directory.CreateTempSubdirectory("GenHub.MacDetector.").FullName;
        try
        {
            var generalsDir = Directory.CreateDirectory(Path.Combine(root, "Command and Conquer Generals")).FullName;
            File.WriteAllText(Path.Combine(generalsDir, "INI.big"), "archive");
            var zeroHourDir = Directory.CreateDirectory(Path.Combine(root, "Command and Conquer Generals Zero Hour")).FullName;
            File.WriteAllText(Path.Combine(zeroHourDir, "INIZH.big"), "archive");

            var (installation, _) = MacOSInstallationDetector.InspectRoot(root);

            Assert.NotNull(installation);
            Assert.True(installation.HasGenerals);
            Assert.Equal(generalsDir, installation.GeneralsPath);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(zeroHourDir, installation.ZeroHourPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A name-matched child without retail archives is discarded: the right directory
    /// name proves nothing about content.
    /// </summary>
    [Fact]
    public void InspectRoot_NameMatchedChildWithoutArchives_FindsNothing()
    {
        var root = Directory.CreateTempSubdirectory("GenHub.MacDetector.").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Command and Conquer Generals Zero Hour"));

            var (installation, accessDenied) = MacOSInstallationDetector.InspectRoot(root);

            Assert.False(accessDenied);
            Assert.Null(installation);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A root holding only unrecognised archives — mod content — must not read as a game.
    /// </summary>
    [Fact]
    public void InspectRoot_ModArchivesOnlyRoot_FindsNothing()
    {
        var root = Directory.CreateTempSubdirectory("GenHub.MacDetector.").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "somemod.big"), "archive");

            var (installation, _) = MacOSInstallationDetector.InspectRoot(root);

            Assert.Null(installation);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// A nonexistent root finds nothing and is not access-denied.
    /// </summary>
    [Fact]
    public void InspectRoot_NonexistentRoot_FindsNothing()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var (installation, accessDenied) = MacOSInstallationDetector.InspectRoot(missing);

        Assert.Null(installation);
        Assert.False(accessDenied);
    }
}
