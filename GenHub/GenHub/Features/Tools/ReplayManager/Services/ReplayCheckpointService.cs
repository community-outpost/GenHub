using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Service responsible for managing replay checkpoints, minting save states from replays,
/// resuming playback deterministically, and taking over live player control.
/// </summary>
public sealed partial class ReplayCheckpointService(
    IProfileLauncherFacade launcherFacade,
    IGameProcessManager processManager,
    ILogger<ReplayCheckpointService> logger,
    string? customSaveDirectory = null,
    TimeSpan? mintTimeout = null) : IReplayCheckpointService
{
    private static readonly TimeSpan DefaultMintTimeout = TimeSpan.FromMinutes(2);
    private readonly TimeSpan _mintTimeout = mintTimeout ?? DefaultMintTimeout;
    private readonly object _mintLock = new();
    private readonly List<CancellationTokenSource> _activeMintSources = [];

    /// <inheritdoc/>
    public string GetSaveDirectory(GameType gameType)
    {
        if (!string.IsNullOrEmpty(customSaveDirectory))
        {
            return customSaveDirectory;
        }

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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

        if (targetFrame <= 0)
        {
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure("Target frame must be greater than zero.");
        }

        var saveDirectory = GetSaveDirectory(replay.GameVersion);
        Directory.CreateDirectory(saveDirectory);

        var safeReplay = GetSafeReplayName(replay.FileName);
        var saveFileName = $"cp_{safeReplay}_{targetFrame}.sav";
        var saveFilePath = Path.Combine(saveDirectory, saveFileName);

        var preExistingTime = File.Exists(saveFilePath) ? File.GetLastWriteTimeUtc(saveFilePath) : (DateTime?)null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_mintLock)
        {
            _activeMintSources.Add(linkedCts);
        }
        try
        {
            var launchResult = await LaunchMintingProcessAsync(replay, profile, targetFrame, saveFileName, linkedCts.Token);
            if (!launchResult.Success || launchResult.Data?.ProcessInfo == null)
            {
                var error = launchResult.FirstError ?? "Failed to launch game client for checkpoint minting.";
                logger.LogError("[ReplayCheckpoint] Mint launch failed: {Error}", error);
                return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(error);
            }

            var processId = launchResult.Data.ProcessInfo.ProcessId;
            var waitResult = await WaitForMintingProcessExitAsync(processId, targetFrame, linkedCts.Token);
            if (!waitResult.Success)
            {
                // If the game client timed out exiting or threw an error, check if the save file was already written
                var fallbackResult = FinalizeCheckpointSave(saveFilePath, saveDirectory, saveFileName, replay.FileName, targetFrame, preExistingTime);
                if (fallbackResult.Success)
                {
                    logger.LogInformation("[ReplayCheckpoint] Checkpoint save file was created on disk despite process monitoring warning.");
                    return fallbackResult;
                }

                return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(waitResult.FirstError ?? "Checkpoint minting failed.");
            }

            return FinalizeCheckpointSave(saveFilePath, saveDirectory, saveFileName, replay.FileName, targetFrame, preExistingTime);
        }
        finally
        {
            lock (_mintLock)
            {
                _activeMintSources.Remove(linkedCts);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> ResumeReplayAsync(
        ReplayFile replay,
        GameProfile profile,
        ReplayCheckpointInfo checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!string.IsNullOrEmpty(checkpoint.AssociatedReplayFileName) &&
            !string.Equals(checkpoint.AssociatedReplayFileName, replay.FileName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "[ReplayCheckpoint] Checkpoint '{Checkpoint}' is associated with replay '{AssociatedReplay}', not '{Replay}'.",
                checkpoint.FileName,
                checkpoint.AssociatedReplayFileName,
                replay.FileName);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                $"Checkpoint '{checkpoint.FileName}' does not belong to replay '{replay.FileName}'.");
        }

        var additionalArgs = new Dictionary<string, string>
        {
            [ReplayManagerConstants.CliQuickStart] = string.Empty,
            [ReplayManagerConstants.CliLoadSave] = checkpoint.FileName,
            [ReplayManagerConstants.CliResumeReplay] = replay.FileName,
        };

        logger.LogInformation(
            "[ReplayCheckpoint] Resuming replay '{Replay}' from checkpoint '{Checkpoint}' (Frame {Frame}) using profile '{Profile}'",
            replay.FileName,
            checkpoint.FileName,
            checkpoint.TargetFrame,
            profile.Name);

        var result = await launcherFacade.LaunchProfileAsync(
            profile.Id,
            skipUserDataCleanup: false,
            cancellationToken: cancellationToken,
            additionalArguments: additionalArgs);

        return result;
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> TakeoverMatchAsync(
        ReplayFile replay,
        GameProfile profile,
        ReplayCheckpointInfo checkpoint,
        int slotIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!string.IsNullOrEmpty(checkpoint.AssociatedReplayFileName) &&
            !string.Equals(checkpoint.AssociatedReplayFileName, replay.FileName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "[ReplayCheckpoint] Checkpoint '{Checkpoint}' is associated with replay '{AssociatedReplay}', not '{Replay}'.",
                checkpoint.FileName,
                checkpoint.AssociatedReplayFileName,
                replay.FileName);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                $"Checkpoint '{checkpoint.FileName}' does not belong to replay '{replay.FileName}'.");
        }

        var additionalArgs = new Dictionary<string, string>
        {
            [ReplayManagerConstants.CliQuickStart] = string.Empty,
            [ReplayManagerConstants.CliLoadSave] = checkpoint.FileName,
            [ReplayManagerConstants.CliResumeAs] = slotIndex.ToString(),
        };

        logger.LogInformation(
            "[ReplayCheckpoint] Taking over match from checkpoint '{Checkpoint}' as slot {Slot} using profile '{Profile}'",
            checkpoint.FileName,
            slotIndex,
            profile.Name);

        var result = await launcherFacade.LaunchProfileAsync(
            profile.Id,
            skipUserDataCleanup: false,
            cancellationToken: cancellationToken,
            additionalArguments: additionalArgs);

        return result;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReplayCheckpointInfo>> GetCheckpointsForReplayAsync(
        ReplayFile replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);

        var saveDirectory = GetSaveDirectory(replay.GameVersion);
        if (!Directory.Exists(saveDirectory))
        {
            return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>([]);
        }

        try
        {
            var files = Directory.GetFiles(saveDirectory, "*.sav");
            var list = new List<ReplayCheckpointInfo>();
            var expectedReplaySafe = GetSafeReplayName(replay.FileName);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var match = CheckpointPattern().Match(nameWithoutExt);
                if (!match.Success)
                {
                    continue;
                }

                var replayGroup = match.Groups["replay"];
                if (replayGroup.Success && !string.IsNullOrEmpty(replayGroup.Value) &&
                    !string.Equals(replayGroup.Value, expectedReplaySafe, StringComparison.OrdinalIgnoreCase))
                {
                    // Belongs to a different replay, ignore
                    continue;
                }

                if (int.TryParse(match.Groups["frame"].Value, out var frame))
                {
                    var fileInfo = new FileInfo(file);
                    list.Add(new ReplayCheckpointInfo
                    {
                        FilePath = file,
                        FileName = fileName,
                        TargetFrame = frame,
                        CreatedAt = fileInfo.CreationTimeUtc,
                        FileSizeBytes = fileInfo.Length,
                        AssociatedReplayFileName = replay.FileName,
                    });
                }
            }

            var ordered = list.OrderBy(c => c.TargetFrame).ToList();
            return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>(ordered);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReplayCheckpoint] Failed to enumerate checkpoints in '{Directory}'", saveDirectory);
            return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>([]);
        }
    }

    /// <inheritdoc/>
    public Task<bool> DeleteCheckpointAsync(
        ReplayCheckpointInfo checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        try
        {
            if (File.Exists(checkpoint.FilePath))
            {
                File.Delete(checkpoint.FilePath);
                logger.LogInformation("[ReplayCheckpoint] Deleted checkpoint file '{Path}'", checkpoint.FilePath);
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReplayCheckpoint] Failed to delete checkpoint file '{Path}'", checkpoint.FilePath);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public void CancelActiveMint()
    {
        List<CancellationTokenSource> sources;
        lock (_mintLock)
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

    private static string GetSafeReplayName(string replayFileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(replayFileName);
        var safe = SanitizePattern().Replace(nameWithoutExt, "_").Trim('_');
        return string.IsNullOrEmpty(safe) ? "replay" : safe;
    }

    [GeneratedRegex(@"^cp_(?:(?<replay>[a-zA-Z0-9_\-]+)_)?(?<frame>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CheckpointPattern();

    [GeneratedRegex(@"[^a-zA-Z0-9_\-]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SanitizePattern();

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

        logger.LogInformation(
            "[ReplayCheckpoint] Minting checkpoint at frame {Frame} for replay '{Replay}' using profile '{Profile}'",
            targetFrame,
            replay.FileName,
            profile.Name);

        return await launcherFacade.LaunchProfileAsync(
            profile.Id,
            skipUserDataCleanup: false,
            cancellationToken: cancellationToken,
            additionalArguments: additionalArgs);
    }

    private async Task<ProfileOperationResult<bool>> WaitForMintingProcessExitAsync(
        int processId,
        int targetFrame,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("[ReplayCheckpoint] Monitoring game process PID {Pid} until exit...", processId);
        using var timeoutCts = new CancellationTokenSource(_mintTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var consecutiveErrors = 0;
            while (!linkedCts.IsCancellationRequested)
            {
                var (stillRunning, newErrorCount) = await CheckProcessRunningStatusAsync(processId, consecutiveErrors, linkedCts.Token);
                consecutiveErrors = newErrorCount;
                if (!stillRunning)
                {
                    break;
                }

                await Task.Delay(500, linkedCts.Token);
            }
        }
        catch (OperationCanceledException ex)
        {
            await TerminateMintingProcessSafeAsync(processId);

            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "[ReplayCheckpoint] Checkpoint minting canceled by user.");
                return ProfileOperationResult<bool>.CreateFailure("Checkpoint minting canceled by user.");
            }

            logger.LogWarning(ex, "[ReplayCheckpoint] Checkpoint minting timed out waiting for process {Pid} to reach target frame {Frame}.", processId, targetFrame);
            return ProfileOperationResult<bool>.CreateFailure($"Checkpoint minting timed out waiting for game client to reach frame {targetFrame}.");
        }

        if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await TerminateMintingProcessSafeAsync(processId);
            logger.LogWarning("[ReplayCheckpoint] Checkpoint minting timed out after {Minutes} minutes for process {Pid}.", _mintTimeout.TotalMinutes, processId);
            return ProfileOperationResult<bool>.CreateFailure($"Checkpoint minting timed out waiting for game client to reach frame {targetFrame}.");
        }

        return ProfileOperationResult<bool>.CreateSuccess(true);
    }

    private async Task<(bool StillRunning, int ConsecutiveErrors)> CheckProcessRunningStatusAsync(
        int processId,
        int consecutiveErrors,
        CancellationToken cancellationToken)
    {
        var processInfo = await processManager.GetProcessInfoAsync(processId, cancellationToken);
        if (processInfo.Success && processInfo.Data != null)
        {
            return (processInfo.Data.IsRunning, 0);
        }

        var errorMsg = processInfo.FirstError ?? string.Empty;
        var isNotFound = errorMsg.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                         errorMsg.Contains("exited", StringComparison.OrdinalIgnoreCase);

        if (isNotFound)
        {
            logger.LogDebug("[ReplayCheckpoint] Process {Pid} is no longer running ({Reason}).", processId, errorMsg);
            return (false, 0);
        }

        var newErrorCount = consecutiveErrors + 1;
        logger.LogWarning("[ReplayCheckpoint] Transient failure polling process {Pid} ({Count}/3): {Error}", processId, newErrorCount, errorMsg);
        return (newErrorCount < 3, newErrorCount);
    }

    private async Task TerminateMintingProcessSafeAsync(int processId)
    {
        try
        {
            logger.LogInformation("[ReplayCheckpoint] Terminating minting game process {Pid} due to timeout or cancellation...", processId);
            await processManager.TerminateProcessAsync(processId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ReplayCheckpoint] Failed to terminate minting game process {Pid}.", processId);
        }
    }

    private ProfileOperationResult<ReplayCheckpointInfo> FinalizeCheckpointSave(
        string saveFilePath,
        string saveDirectory,
        string saveFileName,
        string replayFileName,
        int targetFrame,
        DateTime? preExistingTime)
    {
        if (!File.Exists(saveFilePath))
        {
            // Defensive check: if engine wrote bare cp_<frame>.sav without replay prefix, rename it
            var legacyFileName = $"cp_{targetFrame}.sav";
            var legacyFilePath = Path.Combine(saveDirectory, legacyFileName);
            if (File.Exists(legacyFilePath))
            {
                try
                {
                    File.Move(legacyFilePath, saveFilePath, overwrite: true);
                }
                catch (IOException ioEx)
                {
                    logger.LogWarning(ioEx, "[ReplayCheckpoint] Could not rename {Legacy} to {Target}", legacyFilePath, saveFilePath);
                    saveFilePath = legacyFilePath;
                    saveFileName = legacyFileName;
                }
            }
        }

        if (!File.Exists(saveFilePath))
        {
            logger.LogError("[ReplayCheckpoint] Save file '{Path}' was not created by the game client.", saveFilePath);
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Save file '{saveFileName}' was not created by the game client.");
        }

        var fileInfo = new FileInfo(saveFilePath);
        if (preExistingTime.HasValue && fileInfo.LastWriteTimeUtc <= preExistingTime.Value)
        {
            logger.LogError(
                "[ReplayCheckpoint] Save file '{Path}' was not updated by this mint run (stale file from previous session detected).",
                saveFilePath);
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(
                $"Save file '{saveFileName}' was not updated by the game client (stale file detected).");
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

        logger.LogInformation("[ReplayCheckpoint] Successfully minted checkpoint {FileName} ({Size} bytes)", saveFileName, fileInfo.Length);
        return ProfileOperationResult<ReplayCheckpointInfo>.CreateSuccess(checkpoint);
    }
}
