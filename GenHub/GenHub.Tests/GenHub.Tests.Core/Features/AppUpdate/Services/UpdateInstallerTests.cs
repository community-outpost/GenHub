using System.IO.Compression;
using System.Net;
using FluentAssertions;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.GitHub;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Tests for <see cref="BaseUpdateInstaller"/> and platform implementations.
/// </summary>
public class UpdateInstallerTests : IDisposable
{
    private readonly Mock<ILogger<TestUpdateInstaller>> mockLogger;
    private readonly Mock<HttpMessageHandler> mockHttpHandler;
    private readonly HttpClient httpClient;
    private readonly string tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateInstallerTests"/> class.
    /// </summary>
    public UpdateInstallerTests()
    {
        this.mockLogger = new Mock<ILogger<TestUpdateInstaller>>();
        this.mockHttpHandler = new Mock<HttpMessageHandler>();
        this.mockHttpHandler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        this.httpClient = new HttpClient(this.mockHttpHandler.Object);
        this.tempDirectory = Path.Combine(Path.GetTempPath(), "UpdateInstallerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(this.tempDirectory);
    }

    /// <summary>
    /// Tests that constructor with valid parameters should not throw.
    /// </summary>
    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);
        installer.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that constructor with null HTTP client should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new TestUpdateInstaller(null!, this.mockLogger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    /// <summary>
    /// Tests that constructor with null logger should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new TestUpdateInstaller(this.httpClient, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with invalid URL should throw ArgumentException.
    /// </summary>
    /// <param name="url">The invalid URL to test.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task DownloadAndInstallAsync_WithInvalidUrl_ShouldThrowArgumentException(string? url)
    {
        // Arrange
        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act & Assert
        var act = () => installer.DownloadAndInstallAsync(url!);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("downloadUrl");
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with valid ZIP URL should download and create updater.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DownloadAndInstallAsync_WithValidZipUrl_ShouldDownloadAndCreateUpdater()
    {
        // Arrange
        var zipContent = this.CreateTestZipFile();
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";

        this.SetupHttpResponse(HttpStatusCode.OK, zipContent, "application/zip");

        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);
        var progressReports = new List<UpdateProgress>();
        var progress = new Progress<UpdateProgress>(p => progressReports.Add(p));

        // Act
        var result = await installer.DownloadAndInstallAsync(url, progress);

        // Assert
        result.Should().BeTrue();
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.Status.Contains("Preparing download"));
        progressReports.Should().Contain(p => p.Status.Contains("Downloading"));
        progressReports.Should().Contain(p => p.Status.Contains("Application will restart"));
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with HTTP error should return false.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DownloadAndInstallAsync_WithHttpError_ShouldReturnFalse()
    {
        // Arrange
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        this.SetupHttpResponse(HttpStatusCode.NotFound, Array.Empty<byte>());

        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act
        var result = await installer.DownloadAndInstallAsync(url);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with null assets should return null.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithNullAssets_ShouldReturnNull()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act
        var result = installer.GetPlatformDownloadUrl(null!);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with empty assets should return null.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithEmptyAssets_ShouldReturnNull()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>();

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with matching asset should return correct URL.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithMatchingAsset_ShouldReturnCorrectUrl()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>
            {
                new () { Name = "app-linux.tar.gz", BrowserDownloadUrl = "https://example.com/linux" },
                new () { Name = "app-test.zip", BrowserDownloadUrl = "https://example.com/test" },
                new () { Name = "app-mac.dmg", BrowserDownloadUrl = "https://example.com/mac" },
            };

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().Be("https://example.com/test");
    }

    /// <summary>
    /// Tests that GetPlatformDownloadUrl with no matching asset should return first asset.
    /// </summary>
    [Fact]
    public void GetPlatformDownloadUrl_WithNoMatchingAsset_ShouldReturnFirstAsset()
    {
        // Arrange
        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>
            {
                new () { Name = "readme.txt", BrowserDownloadUrl = "https://example.com/readme" },
                new () { Name = "changelog.md", BrowserDownloadUrl = "https://example.com/changelog" },
            };

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().Be("https://example.com/readme");
    }

    /// <summary>
    /// Tests that DownloadAndInstallAsync with cancellation should return false.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DownloadAndInstallAsync_WithCancellation_ShouldReturnFalse()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";

        // Set up the mock to throw OperationCanceledException when token is cancelled
        this.mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, token) =>
            {
                // Create a task that will be cancelled
                var tcs = new TaskCompletionSource<HttpResponseMessage>();

                // If token is already cancelled, set the task to cancelled immediately
                if (token.IsCancellationRequested)
                {
                    tcs.SetCanceled();
                    return tcs.Task;
                }

                // Register callback to cancel when token is cancelled
                token.Register(() => tcs.TrySetCanceled());

                return tcs.Task;
            });

        using var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act
        cts.Cancel();
        var result = await installer.DownloadAndInstallAsync(url, null, cts.Token);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Tests that Dispose should not throw.
    /// </summary>
    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act & Assert
        var act = installer.Dispose;
        act.Should().NotThrow();
    }

    /// <summary>
    /// Tests that Dispose called multiple times should not throw.
    /// </summary>
    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var installer = new TestUpdateInstaller(this.httpClient, this.mockLogger.Object);

        // Act & Assert
        installer.Dispose();
        var act = installer.Dispose;
        act.Should().NotThrow();
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
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Create a fake publish directory structure
            archive.CreateEntry("publish/");

            // Add some test files - properly close each entry's stream
            var exeEntry = archive.CreateEntry("publish/GenHub.Windows.exe");
            using (var exeStream = exeEntry.Open())
            {
                var exeContent = System.Text.Encoding.UTF8.GetBytes("fake exe content");
                exeStream.Write(exeContent, 0, exeContent.Length);
            }

            var dllEntry = archive.CreateEntry("publish/GenHub.dll");
            using (var dllStream = dllEntry.Open())
            {
                var dllContent = System.Text.Encoding.UTF8.GetBytes("fake dll content");
                dllStream.Write(dllContent, 0, dllContent.Length);
            }

            var configEntry = archive.CreateEntry("publish/appsettings.json");
            using (var configStream = configEntry.Open())
            {
                var configContent = System.Text.Encoding.UTF8.GetBytes("{}");
                configStream.Write(configContent, 0, configContent.Length);
            }
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Test implementation of BaseUpdateInstaller for testing purposes.
    /// </summary>
    public class TestUpdateInstaller : BaseUpdateInstaller
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestUpdateInstaller"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client.</param>
        /// <param name="logger">The logger.</param>
        public TestUpdateInstaller(HttpClient httpClient, ILogger<TestUpdateInstaller> logger)
            : base(httpClient, logger)
        {
        }

        /// <inheritdoc/>
        protected override List<string> GetPlatformAssetPatterns()
        {
            return new List<string> { "test", ".zip" };
        }

        /// <inheritdoc/>
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

        /// <summary>
        /// Override to prevent actual application shutdown during tests.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Always returns true for testing.</returns>
        protected new Task<bool> ScheduleApplicationShutdownAsync(CancellationToken cancellationToken)
        {
            // Do nothing in tests - don't actually try to shutdown or create batch files
            return Task.FromResult(true);
        }
    }
}
