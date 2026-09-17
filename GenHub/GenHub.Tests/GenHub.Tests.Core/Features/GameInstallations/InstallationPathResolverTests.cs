using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GameInstallations;

/// <summary>
/// Unit tests for <see cref="InstallationPathResolver"/>.
/// </summary>
public sealed class InstallationPathResolverTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly InstallationPathResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationPathResolverTests"/> class.
    /// </summary>
    public InstallationPathResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_PathResolverTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync returns false when the installation directory does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateInstallationPathAsync_WhenPathDoesNotExist_ReturnsFalse()
    {
        var nonExistentPath = Path.Combine(_tempDirectory, "does_not_exist");
        var installation = new GameInstallation(nonExistentPath, GameInstallationType.Steam);

        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync returns true for Steam Zero Hour installations containing game.dat.
    /// </summary>
    /// <param name="exeName">The executable name to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("game.dat")]
    [InlineData("GAME.DAT")]
    [InlineData("Game.Dat")]
    public async Task ValidateInstallationPathAsync_WhenZeroHourHasSteamGameDat_ReturnsTrue(string exeName)
    {
        var zhPath = Path.Combine(_tempDirectory, "ZeroHour");
        Directory.CreateDirectory(zhPath);
        File.WriteAllText(Path.Combine(zhPath, exeName), "mock executable content");

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Steam);
        installation.SetPaths(null, zhPath);

        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync returns true for installations containing generals.exe.
    /// </summary>
    /// <param name="exeName">The executable name to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("generals.exe")]
    [InlineData("Generals.exe")]
    [InlineData("GENERALS.EXE")]
    public async Task ValidateInstallationPathAsync_WhenGeneralsHasGeneralsExe_ReturnsTrue(string exeName)
    {
        var generalsPath = Path.Combine(_tempDirectory, "Generals");
        Directory.CreateDirectory(generalsPath);
        File.WriteAllText(Path.Combine(generalsPath, exeName), "mock executable content");

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Retail);
        installation.SetPaths(generalsPath, null);

        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync returns false when no recognized executables exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateInstallationPathAsync_WhenNoValidExecutableExists_ReturnsFalse()
    {
        var generalsPath = Path.Combine(_tempDirectory, "Generals");
        Directory.CreateDirectory(generalsPath);
        File.WriteAllText(Path.Combine(generalsPath, "unrelated_file.txt"), "some text");

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Steam);
        installation.SetPaths(generalsPath, null);

        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Verifies that ResolveInstallationPathAsync immediately returns success when path is already valid.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveInstallationPathAsync_WhenAlreadyValid_ReturnsSameInstallation()
    {
        var zhPath = Path.Combine(_tempDirectory, "ZeroHour");
        Directory.CreateDirectory(zhPath);
        File.WriteAllText(Path.Combine(zhPath, GameClientConstants.SteamGameDatExecutable), "binary");

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Steam);
        installation.SetPaths(null, zhPath);

        var result = await _resolver.ResolveInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.Same(installation, result.Data);
    }
}
