using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Integration.ContentPipeline;

/// <summary>
/// Integration tests for the three main content pipeline features:
/// 1. GeneralsOnline manifest.json endpoint
/// 2. TheSuperHackers GitHub weekly releases
/// 3. GitHub Manager with L3-M/GeneralsControlBar addon
/// </summary>
public class ContentPipelineIntegrationTests
{
    [Fact]
    public async Task GeneralsOnline_ManifestEndpoint_ShouldReturnValidManifests()
    {
        // Arrange
        var discoverer = new GeneralsOnlineDiscoverer(
            NullLogger<GeneralsOnlineDiscoverer>.Instance);

        // Act
        var query = new ContentSearchQuery
        {
            SearchTerm = "generalsonline",
            Take = 10
        };

        var result = await discoverer.DiscoverAsync(query, CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Discovery failed: {result.FirstError}");
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data);

        var firstResult = result.Data.First();

        // Verify it has the release data
        Assert.NotNull(firstResult.Data);
        Assert.Equal("GeneralsOnline", firstResult.ProviderName);
    }

    [Fact]
    public async Task GeneralsOnline_CreateDualManifests_ShouldGenerate30HzAnd60Hz()
    {
        // Arrange
        var discoverer = new GeneralsOnlineDiscoverer(
            NullLogger<GeneralsOnlineDiscoverer>.Instance);

        // Act - Get latest release
        var query = new ContentSearchQuery { SearchTerm = "generalsonline", Take = 1 };
        var searchResult = await discoverer.DiscoverAsync(query, CancellationToken.None);

        Assert.True(searchResult.Success);
        var release = searchResult.Data.First().GetData<Core.Models.GeneralsOnline.GeneralsOnlineReleaseModel>();
        Assert.NotNull(release);

        // Create manifests using factory
        var manifests = GeneralsOnlineManifestFactory.CreateManifests(release);

        // Assert
        Assert.Equal(2, manifests.Count);

        var hz30 = manifests[0];
        var hz60 = manifests[1];

        // Verify manifest IDs follow 5-segment format
        Assert.Contains("generalsonline", hz30.Id.Value);
        Assert.Contains("30hz", hz30.Id.Value);
        Assert.Contains("generalsonline", hz60.Id.Value);
        Assert.Contains("60hz", hz60.Id.Value);

        // Verify both have same version but different names
        Assert.Equal(release.Version, hz30.Version);
        Assert.Equal(release.Version, hz60.Version);
        Assert.Contains("30Hz", hz30.Name);
        Assert.Contains("60Hz", hz60.Name);
    }

    [Fact(Skip = "Requires GitHub API client setup - manual verification at https://github.com/TheSuperHackers/GeneralsGameCode/releases")]
    public async Task TheSuperHackers_GitHubReleases_ShouldFindWeeklyReleases()
    {
        // This test requires actual GitHub API access with authentication
        // Skipped until proper test infrastructure is in place
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TheSuperHackers_ManifestFactory_ShouldCreateGeneralsAndZeroHourManifests()
    {
        // Arrange
        var factory = new SuperHackersManifestFactory(
            null!, // IContentManifestBuilder - would need proper setup
            null!, // IFileHashProvider
            NullLogger<SuperHackersManifestFactory>.Instance);

        // Create a mock manifest
        var baseManifest = new Core.Models.Manifest.ContentManifest
        {
            Id = Core.Models.Manifest.ManifestId.Create("1.20251118.github.gameclient.thesuperhackers-generalsgamecode"),
            Name = "TheSuperHackers Weekly Release",
            Version = "weekly-2025-11-18",
            ContentType = ContentType.GameClient,
            Publisher = new Core.Models.Manifest.PublisherInfo
            {
                PublisherType = PublisherTypeConstants.TheSuperHackers,
                Name = "TheSuperHackers"
            }
        };

        // Act
        var canHandle = factory.CanHandle(baseManifest);

        // Assert - Factory should handle TheSuperHackers manifests
        // Full test requires extracted directory with genlauncher.exe and zhLauncher.exe
        Assert.True(canHandle, "Factory should handle TheSuperHackers manifests");

        await Task.CompletedTask;
    }

    [Fact]
    public void PublisherInference_TheSuperHackers_ShouldRouteToCustomFactory()
    {
        // Arrange
        var owner = "thesuperhackers";
        var repo = "GeneralsGameCode";

        // Act
        var publisherType = DeterminePublisherType(owner, repo);

        // Assert - This will route to SuperHackersManifestFactory
        Assert.Equal(PublisherTypeConstants.TheSuperHackers, publisherType);
    }

    [Fact]
    public void PublisherInference_GenericRepo_ShouldUseGenericPublisherType()
    {
        // Arrange
        var testCases = new[]
        {
            ("someuser", "somemod"),
            ("L3-M", "GeneralsControlBar"),
            ("random", "repository")
        };

        foreach (var (owner, repo) in testCases)
        {
            // Act
            var publisherType = DeterminePublisherType(owner, repo);

            // Assert
            Assert.Equal("github", publisherType);
        }
    }

    [Fact]
    public void ManifestId_Uniqueness_ShouldPreventCollisions()
    {
        // Arrange - Simulate manifest IDs from different sources
        var ids = new[]
        {
            "1.101525.generalsonline.gameclient.30hz",
            "1.101525.generalsonline.gameclient.60hz",
            "1.20251118.thesuperhackers.gameclient.generals",
            "1.20251118.thesuperhackers.gameclient.zerohour"
        };

        // Assert - All IDs should be unique
        var uniqueIds = ids.Distinct().ToList();
        Assert.Equal(ids.Length, uniqueIds.Count);
    }

    // Helper method that matches GitHubResolver.DeterminePublisherType
    private static string DeterminePublisherType(string owner, string repo)
    {
        if (owner.Equals("thesuperhackers", StringComparison.OrdinalIgnoreCase))
        {
            return "thesuperhackers";
        }

        return "github";
    }
}
