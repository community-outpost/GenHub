using GenHub.Core.Constants;
using GenHub.ProxyLauncher;
using System.Reflection;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="SteamConstants"/> and parity with <see cref="ProxyConstants"/>.
/// </summary>
public class SteamConstantsTests
{
    /// <summary>
    /// Tests that all Steam constants have expected values.
    /// </summary>
    [Fact]
    public void SteamConstants_Constants_ShouldHaveExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.Equal("17300", SteamConstants.GeneralsAppId);
            Assert.Equal("2732960", SteamConstants.ZeroHourAppId);
            Assert.Equal("steam://rungameid/", SteamConstants.RunGameIdUrlPrefix);
            Assert.Equal(".genhub-files.json", SteamConstants.TrackingFileName);
            Assert.Equal(".genhub-backup", SteamConstants.BackupDirName);
            Assert.Equal(FileTypes.BackupExtension, SteamConstants.BackupExtension);
            Assert.Equal(".ghbak", SteamConstants.BackupExtension);
            Assert.Equal("GenHub.ProxyLauncher.exe", SteamConstants.ProxyLauncherFileName);
            Assert.Equal("GenHub.ProxyLauncher.dll", SteamConstants.ProxyLauncherDllFileName);
            Assert.Equal("GenHub.ProxyLauncher", SteamConstants.ProxyLauncherName);
            Assert.Equal("GenHub", SteamConstants.AppNameToken);
            Assert.Equal("Proxy", SteamConstants.ProxyDescriptionToken);
            Assert.Equal("Steam", SteamConstants.SteamDirectoryName);
            Assert.Equal("steamapps", SteamConstants.SteamAppsDirectoryName);
            Assert.Equal("common", SteamConstants.CommonDirectoryName);
        });
    }

    /// <summary>
    /// Enforces constant parity between <see cref="SteamConstants.BackupExtension"/> and
    /// <see cref="ProxyConstants.BackupExtension"/>.
    /// Since the single-file proxy launcher deliberately has no project reference to GenHub.Core,
    /// this test prevents silent constant drift between the two components.
    /// </summary>
    [Fact]
    public void BackupExtension_ShouldMatchProxyConstantsBackupExtension()
    {
        // Direct compile-time and value parity check
        Assert.Equal(SteamConstants.BackupExtension, ProxyConstants.BackupExtension);
        Assert.Equal(FileTypes.BackupExtension, ProxyConstants.BackupExtension);

        // Reflection parity check to ensure the field remains present and identical on ProxyConstants
        var proxyField = typeof(ProxyConstants).GetField(
            nameof(ProxyConstants.BackupExtension),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(proxyField);
        Assert.Equal(SteamConstants.BackupExtension, proxyField.GetValue(null));
    }
}
