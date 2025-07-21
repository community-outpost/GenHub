using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Features.Workspace;

/// <summary>
/// Tests for the FileOperationsService class.
/// </summary>
public class FileOperationsServiceTests : IDisposable
{
    private readonly Mock<ILogger<FileOperationsService>> _logger;
    private readonly HttpClient _httpClient;
    private readonly FileOperationsService _service;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileOperationsServiceTests"/> class.
    /// </summary>
    public FileOperationsServiceTests()
    {
        _logger = new Mock<ILogger<FileOperationsService>>();
        _httpClient = new HttpClient();
        _service = new FileOperationsService(_logger.Object, _httpClient);
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Tests that CopyFileAsync creates a file at the destination path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyFileAsync_CreatesFile()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dst = Path.Combine(_tempDir, "destination.txt");

        await File.WriteAllTextAsync(src, "test content");
        await _service.CopyFileAsync(src, dst);

        Assert.True(File.Exists(dst));
        Assert.Equal("test content", await File.ReadAllTextAsync(dst));
    }

    /// <summary>
    /// Tests that CreateSymlinkAsync creates a symbolic link.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateSymlinkAsync_CreatesSymlink()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var link = Path.Combine(_tempDir, "link.txt");

        await File.WriteAllTextAsync(src, "test content");
        bool isWindows = OperatingSystem.IsWindows();
        bool isAdmin = isWindows && new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        if (!isWindows || !isAdmin)
        {
            return;
        }

        await _service.CreateSymlinkAsync(link, src);

        Assert.True(File.Exists(link));
        Assert.Equal("test content", await File.ReadAllTextAsync(link));
    }

    /// <summary>
    /// Tests that VerifyFileHashAsync handles both case sensitive and case insensitive hash comparisons.
    /// </summary>
    /// <param name="caseSensitive">Whether to test case sensitive comparison.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerifyFileHashAsync_HandlesCase(bool caseSensitive)
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "test");

        var expectedHash = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";
        var testHash = caseSensitive ? expectedHash : expectedHash.ToLowerInvariant();

        var result = await _service.VerifyFileHashAsync(file, testHash);

        Assert.True(result);
    }

    /// <summary>
    /// Tests that VerifyFileHashAsync returns false when the hash does not match.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task VerifyFileHashAsync_ReturnsFalse_WhenHashDoesNotMatch()
    {
        var file = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(file, "test");

        var wrongHash = "WRONG_HASH";
        var result = await _service.VerifyFileHashAsync(file, wrongHash);

        Assert.False(result);
    }

    /// <summary>
    /// Tests that CreateHardLinkAsync creates a hard link.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHardLinkAsync_CreatesHardLink()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var link = Path.Combine(_tempDir, "hardlink.txt");

        await File.WriteAllTextAsync(src, "test content");
        bool isWindows = OperatingSystem.IsWindows();
        bool isAdmin = isWindows && new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        if (!isWindows || !isAdmin)
        {
            return;
        }

        await _service.CreateHardLinkAsync(link, src);

        Assert.True(File.Exists(link));
        Assert.Equal("test content", await File.ReadAllTextAsync(link));
    }

    /// <summary>
    /// Performs cleanup by disposing of temporary resources.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    /// Tests that DownloadFileAsync reports progress during the download operation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DownloadFileAsync_ReportsProgress()
    {
        // Skip this test if no internet connection or service unavailable
        // This test is flaky due to external dependency
        try
        {
            var progressReports = new List<DownloadProgress>();
            var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));

            // Use a smaller, more reliable endpoint
            var testUrl = "https://httpbin.org/bytes/100";
            var destination = Path.Combine(_tempDir, "download.bin");

            await _service.DownloadFileAsync(testUrl, destination, progress);

            Assert.True(File.Exists(destination));
            Assert.True(progressReports.Count > 0);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("503") || ex.Message.Contains("Service Temporarily Unavailable"))
        {
            // Skip test if service is unavailable
            return;
        }
        catch (TaskCanceledException)
        {
            // Skip test if timeout
            return;
        }
    }

    /// <summary>
    /// Tests that CopyFileAsync throws an exception when the source file does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyFileAsync_ThrowsException_WhenSourceFileNotFound()
    {
        var nonExistentSource = Path.Combine(_tempDir, "nonexistent.txt");
        var destination = Path.Combine(_tempDir, "destination.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.CopyFileAsync(nonExistentSource, destination));
    }

    /// <summary>
    /// Tests that CopyFileAsync creates the necessary directory structure for the destination file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CopyFileAsync_CreatesDirectoryStructure()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        var destination = Path.Combine(_tempDir, "nested", "deep", "destination.txt");

        await File.WriteAllTextAsync(source, "test content");

        await _service.CopyFileAsync(source, destination);

        Assert.True(File.Exists(destination));
        Assert.True(Directory.Exists(Path.GetDirectoryName(destination)));
    }

    /// <summary>
    /// Tests that CreateSymlinkAsync throws an exception when the target file does not exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateSymlinkAsync_ThrowsException_WhenTargetNotExists()
    {
        var nonExistentTarget = Path.Combine(_tempDir, "nonexistent.txt");
        var link = Path.Combine(_tempDir, "link.txt");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.CreateSymlinkAsync(link, nonExistentTarget));
    }
}
