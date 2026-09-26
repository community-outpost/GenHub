using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using GenHub.Common.Views;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Features.Content.ViewModels.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>
/// Verifies that operating system URL activations are routed through the command-line link handlers.
/// </summary>
public sealed class AppUrlActivationTests
{
    private const string LocalCatalogUrl = "file:///tmp/genhub-activation-catalog.json";

    /// <summary>
    /// Verifies that a subscription link received before the main window opens is handled once the window is ready.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_SubscribeLinkBeforeWindowReady_OpensDialogOnceReadyAsync()
    {
        var app = CreateApp();
        var dialogs = RecordDialogs(app);
        var mainWindow = new MainWindow();
        var link = new Uri($"genhub://subscribe?url={Uri.EscapeDataString(LocalCatalogUrl)}");

        var handling = app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));

        Assert.False(handling.IsCompleted);
        Assert.Empty(dialogs);

        app.MarkMainWindowReady(mainWindow);
        await handling;

        var (viewModel, owner) = Assert.Single(dialogs);
        Assert.Equal(LocalCatalogUrl, viewModel.CatalogUrlDisplay);
        Assert.Same(mainWindow, owner);
    }

    /// <summary>
    /// Verifies that a subscription link received after the main window opens is handled immediately.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_SubscribeLinkAfterWindowReady_OpensDialogAsync()
    {
        var app = CreateApp();
        var dialogs = RecordDialogs(app);
        app.MarkMainWindowReady(new MainWindow());
        var link = new Uri($"genhub://subscribe?url={Uri.EscapeDataString(LocalCatalogUrl)}");

        await app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));

        var (viewModel, _) = Assert.Single(dialogs);
        Assert.Equal(LocalCatalogUrl, viewModel.CatalogUrlDisplay);
    }

    /// <summary>
    /// Verifies that a subscription link pointing at a disallowed scheme is rejected by the existing validation.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_SubscribeLinkWithDisallowedTarget_DoesNotOpenDialogAsync()
    {
        var app = CreateApp();
        var dialogs = RecordDialogs(app);
        app.MarkMainWindowReady(new MainWindow());
        var link = new Uri($"genhub://subscribe?url={Uri.EscapeDataString("ftp://example.com/catalog.json")}");

        await app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));

        Assert.Empty(dialogs);
    }

    /// <summary>
    /// Verifies that activations for other schemes are ignored without waiting for the main window.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_OtherScheme_IsIgnoredAsync()
    {
        var app = CreateApp();
        var dialogs = RecordDialogs(app);
        var link = new Uri($"https://example.com/subscribe?url={Uri.EscapeDataString(LocalCatalogUrl)}");

        var handling = app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));

        Assert.True(handling.IsCompleted);
        await handling;
        Assert.Empty(dialogs);
    }

    /// <summary>
    /// Verifies that activations that carry no URL are ignored without waiting for the main window.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_NonProtocolActivation_IsIgnoredAsync()
    {
        var app = CreateApp();
        var dialogs = RecordDialogs(app);

        var handling = app.HandleUrlActivationAsync(new ActivatedEventArgs(ActivationKind.Reopen));

        Assert.True(handling.IsCompleted);
        await handling;
        Assert.Empty(dialogs);
    }

    private static global::GenHub.App CreateApp()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IUserSettingsService>());
        services.AddSingleton(Mock.Of<IConfigurationProviderService>());
        services.AddSingleton(Mock.Of<ILocalizationService>());
        services.AddSingleton(Mock.Of<IProfileLauncherFacade>());
        services.AddSingleton(Mock.Of<IPublisherSubscriptionStore>());
        services.AddSingleton(Mock.Of<IPublisherCatalogParser>());
        services.AddSingleton(httpClientFactory.Object);
        services.AddSingleton(Mock.Of<INotificationService>());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        return new global::GenHub.App(services.BuildServiceProvider());
    }

    private static List<(SubscriptionConfirmationViewModel ViewModel, Window? Owner)> RecordDialogs(global::GenHub.App app)
    {
        var dialogs = new List<(SubscriptionConfirmationViewModel ViewModel, Window? Owner)>();
        app.ShowSubscriptionDialogAsync = (vm, owner) =>
        {
            dialogs.Add((vm, owner));
            return Task.FromResult(false);
        };

        return dialogs;
    }
}
