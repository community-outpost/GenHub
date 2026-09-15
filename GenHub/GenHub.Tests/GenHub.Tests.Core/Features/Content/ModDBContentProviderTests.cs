using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.Common;
using GenHub.Features.Content.Services.ContentProviders;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="ModDBContentProvider"/>.
/// </summary>
public sealed class ModDBContentProviderTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "ModDBProviderTests", Guid.NewGuid().ToString("N"));
    private readonly Mock<IContentValidator> _validatorMock = new();
    private readonly Mock<IInstallationInstructionsService> _instructionsMock = new();
    private readonly Mock<IContentDeliverer> _delivererMock = new();
    private readonly Mock<IContentDiscoverer> _discovererMock = new();
    private readonly Mock<IContentResolver> _resolverMock = new();
    private readonly Mock<IFileHashProvider> _hashProviderMock = new();
    private readonly Mock<IProviderDefinitionLoader> _providerLoaderMock = new();
    private readonly Mock<ILogger<ModDBContentProvider>> _loggerMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ModDBContentProviderTests"/> class.
    /// </summary>
    public ModDBContentProviderTests()
    {
        Directory.CreateDirectory(_workingDirectory);

        _discovererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.ModDBDiscoverer);
        _resolverMock.Setup(r => r.ResolverId).Returns(ContentSourceNames.ModDBResolverId);
        _delivererMock.Setup(d => d.SourceName).Returns(ContentSourceNames.HttpDeliverer);

        _validatorMock
            .Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", []));

        _validatorMock
            .Setup(v => v.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", []));

        _instructionsMock
            .Setup(i => i.ExecutePostInstallStepsAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult.CreateSuccess());

        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0123456789abcdef0123456789abcdef");
    }

    /// <summary>
    /// Verifies that provider properties (SourceName, Description) are configured properly.
    /// </summary>
    [Fact]
    public void ModDBContentProvider_Properties_ConfiguredCorrectly()
    {
        // Arrange
        var provider = CreateProvider();

        // Assert
        Assert.Equal("ModDB", provider.SourceName);
        Assert.Equal("Provides content from ModDB", provider.Description);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync returns failure if manifest validation fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_ValidationFails_ReturnsFailureAsync()
    {
        // Arrange
        _validatorMock
            .Setup(v => v.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", [new ValidationIssue("Invalid manifest", ValidationSeverity.Error)]));

        var provider = CreateProvider();
        var manifest = new ContentManifest
        {
            Id = "1.0.moddb.mod.test",
            Name = "Test Mod",
            ContentType = ContentType.Mod,
        };

        // Act
        var result = await provider.PrepareContentAsync(manifest, _workingDirectory);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("validation failed", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that PrepareContentAsync delivers download content and enriches manifests using the sibling pattern.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareContentAsync_DeliversAndEnrichesContentAsync()
    {
        // Arrange
        var extractedDir = Path.Combine(_workingDirectory, "extracted");
        Directory.CreateDirectory(extractedDir);
        var subDir = Path.Combine(extractedDir, "Data");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "test.ini"), "setting=1");

        _delivererMock
            .Setup(d => d.CanDeliver(It.IsAny<ContentManifest>()))
            .Returns(true);

        var provider = CreateProvider();
        var manifest = new ContentManifest
        {
            Id = "1.0.moddb.mod.test",
            Name = "Test Mod",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "test-mod.zip",
                    DownloadUrl = "https://www.moddb.com/downloads/start/123",
                    SourceType = ContentSourceType.RemoteDownload,
                },
            ],
        };

        _delivererMock
            .Setup(d => d.DeliverContentAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest m, string _, IProgress<ContentAcquisitionProgress> _, CancellationToken _) =>
                OperationResult<ContentManifest>.CreateSuccess(m));

        // Act
        var result = await provider.PrepareContentAsync(manifest, _workingDirectory);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(result.Data);
        var enrichedFile = Assert.Single(result.Data.Files);
        Assert.Equal(Path.Combine("Data", "test.ini"), enrichedFile.RelativePath);
        Assert.Equal(ContentSourceType.ExtractedPackage, enrichedFile.SourceType);
    }

    /// <summary>
    /// Verifies that GetValidatedContentAsync validates contentId.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetValidatedContentAsync_EmptyContentId_ReturnsFailureAsync()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var result = await provider.GetValidatedContentAsync(string.Empty);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("cannot be null or empty", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cleans up the test working directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private ModDBContentProvider CreateProvider()
    {
        var payloadProcessor = new ArchivePayloadProcessor(
            new Mock<ILogger<ArchivePayloadProcessor>>().Object);

        var manifestFactory = new ModDBManifestFactory(
            () => new Mock<IContentManifestBuilder>().Object,
            _providerLoaderMock.Object,
            _hashProviderMock.Object,
            payloadProcessor,
            new Mock<ILogger<ModDBManifestFactory>>().Object);

        return new ModDBContentProvider(
            [_discovererMock.Object],
            [_resolverMock.Object],
            [_delivererMock.Object],
            manifestFactory,
            _loggerMock.Object,
            _validatorMock.Object,
            _instructionsMock.Object);
    }
}
