using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Notifications.Services;
using GenHub.Features.Notifications.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for managing game profile settings, including content selection and configuration.
/// </summary>
public partial class GameProfileSettingsViewModel : ViewModelBase, IRecipient<Core.Models.Content.ContentAcquiredMessage>
{
    /// <summary>
    /// Information about a content filter type.
    /// </summary>
    public record FilterTypeInfo(ContentType ContentType, string DisplayName, string IconData);

    /// <summary>
    /// Gets the list of available workspace strategies.
    /// </summary>
    public static WorkspaceStrategy[] AvailableWorkspaceStrategies { get; } =
    [
        WorkspaceStrategy.SymlinkOnly,
        WorkspaceStrategy.FullCopy,
        WorkspaceStrategy.HybridCopySymlink,
        WorkspaceStrategy.HardLink,
    ];

    /// <summary>
    /// Gets the list of available game types for local content.
    /// </summary>
    public static GameType[] AvailableLocalGameTypes { get; } =
    [
        Core.Models.Enums.GameType.Generals,
        Core.Models.Enums.GameType.ZeroHour,
    ];

    /// <summary>
    /// Gets the list of allowed content types for local identification.
    /// </summary>
    public static ContentType[] AllowedLocalContentTypes { get; } =
    [
        ContentType.Mod,
        ContentType.GameClient,
        ContentType.Executable,
        ContentType.ModdingTool,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.Mission,
    ];

    private static bool _hasShownFirstLoadNotification;

    private static string NormalizeResourcePath(string? path, string defaultUri)
    {
        if (string.IsNullOrWhiteSpace(path)) return defaultUri;
        if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)) return path;
        if (Uri.TryCreate(path, UriKind.Absolute, out _)) return path;

        // Add backward compatibility for old cover paths
        // Images were renamed/moved: Assets/Images/china-poster.png → Assets/Covers/china-cover.png
        var normalizedPath = path;
        if (normalizedPath.Contains("china-poster.png", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath.Replace("china-poster.png", "china-cover.png", StringComparison.OrdinalIgnoreCase)
                                           .Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase);
        }
        else if (normalizedPath.Contains("usa-poster.png", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath.Replace("usa-poster.png", "usa-cover.png", StringComparison.OrdinalIgnoreCase)
                                           .Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase);
        }
        else if (normalizedPath.Contains("gla-poster.png", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath.Replace("gla-poster.png", "gla-cover.png", StringComparison.OrdinalIgnoreCase)
                                           .Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase);
        }
        else if (normalizedPath.Contains("/Assets/Images/", StringComparison.OrdinalIgnoreCase) &&
                 (normalizedPath.Contains("cover", StringComparison.OrdinalIgnoreCase) ||
                  normalizedPath.Contains("poster", StringComparison.OrdinalIgnoreCase)))
        {
            // Handle any other cover/poster files in the old Images directory
            normalizedPath = normalizedPath.Replace("/Assets/Images/", "/Assets/Covers/", StringComparison.OrdinalIgnoreCase);
        }

        return $"avares://GenHub/{normalizedPath.TrimStart('/')}";
    }

    private static void PopulateGameSettings(CreateProfileRequest request, UpdateProfileRequest? gameSettings)
    {
        if (gameSettings != null) GameSettingsMapper.PopulateRequest(request, gameSettings);
    }

    private static void PopulateGameSettings(UpdateProfileRequest request, UpdateProfileRequest? gameSettings)
    {
        if (gameSettings != null) GameSettingsMapper.PopulateRequest(request, gameSettings);
    }

    private static ContentDisplayItem ConvertToViewModelContentDisplayItem(Core.Models.Content.ContentDisplayItem coreItem)
    {
        return new ContentDisplayItem
        {
            ManifestId = ManifestId.Create(coreItem.ManifestId),
            DisplayName = coreItem.DisplayName,
            ContentType = coreItem.ContentType,
            GameType = coreItem.GameType,
            InstallationType = coreItem.InstallationType,
            Publisher = coreItem.Publisher,
            Version = coreItem.Version,
            SourceId = coreItem.SourceId,
            GameClientId = coreItem.GameClientId,
            IsEnabled = coreItem.IsEnabled,
        };
    }

    private readonly IGameProfileManager? _gameProfileManager;
    private readonly IGameSettingsService? _gameSettingsService;
    private readonly IConfigurationProviderService? _configurationProvider;
    private readonly IProfileContentLoader? _profileContentLoader;
    private readonly Services.ProfileResourceService? _profileResourceService;
    private readonly INotificationService? _notificationService;
    private readonly IContentManifestPool? _manifestPool;
    private readonly IContentStorageService? _contentStorageService;
    private readonly ILocalContentService? _localContentService;
    private readonly ILogger<GameProfileSettingsViewModel>? _logger;
    private readonly ILogger<GameSettingsViewModel>? _gameSettingsLogger;

    private readonly NotificationService _localNotificationService = new(NullLogger<NotificationService>.Instance);

    private WorkspaceStrategy? _originalWorkspaceStrategy;
    private string? _currentProfileId;

    /// <summary>
    /// Event triggered when the view model requests to close.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the notification manager for local window notifications.
    /// </summary>
    public NotificationManagerViewModel NotificationManager { get; }

    /// <summary>
    /// Gets the Game Settings ViewModel for the settings sidebar.
    /// </summary>
    public GameSettingsViewModel GameSettingsViewModel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModel"/> class.
    /// </summary>
    /// <param name="gameProfileManager">The game profile manager.</param>
    /// <param name="gameSettingsService">The game settings service.</param>
    /// <param name="configurationProvider">The configuration provider.</param>
    /// <param name="profileContentLoader">The profile content loader.</param>
    /// <param name="profileResourceService">The profile resource service.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="manifestPool">The manifest pool.</param>
    /// <param name="contentStorageService">The content storage service.</param>
    /// <param name="localContentService">The local content service.</param>
    /// <param name="logger">The logger for this view model.</param>
    /// <param name="gameSettingsLogger">The logger for the game settings view model.</param>
    public GameProfileSettingsViewModel(
        IGameProfileManager? gameProfileManager,
        IGameSettingsService? gameSettingsService,
        IConfigurationProviderService? configurationProvider,
        IProfileContentLoader? profileContentLoader,
        Services.ProfileResourceService? profileResourceService,
        INotificationService? notificationService,
        IContentManifestPool? manifestPool,
        IContentStorageService? contentStorageService,
        ILocalContentService? localContentService,
        ILogger<GameProfileSettingsViewModel>? logger,
        ILogger<GameSettingsViewModel>? gameSettingsLogger)
    {
        _gameProfileManager = gameProfileManager;
        _gameSettingsService = gameSettingsService;
        _configurationProvider = configurationProvider;
        _profileContentLoader = profileContentLoader;
        _profileResourceService = profileResourceService;
        _notificationService = notificationService;
        _manifestPool = manifestPool;
        _contentStorageService = contentStorageService;
        _localContentService = localContentService;
        _logger = logger;
        _gameSettingsLogger = gameSettingsLogger;

        NotificationManager = new NotificationManagerViewModel(
            _localNotificationService,
            NullLogger<NotificationManagerViewModel>.Instance,
            NullLogger<NotificationItemViewModel>.Instance);

        GameSettingsViewModel = new GameSettingsViewModel(gameSettingsService!, gameSettingsLogger!);

        WeakReferenceMessenger.Default.Register(this);
    }

    /// <inheritdoc/>
    public void Receive(Core.Models.Content.ContentAcquiredMessage message) => _ = LoadAvailableContentAsync();

    /// <summary>
    /// Refreshes the visible filters and available content based on the current game type filter.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected internal async Task RefreshFiltersAndContentAsync()
    {
        await RefreshVisibleFiltersAsync();
        await LoadAvailableContentAsync();
    }

    /// <summary>
    /// Called when the game type filter changes.
    /// </summary>
    partial void OnGameTypeFilterChanged(GameType value)
    {
        _ = RefreshFiltersAndContentAsync();
    }

    /// <summary>
    /// Called when the selected game installation changes.
    /// </summary>
    partial void OnSelectedGameInstallationChanged(ContentDisplayItem? value)
    {
        if (value != null && value.GameType != GameTypeFilter)
        {
            GameTypeFilter = value.GameType;
            _logger?.LogInformation("Auto-synced GameTypeFilter to {GameType} based on SelectedGameInstallation", value.GameType);
        }
    }

    private async Task OnContentTypeChangedAsync() => await LoadAvailableContentAsync();

    private async Task EnableContentInternal(ContentDisplayItem? contentItem, bool bypassLoadingGuard = false)
    {
        if (contentItem == null) return;
        if (IsLoadingContent && !bypassLoadingGuard) return;

        if (contentItem.IsEnabled) return;

        var alreadyEnabled = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
        if (alreadyEnabled != null) return;

        if (contentItem.ContentType == ContentType.GameInstallation || contentItem.ContentType == ContentType.GameClient)
        {
            var existingItems = EnabledContent.Where(e => e.ContentType == contentItem.ContentType).ToList();
            foreach (var existing in existingItems)
            {
                if (existing.ContentType == ContentType.GameClient && Name == existing.DisplayName)
                {
                    Name = "New Profile";
                }

                existing.IsEnabled = false;
                EnabledContent.Remove(existing);

                if (existing.ContentType == SelectedContentType && existing.GameType == GameTypeFilter)
                {
                    var alreadyInAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == existing.ManifestId.Value);
                    if (alreadyInAvailable == null)
                    {
                        AvailableContent.Add(new ContentDisplayItem
                        {
                            ManifestId = existing.ManifestId,
                            DisplayName = existing.DisplayName,
                            ContentType = existing.ContentType,
                            GameType = existing.GameType,
                            InstallationType = existing.InstallationType,
                            Publisher = existing.Publisher,
                            IsEnabled = false,
                            SourceId = existing.SourceId,
                            GameClientId = existing.GameClientId,
                            Version = existing.Version,
                        });
                    }
                }
            }
        }

        contentItem.IsEnabled = true;
        EnabledContent.Add(contentItem);

        var itemToRemoveFromAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == contentItem.ManifestId.Value);
        if (itemToRemoveFromAvailable != null)
        {
            AvailableContent.Remove(itemToRemoveFromAvailable);
        }

        if (contentItem.ContentType == ContentType.GameInstallation)
        {
            SelectedGameInstallation = contentItem;
        }

        StatusMessage = $"Enabled {contentItem.DisplayName}";
        _logger?.LogInformation("Enabled content {ContentName} for profile", contentItem.DisplayName);

        _localNotificationService.ShowSuccess(
            "Content Enabled",
            $"Enabled '{contentItem.DisplayName}'");

        if (contentItem.ContentType == ContentType.GameClient && Name == "New Profile")
        {
            Name = contentItem.DisplayName;
        }

        await ResolveDependenciesAsync(contentItem);
    }

    private async Task ResolveDependenciesAsync(ContentDisplayItem contentItem)
    {
        try
        {
            if (_manifestPool == null) return;

            ContentManifest? manifest = null;
            var manifestResult = await _manifestPool.GetManifestAsync(contentItem.ManifestId.Value);

            if (manifestResult.Success && manifestResult.Data != null)
            {
                manifest = manifestResult.Data;
            }
            else if (contentItem.ContentType == ContentType.GameClient && !string.IsNullOrEmpty(contentItem.SourceId))
            {
                manifest = new ContentManifest
                {
                    Id = ManifestId.Create(contentItem.ManifestId.Value),
                    Name = contentItem.DisplayName,
                    ContentType = ContentType.GameClient,
                    TargetGame = contentItem.GameType,
                    Dependencies =
                    [
                        new ContentDependency
                        {
                            Id = ManifestId.Create(contentItem.SourceId),
                            DependencyType = ContentType.GameInstallation,
                            CompatibleGameTypes = [contentItem.GameType],
                            IsOptional = false,
                            InstallBehavior = DependencyInstallBehavior.RequireExisting,
                        }
                    ],
                };
            }
            else
            {
                return;
            }

            if (manifest.Dependencies == null || manifest.Dependencies.Count == 0)
            {
                _ = ValidateEnabledContentDependenciesAsync(contentItem.DisplayName);
                return;
            }

            foreach (var dependency in manifest.Dependencies)
            {
                if (dependency.DependencyType == ContentType.GameInstallation)
                {
                    bool isSatisfied = false;
                    if (dependency.CompatibleGameTypes != null && dependency.CompatibleGameTypes.Count > 0)
                    {
                        if (SelectedGameInstallation != null && SelectedGameInstallation.IsEnabled &&
                            dependency.CompatibleGameTypes.Contains(SelectedGameInstallation.GameType))
                        {
                            isSatisfied = true;
                        }
                    }

                    if (!isSatisfied && dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                    {
                        if (SelectedGameInstallation != null && SelectedGameInstallation.IsEnabled &&
                            SelectedGameInstallation.ManifestId.Value == dependency.Id.ToString())
                        {
                            isSatisfied = true;
                        }
                    }

                    if (!isSatisfied)
                    {
                        ContentDisplayItem? compatibleInstallation = null;
                        if (!string.IsNullOrEmpty(contentItem.SourceId))
                        {
                            compatibleInstallation = AvailableGameInstallations.FirstOrDefault(x => x.ManifestId.Value == contentItem.SourceId);
                        }

                        if (compatibleInstallation == null && dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                        {
                            compatibleInstallation = AvailableGameInstallations.FirstOrDefault(x => x.ManifestId.Value == dependency.Id.ToString());
                        }

                        if (compatibleInstallation == null && dependency.CompatibleGameTypes != null)
                        {
                            compatibleInstallation = AvailableGameInstallations
                                .FirstOrDefault(x => dependency.CompatibleGameTypes.Contains(x.GameType) &&
                                                     x.InstallationType == contentItem.InstallationType);
                            compatibleInstallation ??= AvailableGameInstallations.FirstOrDefault(x => dependency.CompatibleGameTypes.Contains(x.GameType));
                        }

                        if (compatibleInstallation != null)
                        {
                            _localNotificationService.ShowSuccess("Auto-Resolved", $"Switched Game Installation to '{compatibleInstallation.DisplayName}' as required by '{contentItem.DisplayName}'.");
                            await EnableContentInternal(compatibleInstallation);
                        }
                    }
                }
                else
                {
                    bool alreadyEnabled = false;
                    if (dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                    {
                        alreadyEnabled = EnabledContent.Any(x => x.ManifestId.Value == dependency.Id.ToString());
                    }
                    else
                    {
                        alreadyEnabled = EnabledContent.Any(x => x.ContentType == dependency.DependencyType);
                    }

                    if (!alreadyEnabled && !dependency.IsOptional)
                    {
                        var availableOfTargetType = await _profileContentLoader!.LoadAvailableContentAsync(
                            dependency.DependencyType,
                            new ObservableCollection<Core.Models.Content.ContentDisplayItem>(AvailableGameInstallations.Select(x => new Core.Models.Content.ContentDisplayItem
                            {
                                Id = x.ManifestId.Value,
                                ManifestId = x.ManifestId.Value,
                                DisplayName = x.DisplayName,
                                ContentType = x.ContentType,
                                GameType = x.GameType,
                            })),
                            EnabledContent.Select(x => x.ManifestId.Value));

                        Core.Models.Content.ContentDisplayItem? match = null;
                        if (dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                        {
                            match = availableOfTargetType.FirstOrDefault(x => x.ManifestId == dependency.Id.ToString());
                        }

                        if (match != null)
                        {
                            var viewModelItem = ConvertToViewModelContentDisplayItem(match);
                            if (!viewModelItem.IsEnabled)
                            {
                                _localNotificationService.ShowSuccess("Auto-Resolved", $"Automatically enabled required content: '{viewModelItem.DisplayName}'");
                                await EnableContent(viewModelItem);
                            }
                        }
                    }
                }
            }

            await ValidateEnabledContentDependenciesAsync(contentItem.DisplayName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error resolving dependencies for {ContentName}", contentItem.DisplayName);
            _ = ValidateEnabledContentDependenciesAsync(contentItem.DisplayName);
        }
    }

    private async Task ValidateEnabledContentDependenciesAsync(string justEnabledContentName)
    {
        try
        {
            if (_manifestPool == null) return;
            var enabledManifestIds = EnabledContent.Select(e => e.ManifestId.Value).ToList();
            if (enabledManifestIds.Count == 0) return;

            var manifests = new List<ContentManifest>();
            foreach (var manifestId in enabledManifestIds)
            {
                var manifestResult = await _manifestPool.GetManifestAsync(manifestId);
                if (manifestResult.Success && manifestResult.Data != null) manifests.Add(manifestResult.Data);
            }

            var warnings = new List<string>();
            var manifestsById = manifests.ToDictionary(m => m.Id.ToString(), m => m);
            var manifestsByType = manifests.GroupBy(m => m.ContentType).ToDictionary(g => g.Key, g => g.ToList());
            var enabledContentByType = EnabledContent.GroupBy(e => e.ContentType).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var manifest in manifests)
            {
                if (manifest.Dependencies == null) continue;
                foreach (var dependency in manifest.Dependencies)
                {
                    if (dependency.DependencyType == ContentType.GameInstallation || dependency.DependencyType == ContentType.GameClient)
                    {
                        if (!enabledContentByType.TryGetValue(dependency.DependencyType, out var enabledOfType) || enabledOfType.Count == 0)
                        {
                            warnings.Add(dependency.DependencyType == ContentType.GameInstallation
                                ? $"'{manifest.Name}' requires a Game Installation to be selected."
                                : $"'{manifest.Name}' requires a Game Client to be selected.");
                        }

                        continue;
                    }

                    if (!manifestsByType.TryGetValue(dependency.DependencyType, out var potentialMatches) || potentialMatches.Count == 0)
                    {
                        if (!dependency.IsOptional) warnings.Add($"'{manifest.Name}' requires {dependency.DependencyType} content, but none is enabled.");
                        continue;
                    }

                    if (dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                    {
                        bool found = manifestsById.ContainsKey(dependency.Id.ToString());
                        if (!found && !dependency.StrictPublisher)
                        {
                            var depIdSegments = dependency.Id.ToString().Split('.');
                            if (depIdSegments.Length >= 5)
                            {
                                var (depType, depName) = (depIdSegments[3], depIdSegments[4]);
                                found = potentialMatches.Any(m =>
                                {
                                    var segments = m.Id.ToString().Split('.');
                                    return segments.Length >= 5 && segments[3].Equals(depType, StringComparison.OrdinalIgnoreCase) && segments[4].Equals(depName, StringComparison.OrdinalIgnoreCase);
                                });
                            }
                        }

                        if (!found && !dependency.IsOptional) warnings.Add($"'{manifest.Name}' requires '{dependency.Name}' which is not enabled.");
                    }

                    foreach (var conflictId in dependency.ConflictsWith)
                    {
                        if (manifestsById.TryGetValue(conflictId.ToString(), out var conflicting))
                            warnings.Add($"'{manifest.Name}' conflicts with '{conflicting.Name}' - these cannot be used together.");
                    }
                }
            }

            if (warnings.Count > 0)
            {
                _localNotificationService.ShowWarning("Dependency Warning", $"After enabling '{justEnabledContentName}':\n• {string.Join("\n• ", warnings)}", 15000);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during dependency validation");
        }
    }

    private async Task<List<string>> ValidateAllDependenciesAsync(List<string> enabledContentIds)
    {
        var errors = new List<string>();
        try
        {
            if (_manifestPool == null) return errors;
            var manifests = new List<ContentManifest>();
            foreach (var id in enabledContentIds)
            {
                var res = await _manifestPool.GetManifestAsync(id);
                if (res.Success && res.Data != null) manifests.Add(res.Data);
            }

            var manifestsById = manifests.ToDictionary(m => m.Id.ToString(), m => m);
            var manifestsByType = manifests.GroupBy(m => m.ContentType).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var manifest in manifests)
            {
                if (manifest.Dependencies == null) continue;
                foreach (var dep in manifest.Dependencies)
                {
                    if (!manifestsByType.TryGetValue(dep.DependencyType, out var matches) || matches.Count == 0)
                    {
                        if (!dep.IsOptional) errors.Add(dep.DependencyType == ContentType.GameInstallation ? $"• '{manifest.Name}' requires a Game Installation" : dep.DependencyType == ContentType.GameClient ? $"• '{manifest.Name}' requires a Game Client" : $"• '{manifest.Name}' requires {dep.DependencyType} content");
                        continue;
                    }

                    if (dep.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
                    {
                        bool found = manifestsById.ContainsKey(dep.Id.ToString());
                        if (!found && !dep.StrictPublisher)
                        {
                            var segments = dep.Id.ToString().Split('.');
                            if (segments.Length >= 5)
                            {
                                var (type, name) = (segments[3], segments[4]);
                                found = matches.Any(m =>
                                {
                                    var ms = m.Id.ToString().Split('.');
                                    return ms.Length >= 5 && ms[3] == type && ms[4] == name;
                                });
                            }
                        }

                        if (!found && !dep.IsOptional)
                        {
                            var depRes = await _manifestPool.GetManifestAsync(dep.Id.ToString());
                            errors.Add($"• '{manifest.Name}' requires '{(depRes.Success && depRes.Data != null ? depRes.Data.Name : dep.Id.ToString())}'");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
             _logger?.LogError(ex, "Error during comprehensive dependency validation");
        }

        return errors;
    }

    private void LoadAvailableIconsAndCovers(string gameType)
    {
        try
        {
            if (_profileResourceService == null) return;

            var icons = _profileResourceService.GetIconsForGameType(gameType);
            AvailableIcons = new ObservableCollection<ProfileResourceItem>(icons);

            var covers = _profileResourceService.GetAvailableCovers();
            AvailableCoversForSelection = new ObservableCollection<ProfileResourceItem>(covers);

            if (!string.IsNullOrEmpty(IconPath))
            {
                SelectedIcon = AvailableIcons.FirstOrDefault(i => i.Path == IconPath);
            }

            if (!string.IsNullOrEmpty(CoverPath))
            {
                SelectedCoverItem = AvailableCoversForSelection.FirstOrDefault(c => c.Path == CoverPath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading available icons and covers");
        }
    }

    private async Task LoadEnabledContentForProfileAsync(GameProfile profile)
    {
        try
        {
            EnabledContent.Clear();
            var coreItems = await _profileContentLoader!.LoadEnabledContentForProfileAsync(profile);
            foreach (var coreItem in coreItems)
            {
                var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                EnabledContent.Add(viewModelItem);
                viewModelItem.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading enabled content for profile");
        }
    }

    private async Task LoadAvailableGameInstallationsAsync()
    {
        try
        {
            AvailableGameInstallations.Clear();
            var coreItems = await _profileContentLoader!.LoadAvailableGameInstallationsAsync();
            foreach (var coreItem in coreItems)
            {
                try
                {
                    AvailableGameInstallations.Add(ConvertToViewModelContentDisplayItem(coreItem));
                }
                catch (ArgumentException argEx)
                {
                    _logger?.LogWarning("Skipping invalid game installation {DisplayName}: {Message}", coreItem.DisplayName, argEx.Message);
                }
            }

            if (AvailableGameInstallations.Any() && SelectedGameInstallation == null)
            {
                SelectedGameInstallation = AvailableGameInstallations
                    .OrderByDescending(i => i.GameType == Core.Models.Enums.GameType.ZeroHour)
                    .First();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading available game installations");
        }
    }

    private WorkspaceStrategy GetDefaultWorkspaceStrategy() => _configurationProvider!.GetDefaultWorkspaceStrategy();
}
