using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Validation;

/// <summary>
/// Validates the integrity of a base game installation directory (e.g., from Steam, EA App).
/// Focuses on installation-specific validation concerns.
/// </summary>
public class GameInstallationValidator : FileSystemValidator, IGameInstallationValidator, IValidator<GameInstallation>
{
    private readonly ILogger<GameInstallationValidator> _logger;
    private readonly IManifestProvider _manifestProvider;
    private readonly IContentValidator _contentValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameInstallationValidator"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="manifestProvider">The manifest provider.</param>
    /// <param name="contentValidator">Content validator for core validation logic.</param>
    /// <param name="hashProvider">File hash provider for validation.</param>
    public GameInstallationValidator(
        ILogger<GameInstallationValidator> logger,
        IManifestProvider manifestProvider,
        IContentValidator contentValidator,
        IFileHashProvider hashProvider)
        : base(logger ?? throw new ArgumentNullException(nameof(logger)), hashProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
        _contentValidator = contentValidator ?? throw new ArgumentNullException(nameof(contentValidator));
    }

    /// <summary>
    /// Validates the specified game installation.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public async Task<ValidationResult> ValidateAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(installation, null, cancellationToken);
    }

    /// <summary>
    /// Validates the specified game installation with progress reporting.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public async Task<ValidationResult> ValidateAsync(GameInstallation installation, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting validation for installation '{Path}'", installation.InstallationPath);
        var issues = new List<ValidationIssue>();

        try
        {
            progress?.Report(new ValidationProgress(1, 6, "Fetching manifest"));

            // Fetch manifest for this installation type
            var manifest = await _manifestProvider.GetManifestAsync(installation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (manifest == null)
            {
                issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = installation.InstallationPath, Message = "Manifest not found for installation." });
                progress?.Report(new ValidationProgress(6, 6, "Validation complete"));
                return new ValidationResult(installation.InstallationPath, issues);
            }

            progress?.Report(new ValidationProgress(2, 6, "Core manifest validation"));

            // Use ContentValidator for core validation
            var manifestValidationResult = await _contentValidator.ValidateManifestAsync(manifest, cancellationToken);
            issues.AddRange(manifestValidationResult.Issues);

            progress?.Report(new ValidationProgress(3, 6, "Content integrity validation"));

            // Use ContentValidator for file integrity with proper exception handling
            try
            {
                var integrityValidationResult = await _contentValidator.ValidateContentIntegrityAsync(installation.InstallationPath, manifest, cancellationToken);
                issues.AddRange(integrityValidationResult.Issues);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Content integrity validation failed for installation {InstallationPath}", installation.InstallationPath);
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.CorruptedFile,
                    Severity = ValidationSeverity.Error,
                    Message = $"Content validation error: {ex.Message}",
                    Path = installation.InstallationPath,
                });
            }

            progress?.Report(new ValidationProgress(4, 6, "Detecting extraneous files"));

            try
            {
                var extraneousFilesResult = await _contentValidator.DetectExtraneousFilesAsync(installation.InstallationPath, manifest, cancellationToken);
                issues.AddRange(extraneousFilesResult.Issues);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Extraneous files detection failed for installation {InstallationPath}", installation.InstallationPath);
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.UnexpectedFile,
                    Severity = ValidationSeverity.Warning,
                    Message = $"Extraneous files detection error: {ex.Message}",
                    Path = installation.InstallationPath,
                });
            }

            progress?.Report(new ValidationProgress(5, 6, "Installation specific checks"));

            // Installation-specific validations (directories, etc.)
            var requiredDirs = manifest.RequiredDirectories ?? Enumerable.Empty<string>();
            if (requiredDirs.Any())
            {
                issues.AddRange(await ValidateDirectoriesAsync(installation.InstallationPath, requiredDirs, cancellationToken));
            }

            progress?.Report(new ValidationProgress(6, 6, "Validation complete"));

            _logger.LogInformation("Installation validation for '{Path}' completed with {Count} issues.", installation.InstallationPath, issues.Count);
            return new ValidationResult(installation.InstallationPath, issues);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for installation {InstallationPath}", installation.InstallationPath);
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.CorruptedFile,
                Severity = ValidationSeverity.Error,
                Message = $"Content validation error: {ex.Message}",
                Path = installation.InstallationPath,
            });
            return new ValidationResult(installation.InstallationPath, issues);
        }
    }
}
