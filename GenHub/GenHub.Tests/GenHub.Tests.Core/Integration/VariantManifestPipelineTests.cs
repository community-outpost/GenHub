using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Content.Services;
using GenHub.Features.Manifest;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using GenHub.Tests.Core.Models.Manifest;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Integration;

/// <summary>
/// Runs a manifest with platform variants through discovery, the manifest pool, content
/// storage, CAS and workspace preparation, all backed by temporary directories.
/// </summary>
public sealed class VariantManifestPipelineTests : IDisposable
{
    private const string HostEntryPoint = "generalszh-host.exe";
    private const string HostDataFile = "Data/host.big";
    private const string HostProcessName = "generalszh-host-game";
    private const string ForeignEntryPoint = "generalszh-foreign.exe";
    private const string ForeignProcessName = "generalszh-foreign-game";

    private static readonly string ForeignHash = new('f', 64);

    private readonly string _root;
    private readonly string _discoveryDir;
    private readonly string _sourceDir;
    private readonly string _storageRoot;
    private readonly string _casRoot;
    private readonly string _installDir;
    private readonly string _workspaceRoot;
    private readonly CasService _casService;
    private readonly CasReferenceTracker _referenceTracker;
    private readonly ContentStorageService _storageService;
    private readonly ContentManifestPool _pool;
    private readonly ManifestDiscoveryService _discoveryService;

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantManifestPipelineTests"/> class.
    /// </summary>
    public VariantManifestPipelineTests()
    {
        _root = Directory.CreateTempSubdirectory("GenHub.VariantPipeline.").FullName;
        _discoveryDir = Path.Combine(_root, "Discovery");
        _sourceDir = Path.Combine(_root, "Source");
        _storageRoot = Path.Combine(_root, "Storage");
        _casRoot = Path.Combine(_root, "Cas");
        _installDir = Path.Combine(_root, "Install");
        _workspaceRoot = Path.Combine(_root, "Workspaces");
        foreach (var directory in new[] { _discoveryDir, _sourceDir, _installDir, _workspaceRoot })
        {
            Directory.CreateDirectory(directory);
        }

        var casConfig = Options.Create(new CasConfiguration { CasRootPath = _casRoot });
        var hashProvider = new Sha256HashProvider();
        _casService = new CasService(
            new CasStorage(casConfig, NullLogger<CasStorage>.Instance),
            NullLogger<CasService>.Instance,
            hashProvider,
            hashProvider);
        _referenceTracker = new CasReferenceTracker(casConfig, NullLogger<CasReferenceTracker>.Instance);
        _storageService = new ContentStorageService(
            _storageRoot,
            NullLogger<ContentStorageService>.Instance,
            _casService,
            _referenceTracker,
            new CasWriteFence());
        _pool = new ContentManifestPool(_storageService, _referenceTracker, NullLogger<ContentManifestPool>.Instance);

        var configProvider = new Mock<IConfigurationProviderService>();
        configProvider.Setup(x => x.GetApplicationDataPath()).Returns(_root);
        _discoveryService = new ManifestDiscoveryService(
            NullLogger<ManifestDiscoveryService>.Instance,
            new Mock<IManifestCache>().Object,
            configProvider.Object);
    }

    /// <summary>
    /// A format-version-2 variant manifest found on disk is admitted by the pool, which stores
    /// the host variant's files in CAS. Retrieval writes exactly the host variant's files. The
    /// foreign variant's file never exists on disk, so storing it would have failed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DiscoveredVariantManifest_IsStoredAndRetrievesOnlyHostFilesAsync()
    {
        var manifest = await DiscoverAsync(CreateVariantManifest(ManifestConstants.VariantsManifestFormatVersion));

        var addResult = await _pool.AddManifestAsync(manifest, _sourceDir);

        Assert.True(addResult.Success, addResult.FirstError);
        Assert.False(File.Exists(Path.Combine(_sourceDir, ForeignEntryPoint)));

        var stored = await GetStoredManifestAsync(manifest.Id);
        Assert.Equal(ManifestConstants.VariantsManifestFormatVersion.ToString(CultureInfo.InvariantCulture), stored.SchemaVersion);
        Assert.Empty(stored.Files);
        foreach (var file in ManifestVariantResolver.ResolveFiles(stored))
        {
            Assert.Equal(ContentSourceType.ContentAddressable, file.SourceType);
            var exists = await _casService.ExistsAsync(file.Hash);
            Assert.True(exists.Data, $"Host file {file.RelativePath} is not in CAS.");
        }

        var isStored = await _storageService.IsContentStoredAsync(manifest.Id);
        Assert.True(isStored.Data);

        var targetDir = Path.Combine(_root, "Retrieved");
        var retrieveResult = await _storageService.RetrieveContentAsync(manifest.Id, targetDir);

        Assert.True(retrieveResult.Success, retrieveResult.FirstError);
        Assert.Equal(HostRelativePaths(), RelativeFilesUnder(targetDir));

        var referenced = await _referenceTracker.GetAllReferencedHashesAsync();
        Assert.Contains(ForeignHash, referenced);
    }

    /// <summary>
    /// The stored variant manifest resolves the host variant's entry point and launch
    /// relationship, and a full-copy workspace prepared from it holds only the host files
    /// with the host entry point as its executable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task StoredVariantManifest_ResolvesHostLaunchTargetAndMaterializesOnlyHostFilesAsync()
    {
        var manifest = await DiscoverAsync(CreateVariantManifest(ManifestConstants.VariantsManifestFormatVersion));
        var addResult = await _pool.AddManifestAsync(manifest, _sourceDir);
        Assert.True(addResult.Success, addResult.FirstError);

        var stored = await GetStoredManifestAsync(manifest.Id);

        var entryPoint = ManifestVariantResolver.ResolveEntryPoint(stored);
        Assert.True(entryPoint.Success, entryPoint.ToString());
        Assert.Equal(HostEntryPoint, entryPoint.RelativePath);
        Assert.Equal(HostProcessName, ManifestVariantResolver.ResolveLaunchRelationship(stored)?.ProcessName);

        var workspaceResult = await CreateWorkspaceManager().PrepareWorkspaceAsync(new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString("N"),
            Strategy = WorkspaceStrategy.FullCopy,
            WorkspaceRootPath = _workspaceRoot,
            BaseInstallationPath = _installDir,
            GameClient = new GameClient { Id = "variant-pipeline-client" },
            Manifests = [stored],
        });

        Assert.True(workspaceResult.Success, workspaceResult.FirstError);
        var workspace = workspaceResult.Data!;
        Assert.Equal(HostRelativePaths(), RelativeFilesUnder(workspace.WorkspacePath));
        Assert.Equal(Path.GetFullPath(Path.Combine(workspace.WorkspacePath, HostEntryPoint)), Path.GetFullPath(workspace.ExecutablePath));
    }

    /// <summary>
    /// A manifest declaring a format above the supported ceiling is skipped by discovery and
    /// rejected by the pool with the unsupported-format message, before anything reaches
    /// manifest storage, CAS or reference tracking.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task VariantManifestAboveFormatCeiling_IsRejectedBeforeStorageAsync()
    {
        var unsupportedFormat = ManifestConstants.MaxSupportedManifestFormatVersion + 1;
        var manifest = CreateVariantManifest(unsupportedFormat);
        await WriteDiscoverableAsync(manifest);

        var discovered = await _discoveryService.DiscoverManifestsAsync([_discoveryDir]);
        var addResult = await _pool.AddManifestAsync(manifest, _sourceDir);

        Assert.Empty(discovered);
        Assert.False(addResult.Success);
        var expected = string.Format(
            CultureInfo.InvariantCulture,
            ManifestErrorMessages.UnsupportedManifestFormatVersion,
            manifest.Id.Value,
            unsupportedFormat,
            ManifestConstants.MaxSupportedManifestFormatVersion);
        Assert.Contains(expected, addResult.FirstError);

        Assert.False(File.Exists(_storageService.GetManifestStoragePath(manifest.Id)));
        Assert.False(Directory.Exists(_casRoot) && Directory.EnumerateFiles(_casRoot, "*", SearchOption.AllDirectories).Any());
        Assert.Empty(await _referenceTracker.GetAllReferencedHashesAsync());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
    }

    private static string[] HostRelativePaths() => [HostDataFile, HostEntryPoint];

    private static string[] RelativeFilesUnder(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private ContentManifest CreateVariantManifest(int formatVersion)
    {
        foreach (var relativePath in HostRelativePaths())
        {
            var path = Path.Combine(_sourceDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"host content for {relativePath}");
        }

        var manifest = VariantManifestFixture.Create(
            [
                new() { RelativePath = HostEntryPoint, SourceType = ContentSourceType.LocalFile, IsExecutable = true, IsRequired = true },
                new() { RelativePath = HostDataFile, SourceType = ContentSourceType.LocalFile, IsRequired = true },
            ],
            [new() { RelativePath = ForeignEntryPoint, Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable, IsExecutable = true, IsRequired = true }]);
        manifest.SchemaVersion = formatVersion.ToString(CultureInfo.InvariantCulture);

        var host = ManifestVariantResolver.ResolveVariant(manifest)!;
        host.EntryPoint = HostEntryPoint;
        host.LaunchRelationship = new LaunchRelationship { ProcessName = HostProcessName };

        var foreign = ManifestVariantResolver.ResolveVariant(manifest, VariantManifestFixture.ForeignRuntimeIdentifier)!;
        foreign.EntryPoint = ForeignEntryPoint;
        foreign.LaunchRelationship = new LaunchRelationship { ProcessName = ForeignProcessName };

        return manifest;
    }

    private async Task WriteDiscoverableAsync(ContentManifest manifest) =>
        await File.WriteAllTextAsync(Path.Combine(_discoveryDir, "variant.json"), JsonSerializer.Serialize(manifest));

    private async Task<ContentManifest> DiscoverAsync(ContentManifest manifest)
    {
        await WriteDiscoverableAsync(manifest);

        var discovered = await _discoveryService.DiscoverManifestsAsync([_discoveryDir]);

        return Assert.Single(discovered).Value;
    }

    private async Task<ContentManifest> GetStoredManifestAsync(ManifestId manifestId)
    {
        var result = await _pool.GetManifestAsync(manifestId);
        Assert.True(result.Success, result.FirstError);
        return Assert.IsType<ContentManifest>(result.Data);
    }

    private WorkspaceManager CreateWorkspaceManager()
    {
        var configProvider = new Mock<IConfigurationProviderService>();
        configProvider.Setup(x => x.GetApplicationDataPath()).Returns(_root);
        configProvider.Setup(x => x.GetWorkspacePath()).Returns(_workspaceRoot);

        var fileOperations = new FileOperationsService(
            NullLogger<FileOperationsService>.Instance,
            new Mock<IDownloadService>().Object,
            _casService);

        return new WorkspaceManager(
            [new FullCopyStrategy(fileOperations, NullLogger<FullCopyStrategy>.Instance)],
            configProvider.Object,
            NullLogger<WorkspaceManager>.Instance,
            _referenceTracker,
            new WorkspaceValidator(NullLogger<WorkspaceValidator>.Instance),
            new WorkspaceReconciler(NullLogger<WorkspaceReconciler>.Instance, fileOperations));
    }
}
