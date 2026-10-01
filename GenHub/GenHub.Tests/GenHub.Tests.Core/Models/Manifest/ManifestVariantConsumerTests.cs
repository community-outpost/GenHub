using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using GenHub.Features.Launching;
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
}
