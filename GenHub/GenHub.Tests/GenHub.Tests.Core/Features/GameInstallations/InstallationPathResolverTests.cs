using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Features.GameInstallations;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

    /// <summary>
    /// Verifies that ResolveInstallationPathAsync preserves DetectedAt, Id, and DisplayName from the original installation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveInstallationPathAsync_WhenResolved_PreservesOriginalDetectedAtAndMetadata()
    {
        var targetSearchDir = Path.Combine(_tempDirectory, "SearchRoot");
        var resolvedGameDir = Path.Combine(targetSearchDir, "DiscoveredGame");
        Directory.CreateDirectory(resolvedGameDir);
        File.WriteAllText(Path.Combine(resolvedGameDir, GameClientConstants.GeneralsExecutable), "dummy-exe");

        var pathProvider = new TestSearchPathProvider(targetSearchDir);
        var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance, pathProvider);

        var stalePath = Path.Combine(_tempDirectory, "StalePath");
        var originalTimestamp = new DateTime(2023, 5, 12, 10, 30, 0, DateTimeKind.Utc);
        var originalInstallation = new GameInstallation(stalePath, GameInstallationType.Retail)
        {
            Id = "test-installation-id",
            DisplayName = "Test Custom Display Name",
            DetectedAt = originalTimestamp,
        };

        // Model an installation detected while its path was still valid: the Has flags
        // persist on the stale record even though the directories are gone.
        originalInstallation.HasGenerals = true;

        var result = await resolver.ResolveInstallationPathAsync(originalInstallation);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(resolvedGameDir, result.Data.InstallationPath);
        Assert.Equal(originalInstallation.Id, result.Data.Id);
        Assert.Equal(originalInstallation.DisplayName, result.Data.DisplayName);
        Assert.Equal(originalTimestamp, result.Data.DetectedAt);
    }

    /// <summary>
    /// Verifies that ResolveInstallationPathAsync correctly maps Zero Hour and Generals subdirectories using defined constants.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveInstallationPathAsync_WhenSubdirectoriesExist_ResolvesPathsWithConstants()
    {
        var targetSearchDir = Path.Combine(_tempDirectory, "SearchRootSub");
        var resolvedGameDir = Path.Combine(targetSearchDir, "DiscoveredGameSub");
        var generalsDir = Path.Combine(resolvedGameDir, GameClientConstants.GeneralsSubdirectoryName);
        var zhDir = Path.Combine(resolvedGameDir, GameClientConstants.ZeroHourSubdirectoryName);
        Directory.CreateDirectory(generalsDir);
        Directory.CreateDirectory(zhDir);
        File.WriteAllText(Path.Combine(generalsDir, GameClientConstants.GeneralsExecutable), "dummy-exe");
        File.WriteAllText(Path.Combine(zhDir, GameClientConstants.ZeroHourExecutable), "dummy-exe");
        File.WriteAllText(Path.Combine(resolvedGameDir, GameClientConstants.GeneralsExecutable), "dummy-exe");

        var pathProvider = new TestSearchPathProvider(targetSearchDir);
        var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance, pathProvider);

        var stalePath = Path.Combine(_tempDirectory, "StalePathSub");
        var originalInstallation = new GameInstallation(stalePath, GameInstallationType.Retail);
        originalInstallation.SetPaths(Path.Combine(stalePath, "Generals"), Path.Combine(stalePath, "ZeroHour"));

        // Model an installation detected while its path was still valid: SetPaths against
        // the nonexistent stale directories leaves both flags false, so raise them to
        // reflect the persisted detection state.
        originalInstallation.HasGenerals = true;
        originalInstallation.HasZeroHour = true;

        var result = await resolver.ResolveInstallationPathAsync(originalInstallation);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(Directory.Exists(result.Data.GeneralsPath));
        Assert.True(Directory.Exists(result.Data.ZeroHourPath));
        Assert.Equal(generalsDir, result.Data.GeneralsPath, ignoreCase: true);
        Assert.Equal(zhDir, result.Data.ZeroHourPath, ignoreCase: true);
        Assert.True(result.Data.HasGenerals);
        Assert.True(result.Data.HasZeroHour);
    }

    /// <summary>
    /// Verifies that SearchForInstallationAsync propagates OperationCanceledException when cancelled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SearchForInstallationAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Steam);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _resolver.SearchForInstallationAsync(installation, null, cts.Token));
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync accepts Zero Hour edition-specific executables in game subdirectories.
    /// </summary>
    /// <param name="exeName">The executable name to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("generalszh.exe")]
    [InlineData("generals.ctr")]
    public async Task ValidateInstallationPathAsync_WhenSubdirectoryHasZeroHourEditionExecutable_ReturnsTrue(string exeName)
    {
        var zhPath = Path.Combine(_tempDirectory, "ZeroHourEdition_" + Path.GetFileNameWithoutExtension(exeName));
        Directory.CreateDirectory(zhPath);
        File.WriteAllText(Path.Combine(zhPath, exeName), "mock executable content");

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Retail);
        installation.SetPaths(null, zhPath);

        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync accepts Generals edition-specific executables in game subdirectories.
    /// </summary>
    /// <param name="exeName">The executable name to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("generalsv.exe")]
    [InlineData("generals.exe")]
    public async Task ValidateInstallationPathAsync_WhenSubdirectoryHasGeneralsEditionExecutable_ReturnsTrue(string exeName)
    {
        var generalsPath = Path.Combine(_tempDirectory, "GeneralsEdition_" + Path.GetFileNameWithoutExtension(exeName));
        Directory.CreateDirectory(generalsPath);
        File.WriteAllText(Path.Combine(generalsPath, exeName), "mock executable content");

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Retail);
        installation.SetPaths(generalsPath, null);

        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync returns false when Generals subdirectory only contains a Zero Hour executable.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateInstallationPathAsync_WhenGeneralsSubdirectoryOnlyHasZeroHourExecutable_ReturnsFalse()
    {
        var generalsPath = Path.Combine(_tempDirectory, "GeneralsWrongExe");
        Directory.CreateDirectory(generalsPath);
        File.WriteAllText(Path.Combine(generalsPath, GameClientConstants.SuperHackersZeroHourExecutable), "wrong edition exe");

        var installation = new GameInstallation(_tempDirectory, GameInstallationType.Retail);
        installation.SetPaths(generalsPath, null);

        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Verifies that ResolveInstallationPathAsync propagates OperationCanceledException when cancelled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveInstallationPathAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var nonExistentPath = Path.Combine(_tempDirectory, "nonexistent");
        var installation = new GameInstallation(nonExistentPath, GameInstallationType.Steam);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _resolver.ResolveInstallationPathAsync(installation, cts.Token));
    }

    /// <summary>
    /// Verifies that ResolveInstallationPathAsync correctly maps Zero Hour and Generals subdirectories
    /// even when the directory names on disk differ in casing from defined constants.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveInstallationPathAsync_WhenSubdirectoriesHaveDifferentCasing_ResolvesPathsCaseInsensitively()
    {
        var targetSearchDir = Path.Combine(_tempDirectory, "SearchRootCasing");
        var resolvedGameDir = Path.Combine(targetSearchDir, "DiscoveredGameCasing");
        var generalsDir = Path.Combine(resolvedGameDir, "generals");
        var zhDir = Path.Combine(resolvedGameDir, "command and conquer generals zero hour");
        Directory.CreateDirectory(generalsDir);
        Directory.CreateDirectory(zhDir);
        File.WriteAllText(Path.Combine(generalsDir, GameClientConstants.GeneralsExecutable), "dummy-exe");
        File.WriteAllText(Path.Combine(zhDir, GameClientConstants.ZeroHourExecutable), "dummy-exe");
        File.WriteAllText(Path.Combine(resolvedGameDir, GameClientConstants.GeneralsExecutable), "dummy-exe");

        var pathProvider = new TestSearchPathProvider(targetSearchDir);
        var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance, pathProvider);

        var stalePath = Path.Combine(_tempDirectory, "StalePathCasing");
        var originalInstallation = new GameInstallation(stalePath, GameInstallationType.Retail)
        {
            HasGenerals = true,
            HasZeroHour = true,
        };

        var result = await resolver.ResolveInstallationPathAsync(originalInstallation);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(Directory.Exists(result.Data.GeneralsPath));
        Assert.True(Directory.Exists(result.Data.ZeroHourPath));
        Assert.Equal(generalsDir, result.Data.GeneralsPath, ignoreCase: true);
        Assert.Equal(zhDir, result.Data.ZeroHourPath, ignoreCase: true);
        Assert.True(result.Data.HasGenerals);
        Assert.True(result.Data.HasZeroHour);
    }

    /// <summary>
    /// Verifies that ResolveInstallationPathAsync falls back to resolvedPath when subdirectories exist but lack game executables.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveInstallationPathAsync_WhenSubdirectoriesLackExecutable_FallsBackToResolvedPath()
    {
        var targetSearchDir = Path.Combine(_tempDirectory, "SearchRootNoSubExe");
        var resolvedGameDir = Path.Combine(targetSearchDir, "DiscoveredGameNoSubExe");
        var generalsDir = Path.Combine(resolvedGameDir, GameClientConstants.GeneralsSubdirectoryName);
        var zhDir = Path.Combine(resolvedGameDir, GameClientConstants.ZeroHourSubdirectoryName);
        Directory.CreateDirectory(generalsDir);
        Directory.CreateDirectory(zhDir);
        File.WriteAllText(Path.Combine(resolvedGameDir, GameClientConstants.GeneralsExecutable), "dummy-exe");

        var pathProvider = new TestSearchPathProvider(targetSearchDir);
        var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance, pathProvider);

        var stalePath = Path.Combine(_tempDirectory, "StalePathNoSubExe");
        var originalInstallation = new GameInstallation(stalePath, GameInstallationType.Retail)
        {
            HasGenerals = true,
            HasZeroHour = true,
        };

        var result = await resolver.ResolveInstallationPathAsync(originalInstallation);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(resolvedGameDir, result.Data.GeneralsPath);
        Assert.Equal(resolvedGameDir, result.Data.ZeroHourPath);
    }

    /// <summary>
    /// Verifies that SearchForInstallationAsync does not adopt an unrelated directory that only contains
    /// a generic game.exe executable without corroborating Generals files or hash match.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SearchForInstallationAsync_WhenGenericGameExeWithoutCorroboratingFiles_DoesNotMatchGenerals()
    {
        var targetSearchDir = Path.Combine(_tempDirectory, "SearchRootGeneric");
        var unrelatedGameDir = Path.Combine(targetSearchDir, "UnrelatedGame");
        Directory.CreateDirectory(unrelatedGameDir);
        File.WriteAllText(Path.Combine(unrelatedGameDir, GameClientConstants.GameExecutable), "not-generals");

        var pathProvider = new TestSearchPathProvider(targetSearchDir);
        var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance, pathProvider);

        var installation = new GameInstallation(Path.Combine(_tempDirectory, "StaleGenerals"), GameInstallationType.Retail)
        {
            HasGenerals = true,
        };

        var result = await resolver.SearchForInstallationAsync(installation);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that SearchForInstallationAsync adopts a directory with a generic executable
    /// when corroborating Generals signature files are present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SearchForInstallationAsync_WhenGenericGameExeWithCorroboratingBigFile_MatchesGenerals()
    {
        var targetSearchDir = Path.Combine(_tempDirectory, "SearchRootCorroborated");
        var generalsDir = Path.Combine(targetSearchDir, "GeneralsFolder");
        Directory.CreateDirectory(generalsDir);
        File.WriteAllText(Path.Combine(generalsDir, GameClientConstants.GameExecutable), "dummy-exe");
        File.WriteAllText(Path.Combine(generalsDir, GameClientConstants.GeneralsIniBig), "dummy-big");

        var pathProvider = new TestSearchPathProvider(targetSearchDir);
        var resolver = new InstallationPathResolver(NullLogger<InstallationPathResolver>.Instance, pathProvider);

        var installation = new GameInstallation(Path.Combine(_tempDirectory, "StaleGenerals"), GameInstallationType.Retail)
        {
            HasGenerals = true,
        };

        var result = await resolver.SearchForInstallationAsync(installation);

        Assert.True(result.Success);
        Assert.Equal(generalsDir, result.Data);
    }

    /// <summary>
    /// Verifies that ValidateInstallationPathAsync returns false when both HasGenerals and HasZeroHour are false,
    /// even if the root directory contains an executable.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateInstallationPathAsync_WhenBothGeneralsAndZeroHourFalse_ReturnsFalseEvenWithRootExecutable()
    {
        var gameDir = Path.Combine(_tempDirectory, "HollowInstallation");
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(Path.Combine(gameDir, GameClientConstants.GameExecutable), "binary");

        var installation = new GameInstallation(gameDir, GameInstallationType.Retail);

        // HasGenerals and HasZeroHour default to false
        var result = await _resolver.ValidateInstallationPathAsync(installation);

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    private sealed class TestSearchPathProvider(string searchPath) : IInstallationSearchPathProvider
    {
        public IReadOnlyList<string> GetSearchPaths(GameInstallationType installationType)
        {
            return [searchPath];
        }
    }
}
