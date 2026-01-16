using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools;

/// <summary>
/// Integration tests for Publisher Studio and Subscription Store end-to-end workflow.
/// </summary>
public class PublisherStudioIntegrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly PublisherStudioService _publisherService;
    private readonly PublisherSubscriptionStore _subscriptionStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherStudioIntegrationTests"/> class.
    /// </summary>
    public PublisherStudioIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "GenHubPublisherTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);

        // Setup Publisher Service dependencies
        var loggerMock = new Mock<ILogger<PublisherStudioService>>();
        var catalogParserMock = new Mock<IPublisherCatalogParser>();

        // Setup catalog parser mock to return success for any parsing (since we trust export serialization logic for now)
        catalogParserMock.Setup(x => x.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<PublisherCatalog>.CreateSuccess(new PublisherCatalog()));

        _publisherService = new PublisherStudioService(loggerMock.Object, catalogParserMock.Object);

        // Setup Subscription Store dependencies
        var subStoreLoggerMock = new Mock<ILogger<PublisherSubscriptionStore>>();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        configProviderMock.Setup(x => x.GetApplicationDataPath()).Returns(_testDirectory);

        _subscriptionStore = new PublisherSubscriptionStore(subStoreLoggerMock.Object, configProviderMock.Object);
    }

    /// <summary>
    /// Disposes of the test resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests the end-to-end flow of creating a project, exporting a catalog, and subscribing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task EndToEnd_CreateProject_ExportCatalog_Subscribe()
    {
        // 1. Create a Project
        var createResult = await _publisherService.CreateProjectAsync("Test Project");
        Assert.True(createResult.Success);
        var project = createResult.Data;
        Assert.NotNull(project);

        project.ProjectPath = Path.Combine(_testDirectory, "project.json");
        project.Catalog.Publisher.Id = "test-publisher";
        project.Catalog.Publisher.Name = "Test Publisher";
        project.Catalog.Publisher.WebsiteUrl = "https://example.com";

        // Add some content
        var content = new CatalogContentItem
        {
            Id = "awesome-mod",
            Name = "Awesome Mod",
            Description = "A test mod",
        };
        project.Catalog.Content.Add(content);

        // 2. Export Catalog
        var exportResult = await _publisherService.ExportCatalogAsync(project);
        Assert.True(exportResult.Success);
        var catalogJson = exportResult.Data;
        Assert.NotEmpty(catalogJson);

        // Save catalog to a file (simulating hosting)
        var catalogUrl = "https://example.com/catalog.json"; // Mock URL for subscription

        // 3. Subscribe to the Publisher
        var subscription = new PublisherSubscription
        {
            PublisherId = project.Catalog.Publisher.Id,
            PublisherName = project.Catalog.Publisher.Name,
            CatalogUrl = catalogUrl, // In real scenario, this would be the hosted URL
            Added = DateTime.UtcNow,
            TrustLevel = GenHub.Core.Models.Enums.TrustLevel.Trusted,
        };

        var addResult = await _subscriptionStore.AddSubscriptionAsync(subscription);
        Assert.True(addResult.Success, $"Failed to add subscription: {addResult.FirstError}");

        // 4. Verify Subscription is Persisted
        var getResult = await _subscriptionStore.GetSubscriptionsAsync();
        Assert.True(getResult.Success);
        Assert.Contains(getResult.Data, s => s.PublisherId == "test-publisher");

        var storedSub = await _subscriptionStore.GetSubscriptionAsync("test-publisher");
        Assert.True(storedSub.Success);
        Assert.NotNull(storedSub.Data);
        Assert.Equal("Test Publisher", storedSub.Data.PublisherName);
    }

    /// <summary>
    /// Disposes of the test resources.
    /// </summary>
    /// <param name="disposing">True if disposing, false if finalizing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
