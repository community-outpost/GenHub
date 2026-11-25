using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.Controls;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Settings.Views;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Verifies the choice between the profile library and the card gallery.
/// </summary>
public class GameProfileLayoutTests
{
    /// <summary>
    /// Verifies that the layout survives the settings clone used when saving.
    /// </summary>
    [Fact]
    public void UserSettingsClone_CopiesProfileLayout()
    {
        var settings = new UserSettings { ProfileLayout = ProfileLayout.Cards };

        var clone = settings.Clone();

        Assert.Equal(ProfileLayout.Cards, clone.ProfileLayout);
        Assert.Equal(ProfileLayout.Library, new UserSettings().ProfileLayout);
    }

    /// <summary>
    /// Verifies that a saved card layout is restored without being written back during load.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task InitializeAsync_LoadsSavedLayout_WithoutPersistingAsync()
    {
        var settings = new UserSettings { ProfileLayout = ProfileLayout.Cards };
        var settingsService = CreateSettingsService(settings);
        var vm = CreateViewModel(settingsService.Object);

        await vm.InitializeAsync();

        Assert.Equal(ProfileLayout.Cards, vm.ProfileLayout);
        Assert.True(vm.IsCardLayout);
        Assert.False(vm.IsLibraryLayout);
        settingsService.Verify(x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()), Times.Never);
    }

    /// <summary>
    /// Verifies that an out-of-range saved layout falls back to the library.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task InitializeAsync_UnknownSavedLayout_FallsBackToLibraryAsync()
    {
        var settings = new UserSettings { ProfileLayout = (ProfileLayout)42 };
        var vm = CreateViewModel(CreateSettingsService(settings).Object);

        await vm.InitializeAsync();

        Assert.Equal(ProfileLayout.Library, vm.ProfileLayout);
    }

    /// <summary>
    /// Verifies that choosing a layout option switches the layout and saves it.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SelectedProfileLayoutOption_SwitchesAndPersistsLayoutAsync()
    {
        var settings = new UserSettings();
        var vm = CreateViewModel(CreateSettingsService(settings).Object);

        vm.SelectedProfileLayoutOption = vm.AvailableProfileLayouts.Single(option => option.Layout == ProfileLayout.Cards);
        await WaitForAsync(() => settings.ProfileLayout == ProfileLayout.Cards);

        Assert.True(vm.IsCardLayout);
        Assert.Equal(ProfileLayout.Cards, vm.SelectedProfileLayoutOption?.Layout);
        Assert.Equal(ProfileLayout.Cards, settings.ProfileLayout);
    }

    /// <summary>
    /// Verifies that the settings options use localized names and ignore a transient null selection.
    /// </summary>
    [Fact]
    public void SelectedProfileLayoutOption_MapsOptionsAndIgnoresNull()
    {
        var vm = CreateViewModel(null);

        Assert.Equal(["GameProfiles.Layout.Library", "GameProfiles.Layout.Cards"], vm.AvailableProfileLayouts.Select(option => option.DisplayName));

        vm.SelectedProfileLayoutOption = vm.AvailableProfileLayouts[1];
        Assert.Equal(ProfileLayout.Cards, vm.ProfileLayout);

        vm.SelectedProfileLayoutOption = null;
        Assert.Equal(ProfileLayout.Cards, vm.ProfileLayout);
    }

    /// <summary>
    /// Verifies that the host shows exactly one layout and swaps when the layout changes.
    /// </summary>
    [AvaloniaFact]
    public void LauncherView_ShowsOnlyTheSelectedLayout()
    {
        var vm = CreateViewModel(null);
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var library = view.GetVisualDescendants().OfType<SidebarLayout>().Single();
            var cards = view.GetVisualDescendants().OfType<GameProfileCardsView>().Single();

            Assert.True(library.IsVisible);
            Assert.False(cards.IsVisible);

            vm.ProfileLayout = ProfileLayout.Cards;
            Dispatcher.UIThread.RunJobs();

            Assert.False(library.IsVisible);
            Assert.True(cards.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that neither layout offers its own list and grid toggles, since the layout is chosen in settings.
    /// </summary>
    [AvaloniaFact]
    public void Layouts_DoNotShowLayoutToggles()
    {
        var vm = CreateViewModel(null);
        var view = new GameProfileLauncherView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(GetLayoutIcons(view));

            vm.ProfileLayout = ProfileLayout.Cards;
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(view.GetVisualDescendants().OfType<GameProfileCardsView>());
            Assert.Empty(GetLayoutIcons(view));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that a language change renames the layout options while keeping the chosen layout selected.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CultureChange_RenamesLayoutOptions_AndKeepsSelectionAsync()
    {
        var prefix = string.Empty;
        var localization = CreateLocalization();
        localization.Setup(x => x[It.IsAny<string>()]).Returns((string key) => prefix + key);
        var vm = CreateViewModel(null, localization);
        await vm.InitializeAsync();
        vm.ProfileLayout = ProfileLayout.Cards;
        var options = vm.AvailableProfileLayouts;
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        prefix = "ru:";
        localization.Raise(x => x.PropertyChanged += null, new System.ComponentModel.PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));

        Assert.Same(options, vm.AvailableProfileLayouts);
        Assert.Equal(["ru:GameProfiles.Layout.Library", "ru:GameProfiles.Layout.Cards"], options.Select(option => option.DisplayName));
        Assert.Equal(ProfileLayout.Cards, vm.SelectedProfileLayoutOption?.Layout);
        Assert.Contains(nameof(GameProfileLauncherViewModel.SelectedProfileLayoutOption), notified);
        Assert.Equal(ProfileLayout.Cards, vm.ProfileLayout);
    }

    /// <summary>
    /// Verifies that Reset to Defaults returns the shared launcher to the library layout.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SettingsResetToDefaults_RestoresLibraryLayoutAsync()
    {
        var launcher = CreateViewModel(null);
        launcher.ProfileLayout = ProfileLayout.Cards;
        var configService = new Mock<IUserSettingsService>();
        var settingsVm = CreateSettingsViewModel(launcher, configService);

        await settingsVm.ResetToDefaultsCommand.ExecuteAsync(null);

        Assert.Equal(ProfileLayout.Library, launcher.ProfileLayout);
        configService.Verify(x => x.Update(It.IsAny<Action<UserSettings>>()), Times.Once);
    }

    /// <summary>
    /// Verifies that choosing a layout in Settings switches the shared launcher.
    /// </summary>
    [AvaloniaFact]
    public void SettingsLayoutComboBox_UpdatesLauncherLayout()
    {
        var launcher = CreateViewModel(null);
        var settingsVm = CreateSettingsViewModel(launcher);
        var view = new SettingsView { DataContext = settingsVm };
        var window = new Window { Width = 1200, Height = 900, Content = view };
        try
        {
            window.Show();
            view.FindControl<Expander>("Expander_Appearance")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var comboBox = view.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "ProfileLayoutComboBox");

            Assert.True(comboBox.IsEffectivelyVisible);
            Assert.Same(launcher.AvailableProfileLayouts, comboBox.ItemsSource);
            Assert.Equal(ProfileLayout.Library, ((GameProfileLauncherViewModel.ProfileLayoutOption)comboBox.SelectedItem!).Layout);

            comboBox.SelectedItem = launcher.AvailableProfileLayouts.Single(option => option.Layout == ProfileLayout.Cards);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProfileLayout.Cards, launcher.ProfileLayout);

            launcher.ProfileLayout = ProfileLayout.Library;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ProfileLayout.Library, ((GameProfileLauncherViewModel.ProfileLayoutOption)comboBox.SelectedItem!).Layout);
        }
        finally
        {
            window.Close();
        }
    }

    private static List<MaterialIcon> GetLayoutIcons(Control root) =>
        root.GetVisualDescendants().OfType<MaterialIcon>().Where(icon => icon.Kind is MaterialIconKind.ViewList or MaterialIconKind.ViewGrid).ToList();

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private static Mock<IUserSettingsService> CreateSettingsService(UserSettings settings)
    {
        var service = new Mock<IUserSettingsService>();
        service.Setup(x => x.Get()).Returns(settings);
        service.Setup(x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .ReturnsAsync((Func<UserSettings, bool> apply) => apply(settings));
        return service;
    }

    private static Mock<ILocalizationService> CreateLocalization()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(x => x[It.IsAny<string>()]).Returns((string key) => key);
        localization.Setup(x => x.GetString(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] values) => key + ":" + string.Join(",", values));
        localization.SetupGet(x => x.CurrentCulture).Returns(System.Globalization.CultureInfo.InvariantCulture);
        return localization;
    }

    private static GameProfileLauncherViewModel CreateViewModel(IUserSettingsService? settingsService, Mock<ILocalizationService>? localization = null)
    {
        localization ??= CreateLocalization();
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
            userSettingsService: settingsService);
    }

    private static SettingsViewModel CreateSettingsViewModel(GameProfileLauncherViewModel launcher, Mock<IUserSettingsService>? configService = null)
    {
        configService ??= new Mock<IUserSettingsService>();
        configService.Setup(x => x.Get()).Returns(new UserSettings());
        var configurationProvider = new Mock<IConfigurationProviderService>();
        configurationProvider.Setup(x => x.GetApplicationDataPath()).Returns(Path.GetTempPath());

        return new SettingsViewModel(
            configService.Object,
            new Mock<ILogger<SettingsViewModel>>().Object,
            new Mock<ICasService>().Object,
            new Mock<ICasLifecycleManager>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<IWorkspaceManager>().Object,
            new Mock<IContentManifestPool>().Object,
            updateManager: null,
            new Mock<INotificationService>().Object,
            configurationProvider.Object,
            new Mock<IGameInstallationService>().Object,
            new Mock<IStorageLocationService>().Object,
            new Mock<IUserDataTracker>().Object,
            new Mock<IDialogService>().Object,
            new Mock<IStorageMigrationService>().Object,
            profileLibraryActions: launcher);
    }
}
