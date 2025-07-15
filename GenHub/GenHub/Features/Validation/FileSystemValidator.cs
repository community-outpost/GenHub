using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Validation;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Validation;

/// <summary>
/// Base class for file system validation logic, providing directory and file checks, hashing, and security.
/// </summary>
public abstract class FileSystemValidator
{
    /// <summary>
    /// Logger for validation events.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemValidator"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    protected FileSystemValidator(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Computes the SHA256 hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">File path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SHA256 hash string.</returns>
    protected static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    /// Validates that all required directories exist.
    /// </summary>
    /// <param name="basePath">Base path to check from.</param>
    /// <param name="requiredDirectories">Directories to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of validation issues.</returns>
    protected Task<List<ValidationIssue>> ValidateDirectoriesAsync(string basePath, IEnumerable<string> requiredDirectories, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        foreach (var dir in requiredDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absDir = Path.Combine(basePath, dir);
            if (!Directory.Exists(absDir))
            {
                issues.Add(new ValidationIssue { IssueType = ValidationIssueType.DirectoryMissing, Path = absDir, Message = $"Required directory missing: {dir}" });
            }
        }

        return Task.FromResult(issues);
    }

    /// <summary>
    /// Validates files for existence, hash, and security.
    /// </summary>
    /// <param name="basePath">Base path to check from.</param>
    /// <param name="files">Files to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <returns>List of validation issues.</returns>
    protected async Task<List<ValidationIssue>> ValidateFilesAsync(
        string basePath,
        IEnumerable<ManifestFile> files,
        CancellationToken cancellationToken,
        IProgress<ValidationProgress>? progress = null)
    {
        var issues = new System.Collections.Concurrent.ConcurrentBag<ValidationIssue>();
        int totalFiles = files is ICollection<ManifestFile> coll ? coll.Count : files.Count();
        int processed = 0;

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2), CancellationToken = cancellationToken },
            async (file, ct) =>
            {
                if (file.RelativePath.Contains("..") || Path.IsPathRooted(file.RelativePath))
                {
                    throw new ManifestValidationException("Invalid path detected: potential traversal attack.");
                }

                var absFile = Path.Combine(basePath, file.RelativePath);
                if (!File.Exists(absFile))
                {
                    issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = absFile, Message = $"Required file missing: {file.RelativePath}" });
                    return;
                }

                try
                {
                    var fileInfo = new FileInfo(absFile);
                    if (fileInfo.Length != file.Size)
                    {
                        issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MismatchedFileSize, Path = absFile, Message = $"File size mismatch for {file.RelativePath}", Expected = file.Size.ToString(), Actual = fileInfo.Length.ToString() });
                    }

                    if (!string.IsNullOrEmpty(file.Hash))
                    {
                        var actualHash = await ComputeSha256Async(absFile, ct);
                        if (!string.Equals(actualHash, file.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add(new ValidationIssue { IssueType = ValidationIssueType.CorruptedFile, Path = absFile, Message = $"Hash mismatch for {file.RelativePath}", Expected = file.Hash, Actual = actualHash });
                        }
                    }
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "I/O error validating file {FilePath}", absFile);
                    issues.Add(new ValidationIssue { IssueType = ValidationIssueType.UnexpectedFile, Path = absFile, Message = $"I/O error: {ex.Message}" });
                }

                // Progress reporting for MVVM
                if (progress != null)
                {
                    int current = Interlocked.Increment(ref processed);
                    progress.Report(new ValidationProgress(current, totalFiles, absFile));
                }
            });

        return issues.ToList();
    }
}
