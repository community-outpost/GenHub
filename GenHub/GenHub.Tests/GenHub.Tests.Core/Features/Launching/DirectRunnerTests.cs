using GenHub.Core.Models.Launching;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for <see cref="DirectRunner"/>.
/// </summary>
public sealed class DirectRunnerTests
{
    private readonly DirectRunner _runner = new(NullLogger<DirectRunner>.Instance);

    /// <summary>
    /// Verifies the direct runner always reports launch availability.
    /// </summary>
    [Fact]
    public void CanLaunchWindowsExecutables_ReturnsTrue()
    {
        Assert.True(_runner.CanLaunchWindowsExecutables());
    }

    /// <summary>
    /// Verifies resolution passes the executable through untouched.
    /// </summary>
    [Fact]
    public void ResolveCommand_ReturnsDirectCommand()
    {
        // Arrange
        const string executablePath = "/games/zerohour/generals.exe";
        var configuration = new GameLaunchConfiguration { ExecutablePath = executablePath };

        // Act
        var result = _runner.ResolveCommand(configuration);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.NotNull(result.Data);
        Assert.Equal(executablePath, result.Data.FileName);
        Assert.Equal(string.Empty, result.Data.ArgumentPrefix);
        Assert.Empty(result.Data.EnvironmentVariables);
    }

    /// <summary>
    /// Verifies macOS bundles fail launch resolution on Windows and Linux.
    /// </summary>
    [Fact]
    public void ResolveCommand_MacOsTarget_FailsWithCompatibilityError()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        // Arrange
        var configuration = new GameLaunchConfiguration { ExecutablePath = "/games/ZeroHour.app" };

        // Act
        var result = _runner.ResolveCommand(configuration);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("macOS", result.FirstError);
    }

    /// <summary>
    /// Verifies Flatpak bundles fail launch resolution with install-then-run guidance
    /// naming the bundle.
    /// </summary>
    [Fact]
    public void ResolveCommand_LinuxFlatpakTarget_FailsWithCompatibilityError()
    {
        // Arrange
        var configuration = new GameLaunchConfiguration { ExecutablePath = "/games/zerohour.flatpak" };

        // Act
        var result = _runner.ResolveCommand(configuration);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Flatpak", result.FirstError);
        Assert.Contains("/games/zerohour.flatpak", result.FirstError);
    }
}
