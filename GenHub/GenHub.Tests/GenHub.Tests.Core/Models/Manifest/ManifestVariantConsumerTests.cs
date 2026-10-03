using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Launching;
using GenHub.Tests.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Tests that launch and download-state consumers read the files of manifests whose files
/// live in platform variants rather than the flat list.
/// </summary>
public class ManifestVariantConsumerTests
{
    private const string HostHash = "host_variant_hash";
    private const string ForeignHash = "foreign_variant_hash";
    private const string ForeignDownloadUrl = "https://example.invalid/releases/client-foreign.zip";

    /// <summary>
    /// A CAS-symlinked launch monitors the hash of the host variant's executable.
    /// </summary>
    [Fact]
    public void DetermineMonitoringTarget_WithVariantManifest_MonitorsHostVariantExecutableHash()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "generalszh-host", Hash = HostHash, IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }],
            [new() { RelativePath = "generalszh-foreign.exe", Hash = ForeignHash, IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }]);

        var result = GameLauncher.DetermineMonitoringTarget(
            [manifest],
            Path.Combine(workspace, "generalszh-host"),
            workspace,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(new GameProcessIdentity(HostHash, workspace), Assert.Single(result.Data!));
    }

    /// <summary>
    /// A launchable manifest whose executable is only in another platform's variant is skipped,
    /// so the next launchable manifest with a host executable is monitored.
    /// </summary>
    [Fact]
    public void DetermineMonitoringTarget_FirstManifestWithoutHostExecutable_MonitorsNextLaunchableManifest()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
        var foreignOnly = VariantManifestFixture.Create(
            [],
            [new() { RelativePath = "generalszh-foreign.exe", Hash = ForeignHash, IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }]);
        var hostTool = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.executable.tool"),
            ContentType = ContentType.Executable,
            Files = [new ManifestFile { RelativePath = "tool-host", Hash = HostHash, IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }],
        };

        var result = GameLauncher.DetermineMonitoringTarget(
            [foreignOnly, hostTool],
            Path.Combine(workspace, "tool-host"),
            workspace,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(new GameProcessIdentity(HostHash, workspace), Assert.Single(result.Data!));
    }

    /// <summary>
    /// When two launchable manifests declare the launched executable, monitoring uses the one the
    /// workspace materialized: the higher content-type priority, or the later manifest on a tie.
    /// </summary>
    /// <param name="firstType">The content type of the first manifest in load order.</param>
    /// <param name="secondType">The content type of the second manifest in load order.</param>
    /// <param name="expectedHash">The hash the monitor must track.</param>
    [Theory]
    [InlineData(ContentType.GameClient, ContentType.GameClient, "second_hash")]
    [InlineData(ContentType.GameClient, ContentType.ModdingTool, "first_hash")]
    [InlineData(ContentType.ModdingTool, ContentType.GameClient, "second_hash")]
    public void DetermineMonitoringTarget_SharedExecutable_MonitorsMaterializedManifest(
        ContentType firstType,
        ContentType secondType,
        string expectedHash)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));
        ContentManifest CreateLaunchable(ContentType type, string hash) => new()
        {
            ContentType = type,
            Files = [new() { RelativePath = "generals.exe", Hash = hash, IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }],
        };

        var result = GameLauncher.DetermineMonitoringTarget(
            [CreateLaunchable(firstType, "first_hash"), CreateLaunchable(secondType, "second_hash")],
            Path.Combine(workspace, "generals.exe"),
            workspace,
            WorkspaceStrategy.SymlinkOnly,
            expectedChildProcessName: null,
            NullLogger.Instance,
            localizationService: null);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(new GameProcessIdentity(expectedHash, workspace), Assert.Single(result.Data!));
    }

    /// <summary>A copied alias retains the custom entry's CAS validation, but unrelated files do not.</summary>
    /// <param name="entryName">The custom entry point.</param>
    /// <param name="matching">Whether the alias really contains the same binary.</param>
    [Theory]
    [InlineData("game.dat", true)]
    [InlineData("generals.ctr", true)]
    [InlineData("game.dat", false)]
    [InlineData("generals.ctr", false)]
    [InlineData("contra.dat", true)]
    public void DetermineMonitoringTarget_CustomAliasPreservesManifestAssociation(string entryName, bool matching)
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(workspace);
        try
        {
            File.WriteAllText(Path.Combine(workspace, entryName), "entry payload");
            File.WriteAllText(Path.Combine(workspace, "generals.exe"), matching ? "entry payload" : "unrelated");
            var manifest = new ContentManifest
            {
                ContentType = ContentType.GameClient,
                EntryPoint = entryName,
                Files = [new() { RelativePath = entryName, IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }],
            };
            var result = GameLauncher.DetermineMonitoringTarget(
                [manifest], Path.Combine(workspace, "generals.exe"), workspace, WorkspaceStrategy.SymlinkOnly, null, NullLogger.Instance, null);
            var copiedResult = GameLauncher.DetermineMonitoringTarget(
                [manifest], Path.Combine(workspace, "generals.exe"), workspace, WorkspaceStrategy.FullCopy, null, NullLogger.Instance, null);
            Assert.True(copiedResult.Success, copiedResult.FirstError);
            Assert.Equal("generals", Assert.Single(copiedResult.Data!).ProcessName);
            Assert.Equal(!matching, result.Success);
            if (matching)
            {
                Assert.Contains("hash", result.FirstError, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>
    /// A missing entry, or an entry path that leaves the workspace, never associates the alias
    /// with the manifest: monitoring falls back to the alias name instead of failing or throwing.
    /// </summary>
    /// <param name="entryOutsideWorkspace">Whether the entry path escapes the workspace; otherwise the entry file is missing.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetermineMonitoringTarget_CustomAliasWithUnreadableEntry_FallsBackToAliasName(bool entryOutsideWorkspace)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        try
        {
            var entryName = entryOutsideWorkspace ? "../outside.dat" : "game.dat";
            if (entryOutsideWorkspace)
            {
                File.WriteAllText(Path.Combine(root, "outside.dat"), "entry payload");
            }

            File.WriteAllText(Path.Combine(workspace, "generals.exe"), "entry payload");
            var manifest = new ContentManifest
            {
                ContentType = ContentType.GameClient,
                EntryPoint = entryName,
                Files = [new() { RelativePath = entryName, IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }],
            };

            var result = GameLauncher.DetermineMonitoringTarget(
                [manifest], Path.Combine(workspace, "generals.exe"), workspace, WorkspaceStrategy.SymlinkOnly, null, NullLogger.Instance, null);

            Assert.True(result.Success, result.FirstError);
            Assert.Equal("generals", Assert.Single(result.Data!).ProcessName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A copied alias retains CAS identity when the original entry is a link outside the workspace.</summary>
    [SymlinkFact]
    public void DetermineMonitoringTarget_CustomAliasWithCasSymlinkPreservesIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(workspace);
        try
        {
            var blob = Path.Combine(root, HostHash);
            File.WriteAllText(blob, "entry payload");
            File.CreateSymbolicLink(Path.Combine(workspace, "game.dat"), blob);
            var alias = Path.Combine(workspace, "generals.exe");
            File.Copy(blob, alias);
            var file = new ManifestFile { RelativePath = "game.dat", IsExecutable = true, SourceType = ContentSourceType.ContentAddressable, Hash = HostHash };
            var manifest = new ContentManifest { ContentType = ContentType.GameClient, EntryPoint = "game.dat", Files = [file] };
            var result = GameLauncher.DetermineMonitoringTarget(
                [manifest], alias, workspace, WorkspaceStrategy.SymlinkOnly, null, NullLogger.Instance, null);
            Assert.True(result.Success, result.FirstError);
            Assert.Equal(2, result.Data!.Count);
            Assert.Contains(new GameProcessIdentity("generals.exe", workspace), result.Data);
            Assert.Contains(new GameProcessIdentity("generals", workspace), result.Data);

            file.Hash = null!;
            result = GameLauncher.DetermineMonitoringTarget(
                [manifest], alias, workspace, WorkspaceStrategy.SymlinkOnly, null, NullLogger.Instance, null);
            Assert.False(result.Success);
            Assert.Contains("hash", result.FirstError, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(alias, "unrelated");
            result = GameLauncher.DetermineMonitoringTarget(
                [manifest], alias, workspace, WorkspaceStrategy.SymlinkOnly, null, NullLogger.Instance, null);
            Assert.True(result.Success, result.FirstError);
            Assert.Equal("generals", Assert.Single(result.Data!).ProcessName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A later tool cannot replace the actual executable's monitoring identity.</summary>
    [Fact]
    public void DetermineMonitoringTarget_DoesNotSelectUnrelatedTool()
    {
        var workspace = Path.GetTempPath();
        var manifest = new ContentManifest
        {
            ContentType = ContentType.GameClient,
            Files = [new() { RelativePath = "generals.exe", Hash = HostHash, SourceType = ContentSourceType.ContentAddressable }],
        };
        var tool = new ContentManifest
        {
            ContentType = ContentType.ModdingTool,
            Files = [new() { RelativePath = "tool.exe", IsExecutable = true, SourceType = ContentSourceType.ContentAddressable }],
        };
        var result = GameLauncher.DetermineMonitoringTarget(
            [manifest, tool],
            Path.Combine(workspace, "generals.exe"),
            workspace,
            WorkspaceStrategy.SymlinkOnly,
            null,
            NullLogger.Instance,
            null);
        Assert.True(result.Success, result.FirstError);
        Assert.Equal(new GameProcessIdentity(HostHash, workspace), Assert.Single(result.Data!));
    }

    /// <summary>
    /// A stored variant manifest counts as downloaded content when only another platform's
    /// variant carries content-addressable files, because download identity covers every variant.
    /// </summary>
    [Fact]
    public void IsDownloadedManifest_WithCasFilesOnlyInForeignVariant_ReturnsTrue()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "generalszh-host", SourceType = ContentSourceType.RemoteDownload }],
            [new() { RelativePath = "generalszh-foreign.exe", Hash = ForeignHash, SourceType = ContentSourceType.ContentAddressable }]);

        Assert.True(ManifestHelper.IsDownloadedManifest(manifest));
        Assert.Contains(ForeignHash, ManifestHelper.GetContentAddressableHashes(manifest));
    }

    /// <summary>
    /// A variant manifest with no content-addressable file in any variant is not downloaded content.
    /// </summary>
    [Fact]
    public void IsDownloadedManifest_WithNoCasFilesInAnyVariant_ReturnsFalse()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "generalszh-host", SourceType = ContentSourceType.RemoteDownload }],
            [new() { RelativePath = "generalszh-foreign.exe", SourceType = ContentSourceType.RemoteDownload }]);

        Assert.False(ManifestHelper.IsDownloadedManifest(manifest));
    }

    /// <summary>
    /// A catalog row matches an installed variant manifest by the download URL of another
    /// platform's variant, because download identity covers every variant.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GetStateAsync_WithVariantManifest_MatchesByForeignVariantDownloadUrlAsync()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "client-host.zip", DownloadUrl = "https://example.invalid/releases/client-host.zip" }],
            [new() { RelativePath = "client-foreign.zip", DownloadUrl = ForeignDownloadUrl }]);
        manifest.Id = ManifestId.Create("1.20260801.publisher.gameclient.variants");

        var row = new ContentSearchResult
        {
            Id = $"file:{ForeignDownloadUrl}",
            Name = "client-foreign.zip",
            ProviderName = "Publisher",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            SelectedDownloadUrl = ForeignDownloadUrl,
        };

        var pool = new Mock<IContentManifestPool>();
        pool.Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([manifest]));
        pool.Setup(p => p.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var service = new ContentStateService(pool.Object, NullLogger<ContentStateService>.Instance);

        Assert.Equal(ContentState.Downloaded, await service.GetStateAsync(row));
        Assert.Equal(manifest.Id.Value, await service.GetLocalManifestIdAsync(row));
    }

    /// <summary>File identity can match a token in a later variant.</summary>
    [Fact]
    public void GitHubVariantMatch_MatchesLaterVariant()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "client-720p.zip" }],
            [new() { RelativePath = "client-1080p.zip" }]);
        manifest.Name = "client";
        var method = typeof(ContentStateService).GetMethod("IsGitHubVariantMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null, [manifest, new ContentSearchResult { Name = "client-1080p" }])!);
        Assert.False((bool)method.Invoke(null, [manifest, new ContentSearchResult { Name = "client-4k" }])!);
    }

    /// <summary>A flat package keeps its first language identity despite incidental later payloads.</summary>
    [Fact]
    public void GitHubVariantMatch_FlatManifest_UsesFirstToken()
    {
        var manifest = new ContentManifest
        {
            Name = "client",
            Files = [new() { RelativePath = "client-english.zip" }, new() { RelativePath = "Data/Russian/text.big" }],
        };
        var method = typeof(ContentStateService).GetMethod("IsGitHubVariantMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null, [manifest, new ContentSearchResult { Name = "client-english" }])!);
        Assert.False((bool)method.Invoke(null, [manifest, new ContentSearchResult { Name = "client-russian" }])!);
    }

    /// <summary>Incidental payload paths do not change a variant's first-token identity.</summary>
    [Fact]
    public void GitHubVariantMatch_VariantUsesFirstToken()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "client-russian.zip" }, new() { RelativePath = "english-readme.txt" }], []);
        manifest.Name = "client";
        var method = typeof(ContentStateService).GetMethod("IsGitHubVariantMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.False((bool)method.Invoke(null, [manifest, new ContentSearchResult { Name = "client-english" }])!);
        Assert.True((bool)method.Invoke(null, [manifest, new ContentSearchResult { Name = "client-russian" }])!);
    }

    /// <summary>The host variant's hotkey payload satisfies addon detection.</summary>
    [Fact]
    public void HotkeyAddonMatch_MatchesHostPayload()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "hotkeys.big" }],
            [new() { RelativePath = "unrelated.big" }]);
        manifest.ContentType = ContentType.Addon;
        manifest.TargetGame = GameType.ZeroHour;
        var type = typeof(GenHub.Features.Tools.GenHotkeys.ViewModels.GenHotkeysViewModel);
        var method = type.GetMethod("IsAddonMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null, [manifest, null, GameType.ZeroHour, "expected-addon", "hotkeys.big", "legacy.big"])!);
    }

    /// <summary>A foreign-only hotkey payload cannot satisfy the current host's addon.</summary>
    [Fact]
    public void HotkeyAddonMatch_IgnoresForeignPayload()
    {
        var manifest = VariantManifestFixture.Create(
            [new() { RelativePath = "unrelated.big" }],
            [new() { RelativePath = "hotkeys.big" }]);
        manifest.ContentType = ContentType.Addon;
        manifest.TargetGame = GameType.ZeroHour;
        var type = typeof(GenHub.Features.Tools.GenHotkeys.ViewModels.GenHotkeysViewModel);
        var method = type.GetMethod("IsAddonMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.False((bool)method.Invoke(null, [manifest, null, GameType.ZeroHour, "expected-addon", "hotkeys.big", "legacy.big"])!);
    }
}
