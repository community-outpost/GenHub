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
    string? customSaveDirectory = null) : IReplayCheckpointService
{
    private static readonly TimeSpan DefaultMintTimeout = TimeSpan.FromMinutes(2);

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

        var launchResult = await launcherFacade.LaunchProfileAsync(
            profile.Id,
            skipUserDataCleanup: false,
            cancellationToken: cancellationToken,
            additionalArguments: additionalArgs);

        if (!launchResult.Success || launchResult.Data?.ProcessInfo == null)
        {
            var error = launchResult.FirstError ?? "Failed to launch game client for checkpoint minting.";
            logger.LogError("[ReplayCheckpoint] Mint launch failed: {Error}", error);
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(error);
        }

        var processId = launchResult.Data.ProcessInfo.ProcessId;
        logger.LogDebug("[ReplayCheckpoint] Monitoring game process PID {Pid} until exit...", processId);

        using var timeoutCts = new CancellationTokenSource(DefaultMintTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                var processInfo = await processManager.GetProcessInfoAsync(processId, linkedCts.Token);
                if (!processInfo.Success || processInfo.Data == null)
                {
                    logger.LogDebug("[ReplayCheckpoint] Process {Pid} has exited.", processId);
                    break;
                }

                await Task.Delay(500, linkedCts.Token);
            }
        }
        catch (OperationCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "[ReplayCheckpoint] Checkpoint minting canceled by user.");
                return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure("Checkpoint minting canceled by user.");
            }

            logger.LogWarning(ex, "[ReplayCheckpoint] Checkpoint minting timed out waiting for process {Pid} to reach target frame {Frame}.", processId, targetFrame);
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Checkpoint minting timed out waiting for game client to reach frame {targetFrame}.");
        }

        if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[ReplayCheckpoint] Checkpoint minting timed out after {Minutes} minutes for process {Pid}.", DefaultMintTimeout.TotalMinutes, processId);
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure($"Checkpoint minting timed out waiting for game client to reach frame {targetFrame}.");
        }

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
        var checkpoint = new ReplayCheckpointInfo
        {
            FilePath = saveFilePath,
            FileName = saveFileName,
            TargetFrame = targetFrame,
            CreatedAt = fileInfo.CreationTimeUtc,
            FileSizeBytes = fileInfo.Length,
            AssociatedReplayFileName = replay.FileName,
        };

        logger.LogInformation("[ReplayCheckpoint] Successfully minted checkpoint {FileName} ({Size} bytes)", saveFileName, fileInfo.Length);
        return ProfileOperationResult<ReplayCheckpointInfo>.CreateSuccess(checkpoint);
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
}
