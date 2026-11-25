using CommunityToolkit.Mvvm.Input;

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

    [RelayCommand]
    private void SelectPage(ProfileSettingsPage page)
    {
        SelectedPage = page;
    }

    private void ApplySelectedPage(ProfileSettingsPage page)
    {
        if (page == ProfileSettingsPage.GameSettings)
        {
            GameSettingsViewModel.SearchQuery = string.Empty;
        }

        _isSyncingPage = true;
        try
        {
            SelectedTabIndex = GetTabIndex(page);
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
            _ => ProfileSettingsPage.GameSettings,
        };

        if (page == ProfileSettingsPage.GameSettings && _selectedPage != ProfileSettingsPage.GameSettings)
        {
            GameSettingsViewModel.SearchQuery = string.Empty;
        }

        SetProperty(ref _selectedPage, page, nameof(SelectedPage));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        SyncPageFromState();
    }
}
