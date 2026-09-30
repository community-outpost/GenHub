using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for how <see cref="GameLauncher"/> picks the process to discover after a Steam launch.
/// </summary>
public sealed class MonitoringProcessNameTests : IDisposable
{
    private const string BootstrapperHash = "b00757a900000000000000000000000000000000000000000000000000000000";
    private const string ChildHash = "c41d000000000000000000000000000000000000000000000000000000000000";

    private readonly string _root;
    private readonly string _workspace;
    private readonly string _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitoringProcessNameTests"/> class.
    /// </summary>
    public MonitoringProcessNameTests()
    {
        _root = Directory.CreateTempSubdirectory("GenHub.MonitoringProcessNameTests.").FullName;
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "workspace")).FullName;
        _store = Directory.CreateDirectory(Path.Combine(_root, "cas", "objects", "c4")).FullName;
    }

    private static string ExpectedChildName =>
        LaunchEntryPointResolver.ResolveExpectedChildProcessName(GameClientConstants.GeneralsOnlineEacLauncherExecutable)!;

    private string BootstrapperPath => Path.Combine(_workspace, GameClientConstants.GeneralsOnlineEacLauncherExecutable);

    private string ChildWorkspacePath => Path.Combine(_workspace, GameClientConstants.GeneralsOnline60HzExecutable);

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temp directory.
        }
    }

    /// <summary>
    /// A CAS-symlinked bootstrapper launch discovers the wrapped client under its link target's
    /// name, in the link target's directory.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_MonitorsTheChildLinkTarget()
    {
        File.WriteAllText(Path.Combine(_store, ChildHash), "client");
        if (!TryCreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, ChildHash)))
        {
            return;
        }

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ChildHash, result.Data.ProcessName);
        Assert.Equal(_store, result.Data.ResidenceDirectory);
    }

    /// <summary>
    /// The link target, not the manifest hash, names the process: the filesystem is the authority.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_TakesTheNameFromTheLinkTarget()
    {
        const string ObjectName = "differently-named-object";
        File.WriteAllText(Path.Combine(_store, ObjectName), "client");
        if (!TryCreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, ObjectName)))
        {
            return;
        }

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ObjectName, result.Data.ProcessName);
        Assert.Equal(_store, result.Data.ResidenceDirectory);
    }

    /// <summary>
    /// The selector accepts the discovered child with the returned directory and rejects it with
    /// the workspace path, which is what discovery used before.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_ReturnedDirectoryLetsTheSelectorFindTheChild()
    {
        File.WriteAllText(Path.Combine(_store, ChildHash), "client");
        if (!TryCreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, ChildHash)))
        {
            return;
        }

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);
        Assert.True(result.Success, result.FirstError);

        var now = DateTime.UtcNow;
        var imagePath = File.ResolveLinkTarget(ChildWorkspacePath, returnFinalTarget: true)!.FullName;
        var candidates = new[] { new GameProcessCandidate(1, ChildHash, now, imagePath) };

        Assert.NotNull(GameProcessSelector.SelectSpawnedGameProcess(
            candidates, result.Data.ProcessName, result.Data.ResidenceDirectory, now));
        Assert.Null(GameProcessSelector.SelectSpawnedGameProcess(
            candidates, result.Data.ProcessName, _workspace, now));
    }

    /// <summary>
    /// A child that is a plain file in the workspace keeps its hash and the workspace directory.
    /// </summary>
    [Fact]
    public void CasBootstrapper_WithAPlainChildFile_MonitorsTheChildHashInTheWorkspace()
    {
        File.WriteAllText(ChildWorkspacePath, "client");

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ChildHash, result.Data.ProcessName);
        Assert.Equal(_workspace, result.Data.ResidenceDirectory);
    }

    /// <summary>
    /// A wrapped client without a hash fails the launch rather than monitoring the bootstrapper.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithoutTheChildHash_Fails()
    {
        File.WriteAllText(ChildWorkspacePath, "client");

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: string.Empty), WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
        Assert.Contains(ExpectedChildName, result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A manifest that omits the wrapped client fails the launch rather than monitoring the bootstrapper.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithoutTheChildFile_Fails()
    {
        var result = Resolve(BuildGeneralsOnlineManifests(childHash: null), WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
    }

    /// <summary>
    /// A child missing from the workspace fails the launch.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithoutTheChildInTheWorkspace_Fails()
    {
        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
    }

    /// <summary>
    /// A child link whose target is gone fails the launch.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithADanglingChildLink_Fails()
    {
        if (!TryCreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, "missing-object")))
        {
            return;
        }

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
    }

    /// <summary>
    /// A wrapped client from outside CAS, present as a plain file, keeps its own name.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithANonCasChild_MonitorsTheChildName()
    {
        File.WriteAllText(ChildWorkspacePath, "client");

        var result = Resolve(
            BuildGeneralsOnlineManifests(childHash: ChildHash, childSource: ContentSourceType.GameInstallation),
            WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ExpectedChildName, result.Data.ProcessName);
        Assert.Equal(_workspace, result.Data.ResidenceDirectory);
    }

    /// <summary>
    /// A CAS executable that is itself the game and is not a link keeps monitoring its own hash.
    /// </summary>
    [Fact]
    public void CasDirectExecutable_MonitorsItsOwnHash()
    {
        var result = GameLauncher.DetermineMonitoringTarget(
            BuildGeneralsOnlineManifests(childHash: ChildHash),
            BootstrapperPath,
            _workspace,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(BootstrapperHash, result.Data.ProcessName);
        Assert.Equal(_workspace, result.Data.ResidenceDirectory);
    }

    /// <summary>
    /// A CAS-symlinked executable that is itself the game is discovered in its link target's directory.
    /// </summary>
    [Fact]
    public void CasSymlinkedDirectExecutable_MonitorsItsLinkTarget()
    {
        File.WriteAllText(Path.Combine(_store, BootstrapperHash), "client");
        if (!TryCreateSymbolicLink(BootstrapperPath, Path.Combine(_store, BootstrapperHash)))
        {
            return;
        }

        var result = GameLauncher.DetermineMonitoringTarget(
            BuildGeneralsOnlineManifests(childHash: ChildHash),
            BootstrapperPath,
            _workspace,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(BootstrapperHash, result.Data.ProcessName);
        Assert.Equal(_store, result.Data.ResidenceDirectory);
    }

    /// <summary>
    /// Outside CAS symlinking the expected child is monitored by name in the workspace.
    /// </summary>
    [Fact]
    public void NonSymlinkStrategy_MonitorsTheChildName()
    {
        var result = Resolve(BuildGeneralsOnlineManifests(childHash: string.Empty), WorkspaceStrategy.HardLink);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ExpectedChildName, result.Data.ProcessName);
        Assert.Equal(_workspace, result.Data.ResidenceDirectory);
    }

    private static bool TryCreateSymbolicLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows without Developer Mode or elevation cannot create symbolic links.
            return false;
        }
    }

    private static List<ContentManifest> BuildGeneralsOnlineManifests(
        string? childHash,
        ContentSourceType childSource = ContentSourceType.ContentAddressable)
    {
        var client = new ContentManifest
        {
            Name = "GeneralsOnline",
            ContentType = ContentType.GameClient,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = GameClientConstants.GeneralsOnlineEacLauncherExecutable,
                    Hash = BootstrapperHash,
                    SourceType = ContentSourceType.ContentAddressable,
                    IsExecutable = true,
                },
            ],
        };

        if (childHash is not null)
        {
            client.Files.Add(new ManifestFile
            {
                RelativePath = GameClientConstants.GeneralsOnline60HzExecutable,
                Hash = childHash,
                SourceType = childSource,
            });
        }

        var installation = new ContentManifest
        {
            Name = "Zero Hour",
            ContentType = ContentType.GameInstallation,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "INIZH.big",
                    Hash = "d47a000000000000000000000000000000000000000000000000000000000000",
                    SourceType = ContentSourceType.GameInstallation,
                },
            ],
        };

        return [installation, client];
    }

    private OperationResult<(string ProcessName, string ResidenceDirectory)> Resolve(
        IReadOnlyList<ContentManifest> manifests,
        WorkspaceStrategy strategy) =>
        GameLauncher.DetermineMonitoringTarget(
            manifests,
            BootstrapperPath,
            _workspace,
            strategy,
            ExpectedChildName,
            NullLogger.Instance,
            localizationService: null);
}
