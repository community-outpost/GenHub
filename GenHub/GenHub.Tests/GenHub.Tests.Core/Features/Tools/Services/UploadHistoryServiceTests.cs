using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Services;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Tests for local upload history tracking and cloud deletion orchestration.
/// </summary>
public sealed class UploadHistoryServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IUploadThingService> _uploadThingServiceMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadHistoryServiceTests"/> class.
    /// </summary>
    public UploadHistoryServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Removes temporary test data.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that removing an item deletes its local record immediately.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveHistoryItemAsync_WhenItemExists_RemovesLocalRecordAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/example", "example.zip");

        await service.RemoveHistoryItemAsync("https://utfs.io/f/example", deleteFromCloud: false);

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that removing an item with cloud deletion invokes IUploadThingService.DeleteFileAsync.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveHistoryItemAsync_WhenTokenExists_InvokesCloudDeletionAsync()
    {
        _uploadThingServiceMock
            .Setup(u => u.DeleteFileAsync("key_123", "token_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/key_123", "example.zip", "key_123", "token_abc");

        var success = await service.RemoveHistoryItemAsync("https://utfs.io/f/key_123", deleteFromCloud: true);

        Assert.True(success);
        _uploadThingServiceMock.Verify(
            u => u.DeleteFileAsync("key_123", "token_abc", It.IsAny<CancellationToken>()),
            Times.Once);

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that when cloud deletion fails, local history preserves the record so deletion can be retried.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveHistoryItemAsync_WhenCloudDeletionFails_PreservesRecordForRetryAsync()
    {
        _uploadThingServiceMock
            .Setup(u => u.DeleteFileAsync("key_123", "token_abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateFailure("Delete failed"));

        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/key_123", "example.zip", "key_123", "token_abc");

        var success = await service.RemoveHistoryItemAsync("https://utfs.io/f/key_123", deleteFromCloud: true);

        Assert.False(success);
        var reloadedService = CreateService();
        var item = Assert.Single(await reloadedService.GetUploadHistoryAsync());
        Assert.Equal("https://utfs.io/f/key_123", item.Url);
    }

    /// <summary>
    /// Verifies that removing one item preserves other local records.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveHistoryItemAsync_WhenOtherItemsExist_PreservesOtherRecordsAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/first", "first.zip");
        service.RecordUpload(2048, "https://utfs.io/f/second", "second.zip");

        await service.RemoveHistoryItemAsync("https://utfs.io/f/first", deleteFromCloud: false);

        var reloadedService = CreateService();
        var item = Assert.Single(await reloadedService.GetUploadHistoryAsync());
        Assert.Equal("https://utfs.io/f/second", item.Url);
    }

    /// <summary>
    /// Verifies that removing a non-matching URL leaves local history unchanged.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveHistoryItemAsync_WhenUrlDoesNotMatch_PreservesHistoryAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/example", "example.zip");

        await service.RemoveHistoryItemAsync("https://utfs.io/f/missing", deleteFromCloud: false);

        var reloadedService = CreateService();
        var item = Assert.Single(await reloadedService.GetUploadHistoryAsync());
        Assert.Equal("https://utfs.io/f/example", item.Url);
    }

    /// <summary>
    /// Verifies that removing an item by default invokes IUploadThingService.DeleteFileAsync when token exists.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveHistoryItemAsync_Default_InvokesCloudDeletionAsync()
    {
        _uploadThingServiceMock
            .Setup(u => u.DeleteFileAsync("key_default", "token_default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/key_default", "default.zip", "key_default", "token_default");

        var success = await service.RemoveHistoryItemAsync("https://utfs.io/f/key_default");

        Assert.True(success);
        _uploadThingServiceMock.Verify(
            u => u.DeleteFileAsync("key_default", "token_default", It.IsAny<CancellationToken>()),
            Times.Once);

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that clearing history by default invokes IUploadThingService.DeleteFileAsync for all items with tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ClearHistoryAsync_WhenItemsHaveTokens_InvokesCloudDeletionForAllAsync()
    {
        _uploadThingServiceMock
            .Setup(u => u.DeleteFileAsync("key_1", "token_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        _uploadThingServiceMock
            .Setup(u => u.DeleteFileAsync("key_2", "token_2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/key_1", "first.zip", "key_1", "token_1");
        service.RecordUpload(2048, "https://utfs.io/f/key_2", "second.zip", "key_2", "token_2");

        var result = await service.ClearHistoryAsync();

        Assert.Equal(2, result.Deleted);
        Assert.Equal(0, result.Failed);
        _uploadThingServiceMock.Verify(
            u => u.DeleteFileAsync("key_1", "token_1", It.IsAny<CancellationToken>()),
            Times.Once);
        _uploadThingServiceMock.Verify(
            u => u.DeleteFileAsync("key_2", "token_2", It.IsAny<CancellationToken>()),
            Times.Once);

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that clearing history deletes every local record immediately.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ClearHistoryAsync_WhenItemsExist_RemovesAllLocalRecordsAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/first", "first.zip");
        service.RecordUpload(2048, "https://utfs.io/f/second", "second.zip");

        await service.ClearHistoryAsync(deleteFromCloud: false);

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that clearing empty history completes without creating records.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ClearHistoryAsync_WhenHistoryIsEmpty_RemainsEmptyAsync()
    {
        var service = CreateService();

        await service.ClearHistoryAsync();

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that legacy pending-deletion records are removed during migration.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetUploadHistoryAsync_WhenLegacyRecordIsPendingDeletion_RemovesRecordAsync()
    {
        var historyPath = Path.Combine(_tempDirectory, "upload_history.json");
        var timestamp = DateTime.UtcNow.ToString("O");
        var historyJson = $$"""
            [
              {
                "timestamp": "{{timestamp}}",
                "sizeBytes": 1024,
                "url": "https://utfs.io/f/pending",
                "fileName": "pending.zip",
                "isPendingDeletion": true
              },
              {
                "timestamp": "{{timestamp}}",
                "sizeBytes": 2048,
                "url": "https://utfs.io/f/active",
                "fileName": "active.zip"
              }
            ]
            """;
        await File.WriteAllTextAsync(historyPath, historyJson);

        var service = CreateService();
        var history = await service.GetUploadHistoryAsync();

        var item = Assert.Single(history);
        Assert.Equal("https://utfs.io/f/active", item.Url);

        var migratedJson = await File.ReadAllTextAsync(historyPath);
        Assert.DoesNotContain("https://utfs.io/f/pending", migratedJson);
        Assert.DoesNotContain("isPendingDeletion", migratedJson);

        var reloadedService = CreateService();
        Assert.Single(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that FindExistingUploadAsync returns the matching record when the hash matches.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FindExistingUploadAsync_WhenHashMatches_ReturnsExistingRecordAsync()
    {
        var service = CreateService();
        var fileHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        service.RecordUpload(1024, "https://utfs.io/f/existing", "map.zip", "key_1", "token_1", fileHash);

        var record = await service.FindExistingUploadAsync(fileHash);

        Assert.NotNull(record);
        Assert.Equal("https://utfs.io/f/existing", record.Url);
        Assert.Equal(fileHash, record.FileHash);
        Assert.Equal("key_1", record.FileKey);
        Assert.Equal("token_1", record.DeleteToken);
    }

    /// <summary>
    /// Verifies that FindExistingUploadAsync returns null when no matching hash exists.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task FindExistingUploadAsync_WhenHashNotFound_ReturnsNullAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/existing", "map.zip", "key_1", "token_1", "hash_abc");

        var record = await service.FindExistingUploadAsync("hash_nonexistent");

        Assert.Null(record);
    }

    /// <summary>
    /// Verifies that GetUploadHistoryAsync with category filter returns only matching items.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetUploadHistoryAsync_WithCategoryFilter_ReturnsOnlyMatchingItemsAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/replay1", "game.rep", "key_rep", "token_rep", null, ReplayManagerConstants.UploadCategory);
        service.RecordUpload(2048, "https://utfs.io/f/map1", "custom_map.zip", "key_map", "token_map", null, MapManagerConstants.UploadCategory);

        var replayHistory = (await service.GetUploadHistoryAsync(ReplayManagerConstants.UploadCategory)).ToList();
        var mapHistory = (await service.GetUploadHistoryAsync(MapManagerConstants.UploadCategory)).ToList();
        var allHistory = (await service.GetUploadHistoryAsync()).ToList();

        Assert.Single(replayHistory);
        Assert.Equal("game.rep", replayHistory[0].FileName);
        Assert.Single(mapHistory);
        Assert.Equal("custom_map.zip", mapHistory[0].FileName);
        Assert.Equal(2, allHistory.Count);
    }

    /// <summary>
    /// Verifies that ClearHistoryAsync with category filter clears only items of that category.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ClearHistoryAsync_WithCategoryFilter_ClearsOnlySpecifiedCategoryAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/replay1", "game.rep", "key_rep", "token_rep", null, ReplayManagerConstants.UploadCategory);
        service.RecordUpload(2048, "https://utfs.io/f/map1", "custom_map.zip", "key_map", "token_map", null, MapManagerConstants.UploadCategory);

        await service.ClearHistoryAsync(deleteFromCloud: false, category: ReplayManagerConstants.UploadCategory);

        var replayHistory = (await service.GetUploadHistoryAsync(ReplayManagerConstants.UploadCategory)).ToList();
        var mapHistory = (await service.GetUploadHistoryAsync(MapManagerConstants.UploadCategory)).ToList();

        Assert.Empty(replayHistory);
        Assert.Single(mapHistory);
        Assert.Equal("custom_map.zip", mapHistory[0].FileName);
    }

    /// <summary>
    /// Verifies that CanUploadAsync respects category-specific quota limits.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CanUploadAsync_WithCategory_AppliesCategoryQuotaAsync()
    {
        var service = CreateService();

        // 9MB replay upload (within 10MB replay limit)
        Assert.True(await service.CanUploadAsync(9 * 1024 * 1024, ReplayManagerConstants.UploadCategory));

        // 11MB replay upload (exceeds 10MB replay limit)
        Assert.False(await service.CanUploadAsync(11 * 1024 * 1024, ReplayManagerConstants.UploadCategory));

        // 9MB map upload (within 10MB map limit)
        Assert.True(await service.CanUploadAsync(9 * 1024 * 1024, MapManagerConstants.UploadCategory));

        // 11MB map upload (exceeds 10MB map limit)
        Assert.False(await service.CanUploadAsync(11 * 1024 * 1024, MapManagerConstants.UploadCategory));
    }

    /// <summary>
    /// Verifies that GetUsageInfoAsync computes usage and limits partitioned by category.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetUsageInfoAsync_WithCategory_ReturnsCategorySpecificUsageAsync()
    {
        var service = CreateService();
        service.RecordUpload(5 * 1024 * 1024, "https://utfs.io/f/replay1", "game.rep", "key_rep", "token_rep", null, ReplayManagerConstants.UploadCategory);
        service.RecordUpload(2 * 1024 * 1024, "https://utfs.io/f/map1", "map.zip", "key_map", "token_map", null, MapManagerConstants.UploadCategory);

        var replayUsage = await service.GetUsageInfoAsync(ReplayManagerConstants.UploadCategory);
        var mapUsage = await service.GetUsageInfoAsync(MapManagerConstants.UploadCategory);

        Assert.Equal(5 * 1024 * 1024, replayUsage.UsedBytes);
        Assert.Equal(ReplayManagerConstants.MaxUploadBytesPerPeriod, replayUsage.LimitBytes);

        Assert.Equal(2 * 1024 * 1024, mapUsage.UsedBytes);
        Assert.Equal(MapManagerConstants.MaxUploadBytesPerPeriod, mapUsage.LimitBytes);
    }

    /// <summary>
    /// Verifies that UploadHistoryChanged fires on record, remove, and clear operations.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UploadHistoryChanged_FiresOnMutationsAsync()
    {
        var service = CreateService();
        int eventCount = 0;
        service.UploadHistoryChanged += (_, _) => eventCount++;

        service.RecordUpload(1024, "https://utfs.io/f/test1", "test.rep");
        Assert.Equal(1, eventCount);

        await service.RemoveHistoryItemAsync("https://utfs.io/f/test1", deleteFromCloud: false);
        Assert.Equal(2, eventCount);

        service.RecordUpload(2048, "https://utfs.io/f/test2", "test2.rep");
        Assert.Equal(3, eventCount);

        await service.ClearHistoryAsync(deleteFromCloud: false);
        Assert.Equal(4, eventCount);
    }

    private UploadHistoryService CreateService()
    {
        var appConfig = new Mock<IAppConfiguration>();
        appConfig.Setup(config => config.GetConfiguredDataPath()).Returns(_tempDirectory);

        return new UploadHistoryService(
            _uploadThingServiceMock.Object,
            Mock.Of<ILogger<UploadHistoryService>>(),
            appConfig.Object);
    }
}
