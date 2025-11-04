using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

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
    private readonly IGameInstallationService _gameInstallationService = gameInstallationService ?? throw new ArgumentNullException(nameof(gameInstallationService));
    private readonly IContentManifestPool _contentManifestPool = contentManifestPool ?? throw new ArgumentNullException(nameof(contentManifestPool));
    private readonly IContentDisplayFormatter _displayFormatter = displayFormatter ?? throw new ArgumentNullException(nameof(displayFormatter));
    private readonly ILogger<ProfileContentLoader> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameInstallationsAsync()
    {
        var result = new ObservableCollection<ContentDisplayItem>();

        try
        {
            var installationsResult = await _gameInstallationService.GetAllInstallationsAsync();
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                _logger.LogWarning("Failed to load game installations: {Errors}", string.Join(", ", installationsResult.Errors));
                return result;
            }

            foreach (var installation in installationsResult.Data)
            {
                if (!installation.AvailableGameClients.Any())
                {
                    _logger.LogDebug("Skipping installation {InstallationId} - no available game clients", installation.Id);
                    continue;
                }

                // For each UNIQUE game type in the installation, create ONE entry
                // This prevents duplicates when multiple game clients exist for the same game type
                var uniqueGameTypes = installation.AvailableGameClients
                    .Select(gc => gc.GameType)
                    .Distinct()
                    .ToList();

                foreach (var gameType in uniqueGameTypes)
                {
                    // Get the FIRST (base/default) game client for this game type
                    // This should be the official client, not GeneralsOnline variants
                    var baseClient = installation.AvailableGameClients
                        .Where(gc => gc.GameType == gameType)
                        .OrderBy(gc =>
                        {
                            // Prioritize official clients over third-party
                            var name = gc.Name.ToLowerInvariant();
                            if (name.Contains("generalsonline")) return 2;
                            if (name.Contains("superhacker")) return 3;
                            return 1; // Official clients first
                        })
                        .ThenBy(gc => gc.Name) // Then alphabetically
                        .FirstOrDefault();

                    if (baseClient == null)
                        continue;

                    var manifestVersion = gameType == GameType.ZeroHour
                        ? ManifestConstants.ZeroHourManifestVersion
                        : ManifestConstants.GeneralsManifestVersion;

                    // Generate manifest ID for GameInstallation content
                    var installationManifestId = ManifestId.Create(
                        ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, manifestVersion));

                    // Use game client version if available and valid, otherwise use manifest version
                    var clientVersion = baseClient.Version;
                    string normalizedVersion;
                    if (string.IsNullOrEmpty(clientVersion) ||
                        clientVersion.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        // Fallback to manifest version for installations without official clients
                        normalizedVersion = _displayFormatter.NormalizeVersion(manifestVersion.ToString());
                    }
                    else
                    {
                        normalizedVersion = _displayFormatter.NormalizeVersion(clientVersion);
                    }

                    var publisherName = _displayFormatter.GetPublisherFromInstallationType(installation.InstallationType);
                    var displayName = _displayFormatter.BuildDisplayName(gameType, normalizedVersion);

                    var item = new ContentDisplayItem
                    {
                        ManifestId = installationManifestId.Value,
                        SourceId = installation.Id,
                        GameClientId = baseClient.Id,
                        DisplayName = displayName,
                        ContentType = ContentType.GameInstallation,
                        GameType = gameType,
                        InstallationType = installation.InstallationType,
                        Publisher = publisherName,
                        Version = normalizedVersion,
                        IsEnabled = false,
                    };
                    result.Add(item);

                    _logger.LogDebug(
                        "Added GameInstallation option: {DisplayName} (Publisher={Publisher}, InstallationType={InstallationType}, GameType={GameType}, Version={Version})",
                        item.DisplayName,
                        publisherName,
                        installation.InstallationType,
                        gameType,
                        normalizedVersion);
                }
            }

            _logger.LogInformation(
                "Loaded {Count} game installation options from {InstallationCount} detected installations",
                result.Count,
                installationsResult.Data.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading available game installations");
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameClientsAsync()
    {
        var result = new ObservableCollection<ContentDisplayItem>();

        try
        {
            var installationsResult = await _gameInstallationService.GetAllInstallationsAsync();
            if (!installationsResult.Success || installationsResult.Data == null)
            {
                _logger.LogWarning("Failed to load game installations for game clients: {Errors}", string.Join(", ", installationsResult.Errors));
                return result;
            }

            foreach (var installation in installationsResult.Data)
            {
                if (!installation.AvailableGameClients.Any())
                {
                    _logger.LogDebug("Skipping installation {InstallationId} - no available game clients", installation.Id);
                    continue;
                }

                // Create an entry for EACH game client (including all GeneralsOnline variants)
                foreach (var gameClient in installation.AvailableGameClients)
                {
                    var normalizedVersion = _displayFormatter.NormalizeVersion(gameClient.Version);
                    var publisherName = _displayFormatter.GetPublisherFromInstallationType(installation.InstallationType);

                    // Use the game client's name which includes variant info (e.g., "GeneralsOnline 30Hz")
                    var displayName = _displayFormatter.BuildDisplayName(gameClient.GameType, normalizedVersion, gameClient.Name);

                    var item = new ContentDisplayItem
                    {
                        ManifestId = gameClient.Id, // Use game client ID as manifest ID
                        SourceId = installation.Id,
                        GameClientId = gameClient.Id,
                        DisplayName = displayName,
                        ContentType = ContentType.GameClient,
                        GameType = gameClient.GameType,
                        InstallationType = installation.InstallationType,
                        Publisher = publisherName,
                        Version = normalizedVersion,
                        IsEnabled = false,
                    };
                    result.Add(item);

                    _logger.LogDebug(
                        "Added GameClient option: {DisplayName} (Publisher={Publisher}, ExecutablePath={ExecutablePath})",
                        item.DisplayName,
                        publisherName,
                        gameClient.ExecutablePath);
                }
            }

            _logger.LogInformation(
                "Loaded {Count} game client options from {InstallationCount} detected installations",
                result.Count,
                installationsResult.Data.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading available game clients");
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadAvailableContentAsync(
        ContentType contentType,
        ObservableCollection<ContentDisplayItem> availableGameInstallations,
        IEnumerable<string> enabledContentIds)
    {
        var result = new ObservableCollection<ContentDisplayItem>();
        var enabledSet = new HashSet<string>(enabledContentIds);

        try
        {
            // For GameInstallation type, return clones of availableGameInstallations
            if (contentType == ContentType.GameInstallation)
            {
                foreach (var installationItem in availableGameInstallations)
                {
                    var item = new ContentDisplayItem
                    {
                        ManifestId = installationItem.ManifestId,
                        DisplayName = installationItem.DisplayName,
                        ContentType = installationItem.ContentType,
                        GameType = installationItem.GameType,
                        InstallationType = installationItem.InstallationType,
                        Publisher = installationItem.Publisher,
                        SourceId = installationItem.SourceId,
                        GameClientId = installationItem.GameClientId,
                        Version = installationItem.Version,
                        IsEnabled = enabledSet.Contains(installationItem.ManifestId),
                    };
                    result.Add(item);
                }

                _logger.LogInformation("Loaded {Count} content items for content type {ContentType}", result.Count, contentType);
                return result;
            }

            // For GameClient type, use the dedicated method that loads ALL game clients
            if (contentType == ContentType.GameClient)
            {
                var gameClients = await LoadAvailableGameClientsAsync();
                foreach (var client in gameClients)
                {
                    client.IsEnabled = enabledSet.Contains(client.ManifestId);
                    result.Add(client);
                }

                _logger.LogInformation("Loaded {Count} game client items", result.Count);
                return result;
            }

            // For other content types, load from manifests
            var manifestsResult = await _contentManifestPool.GetAllManifestsAsync();
            if (!manifestsResult.Success || manifestsResult.Data == null)
            {
                _logger.LogWarning("Failed to load manifests: {Errors}", string.Join(", ", manifestsResult.Errors));
                return result;
            }

            // Filter by content type
            var filteredManifests = manifestsResult.Data.Where(m => m.ContentType == contentType);

            // Load installations for GameClient type
            GameInstallation[]? installations = null;
            if (contentType == ContentType.GameClient)
            {
                var installationsResult = await _gameInstallationService.GetAllInstallationsAsync();
                if (installationsResult.Success && installationsResult.Data != null)
                {
                    installations = installationsResult.Data.ToArray();
                }
            }

            foreach (var manifest in filteredManifests)
            {
                var publisher = _displayFormatter.GetPublisherFromManifest(manifest);
                string displayName;
                GameInstallationType installationType = _displayFormatter.GetInstallationTypeFromManifest(manifest);
                GameType gameType = manifest.TargetGame;

                // Special handling for GameClient manifests
                if (manifest.ContentType == ContentType.GameClient && installations != null)
                {
                    string? gameClientName = null;
                    string? gameClientVersion = null;
                    GameInstallationType? actualInstallationType = null;
                    string? sourceInstallationId = null;
                    string? gameClientId = null;

                    // Search all installations for this GameClient
                    foreach (var installation in installations)
                    {
                        var matchingClient = installation.AvailableGameClients?.FirstOrDefault(gc => gc.Id == manifest.Id.Value);
                        if (matchingClient != null)
                        {
                            gameClientName = matchingClient.Name;
                            gameClientVersion = matchingClient.Version;
                            gameType = matchingClient.GameType;
                            actualInstallationType = installation.InstallationType;
                            sourceInstallationId = installation.Id;
                            gameClientId = matchingClient.Id;
                            break;
                        }
                    }

                    if (gameClientName != null && gameClientVersion != null && actualInstallationType != null)
                    {
                        var clientNormalizedVersion = _displayFormatter.NormalizeVersion(gameClientVersion);
                        var publisherName = _displayFormatter.GetPublisherFromInstallationType(actualInstallationType.Value);
                        displayName = _displayFormatter.BuildDisplayName(gameType, clientNormalizedVersion, gameClientName);
                        publisher = publisherName;
                        installationType = actualInstallationType.Value;

                        var gameClientItem = new ContentDisplayItem
                        {
                            ManifestId = manifest.Id.Value,
                            DisplayName = displayName,
                            Version = clientNormalizedVersion,
                            ContentType = manifest.ContentType,
                            GameType = gameType,
                            InstallationType = installationType,
                            Publisher = publisher,
                            IsEnabled = enabledSet.Contains(manifest.Id.Value),
                            SourceId = sourceInstallationId!,
                            GameClientId = gameClientId!,
                        };
                        result.Add(gameClientItem);
                        continue;
                    }
                    else
                    {
                        var normalizedManifestVersion = _displayFormatter.NormalizeVersion(manifest.Version);
                        displayName = _displayFormatter.BuildDisplayName(gameType, normalizedManifestVersion, manifest.Name);
                    }
                }
                else
                {
                    var normalizedManifestVersion = _displayFormatter.NormalizeVersion(manifest.Version);
                    displayName = _displayFormatter.BuildDisplayName(gameType, normalizedManifestVersion, manifest.Name);
                }

                // Generic item creation for other content types or fallback cases
                var normalizedVersion = _displayFormatter.NormalizeVersion(manifest.Version);
                var item = new ContentDisplayItem
                {
                    ManifestId = manifest.Id.Value,
                    DisplayName = displayName,
                    Version = normalizedVersion,
                    ContentType = manifest.ContentType,
                    GameType = manifest.TargetGame,
                    InstallationType = installationType,
                    Publisher = publisher,
                    IsEnabled = enabledSet.Contains(manifest.Id.Value),
                };
                result.Add(item);
            }

            _logger.LogInformation("Loaded {Count} content items for content type {ContentType}", result.Count, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading available content");
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ObservableCollection<ContentDisplayItem>> LoadEnabledContentForProfileAsync(GameProfile profile)
    {
        var result = new ObservableCollection<ContentDisplayItem>();

        try
        {
            _logger.LogInformation("LoadEnabledContentForProfileAsync called for profile: {ProfileName} (ID: {ProfileId})", profile.Name, profile.Id);

            if (profile.EnabledContentIds == null || !profile.EnabledContentIds.Any())
            {
                _logger.LogWarning("Profile {ProfileName} has no enabled content IDs", profile.Name);
                return result;
            }

            _logger.LogInformation(
                "Profile has {Count} enabled content IDs: {ContentIds}",
                profile.EnabledContentIds.Count,
                string.Join(", ", profile.EnabledContentIds));

            // Load the actual game installation for accurate information
            GameInstallation? gameInstallation = null;
            if (!string.IsNullOrEmpty(profile.GameInstallationId))
            {
                _logger.LogDebug("Loading game installation: {InstallationId}", profile.GameInstallationId);
                var installationResult = await _gameInstallationService.GetInstallationAsync(profile.GameInstallationId);
                if (installationResult.Success && installationResult.Data != null)
                {
                    gameInstallation = installationResult.Data;
                    _logger.LogDebug("Successfully loaded game installation: {InstallationPath}", gameInstallation.InstallationPath);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to load game installation {InstallationId}: {Errors}",
                        profile.GameInstallationId,
                        string.Join(", ", installationResult.Errors));
                }
            }

            foreach (var contentId in profile.EnabledContentIds)
            {
                _logger.LogDebug("Processing enabled content ID: {ContentId}", contentId);

                ManifestId manifestId;
                try
                {
                    manifestId = ManifestId.Create(contentId);
                    _logger.LogDebug("Successfully created ManifestId from: {ContentId}", contentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create ManifestId from content ID: {ContentId}", contentId);
                    continue;
                }

                var manifestResult = await _contentManifestPool.GetManifestAsync(manifestId);
                _logger.LogDebug(
                    "Manifest retrieval result for {ContentId}: Success={Success}, HasData={HasData}",
                    contentId,
                    manifestResult.Success,
                    manifestResult.Data != null);

                if (!manifestResult.Success)
                {
                    _logger.LogWarning(
                        "Failed to retrieve manifest for {ContentId}: {Errors}",
                        contentId,
                        string.Join(", ", manifestResult.Errors));
                    continue;
                }

                if (manifestResult.Data == null)
                {
                    _logger.LogWarning("Manifest for {ContentId} not found in pool (returned null)", contentId);
                    continue;
                }

                var manifest = manifestResult.Data;
                var publisher = _displayFormatter.GetPublisherFromManifest(manifest);
                string displayName;
                GameInstallationType installationType = _displayFormatter.GetInstallationTypeFromManifest(manifest);
                GameType gameType = manifest.TargetGame;
                string? sourceId = null;
                string? gameClientId = null;

                if (manifest.ContentType == ContentType.GameClient)
                    {
                        GameInstallation? clientInstallation = null;
                        GameClient? matchingClient = null;

                        // First, try the profile's primary installation if available
                        if (gameInstallation != null)
                        {
                            matchingClient = gameInstallation.AvailableGameClients?.FirstOrDefault(gc => gc.Id == manifest.Id.Value);
                            if (matchingClient != null)
                            {
                                clientInstallation = gameInstallation;
                            }
                        }

                        // If not found in primary installation, search ALL installations
                        if (matchingClient == null)
                        {
                            _logger.LogDebug(
                                "GameClient {ManifestId} not found in primary installation, searching all installations",
                                manifest.Id.Value);

                            var allInstallationsResult = await _gameInstallationService.GetAllInstallationsAsync();
                            if (allInstallationsResult.Success && allInstallationsResult.Data != null)
                            {
                                foreach (var installation in allInstallationsResult.Data)
                                {
                                    matchingClient = installation.AvailableGameClients?.FirstOrDefault(gc => gc.Id == manifest.Id.Value);
                                    if (matchingClient != null)
                                    {
                                        clientInstallation = installation;
                                        _logger.LogInformation(
                                            "Found GameClient {ManifestId} in installation {InstallationId} ({InstallationType})",
                                            manifest.Id.Value,
                                            installation.Id,
                                            installation.InstallationType);
                                        break;
                                    }
                                }
                            }
                        }

                        if (matchingClient != null && clientInstallation != null)
                        {
                            var normalizedVersion = _displayFormatter.NormalizeVersion(matchingClient.Version);
                            var publisherName = _displayFormatter.GetPublisherFromInstallationType(clientInstallation.InstallationType);

                            // Build display name from GameClient properties (e.g., "Steam Zero Hour 1.04")
                            displayName = _displayFormatter.BuildDisplayName(matchingClient.GameType, normalizedVersion, matchingClient.Name);
                            gameType = matchingClient.GameType;
                            installationType = clientInstallation.InstallationType;
                            publisher = publisherName;
                            sourceId = clientInstallation.Id;
                            gameClientId = matchingClient.Id;

                            var gameClientItem = new ContentDisplayItem
                            {
                                ManifestId = manifest.Id.Value,
                                DisplayName = displayName,
                                ContentType = manifest.ContentType,
                                GameType = gameType,
                                InstallationType = installationType,
                                Publisher = publisher,
                                Version = normalizedVersion,
                                IsEnabled = true,
                                SourceId = sourceId,
                                GameClientId = gameClientId,
                            };
                            result.Add(gameClientItem);
                            _logger.LogDebug(
                                "Successfully loaded GameClient {DisplayName} from installation {InstallationId}",
                                displayName,
                                clientInstallation.Id);
                            continue; // Skip the generic item creation below
                        }
                        else
                        {
                            // Fallback: GameClient not found in ANY installation - log warning and skip
                            _logger.LogWarning(
                                "GameClient manifest {ManifestId} not found in any installation - skipping",
                                manifest.Id.Value);
                            continue;
                        }
                    }

                // Handle GameInstallation content type
                if (manifest.ContentType == ContentType.GameInstallation && gameInstallation != null)
                {
                    var gameClient = gameInstallation.AvailableGameClients?.FirstOrDefault(gc => gc.Id == profile.GameClient?.Id);
                    if (gameClient != null)
                    {
                        var normalizedVersion = _displayFormatter.NormalizeVersion(gameClient.Version);
                        var publisherName = _displayFormatter.GetPublisherFromInstallationType(gameInstallation.InstallationType);
                        displayName = _displayFormatter.BuildDisplayName(gameClient.GameType, normalizedVersion);
                        installationType = gameInstallation.InstallationType;
                        publisher = publisherName;
                        sourceId = gameInstallation.Id;
                        gameClientId = gameClient.Id;

                        // Create the item directly here with the normalized version to prevent "Automatically added" from showing
                        var installationItem = new ContentDisplayItem
                        {
                            ManifestId = manifest.Id.Value,
                            DisplayName = displayName,
                            ContentType = manifest.ContentType,
                            GameType = gameClient.GameType,
                            InstallationType = installationType,
                            Publisher = publisher,
                            Version = normalizedVersion,
                            IsEnabled = true,
                            SourceId = sourceId,
                            GameClientId = gameClientId,
                        };
                        result.Add(installationItem);
                        continue; // Skip the generic item creation below
                    }
                    else
                    {
                        var normalizedVersion = _displayFormatter.NormalizeVersion(manifest.Version);
                        displayName = _displayFormatter.BuildDisplayName(manifest.TargetGame, normalizedVersion);
                    }
                }
                else if (manifest.ContentType == ContentType.GameInstallation)
                {
                    // Fallback when gameInstallation is null
                    var normalizedVersion = _displayFormatter.NormalizeVersion(manifest.Version);
                    displayName = _displayFormatter.BuildDisplayName(manifest.TargetGame, normalizedVersion);
                }
                else
                {
                    // Handle other content types (Mods, MapPacks, Patches, etc.)
                    var normalizedVersion = _displayFormatter.NormalizeVersion(manifest.Version);
                    displayName = manifest.ContentType switch
                    {
                        ContentType.GameInstallation => _displayFormatter.BuildDisplayName(manifest.TargetGame, normalizedVersion),
                        _ => _displayFormatter.BuildDisplayName(manifest.TargetGame, normalizedVersion, manifest.Name)
                    };
                }

                // Create generic content item for non-GameClient types
                var item = new ContentDisplayItem
                {
                    ManifestId = manifest.Id.Value,
                    DisplayName = displayName,
                    ContentType = manifest.ContentType,
                    GameType = gameType,
                    InstallationType = installationType,
                    Publisher = publisher,
                    Version = _displayFormatter.NormalizeVersion(manifest.Version),
                    IsEnabled = true,
                    SourceId = sourceId ?? string.Empty,
                    GameClientId = gameClientId ?? string.Empty,
                };
                result.Add(item);
            }

            _logger.LogInformation("Loaded {Count} enabled content items for profile {ProfileName}", result.Count, profile.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading enabled content for profile");
        }

        return result;
    }
}
