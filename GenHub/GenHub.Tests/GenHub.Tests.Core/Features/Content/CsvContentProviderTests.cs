using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="CsvContentProvider"/>.
/// </summary>
public class CsvContentProviderTests
{
    /// <summary>
    /// Verifies that the constructor throws <see cref="InvalidOperationException"/> when no matching discoverer is registered.
    /// </summary>
    [Fact]
    public void Constructor_WhenDiscovererMissing_ThrowsInvalidOperationException()
    {
        var mockResolver = new Mock<IContentResolver>();
        mockResolver.Setup(r => r.ResolverId).Returns(CsvConstants.ResolverId);

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var act = () => new CsvContentProvider(
            [],
            [mockResolver.Object],
            [mockDeliverer.Object],
            Mock.Of<ILogger<CsvContentProvider>>(),
            Mock.Of<IContentValidator>(),
            Mock.Of<IInstallationInstructionsService>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CSV discoverer not found*");
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="InvalidOperationException"/> when no matching resolver is registered.
    /// </summary>
    [Fact]
    public void Constructor_WhenResolverMissing_ThrowsInvalidOperationException()
    {
        var mockDiscoverer = new Mock<IContentDiscoverer>();
        mockDiscoverer.Setup(d => d.SourceName).Returns(CsvConstants.SourceName);

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var act = () => new CsvContentProvider(
            [mockDiscoverer.Object],
            [],
            [mockDeliverer.Object],
            Mock.Of<ILogger<CsvContentProvider>>(),
            Mock.Of<IContentValidator>(),
            Mock.Of<IInstallationInstructionsService>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CSV resolver not found*");
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="InvalidOperationException"/> when no deliverer is registered.
    /// </summary>
    [Fact]
    public void Constructor_WhenDelivererMissing_ThrowsInvalidOperationException()
    {
        var mockDiscoverer = new Mock<IContentDiscoverer>();
        mockDiscoverer.Setup(d => d.SourceName).Returns(CsvConstants.SourceName);

        var mockResolver = new Mock<IContentResolver>();
        mockResolver.Setup(r => r.ResolverId).Returns(CsvConstants.ResolverId);

        var act = () => new CsvContentProvider(
            [mockDiscoverer.Object],
            [mockResolver.Object],
            [],
            Mock.Of<ILogger<CsvContentProvider>>(),
            Mock.Of<IContentValidator>(),
            Mock.Of<IInstallationInstructionsService>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*deliverer not found*");
    }

    /// <summary>
    /// Verifies that <see cref="CsvContentProvider.SourceName"/> returns the expected publisher type constant.
    /// </summary>
    [Fact]
    public void SourceName_ReturnsExpectedPublisherType()
    {
        var provider = CreateProvider();

        provider.SourceName.Should().Be(PublisherTypeConstants.CsvRegistry);
    }

    /// <summary>
    /// Verifies that <see cref="CsvContentProvider.Description"/> returns the expected description constant.
    /// </summary>
    [Fact]
    public void Description_ReturnsExpectedDescription()
    {
        var provider = CreateProvider();

        provider.Description.Should().Be(CsvConstants.Description);
    }

    /// <summary>
    /// Verifies that <see cref="BaseContentProvider.SearchAsync"/> coordinates discovery, resolution, and validation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SearchAsync_ExecutesDiscoveryAndResolutionSuccessfullyAsync()
    {
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(PublisherTypeConstants.CsvRegistry, ContentType.GameInstallation, "generals-1.08-en");
        var manifest = new ContentManifest
        {
            Id = new ManifestId(manifestId),
            Name = "Generals 1.08 (EN)",
            Version = "1.08",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            Files = [new ManifestFile { RelativePath = "game.dat", Size = 12345 }],
        };

        var discoveredItem = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Generals 1.08 (EN)",
            RequiresResolution = true,
            ResolverId = CsvConstants.ResolverId,
        };

        var mockDiscoverer = new Mock<IContentDiscoverer>();
        mockDiscoverer.Setup(d => d.SourceName).Returns(CsvConstants.SourceName);
        mockDiscoverer.Setup(d => d.DiscoverAsync(It.IsAny<ProviderDefinition?>(), It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [discoveredItem],
                TotalItems = 1,
            }));

        var mockResolver = new Mock<IContentResolver>();
        mockResolver.Setup(r => r.ResolverId).Returns(CsvConstants.ResolverId);
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<ProviderDefinition?>(), It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var mockValidator = new Mock<IContentValidator>();
        mockValidator.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifestId, []));

        var provider = new CsvContentProvider(
            [mockDiscoverer.Object],
            [mockResolver.Object],
            [mockDeliverer.Object],
            Mock.Of<ILogger<CsvContentProvider>>(),
            mockValidator.Object,
            CreateMockInstallationService());

        var result = await provider.SearchAsync(new ContentSearchQuery());

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Should().ContainSingle();
        result.Data!.First().Name.Should().Be("Generals 1.08 (EN)");
    }

    /// <summary>
    /// Verifies that <see cref="CsvContentProvider.GetValidatedContentAsync"/> returns failure when content ID is null or whitespace.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_WithNullOrWhitespaceContentId_ReturnsFailureAsync()
    {
        var provider = CreateProvider();

        var result = await provider.GetValidatedContentAsync(string.Empty);

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="CsvContentProvider.GetValidatedContentAsync"/> retrieves the manifest when matching content is found.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_WithMatchingContentId_ReturnsManifestAsync()
    {
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(PublisherTypeConstants.CsvRegistry, ContentType.GameInstallation, "generals-1.08-en");
        var manifest = new ContentManifest
        {
            Id = new ManifestId(manifestId),
            Name = "Generals 1.08 (EN)",
            Version = "1.08",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            Files = [new ManifestFile { RelativePath = "game.dat", Size = 12345 }],
        };

        var discoveredItem = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Generals 1.08 (EN)",
            RequiresResolution = true,
            ResolverId = CsvConstants.ResolverId,
        };

        var mockDiscoverer = new Mock<IContentDiscoverer>();
        mockDiscoverer.Setup(d => d.SourceName).Returns(CsvConstants.SourceName);
        mockDiscoverer.Setup(d => d.DiscoverAsync(It.IsAny<ProviderDefinition?>(), It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [discoveredItem],
                TotalItems = 1,
            }));

        var mockResolver = new Mock<IContentResolver>();
        mockResolver.Setup(r => r.ResolverId).Returns(CsvConstants.ResolverId);
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<ProviderDefinition?>(), It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var mockValidator = new Mock<IContentValidator>();
        mockValidator.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifestId, []));

        var provider = new CsvContentProvider(
            [mockDiscoverer.Object],
            [mockResolver.Object],
            [mockDeliverer.Object],
            Mock.Of<ILogger<CsvContentProvider>>(),
            mockValidator.Object,
            CreateMockInstallationService());

        var result = await provider.GetValidatedContentAsync(manifestId);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be("Generals 1.08 (EN)");
    }

    /// <summary>
    /// Verifies that <see cref="CsvContentProvider.GetValidatedContentAsync"/> returns failure when no items match exact content ID.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_WithNonMatchingContentId_ReturnsFailureAsync()
    {
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(PublisherTypeConstants.CsvRegistry, ContentType.GameInstallation, "generals-1.08-en");
        var manifest = new ContentManifest
        {
            Id = new ManifestId(manifestId),
            Name = "Generals 1.08 (EN)",
            Version = "1.08",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            Files = [new ManifestFile { RelativePath = "game.dat", Size = 12345 }],
        };

        var discoveredItem = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Generals 1.08 (EN)",
            RequiresResolution = true,
            ResolverId = CsvConstants.ResolverId,
        };

        var mockDiscoverer = new Mock<IContentDiscoverer>();
        mockDiscoverer.Setup(d => d.SourceName).Returns(CsvConstants.SourceName);
        mockDiscoverer.Setup(d => d.DiscoverAsync(It.IsAny<ProviderDefinition?>(), It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [discoveredItem],
                TotalItems = 1,
            }));

        var mockResolver = new Mock<IContentResolver>();
        mockResolver.Setup(r => r.ResolverId).Returns(CsvConstants.ResolverId);
        mockResolver.Setup(r => r.ResolveAsync(It.IsAny<ProviderDefinition?>(), It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        var mockValidator = new Mock<IContentValidator>();
        mockValidator.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifestId, []));

        var provider = new CsvContentProvider(
            [mockDiscoverer.Object],
            [mockResolver.Object],
            [mockDeliverer.Object],
            Mock.Of<ILogger<CsvContentProvider>>(),
            mockValidator.Object,
            CreateMockInstallationService());

        var result = await provider.GetValidatedContentAsync("non-matching-id");

        result.Success.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that <see cref="BaseContentProvider.PrepareContentAsync"/> delivers CSV catalog files before validation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_WithValidManifest_DeliversContentAsync()
    {
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(PublisherTypeConstants.CsvRegistry, ContentType.GameInstallation, "generals-1.08-en");
        var manifest = new ContentManifest
        {
            Id = new ManifestId(manifestId),
            Name = "Generals 1.08 (EN)",
            Version = "1.08",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            Files = [new ManifestFile { RelativePath = "game.dat", Size = 12345 }],
        };
        var deliveredManifest = new ContentManifest
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Version = "1.08-delivered",
            ContentType = manifest.ContentType,
            TargetGame = manifest.TargetGame,
            Files = manifest.Files,
        };
        var workingDirectory = "C:\\test\\dir";
        var progress = Mock.Of<IProgress<ContentAcquisitionProgress>>();
        using var cancellationSource = new CancellationTokenSource();

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);
        mockDeliverer.Setup(d => d.CanDeliver(manifest)).Returns(true);
        mockDeliverer
            .Setup(d => d.DeliverContentAsync(manifest, workingDirectory, progress, cancellationSource.Token))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(deliveredManifest));

        var mockValidator = new Mock<IContentValidator>();
        mockValidator.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifestId, []));
        mockValidator.Setup(v => v.ValidateAllAsync(workingDirectory, deliveredManifest, It.IsAny<IProgress<GenHub.Core.Models.Validation.ValidationProgress>>(), cancellationSource.Token))
            .ReturnsAsync(new ValidationResult(manifestId, []));

        var provider = CreateProvider(validator: mockValidator.Object, deliverer: mockDeliverer.Object);

        var result = await provider.PrepareContentAsync(manifest, workingDirectory, progress, cancellationSource.Token);

        result.Success.Should().BeTrue();
        result.Data.Should().BeSameAs(deliveredManifest);
        mockDeliverer.Verify(d => d.CanDeliver(manifest), Times.Once);
        mockDeliverer.Verify(
            d => d.DeliverContentAsync(manifest, workingDirectory, progress, cancellationSource.Token),
            Times.Once);
        mockValidator.Verify(
            v => v.ValidateAllAsync(workingDirectory, deliveredManifest, It.IsAny<IProgress<GenHub.Core.Models.Validation.ValidationProgress>>(), cancellationSource.Token),
            Times.Once);
    }

    /// <summary>
    /// Verifies that preparation fails without attempting delivery when the HTTP deliverer cannot handle the manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_WhenDelivererCannotDeliver_ReturnsFailureAsync()
    {
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(PublisherTypeConstants.CsvRegistry, ContentType.GameInstallation, "generals-1.08-en");
        var manifest = new ContentManifest
        {
            Id = new ManifestId(manifestId),
            Name = "Generals 1.08 (EN)",
            Version = "1.08",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            Files = [new ManifestFile { RelativePath = "game.dat", Size = 12345 }],
        };

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);
        mockDeliverer.Setup(d => d.CanDeliver(manifest)).Returns(false);

        var mockValidator = new Mock<IContentValidator>();
        mockValidator.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifestId, []));

        var provider = CreateProvider(validator: mockValidator.Object, deliverer: mockDeliverer.Object);

        var result = await provider.PrepareContentAsync(manifest, "C:\\test\\dir");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Contain("Cannot deliver content");
        mockDeliverer.Verify(
            d => d.DeliverContentAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that a delivery failure is returned without continuing to final validation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_WhenDeliveryFails_ReturnsFailureAsync()
    {
        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(PublisherTypeConstants.CsvRegistry, ContentType.GameInstallation, "generals-1.08-en");
        var manifest = new ContentManifest
        {
            Id = new ManifestId(manifestId),
            Name = "Generals 1.08 (EN)",
            Version = "1.08",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            Files = [new ManifestFile { RelativePath = "game.dat", Size = 12345 }],
        };

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);
        mockDeliverer.Setup(d => d.CanDeliver(manifest)).Returns(true);
        mockDeliverer
            .Setup(d => d.DeliverContentAsync(manifest, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("Download failed"));

        var mockValidator = new Mock<IContentValidator>();
        mockValidator.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifestId, []));

        var provider = CreateProvider(validator: mockValidator.Object, deliverer: mockDeliverer.Object);

        var result = await provider.PrepareContentAsync(manifest, "C:\\test\\dir");

        result.Success.Should().BeFalse();
        result.FirstError.Should().Be("Content delivery failed: Download failed");
        mockValidator.Verify(
            v => v.ValidateAllAsync(
                It.IsAny<string>(),
                It.IsAny<ContentManifest>(),
                It.IsAny<IProgress<GenHub.Core.Models.Validation.ValidationProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static IInstallationInstructionsService CreateMockInstallationService()
    {
        var mockInstallService = new Mock<IInstallationInstructionsService>();
        mockInstallService
            .Setup(s => s.ExecutePostInstallStepsAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        return mockInstallService.Object;
    }

    private static CsvContentProvider CreateProvider(
        IContentValidator? validator = null,
        IContentDeliverer? deliverer = null)
    {
        var mockDiscoverer = new Mock<IContentDiscoverer>();
        mockDiscoverer.Setup(d => d.SourceName).Returns(CsvConstants.SourceName);

        var mockResolver = new Mock<IContentResolver>();
        mockResolver.Setup(r => r.ResolverId).Returns(CsvConstants.ResolverId);

        var mockDeliverer = new Mock<IContentDeliverer>();
        mockDeliverer.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        return new CsvContentProvider(
            [mockDiscoverer.Object],
            [mockResolver.Object],
            [deliverer ?? mockDeliverer.Object],
            Mock.Of<ILogger<CsvContentProvider>>(),
            validator ?? Mock.Of<IContentValidator>(),
            CreateMockInstallationService());
    }
}
