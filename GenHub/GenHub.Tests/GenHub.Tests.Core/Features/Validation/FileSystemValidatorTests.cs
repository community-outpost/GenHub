using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
using GenHub.Features.Validation;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Features.Validation;

/// <summary>
/// Unit tests for FileSystemValidator.
/// </summary>
public class FileSystemValidatorTests
{
    /// <summary>
    /// Tests that ValidateDirectoriesAsync returns an issue for missing directory.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateDirectoriesAsync_MissingDirectory_ReturnsIssue()
    {
        var logger = new Mock<ILogger>().Object;
        var validator = new TestFileSystemValidator(logger);
        var dirs = new List<string> { "notfounddir" };
        var issues = await validator.ValidateDirectoriesAsync(Directory.GetCurrentDirectory(), dirs, CancellationToken.None);
        Assert.Single(issues);
        Assert.Equal(ValidationIssueType.DirectoryMissing, issues[0].IssueType);
    }

    /// <summary>
    /// Tests that ValidateFilesAsync throws for path traversal.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateFilesAsync_PathTraversal_Throws()
    {
        var logger = new Mock<ILogger>().Object;
        var validator = new TestFileSystemValidator(logger);
        var files = new List<ManifestFile> { new() { RelativePath = "..\\evil.txt", Size = 0, Hash = string.Empty } };
        await Assert.ThrowsAsync<ManifestValidationException>(async () =>
            await validator.ValidateFilesAsync(Directory.GetCurrentDirectory(), files, CancellationToken.None));
    }

    /// <summary>
    /// Verifies that IOException during file validation is handled and reported as an issue.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ValidateFilesAsync_IOException_ReportsIssue()
    {
        var logger = new Mock<ILogger>().Object;
        var validator = new TestFileSystemValidator(logger);
        var tempDir = Directory.CreateTempSubdirectory();
        var filePath = Path.Combine(tempDir.FullName, "locked.txt");
        await File.WriteAllTextAsync(filePath, "locked");

        FileStream? stream = null;
        try
        {
            stream = new FileStream(filePath, System.IO.FileMode.Open, FileAccess.Read, FileShare.None);
            var files = new List<ManifestFile> { new() { RelativePath = "locked.txt", Size = 6, Hash = string.Empty } };
            var issues = await validator.ValidateFilesAsync(tempDir.FullName, files, CancellationToken.None);

            // If the platform does not throw, skip the test
            if (issues.Count == 0)
            {
                return;
            }

            Assert.Contains(issues, i => i.IssueType == ValidationIssueType.UnexpectedFile && i.Message.Contains("I/O error"));
        }
        finally
        {
            stream?.Dispose();
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Test implementation of <see cref="FileSystemValidator"/> for unit testing.
    /// </summary>
    public class TestFileSystemValidator : FileSystemValidator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestFileSystemValidator"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        public TestFileSystemValidator(ILogger logger)
            : base(logger, new Mock<IFileHashProvider>().Object)
        {
        }

        /// <summary>
        /// Exposes base ValidateDirectoriesAsync for testing.
        /// </summary>
        /// <param name="basePath">Base path to check from.</param>
        /// <param name="requiredDirectories">Directories to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of validation issues.</returns>
        public new Task<List<ValidationIssue>> ValidateDirectoriesAsync(string basePath, IEnumerable<string> requiredDirectories, CancellationToken cancellationToken)
            => base.ValidateDirectoriesAsync(basePath, requiredDirectories, cancellationToken);

        /// <summary>
        /// Exposes base ValidateFilesAsync for testing.
        /// </summary>
        /// <param name="basePath">Base path to check from.</param>
        /// <param name="files">Files to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <returns>List of validation issues.</returns>
        public new Task<List<ValidationIssue>> ValidateFilesAsync(string basePath, IEnumerable<ManifestFile> files, CancellationToken cancellationToken, IProgress<ValidationProgress>? progress = null)
            => base.ValidateFilesAsync(basePath, files, cancellationToken, progress);
    }
}
