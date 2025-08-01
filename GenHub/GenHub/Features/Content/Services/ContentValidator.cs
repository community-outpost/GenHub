using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Provides implementation for validating content manifests and their integrity.
/// Focuses specifically on content-related validation (manifests, files, dependencies).
/// </summary>
public class ContentValidator : IContentValidator, IValidator<GameManifest>
{
    private readonly IFileOperationsService _fileOperations;
    private readonly ILogger<ContentValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentValidator"/> class.
    /// </summary>
    /// <param name="fileOperations">File operations service.</param>
    /// <param name="logger">Logger instance.</param>
    public ContentValidator(IFileOperationsService fileOperations, ILogger<ContentValidator> logger)
    {
        _fileOperations = fileOperations;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateAsync(GameManifest manifest, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(manifest, null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateAsync(GameManifest manifest, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        // Step 1: Validate manifest structure
        progress?.Report(new ValidationProgress(0, 2, "Validating Manifest Structure"));
        issues.AddRange(ValidateManifestStructure(manifest));
        progress?.Report(new ValidationProgress(1, 2, "Manifest Structure Complete"));

        // Step 2: Validate content integrity
        progress?.Report(new ValidationProgress(1, 2, "Validating Content Integrity"));
        var integrityResult = await ValidateContentIntegrityAsync(string.Empty, manifest, cancellationToken); // Pass actual contentPath if available
        issues.AddRange(integrityResult.Issues);
        progress?.Report(new ValidationProgress(2, 2, "Content Integrity Complete"));

        _logger.LogDebug("Manifest validation for {ManifestId} completed with {IssueCount} issues.", manifest.Id, issues.Count);
        return new ValidationResult(manifest.Id, issues);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateManifestAsync(GameManifest manifest, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(manifest, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateContentIntegrityAsync(string contentPath, GameManifest manifest, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentPath))
            throw new ArgumentException("Content path cannot be null or empty.", nameof(contentPath));
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        var issues = new List<ValidationIssue>();
        var totalFiles = manifest.Files.Count;

        // Performance: Use parallel processing for large file sets
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
        var tasks = manifest.Files.Select(async file =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = Path.Combine(contentPath, file.RelativePath);
                var fileIssues = new List<ValidationIssue>();

                if (!File.Exists(filePath))
                {
                    fileIssues.Add(new ValidationIssue($"File not found: {file.RelativePath}", ValidationSeverity.Error));
                    return fileIssues;
                }

                if (!string.IsNullOrWhiteSpace(file.Hash))
                {
                    var isHashValid = await _fileOperations.VerifyFileHashAsync(filePath, file.Hash, cancellationToken);
                    if (!isHashValid)
                    {
                        fileIssues.Add(new ValidationIssue($"Hash mismatch for file: {file.RelativePath}", ValidationSeverity.Error));
                    }
                }

                return fileIssues;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var result in results)
        {
            issues.AddRange(result);
        }

        _logger.LogDebug("Content integrity validation for {ManifestId} completed with {IssueCount} issues.", manifest.Id, issues.Count);
        return new ValidationResult(manifest.Id, issues);
    }

    private static List<ValidationIssue> ValidateManifestStructure(GameManifest manifest)
    {
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            issues.Add(new ValidationIssue("Manifest Id is missing.", ValidationSeverity.Error));
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            issues.Add(new ValidationIssue("Manifest Name is missing.", ValidationSeverity.Error));
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            issues.Add(new ValidationIssue("Manifest Version is missing.", ValidationSeverity.Warning));
        }

        if (manifest.Files == null)
        {
            issues.Add(new ValidationIssue("Manifest Files collection is null.", ValidationSeverity.Error));
        }
        else if (manifest.Files.Count == 0)
        {
            issues.Add(new ValidationIssue("Manifest contains no files.", ValidationSeverity.Warning));
        }
        else
        {
            var fileIndex = 0;
            foreach (var file in manifest.Files)
            {
                if (file == null)
                {
                    issues.Add(new ValidationIssue($"File at index {fileIndex} is null.", ValidationSeverity.Error));
                }
                else if (string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    issues.Add(new ValidationIssue($"File at index {fileIndex} is missing its RelativePath.", ValidationSeverity.Error));
                }

                fileIndex++;
            }
        }

        return issues;
    }
}
