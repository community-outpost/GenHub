using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Helpers;
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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
    private sealed record ReplayContentResolutionContext(
        IContentManifestPool ManifestPool,
        IContentOrchestrator? ContentOrchestrator,
        IDependencyResolver? DependencyResolver,
        ReplayFile TargetReplay,
        string InstallationManifestId,
        string ClientManifestId);

    private sealed record ProfileCandidateMatchContext(
        GameType GameVersion,
        string ClientManifestId,
        string? DataPatchManifestId,
        string? TargetExeCrc,
        bool IsRetailMatch,
        ILogger? TargetLogger,
        IGameCrcCalculatorService? CrcCalc);

    private sealed record ReplayProfileClientPreparationContext(
        GameInstallation Installation,
        ReplayFile TargetReplay,
        string DefaultVersion,
        bool IsRetailTargetClient,
        GameClient? CustomGameClient,
        string? CustomClientManifestId);

    private static readonly TimeSpan ReplayFileNameRegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Builds a profile name for a replay that is guaranteed not to exceed the specified maximum length.
    /// </summary>
    /// <param name="clientTitle">The display title of the game client.</param>
    /// <param name="replayFileName">The replay filename or full path.</param>
    /// <param name="maxLength">The maximum allowed profile name length.</param>
    /// <returns>A formatted profile name within the length limit.</returns>
    public static string BuildReplayProfileName(
        string clientTitle,
        string replayFileName,
        int maxLength = ProfileConstants.MaxProfileNameLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        var replayBaseName = Path.GetFileNameWithoutExtension(replayFileName);
        if (string.IsNullOrWhiteSpace(replayBaseName))
        {
            replayBaseName = replayFileName ?? string.Empty;
        }

        var title = string.IsNullOrWhiteSpace(clientTitle) ? ReplayManagerConstants.DefaultGameClientTitle : clientTitle.Trim();
        var candidate = $"{title} (Replay: {replayBaseName})";

        if (candidate.Length <= maxLength)
        {
            return candidate;
        }

        const string prefixSeparator = " (Replay: ";
        const string suffix = ")";
        var overhead = prefixSeparator.Length + suffix.Length; // 11 characters

        if (maxLength <= overhead)
        {
            return candidate[..Math.Min(candidate.Length, maxLength)];
        }

        var availableForNames = maxLength - overhead;
        int titleLen;
        int replayLen;

        if (title.Length + replayBaseName.Length <= availableForNames)
        {
            titleLen = title.Length;
            replayLen = replayBaseName.Length;
        }
        else
        {
            var half = availableForNames / 2;
            if (title.Length <= half)
            {
                titleLen = title.Length;
                replayLen = availableForNames - titleLen;
            }
            else if (replayBaseName.Length <= half)
            {
                replayLen = replayBaseName.Length;
                titleLen = availableForNames - replayLen;
            }
            else
            {
                titleLen = half;
                replayLen = availableForNames - half;
            }
        }

        var finalTitle = title[..titleLen].TrimEnd();
        var finalReplay = replayBaseName[..replayLen].TrimEnd();

        var result = $"{finalTitle}{prefixSeparator}{finalReplay}{suffix}";
        if (result.Length > maxLength)
        {
            result = result[..maxLength];
        }

        return result;
    }

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
        ct.ThrowIfCancellationRequested();
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
        if (crcCalculator != null)
        {
            await PreloadProfileExeCrcsAsync(existingProfiles, ct);
        }

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

        var dataPatchManifestId = replay.MatchedClient?.DataPatchManifestId;

        var compatible = await FindCompatibleProfilesAsync(
            profilesResult.Data,
            replay,
            logger,
            crcCalculator,
            ct);

        await AppendCrcCompatibleProfilesAsync(compatible, profilesResult.Data, replay, dataPatchManifestId, ct);

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
        ct.ThrowIfCancellationRequested();

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

            var prepContext = new ReplayProfileClientPreparationContext(
                installation,
                replay,
                defaultVersion,
                isRetailClient,
                customGameClient,
                customClientManifestId);

            var (clientManifestId, gameClient) = await PrepareProfileGameClientAsync(
                prepContext,
                manifestPool,
                contentOrchestrator,
                ct);

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

        IReadOnlyDictionary<string, string>? additionalArgs = null;
        using (var scope = scopeFactory.CreateScope())
        {
            var profileManager = scope.ServiceProvider.GetService<IGameProfileManager>();
            var profileResult = profileManager != null ? await profileManager.GetProfileAsync(targetProfileId ?? string.Empty, ct) : null;
            var clientCapabilities = profileResult?.Data?.GameClient?.Capabilities ?? replay.MatchedClient?.Capabilities ?? GameClientCapabilities.None;
            if (clientCapabilities.HasFlag(GameClientCapabilities.ReplayCliLaunch))
            {
                additionalArgs = new Dictionary<string, string>
                {
                    [ReplayManagerConstants.CliReplay] = replay.FileName,
                };
            }
        }

        var launchResult = await ExecuteProfileLaunchAsync(targetProfileId ?? string.Empty, replay.FileName, ct, additionalArgs);
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

    /// <inheritdoc/>
    public IReadOnlyList<GameProfile> FindCompatibleProfiles(ReplayFile replay, IReadOnlyList<GameProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (profiles == null || profiles.Count == 0)
        {
            return Array.Empty<GameProfile>();
        }

        var clientManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        var isRetailClient = IsRetailClient(null, clientManifestId);

        var compatibleCandidates = profiles.Where(p =>
        {
            if (p.GameClient?.GameType != replay.GameVersion)
            {
                return false;
            }

            return isRetailClient
                ? IsProfileMatchingRetail(p, replay.MatchedClient?.DataPatchManifestId)
                : IsProfileMatchingThirdParty(p, clientManifestId, replay.MatchedClient?.DataPatchManifestId, replay.MatchedClient?.Version);
        }).ToList();

        if (compatibleCandidates.Count == 0)
        {
            return Array.Empty<GameProfile>();
        }

        return compatibleCandidates
            .Select(p => new { Profile = p, Score = ScoreCandidateProfile(p, clientManifestId, replay.MatchedClient?.DataPatchManifestId, replay, logger) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Profile)
            .ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<GameProfile> FindRecoveryProfiles(ReplayFile replay, IReadOnlyList<GameProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(replay);
        if (profiles == null || profiles.Count == 0)
        {
            return Array.Empty<GameProfile>();
        }

        var clientManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        var replayPublisher = replay.MatchedClient?.Publisher ?? string.Empty;
        var isRetailReplay = IsRetailClient(replayPublisher, clientManifestId);
        var isGeneralsOnlineReplay = string.Equals(replayPublisher, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                                     clientManifestId.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase);
        var isSuperHackersReplay = string.Equals(replayPublisher, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(replayPublisher, PublisherTypeConstants.LegacySuperHackers, StringComparison.OrdinalIgnoreCase) ||
                                   clientManifestId.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase);

        var recoveryCandidates = profiles.Where(p =>
        {
            if (p.GameClient?.GameType != replay.GameVersion)
            {
                return false;
            }

            if (!HasCheckpointCapability(p))
            {
                return false;
            }

            var profilePublisher = p.GameClient?.PublisherType ?? string.Empty;
            var profileClientId = p.GameClient?.Id ?? string.Empty;
            var isProfileGeneralsOnline = string.Equals(profilePublisher, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                                          profileClientId.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                                          p.EnabledContentIds?.Any(id => id.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase)) == true;

            if (isGeneralsOnlineReplay)
            {
                return isProfileGeneralsOnline;
            }

            if (isProfileGeneralsOnline)
            {
                return false;
            }

            return true;
        }).ToList();

        if (recoveryCandidates.Count == 0)
        {
            return Array.Empty<GameProfile>();
        }

        return recoveryCandidates
            .Select(p => new { Profile = p, Score = ScoreRecoveryProfile(p, replay, isRetailReplay, isGeneralsOnlineReplay, isSuperHackersReplay) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Profile)
            .ToList();
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
    /// <param name="crcCalculator">Optional game CRC calculator service.</param>
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
        var isRetailClient = IsRetailClient(null, clientManifestId);
        var targetExeCrc = replay?.MatchedClient?.ExeCrc ?? replay?.Metadata?.FormattedExeCrc;

        var matchCtx = new ProfileCandidateMatchContext(
            gameVersion,
            clientManifestId,
            dataPatchManifestId,
            targetExeCrc,
            isRetailClient,
            logger,
            crcCalculator);

        var compatibleCandidates = profiles
            .Where(p => IsProfileCandidateCompatible(p, replay, matchCtx))
            .ToList();

        return compatibleCandidates
            .Select(p => new { Profile = p, Score = ScoreCandidateProfile(p, clientManifestId, dataPatchManifestId, replay, logger) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Profile)
            .ToList();
    }

    /// <summary>
    /// Asynchronously finds all compatible profiles for a replay file by preloading executable CRCs without blocking the UI thread.
    /// </summary>
    /// <param name="profiles">The available game profiles to evaluate.</param>
    /// <param name="replay">The replay file to match against.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="crcCalculator">Optional game CRC calculator service.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of compatible game profiles.</returns>
    internal static async Task<List<GameProfile>> FindCompatibleProfilesAsync(
        IEnumerable<GameProfile> profiles,
        ReplayFile replay,
        ILogger? logger = null,
        IGameCrcCalculatorService? crcCalculator = null,
        CancellationToken ct = default)
    {
        if (crcCalculator != null)
        {
            await PreloadProfileExeCrcsAsync(profiles, crcCalculator, logger, ct);
        }

        var clientManifestId = replay.MatchedClient?.ManifestId ?? string.Empty;
        var dataPatchManifestId = replay.MatchedClient?.DataPatchManifestId;

        return FindCompatibleProfiles(
            profiles,
            replay.GameVersion,
            clientManifestId,
            dataPatchManifestId,
            replay,
            logger,
            crcCalculator);
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
    /// <param name="crcCalculator">Optional game CRC calculator service.</param>
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
        var reqClientPublisher = ExtractPublisherFromManifestId(clientManifestId);
        var profileClientPublisher = profile.GameClient?.PublisherType ?? ExtractPublisherFromManifestId(profile.GameClient?.Id);

        if (!string.IsNullOrEmpty(reqClientPublisher) &&
            !string.IsNullOrEmpty(profileClientPublisher) &&
            !string.Equals(reqClientPublisher, ManifestConstants.AnyPublisherToken, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(reqClientPublisher, profileClientPublisher, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

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
            var reqPatchPublisher = ExtractPublisherFromManifestId(dataPatchManifestId);
            if (!string.IsNullOrEmpty(reqClientPublisher) &&
                !string.IsNullOrEmpty(reqPatchPublisher) &&
                !string.Equals(reqPatchPublisher, ManifestConstants.AnyPublisherToken, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(reqClientPublisher, reqPatchPublisher, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return profile.EnabledContentIds?.Any(id =>
                HasMatchingDataPatchId(dataPatchManifestId, id)) == true;
        }

        if (HasAnyEnabledDataPatches(profile))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether the specified game profile uses a Community Patch game client or includes the community patch content.
    /// </summary>
    /// <param name="p">The game profile to evaluate.</param>
    /// <returns><c>true</c> if the profile corresponds to Community Patch; otherwise, <c>false</c>.</returns>
    internal static bool IsCommunityPatchProfile(GameProfile p)
    {
        var client = p.GameClient;
        if (client == null)
        {
            return false;
        }

        if (client.GameType is not GameType.ZeroHour and not GameType.Unknown)
        {
            return false;
        }

        return (client.Id is { } id1 && id1.Contains(ReplayManagerConstants.CommunityPatchHyphenatedKeyword, StringComparison.OrdinalIgnoreCase)) ||
               (client.Id is { } id2 && id2.Contains(ReplayManagerConstants.CommunityPatchKeyword, StringComparison.OrdinalIgnoreCase)) ||
               (client.Name is { } name && name.Contains(ReplayManagerConstants.CommunityPatchDisplayName, StringComparison.OrdinalIgnoreCase)) ||
               (p.EnabledContentIds is { } contentIds && contentIds.Any(id => id.Contains(ReplayManagerConstants.CommunityPatchHyphenatedKeyword, StringComparison.OrdinalIgnoreCase) || id.Contains(ReplayManagerConstants.CommunityPatchKeyword, StringComparison.OrdinalIgnoreCase)));
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
        if (IsManifestDirectlyOrCompatiblyAcquired(match, acquiredIds))
        {
            return true;
        }

        var publisher = !string.IsNullOrWhiteSpace(match.Publisher)
            ? match.Publisher
            : ExtractPublisherFromManifestId(match.ManifestId);

        var isRetail = IsRetailClient(publisher, match.ManifestId);
        if (isRetail)
        {
            return IsRetailFallbackInstalled(gameVersion, acquiredIds);
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
    /// <param name="crcCalculator">Optional game CRC calculator service.</param>
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
    /// Preloads executable and INI CRCs for the given game profiles into cache asynchronously.
    /// </summary>
    /// <param name="profiles">The collection of profiles to preload CRCs for.</param>
    /// <param name="crcCalculator">The game CRC calculator service.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the preload operation.</returns>
    internal static async Task PreloadProfileExeCrcsAsync(
        IEnumerable<GameProfile> profiles,
        IGameCrcCalculatorService? crcCalculator,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        await ReplayCrcMatchingHelper.PreloadProfileCrcsAsync(profiles, crcCalculator, logger, ct);
    }

    /// <summary>
    /// Computes or retrieves from cache the executable CRC for a given game client executable.
    /// </summary>
    /// <param name="exePath">Path to the game client executable.</param>
    /// <param name="crcCalculator">The game CRC calculator service.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calculated CRC string formatted as 0xXXXXXXXX, or null if calculation failed.</returns>
    internal static async Task<string?> GetOrCalculateProfileExeCrcAsync(
        string exePath,
        IGameCrcCalculatorService crcCalculator,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        return await ReplayCrcMatchingHelper.GetOrCalculateProfileExeCrcAsync(exePath, crcCalculator, logger, ct);
    }

    /// <summary>
    /// Computes or retrieves from cache the INI CRC for a given game installation root.
    /// </summary>
    /// <param name="gameRoot">Path to the game installation root.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="crcCalculator">The game CRC calculator service.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calculated INI CRC string formatted as 0xXXXXXXXX, or null if calculation failed.</returns>
    internal static async Task<string?> GetOrCalculateProfileIniCrcAsync(
        string gameRoot,
        GameType gameType,
        IGameCrcCalculatorService crcCalculator,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        return await ReplayCrcMatchingHelper.GetOrCalculateProfileIniCrcAsync(gameRoot, gameType, crcCalculator, logger, ct);
    }

    /// <summary>
    /// Asynchronously resolves the compatibility status and matching profile for the specified replay file.
    /// </summary>
    /// <param name="replay">The replay file.</param>
    /// <param name="acquiredIds">The set of acquired manifest IDs.</param>
    /// <param name="profiles">The list of existing profiles.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    internal async Task ResolveCompatibilityAsync(
        ReplayFile replay,
        HashSet<string> acquiredIds,
        IReadOnlyList<GameProfile> profiles,
        CancellationToken ct = default)
    {
        if (replay.Metadata == null || string.IsNullOrEmpty(replay.Metadata.FormattedExeCrc) || string.IsNullOrEmpty(replay.Metadata.FormattedIniCrc))
        {
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
            return;
        }

        var exeCrcStr = replay.Metadata.FormattedExeCrc;
        var iniCrcStr = replay.Metadata.FormattedIniCrc;

        if (crcMappingRegistry.TryGetEntryByIniCrc(iniCrcStr, out var iniMatch) &&
            iniMatch != null &&
            !string.IsNullOrWhiteSpace(iniMatch.DataPatchName))
        {
            replay.MatchedIniPatchName = iniMatch.DataPatchName;
        }

        if (crcMappingRegistry.TryGetEntry(exeCrcStr, iniCrcStr, out var match) && match != null)
        {
            ResolveMatchedClientCompatibility(replay, match, acquiredIds, profiles, logger, crcCalculator);
        }
        else if (await TryResolveProfileByLiveCalculatedCrcsAsync(replay, profiles, ct) is { } dynamicEntry)
        {
            ResolveMatchedClientCompatibility(replay, dynamicEntry, acquiredIds, profiles, logger, crcCalculator);
        }
        else
        {
            ResolveUnmappedClientCompatibility(replay);
        }

        var recoveryProfiles = FindRecoveryProfiles(replay, profiles);
        if (recoveryProfiles.Count > 0)
        {
            replay.SupportsCheckpoints = true;
            replay.RecoveryProfileId = recoveryProfiles[0].Id;
            replay.RecoveryProfileName = recoveryProfiles[0].Name;
        }
        else if (replay.MatchedClient?.SupportsCheckpoints == true)
        {
            replay.SupportsCheckpoints = true;
        }
        else
        {
            replay.SupportsCheckpoints = false;
            replay.RecoveryProfileId = null;
            replay.RecoveryProfileName = null;
        }
    }

    private static bool IsProfileCandidateCompatible(
        GameProfile p,
        ReplayFile? replay,
        ProfileCandidateMatchContext ctx)
    {
        if (p.GameClient?.GameType != ctx.GameVersion)
        {
            return false;
        }

        if (ctx.IsRetailMatch)
        {
            if (!IsProfileExeCrcMatching(p, ctx.TargetExeCrc, ctx.CrcCalc, ctx.TargetLogger))
            {
                return false;
            }

            if (ctx.GameVersion == GameType.ZeroHour &&
                (string.IsNullOrEmpty(ctx.TargetExeCrc) || IsZeroHourRetailExeCrc(ctx.TargetExeCrc)) &&
                IsCommunityPatchProfile(p))
            {
                return IsProfileMatchingCommunityPatch(p, ctx.DataPatchManifestId);
            }

            return IsProfileMatchingRetail(p, ctx.DataPatchManifestId);
        }

        return IsProfileMatchingThirdParty(p, ctx.ClientManifestId, ctx.DataPatchManifestId, replay?.MatchedClient?.Version);
    }

    private static bool IsProfileExeCrcMatching(
        GameProfile profile,
        string? targetExeCrc,
        IGameCrcCalculatorService? crcCalculator,
        ILogger? logger)
    {
        if (string.IsNullOrEmpty(targetExeCrc))
        {
            return true;
        }

        var exePath = ResolveProfileFullExePath(profile.GameClient);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return crcCalculator == null;
        }

        try
        {
            var crc = GetCachedExeCrc(exePath);
            if (string.IsNullOrEmpty(crc))
            {
                return crcCalculator == null;
            }

            return IsExeCrcCompatible(crc, targetExeCrc);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "[ReplayManager] Error verifying profile '{ProfileName}' EXE CRC for replay matching", profile.Name);
            return false;
        }
    }

    private static string? GetCachedExeCrc(string exePath) =>
        ReplayCrcMatchingHelper.GetCachedExeCrc(exePath);

    private static bool IsExeCrcCompatible(string actualCrc, string targetExeCrc)
    {
        if (string.Equals(actualCrc, targetExeCrc, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsZeroHourRetailExeCrc(actualCrc) && IsZeroHourRetailExeCrc(targetExeCrc))
        {
            return true;
        }

        return IsGeneralsRetailExeCrc(actualCrc) && IsGeneralsRetailExeCrc(targetExeCrc);
    }

    private static bool IsZeroHourRetailExeCrc(string? crc) =>
        ReplayCrcMatchingHelper.IsZeroHourRetailExeCrc(crc);

    private static bool IsGeneralsRetailExeCrc(string? crc) =>
        ReplayCrcMatchingHelper.IsGeneralsRetailExeCrc(crc);

    private static bool IsProfileCrcMatching(
        string calculatedCrc,
        string targetExeCrc,
        GameType gameVersion)
    {
        if (string.Equals(calculatedCrc, targetExeCrc, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (gameVersion == GameType.ZeroHour)
        {
            return IsZeroHourRetailExeCrc(calculatedCrc) && IsZeroHourRetailExeCrc(targetExeCrc);
        }

        if (gameVersion == GameType.Generals)
        {
            return IsGeneralsRetailExeCrc(calculatedCrc) && IsGeneralsRetailExeCrc(targetExeCrc);
        }

        return false;
    }

    private static bool IsProfileMatchingCommunityPatch(GameProfile profile, string? dataPatchManifestId)
    {
        if (profile.GameClient == null)
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

    private static bool HasCheckpointCapability(GameProfile profile)
    {
        return HasClientCheckpointCapability(profile.GameClient) ||
               HasEnabledContentCheckpointCapability(profile.EnabledContentIds);
    }

    private static bool HasClientCheckpointCapability(GameClient? client)
    {
        if (client == null)
        {
            return false;
        }

        return (client.Capabilities & GameClientCapabilities.AllRecoveryFeatures) == GameClientCapabilities.AllRecoveryFeatures;
    }

    private static bool HasEnabledContentCheckpointCapability(IEnumerable<string>? contentIds)
    {
        return contentIds?.Any(id =>
            (GameClientCapabilitiesHelper.InferCapabilities(null, id, null) & GameClientCapabilities.AllRecoveryFeatures) == GameClientCapabilities.AllRecoveryFeatures) == true;
    }

    private static int ScoreRecoveryProfile(
        GameProfile profile,
        ReplayFile replay,
        bool isRetailReplay,
        bool isGeneralsOnlineReplay,
        bool isSuperHackersReplay)
    {
        var score = 0;

        if (IsDedicatedToThisReplay(profile, replay, null))
        {
            score += 1000;
        }
        else if (!IsDedicatedToAnotherReplay(profile))
        {
            score += 500;
        }

        if (replay.MatchedClient != null && string.Equals(profile.GameClient?.Id, replay.MatchedClient.ManifestId, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        var profilePublisher = profile.GameClient?.PublisherType ?? string.Empty;
        var profileClientId = profile.GameClient?.Id ?? string.Empty;

        var matchesGeneralsOnline = isGeneralsOnlineReplay &&
            (string.Equals(profilePublisher, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
             profileClientId.Contains(PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase));

        var matchesSuperHackers = isSuperHackersReplay &&
            (string.Equals(profilePublisher, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
             profileClientId.Contains(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase));

        if (matchesGeneralsOnline || matchesSuperHackers)
        {
            score += 150;
        }
        else if (isRetailReplay && (profileClientId.Contains(ReplayManagerConstants.CapabilityRecoveryKeyword, StringComparison.OrdinalIgnoreCase) || profile.Name.Contains(ReplayManagerConstants.CapabilityRecoveryKeyword, StringComparison.OrdinalIgnoreCase)))
        {
            score += 100;
        }

        return score;
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
        if (!string.IsNullOrEmpty(profile.Name) && !string.IsNullOrEmpty(replayBaseName))
        {
            if (profile.Name.Contains($"(Replay: {replayBaseName})", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return MatchesTruncatedReplayMarker(profile.Name, replayBaseName);
        }

        return false;
    }

    private static bool MatchesTruncatedReplayMarker(string profileName, string replayBaseName)
    {
        // Only a name truncated to the length limit can produce a genuine
        // prefix match; a short name that happens to be a prefix of an
        // unrelated replay's base name must not match.
        if (profileName.Length != ProfileConstants.MaxProfileNameLength)
        {
            return false;
        }

        // Fallback for truncated replay profile names: if the profile name contains "(Replay: "
        // and the replay segment in the profile name matches the beginning of replayBaseName.
        const string replayMarker = "(Replay: ";
        var markerIdx = profileName.IndexOf(replayMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0)
        {
            return false;
        }

        var contentStart = markerIdx + replayMarker.Length;
        var closingParenIdx = profileName.LastIndexOf(')');
        if (closingParenIdx <= contentStart)
        {
            return false;
        }

        var nameReplayPart = profileName[contentStart..closingParenIdx].TrimEnd();
        return nameReplayPart.Length > 0 &&
               replayBaseName.StartsWith(nameReplayPart, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDedicatedToAnotherReplay(GameProfile profile) =>
        ReplayCrcMatchingHelper.IsDedicatedToAnotherReplay(profile);

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
                             IsRetailClient(context.TargetReplay.MatchedClient?.Publisher, context.ClientManifestId);

        // 2. Add companion manifests from third party publisher (e.g. MapPack and Patches)
        if (!isRetailClient && context.TargetReplay.MatchedClient != null && string.IsNullOrEmpty(context.TargetReplay.MatchedClient.DataPatchManifestId))
        {
            await AddThirdPartyCompanionManifestsAsync(
                context.ManifestPool, context.ClientManifestId, context.TargetReplay, enabledContentIds, logger, ct);
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
        if (context.TargetReplay.MatchedClient == null ||
            string.IsNullOrEmpty(context.TargetReplay.MatchedClient.DataPatchManifestId))
        {
            return;
        }

        var rawDataPatchId = context.TargetReplay.MatchedClient.DataPatchManifestId;

        await AcquireDataPatchIfMissingAsync(
            context.ContentOrchestrator, context.ManifestPool, rawDataPatchId, context.TargetReplay.MatchedClient.Publisher, context.TargetReplay.GameVersion, ct);

        var resolvedDataPatchId = await ResolveExistingPatchManifestIdAsync(context.ManifestPool, rawDataPatchId, context.TargetReplay.GameVersion, ct);
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
                string.Equals(m.Id.Value, dataPatchManifestId, StringComparison.OrdinalIgnoreCase) ||
                (m.TargetGame == gameVersion &&
                 (m.ContentType == ContentType.Patch || m.ContentType == ContentType.MapPack) &&
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

            return ReplayManagerConstants.DefaultGameClientTitle;
        }

        return replay.GameVersion == GameType.ZeroHour
            ? ReplayManagerConstants.ZeroHourGameClientTitle
            : ReplayManagerConstants.GeneralsGameClientTitle;
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

        var profileName = BuildReplayProfileName(clientTitle, replay.FileName);
        var description = isUnmapped
            ? $"[replay:{replay.FileName}] Profile configured for unmapped replay {replay.FileName} (Exe: {replay.Metadata?.FormattedExeCrc ?? ReplayManagerConstants.NotAvailable}, INI: {replay.Metadata?.FormattedIniCrc ?? ReplayManagerConstants.NotAvailable})"
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

    private static bool IsCustomRetailClient(GameClient? customGameClient, string? customClientManifestId)
    {
        if (customGameClient == null)
        {
            return false;
        }

        return IsRetailClient(customGameClient.PublisherType, customClientManifestId) ||
               customClientManifestId?.Contains(ReplayManagerConstants.RetailGameClientSegment, StringComparison.OrdinalIgnoreCase) == true ||
               customGameClient.Id?.Contains(ReplayManagerConstants.RetailGameClientSegment, StringComparison.OrdinalIgnoreCase) == true ||
               string.IsNullOrEmpty(customClientManifestId);
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

    private static bool IsManifestDirectlyOrCompatiblyAcquired(CrcMappingEntry match, HashSet<string> acquiredIds)
    {
        if (string.IsNullOrEmpty(match.ManifestId))
        {
            return false;
        }

        if (acquiredIds.Contains(match.ManifestId))
        {
            return true;
        }

        return acquiredIds.Any(id =>
            string.Equals(match.ManifestId, id, StringComparison.OrdinalIgnoreCase) ||
            (DependencyResolver.HasCompatibleCatalogIdentity(match.ManifestId, id) &&
             HasMatchingClientVersion(match.ManifestId, id, match.Version, null)));
    }

    private static bool IsRetailFallbackInstalled(GameType gameVersion, HashSet<string> acquiredIds)
    {
        var gameTypeSuffix = gameVersion == GameType.ZeroHour ? ManifestConstants.ZeroHourContentName : ManifestConstants.GeneralsContentName;
        var hasBaseInstallation = acquiredIds.Any(id =>
            id.Contains(ManifestConstants.GameInstallationManifestSegment, StringComparison.OrdinalIgnoreCase) &&
            id.EndsWith(gameTypeSuffix, StringComparison.OrdinalIgnoreCase));

        if (hasBaseInstallation)
        {
            return true;
        }

        if (gameVersion == GameType.ZeroHour)
        {
            return acquiredIds.Any(id =>
                id.Contains(ReplayManagerConstants.CommunityPatchHyphenatedKeyword, StringComparison.OrdinalIgnoreCase) ||
                id.Contains(ReplayManagerConstants.CommunityPatchKeyword, StringComparison.OrdinalIgnoreCase) ||
                id.Contains(ReplayManagerConstants.ZeroHourManifestSegment, StringComparison.OrdinalIgnoreCase));
        }

        if (gameVersion == GameType.Generals)
        {
            return acquiredIds.Any(id => id.Contains(ReplayManagerConstants.GeneralsManifestSegment, StringComparison.OrdinalIgnoreCase));
        }

        return false;
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

        return !HasAnyEnabledDataPatches(profile);
    }

    private static bool HasAnyEnabledDataPatches(GameProfile profile) =>
        profile.EnabledContentIds?.Any(id =>
            id.Contains(ManifestConstants.GameDataManifestSegment, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(ManifestConstants.DataPatchManifestSegment, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(ManifestConstants.CommunityManifestSegment, StringComparison.OrdinalIgnoreCase) ||
            id.Contains(ManifestConstants.ModManifestSegment, StringComparison.OrdinalIgnoreCase)) == true;

    private static string ExtractPublisherFromManifestId(string? manifestId)
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
            if (isRetail)
            {
                if (replay.GameVersion == GameType.ZeroHour &&
                    (string.IsNullOrEmpty(replay.MatchedClient.ExeCrc) || IsZeroHourRetailExeCrc(replay.MatchedClient.ExeCrc)) &&
                    IsCommunityPatchProfile(profile))
                {
                    return IsProfileMatchingCommunityPatch(profile, replay.MatchedClient.DataPatchManifestId);
                }

                return IsProfileMatchingRetail(profile, replay.MatchedClient.DataPatchManifestId);
            }

            return IsProfileMatchingThirdParty(profile, replay.MatchedClient.ManifestId, replay.MatchedClient.DataPatchManifestId, replay.MatchedClient.Version);
        }

        return profile.GameClient.GameType == replay.GameVersion &&
               IsDedicatedToThisReplay(profile, replay, null);
    }

    private static void ClearReplayProfileReference(ReplayFile replay)
    {
        replay.MatchingProfileId = null;
        replay.MatchingProfileName = null;
        replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
    }

    private static void ResolveUnmappedClientCompatibility(ReplayFile replay)
    {
        replay.MatchedClient = null;
        replay.MatchingProfileId = null;
        replay.MatchingProfileName = null;
        replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
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

    private static string NormalizeCrcHex(string? value) =>
        ReplayCrcMatchingHelper.NormalizeCrcHex(value);

    private static string GetDefaultExecutableName(GameType gameVersion, string? publisher) =>
        ReplayCrcMatchingHelper.GetDefaultExecutableName(gameVersion, publisher);

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

        var clientName = GetReplayClientDisplayName(replay.MatchedClient, ReplayManagerConstants.RetailClientDisplayName);
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

    private static bool IsVanillaZeroHourIni(string normalizedIni)
    {
        return string.Equals(normalizedIni, ReplayManagerConstants.VanillaZeroHourIniCrcEnglish, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedIni, ReplayManagerConstants.VanillaZeroHourIniCrcGerman, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveProfileFullExePath(GameClient? client) =>
        ReplayCrcMatchingHelper.ResolveProfileFullExePath(client);

    private static CrcMappingEntry CreateMatchedProfileEntry(
        GameProfile profile,
        ReplayFile replay,
        string targetExeCrc,
        string? targetIniCrc)
    {
        var client = profile.GameClient;
        var normalizedIni = !string.IsNullOrEmpty(targetIniCrc) ? NormalizeCrcHex(targetIniCrc) : string.Empty;
        var isVanillaIni = string.IsNullOrEmpty(normalizedIni) || IsVanillaZeroHourIni(normalizedIni);

        var fallbackVersion = replay.GameVersion == GameType.Generals
            ? ReplayManagerConstants.GeneralsRetailVersion
            : ReplayManagerConstants.ZeroHourRetailVersion;

        return new CrcMappingEntry
        {
            ExeCrc = targetExeCrc,
            IniCrc = targetIniCrc ?? string.Empty,
            ManifestId = client?.Id ?? string.Empty,
            Publisher = client?.PublisherType ?? ReplayManagerConstants.CustomPublisher,
            GameType = replay.GameVersion.ToString(),
            Version = client?.Version ?? replay.Metadata?.VersionString ?? fallbackVersion,
            Description = !string.IsNullOrWhiteSpace(client?.Name) ? client.Name : profile.Name,
            DataPatchName = isVanillaIni ? ReplayManagerConstants.Vanilla104IniName : $"{ReplayManagerConstants.CustomIniPrefix} ({normalizedIni})",
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

        var thirdPartyClientName = GetReplayClientDisplayName(replay.MatchedClient, ReplayManagerConstants.ThirdPartyClientDisplayName);
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
                contentId = string.Format(CultureInfo.InvariantCulture, ReplayManagerConstants.GeneralsOnlineContentIdPattern, matchedClient.Version);
            }
            else if (!string.IsNullOrWhiteSpace(matchedClient.ManifestId))
            {
                contentId = matchedClient.ManifestId;
            }
            else
            {
                contentId = string.Format(CultureInfo.InvariantCulture, ReplayManagerConstants.ThirdPartyClientContentIdPattern, matchedClient.Publisher, matchedClient.Version);
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
        var acquiredIds = new HashSet<string>(StringComparer.Ordinal);
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
                    await ResolveCompatibilityAsync(replay, acquiredIds, existingProfiles, ct);
                }
                else
                {
                    replay.CompatibilityStatus = ReplayCompatibilityStatus.Unknown;
                }
            }
        }

        return replay;
    }

    private async Task<(string ClientManifestId, GameClient? GameClient)> PrepareProfileGameClientAsync(
        ReplayProfileClientPreparationContext context,
        IContentManifestPool manifestPool,
        IContentOrchestrator? contentOrchestrator,
        CancellationToken ct)
    {
        var isCustomRetail = IsCustomRetailClient(context.CustomGameClient, context.CustomClientManifestId);

        var (clientManifestId, gameClient) = (context.CustomGameClient != null && !isCustomRetail)
            ? (context.CustomClientManifestId ?? context.CustomGameClient.Id ?? string.Empty, context.CustomGameClient)
            : await ResolveReplayGameClientAsync(
                context.Installation,
                context.TargetReplay,
                context.DefaultVersion,
                isRetailClient: context.IsRetailTargetClient || isCustomRetail,
                manifestPool,
                contentOrchestrator,
                ct);

        if (context.CustomGameClient != null && isCustomRetail && !string.IsNullOrWhiteSpace(context.CustomGameClient.Name) && gameClient != null)
        {
            gameClient.Name = context.CustomGameClient.Name;
        }

        if (context.CustomGameClient != null && !isCustomRetail && gameClient != null)
        {
            var (_, installWorkingDir) = ResolveGameInstallationContext(context.Installation, context.TargetReplay.GameVersion);
            if (string.IsNullOrWhiteSpace(gameClient.WorkingDirectory))
            {
                gameClient.WorkingDirectory = installWorkingDir;
            }

            if (string.IsNullOrWhiteSpace(gameClient.InstallationId))
            {
                gameClient.InstallationId = context.Installation.Id;
            }

            if (gameClient.GameType == GameType.Unknown)
            {
                gameClient.GameType = context.TargetReplay.GameVersion;
            }
        }

        var publisher = isCustomRetail ? null : (context.CustomGameClient?.PublisherType ?? context.TargetReplay.MatchedClient?.Publisher);
        gameClient = EnsureGameClientExecutable(gameClient, context.TargetReplay.GameVersion, publisher);

        return (clientManifestId, gameClient);
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
                replay.Metadata?.FormattedExeCrc ?? ReplayManagerConstants.NotAvailable,
                replay.Metadata?.FormattedIniCrc ?? ReplayManagerConstants.NotAvailable,
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
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? additionalArguments = null)
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

            var launchResult = additionalArguments != null && additionalArguments.Count > 0
                ? await launcherFacade.LaunchProfileAsync(
                    profileId,
                    skipUserDataCleanup: true,
                    additionalArguments: additionalArguments,
                    cancellationToken: ct)
                : await launcherFacade.LaunchProfileAsync(
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

        if (!string.IsNullOrEmpty(iniCrc) &&
            crcMappingRegistry.TryGetEntryByIniCrc(iniCrc, out var iniMatch) &&
            iniMatch != null &&
            !string.IsNullOrWhiteSpace(iniMatch.DataPatchName))
        {
            replay.MatchedIniPatchName = iniMatch.DataPatchName;
        }

        if (!string.IsNullOrEmpty(iniCrc) &&
            crcMappingRegistry.TryGetEntry(exeCrc, iniCrc, out var resolvedMatch) &&
            resolvedMatch != null)
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

    private async Task<CrcMappingEntry?> TryResolveProfileByLiveCalculatedCrcsAsync(
        ReplayFile replay,
        IReadOnlyList<GameProfile> profiles,
        CancellationToken ct)
    {
        if (crcCalculator == null || replay.Metadata == null ||
            string.IsNullOrEmpty(replay.Metadata.FormattedExeCrc) ||
            string.IsNullOrEmpty(replay.Metadata.FormattedIniCrc))
        {
            return null;
        }

        var targetExeCrc = replay.Metadata.FormattedExeCrc;
        var targetIniCrc = replay.Metadata.FormattedIniCrc;

        foreach (var profile in profiles.Where(p => p.GameClient?.GameType == replay.GameVersion))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var matched = await TryMatchLiveProfileCrcAsync(profile, replay, targetExeCrc, targetIniCrc, ct);
            if (matched != null)
            {
                return matched;
            }
        }

        return null;
    }

    private async Task<CrcMappingEntry?> TryMatchLiveProfileCrcAsync(
        GameProfile profile,
        ReplayFile replay,
        string targetExeCrc,
        string targetIniCrc,
        CancellationToken ct)
    {
        var exePath = ResolveProfileFullExePath(profile.GameClient);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return null;
        }

        var calculatedExeCrc = await GetOrCalculateProfileExeCrcAsync(exePath, ct);
        if (string.IsNullOrEmpty(calculatedExeCrc) ||
            !IsProfileCrcMatching(calculatedExeCrc, targetExeCrc, replay.GameVersion))
        {
            return null;
        }

        var gameRoot = Path.GetDirectoryName(exePath) ?? string.Empty;
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
        {
            return null;
        }

        if (await IsLiveIniCrcMatchAsync(profile, gameRoot, targetIniCrc, ct))
        {
            logger.LogInformation(
                "[ReplayManager] Discovered matching profile '{ProfileName}' for replay '{ReplayFile}' via exact live calculated CRCs: exe='{ExeCrc}', ini='{IniCrc}' ({ExePath})",
                profile.Name,
                replay.FileName,
                targetExeCrc,
                targetIniCrc,
                exePath);

            return CreateMatchedProfileEntry(profile, replay, targetExeCrc, targetIniCrc);
        }

        return null;
    }

    private async Task<bool> IsLiveIniCrcMatchAsync(
        GameProfile profile,
        string gameRoot,
        string targetIniCrc,
        CancellationToken ct)
    {
        try
        {
            var calculatedIni = await ReplayCrcMatchingHelper.GetOrCalculateProfileIniCrcAsync(
                gameRoot,
                profile.GameClient!.GameType,
                crcCalculator!,
                logger,
                ct);

            if (string.IsNullOrEmpty(calculatedIni))
            {
                return false;
            }

            var normalizedCalcIni = NormalizeCrcHex(calculatedIni);
            var normalizedTargetIni = NormalizeCrcHex(targetIniCrc);
            return string.Equals(normalizedCalcIni, normalizedTargetIni, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Failed to calculate live INI CRC for profile '{ProfileName}' at '{GameRoot}'", profile.Name, gameRoot);
            return false;
        }
    }

    private Task PreloadProfileExeCrcsAsync(IEnumerable<GameProfile> profiles, CancellationToken ct) =>
        PreloadProfileExeCrcsAsync(profiles, crcCalculator, logger, ct);

    private Task<string?> GetOrCalculateProfileExeCrcAsync(string exePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(crcCalculator);
        return GetOrCalculateProfileExeCrcAsync(exePath, crcCalculator, logger, ct);
    }

    private async Task AppendCrcCompatibleProfilesAsync(
        List<GameProfile> compatible,
        IReadOnlyList<GameProfile> allProfiles,
        ReplayFile replay,
        string? dataPatchManifestId,
        CancellationToken ct)
    {
        if (crcCalculator == null || string.IsNullOrEmpty(replay.Metadata?.FormattedExeCrc))
        {
            return;
        }

        var targetExeCrc = replay.Metadata.FormattedExeCrc;
        var compatibleIds = new HashSet<string>(compatible.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var profile in allProfiles.Where(p => p.GameClient?.GameType == replay.GameVersion && !compatibleIds.Contains(p.Id)))
        {
            if (!string.IsNullOrEmpty(dataPatchManifestId))
            {
                if (profile.EnabledContentIds?.Any(id => HasMatchingDataPatchId(dataPatchManifestId, id)) != true)
                {
                    continue;
                }
            }
            else
            {
                if (HasAnyEnabledDataPatches(profile))
                {
                    continue;
                }
            }

            if (await TryMatchProfileExeCrcAsync(profile, replay, targetExeCrc, ct))
            {
                compatible.Add(profile);
                compatibleIds.Add(profile.Id);
            }
        }
    }

    private async Task<bool> TryMatchProfileExeCrcAsync(
        GameProfile profile,
        ReplayFile replay,
        string targetExeCrc,
        CancellationToken ct)
    {
        var exePath = ResolveProfileFullExePath(profile.GameClient);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            return false;
        }

        var dataPatchId = replay.MatchedClient?.DataPatchManifestId;
        if (!string.IsNullOrEmpty(dataPatchId) &&
            profile.EnabledContentIds?.Any(id => HasMatchingDataPatchId(dataPatchId, id)) != true)
        {
            return false;
        }

        var calculatedCrc = await GetOrCalculateProfileExeCrcAsync(exePath, ct);
        if (string.IsNullOrEmpty(calculatedCrc) ||
            !IsProfileCrcMatching(calculatedCrc, targetExeCrc, replay.GameVersion))
        {
            return false;
        }

        if (await IsIniCrcMismatchAsync(profile, exePath, replay.Metadata?.FormattedIniCrc, ct))
        {
            return false;
        }

        logger.LogInformation(
            "[ReplayManager] Discovered matching profile '{ProfileName}' for replay '{ReplayFile}' via executable CRC '{ExeCrc}' ({ExePath})",
            profile.Name,
            replay.FileName,
            targetExeCrc,
            exePath);

        return true;
    }

    private async Task<bool> IsIniCrcMismatchAsync(
        GameProfile profile,
        string exePath,
        string? targetIniCrc,
        CancellationToken ct)
    {
        if (crcCalculator == null || string.IsNullOrEmpty(targetIniCrc))
        {
            return false;
        }

        var gameRoot = Path.GetDirectoryName(exePath) ?? string.Empty;
        if (!Directory.Exists(gameRoot))
        {
            return false;
        }

        try
        {
            var calculatedIni = await ReplayCrcMatchingHelper.GetOrCalculateProfileIniCrcAsync(
                gameRoot,
                profile.GameClient!.GameType,
                crcCalculator,
                logger,
                ct);

            if (!string.IsNullOrEmpty(calculatedIni))
            {
                var normalizedCalcIni = NormalizeCrcHex(calculatedIni);
                var normalizedTargetIni = NormalizeCrcHex(targetIniCrc);
                if (!string.Equals(normalizedCalcIni, normalizedTargetIni, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[ReplayManager] Could not calculate INI CRC for profile '{ProfileId}'", profile.Id);
        }

        return false;
    }
}
