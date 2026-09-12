using GenHub.Core.Models.Enums;
using GenHub.Linux.GameInstallations;
using GenHub.Tests.Linux.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Linux.Gameinstallations;

/// <summary>
/// Unit tests for <see cref="SteamInstallation"/>.
/// </summary>
[Collection(ApplicationCompositionCollection.Name)]
public class SteamInstallationTests
{
    /// <summary>
    /// Verifies InstallationType is Steam.
    /// </summary>
    [Fact]
    public void InstallationType_IsSteam()
    {
        var installation = new SteamInstallation(NullLogger<SteamInstallation>.Instance);
        Assert.Equal(GameInstallationType.Steam, installation.InstallationType);
    }

    /// <summary>
    /// Verifies Fetch method runs without exception.
    /// </summary>
    [Fact]
    public void Fetch_RunsWithoutException()
    {
        var installation = new SteamInstallation(NullLogger<SteamInstallation>.Instance);
        var exception = Record.Exception(() => installation.Fetch());
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies constructor with fetch parameter.
    /// </summary>
    [Fact]
    public void Constructor_WithFetch_RunsWithoutException()
    {
        var exception = Record.Exception(() => new SteamInstallation(true, NullLogger<SteamInstallation>.Instance));
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies SetPaths sets Generals and Zero Hour paths properly.
    /// </summary>
    [Fact]
    public void SetPaths_SetsGeneralsAndZeroHourPaths()
    {
        var installation = new SteamInstallation(NullLogger<SteamInstallation>.Instance);
        installation.SetPaths("/home/user/games/Generals", "/home/user/games/ZeroHour");

        Assert.True(installation.HasGenerals);
        Assert.Equal("/home/user/games/Generals", installation.GeneralsPath);
        Assert.True(installation.HasZeroHour);
        Assert.Equal("/home/user/games/ZeroHour", installation.ZeroHourPath);
    }

    /// <summary>
    /// Verifies PopulateGameClients adds clients to AvailableGameClients.
    /// </summary>
    [Fact]
    public void PopulateGameClients_AddsClientsSuccessfully()
    {
        var installation = new SteamInstallation(NullLogger<SteamInstallation>.Instance);
        var clients = new[]
        {
            new GenHub.Core.Models.GameClients.GameClient { Id = "test-client-1", Name = "Client 1" },
        };

        installation.PopulateGameClients(clients);

        Assert.Single(installation.AvailableGameClients);
        Assert.Equal("test-client-1", installation.AvailableGameClients[0].Id);
    }

    /// <summary>
    /// Verifies Fetch detects Flatpak Steam game installations from mock home directory.
    /// </summary>
    [Fact]
    public void Fetch_WithFlatpakSteamDirectory_DetectsGameInstallation()
    {
        var tempHome = Path.Combine(Path.GetTempPath(), "genhub_test_home_" + Guid.NewGuid().ToString("N"));
        var originalHome = Environment.GetEnvironmentVariable("HOME");

        try
        {
            var gameDir = Path.Combine(
                tempHome,
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common",
                GenHub.Core.Constants.GameClientConstants.ZeroHourDirectoryNameAmpersandHyphen);

            Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(gameDir, "generals.exe"), "mock exe content");

            Environment.SetEnvironmentVariable("HOME", tempHome);

            var installation = new SteamInstallation(NullLogger<SteamInstallation>.Instance);
            installation.Fetch();

            Assert.True(installation.IsSteamInstalled);
            Assert.True(installation.HasZeroHour);
            Assert.Equal(gameDir, installation.ZeroHourPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", originalHome);
            if (Directory.Exists(tempHome))
            {
                Directory.Delete(tempHome, true);
            }
        }
    }
}