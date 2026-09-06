using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
using GenHub.Features.GameProfiles.Services;
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
    private static readonly TimeSpan ReplayFileNameRegexTimeout = TimeSpan.FromMilliseconds(250);

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

        var files = await Task.Run(
            () =>
            {
                if (!Directory.Exists(directory))
                {
                    return [];
                }

                return Directory.GetFiles(directory, "*.*")
                    .Where(f => f.EndsWith(ReplayManagerConstants.ReplayFileExtension, StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(ReplayManagerConstants.ZipFileExtension, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            },
            ct);

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
                                 string.Equals(replay.MatchedClient?.Publisher, PublisherTypeConstants.Ea, StringComparison.OrdinalIgnoreCase);

            var (clientManifestId, gameClient) = await ResolveReplayGameClientAsync(
                installation, replay, defaultVersion, isRetailClient, manifestPool, contentOrchestrator, ct);

            if (gameClient == null || string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
            {
                logger.LogError("[ReplayManager] Could not determine executable path for {GameVersion} installation", replay.GameVersion);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Could not determine executable path for {replay.GameVersion} installation.");
            }

            var enabledContentIds = await GatherEnabledContentIdsAsync(
                manifestPool, contentOrchestrator, replay, installationManifestId, clientManifestId, logger, ct);

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

    /// <summary>
    /// Checks whether an existing profile matches a third-party client and version.
    /// </summary>
    /// <param name="profile">The game profile.</param>
    /// <param name="clientManifestId">The client manifest ID.</param>
    /// <param name="dataPatchManifestId">The data patch manifest ID if any.</param>
    /// <param name="expectedVersion">The expected client version if any.</param>
    /// <returns><c>true</c> if the profile matches; otherwise, <c>false</c>.</returns>
    internal static bool IsProfileMatchingThirdParty(
        GameProfile profile,
        string clientManifestId,
        string? dataPatchManifestId,
        string? expectedVersion = null)
    {
        var clientMatches = string.Equals(profile.GameClient?.Id, clientManifestId, StringComparison.OrdinalIgnoreCase) ||
                            profile.EnabledContentIds?.Any(id => string.Equals(id, clientManifestId, StringComparison.OrdinalIgnoreCase)) == true ||
                            (DependencyResolver.HasCompatibleCatalogIdentity(clientManifestId, profile.GameClient?.Id) &&
                             HasMatchingClientVersion(clientManifestId, profile.GameClient?.Id, expectedVersion, profile.GameClient?.Version)) ||
                            profile.EnabledContentIds?.Any(id =>
                                DependencyResolver.HasCompatibleCatalogIdentity(clientManifestId, id) &&
                                HasMatchingClientVersion(clientManifestId, id, expectedVersion, null)) == true;

        if (!clientMatches)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(dataPatchManifestId))
        {
            return profile.EnabledContentIds?.Any(id =>
                string.Equals(id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                DependencyResolver.HasCompatibleCatalogIdentity(dataPatchManifestId, id)) == true;
        }

        return true;
    }

    /// <summary>
    /// Resolves the compatibility status and matching profile for the specified replay file.
    /// </summary>
    /// <param name="replay">The replay file.</param>
    /// <param name="acquiredIds">The set of acquired manifest IDs.</param>
    /// <param name="profiles">The list of existing profiles.</param>
    internal void ResolveCompatibility(ReplayFile replay, HashSet<string> acquiredIds, IReadOnlyList<GameProfile> profiles)
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
            ResolveMatchedClientCompatibility(replay, match, acquiredIds, profiles, logger);
        }
        else
        {
            replay.MatchedClient = null;
            var unmappedProfile = profiles.FirstOrDefault(p =>
                p.GameClient?.GameType == replay.GameVersion &&
                ((!string.IsNullOrEmpty(replay.MatchingProfileId) && string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase)) ||
                 MatchesReplayFileName(p.Description, replay.FileName, logger)));

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

        return (installation, null);
    }

    private static async Task<List<string>> GatherEnabledContentIdsAsync(
        IContentManifestPool manifestPool,
        IContentOrchestrator? contentOrchestrator,
        ReplayFile replay,
        string installationManifestId,
        string clientManifestId,
        ILogger logger,
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

        var isRetailClient = string.Equals(clientManifestId, installationManifestId, StringComparison.OrdinalIgnoreCase) ||
                             string.IsNullOrWhiteSpace(replay.MatchedClient?.Publisher) ||
                             string.Equals(replay.MatchedClient?.Publisher, "ea", StringComparison.OrdinalIgnoreCase);

        if (!isRetailClient && replay.MatchedClient != null && string.IsNullOrEmpty(replay.MatchedClient.DataPatchManifestId))
        {
            await AddThirdPartyCompanionManifestsAsync(
                manifestPool, clientManifestId, replay, enabledContentIds, logger, ct);
        }

        if (replay.MatchedClient != null &&
            !string.IsNullOrEmpty(replay.MatchedClient.DataPatchManifestId))
        {
            var rawDataPatchId = replay.MatchedClient.DataPatchManifestId;

            // Try acquiring data patch if not present in pool
            await AcquireDataPatchIfMissingAsync(
                contentOrchestrator, manifestPool, rawDataPatchId, replay.MatchedClient.Publisher, replay.GameVersion, ct);

            // Verify if data patch exists in manifest pool (exact ID or compatible identity)
            var resolvedDataPatchId = await ResolveExistingPatchManifestIdAsync(manifestPool, rawDataPatchId, replay.GameVersion, ct);
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
        GameType gameVersion,
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
        if (allManifestsResult != null && allManifestsResult.Success && allManifestsResult.Data != null)
        {
            var patchManifest = allManifestsResult.Data.FirstOrDefault(m =>
                m.TargetGame == gameVersion &&
                (m.ContentType == ContentType.Patch || m.ContentType == ContentType.MapPack) &&
                (string.Equals(m.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                 m.Dependencies?.Any(d => string.Equals(d.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase)) == true));

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
            ? $"[replay:{replay.FileName}] Profile configured for unmapped replay {replay.FileName} (Exe: {replay.Metadata?.FormattedExeCrc ?? "N/A"}, INI: {replay.Metadata?.FormattedIniCrc ?? "N/A"})"
            : $"[replay:{replay.FileName}] Profile configured for {replay.MatchedClient?.Description} (Exe: {replay.Metadata?.FormattedExeCrc}, INI: {replay.Metadata?.FormattedIniCrc})";

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
        ReplayFile? replay = null,
        ILogger? logger = null)
    {
        var profileList = profiles.ToList();

        // 1. Direct replay profile match by ID or Description
        if (replay != null)
        {
            var byIdOrName = profileList.FirstOrDefault(p =>
                p.GameClient?.GameType == gameVersion &&
                ((!string.IsNullOrEmpty(replay.MatchingProfileId) && string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase)) ||
                 MatchesReplayFileName(p.Description, replay.FileName, logger)));

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
                : IsProfileMatchingThirdParty(p, clientManifestId, dataPatchManifestId, replay?.MatchedClient?.Version);
        });
    }

    private static bool IsProfileMatchingRetail(GameProfile profile, string? dataPatchManifestId)
    {
        var superHackersSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.TheSuperHackers}{ManifestConstants.ManifestIdSegmentSeparator}";
        var legacySuperHackersSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.LegacySuperHackers}{ManifestConstants.ManifestIdSegmentSeparator}";
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
            return profile.EnabledContentIds?.Any(id =>
                string.Equals(id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                DependencyResolver.HasCompatibleCatalogIdentity(dataPatchManifestId, id)) == true;
        }

        var hasCustomDataPatch = profile.EnabledContentIds?.Any(id =>
            id.Contains(".gamedata.", StringComparison.OrdinalIgnoreCase) ||
            id.Contains(".datapatch.", StringComparison.OrdinalIgnoreCase) ||
            id.Contains(".community.", StringComparison.OrdinalIgnoreCase) ||
            id.Contains(".mod.", StringComparison.OrdinalIgnoreCase)) == true;

        return !hasCustomDataPatch;
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

        if (manifestId.Contains($".{PublisherTypeConstants.GeneralsOnline}.", StringComparison.OrdinalIgnoreCase))
        {
            return PublisherTypeConstants.GeneralsOnline;
        }

        if (manifestId.Contains($".{PublisherTypeConstants.LegacySuperHackers}.", StringComparison.OrdinalIgnoreCase))
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

        if (!string.IsNullOrEmpty(match.ManifestId) &&
            acquiredIds.Any(id =>
                string.Equals(match.ManifestId, id, StringComparison.OrdinalIgnoreCase) ||
                (DependencyResolver.HasCompatibleCatalogIdentity(match.ManifestId, id) &&
                 HasMatchingClientVersion(match.ManifestId, id, match.Version, null))))
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

        return false;
    }

    private static ReplayCompatibilityStatus DetermineUnconfiguredStatus(CrcMappingEntry match, bool isInstalled)
    {
        if (isInstalled)
        {
            return ReplayCompatibilityStatus.RequiresProfile;
        }

        var isRetail = string.IsNullOrWhiteSpace(match.Publisher) ||
                       string.Equals(match.Publisher, PublisherTypeConstants.Ea, StringComparison.OrdinalIgnoreCase) ||
                       match.ManifestId.Contains(ReplayManagerConstants.RetailManifestSegment, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(match.CdnUrl) || (!isRetail && !string.IsNullOrWhiteSpace(match.ManifestId)))
        {
            return ReplayCompatibilityStatus.Downloadable;
        }

        return ReplayCompatibilityStatus.Orphaned;
    }

    private static bool MatchesReplayFileName(string? description, string fileName, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var tag = $"[replay:{fileName}]";
        if (description.Contains(tag, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var pattern = $@"(?<![\w.-]){Regex.Escape(fileName)}(?![\w.-])";
        try
        {
            return Regex.IsMatch(description, pattern, RegexOptions.IgnoreCase, ReplayFileNameRegexTimeout);
        }
        catch (RegexMatchTimeoutException ex)
        {
            // A regex timeout indicates pathological description text; intentionally treat as a
            // non-match to degrade gracefully without failing or blocking the replay scan.
            logger?.LogDebug(ex, "Regex matching timed out for replay file '{FileName}' against profile description", fileName);
            return false;
        }
    }

    private static void ResolveMatchedClientCompatibility(
        ReplayFile replay,
        CrcMappingEntry match,
        HashSet<string> acquiredIds,
        IReadOnlyList<GameProfile> profiles,
        ILogger? logger = null)
    {
        replay.MatchedClient = match;

        var matchingProfile = FindMatchingProfile(profiles, replay.GameVersion, match.ManifestId, match.DataPatchManifestId, replay, logger);
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
        CrcMappingEntry? matchedClient,
        GameType gameVersion,
        CancellationToken ct)
    {
        if (matchedClient == null || string.IsNullOrEmpty(matchedClient.ManifestId))
        {
            return string.Empty;
        }

        var exactCheck = await manifestPool.GetManifestAsync(ManifestId.Create(matchedClient.ManifestId), ct);
        if (exactCheck != null && exactCheck.Success && exactCheck.Data != null)
        {
            return exactCheck.Data.Id.Value;
        }

        var allManifestsResult = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifestsResult != null && allManifestsResult.Success && allManifestsResult.Data != null)
        {
            var matchedManifest = FindMatchingExistingClientManifest(allManifestsResult.Data, matchedClient, gameVersion);
            if (matchedManifest != null)
            {
                return matchedManifest.Id.Value;
            }
        }

        return matchedClient.ManifestId;
    }

    private static async Task<ContentManifest?> GetClientManifestAsync(
        IContentManifestPool manifestPool,
        string clientManifestId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(clientManifestId) || !ManifestId.TryCreate(clientManifestId, out var manifestId))
        {
            return null;
        }

        var clientManifestResult = await manifestPool.GetManifestAsync(manifestId, ct);
        return clientManifestResult is { Success: true } ? clientManifestResult.Data : null;
    }

    private static bool IsCandidateCompanion(
        ContentManifest manifest,
        GameType targetGame,
        string publisher,
        string clientVersion)
    {
        return manifest.TargetGame == targetGame &&
               (manifest.ContentType == ContentType.Patch || manifest.ContentType == ContentType.MapPack) &&
               (string.Equals(manifest.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
                manifest.Id.Value.Contains("." + publisher + ".", StringComparison.OrdinalIgnoreCase)) &&
               !string.IsNullOrEmpty(manifest.Version) &&
               string.Equals(manifest.Version, clientVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCompanionDependencyLink(
        ContentManifest? clientManifest,
        ContentManifest companion,
        string clientManifestId)
    {
        return clientManifest?.Dependencies?.Any(d => string.Equals(d.Id.Value, companion.Id.Value, StringComparison.OrdinalIgnoreCase)) == true ||
               companion.Dependencies?.Any(d => string.Equals(d.Id.Value, clientManifestId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static async Task AddThirdPartyCompanionManifestsAsync(
        IContentManifestPool manifestPool,
        string clientManifestId,
        ReplayFile replay,
        List<string> enabledContentIds,
        ILogger logger,
        CancellationToken ct)
    {
        var publisher = replay.MatchedClient?.Publisher;
        var clientVersion = replay.MatchedClient?.Version;
        if (string.IsNullOrEmpty(publisher) || string.IsNullOrEmpty(clientVersion))
        {
            return;
        }

        var clientManifest = await GetClientManifestAsync(manifestPool, clientManifestId, ct);
        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifests == null || !allManifests.Success || allManifests.Data == null)
        {
            return;
        }

        var candidateCompanions = allManifests.Data.Where(m =>
            IsCandidateCompanion(m, replay.GameVersion, publisher, clientVersion));

        foreach (var companion in candidateCompanions)
        {
            if (HasCompanionDependencyLink(clientManifest, companion, clientManifestId))
            {
                if (!enabledContentIds.Contains(companion.Id.Value, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Add(companion.Id.Value);
                }
            }
            else
            {
                logger.LogDebug(
                    "[ReplayManager] Candidate companion {CompanionId} matches publisher '{Publisher}' and version '{Version}' but lacks explicit dependency link to client {ClientId}; skipping",
                    companion.Id.Value,
                    publisher,
                    companion.Version,
                    clientManifestId);
            }
        }
    }

    private static async Task AcquireGeneralsOnlineMapPacksAsync(IContentOrchestrator contentOrchestrator, IContentManifestPool manifestPool, CancellationToken ct)
    {
        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifests.Success && allManifests.Data != null &&
            allManifests.Data.Any(m => m.ContentType == ContentType.MapPack &&
                                       (string.Equals(m.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
                                        m.Id.Value.Contains("." + GeneralsOnlineConstants.PublisherType + ".", StringComparison.OrdinalIgnoreCase))))
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
                string.Equals(m.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                (m.TargetGame == gameVersion &&
                 !string.IsNullOrEmpty(publisher) &&
                 (m.ContentType == ContentType.Patch || m.ContentType == ContentType.MapPack) &&
                 (string.Equals(m.Publisher?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
                  m.Id.Value.Contains("." + publisher + ".", StringComparison.OrdinalIgnoreCase))));

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
                string.Equals(c.Id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase));

            if (patchMatch != null)
            {
                await contentOrchestrator.AcquireContentAsync(patchMatch, null, ct);
            }
        }
    }

    private static string GetReplayClientDisplayName(CrcMappingEntry? matchedClient, string defaultName)
    {
        if (matchedClient == null)
        {
            return defaultName;
        }

        if (!string.IsNullOrWhiteSpace(matchedClient.Description))
        {
            return matchedClient.Description;
        }

        if (!string.IsNullOrWhiteSpace(matchedClient.Publisher) || !string.IsNullOrWhiteSpace(matchedClient.Version))
        {
            return $"{matchedClient.Publisher} {matchedClient.Version}".Trim();
        }

        return defaultName;
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

        var clientName = GetReplayClientDisplayName(replay.MatchedClient, "Retail Client");
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
        return FindMatchingExistingClientManifest(existingManifests, matchedClient, gameVersion) != null;
    }

    private static bool IsManifestTargetGameCompatible(ContentManifest manifest, GameType gameVersion)
    {
        if (manifest.TargetGame == gameVersion || manifest.TargetGame == GameType.Unknown)
        {
            return true;
        }

        var gameStr = gameVersion == GameType.ZeroHour ? ManifestConstants.ZeroHourContentName : ManifestConstants.GeneralsContentName;
        return manifest.Id.Value.Contains($".{gameStr}.", StringComparison.OrdinalIgnoreCase) ||
               manifest.Id.Value.EndsWith($".{gameStr}", StringComparison.OrdinalIgnoreCase);
    }

    private static ContentManifest? FindMatchingExistingClientManifest(
        IEnumerable<ContentManifest> existingManifests,
        CrcMappingEntry matchedClient,
        GameType gameVersion)
    {
        return existingManifests.FirstOrDefault(m =>
            string.Equals(m.Id.Value, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase) ||
            (m.ContentType == ContentType.GameClient &&
             IsManifestTargetGameCompatible(m, gameVersion) &&
             HasMatchingClientVersion(matchedClient, m) &&
             (DependencyResolver.HasCompatibleCatalogIdentity(matchedClient.ManifestId, m.Id.Value) ||
              (!string.IsNullOrEmpty(matchedClient.Publisher) &&
               (string.Equals(m.Publisher?.PublisherType, matchedClient.Publisher, StringComparison.OrdinalIgnoreCase) ||
                m.Id.Value.Contains("." + matchedClient.Publisher + ".", StringComparison.OrdinalIgnoreCase))))));
    }

    private static bool HasMatchingClientVersion(CrcMappingEntry matchedClient, ContentManifest manifest)
    {
        if (!string.IsNullOrEmpty(matchedClient.Version) && !string.IsNullOrEmpty(manifest.Version))
        {
            var v1 = matchedClient.Version.TrimStart('0');
            var v2 = manifest.Version.TrimStart('0');
            return string.Equals(matchedClient.Version, manifest.Version, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(v1) && !string.IsNullOrEmpty(v2) && string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase));
        }

        return HasMatchingVersionSegment(matchedClient.ManifestId, manifest.Id.Value);
    }

    private static bool HasMatchingClientVersion(string declaredId, string? candidateId, string? expectedVersion, string? candidateVersion)
    {
        if (!string.IsNullOrWhiteSpace(expectedVersion) && !string.IsNullOrWhiteSpace(candidateVersion))
        {
            var v1 = expectedVersion.TrimStart('0');
            var v2 = candidateVersion.TrimStart('0');
            return string.Equals(expectedVersion, candidateVersion, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(v1) && !string.IsNullOrEmpty(v2) && string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase));
        }

        return HasMatchingVersionSegment(declaredId, candidateId);
    }

    private static bool HasMatchingVersionSegment(string? id1, string? id2)
    {
        if (string.IsNullOrEmpty(id1) || string.IsNullOrEmpty(id2))
        {
            return false;
        }

        var parts1 = id1.Split('.');
        var parts2 = id2.Split('.');
        if (parts1.Length >= 2 && parts2.Length >= 2)
        {
            var v1 = parts1[1].TrimStart('0');
            var v2 = parts2[1].TrimStart('0');
            if (string.IsNullOrEmpty(v1) || string.IsNullOrEmpty(v2))
            {
                return false;
            }

            if (string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var minLen = Math.Min(v1.Length, v2.Length);
            var maxLen = Math.Max(v1.Length, v2.Length);
            return minLen >= 5 && maxLen - minLen <= 1 &&
                   (v1.StartsWith(v2, StringComparison.OrdinalIgnoreCase) ||
                    v2.StartsWith(v1, StringComparison.OrdinalIgnoreCase));
        }

        return false;
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
            : await ResolveThirdPartyGameClientAsync(installation, replay, defaultVersion, (exePath, workingDir), manifestPool, contentOrchestrator, ct);
    }

    private async Task<(string ClientManifestId, GameClient GameClient)> ResolveThirdPartyGameClientAsync(
        GameInstallation installation,
        ReplayFile replay,
        string defaultVersion,
        (string ExePath, string WorkingDir) launchPaths,
        IContentManifestPool manifestPool,
        IContentOrchestrator? contentOrchestrator,
        CancellationToken ct)
    {
        var thirdPartyManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        if (replay.MatchedClient != null)
        {
            await AcquireThirdPartyClientAndDependenciesAsync(contentOrchestrator, manifestPool, replay.MatchedClient, replay.GameVersion, ct);
            thirdPartyManifestId = await ResolveThirdPartyClientManifestIdAsync(manifestPool, replay.MatchedClient, replay.GameVersion, ct);
        }

        var thirdPartyClientName = GetReplayClientDisplayName(replay.MatchedClient, "Third-Party Client");
        var thirdPartyGameClient = new GameClient
        {
            Id = thirdPartyManifestId,
            Name = thirdPartyClientName,
            Version = replay.MatchedClient?.Version ?? defaultVersion,
            GameType = replay.GameVersion,
            PublisherType = replay.MatchedClient?.Publisher ?? string.Empty,
            InstallationId = installation.Id,
            ExecutablePath = launchPaths.ExePath,
            WorkingDirectory = launchPaths.WorkingDir,
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

    private void EnsureReplayMatch(ReplayFile replay)
    {
        if (replay.MatchedClient == null && replay.Metadata?.FormattedExeCrc != null &&
            crcMappingRegistry.TryGetEntry(replay.Metadata.FormattedExeCrc, replay.Metadata.FormattedIniCrc ?? string.Empty, out var resolvedMatch))
        {
            replay.MatchedClient = resolvedMatch;
        }
    }
}
