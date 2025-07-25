using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Windows.Features.Workspace;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Windows.Features.Workspace;

/// <summary>
/// Tests for the WindowsFileOperationsService class (Windows-specific).
/// </summary>
public class WindowsFileOperationsServiceTests : IDisposable
{
    private readonly Mock<IFileOperationsService> _baseService;
    private readonly Mock<ILogger<WindowsFileOperationsService>> _logger;
    private readonly WindowsFileOperationsService _service;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFileOperationsServiceTests"/> class.
    /// </summary>
    public WindowsFileOperationsServiceTests()
    {
        _baseService = new Mock<IFileOperationsService>();
        _logger = new Mock<ILogger<WindowsFileOperationsService>>();
        _service = new WindowsFileOperationsService(_baseService.Object, _logger.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Tests that CreateHardLinkAsync creates a hard link on Windows.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHardLinkAsync_CreatesHardLink_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Skip on non-Windows
        }

        var src = Path.Combine(_tempDir, "source.txt");
        var link = Path.Combine(_tempDir, "hardlink.txt");
        await File.WriteAllTextAsync(src, "test content");

        try
        {
            await _service.CreateHardLinkAsync(link, src);
        }
        catch (UnauthorizedAccessException)
        {
            // Skip test if privilege is not held
            return;
        }
        catch (PlatformNotSupportedException)
        {
            // Skip test if not supported
            return;
        }

        Assert.True(File.Exists(link));
        Assert.Equal("test content", await File.ReadAllTextAsync(link));
    }

    /// <summary>
    /// Tests that CreateSymlinkAsync delegates to the base service on Windows.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateSymlinkAsync_CreatesSymlink_OnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Skip on non-Windows
        }

        var src = Path.Combine(_tempDir, "source.txt");
        var link = Path.Combine(_tempDir, "link.txt");
        await File.WriteAllTextAsync(src, "test content");

        _baseService.Setup(x => x.CreateSymlinkAsync(link, src, default))
            .Returns(Task.CompletedTask);

        await _service.CreateSymlinkAsync(link, src);

        _baseService.Verify(x => x.CreateSymlinkAsync(link, src, default), Times.Once);
    }

    /// <summary>
    /// Cleans up the temporary directory after each test.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }
}
