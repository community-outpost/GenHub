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
    public async Task RemoveHistoryItemAsync_WhenItemExists_RemovesLocalRecord()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/example", "example.zip");

        await service.RemoveHistoryItemAsync("https://utfs.io/f/example");

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
    }

    /// <summary>
    /// Verifies that clearing history deletes every local record immediately.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ClearHistoryAsync_WhenItemsExist_RemovesAllLocalRecords()
    {
        var service = CreateService();
        service.RecordUpload(1024, "https://utfs.io/f/first", "first.zip");
        service.RecordUpload(2048, "https://utfs.io/f/second", "second.zip");

        await service.ClearHistoryAsync();

        var reloadedService = CreateService();
        Assert.Empty(await reloadedService.GetUploadHistoryAsync());
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
