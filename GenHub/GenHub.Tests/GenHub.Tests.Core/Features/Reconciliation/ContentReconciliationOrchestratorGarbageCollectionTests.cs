using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services.Reconciliation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Reconciliation;

/// <summary>
/// Verifies that reconciliation reports disabled garbage collection without failing
/// otherwise-successful manifest operations.
/// </summary>
public class ContentReconciliationOrchestratorGarbageCollectionTests
{
    /// <summary>
    /// Verifies that replacement results expose the disabled-GC warning.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteContentReplacementAsync_WhenGcDisabled_ReturnsWarningAsync()
    {
        var reconciliationService = new Mock<IContentReconciliationService>();
        reconciliationService
            .Setup(service => service.OrchestrateBulkUpdateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(
                ReconciliationResult.Empty));

        var auditEntries = new List<ReconciliationAuditEntry>();
        var orchestrator = CreateOrchestrator(
            reconciliationService,
            CreateDisabledLifecycleManager(),
            auditEntries);
        var request = new ContentReplacementRequest
        {
            ManifestMapping = new Dictionary<string, string>
            {
                ["1.0.publisher.mod.old"] = "2.0.publisher.mod.new",
            },
            RemoveOldManifests = false,
        };

        var result = await orchestrator.ExecuteContentReplacementAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(CasDefaults.GarbageCollectionDisabledMessage, result.Data.Warnings);
        Assert.Equal(0, result.Data.CasObjectsCollected);
        Assert.Equal(0, result.Data.BytesFreed);
        var auditEntry = Assert.Single(auditEntries);
        Assert.NotNull(auditEntry.Metadata);
        Assert.Contains(CasDefaults.GarbageCollectionDisabledMessage, auditEntry.Metadata["warnings"]);
    }

    /// <summary>
    /// Verifies that removal results expose the disabled-GC warning.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteContentRemovalAsync_WhenGcDisabled_ReturnsWarningAsync()
    {
        var reconciliationService = new Mock<IContentReconciliationService>();
        var lifecycleManager = CreateDisabledLifecycleManager();
        lifecycleManager
            .Setup(manager => manager.UntrackManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<BulkUntrackResult>.CreateSuccess(
                new BulkUntrackResult(0, 0, [])));

        var auditEntries = new List<ReconciliationAuditEntry>();
        var orchestrator = CreateOrchestrator(reconciliationService, lifecycleManager, auditEntries);

        var result = await orchestrator.ExecuteContentRemovalAsync([]);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(CasDefaults.GarbageCollectionDisabledMessage, result.Data.Warnings);
        Assert.Equal(0, result.Data.CasObjectsCollected);
        Assert.Equal(0, result.Data.BytesFreed);
        var auditEntry = Assert.Single(auditEntries);
        Assert.NotNull(auditEntry.Metadata);
        Assert.Contains(CasDefaults.GarbageCollectionDisabledMessage, auditEntry.Metadata["warnings"]);
    }

    /// <summary>
    /// Verifies that when reconciliation fails for a manifest (e.g. active profile), untracking is not called for it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteContentRemovalAsync_WhenReconciliationFails_DoesNotUntrackBlockedManifestAsync()
    {
        var reconciliationService = new Mock<IContentReconciliationService>();
        reconciliationService
            .Setup(r => r.ReconcileManifestRemovalAsync(
                It.IsAny<ManifestId>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateFailure("Cannot remove manifest because active profile references it."));

        var lifecycleManager = new Mock<ICasLifecycleManager>();
        var orchestrator = CreateOrchestrator(reconciliationService, lifecycleManager);

        var result = await orchestrator.ExecuteContentRemovalAsync(["blocked-manifest-id"]);

        Assert.False(result.Success);
        lifecycleManager.Verify(
            manager => manager.UntrackManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        lifecycleManager.Verify(
            manager => manager.RunGarbageCollectionAsync(
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when bulk replacement fails or partially fails for profiles, untracking, removal, and GC are skipped.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteContentReplacementAsync_WhenReconciliationFails_DoesNotUntrackBlockedManifestAsync()
    {
        var reconciliationService = new Mock<IContentReconciliationService>();
        reconciliationService
            .Setup(r => r.OrchestrateBulkUpdateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(
                new ReconciliationResult(0, 0, 1))); // 1 profile failed to update

        var lifecycleManager = new Mock<ICasLifecycleManager>();
        var orchestrator = CreateOrchestrator(reconciliationService, lifecycleManager);

        var request = new ContentReplacementRequest
        {
            ManifestMapping = new Dictionary<string, string>
            {
                ["old-manifest-id"] = "new-manifest-id",
            },
            RemoveOldManifests = true,
            RunGarbageCollection = true,
        };

        var result = await orchestrator.ExecuteContentReplacementAsync(request);

        Assert.False(result.Success);
        lifecycleManager.Verify(
            manager => manager.UntrackManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        lifecycleManager.Verify(
            manager => manager.RunGarbageCollectionAsync(
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<ICasLifecycleManager> CreateDisabledLifecycleManager()
    {
        var lifecycleManager = new Mock<ICasLifecycleManager>();
        lifecycleManager
            .Setup(manager => manager.RunGarbageCollectionAsync(
                It.IsAny<bool>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GarbageCollectionStats>.CreateFailure(
                CasDefaults.GarbageCollectionDisabledMessage,
                GarbageCollectionStats.DisabledResult,
                TimeSpan.Zero));
        return lifecycleManager;
    }

    private static ContentReconciliationOrchestrator CreateOrchestrator(
        Mock<IContentReconciliationService> reconciliationService,
        Mock<ICasLifecycleManager> lifecycleManager,
        List<ReconciliationAuditEntry>? auditEntries = null)
    {
        var auditLog = new Mock<IReconciliationAuditLog>();
        auditLog
            .Setup(log => log.LogOperationAsync(
                It.IsAny<ReconciliationAuditEntry>(),
                It.IsAny<CancellationToken>()))
            .Callback<ReconciliationAuditEntry, CancellationToken>((entry, _) => auditEntries?.Add(entry))
            .Returns(Task.CompletedTask);

        return new ContentReconciliationOrchestrator(
            reconciliationService.Object,
            Mock.Of<IContentManifestPool>(),
            lifecycleManager.Object,
            auditLog.Object,
            NullLogger<ContentReconciliationOrchestrator>.Instance);
    }
}
