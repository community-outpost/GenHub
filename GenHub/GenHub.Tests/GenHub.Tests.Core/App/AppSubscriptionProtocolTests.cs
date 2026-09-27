using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>
/// Verifies the end-to-end subscription handling wired through <see cref="GenHub.App.HandleSubscriptionUrlAsync"/>.
/// </summary>
public sealed class AppSubscriptionProtocolTests : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppSubscriptionProtocolTests"/> class.
    /// </summary>
    public AppSubscriptionProtocolTests()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = false;
    }

    private const string SampleCatalogJson = "{\"publisher\":{\"id\":\"sample-pub\",\"name\":\"Sample Publisher\"},\"content\":[]}";

    /// <summary>
    /// Verifies that HandleSubscriptionUrlAsync saves subscription and shows notification when confirmed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleSubscriptionUrlAsync_WhenConfirmed_SavesSubscriptionAndShowsNotificationAsync()
    {
        // Arrange
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "sample-pub",
                Name = "Sample Publisher",
            },
            Content = [],
        };

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        subscriptionStore
            .Setup(s => s.GetSubscriptionAsync("sample-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(null));
        subscriptionStore
            .Setup(s => s.IsSubscribedAsync("sample-pub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        subscriptionStore
            .Setup(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var catalogParser = new Mock<IPublisherCatalogParser>();
        catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        var notificationService = new Mock<INotificationService>();

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(new TestCatalogHttpHandler(SampleCatalogJson));
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IUserSettingsService>());
        services.AddSingleton(Mock.Of<IConfigurationProviderService>());
        services.AddSingleton(Mock.Of<ILocalizationService>());
        services.AddSingleton(Mock.Of<IProfileLauncherFacade>());
        services.AddSingleton(subscriptionStore.Object);
        services.AddSingleton(catalogParser.Object);
        services.AddSingleton(httpClientFactory.Object);
        services.AddSingleton(notificationService.Object);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var app = new global::GenHub.App(serviceProvider);

        // Inject headless dialog handler simulating confirmation
        app.ShowSubscriptionDialogAsync = async (vm, _) =>
        {
            await vm.InitializeAsync();
            await vm.ConfirmCommand.ExecuteAsync(null);
            return true;
        };

        // Act
        await app.HandleSubscriptionUrlAsync("genhub://subscribe?url=https%3A%2F%2Fexample.com%2Fcatalog.json");

        // Assert
        subscriptionStore.Verify(
            s => s.AddSubscriptionAsync(
                It.Is<PublisherSubscription>(sub => sub.PublisherId == "sample-pub" && sub.CatalogUrl == "https://example.com/catalog.json"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        notificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that HandleSubscriptionUrlAsync does not save or notify when cancelled.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleSubscriptionUrlAsync_WhenCancelled_DoesNotSaveSubscriptionOrNotifyAsync()
    {
        // Arrange
        var catalog = new PublisherCatalog
        {
            Publisher = new PublisherProfile
            {
                Id = "sample-pub",
                Name = "Sample Publisher",
            },
            Content = [],
        };

        var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
        var catalogParser = new Mock<IPublisherCatalogParser>();
        catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

        var notificationService = new Mock<INotificationService>();

        var httpClientFactory = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(new TestCatalogHttpHandler(SampleCatalogJson));
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IUserSettingsService>());
        services.AddSingleton(Mock.Of<IConfigurationProviderService>());
        services.AddSingleton(Mock.Of<ILocalizationService>());
        services.AddSingleton(Mock.Of<IProfileLauncherFacade>());
        services.AddSingleton(subscriptionStore.Object);
        services.AddSingleton(catalogParser.Object);
        services.AddSingleton(httpClientFactory.Object);
        services.AddSingleton(notificationService.Object);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var app = new global::GenHub.App(serviceProvider);

        // Inject headless dialog handler simulating cancellation
        app.ShowSubscriptionDialogAsync = async (vm, _) =>
        {
            await vm.InitializeAsync();
            return false;
        };

        // Act
        await app.HandleSubscriptionUrlAsync("genhub://subscribe?url=https%3A%2F%2Fexample.com%2Fcatalog.json");

        // Assert: No subscription saved, no success notification
        subscriptionStore.Verify(
            s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()),
            Times.Never);

        notificationService.Verify(
            n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that HandleSubscriptionUrlAsync processes and saves subscription for local file URIs.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleSubscriptionUrlAsync_LocalFileUri_ProcessesAndSavesSubscriptionAsync()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"genhub-sub-test-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, SampleCatalogJson);

        try
        {
            var catalog = new PublisherCatalog
            {
                Publisher = new PublisherProfile
                {
                    Id = "local-file-pub",
                    Name = "Local File Publisher",
                },
                Content = [],
            };

            var subscriptionStore = new Mock<IPublisherSubscriptionStore>();
            subscriptionStore
                .Setup(s => s.GetSubscriptionAsync("local-file-pub", It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(null));
            subscriptionStore
                .Setup(s => s.IsSubscribedAsync("local-file-pub", It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
            subscriptionStore
                .Setup(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

            var catalogParser = new Mock<IPublisherCatalogParser>();
            catalogParser
                .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(catalog));

            var notificationService = new Mock<INotificationService>();
            var httpClientFactory = new Mock<IHttpClientFactory>();
            httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

            var services = new ServiceCollection();
            services.AddSingleton(Mock.Of<IUserSettingsService>());
            services.AddSingleton(Mock.Of<IConfigurationProviderService>());
            services.AddSingleton(Mock.Of<ILocalizationService>());
            services.AddSingleton(Mock.Of<IProfileLauncherFacade>());
            services.AddSingleton(subscriptionStore.Object);
            services.AddSingleton(catalogParser.Object);
            services.AddSingleton(httpClientFactory.Object);
            services.AddSingleton(notificationService.Object);
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddLogging();

            var app = new global::GenHub.App(services.BuildServiceProvider());

            app.ShowSubscriptionDialogAsync = async (vm, _) =>
            {
                await vm.InitializeAsync();
                await vm.ConfirmCommand.ExecuteAsync(null);
                return true;
            };

            var fileUri = new Uri(tempFile).AbsoluteUri;

            // Act
            await app.HandleSubscriptionUrlAsync($"genhub://subscribe?url={Uri.EscapeDataString(fileUri)}");

            // Assert
            subscriptionStore.Verify(
                s => s.AddSubscriptionAsync(
                    It.Is<PublisherSubscription>(sub => sub.PublisherId == "local-file-pub"),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            notificationService.Verify(
                n => n.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that HandleSubscriptionUrlAsync does not open dialog for disallowed schemes.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleSubscriptionUrlAsync_DisallowedScheme_DoesNotOpenDialogAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IUserSettingsService>());
        services.AddSingleton(Mock.Of<IConfigurationProviderService>());
        services.AddSingleton(Mock.Of<ILocalizationService>());
        services.AddSingleton(Mock.Of<IProfileLauncherFacade>());
        services.AddSingleton(Mock.Of<IPublisherSubscriptionStore>());
        services.AddSingleton(Mock.Of<IPublisherCatalogParser>());
        services.AddSingleton(Mock.Of<IHttpClientFactory>());
        services.AddSingleton(Mock.Of<INotificationService>());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        var app = new global::GenHub.App(services.BuildServiceProvider());
        var dialogOpened = false;
        app.ShowSubscriptionDialogAsync = (_, _) =>
        {
            dialogOpened = true;
            return Task.FromResult(false);
        };

        // Act
        await app.HandleSubscriptionUrlAsync("genhub://subscribe?url=ftp%3A%2F%2Fmalicious.com%2Fcatalog.json");

        // Assert
        Assert.False(dialogOpened);
    }

    private sealed class TestCatalogHttpHandler(string catalogJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(catalogJson, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
