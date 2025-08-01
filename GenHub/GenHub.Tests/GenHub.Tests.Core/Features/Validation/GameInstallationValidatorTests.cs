using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
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
    private readonly Mock<IContentValidator> _contentValidatorMock = new();
    private readonly GameInstallationValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallationValidatorTests"/> class.
    /// </summary>
    public GameInstallationValidatorTests()
    {
        _loggerMock = new Mock<ILogger<GameInstallationValidator>>();
        _manifestProviderMock = new Mock<IManifestProvider>();
        _contentValidatorMock = new Mock<IContentValidator>();

        // Setup ContentValidator mocks to return valid results
        _contentValidatorMock.Setup(c => c.ValidateManifestAsync(It.IsAny<GameManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));
        _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<GameManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));
        _contentValidatorMock.Setup(c => c.DetectExtraneousFilesAsync(It.IsAny<string>(), It.IsAny<GameManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        _validator = new GameInstallationValidator(_loggerMock.Object, _manifestProviderMock.Object, _contentValidatorMock.Object);
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

        var manifest = new GameManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "file1.txt", Size = 8, Hash = string.Empty },
            },
            RequiredDirectories = new List<string> { "testdir" }, // Add a required directory to trigger step 4
        };
        _manifestProviderMock
            .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
            .ReturnsAsync(manifest);

        // Create the required directory
        Directory.CreateDirectory(Path.Combine(tempDir.FullName, "testdir"));

        var installation = new GameInstallation(
            tempDir.FullName,
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);

        var progressReports = new List<ValidationProgress>();
        var progress = new Progress<ValidationProgress>(p => progressReports.Add(p));

        // Act
        await _validator.ValidateAsync(installation, progress);
        await Task.Delay(100); // Ensure all progress callbacks are processed

        // Assert
        Assert.True(progressReports.Count > 0, "Expected progress reports to be generated");

        // Verify we got all 4 progress steps
        Assert.True(progressReports.Count >= 4, $"Expected at least 4 progress reports, got {progressReports.Count}");

        // Check the final progress
        var finalProgress = progressReports.Last();
        Assert.Equal(6, finalProgress.Total);
        Assert.Equal(6, finalProgress.Processed);
        Assert.Equal(100, finalProgress.PercentComplete);

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
            .ReturnsAsync((GameManifest?)null);

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
        var manifest = new GameManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "missing.txt", Size = 0, Hash = string.Empty },
            },
        };
        _manifestProviderMock
            .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
            .ReturnsAsync(manifest);

        // Setup ContentValidator to return missing file issue
        _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<GameManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
            {
                new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = "missing.txt", Message = "File not found" },
            }));

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);

            // Act
            var result = await _validator.ValidateAsync(installation, null, default);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.MissingFile);
        }
        finally
        {
            tempDir.Delete(true);
        }
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
