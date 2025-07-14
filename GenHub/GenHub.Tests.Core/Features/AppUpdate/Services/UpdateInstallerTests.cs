using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.GitHub;
using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Tests for <see cref="UpdateInstaller"/>.
/// </summary>
public class UpdateInstallerTests : IDisposable
{
    private readonly Mock<ILogger<UpdateInstaller>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;
    private readonly string _tempDirectory;

    public UpdateInstallerTests()
    {
        _mockLogger = new Mock<ILogger<UpdateInstaller>>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), "UpdateInstallerTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Act & Assert
        var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);
        installer.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new UpdateInstaller(null!, _mockLogger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => new UpdateInstaller(_httpClient, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task DownloadAndInstallAsync_WithInvalidUrl_ShouldThrowArgumentException(string? url)
    {
        // Arrange
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);

        // Act & Assert
        var act = () => installer.DownloadAndInstallAsync(url!);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("downloadUrl");
    }

    [Fact]
    public async Task DownloadAndInstallAsync_WithValidZipUrl_ShouldDownloadAndCreateUpdater()
    {
        // Arrange
        var zipContent = CreateTestZipFile();
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        
        SetupHttpResponse(HttpStatusCode.OK, zipContent, "application/zip");
        
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);
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

    [Fact]
    public async Task DownloadAndInstallAsync_WithHttpError_ShouldReturnFalse()
    {
        // Arrange
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        SetupHttpResponse(HttpStatusCode.NotFound, Array.Empty<byte>());
        
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);

        // Act
        var result = await installer.DownloadAndInstallAsync(url);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetPlatformDownloadUrl_WithNullAssets_ShouldReturnNull()
    {
        // Arrange
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);

        // Act
        var result = installer.GetPlatformDownloadUrl(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetPlatformDownloadUrl_WithEmptyAssets_ShouldReturnNull()
    {
        // Arrange
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>();

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetPlatformDownloadUrl_WithWindowsAsset_ShouldReturnWindowsUrl()
    {
        // Arrange
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>
        {
            new() { Name = "app-linux.tar.gz", BrowserDownloadUrl = "https://example.com/linux" },
            new() { Name = "app-windows.zip", BrowserDownloadUrl = "https://example.com/windows" },
            new() { Name = "app-mac.dmg", BrowserDownloadUrl = "https://example.com/mac" }
        };

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            result.Should().Be("https://example.com/windows");
        }
        else
        {
            result.Should().NotBeNull(); // Should return some asset
        }
    }

    [Fact]
    public void GetPlatformDownloadUrl_WithNoMatchingAsset_ShouldReturnFirstAsset()
    {
        // Arrange
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);
        var assets = new List<GitHubReleaseAsset>
        {
            new() { Name = "readme.txt", BrowserDownloadUrl = "https://example.com/readme" },
            new() { Name = "changelog.md", BrowserDownloadUrl = "https://example.com/changelog" }
        };

        // Act
        var result = installer.GetPlatformDownloadUrl(assets);

        // Assert
        result.Should().Be("https://example.com/readme");
    }

    [Fact]
    public async Task DownloadAndInstallAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var url = "https://github.com/test/repo/releases/download/v1.0.0/test.zip";
        
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken token) =>
            {
                await Task.Delay(1000, token); // Simulate slow download
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        
        using var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);

        // Act
        cts.CancelAfter(100);
        var act = () => installer.DownloadAndInstallAsync(url, null, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);

        // Act & Assert
        var act = installer.Dispose;
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var installer = new UpdateInstaller(_httpClient, _mockLogger.Object);

        // Act & Assert
        installer.Dispose();
        var act = installer.Dispose;
        act.Should().NotThrow();
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, byte[] content, string? contentType = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(content)
        };

        if (!string.IsNullOrEmpty(contentType))
        {
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        }

        response.Content.Headers.ContentLength = content.Length;

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private byte[] CreateTestZipFile()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Create a fake publish directory structure
            var publishEntry = archive.CreateEntry("publish/");
            
            // Add some test files
            var exeEntry = archive.CreateEntry("publish/GenHub.Windows.exe");
            using var exeStream = exeEntry.Open();
            var exeContent = System.Text.Encoding.UTF8.GetBytes("fake exe content");
            exeStream.Write(exeContent, 0, exeContent.Length);
            
            var dllEntry = archive.CreateEntry("publish/GenHub.dll");
            using var dllStream = dllEntry.Open();
            var dllContent = System.Text.Encoding.UTF8.GetBytes("fake dll content");
            dllStream.Write(dllContent, 0, dllContent.Length);
            
            var configEntry = archive.CreateEntry("publish/appsettings.json");
            using var configStream = configEntry.Open();
            var configContent = System.Text.Encoding.UTF8.GetBytes("{}");
            configStream.Write(configContent, 0, configContent.Length);
        }
        
        return memoryStream.ToArray();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
