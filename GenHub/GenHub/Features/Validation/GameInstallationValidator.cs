using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.GameInstallations;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Features.Content.Services.ContentProviders;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Validation;

/// <summary>
/// Validates the integrity of a game installation directory (e.g., from Steam, EA App).
/// Integrates with the CSV content pipeline for manifest-driven multi-language validation.
/// </summary>
public class GameInstallationValidator(
    ILogger<GameInstallationValidator> logger,
    IManifestProvider? manifestProvider,
    IContentValidator contentValidator,
    IFileHashProvider hashProvider,
    ILanguageDetector? languageDetector = null,
    CsvContentProvider? csvContentProvider = null,
    IEnumerable<IContentProvider>? contentProviders = null)
    : FileSystemValidator(logger, hashProvider),
      IGameInstallationValidator, IValidator<GameInstallation>
{
    private readonly ILanguageDetector _languageDetector = languageDetector ?? new LanguageDetector();
    private readonly IContentProvider? _resolvedCsvProvider = csvContentProvider ??
        contentProviders?.OfType<CsvContentProvider>().FirstOrDefault() ??
        contentProviders?.FirstOrDefault(p => string.Equals(p.SourceName, PublisherTypeConstants.CsvRegistry, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates the specified game installation against expected files and checksums.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public Task<ValidationResult> ValidateAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        return ValidateInternalAsync(installation, null, null, cancellationToken);
    }

    /// <summary>
    /// Validates the specified game installation with progress reporting.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public Task<ValidationResult> ValidateAsync(GameInstallation installation, IProgress<ValidationProgress>? progress, CancellationToken cancellationToken = default)
    {
        return ValidateInternalAsync(installation, null, progress, cancellationToken);
    }

    /// <summary>
    /// Validates the specified game installation with explicit language and optional progress reporting.
    /// </summary>
    /// <param name="installation">The game installation to validate.</param>
    /// <param name="language">The explicit language code (e.g. "EN", "DE").</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public Task<ValidationResult> ValidateAsync(
        GameInstallation installation,
        string language,
        IProgress<ValidationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ValidateInternalAsync(installation, language, progress, cancellationToken);
    }

    /// <summary>
    /// Validates a specific game installation directory by path, game type, and optional language.
    /// </summary>
    /// <param name="installationPath">The path to the game directory.</param>
    /// <param name="gameType">The target game type (Generals or ZeroHour).</param>
    /// <param name="language">Optional explicit language code. If null, language is auto-detected.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the outcome of the validation.</returns>
    public Task<ValidationResult> ValidateInstallationAsync(
        string installationPath,
        GameType gameType,
        string? language = null,
        IProgress<ValidationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installationPath))
        {
            throw new ArgumentException("Installation path cannot be null or empty.", nameof(installationPath));
        }

        return ValidateInstallationCoreAsync(
            installationPath,
            gameType,
            language,
            installation: null,
            progress: progress,
            cancellationToken: cancellationToken);
    }

    private async Task<ValidationResult> ValidateInternalAsync(
        GameInstallation installation,
        string? language,
        IProgress<ValidationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("Starting validation for installation '{Path}'", installation.InstallationPath);
        var stopwatch = Stopwatch.StartNew();
        var issues = new List<ValidationIssue>();
        int totalFiles = 0;

        var targets = new List<(string Path, GameType GameType)>();

        if (installation.HasGenerals && !string.IsNullOrWhiteSpace(installation.GeneralsPath))
        {
            targets.Add((installation.GeneralsPath, GameType.Generals));
        }

        if (installation.HasZeroHour && !string.IsNullOrWhiteSpace(installation.ZeroHourPath))
        {
            targets.Add((installation.ZeroHourPath, GameType.ZeroHour));
        }

        if (targets.Count == 0)
        {
            var fallbackGameType = installation.HasZeroHour ? GameType.ZeroHour : GameType.Generals;
            targets.Add((installation.InstallationPath, fallbackGameType));
        }

        int targetIndex = 0;
        foreach (var (targetPath, targetGame) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            targetIndex++;

            logger.LogDebug("Validating target {Index}/{Total}: {GameType} at '{Path}'", targetIndex, targets.Count, targetGame, targetPath);

            var result = await ValidateInstallationCoreAsync(
                targetPath,
                targetGame,
                language,
                installation,
                progress,
                cancellationToken);

            issues.AddRange(result.Issues);
            totalFiles += result.TotalFilesValidated;
        }

        stopwatch.Stop();
        logger.LogInformation(
            "Installation validation for '{Path}' completed with {IssueCount} issues ({CriticalCount} critical, {TotalFiles} files validated).",
            installation.InstallationPath,
            issues.Count,
            issues.Count(i => i.Severity == ValidationSeverity.Error || i.Severity == ValidationSeverity.Critical),
            totalFiles);

        return new ValidationResult(installation.InstallationPath, issues, stopwatch.Elapsed, totalFiles);
    }

    private async Task<ValidationResult> ValidateInstallationCoreAsync(
        string installationPath,
        GameType gameType,
        string? language,
        GameInstallation? installation,
        IProgress<ValidationProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        var issues = new List<ValidationIssue>();

        var detectedLanguage = string.IsNullOrWhiteSpace(language)
            ? await _languageDetector.DetectAsync(installationPath, cancellationToken)
            : language;

        var normalizedLanguage = ContentSearchQuery.NormalizeLanguage(detectedLanguage);
        logger.LogInformation(
            "Validating installation at '{Path}' for game {GameType} in language {Language}",
            installationPath,
            gameType,
            normalizedLanguage);

        int totalSteps = 4;
        int currentStep = 0;
        progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Resolving manifest"));

        ContentManifest? manifest = null;
        var csvIssues = new List<ValidationIssue>();
        if (_resolvedCsvProvider != null)
        {
            manifest = await ResolveManifestFromCsvProviderAsync(
                installationPath,
                gameType,
                normalizedLanguage,
                csvIssues,
                cancellationToken);
        }

        if (manifest == null && manifestProvider != null)
        {
            logger.LogDebug("Attempting fallback manifest lookup via IManifestProvider for '{Path}' ({GameType})", installationPath, gameType);
            var targetInstall = new GameInstallation(installationPath, installation?.InstallationType ?? GameInstallationType.Unknown, null);
            if (gameType == GameType.ZeroHour)
            {
                targetInstall.SetPaths(generalsPath: null, zeroHourPath: installationPath);
            }
            else
            {
                targetInstall.SetPaths(generalsPath: installationPath, zeroHourPath: null);
            }

            manifest = await manifestProvider.GetManifestAsync(targetInstall, cancellationToken);
        }

        if (manifest == null)
        {
            if (csvIssues.Count > 0)
            {
                issues.AddRange(csvIssues);
            }
            else
            {
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.MissingFile,
                    Path = installationPath,
                    Message = $"Manifest not found for {gameType} ({normalizedLanguage}) installation at '{installationPath}'.",
                    Severity = ValidationSeverity.Error,
                });
            }

            progress?.Report(new ValidationProgress(totalSteps, totalSteps, "Validation complete"));
            stopwatch.Stop();
            return new ValidationResult(installationPath, issues, stopwatch.Elapsed, 0);
        }

        progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Core manifest validation"));
        var manifestValidationResult = await contentValidator.ValidateManifestAsync(manifest, cancellationToken);
        issues.AddRange(manifestValidationResult.Issues);

        progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Validating content files"));
        int totalFiles = 0;
        try
        {
            var fullValidation = await contentValidator.ValidateAllAsync(
                installationPath,
                manifest,
                progress,
                cancellationToken);
            issues.AddRange(fullValidation.Issues);
            totalFiles = manifest.Files?.Count ?? 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Content validation failed for installation '{Path}' ({GameType}, {Language})", installationPath, gameType, normalizedLanguage);
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.CorruptedFile,
                Path = installationPath,
                Message = $"Content validation failed for {gameType} ({normalizedLanguage}): {ex.Message}",
                Severity = ValidationSeverity.Error,
            });
            totalFiles = 0;
        }

        var requiredDirs = manifest.RequiredDirectories ?? Enumerable.Empty<string>();
        if (requiredDirs.Any())
        {
            var dirIssues = await ValidateDirectoriesAsync(installationPath, requiredDirs, cancellationToken);
            issues.AddRange(dirIssues);
        }

        progress?.Report(new ValidationProgress(totalSteps, totalSteps, "Validation complete"));

        stopwatch.Stop();
        return new ValidationResult(installationPath, issues, stopwatch.Elapsed, totalFiles);
    }

    private async Task<ContentManifest?> ResolveManifestFromCsvProviderAsync(
        string installationPath,
        GameType gameType,
        string language,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new ContentSearchQuery
            {
                TargetGame = gameType,
                Language = language,
                ContentType = ContentType.GameInstallation,
            };

            var searchResult = await _resolvedCsvProvider!.SearchAsync(query, cancellationToken);
            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                logger.LogWarning(
                    "CSV provider search returned no results for {GameType} ({Language}): {Error}",
                    gameType,
                    language,
                    searchResult.FirstError ?? "No matching items");

                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.MissingFile,
                    Path = installationPath,
                    Message = $"No CSV manifest found for {gameType} ({language}): {searchResult.FirstError ?? "No matching catalog entries"}",
                    Severity = ValidationSeverity.Error,
                });
                return null;
            }

            var matchingItem = searchResult.Data.FirstOrDefault();
            var manifest = matchingItem?.GetData<ContentManifest>();
            if (manifest == null)
            {
                logger.LogWarning(
                    "CSV provider search result did not contain valid manifest data for {GameType} ({Language})",
                    gameType,
                    language);

                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.MissingFile,
                    Path = installationPath,
                    Message = $"Failed to parse CSV manifest for {gameType} ({language}).",
                    Severity = ValidationSeverity.Error,
                });
                return null;
            }

            return manifest;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve CSV manifest for {GameType} ({Language})", gameType, language);
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.MissingFile,
                Path = installationPath,
                Message = $"Error retrieving CSV manifest for {gameType} ({language}): {ex.Message}",
                Severity = ValidationSeverity.Error,
            });
            return null;
        }
    }
}