using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
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
    /// Gets the available game types for local content.
    /// </summary>
    public static GameType[] AvailableLocalGameTypes { get; } = [Core.Models.Enums.GameType.Generals, Core.Models.Enums.GameType.ZeroHour];

    /// <summary>
    /// Gets the allowed content types for local content.
    /// </summary>
    public static ContentType[] AllowedLocalContentTypes { get; } =
    [
        ContentType.Mod, ContentType.MapPack, ContentType.Addon, ContentType.Patch,
        ContentType.ModdingTool, ContentType.Executable, ContentType.GameClient
    ];

    /// <summary>
    /// Gets the available content types for filtering.
    /// </summary>
    public static ContentType[] AvailableContentTypes { get; } =
    [
        ContentType.GameClient, ContentType.Mod, ContentType.MapPack, ContentType.Addon,
        ContentType.Patch, ContentType.ModdingTool, ContentType.Executable
    ];

    /// <summary>
    /// Gets the available workspace strategies.
    /// </summary>
    public static WorkspaceStrategy[] AvailableWorkspaceStrategies { get; } = [WorkspaceStrategy.HardLink, WorkspaceStrategy.FullCopy];

    private static bool _hasShownFirstLoadNotification;

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
    /// Event that is raised when the window should be closed.
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
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="contentStorageService">The content storage service.</param>
    /// <param name="localContentService">The local content service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="gameSettingsLogger">The game settings logger.</param>
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
    /// Refreshes the visible filters based on available content.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshVisibleFiltersAsync()
    {
        try
        {
            var manifestsResult = await _manifestPool!.GetAllManifestsAsync();
            if (!manifestsResult.Success || manifestsResult.Data == null) return;

            var availableTypes = manifestsResult.Data
                .Where(m => m.TargetGame == GameTypeFilter)
                .Select(m => m.ContentType)
                .Distinct()
                .ToHashSet();

            if (AvailableGameInstallations.Any(i => i.GameType == GameTypeFilter))
            {
                availableTypes.Add(ContentType.GameClient);
            }

            var newFilters = new List<FilterTypeInfo>();

            void AddFilterIfAvailable(ContentType type, string iconData)
            {
                if (availableTypes.Contains(type))
                {
                    newFilters.Add(new FilterTypeInfo(type, type.GetDisplayName(), iconData));
                }
            }

            AddFilterIfAvailable(ContentType.GameClient, "M20,19V7H4V19H20M20,3A2,2 0 0,1 22,5V19A2,2 0 0,1 20,21H4A2,2 0 0,1 2,19V5C2,3.89 2.9,3 4,3H20");
            AddFilterIfAvailable(ContentType.Mod, "M20.5 11H19V7c0-1.1-.9-2-2-2h-4V3.5C13 2.12 11.88 1 10.5 1S8 2.12 8 3.5V5H4c-1.1 0-1.99.9-1.99 2v3.8H3.5c1.49 0 2.7 1.21 2.7 2.7s-1.21 2.7-2.7 2.7H2V20c0 1.1.9 2 2 2h3.8v-1.5c0-1.49 1.21-2.7 2.7-2.7 1.49 0 2.7 1.21 2.7 2.7V22H17c1.1 0 2-.9 2-2v-4h1.5c1.38 0 2.5-1.12 2.5-2.5S21.88 11 20.5 11z");
            AddFilterIfAvailable(ContentType.MapPack, "M15,19L9,16.89V5L15,7.11M20.5,3C20.44,3 20.39,3 20.34,3L15,5.1L9,3L3.36,4.9C3.15,4.97 3,5.15 3,5.38V20.5A0.5,0.5 0 0,0 3.5,21C3.55,21 3.61,21 3.66,20.97L9,18.9L15,21L20.64,19.1C20.85,19 21,18.85 21,18.62V3.5A0.5,0.5 0 0,0 20.5,3Z");
            AddFilterIfAvailable(ContentType.ModdingTool, "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11.03L21.54,9.37C21.73,9.22 21.78,8.97 21.68,8.76L19.68,5.29C19.58,5.08 19.33,5 19.14,5.07L16.66,6.07C16.14,5.67 15.58,5.33 14.97,5.08L14.59,2.44C14.54,2.2 14.34,2.04 14.1,2.04H10.1C9.86,2.04 9.66,2.2 9.61,2.44L9.23,5.08C8.62,5.33 8.06,5.67 7.54,6.07L5.06,5.07C4.87,5 4.62,5.08 4.52,5.29L2.52,8.76C2.42,8.97 2.47,9.22 2.66,9.37L4.77,11.03C4.73,11.34 4.7,11.67 4.7,12C4.7,12.33 4.73,12.65 4.77,12.97L2.66,14.63C2.47,14.78 2.42,15.03 2.52,15.24L4.52,18.71C4.62,18.92 4.87,19 5.06,18.93L7.54,17.93C8.06,18.33 8.62,18.67 9.23,18.92L9.61,21.56C9.66,21.8 9.86,21.96 10.1,21.96H14.1C14.34,21.96 14.54,21.8 14.59,21.56L14.97,18.92C15.58,18.67 16.14,18.33 16.66,17.93L19.14,18.93C19.33,19 19.58,18.92 19.68,18.71L21.68,15.24C21.78,15.03 21.73,14.78 21.54,14.63L19.43,12.97Z");
            AddFilterIfAvailable(ContentType.Patch, "M14.6,16.6L19.2,12L14.6,7.4L16,6L22,12L16,18L14.6,16.6M9.4,16.6L4.8,12L9.4,7.4L8,6L2,12L8,18L9.4,16.6Z");
            AddFilterIfAvailable(ContentType.Addon, "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");

            VisibleFilters = new ObservableCollection<FilterTypeInfo>(newFilters);

            if (!availableTypes.Contains(SelectedContentType))
            {
                SelectedContentType = newFilters.FirstOrDefault()?.ContentType ?? ContentType.GameClient;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error refreshing visible filters");
        }
    }

    private async Task RefreshFiltersAndContentAsync()
    {
        await RefreshVisibleFiltersAsync();
        await LoadAvailableContentAsync();
    }

    partial void OnSelectedGameInstallationChanged(ContentDisplayItem? value)
    {
        if (value != null && value.GameType != GameTypeFilter)
        {
            GameTypeFilter = value.GameType;
            _logger?.LogInformation("Auto-synced GameTypeFilter to {GameType} based on SelectedGameInstallation", value.GameType);
        }
    }

    partial void OnGameTypeFilterChanged(GameType value)
    {
        _ = RefreshFiltersAndContentAsync();
    }

    private WorkspaceStrategy GetDefaultWorkspaceStrategy() => _configurationProvider!.GetDefaultWorkspaceStrategy();

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

    /// <summary>
    /// Loads available icons and covers based on the game type.
    /// </summary>
    private void LoadAvailableIconsAndCovers(string gameType)
    {
        try
        {
            if (_profileResourceService == null)
            {
                _logger?.LogWarning("ProfileResourceService is not available");
                return;
            }

            // Load icons for this game type
            var icons = _profileResourceService.GetIconsForGameType(gameType);
            AvailableIcons = new ObservableCollection<ProfileResourceItem>(icons);
            _logger?.LogInformation("Loaded {Count} icons for game type {GameType}", icons.Count, gameType);

            // Load ALL covers (not filtered by game type) so users can choose any cover
            var covers = _profileResourceService.GetAvailableCovers();
            AvailableCoversForSelection = new ObservableCollection<ProfileResourceItem>(covers);
            _logger?.LogInformation("Loaded {Count} covers (all types)", covers.Count);

            // Set selected icon based on current IconPath
            if (!string.IsNullOrEmpty(IconPath))
            {
                SelectedIcon = AvailableIcons.FirstOrDefault(i => i.Path == IconPath);
            }

            // Set selected cover based on current CoverPath
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

    private async Task OnContentTypeChangedAsync() => await LoadAvailableContentAsync();

    private string NormalizeResourcePath(string? path, string defaultUri)
    {
        if (string.IsNullOrWhiteSpace(path)) return defaultUri;
        if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)) return path;
        if (Uri.TryCreate(path, UriKind.Absolute, out _)) return path;
        return $"avares://GenHub/{path.TrimStart('/')}";
    }

    private void PopulateGameSettings(CreateProfileRequest request, UpdateProfileRequest? gameSettings)
    {
        if (gameSettings != null) GameSettingsMapper.PopulateRequest(request, gameSettings);
    }

    private void PopulateGameSettings(UpdateProfileRequest request, UpdateProfileRequest? gameSettings)
    {
        if (gameSettings != null) GameSettingsMapper.PopulateRequest(request, gameSettings);
    }

    private ContentDisplayItem ConvertToViewModelContentDisplayItem(Core.Models.Content.ContentDisplayItem coreItem)
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
}
