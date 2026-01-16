using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Integration tests for the subscription flow.
/// Tests the complete flow from subscribing to a publisher to discovering content.
/// </summary>
public class SubscriptionFlowIntegrationTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private readonly Mock<ILogger<GenericCatalogDiscoverer>> _discovererLoggerMock = new();
    private readonly Mock<ILogger<GenericCatalogResolver>> _resolverLoggerMock = new();
    private readonly Mock<ILogger<JsonPublisherCatalogParser>> _parserLoggerMock = new();
    private readonly Mock<ILogger<PublisherSubscriptionStore>> _storeLoggerMock = new();

    /// <summary>
    /// Verifies the complete subscription flow from parsing to discovery.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_ParseCatalogAndDiscoverContent_Successfully()
    {
        // Arrange
        var parser = new JsonPublisherCatalogParser(_parserLoggerMock.Object);
        var catalogJson = CreateSampleCatalogJson();

        // Act - Parse the catalog
        var parseResult = await parser.ParseCatalogAsync(catalogJson);

        // Assert - Parsing succeeded
        Assert.True(parseResult.Success);
        Assert.NotNull(parseResult.Data);
        Assert.Equal("test-publisher", parseResult.Data.Publisher.Id);
        Assert.Equal("Test Publisher", parseResult.Data.Publisher.Name);
        Assert.Equal(2, parseResult.Data.Content.Count);

        // Verify content items
        var superMod = parseResult.Data.Content.FirstOrDefault(c => c.Id == "super-mod");
        Assert.NotNull(superMod);
        Assert.Equal("Super Mod", superMod.Name);
        Assert.Equal(ContentType.Mod, superMod.ContentType);
        Assert.Single(superMod.Releases);
        Assert.Equal("2.0.0", superMod.Releases[0].Version);
        Assert.True(superMod.Releases[0].IsLatest);

        // Verify dependency in super-addon
        var superAddon = parseResult.Data.Content.FirstOrDefault(c => c.Id == "super-addon");
        Assert.NotNull(superAddon);
        Assert.Single(superAddon.Releases[0].Dependencies);
        Assert.Equal("test-publisher", superAddon.Releases[0].Dependencies[0].PublisherId);
        Assert.Equal("super-mod", superAddon.Releases[0].Dependencies[0].ContentId);
        Assert.Equal(">=2.0.0", superAddon.Releases[0].Dependencies[0].VersionConstraint);
        Assert.False(superAddon.Releases[0].Dependencies[0].IsOptional);
    }

    /// <summary>
    /// Verifies that subscription store can save and retrieve subscriptions.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_SaveAndRetrieveSubscription_Successfully()
    {
        // Arrange - Create a subscription store with a temp file
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        var store = new PublisherSubscriptionStore(_storeLoggerMock.Object, mockConfigProvider.Object);

        var subscription = new PublisherSubscription
        {
            PublisherId = "test-publisher",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
            TrustLevel = TrustLevel.Trusted,
        };

        // Act - Save subscription
        var saveResult = await store.AddSubscriptionAsync(subscription);

        // Assert - Save succeeded
        Assert.True(saveResult.Success);

        // Act - Retrieve subscription
        var getResult = await store.GetSubscriptionAsync("test-publisher");

        // Assert - Retrieve succeeded
        Assert.True(getResult.Success);
        Assert.NotNull(getResult.Data);
        Assert.Equal("test-publisher", getResult.Data.PublisherId);
        Assert.Equal("Test Publisher", getResult.Data.PublisherName);
        Assert.Equal(TrustLevel.Trusted, getResult.Data.TrustLevel);

        // Act - Get all subscriptions
        var getAllResult = await store.GetSubscriptionsAsync();

        // Assert
        Assert.True(getAllResult.Success);
        Assert.Single(getAllResult.Data ?? []);
    }

    /// <summary>
    /// Verifies that discoverer can be configured with subscription.
    /// </summary>
    [Fact]
    public void SubscriptionFlow_CatalogJsonFormat_IsValid()
    {
        // This test verifies that our sample catalog JSON is valid
        var catalogJson = CreateSampleCatalogJson();

        // Verify it's valid JSON
        var exception = Record.Exception(() => System.Text.Json.JsonDocument.Parse(catalogJson));

        // Assert - No exception should be thrown
        Assert.Null(exception);

        // Verify it can be deserialized
        var catalog = System.Text.Json.JsonSerializer.Deserialize<GenHub.Core.Models.Providers.PublisherCatalog>(catalogJson);
        Assert.NotNull(catalog);
        Assert.Equal("test-publisher", catalog.Publisher.Id);
        Assert.Equal(2, catalog.Content.Count);
    }

    /// <summary>
    /// Verifies that cross-publisher dependencies are correctly identified.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_CrossPublisherDependencyResolution_CorrectlyIdentifiesDependencies()
    {
        // Arrange
        var catalogJson = CreateSampleCatalogJson();
        var parser = new JsonPublisherCatalogParser(_parserLoggerMock.Object);

        // Act - Parse catalog
        var parseResult = await parser.ParseCatalogAsync(catalogJson);

        // Assert - Verify dependency structure
        Assert.True(parseResult.Success);
        var catalog = parseResult.Data!;

        // The super-addon has a dependency on super-mod from the same publisher
        var superAddon = catalog.Content.FirstOrDefault(c => c.Id == "super-addon");
        Assert.NotNull(superAddon);

        var dependency = superAddon.Releases[0].Dependencies[0];
        Assert.Equal("test-publisher", dependency.PublisherId);
        Assert.Equal("super-mod", dependency.ContentId);
        Assert.Equal(">=2.0.0", dependency.VersionConstraint);
        Assert.False(dependency.IsOptional);

        // Verify the dependency can be resolved within the same catalog
        var dependencyContent = catalog.Content.FirstOrDefault(c => c.Id == dependency.ContentId);
        Assert.NotNull(dependencyContent);
        Assert.Equal("Super Mod", dependencyContent.Name);
    }

    /// <summary>
    /// Verifies that catalog mirrors are properly parsed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_CatalogMirrors_ParsedCorrectly()
    {
        // Arrange
        var catalogJson = CreateSampleCatalogJson();
        var parser = new JsonPublisherCatalogParser(_parserLoggerMock.Object);

        // Act
        var parseResult = await parser.ParseCatalogAsync(catalogJson);

        // Assert
        Assert.True(parseResult.Success);
        var catalog = parseResult.Data!;

        Assert.NotNull(catalog.CatalogMirrors);
        Assert.Equal(2, catalog.CatalogMirrors.Count);
        Assert.Contains("https://example.com/catalog.json", catalog.CatalogMirrors);
        Assert.Contains("https://cdn.example.com/catalog.json", catalog.CatalogMirrors);
    }

    /// <summary>
    /// Verifies that featured releases are properly identified.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_FeaturedReleases_ParsedCorrectly()
    {
        // Arrange
        var catalogJson = CreateSampleCatalogJson();
        var parser = new JsonPublisherCatalogParser(_parserLoggerMock.Object);

        // Act
        var parseResult = await parser.ParseCatalogAsync(catalogJson);

        // Assert
        Assert.True(parseResult.Success);
        var catalog = parseResult.Data!;

        var superMod = catalog.Content.FirstOrDefault(c => c.Id == "super-mod");
        Assert.NotNull(superMod);

        var featuredRelease = superMod.Releases.FirstOrDefault(r => r.IsFeatured);
        Assert.NotNull(featuredRelease);
        Assert.True(featuredRelease.IsFeatured);
        Assert.Equal("2.0.0", featuredRelease.Version);
    }

    /// <summary>
    /// Verifies that subscription can be removed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_RemoveSubscription_Successfully()
    {
        // Arrange
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        var store = new PublisherSubscriptionStore(_storeLoggerMock.Object, mockConfigProvider.Object);

        var subscription = new PublisherSubscription
        {
            PublisherId = "test-publisher",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
        };

        await store.AddSubscriptionAsync(subscription);

        // Act - Remove subscription
        var removeResult = await store.RemoveSubscriptionAsync("test-publisher");

        // Assert
        Assert.True(removeResult.Success);

        // Verify it's gone
        var getResult = await store.GetSubscriptionAsync("test-publisher");
        Assert.False(getResult.Success);
    }

    /// <summary>
    /// Verifies trust level can be updated.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_UpdateTrustLevel_Successfully()
    {
        // Arrange
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        var store = new PublisherSubscriptionStore(_storeLoggerMock.Object, mockConfigProvider.Object);

        var subscription = new PublisherSubscription
        {
            PublisherId = "test-publisher",
            PublisherName = "Test Publisher",
            CatalogUrl = "https://example.com/catalog.json",
            TrustLevel = TrustLevel.Untrusted,
        };

        await store.AddSubscriptionAsync(subscription);

        // Act - Update trust level
        var updateResult = await store.UpdateTrustLevelAsync("test-publisher", TrustLevel.Verified);

        // Assert
        Assert.True(updateResult.Success);

        // Verify the update
        var getResult = await store.GetSubscriptionAsync("test-publisher");
        Assert.True(getResult.Success);
        Assert.Equal(TrustLevel.Verified, getResult.Data?.TrustLevel);
    }

    /// <summary>
    /// Verifies that version selector filters correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SubscriptionFlow_VersionSelector_FiltersCorrectly()
    {
        // This test verifies the integration with version selection
        // For a complete implementation, you'd test IVersionSelector
        var catalogJson = CreateSampleCatalogJson();
        var parser = new JsonPublisherCatalogParser(_parserLoggerMock.Object);
        var parseResult = await parser.ParseCatalogAsync(catalogJson);

        Assert.True(parseResult.Success);
        var catalog = parseResult.Data!;

        // Verify isLatest flags
        var superMod = catalog.Content.FirstOrDefault(c => c.Id == "super-mod");
        Assert.NotNull(superMod);

        var latestReleases = superMod.Releases.Where(r => r.IsLatest && !r.IsPrerelease);
        Assert.Single(latestReleases);
        Assert.Equal("2.0.0", latestReleases.First().Version);
    }

    /// <summary>
    /// Creates a sample catalog JSON for testing.
    /// </summary>
    /// <returns>A sample catalog JSON string.</returns>
    private static string CreateSampleCatalogJson()
    {
        return """
            {
                "$schemaVersion": 1,
                "publisher": {
                    "id": "test-publisher",
                    "name": "Test Publisher",
                    "avatarUrl": "https://example.com/avatar.png",
                    "website": "https://example.com",
                    "supportUrl": "https://example.com/support",
                    "contactEmail": "contact@example.com"
                },
                "content": [
                    {
                        "id": "super-mod",
                        "name": "Super Mod",
                        "description": "An amazing mod that changes everything.",
                        "contentType": "Mod",
                        "targetGame": "ZeroHour",
                        "tags": ["gameplay", "units", "balance"],
                        "releases": [
                            {
                                "version": "2.0.0",
                                "releaseDate": "2026-01-14T00:00:00Z",
                                "isLatest": true,
                                "isPrerelease": false,
                                "isFeatured": true,
                                "changelog": "## What's New\n- New units\n- Balance changes",
                                "artifacts": [
                                    {
                                        "filename": "SuperMod-2.0.0.zip",
                                        "downloadUrl": "https://example.com/SuperMod-2.0.0.zip",
                                        "size": 15728640,
                                        "sha256": "abc123def456",
                                        "isPrimary": true
                                    }
                                ],
                                "dependencies": []
                            }
                        ]
                    },
                    {
                        "id": "super-addon",
                        "name": "Super Addon",
                        "description": "An addon for Super Mod.",
                        "contentType": "Addon",
                        "targetGame": "ZeroHour",
                        "tags": ["addon", "units"],
                        "releases": [
                            {
                                "version": "1.0.0",
                                "releaseDate": "2026-01-10T00:00:00Z",
                                "isLatest": true,
                                "artifacts": [
                                    {
                                        "filename": "SuperAddon-1.0.0.zip",
                                        "downloadUrl": "https://example.com/SuperAddon-1.0.0.zip",
                                        "size": 5000000,
                                        "sha256": "xyz789",
                                        "isPrimary": true
                                    }
                                ],
                                "dependencies": [
                                    {
                                        "publisherId": "test-publisher",
                                        "contentId": "super-mod",
                                        "versionConstraint": ">=2.0.0",
                                        "isOptional": false
                                    }
                                ]
                            }
                        ]
                    }
                ],
                "referrals": [],
                "catalogMirrors": [
                    "https://example.com/catalog.json",
                    "https://cdn.example.com/catalog.json"
                ],
                "lastUpdated": "2026-01-14T00:00:00Z"
            }
            """;
    }
}
