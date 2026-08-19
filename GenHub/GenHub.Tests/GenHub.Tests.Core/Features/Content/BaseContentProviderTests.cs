using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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
        instructionsMock.Verify(i => i.ExecutePostInstallStepsAsync(manifest, "/tmp/test", It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
        validatorMock.Verify(v => v.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync fails when post-install steps fail.
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
    }
}
