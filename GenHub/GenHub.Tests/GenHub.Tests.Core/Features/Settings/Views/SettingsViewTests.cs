using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Features.Settings.ViewModels;
using GenHub.Features.Settings.Views;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Settings.Views;

/// <summary>
/// Headless tests for settings sidebar navigation and section scrolling.
/// </summary>
public class SettingsViewTests
{
    /// <summary>
    /// Verifies that selecting a collapsed bottom section scrolls it fully into view on the
    /// first click instead of stopping partway with a stale scroll extent.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task SelectingCollapsedBottomSection_ScrollsItFullyIntoViewAsync()
    {
        using var viewModel = CreateViewModel();
        var view = new SettingsView { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var updates = viewModel.Sections.First(section => section.Id == SettingsConstants.SectionUpdates);
            var expander = view.FindControl<Expander>("Expander_Updates");
            var scrollViewer = view.FindControl<ScrollViewer>("SettingsScrollViewer");
            Assert.NotNull(expander);
            Assert.NotNull(scrollViewer);
            Assert.False(expander.IsExpanded);

            viewModel.SelectedSection = updates;

            await WaitForScrollToSettleAsync(scrollViewer, expander);

            Assert.True(expander.IsExpanded);
            var content = Assert.IsAssignableFrom<Control>(scrollViewer.Content);
            var transform = expander.TransformToVisual(content);
            Assert.True(transform.HasValue);
            var position = transform.Value.Transform(new Point(0, 0));
            var maxScrollY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            var expected = Math.Clamp(position.Y, 0, maxScrollY);
            Assert.InRange(scrollViewer.Offset.Y, expected - 2, expected + 2);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Polls the dispatcher until the scroll offset converges on the expanded section target
    /// and holds there, instead of sleeping a fixed delay that can race slow CI agents.
    /// </summary>
    /// <param name="scrollViewer">The settings scroll viewer.</param>
    /// <param name="expander">The expanded section anchor.</param>
    /// <returns>A task representing the asynchronous wait operation.</returns>
    private static async Task WaitForScrollToSettleAsync(ScrollViewer scrollViewer, Expander expander)
    {
        var content = Assert.IsAssignableFrom<Control>(scrollViewer.Content);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var settledPolls = 0;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            var transform = expander.TransformToVisual(content);
            if (transform.HasValue)
            {
                var position = transform.Value.Transform(new Point(0, 0));
                var maxScrollY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                var expected = Math.Clamp(position.Y, 0, maxScrollY);
                settledPolls = Math.Abs(scrollViewer.Offset.Y - expected) <= 2 ? settledPolls + 1 : 0;
                if (settledPolls >= 3)
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static SettingsViewModel CreateViewModel()
    {
        var configService = new Mock<IUserSettingsService>();
        configService.Setup(x => x.Get()).Returns(new UserSettings());

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
            new Mock<IConfigurationProviderService>().Object,
            new Mock<IGameInstallationService>().Object,
            new Mock<IStorageLocationService>().Object,
            new Mock<IUserDataTracker>().Object,
            new Mock<IDialogService>().Object,
            new Mock<IStorageMigrationService>().Object);
    }
}
