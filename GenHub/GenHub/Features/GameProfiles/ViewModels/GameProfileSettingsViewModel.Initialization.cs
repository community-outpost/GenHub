using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Initialization logic for the GameProfileSettingsViewModel.
/// </summary>
public partial class GameProfileSettingsViewModel
{
    /// <summary>
    /// Initializes the view model for creating a new profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public virtual async Task InitializeForNewProfileAsync()
    {
        try
        {
            IsInitializing = true;
            LoadingError = false;
            StatusMessage = "Loading available content...";

            if (!_hasShownFirstLoadNotification)
            {
                _notificationService?.ShowInfo("Loading Resources", "Initializing game content cache for the first time...", 3000);
                _hasShownFirstLoadNotification = true;
            }

            _currentProfileId = null;
            Name = "New Profile";
            Description = "A new game profile";
            ColorValue = "#1976D2";
            SelectedWorkspaceStrategy = GetDefaultWorkspaceStrategy();
            SelectedContentType = ContentType.GameClient;

            EnabledContent.Clear();

            await LoadAvailableGameInstallationsAsync();
            await LoadAvailableContentAsync();
            await RefreshVisibleFiltersAsync();

            if (AvailableGameInstallations.Any())
            {
                SelectedGameInstallation = AvailableGameInstallations
                    .OrderByDescending(i => i.GameType == Core.Models.Enums.GameType.ZeroHour)
                    .First();

                IconPath = NormalizeResourcePath(
                    _profileResourceService?.GetDefaultIconPath(SelectedGameInstallation.GameType.ToString()),
                    Core.Constants.UriConstants.DefaultIconUri);
                CoverPath = NormalizeResourcePath(
                    _profileResourceService?.GetDefaultCoverPath(SelectedGameInstallation.GameType.ToString()),
                    string.Empty);

                LoadAvailableIconsAndCovers(SelectedGameInstallation.GameType.ToString());
                GameTypeFilter = SelectedGameInstallation.GameType;
            }

            GameSettingsViewModel.ColorValue = ColorValue;

            if (_gameSettingsService != null)
            {
                try
                {
                    var existingGoSettings = await _gameSettingsService.LoadGeneralsOnlineSettingsAsync();
                    if (existingGoSettings.Success && existingGoSettings.Data != null)
                    {
                        _logger?.LogInformation("Pre-loading existing GeneralsOnline settings for new profile");
                        var data = existingGoSettings.Data;
                        var tempProfile = new GameProfile { Id = "temp_new" };
                        GameSettingsMapper.ApplyFromGeneralsOnlineSettings(data, tempProfile);
                        await GameSettingsViewModel.InitializeForProfileAsync(null, tempProfile);
                    }
                    else
                    {
                        await GameSettingsViewModel.InitializeForProfileAsync(null, null);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to pre-load existing settings for new profile, using defaults");
                    await GameSettingsViewModel.InitializeForProfileAsync(null, null);
                }
            }
            else
            {
                await GameSettingsViewModel.InitializeForProfileAsync(null, null);
            }

            if (SelectedGameInstallation != null)
            {
                GameSettingsViewModel.SelectedGameType = SelectedGameInstallation.GameType;
            }

            StatusMessage = $"Found {AvailableGameInstallations.Count} installations and {AvailableContent.Count} content items";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing new profile");
            StatusMessage = "Error loading content";
            LoadingError = true;
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Initializes the view model for editing an existing profile.
    /// </summary>
    /// <param name="profileId">The ID of the profile to load.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public virtual async Task InitializeForProfileAsync(string profileId)
    {
        try
        {
            IsInitializing = true;
            LoadingError = false;
            StatusMessage = "Loading profile...";

            if (!_hasShownFirstLoadNotification)
            {
                _notificationService?.ShowInfo("Loading Resources", "Initializing game content cache for the first time...", 3000);
                _hasShownFirstLoadNotification = true;
            }

            _currentProfileId = profileId;
            _logger?.LogInformation("InitializeForProfileAsync called with profileId: {ProfileId}", profileId);

            var profileResult = await _gameProfileManager!.GetProfileAsync(profileId);
            if (!profileResult.Success || profileResult.Data == null)
            {
                _logger?.LogWarning("Failed to load profile {ProfileId}: {Errors}", profileId, string.Join(", ", profileResult.Errors));
                StatusMessage = "Failed to load profile";
                LoadingError = true;
                return;
            }

            var profile = profileResult.Data;
            Name = profile.Name;
            Description = profile.Description ?? string.Empty;
            ColorValue = profile.ThemeColor ?? "#1976D2";
            var defaultIconPath = _profileResourceService?.GetDefaultIconPath(profile.GameClient.GameType.ToString())
                ?? Core.Constants.UriConstants.DefaultIconUri;
            IconPath = NormalizeResourcePath(profile.IconPath, defaultIconPath);
            var defaultCoverPath = _profileResourceService?.GetDefaultCoverPath(profile.GameClient.GameType.ToString()) ?? string.Empty;
            CoverPath = NormalizeResourcePath(profile.CoverPath, defaultCoverPath);
            SelectedWorkspaceStrategy = profile.WorkspaceStrategy;
            _originalWorkspaceStrategy = profile.WorkspaceStrategy;
            CommandLineArguments = profile.CommandLineArguments ?? string.Empty;

            LoadAvailableIconsAndCovers(profile.GameClient.GameType.ToString());
            GameTypeFilter = profile.GameClient.GameType;

            GameSettingsViewModel.ColorValue = ColorValue;
            await GameSettingsViewModel.InitializeForProfileAsync(profileId, profile);

            if (!profile.HasCustomSettings())
            {
                var gameSettings = GameSettingsViewModel.GetProfileSettings();
                var updateRequest = new UpdateProfileRequest();
                PopulateGameSettings(updateRequest, gameSettings);

                var updateResult = await _gameProfileManager.UpdateProfileAsync(profileId, updateRequest);
                if (updateResult.Success)
                {
                    _logger?.LogInformation("Saved default game settings for profile {ProfileId}", profileId);
                }
            }

            await LoadEnabledContentForProfileAsync(profile);
            await LoadAvailableGameInstallationsAsync();
            await LoadAvailableContentAsync();
            await RefreshVisibleFiltersAsync();

            var enabledInstallation = EnabledContent.FirstOrDefault(c => c.ContentType == Core.Models.Enums.ContentType.GameInstallation);
            if (enabledInstallation != null)
            {
                SelectedGameInstallation = AvailableGameInstallations
                    .FirstOrDefault(a => a.ManifestId.Value == enabledInstallation.ManifestId.Value)
                    ?? enabledInstallation;
            }

            StatusMessage = $"Profile loaded with {EnabledContent.Count} enabled content items";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error initializing profile {ProfileId}", profileId);
            StatusMessage = "Error loading profile";
            LoadingError = true;
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Refreshes the list of visible content filters based on available content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public virtual async Task RefreshVisibleFiltersAsync()
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
            AddFilterIfAvailable(ContentType.Map, "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z");
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
}
