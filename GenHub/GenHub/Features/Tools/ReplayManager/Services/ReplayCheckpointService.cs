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
    ILogger<ReplayCheckpointService> logger) : IReplayCheckpointService
{
    private static readonly Regex CheckpointPattern = new(
        @"^cp_(\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <inheritdoc/>
    public string GetSaveDirectory(GameType gameType)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dataFolder = gameType == GameType.ZeroHour
            ? GameSettingsConstants.FolderNames.ZeroHour
            : GameSettingsConstants.FolderNames.Generals;
        return Path.Combine(docs, dataFolder, ReplayManagerConstants.SaveFolderName);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReplayCheckpointInfo>> GetCheckpointsForReplayAsync(
        ReplayFile replay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var saveDir = GetSaveDirectory(replay.GameVersion);
        if (!Directory.Exists(saveDir))
        {
            return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>(Array.Empty<ReplayCheckpointInfo>());
        }

        var dirInfo = new DirectoryInfo(saveDir);
        var files = dirInfo.GetFiles($"*{ReplayManagerConstants.SaveFileExtension}");

        var list = new List<ReplayCheckpointInfo>();
        foreach (var file in files)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(file.Name);
            var frame = ExtractFrameFromName(nameWithoutExt);
            if (frame.HasValue)
            {
                list.Add(new ReplayCheckpointInfo
                {
                    FilePath = file.FullName,
                    FileName = file.Name,
                    TargetFrame = frame.Value,
                    CreatedAt = file.LastWriteTimeUtc,
                    FileSizeBytes = file.Length,
                    AssociatedReplayFileName = replay.FileName,
                });
            }
        }

        var ordered = list.OrderBy(x => x.TargetFrame).ToList();
        return Task.FromResult<IReadOnlyList<ReplayCheckpointInfo>>(ordered);
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

        var saveDir = GetSaveDirectory(replay.GameVersion);
        Directory.CreateDirectory(saveDir);

        var saveFileName = $"cp_{targetFrame}.sav";
        var saveFilePath = Path.Combine(saveDir, saveFileName);

        var additionalArgs = new Dictionary<string, string>
        {
            [ReplayManagerConstants.CliReplay] = replay.FileName,
            [ReplayManagerConstants.CliSaveAtFrame] = targetFrame.ToString(),
            [ReplayManagerConstants.CliSaveTo] = saveFileName,
            [ReplayManagerConstants.CliQuitAtFrame] = (targetFrame + 1).ToString(),
            ["-quickstart"] = string.Empty,
        };

        logger.LogInformation(
            "[ReplayCheckpoint] Minting checkpoint at frame {Frame} for replay '{Replay}' using profile '{ProfileId}'",
            targetFrame,
            replay.FileName,
            profile.Id);

        var launchResult = await launcherFacade.LaunchProfileAsync(
            profile.Id,
            skipUserDataCleanup: false,
            additionalArguments: additionalArgs,
            cancellationToken: cancellationToken);

        if (!launchResult.Success || launchResult.Data == null)
        {
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(
                launchResult.FirstError ?? "Failed to launch game client to mint checkpoint.");
        }

        var launchInfo = launchResult.Data;
        var pid = launchInfo.ProcessInfo.ProcessId;

        // Wait for process to exit cleanly after reaching quitatframe
        if (pid > 0)
        {
            var waitStart = DateTime.UtcNow;
            var maxWait = TimeSpan.FromMinutes(2);
            while (DateTime.UtcNow - waitStart < maxWait)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var procInfo = await processManager.GetProcessInfoAsync(pid, cancellationToken);
                if (!procInfo.Success || procInfo.Data == null)
                {
                    break;
                }

                await Task.Delay(500, cancellationToken);
            }
        }

        // Check if the save file was generated
        if (!File.Exists(saveFilePath))
        {
            await Task.Delay(1000, cancellationToken);
        }

        if (!File.Exists(saveFilePath))
        {
            return ProfileOperationResult<ReplayCheckpointInfo>.CreateFailure(
                $"Game client exited but checkpoint save '{saveFileName}' was not found in '{saveDir}'.");
        }

        var fi = new FileInfo(saveFilePath);
        var checkpoint = new ReplayCheckpointInfo
        {
            FilePath = fi.FullName,
            FileName = fi.Name,
            TargetFrame = targetFrame,
            CreatedAt = fi.LastWriteTimeUtc,
            FileSizeBytes = fi.Length,
            AssociatedReplayFileName = replay.FileName,
        };

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
            [ReplayManagerConstants.CliLoadSave] = checkpoint.FileName,
            [ReplayManagerConstants.CliResumeReplay] = replay.FileName,
        };

        logger.LogInformation(
            "[ReplayCheckpoint] Resuming replay '{Replay}' from checkpoint '{Save}' using profile '{ProfileId}'",
            replay.FileName,
            checkpoint.FileName,
            profile.Id);

        return await launcherFacade.LaunchProfileAsync(
            profile.Id,
            skipUserDataCleanup: false,
            additionalArguments: additionalArgs,
            cancellationToken: cancellationToken);
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
            [ReplayManagerConstants.CliLoadSave] = checkpoint.FileName,
            [ReplayManagerConstants.CliResumeAs] = slotIndex.ToString(),
        };

        logger.LogInformation(
            "[ReplayCheckpoint] Taking over replay '{Replay}' from checkpoint '{Save}' as slot {Slot} using profile '{ProfileId}'",
            replay.FileName,
            checkpoint.FileName,
            slotIndex,
            profile.Id);

        return await launcherFacade.LaunchProfileAsync(
            profile.Id,
            skipUserDataCleanup: false,
            additionalArguments: additionalArgs,
            cancellationToken: cancellationToken);
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
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReplayCheckpoint] Failed to delete checkpoint file '{Path}'", checkpoint.FilePath);
        }

        return Task.FromResult(false);
    }

    private static int? ExtractFrameFromName(string name)
    {
        var match = CheckpointPattern.Match(name);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var frame))
        {
            return frame;
        }

        return null;
    }
}
