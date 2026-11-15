using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Verifies the searchable library projection without scanning installations or launching games.
/// </summary>
public class GameProfileLibraryViewModelTests
{
    /// <summary>
    /// Verifies that the library excludes the old add-card sentinel from rows and counts.
    /// </summary>
    [Fact]
    public void LibraryProfiles_ExcludesAddCardFromRowsAndCount()
    {
        var vm = CreateViewModel();
        var profile = CreateProfile("original", "Original game");
        vm.Profiles.Add(new AddProfileItemViewModel());
        vm.Profiles.Insert(0, profile);

        Assert.Equal([profile], vm.LibraryProfiles);
        Assert.Equal(1, vm.ProfileCount);
        Assert.True(vm.HasLibraryProfiles);
    }

    /// <summary>
    /// Verifies that search matches a profile name or game regardless of casing.
    /// </summary>
    /// <param name="query">The search text.</param>
    [Theory]
    [InlineData("vEtErAn")]
    [InlineData("ZERO HOUR")]
    public void ProfileSearchText_MatchesNameAndGameIgnoringCase(string query)
    {
        var vm = CreateViewModel();
        var veteran = CreateProfile("veteran", "Veteran", GameType.ZeroHour);
        vm.Profiles.Add(CreateProfile("campaign", "Campaign", GameType.Generals));
        vm.Profiles.Add(veteran);
        vm.Profiles.Add(new AddProfileItemViewModel());

        vm.ProfileSearchText = query;

        Assert.Equal([veteran], vm.LibraryProfiles);
        Assert.Equal(2, vm.ProfileCount);
    }

    /// <summary>
    /// Verifies that an empty search result does not lose the selected profile or total count.
    /// </summary>
    [Fact]
    public void ProfileSearchText_NoMatches_KeepsSelectionAndTotalCount()
    {
        var vm = CreateViewModel();
        var selected = CreateProfile("campaign", "Campaign");
        vm.Profiles.Add(selected);
        vm.Profiles.Add(CreateProfile("skirmish", "Skirmish"));
        vm.SelectedProfile = selected;

        vm.ProfileSearchText = "no such profile";

        Assert.Empty(vm.LibraryProfiles);
        Assert.False(vm.HasLibraryProfiles);
        Assert.Equal(2, vm.ProfileCount);
        Assert.Same(selected, vm.SelectedProfile);
    }

    /// <summary>
    /// Verifies that filtering preserves the selected profile even when its row is hidden.
    /// </summary>
    [Fact]
    public void ProfileSearchText_HidesSelectedRow_WithoutChangingSelection()
    {
        var vm = CreateViewModel();
        var campaign = CreateProfile("campaign", "Campaign");
        var skirmish = CreateProfile("skirmish", "Skirmish");
        vm.Profiles.Add(campaign);
        vm.Profiles.Add(skirmish);
        vm.SelectedProfile = campaign;

        vm.ProfileSearchText = "Skirmish";

        Assert.Equal([skirmish], vm.LibraryProfiles);
        Assert.Same(campaign, vm.SelectedProfile);

        vm.ProfileSearchText = string.Empty;

        Assert.Equal([campaign, skirmish], vm.LibraryProfiles);
        Assert.Same(campaign, vm.SelectedProfile);
    }

    /// <summary>
    /// Verifies that collection additions and resets update results while a search is active.
    /// </summary>
    [Fact]
    public void Profiles_AddAndClear_RefreshesFilteredRowsAndEmptyState()
    {
        var vm = CreateViewModel();
        vm.ProfileSearchText = "LAN";
        vm.Profiles.Add(CreateProfile("campaign", "Campaign"));
        var lan = CreateProfile("lan", "LAN night");
        vm.Profiles.Add(lan);
        vm.Profiles.Add(new AddProfileItemViewModel());

        Assert.Equal([lan], vm.LibraryProfiles);
        Assert.True(vm.HasLibraryProfiles);
        Assert.Equal(2, vm.ProfileCount);

        vm.Profiles.Clear();

        Assert.Empty(vm.LibraryProfiles);
        Assert.False(vm.HasLibraryProfiles);
        Assert.Equal(0, vm.ProfileCount);
        Assert.Null(vm.DisplayedProfile);
    }

    /// <summary>
    /// Verifies that removing the selected profile chooses a real remaining profile.
    /// </summary>
    [Fact]
    public void Profiles_RemoveSelected_FallsBackToRemainingRealProfile()
    {
        var vm = CreateViewModel();
        var campaign = CreateProfile("campaign", "Campaign");
        var skirmish = CreateProfile("skirmish", "Skirmish");
        vm.Profiles.Add(campaign);
        vm.Profiles.Add(skirmish);
        vm.Profiles.Add(new AddProfileItemViewModel());
        vm.SelectedProfile = campaign;

        vm.Profiles.Remove(campaign);

        Assert.Same(skirmish, vm.DisplayedProfile);
        Assert.Equal([skirmish], vm.LibraryProfiles);

        vm.Profiles.Remove(skirmish);

        Assert.Null(vm.DisplayedProfile);
        Assert.Empty(vm.LibraryProfiles);
    }

    /// <summary>
    /// Verifies that the display fallback does not overwrite the persisted selection contract.
    /// </summary>
    [Fact]
    public void DisplayedProfile_UsesFirstRealProfileWithoutSelectingIt()
    {
        var vm = CreateViewModel();
        var campaign = CreateProfile("campaign", "Campaign");
        vm.Profiles.Add(new AddProfileItemViewModel());
        vm.Profiles.Add(campaign);

        Assert.Same(campaign, vm.DisplayedProfile);
        Assert.Null(vm.SelectedProfile);
    }

    /// <summary>
    /// Verifies that null row selection during filtering does not discard the selected detail.
    /// </summary>
    [Fact]
    public void DisplayedProfile_NullSelectionFromFilteredList_PreservesSelectedProfile()
    {
        var vm = CreateViewModel();
        var campaign = CreateProfile("campaign", "Campaign");
        var skirmish = CreateProfile("skirmish", "Skirmish");
        vm.Profiles.Add(campaign);
        vm.Profiles.Add(skirmish);
        vm.DisplayedProfile = campaign;

        vm.ProfileSearchText = "Skirmish";
        vm.DisplayedProfile = null;

        Assert.Same(campaign, vm.SelectedProfile);
        Assert.Same(campaign, vm.DisplayedProfile);
        Assert.Equal([skirmish], vm.LibraryProfiles);
    }

    /// <summary>
    /// Verifies that replacing a row refreshes the projection and detaches the old name observer.
    /// </summary>
    [Fact]
    public void Profiles_ReplaceItem_RefreshesResultsWithoutStaleRows()
    {
        var vm = CreateViewModel();
        var original = CreateProfile("old", "Old campaign");
        var replacement = CreateProfile("new", "New campaign");
        vm.Profiles.Add(original);
        vm.ProfileSearchText = "New";

        vm.Profiles[0] = replacement;
        original.Name = "New stale profile";

        Assert.Equal([replacement], vm.LibraryProfiles);
        Assert.Equal(1, vm.ProfileCount);
    }

    /// <summary>
    /// Verifies that renaming a profile updates search membership in both directions.
    /// </summary>
    [Fact]
    public void ProfileName_Changes_RefreshesSearchMembership()
    {
        var vm = CreateViewModel();
        var profile = CreateProfile("campaign", "Campaign");
        vm.Profiles.Add(profile);
        vm.ProfileSearchText = "LAN";
        Assert.Empty(vm.LibraryProfiles);

        profile.Name = "LAN night";
        Assert.Equal([profile], vm.LibraryProfiles);

        profile.Name = "Skirmish";
        Assert.Empty(vm.LibraryProfiles);
        Assert.False(vm.HasLibraryProfiles);
    }

    /// <summary>
    /// Verifies that the projection follows the selected sort order without losing selection.
    /// </summary>
    [Fact]
    public void ApplySorting_PreservesFilteredOrderAndSelection()
    {
        var vm = CreateViewModel();
        var zulu = CreateProfile("zulu", "Zulu LAN");
        var alpha = CreateProfile("alpha", "Alpha LAN");
        vm.Profiles.Add(zulu);
        vm.Profiles.Add(CreateProfile("other", "Campaign"));
        vm.Profiles.Add(alpha);
        vm.Profiles.Add(new AddProfileItemViewModel());
        vm.SelectedProfile = zulu;
        vm.ProfileSearchText = "LAN";

        vm.SelectedSortMode = ProfileSortMode.Alphabetical;
        vm.ApplySorting();

        Assert.Equal([alpha, zulu], vm.LibraryProfiles);
        Assert.Same(zulu, vm.SelectedProfile);
        Assert.Equal(3, vm.ProfileCount);
    }

    /// <summary>
    /// Verifies that mounting the detail fallback does not silently persist a default selection.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_MountsDefaultDetail_WithoutChangingRememberedSelection()
    {
        var vm = CreateViewModel();
        var first = CreateProfile("first", "First profile");
        vm.Profiles.Add(first);
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(vm.SelectedProfile);
            Assert.Same(first, vm.DisplayedProfile);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the visible overview does not repeat the selected game and version in several places.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_OverviewShowsOneMetadataLine_AndUpdatesAfterRename()
    {
        var vm = CreateViewModel();
        var profile = CreateProfile("zero-hour", "Zero Hour", GameType.ZeroHour);
        profile.Description = "PC · v1.04 · Zero Hour";
        profile.GameVersion = "v1.04";
        vm.Profiles.Add(profile);
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var visibleText = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();

            Assert.Equal(2, visibleText.Count(text => text == "Zero Hour"));
            Assert.Equal(1, visibleText.Count(text => text == "v1.04"));
            Assert.DoesNotContain(profile.Description, visibleText);

            profile.Name = "LAN night";
            Dispatcher.UIThread.RunJobs();
            visibleText = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();

            Assert.Equal(2, visibleText.Count(text => text == "LAN night"));
            Assert.Equal(1, visibleText.Count(text => text == "Zero Hour · v1.04"));
            Assert.DoesNotContain("Zero Hour", visibleText);
            Assert.DoesNotContain("v1.04", visibleText);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that a filtered ListBox does not clear the selected detail through its two-way binding.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_FilterHidesSelectedRow_PreservesDetailSelection()
    {
        var vm = CreateViewModel();
        var campaign = CreateProfile("campaign", "Campaign");
        var skirmish = CreateProfile("skirmish", "Skirmish");
        vm.Profiles.Add(campaign);
        vm.Profiles.Add(skirmish);
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var list = view.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedItem = skirmish;
            Dispatcher.UIThread.RunJobs();

            vm.ProfileSearchText = "Campaign";
            Dispatcher.UIThread.RunJobs();

            Assert.Same(skirmish, vm.SelectedProfile);
            Assert.Same(skirmish, vm.DisplayedProfile);
            Assert.Equal([campaign], vm.LibraryProfiles);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the library pane stays open and does not offer collapse controls.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_FixedPane_RemainsOpenAtCompactWidths()
    {
        var vm = CreateViewModel();
        vm.Profiles.Add(CreateProfile("profile", "Profile"));
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 900, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var layout = view.FindControl<SidebarLayout>("ProfileLibraryLayout")!;
            Assert.False(layout.IsPaneCollapsible);
            Assert.False(layout.ClosePaneCommand.CanExecute(null));
            layout.IsPaneOpen = false;
            Assert.True(layout.IsPaneOpen);
            window.Width = 600;
            Dispatcher.UIThread.RunJobs();
            Assert.True(layout.IsPaneOpen);
            Assert.DoesNotContain(
                view.GetVisualDescendants().OfType<Button>(),
                control => control.IsEffectivelyVisible && ReferenceEquals(control.Command, layout.ClosePaneCommand));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the launch page uses the existing editor instead of duplicate settings tabs.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_ProfileSettings_UsesExistingEditorCommand()
    {
        var vm = CreateViewModel();
        var profile = CreateProfile("profile", "Profile");
        vm.Profiles.Add(profile);
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var settings = view.GetVisualDescendants().OfType<Button>().Single(control => ReferenceEquals(control.Command, profile.EditProfileCommand));
            Assert.True(settings.IsEffectivelyVisible);
            Assert.True(settings.IsEnabled);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ToggleButton>(), control => Equals(control.CommandParameter, "Overview"));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), control => control.IsEffectivelyVisible && (ReferenceEquals(control.Command, vm.ScanForGamesCommand) || ReferenceEquals(control.Command, vm.ImportProfileCommand)));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that an empty library offers the scan its help text mentions.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_EmptyLibrary_OffersScan()
    {
        var vm = CreateViewModel();
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsLibraryEmpty);
            var scan = view.GetVisualDescendants().OfType<Button>().Single(control => ReferenceEquals(control.Command, vm.ScanForGamesCommand));
            Assert.True(scan.IsEffectivelyVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ProgressBar>(), bar => bar.IsEffectivelyVisible);

            vm.IsScanning = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(scan.IsEnabled);
            Assert.Single(view.GetVisualDescendants().OfType<ProgressBar>(), bar => bar.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the compact sort menu directly changes the existing sort selection.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_SortMenu_UsesExistingSortOptions()
    {
        var vm = CreateViewModel();
        var option = new GameProfileLauncherViewModel.ProfileSortOption(ProfileSortMode.Alphabetical, "Name");
        vm.AvailableSortModes.Add(option);
        var view = new GameProfileLauncherView { DataContext = vm };
        var button = view.FindControl<Button>("ProfileSortButton")!;
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        var sort = menu.Items.OfType<MenuItem>().Single(item => Equals(item.CommandParameter, ProfileSortMode.Alphabetical));
        var window = new Window { Width = 900, Height = 800, Content = view };
        try
        {
            window.Show();
            menu.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(vm.SetLibrarySortModeCommand, sort.Command);
            sort.Command!.Execute(sort.CommandParameter);
            Assert.Same(option, vm.SelectedSortModeItem);
        }
        finally
        {
            menu.Hide();
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that flyout actions retain the displayed profile's command binding.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_ProfileActionsFlyout_BindsDisplayedProfileCommands()
    {
        var vm = CreateViewModel();
        var profile = CreateProfile("profile", "Profile");
        vm.Profiles.Add(profile);
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        MenuFlyout? flyout = null;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>().Single(x => x.Name != "ProfileSortButton" && x.Flyout is MenuFlyout);
            flyout = (MenuFlyout)button.Flyout!;
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();

            var edit = flyout.Items.OfType<MenuItem>().First();
            Assert.Same(profile.EditProfileCommand, edit.Command);
        }
        finally
        {
            flyout?.Hide();
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that sidebar context actions bind to the clicked row rather than the selected detail.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_ContextMenu_UsesClickedProfileCommands()
    {
        var vm = CreateViewModel();
        var selected = CreateProfile("selected", "Selected");
        var other = CreateProfile("other", "Other");
        vm.Profiles.Add(selected);
        vm.Profiles.Add(other);
        vm.SelectedProfile = selected;
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        ContextMenu? contextMenu = null;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var row = view.GetVisualDescendants().OfType<Border>().Single(control => ReferenceEquals(control.DataContext, other) && control.ContextMenu != null);
            contextMenu = row.ContextMenu!;
            contextMenu.Open(row);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(other.EditProfileCommand, contextMenu.Items.OfType<MenuItem>().First().Command);
            Assert.Same(selected, vm.SelectedProfile);
        }
        finally
        {
            contextMenu?.Close();
            window.Close();
        }
    }

    private static GameProfileItemViewModel CreateProfile(string id, string name, GameType gameType = GameType.Generals)
    {
        var profile = new GameProfile
        {
            Id = id,
            Name = name,
            GameClient = new GameClient
            {
                Id = $"sample-{gameType}",
                Name = gameType == GameType.ZeroHour ? "Zero Hour" : "Generals",
                GameType = gameType,
                PublisherType = PublisherTypeConstants.Steam,
                Version = gameType == GameType.ZeroHour ? "1.04" : "1.08",
            },
        };
        return new GameProfileItemViewModel(id, profile, string.Empty, string.Empty);
    }

    private static GameProfileLauncherViewModel CreateViewModel()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(x => x[It.IsAny<string>()]).Returns((string key) => key);
        localization.Setup(x => x.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] values) => key + ":" + string.Join(",", values));
        localization.SetupGet(x => x.CurrentCulture).Returns(System.Globalization.CultureInfo.InvariantCulture);

        return new GameProfileLauncherViewModel(
            new Mock<IGameInstallationService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IProfileLauncherFacade>().Object,
            null!,
            new Mock<IProfileEditorFacade>().Object,
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameProcessManager>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IPublisherProfileOrchestrator>().Object,
            new Mock<ISteamManifestPatcher>().Object,
            new ProfileResourceService(NullLogger<ProfileResourceService>.Instance, localization.Object),
            new Mock<IGameClientDetector>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISetupWizardService>().Object,
            new Mock<IDialogService>().Object,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            localization.Object);
    }
}
