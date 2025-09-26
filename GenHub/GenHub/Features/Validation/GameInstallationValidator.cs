using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Validation;

/// <summary>
/// Validates the integrity of a game installation directory (e.g., from Steam, EA App).
/// Focuses on installation-specific validation concerns.
/// </summary>
public class GameInstallationValidator(
    ILogger<GameInstallationValidator> logger,
    IManifestProvider manifestProvider,
    IContentValidator contentValidator,
    IFileHashProvider hashProvider,
    IContentDiscoverer? csvDiscoverer = null,
    IContentResolver? csvResolver = null,
    HttpClient? httpClient = null)
    : FileSystemValidator(logger ?? throw new ArgumentNullException(nameof(logger)), hashProvider ?? throw new ArgumentNullException(nameof(hashProvider))),
      IGameInstallationValidator, IValidator<GameInstallation>
{
    private readonly ILogger<GameInstallationValidator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IManifestProvider _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
    private readonly IContentValidator _contentValidator = contentValidator ?? throw new ArgumentNullException(nameof(contentValidator));
    private readonly IFileHashProvider _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
    private readonly IContentDiscoverer? _csvDiscoverer = csvDiscoverer;
    private readonly IContentResolver? _csvResolver = csvResolver;
    private readonly HttpClient? _httpClient = httpClient;

    /// <summary>
    /// Attempts to validate the installation using CSV-based content discovery and resolution.
    /// This serves as a fallback or alternative validation method when traditional manifest-based
    /// validation is not available or desired.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the CSV-based validation outcome, or null if CSV validation is not available.</returns>
    private async Task<ValidationResult?> TryCsvValidationAsync(
        GameInstallation installation,
        IProgress<ValidationProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Check if CSV components are available
        if (_csvDiscoverer == null || _csvResolver == null || _httpClient == null)
        {
            _logger.LogDebug("CSV validation components not available, skipping CSV validation");
            return null;
        }

        try
        {
            _logger.LogInformation("Attempting CSV-based validation for installation '{Path}'", installation.InstallationPath);

            // Determine target game for CSV discovery
            var targetGame = installation.HasGenerals ? GameType.Generals :
                           installation.HasZeroHour ? GameType.ZeroHour : (GameType?)null;

            if (targetGame == null)
            {
                _logger.LogDebug("No supported game type found for CSV validation");
                return null;
            }

            // Create search query for CSV discovery
            var query = new ContentSearchQuery
            {
                TargetGame = targetGame.Value,
                Language = "All", // Use "All" to get all language variants
                ContentType = ContentType.GameInstallation,
                Take = 1, // We only need the latest version
            };

            // Discover CSV content
            progress?.Report(new ValidationProgress(1, 4, "Discovering CSV content"));
            var discoveryResult = await _csvDiscoverer.DiscoverAsync(query, cancellationToken);
            if (!discoveryResult.Success || discoveryResult.Data == null || !discoveryResult.Data.Any())
            {
                _logger.LogDebug("No CSV content discovered for game type {GameType}", targetGame);
                return null;
            }

            var discoveredItem = discoveryResult.Data.First();

            // Resolve CSV content to manifest
            progress?.Report(new ValidationProgress(2, 4, "Resolving CSV manifest"));
            var resolutionResult = await _csvResolver.ResolveAsync(discoveredItem, cancellationToken);
            if (!resolutionResult.Success || resolutionResult.Data == null)
            {
                _logger.LogWarning("Failed to resolve CSV content: {Error}", resolutionResult.FirstError);
                return null;
            }

            var csvManifest = resolutionResult.Data;

            // Validate the CSV manifest
            progress?.Report(new ValidationProgress(3, 4, "Validating CSV manifest"));
            var manifestValidationResult = await _contentValidator.ValidateManifestAsync(csvManifest, cancellationToken);
            if (!manifestValidationResult.IsValid)
            {
                var errors = manifestValidationResult.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
                if (errors.Any())
                {
                    _logger.LogWarning("CSV manifest validation failed with {Count} errors", errors.Count);
                    return null;
                }
            }

            // Perform full content validation using CSV manifest
            progress?.Report(new ValidationProgress(4, 4, "Validating content against CSV manifest"));
            var fullValidation = await _contentValidator.ValidateAllAsync(
                installation.InstallationPath,
                csvManifest,
                progress,
                cancellationToken);

            _logger.LogInformation(
                "CSV-based validation completed for '{Path}' with {Count} issues",
                installation.InstallationPath,
                fullValidation.Issues.Count);

            return new ValidationResult(
                installation.InstallationPath,
                fullValidation.Issues.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV validation failed for installation '{Path}'", installation.InstallationPath);
            return null;
        }
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
            // Try CSV validation as fallback
            var csvResult = await TryCsvValidationAsync(installation, progress, cancellationToken);
            if (csvResult != null)
            {
                _logger.LogInformation("Using CSV-based validation as manifest fallback succeeded");
                return csvResult;
            }

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
