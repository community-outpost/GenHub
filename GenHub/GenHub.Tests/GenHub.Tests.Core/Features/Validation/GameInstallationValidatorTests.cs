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
        _contentValidatorMock.Setup(c => c.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

    // Use unified ValidateAllAsync for full validation
        _contentValidatorMock.Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
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
        try
        {
            var filePath = Path.Combine(tempDir.FullName, "file1.txt");
            await File.WriteAllTextAsync(filePath, "file1.txt"); // 8 bytes

            // Create the game installation directories that Fetch() looks for to ensure consistent behavior
            var generalsDir = Path.Combine(tempDir.FullName, "Command and Conquer Generals");
            var zeroHourDir = Path.Combine(tempDir.FullName, "Command and Conquer Generals Zero Hour");
            Directory.CreateDirectory(generalsDir);
            Directory.CreateDirectory(zeroHourDir);

            var manifest = new ContentManifest
            {
                Files = new()
                {
                    new ManifestFile { RelativePath = "file1.txt", Size = 8, Hash = string.Empty },
                },
                RequiredDirectories = new List<string> { "testdir" },
            };
            _manifestProviderMock
                .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
                .ReturnsAsync(manifest);

            // Create the required directory in both game directories
            Directory.CreateDirectory(Path.Combine(generalsDir, "testdir"));
            Directory.CreateDirectory(Path.Combine(zeroHourDir, "testdir"));

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);

            // Ensure the installation is properly fetched to have consistent state
            installation.Fetch();

            // Use thread-safe collection for progress reports
            var progressReports = new System.Collections.Concurrent.ConcurrentBag<ValidationProgress>();
            var progress = new Progress<ValidationProgress>(p => progressReports.Add(p));

            // Act
            await _validator.ValidateAsync(installation, progress);
            await Task.Delay(100); // Ensure all progress callbacks are processed

            // Assert
            var reportsList = progressReports.ToList();
            Assert.True(reportsList.Count > 0, "Expected progress reports to be generated");

            // Find the final progress report (highest processed count)
            var finalProgress = reportsList.OrderBy(p => p.Processed).Last();

            // Verify the final progress shows completion
            Assert.Equal(finalProgress.Total, finalProgress.Processed);
            Assert.Equal(100, finalProgress.PercentComplete);

            // Verify we have reasonable progress reporting (at least 4 steps for basic validation)
            // Don't assert exact counts since they vary based on installation detection
            Assert.True(finalProgress.Total >= 4, $"Expected at least 4 total steps, got {finalProgress.Total}");
            Assert.True(reportsList.Count >= 3, $"Expected at least 3 progress reports, got {reportsList.Count}");

            // Verify all progress reports have consistent total
            var allTotals = reportsList.Select(p => p.Total).Distinct().ToList();
            Assert.True(allTotals.Count == 1, $"All progress reports should have the same total. Found totals: [{string.Join(", ", allTotals)}]");

            // Verify progress values are within valid range
            Assert.All(reportsList, report =>
            {
                Assert.True(report.Processed >= 0 && report.Processed <= report.Total, $"Progress processed ({report.Processed}) should be between 0 and total ({report.Total})");
                Assert.True(report.PercentComplete >= 0 && report.PercentComplete <= 100, $"Percent complete ({report.PercentComplete}) should be between 0 and 100");
            });
        }
        finally
        {
            tempDir.Delete(true);
        }
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

        // Setup ContentValidator to return missing file issue
        _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
            {
                new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = "missing.txt", Message = "File not found" },
            }));

        // Ensure full validation returns the same result
        var missingResult = new ValidationResult("test", new List<ValidationIssue>
        {
            new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = "missing.txt", Message = "File not found" },
        });
        _contentValidatorMock.Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(missingResult);

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
