using GenHub.Core.Interfaces.Common;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Tests for local upload history behavior while cloud deletion is disabled.
/// </summary>
public sealed class UploadHistoryServiceTests : IDisposable
{
    private readonly string _tempDirectory;

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

        await service.RemoveHistoryItemAsync("https://utfs.io/f/example");

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that removing one item preserves the other local records.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveHistoryItemAsync_WhenOtherItemsExist_PreservesOtherRecordsAsync()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/first", "first.zip");
        service.RecordUpload(2048, "https://utfs.io/f/second", "second.zip");

        await service.RemoveHistoryItemAsync("https://utfs.io/f/first");

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

        await service.RemoveHistoryItemAsync("https://utfs.io/f/missing");

        var reloadedService = CreateService();
        var item = Assert.Single(await reloadedService.GetUploadHistoryAsync());
        Assert.Equal("https://utfs.io/f/example", item.Url);
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

        await service.ClearHistoryAsync();

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
        File.WriteAllText(historyPath, historyJson);

        var service = CreateService();
        var history = await service.GetUploadHistoryAsync();

        var item = Assert.Single(history);
        Assert.Equal("https://utfs.io/f/active", item.Url);

        var migratedJson = File.ReadAllText(historyPath);
        Assert.DoesNotContain("https://utfs.io/f/pending", migratedJson);
        Assert.DoesNotContain("isPendingDeletion", migratedJson);

        var reloadedService = CreateService();
        Assert.Single(await reloadedService.GetUploadHistoryAsync());
    }

    private UploadHistoryService CreateService()
    {
        var appConfig = new Mock<IAppConfiguration>();
        appConfig.Setup(config => config.GetConfiguredDataPath()).Returns(_tempDirectory);

        return new UploadHistoryService(
            Mock.Of<ILogger<UploadHistoryService>>(),
            appConfig.Object);
    }
}
