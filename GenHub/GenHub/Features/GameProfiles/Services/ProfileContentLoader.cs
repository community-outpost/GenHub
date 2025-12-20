using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for loading profile-related content including game installations,
/// available content, and enabled content for profiles.
/// </summary>
public class ProfileContentLoader(
    IGameInstallationService gameInstallationService,
    IContentManifestPool contentManifestPool,
    IContentDisplayFormatter displayFormatter,
    ILogger<ProfileContentLoader> logger) : IProfileContentLoader
{
    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameInstallationsAsync()
    {
        var result = new ObservableCollection<ContentDisplayItem>();

        try
        {
            var installationsResult = await gameInstallationService.GetAllInstallationsAsync();
            if (!installationsResult.Success || installationsResult.Data is null)
            {
                logger.LogWarning(
                    "Failed to load game installations: {Errors}",
                    string.Join(", ", installationsResult.Errors));
                return result;
            }

            foreach (var installation in installationsResult.Data)
            {
                if (installation.AvailableGameClients.Count == 0)
                {
                    logger.LogDebug(
                        "Skipping installation {InstallationId} - no available game clients",
                        installation.Id);
                    continue;
                }

                var uniqueGameTypes = installation.AvailableGameClients
                    .Select(gc => gc.GameType)
                    .Distinct();

                foreach (var gameType in uniqueGameTypes)
                {
                    var baseClient = GetBaseGameClient(installation, gameType);
                    if (baseClient is null) continue;

                    var item = CreateInstallationDisplayItem(installation, baseClient, gameType);
                    result.Add(item);

                    logger.LogDebug(
                        "Added GameInstallation: {DisplayName} ({Publisher}, {GameType}, {Version})",
                        item.DisplayName,
                        item.Publisher,
                        gameType,
                        item.Version);
                }
            }

            logger.LogInformation(
                "Loaded {Count} game installation options from {InstallationCount} installations",
                result.Count,
                installationsResult.Data.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading available game installations");
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameClientsAsync()
    {
        var result = new ObservableCollection<ContentDisplayItem>();

        try
        {
            var installationsResult = await gameInstallationService.GetAllInstallationsAsync();
            if (!installationsResult.Success || installationsResult.Data is null)
            {
                logger.LogWarning(
                    "Failed to load game installations for game clients: {Errors}",
                    string.Join(", ", installationsResult.Errors));
                return result;
            }

            var includedManifestIds = new HashSet<string>();

            foreach (var installation in installationsResult.Data)
            {
                foreach (var gameClient in installation.AvailableGameClients)
                {
                    var item = CreateGameClientDisplayItem(installation, gameClient);
                    result.Add(item);
                    includedManifestIds.Add(gameClient.Id);

                    logger.LogDebug(
                        "Added GameClient: {DisplayName} ({Publisher})",
                        item.DisplayName,
                        item.Publisher);
                }
            }

            await AddCasStoredGameClientsAsync(result, includedManifestIds);

            logger.LogInformation("Loaded {Count} game client options", result.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading available game clients");
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadAvailableContentAsync(
        ContentType contentType,
        ObservableCollection<ContentDisplayItem> availableGameInstallations,
        IEnumerable<string> enabledContentIds)
    {
        var enabledSet = new HashSet<string>(enabledContentIds);

        try
        {
            return contentType switch
            {
                ContentType.GameInstallation =>
                    CloneWithEnabledState(availableGameInstallations, enabledSet),
                ContentType.GameClient =>
                    await LoadGameClientsWithEnabledStateAsync(enabledSet),
                _ =>
                    await LoadManifestContentAsync(contentType, enabledSet),
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading available content for type {ContentType}", contentType);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadEnabledContentForProfileAsync(
        GameProfile profile)
    {
        var result = new ObservableCollection<ContentDisplayItem>();

        try
        {
            logger.LogInformation(
                "Loading enabled content for profile: {ProfileName} (ID: {ProfileId})",
                profile.Name,
                profile.Id);

            if (profile.EnabledContentIds is null || profile.EnabledContentIds.Count == 0)
            {
                logger.LogWarning("Profile {ProfileName} has no enabled content IDs", profile.Name);
                return result;
            }

            logger.LogInformation(
                "Profile has {Count} enabled content IDs: {ContentIds}",
                profile.EnabledContentIds.Count,
                string.Join(", ", profile.EnabledContentIds));

            var gameInstallation = await LoadProfileInstallationAsync(profile);

            foreach (var contentId in profile.EnabledContentIds)
            {
                var item = await LoadEnabledContentItemAsync(contentId, profile, gameInstallation);
                if (item is not null)
                {
                    result.Add(item);
                }
            }

            logger.LogInformation(
                "Loaded {Count} enabled content items for profile {ProfileName}",
                result.Count,
                profile.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading enabled content for profile");
        }

        return result;
    }

    private static GameClient? GetBaseGameClient(GameInstallation installation, GameType gameType)
    {
        return installation.AvailableGameClients
            .Where(gc => gc.GameType == gameType)
            .OrderBy(gc => GetClientPriority(gc.Name))
            .ThenBy(gc => gc.Name)
            .FirstOrDefault();
    }

    private static int GetClientPriority(string clientName)
    {
        var name = clientName.ToLowerInvariant();
        return name switch
        {
            _ when name.Contains("generalsonline") => 2,
            _ when name.Contains("superhacker") => 3,
            _ => 1,
        };
    }

    private static ObservableCollection<ContentDisplayItem> CloneWithEnabledState(
        ObservableCollection<ContentDisplayItem> items,
        HashSet<string> enabledIds)
    {
        return new ObservableCollection<ContentDisplayItem>(
            items.Select(item => new ContentDisplayItem
            {
                Id = item.Id,
                ManifestId = item.ManifestId,
                DisplayName = item.DisplayName,
                Description = item.Description,
                Version = item.Version,
                ContentType = item.ContentType,
                GameType = item.GameType,
                InstallationType = item.InstallationType,
                Publisher = item.Publisher,
                SourceId = item.SourceId,
                GameClientId = item.GameClientId,
                IsEnabled = enabledIds.Contains(item.ManifestId),
            }));
    }

    private (string ForManifestId, string ForDisplay) GetVersionStrings(string? detectedVersion)
    {
        var isUnknown = string.IsNullOrEmpty(detectedVersion) ||
            detectedVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            detectedVersion.Equals(
                GameClientConstants.AutoDetectedVersion,
                StringComparison.OrdinalIgnoreCase);

        if (isUnknown)
        {
            var defaultVersion = ManifestConstants.DefaultManifestFormatVersion.ToString();
            return ("0", displayFormatter.NormalizeVersion(defaultVersion));
        }

        return (detectedVersion!, displayFormatter.NormalizeVersion(detectedVersion!));
    }

    private ContentDisplayItem CreateInstallationDisplayItem(
        GameInstallation installation,
        GameClient baseClient,
        GameType gameType)
    {
        var (versionForManifestId, versionForDisplay) = GetVersionStrings(baseClient.Version);
        var manifestId = ManifestIdGenerator.GenerateGameInstallationId(
            installation,
            gameType,
            versionForManifestId);
        var publisher = displayFormatter.GetPublisherFromInstallationType(installation.InstallationType);

        return new ContentDisplayItem
        {
            Id = manifestId,
            ManifestId = manifestId,
            SourceId = installation.Id,
            GameClientId = baseClient.Id,
            DisplayName = displayFormatter.BuildDisplayName(gameType, versionForDisplay),
            Description = $"{publisher} - {installation.InstallationType} - {gameType}",
            Version = versionForDisplay,
            ContentType = ContentType.GameInstallation,
            GameType = gameType,
            InstallationType = installation.InstallationType,
            Publisher = publisher,
        };
    }

    private ContentDisplayItem CreateGameClientDisplayItem(
        GameInstallation installation,
        GameClient gameClient,
        bool isEnabled = false)
    {
        var normalizedVersion = displayFormatter.NormalizeVersion(gameClient.Version);
        var publisher = displayFormatter.GetPublisherFromInstallationType(installation.InstallationType);

        return new ContentDisplayItem
        {
            Id = gameClient.Id,
            ManifestId = gameClient.Id,
            SourceId = installation.Id,
            GameClientId = gameClient.Id,
            DisplayName = displayFormatter.BuildDisplayName(
                gameClient.GameType,
                normalizedVersion,
                gameClient.Name),
            ContentType = ContentType.GameClient,
            GameType = gameClient.GameType,
            InstallationType = installation.InstallationType,
            Publisher = publisher,
            Version = normalizedVersion,
            IsEnabled = isEnabled,
        };
    }

    private ContentDisplayItem CreateManifestDisplayItem(
        ContentManifest manifest,
        string? sourceId = null,
        string? gameClientId = null,
        bool isEnabled = false)
    {
        var normalizedVersion = displayFormatter.NormalizeVersion(manifest.Version);
        var displayName = manifest.ContentType == ContentType.GameInstallation
            ? displayFormatter.BuildDisplayName(manifest.TargetGame, normalizedVersion)
            : displayFormatter.BuildDisplayName(manifest.TargetGame, normalizedVersion, manifest.Name);

        return new ContentDisplayItem
        {
            Id = manifest.Id.Value,
            ManifestId = manifest.Id.Value,
            DisplayName = displayName,
            Version = normalizedVersion,
            ContentType = manifest.ContentType,
            GameType = manifest.TargetGame,
            InstallationType = displayFormatter.GetInstallationTypeFromManifest(manifest),
            Publisher = displayFormatter.GetPublisherFromManifest(manifest),
            SourceId = sourceId ?? string.Empty,
            GameClientId = gameClientId ?? string.Empty,
            IsEnabled = isEnabled,
        };
    }

    private async Task AddCasStoredGameClientsAsync(
        ObservableCollection<ContentDisplayItem> result,
        HashSet<string> excludeIds)
    {
        var manifestsResult = await contentManifestPool.GetAllManifestsAsync();
        if (!manifestsResult.Success || manifestsResult.Data is null) return;

        var casGameClients = manifestsResult.Data
            .Where(m => m.ContentType == ContentType.GameClient && !excludeIds.Contains(m.Id.Value));

        foreach (var manifest in casGameClients)
        {
            result.Add(CreateManifestDisplayItem(manifest));

            logger.LogDebug(
                "Added CAS-stored GameClient: {DisplayName} ({ManifestId})",
                manifest.Name,
                manifest.Id.Value);
        }
    }

    private async Task<ObservableCollection<ContentDisplayItem>> LoadGameClientsWithEnabledStateAsync(
        HashSet<string> enabledIds)
    {
        var gameClients = await LoadAvailableGameClientsAsync();

        foreach (var client in gameClients)
        {
            client.IsEnabled = enabledIds.Contains(client.ManifestId);
        }

        logger.LogInformation("Loaded {Count} game client items", gameClients.Count);
        return gameClients;
    }

    private async Task<ObservableCollection<ContentDisplayItem>> LoadManifestContentAsync(
        ContentType contentType,
        HashSet<string> enabledIds)
    {
        var manifestsResult = await contentManifestPool.GetAllManifestsAsync();
        if (!manifestsResult.Success || manifestsResult.Data is null)
        {
            logger.LogWarning(
                "Failed to load manifests: {Errors}",
                string.Join(", ", manifestsResult.Errors));
            return [];
        }

        var items = manifestsResult.Data
            .Where(m => m.ContentType == contentType)
            .Select(m => CreateManifestDisplayItem(m, isEnabled: enabledIds.Contains(m.Id.Value)));

        logger.LogInformation("Loaded {Count} content items for type {ContentType}", items.Count(), contentType);
        return new ObservableCollection<ContentDisplayItem>(items);
    }

    private async Task<GameInstallation?> LoadProfileInstallationAsync(GameProfile profile)
    {
        if (string.IsNullOrEmpty(profile.GameInstallationId)) return null;

        logger.LogDebug("Loading game installation: {InstallationId}", profile.GameInstallationId);

        var result = await gameInstallationService.GetInstallationAsync(profile.GameInstallationId);

        if (result.Success && result.Data is not null)
        {
            logger.LogDebug("Loaded game installation: {Path}", result.Data.InstallationPath);
            return result.Data;
        }

        logger.LogWarning(
            "Failed to load installation {Id}: {Errors}",
            profile.GameInstallationId,
            string.Join(", ", result.Errors));
        return null;
    }

    private async Task<ContentDisplayItem?> LoadEnabledContentItemAsync(
        string contentId,
        GameProfile profile,
        GameInstallation? gameInstallation)
    {
        logger.LogDebug("Processing enabled content ID: {ContentId}", contentId);

        if (!ManifestId.TryCreate(contentId, out var manifestId))
        {
            logger.LogError("Invalid ManifestId format: {ContentId}", contentId);
            return null;
        }

        var manifestResult = await contentManifestPool.GetManifestAsync(manifestId);

        if (!manifestResult.Success || manifestResult.Data is null)
        {
            logger.LogWarning(
                "Manifest not found for {ContentId}: {Errors}",
                contentId,
                string.Join(", ", manifestResult.Errors));
            return null;
        }

        var manifest = manifestResult.Data;

        return manifest.ContentType switch
        {
            ContentType.GameClient =>
                await CreateEnabledGameClientItemAsync(manifest, gameInstallation),
            ContentType.GameInstallation =>
                CreateEnabledInstallationItem(manifest, profile, gameInstallation),
            _ =>
                CreateManifestDisplayItem(manifest, isEnabled: true),
        };
    }

    private async Task<ContentDisplayItem> CreateEnabledGameClientItemAsync(
        ContentManifest manifest,
        GameInstallation? primaryInstallation)
    {
        var (installation, client) = await FindGameClientInInstallationsAsync(
            manifest.Id.Value,
            primaryInstallation);

        if (installation is not null && client is not null)
        {
            logger.LogDebug(
                "Loaded GameClient {DisplayName} from installation {Id}",
                client.Name,
                installation.Id);

            return CreateGameClientDisplayItem(installation, client, isEnabled: true);
        }

        logger.LogInformation(
            "GameClient {ManifestId} not found in installations - using manifest",
            manifest.Id.Value);

        return CreateManifestDisplayItem(
            manifest,
            sourceId: string.Empty,
            gameClientId: manifest.Id.Value,
            isEnabled: true);
    }

    private async Task<(GameInstallation? Installation, GameClient? Client)> FindGameClientInInstallationsAsync(
        string manifestId,
        GameInstallation? primaryInstallation)
    {
        // Check primary installation first
        var client = primaryInstallation?.AvailableGameClients?
            .FirstOrDefault(gc => gc.Id == manifestId);
        if (client is not null) return (primaryInstallation, client);

        // Search all installations
        var allResult = await gameInstallationService.GetAllInstallationsAsync();
        if (!allResult.Success || allResult.Data is null) return (null, null);

        foreach (var installation in allResult.Data)
        {
            client = installation.AvailableGameClients?.FirstOrDefault(gc => gc.Id == manifestId);
            if (client is not null)
            {
                logger.LogInformation(
                    "Found GameClient {Id} in installation {InstallationId}",
                    manifestId,
                    installation.Id);
                return (installation, client);
            }
        }

        return (null, null);
    }

    private ContentDisplayItem CreateEnabledInstallationItem(
        ContentManifest manifest,
        GameProfile profile,
        GameInstallation? gameInstallation)
    {
        var gameClient = gameInstallation?.AvailableGameClients?
            .FirstOrDefault(gc => gc.Id == profile.GameClient?.Id);

        if (gameInstallation is not null && gameClient is not null)
        {
            var normalizedVersion = displayFormatter.NormalizeVersion(gameClient.Version);
            var publisher = displayFormatter.GetPublisherFromInstallationType(
                gameInstallation.InstallationType);

            return new ContentDisplayItem
            {
                Id = manifest.Id.Value,
                ManifestId = manifest.Id.Value,
                DisplayName = displayFormatter.BuildDisplayName(gameClient.GameType, normalizedVersion),
                Version = normalizedVersion,
                ContentType = ContentType.GameInstallation,
                GameType = gameClient.GameType,
                InstallationType = gameInstallation.InstallationType,
                Publisher = publisher,
                SourceId = gameInstallation.Id,
                GameClientId = gameClient.Id,
                IsEnabled = true,
            };
        }

        return CreateManifestDisplayItem(manifest, isEnabled: true);
    }
}
