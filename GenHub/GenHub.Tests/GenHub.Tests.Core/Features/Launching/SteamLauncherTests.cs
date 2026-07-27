using GenHub.Core.Constants;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Filesystem tests for <see cref="SteamLauncher"/>.
/// </summary>
public sealed class SteamLauncherTests : IDisposable
{
    private const string ExecutableName = "genhub-test-game.exe";
    private readonly string _tempDirectory;
    private readonly string _gameInstallPath;
    private readonly string _workspacePath;
    private readonly string _originalExecutablePath;
    private readonly string _workspaceExecutablePath;
    private readonly string _proxySourcePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamLauncherTests"/> class.
    /// </summary>
    public SteamLauncherTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "GenHub-SteamLauncherTests",
            Guid.NewGuid().ToString("N"));
        _gameInstallPath = Path.Combine(_tempDirectory, "game");
        _workspacePath = Path.Combine(_tempDirectory, "workspace");
        _originalExecutablePath = Path.Combine(_gameInstallPath, ExecutableName);
        _workspaceExecutablePath = Path.Combine(_workspacePath, "workspace-game.exe");
        _proxySourcePath = Path.Combine(_tempDirectory, SteamConstants.ProxyLauncherFileName);

        Directory.CreateDirectory(_gameInstallPath);
        Directory.CreateDirectory(_workspacePath);
        File.WriteAllText(_originalExecutablePath, "original executable");
        File.WriteAllText(_workspaceExecutablePath, "workspace executable");
        File.WriteAllText(_proxySourcePath, "proxy executable");
    }

    /// <summary>
    /// Verifies a successful preparation deploys the proxy and preserves existing files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_ValidPaths_DeploysProxyAndPreservesExistingDependencies()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        var workspaceDependencyPath = Path.Combine(_workspacePath, "binkw32.dll");
        File.WriteAllText(Path.Combine(_gameInstallPath, "steam_api.dll"), "steam api");
        File.WriteAllText(Path.Combine(_gameInstallPath, "binkw32.dll"), "installation dependency");
        File.WriteAllText(workspaceDependencyPath, "pre-existing workspace dependency");

        // Act
        var result = await PrepareAsync(CreateLauncher(), steamAppId: "12345");

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.Equal("proxy executable", File.ReadAllText(_originalExecutablePath));
        Assert.Equal("original executable", File.ReadAllText(backupPath));
        Assert.True(File.Exists(Path.Combine(_gameInstallPath, "proxy_config.json")));
        Assert.Equal("12345", File.ReadAllText(Path.Combine(_gameInstallPath, "steam_appid.txt")));
        Assert.Equal("12345", File.ReadAllText(Path.Combine(_workspacePath, "steam_appid.txt")));
        Assert.Equal("steam api", File.ReadAllText(Path.Combine(_workspacePath, "steam_api.dll")));
        Assert.Equal("pre-existing workspace dependency", File.ReadAllText(workspaceDependencyPath));
        Assert.Empty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Verifies invalid workspace input is rejected before the game executable is changed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_MissingWorkspaceExecutable_DoesNotMutateInstallation()
    {
        // Arrange
        var missingExecutable = Path.Combine(_workspacePath, "missing.exe");

        // Act
        var result = await PrepareAsync(
            CreateLauncher(),
            targetExecutablePath: missingExecutable);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("original executable", File.ReadAllText(_originalExecutablePath));
        Assert.False(File.Exists(_originalExecutablePath + SteamConstants.BackupExtension));
        Assert.False(File.Exists(Path.Combine(_gameInstallPath, "proxy_config.json")));
        Assert.Empty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Verifies a late write failure restores files changed by the current attempt.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_LateWriteFailure_RestoresOriginalAndPreExistingFiles()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        var configPath = Path.Combine(_gameInstallPath, "proxy_config.json");
        var workspaceAppIdPath = Path.Combine(_workspacePath, "steam_appid.txt");
        File.WriteAllText(_originalExecutablePath, "proxy executable");
        File.WriteAllText(backupPath, "pre-existing original executable");
        File.WriteAllText(configPath, "pre-existing config");
        File.WriteAllText(workspaceAppIdPath, "pre-existing app id");

        var writeCount = 0;
        async Task FailingWriter(string path, string contents, CancellationToken cancellationToken)
        {
            writeCount++;
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            if (writeCount == 3)
            {
                throw new IOException("Injected late write failure.");
            }
        }

        // Act
        var result = await PrepareAsync(CreateLauncher(FailingWriter), steamAppId: "12345");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Injected late write failure", result.AllErrors);
        Assert.Equal("pre-existing original executable", File.ReadAllText(_originalExecutablePath));
        Assert.Equal("pre-existing original executable", File.ReadAllText(backupPath));
        Assert.Equal("pre-existing config", File.ReadAllText(configPath));
        Assert.Equal("pre-existing app id", File.ReadAllText(workspaceAppIdPath));
        Assert.Empty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Verifies an uncertain pre-existing backup is preserved but not used to overwrite the current executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_UnrelatedPreExistingBackup_RestoresPreAttemptExecutable()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        File.WriteAllText(_originalExecutablePath, "current executable");
        File.WriteAllText(backupPath, "stale pre-existing backup");

        async Task FailingWriter(string path, string contents, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            throw new IOException("Injected write failure.");
        }

        // Act
        var result = await PrepareAsync(CreateLauncher(FailingWriter));

        // Assert
        Assert.False(result.Success);
        Assert.Equal("current executable", File.ReadAllText(_originalExecutablePath));
        Assert.Equal("stale pre-existing backup", File.ReadAllText(backupPath));
        Assert.Empty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Verifies cancellation after executable replacement restores the original executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_CanceledAfterMutation_RollsBackCurrentAttempt()
    {
        // Arrange
        using var cancellationSource = new CancellationTokenSource();
        var writeCount = 0;

        async Task CancelingWriter(string path, string contents, CancellationToken cancellationToken)
        {
            writeCount++;
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            if (writeCount == 2)
            {
                cancellationSource.Cancel();
            }
        }

        // Act
        var result = await PrepareAsync(
            CreateLauncher(CancelingWriter),
            steamAppId: "12345",
            cancellationToken: cancellationSource.Token);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("canceled", result.AllErrors, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("original executable", File.ReadAllText(_originalExecutablePath));
        Assert.False(File.Exists(_originalExecutablePath + SteamConstants.BackupExtension));
        Assert.False(File.Exists(Path.Combine(_gameInstallPath, "proxy_config.json")));
        Assert.Empty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Verifies rollback reports an unsafe executable conflict and retains recovery copies.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_ExecutableChangesDuringFailure_ReportsRollbackConflict()
    {
        // Arrange
        async Task ConflictingWriter(string path, string contents, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            File.WriteAllText(_originalExecutablePath, "external executable change");
            throw new IOException("Injected write failure.");
        }

        // Act
        var result = await PrepareAsync(CreateLauncher(ConflictingWriter));

        // Assert
        Assert.False(result.Success);
        Assert.Contains("did not overwrite unexpectedly changed executable", result.AllErrors);
        Assert.Equal("external executable change", File.ReadAllText(_originalExecutablePath));
        Assert.Equal(
            "original executable",
            File.ReadAllText(_originalExecutablePath + SteamConstants.BackupExtension));
        Assert.NotEmpty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Deletes the temporary test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private SteamLauncher CreateLauncher(
        Func<string, string, CancellationToken, Task>? writer = null)
    {
        var logger = new Mock<ILogger<SteamLauncher>>();
        return new SteamLauncher(
            logger.Object,
            _proxySourcePath,
            writer ?? File.WriteAllTextAsync);
    }

    private Task<OperationResult<SteamLaunchPrepResult>> PrepareAsync(
        SteamLauncher launcher,
        string? targetExecutablePath = null,
        string? steamAppId = null,
        CancellationToken cancellationToken = default)
    {
        return launcher.PrepareForProfileAsync(
            _gameInstallPath,
            "test-profile",
            Array.Empty<ContentManifest>(),
            ExecutableName,
            targetExecutablePath ?? _workspaceExecutablePath,
            _workspacePath,
            steamAppId: steamAppId,
            cancellationToken: cancellationToken);
    }

    private string[] GetRollbackArtifacts()
    {
        return Directory.GetFiles(
            _tempDirectory,
            "*.genhub-rollback-*",
            SearchOption.AllDirectories);
    }
}
