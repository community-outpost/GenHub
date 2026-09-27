using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Linux.Gameinstallations;

/// <summary>
/// Unit tests for <see cref="LutrisInstallation"/>.
/// </summary>
public class LutrisInstallationTests
{
    /// <summary>
    /// Verifies that default property values are set as expected.
    /// </summary>
    [Fact]
    public void Properties_DefaultValues_AreCorrect()
    {
        var installation = new LutrisInstallation(NullLogger<LutrisInstallation>.Instance);

        Assert.Equal(GameInstallationType.Lutris, installation.InstallationType);
        Assert.False(installation.IsLutrisInstalled);
        Assert.False(installation.HasGenerals);
        Assert.False(installation.HasZeroHour);
        Assert.Empty(installation.InstallationPath);
        Assert.Empty(installation.GeneralsPath);
        Assert.Empty(installation.ZeroHourPath);
        Assert.Empty(installation.AvailableGameClients);
    }

    /// <summary>
    /// Verifies that Fetch handles missing executables gracefully without throwing.
    /// </summary>
    [Fact]
    public void Fetch_WhenExecutablesNotFound_DoesNotThrowAndRemainsUninstalled()
    {
        var installation = new LutrisInstallation(
            (_, _) => (false, string.Empty),
            NullLogger<LutrisInstallation>.Instance);

        // Fetch should attempt binary, flatpak, and snap execution without throwing Win32Exception or crashing
        var exception = Record.Exception(() => installation.Fetch());

        Assert.Null(exception);
        Assert.False(installation.IsLutrisInstalled);
    }

    /// <summary>
    /// Verifies that Fetch correctly identifies Lutris installation when detected.
    /// </summary>
    [Fact]
    public void Fetch_WhenLutrisCommandSucceeds_DetectsInstallation()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
        var eaDir = System.IO.Path.Combine(tempDir, "drive_c", "Program Files", "EA Games", "Command and Conquer Generals Zero Hour");
        System.IO.Directory.CreateDirectory(eaDir);

        try
        {
            var installation = new LutrisInstallation(
                (cmd, args) =>
                {
                    if (System.Linq.Enumerable.Contains(args, "-v"))
                    {
                        return (true, "lutris-0.5.14");
                    }

                    if (System.Linq.Enumerable.Contains(args, "-l"))
                    {
                        var json = $"[{{\"slug\": \"ea-app\", \"directory\": \"{tempDir.Replace('\\', '/')}\"}}]";
                        return (true, json);
                    }

                    return (false, string.Empty);
                },
                NullLogger<LutrisInstallation>.Instance);

            installation.Fetch();

            Assert.True(installation.IsLutrisInstalled);
            Assert.Equal("0.5.14", installation.LutrisVersion);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that Fetch correctly identifies Lutris installation via Snap when binary is absent.
    /// </summary>
    [Fact]
    public void Fetch_WhenSnapLutrisCommandSucceeds_DetectsInstallationAndSetsSnapType()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
        var eaDir = System.IO.Path.Combine(tempDir, "drive_c", "Program Files", "EA Games", "Command and Conquer Generals Zero Hour");
        System.IO.Directory.CreateDirectory(eaDir);

        try
        {
            var installation = new LutrisInstallation(
                (cmd, args) =>
                {
                    if (cmd != "snap run lutris")
                    {
                        return (false, string.Empty);
                    }

                    if (System.Linq.Enumerable.Contains(args, "-v"))
                    {
                        return (true, "lutris-0.5.14");
                    }

                    if (System.Linq.Enumerable.Contains(args, "-l"))
                    {
                        var json = $"[{{\"slug\": \"ea-app\", \"directory\": \"{tempDir.Replace('\\', '/')}\"}}]";
                        return (true, json);
                    }

                    return (false, string.Empty);
                },
                NullLogger<LutrisInstallation>.Instance);

            installation.Fetch();

            Assert.True(installation.IsLutrisInstalled);
            Assert.Equal("0.5.14", installation.LutrisVersion);
            Assert.Equal(LinuxInstallationType.Snap, installation.PackageInstallationType);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that SetPaths properly updates paths and flags.
    /// </summary>
    [Fact]
    public void SetPaths_SetsPathsAndFlags()
    {
        var installation = new LutrisInstallation(NullLogger<LutrisInstallation>.Instance);

        installation.SetPaths("/games/generals", "/games/zerohour");

        Assert.True(installation.HasGenerals);
        Assert.Equal("/games/generals", installation.GeneralsPath);
        Assert.True(installation.HasZeroHour);
        Assert.Equal("/games/zerohour", installation.ZeroHourPath);
    }

    /// <summary>
    /// Verifies that PopulateGameClients adds clients to the list.
    /// </summary>
    [Fact]
    public void PopulateGameClients_AddsClients()
    {
        var installation = new LutrisInstallation(NullLogger<LutrisInstallation>.Instance);
        var clients = new List<GameClient>
        {
            new() { Id = "test-client", Name = "Test Client" },
        };

        installation.PopulateGameClients(clients);

        Assert.Single(installation.AvailableGameClients);
        Assert.Equal("test-client", installation.AvailableGameClients[0].Id);
    }
}
