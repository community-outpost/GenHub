using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Provides implementation for validating content manifests and their integrity.
/// Focuses specifically on content-related validation (manifests, files, dependencies).
/// </summary>
public class ContentValidator(IFileOperationsService fileOperations, ICasService casService, ILogger<ContentValidator> logger) : IContentValidator, IValidator<ContentManifest>
{
    private readonly IFileOperationsService _fileOperations = fileOperations;
    private readonly ICasService _casService = casService;
    private readonly ILogger<ContentValidator> _logger = logger;

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

        ArgumentNullException.ThrowIfNull(manifest);

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

        // Step 3: Extraneous files. Without a host variant there is no expected file set,
        // and the integrity step has already reported the unsupported host.
        if (ManifestVariantResolver.SupportsRuntime(manifest))
        {
            progress?.Report(new ValidationProgress(2, 3, "Detecting Extraneous Files"));
            var extraneousResult = await DetectExtraneousFilesAsync(contentPath, manifest, cancellationToken);
            issues.AddRange(extraneousResult.Issues);
        }

        progress?.Report(new ValidationProgress(3, 3, "Validation Complete"));

        _logger.LogDebug("Full content validation for {ManifestId} completed with {IssueCount} issues.", manifest.Id, issues.Count);
        return new ValidationResult(manifest.Id, issues, totalFilesValidated: integrityResult.TotalFilesValidated);
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

        ArgumentNullException.ThrowIfNull(manifest);

        cancellationToken.ThrowIfCancellationRequested();
        if (!ManifestVariantResolver.SupportsRuntime(manifest))
        {
            return CreateUnsupportedHostResult(manifest);
        }

        var issues = new List<ValidationIssue>();
        var files = ManifestVariantResolver.ResolveFiles(manifest);
        var totalFiles = files.Count;

        // Performance: Use parallel processing for large file sets
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileIssues = new List<ValidationIssue>();
                if (string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    fileIssues.Add(new ValidationIssue(ManifestErrorMessages.ManifestFileMissingRelativePath, ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
                    return fileIssues;
                }

                var fullContentRoot = Path.GetFullPath(contentPath);
                var resolvedFilePath = Path.GetFullPath(Path.Combine(fullContentRoot, file.RelativePath));
                var relativePath = Path.GetRelativePath(fullContentRoot, resolvedFilePath);
                if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
                {
                    fileIssues.Add(new ValidationIssue($"Invalid file path (outside content directory): {file.RelativePath}", ValidationSeverity.Error));
                    return fileIssues;
                }

                // Check file existence based on source type
                bool isMaterializedLocally = File.Exists(resolvedFilePath);
                bool fileExists = false;

                if (isMaterializedLocally)
                {
                    fileExists = true;
                }
                else if (file.SourceType == ContentSourceType.ContentAddressable)
                {
                    if (string.IsNullOrWhiteSpace(file.Hash))
                    {
                        fileIssues.Add(new ValidationIssue($"ContentAddressable file missing hash: {file.RelativePath}", ValidationSeverity.Error));
                        return fileIssues;
                    }

                    var casExistsResult = await _casService.ExistsAsync(file.Hash, cancellationToken);
                    if (!casExistsResult.Success)
                    {
                        fileIssues.Add(new ValidationIssue($"CAS check failed for hash {file.Hash}: {casExistsResult.FirstError}", ValidationSeverity.Error));
                        return fileIssues;
                    }

                    fileExists = casExistsResult.Data;
                }
                else
                {
                    fileExists = false;
                }

                if (!fileExists)
                {
                    fileIssues.Add(new ValidationIssue($"File not found: {file.RelativePath}", ValidationSeverity.Error));
                    return fileIssues;
                }

                // If file is materialized on disk, verify hash if hash is declared
                if (isMaterializedLocally && !string.IsNullOrWhiteSpace(file.Hash))
                {
                    var isHashValid = await _fileOperations.VerifyFileHashAsync(resolvedFilePath, file.Hash, cancellationToken);
                    if (!isHashValid)
                    {
                        fileIssues.Add(new ValidationIssue($"Hash mismatch for file: {file.RelativePath}", ValidationSeverity.Warning));
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
        return new ValidationResult(manifest.Id, issues, totalFilesValidated: totalFiles);
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> DetectExtraneousFilesAsync(string contentPath, ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentPath))
        {
            throw new ArgumentException("Content path cannot be null or empty.", nameof(contentPath));
        }

        ArgumentNullException.ThrowIfNull(manifest);

        if (!ManifestVariantResolver.SupportsRuntime(manifest))
        {
            return CreateUnsupportedHostResult(manifest);
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
                ManifestVariantResolver.ResolveFiles(manifest).Where(f => !string.IsNullOrWhiteSpace(f.RelativePath)).Select(f => Path.GetFullPath(Path.Combine(contentPath, f.RelativePath))),
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
                    ValidationSeverity.Warning)
                {
                    // Typed and pathed so consumers can recognise these structurally;
                    // the untyped form defaulted to MissingFile, the opposite of what
                    // an extra file is.
                    IssueType = ValidationIssueType.UnexpectedFile,
                    Path = extraneousFile,
                });
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

    private static ValidationResult CreateUnsupportedHostResult(ContentManifest manifest) =>
        new(
            manifest.Id,
            [
                new ValidationIssue(string.Format(CultureInfo.InvariantCulture, ManifestErrorMessages.NoHostVariantForValidation, ManifestVariantResolver.CurrentRuntimeIdentifier), ValidationSeverity.Error)
                {
                    IssueType = ValidationIssueType.ValidationUnavailable,
                },
            ]);

    private static List<ValidationIssue> ValidateManifestStructure(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            issues.Add(new ValidationIssue("Manifest Id is missing.", ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
        }

        // Enforce deterministic ID scheme using centralized validator
        if (!string.IsNullOrWhiteSpace(manifest.Id) && !ManifestIdValidator.IsValid(manifest.Id, out var idReason))
        {
            issues.Add(new ValidationIssue(idReason, ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            issues.Add(new ValidationIssue("Manifest Name is missing.", ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            issues.Add(new ValidationIssue("Manifest Version is missing.", ValidationSeverity.Warning) { IssueType = ValidationIssueType.InvalidManifest });
        }

        AddFileStructureIssues(ManifestVariantResolver.GetDeclaredFileLists(manifest)[0], string.Empty, issues);
        if (manifest.Variants.Count > 0)
        {
            for (var variantIndex = 0; variantIndex < manifest.Variants.Count; variantIndex++)
            {
                var variant = manifest.Variants[variantIndex];
                if (variant == null)
                {
                    issues.Add(new ValidationIssue(string.Format(CultureInfo.InvariantCulture, ManifestErrorMessages.VariantIsNull, variantIndex), ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
                    continue;
                }

                AddFileStructureIssues(variant.Files, $" in variant {variantIndex}", issues);
            }
        }

        if (!ManifestVariantResolver.EnumerateAllFiles(manifest).Any())
        {
            issues.Add(new ValidationIssue("Manifest contains no files.", ValidationSeverity.Warning) { IssueType = ValidationIssueType.InvalidManifest });
        }

        return issues;
    }

    private static void AddFileStructureIssues(IReadOnlyList<ManifestFile>? files, string location, List<ValidationIssue> issues)
    {
        if (files is null)
        {
            issues.Add(new ValidationIssue(string.Format(CultureInfo.InvariantCulture, ManifestErrorMessages.FileCollectionIsNull, location), ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
            return;
        }

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            var file = files[fileIndex];
            if (file == null)
            {
                issues.Add(new ValidationIssue(string.Format(CultureInfo.InvariantCulture, ManifestErrorMessages.FileEntryIsNull, fileIndex, location), ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
            }
            else if (string.IsNullOrWhiteSpace(file.RelativePath))
            {
                issues.Add(new ValidationIssue(string.Format(CultureInfo.InvariantCulture, ManifestErrorMessages.FileEntryMissingRelativePath, fileIndex, location), ValidationSeverity.Error) { IssueType = ValidationIssueType.InvalidManifest });
            }
        }
    }
}
