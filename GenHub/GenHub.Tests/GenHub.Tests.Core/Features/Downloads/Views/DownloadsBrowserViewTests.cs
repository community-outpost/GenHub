using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Views;

/// <summary>
/// Headless tests for downloads sidebar pane auto-fit behavior.
/// </summary>
public class DownloadsBrowserViewTests
{
    /// <summary>
    /// Verifies that a long publisher name widens the sidebar beyond its default width.
    /// </summary>
    [AvaloniaFact]
    public void Publishers_WithLongDisplayName_ExpandsPaneBeyondDefault()
    {
        using var viewModel = CreateViewModel();
        viewModel.Publishers = new ObservableCollection<PublisherItemViewModel>(
        [
            new("generals-online", "Generals Online"),
            new("long-catalog", new string('M', 40)),
        ]);

        var view = new DownloadsBrowserView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        try
        {
            Assert.True(viewModel.OpenPaneLength > SidebarConstants.DefaultOpenPaneLength);
            Assert.True(viewModel.OpenPaneLength <= SidebarConstants.MaxPaneLength);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that auto-fit never exceeds the sidebar maximum width.
    /// </summary>
    [AvaloniaFact]
    public void Publishers_WithExtremelyLongDisplayName_ClampsToMaxPaneLength()
    {
        using var viewModel = CreateViewModel();
        viewModel.Publishers = new ObservableCollection<PublisherItemViewModel>(
        [
            new("huge-catalog", new string('M', 200)),
        ]);

        var view = new DownloadsBrowserView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        try
        {
            Assert.Equal(SidebarConstants.MaxPaneLength, viewModel.OpenPaneLength);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that short publisher names leave the default pane width untouched.
    /// </summary>
    [AvaloniaFact]
    public void Publishers_WithShortDisplayNames_KeepsDefaultPaneLength()
    {
        using var viewModel = CreateViewModel();
        viewModel.Publishers = new ObservableCollection<PublisherItemViewModel>(
        [
            new("mods", "Mods"),
            new("maps", "Maps"),
        ]);

        var view = new DownloadsBrowserView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        try
        {
            Assert.Equal(SidebarConstants.DefaultOpenPaneLength, viewModel.OpenPaneLength);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that auto-fit only widens the pane and never shrinks a wider pane.
    /// </summary>
    [AvaloniaFact]
    public void Publishers_ReplacedWithShorterNames_KeepsWiderPane()
    {
        using var viewModel = CreateViewModel();
        viewModel.Publishers = new ObservableCollection<PublisherItemViewModel>(
        [
            new("huge-catalog", new string('M', 200)),
        ]);

        var view = new DownloadsBrowserView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        try
        {
            Assert.Equal(SidebarConstants.MaxPaneLength, viewModel.OpenPaneLength);

            viewModel.Publishers = new ObservableCollection<PublisherItemViewModel>(
            [
                new("mods", "Mods"),
            ]);

            Assert.Equal(SidebarConstants.MaxPaneLength, viewModel.OpenPaneLength);
        }
        finally
        {
            window.Close();
        }
    }

    private static DownloadsBrowserViewModel CreateViewModel()
    {
        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(store => store.GetSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess([]));

        return new DownloadsBrowserViewModel(
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<DownloadsBrowserViewModel>>().Object,
            [],
            new Mock<IContentStateService>().Object,
            new Mock<IContentOrchestrator>().Object,
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ILoggerFactory>().Object,
            subscriptionStore.Object);
    }
}
