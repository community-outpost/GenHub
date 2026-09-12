using System;
using System.Collections.Concurrent;
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
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Tools.Checksum;
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
    ILogger<ReplayDirectoryService> logger,
    IGameCrcCalculatorService? crcCalculator = null) : IReplayDirectoryService
{
    private static readonly TimeSpan ReplayFileNameRegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex GeneralsOnlineFileNameRegex = new(
        @"^match_\d+_user_[a-fA-F0-9]+_replay\.rep$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        ReplayFileNameRegexTimeout);

    private sealed record ReplayContentResolutionContext(
        IContentManifestPool ManifestPool,
        IContentOrchestrator? ContentOrchestrator,
        IDependencyResolver? DependencyResolver,
        ReplayFile Replay,
        string InstallationManifestId,
        string ClientManifestId);

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

        var (resolved, acquiredIds, existingProfiles) = await FetchAcquiredManifestIdsAndProfilesAsync(ct);

        var replayFiles = new ConcurrentBag<ReplayFile>();
        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8), CancellationToken = ct },
            async (file, token) =>
            {
                var replay = await ProcessReplayFileAsync(file, version, resolved, acquiredIds, existingProfiles, token);
                replayFiles.Add(replay);
            });

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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GameProfile>> GetCompatibleProfilesForReplayAsync(
        ReplayFile replay,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);

        using var scope = scopeFactory.CreateScope();
        var profileManager = scope.ServiceProvider.GetService<IGameProfileManager>();
        if (profileManager == null)
        {
            return Array.Empty<GameProfile>();
        }

        var profilesResult = await profileManager.GetAllProfilesAsync(ct);
        if (!profilesResult.Success || profilesResult.Data == null)
        {
            return Array.Empty<GameProfile>();
        }

        var clientManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        var dataPatchManifestId = replay.MatchedClient?.DataPatchManifestId;

        var compatible = FindCompatibleProfiles(
            profilesResult.Data,
            replay.GameVersion,
            clientManifestId,
            dataPatchManifestId,
            replay,
            logger,
            crcCalculator);

        if (crcCalculator != null && !string.IsNullOrEmpty(replay.Metadata?.FormattedExeCrc))
        {
            var targetExeCrc = replay.Metadata.FormattedExeCrc;
            var targetIniCrc = replay.Metadata.FormattedIniCrc;
            var compatibleIds = new HashSet<string>(compatible.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

            foreach (var profile in profilesResult.Data.Where(p => p.GameClient?.GameType == replay.GameVersion && !compatibleIds.Contains(p.Id)))
            {
                if (TryMatchProfileExeCrc(profile, replay, targetExeCrc, targetIniCrc, out _))
                {
                    compatible.Add(profile);
                    compatibleIds.Add(profile.Id);
                }
            }
        }

        return compatible;
    }

    /// <inheritdoc/>
    public Task<ProfileOperationResult<GameProfile>> CreateProfileForReplayAsync(
        ReplayFile replay,
        CancellationToken ct = default)
        => CreateProfileForReplayAsync(replay, customGameClient: null, customClientManifestId: null, ct);

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileForReplayAsync(
        ReplayFile replay,
        GameClient? customGameClient,
        string? customClientManifestId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);

        EnsureReplayMatch(replay);

        var isUnmappedReplay = replay.MatchedClient == null;
        LogCreateProfileStart(replay, customGameClient, isUnmappedReplay);

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
            var dependencyResolver = sp.GetService<IDependencyResolver>();
            var configService = sp.GetService<IConfigurationProviderService>();
            var preferredStrategy = configService?.GetDefaultWorkspaceStrategy() ?? WorkspaceStrategy.HardLink;

            var defaultVersion = replay.GameVersion == GameType.ZeroHour
                ? ManifestConstants.ZeroHourManifestVersion
                : ManifestConstants.GeneralsManifestVersion;

            var installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(
                installation, replay.GameVersion, defaultVersion);

            var isRetailClient = isUnmappedReplay ||
                                 IsRetailClient(replay.MatchedClient?.Publisher, replay.MatchedClient?.ManifestId);

            var (clientManifestId, gameClient) = customGameClient != null
                ? (customClientManifestId ?? customGameClient.Id ?? string.Empty, customGameClient)
                : await ResolveReplayGameClientAsync(
                    installation, replay, defaultVersion, isRetailClient, manifestPool, contentOrchestrator, ct);

            gameClient = EnsureGameClientExecutable(gameClient, replay.GameVersion, replay.MatchedClient?.Publisher);
            if (gameClient == null)
            {
                logger.LogError("[ReplayManager] Could not determine executable path for {GameVersion} installation", replay.GameVersion);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Could not determine executable path for {replay.GameVersion} installation.");
            }

            var resolutionContext = new ReplayContentResolutionContext(
                manifestPool, contentOrchestrator, dependencyResolver, replay, installationManifestId, clientManifestId);
            var enabledContentIds = await GatherEnabledContentIdsAsync(resolutionContext, logger, ct);

            if (!string.IsNullOrWhiteSpace(clientManifestId) && !enabledContentIds.Contains(clientManifestId, StringComparer.OrdinalIgnoreCase))
            {
                enabledContentIds.Add(clientManifestId);
            }

            logger.LogInformation(
                "[ReplayManager] Gathered {Count} enabled content IDs for replay profile: [{ContentIds}]",
                enabledContentIds.Count,
                string.Join(", ", enabledContentIds));

            var request = BuildReplayProfileRequest(replay, installation, clientManifestId, gameClient, enabledContentIds, preferredStrategy, isCustomGameClient: customGameClient != null);

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

    /// <inheritdoc/>
    public Task<ProfileOperationResult<GameLaunchInfo>> LaunchReplayAsync(
        ReplayFile replay,
        CancellationToken ct = default)
        => LaunchReplayAsync(replay, profileId: null, ct);

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameLaunchInfo>> LaunchReplayAsync(
        ReplayFile replay,
        string? profileId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);

        var isExplicitProfile = !string.IsNullOrWhiteSpace(profileId);
        if (!isExplicitProfile)
        {
            await EnsureValidProfileReferenceAsync(replay, ct);
        }

        var targetProfileId = isExplicitProfile ? profileId! : replay.MatchingProfileId;

        logger.LogInformation(
            "[ReplayManager] Starting replay launch workflow for '{ReplayFile}' (GameVersion: {GameVersion}, ProfileId: {ProfileId})",
            replay.FileName,
            replay.GameVersion,
            targetProfileId ?? "none");

        if (string.IsNullOrEmpty(targetProfileId))
        {
            var ensureError = await EnsureReplayProfileExistsAsync(replay, ct);
            if (ensureError != null)
            {
                return ensureError;
            }

            targetProfileId = replay.MatchingProfileId;
        }

        var launchResult = await ExecuteProfileLaunchAsync(targetProfileId ?? string.Empty, replay.FileName, ct);
        if (launchResult.Success && isExplicitProfile)
        {
            replay.MatchingProfileId = profileId;
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
            await ResolveExplicitProfileNameAsync(replay, profileId!, ct);
        }

        return launchResult;
    }

    /// <inheritdoc />
    public async Task<bool> IsProfileRunningAsync(string profileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var launcherFacade = scope.ServiceProvider.GetService<IProfileLauncherFacade>();
            if (launcherFacade != null)
            {
                var status = await launcherFacade.GetLaunchStatusAsync(profileId, ct);
                return status?.Success == true && status.Data?.IsRunning == true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Failed to query launch status for profile {ProfileId}", profileId);
        }

        return false;
    }

    /// <summary>
    /// Finds all compatible game profiles for the specified replay criteria, sorted by candidate score descending.
    /// </summary>
    /// <param name="profiles">The candidate game profiles.</param>
    /// <param name="gameVersion">The game version required by the replay.</param>
    /// <param name="clientManifestId">The client manifest ID.</param>
    /// <param name="dataPatchManifestId">The data patch manifest ID if any.</param>
    /// <param name="replay">The replay file being matched, if available.</param>
    /// <param name="logger">Optional logger for diagnostic warnings.</param>
    /// <returns>A sorted list of compatible <see cref="GameProfile"/> instances.</returns>
    internal static List<GameProfile> FindCompatibleProfiles(
        IEnumerable<GameProfile> profiles,
        GameType gameVersion,
        string clientManifestId,
        string? dataPatchManifestId = null,
        ReplayFile? replay = null,
        ILogger? logger = null,
        IGameCrcCalculatorService? crcCalculator = null)
    {
        var profileList = profiles.ToList();

        var isRetailClient = IsRetailClient(null, clientManifestId);
        var targetExeCrc = replay?.MatchedClient?.ExeCrc ?? replay?.Metadata?.FormattedExeCrc;

        var compatibleCandidates = profileList.Where(p =>
        {
            if (p.GameClient?.GameType != gameVersion)
            {
                return false;
            }

            if (crcCalculator != null && !string.IsNullOrEmpty(targetExeCrc))
            {
                var exePath = ResolveProfileFullExePath(p.GameClient);
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    try
                    {
                        var calcRes = crcCalculator.CalculateExeCrcAsync(exePath, ct: CancellationToken.None).GetAwaiter().GetResult();
                        if (calcRes.Success && !string.IsNullOrEmpty(calcRes.Data) &&
                            !string.Equals(calcRes.Data, targetExeCrc, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger?.LogWarning(ex, "[ReplayManager] Error verifying profile '{ProfileName}' EXE CRC for replay matching", p.Name);
                    }
                }
            }

            if (!string.IsNullOrEmpty(replay?.MatchingProfileId) &&
                string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsDedicatedToThisReplay(p, replay, logger))
            {
                return true;
            }

            return isRetailClient
                ? IsProfileMatchingRetail(p, dataPatchManifestId)
                : IsProfileMatchingThirdParty(p, clientManifestId, dataPatchManifestId, replay?.MatchedClient?.Version);
        }).ToList();

        return compatibleCandidates
            .Select(p => new { Profile = p, Score = ScoreCandidateProfile(p, clientManifestId, dataPatchManifestId, replay, logger) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Profile)
            .ToList();
    }

    /// <summary>
    /// Finds the best matching profile for a replay file from a list of profiles based on game version, client manifest ID, and patch ID.
    /// Uses deterministic scoring and tie-breaking:
    /// 1. Dedicated replay profile (description or name matches current replay filename) gets highest priority (+1000).
    /// 2. General profiles get next priority (+500) over auto-created profiles dedicated to other replays.
    /// 3. Exact client manifest match gets +50.
    /// 4. Exact data patch match gets +25.
    /// 5. Ties are broken alphabetically by profile Name, then by profile Id.
    /// </summary>
    /// <param name="profiles">The candidate game profiles.</param>
    /// <param name="gameVersion">The game version required by the replay.</param>
    /// <param name="clientManifestId">The client manifest ID.</param>
    /// <param name="dataPatchManifestId">The data patch manifest ID if any.</param>
    /// <param name="replay">The replay file being matched, if available.</param>
    /// <param name="logger">Optional logger for diagnostic warnings.</param>
    /// <returns>The best matching <see cref="GameProfile"/> if found; otherwise, <c>null</c>.</returns>
    internal static GameProfile? FindMatchingProfile(
        IEnumerable<GameProfile> profiles,
        GameType gameVersion,
        string clientManifestId,
        string? dataPatchManifestId = null,
        ReplayFile? replay = null,
        ILogger? logger = null,
        IGameCrcCalculatorService? crcCalculator = null)
    {
        return FindCompatibleProfiles(profiles, gameVersion, clientManifestId, dataPatchManifestId, replay, logger, crcCalculator).FirstOrDefault();
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
            var hasAnyPatch = profile.EnabledContentIds?.Any(id =>
                id.Contains(".patch.", StringComparison.OrdinalIgnoreCase)) == true;

            if (hasAnyPatch)
            {
                return profile.EnabledContentIds?.Any(id =>
                    HasMatchingDataPatchId(dataPatchManifestId, id)) == true;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves the relative executable path for a third-party game client within the target working directory,
    /// enforcing strict containment validation against directory traversal and degenerate paths.
    /// </summary>
    /// <param name="targetClient">The target game client, if any.</param>
    /// <param name="workingDir">The installation working directory.</param>
    /// <param name="clientManifest">The client content manifest, if available.</param>
    /// <param name="replay">The replay file context.</param>
    /// <returns>The resolved relative executable path, or default executable name if uncontained.</returns>
    internal static string ResolveThirdPartyRelativeExePath(
        GameClient? targetClient,
        string workingDir,
        ContentManifest? clientManifest,
        ReplayFile replay)
    {
        if (clientManifest != null)
        {
            var entryResolution = ManifestVariantResolver.ResolveEntryPoint(clientManifest);
            if (entryResolution.Success &&
                !string.IsNullOrWhiteSpace(entryResolution.RelativePath) &&
                TryGetContainedRelativePath(workingDir, entryResolution.RelativePath, out var manifestRelPath))
            {
                return manifestRelPath;
            }
        }

        if (targetClient != null &&
            string.Equals(targetClient.PublisherType, replay.MatchedClient?.Publisher, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(targetClient.ExecutablePath) &&
            TryGetContainedRelativePath(workingDir, targetClient.ExecutablePath, out var clientRelPath))
        {
            return clientRelPath;
        }

        return GetDefaultExecutableName(replay.GameVersion, replay.MatchedClient?.Publisher);
    }

    /// <summary>
    /// Checks whether the game client manifest associated with the CRC mapping is installed locally.
    /// </summary>
    /// <param name="match">The CRC mapping entry.</param>
    /// <param name="gameVersion">The game version.</param>
    /// <param name="acquiredIds">The set of acquired manifest IDs.</param>
    /// <returns><c>true</c> if the client manifest is installed; otherwise, <c>false</c>.</returns>
    internal static bool IsClientManifestInstalled(CrcMappingEntry match, GameType gameVersion, HashSet<string> acquiredIds)
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

        var isRetail = IsRetailClient(publisher, match.ManifestId);

        if (isRetail)
        {
            var gameTypeSuffix = gameVersion == GameType.ZeroHour ? ManifestConstants.ZeroHourContentName : ManifestConstants.GeneralsContentName;
            return acquiredIds.Any(id => id.Contains(ManifestConstants.GameInstallationManifestSegment, StringComparison.OrdinalIgnoreCase) && id.EndsWith(gameTypeSuffix, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    /// <summary>
    /// Determines the unconfigured compatibility status for a matched CRC entry.
    /// </summary>
    /// <param name="match">The CRC mapping entry.</param>
    /// <param name="isInstalled">Whether the client manifest is installed.</param>
    /// <returns>The resolved <see cref="ReplayCompatibilityStatus"/>.</returns>
    internal static ReplayCompatibilityStatus DetermineUnconfiguredStatus(CrcMappingEntry match, bool isInstalled)
    {
        if (isInstalled)
        {
            return ReplayCompatibilityStatus.RequiresProfile;
        }

        var isRetail = IsRetailClient(match.Publisher, match.ManifestId);

        if (!string.IsNullOrWhiteSpace(match.CdnUrl) || (!isRetail && !string.IsNullOrWhiteSpace(match.ManifestId)))
        {
            return ReplayCompatibilityStatus.Downloadable;
        }

        return ReplayCompatibilityStatus.Orphaned;
    }

    /// <summary>
    /// Resolves the compatibility status for a replay that matched a known client entry.
    /// </summary>
    /// <param name="replay">The replay file.</param>
    /// <param name="match">The CRC mapping entry.</param>
    /// <param name="acquiredIds">The set of acquired manifest IDs.</param>
    /// <param name="profiles">The list of game profiles.</param>
    /// <param name="logger">Optional logger.</param>
    internal static void ResolveMatchedClientCompatibility(
        ReplayFile replay,
        CrcMappingEntry match,
        HashSet<string> acquiredIds,
        IReadOnlyList<GameProfile> profiles,
        ILogger? logger = null,
        IGameCrcCalculatorService? crcCalculator = null)
    {
        replay.MatchedClient = match;

        var matchingProfile = FindMatchingProfile(profiles, replay.GameVersion, match.ManifestId, match.DataPatchManifestId, replay, logger, crcCalculator);
        if (matchingProfile != null)
        {
            replay.MatchingProfileId = matchingProfile.Id;
            replay.MatchingProfileName = matchingProfile.Name;
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
            return;
        }

        replay.MatchingProfileId = null;
        replay.MatchingProfileName = null;
        var isInstalled = IsClientManifestInstalled(match, replay.GameVersion, acquiredIds);
        replay.CompatibilityStatus = DetermineUnconfiguredStatus(match, isInstalled);
    }

    /// <summary>
    /// Resolves the compatibility status and matching profile for the specified replay file.
    /// </summary>
    /// <param name="replay">The replay file.</param>
    /// <param name="acquiredIds">The set of acquired manifest IDs.</param>
    /// <param name="profiles">The list of existing profiles.</param>
    internal void ResolveCompatibility(ReplayFile replay, HashSet<string> acquiredIds, IReadOnlyList<GameProfile> profiles)
    {
        if (replay.Metadata == null || string.IsNullOrEmpty(replay.Metadata.FormattedExeCrc) || string.IsNullOrEmpty(replay.Metadata.FormattedIniCrc))
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        var exeCrcStr = replay.Metadata.FormattedExeCrc;
        var iniCrcStr = replay.Metadata.FormattedIniCrc;

        if (crcMappingRegistry.TryGetEntry(exeCrcStr, iniCrcStr, out var match) && match != null)
        {
            ResolveMatchedClientCompatibility(replay, match, acquiredIds, profiles, logger, crcCalculator);
            return;
        }

        // Secondary resolution: When exact (exeCRC, iniCRC) pair is not in catalog, check if base client matches
        if (crcMappingRegistry.TryGetEntryByExeCrc(exeCrcStr, out var baseClient) && baseClient != null)
        {
            var resolvedEntry = ResolveSecondaryBaseClientEntry(baseClient, iniCrcStr, acquiredIds);
            ResolveMatchedClientCompatibility(replay, resolvedEntry, acquiredIds, profiles, logger, crcCalculator);
            return;
        }

        // Step 5: Heuristic fallback for third-party / GeneralsOnline replays by filename pattern or build timestamp
        if (TryResolveGeneralsOnlineHeuristic(replay, out var heuristicClient) && heuristicClient != null)
        {
            var resolvedEntry = ResolveSecondaryHeuristicEntry(heuristicClient, exeCrcStr, iniCrcStr);
            ResolveMatchedClientCompatibility(replay, resolvedEntry, acquiredIds, profiles, logger, crcCalculator);
            return;
        }

        // Step 6: Dynamic check for existing profile game clients matching the replay executable CRC
        if (TryResolveProfileByExeCrc(replay, profiles, out var dynamicEntry) && dynamicEntry != null)
        {
            ResolveMatchedClientCompatibility(replay, dynamicEntry, acquiredIds, profiles, logger, crcCalculator);
            return;
        }

        ResolveUnmappedClientCompatibility(replay, profiles);
    }

    private static CrcMappingEntry ResolveSecondaryHeuristicEntry(CrcMappingEntry heuristicClient, string exeCrcStr, string iniCrcStr)
    {
        var normalizedIni = NormalizeCrcHex(iniCrcStr);
        var isVanillaIni = IsVanillaZeroHourIni(normalizedIni);

        return heuristicClient with
        {
            ExeCrc = exeCrcStr,
            IniCrc = iniCrcStr,
            DataPatchManifestId = isVanillaIni ? null : heuristicClient.DataPatchManifestId,
            DataPatchName = isVanillaIni ? ReplayManagerConstants.Vanilla104IniName : (heuristicClient.DataPatchName ?? $"Custom INI ({normalizedIni})"),
        };
    }

    /// <summary>
    /// Attempts to compute and validate a contained relative path within the specified working directory.
    /// </summary>
    /// <param name="workingDir">The working directory root.</param>
    /// <param name="candidatePath">The candidate path to check and make relative.</param>
    /// <param name="relativePath">When this method returns, contains the valid contained relative path if true; otherwise, empty.</param>
    /// <returns><c>true</c> if the candidate path is strictly contained within the working directory; otherwise, <c>false</c>.</returns>
    private static bool TryGetContainedRelativePath(string workingDir, string candidatePath, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(workingDir) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            var fullWorkingDir = Path.GetFullPath(workingDir);
            var fullCandidatePath = Path.IsPathRooted(candidatePath)
                ? Path.GetFullPath(candidatePath)
                : Path.GetFullPath(Path.Combine(fullWorkingDir, candidatePath));

            var rel = Path.GetRelativePath(fullWorkingDir, fullCandidatePath);
            var isContained = !string.IsNullOrWhiteSpace(rel) &&
                              rel != "." &&
                              !string.Equals(rel, "..", StringComparison.Ordinal) &&
                              !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                              !rel.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
                              !Path.IsPathRooted(rel);

            if (isContained)
            {
                relativePath = rel;
                return true;
            }
        }
        catch (ArgumentException)
        {
            // Invalid or unparseable path
        }
        catch (NotSupportedException)
        {
            // Invalid or unparseable path
        }
        catch (PathTooLongException)
        {
            // Invalid or unparseable path
        }

        return false;
    }

    private static int ScoreCandidateProfile(
        GameProfile profile,
        string clientManifestId,
        string? dataPatchManifestId,
        ReplayFile? replay,
        ILogger? logger)
    {
        var score = 0;

        if (!string.IsNullOrEmpty(replay?.MatchingProfileId) &&
            string.Equals(profile.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase))
        {
            score += 2000;
        }

        if (IsDedicatedToThisReplay(profile, replay, logger))
        {
            score += 1000;
        }
        else if (!IsDedicatedToAnotherReplay(profile))
        {
            score += 500;
        }

        if (string.Equals(profile.GameClient?.Id, clientManifestId, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (!string.IsNullOrEmpty(dataPatchManifestId) &&
            profile.EnabledContentIds?.Any(id => string.Equals(id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase)) == true)
        {
            score += 25;
        }

        return score;
    }

    private static bool IsDedicatedToThisReplay(GameProfile profile, ReplayFile? replay, ILogger? logger)
    {
        if (!string.IsNullOrEmpty(replay?.MatchingProfileId) &&
            string.Equals(profile.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (replay == null)
        {
            return false;
        }

        if (MatchesReplayFileName(profile.Description, replay.FileName, logger))
        {
            return true;
        }

        var replayBaseName = Path.GetFileNameWithoutExtension(replay.FileName);
        return !string.IsNullOrEmpty(profile.Name) &&
               !string.IsNullOrEmpty(replayBaseName) &&
               profile.Name.Contains($"(Replay: {replayBaseName})", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDedicatedToAnotherReplay(GameProfile profile)
    {
        var inDescription = !string.IsNullOrEmpty(profile.Description) &&
                            profile.Description.Contains("[replay:", StringComparison.OrdinalIgnoreCase);
        var inName = !string.IsNullOrEmpty(profile.Name) &&
                     profile.Name.Contains("(Replay:", StringComparison.OrdinalIgnoreCase);

        return inDescription || inName;
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

        var installation = ResolveInstallation(installationsResult.Data, replay.GameVersion, replay.MatchedClient?.Publisher);
        if (installation == null)
        {
            return (null, $"No game installation found on this system supporting {replay.GameVersion}.");
        }

        await installationService.CreateAndRegisterInstallationManifestsAsync(installation, ct);

        var installationCasPoolService = sp.GetService<IInstallationCasPoolService>();
        if (installationCasPoolService != null)
        {
            await installationCasPoolService.EnsurePoolPathAsync([installation], ct);
        }

        return (installation, null);
    }

    private static async Task<List<string>> GatherEnabledContentIdsAsync(
        ReplayContentResolutionContext context,
        ILogger logger,
        CancellationToken ct)
    {
        var enabledContentIds = new List<string>();

        if (!string.IsNullOrWhiteSpace(context.InstallationManifestId))
        {
            enabledContentIds.Add(context.InstallationManifestId);
        }

        if (!string.IsNullOrWhiteSpace(context.ClientManifestId) &&
            !enabledContentIds.Contains(context.ClientManifestId, StringComparer.OrdinalIgnoreCase))
        {
            enabledContentIds.Add(context.ClientManifestId);
        }

        // 1. Resolve direct dependencies from the game client manifest (e.g. MapPack for GeneralsOnline)
        await AddDirectClientDependenciesAsync(context.ManifestPool, context.ClientManifestId, enabledContentIds, logger, ct);

        var isRetailClient = string.Equals(context.ClientManifestId, context.InstallationManifestId, StringComparison.OrdinalIgnoreCase) ||
                             IsRetailClient(context.Replay.MatchedClient?.Publisher, context.ClientManifestId);

        // 2. Add companion manifests from third party publisher (e.g. MapPack and Patches)
        if (!isRetailClient && context.Replay.MatchedClient != null && string.IsNullOrEmpty(context.Replay.MatchedClient.DataPatchManifestId))
        {
            await AddThirdPartyCompanionManifestsAsync(
                context.ManifestPool, context.ClientManifestId, context.Replay, enabledContentIds, logger, ct);
        }

        // 3. Add explicit data patch if declared on MatchedClient
        await AddExplicitDataPatchIfDeclaredAsync(context, enabledContentIds, ct);

        // 4. Resolve transitive dependencies via IDependencyResolver (as in wizard & add-to-profile)
        await AddTransitiveDependenciesAsync(context.DependencyResolver, enabledContentIds, logger, ct);

        return enabledContentIds;
    }

    private static async Task AddDirectClientDependenciesAsync(
        IContentManifestPool manifestPool,
        string clientManifestId,
        List<string> enabledContentIds,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientManifestId))
        {
            return;
        }

        try
        {
            var clientManifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(clientManifestId), ct);
            if (clientManifestResult?.Success != true || clientManifestResult.Data?.Dependencies == null)
            {
                return;
            }

            foreach (var dep in clientManifestResult.Data.Dependencies)
            {
                if (dep.DependencyType == ContentType.GameInstallation)
                {
                    continue;
                }

                await ResolveAndAddDependencyAsync(manifestPool, dep, enabledContentIds, logger, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayDirectoryService] Failed to resolve client dependencies for {ClientManifestId}", clientManifestId);
        }
    }

    private static async Task ResolveAndAddDependencyAsync(
        IContentManifestPool manifestPool,
        ContentDependency dep,
        List<string> contentIds,
        ILogger logger,
        CancellationToken ct)
    {
        var depId = dep.Id.Value;
        if (string.IsNullOrEmpty(depId))
        {
            return;
        }

        var depManifestResult = await manifestPool.GetManifestAsync(dep.Id, ct);
        if (depManifestResult?.Success == true && depManifestResult.Data != null)
        {
            AddIdIfNotPresent(contentIds, depManifestResult.Data.Id.Value);
            return;
        }

        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        var compatible = allManifests?.Success == true && allManifests.Data != null
            ? allManifests.Data.FirstOrDefault(m =>
                m.ContentType == dep.DependencyType &&
                (string.Equals(m.Publisher?.PublisherType, dep.PublisherType, StringComparison.OrdinalIgnoreCase) ||
                 DependencyResolver.HasCompatibleCatalogIdentity(dep.Id.Value, m.Id.Value)))
            : null;

        if (compatible != null)
        {
            AddIdIfNotPresent(contentIds, compatible.Id.Value);
        }
        else
        {
            logger.LogWarning(
                "[ReplayDirectoryService] Could not resolve dependency {DepId} ({DepType}, Publisher: {PublisherType}) in local manifest pool",
                depId,
                dep.DependencyType,
                dep.PublisherType);
        }
    }

    private static void AddIdIfNotPresent(List<string> list, string id)
    {
        if (!list.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(id);
        }
    }

    private static async Task AddExplicitDataPatchIfDeclaredAsync(
        ReplayContentResolutionContext context,
        List<string> enabledContentIds,
        CancellationToken ct)
    {
        if (context.Replay.MatchedClient == null ||
            string.IsNullOrEmpty(context.Replay.MatchedClient.DataPatchManifestId))
        {
            return;
        }

        var rawDataPatchId = context.Replay.MatchedClient.DataPatchManifestId;

        await AcquireDataPatchIfMissingAsync(
            context.ContentOrchestrator, context.ManifestPool, rawDataPatchId, context.Replay.MatchedClient.Publisher, context.Replay.GameVersion, ct);

        var resolvedDataPatchId = await ResolveExistingPatchManifestIdAsync(context.ManifestPool, rawDataPatchId, context.Replay.GameVersion, ct);
        if (!string.IsNullOrEmpty(resolvedDataPatchId) && !enabledContentIds.Contains(resolvedDataPatchId, StringComparer.OrdinalIgnoreCase))
        {
            enabledContentIds.Add(resolvedDataPatchId);
        }
    }

    private static async Task AddTransitiveDependenciesAsync(
        IDependencyResolver? dependencyResolver,
        List<string> enabledContentIds,
        ILogger logger,
        CancellationToken ct)
    {
        if (dependencyResolver == null || enabledContentIds.Count == 0)
        {
            return;
        }

        try
        {
            var resolvedDependencies = await dependencyResolver.ResolveDependenciesAsync(enabledContentIds, ct);
            foreach (var depId in resolvedDependencies.Where(id => !enabledContentIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            {
                enabledContentIds.Add(depId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayDirectoryService] Failed to resolve transitive dependencies");
        }
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

        if (ManifestId.TryCreate(dataPatchManifestId, out var requestedPatchId))
        {
            var exactResult = await manifestPool.GetManifestAsync(requestedPatchId, ct);
            if (exactResult?.Success == true && exactResult.Data != null)
            {
                return exactResult.Data.Id.Value;
            }
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
            if (!string.IsNullOrWhiteSpace(replay.MatchedClient.Description))
            {
                return replay.MatchedClient.Description;
            }

            if (!string.IsNullOrWhiteSpace(replay.MatchedClient.Publisher))
            {
                return replay.MatchedClient.Publisher;
            }

            return "Game";
        }

        return replay.GameVersion == GameType.ZeroHour ? "Zero Hour" : "Generals";
    }

    private static CreateProfileRequest BuildReplayProfileRequest(
        ReplayFile replay,
        GameInstallation installation,
        string clientManifestId,
        GameClient gameClient,
        List<string> enabledContentIds,
        WorkspaceStrategy workspaceStrategy = WorkspaceStrategy.HardLink,
        bool isCustomGameClient = false)
    {
        var isUnmapped = replay.MatchedClient == null;
        var clientTitle = isCustomGameClient && !string.IsNullOrWhiteSpace(gameClient.Name)
            ? gameClient.Name
            : GetReplayClientTitle(replay);

        var profileName = $"{clientTitle} (Replay: {Path.GetFileNameWithoutExtension(replay.FileName)})";
        var description = isUnmapped
            ? $"[replay:{replay.FileName}] Profile configured for unmapped replay {replay.FileName} (Exe: {replay.Metadata?.FormattedExeCrc ?? "N/A"}, INI: {replay.Metadata?.FormattedIniCrc ?? "N/A"})"
            : $"[replay:{replay.FileName}] Profile configured for {clientTitle} (Exe: {replay.Metadata?.FormattedExeCrc}, INI: {replay.Metadata?.FormattedIniCrc})";

        return new CreateProfileRequest
        {
            Name = profileName,
            Description = description,
            GameInstallationId = installation.Id,
            GameClientId = clientManifestId,
            GameClient = gameClient,
            EnabledContentIds = enabledContentIds,
            WorkspaceStrategy = workspaceStrategy,
            UseSteamLaunch = false,
        };
    }

    private static bool IsThirdPartyPublisher(string? publisher) =>
        string.Equals(publisher, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(publisher, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(publisher, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(publisher, PublisherTypeConstants.CommunityOutpost, StringComparison.OrdinalIgnoreCase);

    private static bool IsThirdPartyManifestId(string? manifestId)
    {
        if (string.IsNullOrWhiteSpace(manifestId))
        {
            return false;
        }

        var superHackersSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.TheSuperHackers}{ManifestConstants.ManifestIdSegmentSeparator}";
        var legacySuperHackersSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.LegacySuperHackers}{ManifestConstants.ManifestIdSegmentSeparator}";
        var generalsOnlineSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.GeneralsOnline}{ManifestConstants.ManifestIdSegmentSeparator}";
        var communityOutpostSegment = $"{ManifestConstants.ManifestIdSegmentSeparator}{PublisherTypeConstants.CommunityOutpost}{ManifestConstants.ManifestIdSegmentSeparator}";

        return manifestId.Contains(superHackersSegment, StringComparison.OrdinalIgnoreCase) ||
               manifestId.Contains(legacySuperHackersSegment, StringComparison.OrdinalIgnoreCase) ||
               manifestId.Contains(generalsOnlineSegment, StringComparison.OrdinalIgnoreCase) ||
               manifestId.Contains(communityOutpostSegment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetailClient(string? publisher, string? manifestId)
    {
        if (IsThirdPartyPublisher(publisher) || IsThirdPartyManifestId(manifestId))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifestId))
        {
            var extractedPub = ExtractPublisherFromManifestId(manifestId);
            if (IsThirdPartyPublisher(extractedPub))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsProfileMatchingRetail(GameProfile profile, string? dataPatchManifestId)
    {
        if (profile.GameClient == null)
        {
            return false;
        }

        if (!IsRetailClient(profile.GameClient.PublisherType, profile.GameClient.Id))
        {
            return false;
        }

        var isProfileThirdParty = profile.EnabledContentIds?.Any(id => IsThirdPartyManifestId(id)) == true;

        if (isProfileThirdParty)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(dataPatchManifestId))
        {
            return profile.EnabledContentIds?.Any(id =>
                HasMatchingDataPatchId(dataPatchManifestId, id)) == true;
        }

        var hasCustomDataPatch = profile.EnabledContentIds?.Any(id =>
            id.Contains(ManifestConstants.GameDataManifestSegment, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(ManifestConstants.DataPatchManifestSegment, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(ManifestConstants.CommunityManifestSegment, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(ManifestConstants.ModManifestSegment, StringComparison.OrdinalIgnoreCase)) == true;

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

        if (manifestId.Contains($".{PublisherTypeConstants.CommunityOutpost}.", StringComparison.OrdinalIgnoreCase))
        {
            return PublisherTypeConstants.CommunityOutpost;
        }

        return string.Empty;
    }

    private static bool IsExistingProfileCompatible(GameProfile profile, ReplayFile replay)
    {
        if (profile.GameClient == null)
        {
            return false;
        }

        if (replay.MatchedClient != null)
        {
            var isRetail = IsRetailClient(replay.MatchedClient.Publisher, replay.MatchedClient.ManifestId);
            return isRetail
                ? IsProfileMatchingRetail(profile, replay.MatchedClient.DataPatchManifestId)
                : IsProfileMatchingThirdParty(profile, replay.MatchedClient.ManifestId, replay.MatchedClient.DataPatchManifestId, replay.MatchedClient.Version);
        }

        return profile.GameClient.GameType == replay.GameVersion;
    }

    private static void ClearReplayProfileReference(ReplayFile replay)
    {
        replay.MatchingProfileId = null;
        replay.MatchingProfileName = null;
        replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
    }

    private static GameInstallation? ResolveInstallation(
        IReadOnlyList<GameInstallation> installations,
        GameType gameVersion,
        string? preferredPublisher = null)
    {
        var candidates = installations.Where(i =>
            (gameVersion == GameType.Generals && i.HasGenerals) ||
            (gameVersion == GameType.ZeroHour && i.HasZeroHour)).ToList();

        if (!string.IsNullOrWhiteSpace(preferredPublisher))
        {
            var matched = candidates.FirstOrDefault(i =>
                string.Equals(i.InstallationType.ToIdentifierString(), preferredPublisher, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.InstallationType.ToString(), preferredPublisher, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                return matched;
            }
        }

        return candidates.FirstOrDefault();
    }

    private static bool HasMatchingDataPatchId(string requiredPatchId, string candidatePatchId)
    {
        if (string.Equals(requiredPatchId, candidatePatchId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var reqParts = requiredPatchId.Split(ManifestConstants.ManifestIdSegmentSeparator);
        var candParts = candidatePatchId.Split(ManifestConstants.ManifestIdSegmentSeparator);
        if (reqParts.Length == candParts.Length && reqParts.Length >= 4)
        {
            if (!string.Equals(reqParts[0], candParts[0], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var v1 = reqParts[1].TrimStart('0');
            var v2 = candParts[1].TrimStart('0');
            if (!string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (var i = 2; i < reqParts.Length; i++)
            {
                if (!string.Equals(reqParts[i], candParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private static string NormalizeCrcHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return trimmed.ToUpperInvariant();
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

        var exactCheck = ManifestId.TryCreate(matchedClient.ManifestId, out var requestedClientId)
            ? await manifestPool.GetManifestAsync(requestedClientId, ct)
            : null;
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

    private static async Task AcquireGeneralsOnlineMapPacksAsync(
        IContentOrchestrator? contentOrchestrator,
        IContentManifestPool manifestPool,
        GameType targetGame,
        CancellationToken ct)
    {
        if (contentOrchestrator == null)
        {
            return;
        }

        var allManifests = await manifestPool.GetAllManifestsAsync(ct);
        if (allManifests.Success && allManifests.Data != null &&
            allManifests.Data.Any(m => m.ContentType == ContentType.MapPack &&
                                       (m.TargetGame == targetGame || m.TargetGame == GameType.Unknown) &&
                                       (string.Equals(m.Publisher?.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
                                        m.Id.Value.Contains("." + GeneralsOnlineConstants.PublisherType + ".", StringComparison.OrdinalIgnoreCase))))
        {
            return;
        }

        var mapPackQuery = new ContentSearchQuery
        {
            ProviderName = GeneralsOnlineConstants.PublisherType,
            ContentType = ContentType.MapPack,
            TargetGame = targetGame,
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
                string.Equals(m.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase));

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

    private static long ParseVersionSegments(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return 0;
        }

        long score = 0;
        foreach (var part in version.Split('.', '_', '-'))
        {
            if (int.TryParse(part, out var num))
            {
                score = (score * 10000) + num;
            }
        }

        return score;
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
        var defaultVersionInt = replay.GameVersion == GameType.ZeroHour
            ? ReplayManagerConstants.DefaultZeroHourVersionNumber
            : ReplayManagerConstants.DefaultGeneralsVersionNumber;
        var gameTypeName = replay.GameVersion == GameType.ZeroHour ? ManifestConstants.ZeroHourContentName : ManifestConstants.GeneralsContentName;
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
                ManifestContainsPublisherSegment(m.Id.Value, matchedClient.Publisher))))));
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

    private static bool ManifestContainsPublisherSegment(string manifestId, string publisher) =>
        manifestId.Contains($".{publisher}.", StringComparison.OrdinalIgnoreCase);

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

            return string.Equals(v1, v2, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static ContentSearchResult? FindBestMatchingContentSearchResult(
        IEnumerable<ContentSearchResult> items,
        CrcMappingEntry matchedClient)
    {
        var itemList = items as IList<ContentSearchResult> ?? items.ToList();

        var exactMatch = itemList.FirstOrDefault(c =>
            string.Equals(c.Id, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
        {
            return exactMatch;
        }

        return itemList.FirstOrDefault(c =>
            string.Equals(c.Version, matchedClient.Version, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(c.ProviderName, matchedClient.Publisher, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(c.AuthorName, matchedClient.Publisher, StringComparison.OrdinalIgnoreCase)) &&
            c.ContentType is ContentType.GameClient or ContentType.Mod);
    }

    private static (GameClient? TargetClient, string WorkingDir) ResolveGameInstallationContext(
        GameInstallation installation,
        GameType gameVersion)
    {
        var targetClient = gameVersion == GameType.Generals ? installation.GeneralsClient : installation.ZeroHourClient;
        var targetPath = gameVersion == GameType.Generals ? installation.GeneralsPath : installation.ZeroHourPath;
        var workingDir = !string.IsNullOrEmpty(targetPath) ? targetPath : installation.InstallationPath;
        return (targetClient, workingDir);
    }

    private static bool IsBuildDateMatching(string buildDate, string buildTime)
    {
        var parts = buildDate.Split('-');
        if (parts.Length != 3)
        {
            return false;
        }

        var year = parts[0];
        var monthNum = parts[1];
        var dayNum = parts[2].TrimStart('0');

        string monthName = monthNum switch
        {
            "01" => "Jan",
            "02" => "Feb",
            "03" => "Mar",
            "04" => "Apr",
            "05" => "May",
            "06" => "Jun",
            "07" => "Jul",
            "08" => "Aug",
            "09" => "Sep",
            "10" => "Oct",
            "11" => "Nov",
            "12" => "Dec",
            _ => string.Empty,
        };

        if (string.IsNullOrEmpty(monthName))
        {
            return false;
        }

        if (!buildTime.Contains(year, StringComparison.OrdinalIgnoreCase) ||
            !buildTime.Contains(monthName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(
            buildTime,
            $@"\b0?{Regex.Escape(dayNum)}\b",
            RegexOptions.CultureInvariant,
            ReplayFileNameRegexTimeout);
    }

    private static CrcMappingEntry ResolveHeuristicClientEntry(CrcMappingEntry heuristicClient, string exeCrc, string? iniCrc)
    {
        var normalizedIni = !string.IsNullOrEmpty(iniCrc) ? NormalizeCrcHex(iniCrc) : string.Empty;
        var isVanillaIni = string.IsNullOrEmpty(normalizedIni) || IsVanillaZeroHourIni(normalizedIni);

        return heuristicClient with
        {
            ExeCrc = exeCrc,
            IniCrc = iniCrc ?? heuristicClient.IniCrc,
            DataPatchManifestId = isVanillaIni ? null : heuristicClient.DataPatchManifestId,
            DataPatchName = isVanillaIni ? ReplayManagerConstants.Vanilla104IniName : (heuristicClient.DataPatchName ?? $"Custom INI ({normalizedIni})"),
        };
    }

    private static bool IsVanillaZeroHourIni(string normalizedIni)
    {
        return string.Equals(normalizedIni, ReplayManagerConstants.VanillaZeroHourIniCrcEnglish, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedIni, ReplayManagerConstants.VanillaZeroHourIniCrcGerman, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVanillaIni(string normalizedIni, string? baseClientIniCrc = null)
    {
        return IsVanillaZeroHourIni(normalizedIni) ||
               (baseClientIniCrc != null && string.Equals(normalizedIni, NormalizeCrcHex(baseClientIniCrc), StringComparison.OrdinalIgnoreCase)) ||
               string.Equals(normalizedIni, ReplayManagerConstants.VanillaGeneralsIniCrcGerman, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneralsOnlinePattern(string? fileName, string? versionStr)
    {
        if (!string.IsNullOrEmpty(fileName) &&
            (GeneralsOnlineFileNameRegex.IsMatch(fileName) || fileName.Contains(GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrEmpty(versionStr) && versionStr.Contains(GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase);
    }

    private static CrcMappingEntry? TryMatchGeneralsOnlineByBuildDate(List<CrcMappingEntry> entries, string buildTime)
    {
        return entries.FirstOrDefault(e =>
            (!string.IsNullOrEmpty(e.BuildDate) && IsBuildDateMatching(e.BuildDate, buildTime)) ||
            (!string.IsNullOrEmpty(e.Version) && buildTime.Contains(e.Version, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? ResolveProfileFullExePath(GameClient? client)
    {
        if (client == null)
        {
            return null;
        }

        var exePath = client.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            if (!string.IsNullOrWhiteSpace(client.WorkingDirectory))
            {
                var candidate = Path.Combine(client.WorkingDirectory, "generals.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        if (!Path.IsPathRooted(exePath) && !string.IsNullOrWhiteSpace(client.WorkingDirectory))
        {
            exePath = Path.Combine(client.WorkingDirectory, exePath);
        }

        return exePath;
    }

    private static CrcMappingEntry CreateMatchedProfileEntry(
        GameProfile profile,
        ReplayFile replay,
        string targetExeCrc,
        string? targetIniCrc)
    {
        var client = profile.GameClient;
        var normalizedIni = !string.IsNullOrEmpty(targetIniCrc) ? NormalizeCrcHex(targetIniCrc) : string.Empty;
        var isVanillaIni = string.IsNullOrEmpty(normalizedIni) || IsVanillaZeroHourIni(normalizedIni);

        return new CrcMappingEntry
        {
            ExeCrc = targetExeCrc,
            IniCrc = targetIniCrc ?? string.Empty,
            ManifestId = client?.Id ?? string.Empty,
            Publisher = client?.PublisherType ?? "Custom",
            GameType = replay.GameVersion.ToString(),
            Version = client?.Version ?? replay.Metadata?.VersionString ?? "1.04",
            Description = !string.IsNullOrWhiteSpace(client?.Name) ? client.Name : profile.Name,
            DataPatchName = isVanillaIni ? ReplayManagerConstants.Vanilla104IniName : $"Custom INI ({normalizedIni})",
        };
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
        var (targetClient, workingDir) = ResolveGameInstallationContext(installation, replay.GameVersion);
        var defaultExeName = GetDefaultExecutableName(replay.GameVersion, replay.MatchedClient?.Publisher);

        if (isRetailClient)
        {
            var exePath = targetClient?.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(workingDir))
            {
                exePath = Path.Combine(workingDir, defaultExeName);
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return (string.Empty, null);
            }

            return CreateRetailGameClient(installation, replay, defaultVersion, exePath, workingDir, targetClient);
        }

        return await ResolveThirdPartyGameClientAsync(installation, replay, defaultVersion, manifestPool, contentOrchestrator, ct);
    }

    private async Task<(string ClientManifestId, GameClient GameClient)> ResolveThirdPartyGameClientAsync(
        GameInstallation installation,
        ReplayFile replay,
        string defaultVersion,
        IContentManifestPool manifestPool,
        IContentOrchestrator? contentOrchestrator,
        CancellationToken ct)
    {
        var (targetClient, workingDir) = ResolveGameInstallationContext(installation, replay.GameVersion);
        var thirdPartyManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        if (replay.MatchedClient != null)
        {
            await AcquireThirdPartyClientAndDependenciesAsync(contentOrchestrator, manifestPool, replay.MatchedClient, replay.GameVersion, ct);
            thirdPartyManifestId = await ResolveThirdPartyClientManifestIdAsync(manifestPool, replay.MatchedClient, replay.GameVersion, ct);
        }

        var clientManifest = await GetClientManifestAsync(manifestPool, thirdPartyManifestId, ct);
        var relativeExePath = ResolveThirdPartyRelativeExePath(targetClient, workingDir, clientManifest, replay);

        var thirdPartyExePath = !string.IsNullOrWhiteSpace(workingDir)
            ? Path.Combine(workingDir, relativeExePath)
            : relativeExePath;

        var thirdPartyClientName = GetReplayClientDisplayName(replay.MatchedClient, "Third-Party Client");
        var thirdPartyGameClient = new GameClient
        {
            Id = thirdPartyManifestId,
            Name = thirdPartyClientName,
            Version = replay.MatchedClient?.Version ?? defaultVersion,
            GameType = replay.GameVersion,
            PublisherType = replay.MatchedClient?.Publisher ?? string.Empty,
            InstallationId = installation.Id,
            ExecutablePath = thirdPartyExePath,
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

        if (!IsClientAlreadyAcquired(existingManifests, matchedClient, gameVersion))
        {
            await DownloadThirdPartyClientIfMissingAsync(contentOrchestrator, matchedClient, gameVersion, ct);
        }
        else
        {
            logger.LogDebug("[ReplayManager] Client for publisher '{Publisher}' already acquired in manifest pool, skipping download.", matchedClient.Publisher);
        }

        // If GeneralsOnline, also ensure MapPack is acquired if missing
        if (string.Equals(matchedClient.Publisher, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            await AcquireGeneralsOnlineMapPacksAsync(contentOrchestrator, manifestPool, gameVersion, ct);
        }
    }

    private async Task DownloadThirdPartyClientIfMissingAsync(
        IContentOrchestrator contentOrchestrator,
        CrcMappingEntry matchedClient,
        GameType gameVersion,
        CancellationToken ct)
    {
        logger.LogInformation("Downloading and acquiring client manifest {ManifestId} from {Publisher}...", matchedClient.ManifestId, matchedClient.Publisher);
        ContentSearchResult? match = null;

        var searchQuery = new ContentSearchQuery
        {
            ProviderName = matchedClient.Publisher,
            ContentType = ContentType.GameClient,
            TargetGame = gameVersion,
        };
        var searchResult = await contentOrchestrator.SearchAsync(searchQuery, ct);
        if (searchResult?.Success == true && searchResult.Data != null)
        {
            match = FindBestMatchingContentSearchResult(searchResult.Data, matchedClient);
        }

        if (match == null && !string.IsNullOrWhiteSpace(matchedClient.CdnUrl))
        {
            var isGeneralsOnline = string.Equals(matchedClient.Publisher, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase);
            string contentId;
            if (isGeneralsOnline)
            {
                contentId = $"GeneralsOnline_{matchedClient.Version}";
            }
            else if (!string.IsNullOrWhiteSpace(matchedClient.ManifestId))
            {
                contentId = matchedClient.ManifestId;
            }
            else
            {
                contentId = $"Client_{matchedClient.Publisher}_{matchedClient.Version}";
            }

            match = new ContentSearchResult
            {
                Id = contentId,
                Name = matchedClient.Description ?? $"{matchedClient.Publisher} {matchedClient.Version}",
                Version = matchedClient.Version ?? string.Empty,
                ProviderName = matchedClient.Publisher,
                ContentType = ContentType.GameClient,
                TargetGame = gameVersion,
                RequiresResolution = isGeneralsOnline,
                ResolverId = isGeneralsOnline ? GeneralsOnlineConstants.ResolverId : string.Empty,
                SelectedDownloadUrl = matchedClient.CdnUrl,
            };

            logger.LogInformation(
                "[ReplayManager] Synthesized acquisition search result for {Publisher} {Version} using CDN URL: {CdnUrl}",
                matchedClient.Publisher,
                matchedClient.Version,
                matchedClient.CdnUrl);
        }

        if (match == null)
        {
            logger.LogWarning(
                "[ReplayManager] Could not find or synthesize downloadable content result for {Publisher} {Version}",
                matchedClient.Publisher,
                matchedClient.Version);
            return;
        }

        var acquireResult = await contentOrchestrator.AcquireContentAsync(match, null, ct);
        if (acquireResult != null && !acquireResult.Success)
        {
            logger.LogWarning("Failed to acquire client manifest {ManifestId}: {Error}", matchedClient.ManifestId, acquireResult.FirstError);
        }
    }

    private async Task<(bool Resolved, HashSet<string> AcquiredIds, List<GameProfile> Profiles)> FetchAcquiredManifestIdsAndProfilesAsync(CancellationToken ct)
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
                if (!manifestsResult.Success || manifestsResult.Data == null)
                {
                    logger.LogWarning("Failed to retrieve manifests for replay compatibility matching: {Error}", manifestsResult.FirstError);
                    return (false, acquiredIds, existingProfiles);
                }

                foreach (var manifest in manifestsResult.Data)
                {
                    acquiredIds.Add(manifest.Id.Value);
                }
            }

            var profileManager = scope.ServiceProvider.GetService<IGameProfileManager>();
            if (profileManager != null)
            {
                var profilesResult = await profileManager.GetAllProfilesAsync(ct);
                if (!profilesResult.Success || profilesResult.Data == null)
                {
                    logger.LogWarning("Failed to retrieve profiles for replay compatibility matching: {Error}", profilesResult.FirstError);
                    return (false, acquiredIds, existingProfiles);
                }

                existingProfiles.AddRange(profilesResult.Data);
            }

            return (true, acquiredIds, existingProfiles);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to retrieve acquired manifests or profiles for replay compatibility matching.");
            return (false, acquiredIds, existingProfiles);
        }
    }

    private async Task<ReplayFile> ProcessReplayFileAsync(
        string file,
        GameType version,
        bool resolved,
        HashSet<string> acquiredIds,
        IReadOnlyList<GameProfile> existingProfiles,
        CancellationToken ct)
    {
        long sizeInBytes = 0;
        DateTime lastModified = DateTime.MinValue;

        try
        {
            var info = new FileInfo(file);
            if (info.Exists)
            {
                sizeInBytes = info.Length;
                lastModified = info.LastWriteTime;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not access FileInfo for {File}", file);
        }

        var replay = new ReplayFile
        {
            FullPath = file,
            FileName = Path.GetFileName(file),
            SizeInBytes = sizeInBytes,
            LastModified = lastModified,
            GameVersion = version,
        };

        if (file.EndsWith(ReplayManagerConstants.ReplayFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            var parseResult = await headerParser.ParseHeaderAsync(file, ct);
            if (parseResult.Success && parseResult.Data != null)
            {
                replay.Metadata = parseResult.Data;
                if (resolved)
                {
                    ResolveCompatibility(replay, acquiredIds, existingProfiles);
                }
                else
                {
                    replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
                }
            }
        }

        return replay;
    }

    private GameClient? EnsureGameClientExecutable(GameClient? gameClient, GameType gameVersion, string? publisher)
    {
        if (gameClient != null && string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
        {
            var defaultExe = GetDefaultExecutableName(gameVersion, publisher);
            gameClient.ExecutablePath = !string.IsNullOrWhiteSpace(gameClient.WorkingDirectory)
                ? Path.Combine(gameClient.WorkingDirectory, defaultExe)
                : defaultExe;
            logger.LogDebug(
                "[ReplayManager] Assigned default executable path '{ExePath}' for game client '{ClientName}'",
                gameClient.ExecutablePath,
                gameClient.Name);
        }

        return gameClient;
    }

    private void LogCreateProfileStart(ReplayFile replay, GameClient? customGameClient, bool isUnmappedReplay)
    {
        if (customGameClient != null)
        {
            logger.LogInformation(
                "[ReplayManager] Creating profile for replay '{ReplayFile}' using custom game client '{ClientName}' ({Publisher}, {Version})",
                replay.FileName,
                customGameClient.Name,
                customGameClient.PublisherType,
                customGameClient.Version);
        }
        else if (isUnmappedReplay)
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
    }

    private async Task ResolveExplicitProfileNameAsync(ReplayFile replay, string profileId, CancellationToken ct)
    {
        using var checkScope = scopeFactory.CreateScope();
        var profileManager = checkScope.ServiceProvider.GetService<IGameProfileManager>();
        if (profileManager != null)
        {
            var profileCheck = await profileManager.GetProfileAsync(profileId, ct);
            if (profileCheck?.Success == true && profileCheck.Data != null)
            {
                replay.MatchingProfileName = profileCheck.Data.Name;
            }
        }
    }

    private async Task<ProfileOperationResult<GameLaunchInfo>?> EnsureReplayProfileExistsAsync(ReplayFile replay, CancellationToken ct)
    {
        logger.LogInformation("[ReplayManager] No matching profile associated with '{ReplayFile}', creating one now...", replay.FileName);
        var createResult = await CreateProfileForReplayAsync(replay, ct);
        if (!createResult.Success || createResult.Data == null)
        {
            logger.LogError("[ReplayManager] Profile creation failed for '{ReplayFile}': {Error}", replay.FileName, createResult.FirstError);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                createResult.FirstError ?? "Failed to create or find a matching profile for this replay.");
        }

        return null;
    }

    private async Task<ProfileOperationResult<GameLaunchInfo>> ExecuteProfileLaunchAsync(
        string profileId,
        string replayFileName,
        CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var launcherFacade = scope.ServiceProvider.GetRequiredService<IProfileLauncherFacade>();

            var runningStatus = await launcherFacade.GetLaunchStatusAsync(profileId, ct);
            if (runningStatus?.Success == true && runningStatus.Data?.IsRunning == true)
            {
                logger.LogWarning("[ReplayManager] Profile '{ProfileId}' is already running.", profileId);
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure("The game profile for this replay is already running.");
            }

            logger.LogInformation(
                "[ReplayManager] Launching profile '{ProfileId}' for replay '{ReplayFile}'...",
                profileId,
                replayFileName);

            var launchResult = await launcherFacade.LaunchProfileAsync(
                profileId,
                skipUserDataCleanup: true,
                cancellationToken: ct);

            if (launchResult.Success)
            {
                logger.LogInformation(
                    "[ReplayManager] Successfully launched profile '{ProfileId}' for replay '{ReplayFile}'",
                    profileId,
                    replayFileName);
                return launchResult;
            }

            logger.LogError(
                "[ReplayManager] Launch failed for profile '{ProfileId}' (Replay: '{ReplayFile}'): {Error}",
                profileId,
                replayFileName,
                launchResult.FirstError);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure(
                launchResult.FirstError ?? "Failed to launch game profile.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[ReplayManager] Exception launching profile '{ProfileId}' for replay '{ReplayFile}'", profileId, replayFileName);
            return ProfileOperationResult<GameLaunchInfo>.CreateFailure($"Launch failed: {ex.Message}");
        }
    }

    private void EnsureReplayMatch(ReplayFile replay)
    {
        if (replay.MatchedClient != null)
        {
            return;
        }

        var exeCrc = replay.Metadata?.FormattedExeCrc;
        var iniCrc = replay.Metadata?.FormattedIniCrc;

        if (string.IsNullOrEmpty(exeCrc))
        {
            return;
        }

        // 1. Exact match
        if (!string.IsNullOrEmpty(iniCrc) &&
            crcMappingRegistry.TryGetEntry(exeCrc, iniCrc, out var resolvedMatch) &&
            resolvedMatch != null)
        {
            replay.MatchedClient = resolvedMatch;
            return;
        }

        // 2. Secondary resolution: Base client by Exe CRC
        if (crcMappingRegistry.TryGetEntryByExeCrc(exeCrc, out var baseClient) && baseClient != null)
        {
            replay.MatchedClient = ResolveBaseClientEntry(baseClient, iniCrc);
            return;
        }

        // 3. Heuristic resolution for GeneralsOnline replays
        if (TryResolveGeneralsOnlineHeuristic(replay, out var heuristicClient) && heuristicClient != null)
        {
            replay.MatchedClient = ResolveHeuristicClientEntry(heuristicClient, exeCrc, iniCrc);
        }
    }

    private CrcMappingEntry ResolveBaseClientEntry(CrcMappingEntry baseClient, string? iniCrc)
    {
        var normalizedIni = !string.IsNullOrEmpty(iniCrc) ? NormalizeCrcHex(iniCrc) : string.Empty;

        if (!string.IsNullOrEmpty(iniCrc) &&
            crcMappingRegistry.TryGetEntryByIniCrc(iniCrc, out var catalogEntry) &&
            !string.IsNullOrEmpty(catalogEntry?.DataPatchManifestId))
        {
            return baseClient with
            {
                IniCrc = iniCrc,
                DataPatchManifestId = catalogEntry.DataPatchManifestId,
                DataPatchName = catalogEntry.DataPatchName,
                DataPatchCdnUrl = catalogEntry.DataPatchCdnUrl,
            };
        }

        var isVanilla = string.IsNullOrEmpty(normalizedIni) || IsVanillaIni(normalizedIni, baseClient.IniCrc);
        return baseClient with
        {
            IniCrc = iniCrc ?? baseClient.IniCrc,
            DataPatchManifestId = null,
            DataPatchName = isVanilla ? ReplayManagerConstants.Vanilla104IniName : $"Custom INI ({normalizedIni})",
            DataPatchCdnUrl = null,
        };
    }

    private CrcMappingEntry ResolveSecondaryBaseClientEntry(CrcMappingEntry baseClient, string iniCrcStr, HashSet<string> acquiredIds)
    {
        var normalizedIni = NormalizeCrcHex(iniCrcStr);

        // Step 1: Check if user has a corresponding local ContentManifest matching the INI CRC (e.g. 1.828261.generalsonline.patch.gamedata)
        var localMatchingManifestId = FindLocalMatchingManifestId(acquiredIds, iniCrcStr, normalizedIni);
        if (!string.IsNullOrEmpty(localMatchingManifestId))
        {
            crcMappingRegistry.TryGetEntryByIniCrc(iniCrcStr, out var knownEntry);
            return baseClient with
            {
                IniCrc = iniCrcStr,
                DataPatchManifestId = localMatchingManifestId,
                DataPatchName = knownEntry?.DataPatchName ?? $"Local Game Data ({normalizedIni})",
                DataPatchCdnUrl = knownEntry?.DataPatchCdnUrl,
            };
        }

        // Step 2: Check if catalog has a known data patch mapping for this INI CRC (e.g. TheSuperHackers 1.0.0/1.0.1 or GeneralsOnline)
        if (crcMappingRegistry.TryGetEntryByIniCrc(iniCrcStr, out var catalogEntry) &&
            !string.IsNullOrEmpty(catalogEntry?.DataPatchManifestId))
        {
            return baseClient with
            {
                IniCrc = iniCrcStr,
                DataPatchManifestId = catalogEntry.DataPatchManifestId,
                DataPatchName = catalogEntry.DataPatchName,
                DataPatchCdnUrl = catalogEntry.DataPatchCdnUrl,
            };
        }

        // Step 3 & 4: Check if INI is vanilla Zero Hour/Generals or custom
        var isVanilla = IsVanillaIni(normalizedIni, baseClient.IniCrc);
        return baseClient with
        {
            IniCrc = iniCrcStr,
            DataPatchManifestId = null,
            DataPatchName = isVanilla ? ReplayManagerConstants.Vanilla104IniName : $"Custom INI ({normalizedIni})",
            DataPatchCdnUrl = null,
        };
    }

    private string? FindLocalMatchingManifestId(HashSet<string> acquiredIds, string iniCrcStr, string normalizedIni)
    {
        return acquiredIds.FirstOrDefault(id =>
            id.Split('.').Any(token => string.Equals(token, normalizedIni, StringComparison.OrdinalIgnoreCase)) ||
            (crcMappingRegistry.TryGetEntryByIniCrc(iniCrcStr, out var knownEntry) &&
             !string.IsNullOrEmpty(knownEntry?.DataPatchManifestId) &&
             (string.Equals(id, knownEntry.DataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
              HasMatchingDataPatchId(knownEntry.DataPatchManifestId, id))));
    }

    private bool TryResolveGeneralsOnlineHeuristic(ReplayFile replay, out CrcMappingEntry? matchedEntry)
    {
        matchedEntry = null;

        var isGeneralsOnlinePattern = IsGeneralsOnlinePattern(replay.FileName, replay.Metadata?.VersionString);
        var buildTime = replay.Metadata?.BuildTimeString;
        var isModernBuild = false;
        if (!string.IsNullOrEmpty(buildTime))
        {
            var match = Regex.Match(buildTime, @"\b(20\d{2})\b", RegexOptions.None, ReplayFileNameRegexTimeout);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var year))
            {
                isModernBuild = year >= 2024;
            }
        }

        if (!isGeneralsOnlinePattern && !isModernBuild)
        {
            return false;
        }

        var generalsOnlineEntries = crcMappingRegistry.GetAllEntries()
            .Where(e => string.Equals(e.Publisher, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (generalsOnlineEntries.Count == 0)
        {
            return false;
        }

        // 1. Try to match by build date
        if (!string.IsNullOrEmpty(buildTime))
        {
            var dateMatch = TryMatchGeneralsOnlineByBuildDate(generalsOnlineEntries, buildTime);
            if (dateMatch != null)
            {
                matchedEntry = dateMatch;
                return true;
            }
        }

        // 2. If it is a GeneralsOnline pattern replay, fall back to the most recent GeneralsOnline entry
        if (isGeneralsOnlinePattern)
        {
            matchedEntry = generalsOnlineEntries
                .OrderByDescending(e => e.BuildDate ?? string.Empty)
                .ThenByDescending(e => ParseVersionSegments(e.Version))
                .ThenByDescending(e => e.Version ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .First();
            return true;
        }

        return false;
    }

    private async Task EnsureValidProfileReferenceAsync(ReplayFile replay, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(replay.MatchingProfileId))
        {
            return;
        }

        using var checkScope = scopeFactory.CreateScope();
        var profileManager = checkScope.ServiceProvider.GetService<IGameProfileManager>();
        if (profileManager == null)
        {
            return;
        }

        var existingCheck = await profileManager.GetProfileAsync(replay.MatchingProfileId, ct);
        if (existingCheck?.Success == true && existingCheck.Data != null)
        {
            var profile = existingCheck.Data;
            if (IsExistingProfileCompatible(profile, replay))
            {
                return;
            }

            logger.LogWarning(
                "[ReplayManager] Profile '{ProfileId}' ('{ProfileName}') is no longer compatible with replay '{ReplayFile}'. Clearing reference.",
                replay.MatchingProfileId,
                profile.Name,
                replay.FileName);

            ClearReplayProfileReference(replay);
            return;
        }

        // Verify with GetAllProfilesAsync whether the profile is truly absent from the repository
        // before clearing the reference, preventing transient load errors or string mismatch from unlinking valid profiles.
        var allProfilesResult = await profileManager.GetAllProfilesAsync(ct);
        var isDefinitivelyMissing = allProfilesResult?.Success == true &&
                                    allProfilesResult.Data?.All(p => !string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase)) == true;

        if (isDefinitivelyMissing)
        {
            logger.LogWarning(
                "[ReplayManager] Profile '{ProfileId}' for replay '{ReplayFile}' no longer exists in repository. Clearing stale reference.",
                replay.MatchingProfileId,
                replay.FileName);
            ClearReplayProfileReference(replay);
        }
    }

    private bool TryResolveProfileByExeCrc(
        ReplayFile replay,
        IReadOnlyList<GameProfile> profiles,
        out CrcMappingEntry? matchedProfileEntry)
    {
        matchedProfileEntry = null;
        if (crcCalculator == null || replay.Metadata == null || string.IsNullOrEmpty(replay.Metadata.FormattedExeCrc))
        {
            return false;
        }

        var targetExeCrc = replay.Metadata.FormattedExeCrc;
        var targetIniCrc = replay.Metadata.FormattedIniCrc;

        foreach (var profile in profiles.Where(p => p.GameClient?.GameType == replay.GameVersion))
        {
            if (TryMatchProfileExeCrc(profile, replay, targetExeCrc, targetIniCrc, out matchedProfileEntry))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryMatchProfileExeCrc(
        GameProfile profile,
        ReplayFile replay,
        string targetExeCrc,
        string? targetIniCrc,
        out CrcMappingEntry? matchedProfileEntry)
    {
        matchedProfileEntry = null;
        var exePath = ResolveProfileFullExePath(profile.GameClient);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        try
        {
            var calcResult = crcCalculator!.CalculateExeCrcAsync(exePath, ct: CancellationToken.None).GetAwaiter().GetResult();
            if (calcResult.Success && string.Equals(calcResult.Data, targetExeCrc, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "[ReplayManager] Discovered matching profile '{ProfileName}' for replay '{ReplayFile}' via executable CRC '{ExeCrc}' ({ExePath})",
                    profile.Name,
                    replay.FileName,
                    targetExeCrc,
                    exePath);

                matchedProfileEntry = CreateMatchedProfileEntry(profile, replay, targetExeCrc, targetIniCrc);
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Error calculating executable CRC for profile '{ProfileName}' ({ExePath})", profile.Name, exePath);
        }

        return false;
    }

    private void ResolveUnmappedClientCompatibility(ReplayFile replay, IReadOnlyList<GameProfile> profiles)
    {
        replay.MatchedClient = null;
        var replayBaseName = Path.GetFileNameWithoutExtension(replay.FileName);
        var expectedReplayTag = $"(Replay: {replayBaseName})";

        var unmappedCandidates = profiles
            .Where(p => p.GameClient?.GameType == replay.GameVersion)
            .OrderByDescending(p =>
            {
                if (!string.IsNullOrEmpty(replay.MatchingProfileId) &&
                    string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    return 2000;
                }

                var nameMatches = !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(replayBaseName) &&
                    p.Name.Contains(expectedReplayTag, StringComparison.OrdinalIgnoreCase);
                var descMatches = MatchesReplayFileName(p.Description, replay.FileName, logger);

                if (nameMatches || descMatches)
                {
                    return 1000;
                }

                var clientName = p.GameClient?.Name ?? string.Empty;
                if (clientName.Contains("Patch", StringComparison.OrdinalIgnoreCase) ||
                    clientName.Contains("Community", StringComparison.OrdinalIgnoreCase) ||
                    clientName.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Patch", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Community", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("Recovery", StringComparison.OrdinalIgnoreCase))
                {
                    return 250;
                }

                return 0;
            })
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unmappedProfile = unmappedCandidates.FirstOrDefault(p =>
            (!string.IsNullOrEmpty(replay.MatchingProfileId) && string.Equals(p.Id, replay.MatchingProfileId, StringComparison.OrdinalIgnoreCase)) ||
            MatchesReplayFileName(p.Description, replay.FileName, logger) ||
            (!string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(replayBaseName) && p.Name.Contains(expectedReplayTag, StringComparison.OrdinalIgnoreCase)));

        if (unmappedProfile != null)
        {
            replay.MatchingProfileId = unmappedProfile.Id;
            replay.MatchingProfileName = unmappedProfile.Name;
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Compatible;
        }
        else
        {
            replay.MatchingProfileId = null;
            replay.MatchingProfileName = null;
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
        }
    }
}
