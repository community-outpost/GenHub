using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Features.Validation;

/// <summary>
/// Unit tests for <see cref="GameVersionValidator"/>.
/// </summary>
public class GameVersionValidatorTests
{
    private readonly Mock<ILogger<GameVersionValidator>> _loggerMock = new();
    private readonly Mock<IManifestProvider> _manifestProviderMock = new();
    private readonly GameVersionValidator _validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameVersionValidatorTests"/> class.
    /// </summary>
    public GameVersionValidatorTests()
    {
        _validator = new GameVersionValidator(_loggerMock.Object, _manifestProviderMock.Object);
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

        var manifest = new GameManifest
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
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameVersion>(), default)).ReturnsAsync(manifest);
        var version = new GameVersion { WorkingDirectory = tempDir.FullName };

        // Act
        var result = await _validator.ValidateAsync(version, null, default);

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

        var manifest = new GameManifest
        {
            Files = new()
            {
                new ManifestFile { RelativePath = "expected.txt", Size = 8, Hash = string.Empty },
            },
        };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameVersion>(), default)).ReturnsAsync(manifest);
        var version = new GameVersion { WorkingDirectory = tempDir.FullName };

        // Act
        var result = await _validator.ValidateAsync(version, null, default);

        // Assert
        Assert.True(result.IsValid); // UnexpectedFile does not make result invalid
        var relPath = Path.GetRelativePath(tempDir.FullName, unexpectedFilePath).Replace('\\', '/');
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
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameVersion>(), It.IsAny<CancellationToken>())).ReturnsAsync((GameManifest?)null);
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var version = new GameVersion { WorkingDirectory = tempDir.FullName };
            var result = await _validator.ValidateAsync(version, null, default);
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
        var manifest = new GameManifest { Files = new() { new ManifestFile { RelativePath = "missing.txt", Size = 0, Hash = string.Empty } } };
        _manifestProviderMock.Setup(m => m.GetManifestAsync(It.IsAny<GameVersion>(), default)).ReturnsAsync(manifest);
        var tempDir = Directory.GetCurrentDirectory();
        var version = new GameVersion { WorkingDirectory = tempDir };
        var result = await _validator.ValidateAsync(version, null, default);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.IssueType == ValidationIssueType.MissingFile);
    }

    /// <summary>
    /// Verifies that cancellation during validation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateAsync_Cancellation_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var version = new GameVersion { WorkingDirectory = "path" };
        await Assert.ThrowsAsync<OperationCanceledException>(() => _validator.ValidateAsync(version, cts.Token));
    }
}
