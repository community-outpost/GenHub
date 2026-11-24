using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Sidebar page navigation for the GameProfileSettingsViewModel.
/// </summary>
public partial class GameProfileSettingsViewModel
{
    private const int ContentTabIndex = 0;
    private const int ProfileTabIndex = 1;
    private const int GameSettingsTabIndex = 2;

    private ProfileSettingsPage _selectedPage = ProfileSettingsPage.General;
    private bool _isSyncingPage;

    /// <summary>
    /// Gets or sets the page shown in the profile settings window.
    /// </summary>
    public ProfileSettingsPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                ApplySelectedPage(value);
            }
        }
    }

    private static int GetTabIndex(ProfileSettingsPage page) => page switch
    {
        ProfileSettingsPage.Game or ProfileSettingsPage.Content => ContentTabIndex,
        ProfileSettingsPage.General or ProfileSettingsPage.Appearance => ProfileTabIndex,
        _ => GameSettingsTabIndex,
    };

    private static ProfileSettingsPage GetPage(SettingsCategory category) => category switch
    {
        SettingsCategory.Audio => ProfileSettingsPage.Audio,
        SettingsCategory.Controls => ProfileSettingsPage.Controls,
        SettingsCategory.TheSuperHackers => ProfileSettingsPage.TheSuperHackers,
        SettingsCategory.GeneralsOnline => ProfileSettingsPage.GeneralsOnline,
        _ => ProfileSettingsPage.Video,
    };

    private static SettingsCategory? GetCategory(ProfileSettingsPage page) => page switch
    {
        ProfileSettingsPage.Video => SettingsCategory.Video,
        ProfileSettingsPage.Audio => SettingsCategory.Audio,
        ProfileSettingsPage.Controls => SettingsCategory.Controls,
        ProfileSettingsPage.TheSuperHackers => SettingsCategory.TheSuperHackers,
        ProfileSettingsPage.GeneralsOnline => SettingsCategory.GeneralsOnline,
        _ => null,
    };

    [RelayCommand]
    private void SelectPage(ProfileSettingsPage page)
    {
        SelectedPage = page;
    }

    private void ApplySelectedPage(ProfileSettingsPage page)
    {
        _isSyncingPage = true;
        try
        {
            SelectedTabIndex = GetTabIndex(page);
            if (page == ProfileSettingsPage.General)
            {
                SelectedGeneralCategory = GeneralSettingsCategory.Identity;
            }
            else if (page == ProfileSettingsPage.Appearance)
            {
                SelectedGeneralCategory = GeneralSettingsCategory.Appearance;
            }

            if (GetCategory(page) is { } category)
            {
                GameSettingsViewModel.SelectedCategory = category;
            }
        }
        finally
        {
            _isSyncingPage = false;
        }

        OnTabSelected(SelectedTabIndex);
    }

    private void SyncPageFromState()
    {
        if (_isSyncingPage)
        {
            return;
        }

        var page = SelectedTabIndex switch
        {
            ContentTabIndex => ProfileSettingsPage.Content,
            ProfileTabIndex => ProfileSettingsPage.General,
            _ => GetPage(GameSettingsViewModel.SelectedCategory),
        };

        SetProperty(ref _selectedPage, page, nameof(SelectedPage));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        SyncPageFromState();
    }

    private void OnGameSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameSettingsViewModel.SelectedCategory) && SelectedTabIndex == GameSettingsTabIndex)
        {
            SyncPageFromState();
        }
    }
}
