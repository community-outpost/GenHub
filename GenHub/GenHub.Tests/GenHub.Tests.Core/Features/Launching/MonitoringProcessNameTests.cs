using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Infrastructure;
using GenHub.Features.Launching;
using GenHub.Tests.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for how <see cref="GameLauncher"/> picks the process to discover after a Steam launch.
/// </summary>
public sealed class MonitoringProcessNameTests : IDisposable
{
    private const string BootstrapperHash = "b00757a900000000000000000000000000000000000000000000000000000000";
    private const string InstalledClientFileName = "installed-client.exe";
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

    private GameProcessIdentity[] ChildWorkspaceIdentities =>
    [
        new GameProcessIdentity(GameClientConstants.GeneralsOnline60HzExecutable, _workspace),
        new GameProcessIdentity(ExpectedChildName, _workspace),
    ];

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
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_MonitorsTheChildLinkTarget()
    {
        File.WriteAllText(Path.Combine(_store, ChildHash), "client");
        File.CreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, ChildHash));

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, [.. ChildWorkspaceIdentities, new GameProcessIdentity(ChildHash, _store)]);
    }

    /// <summary>
    /// The link target, not the manifest hash, names the process: the filesystem is the authority.
    /// </summary>
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_TakesTheNameFromTheLinkTarget()
    {
        const string ObjectName = "differently-named-object";
        File.WriteAllText(Path.Combine(_store, ObjectName), "client");
        File.CreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, ObjectName));

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(result, [.. ChildWorkspaceIdentities, new GameProcessIdentity(ObjectName, _store)]);
    }

    /// <summary>
    /// Windows names a process started through a link after the target but reports the link as its
    /// image. The returned identities select it, and the target identity alone would not.
    /// </summary>
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_IdentitiesSelectTheWindowsShape()
    {
        var identities = ResolveLinkedChildIdentities();

        var now = DateTime.UtcNow;
        var candidates = new[] { new GameProcessCandidate(1, ChildHash, now, ChildWorkspacePath) };

        Assert.NotNull(GameProcessSelector.SelectSpawnedGameProcess(candidates, identities, now));
        Assert.Null(GameProcessSelector.SelectSpawnedGameProcess(candidates, [.. identities.Where(identity => identity.Directory == _store)], now));
    }

    /// <summary>
    /// A readable target image path selects the target identity regardless of the reported name.
    /// The workspace identities alone cannot select a process whose image is in the store.
    /// </summary>
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_TargetImageSelectsTargetIdentity()
    {
        var identities = ResolveLinkedChildIdentities();

        var now = DateTime.UtcNow;
        var candidates = new[] { new GameProcessCandidate(1, GameClientConstants.GeneralsOnline60HzExecutable, now, Path.Combine(_store, ChildHash)) };

        Assert.NotNull(GameProcessSelector.SelectSpawnedGameProcess(candidates, identities, now));
        Assert.Null(GameProcessSelector.SelectSpawnedGameProcess(candidates, [.. identities.Where(identity => identity.Directory == _workspace)], now));
    }

    /// <summary>
    /// Enumeration asks for the full name as well as the truncated one, because .NET on Linux can
    /// report the full link name from the command line.
    /// </summary>
    [Fact]
    public void DiscoveryNames_IncludeTheFullNameAndTheTruncatedOne()
    {
        var names = GameProcessSelector.GetDiscoveryNames(GameClientConstants.GeneralsOnline60HzExecutable);

        Assert.Contains(GameClientConstants.GeneralsOnline60HzExecutable, names);
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(GameClientConstants.GeneralsOnline60HzExecutable.Length > ProcessConstants.UnixProcessNameMaxLength);
            Assert.Contains(GameClientConstants.GeneralsOnline60HzExecutable[..ProcessConstants.UnixProcessNameMaxLength], names);
        }
    }

    /// <summary>
    /// A process matched through the link is preferred over a newer one that only matches the shared
    /// CAS target, which another profile running the same object would also match.
    /// </summary>
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_PrefersALinkMatchOverANewerTargetOnlyMatch()
    {
        var identities = ResolveLinkedChildIdentities();
        var now = DateTime.UtcNow;
        var candidates = new[]
        {
            new GameProcessCandidate(1, ChildHash, now.AddSeconds(-2), ChildWorkspacePath),
            new GameProcessCandidate(2, ChildHash, now.AddSeconds(-1), Path.Combine(_store, ChildHash)),
        };

        Assert.Equal(1, GameProcessSelector.SelectSpawnedGameProcess(candidates, identities, now)?.ProcessId);
    }

    /// <summary>
    /// The retail launcher hands the session to game.dat. When the discovered process's image path
    /// cannot be read, the recorded executable must be game.dat, not a reconstructed game.exe.
    /// </summary>
    [Fact]
    public void RetailLauncher_WithAPlainGameDatChild_FallsBackToTheGameDatPath()
    {
        var entryPath = Path.Combine(_workspace, GameClientConstants.GeneralsExecutable);
        var gameDatPath = Path.Combine(_workspace, GameClientConstants.SteamGameDatExecutable);
        File.WriteAllText(entryPath, "launcher");
        File.WriteAllText(gameDatPath, "game");
        var manifests = new List<ContentManifest>
        {
            new()
            {
                Name = "Generals",
                ContentType = ContentType.GameClient,
                Files =
                [
                    new ManifestFile
                    {
                        RelativePath = GameClientConstants.GeneralsExecutable,
                        Hash = BootstrapperHash,
                        SourceType = ContentSourceType.ContentAddressable,
                        IsExecutable = true,
                    },
                    new ManifestFile
                    {
                        RelativePath = GameClientConstants.SteamGameDatExecutable,
                        Hash = ChildHash,
                        SourceType = ContentSourceType.ContentAddressable,
                    },
                ],
            },
        };

        var result = GameLauncher.DetermineMonitoringTarget(
            manifests,
            entryPath,
            _workspace,
            WorkspaceStrategy.SymlinkOnly,
            LaunchEntryPointResolver.ResolveExpectedChildProcessName(entryPath),
            NullLogger.Instance,
            localizationService: null);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(gameDatPath, GameProcessManager.BuildDiscoveryFallbackPath(result.Data!, isWindows: true));
        Assert.Equal(gameDatPath, GameProcessManager.BuildDiscoveryFallbackPath(result.Data!, isWindows: false));
    }

    /// <summary>
    /// A name match is never enough: a process carrying either name from another directory is rejected.
    /// </summary>
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_IdentitiesRejectANameMatchElsewhere()
    {
        var identities = ResolveLinkedChildIdentities();

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
    /// A child materialized as a regular file runs under its own file name in the workspace.
    /// </summary>
    [Fact]
    public void CasBootstrapper_WithAPlainChildFile_MonitorsTheChildFileInTheWorkspace()
    {
        File.WriteAllText(ChildWorkspacePath, "client");

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);

        AssertIdentities(result, ChildWorkspaceIdentities);
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
    /// A manifest that omits the wrapped client fails the launch even when the workspace has the file.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithoutTheChildFile_Fails()
    {
        File.WriteAllText(ChildWorkspacePath, "client");

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: null), WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
        Assert.Contains(ExpectedChildName, result.FirstError, StringComparison.OrdinalIgnoreCase);
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
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_WithADanglingChildLink_Fails()
    {
        File.CreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, "missing-object"));

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

        AssertIdentities(result, ChildWorkspaceIdentities);
    }

    /// <summary>
    /// A missing direct CAS entry point retains the manifest-hash fallback.
    /// </summary>
    [Fact]
    public void CasDirectExecutable_MissingFileFallsBackToItsHash()
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
    /// An existing direct CAS entry that is a plain file uses workspace file identities.
    /// </summary>
    [Fact]
    public void CasDirectExecutable_PlainFileMonitorsItsWorkspaceIdentities()
    {
        File.WriteAllText(BootstrapperPath, "client");
        var result = GameLauncher.DetermineMonitoringTarget(
            BuildGeneralsOnlineManifests(childHash: ChildHash),
            BootstrapperPath,
            _workspace,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        AssertIdentities(
            result,
            new GameProcessIdentity(Path.GetFileName(BootstrapperPath), _workspace),
            new GameProcessIdentity(Path.GetFileNameWithoutExtension(BootstrapperPath), _workspace));
    }

    /// <summary>
    /// A CAS-symlinked executable that is itself the game is discovered in its link target's directory.
    /// </summary>
    [SymlinkFact]
    public void CasSymlinkedDirectExecutable_MonitorsItsLinkTarget()
    {
        File.WriteAllText(Path.Combine(_store, BootstrapperHash), "client");
        File.CreateSymbolicLink(BootstrapperPath, Path.Combine(_store, BootstrapperHash));

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
            new GameProcessIdentity(GameClientConstants.GeneralsOnlineEacLauncherExecutable, _workspace),
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
    /// A same-stem sibling with a valid hash does not stand in for the wrapped client.
    /// </summary>
    [Fact]
    public void CasBootstrapper_IgnoresASameStemSiblingOfTheChild()
    {
        File.WriteAllText(ChildWorkspacePath, "client");
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash);
        var client = manifests.Single(m => m.ContentType == ContentType.GameClient);
        client.Files.Insert(1, new ManifestFile
        {
            RelativePath = Path.ChangeExtension(GameClientConstants.GeneralsOnline60HzExecutable, ".pdb"),
            Hash = BootstrapperHash,
            SourceType = ContentSourceType.ContentAddressable,
        });

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        AssertIdentities(result, ChildWorkspaceIdentities);
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

        AssertIdentities(result, ChildWorkspaceIdentities);
    }

    /// <summary>
    /// When several manifests list the child, the entry point's manifest wins, even when another
    /// manifest comes first. The other copy carries no hash, so picking it would fail the launch.
    /// </summary>
    [Fact]
    public void CasBootstrapper_PrefersTheEntryManifestsChild()
    {
        File.WriteAllText(ChildWorkspacePath, "client");
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash);
        manifests.Insert(0, BuildChildOnlyManifest(string.Empty));

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        AssertIdentities(result, ChildWorkspaceIdentities);
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
    [SymlinkFact]
    public void CasSymlinkedBootstrapper_WithASymlinkedNonCasChild_MonitorsTheLinkTarget()
    {
        var installation = Directory.CreateDirectory(Path.Combine(_root, "installation")).FullName;
        var installedChild = Path.Combine(installation, InstalledClientFileName);
        File.WriteAllText(installedChild, "client");
        File.CreateSymbolicLink(ChildWorkspacePath, installedChild);

        var result = Resolve(
            BuildGeneralsOnlineManifests(childHash: ChildHash, childSource: ContentSourceType.GameInstallation),
            WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        AssertIdentities(
            result,
            [
                .. ChildWorkspaceIdentities,
                new GameProcessIdentity(InstalledClientFileName, installation),
                new GameProcessIdentity(Path.GetFileNameWithoutExtension(InstalledClientFileName), installation),
            ]);
    }

    private static void AssertIdentities(
        OperationResult<IReadOnlyList<GameProcessIdentity>> result,
        params GameProcessIdentity[] expected)
    {
        Assert.True(result.Success, result.FirstError);
        Assert.Equal(expected, result.Data);
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

    private IReadOnlyList<GameProcessIdentity> ResolveLinkedChildIdentities()
    {
        File.WriteAllText(Path.Combine(_store, ChildHash), "client");
        File.CreateSymbolicLink(ChildWorkspacePath, Path.Combine(_store, ChildHash));

        var result = Resolve(BuildGeneralsOnlineManifests(childHash: ChildHash), WorkspaceStrategy.SymlinkOnly);
        Assert.True(result.Success, result.FirstError);
        return result.Data!;
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
