using System.Net.Http;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.GitHub;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests for DownloadsViewModel.
/// </summary>
public class DownloadsViewModelTests
{
    /// <summary>
    /// Ensures InitializeAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_PopulatesVersionsAndCards()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<DownloadsViewModel>>();
        var mockNotificationService = new Mock<INotificationService>();

        // Mock PublisherCardViewModel with correct constructor arguments
        mockServiceProvider.Setup(x => x.GetService(typeof(PublisherCardViewModel)))
            .Returns(() => new PublisherCardViewModel(
                new Mock<ILogger<PublisherCardViewModel>>().Object,
                new Mock<IContentOrchestrator>().Object,
                new Mock<IContentManifestPool>().Object,
                new Mock<IGameClientProfileService>().Object,
                new Mock<IProfileContentService>().Object,
                new Mock<IGameProfileManager>().Object,
                new Mock<INotificationService>().Object));

        // Mock Discoverers with correct constructor arguments
        var mockGeneralsOnline = new Mock<GeneralsOnlineDiscoverer>(
            new Mock<ILogger<GeneralsOnlineDiscoverer>>().Object,
            new Mock<IProviderDefinitionLoader>().Object,
            new Mock<ICatalogParserFactory>().Object,
            new Mock<IHttpClientFactory>().Object);

        var result = OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
        {
            Items = [new ContentSearchResult { Version = "1.0", ContentType = GenHub.Core.Models.Enums.ContentType.Mod },],
        });

        mockGeneralsOnline.Setup(x => x.DiscoverAsync(It.IsAny<ProviderDefinition?>(), It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        mockServiceProvider.Setup(x => x.GetService(typeof(GenHub.Features.Content.Services.GeneralsOnline.GeneralsOnlineDiscoverer)))
            .Returns(mockGeneralsOnline.Object);

        var discoverer = new GitHubTopicsDiscoverer(
            new Mock<IGitHubApiClient>().Object,
            new Mock<ILogger<GitHubTopicsDiscoverer>>().Object);

        var vm = new DownloadsViewModel(
            mockServiceProvider.Object,
            mockLogger.Object,
            mockNotificationService.Object,
            discoverer);

        // Act
        await vm.InitializeAsync();

        // Assert
        Assert.NotEmpty(vm.PublisherCards);
        Assert.Equal("v1.0", vm.GeneralsOnlineVersion);
    }
}