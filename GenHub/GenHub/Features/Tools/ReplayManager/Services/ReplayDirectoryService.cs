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

            var (clientManifestId, gameClient) = await ResolveReplayGameClientAsync(
                installation, replay, defaultVersion, isRetailClient, manifestPool, contentOrchestrator, ct);

            if (gameClient == null || string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
            {
                logger.LogError("[ReplayManager] Could not determine executable path for {GameVersion} installation", replay.GameVersion);
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Could not determine executable path for {replay.GameVersion} installation.");
            }

            var resolutionContext = new ReplayContentResolutionContext(
                manifestPool, contentOrchestrator, dependencyResolver, replay, installationManifestId, clientManifestId);
            var enabledContentIds = await GatherEnabledContentIdsAsync(resolutionContext, logger, ct);

            logger.LogInformation(
                "[ReplayManager] Gathered {Count} enabled content IDs for replay profile: [{ContentIds}]",
                enabledContentIds.Count,
                string.Join(", ", enabledContentIds));

            var request = BuildReplayProfileRequest(replay, installation, clientManifestId, gameClient, enabledContentIds, preferredStrategy);

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

        await EnsureValidProfileReferenceAsync(replay, ct);

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

            var runningStatus = await launcherFacade.GetLaunchStatusAsync(replay.MatchingProfileId ?? string.Empty, ct);
            if (runningStatus?.Success == true && runningStatus.Data?.IsRunning == true)
            {
                logger.LogWarning("[ReplayManager] Profile '{ProfileId}' is already running.", replay.MatchingProfileId);
                return ProfileOperationResult<GameLaunchInfo>.CreateFailure("The game profile for this replay is already running.");
            }

            logger.LogInformation(
                "[ReplayManager] Launching profile '{ProfileId}' for replay '{ReplayFile}'...",
                replay.MatchingProfileId,
                replay.FileName);

            var launchResult = await launcherFacade.LaunchProfileAsync(
                replay.MatchingProfileId ?? string.Empty,
                skipUserDataCleanup: true,
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
        ILogger? logger = null)
    {
        var profileList = profiles.ToList();

        var isRetailClient = IsRetailClient(null, clientManifestId);

        var compatibleCandidates = profileList.Where(p =>
        {
            if (p.GameClient?.GameType != gameVersion)
            {
                return false;
            }

            return isRetailClient
                ? IsProfileMatchingRetail(p, dataPatchManifestId)
                : IsProfileMatchingThirdParty(p, clientManifestId, dataPatchManifestId, replay?.MatchedClient?.Version);
        }).ToList();

        if (compatibleCandidates.Count == 0)
        {
            return null;
        }

        return compatibleCandidates
            .Select(p => new { Profile = p, Score = ScoreCandidateProfile(p, clientManifestId, dataPatchManifestId, replay, logger) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Profile)
            .First();
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
                HasMatchingDataPatchId(dataPatchManifestId, id)) == true;
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
        if (replay.Metadata == null || string.IsNullOrEmpty(replay.Metadata.FormattedExeCrc) || string.IsNullOrEmpty(replay.Metadata.FormattedIniCrc))
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        var exeCrcStr = replay.Metadata.FormattedExeCrc;
        var iniCrcStr = replay.Metadata.FormattedIniCrc;

        if (crcMappingRegistry.TryGetEntry(exeCrcStr, iniCrcStr, out var match) && match != null)
        {
            ResolveMatchedClientCompatibility(replay, match, acquiredIds, profiles, logger);
        }
        else
        {
            ResolveUnmappedClientCompatibility(replay, profiles);
        }
    }

    private static int ScoreCandidateProfile(
        GameProfile profile,
        string clientManifestId,
        string? dataPatchManifestId,
        ReplayFile? replay,
        ILogger? logger)
    {
        var score = 0;

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
        List<string> enabledContentIds,
        WorkspaceStrategy workspaceStrategy = WorkspaceStrategy.HardLink)
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

        var isRetail = IsRetailClient(publisher, match.ManifestId);

        if (isRetail)
        {
            var gameTypeSuffix = gameVersion == GameType.ZeroHour ? ManifestConstants.ZeroHourContentName : ManifestConstants.GeneralsContentName;
            return acquiredIds.Any(id => id.Contains(ManifestConstants.GameInstallationManifestSegment, StringComparison.OrdinalIgnoreCase) && id.EndsWith(gameTypeSuffix, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static ReplayCompatibilityStatus DetermineUnconfiguredStatus(CrcMappingEntry match, bool isInstalled)
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

        replay.MatchingProfileId = null;
        replay.MatchingProfileName = null;
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

    private static async Task AcquireGeneralsOnlineMapPacksAsync(IContentOrchestrator? contentOrchestrator, IContentManifestPool manifestPool, CancellationToken ct)
    {
        if (contentOrchestrator == null)
        {
            return;
        }

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
        return items.FirstOrDefault(c =>
            string.Equals(c.Id, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Version, matchedClient.Version, StringComparison.OrdinalIgnoreCase));
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
        var targetClient = replay.GameVersion == GameType.Generals ? installation.GeneralsClient : installation.ZeroHourClient;
        var targetPath = replay.GameVersion == GameType.Generals ? installation.GeneralsPath : installation.ZeroHourPath;
        var workingDir = !string.IsNullOrEmpty(targetPath) ? targetPath : installation.InstallationPath;
        var thirdPartyManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        if (replay.MatchedClient != null)
        {
            await AcquireThirdPartyClientAndDependenciesAsync(contentOrchestrator, manifestPool, replay.MatchedClient, replay.GameVersion, ct);
            thirdPartyManifestId = await ResolveThirdPartyClientManifestIdAsync(manifestPool, replay.MatchedClient, replay.GameVersion, ct);
        }

        var clientManifest = await GetClientManifestAsync(manifestPool, thirdPartyManifestId, ct);
        string? relativeExePath = null;
        if (clientManifest != null)
        {
            var entryResolution = ManifestVariantResolver.ResolveEntryPoint(clientManifest);
            if (entryResolution.Success && !string.IsNullOrWhiteSpace(entryResolution.RelativePath))
            {
                relativeExePath = entryResolution.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            }
        }

        if (string.IsNullOrWhiteSpace(relativeExePath))
        {
            if (targetClient != null &&
                string.Equals(targetClient.PublisherType, replay.MatchedClient?.Publisher, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(targetClient.ExecutablePath))
            {
                relativeExePath = Path.GetFileName(targetClient.ExecutablePath);
            }
            else
            {
                relativeExePath = GetDefaultExecutableName(replay.GameVersion, replay.MatchedClient?.Publisher);
            }
        }

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
            await AcquireGeneralsOnlineMapPacksAsync(contentOrchestrator, manifestPool, ct);
        }
    }

    private async Task DownloadThirdPartyClientIfMissingAsync(
        IContentOrchestrator contentOrchestrator,
        CrcMappingEntry matchedClient,
        GameType gameVersion,
        CancellationToken ct)
    {
        logger.LogInformation("Downloading and acquiring client manifest {ManifestId} from {Publisher}...", matchedClient.ManifestId, matchedClient.Publisher);
        var searchQuery = new ContentSearchQuery
        {
            ProviderName = matchedClient.Publisher,
            ContentType = ContentType.GameClient,
            TargetGame = gameVersion,
        };
        var searchResult = await contentOrchestrator.SearchAsync(searchQuery, ct);
        if (searchResult?.Success != true || searchResult.Data == null)
        {
            return;
        }

        var match = FindBestMatchingContentSearchResult(searchResult.Data, matchedClient);
        if (match == null)
        {
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
        var info = new FileInfo(file);
        var replay = new ReplayFile
        {
            FullPath = file,
            FileName = Path.GetFileName(file),
            SizeInBytes = info.Length,
            LastModified = info.LastWriteTime,
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

    private void EnsureReplayMatch(ReplayFile replay)
    {
        if (replay.MatchedClient == null &&
            !string.IsNullOrEmpty(replay.Metadata?.FormattedExeCrc) &&
            !string.IsNullOrEmpty(replay.Metadata?.FormattedIniCrc) &&
            crcMappingRegistry.TryGetEntry(replay.Metadata.FormattedExeCrc, replay.Metadata.FormattedIniCrc, out var resolvedMatch))
        {
            replay.MatchedClient = resolvedMatch;
        }
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
                    return 1000;
                }

                var nameMatches = !string.IsNullOrEmpty(p.Name) && !string.IsNullOrEmpty(replayBaseName) &&
                    p.Name.Contains(expectedReplayTag, StringComparison.OrdinalIgnoreCase);
                var descMatches = MatchesReplayFileName(p.Description, replay.FileName, logger);

                if (nameMatches || descMatches)
                {
                    return 1000;
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
