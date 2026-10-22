using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Services;

/// <summary>
/// Tests for <see cref="GitHubProjectImportService"/>.
/// </summary>
public sealed class GitHubProjectImportServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<ILogger<GitHubProjectImportService>> _mockLogger;
    private readonly Mock<IDownloadService> _mockDownloadService;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubProjectImportServiceTests"/> class.
    /// </summary>
    public GitHubProjectImportServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_GitHubImportTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _mockLogger = new Mock<ILogger<GitHubProjectImportService>>();
        _mockDownloadService = new Mock<IDownloadService>();
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    /// <summary>
    /// A repository without a project file gets a generated project.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task ImportRepositoryAsync_WithoutProjectFile_CreatesProjectAsync()
    {
        // Arrange
        var fixtureZip = CreateFixtureZip("repo-main", ("repo-main/window/Menus/MainMenu.wnd", "WINDOW\r\nEND"));
        Uri? requestedUrl = null;
        SetupDownload(fixtureZip, url => requestedUrl = url);
        var configService = new ProjectConfigService(Mock.Of<ILogger<ProjectConfigService>>());
        var service = new GitHubProjectImportService(_mockLogger.Object, _mockDownloadService.Object, configService);
        var reference = new GitHubRepositoryReference("owner", "repo", "main");
        var targetDir = Path.Combine(_tempDirectory, "target");

        // Act
        var result = await service.ImportRepositoryAsync(reference, targetDir);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.NotNull(requestedUrl);
        Assert.Contains("owner", requestedUrl.ToString());
        Assert.Contains("repo", requestedUrl.ToString());
        Assert.Contains("main", requestedUrl.ToString());
        Assert.True(File.Exists(result.Data), $"Project file missing: {result.Data}");
        Assert.True(File.Exists(Path.Combine(targetDir, "GameFilesEdited", "window", "Menus", "MainMenu.wnd")));
    }

    /// <summary>
    /// A repository with a project file is linked as-is without generating one.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task ImportRepositoryAsync_WithProjectFile_LinksExistingAsync()
    {
        // Arrange
        var fixtureZip = CreateFixtureZip("repo-main", ("repo-main/MyRepo.mbproj", "{}"));
        SetupDownload(fixtureZip);
        var mockConfigService = new Mock<IProjectConfigService>(MockBehavior.Strict);
        var service = new GitHubProjectImportService(_mockLogger.Object, _mockDownloadService.Object, mockConfigService.Object);
        var reference = new GitHubRepositoryReference("owner", "MyRepo", "main");
        var targetDir = Path.Combine(_tempDirectory, "target");

        // Act
        var result = await service.ImportRepositoryAsync(reference, targetDir);

        // Assert
        Assert.True(result.Success, result.FirstError);
        Assert.Equal(Path.Combine(targetDir, "MyRepo.mbproj"), result.Data);
        mockConfigService.Verify(
            x => x.CreateProjectFromDirectoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GenHub.Core.Models.Enums.ContentType>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A failed download surfaces the repository identity in the error.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task ImportRepositoryAsync_WhenDownloadFails_ReturnsFailureAsync()
    {
        // Arrange
        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadResult.CreateFailure("404 Not Found"));
        var configService = new ProjectConfigService(Mock.Of<ILogger<ProjectConfigService>>());
        var service = new GitHubProjectImportService(_mockLogger.Object, _mockDownloadService.Object, configService);
        var reference = new GitHubRepositoryReference("owner", "missing", "main");

        // Act
        var result = await service.ImportRepositoryAsync(reference, Path.Combine(_tempDirectory, "target"));

        // Assert
        Assert.False(result.Success);
        Assert.Contains("owner/missing", result.FirstError);
    }

    /// <summary>
    /// An empty target directory is rejected.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task ImportRepositoryAsync_WithEmptyTarget_ReturnsFailureAsync()
    {
        // Arrange
        var configService = new ProjectConfigService(Mock.Of<ILogger<ProjectConfigService>>());
        var service = new GitHubProjectImportService(_mockLogger.Object, _mockDownloadService.Object, configService);

        // Act
        var result = await service.ImportRepositoryAsync(new GitHubRepositoryReference("owner", "repo", "main"), "  ");

        // Assert
        Assert.False(result.Success);
    }

    private void SetupDownload(string fixtureZip, Action<Uri>? onUrl = null)
    {
        _mockDownloadService.Setup(x => x.DownloadFileAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Uri, string, string?, IProgress<DownloadProgress>?, CancellationToken>((url, destination, _, _, _) =>
            {
                onUrl?.Invoke(url);
                File.Copy(fixtureZip, destination, overwrite: true);
            })
            .ReturnsAsync((Uri url, string destination, string? _, IProgress<DownloadProgress>? _, CancellationToken _) =>
                DownloadResult.CreateSuccess(destination, new FileInfo(fixtureZip).Length, TimeSpan.FromMilliseconds(10)));
    }

    private string CreateFixtureZip(string wrapperDir, params (string EntryName, string Content)[] entries)
    {
        var zipPath = Path.Combine(_tempDirectory, wrapperDir + "_" + Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return zipPath;
    }
}
