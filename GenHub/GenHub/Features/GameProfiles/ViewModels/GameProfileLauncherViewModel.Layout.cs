using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Choice between the profile library and the profile card gallery.
/// </summary>
public partial class GameProfileLauncherViewModel
{
    private readonly SemaphoreSlim _layoutPersistLock = new(1, 1);

    private ObservableCollection<ProfileLayoutOption>? _availableProfileLayouts;

    private bool _isLoadingProfileLayout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryLayout))]
    [NotifyPropertyChangedFor(nameof(IsCardLayout))]
    [NotifyPropertyChangedFor(nameof(SelectedProfileLayoutOption))]
    private ProfileLayout _profileLayout = ProfileLayout.Library;

    /// <summary>
    /// Represents a selectable profile layout.
    /// </summary>
    /// <param name="Layout">The layout value.</param>
    /// <param name="DisplayName">The localized layout name.</param>
    public record ProfileLayoutOption(ProfileLayout Layout, string DisplayName);

    /// <summary>Gets a value indicating whether profiles are shown as a searchable library.</summary>
    public bool IsLibraryLayout => ProfileLayout == ProfileLayout.Library;

    /// <summary>Gets a value indicating whether profiles are shown as a card gallery.</summary>
    public bool IsCardLayout => ProfileLayout == ProfileLayout.Cards;

    /// <summary>Gets the layouts offered in settings, with localized names.</summary>
    public ObservableCollection<ProfileLayoutOption> AvailableProfileLayouts => _availableProfileLayouts ??= [.. CreateProfileLayoutOptions()];

    /// <summary>
    /// Gets or sets the selected layout option. Null values from a refreshing list are ignored.
    /// </summary>
    public ProfileLayoutOption? SelectedProfileLayoutOption
    {
        get => AvailableProfileLayouts.FirstOrDefault(option => option.Layout == ProfileLayout);
        set
        {
            if (value != null)
            {
                ProfileLayout = value.Layout;
            }
        }
    }

    partial void OnProfileLayoutChanged(ProfileLayout value)
    {
        if (!_isLoadingProfileLayout && userSettingsService != null)
        {
            _ = PersistProfileLayoutAsync(userSettingsService, value);
        }
    }

    private void LoadProfileLayout(IUserSettingsService settingsService)
    {
        try
        {
            _isLoadingProfileLayout = true;
            var savedLayout = settingsService.Get().ProfileLayout;
            if (!Enum.IsDefined(savedLayout))
            {
                logger.LogWarning("Ignoring out-of-range saved profile layout {Layout}", (int)savedLayout);
                savedLayout = ProfileLayout.Library;
            }

            ProfileLayout = savedLayout;
        }
        finally
        {
            _isLoadingProfileLayout = false;
        }
    }

    private void RefreshProfileLayoutOptions()
    {
        if (_availableProfileLayouts == null)
        {
            return;
        }

        var options = CreateProfileLayoutOptions();
        for (var i = 0; i < options.Length; i++)
        {
            _availableProfileLayouts[i] = options[i];
        }

        OnPropertyChanged(nameof(SelectedProfileLayoutOption));
    }

    private ProfileLayoutOption[] CreateProfileLayoutOptions() =>
    [
        new ProfileLayoutOption(ProfileLayout.Library, localizationService["GameProfiles.Layout.Library"]),
        new ProfileLayoutOption(ProfileLayout.Cards, localizationService["GameProfiles.Layout.Cards"]),
    ];

    private async Task PersistProfileLayoutAsync(IUserSettingsService settingsService, ProfileLayout layout)
    {
        await _layoutPersistLock.WaitAsync();
        try
        {
            if (ProfileLayout != layout)
            {
                return;
            }

            var saved = await settingsService.TryUpdateAndSaveAsync(settings =>
            {
                settings.ProfileLayout = layout;
                return true;
            });
            if (!saved)
            {
                logger.LogWarning("Failed to persist profile layout setting");
            }
        }
        finally
        {
            _layoutPersistLock.Release();
        }
    }
}
