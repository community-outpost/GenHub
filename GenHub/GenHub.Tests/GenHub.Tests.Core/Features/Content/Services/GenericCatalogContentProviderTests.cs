using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests for <see cref="GenericCatalogContentProvider"/>.
/// </summary>
public sealed class GenericCatalogContentProviderTests
{
    /// <summary>
    /// The provider advertises the shared generic catalog source name.
    /// </summary>
    [Fact]
    public void SourceName_IsGenericCatalogProviderName()
    {
        var provider = CreateProvider();

        Assert.Equal(CatalogConstants.GenericCatalogProviderName, provider.SourceName);
        Assert.NotEmpty(provider.Description);
    }

    /// <summary>
    /// Global search returns no results: subscription feeds are browsed through
    /// per-subscription discoverers, not provider search.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_ReturnsEmptySuccessAsync()
    {
        var provider = CreateProvider();

        var result = await provider.SearchAsync(new ContentSearchQuery());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// ID-only manifest fetch fails with guidance: generic catalog manifests
    /// require resolution metadata from discovery.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_ReturnsResolutionGuidanceFailureAsync()
    {
        var provider = CreateProvider();

        var result = await provider.GetValidatedContentAsync("some-content-id");

        Assert.False(result.Success);
        Assert.Contains("resolution metadata", result.FirstError);
    }

    private static GenericCatalogContentProvider CreateProvider()
    {
        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IPublisherCatalogParser>(),
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            Mock.Of<IGitHubApiClient>());

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(r => r.ResolverId).Returns(CatalogConstants.GenericCatalogResolverId);

        var delivererMock = new Mock<IContentDeliverer>();
        delivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var factory = new GenericCatalogManifestFactory(
            Mock.Of<IFileHashProvider>(),
            NullLogger<GenericCatalogManifestFactory>.Instance,
            Mock.Of<IArchivePayloadProcessor>());

        return new GenericCatalogContentProvider(
            discoverer,
            [resolverMock.Object],
            [delivererMock.Object],
            factory,
            NullLogger<GenericCatalogContentProvider>.Instance,
            Mock.Of<IContentValidator>(),
            Mock.Of<IInstallationInstructionsService>());
    }
}
