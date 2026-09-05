using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.GameInstallations;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
using GenHub.Features.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Features.Validation;

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

        // Setup ContentValidator mocks to return valid results
        _contentValidatorMock.Setup(c => c.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        // Use unified ValidateAllAsync for full validation
        _contentValidatorMock.Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        _validator = new GameInstallationValidator(_loggerMock.Object, _manifestProviderMock.Object, _contentValidatorMock.Object, _hashProviderMock.Object);
    }

    /// <summary>
    /// Verifies that progress is reported during validation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithProgressCallback_ReportsProgressAsync()
    {
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

            var progress = new SynchronousProgress<ValidationProgress>();

            // Act
            await _validator.ValidateAsync(installation, progress);

            // Assert
            var reportsList = progress.GetReports();
            Assert.True(reportsList.Count > 0, "Expected progress reports to be generated");

            // Find the final progress report (highest processed count)
            var finalProgress = reportsList.MaxBy(p => p.Processed);
            Assert.NotNull(finalProgress);

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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_ManifestNotFound_AddsIssueAsync()
    {
        _manifestProviderMock
            .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentManifest?)null);

        var installation = new GameInstallation(
            "path",
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);

        var result = await _validator.ValidateAsync(installation, null, default);

        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal(ValidationIssueType.MissingFile, result.Issues[0].IssueType);
    }

    /// <summary>
    /// Tests that ValidateAsync adds a missing file issue.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_MissingFile_AddsMissingFileIssueAsync()
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
        _contentValidatorMock.Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_Cancellation_ThrowsOperationCanceledExceptionAsync()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var installation = new GameInstallation(
            "path",
            GameInstallationType.Steam,
            new Mock<ILogger<GameInstallation>>().Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _validator.ValidateAsync(installation, null, cts.Token));
    }

    /// <summary>
    /// Tests that ValidateAsync detects missing required directories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_MissingRequiredDirectory_AddsMissingDirectoryIssueAsync()
    {
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

            _contentValidatorMock
                .Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
                {
                        new ValidationIssue { IssueType = ValidationIssueType.DirectoryMissing, Path = "RequiredDir", Message = "Required directory not found" },
                }));

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);

            var result = await _validator.ValidateAsync(installation, null, default);

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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_EmptyManifest_HandlesGracefullyAsync()
    {
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

            var result = await _validator.ValidateAsync(installation, null, default);

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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_UnexpectedFiles_DetectsAsWarningsAsync()
    {
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

            _contentValidatorMock
                .Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
                {
                        new ValidationIssue { IssueType = ValidationIssueType.UnexpectedFile, Path = "unexpected.txt", Severity = ValidationSeverity.Warning, Message = "Unexpected file found" },
                }));

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);

            var result = await _validator.ValidateAsync(installation, null, default);

            Assert.True(result.IsValid);
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
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_ContentValidatorException_HandlesGracefullyAsync()
    {
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

            _contentValidatorMock
                .Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Content validator error"));

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);

            var result = await _validator.ValidateAsync(installation, null, default);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Message.Contains("Content validator error"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that ValidateAsync validates a multi-language installation using CsvContentProvider.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_WithCsvContentProvider_ValidatesMultiLanguageInstallationSuccessfullyAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new ContentManifest
            {
                Id = new ManifestId("csv-generals-1.08-de"),
                Name = "Generals 1.08 (DE)",
                Version = "1.08",
                ContentType = ContentType.GameInstallation,
                TargetGame = GameType.Generals,
                Files = new List<ManifestFile>
                {
                    new() { RelativePath = "generals.exe", Size = 100, Hash = "abc", SourceType = ContentSourceType.GameInstallation, IsRequired = true },
                    new() { RelativePath = "German.big", Size = 200, Hash = "def", SourceType = ContentSourceType.GameInstallation, IsRequired = true },
                },
            };

            var searchResult = new ContentSearchResult
            {
                Id = "csv-generals-1.08-de",
                Name = "Generals 1.08 (DE)",
                Version = "1.08",
                ContentType = ContentType.GameInstallation,
                TargetGame = GameType.Generals,
            };
            searchResult.SetData(manifest);

            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.TargetGame == GameType.Generals && q.Language == CsvConstants.LanguageDe), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResult]));

            var mockLanguageDetector = new Mock<ILanguageDetector>();
            mockLanguageDetector
                .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CsvConstants.LanguageDe);

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                null,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                mockLanguageDetector.Object,
                null,
                [mockContentProvider.Object]);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation, CancellationToken.None);

            Assert.True(result.IsValid);
            Assert.Equal(2, result.TotalFilesValidated);
            Assert.Empty(result.Issues);
            mockContentProvider.Verify(
                p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.TargetGame == GameType.Generals && q.Language == CsvConstants.LanguageDe), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that ValidateAsync with an explicit language overrides auto-detection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_WithExplicitLanguage_OverridesAutoDetectionAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new ContentManifest
            {
                Id = new ManifestId("csv-generals-1.08-fr"),
                Name = "Generals 1.08 (FR)",
                Version = "1.08",
                ContentType = ContentType.GameInstallation,
                TargetGame = GameType.Generals,
                Files = [new ManifestFile { RelativePath = "French.big", Size = 100, Hash = "abc", SourceType = ContentSourceType.GameInstallation }],
            };

            var searchResult = new ContentSearchResult { Id = "csv-generals-1.08-fr" };
            searchResult.SetData(manifest);

            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.Language == CsvConstants.LanguageFr), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResult]));

            var mockLanguageDetector = new Mock<ILanguageDetector>();
            mockLanguageDetector
                .Setup(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CsvConstants.LanguageDe); // Auto-detect would say DE, but explicit is FR

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                null,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                mockLanguageDetector.Object,
                null,
                [mockContentProvider.Object]);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation, "fr");

            Assert.True(result.IsValid);
            Assert.Equal(1, result.TotalFilesValidated);
            mockContentProvider.Verify(
                p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.Language == CsvConstants.LanguageFr), It.IsAny<CancellationToken>()),
                Times.Once);
            mockLanguageDetector.Verify(d => d.DetectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that ValidateInstallationAsync validates direct path and game type with language normalization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateInstallationAsync_DirectPathAndGameType_ResolvesAndValidatesAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new ContentManifest
            {
                Id = new ManifestId("csv-zerohour-1.04-zh-cn"),
                Name = "Zero Hour 1.04 (ZH-CN)",
                Version = "1.04",
                ContentType = ContentType.GameInstallation,
                TargetGame = GameType.ZeroHour,
                Files = [new ManifestFile { RelativePath = "ChineseZH.big", Size = 50, Hash = "xyz", SourceType = ContentSourceType.GameInstallation }],
            };

            var searchResult = new ContentSearchResult { Id = "csv-zerohour-1.04-zh-cn" };
            searchResult.SetData(manifest);

            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.TargetGame == GameType.ZeroHour && q.Language == CsvConstants.LanguageZhCn), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResult]));

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                null,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                new LanguageDetector(),
                null,
                [mockContentProvider.Object]);

            var result = await validator.ValidateInstallationAsync(tempDir.FullName, GameType.ZeroHour, "zh-cn");

            Assert.True(result.IsValid);
            Assert.Equal(1, result.TotalFilesValidated);
            mockContentProvider.Verify(
                p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.TargetGame == GameType.ZeroHour && q.Language == CsvConstants.LanguageZhCn), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that ValidateAsync reports detailed issue counts on ValidationResult.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_DetailedCounts_ReportsCorrectMissingCorruptedAndExtraCountsAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new ContentManifest
            {
                Id = new ManifestId("csv-generals-1.08-en"),
                Files =
                [
                    new ManifestFile { RelativePath = "missing1.txt", Size = 10, Hash = "h1" },
                    new ManifestFile { RelativePath = "corrupted1.txt", Size = 20, Hash = "h2" },
                    new ManifestFile { RelativePath = "valid1.txt", Size = 30, Hash = "h3" },
                ],
            };

            var searchResult = new ContentSearchResult { Id = "csv-generals-1.08-en" };
            searchResult.SetData(manifest);

            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResult]));

            var mockContentValidator = new Mock<IContentValidator>();
            mockContentValidator
                .Setup(c => c.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult("test", []));

            mockContentValidator
                .Setup(c => c.ValidateAllAsync(
                    It.IsAny<string>(),
                    It.IsAny<ContentManifest>(),
                    It.IsAny<IProgress<ValidationProgress>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(
                    "test",
                    [
                        new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Message = "Missing file 1", Severity = ValidationSeverity.Error },
                        new ValidationIssue { IssueType = ValidationIssueType.CorruptedFile, Message = "Corrupted file 1", Severity = ValidationSeverity.Error },
                        new ValidationIssue { IssueType = ValidationIssueType.MismatchedFileSize, Message = "Size mismatch", Severity = ValidationSeverity.Warning },
                        new ValidationIssue { IssueType = ValidationIssueType.UnexpectedFile, Message = "Extra file 1", Severity = ValidationSeverity.Warning },
                        new ValidationIssue { IssueType = ValidationIssueType.UnexpectedFile, Message = "Extra file 2", Severity = ValidationSeverity.Warning },
                    ]));

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                null,
                mockContentValidator.Object,
                _hashProviderMock.Object,
                null,
                null,
                [mockContentProvider.Object]);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation, CancellationToken.None);

            Assert.False(result.IsValid);
            Assert.Equal(3, result.TotalFilesValidated);
            Assert.Equal(1, result.MissingFilesCount);
            Assert.Equal(2, result.CorruptedFilesCount); // CorruptedFile + MismatchedFileSize
            Assert.Equal(2, result.ExtraFilesCount);
            Assert.Equal(2, result.CriticalIssueCount);
            Assert.Equal(3, result.WarningIssueCount);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that ValidateAsync reports validation unavailability when the CSV catalog has no manifest.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_WithNoCsvManifest_ReportsValidationUnavailableAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([]));

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                null,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                null,
                null,
                [mockContentProvider.Object]);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation, "EN");

            Assert.False(result.IsValid);
            var issue = Assert.Single(result.Issues);
            Assert.Equal(ValidationIssueType.ValidationUnavailable, issue.IssueType);
            Assert.Equal(0, result.MissingFilesCount);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that ValidateAsync reports catalog unavailability instead of a missing installation file when CSV provider search fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_WithCsvProviderFailure_ReturnsLanguageSpecificErrorMessageAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("Network timeout"));

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                null,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                null,
                null,
                [mockContentProvider.Object]);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation, "PL");

            Assert.False(result.IsValid);
            var issue = Assert.Single(result.Issues);
            Assert.Equal(ValidationIssueType.ValidationUnavailable, issue.IssueType);
            Assert.Equal(ValidationSeverity.Error, issue.Severity);
            Assert.Contains("PL", issue.Message);
            Assert.Contains("Network timeout", issue.Message);
            Assert.Equal(0, result.MissingFilesCount);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests multi-language normalization and support for all supported language codes.
    /// </summary>
    /// <param name="inputLanguage">The raw input language code.</param>
    /// <param name="expectedNormalized">The expected normalized uppercase language code.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("en", CsvConstants.LanguageEn)]
    [InlineData("de", CsvConstants.LanguageDe)]
    [InlineData("fr", CsvConstants.LanguageFr)]
    [InlineData("es", CsvConstants.LanguageEs)]
    [InlineData("it", CsvConstants.LanguageIt)]
    [InlineData("ko", CsvConstants.LanguageKo)]
    [InlineData("pl", CsvConstants.LanguagePl)]
    [InlineData("pt-br", CsvConstants.LanguagePtBr)]
    [InlineData("zh-cn", CsvConstants.LanguageZhCn)]
    [InlineData("zh-tw", CsvConstants.LanguageZhTw)]
    public async Task ValidateAsync_MultiLanguageSupport_NormalizesLanguageAndValidatesAsync(string inputLanguage, string expectedNormalized)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new ContentManifest
            {
                Id = new ManifestId($"csv-generals-1.08-{inputLanguage}"),
                Files = [new ManifestFile { RelativePath = "test.txt", Size = 10, Hash = "h" }],
            };

            var searchResult = new ContentSearchResult { Id = $"csv-generals-1.08-{inputLanguage}" };
            searchResult.SetData(manifest);

            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.Language == expectedNormalized), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResult]));

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                null,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                null,
                null,
                [mockContentProvider.Object]);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation, inputLanguage);

            Assert.True(result.IsValid);
            mockContentProvider.Verify(
                p => p.SearchAsync(It.Is<ContentSearchQuery>(q => q.Language == expectedNormalized), It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that ValidateAsync throws ArgumentNullException when installation is null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_NullInstallation_ThrowsArgumentNullExceptionAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _validator.ValidateAsync(null!, CancellationToken.None));
    }

    /// <summary>
    /// Tests that when CSV provider fails to find a manifest, fallback to IManifestProvider succeeds without retaining CSV failure issues.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_CsvFails_FallbackManifestProviderSucceeds_DoesNotPreserveCsvFailureAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var fallbackManifest = new ContentManifest
            {
                Id = new ManifestId("fallback-manifest"),
                Name = "Fallback Manifest",
                Version = "1.0",
                Files = [new ManifestFile { RelativePath = "test.big", Size = 50, Hash = "abc" }],
            };

            var mockManifestProvider = new Mock<IManifestProvider>();
            mockManifestProvider
                .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(fallbackManifest);

            var mockContentProvider = new Mock<IContentProvider>();
            mockContentProvider.Setup(p => p.SourceName).Returns(PublisherTypeConstants.CsvRegistry);
            mockContentProvider
                .Setup(p => p.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("Catalog not found"));

            _contentValidatorMock
                .Setup(c => c.ValidateAllAsync(It.IsAny<string>(), fallbackManifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult(tempDir.FullName, [], TimeSpan.FromSeconds(1), 1));

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                mockManifestProvider.Object,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                null,
                null,
                [mockContentProvider.Object]);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation);

            Assert.True(result.IsValid);
            Assert.Empty(result.Issues);
            Assert.Equal(1, result.TotalFilesValidated);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that when content validator throws an exception, TotalFilesValidated reports 0 rather than full manifest count.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ValidateAsync_ContentValidatorThrows_ReportsZeroTotalFilesValidatedAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var manifest = new ContentManifest
            {
                Id = new ManifestId("test-manifest"),
                Name = "Test Manifest",
                Files =
                [
                    new ManifestFile { RelativePath = "file1.big", Size = 10, Hash = "h1" },
                    new ManifestFile { RelativePath = "file2.big", Size = 20, Hash = "h2" },
                ],
            };

            var mockManifestProvider = new Mock<IManifestProvider>();
            mockManifestProvider
                .Setup(m => m.GetManifestAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(manifest);

            _contentValidatorMock
                .Setup(c => c.ValidateAllAsync(It.IsAny<string>(), manifest, It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Disk read error"));

            var validator = new GameInstallationValidator(
                _loggerMock.Object,
                mockManifestProvider.Object,
                _contentValidatorMock.Object,
                _hashProviderMock.Object,
                null,
                null,
                null);

            var installation = new GameInstallation(
                tempDir.FullName,
                GameInstallationType.Steam,
                new Mock<ILogger<GameInstallation>>().Object);
            installation.SetPaths(tempDir.FullName, null);

            var result = await validator.ValidateAsync(installation);

            Assert.False(result.IsValid);
            Assert.Equal(0, result.TotalFilesValidated);
            Assert.Contains(result.Issues, i => i.Message.Contains("Disk read error"));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Custom progress implementation that captures reports synchronously.
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly List<T> _reports = new();
        private readonly object _lock = new();

        public IReadOnlyList<T> GetReports()
        {
            lock (_lock)
            {
                return _reports.ToList();
            }
        }

        public void Report(T value)
        {
            lock (_lock)
            {
                _reports.Add(value);
            }
        }
    }
}
