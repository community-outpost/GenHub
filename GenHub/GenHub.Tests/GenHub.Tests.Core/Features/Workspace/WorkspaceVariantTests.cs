using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using GenHub.Tests.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests that workspace preparation and reconciliation use the host variant's files of
/// manifests whose files live in platform variants rather than the flat list.
/// </summary>
public sealed class WorkspaceVariantTests : IDisposable
{
    private const string HostFileName = "generals.exe";
    private const string ForeignFileName = "generalszh-foreign.exe";

    private readonly Mock<IFileOperationsService> _fileOperations = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
    private readonly string _installDir;
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceVariantTests"/> class.
    /// </summary>
    public WorkspaceVariantTests()
    {
        _installDir = Path.Combine(_root, "Install");
        _workspaceRoot = Path.Combine(_root, "Workspaces");
        Directory.CreateDirectory(_installDir);
        Directory.CreateDirectory(_workspaceRoot);
    }

    /// <summary>
    /// Every strategy materializes the host variant's files and never the foreign variant's.
    /// </summary>
    /// <param name="strategyType">The strategy under test.</param>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public async Task PrepareAsync_WithVariantManifest_MaterializesOnlyHostVariantAsync(WorkspaceStrategy strategyType)
    {
        var configuration = CreateConfiguration(strategyType, CreateManifest());

        var result = await CreateStrategy(strategyType).PrepareAsync(configuration, null, CancellationToken.None);

        Assert.True(result.IsPrepared);

        Assert.True(FileOperationsTouched(HostFileName), $"{strategyType} did not materialize the host variant's file.");
        Assert.False(FileOperationsTouched(ForeignFileName), $"{strategyType} materialized the foreign variant's file.");
    }

    /// <summary>
    /// The reconciler plans only the host variant's files.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task AnalyzeWorkspaceDeltaAsync_WithVariantManifest_PlansOnlyHostVariantAsync()
    {
        var reconciler = new WorkspaceReconciler(new Mock<ILogger<WorkspaceReconciler>>().Object, _fileOperations.Object);
        var configuration = CreateConfiguration(WorkspaceStrategy.HybridCopySymlink, CreateManifest());

        var deltas = await reconciler.AnalyzeWorkspaceDeltaAsync(null, configuration);

        Assert.Contains(deltas, d => d.File.RelativePath == HostFileName);
        Assert.DoesNotContain(deltas, d => d.File.RelativePath == ForeignFileName);
    }

    /// <summary>
    /// Unique workspace entries pair each host-variant file with the manifest it came from.
    /// </summary>
    [Fact]
    public void GetWorkspaceUniqueFileEntries_WithVariantManifest_PairsHostFilesWithOwner()
    {
        var manifest = CreateManifest();
        var configuration = CreateConfiguration(WorkspaceStrategy.SymlinkOnly, manifest);

        var entry = Assert.Single(configuration.GetWorkspaceUniqueFileEntries());

        Assert.Equal(HostFileName, entry.File.RelativePath);
        Assert.Same(manifest, entry.Manifest);
    }

    /// <summary>A null file entry in the host variant is skipped instead of throwing.</summary>
    [Fact]
    public void GetWorkspaceUniqueFileEntries_WithNullHostEntry_SkipsIt()
    {
        var manifest = CreateManifest();
        ManifestVariantResolver.ResolveVariant(manifest)!.Files.Insert(0, null!);
        var configuration = CreateConfiguration(WorkspaceStrategy.SymlinkOnly, manifest);

        Assert.Equal(HostFileName, Assert.Single(configuration.GetWorkspaceUniqueFileEntries()).File.RelativePath);
        Assert.Equal(HostFileName, Assert.Single(configuration.GetAllUniqueFiles()).RelativePath);
    }

    /// <summary>Every strategy estimates only the unique files it will place in the workspace.</summary>
    /// <param name="strategyType">The strategy under test.</param>
    [Theory]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public void EstimateDiskUsage_ExcludesNonWorkspaceAndDuplicateFiles(WorkspaceStrategy strategyType)
    {
        var manifest = CreateManifest();
        var configuration = CreateConfiguration(strategyType, manifest);
        var strategy = CreateStrategy(strategyType);
        var expected = strategy.EstimateDiskUsage(configuration);
        Assert.True(expected > 0);
        configuration.Manifests.Add(manifest);
        ManifestVariantResolver.ResolveVariant(manifest)!.Files.Add(new ManifestFile
        {
            RelativePath = "user-data.txt",
            InstallTarget = ContentInstallTarget.UserDataDirectory,
        });
        Assert.Equal(expected, strategy.EstimateDiskUsage(configuration));
    }

    /// <summary>Progress completes using the same unique workspace entries that are processed.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task PrepareAsync_SymlinkProgress_ExcludesDuplicatesAndNonWorkspaceFilesAsync()
    {
        var manifest = CreateManifest();
        var configuration = CreateConfiguration(WorkspaceStrategy.SymlinkOnly, manifest);
        configuration.Manifests.Add(manifest);
        ManifestVariantResolver.ResolveVariant(manifest)!.Files.Add(new ManifestFile
        {
            RelativePath = "user-data.txt",
            InstallTarget = ContentInstallTarget.UserDataDirectory,
        });
        var progress = new Mock<IProgress<WorkspacePreparationProgress>>();
        var result = await CreateStrategy(WorkspaceStrategy.SymlinkOnly).PrepareAsync(configuration, progress.Object, CancellationToken.None);
        Assert.True(result.IsPrepared);
        progress.Verify(
            p => p.Report(It.Is<WorkspacePreparationProgress>(v =>
                v.CurrentOperation == "Creating symlinks" && v.TotalFiles == 1 && v.FilesProcessed == 1)),
            Times.Once);
    }

    /// <summary>Hybrid preparation copies a duplicate host file only once.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task PrepareAsync_Hybrid_DeduplicatesActualFileOperationsAsync()
    {
        var manifest = CreateManifest();
        var configuration = CreateConfiguration(WorkspaceStrategy.HybridCopySymlink, manifest);
        configuration.Manifests.Add(manifest);
        var result = await CreateStrategy(WorkspaceStrategy.HybridCopySymlink).PrepareAsync(configuration, null, CancellationToken.None);
        Assert.True(result.IsPrepared);
        _fileOperations.Verify(
            f => f.CopyFileAsync(It.Is<string>(p => p.EndsWith(HostFileName)), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
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
    }

    private ContentManifest CreateManifest()
    {
        var hostPath = Path.Combine(_installDir, HostFileName);
        var foreignPath = Path.Combine(_installDir, ForeignFileName);
        File.WriteAllText(hostPath, "host");
        File.WriteAllText(foreignPath, "foreign");

        return VariantManifestFixture.Create(
            [new() { RelativePath = HostFileName, SourcePath = hostPath, Size = 4, SourceType = ContentSourceType.GameInstallation }],
            [new() { RelativePath = ForeignFileName, SourcePath = foreignPath, Size = 7, SourceType = ContentSourceType.GameInstallation }]);
    }

    private WorkspaceConfiguration CreateConfiguration(WorkspaceStrategy strategy, ContentManifest manifest) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Strategy = strategy,
        WorkspaceRootPath = _workspaceRoot,
        BaseInstallationPath = _installDir,
        GameClient = new GameClient { Id = "test" },
        Manifests = [manifest],
    };

    private bool FileOperationsTouched(string fileName) =>
        _fileOperations.Invocations.Any(invocation => invocation.Arguments
            .OfType<string>()
            .Any(argument => argument.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)));

    private IWorkspaceStrategy CreateStrategy(WorkspaceStrategy strategyType) => strategyType switch
    {
        WorkspaceStrategy.FullCopy => new FullCopyStrategy(_fileOperations.Object, new Mock<ILogger<FullCopyStrategy>>().Object),
        WorkspaceStrategy.SymlinkOnly => new SymlinkOnlyStrategy(_fileOperations.Object, new Mock<ILogger<SymlinkOnlyStrategy>>().Object),
        WorkspaceStrategy.HybridCopySymlink => new HybridCopySymlinkStrategy(_fileOperations.Object, new Mock<ILogger<HybridCopySymlinkStrategy>>().Object),
        WorkspaceStrategy.HardLink => new HardLinkStrategy(_fileOperations.Object, new Mock<ILogger<HardLinkStrategy>>().Object),
        _ => throw new ArgumentException($"Unknown strategy type: {strategyType}"),
    };
}
