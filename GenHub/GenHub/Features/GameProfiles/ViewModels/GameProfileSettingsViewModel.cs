using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
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
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Notifications.Services;
using GenHub.Features.Notifications.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel for managing game profile settings, including content selection and configuration.
/// </summary>
public partial class GameProfileSettingsViewModel : ViewModelBase,
    IRecipient<Core.Models.Content.ContentAcquiredMessage>,
    IRecipient<ManifestReplacedMessage>
{
    /// <summary>
    /// Information about a content filter type.
    /// </summary>
    public record FilterTypeInfo(ContentType ContentType, string DisplayName, string IconData);

    /// <summary>
    /// Gets the list of available workspace strategies.
    /// </summary>
    public static IReadOnlyList<WorkspaceStrategy> AvailableWorkspaceStrategies { get; } =
    [
        WorkspaceStrategy.SymlinkOnly,
        WorkspaceStrategy.FullCopy,
        WorkspaceStrategy.HybridCopySymlink,
        WorkspaceStrategy.HardLink,
    ];

    /// <summary>
    /// Gets the list of available game types for local content.
    /// </summary>
    public static IReadOnlyList<GameType> AvailableLocalGameTypes { get; } =
    [
        Core.Models.Enums.GameType.Generals,
        Core.Models.Enums.GameType.ZeroHour,
    ];

    /// <summary>
    /// Gets the list of allowed content types for local identification.
    /// </summary>
    public static IReadOnlyList<ContentType> AllowedLocalContentTypes { get; } =
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

    private static bool HasShownFirstLoadNotification { get; set; }

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
            IsEditable = coreItem.IsEditable,
            SourcePath = coreItem.SourcePath,
            IsLocked = false,
            CanToggle = true,
        };
    }

    private static void ValidateSingleDependencyWarning(
        ContentManifest manifest,
        ContentDependency dependency,
        Dictionary<string, ContentManifest> manifestsById,
        Dictionary<ContentType, List<ContentManifest>> manifestsByType,
        Dictionary<ContentType, List<ContentDisplayItem>> enabledContentByType,
        List<string> warnings)
    {
        if (dependency.DependencyType == ContentType.GameInstallation || dependency.DependencyType == ContentType.GameClient)
        {
            if (!enabledContentByType.TryGetValue(dependency.DependencyType, out var enabledOfType) || enabledOfType.Count == 0)
            {
                warnings.Add(dependency.DependencyType == ContentType.GameInstallation
                    ? $"'{manifest.Name}' requires a Game Installation to be selected."
                    : $"'{manifest.Name}' requires a Game Client to be selected.");
            }

            return;
        }

        if (!manifestsByType.TryGetValue(dependency.DependencyType, out var potentialMatches) || potentialMatches.Count == 0)
        {
            if (!dependency.IsOptional) warnings.Add($"'{manifest.Name}' requires {dependency.DependencyType} content, but none is enabled.");
            return;
        }

        if (dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
        {
            var declaredId = dependency.Id.ToString();
            bool found = manifestsById.ContainsKey(declaredId);
            if (!found)
            {
                var depIdSegments = declaredId.Split('.');
                found = potentialMatches.Any(m =>
                {
                    var segments = m.Id.ToString().Split('.');
                    return HasCompatibleCatalogMatch(declaredId, m.Id.ToString()) ||
                        (!dependency.StrictPublisher && segments.Length >= 5 && depIdSegments.Length >= 5 &&
                         segments[3].Equals(depIdSegments[3], StringComparison.OrdinalIgnoreCase) &&
                         segments[4].Equals(depIdSegments[4], StringComparison.OrdinalIgnoreCase));
                });
            }

            if (!found && !dependency.IsOptional) warnings.Add($"'{manifest.Name}' requires '{dependency.Name}' which is not enabled.");
        }

        foreach (var conflictId in dependency.ConflictsWith)
        {
            if (manifestsById.TryGetValue(conflictId.ToString(), out var conflicting))
                warnings.Add($"'{manifest.Name}' conflicts with '{conflicting.Name}' - these cannot be used together.");
        }
    }

    private static bool HasCompatibleCatalogMatch(string declaredId, string availableId) =>
        DependencyResolver.HasCompatibleCatalogIdentity(declaredId, availableId);

    private readonly IGameProfileManager? _gameProfileManager;
    private readonly IGameSettingsService? _gameSettingsService;
    private readonly IConfigurationProviderService? _configurationProvider;
    private readonly IProfileContentLoader? _profileContentLoader;
    private readonly Services.ProfileResourceService? _profileResourceService;
    private readonly INotificationService? _notificationService;
    private readonly IContentManifestPool? _manifestPool;
    private readonly IContentStorageService? _contentStorageService;
    private readonly ILocalContentService? _localContentService;
    private readonly IGenLauncherNormalizationService? _genLauncherNormalizationService;
    private readonly IDialogService? _dialogService;
    private readonly IProfileSharingService? _profileSharingService;
    private readonly ILogger<GameProfileSettingsViewModel>? _logger;
    private readonly ILogger<GameSettingsViewModel>? _gameSettingsLogger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly NotificationService _localNotificationService = new(NullLogger<NotificationService>.Instance);

    /// <summary>
    /// Gets a value indicating whether the current profile can be shared (i.e. is already saved and has an ID).
    /// </summary>
    public bool CanShareProfile => !string.IsNullOrEmpty(CurrentProfileId);

    private WorkspaceStrategy? OriginalWorkspaceStrategy { get; set; }

    private string? CurrentProfileId { get; set; }

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
    /// <param name="genLauncherNormalizationService">The GenLauncher normalization service.</param>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="logger">The logger for this view model.</param>
    /// <param name="gameSettingsLogger">The logger for the game settings view model.</param>
    /// <param name="profileSharingService">The profile sharing service.</param>
    /// <param name="loggerFactory">Optional logger factory for creating child view model loggers.</param>
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
        IGenLauncherNormalizationService? genLauncherNormalizationService,
        IDialogService? dialogService,
        ILogger<GameProfileSettingsViewModel>? logger,
        ILogger<GameSettingsViewModel>? gameSettingsLogger,
        IProfileSharingService? profileSharingService = null,
        ILoggerFactory? loggerFactory = null)
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
        _genLauncherNormalizationService = genLauncherNormalizationService;
        _dialogService = dialogService;
        _logger = logger;
        _gameSettingsLogger = gameSettingsLogger;
        _profileSharingService = profileSharingService;
        _loggerFactory = loggerFactory;

        NotificationManager = new NotificationManagerViewModel(
            _localNotificationService,
            NullLogger<NotificationManagerViewModel>.Instance,
            NullLogger<NotificationItemViewModel>.Instance);

        GameSettingsViewModel = new GameSettingsViewModel(gameSettingsService!, gameSettingsLogger!);

        WeakReferenceMessenger.Default.Register<Core.Models.Content.ContentAcquiredMessage>(this);
        WeakReferenceMessenger.Default.Register<ManifestReplacedMessage>(this);
    }

    /// <inheritdoc/>
    public void Receive(Core.Models.Content.ContentAcquiredMessage message) => _ = LoadAvailableContentAsync();

    /// <inheritdoc/>
    public void Receive(ManifestReplacedMessage message)
    {
        // Global manifest replacement - update our state surgicaly to avoid losing unsaved toggles
        // Dispatch to UI thread to ensure ObservableCollection mutations happen safely
        Dispatcher.UIThread.Post(() => _ = HandleManifestReplacementAsync(message.OldId, message.NewId));
    }

    /// <summary>
    /// Handles the replacement of a manifest ID with a new one globally.
    /// Updates enabled and available content collections to use the new manifest ID.
    /// </summary>
    /// <param name="oldId">The old manifest ID to replace.</param>
    /// <param name="newId">The new manifest ID to use.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    internal async Task HandleManifestReplacementAsync(string oldId, string newId)
    {
        try
        {
            bool affected = false;

            // 1. Check EnabledContent - use ManifestId.Value for comparison
            var inEnabled = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == oldId);
            if (inEnabled != null)
            {
                _logger?.LogInformation("Replacing manifest {OldId} with {NewId} in EnabledContent", oldId, newId);
                var index = EnabledContent.IndexOf(inEnabled);

                // Get the new presentation data for the item
                if (_manifestPool != null && _profileContentLoader != null)
                {
                    var manifestResult = await _manifestPool.GetManifestAsync(newId);
                    if (manifestResult.Success && manifestResult.Data != null)
                    {
                        var coreItem = _profileContentLoader.CreateManifestDisplayItem(manifestResult.Data);
                        var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                        viewModelItem.IsEnabled = true;
                        EnabledContent[index] = viewModelItem;
                        affected = true;
                    }
                }
            }

            // 2. Check AvailableContent - use ManifestId.Value for comparison
            var inAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == oldId);
            if (inAvailable != null)
            {
                _logger?.LogInformation("Removing old manifest {OldId} from AvailableContent", oldId);
                AvailableContent.Remove(inAvailable);
                affected = true;
            }

            // 3. Check SelectedGameInstallation (if it's a GameClient replacement)
            if (SelectedGameInstallation != null &&
                SelectedGameInstallation.ManifestId.Value == oldId &&
                _manifestPool != null &&
                _profileContentLoader != null)
            {
                var manifestResult = await _manifestPool.GetManifestAsync(newId);
                if (manifestResult.Success && manifestResult.Data != null)
                {
                    var coreItem = _profileContentLoader.CreateManifestDisplayItem(manifestResult.Data);
                    SelectedGameInstallation = ConvertToViewModelContentDisplayItem(coreItem);
                    SelectedGameInstallation.IsEnabled = true;
                    affected = true;
                }
            }

            if (affected)
            {
                // Refresh to ensure everything (filters, lists) is consistent
                await RefreshFiltersAndContentAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling manifest replacement message");
        }
    }

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
        if (value is { GameType: var gameType } && gameType != GameTypeFilter)
        {
            GameTypeFilter = gameType;
            _logger?.LogInformation("Auto-synced GameTypeFilter to {GameType} based on SelectedGameInstallation", gameType);
        }
    }

    private async Task OnContentTypeChangedAsync() => await LoadAvailableContentAsync();

    private async Task EnableContentInternal(
        ContentDisplayItem? contentItem,
        bool bypassLoadingGuard = false,
        bool isRootOperation = true,
        List<string>? autoEnabledNames = null,
        CancellationToken cancellationToken = default)
    {
        if (contentItem is null || !CanEnableContent(contentItem, bypassLoadingGuard))
        {
            return;
        }

        ReplaceConflictingEnabledContent(contentItem);
        ActivateContentItem(contentItem);

        var autoResolved = autoEnabledNames ?? [];
        await ResolveDependenciesAsync(contentItem, autoResolved, cancellationToken);

        if (isRootOperation)
        {
            await HandleRootOperationCompletionAsync(contentItem, autoResolved, cancellationToken);
        }
    }

    private bool CanEnableContent(ContentDisplayItem? contentItem, bool bypassLoadingGuard)
    {
        if (contentItem == null || (IsLoadingContent && !bypassLoadingGuard))
        {
            return false;
        }

        if (contentItem.ContentType == ContentType.GameInstallation && SelectedGameInstallation == contentItem && contentItem.IsEnabled)
        {
            return false;
        }

        if (contentItem.IsLocked)
        {
            StatusMessage = "This content item is locked and cannot be modified";
            return false;
        }

        if (!contentItem.CanToggle)
        {
            StatusMessage = "This content item cannot be toggled";
            return false;
        }

        if (contentItem.IsEnabled || EnabledContent.Any(e => e.ManifestId.Value == contentItem.ManifestId.Value))
        {
            return false;
        }

        return true;
    }

    private void ReplaceConflictingEnabledContent(ContentDisplayItem contentItem)
    {
        if (contentItem.ContentType != ContentType.GameInstallation && contentItem.ContentType != ContentType.GameClient)
        {
            return;
        }

        var existingItems = EnabledContent.Where(e => e.ContentType == contentItem.ContentType).ToList();
        foreach (var existing in existingItems)
        {
            if (existing.ContentType == ContentType.GameClient && Name == existing.DisplayName)
            {
                Name = ProfileConstants.DefaultProfileName;
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
                        IsEditable = existing.IsEditable,
                        SourcePath = existing.SourcePath,
                        IsLocked = existing.IsLocked,
                        CanToggle = existing.CanToggle,
                    });
                }
            }
        }
    }

    private void ActivateContentItem(ContentDisplayItem contentItem)
    {
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

        if (contentItem.ContentType == ContentType.GameClient && Name == ProfileConstants.DefaultProfileName)
        {
            Name = contentItem.DisplayName;
        }
    }

    private async Task HandleRootOperationCompletionAsync(ContentDisplayItem contentItem, List<string> autoResolved, CancellationToken cancellationToken = default)
    {
        if (autoResolved.Count > 0)
        {
            _localNotificationService.ShowSuccess(
                "Content Enabled",
                $"Enabled '{contentItem.DisplayName}' and auto-resolved: {string.Join(", ", autoResolved)}");
        }
        else
        {
            _localNotificationService.ShowSuccess(
                "Content Enabled",
                $"Enabled '{contentItem.DisplayName}'");
        }

        await ValidateEnabledContentDependenciesAsync(contentItem.DisplayName, cancellationToken);
    }

    private async Task ResolveDependenciesAsync(ContentDisplayItem contentItem, List<string> autoEnabledNames, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_manifestPool == null) return;

            var manifest = await GetOrSynthesizeManifestForContentAsync(contentItem, cancellationToken);
            if (manifest?.Dependencies == null || manifest.Dependencies.Count == 0)
            {
                return;
            }

            foreach (var dependency in manifest.Dependencies)
            {
                if (dependency.DependencyType == ContentType.GameInstallation)
                {
                    await ResolveGameInstallationDependencyAsync(contentItem, dependency, autoEnabledNames, cancellationToken);
                }
                else
                {
                    await ResolveContentDependencyAsync(dependency, autoEnabledNames, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error resolving dependencies for {ContentName}", contentItem.DisplayName);
        }
    }

    private async Task<ContentManifest?> GetOrSynthesizeManifestForContentAsync(ContentDisplayItem contentItem, CancellationToken cancellationToken = default)
    {
        if (_manifestPool == null)
        {
            return null;
        }

        var manifestResult = await _manifestPool.GetManifestAsync(ManifestId.Create(contentItem.ManifestId.Value), cancellationToken);
        if (manifestResult.Success && manifestResult.Data != null)
        {
            return manifestResult.Data;
        }

        if (contentItem.ContentType == ContentType.GameClient && !string.IsNullOrEmpty(contentItem.SourceId))
        {
            return new ContentManifest
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

        return null;
    }

    private async Task ResolveGameInstallationDependencyAsync(
        ContentDisplayItem contentItem,
        ContentDependency dependency,
        List<string> autoEnabledNames,
        CancellationToken cancellationToken = default)
    {
        bool isSatisfied = false;
        var isDefaultDep = dependency.Id.ToString() == ManifestConstants.DefaultContentDependencyId;

        if (isDefaultDep)
        {
            if (dependency.CompatibleGameTypes is { Count: > 0 } compatibleGameTypes &&
                SelectedGameInstallation is { IsEnabled: true } selectedInstallation &&
                compatibleGameTypes.Contains(selectedInstallation.GameType))
            {
                isSatisfied = true;
            }
        }
        else
        {
            if (SelectedGameInstallation is { IsEnabled: true } selectedInst &&
                selectedInst.ManifestId.Value == dependency.Id.ToString())
            {
                isSatisfied = true;
            }
        }

        if (isSatisfied) return;

        ContentDisplayItem? compatibleInstallation = null;
        if (dependency.Id.ToString() != ManifestConstants.DefaultContentDependencyId)
        {
            compatibleInstallation = AvailableGameInstallations.FirstOrDefault(x => x.ManifestId.Value == dependency.Id.ToString());
        }

        if (compatibleInstallation == null && !string.IsNullOrEmpty(contentItem.SourceId))
        {
            compatibleInstallation = AvailableGameInstallations.FirstOrDefault(x => x.ManifestId.Value == contentItem.SourceId);
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
            if (!autoEnabledNames.Contains(compatibleInstallation.DisplayName))
            {
                autoEnabledNames.Add(compatibleInstallation.DisplayName);
            }

            await EnableContentInternal(compatibleInstallation, bypassLoadingGuard: true, isRootOperation: false, autoEnabledNames, cancellationToken);
        }
    }

    private async Task ResolveContentDependencyAsync(
        ContentDependency dependency,
        List<string> autoEnabledNames,
        CancellationToken cancellationToken = default)
    {
        var declaredId = dependency.Id.ToString();
        bool alreadyEnabled = declaredId != ManifestConstants.DefaultContentDependencyId
            ? EnabledContent.Any(x => x.ManifestId.Value == declaredId ||
                (x.ContentType == dependency.DependencyType &&
                 HasCompatibleCatalogMatch(declaredId, x.ManifestId.Value)))
            : EnabledContent.Any(x => x.ContentType == dependency.DependencyType);

        if (alreadyEnabled || dependency.IsOptional || _profileContentLoader == null) return;

        var availableOfTargetType = await _profileContentLoader.LoadAvailableContentAsync(
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

        var match = declaredId != ManifestConstants.DefaultContentDependencyId
            ? (availableOfTargetType.FirstOrDefault(x => x.ManifestId == declaredId)
               ?? availableOfTargetType.FirstOrDefault(x => HasCompatibleCatalogMatch(declaredId, x.ManifestId)))
            : availableOfTargetType.FirstOrDefault(x => x.ContentType == dependency.DependencyType);

        if (match != null)
        {
            var viewModelItem = ConvertToViewModelContentDisplayItem(match);
            if (!viewModelItem.IsEnabled)
            {
                if (!autoEnabledNames.Contains(viewModelItem.DisplayName))
                {
                    autoEnabledNames.Add(viewModelItem.DisplayName);
                }

                await EnableContentInternal(viewModelItem, bypassLoadingGuard: true, isRootOperation: false, autoEnabledNames, cancellationToken);
            }
        }
    }

    private async Task ValidateEnabledContentDependenciesAsync(string justEnabledContentName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_manifestPool == null) return;
            var enabledManifestIds = EnabledContent.Select(e => e.ManifestId.Value).ToList();
            if (enabledManifestIds.Count == 0) return;

            var manifests = new List<ContentManifest>();
            foreach (var manifestId in enabledManifestIds)
            {
                var manifestResult = await _manifestPool.GetManifestAsync(ManifestId.Create(manifestId), cancellationToken);
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
                    ValidateSingleDependencyWarning(manifest, dependency, manifestsById, manifestsByType, enabledContentByType, warnings);
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
                        if (!dep.IsOptional)
                        {
                            var reqType = dep.DependencyType switch
                            {
                                ContentType.GameInstallation => "a Game Installation",
                                ContentType.GameClient => "a Game Client",
                                _ => $"{dep.DependencyType} content",
                            };
                            errors.Add($"• '{manifest.Name}' requires {reqType}");
                        }

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
            if (_profileContentLoader == null) return;

            var coreItems = await _profileContentLoader.LoadEnabledContentForProfileAsync(profile);
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
            if (_profileContentLoader == null) return;

            var coreItems = await _profileContentLoader.LoadAvailableGameInstallationsAsync();
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

    private WorkspaceStrategy GetDefaultWorkspaceStrategy() =>
        _configurationProvider?.GetDefaultWorkspaceStrategy() ?? WorkspaceConstants.DefaultWorkspaceStrategy;
}
