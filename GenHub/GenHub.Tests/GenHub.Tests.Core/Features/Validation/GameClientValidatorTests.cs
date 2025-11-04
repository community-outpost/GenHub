using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;

using GenHub.Features.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Features.Validation;

/// <summary>
/// Unit tests for <see cref="GameClientValidator"/>.
/// </summary>
public class GameClientValidatorTests
{
    /// <summary>
    /// Synchronous progress implementation for testing to avoid SynchronizationContext issues.
    /// </summary>
    /// <typeparam name="T">The type of progress value.</typeparam>
    private class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;

        public SynchronousProgress(Action<T> action) => _action = action ?? throw new ArgumentNullException(nameof(action));

        public void Report(T value) => _action(value);
    }

    private readonly Mock<ILogger<GameClientValidator>> _loggerMock;
    private readonly Mock<IManifestProvider> _manifestProviderMock;
    private readonly Mock<IContentValidator> _contentValidatorMock;
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly GameClientValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameClientValidatorTests"/> class.
    /// </summary>
    public GameClientValidatorTests()
    {
        _loggerMock = new Mock<ILogger<GameClientValidator>>();
        _manifestProviderMock = new Mock<IManifestProvider>();
        _contentValidatorMock = new Mock<IContentValidator>();
        _hashProviderMock = new Mock<IFileHashProvider>();

        // Setup ContentValidator mocks to return valid results
        _contentValidatorMock.Setup(c => c.ValidateManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        // Setup ValidateContentIntegrityAsync mock
        _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        // Use unified ValidateAllAsync for full validation
        _contentValidatorMock.Setup(c => c.ValidateAllAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<IProgress<ValidationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        _validator = new GameClientValidator(_loggerMock.Object, _manifestProviderMock.Object, _contentValidatorMock.Object, _hashProviderMock.Object);
    }

    /// <summary>
    /// Verifies that KnownAddons in the manifest are detected as warnings.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithKnownAddonInManifest_DetectsAddonAsWarning()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var addonFilePath = Path.Combine(tempDir.FullName, "d3d8.dll");
        await File.WriteAllTextAsync(addonFilePath, "addon content");

        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "d3d8.dll", Size = 13, Hash = string.Empty },
            },
            KnownAddons = new()
            {
                "d3d8.dll",
            },
        };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), default)).ReturnsAsync(manifest);
        var client = new GameClient { WorkingDirectory = tempDir.FullName };

        // Act
        var result = await _validator.ValidateAsync(client, null, default);

        // Assert
        Assert.True(result.IsValid); // Warnings don't make it invalid
        var addonIssue = result.Issues.FirstOrDefault(i => i.IssueType == ValidationIssueType.AddonDetected);
        Assert.NotNull(addonIssue);
        Assert.Equal(ValidationSeverity.Warning, addonIssue.Severity);
        Assert.Contains("d3d8.dll", addonIssue.Message);

        tempDir.Delete(true);
    }

    /// <summary>
    /// Verifies that unexpected files are detected as issues.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithUnexpectedFile_DetectsUnexpectedFile()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var expectedFilePath = Path.Combine(tempDir.FullName, "expected.txt");
        var unexpectedFilePath = Path.Combine(tempDir.FullName, "unexpected.txt");
        await File.WriteAllTextAsync(expectedFilePath, "expected");
        await File.WriteAllTextAsync(unexpectedFilePath, "unexpected");

        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "expected.txt", Size = 8, Hash = string.Empty },
            },
        };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), default)).ReturnsAsync(manifest);
        var client = new GameClient { WorkingDirectory = tempDir.FullName };

        // Act
        var result = await _validator.ValidateAsync(client, null, default);

        // Assert
        Assert.True(result.IsValid); // UnexpectedFile does not make result invalid
        var relPath = Path.GetRelativePath(tempDir.FullName, unexpectedFilePath).Replace(Path.DirectorySeparatorChar, '/');
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.UnexpectedFile && i.Path == relPath);

        tempDir.Delete(true);
    }

    /// <summary>
    /// Verifies that a missing manifest results in a <see cref="ValidationIssueType.MissingFile"/> issue.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_ManifestNotFound_AddsIssue()
    {
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), It.IsAny<CancellationToken>())).ReturnsAsync((ContentManifest?)null);
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var client = new GameClient { WorkingDirectory = tempDir.FullName };
            var result = await _validator.ValidateAsync(client, null, default);
            Assert.False(result.IsValid);
            Assert.Single(result.Issues);
            Assert.Equal(ValidationIssueType.MissingFile, result.Issues[0].IssueType);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Verifies that a missing file in the manifest results in a <see cref="ValidationIssueType.MissingFile"/> issue.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_MissingFile_AddsMissingFileIssue()
    {
        var manifest = new ContentManifest { Files = new() { new ManifestFile { RelativePath = "missing.txt", Size = 0, Hash = string.Empty } } };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), default)).ReturnsAsync(manifest);

        // Setup ContentValidator to return missing file issue
        _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
            {
                new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = "missing.txt", Message = "File not found" },
            }));

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var client = new GameClient { WorkingDirectory = tempDir.FullName };
            var result = await _validator.ValidateAsync(client, null, default);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.MissingFile);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Verifies that cancellation during validation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var client = new GameClient { WorkingDirectory = tempDir.FullName };
        await Assert.ThrowsAsync<OperationCanceledException>(() => _validator.ValidateAsync(client, cancellationToken: cts.Token));
        tempDir.Delete(true);
    }

    /// <summary>
    /// Verifies that multiple addon files are all detected.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithMultipleKnownAddons_DetectsAllAddons()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var addon1Path = Path.Combine(tempDir.FullName, "d3d8.dll");
        var addon2Path = Path.Combine(tempDir.FullName, "ddraw.dll");
        await File.WriteAllTextAsync(addon1Path, "addon1 content");
        await File.WriteAllTextAsync(addon2Path, "addon2 content");

        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "d3d8.dll", Size = 14, Hash = string.Empty },
                new ManifestFile { RelativePath = "ddraw.dll", Size = 14, Hash = string.Empty },
            },
            KnownAddons = new()
            {
                "d3d8.dll",
                "ddraw.dll",
            },
        };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), default)).ReturnsAsync(manifest);
        var client = new GameClient { WorkingDirectory = tempDir.FullName };

        // Act
        var result = await _validator.ValidateAsync(client, null, default);

        // Assert
        Assert.True(result.IsValid);
        var addonIssues = result.Issues.Where(i => i.IssueType == ValidationIssueType.AddonDetected).ToList();
        Assert.Equal(2, addonIssues.Count);
        Assert.Contains(addonIssues, i => i.Message.Contains("d3d8.dll"));
        Assert.Contains(addonIssues, i => i.Message.Contains("ddraw.dll"));

        tempDir.Delete(true);
    }

    /// <summary>
    /// Verifies that validation with progress callback reports progress.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithProgressCallback_ReportsProgress()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var filePath = Path.Combine(tempDir.FullName, "test.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "test.txt", Size = 12, Hash = string.Empty },
            },
        };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), default)).ReturnsAsync(manifest);
        var client = new GameClient { WorkingDirectory = tempDir.FullName };

        object lockObj = new();
        var progressReports = new List<ValidationProgress>();
        var progress = new SynchronousProgress<ValidationProgress>(p => progressReports.Add(p));

        // Act
        var result = await _validator.ValidateAsync(client, progress, default);

        // Assert
        Assert.True(result.IsValid);
        lock (lockObj)
        {
            Assert.NotEmpty(progressReports);
        }

        lock (lockObj)
        {
            Assert.Contains(progressReports, p => p.PercentComplete == 100);
        }

        tempDir.Delete(true);
    }

    /// <summary>
    /// Verifies that hash mismatch is detected as corruption.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithHashMismatch_DetectsCorruption()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var filePath = Path.Combine(tempDir.FullName, "corrupted.txt");
        await File.WriteAllTextAsync(filePath, "corrupted content");

        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "corrupted.txt", Size = 17, Hash = "expected-hash" },
            },
        };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), default)).ReturnsAsync(manifest);

        // Setup ContentValidator to return corruption issue
        _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
            {
                new ValidationIssue { IssueType = ValidationIssueType.CorruptedFile, Path = "corrupted.txt", Message = "Hash mismatch" },
            }));

        var client = new GameClient { WorkingDirectory = tempDir.FullName };

        // Act
        var result = await _validator.ValidateAsync(client, null, default);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.CorruptedFile);

        tempDir.Delete(true);
    }

    /// <summary>
    /// Verifies that validation works with empty directory.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_WithEmptyDirectory_HandlesGracefully()
    {
        // Arrange
        var tempDir = Directory.CreateTempSubdirectory();
        var manifest = new ContentManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "missing.txt", Size = 0, Hash = string.Empty },
            },
        };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameClient>(), default)).ReturnsAsync(manifest);

        // Setup ContentValidator to return missing file issue
        _contentValidatorMock.Setup(c => c.ValidateContentIntegrityAsync(It.IsAny<string>(), It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>
            {
                new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = "missing.txt", Message = "File not found" },
            }));

        var client = new GameClient { WorkingDirectory = tempDir.FullName };

        // Act
        var result = await _validator.ValidateAsync(client, null, default);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.MissingFile);

        tempDir.Delete(true);
    }
}
