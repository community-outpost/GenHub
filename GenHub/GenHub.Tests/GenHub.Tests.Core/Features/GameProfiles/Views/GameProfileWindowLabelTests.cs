using Avalonia;
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
    private const double MinimumWindowWidth = 600;

    private static readonly ResourceManager Resources =
        new(LocalizationConstants.StringResourceBaseName, typeof(LocalizationService).Assembly);

    /// <summary>
    /// Verifies each top tab shows its translated label inside the navigation bar at the window's minimum width.
    /// </summary>
    /// <param name="cultureName">The shipped culture to render.</param>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("ar")]
    [InlineData("ru")]
    public void NavigationTabs_ShowTranslatedLabelsWithinBarAtMinimumWidth(string cultureName)
    {
        var resourceSet = Resources.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false)!;
        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => resourceSet.GetString(key) ?? key);
        string[] keys = ["GameProfiles.Navigation.Content", "GameProfiles.Navigation.ProfileSettings", "GameProfiles.Navigation.GameSettings"];

        WithLocalization(localization.Object, () =>
        {
            var view = new GameProfileNavigationView { DataContext = CreateViewModel(), Margin = new Thickness(24, 0, 24, 12) };
            var window = new Window { Width = MinimumWindowWidth, Height = 200, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var tabs = view.GetLogicalDescendants().OfType<Button>().ToList();
                var labels = tabs.Select(tab =>
                {
                    Assert.Contains(tab.GetLogicalDescendants(), node => node is PathIcon);
                    return Assert.Single(tab.GetLogicalDescendants().OfType<TextBlock>());
                }).ToList();

                Assert.Equal(keys.Select(resourceSet.GetString), labels.Select(label => label.Text));
                Assert.All(tabs, tab =>
                {
                    var origin = tab.TranslatePoint(default, view);
                    Assert.NotNull(origin);
                    Assert.InRange(origin.Value.X, 0, view.Bounds.Width - tab.Bounds.Width);
                });
                Assert.All(labels, label =>
                {
                    Assert.True(label.IsEffectivelyVisible);
                    Assert.True(label.Bounds.Width >= label.DesiredSize.Width);
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

    private delegate bool TryGetStringCallback(string key, out string? value, object?[] arguments);
}
