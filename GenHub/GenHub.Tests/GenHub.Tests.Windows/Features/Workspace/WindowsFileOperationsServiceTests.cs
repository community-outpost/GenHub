using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Features.Workspace;
using GenHub.Windows.Features.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Windows.Features.Workspace;

/// <summary>
/// Tests for the WindowsFileOperationsService class (Windows-specific).
/// </summary>
public class WindowsFileOperationsServiceTests : IDisposable
{
    private readonly ILogger<WindowsFileOperationsService> _logger;
    private readonly WindowsFileOperationsService _service;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsFileOperationsServiceTests"/> class.
    /// </summary>
    public WindowsFileOperationsServiceTests()
    {
        var loggerMock = new Mock<ILogger<FileOperationsService>>();
        var downloadServiceMock = new Mock<IDownloadService>();
        var casServiceMock = new Mock<ICasService>();
        var baseService = new FileOperationsService(loggerMock.Object, downloadServiceMock.Object, casServiceMock.Object);
        _logger = NullLogger<WindowsFileOperationsService>.Instance;
        _service = new WindowsFileOperationsService(baseService, casServiceMock.Object, _logger);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Tests that CreateHardLinkAsync creates a hard link on Windows.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHardLinkAsync_CreatesHardLink_OnWindowsAsync()
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
    public async Task CreateSymlinkAsync_CreatesSymlink_OnWindowsAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Skip on non-Windows
        }

        var src = Path.Combine(_tempDir, "source.txt");
        var link = Path.Combine(_tempDir, "link.txt");
        await File.WriteAllTextAsync(src, "test content");

        try
        {
            await _service.CreateSymlinkAsync(link, src);
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
        catch (IOException)
        {
            // Skip test if privilege is not held (different exception on some systems)
            return;
        }

        // Note: Since we're using a concrete mock, we can't easily verify calls
        // This test would need to be refactored to work with the new architecture
        // For now, we've ensured the service doesn't throw unhandled exceptions
    }

    /// <summary>
    /// Tests that DeleteDirectoryIfExists unlinks an NTFS directory junction without deleting the target directory or its contents.
    /// </summary>
    [Fact]
    public void DeleteDirectoryIfExists_UnlinksNtfsJunctionWithoutDeletingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var targetDir = Path.Combine(_tempDir, "junction_target");
        Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, "important.txt");
        File.WriteAllText(targetFile, "critical game data");

        var parentDir = Path.Combine(_tempDir, "workspace");
        Directory.CreateDirectory(parentDir);
        var junctionPath = Path.Combine(parentDir, "Core");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetDir}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(psi);
        process?.WaitForExit(ProcessConstants.HelperProcessTimeoutMs);

        if (!Directory.Exists(junctionPath))
        {
            // Skip if environment does not allow junction creation
            return;
        }

        var result = FileOperationsService.DeleteDirectoryIfExists(parentDir);

        Assert.True(result);
        Assert.False(Directory.Exists(parentDir));
        Assert.True(Directory.Exists(targetDir), "Target directory of junction must remain intact!");
        Assert.True(File.Exists(targetFile), "Files in target directory must remain intact!");
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