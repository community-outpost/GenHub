using GenHub.Core.Models.Enums;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Linux.Gameinstallations;

/// <summary>
/// Unit tests for <see cref="SteamInstallation"/>.
/// </summary>
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
}