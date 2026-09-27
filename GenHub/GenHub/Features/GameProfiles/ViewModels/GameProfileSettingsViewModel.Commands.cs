using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Commands for the GameProfileSettingsViewModel.
/// </summary>
public partial class GameProfileSettingsViewModel
{
    /// <summary>
    /// Updates the selected general category from the scroll spy without triggering a scroll request.
    /// </summary>
    /// <param name="category">The new active category.</param>
    public void UpdateGeneralCategoryFromScroll(GeneralSettingsCategory category)
    {
        SelectedGeneralCategory = category;
    }

    /// <summary>
    /// Updates the selected content category from the scroll spy without triggering a scroll request.
    /// </summary>
    /// <param name="category">The new active category.</param>
    public void UpdateContentCategoryFromScroll(ContentSettingsCategory category)
    {
        SelectedContentCategory = category;
    }

    /// <summary>
    /// Updates the selected content editor category from the scroll spy without triggering a scroll request.
    /// </summary>
    /// <param name="category">The new active category.</param>
    public void UpdateContentEditorCategoryFromScroll(ContentEditorCategory category)
    {
        SelectedContentEditorCategory = category;
    }

    /// <summary>
    /// Imports dropped files or directories directly into the Add Local Content flow.
    /// </summary>
    /// <param name="paths">The paths to the dropped files or directories.</param>
    /// <param name="suggestedContentType">Optional suggested content type based on binary detection.</param>
    /// <param name="suggestedGameType">Optional suggested game type based on binary detection.</param>
    /// <param name="owner">Optional window owner for modal dialogs.</param>
    /// <param name="cancellationToken">Token to cancel staging of the dropped paths.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ImportDroppedFilesAsync(
        IReadOnlyList<string> paths,
        ContentType? suggestedContentType = null,
        GameType? suggestedGameType = null,
        Avalonia.Controls.Window? owner = null,
        CancellationToken cancellationToken = default)
    {
        IsDropImportInProgress = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_localContentService == null || _contentStorageService == null)
            {
                StatusMessage = _localizationService?["GameProfiles.Status.ContentServicesUnavailable"] ?? "Content services unavailable";
                return;
            }

            var dialogOwner = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (dialogOwner == null)
            {
                return;
            }

            using var vm = new AddLocalContentViewModel(
                _localContentService,
                _contentStorageService,
                _genLauncherNormalizationService,
                _dialogService,
                _archivePayloadProcessor);

            if (suggestedContentType.HasValue)
            {
                vm.SelectedContentType = suggestedContentType.Value;
            }

            if (suggestedGameType.HasValue)
            {
                vm.SelectedGameType = suggestedGameType.Value;
            }

            foreach (var p in paths.Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await vm.ImportContentAsync(p, cancellationToken);
            }

            var window = new Views.AddLocalContentWindow
            {
                DataContext = vm,
            };

            var result = await window.ShowDialog<bool>(dialogOwner);

            if (result && vm.CreatedContentItem != null)
            {
                var contentItem = vm.CreatedContentItem;

                if (AvailableContent.All(a => a.ManifestId.Value != contentItem.ManifestId.Value))
                {
                    AvailableContent.Add(contentItem);
                }

                _logger?.LogInformation("Added dropped local content via dialog: {Name}", contentItem.DisplayName);

                NotifyLocalContentAdded(contentItem.DisplayName);

                // The confirmed dialog result is a committed operation: enabling runs to completion
                // so cancellation cannot leave the profile and displayed collections partially updated.
                await EnableContentInternal(contentItem, bypassLoadingGuard: true, cancellationToken: CancellationToken.None);

                await RefreshFiltersAndContentAsync();
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation(ex, "Dropped file import was cancelled.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error importing dropped files into Add Local Content dialog");
            var importErrorMessage = _localizationService?.GetString("GameProfiles.Settings.LocalContent.ImportError") ?? "Error importing dropped files";
            StatusMessage = importErrorMessage;
            _localNotificationService.ShowError(
                _localizationService?.GetString("GameProfiles.Notification.Error.Title") ?? "Error",
                importErrorMessage);
        }
        finally
        {
            IsDropImportInProgress = false;
        }
    }

    /// <summary>
    /// Invoked when a tab is selected via <see cref="SelectTabCommand"/>.
    /// </summary>
    /// <param name="tabIndex">The selected tab index.</param>
    protected virtual void OnTabSelected(int tabIndex)
    {
    }

    /// <summary>
    /// Loads the available content items based on current filters.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    protected virtual async Task LoadAvailableContentAsync()
    {
        var version = Interlocked.Increment(ref _loadContentVersion);

        await _loadContentSemaphore.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref _loadContentVersion))
            {
                return;
            }

            IsLoadingContent = true;
            StatusMessage = "Loading content...";

            await RefreshHotswapStateAsync();

            var enabledContentIds = EnabledContent.Select(e => e.ManifestId.Value).ToList();

            var coreAvailableInstallations = AvailableGameInstallations.Select(ToCoreContentDisplayItem).ToList();

            if (_profileContentLoader == null)
            {
                StatusMessage = "Content loader unavailable";
                return;
            }

            var coreItems = await _profileContentLoader.LoadAvailableContentAsync(
                SelectedContentType,
                new ObservableCollection<Core.Models.Content.ContentDisplayItem>(coreAvailableInstallations),
                enabledContentIds);

            if (version != Volatile.Read(ref _loadContentVersion))
            {
                return;
            }

            var newItems = FilterAndConvertContentItems(coreItems, enabledContentIds, GameTypeFilter);

            if (version != Volatile.Read(ref _loadContentVersion))
            {
                return;
            }

            AvailableContent.Clear();
            foreach (var item in newItems)
            {
                AvailableContent.Add(item);
            }

            StatusMessage = string.Empty;
            _logger?.LogInformation("Loaded {Count} content items for content type {ContentType}", AvailableContent.Count, SelectedContentType);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading available content");
            StatusMessage = DefaultErrorLoadingContent;
            var title = GetErrorLoadingContentTitle();
            _notificationService?.ShowError(title, ex.Message);
        }
        finally
        {
            if (version == Volatile.Read(ref _loadContentVersion))
            {
                IsLoadingContent = false;
            }

            _loadContentSemaphore.Release();
        }
    }

    private static Core.Models.Content.ContentDisplayItem ToCoreContentDisplayItem(ContentDisplayItem vmItem) =>
        new()
        {
            Id = vmItem.ManifestId.Value,
            ManifestId = vmItem.ManifestId.Value,
            DisplayName = vmItem.DisplayName,
            ContentType = vmItem.ContentType,
            GameType = vmItem.GameType,
            InstallationType = vmItem.InstallationType,
            Publisher = vmItem.Publisher ?? string.Empty,
            Version = vmItem.Version ?? string.Empty,
            SourceId = vmItem.SourceId ?? string.Empty,
            GameClientId = vmItem.GameClientId ?? string.Empty,
            GameClient = vmItem.GameClient?.Clone(),
            Manifest = vmItem.Manifest,
            IsEnabled = vmItem.IsEnabled,
        };

    private List<ContentDisplayItem> FilterAndConvertContentItems(
        IEnumerable<Core.Models.Content.ContentDisplayItem> coreItems,
        ICollection<string> enabledContentIds,
        GameType targetFilter)
    {
        var newItems = new List<ContentDisplayItem>();

        foreach (var coreItem in coreItems)
        {
            if (enabledContentIds.Contains(coreItem.ManifestId) || (coreItem.GameType != targetFilter && coreItem.GameType != Core.Models.Enums.GameType.Unknown))
            {
                continue;
            }

            if (newItems.Any(existing =>
                string.Equals(existing.ManifestId.Value, coreItem.ManifestId, StringComparison.OrdinalIgnoreCase) ||
                (existing.ContentType == coreItem.ContentType &&
                 existing.GameType == coreItem.GameType &&
                 string.Equals(existing.DisplayName, coreItem.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(existing.Publisher, coreItem.Publisher, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(existing.Version, coreItem.Version, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            try
            {
                newItems.Add(ConvertToViewModelContentDisplayItem(coreItem));
            }
            catch (ArgumentException argEx)
            {
                _logger?.LogWarning(argEx, "Skipping invalid content item {DisplayName} (ID: {Id})", coreItem.DisplayName, coreItem.ManifestId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error converting content item {DisplayName}", coreItem.DisplayName);
            }
        }

        return newItems;
    }

    [RelayCommand]
    private void SelectGeneralCategory(GeneralSettingsCategory category)
    {
        SelectedGeneralCategory = category;
        ScrollToSectionRequested?.Invoke(category.ToString() + "Section");
    }

    [RelayCommand]
    private void SelectContentCategory(ContentSettingsCategory category)
    {
        SelectedContentCategory = category;
        ScrollToSectionRequested?.Invoke(category.ToString() + "Section");
    }

    [RelayCommand]
    private void SelectContentEditorCategory(ContentEditorCategory category)
    {
        System.Diagnostics.Debug.WriteLine($"[ViewModel] SelectContentEditorCategory called with category: {category}");
        System.Diagnostics.Debug.WriteLine($"[ViewModel] ScrollToSectionRequested is null: {ScrollToSectionRequested == null}");

        SelectedContentEditorCategory = category;

        var sectionName = category.ToString() + "Section";
        System.Diagnostics.Debug.WriteLine($"[ViewModel] Invoking ScrollToSectionRequested with: {sectionName}");

        ScrollToSectionRequested?.Invoke(sectionName);

        System.Diagnostics.Debug.WriteLine("[ViewModel] ScrollToSectionRequested invoked");
    }

    [RelayCommand]
    private void ScrollToSection(string sectionName)
    {
        ScrollToSectionRequested?.Invoke(sectionName);
    }

    [RelayCommand]
    private async Task EnableContentAsync(ContentDisplayItem? contentItem)
    {
        await EnableContentInternal(contentItem, bypassLoadingGuard: false);
    }

    private async Task<bool> ValidateInstallationRemovalAsync(
        ContentDisplayItem contentItem,
        string actionCommand,
        string actionVerb,
        string notificationTitle,
        CancellationToken cancellationToken = default)
    {
        if (contentItem.ContentType != ContentType.GameInstallation)
        {
            return true;
        }

        var installation = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value) ?? contentItem;
        var dependentClients = await GetDependentActiveGameClientsAsync(installation, cancellationToken);
        if (dependentClients.Count == 0)
        {
            return true;
        }

        var clientNames = string.Join(", ", dependentClients.Select(c => $"'{c.DisplayName}'"));
        StatusMessage = string.Format(
            ProfileValidationConstants.InstallationActionBlockedStatusFormat,
            actionVerb,
            contentItem.DisplayName,
            clientNames);
        _logger?.LogWarning(
            "{Action} blocked: Game Installation '{Installation}' is required by active Game Client(s) {Clients}",
            actionCommand,
            contentItem.DisplayName,
            clientNames);

        var notificationMessage = string.Format(
            ProfileValidationConstants.InstallationActionBlockedNotificationFormat,
            actionVerb,
            contentItem.DisplayName,
            clientNames);
        _localNotificationService.ShowWarning(notificationTitle, notificationMessage);
        _notificationService?.ShowWarning(notificationTitle, notificationMessage);
        return false;
    }

    [RelayCommand]
    private async Task DisableContentAsync(ContentDisplayItem? contentItem, CancellationToken cancellationToken = default)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            _logger?.LogWarning("DisableContent: contentItem parameter is null");
            return;
        }

        if (!await ValidateInstallationRemovalAsync(
                contentItem,
                "DisableContent",
                "remove",
                ProfileValidationConstants.CannotRemoveInstallationTitle,
                cancellationToken))
        {
            return;
        }

        if (contentItem.IsLocked)
        {
            StatusMessage = ContentLockedMessage;
            _logger?.LogWarning("DisableContent: Cannot disable locked item {DisplayName}", contentItem.DisplayName);
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentLocked.Title", ContentLockedTitle),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentLocked.Message", $"'{contentItem.DisplayName}' is locked and cannot be modified while the game is running.", contentItem.DisplayName));
            return;
        }

        if (!contentItem.CanToggle)
        {
            StatusMessage = "This content item cannot be toggled";
            _logger?.LogWarning("DisableContent: Cannot disable non-toggleable item {DisplayName}", contentItem.DisplayName);
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.CannotModifyContent.Title", ProfileValidationConstants.CannotModifyContentTitle),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.CannotModifyContent.Message", $"'{contentItem.DisplayName}' cannot be modified in this mode.", contentItem.DisplayName));
            return;
        }

        _logger?.LogInformation(
            "DisableContent called for: {DisplayName} (ManifestId: {ManifestId})",
            contentItem.DisplayName,
            contentItem.ManifestId.Value);

        var itemToRemove = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
        if (itemToRemove != null)
        {
            itemToRemove.IsEnabled = false;
            EnabledContent.Remove(itemToRemove);

            UpdateAvailableContentOnDisable(itemToRemove);
            UpdateSelectedInstallationOnDisable(itemToRemove);
            UpdateApplicableClientVisibility();

            ApplyPrimaryBranding();

            StatusMessage = $"Disabled {itemToRemove.DisplayName}";
            _logger?.LogInformation("Disabled content {ContentName} from profile", itemToRemove.DisplayName);
        }
        else
        {
            StatusMessage = "Content not found in enabled list";
            _logger?.LogWarning("DisableContent: ManifestId {ManifestId} not found in EnabledContent", contentItem.ManifestId.Value);
        }

        await Task.CompletedTask;
    }

    private void UpdateAvailableContentOnDisable(ContentDisplayItem itemToRemove)
    {
        if (itemToRemove.ContentType != SelectedContentType ||
            (itemToRemove.GameType != GameTypeFilter && itemToRemove.GameType != Core.Models.Enums.GameType.Unknown))
        {
            return;
        }

        var alreadyInAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == itemToRemove.ManifestId.Value);
        if (alreadyInAvailable == null)
        {
            AvailableContent.Add(itemToRemove);
        }
        else
        {
            alreadyInAvailable.IsEnabled = false;
        }
    }

    private void UpdateSelectedInstallationOnDisable(ContentDisplayItem itemToRemove)
    {
        if (itemToRemove.ContentType == ContentType.GameInstallation &&
            SelectedGameInstallation?.ManifestId.Value == itemToRemove.ManifestId.Value)
        {
            SelectedGameInstallation = null;
            _logger?.LogInformation("Cleared SelectedGameInstallation");

            var remainingInstallation = EnabledContent.FirstOrDefault(e => e.ContentType == ContentType.GameInstallation && e.ManifestId.Value != itemToRemove.ManifestId.Value);
            if (remainingInstallation != null)
            {
                SelectedGameInstallation = remainingInstallation;
            }
        }
        else if (itemToRemove.ContentType is ContentType.GameClient or ContentType.Mod &&
                 SelectedGameInstallation != null &&
                 EnabledContent.All(e => e.ContentType is not (ContentType.GameClient or ContentType.Mod)))
        {
            SelectedGameInstallation = null;
            _logger?.LogInformation("Auto-disabled SelectedGameInstallation as no GameClient or Mod remains enabled");
        }
    }

    [RelayCommand]
    private async Task DeleteContentAsync(ContentDisplayItem? contentItem, CancellationToken cancellationToken = default)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            _logger?.LogWarning("DeleteContent: contentItem parameter is null");
            return;
        }

        if (!await ValidateInstallationRemovalAsync(
                contentItem,
                "DeleteContent",
                "delete",
                ProfileValidationConstants.CannotDeleteInstallationTitle,
                cancellationToken))
        {
            return;
        }

        if (contentItem.IsLocked)
        {
            StatusMessage = ContentLockedMessage;
            _logger?.LogWarning("DeleteContent: Cannot delete locked item {DisplayName}", contentItem.DisplayName);
            return;
        }

        _logger?.LogInformation(
            "DeleteContent called for: {DisplayName} (ManifestId: {ManifestId})",
            contentItem.DisplayName,
            contentItem.ManifestId.Value);

        try
        {
            if (_localContentService == null || _contentStorageService == null)
            {
                _localNotificationService.ShowError(
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ServiceUnavailable.Title", "Service Unavailable"),
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ServiceUnavailable.Message", "Content deletion service is not available."));
                return;
            }

            _logger?.LogInformation("Attempting to delete content: {ContentName}", contentItem.DisplayName);

            var result = await _localContentService.DeleteLocalContentAsync(contentItem.ManifestId.Value, cancellationToken);

            if (result.Success)
            {
                var enabledItem = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == contentItem.ManifestId.Value);
                if (enabledItem != null)
                {
                    EnabledContent.Remove(enabledItem);
                }

                var availableItem = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == contentItem.ManifestId.Value);
                if (availableItem != null)
                {
                    AvailableContent.Remove(availableItem);
                }

                StatusMessage = $"Deleted {contentItem.DisplayName}";
                _localNotificationService.ShowSuccess(
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentDeleted.Title", "Content Deleted"),
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentDeleted.Message", $"'{contentItem.DisplayName}' has been permanently deleted.", contentItem.DisplayName));
                _logger?.LogInformation("Successfully deleted content: {ContentName}", contentItem.DisplayName);
            }
            else
            {
                StatusMessage = $"Failed to delete {contentItem.DisplayName}";
                _localNotificationService.ShowError(
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentDeleteFailed.Title", "Delete Failed"),
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentDeleteFailed.Message", $"Failed to delete '{contentItem.DisplayName}': {string.Join(", ", result.Errors)}", contentItem.DisplayName, string.Join(", ", result.Errors)));
                _logger?.LogWarning(
                    "Failed to delete content {ContentName}: {Errors}",
                    contentItem.DisplayName,
                    string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deleting content {ContentName}", contentItem.DisplayName);
            StatusMessage = "Error deleting content";
            _localNotificationService.ShowError(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentDeleteError.Title", "Delete Error"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentDeleteError.Message", $"An error occurred while deleting '{contentItem.DisplayName}'.", contentItem.DisplayName));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsSaving = true;

            if (IsDropImportInProgress)
            {
                var importTitle = _localizationService?.GetString("GameProfiles.Settings.Save.DropImportInProgress.Title") ?? "Import in progress";
                var importMessage = _localizationService?.GetString("GameProfiles.Settings.Save.DropImportInProgress.Message") ?? "Please wait for the dropped content import to finish before saving.";
                StatusMessage = importMessage;
                _localNotificationService.ShowWarning(importTitle, importMessage);
                _logger?.LogWarning("Profile save blocked: dropped content import still in progress");
                return;
            }

            StatusMessage = "Saving profile...";

            if (_gameProfileManager == null)
            {
                StatusMessage = "Profile manager not available";
                return;
            }

            var enabledItems = EnabledContent.Where(c => c.IsEnabled).ToList();
            var isStandaloneProfile = ToolProfileHelper.IsToolProfile(
                enabledItems.Select(c => (c.ManifestId.Value, c.ContentType)));

            if (SelectedGameInstallation == null && !isStandaloneProfile)
            {
                StatusMessage = "Please select a game installation";
                _localNotificationService.ShowError(
                    ProfileValidationConstants.MissingGameInstallationTitle,
                    ProfileValidationConstants.SelectGameInstallationBeforeSaving);
                _notificationService?.ShowError(
                    ProfileValidationConstants.MissingGameInstallationTitle,
                    ProfileValidationConstants.SelectGameInstallationBeforeSaving);
                _logger?.LogWarning("Profile save blocked: No game installation selected");
                return;
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                StatusMessage = "Please enter a profile name";
                _localNotificationService.ShowWarning(
                    ProfileValidationConstants.MissingProfileNameTitle,
                    ProfileValidationConstants.EnterProfileNameBeforeSaving);
                _logger?.LogWarning("Profile save blocked: Profile name is empty");
                return;
            }

            var hasLaunchableContent = enabledItems.Any(c =>
                c.ContentType == ContentType.GameInstallation ||
                c.ContentType == ContentType.GameClient ||
                c.ContentType == ContentType.Executable ||
                c.ContentType == ContentType.ModdingTool);

            if (!hasLaunchableContent)
            {
                StatusMessage = "Error: A Game, Executable, or Tool must be enabled.";
                _localNotificationService.ShowError(
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.MissingLaunchableContent.Title", "Missing Launchable Content"),
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.MissingLaunchableContent.Message", "Please enable a Game, Executable, or Tool before saving."));
                _logger?.LogWarning("Profile save blocked: No launchable content enabled");
                return;
            }

            var enabledContentIds = enabledItems.Select(c => c.ManifestId.Value).ToList();

            if (_manifestPool != null)
            {
                var validationErrors = await ValidateAllDependenciesAsync(enabledContentIds);
                if (validationErrors.Count > 0)
                {
                    var errorMessage = string.Join("\n", validationErrors);
                    StatusMessage = "Error: Missing required dependencies";
                    _localNotificationService.ShowError(
                        _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.MissingDependencies.Title", "Missing Dependencies"),
                        _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.MissingDependencies.Message", $"Cannot save profile with missing dependencies:\n\n{errorMessage}", errorMessage));
                    _logger?.LogWarning("Profile save blocked: {Errors}", errorMessage);
                    return;
                }
            }

            _logger?.LogInformation(
                "Profile will be created/updated with {Count} enabled content items: {ContentIds}",
                enabledContentIds.Count,
                string.Join(", ", enabledContentIds));

            if (string.IsNullOrEmpty(CurrentProfileId))
            {
                await CreateProfileAsync(enabledContentIds, cancellationToken: default);
            }
            else
            {
                await UpdateProfileAsync(enabledContentIds, cancellationToken: default);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving profile");
            StatusMessage = "Error saving profile";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task CreateProfileAsync(List<string> enabledContentIds, CancellationToken cancellationToken = default)
    {
        if (_gameProfileManager == null)
        {
            return;
        }

        var isStandaloneProfile = ToolProfileHelper.IsToolProfile(
            EnabledContent.Where(c => c.IsEnabled).Select(c => (c.ManifestId.Value, c.ContentType)));

        var activeGameClient = isStandaloneProfile ? null : GameProfileClientResolutionHelper.ResolveActiveGameClient(EnabledContent, SelectedGameInstallation);

        var createRequest = new CreateProfileRequest
        {
            Name = Name,
            Description = Description,
            GameInstallationId = isStandaloneProfile ? null : SelectedGameInstallation?.SourceId,
            GameClientId = isStandaloneProfile ? null : (activeGameClient?.Id ?? SelectedGameInstallation?.GameClientId),
            GameClient = activeGameClient,
            WorkspaceStrategy = SelectedWorkspaceStrategy,
            EnabledContentIds = enabledContentIds,
            CommandLineArguments = CommandLineArguments,
            IconPath = IconPath,
            CoverPath = CoverPath,
            ThemeColor = ColorValue,
        };

        var gameSettings = GameSettingsViewModel.GetProfileSettings();
        PopulateGameSettings(createRequest, gameSettings);

        var result = await _gameProfileManager.CreateProfileAsync(createRequest, cancellationToken);
        if (result.Success && result.Data != null)
        {
            CurrentProfileId = result.Data.Id;

            if (GameSettingsViewModel.SaveSettingsCommand.CanExecute(null))
            {
                await GameSettingsViewModel.SaveSettingsCommand.ExecuteAsync(null);
            }

            StatusMessage = "Profile created successfully";
            _logger?.LogInformation("Created new profile {ProfileName} with {ContentCount} enabled content items", Name, enabledContentIds.Count);

            WeakReferenceMessenger.Default.Send(new ProfileCreatedMessage(result.Data));
            ExecuteCancel();
        }
        else
        {
            StatusMessage = $"Failed to create profile: {string.Join(", ", result.Errors)}";
            _logger?.LogWarning("Failed to create profile: {Errors}", string.Join(", ", result.Errors));
        }
    }

    private async Task UpdateProfileAsync(List<string> enabledContentIds, CancellationToken cancellationToken = default)
    {
        if (_gameProfileManager == null || string.IsNullOrEmpty(CurrentProfileId))
        {
            return;
        }

        var wasHotswap = IsHotswapMode;
        bool isProfileRunning = await CheckIsProfileRunningAsync();
        if (!wasHotswap && isProfileRunning)
        {
            NotifyHotswapLockedSession();
            return;
        }

        var liveGameType = SelectedGameInstallation?.GameType ?? GameTypeFilter;
        if (!await TryExecutePreSaveLiveSyncAsync(enabledContentIds, liveGameType, isProfileRunning, cancellationToken))
        {
            return;
        }

        var gameSettings = GameSettingsViewModel.GetProfileSettings();
        var updateRequest = BuildUpdateRequest(enabledContentIds, gameSettings);
        var result = await _gameProfileManager.UpdateProfileAsync(CurrentProfileId, updateRequest, cancellationToken);

        if (result.Success && result.Data != null)
        {
            var (postSyncSuccess, updatedRunningState) = await TryExecutePostSaveLiveSyncAsync(enabledContentIds, liveGameType, isProfileRunning, cancellationToken);
            if (!postSyncSuccess)
            {
                return;
            }

            await HandleProfileUpdateSuccessAsync(result, enabledContentIds, updatedRunningState);
        }
        else
        {
            await HandleProfileUpdateFailureAsync(isProfileRunning, liveGameType, result, cancellationToken);
        }
    }

    private void NotifyHotswapLockedSession()
    {
        StatusMessage = "Game session started; non-hotswappable settings are now locked";
        _localNotificationService.ShowWarning(
            _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.HotswapModeEnabled.Title", "Hotswap Mode Enabled"),
            _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.HotswapModeEnabled.Message", "The game was started while editing this profile. Non-hotswappable settings have been locked. Please review your changes and save again."));
    }

    private async Task<bool> TryExecutePreSaveLiveSyncAsync(
        List<string> enabledContentIds,
        GameType liveGameType,
        bool isProfileRunning,
        CancellationToken cancellationToken)
    {
        if (!isProfileRunning || _profileContentLinker == null || _manifestPool == null)
        {
            return true;
        }

        return await PerformLiveSyncAsync(enabledContentIds, liveGameType, cancellationToken);
    }

    private async Task<(bool Success, bool IsRunning)> TryExecutePostSaveLiveSyncAsync(
        List<string> enabledContentIds,
        GameType liveGameType,
        bool isProfileRunning,
        CancellationToken cancellationToken)
    {
        if (isProfileRunning || _profileContentLinker == null || _manifestPool == null)
        {
            return (true, isProfileRunning);
        }

        var runningNow = await CheckIsProfileRunningAsync();
        if (!runningNow)
        {
            return (true, false);
        }

        _logger?.LogInformation("Game session started during profile save for {ProfileId}; performing post-save live sync", CurrentProfileId);
        var liveSyncSuccess = await PerformLiveSyncAsync(enabledContentIds, liveGameType, cancellationToken);
        if (!liveSyncSuccess)
        {
            await HandlePostSaveLiveSyncFailureAsync(liveGameType, cancellationToken);
            return (false, true);
        }

        return (true, true);
    }

    private async Task HandlePostSaveLiveSyncFailureAsync(GameType liveGameType, CancellationToken cancellationToken)
    {
        _logger?.LogWarning("Live sync failed after profile update for {ProfileId}; rolling back persisted profile", CurrentProfileId);
        if (_originalProfile == null || string.IsNullOrEmpty(CurrentProfileId) || _gameProfileManager == null)
        {
            _logger?.LogError("Cannot roll back profile {ProfileId} after live sync failure: original profile snapshot or required manager is null", CurrentProfileId);
            StatusMessage = "Live synchronization failed and profile snapshot was missing for rollback";
            _localNotificationService.ShowError(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncFailed.Title", LiveSyncFailedTitle),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncNoSnapshot.Message", "A game session was started during save, and live synchronization failed. Profile snapshot was missing so rollback could not be performed."));
            return;
        }

        var rollbackRequest = BuildRollbackRequest(_originalProfile, _originalEnabledContentIds.ToList(), _originalGameSettings);
        var rollbackResult = await _gameProfileManager.UpdateProfileAsync(CurrentProfileId, rollbackRequest, cancellationToken);
        if (rollbackResult.Success)
        {
            await RestoreOriginalProfileStateAsync(_originalProfile);
            var userDataRollbackSuccess = await RollbackLiveUserDataAsync(liveGameType, cancellationToken);
            NotifyRollbackCompletion(userDataRollbackSuccess);
        }
        else
        {
            _logger?.LogError("Failed to roll back profile {ProfileId} after live sync failure: {Error}", CurrentProfileId, rollbackResult.FirstError);
            StatusMessage = "Live synchronization failed and profile rollback could not be persisted";
            _localNotificationService.ShowError(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncRollbackFailed.Title", "Live Sync & Rollback Failed"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncRollbackFailed.Message", $"A game session was started during save, and live synchronization failed. Furthermore, rolling back persisted profile changes failed: {rollbackResult.FirstError}. Profile and running game may be out of sync.", rollbackResult.FirstError));
        }
    }

    private async Task RestoreOriginalProfileStateAsync(GameProfile originalProfile)
    {
        ApplyLoadedProfileProperties(originalProfile);
        await GameSettingsViewModel.InitializeForProfileAsync(CurrentProfileId!, originalProfile);
        await LoadEnabledContentForProfileAsync(originalProfile);
        await LoadAvailableContentAsync();
        SelectInitialGameInstallation(originalProfile);
        UpdateAllItemsHotswapState();
        CaptureLoadedClientSelection();
    }

    private async Task<bool> RollbackLiveUserDataAsync(GameType liveGameType, CancellationToken cancellationToken)
    {
        if (_profileContentLinker == null)
        {
            return true;
        }

        var (originalManifests, missingOriginalIds) = await ResolveOriginalManifestsForRollbackAsync(cancellationToken);
        if (missingOriginalIds.Count > 0)
        {
            _logger?.LogWarning("Live user data rollback for {ProfileId} skipped due to missing manifests: {Ids}", CurrentProfileId, string.Join(", ", missingOriginalIds));
            return false;
        }

        var userDataResult = await _profileContentLinker.UpdateProfileUserDataAsync(
            CurrentProfileId!,
            originalManifests,
            liveGameType,
            cancellationToken);

        if (!userDataResult.Success)
        {
            _logger?.LogWarning("Live user data rollback for {ProfileId} failed: {Error}", CurrentProfileId, userDataResult.FirstError);
            return false;
        }

        return true;
    }

    private void NotifyRollbackCompletion(bool userDataRollbackSuccess)
    {
        if (userDataRollbackSuccess)
        {
            StatusMessage = "Live synchronization failed; profile changes were rolled back";
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncFailed.Title", LiveSyncFailedTitle),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncRolledBack.Message", "A game session was started during save, but live synchronization failed. Profile changes were rolled back to match the running game."));
        }
        else
        {
            StatusMessage = "Live synchronization failed; profile was rolled back but live user data could not be restored";
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncFailed.Title", LiveSyncFailedTitle),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveSyncUserDataNotRestored.Message", "A game session was started during save, and live synchronization failed. Profile changes were rolled back, but live user data could not be fully restored to match the running game."));
        }
    }

    private UpdateProfileRequest BuildRollbackRequest(
        GameProfile originalProfile,
        List<string> originalEnabledContentIds,
        UpdateProfileRequest? originalGameSettings)
    {
        var rollbackRequest = new UpdateProfileRequest
        {
            Name = originalProfile.Name,
            Description = originalProfile.Description,
            ThemeColor = originalProfile.ThemeColor,
            GameInstallationId = originalProfile.GameInstallationId,
            WorkspaceStrategy = originalProfile.WorkspaceStrategy,
            ClearWorkspaceStrategy = !originalProfile.WorkspaceStrategy.HasValue,
            ActiveWorkspaceId = originalProfile.ActiveWorkspaceId,
            EnabledContentIds = originalEnabledContentIds,
            CommandLineArguments = originalProfile.CommandLineArguments,
            IconPath = originalProfile.IconPath,
            CoverPath = originalProfile.CoverPath,
            GameClient = originalProfile.GameClient,
            IsRollback = true,
        };

        PopulateGameSettings(rollbackRequest, originalGameSettings);
        return rollbackRequest;
    }

    private UpdateProfileRequest BuildUpdateRequest(List<string> enabledContentIds, UpdateProfileRequest? gameSettings)
    {
        var isStandaloneProfile = ToolProfileHelper.IsToolProfile(
            EnabledContent.Where(c => c.IsEnabled).Select(c => (c.ManifestId.Value, c.ContentType)));

        var activeGameClient = isStandaloneProfile ? null : ResolveGameClientForUpdate();

        var updateRequest = new UpdateProfileRequest
        {
            Name = Name,
            Description = Description,
            ThemeColor = ColorValue,
            GameInstallationId = isStandaloneProfile ? null : SelectedGameInstallation?.SourceId,
            WorkspaceStrategy = OriginalWorkspaceStrategy.HasValue && SelectedWorkspaceStrategy != OriginalWorkspaceStrategy.Value
                ? SelectedWorkspaceStrategy
                : null,
            EnabledContentIds = enabledContentIds,
            CommandLineArguments = CommandLineArguments,
            IconPath = IconPath,
            CoverPath = CoverPath,
            GameClient = activeGameClient,
        };

        PopulateGameSettings(updateRequest, gameSettings);
        return updateRequest;
    }

    /// <summary>Preserves a stored client for unchanged selections while accepting explicit client edits.</summary>
    private GameClient? ResolveGameClientForUpdate()
    {
        if (_originalProfile?.GameClient != null &&
            !HasClientSelectionChangedSinceLoad() &&
            GetActiveClientSelection().Client?.GameClient == null)
        {
            return _originalProfile.GameClient.Clone();
        }

        return GameProfileClientResolutionHelper.ResolveActiveGameClient(EnabledContent, SelectedGameInstallation, _originalProfile?.GameClient);
    }

    private async Task HandleProfileUpdateSuccessAsync(ProfileOperationResult<GameProfile> result, List<string> enabledContentIds, bool isProfileRunning)
    {
        if (!isProfileRunning && GameSettingsViewModel.SaveSettingsCommand.CanExecute(null))
        {
            await GameSettingsViewModel.SaveSettingsCommand.ExecuteAsync(null);
        }

        if (isProfileRunning)
        {
            _localNotificationService.ShowSuccess(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveUpdateComplete.Title", "Live Update Complete"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveUpdateComplete.Message", "Content changes have been applied to the active game session."));
        }

        StatusMessage = "Profile updated successfully";
        _logger?.LogInformation("Updated profile {ProfileId} with {ContentCount} enabled content items", CurrentProfileId, enabledContentIds.Count);

        if (result.Data != null)
        {
            WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(result.Data));
        }

        ExecuteCancel();
    }

    private async Task<bool> CheckIsProfileRunningAsync()
    {
        if (string.IsNullOrEmpty(CurrentProfileId))
        {
            return false;
        }

        var isRunning = await DetermineHotswapModeAsync(CurrentProfileId);
        if (isRunning != IsHotswapMode)
        {
            IsHotswapMode = isRunning;
            UpdateAllItemsHotswapState();
        }

        return isRunning;
    }

    private async Task<bool> PerformLiveSyncAsync(
        List<string> enabledContentIds,
        GameType liveGameType,
        CancellationToken cancellationToken = default)
    {
        if (_manifestPool == null || _profileContentLinker == null || string.IsNullOrEmpty(CurrentProfileId))
        {
            return false;
        }

        var manifests = new List<ContentManifest>();
        var missingManifestIds = new List<string>();
        foreach (var id in enabledContentIds)
        {
            if (!ManifestId.TryCreate(id, out var manifestId))
            {
                missingManifestIds.Add(id);
                continue;
            }

            var manifestRes = await _manifestPool.GetManifestAsync(manifestId, cancellationToken);
            if (manifestRes.Success && manifestRes.Data != null)
            {
                manifests.Add(manifestRes.Data);
            }
            else
            {
                missingManifestIds.Add(id);
            }
        }

        if (missingManifestIds.Count > 0)
        {
            var error = _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveUpdateMissingManifests.Message", $"Cannot live-sync active session: failed to resolve manifests for {string.Join(", ", missingManifestIds)}", string.Join(", ", missingManifestIds));
            StatusMessage = error;
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveUpdateWarning.Title", "Live Update Warning"),
                error);
            _logger?.LogWarning("Profile {ProfileId} live sync aborted due to missing manifests: {Ids}", CurrentProfileId, string.Join(", ", missingManifestIds));
            return false;
        }

        var liveUpdateResult = await _profileContentLinker.UpdateProfileUserDataAsync(
            CurrentProfileId,
            manifests,
            liveGameType,
            cancellationToken);

        if (!liveUpdateResult.Success)
        {
            StatusMessage = $"Live sync failed: {liveUpdateResult.FirstError}";
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveUpdateFailed.Title", "Live Update Failed"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveUpdateFailed.Message", $"Live content synchronization failed: {liveUpdateResult.FirstError}. Profile changes were not saved.", liveUpdateResult.FirstError));
            _logger?.LogWarning("Profile {ProfileId} live sync failed: {Error}", CurrentProfileId, liveUpdateResult.FirstError);
            return false;
        }

        return true;
    }

    private async Task HandleProfileUpdateFailureAsync(
        bool isProfileRunning,
        GameType liveGameType,
        ProfileOperationResult<GameProfile> result,
        CancellationToken cancellationToken = default)
    {
        if (!isProfileRunning || _profileContentLinker == null || _manifestPool == null || string.IsNullOrEmpty(CurrentProfileId))
        {
            var errors = string.Join(", ", result.Errors);
            StatusMessage = $"Failed to update profile: {errors}";
            _logger?.LogWarning("Failed to update profile {ProfileId}: {Errors}", CurrentProfileId, errors);
            var title = GetErrorLoadingProfileTitle();
            var msgFormat = _localizationService?.GetString("GameProfiles.Notification.ProfileUpdateFailedMessage") ?? "Failed to update profile: {0}";
            _notificationService?.ShowError(title, string.Format(CultureInfo.CurrentCulture, msgFormat, errors));
            return;
        }

        var (originalManifests, missingOriginalIds) = await ResolveOriginalManifestsForRollbackAsync(cancellationToken);
        if (missingOriginalIds.Count > 0)
        {
            _logger?.LogError("Live sync rollback for profile {ProfileId} had missing original manifests: {Ids}", CurrentProfileId, string.Join(", ", missingOriginalIds));
            _localNotificationService.ShowError(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveRollbackWarning.Title", "Live Rollback Warning"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveRollbackWarning.Message", $"Profile save failed ({string.Join(", ", result.Errors)}), and original content could not be fully resolved for rollback: {string.Join(", ", missingOriginalIds)}. Live content was left as synchronized and may not match the saved profile.", string.Join(", ", result.Errors), string.Join(", ", missingOriginalIds)));
            StatusMessage = $"Failed to update profile: {string.Join(", ", result.Errors)}. Live rollback skipped: unresolved original manifests.";
            _logger?.LogWarning("Failed to update profile {ProfileId}: {Errors}", CurrentProfileId, string.Join(", ", result.Errors));
            return;
        }

        await ExecuteLiveSyncRollbackAsync(originalManifests, liveGameType, result, cancellationToken);
    }

    private async Task<(List<ContentManifest> Manifests, List<string> MissingIds)> ResolveOriginalManifestsForRollbackAsync(CancellationToken cancellationToken)
    {
        var originalManifests = new List<ContentManifest>();
        var missingOriginalIds = new List<string>();

        if (_manifestPool == null)
        {
            return (originalManifests, _originalEnabledContentIds.ToList());
        }

        foreach (var id in _originalEnabledContentIds)
        {
            if (!ManifestId.TryCreate(id, out var manifestId))
            {
                missingOriginalIds.Add(id);
                continue;
            }

            var manifestRes = await _manifestPool.GetManifestAsync(manifestId, cancellationToken);
            if (manifestRes.Success && manifestRes.Data != null)
            {
                originalManifests.Add(manifestRes.Data);
            }
            else
            {
                missingOriginalIds.Add(id);
            }
        }

        return (originalManifests, missingOriginalIds);
    }

    private async Task ExecuteLiveSyncRollbackAsync(
        List<ContentManifest> originalManifests,
        GameType liveGameType,
        ProfileOperationResult<GameProfile> result,
        CancellationToken cancellationToken)
    {
        if (_profileContentLinker == null || string.IsNullOrEmpty(CurrentProfileId))
        {
            return;
        }

        var rollbackResult = await _profileContentLinker.UpdateProfileUserDataAsync(
            CurrentProfileId,
            originalManifests,
            liveGameType,
            cancellationToken);

        if (!rollbackResult.Success)
        {
            _logger?.LogError("Failed to roll back live user data sync for profile {ProfileId}: {Error}", CurrentProfileId, rollbackResult.FirstError);
            _localNotificationService.ShowError(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveRollbackFailed.Title", "Live Rollback Failed"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.LiveRollbackFailed.Message", $"Profile save failed ({string.Join(", ", result.Errors)}), and live content rollback reported: {rollbackResult.FirstError}", string.Join(", ", result.Errors), rollbackResult.FirstError));
            StatusMessage = $"Failed to update profile: {string.Join(", ", result.Errors)}. Live rollback failed: {rollbackResult.FirstError}";
        }
        else
        {
            _logger?.LogInformation("Successfully rolled back live user data sync for profile {ProfileId}", CurrentProfileId);
            StatusMessage = $"Failed to update profile: {string.Join(", ", result.Errors)}. Live content was rolled back.";
        }
    }

    [RelayCommand]
    private void SelectIcon(ProfileResourceItem? icon)
    {
        if (icon == null) return;
        SelectedIcon = icon;
        IconPath = icon.Path;
        _isIconCustomized = true;
        _logger?.LogInformation("Selected icon: {DisplayName} ({Path})", icon.DisplayName, icon.Path);
    }

    [RelayCommand]
    private void SelectCover(ProfileResourceItem? cover)
    {
        if (cover == null) return;
        SelectedCoverItem = cover;
        CoverPath = cover.Path;
        _isCoverCustomized = true;
        _logger?.LogInformation("Selected cover: {DisplayName} ({Path})", cover.DisplayName, cover.Path);
    }

    /// <summary>
    /// Reports a newly added local content item via status and toast notification using shared localized strings.
    /// </summary>
    /// <param name="displayName">The display name of the added content item.</param>
    private void NotifyLocalContentAdded(string displayName)
    {
        var addedStatusFormat = _localizationService?.GetString("GameProfiles.Settings.LocalContent.AddedStatus") ?? "Added {0}";
        StatusMessage = string.Format(CultureInfo.CurrentCulture, addedStatusFormat, displayName);

        var addedTitle = _localizationService?.GetString("GameProfiles.Settings.LocalContent.AddedTitle") ?? "Content Added";
        var addedMessageFormat = _localizationService?.GetString("GameProfiles.Settings.LocalContent.AddedMessage") ?? "\"{0}\" has been added successfully.";
        _localNotificationService?.ShowSuccess(
             addedTitle,
             string.Format(CultureInfo.CurrentCulture, addedMessageFormat, displayName));
    }

    [RelayCommand]
    private async Task BrowseForCustomIconAsync()
    {
        try
        {
            var openFileDialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Custom Icon",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Image Files")
                    {
                        Patterns = MediaFileHelper.ImageExtensions.Select(ext => $"*{ext}").ToArray(),
                    },
                    Avalonia.Platform.Storage.FilePickerFileTypes.All,
                ],
            };

            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel != null)
            {
                var storageProvider = topLevel.StorageProvider;
                var result = await storageProvider.OpenFilePickerAsync(openFileDialog);

                if (result.Count > 0)
                {
                    var selectedFile = result[0];
                    var validatedPath = ValidateCustomImagePath(selectedFile.Path.LocalPath);
                    if (validatedPath == null)
                    {
                        return;
                    }

                    IconPath = validatedPath;
                    SelectedIcon = null;
                    _isIconCustomized = true;
                    _logger?.LogInformation("Selected custom icon: {Path}", IconPath);
                    StatusMessage = "Custom icon selected";
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error browsing for custom icon");
            StatusMessage = "Error selecting custom icon";
        }
    }

    [RelayCommand]
    private async Task BrowseForCustomCoverAsync()
    {
        try
        {
            var openFileDialog = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select Custom Cover",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Image Files")
                    {
                        Patterns = MediaFileHelper.ImageExtensions.Select(ext => $"*{ext}").ToArray(),
                    },
                    Avalonia.Platform.Storage.FilePickerFileTypes.All,
                ],
            };

            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (topLevel != null)
            {
                var storageProvider = topLevel.StorageProvider;
                var result = await storageProvider.OpenFilePickerAsync(openFileDialog);

                if (result.Count > 0)
                {
                    var selectedFile = result[0];
                    var validatedPath = ValidateCustomImagePath(selectedFile.Path.LocalPath);
                    if (validatedPath == null)
                    {
                        return;
                    }

                    CoverPath = validatedPath;
                    SelectedCoverItem = null;
                    _isCoverCustomized = true;
                    _logger?.LogInformation("Selected custom cover: {Path}", CoverPath);
                    StatusMessage = "Custom cover selected";
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error browsing for custom cover");
            StatusMessage = "Error selecting custom cover";
        }
    }

    /// <summary>
    /// Validates that a user-picked file is a supported image before it is stored as an icon or cover.
    /// </summary>
    /// <param name="localPath">The picked file path.</param>
    /// <returns>The path when it is a supported image; otherwise null after notifying the user.</returns>
    private string? ValidateCustomImagePath(string? localPath)
    {
        if (!string.IsNullOrEmpty(localPath) && MediaFileHelper.IsImageFile(localPath))
        {
            if (MediaFileHelper.HasImageContent(localPath))
            {
                return localPath;
            }

            _logger?.LogWarning("Rejected custom profile image with unreadable or mismatched content: {Path}", localPath);
        }
        else
        {
            _logger?.LogWarning("Rejected custom profile image with unsupported extension: {Path}", localPath);
        }

        INotificationService notifier = _notificationService ?? _localNotificationService;
        notifier.ShowError(
            _localizationService?.GetString(InvalidCustomImageTitleKey) ?? DefaultInvalidCustomImageTitle,
            _localizationService?.GetString(InvalidCustomImageMessageKey) ?? DefaultInvalidCustomImageMessage);
        return null;
    }

    [RelayCommand]
    private void RandomizeColor()
    {
        var colors = new List<string>
        {
            "#1976D2", "#388E3C", "#FBC02D", "#FF5722", "#7B1FA2",
            "#D32F2F", "#0097A7", "#689F38", "#AFB42B", "#0288D1",
            "#C2185B", "#512DA8",
        };

        ColorValue = colors[System.Security.Cryptography.RandomNumberGenerator.GetInt32(colors.Count)];
        _isColorCustomized = true;
        if (GameSettingsViewModel != null)
        {
            GameSettingsViewModel.ColorValue = ColorValue;
        }

        StatusMessage = $"Color randomized to {ColorValue}";
        _logger?.LogInformation("Randomized profile color to {ColorValue}", ColorValue);
    }

    [RelayCommand]
    private void SelectThemeColor(string? color)
    {
        if (!string.IsNullOrEmpty(color))
        {
            ColorValue = color;
            _isColorCustomized = true;
            if (GameSettingsViewModel != null)
            {
                GameSettingsViewModel.ColorValue = ColorValue;
            }

            StatusMessage = $"Selected theme color {color}";
            _logger?.LogInformation("Selected theme color {ColorValue}", color);
        }
        else
        {
            StatusMessage = "Invalid color selected";
            _logger?.LogWarning("Invalid color parameter passed to SelectThemeColor");
        }
    }

    [RelayCommand]
    private void BrowseCustomCover()
    {
        StatusMessage = "Browse custom cover: TODO - Implement file dialog";
        _logger?.LogInformation("BrowseCustomCoverCommand executed");
    }

    [RelayCommand]
    private void BrowseShortcutPath()
    {
        StatusMessage = "Browse shortcut path: TODO - Implement file dialog";
        _logger?.LogInformation("BrowseShortcutPathCommand executed");
    }

    [RelayCommand]
    private void SelectContentTypeFilter(ContentType? contentType)
    {
        if (contentType.HasValue && contentType.Value != SelectedContentType)
        {
            SelectedContentType = contentType.Value;
            _logger?.LogInformation("Content type filter changed to {ContentType}", contentType.Value);
        }
    }

    [RelayCommand]
    private void SelectGameTypeFilter(GameType gameType)
    {
        if (gameType != GameTypeFilter)
        {
            GameTypeFilter = gameType;
            _logger?.LogInformation("Game type filter changed to {GameType}", gameType);
        }
    }

    [RelayCommand]
    private void SelectTab(string? tabIndexStr)
    {
        if (int.TryParse(tabIndexStr, out var tabIndex))
        {
            SelectedTabIndex = tabIndex;
            _logger?.LogDebug("Tab selected: {TabIndex}", tabIndex);
            OnTabSelected(tabIndex);
        }
    }

    [RelayCommand]
    private void ExecuteCancel()
    {
        StatusMessage = "Cancelled";
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task AddLocalContentAsync(Avalonia.Controls.Window? owner)
    {
        try
        {
            if (_localContentService == null || _contentStorageService == null)
            {
                StatusMessage = "Content services unavailable";
                return;
            }

            var dialogOwner = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (dialogOwner == null) return;

            using var vm = new AddLocalContentViewModel(
                _localContentService,
                _contentStorageService,
                _genLauncherNormalizationService,
                _dialogService,
                _archivePayloadProcessor);
            var window = new Views.AddLocalContentWindow
            {
                DataContext = vm,
            };

            var result = await window.ShowDialog<bool>(dialogOwner);

            if (result && vm.CreatedContentItem != null)
            {
                var contentItem = vm.CreatedContentItem;

                if (AvailableContent.All(a => a.ManifestId.Value != contentItem.ManifestId.Value))
                {
                    AvailableContent.Add(contentItem);
                }

                _logger?.LogInformation("Added local content via dialog: {Name}", contentItem.DisplayName);

                NotifyLocalContentAdded(contentItem.DisplayName);
                await EnableContentInternal(contentItem, bypassLoadingGuard: true);

                // Refresh filters and content to ensure new type appears and list updates
                await RefreshFiltersAndContentAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error opening Add Local Content dialog");
            StatusMessage = "Error opening dialog";
        }
    }

    [RelayCommand]
    private async Task EditContentAsync(ContentDisplayItem? contentItem)
    {
        if (contentItem == null) return;

        if (contentItem.IsLocked)
        {
            StatusMessage = ContentLockedMessage;
            _logger?.LogWarning("EditContent: Cannot edit locked item {DisplayName}", contentItem.DisplayName);
            return;
        }

        try
        {
            if (_localContentService == null || _contentStorageService == null)
            {
                StatusMessage = "Content services unavailable";
                return;
            }

            var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (owner == null) return;

            using var vm = new AddLocalContentViewModel(
                _localContentService,
                _contentStorageService,
                _genLauncherNormalizationService,
                _dialogService,
                _archivePayloadProcessor);
            await vm.LoadFromManifestAsync(contentItem);

            var window = new Views.AddLocalContentWindow
            {
                DataContext = vm,
            };

            var result = await window.ShowDialog<bool>(owner);

            if (result && vm.CreatedContentItem != null)
            {
                var updatedItem = vm.CreatedContentItem;
                var oldId = contentItem.ManifestId.Value;
                var newId = updatedItem.ManifestId.Value;

                _logger?.LogInformation("Edited local content: {Name} (ID: {OldId} -> {NewId})", contentItem.DisplayName, oldId, newId);
                StatusMessage = "Content updated";
                _localNotificationService?.ShowSuccess(
                    _localizationService.GetLocalizedString("Common.Notification.ContentUpdated.Title", "Content Updated"),
                    _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ContentUpdated.Message", $"'{contentItem.DisplayName}' has been updated.", contentItem.DisplayName));

                // Architecture: Synchronize our internal collections IMMEDIATELY to avoid duplication/flicker.
                // If it was in EnabledContent, replace it with the new item (maintaining enabled state).
                var inEnabled = EnabledContent.FirstOrDefault(e => e.ManifestId.Value == oldId);
                if (inEnabled != null)
                {
                    var index = EnabledContent.IndexOf(inEnabled);
                    updatedItem.IsEnabled = true;
                    EnabledContent[index] = updatedItem;
                }

                // If it was in AvailableContent, remove the old one (the refresh below will add the new one back if appropriate).
                var inAvailable = AvailableContent.FirstOrDefault(a => a.ManifestId.Value == oldId);
                if (inAvailable != null)
                {
                    AvailableContent.Remove(inAvailable);
                }

                // If GameClient or GameInstallation ID changed and this was our selection, synchronize SelectedGameInstallation.
                if ((contentItem.ContentType == ContentType.GameClient || contentItem.ContentType == ContentType.GameInstallation) &&
                    SelectedGameInstallation != null &&
                    SelectedGameInstallation.ManifestId.Value == oldId)
                {
                    SelectedGameInstallation = updatedItem;
                    _logger?.LogInformation("Synchronized SelectedGameInstallation with newly edited {ContentType}", contentItem.ContentType);
                }

                // Reload content and filters to reflect all changes (e.g. type changes, category updates).
                await RefreshFiltersAndContentAsync();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error editing content {Name}", contentItem.DisplayName);
            StatusMessage = "Error editing content";
        }
    }

    [RelayCommand]
    private void CancelAddLocalContent()
    {
        IsAddLocalContentDialogOpen = false;
        LocalContentName = string.Empty;
        LocalContentDirectoryPath = string.Empty;
        SelectedLocalContentType = ContentType.Addon;
    }

    [RelayCommand]
    private async Task ConfirmAddLocalContentAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalContentName))
        {
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ValidationError.Title", "Validation Error"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ValidationNameRequired.Message", "Please enter a name for the content."));
            return;
        }

        if (string.IsNullOrWhiteSpace(LocalContentDirectoryPath))
        {
            _localNotificationService.ShowWarning(
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ValidationError.Title", "Validation Error"),
                _localizationService.GetLocalizedString("GameProfiles.Settings.Notification.ValidationFolderRequired.Message", "Please select a folder for the content."));
            return;
        }

        try
        {
            IsSaving = true;

            var result = await _localContentService!.AddLocalContentAsync(
                LocalContentName,
                LocalContentDirectoryPath,
                SelectedLocalContentType,
                SelectedLocalGameType);

            if (result.Success)
            {
                IsAddLocalContentDialogOpen = false;

                // Refresh filters and content to ensure new type appears and list updates
                await RefreshFiltersAndContentAsync();

                // If the added item matches current filter, ensure it's selected/visible (handled by LoadAvailableContent)
                // If the item introduced a new filter, user might want to switch to it.
                // For now, just refreshing ensures it's reachable.
            }
            else
            {
                _logger?.LogWarning("Failed to add local content: {Errors}", string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error adding local content");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ShareProfileAsync()
    {
        if (string.IsNullOrEmpty(CurrentProfileId))
        {
            var title = _localizationService?.GetString("GameProfiles.ShareDialog.Notification.CannotShareTitle") ?? "Cannot Share";
            var msg = _localizationService?.GetString("GameProfiles.ShareDialog.Notification.SaveBeforeShareMsg") ?? "Please save the profile first before sharing.";
            _localNotificationService.ShowWarning(title, msg);
            return;
        }

        var sharingService = ProfileSharingService;
        if (sharingService == null || _gameProfileManager == null)
        {
            var title = _localizationService?.GetString("GameProfiles.ShareDialog.Notification.ShareErrorTitle") ?? "Share Error";
            var msg = _localizationService?.GetString("GameProfiles.Launcher.Notify.SharingServiceUnavailable") ?? "Profile sharing service is not available.";
            _localNotificationService.ShowError(title, msg);
            return;
        }

        if (!await _shareDialogSemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            await Helpers.ProfileSharingDialogHelper.OpenShareDialogAsync(
                CurrentProfileId,
                _gameProfileManager,
                sharingService,
                _localNotificationService,
                _loggerFactory,
                _uploadHistoryService,
                _logger,
                _localizationService);
        }
        finally
        {
            _shareDialogSemaphore.Release();
        }
    }
}
