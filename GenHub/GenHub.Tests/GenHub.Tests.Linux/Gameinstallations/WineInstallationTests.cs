using GenHub.Core.Models.Enums;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Linux.Gameinstallations;

/// <summary>
/// Unit tests for <see cref="WineInstallation"/>.
/// </summary>
public class WineInstallationTests
{
    /// <summary>
    /// Verifies InstallationType is Wine.
    /// </summary>
    [Fact]
    public void InstallationType_IsWine()
    {
        var installation = new WineInstallation(NullLogger<WineInstallation>.Instance);
        Assert.Equal(GameInstallationType.Wine, installation.InstallationType);
    }

    /// <summary>
    /// Verifies Fetch method runs without exception.
    /// </summary>
    [Fact]
    public void Fetch_RunsWithoutException()
    {
        var installation = new WineInstallation(NullLogger<WineInstallation>.Instance);
        var exception = Record.Exception(() => installation.Fetch());
        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies constructor with fetch parameter.
    /// </summary>
    [Fact]
    public void Constructor_WithFetch_RunsWithoutException()
    {
        var exception = Record.Exception(() => new WineInstallation(true, NullLogger<WineInstallation>.Instance));
        Assert.Null(exception);
    }
}