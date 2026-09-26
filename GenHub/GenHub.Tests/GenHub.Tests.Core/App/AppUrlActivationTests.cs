using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using GenHub.Common.Services;
using GenHub.Common.Views;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.ViewModels.Catalog;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
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

    /// <summary>Startup and subsequent activations must never overlap dialogs.</summary>
    /// <returns>The asynchronous test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_WaitsForStartupAndPreviousActivationAsync()
    {
        var app = CreateApp();
        var startupClosed = new TaskCompletionSource<bool>();
        var activationOpened = new TaskCompletionSource<bool>();
        var activationClosed = new TaskCompletionSource<bool>();
        var dialogCount = 0;
        app.ShowSubscriptionDialogAsync = (_, _) =>
        {
            dialogCount++;
            if (dialogCount == 1)
            {
                return startupClosed.Task;
            }

            if (dialogCount == 2)
            {
                activationOpened.SetResult(true);
                return activationClosed.Task;
            }

            return Task.FromResult(false);
        };
        var link = new Uri($"genhub://subscribe?url={Uri.EscapeDataString(LocalCatalogUrl)}");
        var startup = app.CompleteWindowStartupAsync([link.OriginalString], new MainWindow());
        var first = app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));
        var second = app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));
        Assert.Equal(1, dialogCount);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        startupClosed.SetResult(false);
        await startup;
        await activationOpened.Task;
        Assert.Equal(2, dialogCount);
        activationClosed.SetResult(false);
        await Task.WhenAll(first, second);
        Assert.Equal(3, dialogCount);
    }

    /// <summary>Profile activation preserves escaped base64 characters through the import boundary.</summary>
    /// <returns>The asynchronous test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_ProfileImport_PreservesOriginalUriAsync()
    {
        const string original = "genhub://profile/import?data=%2B%2F8%3D";
        var sharing = new Mock<IProfileSharingService>();
        sharing.Setup(x => x.InspectSharedProfileAsync(original, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<SharedProfileInspectionResult>.CreateFailure("test inspection stop"));
        var launcher = new GameProfileLauncherViewModel(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            Mock.Of<INotificationService>(),
            null!,
            null!,
            NullLogger<GameProfileLauncherViewModel>.Instance,
            Mock.Of<ILocalizationService>(),
            profileSharingServiceFactory: () => sharing.Object);
        var app = CreateApp(launcher);
        app.MarkMainWindowReady(new MainWindow());

        await app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(new Uri(original)));

        sharing.Verify(x => x.InspectSharedProfileAsync(original, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A link that arrives before the window opens marks the session so Getting Started does not stack on the link dialog.</summary>
    /// <returns>The asynchronous test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_RecordsLinkBeforeWindowReadyAsync()
    {
        var tracker = new LinkActivationTracker();
        var app = CreateApp(linkActivationTracker: tracker);
        RecordDialogs(app);
        var link = new Uri($"genhub://subscribe?url={Uri.EscapeDataString(LocalCatalogUrl)}");

        var handling = app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));

        Assert.True(tracker.HasReceivedLink);
        app.MarkMainWindowReady(new MainWindow());
        await handling;
    }

    /// <summary>Startup arguments mark the session only when they carry a link.</summary>
    /// <param name="argument">The startup argument.</param>
    /// <param name="expected">Whether the session should count as opened for a link.</param>
    /// <returns>The asynchronous test.</returns>
    [AvaloniaTheory]
    [InlineData("genhub://subscribe?url=file%3A%2F%2F%2Ftmp%2Fgenhub-activation-catalog.json", true)]
    [InlineData("--verbose", false)]
    public async Task CompleteWindowStartupAsync_RecordsLinkArgumentsAsync(string argument, bool expected)
    {
        var tracker = new LinkActivationTracker();
        var app = CreateApp(linkActivationTracker: tracker);
        RecordDialogs(app);

        await app.CompleteWindowStartupAsync([argument], new MainWindow());

        Assert.Equal(expected, tracker.HasReceivedLink);
    }

    /// <summary>A link brings GenHub forward as one user-requested activation that restores a minimized window.</summary>
    /// <returns>The asynchronous test.</returns>
    [AvaloniaFact]
    public async Task HandleUrlActivationAsync_BringsMinimizedWindowForwardAsync()
    {
        var app = CreateApp();
        RecordDialogs(app);
        var mainWindow = new MainWindow { WindowState = WindowState.Minimized };
        app.MarkMainWindowReady(mainWindow);
        var link = new Uri($"genhub://subscribe?url={Uri.EscapeDataString(LocalCatalogUrl)}");

        await app.HandleUrlActivationAsync(new ProtocolActivatedEventArgs(link));

        Assert.Equal(WindowState.Normal, mainWindow.WindowState);
        Assert.False(WindowActivation.IsUserRequestInProgress);
    }

    private static global::GenHub.App CreateApp(GameProfileLauncherViewModel? launcher = null, ILinkActivationTracker? linkActivationTracker = null)
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
        if (launcher != null)
        {
            services.AddSingleton(launcher);
        }

        if (linkActivationTracker != null)
        {
            services.AddSingleton(linkActivationTracker);
        }

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
