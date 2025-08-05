using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
using GenHub.Features.Validation;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Features.Validation;

/// <summary>
/// Unit tests for GameInstallationValidator.
/// </summary>
public class GameInstallationValidatorTests
{
    private readonly Mock<ILogger<GameInstallationValidator>> _loggerMock;
    private readonly Mock<IManifestProvider> _manifestProviderMock;
    private readonly GameInstallationValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallationValidatorTests"/> class.
    /// </summary>
    public GameInstallationValidatorTests()
    {
        _loggerMock = new Mock<ILogger<GameInstallationValidator>>();
        _manifestProviderMock = new Mock<IManifestProvider>();
        _validator = new GameInstallationValidator(_loggerMock.Object, _manifestProviderMock.Object);
    }

    /// <summary>
    /// Verifies that progress is reported during validation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithProgressCallback_ReportsProgress()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var filePath = Path.Combine(tempDir.FullName, "file1.txt");
        await File.WriteAllTextAsync(filePath, "file1.txt"); // 8 bytes

        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "file1.txt", Size = 8, Hash = string.Empty },
            },
        };
        _manifestProviderMock
            .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
            .ReturnsAsync(manifest);

        var installation = new GameInstallation(
            tempDir.FullName,
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);

        var progressReports = new List<ValidationProgress>();
        var progress = new Progress<ValidationProgress>(p => progressReports.Add(p));

        // Act
        await _validator.ValidateAsync(installation, progress);
        await Task.Delay(50); // Ensure all progress callbacks are processed

        // Assert
        if (progressReports.Count == 0)
        {
            var files = string.Join("\n", Directory.GetFiles(tempDir.FullName).Select(f => Path.GetFileName(f)));
            var issuesStr = string.Empty;
            try
            {
                var manifest2 = new ContentManifest
                {
                    Files = new()
                    {
                        new ManifestFile { RelativePath = "file1.txt", Size = 8, Hash = string.Empty },
                    },
                };
                var validator2 = new FileSystemValidatorTests.TestFileSystemValidator(new Mock<ILogger>().Object);
                var issuesList = await validator2.ValidateFilesAsync(tempDir.FullName, manifest2.Files, CancellationToken.None);
                issuesStr = string.Join("\n", issuesList.Select(i => $"Type: {i.IssueType}, Path: {i.Path}, Msg: {i.Message}"));
            }
            catch
            {
                // ignore
            }

            throw new Xunit.Sdk.XunitException($"No progress reports. Directory contents:\n{files}\nIssues:\n{issuesStr}");
        }

        Assert.Equal(1, progressReports.Last().Processed);
        Assert.Equal(1, progressReports.Last().Total);
        Assert.Equal(100, progressReports.Last().PercentComplete);

        tempDir.Delete(true);
    }

    /// <summary>
    /// Tests that ValidateAsync adds an issue when manifest is not found.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_ManifestNotFound_AddsIssue()
    {
        // Arrange
        _manifestProviderMock
            .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest?)null);

        var installation = new GameInstallation(
            "path",
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);

        // Act
        var result = await _validator.ValidateAsync(installation, null, default);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal(ValidationIssueType.MissingFile, result.Issues[0].IssueType);
    }

    /// <summary>
    /// Tests that ValidateAsync adds a missing file issue.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_MissingFile_AddsMissingFileIssue()
    {
        // Arrange
        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "missing.txt", Size = 0, Hash = string.Empty },
            },
        };
        _manifestProviderMock
            .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
            .ReturnsAsync(manifest);

        var installation = new GameInstallation(
            Directory.GetCurrentDirectory(),
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);

        // Act
        var result = await _validator.ValidateAsync(installation, null, default);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.MissingFile);
    }

    /// <summary>
    /// Tests that ValidateAsync throws OperationCanceledException when cancelled.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var installation = new GameInstallation(
            "path",
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _validator.ValidateAsync(installation, null, cts.Token));
    }
}
