using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
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
    public async Task ExecuteContentReplacementAsync_WhenGcDisabled_ReturnsWarning()
    {
        var reconciliationService = new Mock<IContentReconciliationService>();
        reconciliationService
            .Setup(service => service.OrchestrateBulkUpdateAsync(
                It.IsAny<IReadOnlyDictionary<string, string>>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ReconciliationResult>.CreateSuccess(
                ReconciliationResult.Empty));

        var orchestrator = CreateOrchestrator(
            reconciliationService,
            CreateDisabledLifecycleManager());
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
    }

    /// <summary>
    /// Verifies that removal results expose the disabled-GC warning.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task ExecuteContentRemovalAsync_WhenGcDisabled_ReturnsWarning()
    {
        var reconciliationService = new Mock<IContentReconciliationService>();
        var lifecycleManager = CreateDisabledLifecycleManager();
        lifecycleManager
            .Setup(manager => manager.UntrackManifestsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<BulkUntrackResult>.CreateSuccess(
                new BulkUntrackResult(0, 0, [])));

        var orchestrator = CreateOrchestrator(reconciliationService, lifecycleManager);

        var result = await orchestrator.ExecuteContentRemovalAsync([]);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(CasDefaults.GarbageCollectionDisabledMessage, result.Data.Warnings);
        Assert.Equal(0, result.Data.CasObjectsCollected);
        Assert.Equal(0, result.Data.BytesFreed);
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
        Mock<ICasLifecycleManager> lifecycleManager)
    {
        var auditLog = new Mock<IReconciliationAuditLog>();
        auditLog
            .Setup(log => log.LogOperationAsync(
                It.IsAny<ReconciliationAuditEntry>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ContentReconciliationOrchestrator(
            reconciliationService.Object,
            Mock.Of<IContentManifestPool>(),
            lifecycleManager.Object,
            auditLog.Object,
            NullLogger<ContentReconciliationOrchestrator>.Instance);
    }
}
