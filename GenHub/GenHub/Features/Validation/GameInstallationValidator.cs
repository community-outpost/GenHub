using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
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
    IFileHashProvider hashProvider)
    : FileSystemValidator(logger, hashProvider),
      IGameInstallationValidator, IValidator<GameInstallation>
{
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
        logger.LogInformation("Starting validation for installation '{Path}'", installation.InstallationPath);
        var issues = new List<ValidationIssue>();

        // Each flagged game is validated as its own pass against its own manifest and
        // directory. A combined installation carries both games — asking for "the"
        // manifest of such an installation would surface Zero Hour only and Generals
        // would silently never be validated.
        var passes = new List<(GameType? GameType, string SourcePath)>();
        if (installation.HasGenerals)
        {
            passes.Add((GameType.Generals, string.IsNullOrEmpty(installation.GeneralsPath) ? installation.InstallationPath : installation.GeneralsPath));
        }

        if (installation.HasZeroHour)
        {
            passes.Add((GameType.ZeroHour, string.IsNullOrEmpty(installation.ZeroHourPath) ? installation.InstallationPath : installation.ZeroHourPath));
        }

        if (passes.Count == 0)
        {
            // No game flagged: fall back to the installation-level manifest lookup so a
            // bare registration still gets a definite answer instead of no validation.
            passes.Add((null, installation.InstallationPath));
        }

        // In a combined directory both games' retail archives sit side by side, so each
        // per-game pass sees the sibling game's root archives as files its manifest never
        // mentions. Those are not extraneous — they are the other half of the same
        // installation — and flagging them would indict every combined install.
        var isCombinedDirectory = installation.HasGenerals
            && installation.HasZeroHour
            && !string.IsNullOrEmpty(installation.GeneralsPath)
            && !string.IsNullOrEmpty(installation.ZeroHourPath)
            && Path.GetFullPath(installation.GeneralsPath).Equals(
                Path.GetFullPath(installation.ZeroHourPath),
                StringComparison.OrdinalIgnoreCase);

        // Four steps per pass: manifest fetch, manifest validation, content files, directories.
        int totalSteps = passes.Count * 4;
        int currentStep = 0;

        foreach (var (gameType, sourcePath) in passes)
        {
            progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Fetching manifest"));

            var manifest = gameType is null
                ? await manifestProvider.GetManifestAsync(installation, cancellationToken)
                : await manifestProvider.GetManifestAsync(installation, gameType.Value, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (manifest == null)
            {
                issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = sourcePath, Message = "Manifest not found for installation." });
                currentStep += 3;
                continue;
            }

            progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Core manifest validation"));

            var manifestValidationResult = await contentValidator.ValidateManifestAsync(manifest, cancellationToken);
            issues.AddRange(manifestValidationResult.Issues);

            progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Validating content files"));

            // Use ContentValidator for full content validation (integrity + extraneous files)
            try
            {
                var fullValidation = await contentValidator.ValidateAllAsync(sourcePath, manifest, progress, cancellationToken);
                var contentIssues = isCombinedDirectory && gameType is not null
                    ? fullValidation.Issues.Where(issue => !IsSiblingGameRootArchive(issue, gameType.Value))
                    : fullValidation.Issues;
                issues.AddRange(contentIssues);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Content validation failed for installation '{Path}'", sourcePath);
                issues.Add(new ValidationIssue
                {
                    IssueType = ValidationIssueType.CorruptedFile,
                    Path = sourcePath,
                    Message = $"Content validation failed: {ex.Message}",
                    Severity = ValidationSeverity.Error,
                });
            }

            // Installation-specific validations (directories, etc.)
            progress?.Report(new ValidationProgress(++currentStep, totalSteps, "Validating game directories"));

            var requiredDirs = manifest.RequiredDirectories ?? Enumerable.Empty<string>();
            if (requiredDirs.Any() && gameType is not null)
            {
                issues.AddRange(await ValidateDirectoriesAsync(sourcePath, requiredDirs, cancellationToken));
            }
        }

        progress?.Report(new ValidationProgress(totalSteps, totalSteps, "Validation complete"));

        logger.LogInformation("Installation validation for '{Path}' completed with {Count} issues.", installation.InstallationPath, issues.Count);
        return new ValidationResult(installation.InstallationPath, issues);
    }

    /// <summary>
    /// Determines whether an extraneous-file issue actually names a root archive of the
    /// sibling game in a combined directory.
    /// </summary>
    /// <param name="issue">The issue reported by content validation.</param>
    /// <param name="gameType">The game whose pass produced the issue.</param>
    /// <returns>True when the issue refers to the other game's known root archive.</returns>
    /// <remarks>
    /// Recognition uses the same retail vocabulary that classified the directory in the
    /// first place: in the Generals pass any root-level <c>*zh.big</c> belongs to Zero
    /// Hour, and in the Zero Hour pass any canonical Generals archive name belongs to
    /// Generals. Only the directory root is tolerated — deeper files are outside the
    /// vocabulary and stay reported.
    /// </remarks>
    private static bool IsSiblingGameRootArchive(ValidationIssue issue, GameType gameType)
    {
        if (issue.IssueType != ValidationIssueType.UnexpectedFile || string.IsNullOrEmpty(issue.Path))
        {
            return false;
        }

        if (issue.Path.Contains(Path.DirectorySeparatorChar) || issue.Path.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return gameType == GameType.Generals
            ? issue.Path.EndsWith(RetailArchiveConstants.ZeroHourArchiveSuffix, StringComparison.OrdinalIgnoreCase)
            : RetailArchiveConstants.GeneralsArchiveNames.Contains(issue.Path);
    }
}
