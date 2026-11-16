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
using ContentType = GenHub.Core.Models.Enums.ContentType;

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

    /// <summary>Gets every strategy paired with each shared-path collision case.</summary>
    /// <returns>The strategy, the two content types in load order, and the expected winner.</returns>
    public static TheoryData<WorkspaceStrategy, ContentType, ContentType, int> SharedPathCollisionCases()
    {
        var data = new TheoryData<WorkspaceStrategy, ContentType, ContentType, int>();
        foreach (var strategy in new[] { WorkspaceStrategy.HybridCopySymlink, WorkspaceStrategy.SymlinkOnly, WorkspaceStrategy.HardLink, WorkspaceStrategy.FullCopy })
        {
            data.Add(strategy, ContentType.Mod, ContentType.Mod, 1);
            data.Add(strategy, ContentType.Addon, ContentType.Addon, 1);
            data.Add(strategy, ContentType.Mod, ContentType.GameInstallation, 0);
            data.Add(strategy, ContentType.GameInstallation, ContentType.Mod, 1);
        }

        return data;
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
    /// A non-client variant manifest that declares its entry point only on the host variant
    /// still supplies the workspace executable, matching the launch target lookup.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PrepareAsync_ModVariantManifestWithHostEntryPoint_ResolvesHostExecutableAsync()
    {
        var manifest = CreateManifest();
        manifest.ContentType = ContentType.Mod;
        manifest.EntryPoint = null;
        ManifestVariantResolver.ResolveVariant(manifest)!.EntryPoint = HostFileName;

        var result = await CreateStrategy(WorkspaceStrategy.FullCopy).PrepareAsync(CreateConfiguration(WorkspaceStrategy.FullCopy, manifest), null, CancellationToken.None);

        Assert.EndsWith(HostFileName, result.ExecutablePath, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Link strategies give executables a private copy on Unix, so the estimate counts
    /// their full size rather than only the link overhead.
    /// </summary>
    /// <param name="strategyType">The strategy under test.</param>
    [Theory]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public void EstimateDiskUsage_LinkStrategies_CountExecutableCopiesOnUnix(WorkspaceStrategy strategyType)
    {
        const long executableSize = 50_000_000;
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.estimate"),
            ContentType = ContentType.GameClient,
            Files = [new() { RelativePath = HostFileName, Size = executableSize, IsExecutable = true, SourceType = ContentSourceType.GameInstallation }],
        };

        var estimate = CreateStrategy(strategyType).EstimateDiskUsage(CreateConfiguration(strategyType, manifest));

        if (OperatingSystem.IsWindows())
        {
            Assert.True(estimate < executableSize);
        }
        else
        {
            Assert.True(estimate >= executableSize, $"{strategyType} estimated {estimate} bytes for a {executableSize}-byte executable.");
        }
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

    /// <summary>
    /// When two manifests ship the same path, every strategy materializes the copy from the
    /// higher-priority content type, and on equal priority the copy from the later manifest.
    /// Full copy writes every copy in priority order, so the winner is the copy written last.
    /// </summary>
    /// <param name="strategyType">The strategy under test.</param>
    /// <param name="firstType">The content type of the first manifest in load order.</param>
    /// <param name="secondType">The content type of the second manifest in load order.</param>
    /// <param name="expectedWinner">The index of the manifest whose copy must be materialized.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [MemberData(nameof(SharedPathCollisionCases))]
    public async Task PrepareAsync_SharedPathCollision_MaterializesWinningManifestAsync(
        WorkspaceStrategy strategyType,
        ContentType firstType,
        ContentType secondType,
        int expectedWinner)
    {
        const string sharedPath = "Data/INI/GameData.ini";
        var sources = new[] { Path.Combine(_installDir, "first.ini"), Path.Combine(_installDir, "second.ini") };
        File.WriteAllText(sources[0], "first");
        File.WriteAllText(sources[1], "second");
        var configuration = CreateConfiguration(strategyType, CreateCollidingManifest("1.0.test.mod.first", firstType, sharedPath, sources[0]));
        configuration.Manifests.Add(CreateCollidingManifest("1.0.test.mod.second", secondType, sharedPath, sources[1]));

        var result = await CreateStrategy(strategyType).PrepareAsync(configuration, null, CancellationToken.None);

        Assert.True(result.IsPrepared);
        Assert.Equal(sources[expectedWinner], LastMaterializedSource(sharedPath, sources));
    }

    /// <summary>
    /// Full copy copies only the winning manifest's file for a shared path, so a losing manifest
    /// whose CAS object is missing cannot fail preparation.
    /// </summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task PrepareAsync_FullCopy_LosingManifestWithMissingCasObject_DoesNotFailAsync()
    {
        const string sharedPath = "Data/INI/GameData.ini";
        const string losingHash = "missing_losing_hash";
        var winningSource = Path.Combine(_installDir, "winner.ini");
        File.WriteAllText(winningSource, "winner");
        var losing = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameinstallation.base"),
            ContentType = ContentType.GameInstallation,
            Files = [new() { RelativePath = sharedPath, Hash = losingHash, Size = 6, SourceType = ContentSourceType.ContentAddressable }],
        };
        var configuration = CreateConfiguration(WorkspaceStrategy.FullCopy, losing);
        configuration.Manifests.Add(CreateCollidingManifest("1.0.test.mod.winner", ContentType.Mod, sharedPath, winningSource));
        _fileOperations
            .Setup(f => f.CopyFromCasAsync(losingHash, It.IsAny<string>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateStrategy(WorkspaceStrategy.FullCopy).PrepareAsync(configuration, null, CancellationToken.None);

        Assert.True(result.IsPrepared);
        _fileOperations.Verify(
            f => f.CopyFromCasAsync(losingHash, It.IsAny<string>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(winningSource, LastMaterializedSource(sharedPath, [winningSource]));
    }

    /// <summary>
    /// The reconciler resolves a shared path the same way the strategies do.
    /// </summary>
    /// <param name="firstType">The content type of the first manifest in load order.</param>
    /// <param name="secondType">The content type of the second manifest in load order.</param>
    /// <param name="expectedWinner">The index of the manifest whose copy must be planned.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(ContentType.Mod, ContentType.Mod, 1)]
    [InlineData(ContentType.Mod, ContentType.GameInstallation, 0)]
    [InlineData(ContentType.GameInstallation, ContentType.Mod, 1)]
    public async Task AnalyzeWorkspaceDeltaAsync_SharedPathCollision_PlansWinningManifestAsync(
        ContentType firstType,
        ContentType secondType,
        int expectedWinner)
    {
        const string sharedPath = "Data/INI/GameData.ini";
        var sources = new[] { Path.Combine(_installDir, "first.ini"), Path.Combine(_installDir, "second.ini") };
        var reconciler = new WorkspaceReconciler(new Mock<ILogger<WorkspaceReconciler>>().Object, _fileOperations.Object);
        var configuration = CreateConfiguration(WorkspaceStrategy.HybridCopySymlink, CreateCollidingManifest("1.0.test.mod.first", firstType, sharedPath, sources[0]));
        configuration.Manifests.Add(CreateCollidingManifest("1.0.test.mod.second", secondType, sharedPath, sources[1]));

        var deltas = await reconciler.AnalyzeWorkspaceDeltaAsync(null, configuration);

        Assert.Equal(sources[expectedWinner], Assert.Single(deltas).File.SourcePath);
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

    private static ContentManifest CreateCollidingManifest(string id, ContentType type, string relativePath, string source) => new()
    {
        Id = ManifestId.Create(id),
        ContentType = type,
        Files = [new() { RelativePath = relativePath, SourcePath = source, Size = 5, SourceType = ContentSourceType.GameInstallation }],
    };

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

    private string? LastMaterializedSource(string relativePath, string[] sources) =>
        _fileOperations.Invocations
            .Select(invocation => invocation.Arguments.OfType<string>().ToList())
            .Where(arguments => arguments.Any(argument => argument.Replace('\\', '/').EndsWith(relativePath, StringComparison.OrdinalIgnoreCase)))
            .Select(arguments => arguments.FirstOrDefault(sources.Contains))
            .LastOrDefault(source => source is not null);

    private IWorkspaceStrategy CreateStrategy(WorkspaceStrategy strategyType) => strategyType switch
    {
        WorkspaceStrategy.FullCopy => new FullCopyStrategy(_fileOperations.Object, new Mock<ILogger<FullCopyStrategy>>().Object),
        WorkspaceStrategy.SymlinkOnly => new SymlinkOnlyStrategy(_fileOperations.Object, new Mock<ILogger<SymlinkOnlyStrategy>>().Object),
        WorkspaceStrategy.HybridCopySymlink => new HybridCopySymlinkStrategy(_fileOperations.Object, new Mock<ILogger<HybridCopySymlinkStrategy>>().Object),
        WorkspaceStrategy.HardLink => new HardLinkStrategy(_fileOperations.Object, new Mock<ILogger<HardLinkStrategy>>().Object),
        _ => throw new ArgumentException($"Unknown strategy type: {strategyType}"),
    };
}
