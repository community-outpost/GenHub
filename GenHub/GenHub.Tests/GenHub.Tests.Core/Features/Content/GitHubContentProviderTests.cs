using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="GitHubContentProvider"/>.
/// </summary>
public class GitHubContentProviderTests
{
    private readonly Mock<IContentDiscoverer> _discovererMock;
    private readonly Mock<IContentResolver> _resolverMock;
    private readonly Mock<IContentDeliverer> _delivererMock;
    private readonly Mock<IContentValidator> _validatorMock;
    private readonly Mock<ILogger<GitHubContentProvider>> _loggerMock;
    private readonly Mock<IGitHubApiClient> _gitHubApiClientMock = new();
    private readonly GitHubContentProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubContentProviderTests"/> class.
    /// </summary>
    public GitHubContentProviderTests()
    {
        _discovererMock = new Mock<IContentDiscoverer>();
        _resolverMock = new Mock<IContentResolver>();
        _delivererMock = new Mock<IContentDeliverer>();
        _validatorMock = new Mock<IContentValidator>();
        _loggerMock = new Mock<ILogger<GitHubContentProvider>>();

        // Setup mocks to be correctly identified by the provider
        _discovererMock.Setup(d => d.SourceName).Returns("GitHub");
        _resolverMock.Setup(r => r.ResolverId).Returns("GitHubRelease");
        _delivererMock.Setup(d => d.SourceName).Returns("GitHub Content Deliverer");

        // Setup validator to return valid results for all calls
        _validatorMock.Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));
        _validatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        _provider = new GitHubContentProvider(
            new[] { _discovererMock.Object },
            new[] { _resolverMock.Object },
            new[] { _delivererMock.Object },
            _loggerMock.Object,
            _validatorMock.Object);
    }

    /// <summary>
    /// Verifies that GitHubContentProvider.SearchAsync orchestrates discovery and resolution successfully.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    [Fact]
    public async Task SearchAsync_OrchestratesDiscoveryAndResolution_Successfully()
    {
        // Arrange
        var query = new ContentSearchQuery { SearchTerm = "Test" };
        var discoveredItem = new ContentSearchResult { Id = "1.0.genhub.mod.ghtestmod", RequiresResolution = true, ResolverId = "GitHubRelease" };
        var resolvedManifest = new ContentManifest { Id = "1.0.genhub.mod.ghtestmod", Name = "Resolved Test Mod" };

        _discovererMock.Setup(d => d.DiscoverAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(new[] { discoveredItem }));

        _resolverMock.Setup(r => r.ResolveAsync(discoveredItem, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(resolvedManifest));

        _validatorMock.Setup(v => v.ValidateManifestAsync(resolvedManifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("1.0.genhub.mod.ghtestmod", new List<ValidationIssue>())); // Valid result

        // Act
        var result = await _provider.SearchAsync(query);

        // Assert
        Assert.True(result.Success);
        var searchResult = Assert.Single(result.Data ?? Enumerable.Empty<ContentSearchResult>());
        Assert.Equal("Resolved Test Mod", searchResult.Name);
        Assert.False(searchResult.RequiresResolution); // Should be resolved now
        Assert.NotNull(searchResult.GetData<ContentManifest>()); // Manifest should be embedded

        _discovererMock.Verify(d => d.DiscoverAsync(query, It.IsAny<CancellationToken>()), Times.Once);
        _resolverMock.Verify(r => r.ResolveAsync(discoveredItem, It.IsAny<CancellationToken>()), Times.Once);
        _validatorMock.Verify(v => v.ValidateManifestAsync(resolvedManifest, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that GitHubContentProvider.PrepareContentAsync calls deliverer and validator successfully.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    [Fact]
    public async Task PrepareContentAsync_CallsDelivererAndValidator_Successfully()
    {
        // Arrange
        var manifest = new ContentManifest { Id = "1.0.genhub.mod.ghtestmod", Files = new List<ManifestFile>() };
        var deliveredManifest = new ContentManifest { Id = "1.0.genhub.mod.ghtestmod", Files = [new ManifestFile { RelativePath = "file.txt" }] };
        var targetDirectory = Path.GetTempPath();

        _delivererMock.Setup(d => d.CanDeliver(It.IsAny<ContentManifest>())).Returns(true);
        _delivererMock.Setup(d => d.DeliverContentAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(deliveredManifest));

        _validatorMock.Setup(v => v.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("1.0.genhub.mod.ghtestmod", new List<ValidationIssue>())); // Valid result

        // Act
        var result = await _provider.PrepareContentAsync(manifest, targetDirectory);

        // Assert
        Assert.True(result.Success, $"Expected success but got: {result.FirstError}");

        // The base class should orchestrate the calls
        _delivererMock.Verify(d => d.CanDeliver(It.IsAny<ContentManifest>()), Times.AtLeastOnce());
        _delivererMock.Verify(d => d.DeliverContentAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}