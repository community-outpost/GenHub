using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
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
    private void ScrollToSection(string sectionName)
    {
        ScrollToSectionRequested?.Invoke(sectionName);
    }

    [RelayCommand]
    private async Task LoadAvailableContentAsync()
    {
        try
        {
            IsLoadingContent = true;
            StatusMessage = "Loading content...";
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

            var coreItems = await _profileContentLoader!.LoadAvailableContentAsync(
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
    private async Task EnableContent(ContentDisplayItem? contentItem)
    {
        await EnableContentInternal(contentItem, bypassLoadingGuard: false);
    }

    [RelayCommand]
    private async Task DisableContent(ContentDisplayItem? contentItem)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            _logger?.LogWarning("DisableContent: contentItem parameter is null");
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
    private async Task DeleteContent(ContentDisplayItem? contentItem)
    {
        if (contentItem == null)
        {
            StatusMessage = "No content selected";
            _logger?.LogWarning("DeleteContent: contentItem parameter is null");
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

            if (string.IsNullOrEmpty(_currentProfileId))
            {
                var createRequest = new CreateProfileRequest
                {
                    Name = Name,
                    Description = Description,
                    GameInstallationId = SelectedGameInstallation.SourceId,
                    GameClientId = SelectedGameInstallation.GameClientId,
                    PreferredStrategy = SelectedWorkspaceStrategy,
                    EnabledContentIds = enabledContentIds,
                    CommandLineArguments = CommandLineArguments,
                    IconPath = IconPath,
                    CoverPath = CoverPath,
                    ThemeColor = ColorValue,
                };

                var gameSettings = GameSettingsViewModel.GetProfileSettings();
                PopulateGameSettings(createRequest, gameSettings);

                var result = await _gameProfileManager.CreateProfileAsync(createRequest);
                if (result.Success && result.Data != null)
                {
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
            else
            {
                var gameSettings = GameSettingsViewModel.GetProfileSettings();

                var updateRequest = new UpdateProfileRequest
                {
                    Name = Name,
                    Description = Description,
                    ThemeColor = ColorValue,
                    GameInstallationId = SelectedGameInstallation?.SourceId,

                    PreferredStrategy = _originalWorkspaceStrategy.HasValue && SelectedWorkspaceStrategy != _originalWorkspaceStrategy.Value
                        ? SelectedWorkspaceStrategy
                        : null,
                    EnabledContentIds = enabledContentIds,
                    CommandLineArguments = CommandLineArguments,
                    IconPath = IconPath,
                    CoverPath = CoverPath,
                };

                PopulateGameSettings(updateRequest, gameSettings);

                var result = await _gameProfileManager.UpdateProfileAsync(_currentProfileId, updateRequest);
                if (result.Success && result.Data != null)
                {
                    if (GameSettingsViewModel.SaveSettingsCommand.CanExecute(null))
                    {
                        await GameSettingsViewModel.SaveSettingsCommand.ExecuteAsync(null);
                    }

                    StatusMessage = "Profile updated successfully";
                    _logger?.LogInformation("Updated profile {ProfileId} with {ContentCount} enabled content items", _currentProfileId, enabledContentIds.Count);

                    WeakReferenceMessenger.Default.Send(new ProfileUpdatedMessage(result.Data));

                    ExecuteCancel();
                }
                else
                {
                    StatusMessage = $"Failed to update profile: {string.Join(", ", result.Errors)}";
                    _logger?.LogWarning("Failed to update profile {ProfileId}: {Errors}", _currentProfileId, string.Join(", ", result.Errors));
                }
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

        var random = new Random();
        ColorValue = colors[random.Next(colors.Count)];
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
            if (_localContentService == null)
            {
                StatusMessage = "Local content service unavailable";
                return;
            }

            var dialogOwner = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (dialogOwner == null) return;

            var vm = new AddLocalContentViewModel(_localContentService, null);
            var window = new Views.AddLocalContentWindow
            {
                DataContext = vm,
            };

            var result = await window.ShowDialog<bool>(dialogOwner);

            if (result && vm.CreatedContentItem != null)
            {
                var contentItem = vm.CreatedContentItem;

                if (!AvailableContent.Any(a => a.ManifestId.Value == contentItem.ManifestId.Value))
                {
                    AvailableContent.Add(contentItem);
                }

                _logger?.LogInformation("Added local content via dialog: {Name}", contentItem.DisplayName);

                StatusMessage = $"Added {contentItem.DisplayName}";
                await EnableContentInternal(contentItem, bypassLoadingGuard: true);

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
    private void CancelAddLocalContent()
    {
        IsAddLocalContentDialogOpen = false;
        LocalContentName = string.Empty;
        LocalContentDirectoryPath = string.Empty;
        SelectedLocalContentType = ContentType.Addon;
    }

    [RelayCommand]
    private async Task ConfirmAddLocalContent()
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
                 await LoadAvailableContentAsync();
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
