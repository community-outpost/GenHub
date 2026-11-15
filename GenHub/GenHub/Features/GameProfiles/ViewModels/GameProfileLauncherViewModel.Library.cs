using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Search and selected-profile presentation for the profile library.
/// </summary>
public partial class GameProfileLauncherViewModel
{
    private readonly HashSet<GameProfileItemViewModel> _librarySubscriptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryEmpty))]
    private bool _isLibraryInitializing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryEmpty))]
    private bool _hasLibraryLoadError;

    [ObservableProperty]
    private string _profileSearchText = string.Empty;

    /// <summary>Gets a value indicating whether the library has loaded without any profiles.</summary>
    public bool IsLibraryEmpty => !HasDisplayedProfile && !IsLibraryInitializing && !HasLibraryLoadError;

    /// <summary>Gets the total profile count in a localized caption.</summary>
    public string LibraryProfileCount => localizationService.GetString("GameProfiles.Library.MyProfilesCount", ProfileCount);

    /// <summary>Gets profiles matching the search in the user's existing sort order.</summary>
    public ObservableCollection<GameProfileItemViewModel> LibraryProfiles { get; } = [];

    /// <summary>Gets the number of real profiles, excluding the former Add card.</summary>
    public int ProfileCount => Profiles.Count(p => p is not AddProfileItemViewModel);

    /// <summary>Gets a value indicating whether the search matches any profiles.</summary>
    public bool HasLibraryProfiles => LibraryProfiles.Count > 0;

    /// <summary>Gets a value indicating whether a profile is available for the detail pane.</summary>
    public bool HasDisplayedProfile => DisplayedProfile != null;

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

    partial void OnProfileSearchTextChanged(string value) => RefreshLibraryProfiles();

    [RelayCommand]
    private void SetLibrarySortMode(Core.Models.Enums.ProfileSortMode mode)
    {
        var option = AvailableSortModes.FirstOrDefault(item => item.Mode == mode);
        if (option != null)
        {
            SelectedSortModeItem = option;
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
    }

    private void RefreshDisplayedProfile()
    {
        OnPropertyChanged(nameof(DisplayedProfile));
        OnPropertyChanged(nameof(HasDisplayedProfile));
        OnPropertyChanged(nameof(IsLibraryEmpty));
    }
}
