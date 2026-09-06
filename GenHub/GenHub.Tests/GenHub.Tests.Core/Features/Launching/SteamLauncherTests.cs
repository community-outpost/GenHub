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
            $"GenHub-SteamLauncherTests-{Guid.NewGuid():N}");
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
    public async Task PrepareForProfileAsync_ValidPaths_DeploysProxyAndPreservesExistingDependenciesAsync()
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
    public async Task PrepareForProfileAsync_MissingWorkspaceExecutable_DoesNotMutateInstallationAsync()
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
    /// Verifies preparation can recover when a prior crash left only the executable backup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_MissingTargetWithBackup_DeploysProxyFromRecoveryStateAsync()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        File.Move(_originalExecutablePath, backupPath);

        // Act
        var result = await PrepareAsync(CreateLauncher());

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.Equal("proxy executable", File.ReadAllText(_originalExecutablePath));
        Assert.Equal("original executable", File.ReadAllText(backupPath));
        Assert.Empty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Verifies a late write failure restores files changed by the current attempt.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_LateWriteFailure_RestoresOriginalAndPreExistingFilesAsync()
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
        async Task FailingWriterAsync(string path, string contents, CancellationToken cancellationToken)
        {
            writeCount++;
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            if (writeCount == 3)
            {
                throw new IOException("Injected late write failure.");
            }
        }

        // Act
        var result = await PrepareAsync(CreateLauncher(FailingWriterAsync), steamAppId: "12345");

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
    /// Verifies preparation refreshes a stale backup from the genuine game executable and deploys the proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_WhenTargetIsGenuineAndBackupExists_RefreshesBackupAndDeploysProxyAsync()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        File.WriteAllText(_originalExecutablePath, "current executable");
        File.WriteAllText(backupPath, "stale pre-existing backup");

        // Act
        var result = await PrepareAsync(CreateLauncher());

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.Equal("proxy executable", File.ReadAllText(_originalExecutablePath));
        Assert.Equal("current executable", File.ReadAllText(backupPath));
        Assert.True(File.Exists(Path.Combine(_gameInstallPath, "proxy_config.json")));
        Assert.Empty(GetRollbackArtifacts());
    }

    /// <summary>
    /// Verifies cleanup preserves genuine executable, removes stale backup and proxy artifacts, and succeeds.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CleanupGameDirectoryAsync_WhenTargetIsGenuineAndBackupExists_CleansArtifactsAndPreservesExecutableAsync()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        var configPath = Path.Combine(_gameInstallPath, "proxy_config.json");
        File.WriteAllText(backupPath, "stale pre-existing backup");
        File.WriteAllText(configPath, "pre-existing config");

        // Act
        var result = await CreateLauncher().CleanupGameDirectoryAsync(
            _gameInstallPath,
            ExecutableName);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.Equal("original executable", File.ReadAllText(_originalExecutablePath));
        Assert.False(File.Exists(backupPath));
        Assert.False(File.Exists(configPath));
    }

    /// <summary>
    /// Verifies cleanup removes identical duplicate backup and succeeds.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CleanupGameDirectoryAsync_IdenticalBackup_DeletesDuplicateBackupAndSucceedsAsync()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        File.WriteAllText(backupPath, "original executable");

        // Act
        var result = await CreateLauncher().CleanupGameDirectoryAsync(
            _gameInstallPath,
            ExecutableName);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.Equal("original executable", File.ReadAllText(_originalExecutablePath));
        Assert.False(File.Exists(backupPath));
    }

    /// <summary>
    /// Verifies cleanup restores a backup only when the installed executable is the known proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CleanupGameDirectoryAsync_DeployedProxy_RestoresVerifiedBackupAsync()
    {
        // Arrange
        var backupPath = _originalExecutablePath + SteamConstants.BackupExtension;
        var configPath = Path.Combine(_gameInstallPath, "proxy_config.json");
        File.WriteAllText(_originalExecutablePath, "proxy executable");
        File.WriteAllText(backupPath, "original executable");
        File.WriteAllText(configPath, "prepared config");

        // Act
        var result = await CreateLauncher().CleanupGameDirectoryAsync(
            _gameInstallPath,
            ExecutableName);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        Assert.Equal("original executable", File.ReadAllText(_originalExecutablePath));
        Assert.False(File.Exists(backupPath));
        Assert.False(File.Exists(configPath));
    }

    /// <summary>
    /// Verifies cleanup fails without deleting proxy artifacts when the original backup is missing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CleanupGameDirectoryAsync_DeployedProxyWithoutBackup_FailsClosedAsync()
    {
        // Arrange
        var configPath = Path.Combine(_gameInstallPath, "proxy_config.json");
        File.WriteAllText(_originalExecutablePath, "proxy executable");
        File.WriteAllText(configPath, "prepared config");

        // Act
        var result = await CreateLauncher().CleanupGameDirectoryAsync(
            _gameInstallPath,
            ExecutableName);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("proxy executable", File.ReadAllText(_originalExecutablePath));
        Assert.Equal("prepared config", File.ReadAllText(configPath));
    }

    /// <summary>
    /// Verifies concurrent preparations for one installation cannot overlap their mutations.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_ConcurrentProfilesSharingInstallation_SerializesMutationsAsync()
    {
        // Arrange
        var firstWriteStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWriteStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task BlockingWriterAsync(string path, string contents, CancellationToken cancellationToken)
        {
            firstWriteStarted.TrySetResult(true);
            await releaseFirstWrite.Task.WaitAsync(cancellationToken);
            await File.WriteAllTextAsync(path, contents, cancellationToken);
        }

        async Task ObservedWriterAsync(string path, string contents, CancellationToken cancellationToken)
        {
            secondWriteStarted.TrySetResult(true);
            await File.WriteAllTextAsync(path, contents, cancellationToken);
        }

        var firstPreparation = PrepareAsync(
            CreateLauncher(BlockingWriterAsync),
            profileId: "first-profile");
        await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondPreparation = PrepareAsync(
            CreateLauncher(ObservedWriterAsync),
            profileId: "second-profile");

        try
        {
            var prematureSecondWrite = await Task.WhenAny(
                secondWriteStarted.Task,
                Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.NotSame(secondWriteStarted.Task, prematureSecondWrite);
        }
        finally
        {
            releaseFirstWrite.TrySetResult(true);
        }

        // Assert
        var results = await Task.WhenAll(firstPreparation, secondPreparation);
        Assert.All(results, result => Assert.True(result.Success, result.AllErrors));
        Assert.True(secondWriteStarted.Task.IsCompleted);
    }

    /// <summary>
    /// Verifies cancellation after executable replacement restores the original executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PrepareForProfileAsync_CanceledAfterMutation_RollsBackCurrentAttemptAsync()
    {
        // Arrange
        using var cancellationSource = new CancellationTokenSource();
        var writeCount = 0;

        async Task CancelingWriterAsync(string path, string contents, CancellationToken cancellationToken)
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
            CreateLauncher(CancelingWriterAsync),
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
    public async Task PrepareForProfileAsync_ExecutableChangesDuringFailure_ReportsRollbackConflictAsync()
    {
        // Arrange
        async Task ConflictingWriterAsync(string path, string contents, CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            File.WriteAllText(_originalExecutablePath, "external executable change");
            throw new IOException("Injected write failure.");
        }

        // Act
        var result = await PrepareAsync(CreateLauncher(ConflictingWriterAsync));

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
        string profileId = "test-profile",
        CancellationToken cancellationToken = default)
    {
        return launcher.PrepareForProfileAsync(
            _gameInstallPath,
            profileId,
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
