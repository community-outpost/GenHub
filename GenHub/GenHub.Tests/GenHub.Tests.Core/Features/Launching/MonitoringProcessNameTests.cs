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
        AssertIdentities(result, new GameProcessIdentity(ExpectedChildName, _workspace), new GameProcessIdentity(ChildHash, _store));
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
        AssertIdentities(result, new GameProcessIdentity(ExpectedChildName, _workspace), new GameProcessIdentity(ObjectName, _store));
    }

    /// <summary>
    /// Windows names a process started through a link after the target but reports the link as its
    /// image. The returned identities select it, and the target identity alone would not.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_IdentitiesSelectTheWindowsShape()
    {
        var identities = ResolveLinkedChildIdentities();
        if (identities is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var candidates = new[] { new GameProcessCandidate(1, ChildHash, now, ChildWorkspacePath) };

        Assert.NotNull(GameProcessSelector.SelectSpawnedGameProcess(candidates, identities, now));
        Assert.Null(GameProcessSelector.SelectSpawnedGameProcess(candidates, [identities[1]], now));
    }

    /// <summary>
    /// Linux names a process started through a link after the link but reports the target as its
    /// image. The returned identities select it, and the link identity alone would not.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_IdentitiesSelectTheLinuxShape()
    {
        var identities = ResolveLinkedChildIdentities();
        if (identities is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var truncatedLinkName = ExpectedChildName[..ProcessConstants.UnixProcessNameMaxLength];
        var candidates = new[] { new GameProcessCandidate(1, truncatedLinkName, now, Path.Combine(_store, ChildHash)) };

        Assert.NotNull(GameProcessSelector.SelectSpawnedGameProcess(candidates, identities, now));
        Assert.Null(GameProcessSelector.SelectSpawnedGameProcess(candidates, [identities[0]], now));
    }

    /// <summary>
    /// A name match is never enough: a process carrying either name from another directory is rejected.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_IdentitiesRejectANameMatchElsewhere()
    {
        var identities = ResolveLinkedChildIdentities();
        if (identities is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;
        var candidates = new[]
        {
            new GameProcessCandidate(1, ChildHash, now, Path.Combine(elsewhere, ChildHash)),
            new GameProcessCandidate(2, ExpectedChildName, now, Path.Combine(elsewhere, GameClientConstants.GeneralsOnline60HzExecutable)),
        };

        Assert.Null(GameProcessSelector.SelectSpawnedGameProcess(candidates, identities, now));
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
        AssertIdentities(result, new GameProcessIdentity(ChildHash, _workspace));
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
    /// A wrapped client from outside CAS that fell back to a plain file keeps its own name.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithANonCasChild_MonitorsTheChildName()
    {
        File.WriteAllText(ChildWorkspacePath, "client");

        var result = Resolve(
            BuildGeneralsOnlineManifests(childHash: ChildHash, childSource: ContentSourceType.GameInstallation),
            WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, new GameProcessIdentity(ExpectedChildName, _workspace));
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
        AssertIdentities(result, new GameProcessIdentity(BootstrapperHash, _workspace));
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
        AssertIdentities(
            result,
            new GameProcessIdentity(Path.GetFileNameWithoutExtension(GameClientConstants.GeneralsOnlineEacLauncherExecutable), _workspace),
            new GameProcessIdentity(BootstrapperHash, _store));
    }

    /// <summary>
    /// Outside CAS symlinking the expected child is monitored by name in the workspace.
    /// </summary>
    [Fact]
    public void NonSymlinkStrategy_MonitorsTheChildName()
    {
        var result = Resolve(BuildGeneralsOnlineManifests(childHash: string.Empty), WorkspaceStrategy.HardLink);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, new GameProcessIdentity(ExpectedChildName, _workspace));
    }

    /// <summary>
    /// A same-stem sibling such as a symbol file does not stand in for the wrapped client.
    /// </summary>
    [Fact]
    public void CasBootstrapper_IgnoresASameStemSiblingOfTheChild()
    {
        const string SiblingHash = "5b1b000000000000000000000000000000000000000000000000000000000000";
        File.WriteAllText(ChildWorkspacePath, "client");
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash);
        var client = manifests.Single(m => m.ContentType == ContentType.GameClient);
        client.Files.Insert(1, new ManifestFile
        {
            RelativePath = Path.ChangeExtension(GameClientConstants.GeneralsOnline60HzExecutable, ".pdb"),
            Hash = SiblingHash,
            SourceType = ContentSourceType.ContentAddressable,
        });

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, new GameProcessIdentity(ChildHash, _workspace));
    }

    /// <summary>
    /// A CAS entry point without a hash fails the launch instead of monitoring an empty name.
    /// </summary>
    [Fact]
    public void CasDirectExecutable_WithoutAHash_Fails()
    {
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash);
        manifests.Single(m => m.ContentType == ContentType.GameClient).Files[0].Hash = string.Empty;

        var result = GameLauncher.DetermineMonitoringTarget(
            manifests,
            BootstrapperPath,
            _workspace,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        Assert.False(result.Success);
        Assert.Contains(GameClientConstants.GeneralsOnlineEacLauncherExecutable, result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The child is found when only another selected manifest carries it.
    /// </summary>
    [Fact]
    public void CasBootstrapper_FindsTheChildInAnotherManifest()
    {
        File.WriteAllText(ChildWorkspacePath, "client");
        var manifests = BuildGeneralsOnlineManifests(childHash: null);
        manifests.Add(BuildChildOnlyManifest(ChildHash));

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, new GameProcessIdentity(ChildHash, _workspace));
    }

    /// <summary>
    /// When several manifests list the child, the entry point's manifest wins, even when another
    /// manifest comes first.
    /// </summary>
    [Fact]
    public void CasBootstrapper_PrefersTheEntryManifestsChild()
    {
        const string OtherHash = "0e4e000000000000000000000000000000000000000000000000000000000000";
        File.WriteAllText(ChildWorkspacePath, "client");
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash);
        manifests.Insert(0, BuildChildOnlyManifest(OtherHash));

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, new GameProcessIdentity(ChildHash, _workspace));
    }

    /// <summary>
    /// A child listed only in another directory is not the one the bootstrapper starts.
    /// </summary>
    [Fact]
    public void CasBootstrapper_IgnoresAChildInAnotherDirectory()
    {
        var subdirectory = Directory.CreateDirectory(Path.Combine(_workspace, "bin")).FullName;
        File.WriteAllText(Path.Combine(subdirectory, GameClientConstants.GeneralsOnline60HzExecutable), "client");
        var manifests = BuildGeneralsOnlineManifests(childHash: null);
        manifests.Single(m => m.ContentType == ContentType.GameClient).Files.Add(new ManifestFile
        {
            RelativePath = "bin/" + GameClientConstants.GeneralsOnline60HzExecutable,
            Hash = ChildHash,
            SourceType = ContentSourceType.ContentAddressable,
        });

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
    }

    /// <summary>
    /// A non-CAS child is symlinked into the workspace like any other file, so it is discovered
    /// under its link target's name in the link target's directory.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithASymlinkedNonCasChild_MonitorsTheLinkTarget()
    {
        var installation = Directory.CreateDirectory(Path.Combine(_root, "installation")).FullName;
        var installedChild = Path.Combine(installation, GameClientConstants.GeneralsOnline60HzExecutable);
        File.WriteAllText(installedChild, "client");
        if (!TryCreateSymbolicLink(ChildWorkspacePath, installedChild))
        {
            return;
        }

        var result = Resolve(
            BuildGeneralsOnlineManifests(childHash: ChildHash, childSource: ContentSourceType.GameInstallation),
            WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, new GameProcessIdentity(ExpectedChildName, _workspace), new GameProcessIdentity(ExpectedChildName, installation));
    }

    private static void AssertIdentities(
        OperationResult<IReadOnlyList<GameProcessIdentity>> result,
        params GameProcessIdentity[] expected)
    {
        Assert.True(result.Success, result.FirstError);
        Assert.Equal(expected, result.Data);
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

    private static ContentManifest BuildChildOnlyManifest(string childHash) =>
        new()
        {
            Name = "GeneralsOnline client files",
            ContentType = ContentType.Addon,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = GameClientConstants.GeneralsOnline60HzExecutable,
                    Hash = childHash,
                    SourceType = ContentSourceType.ContentAddressable,
                },
            ],
        };

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

    private IReadOnlyList<GameProcessIdentity>? ResolveLinkedChildIdentities()
    {
        File.WriteAllText(Path.Combine(_store, ChildHash), "client");
        if (!TryCreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, ChildHash)))
        {
            return null;
        }

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);
        Assert.True(result.Success, result.FirstError);
        return result.Data;
    }

    private OperationResult<IReadOnlyList<GameProcessIdentity>> Resolve(
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
