using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Service responsible for managing replay checkpoints, minting save states from replays,
/// resuming playback deterministically, and taking over live player control.
/// </summary>
public sealed partial class ReplayCheckpointService : IReplayCheckpointService, IDisposable
{
    private static readonly TimeSpan DefaultMintTimeout = TimeSpan.FromMinutes(2);
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IProfileLauncherFacade? _directLauncherFacade;
    private readonly IGameProcessManager _processManager;
    private readonly ILogger<ReplayCheckpointService> _logger;
    private readonly string? _customSaveDirectory;
    private readonly TimeSpan _mintTimeout;
    private readonly SemaphoreSlim _mintLock = new(1, 1);
    private readonly object _activeSourcesLock = new();
    private readonly List<CancellationTokenSource> _activeMintSources = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayCheckpointService"/> class using a service scope factory.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory to create scopes for launching profiles.</param>
    /// <param name="processManager">The game process manager.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="customSaveDirectory">Optional custom save directory path.</param>
    /// <param name="mintTimeout">Optional custom minting timeout duration.</param>
    public ReplayCheckpointService(
        IServiceScopeFactory scopeFactory,
        IGameProcessManager processManager,
        ILogger<ReplayCheckpointService> logger,
        string? customSaveDirectory = null,
        TimeSpan? mintTimeout = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _customSaveDirectory = customSaveDirectory;
        _mintTimeout = mintTimeout ?? DefaultMintTimeout;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayCheckpointService"/> class directly with a launcher facade.
    /// </summary>
    /// <param name="launcherFacade">The profile launcher facade.</param>
    /// <param name="processManager">The game process manager.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="customSaveDirectory">Optional custom save directory path.</param>
    /// <param name="mintTimeout">Optional custom minting timeout duration.</param>
    public ReplayCheckpointService(
        IProfileLauncherFacade launcherFacade,
        IGameProcessManager processManager,
        ILogger<ReplayCheckpointService> logger,
        string? customSaveDirectory = null,
        TimeSpan? mintTimeout = null)
    {
        _directLauncherFacade = launcherFacade ?? throw new ArgumentNullException(nameof(launcherFacade));
        _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _customSaveDirectory = customSaveDirectory;
        _mintTimeout = mintTimeout ?? DefaultMintTimeout;
    }

    /// <inheritdoc/>
    public string GetSaveDirectory(GameType gameType)
    {
        if (!string.IsNullOrEmpty(_customSaveDirectory))
        {
            return _customSaveDirectory;
        }

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(docs) || !Path.IsPathRooted(docs))
        {
            docs = AppContext.BaseDirectory;
        }

        var dataFolder = gameType == GameType.ZeroHour
            ? GameSettingsConstants.FolderNames.ZeroHour
            : GameSettingsConstants.FolderNames.Generals;
        return Path.Combine(docs, dataFolder, ReplayManagerConstants.SaveFolderName);
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<ReplayCheckpointInfo>> MintCheckpointAsync(
        ReplayFile replay,
        GameProfile profile,
        int targetFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (targetFrame <= 0)
        {
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure("Target frame must be greater than zero.");
        }

        await _mintLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var saveDirectory = GetSaveDirectory(replay.GameVersion);
            try
            {
                Directory.CreateDirectory(saveDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "[ReplayCheckpoint] Failed to create or access save directory: {SaveDirectory}", saveDirectory);
                return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Failed to create or access checkpoint save directory '{saveDirectory}': {ex.Message}");
            }

            var safeReplay = GetSafeReplayName(replay.FileName);
            var saveFileName = $"{ReplayManagerConstants.CheckpointFilePrefix}{safeReplay}_{targetFrame}{ReplayManagerConstants.SaveFileExtension}";
            var saveFilePath = Path.Combine(saveDirectory, saveFileName);
            var legacyFileName = $"{ReplayManagerConstants.CheckpointFilePrefix}{targetFrame}{ReplayManagerConstants.SaveFileExtension}";
            var legacyFilePath = Path.Combine(saveDirectory, legacyFileName);

            var preExistingTargetTime = File.Exists(saveFilePath) ? File.GetLastWriteTimeUtc(saveFilePath) : (DateTime?)null;
            var preExistingLegacyTime = File.Exists(legacyFilePath) ? File.GetLastWriteTimeUtc(legacyFilePath) : (DateTime?)null;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_activeSourcesLock)
            {
                _activeMintSources.Add(linkedCts);
            }

            try
            {
                var launchResult = await LaunchMintingProcessAsync(replay, profile, targetFrame, saveFileName, linkedCts.Token);
                if (!launchResult.Success || launchResult.Data?.ProcessInfo == null)
                {
                    var error = launchResult.FirstError ?? "Failed to launch game client for checkpoint minting.";
                    _logger.LogError("[ReplayCheckpoint] Mint launch failed: {Error}", error);
                    return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(error);
                }

                var processId = launchResult.Data.ProcessInfo.ProcessId;
                var waitResult = await WaitForMintingProcessExitAsync(processId, targetFrame, linkedCts.Token);
                if (!waitResult.Success)
                {
                    if (linkedCts.Token.IsCancellationRequested)
                    {
                        return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(waitResult.FirstError ?? ReplayManagerConstants.CheckpointMintingCanceledErrorMessage);
                    }

                    // If the game client timed out exiting or threw an error, check if the save file was already written
                    var fallbackResult = FinalizeCheckpointSave(saveFilePath, saveDirectory, saveFileName, replay.FileName, targetFrame, preExistingTargetTime, preExistingLegacyTime);
                    if (fallbackResult.Success)
                    {
                        _logger.LogInformation("[ReplayCheckpoint] Checkpoint save file was created on disk despite process monitoring warning.");
                        return fallbackResult;
                    }

                    return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(waitResult.FirstError ?? "Checkpoint minting failed.");
                }

                return FinalizeCheckpointSave(saveFilePath, saveDirectory, saveFileName, replay.FileName, targetFrame, preExistingTargetTime, preExistingLegacyTime);
            }
            finally
            {
                lock (_activeSourcesLock)
                {
                    _activeMintSources.Remove(linkedCts);
                }
            }
        }
        finally
        {
            _mintLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> ResumeReplayAsync(
        ReplayFile replay,
        GameProfile profile,
        ReplayCheckpointInfo checkpoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var validationFailure = ValidateCheckpointReplayAssociation(replay, profile, checkpoint);
        if (validationFailure != null)
        {
            return validationFailure;
        }

        var additionalArgs = new Dictionary<string, string>
        {
            [ReplayManagerConstants.CliQuickStart] = string.Empty,
            [ReplayManagerConstants.CliLoadSave] = checkpoint.FileName,
            [ReplayManagerConstants.CliResumeReplay] = replay.FileName,
        };

        _logger.LogInformation(
            "[ReplayCheckpoint] Resuming replay '{Replay}' from checkpoint '{Checkpoint}' (Frame {Frame}) using profile '{Profile}'",
            replay.FileName,
            checkpoint.FileName,
            checkpoint.TargetFrame,
            profile.Name);

        var (facade, scope) = ResolveLauncherFacade();
        try
        {
            return await facade.LaunchProfileAsync(
                profile.Id,
                skipUserDataCleanup: false,
                additionalArguments: additionalArgs,
                cancellationToken: cancellationToken);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> TakeoverMatchAsync(
        ReplayFile replay,
        GameProfile profile,
        ReplayCheckpointInfo checkpoint,
        int slotIndex,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (slotIndex < 0 || slotIndex > 7)
        {
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure($"Invalid player slot index {slotIndex}. Expected a slot index between 0 and 7.");
        }

        var validationFailure = ValidateCheckpointReplayAssociation(replay, profile, checkpoint);
        if (validationFailure != null)
        {
            return validationFailure;
        }

        var additionalArgs = new Dictionary<string, string>
        {
            [ReplayManagerConstants.CliQuickStart] = string.Empty,
            [ReplayManagerConstants.CliLoadSave] = checkpoint.FileName,
            [ReplayManagerConstants.CliResumeAs] = slotIndex.ToString(),
        };

        _logger.LogInformation(
            "[ReplayCheckpoint] Taking over match from checkpoint '{Checkpoint}' as slot {Slot} using profile '{Profile}'",
            checkpoint.FileName,
            slotIndex,
            profile.Name);

        var (facade, scope) = ResolveLauncherFacade();
        try
        {
            return await facade.LaunchProfileAsync(
                profile.Id,
                skipUserDataCleanup: false,
                additionalArguments: additionalArgs,
                cancellationToken: cancellationToken);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReplayCheckpointInfo>> GetCheckpointsForReplayAsync(
        ReplayFile replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var saveDirectory = GetSaveDirectory(replay.GameVersion);
        if (!Directory.Exists(saveDirectory))
        {
            return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>([]);
        }

        try
        {
            var files = Directory.GetFiles(saveDirectory, ReplayManagerConstants.CheckpointFileSearchPattern);
            var list = new List<ReplayCheckpointInfo>();
            var expectedReplaySafe = GetSafeReplayName(replay.FileName);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryParseCheckpointFile(file, expectedReplaySafe, replay.FileName, out var checkpointInfo) && checkpointInfo != null)
                {
                    list.Add(checkpointInfo);
                }
            }

            var ordered = list.OrderBy(c => c.TargetFrame).ToList();
            return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>(ordered);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "[ReplayCheckpoint] Failed to enumerate checkpoints in '{Directory}'", saveDirectory);
            return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>([]);
        }
    }

    /// <inheritdoc/>
    public Task<ProfileOperationResult<bool>> DeleteCheckpointAsync(
        ReplayCheckpointInfo checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!File.Exists(checkpoint.FilePath))
            {
                _logger.LogWarning("[ReplayCheckpoint] Checkpoint file '{Path}' does not exist on disk.", checkpoint.FilePath);
                return Task.FromResult(ProfileOperationResult<bool>.CreateFailure($"Checkpoint file '{checkpoint.FileName}' was not found on disk."));
            }

            File.Delete(checkpoint.FilePath);
            _logger.LogInformation("[ReplayCheckpoint] Deleted checkpoint file '{Path}'.", checkpoint.FilePath);
            return Task.FromResult(ProfileOperationResult<bool>.CreateSuccess(true));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "[ReplayCheckpoint] Failed to delete checkpoint file '{Path}'", checkpoint.FilePath);
            return Task.FromResult(ProfileOperationResult<bool>.CreateFailure($"Failed to delete checkpoint file '{checkpoint.FileName}': {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public void CancelActiveMint()
    {
        List<CancellationTokenSource> sources;
        lock (_activeSourcesLock)
        {
            sources = [.. _activeMintSources];
        }

        foreach (var source in sources)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Source was already disposed
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelActiveMint();
        _mintLock.Dispose();
    }

    private static bool TryParseCheckpointFile(
        string file,
        string expectedReplaySafe,
        string replayFileName,
        out ReplayCheckpointInfo? checkpointInfo)
    {
        checkpointInfo = null;
        var fileName = Path.GetFileName(file);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var match = CheckpointPattern().Match(nameWithoutExt);
        if (!match.Success)
        {
            return false;
        }

        var replayGroup = match.Groups["replay"];
        var isLegacy = !replayGroup.Success || string.IsNullOrEmpty(replayGroup.Value);
        if (!isLegacy && !string.Equals(replayGroup.Value, expectedReplaySafe, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(match.Groups["frame"].Value, out var frame))
        {
            return false;
        }

        var fileInfo = new FileInfo(file);
        checkpointInfo = new ReplayCheckpointInfo
        {
            FilePath = file,
            FileName = fileName,
            TargetFrame = frame,
            CreatedAt = fileInfo.CreationTimeUtc,
            FileSizeBytes = fileInfo.Length,
            AssociatedReplayFileName = isLegacy ? null : replayFileName,
        };

        return true;
    }

    private static string GetSafeReplayName(string replayFileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(replayFileName);
        var safe = SanitizePattern().Replace(nameWithoutExt, "_").Trim('_');
        var baseName = string.IsNullOrEmpty(safe) ? "replay" : safe;

        if (string.Equals(nameWithoutExt, baseName, StringComparison.Ordinal))
        {
            return baseName;
        }

        var hash = ComputeStableHash(replayFileName);
        return $"{baseName}_{hash:x8}";
    }

    private static uint ComputeStableHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToUpperInvariant());
        var hashBytes = SHA256.HashData(bytes);
        return BitConverter.ToUInt32(hashBytes, 0);
    }

    [GeneratedRegex(@"^cp_(?:(?<replay>[a-zA-Z0-9_\-]+)_)?(?<frame>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CheckpointPattern();

    [GeneratedRegex(@"[^a-zA-Z0-9_\-]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SanitizePattern();

    private (IProfileLauncherFacade Facade, IServiceScope? Scope) ResolveLauncherFacade()
    {
        if (_directLauncherFacade != null)
        {
            return (_directLauncherFacade, null);
        }

        if (_scopeFactory != null)
        {
            var scope = _scopeFactory.CreateScope();
            var facade = scope.ServiceProvider.GetRequiredService<IProfileLauncherFacade>();
            return (facade, scope);
        }

        throw new InvalidOperationException("Neither launcher facade nor scope factory is available.");
    }

    private ProfileOperationResult<GameLaunchInfo>? ValidateCheckpointReplayAssociation(
        ReplayFile replay,
        GameProfile profile,
        ReplayCheckpointInfo checkpoint)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!string.IsNullOrEmpty(checkpoint.AssociatedReplayFileName) &&
            !string.Equals(checkpoint.AssociatedReplayFileName, replay.FileName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[ReplayCheckpoint] Checkpoint '{Checkpoint}' is associated with replay '{AssociatedReplay}', not '{Replay}'.",
                checkpoint.FileName,
                checkpoint.AssociatedReplayFileName,
                replay.FileName);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                $"Checkpoint '{checkpoint.FileName}' does not belong to replay '{replay.FileName}'.");
        }

        var expectedSafe = GetSafeReplayName(replay.FileName);
        var match = CheckpointPattern().Match(Path.GetFileNameWithoutExtension(checkpoint.FileName));
        if (match.Success)
        {
            var replayGroup = match.Groups["replay"];
            if (replayGroup.Success &&
                !string.IsNullOrEmpty(replayGroup.Value) &&
                !string.Equals(replayGroup.Value, expectedSafe, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "[ReplayCheckpoint] Checkpoint '{Checkpoint}' safe name mismatch. Expected '{Expected}', found '{Actual}'.",
                    checkpoint.FileName,
                    expectedSafe,
                    replayGroup.Value);
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                    $"Checkpoint '{checkpoint.FileName}' does not belong to replay '{replay.FileName}'.");
            }
        }

        return null;
    }

    private async Task<ProfileOperationResult<GameLaunchInfo>> LaunchMintingProcessAsync(
        ReplayFile replay,
        GameProfile profile,
        int targetFrame,
        string saveFileName,
        CancellationToken cancellationToken)
    {
        var quitFrame = targetFrame + 1;
        var additionalArgs = new Dictionary<string, string>
        {
            [ReplayManagerConstants.CliQuickStart] = string.Empty,
            [ReplayManagerConstants.CliReplay] = replay.FileName,
            [ReplayManagerConstants.CliSaveAtFrame] = targetFrame.ToString(),
            [ReplayManagerConstants.CliSaveTo] = saveFileName,
            [ReplayManagerConstants.CliQuitAtFrame] = quitFrame.ToString(),
        };

        _logger.LogInformation(
            "[ReplayCheckpoint] Minting checkpoint at frame {Frame} for replay '{Replay}' using profile '{Profile}'",
            targetFrame,
            replay.FileName,
            profile.Name);

        var (facade, scope) = ResolveLauncherFacade();
        try
        {
            return await facade.LaunchProfileAsync(
                profile.Id,
                skipUserDataCleanup: false,
                additionalArguments: additionalArgs,
                cancellationToken: cancellationToken);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private async Task<ProfileOperationResult<bool>> WaitForMintingProcessExitAsync(
        int processId,
        int targetFrame,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            await TerminateMintingProcessSafeAsync(processId);
            _logger.LogWarning("[ReplayCheckpoint] Checkpoint minting canceled before monitoring process {Pid}.", processId);
            return ProfileOperationResult<bool>.CreateFailure(ReplayManagerConstants.CheckpointMintingCanceledErrorMessage);
        }

        _logger.LogDebug("[ReplayCheckpoint] Monitoring game process PID {Pid} until exit...", processId);
        using var timeoutCts = new CancellationTokenSource(_mintTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var consecutiveErrors = 0;
        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                var (stillRunning, newErrorCount) = await CheckProcessRunningStatusAsync(processId, consecutiveErrors, linkedCts.Token);
                consecutiveErrors = newErrorCount;
                if (!stillRunning)
                {
                    break;
                }

                await Task.Delay(ReplayManagerConstants.DefaultCheckpointPollIntervalMs, linkedCts.Token);
            }
        }
        catch (OperationCanceledException ex)
        {
            await TerminateMintingProcessSafeAsync(processId);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[ReplayCheckpoint] Checkpoint minting canceled by user.");
                return ProfileOperationResult<bool>.CreateFailure(ReplayManagerConstants.CheckpointMintingCanceledErrorMessage);
            }

            _logger.LogWarning(ex, "[ReplayCheckpoint] Checkpoint minting timed out waiting for process {Pid} to reach target frame {Frame}.", processId, targetFrame);
            return ProfileOperationResult<bool>.CreateFailure($"Checkpoint minting timed out waiting for game client to reach frame {targetFrame}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await TerminateMintingProcessSafeAsync(processId);
            _logger.LogWarning("[ReplayCheckpoint] Checkpoint minting canceled by user.");
            return ProfileOperationResult<bool>.CreateFailure(ReplayManagerConstants.CheckpointMintingCanceledErrorMessage);
        }

        if (timeoutCts.IsCancellationRequested)
        {
            await TerminateMintingProcessSafeAsync(processId);
            _logger.LogWarning("[ReplayCheckpoint] Checkpoint minting timed out after {Minutes} minutes for process {Pid}.", _mintTimeout.TotalMinutes, processId);
            return ProfileOperationResult<bool>.CreateFailure($"Checkpoint minting timed out waiting for game client to reach frame {targetFrame}.");
        }

        if (consecutiveErrors >= ReplayManagerConstants.MaxProcessExitRetries)
        {
            await TerminateMintingProcessSafeAsync(processId);
            _logger.LogWarning("[ReplayCheckpoint] Checkpoint monitoring failed after {Count} consecutive errors polling process {Pid}.", consecutiveErrors, processId);
            return ProfileOperationResult<bool>.CreateFailure($"Monitoring minting process {processId} failed due to repeated process communication errors.");
        }

        return ProfileOperationResult<bool>.CreateSuccess(true);
    }

    private async Task<(bool StillRunning, int ConsecutiveErrors)> CheckProcessRunningStatusAsync(
        int processId,
        int consecutiveErrors,
        CancellationToken cancellationToken)
    {
        var processInfo = await _processManager.GetProcessInfoAsync(processId, cancellationToken);
        if (processInfo.Success && processInfo.Data != null)
        {
            return (processInfo.Data.IsRunning, 0);
        }

        var errorMsg = processInfo.FirstError ?? string.Empty;
        var isNotFound = string.Equals(errorMsg, ProcessConstants.ProcessNotFoundErrorMessage, StringComparison.OrdinalIgnoreCase);

        if (isNotFound)
        {
            _logger.LogDebug("[ReplayCheckpoint] Process {Pid} is no longer running ({Reason}).", processId, errorMsg);
            return (false, 0);
        }

        var newErrorCount = consecutiveErrors + 1;
        _logger.LogWarning("[ReplayCheckpoint] Transient failure polling process {Pid} ({Count}/3): {Error}", processId, newErrorCount, errorMsg);
        return (newErrorCount < ReplayManagerConstants.MaxProcessExitRetries, newErrorCount);
    }

    private async Task TerminateMintingProcessSafeAsync(int processId)
    {
        try
        {
            _logger.LogInformation("[ReplayCheckpoint] Terminating minting game process {Pid} due to timeout or cancellation...", processId);
            var result = await _processManager.TerminateProcessAsync(processId, CancellationToken.None);
            if (!result.Success)
            {
                _logger.LogWarning("[ReplayCheckpoint] Failed to terminate minting game process {Pid}: {Error}", processId, result.FirstError);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ReplayCheckpoint] Failed to terminate minting game process {Pid}.", processId);
        }
    }

    private ProfileOperationResult<ReplayCheckpointInfo> FinalizeCheckpointSave(
        string saveFilePath,
        string saveDirectory,
        string saveFileName,
        string replayFileName,
        int targetFrame,
        DateTime? preExistingTargetTime,
        DateTime? preExistingLegacyTime)
    {
        var usedLegacy = false;
        if (!File.Exists(saveFilePath))
        {
            // Defensive check: if engine wrote bare cp_<frame>.sav without replay prefix, rename it
            var legacyFileName = $"{ReplayManagerConstants.CheckpointFilePrefix}{targetFrame}{ReplayManagerConstants.SaveFileExtension}";
            var legacyFilePath = Path.Combine(saveDirectory, legacyFileName);
            if (File.Exists(legacyFilePath))
            {
                if (preExistingLegacyTime.HasValue && File.GetLastWriteTimeUtc(legacyFilePath) <= preExistingLegacyTime.Value)
                {
                    _logger.LogError(
                        "[ReplayCheckpoint] Legacy save file '{Path}' was not updated by this mint run (stale file from previous session detected).",
                        legacyFilePath);
                    return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(
                        $"Save file '{legacyFileName}' was not updated by the game client (stale file detected).");
                }

                usedLegacy = true;
                try
                {
                    File.Move(legacyFilePath, saveFilePath, overwrite: true);
                }
                catch (IOException ioEx)
                {
                    _logger.LogWarning(ioEx, "[ReplayCheckpoint] Could not rename {Legacy} to {Target}", legacyFilePath, saveFilePath);
                    saveFilePath = legacyFilePath;
                    saveFileName = legacyFileName;
                }
            }
        }

        if (!File.Exists(saveFilePath))
        {
            _logger.LogError("[ReplayCheckpoint] Save file '{Path}' was not created by the game client.", saveFilePath);
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Save file '{saveFileName}' was not created by the game client.");
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(saveFilePath);
            if (!fileInfo.Exists)
            {
                _logger.LogError("[ReplayCheckpoint] Save file '{Path}' was not created by the game client.", saveFilePath);
                return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Save file '{saveFileName}' was not created by the game client.");
            }

            if (fileInfo.Length < ReplayManagerConstants.MinValidSaveFileSizeBytes)
            {
                _logger.LogWarning("[ReplayCheckpoint] Save file '{Path}' is too small ({Size} bytes) to be a valid game save.", saveFilePath, fileInfo.Length);
                return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Save file '{saveFileName}' is corrupted or incomplete (file size too small).");
            }

            if (!usedLegacy && preExistingTargetTime.HasValue && fileInfo.LastWriteTimeUtc <= preExistingTargetTime.Value)
            {
                _logger.LogError(
                    "[ReplayCheckpoint] Save file '{Path}' was not updated by this mint run (stale file from previous session detected).",
                    saveFilePath);
                return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(
                    $"Save file '{saveFileName}' was not updated by the game client (stale file detected).");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "[ReplayCheckpoint] Failed to access save file metadata for '{Path}'", saveFilePath);
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Failed to read metadata for save file '{saveFileName}': {ex.Message}");
        }

        var checkpoint = new ReplayCheckpointInfo
        {
            FilePath = saveFilePath,
            FileName = saveFileName,
            TargetFrame = targetFrame,
            CreatedAt = fileInfo.CreationTimeUtc,
            FileSizeBytes = fileInfo.Length,
            AssociatedReplayFileName = replayFileName,
        };

        _logger.LogInformation("[ReplayCheckpoint] Successfully minted checkpoint {FileName} ({Size} bytes)", saveFileName, fileInfo.Length);
        return ProfileOperationResult<ReplayCheckpointInfo>.CreateSuccess(checkpoint);
    }
}
