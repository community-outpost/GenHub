using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

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
    /// Loads the available content items based on current filters.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    protected virtual async Task LoadAvailableContentAsync()
    {
        try
        {
            IsLoadingContent = true;
            StatusMessage = "Loading content...";

            await RefreshHotswapStateAsync();

            AvailableContent.Clear();

            var enabledContentIds = EnabledContent.Select(e => e.ManifestId.Value).ToList();

            var coreAvailableInstallations = new List<Core.Models.Content.ContentDisplayItem>();
            foreach (var vmItem in AvailableGameInstallations)
            {
                coreAvailableInstallations.Add(new Core.Models.Content.ContentDisplayItem
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
                    IsEnabled = vmItem.IsEnabled,
                });
            }

            if (_profileContentLoader == null)
            {
                StatusMessage = "Content loader unavailable";
                return;
            }

            var coreItems = await _profileContentLoader.LoadAvailableContentAsync(
                SelectedContentType,
                new ObservableCollection<Core.Models.Content.ContentDisplayItem>(coreAvailableInstallations),
                enabledContentIds);

            foreach (var coreItem in coreItems)
            {
                try
                {
                    if (enabledContentIds.Contains(coreItem.ManifestId))
                    {
                        continue;
                    }

                    if (coreItem.GameType != GameTypeFilter)
                    {
                        continue;
                    }

                    var viewModelItem = ConvertToViewModelContentDisplayItem(coreItem);
                    AvailableContent.Add(viewModelItem);
                }
                catch (ArgumentException argEx)
                {
                    _logger?.LogWarning("Skipping invalid content item {DisplayName} (ID: {Id}): {Message}", coreItem.DisplayName, coreItem.ManifestId, argEx.Message);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error converting content item {DisplayName}", coreItem.DisplayName);
                }
            }

            StatusMessage = $"Loaded {AvailableContent.Count} {SelectedContentType} items";
            _logger?.LogInformation("Loaded {Count} content items for content type {ContentType}", AvailableContent.Count, SelectedContentType);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading available content");
            StatusMessage = "Error loading content";
        }
        finally
        {
            IsLoadingContent = false;
        }
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
        SelectedContentEditorCategory = category;

        var sectionName = category.ToString() + "Section";
        ScrollToSectionRequested?.Invoke(sectionName);
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

    [RelayCommand]
    private async Task DisableContentAsync(ContentDisplayItem? contentItem)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            _logger?.LogWarning("DisableContent: contentItem parameter is null");
            return;
        }

        if (contentItem.IsLocked)
        {
            StatusMessage = "This content item is locked and cannot be modified";
            _logger?.LogWarning("DisableContent: Cannot disable locked item {DisplayName}", contentItem.DisplayName);
            _localNotificationService.ShowWarning("Content Locked", $"'{contentItem.DisplayName}' is locked and cannot be modified while the game is running.");
            return;
        }

        if (!contentItem.CanToggle)
        {
            StatusMessage = "This content item cannot be toggled";
            _logger?.LogWarning("DisableContent: Cannot disable non-toggleable item {DisplayName}", contentItem.DisplayName);
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

            if (itemToRemove.ContentType == SelectedContentType && itemToRemove.GameType == GameTypeFilter)
            {
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

            if (itemToRemove.ContentType == ContentType.GameInstallation &&
                SelectedGameInstallation?.ManifestId.Value == itemToRemove.ManifestId.Value)
            {
                SelectedGameInstallation = null;
                _logger?.LogInformation("Cleared SelectedGameInstallation");
            }

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

    [RelayCommand]
    private async Task DeleteContentAsync(ContentDisplayItem? contentItem)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            _logger?.LogWarning("DeleteContent: contentItem parameter is null");
            return;
        }

        if (contentItem.IsLocked)
        {
            StatusMessage = "This content item is locked and cannot be modified";
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
                    "Service Unavailable",
                    "Content deletion service is not available.");
                return;
            }

            _logger?.LogInformation("Attempting to delete content: {ContentName}", contentItem.DisplayName);

            var result = await _localContentService.DeleteLocalContentAsync(contentItem.ManifestId.Value);

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
                    "Content Deleted",
                    $"'{contentItem.DisplayName}' has been permanently deleted.");
                _logger?.LogInformation("Successfully deleted content: {ContentName}", contentItem.DisplayName);
            }
            else
            {
                StatusMessage = $"Failed to delete {contentItem.DisplayName}";
                _localNotificationService.ShowError(
                    "Delete Failed",
                    $"Failed to delete '{contentItem.DisplayName}': {string.Join(", ", result.Errors)}");
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
                "Delete Error",
                $"An error occurred while deleting '{contentItem.DisplayName}'.");
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            IsSaving = true;
            StatusMessage = "Saving profile...";

            if (_gameProfileManager == null)
            {
                StatusMessage = "Profile manager not available";
                return;
            }

            if (SelectedGameInstallation == null)
            {
                StatusMessage = "Please select a game installation";
                return;
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                StatusMessage = "Please enter a profile name";
                return;
            }

            var hasLaunchableContent = EnabledContent.Any(c =>
                c.IsEnabled &&
                (c.ContentType == ContentType.GameInstallation ||
                 c.ContentType == ContentType.GameClient ||
                 c.ContentType == ContentType.Executable ||
                 c.ContentType == ContentType.ModdingTool));

            if (!hasLaunchableContent)
            {
                StatusMessage = "Error: A Game, Executable, or Tool must be enabled.";
                _localNotificationService.ShowError(
                    "Missing Launchable Content",
                    "Please enable a Game, Executable, or Tool before saving.");
                _logger?.LogWarning("Profile save blocked: No launchable content enabled");
                return;
            }

            var enabledContentIds = EnabledContent.Where(c => c.IsEnabled).Select(c => c.ManifestId.Value).ToList();

            if (SelectedGameInstallation != null)
            {
                if (!string.IsNullOrEmpty(SelectedGameInstallation.GameClientId) &&
                    !enabledContentIds.Contains(SelectedGameInstallation.GameClientId, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Add(SelectedGameInstallation.GameClientId);
                }

                if (!string.IsNullOrEmpty(SelectedGameInstallation.ManifestId.Value) &&
                    !enabledContentIds.Contains(SelectedGameInstallation.ManifestId.Value, StringComparer.OrdinalIgnoreCase))
                {
                    enabledContentIds.Add(SelectedGameInstallation.ManifestId.Value);
                }
            }

            if (_manifestPool != null)
            {
                var validationErrors = await ValidateAllDependenciesAsync(enabledContentIds);
                if (validationErrors.Count > 0)
                {
                    var errorMessage = string.Join("\n", validationErrors);
                    StatusMessage = "Error: Missing required dependencies";
                    _localNotificationService.ShowError(
                        "Missing Dependencies",
                        $"Cannot save profile with missing dependencies:\n\n{errorMessage}");
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

        var createRequest = new CreateProfileRequest
        {
            Name = Name,
            Description = Description,
            GameInstallationId = SelectedGameInstallation?.SourceId,
            GameClientId = SelectedGameInstallation?.GameClientId,
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
            "Hotswap Mode Enabled",
            "The game was started while editing this profile. Non-hotswappable settings have been locked. Please review your changes and save again.");
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
                "Live Sync Failed",
                "A game session was started during save, and live synchronization failed. Profile snapshot was missing so rollback could not be performed.");
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
                "Live Sync & Rollback Failed",
                $"A game session was started during save, and live synchronization failed. Furthermore, rolling back persisted profile changes failed: {rollbackResult.FirstError}. Profile and running game may be out of sync.");
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
                "Live Sync Failed",
                "A game session was started during save, but live synchronization failed. Profile changes were rolled back to match the running game.");
        }
        else
        {
            StatusMessage = "Live synchronization failed; profile was rolled back but live user data could not be restored";
            _localNotificationService.ShowWarning(
                "Live Sync Failed",
                "A game session was started during save, and live synchronization failed. Profile changes were rolled back, but live user data could not be fully restored to match the running game.");
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
        var updateRequest = new UpdateProfileRequest
        {
            Name = Name,
            Description = Description,
            ThemeColor = ColorValue,
            GameInstallationId = SelectedGameInstallation?.SourceId,
            WorkspaceStrategy = OriginalWorkspaceStrategy.HasValue && SelectedWorkspaceStrategy != OriginalWorkspaceStrategy.Value
                ? SelectedWorkspaceStrategy
                : null,
            EnabledContentIds = enabledContentIds,
            CommandLineArguments = CommandLineArguments,
            IconPath = IconPath,
            CoverPath = CoverPath,
        };

        PopulateGameSettings(updateRequest, gameSettings);
        return updateRequest;
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
                "Live Update Complete",
                "Content changes have been applied to the active game session.");
        }

        StatusMessage = "Profile updated successfully";
        _logger?.LogInformation("Updated profile {ProfileId} with {ContentCount} enabled content items", CurrentProfileId, enabledContentIds.Count);

        WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(result.Data));
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
            var error = $"Cannot live-sync active session: failed to resolve manifests for {string.Join(", ", missingManifestIds)}";
            StatusMessage = error;
            _localNotificationService.ShowWarning("Live Update Warning", error);
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
                "Live Update Failed",
                $"Live content synchronization failed: {liveUpdateResult.FirstError}. Profile changes were not saved.");
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
            StatusMessage = $"Failed to update profile: {string.Join(", ", result.Errors)}";
            _logger?.LogWarning("Failed to update profile {ProfileId}: {Errors}", CurrentProfileId, string.Join(", ", result.Errors));
            return;
        }

        var (originalManifests, missingOriginalIds) = await ResolveOriginalManifestsForRollbackAsync(cancellationToken);
        if (missingOriginalIds.Count > 0)
        {
            _logger?.LogError("Live sync rollback for profile {ProfileId} had missing original manifests: {Ids}", CurrentProfileId, string.Join(", ", missingOriginalIds));
            _localNotificationService.ShowError(
                "Live Rollback Warning",
                $"Profile save failed ({string.Join(", ", result.Errors)}), and original content could not be fully resolved for rollback: {string.Join(", ", missingOriginalIds)}. Live content was left as synchronized and may not match the saved profile.");
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
                "Live Rollback Failed",
                $"Profile save failed ({string.Join(", ", result.Errors)}), and live content rollback reported: {rollbackResult.FirstError}");
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
        _logger?.LogInformation("Selected icon: {DisplayName} ({Path})", icon.DisplayName, icon.Path);
    }

    [RelayCommand]
    private void SelectCover(ProfileResourceItem? cover)
    {
        if (cover == null) return;
        SelectedCoverItem = cover;
        CoverPath = cover.Path;
        _logger?.LogInformation("Selected cover: {DisplayName} ({Path})", cover.DisplayName, cover.Path);
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
                        Patterns = [ "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.ico" ],
                    },
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
                    IconPath = selectedFile.Path.LocalPath;
                    SelectedIcon = null;
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
                        Patterns = [ "*.png", "*.jpg", "*.jpeg", "*.bmp" ],
                    },
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
                    CoverPath = selectedFile.Path.LocalPath;
                    SelectedCoverItem = null;
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
        }
    }

    [RelayCommand]
    private void ExecuteCancel()
    {
        StatusMessage = "Cancelled";
        CloseRequested?.Invoke(this, EventArgs.Empty);
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
                _archivePayloadProcessor,
                null);
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

                StatusMessage = $"Added {contentItem.DisplayName}";
                await EnableContentInternal(contentItem, bypassLoadingGuard: true);

                // Refresh filters and content to ensure new type appears and list updates
                await RefreshFiltersAndContentAsync();

                _localNotificationService?.ShowSuccess(
                     "Content Added",
                     $"'{contentItem.DisplayName}' has been added successfully.");
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
            StatusMessage = "This content item is locked and cannot be modified";
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
                _archivePayloadProcessor,
                null);
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
                _localNotificationService?.ShowSuccess("Content Updated", $"'{contentItem.DisplayName}' has been updated.");

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
            _localNotificationService.ShowWarning("Validation Error", "Please enter a name for the content.");
            return;
        }

        if (string.IsNullOrWhiteSpace(LocalContentDirectoryPath))
        {
             _localNotificationService.ShowWarning("Validation Error", "Please select a folder for the content.");
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
}
