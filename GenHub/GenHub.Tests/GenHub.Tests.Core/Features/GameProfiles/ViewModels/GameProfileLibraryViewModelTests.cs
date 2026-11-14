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
using System.Collections.ObjectModel;
using ContentDisplayItem = GenHub.Core.Models.Content.ContentDisplayItem;

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
    /// Verifies that the selected content tab changes only for supported tab names.
    /// </summary>
    [Fact]
    public void SelectLibraryTabCommand_IgnoresUnknownTab()
    {
        var vm = CreateViewModel();
        Assert.Equal("Overview", vm.SelectedLibraryTab);

        vm.SelectLibraryTabCommand.Execute("Content");
        Assert.Equal("Content", vm.SelectedLibraryTab);

        vm.SelectLibraryTabCommand.Execute("Settings");
        Assert.Equal("Settings", vm.SelectedLibraryTab);

        vm.SelectLibraryTabCommand.Execute("Unknown");
        Assert.Equal("Settings", vm.SelectedLibraryTab);

        vm.SelectLibraryTabCommand.Execute("Overview");
        Assert.Equal("Overview", vm.SelectedLibraryTab);
    }

    /// <summary>
    /// Verifies that a slower previous selection cannot replace the current content or loading state.
    /// </summary>
    /// <returns>A task representing the asynchronous regression check.</returns>
    [Fact]
    public async Task LibraryContent_LatePreviousSelection_DoesNotOverwriteCurrentProfileAsync()
    {
        var loader = new Mock<IProfileContentLoader>();
        var alphaCompletion = new TaskCompletionSource<ObservableCollection<ContentDisplayItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bravoCompletion = new TaskCompletionSource<ObservableCollection<ContentDisplayItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.Is<GameProfile>(p => p.Id == "alpha"))).Returns(alphaCompletion.Task);
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.Is<GameProfile>(p => p.Id == "bravo"))).Returns(bravoCompletion.Task);
        var vm = CreateViewModel(loader.Object);
        vm.Profiles.Add(CreateProfile("alpha", "Alpha"));
        var alphaLoad = vm.LibraryContentLoad;
        var bravo = CreateProfile("bravo", "Bravo");
        vm.Profiles.Add(bravo);
        vm.DisplayedProfile = bravo;
        var bravoLoad = vm.LibraryContentLoad;
        Assert.True(vm.IsLibraryContentLoading);

        bravoCompletion.SetResult([new ContentDisplayItem { Id = "bravo-content", DisplayName = "Bravo content" }]);
        await bravoLoad;
        alphaCompletion.SetResult([new ContentDisplayItem { Id = "alpha-content", DisplayName = "Alpha content" }]);
        await alphaLoad;

        Assert.Equal("bravo-content", Assert.Single(vm.LibraryContent).Id);
        Assert.False(vm.IsLibraryContentLoading);
        Assert.False(vm.HasLibraryContentError);
    }

    /// <summary>
    /// Verifies that a loader failure is visible and completes the loading state.
    /// </summary>
    /// <returns>A task representing the asynchronous regression check.</returns>
    [Fact]
    public async Task LibraryContent_LoaderFails_ShowsErrorInsteadOfEmptySuccessAsync()
    {
        var completion = new TaskCompletionSource<ObservableCollection<ContentDisplayItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new Mock<IProfileContentLoader>();
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.IsAny<GameProfile>())).Returns(completion.Task);
        var vm = CreateViewModel(loader.Object);
        vm.Profiles.Add(CreateProfile("alpha", "Alpha"));
        var load = vm.LibraryContentLoad;

        completion.SetException(new IOException("Content metadata unavailable"));
        await load;

        Assert.True(vm.HasLibraryContentError);
        Assert.False(vm.IsLibraryContentLoading);
        Assert.Empty(vm.LibraryContent);
    }

    /// <summary>
    /// Verifies that completion for a removed final profile cannot repopulate the empty detail pane.
    /// </summary>
    /// <returns>A task representing the asynchronous regression check.</returns>
    [Fact]
    public async Task LibraryContent_ProfileRemoved_IgnoresLateCompletionAsync()
    {
        var completion = new TaskCompletionSource<ObservableCollection<ContentDisplayItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new Mock<IProfileContentLoader>();
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.IsAny<GameProfile>())).Returns(completion.Task);
        var vm = CreateViewModel(loader.Object);
        var alpha = CreateProfile("alpha", "Alpha");
        vm.Profiles.Add(alpha);
        var load = vm.LibraryContentLoad;

        vm.Profiles.Remove(alpha);
        completion.SetResult([new ContentDisplayItem { Id = "removed-content", DisplayName = "Removed content" }]);
        await load;

        Assert.Null(vm.DisplayedProfile);
        Assert.Empty(vm.LibraryContent);
        Assert.False(vm.IsLibraryContentLoading);
        Assert.False(vm.HasLibraryContentError);
    }

    /// <summary>
    /// Verifies that unresolved saved content is reported even when the loader returns a partial list.
    /// </summary>
    /// <param name="resolvedCount">The number of content entries the loader resolves.</param>
    /// <returns>A task representing the asynchronous regression check.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task LibraryContent_PartialMetadata_ShowsErrorAndRetainsResolvedRowsAsync(int resolvedCount)
    {
        var resolved = new ObservableCollection<ContentDisplayItem>();
        if (resolvedCount > 0)
        {
            resolved.Add(new ContentDisplayItem { Id = "first-content", DisplayName = "First content" });
        }

        var loader = new Mock<IProfileContentLoader>();
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.IsAny<GameProfile>())).ReturnsAsync(resolved);
        var vm = CreateViewModel(loader.Object);
        var alpha = CreateProfile("alpha", "Alpha");
        ((GameProfile)alpha.Profile).EnabledContentIds = ["first-content", "missing-content"];
        vm.Profiles.Add(alpha);

        await vm.LibraryContentLoad;

        Assert.True(vm.HasLibraryContentError);
        Assert.False(vm.IsLibraryContentEmpty);
        Assert.False(vm.IsLibraryContentLoading);
        Assert.Equal(resolvedCount, vm.LibraryContent.Count);
        Assert.Equal(resolved, vm.LibraryContent);
    }

    /// <summary>
    /// Verifies that the scoped content service remains alive during loading and is disposed afterward.
    /// </summary>
    /// <param name="fails">Whether the loader completes with an error.</param>
    /// <returns>A task representing the asynchronous regression check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryContent_DisposesOwnedScopeAfterLoadAsync(bool fails)
    {
        var completion = new TaskCompletionSource<ObservableCollection<ContentDisplayItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new Mock<IProfileContentLoader>();
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.IsAny<GameProfile>())).Returns(completion.Task);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(x => x.GetService(typeof(IProfileContentLoader))).Returns(loader.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(x => x.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);
        var vm = CreateViewModel(libraryScopeFactory: scopeFactory.Object);
        vm.Profiles.Add(CreateProfile("alpha", "Alpha"));
        var load = vm.LibraryContentLoad;
        scope.Verify(x => x.Dispose(), Times.Never);

        if (fails)
        {
            completion.SetException(new IOException("Content metadata unavailable"));
        }
        else
        {
            completion.SetResult([]);
        }

        await load;

        scope.Verify(x => x.Dispose(), Times.Once);
        Assert.Equal(fails, vm.HasLibraryContentError);
    }

    /// <summary>
    /// Verifies that replacing content with the same entry count still reloads saved metadata.
    /// </summary>
    /// <returns>A task representing the asynchronous regression check.</returns>
    [Fact]
    public async Task LibraryContent_ProfileUpdatedWithSameCount_ReloadsCurrentSnapshotAsync()
    {
        var loader = new Mock<IProfileContentLoader>();
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.IsAny<GameProfile>()))
            .Returns((GameProfile p) => Task.FromResult(new ObservableCollection<ContentDisplayItem>
            {
                new() { Id = p.EnabledContentIds.Single(), DisplayName = p.EnabledContentIds.Single() },
            }));
        var vm = CreateViewModel(loader.Object);
        var alpha = CreateProfile("alpha", "Alpha");
        var original = (GameProfile)alpha.Profile;
        original.EnabledContentIds = ["old-content"];
        vm.Profiles.Add(alpha);
        await vm.LibraryContentLoad;
        var notified = false;
        alpha.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(GameProfileItemViewModel.LibraryContentCount);

        alpha.UpdateFromProfile(new GameProfile
        {
            Id = original.Id,
            Name = original.Name,
            GameClient = original.GameClient,
            EnabledContentIds = ["new-content"],
        });
        await vm.LibraryContentLoad;

        Assert.True(notified);
        Assert.Equal(1, alpha.LibraryContentCount);
        Assert.Equal("new-content", Assert.Single(vm.LibraryContent).Id);
        loader.Verify(x => x.LoadEnabledContentForProfileAsync(It.Is<GameProfile>(p => p.EnabledContentIds.Contains("new-content"))), Times.Once);
    }

    /// <summary>
    /// Verifies that a caption refresh for a language change does not reload content.
    /// </summary>
    /// <returns>A task representing the asynchronous regression check.</returns>
    [Fact]
    public async Task LibraryContent_CaptionRefresh_DoesNotReloadContentAsync()
    {
        var loader = new Mock<IProfileContentLoader>();
        loader.Setup(x => x.LoadEnabledContentForProfileAsync(It.IsAny<GameProfile>()))
            .ReturnsAsync(new ObservableCollection<ContentDisplayItem>());
        var vm = CreateViewModel(loader.Object);
        var alpha = CreateProfile("alpha", "Alpha");
        vm.Profiles.Add(alpha);
        await vm.LibraryContentLoad;
        var load = vm.LibraryContentLoad;
        var notified = false;
        alpha.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(GameProfileItemViewModel.LibraryContentCount);

        alpha.NotifyLibraryCaptionsChanged();

        Assert.False(notified);
        Assert.Same(load, vm.LibraryContentLoad);
        loader.Verify(x => x.LoadEnabledContentForProfileAsync(It.IsAny<GameProfile>()), Times.Once);
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
    /// Verifies that a collapsed profile list has a visible action for reopening it without hover.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_CollapsedSidebar_HasVisibleReopenAction()
    {
        var vm = CreateViewModel();
        vm.Profiles.Add(CreateProfile("profile", "Profile"));
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var layout = view.FindControl<SidebarLayout>("ProfileLibraryLayout")!;
            layout.IsPaneOpen = false;
            Dispatcher.UIThread.RunJobs();
            var reopen = view.GetVisualDescendants().OfType<Button>().Single(x => ReferenceEquals(x.Command, layout.OpenPaneCommand));
            Assert.True(reopen.IsVisible);
            Assert.True(reopen.IsEnabled);
            Assert.True(reopen.Focusable);

            reopen.Command!.Execute(reopen.CommandParameter);
            Dispatcher.UIThread.RunJobs();

            Assert.True(layout.IsPaneOpen);
            Assert.False(reopen.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that clicking an already selected tab does not leave its toggle unchecked.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_ClickSelectedTab_KeepsSelectedAppearance()
    {
        var vm = CreateViewModel();
        vm.Profiles.Add(CreateProfile("profile", "Profile"));
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var overview = view.GetVisualDescendants().OfType<ToggleButton>().Single(x => Equals(x.CommandParameter, "Overview"));
            Assert.True(overview.IsChecked);
            var point = overview.TranslatePoint(new Point(overview.Bounds.Width / 2, overview.Bounds.Height / 2), window);
            Assert.True(point.HasValue);

            window.MouseDown(point.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Overview", vm.SelectedLibraryTab);
            Assert.True(overview.IsChecked);
        }
        finally
        {
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
            var button = view.GetVisualDescendants().OfType<Button>().Single(x => x.Flyout is MenuFlyout);
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
    /// Verifies that the Steam launch setting writes its checked state back before persisting.
    /// </summary>
    [AvaloniaFact]
    public void LibraryView_SteamToggle_BindsBothDirections()
    {
        var vm = CreateViewModel();
        var profile = CreateProfile("profile", "Profile", GameType.ZeroHour);
        profile.IsSteamInstallation = true;
        bool? persisted = null;
        profile.ToggleSteamLaunchAction = item =>
        {
            persisted = item.UseSteamLaunch;
            return Task.CompletedTask;
        };
        vm.Profiles.Add(profile);
        vm.SelectedLibraryTab = "Settings";
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(x => ReferenceEquals(x.Command, profile.ToggleSteamLaunchCommand));

            profile.UseSteamLaunch = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(toggle.IsChecked);

            var point = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window);
            Assert.True(point.HasValue);
            window.MouseDown(point.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(profile.UseSteamLaunch);
            Assert.True(persisted);
        }
        finally
        {
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

    private static GameProfileLauncherViewModel CreateViewModel(IProfileContentLoader? contentLoader = null, IServiceScopeFactory? libraryScopeFactory = null)
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(x => x[It.IsAny<string>()]).Returns((string key) => key);
        localization.SetupGet(x => x.CurrentCulture).Returns(System.Globalization.CultureInfo.InvariantCulture);
        Mock<IServiceScopeFactory>? scopeFactory = null;
        if (contentLoader != null)
        {
            var provider = new Mock<IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(IProfileContentLoader))).Returns(contentLoader);
            scopeFactory = new Mock<IServiceScopeFactory>();
            scopeFactory.Setup(x => x.CreateScope()).Returns(() =>
            {
                var scope = new Mock<IServiceScope>();
                scope.SetupGet(x => x.ServiceProvider).Returns(provider.Object);
                return scope.Object;
            });
        }

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
            localization.Object,
            libraryScopeFactory: libraryScopeFactory ?? scopeFactory?.Object);
    }
}
