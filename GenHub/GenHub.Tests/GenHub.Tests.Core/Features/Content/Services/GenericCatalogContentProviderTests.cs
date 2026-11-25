using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
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

    /// <summary>
    /// When a specialized non-HTTP deliverer matches the manifest (such as CommunityOutpostDeliverer),
    /// PrepareContentAsync delegates execution directly to DeliverContentOnlyAsync rather than
    /// flat catalog zip extraction.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_WhenSpecializedDelivererCanDeliver_RoutesToSpecializedDelivererAsync()
    {
        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IPublisherCatalogParser>(),
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            Mock.Of<IGitHubApiClient>());

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(r => r.ResolverId).Returns(CatalogConstants.GenericCatalogResolverId);

        var httpDelivererMock = new Mock<IContentDeliverer>();
        httpDelivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);
        httpDelivererMock.Setup(d => d.CanDeliver(It.IsAny<ContentManifest>())).Returns(true);

        var specializedDelivererMock = new Mock<IContentDeliverer>();
        specializedDelivererMock.Setup(d => d.SourceName).Returns("CommunityOutpostDeliverer");
        specializedDelivererMock.Setup(d => d.CanDeliver(It.IsAny<ContentManifest>())).Returns(true);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.undead2146.addon.leikezehotkeys"),
            Name = "Leikeze Competitive Hotkeys",
            Version = "1.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.Addon,
        };

        var deliveredManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.communityoutpost.addon.hlei-zerohour-en"),
            Name = "Leikeze Competitive Hotkeys (Zero Hour - English)",
            Version = "1.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.Addon,
        };

        specializedDelivererMock
            .Setup(d => d.DeliverContentAsync(manifest, "C:/work", It.IsAny<System.IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(deliveredManifest));

        var factory = new GenericCatalogManifestFactory(
            Mock.Of<IFileHashProvider>(),
            NullLogger<GenericCatalogManifestFactory>.Instance,
            Mock.Of<IArchivePayloadProcessor>());

        var validatorMock = new Mock<IContentValidator>();
        validatorMock.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("1.10000.undead2146.addon.leikezehotkeys", []));
        validatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<System.IProgress<ValidationProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("1.10000.undead2146.addon.leikezehotkeys", []));

        var instructionsMock = new Mock<IInstallationInstructionsService>();
        instructionsMock.Setup(i => i.ExecutePostInstallStepsAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<System.IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        var provider = new GenericCatalogContentProvider(
            discoverer,
            [resolverMock.Object],
            [httpDelivererMock.Object, specializedDelivererMock.Object],
            factory,
            NullLogger<GenericCatalogContentProvider>.Instance,
            validatorMock.Object,
            instructionsMock.Object);

        var result = await provider.PrepareContentAsync(manifest, "C:/work", null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(deliveredManifest.Id.Value, result.Data.Id.Value);
        specializedDelivererMock.Verify(d => d.DeliverContentAsync(manifest, "C:/work", It.IsAny<System.IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()), Times.Once);
        httpDelivererMock.Verify(d => d.DeliverContentAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<System.IProgress<ContentAcquisitionProgress>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// When GeneralsOnline deliverer is selected on non-Windows hosts, PrepareContentAsync returns a clear OS incompatibility error.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_GeneralsOnlineDelivererOnNonWindows_ReturnsUnsupportedOsFailureAsync()
    {
        if (System.OperatingSystem.IsWindows())
        {
            return;
        }

        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IPublisherCatalogParser>(),
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            Mock.Of<IGitHubApiClient>());

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(r => r.ResolverId).Returns(CatalogConstants.GenericCatalogResolverId);

        var httpDelivererMock = new Mock<IContentDeliverer>();
        httpDelivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var goDelivererMock = new Mock<IContentDeliverer>();
        goDelivererMock.Setup(d => d.SourceName).Returns(GeneralsOnlineConstants.DelivererSourceName);
        goDelivererMock.Setup(d => d.CanDeliver(It.IsAny<ContentManifest>())).Returns(true);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.generalsonline.gameclient.generalsonline"),
            Name = "Generals Online",
            Version = "1.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
        };

        var factory = new GenericCatalogManifestFactory(
            Mock.Of<IFileHashProvider>(),
            NullLogger<GenericCatalogManifestFactory>.Instance,
            Mock.Of<IArchivePayloadProcessor>());

        var validatorMock = new Mock<IContentValidator>();
        validatorMock.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("1.10000.generalsonline.gameclient.generalsonline", []));
        validatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<System.IProgress<ValidationProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("1.10000.generalsonline.gameclient.generalsonline", []));

        var provider = new GenericCatalogContentProvider(
            discoverer,
            [resolverMock.Object],
            [httpDelivererMock.Object, goDelivererMock.Object],
            factory,
            NullLogger<GenericCatalogContentProvider>.Instance,
            validatorMock.Object,
            Mock.Of<IInstallationInstructionsService>());

        var result = await provider.PrepareContentAsync(manifest, "C:/work", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("GeneralsOnline is currently supported only on Windows", result.FirstError);
    }

    /// <summary>
    /// When post-preparation fails, rollback unregisters newly added GeneralsOnline manifests from the pool.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RollbackPreparedContentAsync_UnregistersNewlyAddedGeneralsOnlineManifestsAsync()
    {
        var discoverer = new GenericCatalogDiscoverer(
            NullLogger<GenericCatalogDiscoverer>.Instance,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<IPublisherCatalogParser>(),
            new VersionSelector(NullLogger<VersionSelector>.Instance),
            Mock.Of<IGitHubApiClient>());

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(r => r.ResolverId).Returns(CatalogConstants.GenericCatalogResolverId);

        var httpDelivererMock = new Mock<IContentDeliverer>();
        httpDelivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.generalsonline.gameclient.generalsonline"),
            Name = "Generals Online",
            Version = "1.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var preparedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.generalsonline.gameclient.generalsonline"),
            Name = "Generals Online",
            Version = "1.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var preExistingManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.other.mod.existing"),
            Version = "1.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var newlyAddedVariant = new ContentManifest
        {
            Id = ManifestId.Create("1.10000.generalsonline.mod.60hz"),
            Version = "1.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Publisher = new PublisherInfo { PublisherType = GeneralsOnlineConstants.PublisherType },
        };

        var manifestPoolMock = new Mock<IContentManifestPool>();
        manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([preExistingManifest, newlyAddedVariant]));

        manifestPoolMock
            .Setup(p => p.RemoveManifestAsync(newlyAddedVariant.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var factory = new GenericCatalogManifestFactory(
            Mock.Of<IFileHashProvider>(),
            NullLogger<GenericCatalogManifestFactory>.Instance,
            Mock.Of<IArchivePayloadProcessor>());

        var validatorMock = new Mock<IContentValidator>();

        var provider = new GenericCatalogContentProvider(
            discoverer,
            [resolverMock.Object],
            [httpDelivererMock.Object],
            factory,
            NullLogger<GenericCatalogContentProvider>.Instance,
            validatorMock.Object,
            Mock.Of<IInstallationInstructionsService>(),
            manifestPoolMock.Object);

        provider.SetPreExistingManifestsForTesting(originalManifest.Id, "C:/work", [preExistingManifest.Id]);

        await provider.InvokeRollbackPreparedContentAsyncForTesting(originalManifest, preparedManifest, "C:/work", CancellationToken.None);

        manifestPoolMock.Verify(p => p.RemoveManifestAsync(newlyAddedVariant.Id, false, It.IsAny<CancellationToken>()), Times.Once);
        manifestPoolMock.Verify(p => p.RemoveManifestAsync(preExistingManifest.Id, false, It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that Dispose can be called safely without throwing exceptions.
    /// </summary>
    [Fact]
    public void Dispose_DisposesResourcesSafely()
    {
        var provider = CreateProvider();
        provider.Dispose();
        provider.Dispose();
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
