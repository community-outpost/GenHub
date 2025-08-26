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
public class ContentValidator : IContentValidator, IValidator<ContentManifest>
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
    public Task<ValidationResult> ValidateAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(manifest, null, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateAsync(ContentManifest manifest, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        // Step 1: Validate manifest structure
        progress?.Report(new ValidationProgress(0, 1, "Validating Manifest Structure"));
        issues.AddRange(ValidateManifestStructure(manifest));
        progress?.Report(new ValidationProgress(1, 1, "Manifest Structure Complete"));

        _logger.LogDebug("Manifest validation for {ManifestId} completed with {IssueCount} issues.", manifest.Id, issues.Count);
        return Task.FromResult(new ValidationResult(manifest.Id, issues));
    }

    /// <summary>
    /// Performs full validation including manifest structure, file integrity and extraneous file detection.
    /// </summary>
    /// <param name="contentPath">Path to the content directory to validate.</param>
    /// <param name="manifest">The manifest to validate against.</param>
    /// <param name="progress">Optional progress reporter for validation phases.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregated <see cref="ValidationResult"/> for the manifest and files.</returns>
    public async Task<ValidationResult> ValidateAllAsync(string contentPath, ContentManifest manifest, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentPath))
        {
            throw new ArgumentException("Content path cannot be null or empty.", nameof(contentPath));
        }

        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var issues = new List<ValidationIssue>();

        // Step 1: Manifest structure
        progress?.Report(new ValidationProgress(0, 3, "Validating Manifest Structure"));
        issues.AddRange(ValidateManifestStructure(manifest));
        progress?.Report(new ValidationProgress(1, 3, "Manifest Structure Complete"));

        // Step 2: Content integrity
        progress?.Report(new ValidationProgress(1, 3, "Validating Content Integrity"));
        var integrityResult = await ValidateContentIntegrityAsync(contentPath, manifest, cancellationToken);
        issues.AddRange(integrityResult.Issues);
        progress?.Report(new ValidationProgress(2, 3, "Content Integrity Complete"));

        // Step 3: Extraneous files
        progress?.Report(new ValidationProgress(2, 3, "Detecting Extraneous Files"));
        var extraneousResult = await DetectExtraneousFilesAsync(contentPath, manifest, cancellationToken);
        issues.AddRange(extraneousResult.Issues);

        progress?.Report(new ValidationProgress(3, 3, "Validation Complete"));

        _logger.LogDebug("Full content validation for {ManifestId} completed with {IssueCount} issues.", manifest.Id, issues.Count);
        return new ValidationResult(manifest.Id, issues);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(manifest, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateContentIntegrityAsync(string contentPath, ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentPath))
        {
            throw new ArgumentException("Content path cannot be null or empty.", nameof(contentPath));
        }

        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

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
                        fileIssues.Add(new ValidationIssue($"Hash mismatch for file: {file.RelativePath}", ValidationSeverity.Warning)); // xezon:' File validation is probably fine by just names, and just warn if hash is mismatching.' - on discord 22:20 01/08/2025
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

    /// <inheritdoc/>
    public async Task<ValidationResult> DetectExtraneousFilesAsync(string contentPath, ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentPath))
        {
            throw new ArgumentException("Content path cannot be null or empty.", nameof(contentPath));
        }

        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var issues = new List<ValidationIssue>();

        if (!Directory.Exists(contentPath))
        {
            issues.Add(new ValidationIssue($"Content directory does not exist: {contentPath}", ValidationSeverity.Error));
            return new ValidationResult(manifest.Id, issues);
        }

        try
        {
            // Build a hashset of expected file paths for O(1) lookup performance
            var expectedFiles = new HashSet<string>(
                manifest.Files.Select(f => Path.GetFullPath(Path.Combine(contentPath, f.RelativePath))),
                StringComparer.OrdinalIgnoreCase);

            // Add expected directories if specified in manifest
            var expectedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (manifest.RequiredDirectories != null)
            {
                foreach (var dir in manifest.RequiredDirectories)
                {
                    expectedDirectories.Add(Path.GetFullPath(Path.Combine(contentPath, dir)));
                }
            }

            // Recursively scan all files in the content directory
            var allFiles = Directory.GetFiles(contentPath, "*", SearchOption.AllDirectories);
            var extraneousFiles = new List<string>();

            await Task.Run(
                () =>
                {
                    foreach (var file in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var fullPath = Path.GetFullPath(file);
                        if (!expectedFiles.Contains(fullPath))
                        {
                            // Check if file is in an expected directory (some files might be allowed in certain dirs)
                            var isInExpectedDirectory = expectedDirectories.Any(dir =>
                                fullPath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

                            if (!isInExpectedDirectory)
                            {
                                extraneousFiles.Add(Path.GetRelativePath(contentPath, file));
                            }
                        }
                    }
                },
                cancellationToken);

            // Report extraneous files as warnings (they don't break functionality but indicate potential issues)
            foreach (var extraneousFile in extraneousFiles)
            {
                issues.Add(new ValidationIssue(
                    $"Extraneous file detected (not in manifest): {extraneousFile}",
                    ValidationSeverity.Warning));
            }

            _logger.LogDebug("Extraneous file detection for {ManifestId} found {ExtraneousCount} files.", manifest.Id, extraneousFiles.Count);
        }
        catch (UnauthorizedAccessException ex)
        {
            issues.Add(new ValidationIssue($"Access denied while scanning directory: {ex.Message}", ValidationSeverity.Error));
            _logger.LogError(ex, "Access denied while scanning {ContentPath} for extraneous files", contentPath);
        }
        catch (DirectoryNotFoundException ex)
        {
            issues.Add(new ValidationIssue($"Directory not found: {ex.Message}", ValidationSeverity.Error));
            _logger.LogError(ex, "Directory not found while scanning {ContentPath} for extraneous files", contentPath);
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue($"Unexpected error during extraneous file detection: {ex.Message}", ValidationSeverity.Error));
            _logger.LogError(ex, "Unexpected error while scanning {ContentPath} for extraneous files", contentPath);
        }

        return new ValidationResult(manifest.Id, issues);
    }

    private static List<ValidationIssue> ValidateManifestStructure(ContentManifest manifest)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

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
