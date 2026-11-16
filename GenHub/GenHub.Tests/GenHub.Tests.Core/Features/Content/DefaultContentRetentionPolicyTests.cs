using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="DefaultContentRetentionPolicy"/>.
/// </summary>
public sealed class DefaultContentRetentionPolicyTests
{
    private const string Id1 = "1.1.generalsonline.gameclient.v1";
    private const string Id2 = "1.2.generalsonline.gameclient.v2";
    private const string Id3 = "1.3.generalsonline.gameclient.v3";

    /// <summary>
    /// Verifies that empty candidate lists return empty deletable results.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FilterDeletableManifestsAsync_EmptyCandidates_ReturnsEmptyAsync()
    {
        var policy = new DefaultContentRetentionPolicy(NullLogger<DefaultContentRetentionPolicy>.Instance);
        var result = await policy.FilterDeletableManifestsAsync([]);
        Assert.Empty(result);
    }

    /// <summary>
    /// Verifies that candidates within the recent retention quota are retained.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FilterDeletableManifestsAsync_WithinRecentQuota_RetainsAllAsync()
    {
        var policy = new DefaultContentRetentionPolicy(
            NullLogger<DefaultContentRetentionPolicy>.Instance,
            recentVersionsToRetain: 2);

        var manifests = new[]
        {
            CreateManifest(Id1, version: "1.0", releaseDate: DateTime.UtcNow.AddDays(-2)),
            CreateManifest(Id2, version: "2.0", releaseDate: DateTime.UtcNow.AddDays(-1)),
        };

        var deletable = await policy.FilterDeletableManifestsAsync(manifests);
        Assert.Empty(deletable);
    }

    /// <summary>
    /// Verifies that manifests exceeding the retention quota have older versions marked for deletion.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FilterDeletableManifestsAsync_ExceedsQuota_DeletesOldestUnpinnedAsync()
    {
        var policy = new DefaultContentRetentionPolicy(
            NullLogger<DefaultContentRetentionPolicy>.Instance,
            recentVersionsToRetain: 2);

        var m1 = CreateManifest(Id1, version: "1.0", releaseDate: new DateTime(2026, 1, 1));
        var m2 = CreateManifest(Id2, version: "2.0", releaseDate: new DateTime(2026, 2, 1));
        var m3 = CreateManifest(Id3, version: "3.0", releaseDate: new DateTime(2026, 3, 1));

        var deletable = await policy.FilterDeletableManifestsAsync([m1, m2, m3]);

        // Retains m3 and m2 (most recent 2); deletes m1 (oldest)
        Assert.Single(deletable);
        Assert.Equal(Id1, deletable[0].Id.Value);
    }

    /// <summary>
    /// Verifies that pinned manifests are never marked for deletion.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FilterDeletableManifestsAsync_PinnedManifests_AreNeverDeletedAsync()
    {
        var pinnedMock = new Mock<IPinnedManifestProvider>();
        pinnedMock.Setup(p => p.GetPinnedManifestIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Id1 });

        var policy = new DefaultContentRetentionPolicy(
            NullLogger<DefaultContentRetentionPolicy>.Instance,
            pinnedProviders: [pinnedMock.Object],
            recentVersionsToRetain: 1);

        var m1 = CreateManifest(Id1, version: "1.0", releaseDate: new DateTime(2026, 1, 1)); // Pinned!
        var m2 = CreateManifest(Id2, version: "2.0", releaseDate: new DateTime(2026, 2, 1));
        var m3 = CreateManifest(Id3, version: "3.0", releaseDate: new DateTime(2026, 3, 1));

        var deletable = await policy.FilterDeletableManifestsAsync([m1, m2, m3]);

        // m1 is pinned so excluded from deletion; m3 is retained as top 1 recent; m2 is deletable
        Assert.Single(deletable);
        Assert.Equal(Id2, deletable[0].Id.Value);
    }

    /// <summary>
    /// Verifies that provider exceptions do not prevent filtering from completing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FilterDeletableManifestsAsync_ProviderThrows_DoesNotCrashAndContinuesAsync()
    {
        var throwingMock = new Mock<IPinnedManifestProvider>();
        throwingMock.Setup(p => p.GetPinnedManifestIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Replay database offline"));

        var policy = new DefaultContentRetentionPolicy(
            NullLogger<DefaultContentRetentionPolicy>.Instance,
            pinnedProviders: [throwingMock.Object],
            recentVersionsToRetain: 1);

        var m1 = CreateManifest(Id1, version: "1.0", releaseDate: new DateTime(2026, 1, 1));
        var m2 = CreateManifest(Id2, version: "2.0", releaseDate: new DateTime(2026, 2, 1));

        var deletable = await policy.FilterDeletableManifestsAsync([m1, m2]);

        Assert.Single(deletable);
        Assert.Equal(Id1, deletable[0].Id.Value);
    }

    /// <summary>
    /// Verifies that operation canceled exceptions are propagated rather than swallowed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FilterDeletableManifestsAsync_ProviderCanceled_PropagatesCancellationAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelingMock = new Mock<IPinnedManifestProvider>();
        cancelingMock.Setup(p => p.GetPinnedManifestIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var policy = new DefaultContentRetentionPolicy(
            NullLogger<DefaultContentRetentionPolicy>.Instance,
            pinnedProviders: [cancelingMock.Object],
            recentVersionsToRetain: 1);

        var m1 = CreateManifest(Id1, version: "1.0");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => policy.FilterDeletableManifestsAsync([m1], cts.Token));
    }

    /// <summary>
    /// Verifies that manifests with identical release dates use version comparison for tie-breaking.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FilterDeletableManifestsAsync_SameReleaseDate_UsesVersionComparisonAsync()
    {
        var policy = new DefaultContentRetentionPolicy(
            NullLogger<DefaultContentRetentionPolicy>.Instance,
            recentVersionsToRetain: 1);

        var sameDate = new DateTime(2026, 3, 1);
        var m1 = CreateManifest(Id1, version: "1.2", releaseDate: sameDate);
        var m2 = CreateManifest(Id2, version: "1.10", releaseDate: sameDate);

        var deletable = await policy.FilterDeletableManifestsAsync([m1, m2]);

        // "1.10" is newer than "1.2", so m2 is retained and m1 is marked deletable
        Assert.Single(deletable);
        Assert.Equal(Id1, deletable[0].Id.Value);
    }

    private static ContentManifest CreateManifest(
        string id,
        string publisherType = "generalsonline",
        string version = "1.0",
        DateTime? releaseDate = null)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = id,
            Version = version,
            Publisher = new PublisherInfo { Name = publisherType, PublisherType = publisherType },
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Metadata = new ContentMetadata
            {
                ReleaseDate = releaseDate ?? DateTime.UtcNow,
            },
        };
    }
}
