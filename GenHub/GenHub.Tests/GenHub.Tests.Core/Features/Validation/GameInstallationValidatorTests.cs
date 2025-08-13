using GenHub.Core.Interfaces.Common;
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

namespace GenHub.Tests.Features.Validation
{
    /// <summary>
    /// Unit tests for GameInstallationValidator.
    /// </summary>
    public class GameInstallationValidatorTests
    {
        private readonly Mock<ILogger<GameInstallationValidator>> _loggerMock;
        private readonly Mock<IManifestProvider> _manifestProviderMock;
        private readonly Mock<IContentValidator> _contentValidatorMock = new();
        private readonly Mock<IFileHashProvider> _hashProviderMock = new();
        private readonly GameInstallationValidator _validator;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameInstallationValidatorTests"/> class.
        /// </summary>
        public GameInstallationValidatorTests()
        {
            _loggerMock = new Mock<ILogger<GameInstallationValidator>>();
            _manifestProviderMock = new Mock<IManifestProvider>();
            _contentValidatorMock = new Mock<IContentValidator>();
            _hashProviderMock = new Mock<IFileHashProvider>();

            // Setup ContentValidator mocks to return valid results
            _contentValidatorMock.Setup(c => c.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));
            _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));
            _contentValidatorMock.Setup(c => c.DetectExtraneousFilesAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));
            _contentValidatorMock.Setup(c => c.DetectExtraneousFilesAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

            _validator = new GameInstallationValidator(_loggerMock.Object, _manifestProviderMock.Object, _contentValidatorMock.Object, _hashProviderMock.Object);
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

                var requiredDir = "testdir";
                var manifest = new ContentManifest
                {
                    Files = new()
                    {
                        new ManifestFile { RelativePath = "file1.txt", Size = 8, Hash = string.Empty },
                    },
                    RequiredDirectories = new List<string> { requiredDir },
                };
                _manifestProviderMock
                    .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
                    .ReturnsAsync(manifest);

                Directory.CreateDirectory(Path.Combine(tempDir.FullName, requiredDir));

                var installation = new GameInstallation(
                    tempDir.FullName,
                    GameInstallationType.Steam,
                    new Mock<ILogger<GameInstallation>>().Object);

                // Use a custom synchronous progress implementation
                var progressTracker = new SynchronousProgress<ValidationProgress>();

                // Act
                await _validator.ValidateAsync(
                    installation,
                    progressTracker);

                // Assert
                var progressReports = progressTracker.Reports;
                Assert.True(progressReports.Count > 0, $"Expected progress reports to be generated, got {progressReports.Count}");

                // Verify we have reasonable number of progress reports
                Assert.True(progressReports.Count >= 2, $"Expected at least 2 progress reports, got {progressReports.Count}");

                // Verify the final progress report shows completion
                var finalProgress = progressReports.Last();
                Assert.Equal(finalProgress.Total, finalProgress.Processed);
                Assert.Equal(100, finalProgress.PercentComplete);

                // Verify that all progress reports have reasonable step counts
                foreach (var report in progressReports)
                {
                    Assert.True(report.Total >= 4, $"Expected total steps >= 4, got {report.Total}");
                    Assert.True(report.Processed >= 1, $"Expected processed steps >= 1, got {report.Processed}");
                    Assert.True(report.Processed <= report.Total, $"Expected processed <= total, got {report.Processed}/{report.Total}");
                }

                // Verify progress is monotonically increasing
                for (int i = 1; i < progressReports.Count; i++)
                {
                    Assert.True(
                        progressReports[i].Processed >= progressReports[i - 1].Processed,
                        $"Progress should be monotonically increasing. Step {i}: {progressReports[i].Processed} vs {progressReports[i - 1].Processed}");
                }
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

        /// <summary>
        /// Tests that ValidateAsync detects missing required directories.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task ValidateAsync_MissingRequiredDirectory_AddsMissingDirectoryIssue()
        {
            // Arrange
            var tempDir = Directory.CreateTempSubdirectory();
            try
            {
                var manifest = new ContentManifest
                {
                    Files = new() { new ManifestFile { RelativePath = "file1.txt", Size = 0, Hash = string.Empty } },
                    RequiredDirectories = new List<string> { "RequiredDir" },
                };
                _manifestProviderMock
                    .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
                    .ReturnsAsync(manifest);

                // Setup ContentValidator to return missing directory issue
                _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
                    {
                        new ValidationIssue { IssueType = ValidationIssueType.DirectoryMissing, Path = "RequiredDir", Message = "Required directory not found" },
                    }));

                var installation = new GameInstallation(
                    tempDir.FullName,
                    GameInstallationType.Steam,
                    new Mock<ILogger<GameInstallation>>().Object);

                // Act
                var result = await _validator.ValidateAsync(installation, null, default);

                // Assert
                Assert.False(result.IsValid);
                Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.DirectoryMissing);
            }
            finally
            {
                tempDir.Delete(true);
            }
        }

        /// <summary>
        /// Tests that ValidateAsync handles empty manifest gracefully.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task ValidateAsync_EmptyManifest_HandlesGracefully()
        {
            // Arrange
            var tempDir = Directory.CreateTempSubdirectory();
            try
            {
                var manifest = new ContentManifest
                {
                    Files = new List<ManifestFile>(),
                    RequiredDirectories = new List<string>(),
                };
                _manifestProviderMock
                    .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
                    .ReturnsAsync(manifest);

                var installation = new GameInstallation(
                    tempDir.FullName,
                    GameInstallationType.Steam,
                    new Mock<ILogger<GameInstallation>>().Object);

                // Act
                var result = await _validator.ValidateAsync(installation, null, default);

                // Assert
                Assert.True(result.IsValid);
                Assert.Empty(result.Issues);
            }
            finally
            {
                tempDir.Delete(true);
            }
        }

        /// <summary>
        /// Tests that ValidateAsync detects unexpected files as warnings.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task ValidateAsync_UnexpectedFiles_DetectsAsWarnings()
        {
            // Arrange
            var tempDir = Directory.CreateTempSubdirectory();
            try
            {
                var expectedFilePath = Path.Combine(tempDir.FullName, "expected.txt");
                var unexpectedFilePath = Path.Combine(tempDir.FullName, "unexpected.txt");
                await File.WriteAllTextAsync(expectedFilePath, "expected content");
                await File.WriteAllTextAsync(unexpectedFilePath, "unexpected content");

                var manifest = new ContentManifest
                {
                    Files = new()
                    {
                        new ManifestFile { RelativePath = "expected.txt", Size = 16, Hash = string.Empty },
                    },
                };
                _manifestProviderMock
                    .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
                    .ReturnsAsync(manifest);

                // Setup ContentValidator to return unexpected file issue
                _contentValidatorMock.Setup(c => c.DetectExtraneousFilesAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
                    {
                        new ValidationIssue { IssueType = ValidationIssueType.UnexpectedFile, Path = "unexpected.txt", Severity = ValidationSeverity.Warning, Message = "Unexpected file found" },
                    }));

                var installation = new GameInstallation(
                    tempDir.FullName,
                    GameInstallationType.Steam,
                    new Mock<ILogger<GameInstallation>>().Object);

                // Act
                var result = await _validator.ValidateAsync(installation, null, default);

                // Assert
                Assert.True(result.IsValid); // Warnings don't make result invalid
                Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.UnexpectedFile);
                Assert.All(
                    result.Issues.Where(i => i.IssueType == ValidationIssueType.UnexpectedFile),
                    i => Assert.Equal(ValidationSeverity.Warning, i.Severity));
            }
            finally
            {
                tempDir.Delete(true);
            }
        }

        /// <summary>
        /// Tests that ValidateAsync handles content validator exceptions gracefully.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task ValidateAsync_ContentValidatorException_HandlesGracefully()
        {
            // Arrange
            var tempDir = Directory.CreateTempSubdirectory();
            try
            {
                var manifest = new ContentManifest
                {
                    Files = new() { new ManifestFile { RelativePath = "test.txt", Size = 0, Hash = string.Empty } },
                };
                _manifestProviderMock
                    .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), default))
                    .ReturnsAsync(manifest);

                _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("Content validator error"));

                var installation = new GameInstallation(
                    tempDir.FullName,
                    GameInstallationType.Steam,
                    new Mock<ILogger<GameInstallation>>().Object);

                // Act
                var result = await _validator.ValidateAsync(installation, null, default);

                // Assert
                Assert.False(result.IsValid);
                Assert.Contains(result.Issues, i => i.Message.Contains("Content validator error"));
            }
            finally
            {
                tempDir.Delete(true);
            }
        }

        /// <summary>
        /// Custom progress implementation that captures reports synchronously.
        /// </summary>
        private class SynchronousProgress<T> : IProgress<T>
        {
            private readonly List<T> _reports = new();

            public IReadOnlyList<T> Reports => _reports;

            public void Report(T value)
            {
                _reports.Add(value);
            }
        }
    }
}
