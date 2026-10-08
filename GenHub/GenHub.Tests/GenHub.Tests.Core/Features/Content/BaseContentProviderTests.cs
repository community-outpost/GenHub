using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="BaseContentProvider"/>.
/// </summary>
public class BaseContentProviderTests
{
    /// <summary>
    /// Verifies that PrepareContentAsync validates manifest before preparation and executes post-install steps.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_ValidatesManifestAndExecutesPostInstallStepsAsync()
    {
        // Arrange
        var validatorMock = new Mock<IContentValidator>();
        var instructionsMock = new Mock<IInstallationInstructionsService>();
        var loggerMock = new Mock<ILogger>();
        var discovererMock = new Mock<IContentDiscoverer>();
        var resolverMock = new Mock<IContentResolver>();
        var delivererMock = new Mock<IContentDeliverer>();

        var manifest = new ContentManifest { Id = "1.0.genhub.mod.content", Name = "Test" };
        var validationResult = new ValidationResult(manifest.Id, new List<ValidationIssue>());

        validatorMock.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);
        validatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContentManifest, IProgress<ValidationProgress>, CancellationToken>((p, m, prog, ct) =>
            {
                prog?.Report(new ValidationProgress(1, 1, "file1"));
            })
            .ReturnsAsync(validationResult);

        instructionsMock.Setup(i => i.ExecutePostInstallStepsAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        var provider = new TestContentProvider(
            validatorMock.Object,
            instructionsMock.Object,
            loggerMock.Object,
            discovererMock.Object,
            resolverMock.Object,
            delivererMock.Object);

        // Act
        var result = await provider.PrepareContentAsync(manifest, "/tmp/test");

        // Assert
        Assert.True(result.Success);
        validatorMock.Verify(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()), Times.Once);
        instructionsMock.Verify(i => i.ExecutePostInstallStepsAsync(manifest, "/tmp/test", "Test Provider", false, It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
        validatorMock.Verify(v => v.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync fails and triggers rollback when post-install steps fail.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_FailsWhenPostInstallStepsFailAsync()
    {
        // Arrange
        var validatorMock = new Mock<IContentValidator>();
        var instructionsMock = new Mock<IInstallationInstructionsService>();
        var loggerMock = new Mock<ILogger>();
        var discovererMock = new Mock<IContentDiscoverer>();
        var resolverMock = new Mock<IContentResolver>();
        var delivererMock = new Mock<IContentDeliverer>();

        var manifest = new ContentManifest { Id = "1.0.genhub.mod.content", Name = "Test" };
        var validationResult = new ValidationResult(manifest.Id, new List<ValidationIssue>());

        validatorMock.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        instructionsMock.Setup(i => i.ExecutePostInstallStepsAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateFailure("Post-install step execution error"));

        var provider = new TestContentProvider(
            validatorMock.Object,
            instructionsMock.Object,
            loggerMock.Object,
            discovererMock.Object,
            resolverMock.Object,
            delivererMock.Object);

        // Act
        var result = await provider.PrepareContentAsync(manifest, "/tmp/test");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Post-install step execution error", result.FirstError);
        Assert.True(provider.RollbackCalled);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync triggers rollback when post-install steps are canceled.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_CancelsAndTriggersRollbackAsync()
    {
        // Arrange
        var validatorMock = new Mock<IContentValidator>();
        var instructionsMock = new Mock<IInstallationInstructionsService>();
        var loggerMock = new Mock<ILogger>();
        var discovererMock = new Mock<IContentDiscoverer>();
        var resolverMock = new Mock<IContentResolver>();
        var delivererMock = new Mock<IContentDeliverer>();

        var manifest = new ContentManifest { Id = "1.0.genhub.mod.content", Name = "Test" };
        var validationResult = new ValidationResult(manifest.Id, new List<ValidationIssue>());

        validatorMock.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        instructionsMock.Setup(i => i.ExecutePostInstallStepsAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var provider = new TestContentProvider(
            validatorMock.Object,
            instructionsMock.Object,
            loggerMock.Object,
            discovererMock.Object,
            resolverMock.Object,
            delivererMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.PrepareContentAsync(manifest, "/tmp/test"));

        Assert.True(provider.RollbackCalled);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync fails when manifest validation fails with errors.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_FailsWhenManifestValidationHasErrorsAsync()
    {
        // Arrange
        var validatorMock = new Mock<IContentValidator>();
        var instructionsMock = new Mock<IInstallationInstructionsService>();
        var loggerMock = new Mock<ILogger>();
        var discovererMock = new Mock<IContentDiscoverer>();
        var resolverMock = new Mock<IContentResolver>();
        var delivererMock = new Mock<IContentDeliverer>();

        var manifest = new ContentManifest { Id = "1.0.genhub.mod.content", Name = "Test" };
        var validationIssues = new List<ValidationIssue>
        {
            new ValidationIssue("Test error", ValidationSeverity.Error),
        };
        var validationResult = new ValidationResult(manifest.Id, validationIssues);

        validatorMock.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult);

        var provider = new TestContentProvider(
            validatorMock.Object,
            instructionsMock.Object,
            loggerMock.Object,
            discovererMock.Object,
            resolverMock.Object,
            delivererMock.Object);

        // Act
        var result = await provider.PrepareContentAsync(manifest, "/tmp/test");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Manifest validation failed", result.FirstError);
    }

    /// <summary>
    /// Test implementation of BaseContentProvider for testing.
    /// </summary>
    private class TestContentProvider : BaseContentProvider
    {
        private readonly IContentDiscoverer _discoverer;
        private readonly IContentResolver _resolver;
        private readonly IContentDeliverer _deliverer;

        public bool RollbackCalled { get; private set; }

        public TestContentProvider(
            IContentValidator validator,
            IInstallationInstructionsService instructionsService,
            ILogger logger,
            IContentDiscoverer discoverer,
            IContentResolver resolver,
            IContentDeliverer deliverer)
            : base(validator, instructionsService, logger)
        {
            _discoverer = discoverer;
            _resolver = resolver;
            _deliverer = deliverer;
        }

        public override string SourceName => "Test Provider";

        public override string Description => "Test provider for unit testing";

        protected override IContentDiscoverer Discoverer => _discoverer;

        protected override IContentResolver Resolver => _resolver;

        protected override IContentDeliverer Deliverer => _deliverer;

        public override Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
            string contentId,
            CancellationToken cancellationToken = default)
        {
            var manifest = new ContentManifest
            {
                Id = ManifestId.Create(contentId),
                Name = "Test Content",
                Version = "1.0.0",
                ContentType = ContentType.Map,
                TargetGame = GameType.Generals,
            };

            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(manifest));
        }

        protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
            ContentManifest manifest, string workingDirectory, IProgress<ContentAcquisitionProgress>? progress, CancellationToken cancellationToken)
        {
            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(manifest));
        }

        protected override Task RollbackPreparedContentAsync(
            ContentManifest originalManifest,
            ContentManifest preparedManifest,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            RollbackCalled = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Verifies that exact-ID search finds the exact match even when a fuzzy result sorts first.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchManifestByIdAsync_FindsExactMatchBehindFuzzyResultAsync()
    {
        // Arrange
        var exactManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.csv.map.exact-content"),
            Name = "Exact",
            Version = "1.0.0",
            ContentType = ContentType.Map,
            TargetGame = GameType.Generals,
        };
        var allResults = new List<ContentSearchResult>
        {
            new() { Id = "1.0.csv.map.exact-content-fuzzy", Name = "Fuzzy", Data = exactManifest },
            new() { Id = "1.0.csv.map.exact-content", Name = "Exact", Data = exactManifest },
        };

        var discovererMock = new Mock<IContentDiscoverer>();
        discovererMock.Setup(d => d.DiscoverAsync(
                It.IsAny<ProviderDefinition?>(),
                It.IsAny<ContentSearchQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderDefinition? provider, ContentSearchQuery query, CancellationToken cancellationToken) =>
                OperationResult<ContentDiscoveryResult>.CreateSuccess(
                    new ContentDiscoveryResult { Items = allResults.Take(query.Take).ToList() }));

        var provider = new SearchTestProvider(
            Mock.Of<IContentValidator>(),
            Mock.Of<IInstallationInstructionsService>(),
            Mock.Of<ILogger>(),
            discovererMock.Object);

        // Act
        var result = await provider.SearchByIdAsync("1.0.csv.map.exact-content", requireExactIdMatch: true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("1.0.csv.map.exact-content", result.Data?.Id.Value);
    }

    /// <summary>
    /// Verifies that resolved search results keep the discovered variant grouping fields.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SearchAsync_PreservesVariantGroupingOnResolvedCardsAsync()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.sh.gameclient.weekly-test"),
            Name = "Game code",
            Version = "weekly-test",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        var variants = new List<ContentVariantInfo>
        {
            new() { Id = "zerohour", Name = "Zero Hour", VariantType = "game-type", TargetGame = GameType.ZeroHour },
        };
        var discovered = new ContentSearchResult
        {
            Id = "1.0.sh.gameclient.weekly-test",
            Name = "Game code",
            RequiresResolution = true,
            VariantGroupId = "thesuperhackers.gameclient.weekly-test",
            VariantFamilyName = "Game code",
            Variants = variants,
        };

        var discovererMock = new Mock<IContentDiscoverer>();
        discovererMock.Setup(d => d.DiscoverAsync(
                It.IsAny<ProviderDefinition?>(),
                It.IsAny<ContentSearchQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(
                new ContentDiscoveryResult { Items = [discovered] }));

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(r => r.ResolveAsync(
                It.IsAny<ProviderDefinition?>(),
                It.IsAny<ContentSearchResult>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        var validatorMock = new Mock<IContentValidator>();
        validatorMock.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(manifest.Id, new List<ValidationIssue>()));

        var provider = new TestContentProvider(
            validatorMock.Object,
            Mock.Of<IInstallationInstructionsService>(),
            Mock.Of<ILogger>(),
            discovererMock.Object,
            resolverMock.Object,
            Mock.Of<IContentDeliverer>());

        // Act
        var result = await provider.SearchAsync(new ContentSearchQuery());

        // Assert
        Assert.True(result.Success);
        var card = Assert.Single(result.Data!);
        Assert.Equal("thesuperhackers.gameclient.weekly-test", card.VariantGroupId);
        Assert.Equal("Game code", card.VariantFamilyName);
        Assert.Same(variants, card.Variants);
    }

    /// <summary>
    /// Test implementation exposing the search-by-ID helper.
    /// </summary>
    private class SearchTestProvider : BaseContentProvider
    {
        private readonly IContentDiscoverer _discoverer;

        public SearchTestProvider(
            IContentValidator validator,
            IInstallationInstructionsService instructionsService,
            ILogger logger,
            IContentDiscoverer discoverer)
            : base(validator, instructionsService, logger)
        {
            _discoverer = discoverer;
        }

        public override string SourceName => "Search Test Provider";

        public override string Description => "Test provider for search testing";

        protected override IContentDiscoverer Discoverer => _discoverer;

        protected override IContentResolver Resolver => throw new NotSupportedException();

        protected override IContentDeliverer Deliverer => throw new NotSupportedException();

        public override Task<OperationResult<ContentManifest>> GetValidatedContentAsync(
            string contentId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OperationResult<ContentManifest>> SearchByIdAsync(
            string contentId,
            bool requireExactIdMatch,
            CancellationToken cancellationToken = default)
        {
            return SearchManifestByIdAsync(contentId, requireExactIdMatch, cancellationToken);
        }

        protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
            ContentManifest manifest, string workingDirectory, IProgress<ContentAcquisitionProgress>? progress, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        protected override Task RollbackPreparedContentAsync(
            ContentManifest originalManifest,
            ContentManifest preparedManifest,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
