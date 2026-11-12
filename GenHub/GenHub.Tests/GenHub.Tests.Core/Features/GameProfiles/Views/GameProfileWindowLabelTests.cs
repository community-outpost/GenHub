using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>
/// Headless tests for the visible labels in the profile settings window.
/// </summary>
public class GameProfileWindowLabelTests
{
    /// <summary>
    /// Verifies each top tab shows a text label next to its icon.
    /// </summary>
    [AvaloniaFact]
    public void NavigationTabs_EachShowAVisibleTextLabel()
    {
        WithKeyLocalization(() =>
        {
            var view = new GameProfileNavigationView { DataContext = CreateViewModel() };
            var window = new Window { Width = 800, Height = 200, Content = view };
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

                Assert.Equal(
                    ["GameProfiles.Navigation.Content", "GameProfiles.Navigation.ProfileSettings", "GameProfiles.Navigation.GameSettings"],
                    labels.Select(label => label.Text));
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
    /// Verifies the workspace strategy field shows why it is locked while the profile is running.
    /// </summary>
    /// <param name="isRunning">Whether the profile's game is running.</param>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void WorkspaceStrategy_ShowsLockedReasonOnlyWhileRunning(bool isRunning)
    {
        WithKeyLocalization(() =>
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

    private static void WithKeyLocalization(Action test)
    {
        var resources = Avalonia.Application.Current!.Resources;
        var hadPrevious = resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => key);
        resources[LocalizationConstants.ResourceServiceKey] = localization.Object;
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
