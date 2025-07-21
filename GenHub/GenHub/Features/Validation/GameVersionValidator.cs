using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Validation;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Validation;

/// <summary>
/// Validates the integrity of a specific game version workspace using manifest-driven checks.
/// </summary>
public class GameVersionValidator : FileSystemValidator, IGameVersionValidator
{
    private readonly ILogger<GameVersionValidator> _logger;
    private readonly IManifestProvider _manifestProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameVersionValidator"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="manifestProvider">Manifest provider.</param>
    public GameVersionValidator(ILogger<GameVersionValidator> logger, IManifestProvider manifestProvider)
        : base(logger ?? throw new ArgumentNullException(nameof(logger)))
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestProvider = manifestProvider ?? throw new ArgumentNullException(nameof(manifestProvider));
    }

    /// <summary>
    /// Validates the specified game version.
    /// </summary>
    /// <param name="gameVersion">The game version to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public async Task<ValidationResult> ValidateAsync(GameVersion gameVersion, CancellationToken cancellationToken = default)
    {
        return await ValidateAsync(gameVersion, null, cancellationToken);
    }

    /// <summary>
    /// Validates the specified game version with progress reporting.
    /// </summary>
    /// <param name="gameVersion">The game version to validate.</param>
    /// <param name="progress">Progress reporter for MVVM integration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> representing the validation outcome.</returns>
    public async Task<ValidationResult> ValidateAsync(GameVersion gameVersion, IProgress<ValidationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting validation for version '{VersionName}' (ID: {VersionId}) at '{Path}'", gameVersion.Name, gameVersion.Id, gameVersion.WorkingDirectory);
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrEmpty(gameVersion.WorkingDirectory) || !Directory.Exists(gameVersion.WorkingDirectory))
        {
            issues.Add(new ValidationIssue { IssueType = ValidationIssueType.DirectoryMissing, Path = gameVersion.WorkingDirectory, Message = "Game version working directory is missing or not prepared." });
            _logger.LogError("Validation failed: Working directory '{Path}' is invalid.", gameVersion.WorkingDirectory);
            return new ValidationResult(gameVersion.Id, issues);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var manifest = await _manifestProvider.GetManifestAsync(gameVersion, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (manifest == null)
        {
            issues.Add(new ValidationIssue { IssueType = ValidationIssueType.MissingFile, Path = "Manifest", Message = "Validation manifest could not be found for this game version." });
            _logger.LogError("Validation failed: No manifest found for game version ID '{VersionId}'.", gameVersion.Id);
            return new ValidationResult(gameVersion.Id, issues);
        }

        // Validate required directories
        issues.AddRange(await ValidateDirectoriesAsync(gameVersion.WorkingDirectory, manifest.RequiredDirectories, cancellationToken));

        // Validate files (with progress reporting and path traversal security)
        issues.AddRange(await ValidateFilesAsync(gameVersion.WorkingDirectory, manifest.Files, cancellationToken, progress));

        // Addon detection (manifest-driven)
        foreach (var file in manifest.Files)
        {
            if (file.SourceType == ManifestFileSourceType.OptionalAddon)
            {
                issues.Add(new ValidationIssue { IssueType = ValidationIssueType.AddonDetected, Path = file.RelativePath, Message = "Detected optional addon as specified in manifest." });
            }
        }

        // KnownAddons detection
        if (manifest.KnownAddons != null)
        {
            foreach (var knownAddon in manifest.KnownAddons)
            {
                foreach (var file in manifest.Files)
                {
                    if (!string.IsNullOrEmpty(knownAddon) && file.RelativePath.Contains(knownAddon, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new ValidationIssue
                        {
                            IssueType = ValidationIssueType.AddonDetected,
                            Path = file.RelativePath,
                            Message = $"Detected known addon: {knownAddon}",
                            Severity = ValidationSeverity.Warning,
                        });
                    }
                }
            }
        }

        // Unexpected file detection
        var actualFiles = Directory.EnumerateFiles(gameVersion.WorkingDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(gameVersion.WorkingDirectory, f).Replace('\\', '/'))
            .ToList();
        var expectedRelativePaths = manifest.Files.Select(f => f.RelativePath).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        foreach (var actualRelativePath in actualFiles)
        {
            if (!expectedRelativePaths.Contains(actualRelativePath))
            {
                issues.Add(new ValidationIssue { IssueType = ValidationIssueType.UnexpectedFile, Path = actualRelativePath, Message = "An unexpected file was found in the working directory." });
            }
        }

        _logger.LogInformation("Validation for '{VersionName}' completed with {IssueCount} issues.", gameVersion.Name, issues.Count);
        return new ValidationResult(gameVersion.Id, issues);
    }
}
