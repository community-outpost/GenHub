using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="BaseContentProvider"/>.
/// </summary>
public class BaseContentProviderTests
{
    /// <summary>
    /// Verifies that PrepareContentAsync validates manifest before preparation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_ValidatesManifestBeforePreparation()
    {
        // Arrange
        var validatorMock = new Mock<IContentValidator>();
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

        var provider = new TestContentProvider(validatorMock.Object, loggerMock.Object, discovererMock.Object, resolverMock.Object, delivererMock.Object);

        // Act
        var result = await provider.PrepareContentAsync(manifest, "/tmp/test");

        // Assert
        Assert.True(result.Success);
        validatorMock.Verify(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>()), Times.Once);
        validatorMock.Verify(v => v.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync forwards validation progress from the validator into the provider's progress reporter.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_ReportsValidationProgress()
    {
        // Arrange
        var validatorMock = new Mock<IContentValidator>();
        var loggerMock = new Mock<ILogger>();
        var discovererMock = new Mock<IContentDiscoverer>();
        var resolverMock = new Mock<IContentResolver>();
        var delivererMock = new Mock<IContentDeliverer>();

        var manifest = new ContentManifest { Id = "1.0.genhub.mod.content", Name = "Test" };
        var validationResult = new ValidationResult(manifest.Id, new List<ValidationIssue>());

        validatorMock.Setup(v => v.ValidateManifestAsync(manifest, It.IsAny<CancellationToken>())).ReturnsAsync(validationResult);

        // When ValidateAllAsync is invoked, invoke the provided IProgress<ValidationProgress> with a sample update
        validatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContentManifest, IProgress<ValidationProgress>, CancellationToken>((p, m, prog, ct) =>
            {
                prog?.Report(new ValidationProgress(1, 2, "file1"));
            })
            .ReturnsAsync(validationResult);

        var provider = new TestContentProvider(validatorMock.Object, loggerMock.Object, discovererMock.Object, resolverMock.Object, delivererMock.Object);

        var reports = new List<ContentAcquisitionProgress>();
        var progress = new Progress<ContentAcquisitionProgress>(r => reports.Add(r));

        // Act
        var result = await provider.PrepareContentAsync(manifest, "/tmp/test", progress);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(reports);

        // Ensure we received a validation-related update (manifest validation or prepared-content validation)
        Assert.Contains(reports, r => r.CurrentOperation != null && r.CurrentOperation.Contains("Validating"));
    }

    /// <summary>
    /// Verifies that PrepareContentAsync fails when manifest validation fails with errors.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_FailsWhenManifestValidationHasErrors()
    {
        // Arrange
        var validatorMock = new Mock<IContentValidator>();
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

        var provider = new TestContentProvider(validatorMock.Object, loggerMock.Object, discovererMock.Object, resolverMock.Object, delivererMock.Object);

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
            ILogger logger,
            IContentDiscoverer discoverer,
            IContentResolver resolver,
            IContentDeliverer deliverer)
            : base(validator, logger)
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

        public override Task<OperationResult<ContentManifest>> GetValidatedContentAsync(string contentId, CancellationToken cancellationToken = default)
        {
            var manifest = new ContentManifest { Id = contentId, Name = $"Content {contentId}" };
            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(manifest));
        }

        protected override Task<OperationResult<ContentManifest>> PrepareContentInternalAsync(
            ContentManifest manifest, string workingDirectory, IProgress<ContentAcquisitionProgress>? progress, CancellationToken cancellationToken)
        {
            return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(manifest));
        }
    }
}
