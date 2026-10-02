using GenHub.Core.Extensions.Storage;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services;
using GenHub.Features.Storage.Services;
using GenHub.Tests.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests that <see cref="ContentStorageService"/> and the CAS manifest checks handle
/// manifests whose files live in platform variants rather than the flat list.
/// </summary>
public class ContentStorageServiceVariantTests : IDisposable
{
    private const string HostFileName = "generalszh-host";
    private const string ForeignFileName = "generalszh-foreign.exe";
    private const string HostHash = "host_variant_hash";
    private const string ForeignHash = "foreign_variant_hash";

    private readonly string _tempRoot;
    private readonly string _storageRoot;
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly CasReferenceTracker _referenceTracker;
    private readonly ContentStorageService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentStorageServiceVariantTests"/> class.
    /// </summary>
    public ContentStorageServiceVariantTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        _storageRoot = Path.Combine(_tempRoot, "Storage");
        Directory.CreateDirectory(_storageRoot);

        var casConfig = Options.Create(new CasConfiguration { CasRootPath = _storageRoot });
        _referenceTracker = new CasReferenceTracker(casConfig, new Mock<ILogger<CasReferenceTracker>>().Object);

        _service = new ContentStorageService(
            _storageRoot,
            new Mock<ILogger<ContentStorageService>>().Object,
            _casServiceMock.Object,
            _referenceTracker,
            new CasWriteFence());

        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));
        _casServiceMock
            .Setup(c => c.GetContentPathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("not in CAS"));
        _casServiceMock
            .Setup(c => c.ExistsAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _casServiceMock
            .Setup(c => c.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
    }

    /// <summary>
    /// Storing a variant manifest stores the host variant's files in CAS and writes their
    /// hashes back into that variant. The foreign variant and the empty flat list are
    /// kept, and the references cover both variants so garbage collection keeps blobs
    /// that only the foreign variant names.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithVariantManifest_StoresHostVariantAndTracksEveryVariantAsync()
    {
        var sourceDir = Path.Combine(_tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);
        var hostPath = Path.Combine(sourceDir, HostFileName);
        await File.WriteAllTextAsync(hostPath, "host-binary");

        _casServiceMock
            .Setup(c => c.StoreContentAsync(hostPath, ContentType.GameClient, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(HostHash));

        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, SourcePath = hostPath, SourceType = ContentSourceType.LocalFile, IsRequired = true }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, SourcePath = Path.Combine(sourceDir, ForeignFileName), IsRequired = true }]);

        var result = await _service.StoreContentAsync(manifest, sourceDir);

        Assert.True(result.Success, result.FirstError);
        var stored = result.Data!;
        Assert.Empty(stored.Files);

        var hostFile = Assert.Single(ManifestVariantResolver.ResolveFiles(stored));
        Assert.Equal(HostHash, hostFile.Hash);
        Assert.Equal(ContentSourceType.ContentAddressable, hostFile.SourceType);

        var foreignFile = Assert.Single(ManifestVariantResolver.ResolveFiles(stored, VariantManifestFixture.ForeignRuntimeIdentifier));
        Assert.Equal(ForeignHash, foreignFile.Hash);
        Assert.Null(foreignFile.SourcePath);

        _casServiceMock.Verify(
            c => c.StoreContentAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var referenced = await _referenceTracker.GetAllReferencedHashesAsync();
        Assert.Contains(HostHash, referenced);
        Assert.Contains(ForeignHash, referenced);
    }

    /// <summary>
    /// A path that escapes the source directory is rejected even when it sits in a
    /// variant for another platform.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithTraversalInForeignVariant_FailsAsync()
    {
        var sourceDir = Path.Combine(_tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);

        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, SourceType = ContentSourceType.RemoteDownload }],
            [new() { RelativePath = "../../escape.exe", SourceType = ContentSourceType.RemoteDownload }]);

        var result = await _service.StoreContentAsync(manifest, sourceDir);

        Assert.False(result.Success);
        Assert.Contains("path traversal", result.FirstError);
    }

    /// <summary>
    /// With the source gone, metadata-only storage needs only the host variant's objects
    /// in CAS. A missing foreign object does not block it, and staging source paths are
    /// cleared from every variant.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithMissingSource_ChecksHostVariantAndSanitizesEveryVariantAsync()
    {
        _casServiceMock
            .Setup(c => c.ExistsAsync(HostHash, ContentType.GameClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourcePath = "staging/foreign.exe", SourceType = ContentSourceType.ContentAddressable, IsRequired = true }]);

        var missingSource = Path.Combine(_tempRoot, "Missing");
        var result = await _service.StoreContentAsync(manifest, missingSource);

        Assert.True(result.Success, result.FirstError);
        var foreignFile = Assert.Single(ManifestVariantResolver.ResolveFiles(result.Data!, VariantManifestFixture.ForeignRuntimeIdentifier));
        Assert.Null(foreignFile.SourcePath);
        Assert.Equal(ForeignHash, foreignFile.Hash);
    }

    /// <summary>
    /// With the source gone, a foreign CAS entry whose staging source path lies outside
    /// the storage root does not fail validation, because metadata-only storage clears it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithMissingSourceAndAbsoluteForeignStagingPath_SucceedsAsync()
    {
        _casServiceMock
            .Setup(c => c.ExistsAsync(HostHash, ContentType.GameClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var stagingPath = Path.Combine(_tempRoot, "Staging", ForeignFileName);
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourcePath = stagingPath, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }]);

        var result = await _service.StoreContentAsync(manifest, Path.Combine(_tempRoot, "Missing"));

        Assert.True(result.Success, result.FirstError);
        var foreignFile = Assert.Single(ManifestVariantResolver.ResolveFiles(result.Data!, VariantManifestFixture.ForeignRuntimeIdentifier));
        Assert.Null(foreignFile.SourcePath);
    }

    /// <summary>
    /// Metadata-only storage fails when the host variant's required object is missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_WithMissingSourceAndMissingHostObject_FailsAsync()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }]);

        var result = await _service.StoreContentAsync(manifest, Path.Combine(_tempRoot, "Missing"));

        Assert.False(result.Success);
        Assert.Contains(HostFileName, result.FirstError);
        Assert.DoesNotContain(ForeignFileName, result.FirstError);
    }

    /// <summary>
    /// The missing-object check reports only the host variant's files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetMissingRequiredCasFilesAsync_WithVariantManifest_ReportsOnlyHostFilesAsync()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }]);

        var missing = await _casServiceMock.Object.GetMissingRequiredCasFilesAsync(manifest);

        Assert.Equal(HostFileName, Assert.Single(missing).RelativePath);
    }

    /// <summary>The missing-object check skips a null host entry instead of throwing.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetMissingRequiredCasFilesAsync_WithNullHostEntry_SkipsItAsync()
    {
        var manifest = VariantManifestFixture.Create(
            [null!, new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }],
            []);

        var missing = await _casServiceMock.Object.GetMissingRequiredCasFilesAsync(manifest);

        Assert.Equal(HostFileName, Assert.Single(missing).RelativePath);
    }

    /// <summary>
    /// Physical storage does not validate the staging source path of a foreign CAS entry,
    /// which it never reads, and clears it before persisting.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_PhysicalWithForeignStagingPathOutsideSource_SucceedsAsync()
    {
        var sourceDir = Path.Combine(_tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);
        var hostPath = Path.Combine(sourceDir, HostFileName);
        await File.WriteAllTextAsync(hostPath, "host-binary");
        _casServiceMock
            .Setup(c => c.StoreContentAsync(hostPath, ContentType.GameClient, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(HostHash));

        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, SourcePath = hostPath, SourceType = ContentSourceType.LocalFile, IsRequired = true }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, SourcePath = Path.Combine(_tempRoot, "Staging", ForeignFileName), IsRequired = true }]);

        var result = await _service.StoreContentAsync(manifest, sourceDir);

        Assert.True(result.Success, result.FirstError);
        Assert.Null(Assert.Single(ManifestVariantResolver.ResolveFiles(result.Data!, VariantManifestFixture.ForeignRuntimeIdentifier)).SourcePath);
    }

    /// <summary>A host CAS entry whose staging source path escapes the source directory is still rejected.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreContentAsync_PhysicalWithHostCasStagingPathOutsideSource_FailsAsync()
    {
        var sourceDir = Path.Combine(_tempRoot, "Source");
        Directory.CreateDirectory(sourceDir);
        var hostPath = Path.Combine(sourceDir, HostFileName);
        await File.WriteAllTextAsync(hostPath, "host-binary");

        var manifest = VariantManifestFixture.Create(
            [
                new() { RelativePath = HostFileName, SourcePath = hostPath, SourceType = ContentSourceType.LocalFile, IsRequired = true },
                new() { RelativePath = "extra.big", Hash = HostHash, SourceType = ContentSourceType.ContentAddressable, SourcePath = Path.Combine(_tempRoot, "Staging", "extra.big") },
            ],
            []);

        var result = await _service.StoreContentAsync(manifest, sourceDir);

        Assert.False(result.Success);
        Assert.Contains("traverses outside base directory", result.FirstError);
    }

    /// <summary>
    /// Retrieving a stored variant manifest materializes the host variant's files only.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RetrieveContentAsync_WithVariantManifest_MaterializesHostVariantAsync()
    {
        var casDir = Path.Combine(_tempRoot, "Cas");
        Directory.CreateDirectory(casDir);
        var hostBlob = Path.Combine(casDir, HostHash);
        await File.WriteAllTextAsync(hostBlob, "host-binary");
        var foreignBlob = Path.Combine(casDir, ForeignHash);
        await File.WriteAllTextAsync(foreignBlob, "foreign-binary");

        _casServiceMock
            .Setup(c => c.GetContentPathAsync(HostHash, ContentType.GameClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(hostBlob));

        // The foreign blob resolves so that materializing every variant would copy it and fail the absence assertion.
        _casServiceMock
            .Setup(c => c.GetContentPathAsync(ForeignHash, ContentType.GameClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(foreignBlob));
        _casServiceMock
            .Setup(c => c.ExistsAsync(It.IsAny<string>(), ContentType.GameClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }]);
        var storeResult = await _service.StoreContentAsync(manifest, Path.Combine(_tempRoot, "Missing"));
        Assert.True(storeResult.Success, storeResult.FirstError);

        var target = Path.Combine(_tempRoot, "Target");
        var result = await _service.RetrieveContentAsync(manifest.Id, target);

        Assert.True(result.Success, result.FirstError);
        Assert.True(File.Exists(Path.Combine(target, HostFileName)));
        Assert.False(File.Exists(Path.Combine(target, ForeignFileName)));
    }

    /// <summary>Retrieval finds required objects in a different content pool.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task RetrieveContentAsync_RequiredBlobInDifferentPool_SucceedsAsync()
    {
        var blob = Path.Combine(_tempRoot, "other-pool-blob");
        await File.WriteAllTextAsync(blob, "payload");
        _casServiceMock.Setup(c => c.GetContentPathAsync(HostHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(blob));
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable }], []);
        Directory.CreateDirectory(Path.GetDirectoryName(_service.GetManifestStoragePath(manifest.Id))!);
        await File.WriteAllTextAsync(_service.GetManifestStoragePath(manifest.Id), JsonSerializer.Serialize(manifest));
        var target = Path.Combine(_tempRoot, "Target");

        var result = await _service.RetrieveContentAsync(manifest.Id, target);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(target, HostFileName)));
    }

    /// <summary>Required missing blobs cannot produce a successful empty retrieval.</summary>
    /// <returns>The asynchronous test.</returns>
    /// <param name="hash">A missing hash or a hash with no available blob.</param>
    [Theory]
    [InlineData("")]
    [InlineData(HostHash)]
    public async Task RetrieveContentAsync_MissingRequiredBlob_FailsAsync(string hash)
    {
        var manifest = VariantManifestFixture.Create([new() { RelativePath = HostFileName, Hash = hash }], []);
        Directory.CreateDirectory(Path.GetDirectoryName(_service.GetManifestStoragePath(manifest.Id))!);
        await File.WriteAllTextAsync(_service.GetManifestStoragePath(manifest.Id), JsonSerializer.Serialize(manifest));
        var result = await _service.RetrieveContentAsync(manifest.Id, Path.Combine(_tempRoot, "Target"));
        Assert.False(result.Success);
        Assert.Contains(HostFileName, result.FirstError);
    }

    /// <summary>Rejected metadata preserves the caller's staging paths.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task StoreContentAsync_InvalidMetadata_PreservesStagingPathAsync()
    {
        _casServiceMock.Setup(c => c.ExistsAsync(HostHash, ContentType.GameClient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var stagingPath = Path.Combine(_tempRoot, "Staging", ForeignFileName);
        var foreign = new ManifestFile
        {
            RelativePath = "../escape.exe", Hash = ForeignHash,
            SourceType = ContentSourceType.ContentAddressable, SourcePath = stagingPath,
        };
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, Hash = HostHash, SourceType = ContentSourceType.ContentAddressable }],
            [foreign]);

        var result = await _service.StoreContentAsync(manifest, Path.Combine(_tempRoot, "Missing"));

        Assert.False(result.Success);
        Assert.Equal(stagingPath, foreign.SourcePath);
    }

    /// <summary>Unsupported map payloads fail before being persisted.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task StoreContentAsync_UnsupportedMapPack_FailsAsync()
    {
        var manifest = VariantManifestFixture.Create([], [new() { RelativePath = ForeignFileName }]);
        manifest.ContentType = ContentType.MapPack;
        manifest.Variants.RemoveAt(1);
        var result = await _service.StoreContentAsync(manifest, _tempRoot);
        Assert.False(result.Success);
        Assert.False(File.Exists(_service.GetManifestStoragePath(manifest.Id)));
    }

    /// <summary>Retrieving a stored manifest with no host variant names the host, not a corrupt manifest.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RetrieveContentAsync_WithoutHostVariant_ReportsUnsupportedHostAsync()
    {
        var manifest = VariantManifestFixture.Create(
            [],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, IsRequired = true }]);
        manifest.Variants.RemoveAt(1);
        Directory.CreateDirectory(Path.GetDirectoryName(_service.GetManifestStoragePath(manifest.Id))!);
        await File.WriteAllTextAsync(_service.GetManifestStoragePath(manifest.Id), JsonSerializer.Serialize(manifest));

        var result = await _service.RetrieveContentAsync(manifest.Id, Path.Combine(_tempRoot, "Target"));

        Assert.False(result.Success);
        Assert.Contains("has no variant for this host", result.FirstError);
    }

    /// <summary>Existing source directories do not make foreign CAS staging paths required.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task StoreContentAsync_MetadataOnlyWithExistingSource_IgnoresForeignCasStagingPathAsync()
    {
        var source = Path.Combine(_tempRoot, "Source");
        Directory.CreateDirectory(source);
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, SourceType = ContentSourceType.RemoteDownload }],
            [new() { RelativePath = ForeignFileName, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, SourcePath = Path.Combine(_tempRoot, "Foreign", ForeignFileName) }]);
        var result = await _service.StoreContentAsync(manifest, source);
        Assert.True(result.Success, result.FirstError);
        Assert.Null(Assert.Single(ManifestVariantResolver.ResolveFiles(result.Data!, VariantManifestFixture.ForeignRuntimeIdentifier)).SourcePath);
    }

    /// <summary>Metadata sanitization preserves malformed entries for validation to reject.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task StoreContentAsync_NullForeignFile_ReturnsValidationFailureAsync()
    {
        var manifest = VariantManifestFixture.Create([], [null!]);
        var result = await _service.StoreContentAsync(manifest, Path.Combine(_tempRoot, "Missing"));
        Assert.False(result.Success);
        Assert.Contains("null variant or file", result.FirstError);
    }

    /// <summary>Malformed host entries are rejected before missing-source handling.</summary>
    /// <param name="sourceExists">Whether the source directory exists.</param>
    /// <param name="flat">Whether the malformed entry is in the flat file list.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task StoreContentAsync_NullHostFile_ReturnsValidationFailureAsync(bool sourceExists, bool flat)
    {
        var source = Path.Combine(_tempRoot, "MalformedSource");
        if (sourceExists)
        {
            Directory.CreateDirectory(source);
        }

        var manifest = VariantManifestFixture.Create([null!], []);
        if (flat)
        {
            manifest.Variants.Clear();
            manifest.Files = [null!];
        }

        var result = await _service.StoreContentAsync(manifest, source);
        Assert.False(result.Success);
        Assert.Contains("null variant or file", result.FirstError);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }
}
