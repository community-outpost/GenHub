using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Resources;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>
/// Headless tests for the visible labels in the profile settings window.
/// </summary>
public class GameProfileWindowLabelTests
{
    private delegate bool TryGetStringCallback(string key, out string? value, object?[] arguments);

    private const double SidebarWidth = 240;

    private static readonly ResourceManager Resources =
        new(LocalizationConstants.StringResourceBaseName, typeof(LocalizationService).Assembly);

    /// <summary>
    /// Verifies the embedded settings host renders the view model's own toasts.
    /// </summary>
    [AvaloniaFact]
    public void EmbeddedHost_RendersViewModelNotifications()
    {
        var viewModel = CreateViewModel();
        var view = new GameProfileSettingsContentView { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 800, Content = view };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();

            var container = Assert.Single(view.GetVisualDescendants().OfType<GenHub.Features.Notifications.Views.NotificationContainerView>());
            Assert.Same(viewModel.NotificationManager, container.DataContext);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies each sidebar page shows its translated label within the sidebar width.
    /// </summary>
    /// <param name="cultureName">The shipped culture to render.</param>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("ar")]
    [InlineData("ru")]
    public void SidebarPages_ShowTranslatedLabelsWithinSidebar(string cultureName)
    {
        var resourceSet = Resources.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;
        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => resourceSet.GetString(key) ?? key);
        string[] keys =
        [
            "GameProfiles.Navigation.General",
            "GameProfiles.Navigation.Game",
            "GameProfiles.Navigation.Content",
            "GameProfiles.General.TabAppearance",
            "GameSettings.Category.Video",
            "GameSettings.Category.Audio",
            "GameSettings.Category.Controls",
            "GameSettings.Category.TheSuperHackers",
            "GameSettings.Category.GeneralsOnline",
        ];

        WithLocalization(localization.Object, () =>
        {
            var viewModel = CreateViewModel();
            viewModel.GameSettingsViewModel.UpdateApplicableClientVisibility(isTheSuperHackers: true, isGeneralsOnline: true);
            var view = new GameProfileSettingsSidebarView { DataContext = viewModel };
            var window = new Window { Width = SidebarWidth, Height = 700, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var pages = view.GetLogicalDescendants().OfType<Button>().Where(button => button.IsVisible).ToList();
                var labels = pages.Select(page => Assert.Single(page.GetLogicalDescendants().OfType<TextBlock>())).ToList();

                Assert.Equal(keys.Select(resourceSet.GetString), labels.Select(label => label.Text));
                Assert.Equal(keys.Select(resourceSet.GetString), pages.Select(AutomationProperties.GetName));
                Assert.All(pages, page =>
                {
                    var label = Assert.Single(page.GetLogicalDescendants().OfType<TextBlock>());
                    Assert.True(label.IsEffectivelyVisible);
                    var natural = new TextBlock
                    {
                        Text = label.Text,
                        FontSize = label.FontSize,
                        FontWeight = label.FontWeight,
                        FontFamily = label.FontFamily,
                    };
                    natural.Measure(Size.Infinity);
                    var available = page.Bounds.Width - page.Padding.Left - page.Padding.Right;
                    Assert.True(natural.DesiredSize.Width <= available, $"'{label.Text}' needs {natural.DesiredSize.Width} but the page row offers {available}");
                });
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Verifies the strategy picker shows the new language without a new selection.
    /// </summary>
    [AvaloniaFact]
    public void WorkspaceStrategyPicker_RefreshesTextWhenLanguageChanges()
    {
        var culture = CultureInfo.GetCultureInfo("en");
        var localization = new Mock<ILocalizationService>();
        localization.SetupGet(service => service.CurrentCulture).Returns(() => culture);
        localization.Setup(service => service.TryGetString(It.IsAny<string>(), out It.Ref<string?>.IsAny, It.IsAny<object?[]>()))
            .Returns(new TryGetStringCallback((string key, out string? value, object?[] arguments) =>
            {
                value = $"{culture.Name}:{key}";
                return true;
            }));

        WithLocalization(localization.Object, () =>
        {
            var picker = new ComboBox
            {
                Classes = { "workspace-strategy" },
                ItemsSource = Enum.GetValues<WorkspaceStrategy>(),
                SelectedItem = WorkspaceStrategy.HardLink,
            };
            var window = new Window { Width = 600, Height = 200, Content = picker };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                Assert.Equal("en:WorkspaceStrategy.HardLink.Name", SelectionBoxText(picker));

                culture = CultureInfo.GetCultureInfo("ru");
                localization.Raise(service => service.PropertyChanged += null, new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("ru:WorkspaceStrategy.HardLink.Name", SelectionBoxText(picker));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Verifies the strategy description below the picker shows the new language without a new selection.
    /// </summary>
    [AvaloniaFact]
    public void WorkspaceStrategyDescription_RefreshesWhenLanguageChanges()
    {
        var culture = CultureInfo.GetCultureInfo("en");
        var localization = new Mock<ILocalizationService>();
        localization.SetupGet(service => service.CurrentCulture).Returns(() => culture);
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => key);
        localization.Setup(service => service.TryGetString(It.IsAny<string>(), out It.Ref<string?>.IsAny, It.IsAny<object?[]>()))
            .Returns(new TryGetStringCallback((string key, out string? value, object?[] arguments) =>
            {
                value = $"{culture.Name}:{key}";
                return true;
            }));

        WithLocalization(localization.Object, () =>
        {
            var viewModel = CreateViewModel();
            viewModel.SelectedTabIndex = 1;
            viewModel.SelectedWorkspaceStrategy = WorkspaceStrategy.HardLink;
            var view = new GameProfileSettingsContentView { DataContext = viewModel };
            var window = new Window { Width = 1100, Height = 900, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                const string descriptionKey = "WorkspaceStrategy.HardLink.Description";
                Assert.Single(DescriptionTexts(view), text => text.StartsWith("en:" + descriptionKey, StringComparison.Ordinal));

                culture = CultureInfo.GetCultureInfo("ru");
                localization.Raise(service => service.PropertyChanged += null, new PropertyChangedEventArgs(nameof(ILocalizationService.CurrentCulture)));
                Dispatcher.UIThread.RunJobs();

                Assert.Single(DescriptionTexts(view), text => text.StartsWith("ru:" + descriptionKey, StringComparison.Ordinal));
                Assert.DoesNotContain(DescriptionTexts(view), text => text.StartsWith("en:", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        });

        static IEnumerable<string> DescriptionTexts(Control root) =>
            root.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty)
                .Where(text => text.Contains("WorkspaceStrategy.HardLink.Description", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies the workspace strategy field shows why it is locked while the profile is running.
    /// </summary>
    /// <param name="isRunning">Whether the profile's game is running.</param>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void WorkspaceStrategy_ShowsLockedReasonOnlyWhileRunning(bool isRunning)
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => key);

        WithLocalization(localization.Object, () =>
        {
            var viewModel = CreateViewModel();
            viewModel.IsHotswapMode = isRunning;
            var view = new GameProfileGeneralSettingsView { DataContext = viewModel };
            var window = new Window { Width = 1100, Height = 900, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var reason = view.FindControl<TextBlock>("LaunchConfigurationLockedReason");
                Assert.NotNull(reason);
                var strategyPicker = Assert.Single(view.GetLogicalDescendants().OfType<ComboBox>(), combo => combo.Classes.Contains("workspace-strategy"));

                Assert.Equal("GameProfiles.General.LaunchConfigurationLocked", reason.Text);
                Assert.Equal(isRunning, reason.IsVisible);
                Assert.Equal(!isRunning, strategyPicker.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static string? SelectionBoxText(ComboBox picker) =>
        picker.GetVisualDescendants().OfType<ContentControl>()
            .Single(control => control.Name == "ContentPresenter")
            .GetVisualDescendants().OfType<TextBlock>().Single().Text;

    private static void WithLocalization(ILocalizationService localization, Action test)
    {
        var resources = Avalonia.Application.Current!.Resources;
        var hadPrevious = resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        resources[LocalizationConstants.ResourceServiceKey] = localization;
        try
        {
            test();
        }
        finally
        {
            if (hadPrevious)
            {
                resources[LocalizationConstants.ResourceServiceKey] = previous;
            }
            else
            {
                resources.Remove(LocalizationConstants.ResourceServiceKey);
            }
        }
    }

    private static GameProfileSettingsViewModel CreateViewModel() =>
        new(
            Mock.Of<IGameProfileManager>(),
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IConfigurationProviderService>(),
            Mock.Of<IProfileContentLoader>(),
            null,
            null,
            Mock.Of<IContentManifestPool>(),
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);
}
