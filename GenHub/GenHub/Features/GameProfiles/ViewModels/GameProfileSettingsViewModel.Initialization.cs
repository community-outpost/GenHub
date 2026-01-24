using System;
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
    public async Task InitializeForNewProfileAsync()
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
    public async Task InitializeForProfileAsync(string profileId)
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
}
