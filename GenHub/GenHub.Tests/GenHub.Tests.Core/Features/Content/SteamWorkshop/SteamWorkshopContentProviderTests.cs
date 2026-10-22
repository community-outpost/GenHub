using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.SteamWorkshop;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopContentProvider"/>.
/// </summary>
public sealed class SteamWorkshopContentProviderTests
{
    private readonly Mock<IContentValidator> _validatorMock = new();
    private readonly Mock<IInstallationInstructionsService> _instructionsMock = new();
    private readonly Mock<IContentDeliverer> _delivererMock = new();
    private readonly Mock<IContentDiscoverer> _discovererMock = new();
    private readonly Mock<IContentResolver> _resolverMock = new();
    private readonly Mock<IFileHashProvider> _hashProviderMock = new();
    private readonly Mock<ILogger<SteamWorkshopContentProvider>> _loggerMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamWorkshopContentProviderTests"/> class.
    /// </summary>
    public SteamWorkshopContentProviderTests()
    {
        _discovererMock.Setup(discoverer => discoverer.SourceName).Returns(ContentSourceNames.SteamWorkshopDiscoverer);
        _resolverMock.Setup(resolver => resolver.ResolverId).Returns(ContentSourceNames.SteamWorkshopResolverId);
        _delivererMock.Setup(deliverer => deliverer.SourceName).Returns(ContentSourceNames.SteamWorkshopDeliverer);
        _validatorMock
            .Setup(validator => validator.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", []));
    }

    /// <summary>
    /// Verifies the provider exposes the Steam Workshop source name.
    /// </summary>
    [Fact]
    public void SourceName_ReturnsSteamWorkshop()
    {
        var provider = CreateProvider();

        Assert.Equal(SteamWorkshopConstants.DiscovererSourceName, provider.SourceName);
    }

    /// <summary>
    /// Verifies foreign content IDs are rejected without resolution.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_ForeignId_ReturnsFailureAsync()
    {
        var provider = CreateProvider();

        var result = await provider.GetValidatedContentAsync("cnclabs.map.1");

        Assert.False(result.Success);
        _resolverMock.Verify(
            resolver => resolver.ResolveAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies workshop IDs resolve through the Steam Workshop resolver.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_WorkshopId_ResolvesManifestAsync()
    {
        var manifest = new ContentManifest
        {
            Id = "1.0.steamworkshop.map.duel",
            Name = "Desert Duel",
            ContentType = GenHub.Core.Models.Enums.ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };
        ContentSearchResult? captured = null;
        _resolverMock
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSearchResult, CancellationToken>((item, _) => captured = item)
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));
        var provider = CreateProvider();

        var result = await provider.GetValidatedContentAsync("steamworkshop.3790356853");

        Assert.True(result.Success);
        Assert.Same(manifest, result.Data);
        Assert.NotNull(captured);
        Assert.Equal("3790356853", captured!.ResolverMetadata[SteamWorkshopConstants.PublishedFileIdMetadataKey]);
        Assert.Contains("3790356853", captured.SourceUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies failed validation blocks ID-based installs.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_ValidatorFailure_ReturnsFailureAsync()
    {
        _resolverMock
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest { Id = "1.0.steamworkshop.map.test", Name = "Test" }));
        _validatorMock
            .Setup(validator => validator.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", [new ValidationIssue("broken manifest", ValidationSeverity.Error)]));

        var provider = CreateProvider();
        var result = await provider.GetValidatedContentAsync("steamworkshop.3790356853");

        Assert.False(result.Success);
    }

    private SteamWorkshopContentProvider CreateProvider()
    {
        var manifestFactory = new SteamWorkshopManifestFactory(
            SteamWorkshopTestBuilders.CreateBuilder,
            Mock.Of<IProviderDefinitionLoader>(),
            _hashProviderMock.Object,
            Mock.Of<ILogger<SteamWorkshopManifestFactory>>());
        return new SteamWorkshopContentProvider(
            [_discovererMock.Object],
            [_resolverMock.Object],
            [_delivererMock.Object],
            manifestFactory,
            _loggerMock.Object,
            _validatorMock.Object,
            _instructionsMock.Object);
    }
}
