using System.Net;
using FluentAssertions;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.GitHub;
using GenHub.Windows.Features.AppUpdate;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace GenHub.Tests.Windows.Features.AppUpdate;

/// <summary>
/// Tests for <see cref="WindowsUpdateInstaller"/>.
/// </summary>
public class WindowsUpdateInstallerTests : IDisposable
{
    private readonly Mock<ILogger<WindowsUpdateInstaller>> mockLogger;
    private readonly Mock<HttpMessageHandler> mockHttpHandler;
    private readonly HttpClient httpClient;
    private readonly string tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsUpdateInstallerTests"/> class.
    /// </summary>
    public WindowsUpdateInstallerTests()
    {
        this.mockLogger = new Mock<ILogger<WindowsUpdateInstaller>>();
        this.mockHttpHandler = new Mock<HttpMessageHandler>();
        this.mockHttpHandler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        this.httpClient = new HttpClient(this.mockHttpHandler.Object);
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "WindowsUpdateInstallerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(this.tempDirectory);
    }

    /// <summary>
    /// Tests that constructor with valid parameters should not throw.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var installer = new WindowsUpdateInstaller(this.httpClient, this.mockLogger.Object);
        installer.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that GetPlatformAssetPatterns returns Windows-specific patterns.
    /// </summary>
    [Fact]
    public void GetPlatformAssetPatterns_ShouldReturnWindowsPatterns()
    {
        // Arrange
        using var installer = new WindowsUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act
        var result = installer.GetPlatformDownloadUrl(new List<GitHubReleaseAsset>
            {
                new () { Name = "app-linux.tar.gz", BrowserDownloadUrl = "https://example.com/linux" },
                new () { Name = "app-windows.zip", BrowserDownloadUrl = "https://example.com/windows" },
                new () { Name = "app-mac.dmg", BrowserDownloadUrl = "https://example.com/mac" },
            });

        // Assert
        result.Should().Be("https://example.com/windows");
    }

    /// <summary>
    /// Tests that Windows installer prioritizes .exe files.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_ShouldPrioritizeWindowsExecutables()
    {
        // Arrange
        using var installer = new WindowsUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act
        var result = installer.GetPlatformDownloadUrl(new List<GitHubReleaseAsset>
            {
                new () { Name = "app.zip", BrowserDownloadUrl = "https://example.com/zip" },
                new () { Name = "app-setup.exe", BrowserDownloadUrl = "https://example.com/exe" },
                new () { Name = "app.msi", BrowserDownloadUrl = "https://example.com/msi" },
            });

        // Assert
        result.Should().Be("https://example.com/exe");
    }

    /// <summary>
    /// Tests that ZIP installation creates external updater.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task InstallZipAsync_ShouldCreateExternalUpdater()
    {
        // Arrange
        var zipContent = this.CreateTestZipFile();
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";

        this.SetupHttpResponse(HttpStatusCode.OK, zipContent, "application/zip");

        // Use test implementation that doesn't create actual processes
        using var installer = new TestWindowsUpdateInstaller(this.httpClient, this.mockLogger.Object);
        var progressReports = new List<UpdateProgress>();
        var progress = new Progress<UpdateProgress>(p => progressReports.Add(p));

        // Act
        var result = await installer.DownloadAndInstallAsync(url, progress);

        // Assert
        result.Should().BeTrue();
        progressReports.Should().Contain(p => p.Status.Contains("Application will restart"));
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        this.httpClient?.Dispose();

        if (Directory.Exists(this.tempDirectory))
        {
            try
            {
                Directory.Delete(this.tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        GC.SuppressFinalize(this);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, byte[] content, string? contentType = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(content),
        };

        if (!string.IsNullOrEmpty(contentType))
        {
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        response.Content.Headers.ContentLength = content.Length;

        this.mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private byte[] CreateTestZipFile()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("publish/");

            var exeEntry = archive.CreateEntry("publish/GenHub.Windows.exe");
            using (var exeStream = exeEntry.Open())
            {
                var exeContent = System.Text.Encoding.UTF8.GetBytes("fake exe content");
                exeStream.Write(exeContent, 0, exeContent.Length);
            }
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Test implementation that doesn't create actual processes.
    /// </summary>
    private class TestWindowsUpdateInstaller : WindowsUpdateInstaller
    {
        public TestWindowsUpdateInstaller(HttpClient httpClient, ILogger<WindowsUpdateInstaller> logger)
            : base(httpClient, logger)
        {
        }

        protected override Task<bool> CreateAndLaunchExternalUpdaterAsync(
            string sourceDirectory,
            string targetDirectory,
            IProgress<UpdateProgress>? progress,
            CancellationToken cancellationToken)
        {
            // Mock implementation for testing - DON'T create actual scripts/processes
            progress?.Report(new UpdateProgress
            {
                Status = "Application will restart to complete installation.",
                PercentComplete = 100,
                IsCompleted = true,
            });
            return Task.FromResult(true);
        }
    }
}
