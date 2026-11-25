using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Verifies single-asset Flatpak downloads survive staging cleanup by persisting to
/// CAS, so a later launch can materialize the bundle the provisioner installs.
/// </summary>
public sealed class ContentStorageServiceFlatpakTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _storageRoot;
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly ContentStorageService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStorageServiceFlatpakTests"/> class.
    /// </summary>
    public ContentStorageServiceFlatpakTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        _storageRoot = Path.Combine(_tempRoot, "Storage");
        Directory.CreateDirectory(_storageRoot);

        var casConfig = Options.Create(new CasConfiguration { CasRootPath = _storageRoot });
        var referenceTracker = new CasReferenceTracker(casConfig, new Mock<ILogger<CasReferenceTracker>>().Object);

        _service = new ContentStorageService(
            _storageRoot,
            new Mock<ILogger<ContentStorageService>>().Object,
            _casServiceMock.Object,
            referenceTracker,
            new CasWriteFence());
    }

    /// <summary>
    /// A lone Flatpak bundle downloaded as a remote asset is persisted to CAS even
    /// though its source type alone would keep it metadata-only: staging is deleted
    /// after acquisition, so metadata-only storage would dangle the source mapping
    /// and the launch could never materialize the bundle.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StoreContentAsync_LoneFlatpakBundle_PersistsToCasAsync()
    {
        // Arrange
        var stagingDirectory = CreateDirectory("staging");
        var bundlePath = Path.Combine(stagingDirectory, "Linux-GeneralsXZH.flatpak");
        await File.WriteAllTextAsync(bundlePath, "flatpak bundle bytes");
        var manifest = new ContentManifest
        {
            Id = "1.100.fbraz3.gameclient.generalsxlinuxgeneralsxzh",
            ContentType = ContentType.GameClient,
            Files = [new ManifestFile { RelativePath = "Linux-GeneralsXZH.flatpak", SourceType = ContentSourceType.RemoteDownload, IsRequired = true }],
        };
        _casServiceMock
            .Setup(x => x.StoreContentAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess("bundle-hash"));

        // Act
        var result = await _service.StoreContentAsync(manifest, stagingDirectory, progress: null, CancellationToken.None);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        _casServiceMock.Verify(
            x => x.StoreContentAsync(bundlePath, ContentType.GameClient, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        var storedFile = Assert.Single(result.Data!.Files);
        Assert.Equal(ContentSourceType.ContentAddressable, storedFile.SourceType);
        Assert.Equal("bundle-hash", storedFile.Hash);
    }

    /// <summary>
    /// Ordinary game clients referencing installation files stay metadata-only, so the
    /// Flatpak persistence rule cannot reroute retail content into CAS.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task StoreContentAsync_InstallationBackedClient_StaysMetadataOnlyAsync()
    {
        // Arrange
        var stagingDirectory = CreateDirectory("staging");
        var manifest = new ContentManifest
        {
            Id = "1.105.steam.gameclient.zerohour",
            ContentType = ContentType.GameClient,
            Files = [new ManifestFile { RelativePath = "generals.exe", SourceType = ContentSourceType.GameInstallation, IsRequired = true }],
        };

        // Act
        var result = await _service.StoreContentAsync(manifest, stagingDirectory, progress: null, CancellationToken.None);

        // Assert
        Assert.True(result.Success, result.AllErrors);
        _casServiceMock.Verify(
            x => x.StoreContentAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignored during cleanup
            }
        }
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(_tempRoot, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
