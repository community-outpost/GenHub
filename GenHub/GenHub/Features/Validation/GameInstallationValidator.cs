using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public GameInstallationValidator(
        ILogger<GameInstallationValidator> logger,
        IManifestProvider manifestProvider,
        IContentValidator contentValidator)
        : base(logger ?? throw new ArgumentNullException(nameof(logger)))
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

        // Calculate total steps dynamically based on installation
        int totalSteps = 4; // Base steps: manifest fetch, manifest validation, integrity, extraneous files
        if (installation.HasGenerals) totalSteps++;
        if (installation.HasZeroHour) totalSteps++;

        int currentStep = 0;

        progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Fetching manifest"));

        // Fetch manifest for this installation type
        var manifest = await _manifestProvider.GetManifestAsync(installation, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (manifest == null)
        {
            issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = installation.InstallationPath, Message = "Manifest not found for installation." });
            progress?.Report(new ValidationProgress(totalSteps, totalSteps, "Validation complete"));
            return new ValidationResult(installation.InstallationPath, issues);
        }

        progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Core manifest validation"));

        // Use ContentValidator for core validation
        var manifestValidationResult = await _contentValidator.ValidateManifestAsync(manifest, cancellationToken);
        issues.AddRange(manifestValidationResult.Issues);

        progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Validating content files"));

        // Use ContentValidator for full content validation (integrity + extraneous files)
        var fullValidation = await _contentValidator.ValidateAllAsync(installation.InstallationPath, manifest, progress, cancellationToken);
        issues.AddRange(fullValidation.Issues);

        // Installation-specific validations (directories, etc.)
        var requiredDirs = manifest.RequiredDirectories ?? Enumerable.Empty<string>();
        if (requiredDirs.Any())
        {
            if (installation.HasGenerals)
            {
                progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Validating Generals directories"));
                issues.AddRange(await ValidateDirectoriesAsync(installation.GeneralsPath, requiredDirs, cancellationToken));
            }

            if (installation.HasZeroHour)
            {
                progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Validating Zero Hour directories"));
                issues.AddRange(await ValidateDirectoriesAsync(installation.ZeroHourPath, requiredDirs, cancellationToken));
            }
        }

        progress?.Report(new ValidationProgress(totalSteps, totalSteps, "Validation complete"));

        _logger.LogInformation("Installation validation for '{Path}' completed with {Count} issues.", installation.InstallationPath, issues.Count);
        return new ValidationResult(installation.InstallationPath, issues);
    }
}
