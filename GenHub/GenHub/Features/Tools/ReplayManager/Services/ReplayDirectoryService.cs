using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Implementation of <see cref="IReplayDirectoryService"/> for managing replay files on disk.
/// Automatically parses replay headers and resolves game client and profile compatibility against installed content.
/// </summary>
public sealed class ReplayDirectoryService(
    IReplayHeaderParser headerParser,
    ICrcMappingRegistry crcMappingRegistry,
    IServiceScopeFactory scopeFactory,
    ILogger<ReplayDirectoryService> logger) : IReplayDirectoryService
{
    /// <inheritdoc />
    public string GetReplayDirectory(GameType version)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var gameDataFolder = version switch
        {
            GameType.Generals => GameSettingsConstants.FolderNames.Generals,
            GameType.ZeroHour => GameSettingsConstants.FolderNames.ZeroHour,
            _ => throw new ArgumentException("Unsupported game version", nameof(version)),
        };

        return Path.Combine(documents, gameDataFolder, GameSettingsConstants.FolderNames.Replays);
    }

    /// <inheritdoc />
    public void EnsureDirectoryExists(GameType version)
    {
        var path = GetReplayDirectory(version);
        if (!Directory.Exists(path))
        {
            logger.LogInformation(LogMessages.CreatingReplayDirectory, path);
            Directory.CreateDirectory(path);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplayFile>> GetReplaysAsync(GameType version, CancellationToken ct = default)
    {
        var directory = GetReplayDirectory(version);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = Directory.GetFiles(directory, "*.*")
            .Where(f => f.EndsWith(".rep", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var (acquiredIds, existingProfiles) = await FetchAcquiredManifestIdsAndProfilesAsync(ct);

        var replayFiles = new List<ReplayFile>(files.Count);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var replay = await ProcessReplayFileAsync(file, version, acquiredIds, existingProfiles, ct);
            replayFiles.Add(replay);
        }

        return replayFiles.OrderByDescending(r => r.LastModified).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteReplaysAsync(IEnumerable<ReplayFile> replays, CancellationToken ct = default)
    {
        return await Task.Run(
            () =>
            {
                var success = true;
                foreach (var replay in replays)
                {
                    try
                    {
                        if (File.Exists(replay.FullPath))
                        {
                            File.Delete(replay.FullPath);
                            logger.LogInformation(LogMessages.DeletedReplay, replay.FullPath);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogError(ex, LogMessages.FailedToDeleteReplay, replay.FullPath);
                        success = false;
                    }
                }

                return success;
            },
            ct);
    }

    /// <inheritdoc />
    [SuppressMessage("Security", "S4036:Command path should not be passed without validation", Justification = "Windows explorer launcher with absolute path.")]
    public void OpenInExplorer(GameType version)
    {
        var path = GetReplayDirectory(version);
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PlatformConstants.WindowsExplorerExecutable,
                Arguments = path,
                UseShellExecute = true,
            });
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Security", "S4036:Command path should not be passed without validation", Justification = "Windows explorer selection launcher with absolute file path.")]
    public void RevealInExplorer(ReplayFile replay)
    {
        if (File.Exists(replay.FullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PlatformConstants.WindowsExplorerExecutable,
                Arguments = string.Format(PlatformConstants.WindowsExplorerSelectArgument, replay.FullPath),
                UseShellExecute = true,
            });
        }
    }

    /// <inheritdoc />
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileForReplayAsync(ReplayFile replay, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);

        if (replay.MatchedClient == null && replay.Metadata?.FormattedExeCrc != null &&
            crcMappingRegistry.TryGetEntry(replay.Metadata.FormattedExeCrc, replay.Metadata.FormattedIniCrc ?? string.Empty, out var resolvedMatch))
        {
            replay.MatchedClient = resolvedMatch;
        }

        if (replay.MatchedClient == null)
        {
            return ProfileOperationResult<GameProfile>.CreateFailure(
                $"Cannot create profile for replay '{replay.FileName}': Exe CRC {replay.Metadata?.FormattedExeCrc ?? "N/A"} / INI CRC {replay.Metadata?.FormattedIniCrc ?? "N/A"} is not mapped to any known game client.");
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var profileManager = scope.ServiceProvider.GetRequiredService<IGameProfileManager>();
            var installationService = scope.ServiceProvider.GetRequiredService<IGameInstallationService>();

            // Check if matching profile already exists
            var existingProfilesResult = await profileManager.GetAllProfilesAsync(ct);
            if (existingProfilesResult.Success && existingProfilesResult.Data != null)
            {
                var existing = FindMatchingProfile(existingProfilesResult.Data, replay.GameVersion, replay.MatchedClient.ManifestId);
                if (existing != null)
                {
                    replay.MatchingProfileId = existing.Id;
                    replay.MatchingProfileName = existing.Name;
                    replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
                    return ProfileOperationResult<GameProfile>.CreateSuccess(existing);
                }
            }

            // Retrieve available installations
            var installationsResult = await installationService.GetAllInstallationsAsync(ct);
            if (!installationsResult.Success || installationsResult.Data == null || installationsResult.Data.Count == 0)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"No game installation found on this system for {replay.GameVersion}. Please ensure Generals or Zero Hour is installed.");
            }

            var installation = ResolveInstallation(installationsResult.Data, replay.GameVersion);
            if (installation == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"No game installation found on this system supporting {replay.GameVersion}.");
            }

            var request = BuildCreateProfileRequest(replay, installation);

            var createResult = await profileManager.CreateProfileAsync(request, ct);
            if (createResult.Success && createResult.Data != null)
            {
                replay.MatchingProfileId = createResult.Data.Id;
                replay.MatchingProfileName = createResult.Data.Name;
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
                logger.LogInformation("Successfully created profile '{ProfileName}' for replay '{ReplayFile}'", request.Name, replay.FileName);
                return createResult;
            }

            return ProfileOperationResult<GameProfile>.CreateFailure(
                createResult.FirstError ?? "Failed to create game profile for replay.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception creating profile for replay '{ReplayFile}'", replay.FileName);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Error creating profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ProfileOperationResult<GameLaunchInfo>> LaunchReplayAsync(ReplayFile replay, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);

        if (string.IsNullOrEmpty(replay.MatchingProfileId))
        {
            var createResult = await CreateProfileForReplayAsync(replay, ct);
            if (!createResult.Success || createResult.Data == null)
            {
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                    createResult.FirstError ?? "Failed to create or find a matching profile for this replay.");
            }
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var launcherFacade = scope.ServiceProvider.GetRequiredService<IProfileLauncherFacade>();

            var launchResult = await launcherFacade.LaunchProfileAsync(
                replay.MatchingProfileId ?? string.Empty,
                skipUserDataCleanup: false,
                cancellationToken: ct);

            if (launchResult.Success)
            {
                logger.LogInformation("Successfully launched profile '{ProfileId}' for replay '{ReplayFile}'", replay.MatchingProfileId, replay.FileName);
                return launchResult;
            }

            return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                launchResult.FirstError ?? "Failed to launch game profile.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Exception launching profile '{ProfileId}' for replay '{ReplayFile}'", replay.MatchingProfileId, replay.FileName);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}");
        }
    }

    private static GameProfile? FindMatchingProfile(IEnumerable<GameProfile> profiles, GameType gameVersion, string manifestId)
    {
        return profiles.FirstOrDefault(p =>
            p.GameClient?.GameType == gameVersion &&
            (string.Equals(p.GameClient?.Id, manifestId, StringComparison.OrdinalIgnoreCase) ||
             p.EnabledContentIds?.Any(id => string.Equals(id, manifestId, StringComparison.OrdinalIgnoreCase)) == true));
    }

    private static GameInstallation? ResolveInstallation(IReadOnlyList<GameInstallation> installations, GameType gameVersion)
    {
        return installations.FirstOrDefault(i =>
            (gameVersion == GameType.Generals && i.HasGenerals) ||
            (gameVersion == GameType.ZeroHour && i.HasZeroHour));
    }

    private static CreateProfileRequest BuildCreateProfileRequest(ReplayFile replay, GameInstallation installation)
    {
        var clientName = replay.MatchedClient?.Description ?? $"{replay.MatchedClient?.Publisher} {replay.MatchedClient?.Version}";
        var profileName = $"{clientName} (Replay: {Path.GetFileNameWithoutExtension(replay.FileName)})";

        var targetClient = replay.GameVersion == GameType.Generals ? installation.GeneralsClient : installation.ZeroHourClient;
        var targetPath = replay.GameVersion == GameType.Generals ? installation.GeneralsPath : installation.ZeroHourPath;
        var defaultExeName = replay.GameVersion == GameType.Generals
            ? GameClientConstants.GeneralsExecutable
            : (string.Equals(replay.MatchedClient?.Publisher, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase)
                ? GameClientConstants.SuperHackersZeroHourExecutable
                : GameClientConstants.ZeroHourExecutable);
        var workingDir = !string.IsNullOrEmpty(targetPath) ? targetPath : installation.InstallationPath;
        var exePath = targetClient?.ExecutablePath ?? (!string.IsNullOrEmpty(workingDir) ? Path.Combine(workingDir, defaultExeName) : string.Empty);

        var gameClient = new GameClient
        {
            Id = replay.MatchedClient?.ManifestId ?? string.Empty,
            Name = clientName,
            Version = replay.MatchedClient?.Version ?? string.Empty,
            GameType = replay.GameVersion,
            PublisherType = replay.MatchedClient?.Publisher ?? string.Empty,
            InstallationId = installation.Id,
            ExecutablePath = exePath,
            WorkingDirectory = workingDir,
        };

        return new CreateProfileRequest
        {
            Name = profileName,
            Description = $"Profile configured for {clientName} (Exe: {replay.Metadata?.FormattedExeCrc}, INI: {replay.Metadata?.FormattedIniCrc})",
            GameInstallationId = installation.Id,
            GameClientId = replay.MatchedClient?.ManifestId ?? string.Empty,
            GameClient = gameClient,
            EnabledContentIds = !string.IsNullOrEmpty(replay.MatchedClient?.ManifestId) ? [replay.MatchedClient.ManifestId] : [],
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            UseSteamLaunch = installation.InstallationType == GameInstallationType.Steam,
        };
    }

    private async Task<(HashSet<string> AcquiredIds, List<GameProfile> Profiles)> FetchAcquiredManifestIdsAndProfilesAsync(CancellationToken ct)
    {
        var acquiredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingProfiles = new List<GameProfile>();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var manifestPool = scope.ServiceProvider.GetService<IContentManifestPool>();
            if (manifestPool != null)
            {
                var manifestsResult = await manifestPool.GetAllManifestsAsync(ct);
                if (manifestsResult.Success && manifestsResult.Data != null)
                {
                    foreach (var manifest in manifestsResult.Data)
                    {
                        acquiredIds.Add(manifest.Id.Value);
                    }
                }
            }

            var profileManager = scope.ServiceProvider.GetService<IGameProfileManager>();
            if (profileManager != null)
            {
                var profilesResult = await profileManager.GetAllProfilesAsync(ct);
                if (profilesResult.Success && profilesResult.Data != null)
                {
                    existingProfiles.AddRange(profilesResult.Data);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to retrieve acquired manifests or profiles for replay compatibility matching.");
        }

        return (acquiredIds, existingProfiles);
    }

    private async Task<ReplayFile> ProcessReplayFileAsync(
        string file,
        GameType version,
        HashSet<string> acquiredIds,
        IReadOnlyList<GameProfile> existingProfiles,
        CancellationToken ct)
    {
        var info = new FileInfo(file);
        var replay = new ReplayFile
        {
            FullPath = file,
            FileName = Path.GetFileName(file),
            SizeInBytes = info.Length,
            LastModified = info.LastWriteTime,
            GameVersion = version,
        };

        if (file.EndsWith(".rep", StringComparison.OrdinalIgnoreCase))
        {
            var parseResult = await headerParser.ParseHeaderAsync(file, ct);
            if (parseResult.Success && parseResult.Data != null)
            {
                replay.Metadata = parseResult.Data;
                ResolveCompatibility(replay, acquiredIds, existingProfiles);
            }
        }

        return replay;
    }

    private void ResolveCompatibility(ReplayFile replay, HashSet<string> acquiredIds, IReadOnlyList<GameProfile> profiles)
    {
        if (replay.Metadata == null)
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        var exeCrcStr = replay.Metadata.FormattedExeCrc;
        var iniCrcStr = replay.Metadata.FormattedIniCrc;

        if (string.IsNullOrEmpty(exeCrcStr))
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        if (crcMappingRegistry.TryGetEntry(exeCrcStr, iniCrcStr ?? string.Empty, out var match) && match != null)
        {
            replay.MatchedClient = match;

            // Check if an existing profile matches this game client / manifest
            var matchingProfile = FindMatchingProfile(profiles, replay.GameVersion, match.ManifestId);
            if (matchingProfile != null)
            {
                replay.MatchingProfileId = matchingProfile.Id;
                replay.MatchingProfileName = matchingProfile.Name;
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
                return;
            }

            // No profile configured yet. Check if the manifest is installed / acquired
            var isInstalled = !string.IsNullOrEmpty(match.ManifestId) && acquiredIds.Contains(match.ManifestId);
            if (isInstalled)
            {
                replay.CompatibilityStatus = ReplayCompatibilityStatus.RequiresProfile;
            }
            else if (!string.IsNullOrWhiteSpace(match.CdnUrl))
            {
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Downloadable;
            }
            else
            {
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
            }
        }
        else
        {
            // Neither exact pair nor known match exists -> replay will mismatch as no compatible CRC was found
            replay.MatchedClient = null;
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
        }
    }
}
