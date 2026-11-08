using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Workspace;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Verifies that live content is separated from the snapshot manifests that the profile's
/// workspace strategy materializes.
/// </summary>
public class LiveWorkspaceSeparationTests
{
    /// <summary>
    /// Ensures a mixed profile is split into snapshot and live sets without moving any
    /// manifest into the wrong set.
    /// </summary>
    [Fact]
    public void SplitManifests_SeparatesLiveFromSnapshot()
    {
        var snapshot = CreateManifest("1.0.local.mod.snapshot-test", ContentSourceMode.Snapshot);
        var live = CreateManifest("1.0.local.mod.live-test", ContentSourceMode.Live);

        var (snapshots, liveManifests) = WorkspaceManager.SplitManifests([snapshot, live]);

        Assert.Single(snapshots);
        Assert.Same(snapshot, snapshots[0]);
        Assert.Single(liveManifests);
        Assert.Same(live, liveManifests[0]);
    }

    private static ContentManifest CreateManifest(string id, ContentSourceMode mode) => new()
    {
        Id = ManifestId.Create(id),
        Name = id,
        Version = "1.0",
        ContentType = ContentType.Mod,
        TargetGame = GameType.ZeroHour,
        SourceMode = mode,
    };
}
