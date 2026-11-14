using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.GameProfile;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Search and selected-profile presentation for the profile library.
/// </summary>
public partial class GameProfileLauncherViewModel
{
    private readonly HashSet<GameProfileItemViewModel> _librarySubscriptions = [];
    private GameProfileItemViewModel? _displayedLibraryProfile;
    private int _libraryContentRevision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryEmpty))]
    private bool _isLibraryInitializing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryEmpty))]
    private bool _hasLibraryLoadError;

    [ObservableProperty]
    private string _profileSearchText = string.Empty;

    [ObservableProperty]
    private string _selectedLibraryTab = "Overview";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryContentEmpty))]
    private bool _isLibraryContentLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryContentEmpty))]
    private bool _hasLibraryContentError;

    /// <summary>Gets a value indicating whether the library has loaded without any profiles.</summary>
    public bool IsLibraryEmpty => !HasDisplayedProfile && !IsLibraryInitializing && !HasLibraryLoadError;

    /// <summary>Gets a value indicating whether no content rows are available.</summary>
    public bool IsLibraryContentEmpty => !IsLibraryContentLoading && !HasLibraryContentError && LibraryContent.Count == 0;

    /// <summary>Gets the enabled content count in a readable caption.</summary>
    public string LibraryContentSummary => localizationService.GetString("GameProfiles.Library.ContentCount", DisplayedProfile?.LibraryContentCount ?? 0);

    /// <summary>Gets the total profile count in a localized caption.</summary>
    public string LibraryProfileCount => localizationService.GetString("GameProfiles.Library.ProfileCount", ProfileCount);

    /// <summary>Gets profiles matching the search in the user's existing sort order.</summary>
    public ObservableCollection<GameProfileItemViewModel> LibraryProfiles { get; } = [];

    /// <summary>Gets the enabled content of the currently displayed profile.</summary>
    public ObservableCollection<Core.Models.Content.ContentDisplayItem> LibraryContent { get; } = [];

    /// <summary>Gets the number of real profiles, excluding the former Add card.</summary>
    public int ProfileCount => Profiles.Count(p => p is not AddProfileItemViewModel);

    /// <summary>Gets a value indicating whether the search matches any profiles.</summary>
    public bool HasLibraryProfiles => LibraryProfiles.Count > 0;

    /// <summary>Gets a value indicating whether a profile is available for the detail pane.</summary>
    public bool HasDisplayedProfile => DisplayedProfile != null;

    /// <summary>Gets a value indicating whether the overview is selected.</summary>
    public bool IsLibraryOverview => SelectedLibraryTab == "Overview";

    /// <summary>Gets a value indicating whether the content tab is selected.</summary>
    public bool IsLibraryContent => SelectedLibraryTab == "Content";

    /// <summary>Gets a value indicating whether the settings tab is selected.</summary>
    public bool IsLibrarySettings => SelectedLibraryTab == "Settings";

    /// <summary>
    /// Gets or sets the displayed profile. A default preview does not overwrite the
    /// remembered selection; null changes from filtering the list are ignored.
    /// </summary>
    public GameProfileItemViewModel? DisplayedProfile
    {
        get => SelectedProfile is not null and not AddProfileItemViewModel && Profiles.Contains(SelectedProfile)
            ? SelectedProfile
            : Profiles.FirstOrDefault(p => p is not AddProfileItemViewModel);
        set
        {
            if (value is not null and not AddProfileItemViewModel && Profiles.Contains(value))
            {
                SelectedProfile = value;
            }
        }
    }

    /// <summary>Gets the current content load for deterministic completion checks.</summary>
    internal Task LibraryContentLoad { get; private set; } = Task.CompletedTask;

    partial void OnProfileSearchTextChanged(string value) => RefreshLibraryProfiles();

    partial void OnSelectedLibraryTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsLibraryOverview));
        OnPropertyChanged(nameof(IsLibraryContent));
        OnPropertyChanged(nameof(IsLibrarySettings));
    }

    [RelayCommand]
    private void SelectLibraryTab(string tab)
    {
        if (tab is "Overview" or "Content" or "Settings")
        {
            if (SelectedLibraryTab == tab)
            {
                OnSelectedLibraryTabChanged(tab);
            }
            else
            {
                SelectedLibraryTab = tab;
            }
        }
    }

    [RelayCommand]
    private async Task EditLibraryProfileAsync(string tab)
    {
        if (DisplayedProfile is { CanEdit: true } profile)
        {
            await EditProfileCoreAsync(profile, tab == "Content" ? 0 : 1);
        }
    }

    private void RefreshLibraryProfiles()
    {
        var profiles = Profiles.Where(p => p is not AddProfileItemViewModel).ToList();
        foreach (var old in _librarySubscriptions.Where(p => !profiles.Contains(p)).ToArray())
        {
            old.PropertyChanged -= OnLibraryProfilePropertyChanged;
            _librarySubscriptions.Remove(old);
        }

        foreach (var profile in profiles.Where(p => !_librarySubscriptions.Contains(p)))
        {
            profile.PropertyChanged += OnLibraryProfilePropertyChanged;
            _librarySubscriptions.Add(profile);
        }

        var query = ProfileSearchText?.Trim() ?? string.Empty;
        var matches = profiles.Where(p => query.Length == 0 ||
            p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            p.LibraryGameName.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        for (var i = 0; i < matches.Count; i++)
        {
            var index = LibraryProfiles.IndexOf(matches[i]);
            if (index < 0)
            {
                LibraryProfiles.Insert(i, matches[i]);
            }
            else if (index != i)
            {
                LibraryProfiles.Move(index, i);
            }
        }

        while (LibraryProfiles.Count > matches.Count)
        {
            LibraryProfiles.RemoveAt(LibraryProfiles.Count - 1);
        }

        OnPropertyChanged(nameof(ProfileCount));
        OnPropertyChanged(nameof(LibraryProfileCount));
        OnPropertyChanged(nameof(HasLibraryProfiles));
        RefreshDisplayedProfile();
    }

    private void OnLibraryProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameProfileItemViewModel.Name) or nameof(GameProfileItemViewModel.LibraryGameName))
        {
            RefreshLibraryProfiles();
        }

        if (ReferenceEquals(sender, DisplayedProfile) && e.PropertyName == nameof(GameProfileItemViewModel.LibraryContentCount))
        {
            OnPropertyChanged(nameof(LibraryContentSummary));
            LibraryContentLoad = LoadLibraryContentAsync(DisplayedProfile);
        }
    }

    private void RefreshDisplayedProfile()
    {
        OnPropertyChanged(nameof(DisplayedProfile));
        OnPropertyChanged(nameof(HasDisplayedProfile));
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(LibraryContentSummary));
        var profile = DisplayedProfile;
        if (!ReferenceEquals(_displayedLibraryProfile, profile))
        {
            _displayedLibraryProfile = profile;
            LibraryContentLoad = LoadLibraryContentAsync(profile);
        }
    }

    private void ShowLibraryContentError()
    {
        HasLibraryContentError = true;
        notificationService.ShowWarning(localizationService["GameProfiles.Library.Content"], localizationService["GameProfiles.Library.ContentError"]);
    }

    private async Task LoadLibraryContentAsync(GameProfileItemViewModel? item)
    {
        var revision = ++_libraryContentRevision;
        LibraryContent.Clear();
        OnPropertyChanged(nameof(IsLibraryContentEmpty));
        HasLibraryContentError = false;
        IsLibraryContentLoading = false;
        if (item?.LibraryProfile is not GameProfile profile || libraryScopeFactory == null)
        {
            return;
        }

        IsLibraryContentLoading = true;
        try
        {
            using var scope = libraryScopeFactory.CreateScope();
            var loader = scope.ServiceProvider.GetRequiredService<Core.Interfaces.GameProfiles.IProfileContentLoader>();
            var content = await loader.LoadEnabledContentForProfileAsync(profile);
            if (revision == _libraryContentRevision && content != null)
            {
                foreach (var entry in content)
                {
                    LibraryContent.Add(entry);
                }

                if (content.Count < (profile.EnabledContentIds?.Count ?? 0))
                {
                    ShowLibraryContentError();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load library content for profile {ProfileId}", profile.Id);
            if (revision == _libraryContentRevision)
            {
                ShowLibraryContentError();
            }
        }
        finally
        {
            if (revision == _libraryContentRevision)
            {
                IsLibraryContentLoading = false;
                OnPropertyChanged(nameof(IsLibraryContentEmpty));
            }
        }
    }
}
