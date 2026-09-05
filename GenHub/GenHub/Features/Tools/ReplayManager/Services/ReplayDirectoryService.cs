using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
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

        EnsureReplayMatch(replay);

        var isUnmappedReplay = replay.MatchedClient == null;
        if (isUnmappedReplay)
        {
            logger.LogInformation(
                "[ReplayManager] Replay '{ReplayFile}' (Exe: {ExeCrc}, INI: {IniCrc}) is unmapped; creating profile using base {GameVersion} installation",
                replay.FileName,
                replay.Metadata?.FormattedExeCrc ?? "N/A",
                replay.Metadata?.FormattedIniCrc ?? "N/A",
                replay.GameVersion);
        }
        else
        {
            logger.LogInformation(
                "[ReplayManager] Creating profile for replay '{ReplayFile}' matched to {MatchedDescription} (Publisher: {Publisher}, Version: {Version})",
                replay.FileName,
                replay.MatchedClient?.Description,
                replay.MatchedClient?.Publisher,
                replay.MatchedClient?.Version);
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var (installation, installError) = await ResolveAndPrepareInstallationAsync(sp, replay, ct);
            if (installation == null)
            {
                logger.LogError("[ReplayManager] Installation resolution failed for '{ReplayFile}': {Error}", replay.FileName, installError);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    installError ?? $"No game installation found on this system supporting {replay.GameVersion}.");
            }

            logger.LogInformation(
                "[ReplayManager] Selected installation {InstallationId} ({InstallationType}) for replay '{ReplayFile}'",
                installation.Id,
                installation.InstallationType,
                replay.FileName);

            var manifestPool = sp.GetRequiredService<IContentManifestPool>();
            var contentOrchestrator = sp.GetService<IContentOrchestrator>();
            var profileManager = sp.GetRequiredService<IGameProfileManager>();

            var defaultVersion = replay.GameVersion == GameType.ZeroHour
                ? ManifestConstants.ZeroHourManifestVersion
                : ManifestConstants.GeneralsManifestVersion;

            var installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(
                installation, replay.GameVersion, defaultVersion);

            var isRetailClient = isUnmappedReplay ||
                                 string.IsNullOrWhiteSpace(replay.MatchedClient?.Publisher) ||
                                 string.Equals(replay.MatchedClient?.Publisher, "ea", StringComparison.OrdinalIgnoreCase);

            var (clientManifestId, gameClient) = await ResolveReplayGameClientAsync(
                installation, replay, defaultVersion, isRetailClient, manifestPool, contentOrchestrator, ct);

            if (gameClient == null || string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
            {
                logger.LogError("[ReplayManager] Could not determine executable path for {GameVersion} installation", replay.GameVersion);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Could not determine executable path for {replay.GameVersion} installation.");
            }

            var enabledContentIds = await GatherEnabledContentIdsAsync(
                manifestPool, contentOrchestrator, replay, installationManifestId, clientManifestId, isRetailClient, ct);

            logger.LogInformation(
                "[ReplayManager] Gathered {Count} enabled content IDs for replay profile: [{ContentIds}]",
                enabledContentIds.Count,
                string.Join(", ", enabledContentIds));

            var request = BuildReplayProfileRequest(replay, installation, clientManifestId, gameClient, enabledContentIds);

            var createResult = await profileManager.CreateProfileAsync(request, ct);
            if (createResult.Success && createResult.Data != null)
            {
                replay.MatchingProfileId = createResult.Data.Id;
                replay.MatchingProfileName = createResult.Data.Name;
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
                logger.LogInformation(
                    "[ReplayManager] Successfully created profile '{ProfileName}' (ID: {ProfileId}) for replay '{ReplayFile}'",
                    createResult.Data.Name,
                    createResult.Data.Id,
                    replay.FileName);
                return createResult;
            }

            logger.LogError(
                "[ReplayManager] Failed to create game profile for replay '{ReplayFile}': {Error}",
                replay.FileName,
                createResult.FirstError);
            return ProfileOperationResult<GameProfile>.CreateFailure(
                createResult.FirstError ?? "Failed to create game profile for replay.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[ReplayManager] Exception creating profile for replay '{ReplayFile}'", replay.FileName);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Error creating profile: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<ProfileOperationResult<GameLaunchInfo>> LaunchReplayAsync(ReplayFile replay, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);

        logger.LogInformation(
            "[ReplayManager] Starting replay launch workflow for '{ReplayFile}' (GameVersion: {GameVersion}, ProfileId: {ProfileId})",
            replay.FileName,
            replay.GameVersion,
            replay.MatchingProfileId ?? "none");

        if (string.IsNullOrEmpty(replay.MatchingProfileId))
        {
            logger.LogInformation("[ReplayManager] No matching profile associated with '{ReplayFile}', creating one now...", replay.FileName);
            var createResult = await CreateProfileForReplayAsync(replay, ct);
            if (!createResult.Success || createResult.Data == null)
            {
                logger.LogError("[ReplayManager] Profile creation failed for '{ReplayFile}': {Error}", replay.FileName, createResult.FirstError);
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                    createResult.FirstError ?? "Failed to create or find a matching profile for this replay.");
            }
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var launcherFacade = scope.ServiceProvider.GetRequiredService<IProfileLauncherFacade>();

            logger.LogInformation(
                "[ReplayManager] Launching profile '{ProfileId}' for replay '{ReplayFile}'...",
                replay.MatchingProfileId,
                replay.FileName);

            var launchResult = await launcherFacade.LaunchProfileAsync(
                replay.MatchingProfileId ?? string.Empty,
                skipUserDataCleanup: false,
                cancellationToken: ct);

            if (launchResult.Success)
            {
                logger.LogInformation(
                    "[ReplayManager] Successfully launched profile '{ProfileId}' for replay '{ReplayFile}'",
                    replay.MatchingProfileId,
                    replay.FileName);
                return launchResult;
            }

            logger.LogError(
                "[ReplayManager] Launch failed for profile '{ProfileId}' (Replay: '{ReplayFile}'): {Error}",
                replay.MatchingProfileId,
                replay.FileName,
                launchResult.FirstError);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                launchResult.FirstError ?? "Failed to launch game profile.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[ReplayManager] Exception launching profile '{ProfileId}' for replay '{ReplayFile}'", replay.MatchingProfileId, replay.FileName);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}");
        }
    }

    private static async Task<(GameInstallation? Installation, string? Error)> ResolveAndPrepareInstallationAsync(
        IServiceProvider sp, ReplayFile replay, CancellationToken ct)
    {
        var installationService = sp.GetRequiredService<IGameInstallationService>();
        var installationsResult = await installationService.GetAllInstallationsAsync(ct);
        if (!installationsResult.Success || installationsResult.Data == null || installationsResult.Data.Count == 0)
        {
            return (null, $"No game installation found on this system for {replay.GameVersion}. Please ensure Generals or Zero Hour is installed.");
        }

        var installation = ResolveInstallation(installationsResult.Data, replay.GameVersion);
        if (installation == null)
        {
            return (null, $"No game installation found on this system supporting {replay.GameVersion}.");
        }

        await installationService.CreateAndRegisterInstallationManifestsAsync(installation, ct);
        var clientDetector = sp.GetService<IGameClientDetector>();
        if (clientDetector != null)
        {
            await clientDetector.DetectGameClientsFromInstallationsAsync([installation], ct);
        }

        return (installation, null);
    }

    private static async Task<List<string>> GatherEnabledContentIdsAsync(
        IContentManifestPool manifestPool,
        IContentOrchestrator? contentOrchestrator,
        ReplayFile replay,
        string installationManifestId,
        string clientManifestId,
        bool isRetailClient,
        CancellationToken ct)
    {
        var enabledContentIds = new List<string>();

        if (!string.IsNullOrWhiteSpace(installationManifestId))
        {
            enabledContentIds.Add(installationManifestId);
        }

        if (!string.IsNullOrWhiteSpace(clientManifestId) && !enabledContentIds.Contains(clientManifestId, StringComparer.OrdinalIgnoreCase))
        {
            enabledContentIds.Add(clientManifestId);
        }

        if (!isRetailClient && replay.MatchedClient != null)
        {
            await AddThirdPartyCompanionManifestsAsync(manifestPool, replay.MatchedClient.Publisher, enabledContentIds, ct);
        }

        if (replay.MatchedClient != null &&
            !string.IsNullOrEmpty(replay.MatchedClient.DataPatchManifestId))
        {
            var rawDataPatchId = replay.MatchedClient.DataPatchManifestId;

            // Try acquiring data patch if not present in pool
            await AcquireDataPatchIfMissingAsync(
                contentOrchestrator, manifestPool, rawDataPatchId, replay.MatchedClient.Publisher, replay.GameVersion, ct);

            // Verify if data patch exists in manifest pool (exact ID or compatible identity)
            var resolvedDataPatchId = await ResolveExistingPatchManifestIdAsync(manifestPool, rawDataPatchId, replay.MatchedClient.Publisher, replay.GameVersion, replay.MatchedClient.Version, ct);
            if (!string.IsNullOrEmpty(resolvedDataPatchId) && !enabledContentIds.Contains(resolvedDataPatchId, StringComparer.OrdinalIgnoreCase))
            {
                enabledContentIds.Add(resolvedDataPatchId);
            }
        }

        return enabledContentIds;
    }

    private static async Task<string?> ResolveExistingPatchManifestIdAsync(
        IContentManifestPool manifestPool,
        string dataPatchManifestId,
        string? publisher,
        GameType gameVersion,
        string? clientVersion,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(dataPatchManifestId))
        {
            return null;
        }

        var exactResult = await manifestPool.GetManifestAsync(ManifestId.Create(dataPatchManifestId), ct);
        if (exactResult.Success && exactResult.Data != null)
        {
            return exactResult.Data.Id.Value;
        }

        var allManifestsResult = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifestsResult.Success && allManifestsResult.Data != null)
        {
            var patchManifest = allManifestsResult.Data.FirstOrDefault(m =>
                m.TargetGame == gameVersion &&
                (m.ContentType == ContentType.Patch || m.ContentType == ContentType.MapPack) &&
                (string.Equals(m.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                 m.Dependencies?.Any(d => string.Equals(d.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase)) == true ||
                 (!string.IsNullOrEmpty(publisher) &&
                  string.Equals(m.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) &&
                  (!string.IsNullOrEmpty(clientVersion) && string.Equals(m.Version?.ToString(), clientVersion, StringComparison.OrdinalIgnoreCase)))));

            if (patchManifest != null)
            {
                return patchManifest.Id.Value;
            }
        }

        return null;
    }

    private static string GetReplayClientTitle(ReplayFile replay)
    {
        if (replay.MatchedClient != null)
        {
            return replay.MatchedClient.Description ?? replay.MatchedClient.Publisher ?? "Game";
        }

        return replay.GameVersion == GameType.ZeroHour ? "Zero Hour" : "Generals";
    }

    private static CreateProfileRequest BuildReplayProfileRequest(
        ReplayFile replay,
        GameInstallation installation,
        string clientManifestId,
        GameClient gameClient,
        List<string> enabledContentIds)
    {
        var isUnmapped = replay.MatchedClient == null;
        var clientTitle = GetReplayClientTitle(replay);

        var profileName = $"{clientTitle} (Replay: {Path.GetFileNameWithoutExtension(replay.FileName)})";
        var description = isUnmapped
            ? $"Profile configured for unmapped replay {replay.FileName} (Exe: {replay.Metadata?.FormattedExeCrc ?? "N/A"}, INI: {replay.Metadata?.FormattedIniCrc ?? "N/A"})"
            : $"Profile configured for {replay.MatchedClient?.Description} (Exe: {replay.Metadata?.FormattedExeCrc}, INI: {replay.Metadata?.FormattedIniCrc})";

        return new CreateProfileRequest
        {
            Name = profileName,
            Description = description,
            GameInstallationId = installation.Id,
            GameClientId = clientManifestId,
            GameClient = gameClient,
            EnabledContentIds = enabledContentIds,
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            UseSteamLaunch = installation.InstallationType == GameInstallationType.Steam,
        };
    }

    private static GameProfile? FindMatchingProfile(
        IEnumerable<GameProfile> profiles,
        GameType gameVersion,
        string clientManifestId,
        string? dataPatchManifestId,
        ReplayFile? replay = null)
    {
        var profileList = profiles.ToList();

        // 1. Direct replay profile match by ID or Description
        if (replay != null)
        {
            var byIdOrName = profileList.FirstOrDefault(p =>
                p.GameClient?.GameType == gameVersion &&
                ((!string.IsNullOrEmpty(replay.MatchingProfileId) && string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(p.Description) && p.Description.Contains(replay.FileName, StringComparison.OrdinalIgnoreCase))));

            if (byIdOrName != null)
            {
                return byIdOrName;
            }
        }

        // 2. Compatibility match based on client and patch IDs
        var isRetailClient = clientManifestId.Contains(ReplayManagerConstants.RetailManifestSegment, StringComparison.OrdinalIgnoreCase) ||
                             clientManifestId.Contains(".steam.", StringComparison.OrdinalIgnoreCase) ||
                             clientManifestId.Contains(".eaapp.", StringComparison.OrdinalIgnoreCase);

        return profileList.FirstOrDefault(p =>
        {
            if (p.GameClient?.GameType != gameVersion)
            {
                return false;
            }

            return isRetailClient
                ? IsProfileMatchingRetail(p, dataPatchManifestId)
                : IsProfileMatchingThirdParty(p, clientManifestId, dataPatchManifestId);
        });
    }

    private static bool IsProfileMatchingRetail(GameProfile profile, string? dataPatchManifestId)
    {
        var superHackersSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.TheSuperHackers}{ManifestConstants.ManifestIdSegmentSeparator}";
        var legacySuperHackersSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}superhackers{ManifestConstants.ManifestIdSegmentSeparator}";
        var generalsOnlineSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.GeneralsOnline}{ManifestConstants.ManifestIdSegmentSeparator}";

        var isProfileThirdParty = string.Equals(profile.GameClient?.PublisherType, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(profile.GameClient?.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                                  (profile.GameClient?.Id != null &&
                                   (profile.GameClient.Id.Contains(superHackersSegment, StringComparison.OrdinalIgnoreCase) ||
                                    profile.GameClient.Id.Contains(legacySuperHackersSegment, StringComparison.OrdinalIgnoreCase) ||
                                    profile.GameClient.Id.Contains(generalsOnlineSegment, StringComparison.OrdinalIgnoreCase))) ||
                                  profile.EnabledContentIds?.Any(id =>
                                      id.Contains(superHackersSegment, StringComparison.OrdinalIgnoreCase) ||
                                      id.Contains(legacySuperHackersSegment, StringComparison.OrdinalIgnoreCase) ||
                                      id.Contains(generalsOnlineSegment, StringComparison.OrdinalIgnoreCase)) == true;

        if (isProfileThirdParty)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(dataPatchManifestId))
        {
            return profile.EnabledContentIds?.Any(id => string.Equals(id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase)) == true;
        }

        var hasCustomDataPatch = profile.EnabledContentIds?.Any(id =>
            id.Contains(".gamedata.", StringComparison.OrdinalIgnoreCase) ||
            id.Contains(".datapatch.", StringComparison.OrdinalIgnoreCase) ||
            id.Contains(".community.", StringComparison.OrdinalIgnoreCase) ||
            id.Contains(".mod.", StringComparison.OrdinalIgnoreCase)) == true;

        return !hasCustomDataPatch;
    }

    private static bool IsProfileMatchingThirdParty(GameProfile profile, string clientManifestId, string? dataPatchManifestId)
    {
        var clientMatches = string.Equals(profile.GameClient?.Id, clientManifestId, StringComparison.OrdinalIgnoreCase) ||
                            profile.EnabledContentIds?.Any(id => string.Equals(id, clientManifestId, StringComparison.OrdinalIgnoreCase)) == true;

        var publisher = ExtractPublisherFromManifestId(clientManifestId);
        if (!clientMatches &&
            !string.IsNullOrEmpty(publisher) &&
            (string.Equals(profile.GameClient?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
             profile.EnabledContentIds?.Any(id => id.Contains("." + publisher + ".", StringComparison.OrdinalIgnoreCase)) == true))
        {
            clientMatches = true;
        }

        if (!clientMatches)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(dataPatchManifestId))
        {
            var patchPublisher = ExtractPublisherFromManifestId(dataPatchManifestId);
            return profile.EnabledContentIds?.Any(id =>
                string.Equals(id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(patchPublisher) &&
                 id.Contains("." + patchPublisher + ".", StringComparison.OrdinalIgnoreCase) &&
                 (id.Contains(".patch.", StringComparison.OrdinalIgnoreCase) ||
                  id.Contains(".gamedata.", StringComparison.OrdinalIgnoreCase) ||
                  id.Contains(".datapatch.", StringComparison.OrdinalIgnoreCase)))) == true;
        }

        return true;
    }

    private static string ExtractPublisherFromManifestId(string manifestId)
    {
        if (string.IsNullOrWhiteSpace(manifestId))
        {
            return string.Empty;
        }

        var segments = manifestId.Split(ManifestConstants.ManifestIdSegmentSeparator);
        if (segments.Length >= 3)
        {
            return segments[2];
        }

        if (manifestId.Contains(".generalsonline.", StringComparison.OrdinalIgnoreCase))
        {
            return PublisherTypeConstants.GeneralsOnline;
        }

        if (manifestId.Contains(".superhackers.", StringComparison.OrdinalIgnoreCase))
        {
            return PublisherTypeConstants.TheSuperHackers;
        }

        return string.Empty;
    }

    private static GameInstallation? ResolveInstallation(IReadOnlyList<GameInstallation> installations, GameType gameVersion)
    {
        return installations.FirstOrDefault(i =>
            (gameVersion == GameType.Generals && i.HasGenerals) ||
            (gameVersion == GameType.ZeroHour && i.HasZeroHour));
    }

    private static string GetDefaultExecutableName(GameType gameVersion, string? publisher)
    {
        if (gameVersion == GameType.Generals)
        {
            return GameClientConstants.GeneralsExecutable;
        }

        if (string.Equals(publisher, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase))
        {
            return GameClientConstants.SuperHackersZeroHourExecutable;
        }

        return GameClientConstants.ZeroHourExecutable;
    }

    private static bool IsClientManifestInstalled(CrcMappingEntry match, GameType gameVersion, HashSet<string> acquiredIds)
    {
        if (!string.IsNullOrEmpty(match.ManifestId) && acquiredIds.Contains(match.ManifestId))
        {
            return true;
        }

        var publisher = !string.IsNullOrWhiteSpace(match.Publisher)
            ? match.Publisher
            : ExtractPublisherFromManifestId(match.ManifestId);

        var isRetail = string.IsNullOrWhiteSpace(publisher) ||
                       string.Equals(publisher, "ea", StringComparison.OrdinalIgnoreCase) ||
                       match.ManifestId.Contains(ReplayManagerConstants.RetailManifestSegment, StringComparison.OrdinalIgnoreCase);

        if (isRetail)
        {
            var gameTypeSuffix = gameVersion == GameType.ZeroHour ? "zerohour" : "generals";
            return acquiredIds.Any(id => id.Contains(".gameinstallation.", StringComparison.OrdinalIgnoreCase) && id.EndsWith(gameTypeSuffix, StringComparison.OrdinalIgnoreCase));
        }

        // Third-party publisher: check if any gameclient manifest for this publisher exists in acquiredIds
        return acquiredIds.Any(id =>
            id.Contains("." + publisher + ".", StringComparison.OrdinalIgnoreCase) &&
            id.Contains(".gameclient.", StringComparison.OrdinalIgnoreCase));
    }

    private static ReplayCompatibilityStatus DetermineUnconfiguredStatus(CrcMappingEntry match, bool isInstalled)
    {
        if (isInstalled)
        {
            return ReplayCompatibilityStatus.RequiresProfile;
        }

        var isRetail = string.IsNullOrWhiteSpace(match.Publisher) ||
                       string.Equals(match.Publisher, "ea", StringComparison.OrdinalIgnoreCase) ||
                       match.ManifestId.Contains(ReplayManagerConstants.RetailManifestSegment, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(match.CdnUrl) || !isRetail)
        {
            return ReplayCompatibilityStatus.Downloadable;
        }

        return ReplayCompatibilityStatus.Orphaned;
    }

    private static void ResolveMatchedClientCompatibility(
        ReplayFile replay,
        CrcMappingEntry match,
        HashSet<string> acquiredIds,
        IReadOnlyList<GameProfile> profiles)
    {
        replay.MatchedClient = match;

        var matchingProfile = FindMatchingProfile(profiles, replay.GameVersion, match.ManifestId, match.DataPatchManifestId, replay);
        if (matchingProfile != null)
        {
            replay.MatchingProfileId = matchingProfile.Id;
            replay.MatchingProfileName = matchingProfile.Name;
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
            return;
        }

        var isInstalled = IsClientManifestInstalled(match, replay.GameVersion, acquiredIds);
        replay.CompatibilityStatus = DetermineUnconfiguredStatus(match, isInstalled);
    }

    private static async Task<string> ResolveThirdPartyClientManifestIdAsync(
        IContentManifestPool manifestPool,
        string manifestId,
        string? publisher,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(manifestId))
        {
            return string.Empty;
        }

        var exactCheck = await manifestPool.GetManifestAsync(ManifestId.Create(manifestId), ct);
        if (exactCheck != null && exactCheck.Success && exactCheck.Data != null)
        {
            return exactCheck.Data.Id.Value;
        }

        if (string.IsNullOrEmpty(publisher))
        {
            return manifestId;
        }

        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifests != null && allManifests.Success && allManifests.Data != null)
        {
            var providerClient = allManifests.Data.FirstOrDefault(m =>
                m.ContentType == ContentType.GameClient &&
                (string.Equals(m.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
                 m.Id.Value.Contains("." + publisher + ".")));
            if (providerClient != null)
            {
                return providerClient.Id.Value;
            }
        }

        return manifestId;
    }

    private static async Task AddThirdPartyCompanionManifestsAsync(
        IContentManifestPool manifestPool,
        string? publisher,
        List<string> enabledContentIds,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(publisher))
        {
            return;
        }

        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifests == null || !allManifests.Success || allManifests.Data == null)
        {
            return;
        }

        var companionManifests = allManifests.Data.Where(m =>
            (m.ContentType == ContentType.Patch || m.ContentType == ContentType.MapPack) &&
            (string.Equals(m.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
             m.Id.Value.Contains("." + publisher + ".")));

        foreach (var companion in companionManifests.Where(companion => !enabledContentIds.Contains(companion.Id.Value)))
        {
            enabledContentIds.Add(companion.Id.Value);
        }
    }

    private static async Task AcquireGeneralsOnlineMapPacksAsync(IContentOrchestrator contentOrchestrator, IContentManifestPool manifestPool, CancellationToken ct)
    {
        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifests.Success && allManifests.Data != null &&
            allManifests.Data.Any(m => m.ContentType == ContentType.MapPack &&
                                       (string.Equals(m.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
                                        m.Id.Value.Contains("." + GeneralsOnlineConstants.PublisherType + "."))))
        {
            return;
        }

        var mapPackQuery = new ContentSearchQuery
        {
            ProviderName = GeneralsOnlineConstants.PublisherType,
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };
        var mapPackResult = await contentOrchestrator.SearchAsync(mapPackQuery, ct);
        if (mapPackResult != null && mapPackResult.Success && mapPackResult.Data != null)
        {
            foreach (var item in mapPackResult.Data)
            {
                await contentOrchestrator.AcquireContentAsync(item, null, ct);
            }
        }
    }

    private static async Task AcquireDataPatchIfMissingAsync(
        IContentOrchestrator? contentOrchestrator,
        IContentManifestPool manifestPool,
        string dataPatchManifestId,
        string? publisher,
        GameType gameVersion,
        CancellationToken ct)
    {
        if (contentOrchestrator == null || string.IsNullOrEmpty(dataPatchManifestId))
        {
            return;
        }

        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifests.Success && allManifests.Data != null)
        {
            var alreadyExists = allManifests.Data.Any(m =>
                m.TargetGame == gameVersion &&
                (string.Equals(m.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrEmpty(publisher) &&
                  (m.ContentType == ContentType.Patch || m.ContentType == ContentType.MapPack) &&
                  (string.Equals(m.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
                   m.Id.Value.Contains("." + publisher + ".")))));

            if (alreadyExists)
            {
                return;
            }
        }

        var dataPatchQuery = new ContentSearchQuery
        {
            ProviderName = publisher,
            ContentType = ContentType.Patch,
            TargetGame = gameVersion,
        };
        var dataPatchSearch = await contentOrchestrator.SearchAsync(dataPatchQuery, ct);
        if (dataPatchSearch != null && dataPatchSearch.Success && dataPatchSearch.Data != null)
        {
            var patchMatch = dataPatchSearch.Data.FirstOrDefault(c =>
                string.Equals(c.Id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase))
                ?? dataPatchSearch.Data.FirstOrDefault();

            if (patchMatch != null)
            {
                await contentOrchestrator.AcquireContentAsync(patchMatch, null, ct);
            }
        }
    }

    private static (string ClientManifestId, GameClient GameClient) CreateRetailGameClient(
        GameInstallation installation,
        ReplayFile replay,
        string defaultVersion,
        string exePath,
        string workingDir,
        GameClient? targetClient)
    {
        var defaultVersionInt = replay.GameVersion == GameType.ZeroHour ? 104 : 108;
        var gameTypeName = replay.GameVersion == GameType.ZeroHour ? "zerohour" : "generals";
        var clientManifestId = targetClient?.Id ?? ManifestIdGenerator.GeneratePublisherContentId(
            installation.InstallationType.ToIdentifierString(),
            ContentType.GameClient,
            gameTypeName,
            defaultVersionInt);

        var clientName = replay.MatchedClient?.Description ?? $"{replay.MatchedClient?.Publisher} {replay.MatchedClient?.Version}";
        var gameClient = targetClient ?? new GameClient
        {
            Id = clientManifestId,
            Name = clientName,
            Version = defaultVersion,
            GameType = replay.GameVersion,
            PublisherType = installation.InstallationType.ToIdentifierString(),
            InstallationId = installation.Id,
            ExecutablePath = exePath,
            WorkingDirectory = workingDir,
        };

        if (string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
        {
            gameClient.ExecutablePath = exePath;
        }

        if (string.IsNullOrWhiteSpace(gameClient.WorkingDirectory))
        {
            gameClient.WorkingDirectory = workingDir;
        }

        if (string.IsNullOrWhiteSpace(gameClient.PublisherType))
        {
            gameClient.PublisherType = installation.InstallationType.ToIdentifierString();
        }

        return (clientManifestId, gameClient);
    }

    private static bool IsClientAlreadyAcquired(
        IEnumerable<ContentManifest> existingManifests,
        CrcMappingEntry matchedClient,
        GameType gameVersion)
    {
        return existingManifests.Any(m =>
            string.Equals(m.Id.Value, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(matchedClient.Publisher) &&
             m.ContentType == ContentType.GameClient &&
             m.TargetGame == gameVersion &&
             string.Equals(m.Publisher?.PublisherType, matchedClient.Publisher, StringComparison.OrdinalIgnoreCase) &&
             (!string.IsNullOrEmpty(matchedClient.Version) && string.Equals(m.Version?.ToString(), matchedClient.Version, StringComparison.OrdinalIgnoreCase))));
    }

    private static ContentSearchResult? FindBestMatchingContentSearchResult(
        IEnumerable<ContentSearchResult> items,
        CrcMappingEntry matchedClient)
    {
        return items.FirstOrDefault(c =>
            string.Equals(c.Id, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Version, matchedClient.Version, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault();
    }

    private async Task<(string ClientManifestId, GameClient? GameClient)> ResolveReplayGameClientAsync(
        GameInstallation installation,
        ReplayFile replay,
        string defaultVersion,
        bool isRetailClient,
        IContentManifestPool manifestPool,
        IContentOrchestrator? contentOrchestrator,
        CancellationToken ct)
    {
        var targetClient = replay.GameVersion == GameType.Generals ? installation.GeneralsClient : installation.ZeroHourClient;
        var targetPath = replay.GameVersion == GameType.Generals ? installation.GeneralsPath : installation.ZeroHourPath;
        var defaultExeName = GetDefaultExecutableName(replay.GameVersion, replay.MatchedClient?.Publisher);
        var workingDir = !string.IsNullOrEmpty(targetPath) ? targetPath : installation.InstallationPath;
        var exePath = targetClient?.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(workingDir))
        {
            exePath = Path.Combine(workingDir, defaultExeName);
        }

        if (string.IsNullOrWhiteSpace(exePath))
        {
            return (string.Empty, null);
        }

        return isRetailClient
            ? CreateRetailGameClient(installation, replay, defaultVersion, exePath, workingDir, targetClient)
            : await ResolveThirdPartyGameClientAsync(installation, replay, defaultVersion, exePath, workingDir, manifestPool, contentOrchestrator, ct);
    }

    private async Task<(string ClientManifestId, GameClient GameClient)> ResolveThirdPartyGameClientAsync(
        GameInstallation installation,
        ReplayFile replay,
        string defaultVersion,
        string exePath,
        string workingDir,
        IContentManifestPool manifestPool,
        IContentOrchestrator? contentOrchestrator,
        CancellationToken ct)
    {
        var thirdPartyManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        if (replay.MatchedClient != null)
        {
            await AcquireThirdPartyClientAndDependenciesAsync(contentOrchestrator, manifestPool, replay.MatchedClient, replay.GameVersion, ct);
            thirdPartyManifestId = await ResolveThirdPartyClientManifestIdAsync(manifestPool, thirdPartyManifestId, replay.MatchedClient.Publisher, ct);
        }

        var thirdPartyClientName = replay.MatchedClient?.Description ?? $"{replay.MatchedClient?.Publisher} {replay.MatchedClient?.Version}";
        var thirdPartyGameClient = new GameClient
        {
            Id = thirdPartyManifestId,
            Name = thirdPartyClientName,
            Version = replay.MatchedClient?.Version ?? defaultVersion,
            GameType = replay.GameVersion,
            PublisherType = replay.MatchedClient?.Publisher ?? string.Empty,
            InstallationId = installation.Id,
            ExecutablePath = exePath,
            WorkingDirectory = workingDir,
        };

        return (thirdPartyManifestId, thirdPartyGameClient);
    }

    private async Task AcquireThirdPartyClientAndDependenciesAsync(
        IContentOrchestrator? contentOrchestrator,
        IContentManifestPool manifestPool,
        CrcMappingEntry matchedClient,
        GameType gameVersion,
        CancellationToken ct)
    {
        if (contentOrchestrator == null || string.IsNullOrEmpty(matchedClient.ManifestId))
        {
            return;
        }

        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        var existingManifests = allManifests.Success && allManifests.Data != null ? allManifests.Data : [];

        if (IsClientAlreadyAcquired(existingManifests, matchedClient, gameVersion))
        {
            logger.LogDebug("[ReplayManager] Client for publisher '{Publisher}' already acquired in manifest pool, skipping download.", matchedClient.Publisher);
            return;
        }

        logger.LogInformation("Downloading and acquiring client manifest {ManifestId} from {Publisher}...", matchedClient.ManifestId, matchedClient.Publisher);
        var searchQuery = new ContentSearchQuery
        {
            ProviderName = matchedClient.Publisher,
            ContentType = ContentType.GameClient,
            TargetGame = gameVersion,
        };
        var searchResult = await contentOrchestrator.SearchAsync(searchQuery, ct);
        if (searchResult?.Success == true && searchResult.Data != null)
        {
            var match = FindBestMatchingContentSearchResult(searchResult.Data, matchedClient);
            if (match != null)
            {
                var acquireResult = await contentOrchestrator.AcquireContentAsync(match, null, ct);
                if (acquireResult != null && !acquireResult.Success)
                {
                    logger.LogWarning("Failed to acquire client manifest {ManifestId}: {Error}", matchedClient.ManifestId, acquireResult.FirstError);
                }
            }
        }

        // If GeneralsOnline, also ensure MapPack is acquired if missing
        if (string.Equals(matchedClient.Publisher, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            await AcquireGeneralsOnlineMapPacksAsync(contentOrchestrator, manifestPool, ct);
        }
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
        if (replay.Metadata == null || string.IsNullOrEmpty(replay.Metadata.FormattedExeCrc))
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        var exeCrcStr = replay.Metadata.FormattedExeCrc;
        var iniCrcStr = replay.Metadata.FormattedIniCrc ?? string.Empty;

        if (crcMappingRegistry.TryGetEntry(exeCrcStr, iniCrcStr, out var match) && match != null)
        {
            ResolveMatchedClientCompatibility(replay, match, acquiredIds, profiles);
        }
        else
        {
            replay.MatchedClient = null;
            var unmappedProfile = profiles.FirstOrDefault(p =>
                p.GameClient?.GameType == replay.GameVersion &&
                ((!string.IsNullOrEmpty(replay.MatchingProfileId) && string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase)) ||
                 (!string.IsNullOrEmpty(p.Description) && p.Description.Contains(replay.FileName, StringComparison.OrdinalIgnoreCase))));

            if (unmappedProfile != null)
            {
                replay.MatchingProfileId = unmappedProfile.Id;
                replay.MatchingProfileName = unmappedProfile.Name;
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
            }
            else
            {
                replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
            }
        }
    }

    private void EnsureReplayMatch(ReplayFile replay)
    {
        if (replay.MatchedClient == null && replay.Metadata?.FormattedExeCrc != null &&
            crcMappingRegistry.TryGetEntry(replay.Metadata.FormattedExeCrc, replay.Metadata.FormattedIniCrc ?? string.Empty, out var resolvedMatch))
        {
            replay.MatchedClient = resolvedMatch;
        }
    }
}
