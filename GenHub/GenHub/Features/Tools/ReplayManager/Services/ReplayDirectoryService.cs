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
            var manifestPool = scope.ServiceProvider.GetRequiredService<IContentManifestPool>();
            var contentOrchestrator = scope.ServiceProvider.GetService<IContentOrchestrator>();

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

            // Ensure base GameInstallation and GameClient manifests are registered in the manifest pool
            await installationService.CreateAndRegisterInstallationManifestsAsync(installation, ct);
            var clientDetector = scope.ServiceProvider.GetService<IGameClientDetector>();
            if (clientDetector != null)
            {
                await clientDetector.DetectGameClientsFromInstallationsAsync([installation], ct);
            }

            var defaultVersion = replay.GameVersion == GameType.ZeroHour
                ? ManifestConstants.ZeroHourManifestVersion
                : ManifestConstants.GeneralsManifestVersion;

            var installationManifestId = ManifestIdGenerator.GenerateGameInstallationId(
                installation, replay.GameVersion, defaultVersion);

            var isRetailClient = string.IsNullOrWhiteSpace(replay.MatchedClient.Publisher) ||
                                 string.Equals(replay.MatchedClient.Publisher, "ea", StringComparison.OrdinalIgnoreCase);

            var (clientManifestId, gameClient) = await ResolveReplayGameClientAsync(
                installation, replay, defaultVersion, isRetailClient, manifestPool, contentOrchestrator, ct);

            if (gameClient == null || string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Could not determine executable path for {replay.GameVersion} installation.");
            }

            var enabledContentIds = new List<string>
            {
                installationManifestId,
                clientManifestId,
            };

            if (!isRetailClient)
            {
                await AddThirdPartyCompanionManifestsAsync(manifestPool, replay.MatchedClient.Publisher, enabledContentIds, ct);
            }

            if (!string.IsNullOrEmpty(replay.MatchedClient.DataPatchManifestId) &&
                !enabledContentIds.Contains(replay.MatchedClient.DataPatchManifestId))
            {
                await AcquireDataPatchIfMissingAsync(
                    contentOrchestrator, manifestPool, replay.MatchedClient.DataPatchManifestId, replay.MatchedClient.Publisher, replay.GameVersion, ct);
                enabledContentIds.Add(replay.MatchedClient.DataPatchManifestId);
            }

            var profileName = $"{replay.MatchedClient.Description ?? replay.MatchedClient.Publisher} (Replay: {Path.GetFileNameWithoutExtension(replay.FileName)})";

            var request = new CreateProfileRequest
            {
                Name = profileName,
                Description = $"Profile configured for {replay.MatchedClient.Description} (Exe: {replay.Metadata?.FormattedExeCrc}, INI: {replay.Metadata?.FormattedIniCrc})",
                GameInstallationId = installation.Id,
                GameClientId = clientManifestId,
                GameClient = gameClient,
                EnabledContentIds = enabledContentIds,
                WorkspaceStrategy = WorkspaceStrategy.HardLink,
                UseSteamLaunch = installation.InstallationType == GameInstallationType.Steam,
            };

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

    private static GameProfile? FindMatchingProfile(IEnumerable<GameProfile> profiles, GameType gameVersion, string clientManifestId, string? dataPatchManifestId)
    {
        var isRetailClient = clientManifestId.Contains(ReplayManagerConstants.RetailManifestSegment) ||
                             clientManifestId.Contains(".steam.") ||
                             clientManifestId.Contains(".eaapp.");

        return profiles.FirstOrDefault(p =>
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
        var isProfileThirdParty = string.Equals(profile.GameClient?.PublisherType, PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(profile.GameClient?.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                                  (profile.GameClient?.Id != null && (profile.GameClient.Id.Contains(".superhackers.") || profile.GameClient.Id.Contains(".generalsonline."))) ||
                                  (profile.EnabledContentIds != null && profile.EnabledContentIds.Any(id => id.Contains(".superhackers.") || id.Contains(".generalsonline.")));

        if (isProfileThirdParty)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(dataPatchManifestId))
        {
            return profile.EnabledContentIds != null && profile.EnabledContentIds.Any(id => string.Equals(id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase));
        }

        var hasCustomDataPatch = profile.EnabledContentIds != null && profile.EnabledContentIds.Any(id =>
            id.Contains(".gamedata.") || id.Contains(".datapatch.") || id.Contains(".community.") || id.Contains(".mod."));

        return !hasCustomDataPatch;
    }

    private static bool IsProfileMatchingThirdParty(GameProfile profile, string clientManifestId, string? dataPatchManifestId)
    {
        var clientMatches = string.Equals(profile.GameClient?.Id, clientManifestId, StringComparison.OrdinalIgnoreCase) ||
                            profile.EnabledContentIds?.Any(id => string.Equals(id, clientManifestId, StringComparison.OrdinalIgnoreCase)) == true;

        if (!clientMatches)
        {
            var publisher = ExtractPublisherFromManifestId(clientManifestId);
            if (!string.IsNullOrEmpty(publisher) &&
                (string.Equals(profile.GameClient?.PublisherType, publisher, StringComparison.OrdinalIgnoreCase) ||
                 (profile.EnabledContentIds != null && profile.EnabledContentIds.Any(id => id.Contains("." + publisher + ".")))))
            {
                clientMatches = true;
            }
        }

        if (!clientMatches)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(dataPatchManifestId))
        {
            return profile.EnabledContentIds?.Any(id => string.Equals(id, dataPatchManifestId, StringComparison.OrdinalIgnoreCase)) == true;
        }

        return true;
    }

    private static string ExtractPublisherFromManifestId(string manifestId)
    {
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

        var isRetail = string.IsNullOrWhiteSpace(match.Publisher) ||
                       string.Equals(match.Publisher, "ea", StringComparison.OrdinalIgnoreCase) ||
                       match.ManifestId.Contains(ReplayManagerConstants.RetailManifestSegment);

        if (!isRetail)
        {
            return false;
        }

        var gameTypeSuffix = gameVersion == GameType.ZeroHour ? "zerohour" : "generals";
        return acquiredIds.Any(id => id.Contains(".gameinstallation.") && id.EndsWith(gameTypeSuffix, StringComparison.OrdinalIgnoreCase));
    }

    private static ReplayCompatibilityStatus DetermineUnconfiguredStatus(CrcMappingEntry match, bool isInstalled)
    {
        if (isInstalled)
        {
            return ReplayCompatibilityStatus.RequiresProfile;
        }

        var isRetail = string.IsNullOrWhiteSpace(match.Publisher) ||
                       string.Equals(match.Publisher, "ea", StringComparison.OrdinalIgnoreCase) ||
                       match.ManifestId.Contains(ReplayManagerConstants.RetailManifestSegment);

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

        var matchingProfile = FindMatchingProfile(profiles, replay.GameVersion, match.ManifestId, match.DataPatchManifestId);
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

        foreach (var companion in companionManifests)
        {
            if (!enabledContentIds.Contains(companion.Id.Value))
            {
                enabledContentIds.Add(companion.Id.Value);
            }
        }
    }

    private static async Task AcquireGeneralsOnlineMapPacksAsync(IContentOrchestrator contentOrchestrator, CancellationToken ct)
    {
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

        var dataPatchManifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(dataPatchManifestId), ct);
        if (dataPatchManifestResult != null && dataPatchManifestResult.Success && dataPatchManifestResult.Data != null)
        {
            return;
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

        if (isRetailClient)
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

            return (clientManifestId, gameClient);
        }

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

        var clientManifestResult = await manifestPool.GetManifestAsync(ManifestId.Create(matchedClient.ManifestId), ct);
        if (clientManifestResult != null && clientManifestResult.Success && clientManifestResult.Data != null)
        {
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
        if (searchResult != null && searchResult.Success && searchResult.Data != null)
        {
            var match = searchResult.Data.FirstOrDefault(c =>
                string.Equals(c.Id, matchedClient.ManifestId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Version, matchedClient.Version, StringComparison.OrdinalIgnoreCase))
                ?? searchResult.Data.FirstOrDefault();

            if (match != null)
            {
                var acquireResult = await contentOrchestrator.AcquireContentAsync(match, null, ct);
                if (acquireResult != null && !acquireResult.Success)
                {
                    logger.LogWarning("Failed to acquire client manifest {ManifestId}: {Error}", matchedClient.ManifestId, acquireResult.FirstError);
                }
            }
        }

        // If GeneralsOnline, also ensure MapPack is acquired
        if (string.Equals(matchedClient.Publisher, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            await AcquireGeneralsOnlineMapPacksAsync(contentOrchestrator, ct);
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
            replay.CompatibilityStatus = ReplayCompatibilityStatus.Orphaned;
        }
    }
}
