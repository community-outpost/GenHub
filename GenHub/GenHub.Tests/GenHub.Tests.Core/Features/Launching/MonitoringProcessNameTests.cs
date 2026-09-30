using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Launching;
using Microsoft.Extensions.Logging.Abstractions;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Tests for how <see cref="GameLauncher"/> picks the process to discover after a Steam launch.
/// </summary>
public class MonitoringProcessNameTests
{
    private const string BootstrapperHash = "b00757a900000000000000000000000000000000000000000000000000000000";
    private const string ChildHash = "c41d000000000000000000000000000000000000000000000000000000000000";

    private static readonly string BootstrapperPath =
        Path.Combine("workspace", GameClientConstants.GeneralsOnlineEacLauncherExecutable);

    private static string ExpectedChildName =>
        LaunchEntryPointResolver.ResolveExpectedChildProcessName(BootstrapperPath)!;

    /// <summary>
    /// A CAS-symlinked bootstrapper launch discovers the wrapped client by its own hash.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_MonitorsTheWrappedClientHash()
    {
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash);

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ChildHash, result.Data);
    }

    /// <summary>
    /// A wrapped client without a hash fails the launch rather than monitoring the bootstrapper.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithoutTheChildHash_Fails()
    {
        var manifests = BuildGeneralsOnlineManifests(childHash: string.Empty);

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
        Assert.Contains(ExpectedChildName, result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A manifest that omits the wrapped client fails the launch rather than monitoring the bootstrapper.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithoutTheChildFile_Fails()
    {
        var manifests = BuildGeneralsOnlineManifests(childHash: null);

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.False(result.Success);
    }

    /// <summary>
    /// A wrapped client linked from outside CAS keeps its own file name as the process name.
    /// </summary>
    [Fact]
    public void CasSymlinkedBootstrapper_WithANonCasChild_MonitorsTheChildName()
    {
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash, childSource: ContentSourceType.GameInstallation);

        var result = Resolve(manifests, WorkspaceStrategy.SymlinkOnly);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ExpectedChildName, result.Data);
    }

    /// <summary>
    /// A CAS-symlinked executable that is itself the game keeps monitoring its own hash.
    /// </summary>
    [Fact]
    public void CasSymlinkedDirectExecutable_MonitorsItsOwnHash()
    {
        var manifests = BuildGeneralsOnlineManifests(childHash: ChildHash);

        var result = GameLauncher.DetermineMonitoringProcessName(
            manifests,
            BootstrapperPath,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(BootstrapperHash, result.Data);
    }

    /// <summary>
    /// Outside CAS symlinking the expected child is monitored by name.
    /// </summary>
    [Fact]
    public void NonSymlinkStrategy_MonitorsTheChildName()
    {
        var manifests = BuildGeneralsOnlineManifests(childHash: string.Empty);

        var result = Resolve(manifests, WorkspaceStrategy.HardLink);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(ExpectedChildName, result.Data);
    }

    private static OperationResult<string> Resolve(
        IReadOnlyList<ContentManifest> manifests,
        WorkspaceStrategy strategy) =>
        GameLauncher.DetermineMonitoringProcessName(
            manifests,
            BootstrapperPath,
            strategy,
            ExpectedChildName,
            NullLogger.Instance,
            localizationService: null);

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
}
